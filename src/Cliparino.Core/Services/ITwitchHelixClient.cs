using Cliparino.Core.Models;

namespace Cliparino.Core.Services;

/// <summary>
///     Defines the contract for interacting with the Twitch Helix API.
/// </summary>
/// <remarks>
///     <para>
///         This interface is implemented by <see cref="TwitchHelixClient" /> and is used throughout
///         the application for all Twitch API operations (clips, user lookups, channel info, chat messages, shoutouts).
///     </para>
///     <para>
///         Key responsibilities:
///         - Fetch clip metadata by ID or URL
///         - Search clips by broadcaster with optional time range filters
///         - Look up broadcaster IDs and channel information
///         - Send chat messages and Twitch native shoutouts
///         - Handle OAuth authentication via <see cref="ITwitchAuthStore" />
///         - Implement retry logic with exponential backoff for transient failures
///     </para>
///     <para>
///         Authentication: All methods require a valid OAuth token obtained via <see cref="ITwitchOAuthService" />.
///         The client automatically includes the Bearer token from <see cref="ITwitchAuthStore" /> in all requests.
///     </para>
///     <para>
///         Rate Limiting: The Twitch Helix API enforces rate limits (800 requests per minute for most endpoints).
///         This implementation includes automatic retry logic for 429 Too Many Requests responses.
///     </para>
///     <para>
///         Thread-safety: All methods are async and stateless (except for the injected HttpClient). Safe to
///         call concurrently from multiple threads.
///     </para>
/// </remarks>
public interface ITwitchHelixClient {
    /// <summary>
    ///     Fetches clip metadata by its unique Twitch clip ID (slug).
    /// </summary>
    /// <param name="clipId">The Twitch clip ID (e.g., "FunnyClipSlug123")</param>
    /// <returns>
    ///     A task containing the <see cref="ClipData" /> if the clip exists, or null if the clip
    ///     was not found or an error occurred.
    /// </returns>
    /// <remarks>
    ///     Uses the <c>GET https://api.twitch.tv/helix/clips?id={clipId}</c> endpoint.
    /// </remarks>
    Task<ClipData?> GetClipByIdAsync(string clipId);

    /// <summary>
    ///     Fetches clip metadata by parsing and extracting the clip ID from a Twitch clip URL.
    /// </summary>
    /// <param name="clipUrl">
    ///     A Twitch clip URL (e.g., "https://clips.twitch.tv/FunnyClipSlug123" or
    ///     "https://www.twitch.tv/broadcaster/clip/FunnyClipSlug123")
    /// </param>
    /// <returns>
    ///     A task containing the <see cref="ClipData" /> if the clip exists, or null if the clip
    ///     was not found, the URL format was invalid, or an error occurred.
    /// </returns>
    /// <remarks>
    ///     This method extracts the clip ID from the URL and calls <see cref="GetClipByIdAsync" />.
    /// </remarks>
    Task<ClipData?> GetClipByUrlAsync(string clipUrl);

    /// <summary>
    ///     Fetches multiple clips from a broadcaster's channel with optional filtering.
    /// </summary>
    /// <param name="broadcasterId">The Twitch user ID of the broadcaster</param>
    /// <param name="count">The maximum number of clips to return (default: 20, max: 100)</param>
    /// <param name="startedAt">Optional: Only return clips created after this UTC timestamp</param>
    /// <param name="endedAt">Optional: Only return clips created before this UTC timestamp</param>
    /// <returns>
    ///     A task containing a read-only list of <see cref="ClipData" /> ordered by creation date (newest first).
    ///     Returns an empty list if no clips match the criteria or an error occurred.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         Uses the <c>GET https://api.twitch.tv/helix/clips?broadcaster_id={broadcasterId}</c> endpoint.
    ///     </para>
    ///     <para>
    ///         This method is used by <see cref="IClipSearchService" /> to fetch clips for fuzzy search matching
    ///         and by <see cref="IShoutoutService" /> to find random clips for shoutouts.
    ///     </para>
    /// </remarks>
    Task<IReadOnlyList<ClipData>> GetClipsByBroadcasterAsync(string broadcasterId, int count = 20,
        DateTimeOffset? startedAt = null, DateTimeOffset? endedAt = null);

    /// <summary>
    ///     Looks up a broadcaster's Twitch user ID by their username.
    /// </summary>
    /// <param name="broadcasterName">The broadcaster's username (case-insensitive)</param>
    /// <returns>
    ///     A task containing the broadcaster's Twitch user ID as a string, or null if the user
    ///     was not found or an error occurred.
    /// </returns>
    /// <remarks>
    ///     Uses the <c>GET https://api.twitch.tv/helix/users?login={broadcasterName}</c> endpoint.
    ///     Results are case-insensitive; "shroud", "Shroud", and "SHROUD" all return the same user ID.
    /// </remarks>
    Task<string?> GetBroadcasterIdByNameAsync(string broadcasterName);

    /// <summary>
    ///     Gets the Twitch user ID of the currently authenticated user.
    /// </summary>
    /// <returns>
    ///     A task containing the authenticated user's Twitch user ID as a string, or null if the
    ///     user could not be determined (e.g., invalid token, API error).
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         Uses the <c>GET https://api.twitch.tv/helix/users</c> endpoint (no parameters).
    ///     </para>
    ///     <para>
    ///         This method is used to determine the broadcaster's own ID for operations that require it,
    ///         such as sending shoutouts or accessing channel-specific data.
    ///     </para>
    /// </remarks>
    Task<string?> GetAuthenticatedUserIdAsync();

    /// <summary>
    ///     Fetches channel information including the current game/category and broadcaster display name.
    /// </summary>
    /// <param name="broadcasterId">The Twitch user ID of the broadcaster</param>
    /// <returns>
    ///     A task containing a tuple with (GameName, BroadcasterDisplayName), or (null, null) if the
    ///     channel was not found or an error occurred.
    /// </returns>
    /// <remarks>
    ///     Uses the <c>GET https://api.twitch.tv/helix/channels?broadcaster_id={broadcasterId}</c> endpoint.
    /// </remarks>
    Task<(string? GameName, string? BroadcasterDisplayName)> GetChannelInfoAsync(string broadcasterId);

    /// <summary>
    ///     Sends a chat message to a broadcaster's channel as the authenticated user.
    /// </summary>
    /// <param name="broadcasterId">The Twitch user ID of the channel to send the message to</param>
    /// <param name="message">The message text to send (max 500 characters)</param>
    /// <returns>
    ///     A task containing true if the message was sent successfully, or false if an error occurred.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         Uses the <c>POST https://api.twitch.tv/helix/chat/messages</c> endpoint.
    ///     </para>
    ///     <para>
    ///         The authenticated user must have permission to send messages in the target channel
    ///         (not banned, timeout, etc.). The message must comply with Twitch chat rules and
    ///         rate limits.
    ///     </para>
    /// </remarks>
    Task<bool> SendChatMessageAsync(string broadcasterId, string message);

    /// <summary>
    ///     Sends a Twitch native shoutout command from one broadcaster to another.
    /// </summary>
    /// <param name="fromBroadcasterId">The Twitch user ID of the broadcaster giving the shoutout</param>
    /// <param name="toBroadcasterId">The Twitch user ID of the broadcaster receiving the shoutout</param>
    /// <returns>
    ///     A task containing true if the shoutout was sent successfully, or false if an error occurred.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         Uses the <c>POST https://api.twitch.tv/helix/chat/shoutouts</c> endpoint.
    ///     </para>
    ///     <para>
    ///         The authenticated user (fromBroadcaster) must have moderator privileges in their own channel.
    ///         Twitch enforces rate limits on shoutouts (2 minutes between shoutouts to the same user,
    ///         60 seconds between different users).
    ///     </para>
    /// </remarks>
    Task<bool> SendShoutoutAsync(string fromBroadcasterId, string toBroadcasterId);
}