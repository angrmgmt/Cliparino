using Cliparino.Core.Models;

namespace Cliparino.Core.Services;

/// <summary>
///     Defines the contract for executing Twitch shoutouts with random clip playback.
/// </summary>
/// <remarks>
///     <para>
///         This interface is implemented by <see cref="ShoutoutService" /> and is used by
///         <see cref="CommandRouter" /> to handle <c>!so @username</c> and <c>!shoutout @username</c> commands,
///         as well as automatic shoutouts triggered by raid events.
///     </para>
///     <para>
///         Key responsibilities:
///         - Select random clips from a broadcaster's channel with optional filters
///         - Send Twitch's native <c>/shoutout</c> command via the Helix API
///         - Play the selected clip using a separate shoutout queue
///         - Apply configurable filters (featured only, max duration, max age)
///     </para>
///     <para>
///         <strong>Clip selection strategy:</strong><br />
///         1. Fetch clips from target broadcaster (recent clips within configured age limit)<br />
///         2. Filter by duration (exclude clips longer than max duration)<br />
///         3. Optionally filter to featured clips only<br />
///         4. Select random clip from filtered results<br />
///         5. If no clips match filters, fallback to any available clip
///     </para>
///     <para>
///         Thread-safety: All methods are async and stateless (except for injected dependencies).
///         Safe to call from multiple threads concurrently.
///     </para>
/// </remarks>
public interface IShoutoutService {
    /// <summary>
    ///     Selects a random clip from a broadcaster's channel for shoutout playback.
    /// </summary>
    /// <param name="broadcasterName">The broadcaster's username to select a clip from</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>
    ///     A task containing a randomly selected <see cref="ClipData" />, or null if no clips
    ///     are available or the broadcaster was not found.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         This method applies the configured filters from appsettings.json:
    ///         - <c>Shoutout:FeaturedOnly</c> - Only consider clips marked as featured
    ///         - <c>Shoutout:MaxDurationSeconds</c> - Exclude clips longer than this duration
    ///         - <c>Shoutout:MaxAgeInDays</c> - Only consider clips created within this timeframe
    ///     </para>
    ///     <para>
    ///         If no clips match the filters, the method falls back to selecting from all available clips.
    ///         This ensures shoutouts always have a clip to play when possible.
    ///     </para>
    /// </remarks>
    Task<ClipData?> SelectRandomClipAsync(string broadcasterName, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Executes a complete shoutout: sends Twitch native shoutout command and plays a random clip.
    /// </summary>
    /// <param name="sourceBroadcasterId">The Twitch user ID of the broadcaster giving the shoutout</param>
    /// <param name="targetUsername">The username of the broadcaster receiving the shoutout</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>
    ///     A task containing true if the shoutout was executed successfully (both Twitch shoutout sent
    ///     and clip played), or false if any step failed.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         <strong>Execution steps:</strong><br />
    ///         1. Look up target broadcaster's Twitch user ID<br />
    ///         2. Send Twitch native shoutout via <see cref="ITwitchHelixClient.SendShoutoutAsync" /><br />
    ///         3. Select a random clip via <see cref="SelectRandomClipAsync" /><br />
    ///         4. Enqueue the clip in the playback engine (separate from regular watch queue)
    ///     </para>
    ///     <para>
    ///         Failures are logged but do not throw exceptions. Partial success is possible (e.g., Twitch
    ///         shoutout succeeds but clip playback fails). The return value indicates overall success.
    ///     </para>
    /// </remarks>
    Task<bool> ExecuteShoutoutAsync(string sourceBroadcasterId, string targetUsername,
        CancellationToken cancellationToken = default);
}