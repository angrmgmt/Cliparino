using Cliparino.Core.Models;

namespace Cliparino.Core.Services;

public interface IClipSearchService {
    Task<ClipData?> SearchClipAsync(
        string broadcasterName, string searchTerms, CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<ClipData>> GetMatchingClipsAsync(
        string broadcasterName, string searchTerms, int maxResults = 10, CancellationToken cancellationToken = default
    );
}