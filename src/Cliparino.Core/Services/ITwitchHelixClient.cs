using Cliparino.Core.Models;

namespace Cliparino.Core.Services;

public interface ITwitchHelixClient {
    Task<ClipData?> GetClipByIdAsync(string clipId);
    Task<ClipData?> GetClipByUrlAsync(string clipUrl);

    Task<IReadOnlyList<ClipData>> GetClipsByBroadcasterAsync(
        string broadcasterId, int count = 20, DateTimeOffset? startedAt = null, DateTimeOffset? endedAt = null
    );

    Task<string?> GetBroadcasterIdByNameAsync(string broadcasterName);
    Task<string?> GetAuthenticatedUserIdAsync();
    Task<(string? GameName, string? BroadcasterDisplayName)> GetChannelInfoAsync(string broadcasterId);
    Task<bool> SendChatMessageAsync(string broadcasterId, string message);
    Task<bool> SendShoutoutAsync(string fromBroadcasterId, string toBroadcasterId);
}