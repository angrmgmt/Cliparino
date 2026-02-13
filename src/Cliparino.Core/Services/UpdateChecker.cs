using System.Reflection;
using System.Text.Json;

namespace Cliparino.Core.Services;

/// <summary>
///     Checks for application updates by querying the GitHub Releases API.
/// </summary>
/// <remarks>
///     <para>
///         This is the primary implementation of <see cref="IUpdateChecker" />, using the GitHub Releases API
///         (with optional custom repository configuration) to check for newer versions.
///     </para>
///     <para>
///         Key features:
///         <list type="bullet">
///             <item>Queries GitHub Releases API to retrieve the latest release</item>
///             <item>Extracts current version from assembly metadata at runtime</item>
///             <item>Compares versions using semantic versioning rules</item>
///             <item>Logs check results and errors for debugging</item>
///             <item>Configurable GitHub repository via configuration (defaults to "angrmgmt/Cliparino")</item>
///         </list>
///     </para>
///     <para>
///         Dependencies:
///         <list type="bullet">
///             <item><see cref="IHttpClientFactory" /> - For creating HTTP client instances (respects configured proxies)</item>
///             <item><see cref="IConfiguration" /> - For reading custom GitHub repository configuration</item>
///             <item><see cref="ILogger{UpdateChecker}" /> - For logging check results and errors</item>
///         </list>
///     </para>
///     <para>Thread-safety: Thread-safe. HTTP requests are concurrent-safe and CurrentVersion is immutable.</para>
///     <para>Lifecycle: Typically registered as Singleton.</para>
/// </remarks>
public class UpdateChecker : IUpdateChecker {
    private const string DefaultGitHubRepo = "angrmgmt/Cliparino";
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<UpdateChecker> _logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="UpdateChecker" /> class.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Extracts the current version from the executing assembly's version attribute.
    ///         If version extraction fails, defaults to "0.0.0" to allow update checks to proceed.
    ///     </para>
    /// </remarks>
    /// <param name="httpClientFactory">Factory for creating HTTP clients with configured proxy and headers.</param>
    /// <param name="configuration">Configuration provider for reading update settings.</param>
    /// <param name="logger">Logger instance for recording check results and errors.</param>
    public UpdateChecker(IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<UpdateChecker> logger) {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;

        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        CurrentVersion = version?.ToString(3) ?? "0.0.0";
    }

    /// <inheritdoc />
    public string CurrentVersion { get; }

    /// <inheritdoc />
    public async Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default) {
        try {
            var githubRepo = _configuration["Update:GitHubRepo"] ?? DefaultGitHubRepo;
            var apiUrl = $"https://api.github.com/repos/{githubRepo}/releases/latest";

            var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", $"Cliparino/{CurrentVersion}");

            _logger.LogDebug("Checking for updates from {ApiUrl}", apiUrl);

            var response = await httpClient.GetAsync(apiUrl, cancellationToken);

            if (!response.IsSuccessStatusCode) {
                _logger.LogWarning("Failed to check for updates: {StatusCode}", response.StatusCode);

                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.GetProperty("tag_name").GetString() ?? "";
            var htmlUrl = root.GetProperty("html_url").GetString() ?? "";
            var publishedAt = root.GetProperty("published_at").GetDateTime();
            var description = root.TryGetProperty("body", out var bodyElement)
                ? bodyElement.GetString()
                : null;

            var latestVersion = tagName.TrimStart('v');
            var isNewer = IsVersionNewer(latestVersion, CurrentVersion);

            _logger.LogInformation("Update check: Current={Current}, Latest={Latest}, IsNewer={IsNewer}",
                CurrentVersion, latestVersion, isNewer);

            return new UpdateInfo(latestVersion,
                htmlUrl,
                description,
                publishedAt,
                isNewer);
        } catch (Exception ex) {
            _logger.LogError(ex, "Error checking for updates");

            return null;
        }
    }

    /// <summary>
    ///     Compares two semantic version strings to determine if the latest is newer.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Uses .NET's built-in <see cref="Version" /> class for comparison.
    ///         If either version cannot be parsed, logs a warning and returns false (no upgrade).
    ///     </para>
    /// </remarks>
    /// <param name="latestVersion">The version string from the release channel (e.g., "2.0.1").</param>
    /// <param name="currentVersion">The currently running version string (e.g., "2.0.0").</param>
    /// <returns>True if latestVersion is greater than currentVersion; false otherwise.</returns>
    private bool IsVersionNewer(string latestVersion, string currentVersion) {
        try {
            var latest = Version.Parse(latestVersion);
            var current = Version.Parse(currentVersion);

            return latest > current;
        } catch {
            _logger.LogWarning("Could not parse versions: Latest={Latest}, Current={Current}",
                latestVersion, currentVersion);

            return false;
        }
    }
}