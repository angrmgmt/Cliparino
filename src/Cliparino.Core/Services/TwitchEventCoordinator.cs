using Cliparino.Core.Models;

namespace Cliparino.Core.Services;

/// <summary>
///     Orchestrates Twitch event sources, providing automatic failover between EventSub WebSockets and IRC fallback.
/// </summary>
/// <remarks>
///     <para>
///         This service maintains the primary connection to Twitch's EventSub via WebSockets. If the connection fails,
///         it automatically falls back to IRC to ensure continuous event monitoring.
///     </para>
///     <para>
///         Failover Logic: EventSub (Primary) -> IRC (Fallback). It uses an exponential backoff policy for
///         reconnection attempts.
///     </para>
///     <para>Dependencies:</para>
///     <list type="bullet">
///         <item>
///             <description><see cref="TwitchEventSubWebSocketSource" />: Primary event source using modern WebSockets.</description>
///         </item>
///         <item>
///             <description><see cref="TwitchIrcEventSource" />: Fallback event source using IRC.</description>
///         </item>
///         <item>
///             <description><see cref="ICommandRouter" />: Routes parsed events to their respective handlers.</description>
///         </item>
///         <item>
///             <description><see cref="IHealthReporter" />: Reports connection status and failover events.</description>
///         </item>
///     </list>
///     <para>
///         Thread-safety: Implemented as a <see cref="BackgroundService" />, processing events in a single-threaded loop
///         per source.
///     </para>
///     <para>Lifecycle: Singleton hosted service.</para>
/// </remarks>
public class TwitchEventCoordinator : BackgroundService {
    private readonly BackoffPolicy _backoffPolicy = BackoffPolicy.Default;
    private readonly ICommandRouter _commandRouter;
    private readonly TwitchEventSubWebSocketSource _eventSubSource;
    private readonly IHealthReporter? _healthReporter;
    private readonly TwitchIrcEventSource _ircSource;
    private readonly ILogger<TwitchEventCoordinator> _logger;

    private ITwitchEventSource? _activeSource;
    private int _reconnectAttempts;
    private bool _useEventSub = true;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TwitchEventCoordinator" /> class.
    /// </summary>
    /// <param name="eventSubSource">The primary EventSub WebSocket source.</param>
    /// <param name="ircSource">The fallback IRC source.</param>
    /// <param name="commandRouter">The command router for event processing.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="healthReporter">The health reporter instance.</param>
    public TwitchEventCoordinator(
        TwitchEventSubWebSocketSource eventSubSource,
        TwitchIrcEventSource ircSource,
        ICommandRouter commandRouter,
        ILogger<TwitchEventCoordinator> logger,
        IHealthReporter? healthReporter = null
    ) {
        _eventSubSource = eventSubSource;
        _ircSource = ircSource;
        _commandRouter = commandRouter;
        _logger = logger;
        _healthReporter = healthReporter;
    }

    /// <summary>
    ///     Executes the coordination logic, managing connections and event processing.
    /// </summary>
    /// <param name="stoppingToken">Triggered when the host is shutting down.</param>
    /// <returns>A task representing the background operation.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        _logger.LogInformation("Twitch Event Coordinator starting...");

        while (!stoppingToken.IsCancellationRequested)
            try {
                await ConnectToEventSourceAsync(stoppingToken);

                if (_activeSource != null) await ProcessEventsAsync(_activeSource, stoppingToken);
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                _logger.LogError(ex, "Error in event coordinator, will retry with fallback");
                _healthReporter?.ReportHealth("Twitch", ComponentStatus.Degraded, $"Error: {ex.Message}");

                if (_activeSource != null) await _activeSource.DisconnectAsync(stoppingToken);

                if (_useEventSub) {
                    _logger.LogWarning("EventSub failed, falling back to IRC");
                    _healthReporter?.ReportRepairAction("Twitch", "Falling back to IRC");
                    _useEventSub = false;
                }

                _reconnectAttempts++;
                var delay = _backoffPolicy.CalculateDelay(_reconnectAttempts);
                _logger.LogInformation("Reconnecting in {Delay:0.0}s...", delay.TotalSeconds);

                await Task.Delay(delay, stoppingToken);
            }

        _logger.LogInformation("Twitch Event Coordinator stopping...");

        if (_activeSource != null) await _activeSource.DisconnectAsync(CancellationToken.None);
    }

    private async Task ConnectToEventSourceAsync(CancellationToken cancellationToken) {
        if (_useEventSub)
            try {
                _logger.LogInformation("Attempting to connect via EventSub WebSocket...");
                await _eventSubSource.ConnectAsync(cancellationToken);
                _activeSource = _eventSubSource;
                _reconnectAttempts = 0;
                _logger.LogInformation("EventSub connection established successfully");
                _healthReporter?.ReportHealth("Twitch", ComponentStatus.Healthy);
                _healthReporter?.ReportRepairAction("Twitch", "EventSub connection established");

                return;
            } catch (Exception ex) {
                _logger.LogWarning(ex, "EventSub connection failed, falling back to IRC");
                _healthReporter?.ReportHealth("Twitch", ComponentStatus.Degraded, "EventSub unavailable, using IRC");
                _useEventSub = false;
            }

        _logger.LogInformation("Connecting via IRC fallback...");
        await _ircSource.ConnectAsync(cancellationToken);
        _activeSource = _ircSource;
        _reconnectAttempts = 0;
        _logger.LogInformation("IRC connection established successfully");
        _healthReporter?.ReportHealth("Twitch", ComponentStatus.Degraded, "Using IRC fallback");
        _healthReporter?.ReportRepairAction("Twitch", "IRC fallback connection established");
    }

    private async Task ProcessEventsAsync(ITwitchEventSource source, CancellationToken cancellationToken) {
        _logger.LogInformation("Processing events from {SourceName}", source.SourceName);

        await foreach (var twitchEvent in source.StreamEventsAsync(cancellationToken))
            try {
                await HandleEventAsync(twitchEvent, cancellationToken);
            } catch (Exception ex) {
                _logger.LogError(ex, "Error handling event: {Event}", twitchEvent);
            }
    }

    private async Task HandleEventAsync(TwitchEvent twitchEvent, CancellationToken cancellationToken) {
        switch (twitchEvent) {
            case ChatMessageEvent chatEvent:
                await HandleChatMessageAsync(chatEvent.Message, cancellationToken);

                break;

            case RaidEvent raidEvent:
                await HandleRaidAsync(raidEvent, cancellationToken);

                break;

            default:
                _logger.LogDebug("Unhandled event type: {EventType}", twitchEvent.GetType().Name);

                break;
        }
    }

    private async Task HandleChatMessageAsync(ChatMessage message, CancellationToken cancellationToken) {
        await _commandRouter.ProcessChatMessageAsync(message, cancellationToken);
    }

    private async Task HandleRaidAsync(RaidEvent raidEvent, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation(
            "Raid detected from {Raider} with {Viewers} viewers",
            raidEvent.RaiderUsername, raidEvent.ViewerCount
        );

        await Task.CompletedTask;
    }
}