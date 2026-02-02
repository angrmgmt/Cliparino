namespace Cliparino.Core.Services;

public class PeriodicUpdateCheckService : BackgroundService {
    private readonly IConfiguration _configuration;
    private readonly ILogger<PeriodicUpdateCheckService> _logger;
    private readonly IUpdateChecker _updateChecker;

    public PeriodicUpdateCheckService(
        IUpdateChecker updateChecker,
        IConfiguration configuration,
        ILogger<PeriodicUpdateCheckService> logger
    ) {
        _updateChecker = updateChecker;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        var checkOnStartup = _configuration.GetValue("Update:CheckOnStartup", true);
        var checkIntervalHours = _configuration.GetValue("Update:CheckIntervalHours", 24);

        if (checkOnStartup) {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            await PerformUpdateCheckAsync(stoppingToken);
        }

        var checkInterval = TimeSpan.FromHours(checkIntervalHours);

        while (!stoppingToken.IsCancellationRequested)
            try {
                await Task.Delay(checkInterval, stoppingToken);
                await PerformUpdateCheckAsync(stoppingToken);
            } catch (OperationCanceledException) {
                break;
            } catch (Exception ex) {
                _logger.LogError(ex, "Error in periodic update check");
            }
    }

    private async Task PerformUpdateCheckAsync(CancellationToken cancellationToken) {
        try {
            var updateInfo = await _updateChecker.CheckForUpdatesAsync(cancellationToken);

            if (updateInfo?.IsNewer == true)
                _logger.LogInformation(
                    "Update available: v{LatestVersion} (current: v{CurrentVersion}). Download: {ReleaseUrl}",
                    updateInfo.LatestVersion,
                    _updateChecker.CurrentVersion,
                    updateInfo.ReleaseUrl
                );
            else
                _logger.LogDebug(
                    "No updates available. Current version: v{CurrentVersion}",
                    _updateChecker.CurrentVersion
                );
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to check for updates");
        }
    }
}