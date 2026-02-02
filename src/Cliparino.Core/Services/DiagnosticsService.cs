using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cliparino.Core.Services;

/// <summary>
///     Collects and exports comprehensive diagnostic information for troubleshooting and support.
/// </summary>
/// <remarks>
///     <para>
///         This is the primary implementation of <see cref="IDiagnosticsService" />, providing both plaintext
///         and ZIP-archived diagnostic exports.
///     </para>
///     <para>
///         Key features:
///         <list type="bullet">
///             <item>Gathers system information (OS, runtime, machine name)</item>
///             <item>Exports application configuration with automatic sensitive key redaction</item>
///             <item>Includes recent log files (up to 3 most recent, with sensitive data filtered)</item>
///             <item>Optionally includes component health status when <see cref="IHealthReporter" /> is available</item>
///             <item>Uses regex-based sensitive data filtering to protect credentials in logs</item>
///         </list>
///     </para>
///     <para>
///         Dependencies:
///         <list type="bullet">
///             <item><see cref="IConfiguration" /> - Application configuration for export</item>
///             <item><see cref="ILogger{DiagnosticsService}" /> - Logging errors during export</item>
///             <item><see cref="IHealthReporter" /> (optional) - Component health status</item>
///         </list>
///     </para>
///     <para>Thread-safety: Thread-safe. File and log directory operations are concurrent-safe by the OS.</para>
///     <para>Lifecycle: Typically registered as Singleton and injected into diagnostic controllers.</para>
/// </remarks>
public partial class DiagnosticsService : IDiagnosticsService {
    private readonly IConfiguration _configuration;
    private readonly IHealthReporter? _healthReporter;
    private readonly ILogger<DiagnosticsService> _logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DiagnosticsService" /> class.
    /// </summary>
    /// <param name="configuration">Application configuration provider.</param>
    /// <param name="logger">Logger instance for recording export operations and errors.</param>
    /// <param name="healthReporter">Optional health reporter instance. If null, health status is excluded from exports.</param>
    public DiagnosticsService(
        IConfiguration configuration,
        ILogger<DiagnosticsService> logger,
        IHealthReporter? healthReporter = null
    ) {
        _configuration = configuration;
        _logger = logger;
        _healthReporter = healthReporter;
    }

    /// <inheritdoc />
    public async Task<string> ExportDiagnosticsAsync(CancellationToken cancellationToken = default) {
        var sb = new StringBuilder();

        sb.AppendLine("=== Cliparino Diagnostics Export ===");
        sb.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();

        sb.AppendLine("=== System Information ===");
        sb.AppendLine($"OS: {Environment.OSVersion}");
        sb.AppendLine($"Runtime: {Environment.Version}");
        sb.AppendLine($"Machine: {Environment.MachineName}");
        sb.AppendLine();

        sb.AppendLine("=== Configuration (Redacted) ===");
        sb.AppendLine(GetRedactedConfiguration());
        sb.AppendLine();

        if (_healthReporter != null) {
            sb.AppendLine("=== Component Health Status ===");
            sb.AppendLine(await GetHealthStatusAsync(cancellationToken));
            sb.AppendLine();
        }

        sb.AppendLine("=== Recent Logs ===");
        sb.AppendLine(await GetRecentLogsAsync(cancellationToken));
        sb.AppendLine();

        return sb.ToString();
    }

    /// <inheritdoc />
    public async Task<byte[]> ExportDiagnosticsZipAsync(CancellationToken cancellationToken = default) {
        using var memoryStream = new MemoryStream();

        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true)) {
            var diagnosticsEntry = archive.CreateEntry("diagnostics.txt");

            await using (var entryStream = diagnosticsEntry.Open())
            await using (var writer = new StreamWriter(entryStream)) {
                await writer.WriteAsync(await ExportDiagnosticsAsync(cancellationToken));
            }

            var configEntry = archive.CreateEntry("appsettings-redacted.json");

            await using (var entryStream = configEntry.Open())
            await using (var writer = new StreamWriter(entryStream)) {
                await writer.WriteAsync(GetRedactedConfiguration());
            }

            await AddRecentLogFilesAsync(archive, cancellationToken);
        }

        return memoryStream.ToArray();
    }

    /// <summary>
    ///     Gets a JSON representation of application configuration with sensitive values redacted.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Scans all configuration keys and redacts values for keys matching patterns like
    ///         "password", "secret", "token", "key", "clientid", etc. (case-insensitive).
    ///     </para>
    ///     <para>Redacted values show first 2 and last 2 characters with "***" in between for verification purposes.</para>
    /// </remarks>
    /// <returns>Pretty-printed JSON string with sensitive values redacted.</returns>
    private string GetRedactedConfiguration() {
        var configDict = new Dictionary<string, object?>();

        foreach (var section in _configuration.GetChildren()) configDict[section.Key] = GetRedactedSection(section);

        return JsonSerializer.Serialize(
            configDict, new JsonSerializerOptions {
                WriteIndented = true
            }
        );
    }

    /// <summary>
    ///     Recursively processes a configuration section, redacting sensitive values.
    /// </summary>
    /// <param name="section">Configuration section to process.</param>
    /// <returns>Dictionary or string representing the section, with sensitive values redacted.</returns>
    private object? GetRedactedSection(IConfigurationSection section) {
        if (!section.GetChildren().Any()) {
            var value = section.Value;

            if (value != null && ShouldRedact(section.Key)) return RedactValue(value);

            return value;
        }

        var dict = new Dictionary<string, object?>();
        foreach (var child in section.GetChildren()) dict[child.Key] = GetRedactedSection(child);

        return dict;
    }

    /// <summary>
    ///     Determines if a configuration key should have its value redacted for security.
    /// </summary>
    /// <param name="key">Configuration key name (case-insensitive).</param>
    /// <returns>True if the key appears to contain sensitive information.</returns>
    private bool ShouldRedact(string key) {
        var sensitiveKeys = new[] {
            "password", "secret", "token", "key", "clientid", "clientsecret",
            "accesstoken", "refreshtoken", "apikey", "connectionstring"
        };

        return sensitiveKeys.Any(sk =>
            key.Contains(sk, StringComparison.OrdinalIgnoreCase)
        );
    }

    /// <summary>
    ///     Redacts a sensitive configuration value while preserving partial visibility.
    /// </summary>
    /// <param name="value">The value to redact.</param>
    /// <returns>Redacted version showing first/last 2 chars or "[REDACTED]"/"[EMPTY]".</returns>
    private string RedactValue(string value) {
        if (string.IsNullOrWhiteSpace(value))
            return "[EMPTY]";

        if (value.Length <= 4)
            return "[REDACTED]";

        return $"{value[..2]}***{value[^2..]}";
    }

    /// <summary>
    ///     Retrieves recent log files (up to 3 most recent, with sensitive data redacted).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the async operation.</param>
    /// <returns>Formatted text containing recent log entries or an error message if logs are unavailable.</returns>
    private async Task<string> GetRecentLogsAsync(CancellationToken cancellationToken) {
        try {
            var logsPath = Path.Combine(Directory.GetCurrentDirectory(), "logs");

            if (!Directory.Exists(logsPath)) return "No log files found";

            var logFiles = Directory.GetFiles(logsPath, "*.log")
                .OrderByDescending(File.GetLastWriteTime)
                .Take(3)
                .ToList();

            if (!logFiles.Any()) return "No log files found";

            var sb = new StringBuilder();

            foreach (var logFile in logFiles) {
                sb.AppendLine($"--- {Path.GetFileName(logFile)} ---");
                var lines = await File.ReadAllLinesAsync(logFile, cancellationToken);
                var recentLines = lines.TakeLast(100);

                foreach (var line in recentLines) sb.AppendLine(RedactSensitiveData(line));
                sb.AppendLine();
            }

            return sb.ToString();
        } catch (Exception ex) {
            _logger.LogError(ex, "Error reading recent logs");

            return $"Error reading logs: {ex.Message}";
        }
    }

    /// <summary>
    ///     Adds recent log files to a ZIP archive for distribution, with sensitive data redacted.
    /// </summary>
    /// <param name="archive">ZIP archive to add log files to.</param>
    /// <param name="cancellationToken">Cancellation token for the async operation.</param>
    /// <remarks>Adds up to 3 most recent log files under a "logs/" directory in the archive.</remarks>
    private async Task AddRecentLogFilesAsync(ZipArchive archive, CancellationToken cancellationToken) {
        try {
            var logsPath = Path.Combine(Directory.GetCurrentDirectory(), "logs");

            if (!Directory.Exists(logsPath))
                return;

            var logFiles = Directory.GetFiles(logsPath, "*.log")
                .OrderByDescending(File.GetLastWriteTime)
                .Take(3)
                .ToList();

            foreach (var logFile in logFiles) {
                var fileName = Path.GetFileName(logFile);
                var entry = archive.CreateEntry($"logs/{fileName}");

                await using var entryStream = entry.Open();
                await using var writer = new StreamWriter(entryStream);

                var content = await File.ReadAllTextAsync(logFile, cancellationToken);
                await writer.WriteAsync(RedactSensitiveData(content));
            }
        } catch (Exception ex) {
            _logger.LogError(ex, "Error adding log files to diagnostics archive");
        }
    }

    /// <summary>
    ///     Redacts common sensitive patterns from log text (tokens, passwords, secrets).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Uses compiled regex patterns to match and redact:
    ///         <list type="bullet">
    ///             <item>Bearer tokens: "Bearer [token_value]" → "Bearer [REDACTED]"</item>
    ///             <item>Refresh tokens: "refresh_token=[value]" → "refresh_token=[REDACTED]"</item>
    ///             <item>Client secrets: "client_secret=[value]" → "client_secret=[REDACTED]"</item>
    ///             <item>Passwords: "password=[value]" → "password=[REDACTED]"</item>
    ///         </list>
    ///     </para>
    /// </remarks>
    /// <param name="text">The log text to redact.</param>
    /// <returns>Log text with sensitive patterns replaced.</returns>
    private string RedactSensitiveData(string text) {
        text = AccessTokenRegex().Replace(text, "Bearer [REDACTED]");
        text = RefreshTokenRegex().Replace(text, "refresh_token=[REDACTED]");
        text = ClientSecretRegex().Replace(text, "client_secret=[REDACTED]");
        text = PasswordRegex().Replace(text, "password=[REDACTED]");

        return text;
    }

    /// <summary>
    ///     Formats component health status as readable text for inclusion in diagnostics export.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the async operation.</param>
    /// <returns>Formatted health status or error message if health reporter is unavailable.</returns>
    private async Task<string> GetHealthStatusAsync(CancellationToken cancellationToken) {
        if (_healthReporter == null)
            return "Health reporter not available";

        var sb = new StringBuilder();
        var healthStatus = await _healthReporter.GetHealthStatusAsync(cancellationToken);

        foreach (var component in healthStatus) {
            sb.AppendLine($"{component.Key}: {component.Value.Status}");
            if (!string.IsNullOrEmpty(component.Value.LastError))
                sb.AppendLine($"  Last Error: {component.Value.LastError}");

            if (component.Value.RepairActions?.Any() == true)
                sb.AppendLine($"  Repair Actions: {string.Join(", ", component.Value.RepairActions)}");
        }

        return sb.ToString();
    }

    /// <summary>Regex pattern for matching Bearer tokens.</summary>
    [GeneratedRegex(@"Bearer\s+[\w\-\.]+", RegexOptions.IgnoreCase)]
    private static partial Regex AccessTokenRegex();

    /// <summary>Regex pattern for matching refresh token parameters.</summary>
    [GeneratedRegex(@"refresh_token=[\w\-\.]+", RegexOptions.IgnoreCase)]
    private static partial Regex RefreshTokenRegex();

    /// <summary>Regex pattern for matching client_secret parameters.</summary>
    [GeneratedRegex(@"client_secret=[\w\-\.]+", RegexOptions.IgnoreCase)]
    private static partial Regex ClientSecretRegex();

    /// <summary>Regex pattern for matching password parameters.</summary>
    [GeneratedRegex(@"password=[\w\-\.]+", RegexOptions.IgnoreCase)]
    private static partial Regex PasswordRegex();
}