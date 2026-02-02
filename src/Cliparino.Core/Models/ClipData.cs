namespace Cliparino.Core.Models;

public record ClipData(
    string Id,
    string Url,
    string Title,
    string CreatorName,
    string BroadcasterName,
    string BroadcasterId,
    string GameName,
    int DurationSeconds,
    DateTime CreatedAt,
    int ViewCount,
    bool IsFeatured
);