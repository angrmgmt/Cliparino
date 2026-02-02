using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cliparino.Core.Services;

public partial class DiagnosticsService : IDiagnosticsService {
    private readonly IConfiguration _configuration;
    private readonly IHealthReporter? _healthReporter;
    private readonly ILogger<DiagnosticsService> _logger;

    public DiagnosticsService(
        IConfiguration configuration,
        ILogger<DiagnosticsService> logger,
        IHealthReporter? healthReporter = null
    ) {
        _configuration = configuration;
        _logger = logger;
        _healthReporter = healthReporter;
    }

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

    private string GetRedactedConfiguration() {
        var configDict = new Dictionary<string, object?>();

        foreach (var section in _configuration.GetChildren()) configDict[section.Key] = GetRedactedSection(section);

        return JsonSerializer.Serialize(
            configDict, new JsonSerializerOptions {
                WriteIndented = true
            }
        );
    }

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

    private bool ShouldRedact(string key) {
        var sensitiveKeys = new[] {
            "password", "secret", "token", "key", "clientid", "clientsecret",
            "accesstoken", "refreshtoken", "apikey", "connectionstring"
        };

        return sensitiveKeys.Any(sk =>
            key.Contains(sk, StringComparison.OrdinalIgnoreCase)
        );
    }

    private string RedactValue(string value) {
        if (string.IsNullOrWhiteSpace(value))
            return "[EMPTY]";

        if (value.Length <= 4)
            return "[REDACTED]";

        return $"{value[..2]}***{value[^2..]}";
    }

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

    private string RedactSensitiveData(string text) {
        text = AccessTokenRegex().Replace(text, "Bearer [REDACTED]");
        text = RefreshTokenRegex().Replace(text, "refresh_token=[REDACTED]");
        text = ClientSecretRegex().Replace(text, "client_secret=[REDACTED]");
        text = PasswordRegex().Replace(text, "password=[REDACTED]");

        return text;
    }

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

    [GeneratedRegex(@"Bearer\s+[\w\-\.]+", RegexOptions.IgnoreCase)]
    private static partial Regex AccessTokenRegex();

    [GeneratedRegex(@"refresh_token=[\w\-\.]+", RegexOptions.IgnoreCase)]
    private static partial Regex RefreshTokenRegex();

    [GeneratedRegex(@"client_secret=[\w\-\.]+", RegexOptions.IgnoreCase)]
    private static partial Regex ClientSecretRegex();

    [GeneratedRegex(@"password=[\w\-\.]+", RegexOptions.IgnoreCase)]
    private static partial Regex PasswordRegex();
}