using System.Threading.Channels;
using Cliparino.Core.Models;

namespace Cliparino.Core.Services;

/// <summary>
///     Background service that manages clip playback with a state machine and automatic queue processing.
/// </summary>
/// <remarks>
///     <para>
///         This class implements <see cref="IPlaybackEngine" /> as a BackgroundService that runs continuously,
///         processing playback commands via an internal unbounded channel. The engine maintains a state machine
///         that transitions through Idle, Loading, Playing, Cooldown, and Stopped states.
///     </para>
///     <para>
///         <strong>State Machine Flow:</strong><br />
///         Normal playback: Idle → Loading → Playing → Cooldown → Idle<br />
///         Manual stop: Playing → Stopped → Idle<br />
///     </para>
///     <para>
///         <strong>Key architectural patterns:</strong>
///         - Command pattern: PlayClipAsync/StopPlaybackAsync write commands to an internal channel
///         - Single-threaded processing: Commands are processed sequentially via Channel to prevent race conditions
///         - State locking: SemaphoreSlim ensures state transitions are atomic
///         - Quarantine mechanism: Clips that fail 3+ times are quarantined to prevent queue stalls
///         - Self-healing: Reports health status to <see cref="IHealthReporter" /> for monitoring
///     </para>
///     <para>
///         Dependencies:
///         - <see cref="IClipQueue" /> - Source of clips to play and last-played tracking
///         - <see cref="ILogger{TCategoryName}" /> - Structured logging for debugging
///         - <see cref="IHealthReporter" /> (optional) - Health status reporting for self-healing
///     </para>
///     <para>
///         Thread-safety: Public methods write to the internal channel (thread-safe). The background service
///         processes commands sequentially, ensuring state transitions are serialized and consistent.
///     </para>
///     <para>
///         Lifecycle: Singleton + HostedService. Starts automatically with the application and runs until shutdown.
///     </para>
/// </remarks>
public class PlaybackEngine(
    IClipQueue clipQueue,
    ILogger<PlaybackEngine> logger,
    IHealthReporter? healthReporter = null) : BackgroundService, IPlaybackEngine {
    /// <summary>
    ///     Tracks the number of times each clip (by ID) has failed to play. Used for quarantine logic.
    /// </summary>
    private readonly Dictionary<string, int> _clipFailureCount = new();

    /// <summary>
    ///     Unbounded channel for playback commands (Play, Stop). Provides sequential command processing.
    /// </summary>
    private readonly Channel<PlaybackCommand> _commandChannel = Channel.CreateUnbounded<PlaybackCommand>();

    /// <summary>
    ///     Set of clip IDs that have been quarantined after 3+ failures. Prevents infinite retry loops.
    /// </summary>
    private readonly HashSet<string> _quarantinedClips = new();

    /// <summary>
    ///     Semaphore for serializing state transitions. Ensures atomic state changes during command processing.
    /// </summary>
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    /// <inheritdoc />
    public PlaybackState CurrentState { get; private set; } = PlaybackState.Idle;

    /// <inheritdoc />
    public ClipData? CurrentClip { get; private set; }

    /// <inheritdoc />
    public async Task PlayClipAsync(ClipData clip, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(clip);

        clipQueue.Enqueue(clip);
        logger.LogInformation("Enqueued clip: {ClipTitle} by {Creator}", clip.Title, clip.CreatorName);

        await _commandChannel.Writer.WriteAsync(new PlaybackCommand(PlaybackCommandType.Play), cancellationToken);
    }

    /// <inheritdoc />
    public async Task ReplayAsync(CancellationToken cancellationToken = default) {
        var lastClip = clipQueue.LastPlayed;

        if (lastClip == null) {
            logger.LogWarning("No clip to replay");

            return;
        }

        await PlayClipAsync(lastClip, cancellationToken);
    }

    /// <inheritdoc />
    public async Task StopPlaybackAsync(CancellationToken cancellationToken = default) {
        await _commandChannel.Writer.WriteAsync(new PlaybackCommand(PlaybackCommandType.Stop), cancellationToken);
    }

    /// <summary>
    ///     Background service execution loop that continuously processes playback commands from the internal channel.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token signaled when the application is shutting down</param>
    /// <returns>A task representing the execution loop</returns>
    /// <remarks>
    ///     This method runs for the lifetime of the application, processing commands as they arrive.
    ///     Each command is handled sequentially to ensure state transitions are consistent.
    /// </remarks>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        logger.LogInformation("Playback engine started");

        await foreach (var command in _commandChannel.Reader.ReadAllAsync(stoppingToken))
            try {
                await ProcessCommandAsync(command, stoppingToken);
            } catch (Exception ex) {
                logger.LogError(ex, "Error processing playback command: {CommandType}", command.Type);
            }
    }

    /// <summary>
    ///     Processes a single playback command with state locking to ensure atomic state transitions.
    /// </summary>
    /// <param name="command">The command to process (Play or Stop)</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>A task representing the command processing</returns>
    /// <remarks>
    ///     The <see cref="_stateLock" /> ensures that only one command is processed at a time,
    ///     preventing race conditions during state transitions.
    /// </remarks>
    private async Task ProcessCommandAsync(PlaybackCommand command, CancellationToken cancellationToken) {
        await _stateLock.WaitAsync(cancellationToken);

        try {
            switch (command.Type) {
                case PlaybackCommandType.Play:
                    await HandlePlayCommandAsync(cancellationToken);

                    break;

                case PlaybackCommandType.Stop:
                    await HandleStopCommandAsync(cancellationToken);

                    break;
            }
        } finally {
            _stateLock.Release();
        }
    }

    /// <summary>
    ///     Handles a Play command by dequeuing the next clip and transitioning through the playback states.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>A task representing the playback operation</returns>
    /// <remarks>
    ///     <para>
    ///         This method implements the core playback logic:
    ///         1. Check if already playing (if so, queue the command for later)
    ///         2. Dequeue the next clip from <see cref="IClipQueue" />
    ///         3. Check if the clip is quarantined (skip if so, process next clip)
    ///         4. Transition: Idle → Loading → Playing
    ///         5. Wait for clip duration to elapse
    ///         6. Transition: Playing → Cooldown (2 seconds) → Idle
    ///         7. If more clips in queue, automatically play the next one
    ///     </para>
    ///     <para>
    ///         <strong>Quarantine Mechanism:</strong><br />
    ///         If a clip fails to play (exception during playback), the failure count is incremented.
    ///         After 3 failures, the clip is added to <see cref="_quarantinedClips" /> and will be
    ///         skipped in all future playback attempts. This prevents a single bad clip from
    ///         stalling the entire queue.
    ///     </para>
    /// </remarks>
    private async Task HandlePlayCommandAsync(CancellationToken cancellationToken) {
        if (CurrentState is PlaybackState.Playing or PlaybackState.Loading) {
            logger.LogDebug("Already playing or loading, clip will play next");

            return;
        }

        var clip = clipQueue.Dequeue();

        if (clip == null) {
            logger.LogDebug("No clips in queue");

            return;
        }

        if (_quarantinedClips.Contains(clip.Id)) {
            logger.LogWarning("Skipping quarantined clip: {ClipId} - {ClipTitle}", clip.Id, clip.Title);
            healthReporter?.ReportHealth(
                "PlaybackEngine", ComponentStatus.Degraded, $"Skipped quarantined clip: {clip.Title}"
            );

            if (clipQueue.Count > 0)
                await _commandChannel.Writer.WriteAsync(
                    new PlaybackCommand(PlaybackCommandType.Play), cancellationToken
                );

            return;
        }

        await TransitionToState(PlaybackState.Loading, cancellationToken);
        CurrentClip = clip;

        logger.LogInformation("Now playing: {ClipTitle} ({ClipUrl})", clip.Title, clip.Url);

        try {
            await TransitionToState(PlaybackState.Playing, cancellationToken);

            clipQueue.SetLastPlayed(clip);

            var playbackDuration = TimeSpan.FromSeconds(clip.DurationSeconds);
            await Task.Delay(playbackDuration, cancellationToken);

            _clipFailureCount.Remove(clip.Id);

            await TransitionToState(PlaybackState.Cooldown, cancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        } catch (Exception ex) {
            logger.LogError(ex, "Error during clip playback: {ClipTitle}", clip.Title);

            _clipFailureCount.TryGetValue(clip.Id, out var failureCount);
            failureCount++;
            _clipFailureCount[clip.Id] = failureCount;

            if (failureCount >= 3) {
                _quarantinedClips.Add(clip.Id);
                logger.LogWarning("Clip {ClipId} quarantined after {FailureCount} failures", clip.Id, failureCount);
                healthReporter?.ReportHealth(
                    "PlaybackEngine", ComponentStatus.Degraded, $"Quarantined clip: {clip.Title}"
                );
                healthReporter?.ReportRepairAction(
                    "PlaybackEngine", $"Quarantined clip {clip.Id} after {failureCount} failures"
                );
            }
        }

        await TransitionToState(PlaybackState.Idle, cancellationToken);
        CurrentClip = null;
        healthReporter?.ReportHealth("PlaybackEngine", ComponentStatus.Healthy);

        if (clipQueue.Count > 0)
            await _commandChannel.Writer.WriteAsync(new PlaybackCommand(PlaybackCommandType.Play), cancellationToken);
    }

    /// <summary>
    ///     Handles a Stop command by transitioning from Playing to Stopped to Idle.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>A task representing the stop operation</returns>
    /// <remarks>
    ///     <para>
    ///         State transitions: Playing → Stopped (1 second delay) → Idle
    ///     </para>
    ///     <para>
    ///         The 1-second delay in the Stopped state allows OBS and the browser source to clean up
    ///         before transitioning back to Idle. If more clips are in the queue, the next clip will
    ///         automatically begin playing after transitioning to Idle.
    ///     </para>
    /// </remarks>
    private async Task HandleStopCommandAsync(CancellationToken cancellationToken) {
        logger.LogInformation("Stopping playback");

        await TransitionToState(PlaybackState.Stopped, cancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

        CurrentClip = null;
        await TransitionToState(PlaybackState.Idle, cancellationToken);

        if (clipQueue.Count > 0)
            await _commandChannel.Writer.WriteAsync(new PlaybackCommand(PlaybackCommandType.Play), cancellationToken);
    }

    /// <summary>
    ///     Transitions the playback engine to a new state, logging the transition.
    /// </summary>
    /// <param name="newState">The target state to transition to</param>
    /// <param name="cancellationToken">Cancellation token to check for cancellation requests</param>
    /// <returns>A completed task</returns>
    /// <remarks>
    ///     This method provides a single point for all state transitions, ensuring consistent logging
    ///     and cancellation checking. All state changes flow through this method.
    /// </remarks>
    private Task TransitionToState(PlaybackState newState, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        logger.LogDebug("State transition: {OldState} -> {NewState}", CurrentState, newState);
        CurrentState = newState;

        return Task.CompletedTask;
    }
}

/// <summary>
///     Internal command record used for channel-based communication within the playback engine.
/// </summary>
/// <param name="Type">The type of playback command (Play or Stop)</param>
internal record PlaybackCommand(PlaybackCommandType Type);

/// <summary>
///     Internal enum defining the types of commands that can be sent to the playback engine.
/// </summary>
internal enum PlaybackCommandType {
    /// <summary>
    ///     Command to play the next clip in the queue.
    /// </summary>
    Play,

    /// <summary>
    ///     Command to stop the currently playing clip.
    /// </summary>
    Stop
}