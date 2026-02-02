namespace Cliparino.Core.Services;

public class ObsHealthSupervisor : BackgroundService {
    private const int MaxReconnectAttempts = 10;
    private readonly BackoffPolicy _backoffPolicy = BackoffPolicy.Default;
    private readonly IConfiguration _configuration;
    private readonly IHealthReporter? _healthReporter;
    private readonly ILogger<ObsHealthSupervisor> _logger;
    private readonly IObsController _obsController;
    private int _reconnectAttempts;

    public ObsHealthSupervisor(
        IObsController obsController,
        ILogger<ObsHealthSupervisor> logger,
        IConfiguration configuration,
        IHealthReporter? healthReporter = null
    ) {
        _obsController = obsController;
        _logger = logger;
        _configuration = configuration;
        _healthReporter = healthReporter;

        _obsController.Disconnected += OnObsDisconnected;
        _obsController.Connected += OnObsConnected;
    }

    private string SceneName => _configuration["OBS:SceneName"] ?? "Cliparino";
    private string SourceName => _configuration["OBS:SourceName"] ?? "CliparinoPlayer";
    private string PlayerUrl => _configuration["Player:Url"] ?? "http://localhost:5290";
    private int Width => int.Parse(_configuration["OBS:Width"] ?? "1920");
    private int Height => int.Parse(_configuration["OBS:Height"] ?? "1080");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        _logger.LogInformation("OBS Health Supervisor starting...");

        await InitialConnectionAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
            try {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

                if (_obsController.IsConnected) await PerformHealthCheckAsync();
            } catch (OperationCanceledException) {
                break;
            } catch (Exception ex) {
                _logger.LogError(ex, "Error in OBS health check loop");
            }

        _logger.LogInformation("OBS Health Supervisor stopped");
    }

    private async Task InitialConnectionAsync(CancellationToken stoppingToken) {
        var host = _configuration["OBS:Host"] ?? "localhost";
        var port = int.Parse(_configuration["OBS:Port"] ?? "4455");
        var password = _configuration["OBS:Password"] ?? "";

        while (!stoppingToken.IsCancellationRequested && !_obsController.IsConnected)
            try {
                _logger.LogInformation("Attempting initial connection to OBS...");
                var connected = await _obsController.ConnectAsync(host, port, password);

                if (connected) {
                    _logger.LogInformation("Initial OBS connection successful");
                    _reconnectAttempts = 0;
                    _healthReporter?.ReportHealth("OBS", ComponentStatus.Healthy);

                    await EnsureObsConfigurationAsync();

                    break;
                }

                _reconnectAttempts++;
                var delay = _backoffPolicy.CalculateDelay(_reconnectAttempts);
                _logger.LogWarning(
                    "Initial OBS connection failed. Retry {Attempt}/{Max} in {Delay:0.0}s",
                    _reconnectAttempts, MaxReconnectAttempts, delay.TotalSeconds
                );

                _healthReporter?.ReportHealth("OBS", ComponentStatus.Unhealthy, "Connection failed");

                await Task.Delay(delay, stoppingToken);
            } catch (Exception ex) {
                _logger.LogError(ex, "Error during initial OBS connection");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
    }

    private async Task PerformHealthCheckAsync() {
        try {
            _logger.LogDebug("Performing OBS health check...");

            var hasDrift = await _obsController.CheckConfigurationDriftAsync(
                SceneName, SourceName, PlayerUrl, Width, Height
            );

            if (hasDrift) {
                _logger.LogWarning("Configuration drift detected. Attempting repair...");
                _healthReporter?.ReportHealth("OBS", ComponentStatus.Degraded, "Configuration drift detected");
                _healthReporter?.ReportRepairAction("OBS", "Configuration drift repair initiated");
                await RepairConfigurationAsync();
            } else {
                _logger.LogDebug("OBS health check passed - no drift detected");
                _healthReporter?.ReportHealth("OBS", ComponentStatus.Healthy);
            }
        } catch (Exception ex) {
            _logger.LogError(ex, "Error during OBS health check");
        }
    }

    private async Task RepairConfigurationAsync() {
        try {
            _logger.LogInformation("Repairing OBS configuration...");

            await _obsController.EnsureClipSceneAndSourceExistsAsync(
                SceneName, SourceName, PlayerUrl, Width, Height
            );

            await _obsController.SetBrowserSourceUrlAsync(SceneName, SourceName, PlayerUrl);

            await _obsController.RefreshBrowserSourceAsync(SourceName);

            _logger.LogInformation("OBS configuration repaired successfully");
            _healthReporter?.ReportRepairAction("OBS", "Configuration repaired successfully");
            _healthReporter?.ReportHealth("OBS", ComponentStatus.Healthy);
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to repair OBS configuration");
            _healthReporter?.ReportHealth("OBS", ComponentStatus.Degraded, $"Repair failed: {ex.Message}");
        }
    }

    private async Task EnsureObsConfigurationAsync() {
        try {
            _logger.LogInformation("Ensuring OBS scene and source configuration...");

            await _obsController.EnsureClipSceneAndSourceExistsAsync(
                SceneName, SourceName, PlayerUrl, Width, Height
            );

            _logger.LogInformation("OBS configuration verified");
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to ensure OBS configuration");
        }
    }

    private void OnObsDisconnected(object? sender, EventArgs e) {
        _logger.LogWarning("OBS disconnected. Starting reconnection process...");
        _healthReporter?.ReportHealth("OBS", ComponentStatus.Unhealthy, "Connection lost");
        _healthReporter?.ReportRepairAction("OBS", "Reconnection process started");
        _ = Task.Run(async () => await ReconnectAsync());
    }

    private void OnObsConnected(object? sender, EventArgs e) {
        _logger.LogInformation("OBS reconnected. Resetting reconnection attempts.");
        _reconnectAttempts = 0;
        _healthReporter?.ReportHealth("OBS", ComponentStatus.Healthy);
        _healthReporter?.ReportRepairAction("OBS", "Reconnected successfully");
        _ = Task.Run(async () => await EnsureObsConfigurationAsync());
    }

    private async Task ReconnectAsync() {
        var host = _configuration["OBS:Host"] ?? "localhost";
        var port = int.Parse(_configuration["OBS:Port"] ?? "4455");
        var password = _configuration["OBS:Password"] ?? "";

        while (_reconnectAttempts < MaxReconnectAttempts && !_obsController.IsConnected) {
            _reconnectAttempts++;
            var delay = _backoffPolicy.CalculateDelay(_reconnectAttempts);

            _logger.LogInformation(
                "Reconnection attempt {Attempt}/{Max} in {Delay:0.0}s",
                _reconnectAttempts, MaxReconnectAttempts, delay.TotalSeconds
            );

            await Task.Delay(delay);

            try {
                var connected = await _obsController.ConnectAsync(host, port, password);

                if (connected) {
                    _logger.LogInformation("Reconnection successful");
                    _reconnectAttempts = 0;
                    await EnsureObsConfigurationAsync();

                    return;
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Reconnection attempt failed");
            }
        }

        if (_reconnectAttempts >= MaxReconnectAttempts) {
            _logger.LogError("Max reconnection attempts reached. Stopping reconnection attempts.");
            _healthReporter?.ReportHealth("OBS", ComponentStatus.Unhealthy, "Max reconnection attempts reached");
        }
    }
}