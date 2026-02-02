using Cliparino.Core.Models;

namespace Cliparino.Core.Services;

public interface IShoutoutService {
    Task<ClipData?> SelectRandomClipAsync(string broadcasterName, CancellationToken cancellationToken = default);

    Task<bool> ExecuteShoutoutAsync(
        string sourceBroadcasterId, string targetUsername, CancellationToken cancellationToken = default
    );
}