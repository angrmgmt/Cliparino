using Cliparino.Core.Models;

namespace Cliparino.Core.Services;

/// <summary>
///     Searches Twitch clips using word-based fuzzy matching to find clips by title.
/// </summary>
/// <remarks>
///     <para>
///         This class implements <see cref="IClipSearchService" /> using a word-tokenization similarity
///         algorithm. The search algorithm splits both search terms and clip titles into words, then
///         calculates similarity scores based on word matches, handling typos and word order variations.
///     </para>
///     <para>
///         Dependencies:
///         - <see cref="ITwitchHelixClient" /> - Fetch clips from Twitch
///         - <see cref="IConfiguration" /> - Search configuration settings
///         - <see cref="ILogger{TCategoryName}" /> - Structured logging
///     </para>
///     <para>
///         Lifecycle: Registered as a singleton.
///     </para>
/// </remarks>
public class ClipSearchService : IClipSearchService {
    private readonly IConfiguration _configuration;
    private readonly ITwitchHelixClient _helixClient;
    private readonly ILogger<ClipSearchService> _logger;

    public ClipSearchService(
        ITwitchHelixClient helixClient,
        IConfiguration configuration,
        ILogger<ClipSearchService> logger
    ) {
        _helixClient = helixClient;
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ClipData?> SearchClipAsync(
        string broadcasterName, string searchTerms, CancellationToken cancellationToken = default
    ) {
        var matchingClips = await GetMatchingClipsAsync(broadcasterName, searchTerms, 10, cancellationToken);

        if (!matchingClips.Any()) {
            _logger.LogInformation(
                "No clips found matching search terms: '{SearchTerms}' for @{BroadcasterName}",
                searchTerms, broadcasterName
            );

            return null;
        }

        return matchingClips.First();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClipData>> GetMatchingClipsAsync(
        string broadcasterName,
        string searchTerms,
        int maxResults = 10,
        CancellationToken cancellationToken = default
    ) {
        if (string.IsNullOrWhiteSpace(broadcasterName) || string.IsNullOrWhiteSpace(searchTerms)) {
            _logger.LogWarning("GetMatchingClipsAsync called with empty parameters");

            return Array.Empty<ClipData>();
        }

        var broadcasterId = await _helixClient.GetBroadcasterIdByNameAsync(broadcasterName);

        if (string.IsNullOrEmpty(broadcasterId)) {
            _logger.LogWarning("Could not find broadcaster ID for: {BroadcasterName}", broadcasterName);

            return Array.Empty<ClipData>();
        }

        var searchWindow = _configuration.GetValue("ClipSearch:SearchWindowDays", 90);
        var startDate = DateTimeOffset.UtcNow.AddDays(-searchWindow);
        var endDate = DateTimeOffset.UtcNow;

        var clips = await _helixClient.GetClipsByBroadcasterAsync(
            broadcasterId,
            100,
            startDate,
            endDate
        );

        if (!clips.Any()) {
            _logger.LogDebug(
                "No clips found for broadcaster {BroadcasterName} in the last {SearchWindow} days",
                broadcasterName, searchWindow
            );

            return Array.Empty<ClipData>();
        }

        _logger.LogDebug(
            "Retrieved {Count} clips for broadcaster {BroadcasterName}, searching for: '{SearchTerms}'",
            clips.Count, broadcasterName, searchTerms
        );

        var scoredClips = clips
            .Select(clip => new {
                    Clip = clip,
                    Score = CalculateFuzzyScore(clip.Title, searchTerms)
                }
            )
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(maxResults)
            .Select(x => x.Clip)
            .ToList();

        _logger.LogInformation(
            "Found {Count} matching clips for search: '{SearchTerms}' on @{BroadcasterName}",
            scoredClips.Count, searchTerms, broadcasterName
        );

        return scoredClips;
    }

    private double CalculateFuzzyScore(string clipTitle, string searchTerms) {
        if (string.IsNullOrWhiteSpace(clipTitle) || string.IsNullOrWhiteSpace(searchTerms))
            return 0;

        var titleLower = clipTitle.ToLowerInvariant();
        var searchLower = searchTerms.ToLowerInvariant();

        if (titleLower.Contains(searchLower)) return 100.0;

        var searchWords = searchLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (searchWords.Length == 0)
            return 0;

        var matchedWords = searchWords.Count(word => titleLower.Contains(word));
        var wordMatchScore = (double)matchedWords / searchWords.Length * 80.0;

        if (wordMatchScore > 0)
            return wordMatchScore;

        var levenshteinSimilarity = CalculateLevenshteinSimilarity(titleLower, searchLower);
        var minThreshold = _configuration.GetValue("ClipSearch:FuzzyMatchThreshold", 0.4);

        if (levenshteinSimilarity >= minThreshold) return levenshteinSimilarity * 60.0;

        return 0;
    }

    private double CalculateLevenshteinSimilarity(string source, string target) {
        if (string.IsNullOrEmpty(source))
            return string.IsNullOrEmpty(target) ? 1.0 : 0.0;

        if (string.IsNullOrEmpty(target))
            return 0.0;

        var distance = CalculateLevenshteinDistance(source, target);
        var maxLength = Math.Max(source.Length, target.Length);

        return 1.0 - (double)distance / maxLength;
    }

    private int CalculateLevenshteinDistance(string source, string target) {
        var n = source.Length;
        var m = target.Length;
        var d = new int[n + 1, m + 1];

        if (n == 0)
            return m;

        if (m == 0)
            return n;

        for (var i = 0; i <= n; i++)
            d[i, 0] = i;

        for (var j = 0; j <= m; j++)
            d[0, j] = j;

        for (var i = 1; i <= n; i++)
        for (var j = 1; j <= m; j++) {
            var cost = target[j - 1] == source[i - 1] ? 0 : 1;

            d[i, j] = Math.Min(
                Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                d[i - 1, j - 1] + cost
            );
        }

        return d[n, m];
    }
}