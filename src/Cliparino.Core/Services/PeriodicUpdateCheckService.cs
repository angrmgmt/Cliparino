namespace Cliparino.Core.Services;

/// <summary>
///     Periodically checks for application updates in the background.
/// </summary>
/// <remarks>
///     <para>
///         This is a <see cref="BackgroundService" /> that continuously monitors for new application versions
///         using <see cref="IUpdateChecker" />, with configurable check frequency and startup behavior.
///     </para>
///     <para>
///         Key features:
///         <list type="bullet">
///             <item>Optionally performs an initial update check on startup (configurable)</item>
///             <item>Periodically checks for updates at configurable intervals (default: 24 hours)</item>
///             <item>Logs when newer versions are available with download URL</item>
///             <item>Gracefully handles cancellation and errors without crashing</item>
///             <item>Non-blocking: update checks do not affect application functionality</item>
///         </list>
///     </para>
///     <para>
///         Configuration (appsettings.json):
///         <list type="code">
///             <item>"Update:CheckOnStartup": bool (default: true) - Perform check 10 seconds after startup</item>
///             <item>"Update:CheckIntervalHours": int (default: 24) - Interval between periodic checks</item>
///         </list>
///     </para>
///     <para>
///         Dependencies:
///         <list type="bullet">
///             <item><see cref="IUpdateChecker" /> - The service that actually checks for updates</item>
///             <item><see cref="IConfiguration" /> - Configuration provider for check settings</item>
///             <item><see cref="ILogger{PeriodicUpdateCheckService}" /> - Logging check results and errors</item>
///         </list>
///     </para>
///     <para>Thread-safety: Thread-safe. BackgroundService provides synchronization for ExecuteAsync.</para>
///     <para>
///         Lifecycle: Registered as HostedService in <see cref="Program" />. Starts automatically when application
///         starts.
///     </para>
/// </remarks>
public class PeriodicUpdateCheckService : BackgroundService {
    private readonly IConfiguration _configuration;
    private readonly ILogger<PeriodicUpdateCheckService> _logger;
    private readonly IUpdateChecker _updateChecker;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PeriodicUpdateCheckService" /> class.
    /// </summary>
    /// <param name="updateChecker">The update checker service to use for checking versions.</param>
    /// <param name="configuration">Configuration provider for check interval and startup settings.</param>
    /// <param name="logger">Logger instance for recording check results.</param>
    public PeriodicUpdateCheckService(IUpdateChecker updateChecker,
        IConfiguration configuration,
        ILogger<PeriodicUpdateCheckService> logger) {
        _updateChecker = updateChecker;
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <summary>
    ///     Executes the background update check loop until cancellation is requested.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         If <c>Update:CheckOnStartup</c> is true, performs an initial check after a 10-second delay.
    ///         Then enters an infinite loop checking every <c>Update:CheckIntervalHours</c> until cancellation.
    ///     </para>
    ///     <para>Errors during checking are caught and logged as non-fatal warnings.</para>
    /// </remarks>
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

    /// <summary>
    ///     Performs a single update check and logs the result.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         If a newer version is available, logs at Information level with the version and download URL.
    ///         If the current version is up-to-date, logs at Debug level. Exceptions are logged but not thrown.
    ///     </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token for the async operation.</param>
    private async Task PerformUpdateCheckAsync(CancellationToken cancellationToken) {
        try {
            var updateInfo = await _updateChecker.CheckForUpdatesAsync(cancellationToken);

            if (updateInfo?.IsNewer == true)
                _logger.LogInformation(
                    "Update available: v{LatestVersion} (current: v{CurrentVersion}). Download: {ReleaseUrl}",
                    updateInfo.LatestVersion,
                    _updateChecker.CurrentVersion,
                    updateInfo.ReleaseUrl);
            else
                _logger.LogDebug("No updates available. Current version: v{CurrentVersion}",
                    _updateChecker.CurrentVersion);
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to check for updates");
        }
    }
}