using Cliparino.Core.Models;

namespace Cliparino.Core.Services;

public interface IPlaybackEngine {
    PlaybackState CurrentState { get; }

    ClipData? CurrentClip { get; }
    Task PlayClipAsync(ClipData clip, CancellationToken cancellationToken = default);

    Task ReplayAsync(CancellationToken cancellationToken = default);

    Task StopPlaybackAsync(CancellationToken cancellationToken = default);
}