using System.Threading.Channels;
using Cliparino.Core.Models;
using Microsoft.Extensions.Options;

namespace Cliparino.Core.Services;

/// <summary>
///     Background service that manages clip playback with a state machine and automatic queue processing.
///     State flow: Idle → Loading → Playing → Cooldown → Idle (or Playing → Stopped → Idle on manual stop).
///     Clips that fail 3+ times are quarantined to prevent queue stalls.
/// </summary>
public class PlaybackEngine(
    IClipQueue clipQueue,
    IObsController obsController,
    IOptionsMonitor<ObsOptions> obsOptions,
    ILogger<PlaybackEngine> logger,
    IHealthReporter? healthReporter = null) : BackgroundService, IPlaybackEngine {
    private readonly Dictionary<string, int> _clipFailureCount = new();
    private readonly Channel<PlaybackCommand> _commandChannel = Channel.CreateUnbounded<PlaybackCommand>();
    private readonly HashSet<string> _quarantinedClips = [];

    /// <inheritdoc />
    public PlaybackState CurrentState { get; private set; } = PlaybackState.Idle;

    /// <inheritdoc />
    public ClipData? CurrentClip { get; private set; }

    /// <inheritdoc />
    public async Task PlayClipAsync(ClipData clip, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(clip);

        clipQueue.Enqueue(clip);
        logger.LogInformation("Enqueued clip: {ClipTitle} by {Creator}", clip.Title, clip.Creator.DisplayName);

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

    /// <summary>Reads commands from the channel and dispatches them sequentially.</summary>
    /// <param name="stoppingToken">Token signaled when the host is shutting down.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        logger.LogInformation("Playback engine started");

        await foreach (var command in _commandChannel.Reader.ReadAllAsync(stoppingToken))
            try {
                switch (command.Type) {
                    case PlaybackCommandType.Play:
                        await HandlePlayCommandAsync(stoppingToken);

                        break;
                    case PlaybackCommandType.Stop:
                        await HandleStopCommandAsync(stoppingToken);

                        break;
                    default:
                        throw new InvalidOperationException($"Unknown playback command type: {command.Type}");
                }
            } catch (Exception ex) {
                logger.LogError(ex, "Error processing playback command: {CommandType}", command.Type);
            }
    }

    /// <summary>Dequeues the next clip and runs the Loading → Playing → Cooldown cycle.</summary>
    /// <param name="cancellationToken">Token to cancel the playback wait.</param>
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
            healthReporter?.ReportHealth("PlaybackEngine", ComponentStatus.Degraded,
                $"Skipped quarantined clip: {clip.Title}");
            await EnqueueNextIfAvailableAsync(cancellationToken);

            return;
        }

        TransitionToState(PlaybackState.Loading, cancellationToken);
        CurrentClip = clip;

        logger.LogInformation("Now playing: {ClipTitle} ({ClipUrl})", clip.Title, clip.Url);

        try {
            if (obsController.IsConnected)
                await SetObsSourceVisibilityAsync(true);
            else
                logger.LogWarning("OBS is not connected. Skipping OBS updates for clip: {ClipTitle}", clip.Title);

            TransitionToState(PlaybackState.Playing, cancellationToken);
            clipQueue.SetLastPlayed(clip);

            await Task.Delay(TimeSpan.FromSeconds(clip.DurationSeconds), cancellationToken);

            _clipFailureCount.Remove(clip.Id);

            TransitionToState(PlaybackState.Cooldown, cancellationToken);

            if (obsController.IsConnected)
                await SetObsSourceVisibilityAsync(false);

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        } catch (Exception ex) {
            logger.LogError(ex, "Error during clip playback: {ClipTitle}", clip.Title);

            _clipFailureCount.TryGetValue(clip.Id, out var failureCount);
            failureCount++;
            _clipFailureCount[clip.Id] = failureCount;

            if (failureCount >= 3) {
                _quarantinedClips.Add(clip.Id);
                logger.LogWarning("Clip {ClipId} quarantined after {FailureCount} failures", clip.Id, failureCount);
                healthReporter?.ReportHealth("PlaybackEngine", ComponentStatus.Degraded,
                    $"Quarantined clip: {clip.Title}");
                healthReporter?.ReportRepairAction("PlaybackEngine",
                    $"Quarantined clip {clip.Id} after {failureCount} failures");
            }
        }

        TransitionToState(PlaybackState.Idle, cancellationToken);
        CurrentClip = null;
        healthReporter?.ReportHealth("PlaybackEngine", ComponentStatus.Healthy);

        await EnqueueNextIfAvailableAsync(cancellationToken);
    }

    /// <summary>Hides the OBS source and transitions through Stopped → Idle.</summary>
    /// <param name="cancellationToken">Token to cancel the stop delay.</param>
    private async Task HandleStopCommandAsync(CancellationToken cancellationToken) {
        logger.LogInformation("Stopping playback");

        if (obsController.IsConnected)
            await SetObsSourceVisibilityAsync(false);

        TransitionToState(PlaybackState.Stopped, cancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

        CurrentClip = null;
        TransitionToState(PlaybackState.Idle, cancellationToken);

        await EnqueueNextIfAvailableAsync(cancellationToken);
    }

    /// <summary>Shows or hides the Cliparino scene (used as a nested scene source) in the current scene.</summary>
    /// <param name="visible">True to show the scene source, false to hide it.</param>
    private async Task SetObsSourceVisibilityAsync(bool visible) {
        var currentScene = await obsController.GetCurrentSceneAsync();
        if (!string.IsNullOrEmpty(currentScene))
            await obsController.SetSourceVisibilityAsync(currentScene, obsOptions.CurrentValue.SceneName, visible);
    }

    /// <summary>Sends a Play command if the queue still has clips waiting.</summary>
    /// <param name="cancellationToken">Token to cancel the channel write operation.</param>
    private async Task EnqueueNextIfAvailableAsync(CancellationToken cancellationToken) {
        if (clipQueue.Count > 0)
            await _commandChannel.Writer.WriteAsync(new PlaybackCommand(PlaybackCommandType.Play), cancellationToken);
    }

    /// <summary>Updates <see cref="CurrentState" /> and logs the transition.</summary>
    /// <param name="newState">The state to transition to.</param>
    /// <param name="cancellationToken">Token checked before transitioning; throws if canceled.</param>
    private void TransitionToState(PlaybackState newState, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogDebug("State transition: {OldState} -> {NewState}", CurrentState, newState);
        CurrentState = newState;
    }
}

internal record PlaybackCommand(PlaybackCommandType Type);

internal enum PlaybackCommandType {
    Play,
    Stop
}