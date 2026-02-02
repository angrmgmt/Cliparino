using Cliparino.Core.Models;

namespace Cliparino.Core.Services;

/// <summary>
///     Defines the contract for searching Twitch clips using fuzzy text matching.
/// </summary>
/// <remarks>
///     <para>
///         This interface is implemented by <see cref="ClipSearchService" /> and is used by
///         <see cref="CommandRouter" /> to find clips when users execute <c>!watch @broadcaster search terms</c> commands.
///     </para>
///     <para>
///         Key responsibilities:
///         - Search broadcaster's clips using word-based similarity matching
///         - Cache clip data to reduce Twitch API calls
///         - Return best match or ranked list of matches
///     </para>
///     <para>
///         Search algorithm: Uses word-based fuzzy matching that tokenizes both the search terms and
///         clip titles, then calculates similarity scores. This approach is more forgiving than exact
///         string matching and handles typos, word order variations, and partial matches.
///     </para>
///     <para>
///         Thread-safety: All methods are async and thread-safe. Caching is implemented with
///         appropriate synchronization.
///     </para>
/// </remarks>
public interface IClipSearchService {
    /// <summary>
    ///     Searches for the best matching clip from a broadcaster's channel.
    /// </summary>
    /// <param name="broadcasterName">The broadcaster's username to search clips from</param>
    /// <param name="searchTerms">The search terms to match against clip titles</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>
    ///     A task containing the best matching <see cref="ClipData" />, or null if no clips match
    ///     or the broadcaster was not found.
    /// </returns>
    /// <remarks>
    ///     This method fetches clips from the broadcaster's channel (via <see cref="ITwitchHelixClient" />),
    ///     calculates similarity scores for each clip title, and returns the highest-scoring match.
    ///     Results are cached to reduce API calls for repeated searches.
    /// </remarks>
    Task<ClipData?> SearchClipAsync(
        string broadcasterName, string searchTerms, CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Searches for multiple matching clips from a broadcaster's channel, ranked by relevance.
    /// </summary>
    /// <param name="broadcasterName">The broadcaster's username to search clips from</param>
    /// <param name="searchTerms">The search terms to match against clip titles</param>
    /// <param name="maxResults">The maximum number of results to return (default: 10)</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>
    ///     A task containing a read-only list of <see cref="ClipData" /> ordered by relevance (best match first).
    ///     Returns an empty list if no clips match or the broadcaster was not found.
    /// </returns>
    /// <remarks>
    ///     This method is useful for presenting multiple options to the user or for diagnostic purposes.
    ///     The current implementation primarily uses <see cref="SearchClipAsync" /> for single-result workflows.
    /// </remarks>
    Task<IReadOnlyList<ClipData>> GetMatchingClipsAsync(
        string broadcasterName, string searchTerms, int maxResults = 10, CancellationToken cancellationToken = default
    );
}