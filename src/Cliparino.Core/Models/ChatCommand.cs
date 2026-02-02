namespace Cliparino.Core.Models;

/// <summary>
///     Abstract base record for all chat commands parsed from Twitch chat messages.
/// </summary>
/// <remarks>
///     <para>
///         This record hierarchy represents the various commands that can be executed by Cliparino
///         in response to chat messages. Each derived command type encapsulates the parameters
///         specific to that command, while the base record holds the source chat message for
///         permission checks and logging.
///     </para>
///     <para>
///         Commands are created by <see cref="Services.ICommandRouter.ParseCommand" /> and executed
///         by <see cref="Services.ICommandRouter.ExecuteCommandAsync" />.
///     </para>
///     <para>
///         Thread-safe due to immutability. Commands can be safely passed between parsing and
///         execution stages without synchronization.
///     </para>
/// </remarks>
/// <param name="Source">The original chat message that triggered this command</param>
public abstract record ChatCommand(ChatMessage Source);

/// <summary>
///     Command to play a specific Twitch clip by its identifier or URL.
/// </summary>
/// <remarks>
///     Triggered by: <c>!watch &lt;clip-url&gt;</c> or <c>!watch &lt;clip-id&gt;</c>
/// </remarks>
/// <param name="Source">The original chat message that triggered this command</param>
/// <param name="ClipIdentifier">The clip ID (slug) or full Twitch clip URL to play</param>
public record WatchClipCommand(ChatMessage Source, string ClipIdentifier) : ChatCommand(Source);

/// <summary>
///     Command to search for and play a clip from a broadcaster's channel using fuzzy search.
/// </summary>
/// <remarks>
///     <para>
///         Triggered by: <c>!watch @broadcaster search terms</c>
///     </para>
///     <para>
///         This command requires moderator approval by default. The approval workflow is managed
///         by <see cref="Services.IApprovalService" />.
///     </para>
/// </remarks>
/// <param name="Source">The original chat message that triggered this command</param>
/// <param name="BroadcasterName">The broadcaster's username to search clips from (without the @ symbol)</param>
/// <param name="SearchTerms">The search terms to match against clip titles using fuzzy search</param>
public record WatchSearchCommand(ChatMessage Source, string BroadcasterName, string SearchTerms) : ChatCommand(Source);

/// <summary>
///     Command to stop the currently playing clip and advance to the next clip in queue.
/// </summary>
/// <remarks>
///     Triggered by: <c>!stop</c>
/// </remarks>
/// <param name="Source">The original chat message that triggered this command</param>
public record StopCommand(ChatMessage Source) : ChatCommand(Source);

/// <summary>
///     Command to replay the most recently played clip.
/// </summary>
/// <remarks>
///     <para>
///         Triggered by: <c>!replay</c>
///     </para>
///     <para>
///         The last played clip is tracked by <see cref="Services.IClipQueue.LastPlayed" />.
///         If no clip has been played yet, this command has no effect.
///     </para>
/// </remarks>
/// <param name="Source">The original chat message that triggered this command</param>
public record ReplayCommand(ChatMessage Source) : ChatCommand(Source);

/// <summary>
///     Command to perform a shoutout with a random clip from the target user's channel.
/// </summary>
/// <remarks>
///     <para>
///         Triggered by: <c>!so @username</c> or <c>!shoutout @username</c>
///     </para>
///     <para>
///         Also triggered automatically by raid events when configured. Sends Twitch's native
///         <c>/shoutout</c> command and plays a random clip with optional filters (featured only,
///         max duration, max age).
///     </para>
///     <para>
///         Shoutout clips use a separate queue from watch commands and don't interfere with
///         the regular clip playback queue.
///     </para>
/// </remarks>
/// <param name="Source">The original chat message that triggered this command</param>
/// <param name="TargetUsername">The username to shoutout (without the @ symbol)</param>
public record ShoutoutCommand(ChatMessage Source, string TargetUsername) : ChatCommand(Source);