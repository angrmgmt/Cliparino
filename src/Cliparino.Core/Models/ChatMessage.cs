namespace Cliparino.Core.Models;

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