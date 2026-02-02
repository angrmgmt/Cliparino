using System.Reflection;
using System.Text.Json;

namespace Cliparino.Core.Services;

public class UpdateChecker : IUpdateChecker {
    private const string DefaultGitHubRepo = "angrmgmt/Cliparino";
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<UpdateChecker> _logger;

    public UpdateChecker(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<UpdateChecker> logger
    ) {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;

        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        CurrentVersion = version?.ToString(3) ?? "0.0.0";
    }

    public string CurrentVersion { get; }

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

            _logger.LogInformation(
                "Update check: Current={Current}, Latest={Latest}, IsNewer={IsNewer}",
                CurrentVersion, latestVersion, isNewer
            );

            return new UpdateInfo(
                latestVersion,
                htmlUrl,
                description,
                publishedAt,
                isNewer
            );
        } catch (Exception ex) {
            _logger.LogError(ex, "Error checking for updates");

            return null;
        }
    }

    private bool IsVersionNewer(string latestVersion, string currentVersion) {
        try {
            var latest = Version.Parse(latestVersion);
            var current = Version.Parse(currentVersion);

            return latest > current;
        } catch {
            _logger.LogWarning(
                "Could not parse versions: Latest={Latest}, Current={Current}",
                latestVersion, currentVersion
            );

            return false;
        }
    }
}