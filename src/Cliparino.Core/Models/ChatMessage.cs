namespace Cliparino.Core.Models;

/// <summary>
///     Represents a chat message received from Twitch via EventSub WebSocket or IRC.
/// </summary>
/// <remarks>
///     <para>
///         This immutable record encapsulates all information about a Twitch chat message needed
///         for command parsing, approval workflows, and permission checks. Instances are created
///         from raw EventSub or IRC events and passed to the command router for processing.
///     </para>
///     <para>
///         The boolean flags (IsModerator, IsVip, IsBroadcaster, IsSubscriber) are used by
///         <see cref="Services.IApprovalService" /> to determine if the user has sufficient
///         permissions for approval actions and by <see cref="Services.ICommandRouter" /> for
///         general command execution permissions.
///     </para>
///     <para>
///         Thread-safe due to immutability. Safe to pass between EventSub/IRC sources,
///         event coordinator, and command router.
///     </para>
/// </remarks>
/// <param name="Username">The lowercase username of the message sender</param>
/// <param name="DisplayName">The display name of the message sender (may have mixed case)</param>
/// <param name="Channel">The channel name where the message was sent</param>
/// <param name="UserId">The unique Twitch user ID of the message sender</param>
/// <param name="ChannelId">The unique Twitch channel ID where the message was sent</param>
/// <param name="Message">The full text content of the chat message</param>
/// <param name="IsModerator">True if the sender has moderator privileges in the channel</param>
/// <param name="IsVip">True if the sender has VIP status in the channel</param>
/// <param name="IsBroadcaster">True if the sender is the channel owner</param>
/// <param name="IsSubscriber">True if the sender is subscribed to the channel</param>
public record ChatMessage(
    string Username,
    string DisplayName,
    string Channel,
    string UserId,
    string ChannelId,
    string Message,
    bool IsModerator,
    bool IsVip,
    bool IsBroadcaster,
    bool IsSubscriber
);