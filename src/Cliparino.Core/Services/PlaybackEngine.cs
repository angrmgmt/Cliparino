using System.Threading.Channels;
using Cliparino.Core.Models;

namespace Cliparino.Core.Services;

public class PlaybackEngine(
    IClipQueue clipQueue,
    ILogger<PlaybackEngine> logger,
    IHealthReporter? healthReporter = null) : BackgroundService, IPlaybackEngine {
    private readonly Dictionary<string, int> _clipFailureCount = new();
    private readonly Channel<PlaybackCommand> _commandChannel = Channel.CreateUnbounded<PlaybackCommand>();
    private readonly HashSet<string> _quarantinedClips = new();
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    public PlaybackState CurrentState { get; private set; } = PlaybackState.Idle;

    public ClipData? CurrentClip { get; private set; }

    public async Task PlayClipAsync(ClipData clip, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(clip);

        clipQueue.Enqueue(clip);
        logger.LogInformation("Enqueued clip: {ClipTitle} by {Creator}", clip.Title, clip.CreatorName);

        await _commandChannel.Writer.WriteAsync(new PlaybackCommand(PlaybackCommandType.Play), cancellationToken);
    }

    public async Task ReplayAsync(CancellationToken cancellationToken = default) {
        var lastClip = clipQueue.LastPlayed;

        if (lastClip == null) {
            logger.LogWarning("No clip to replay");

            return;
        }

        await PlayClipAsync(lastClip, cancellationToken);
    }

    public async Task StopPlaybackAsync(CancellationToken cancellationToken = default) {
        await _commandChannel.Writer.WriteAsync(new PlaybackCommand(PlaybackCommandType.Stop), cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        logger.LogInformation("Playback engine started");

        await foreach (var command in _commandChannel.Reader.ReadAllAsync(stoppingToken))
            try {
                await ProcessCommandAsync(command, stoppingToken);
            } catch (Exception ex) {
                logger.LogError(ex, "Error processing playback command: {CommandType}", command.Type);
            }
    }

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

    private async Task HandleStopCommandAsync(CancellationToken cancellationToken) {
        logger.LogInformation("Stopping playback");

        await TransitionToState(PlaybackState.Stopped, cancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

        CurrentClip = null;
        await TransitionToState(PlaybackState.Idle, cancellationToken);

        if (clipQueue.Count > 0)
            await _commandChannel.Writer.WriteAsync(new PlaybackCommand(PlaybackCommandType.Play), cancellationToken);
    }

    private Task TransitionToState(PlaybackState newState, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        logger.LogDebug("State transition: {OldState} -> {NewState}", CurrentState, newState);
        CurrentState = newState;

        return Task.CompletedTask;
    }
}

internal record PlaybackCommand(PlaybackCommandType Type);

internal enum PlaybackCommandType {
    Play,
    Stop
}