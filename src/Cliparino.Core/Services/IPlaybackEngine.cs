using Cliparino.Core.Models;

namespace Cliparino.Core.Services;

/// <summary>
///     Defines the contract for the clip playback state machine and queue processor.
/// </summary>
/// <remarks>
///     <para>
///         This interface is implemented by <see cref="PlaybackEngine" /> as a BackgroundService
///         and is used by <see cref="ICommandRouter" />, HTTP controllers, and health supervisors
///         to control and monitor clip playback.
///     </para>
///     <para>
///         Key responsibilities:
///         - Process clips from <see cref="IClipQueue" /> in FIFO order
///         - Manage playback state machine (Idle → Loading → Playing → Cooldown → Idle)
///         - Handle playback commands (play, stop, replay) via internal channel
///         - Quarantine clips that fail repeatedly to prevent queue stalls
///         - Report health status for self-healing supervision
///     </para>
///     <para>
///         Thread-safety: All methods are async and thread-safe. Commands are processed sequentially
///         via an internal Channel to prevent race conditions during state transitions.
///     </para>
///     <para>
///         Lifecycle: Singleton + HostedService. The background service runs continuously, processing
///         commands from an internal channel and transitioning between playback states.
///     </para>
/// </remarks>
public interface IPlaybackEngine {
    /// <summary>
    ///     Gets the current state of the playback engine.
    /// </summary>
    /// <value>
    ///     The current <see cref="PlaybackState" />. Can be Idle, Loading, Playing, Cooldown, or Stopped.
    ///     This property is updated atomically during state transitions and is safe to read from any thread.
    /// </value>
    PlaybackState CurrentState { get; }

    /// <summary>
    ///     Gets the clip currently being played, or null if no clip is active.
    /// </summary>
    /// <value>
    ///     The <see cref="ClipData" /> of the currently playing clip, or null if the engine is Idle or Stopped.
    ///     This property is updated when the engine transitions to the Loading state and cleared when
    ///     transitioning back to Idle.
    /// </value>
    ClipData? CurrentClip { get; }

    /// <summary>
    ///     Enqueues a clip for playback and triggers the playback engine if idle.
    /// </summary>
    /// <param name="clip">The clip to play</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>
    ///     A task representing the async operation. Completes when the clip has been enqueued
    ///     and a play command has been sent to the internal processing channel.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="clip" /> is null</exception>
    /// <exception cref="OperationCanceledException">Thrown when canceled via <paramref name="cancellationToken" /></exception>
    /// <remarks>
    ///     This method adds the clip to <see cref="IClipQueue" /> and sends a play command to the
    ///     internal channel. If the engine is already playing a clip, the new clip will be queued
    ///     and will play after the current clip finishes.
    /// </remarks>
    Task PlayClipAsync(ClipData clip, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Re-enqueues the most recently played clip for playback.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>
    ///     A task representing the async operation. Completes when the last played clip has been
    ///     re-enqueued, or immediately if no clip has been played yet.
    /// </returns>
    /// <exception cref="OperationCanceledException">Thrown when canceled via <paramref name="cancellationToken" /></exception>
    /// <remarks>
    ///     This method retrieves the last played clip from <see cref="IClipQueue.LastPlayed" /> and
    ///     calls <see cref="PlayClipAsync" />. If no clip has been played yet, this method logs a
    ///     warning and returns without action.
    /// </remarks>
    Task ReplayAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Stops the currently playing clip and advances to the next clip in queue.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>
    ///     A task representing the async operation. Completes when a stop command has been sent
    ///     to the internal processing channel.
    /// </returns>
    /// <exception cref="OperationCanceledException">Thrown when canceled via <paramref name="cancellationToken" /></exception>
    /// <remarks>
    ///     <para>
    ///         This method transitions the engine from Playing to Stopped to Idle. If additional clips
    ///         are in the queue, the next clip will automatically begin playing after the transition to Idle.
    ///     </para>
    ///     <para>
    ///         If no clip is currently playing (state is Idle or Cooldown), this method has no effect.
    ///     </para>
    /// </remarks>
    Task StopPlaybackAsync(CancellationToken cancellationToken = default);
}