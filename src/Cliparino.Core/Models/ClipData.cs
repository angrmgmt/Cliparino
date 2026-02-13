namespace Cliparino.Core.Models;

/// <summary>
///     Represents a Twitch clip with all associated metadata retrieved from the Twitch Helix API.
/// </summary>
/// <remarks>
///     <para>
///         This immutable record encapsulates all information about a Twitch clip needed for playback
///         and display in the Cliparino application. Instances are created from Twitch Helix API responses
///         and passed through the playback pipeline.
///     </para>
///     <para>
///         The record is immutable to ensure thread-safety when clips are queued and processed
///         by multiple components (CommandRouter, PlaybackEngine, HTTP controllers).
///     </para>
///     <para>
///         Used by: <see cref="Services.IClipQueue" />, <see cref="Services.IPlaybackEngine" />,
///         <see cref="Services.ITwitchHelixClient" />, <see cref="Services.IClipSearchService" />.
///     </para>
/// </remarks>
/// <param name="Id">The unique Twitch clip identifier (slug)</param>
/// <param name="Url">The full URL to the clip on Twitch.tv</param>
/// <param name="Title">The clip title as set by the clip creator</param>
/// <param name="Creator">The user who created the clip</param>
/// <param name="Broadcaster">The broadcaster whose channel the clip is from</param>
/// <param name="GameName">The name of the game/category being played when the clip was created</param>
/// <param name="DurationSeconds">The duration of the clip in seconds (typically 5-60 seconds)</param>
/// <param name="CreatedAt">The UTC timestamp when the clip was created</param>
/// <param name="ViewCount">The number of views the clip has received on Twitch</param>
/// <param name="IsFeatured">Indicates whether the broadcaster has marked this clip as featured</param>
public record ClipData(
    string Id,
    string Url,
    string Title,
    UserData Creator,
    UserData Broadcaster,
    string GameName,
    int DurationSeconds,
    DateTime CreatedAt,
    int ViewCount,
    bool IsFeatured
);