namespace Cliparino.Core.Services;

/// <summary>
///     Monitors OBS connection health, performs automatic reconnection with exponential backoff, and repairs configuration
///     drift.
/// </summary>
/// <remarks>
///     <para>
///         This background service is the self-healing supervisor for OBS integration. It handles three critical
///         responsibilities:
///         1. Initial connection establishment with retry logic
///         2. Periodic health checks to detect configuration drift
///         3. Automatic reconnection when OBS disconnects (e.g., OBS closes, network error)
///     </para>
///     <para>
///         Dependencies:
///         - <see cref="IObsController" />: OBS WebSocket controller to supervise
///         - <see cref="ILogger{TCategoryName}" />: Diagnostic logging
///         - <see cref="IConfiguration" />: OBS connection settings (host, port, password, scene/source config)
///         - <see cref="IHealthReporter" /> (optional): Health status reporting for diagnostics endpoint
///     </para>
///     <para>
///         Thread-safety: Runs as a BackgroundService with a single background thread. Event handlers
///         (OnObsDisconnected/OnObsConnected) spawn new tasks via Task.Run to avoid blocking event dispatch.
///     </para>
///     <para>
///         Lifecycle: Registered as a hosted service (singleton). Starts automatically when the application
///         starts and stops gracefully on shutdown. Runs continuously for the application lifetime.
///     </para>
///     <para>
///         Self-Healing Mechanisms:
///         - Exponential backoff reconnection (using <see cref="BackoffPolicy" />) up to 10 attempts
///         - Configuration drift detection (1-minute intervals when connected)
///         - Automatic scene/source repair when drift is detected
///         - Post-connection verification to ensure OBS is properly configured
///     </para>
/// </remarks>
public class ObsHealthSupervisor : BackgroundService {
    private const int MaxReconnectAttempts = 10;
    private readonly BackoffPolicy _backoffPolicy = BackoffPolicy.Default;
    private readonly IConfiguration _configuration;
    private readonly IHealthReporter? _healthReporter;
    private readonly ILogger<ObsHealthSupervisor> _logger;
    private readonly IObsController _obsController;
    private int _reconnectAttempts;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ObsHealthSupervisor" /> class.
    /// </summary>
    /// <param name="obsController">The OBS controller to supervise</param>
    /// <param name="logger">Logger for diagnostic messages</param>
    /// <param name="configuration">Configuration containing OBS connection and scene settings</param>
    /// <param name="healthReporter">Optional health reporter for diagnostics (null in tests)</param>
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

    /// <summary>
    ///     Gets the configured OBS scene name for clip playback.
    /// </summary>
    private string SceneName => _configuration["OBS:SceneName"] ?? "Cliparino";

    /// <summary>
    ///     Gets the configured browser source name for the clip player.
    /// </summary>
    private string SourceName => _configuration["OBS:SourceName"] ?? "CliparinoPlayer";

    /// <summary>
    ///     Gets the URL of the embedded clip player web interface.
    /// </summary>
    private string PlayerUrl => _configuration["Player:Url"] ?? "http://localhost:5290";

    /// <summary>
    ///     Gets the configured browser source width in pixels.
    /// </summary>
    private int Width => int.Parse(_configuration["OBS:Width"] ?? "1920");

    /// <summary>
    ///     Gets the configured browser source height in pixels.
    /// </summary>
    private int Height => int.Parse(_configuration["OBS:Height"] ?? "1080");

    /// <summary>
    ///     Executes the health supervision loop: initial connection, periodic drift checks, and reconnection.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token signaling application shutdown</param>
    /// <returns>A task representing the background service execution</returns>
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

    /// <summary>
    ///     Establishes the initial OBS connection with retry logic using exponential backoff.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token signaling application shutdown</param>
    /// <returns>A task representing the connection attempt loop</returns>
    /// <remarks>
    ///     Retries indefinitely until connected or shutdown. Uses <see cref="BackoffPolicy" /> to
    ///     avoid overwhelming OBS with connection attempts if it's not running.
    /// </remarks>
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

    /// <summary>
    ///     Performs a health check to detect configuration drift in OBS scene and source settings.
    /// </summary>
    /// <returns>A task representing the health check operation</returns>
    /// <remarks>
    ///     Checks if the browser source URL, width, and height match expected values. If drift is
    ///     detected, automatically triggers <see cref="RepairConfigurationAsync" /> to restore desired state.
    /// </remarks>
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

    /// <summary>
    ///     Repairs OBS configuration drift by recreating/updating the scene, source, and browser URL.
    /// </summary>
    /// <returns>A task representing the repair operation</returns>
    /// <remarks>
    ///     This method restores the desired state by ensuring the scene and source exist with correct
    ///     settings, updating the browser source URL, and refreshing the source. Called automatically
    ///     when drift is detected during health checks.
    /// </remarks>
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

    /// <summary>
    ///     Ensures that the OBS scene and browser source are correctly configured.
    /// </summary>
    /// <returns>A task representing the configuration verification operation</returns>
    /// <remarks>
    ///     Called after initial connection and after reconnection to verify that OBS has the required
    ///     scene and source. This prevents playback failures due to missing OBS resources.
    /// </remarks>
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

    /// <summary>
    ///     Event handler for OBS disconnection events, initiating automatic reconnection.
    /// </summary>
    /// <param name="sender">The event source (OBS controller)</param>
    /// <param name="e">Event arguments</param>
    /// <remarks>
    ///     Spawns a background task to handle reconnection without blocking the event dispatch.
    ///     Reports unhealthy status to the health reporter.
    /// </remarks>
    private void OnObsDisconnected(object? sender, EventArgs e) {
        _logger.LogWarning("OBS disconnected. Starting reconnection process...");
        _healthReporter?.ReportHealth("OBS", ComponentStatus.Unhealthy, "Connection lost");
        _healthReporter?.ReportRepairAction("OBS", "Reconnection process started");
        _ = Task.Run(async () => await ReconnectAsync());
    }

    /// <summary>
    ///     Event handler for OBS reconnection events, resetting backoff state and verifying configuration.
    /// </summary>
    /// <param name="sender">The event source (OBS controller)</param>
    /// <param name="e">Event arguments</param>
    /// <remarks>
    ///     Resets the reconnection attempt counter and spawns a background task to verify OBS
    ///     configuration. Reports healthy status to the health reporter.
    /// </remarks>
    private void OnObsConnected(object? sender, EventArgs e) {
        _logger.LogInformation("OBS reconnected. Resetting reconnection attempts.");
        _reconnectAttempts = 0;
        _healthReporter?.ReportHealth("OBS", ComponentStatus.Healthy);
        _healthReporter?.ReportRepairAction("OBS", "Reconnected successfully");
        _ = Task.Run(async () => await EnsureObsConfigurationAsync());
    }

    /// <summary>
    ///     Attempts to reconnect to OBS using exponential backoff up to a maximum number of retries.
    /// </summary>
    /// <returns>A task representing the reconnection loop</returns>
    /// <remarks>
    ///     <para>
    ///         Uses <see cref="BackoffPolicy" /> to calculate delays between reconnection attempts.
    ///         Stops after <see cref="MaxReconnectAttempts" /> (10) consecutive failures.
    ///     </para>
    ///     <para>
    ///         If reconnection succeeds, <see cref="OnObsConnected" /> is automatically invoked via the
    ///         Connected event, which resets the attempt counter and verifies configuration.
    ///     </para>
    /// </remarks>
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