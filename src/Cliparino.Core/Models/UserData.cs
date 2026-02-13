using System.Text.Json.Serialization;

namespace Cliparino.Core.Models;

/// <summary>
///     Represents a Twitch user with identity and display information.
/// </summary>
/// <remarks>
///     <para>
///         This immutable record encapsulates Twitch user information including their ID,
///         login name (username), and display name (presentation name). This is the canonical
///         representation for any Twitch user entity where names need to be displayed.
///     </para>
///     <para>
///         <strong>Field Usage:</strong><br />
///         - <see cref="Id" />: Unique Twitch user ID for API lookups and identity<br />
///         - <see cref="Login" />: Username (lowercase) for API calls and @mentions<br />
///         - <see cref="DisplayName" />: Presentation name for UI, chat, and logs (preferred for display)
///     </para>
///     <para>
///         Use cases: Clip authors, clip broadcasters, raiders, shoutout targets, chat participants.
///     </para>
///     <para>
///         Thread-safe due to immutability. Safe to pass between services and UI components.
///     </para>
/// </remarks>
/// <param name="Id">The unique Twitch user ID</param>
/// <param name="Login">The user's login name (username, typically lowercase)</param>
/// <param name="DisplayName">The user's display name (mixed case, for presentation)</param>
public record UserData(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("login")] string Login,
    [property: JsonPropertyName("display_name")]
    string DisplayName
);