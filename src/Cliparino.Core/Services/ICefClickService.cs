namespace Cliparino.Core.Services;

/// <summary>
///     Simulates a trusted mouse click in OBS's CEF browser source via the Chrome DevTools Protocol,
///     establishing sticky user activation so Twitch's embed player does not auto-mute on playback.
/// </summary>
public interface ICefClickService {
    /// <summary>
    ///     Dispatches a synthetic left-click to the Cliparino player page via CDP.
    ///     Fails silently if the remote debugging port is unavailable (e.g. OBS not yet restarted).
    /// </summary>
    Task SimulateClickAsync(CancellationToken cancellationToken = default);
}