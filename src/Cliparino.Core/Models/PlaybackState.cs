namespace Cliparino.Core.Models;

/// <summary>
///     Represents the current state of the clip playback engine.
/// </summary>
/// <remarks>
///     <para>
///         This enum defines the state machine for clip playback managed by <see cref="Services.PlaybackEngine" />.
///         The playback engine transitions through these states during the lifecycle of playing a clip.
///     </para>
///     <para>
///         <strong>Normal Playback Flow:</strong><br />
///         Idle → Loading → Playing → Cooldown → Idle (or back to Loading if more clips in queue)
///     </para>
///     <para>
///         <strong>Manual Stop Flow:</strong><br />
///         Playing → Stopped → Idle (or back to Loading if more clips in queue)
///     </para>
///     <para>
///         The state is exposed via <see cref="Services.IPlaybackEngine.CurrentState" /> and can be
///         monitored by HTTP controllers, health supervisors, and diagnostic services.
///     </para>
/// </remarks>
public enum PlaybackState {
    /// <summary>
    ///     No clip is currently playing and the engine is waiting for the next clip to be queued.
    /// </summary>
    Idle,

    /// <summary>
    ///     A clip has been dequeued and is being prepared for playback (fetching metadata, updating OBS source).
    /// </summary>
    Loading,

    /// <summary>
    ///     A clip is actively playing. The engine is waiting for the clip duration to elapse.
    /// </summary>
    Playing,

    /// <summary>
    ///     A clip has finished playing and the engine is in a brief cooldown period before transitioning to Idle.
    ///     This cooldown (typically 2 seconds) allows OBS and the browser source to settle.
    /// </summary>
    Cooldown,

    /// <summary>
    ///     A clip was manually stopped via the !stop command or HTTP API. The engine will transition to Idle shortly.
    /// </summary>
    Stopped
}