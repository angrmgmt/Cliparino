/*  Cliparino is a clip player for Twitch.tv built to work with Streamer.bot.
    Copyright (C) 2024 Scott Mongrain - (angrmgmt@gmail.com)

    This library is free software; you can redistribute it and/or
    modify it under the terms of the GNU Lesser General Public
    License as published by the Free Software Foundation; either
    version 2.1 of the License, or (at your option) any later version.

    This library is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
    Lesser General Public License for more details.

    You should have received a copy of the GNU Lesser General Public
    License along with this library; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA 02110-1301
    USA
*/

#region

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Streamer.bot.Common.Events;
using Streamer.bot.Plugin.Interface;
using Streamer.bot.Plugin.Interface.Enums;
using Streamer.bot.Plugin.Interface.Model;
using Twitch.Common.Models.Api;

#endregion

/// <summary>
///     Manages Twitch clips, including fetching, caching, and updating clip data.
/// </summary>
public class ClipManager {
    private static readonly Dictionary<string, CachedClipData> ClipCache = new Dictionary<string, CachedClipData>();
    private readonly object _cacheLock = new object();
    private readonly IInlineInvokeProxy _cph;
    private readonly CPHLogger _logger;
    private readonly TwitchApiManager _twitchApiManager;
    private string _lastClipUrl;

    public ClipManager(IInlineInvokeProxy cph, CPHLogger logger, TwitchApiManager twitchApiManager) {
        _cph = cph ?? throw new ArgumentNullException(nameof(cph));
        _logger = logger;
        _twitchApiManager = twitchApiManager;
    }

    /// <summary>
    ///     Retrieves the URL of the last recorded clip.
    /// </summary>
    /// <returns>
    ///     A string URL of the last clip if available; otherwise, retrieves the global variable
    ///     "last_clip_url".
    /// </returns>
    public string GetLastClipUrl() {
        return _lastClipUrl ?? _cph.GetGlobalVar<string>("last_clip_url");
    }

    /// <summary>
    ///     Sets the last clip URL to the specified value.
    /// </summary>
    /// <param name="url">
    ///     The URL of the last clip to set. If the value is null, empty, or whitespace, no changes will be
    ///     made.
    /// </param>
    public void SetLastClipUrl(string url) {
        if (string.IsNullOrWhiteSpace(url)) return;

        _lastClipUrl = url;
        _cph.SetGlobalVar("last_clip_url", url);
    }

    /// <summary>
    ///     Retrieves a random clip for a specified user based on given clip settings.
    /// </summary>
    /// <param name="userName">
    ///     The username of the Twitch user to retrieve a clip for.
    /// </param>
    /// <param name="clipSettings">
    ///     Settings to filter the clips, including featured-only preference, maximum clip length, and clip
    ///     age in days.
    /// </param>
    /// <returns>
    ///     A task that represents the asynchronous operation. The task result contains the random
    ///     <see cref="ClipData" /> if found, or null if no suitable clip is available.
    /// </returns>
    public async Task<ClipData> GetRandomClipAsync(string userName, ClipSettings clipSettings) {
        clipSettings.Deconstruct(out var featuredOnly, out var maxClipSeconds, out var clipAgeDays);

        _logger.Log(LogLevel.Debug,
                    $"Getting random clip for userName: {userName}, featuredOnly: {featuredOnly}, maxSeconds: {maxClipSeconds}, ageDays: {clipAgeDays}");

        try {
            var twitchUser = _cph.TwitchGetExtendedUserInfoByLogin(userName);

            if (twitchUser == null) {
                _logger.Log(LogLevel.Warn, $"Twitch user not found for userName: {userName}");

                return null;
            }

            var userId = twitchUser.UserId;
            var validPeriods = new[] { 1, 7, 30, 365, 36500 };

            return await Task.Run(() => {
                                      foreach (var period in validPeriods.Where(p => p >= clipAgeDays)) {
                                          var clips = RetrieveClips(userId, period).ToList();

                                          if (!clips.Any()) continue;

                                          var clip = GetMatchingClip(clips, featuredOnly, maxClipSeconds, userId);

                                          if (clip != null) return clip;
                                      }

                                      _logger.Log(LogLevel.Warn,
                                                  $"No clips found for userName: {userName} after exhausting all periods and filter combinations.");

                                      return null;
                                  });
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "An error occurred while getting random clip.", ex);

            return null;
        }
    }

    /// <summary>
    ///     Retrieves a collection of clips for a specific user within a specified time period.
    /// </summary>
    /// <param name="userId">
    ///     The unique identifier of the user whose clips are being retrieved.
    /// </param>
    /// <param name="clipAgeDays">
    ///     The maximum age of the clips, in days, that should be retrieved.
    /// </param>
    /// <returns>
    ///     An enumerable collection of <see cref="ClipData" /> representing the user's clips within the
    ///     specified time frame.
    /// </returns>
    private IEnumerable<ClipData> RetrieveClips(string userId, int clipAgeDays) {
        _logger.Log(LogLevel.Debug, $"RetrieveClips called with userId: {userId}, clipAgeDays: {clipAgeDays}");

        try {
            var endDate = DateTime.UtcNow;
            var startDate = endDate.AddDays(-clipAgeDays);

            return _cph.GetClipsForUserById(userId, startDate, endDate);
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "An error occurred while retrieving clips.", ex);

            return Array.Empty<ClipData>();
        }
    }

    /// <summary>
    ///     Selects a matching clip from a collection of clips based on specified filter criteria.
    /// </summary>
    /// <param name="clips">
    ///     A collection of clips to filter.
    /// </param>
    /// <param name="featuredOnly">
    ///     A flag indicating if only featured clips should be included in the selection.
    /// </param>
    /// <param name="maxSeconds">
    ///     The maximum duration (in seconds) of the clip to be considered.
    /// </param>
    /// <param name="userId">
    ///     The user ID for which the clip is being retrieved.
    /// </param>
    /// <returns>
    ///     A <see cref="ClipData" /> object that matches the filter criteria, or null if no matches are
    ///     found.
    /// </returns>
    private ClipData GetMatchingClip(IEnumerable<ClipData> clips, bool featuredOnly, int maxSeconds, string userId) {
        clips = clips.ToList();

        var matchingClips = FilterClips(clips, featuredOnly, maxSeconds);

        if (matchingClips.Any()) {
            var selectedClip = SelectRandomClip(matchingClips);

            _logger.Log(LogLevel.Debug, $"Selected clip: {selectedClip.Url}");

            return selectedClip;
        }

        _logger.Log(LogLevel.Debug, $"No matching clips found with the current filter (featuredOnly: {featuredOnly}).");

        if (!featuredOnly) return null;

        matchingClips = FilterClips(clips, false, maxSeconds);

        if (matchingClips.Any()) {
            var selectedClip = SelectRandomClip(matchingClips);

            _logger.Log(LogLevel.Debug, $"Selected clip without featuredOnly: {selectedClip.Url}");

            return selectedClip;
        }

        _logger.Log(LogLevel.Debug, $"No matching clips found without featuredOnly for userId: {userId}");

        return null;
    }

    /// <summary>
    ///     Filters a collection of clips based on specified criteria.
    /// </summary>
    /// <param name="clips">
    ///     A collection of clips to filter.
    /// </param>
    /// <param name="featuredOnly">
    ///     A boolean indicating whether to include only featured clips.
    /// </param>
    /// <param name="maxSeconds">
    ///     The maximum duration of clips to include, in seconds.
    /// </param>
    /// <returns>
    ///     A list of clips that match the filtering criteria.
    /// </returns>
    private static List<ClipData> FilterClips(IEnumerable<ClipData> clips, bool featuredOnly, int maxSeconds) {
        return clips.Where(c => (!featuredOnly || c.IsFeatured) && c.Duration <= maxSeconds).ToList();
    }

    /// <summary>
    ///     Selects a random clip from a provided list of clips.
    /// </summary>
    /// <param name="clips">
    ///     A list of <see cref="ClipData" /> objects to select from.
    /// </param>
    /// <returns>
    ///     A randomly selected <see cref="ClipData" /> object from the provided list.
    /// </returns>
    private static ClipData SelectRandomClip(List<ClipData> clips) {
        return clips[new Random().Next(clips.Count)];
    }

    /// <summary>
    ///     Retrieves data for a specific Twitch clip based on the provided clip URL asynchronously.
    /// </summary>
    /// <param name="clipUrl">
    ///     The URL of the Twitch clip to retrieve information for.
    /// </param>
    /// <returns>
    ///     A task that represents the asynchronous operation. The task result contains the clip data if
    ///     successfully retrieved; otherwise, null.
    /// </returns>
    public async Task<ClipData> GetClipDataAsync(string clipUrl) {
        if (string.IsNullOrWhiteSpace(clipUrl)) {
            _logger.Log(LogLevel.Error, "Invalid clip URL provided.");

            return null;
        }

        var clipId = ExtractClipId(clipUrl);

        if (!string.IsNullOrWhiteSpace(clipId)) return await FetchClipDataAsync(clipId);

        _logger.Log(LogLevel.Error, "Could not extract clip ID from URL.");

        return null;
    }

    /// <summary>
    ///     Extracts the clip ID from the provided clip URL.
    /// </summary>
    /// <param name="clipUrl">
    ///     The full URL of the clip from which the clip ID needs to be extracted.
    /// </param>
    /// <returns>
    ///     The clip ID if successfully extracted; otherwise, null.
    /// </returns>
    private static string ExtractClipId(string clipUrl) {
        var segments = clipUrl.Split('/');

        return segments.Length > 0 ? segments.Last().Trim() : null;
    }

    /// <summary>
    ///     Fetches clip data using the given clip ID.
    /// </summary>
    /// <param name="clipId">
    ///     The unique identifier of the clip.
    /// </param>
    /// <returns>
    ///     An asynchronous task that resolves to a <see cref="ClipData" /> object if successful;
    ///     otherwise, null.
    /// </returns>
    /// <remarks>
    ///     This method utilizes <see cref="TwitchApiManager" /> to retrieve clip data. If the clip data is
    ///     unavailable or an error occurs during the fetch, it is logged and null is returned.
    /// </remarks>
    private async Task<ClipData> FetchClipDataAsync(string clipId) {
        try {
            var clipData = await _twitchApiManager.FetchClipById(clipId);

            if (clipData == null) {
                _logger.Log(LogLevel.Error, $"No clip data found for ID {clipId}.");

                return null;
            }

            _logger.Log(LogLevel.Info, $"Retrieved clip '{clipData.Title}' by {clipData.CreatorName}.");

            return clipData;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error retrieving clip data.", ex);

            return null;
        }
    }

    /// <summary>
    ///     Searches for clips associated with a specified broadcaster that match a given search term. If a
    ///     clip with a similarity score exceeding the threshold is found, returns the clip data.
    /// </summary>
    /// <param name="broadcasterId">
    ///     The ID of the broadcaster whose clips are to be searched.
    /// </param>
    /// <param name="searchTerm">
    ///     The term used to search for matching clips.
    /// </param>
    /// <returns>
    ///     A task that represents the asynchronous operation. The task result contains the matching clip
    ///     data if a suitable clip is found; otherwise, null.
    /// </returns>
    public async Task<ClipData> SearchClipsWithThresholdAsync(string broadcasterId, string searchTerm) {
        _logger.Log(LogLevel.Debug,
                    $"Searching clips for broadcaster ID: {broadcasterId} using search term: '{searchTerm}'");

        try {
            string cursor = null;

            do {
                var clipsResponse = await _twitchApiManager.FetchClipsAsync(broadcasterId, cursor);
                var clips = clipsResponse?.Data;

                if (clips == null || clips.Length == 0) {
                    _logger.Log(LogLevel.Warn, "No clips retrieved from Twitch API.");

                    return null;
                }

                var searchWords = PreprocessText(searchTerm);
                ClipData bestClip = null;
                double bestScore = 0;

                foreach (var clip in clips) {
                    var clipTitleWords = PreprocessText(clip.Title);
                    var similarityScore = ComputeWordOverlap(searchWords, clipTitleWords);

                    if (!(similarityScore > bestScore)) continue;

                    bestScore = similarityScore;
                    bestClip = clip;
                }

                const double similarityThreshold = 0.5;

                if (bestClip != null && bestScore >= similarityThreshold) {
                    _logger.Log(LogLevel.Debug, $"Best matching clip: {bestClip.Title} with score: {bestScore}");

                    return bestClip;
                }

                cursor = clipsResponse.Pagination.Cursor;

                if (!string.IsNullOrEmpty(cursor))
                    _logger.Log(LogLevel.Debug, $"Continuing search with cursor: {cursor}");
            } while (!string.IsNullOrWhiteSpace(cursor));

            _logger.Log(LogLevel.Warn, "No matching clip found.");

            return null;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error occurred while searching for clip.", ex);

            return null;
        }
    }

    /// <summary>
    ///     Processes the input text and returns a list of words in lowercase, splitting by spaces, tabs,
    ///     line breaks, and common punctuation characters.
    /// </summary>
    /// <param name="text">
    ///     The input string to be processed.
    /// </param>
    /// <returns>
    ///     A list of lowercase words extracted from the input string.
    /// </returns>
    private static List<string> PreprocessText(string text) {
        return text.ToLowerInvariant()
                   .Split(new[] { ' ', '\t', '\n', '\r', '.', ',', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                   .ToList();
    }

    /// <summary>
    ///     Computes the word overlap between two lists of words.
    /// </summary>
    /// <param name="searchWords">
    ///     The list of words representing the search query.
    /// </param>
    /// <param name="clipTitleWords">
    ///     The list of words representing a clip title.
    /// </param>
    /// <returns>
    ///     The fraction of words in <paramref name="searchWords" /> that match words in
    ///     <paramref name="clipTitleWords" />. Returns 0 if either list is null or empty.
    /// </returns>
    private static double ComputeWordOverlap(List<string> searchWords, List<string> clipTitleWords) {
        if (searchWords == null || clipTitleWords == null || searchWords.Count == 0 || clipTitleWords.Count == 0)
            return 0;

        var matchingWords = searchWords.Intersect(clipTitleWords).Count();

        return (double)matchingWords / searchWords.Count;
    }

    /// <summary>
    ///     Retrieves a clip from the cache by the specified search term.
    /// </summary>
    /// <param name="searchTerm">
    ///     The search term used to locate the cached clip.
    /// </param>
    /// <returns>
    ///     The clip data associated with the specified search term if it exists in the cache; otherwise,
    ///     null.
    /// </returns>
    public static ClipData GetFromCache(string searchTerm) {
        if (!ClipCache.TryGetValue(searchTerm, out var cachedData)) return null;

        cachedData.LastAccessed = DateTime.UtcNow;
        cachedData.SearchFrequency++;

        return cachedData.Clip;
    }

    /// <summary>
    ///     Adds a clip to the cache for the specified search term.
    /// </summary>
    /// <param name="searchTerm">
    ///     The search term associated with the clip being cached.
    /// </param>
    /// <param name="clipData">
    ///     The clip data to be added to the cache.
    /// </param>
    public void AddToCache(string searchTerm, ClipData clipData) {
        ClipCache[searchTerm] =
            new CachedClipData { Clip = clipData, SearchFrequency = 1, LastAccessed = DateTime.UtcNow };

        _logger.Log(LogLevel.Debug, $"Added '{clipData.Title}' to cache for search term: {searchTerm}");
    }

    /// <summary>
    ///     Cleans expired entries from the clip cache based on a predefined expiration time.
    /// </summary>
    /// <returns>
    ///     Returns true if the cache was cleaned successfully, otherwise false.
    /// </returns>
    public bool CleanCache() {
        try {
            lock (_cacheLock) {
                var expirationTime = TimeSpan.FromDays(CPHInline.GetArgument(_cph, "ClipCacheExpirationDays", 30));
                var now = DateTime.UtcNow;
                var toRemove = ClipCache.Where(entry => now - entry.Value.LastAccessed > expirationTime)
                                        .Select(entry => entry.Key)
                                        .ToList();

                foreach (var key in toRemove) {
                    _logger.Log(LogLevel.Debug, $"Removing expired cache item: {key}");
                    ClipCache.Remove(key);
                }

                return true;
            }
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error cleaning cache.", ex);

            return false;
        }
    }

    /// <summary>
    ///     Represents a response object containing clips and associated pagination data.
    /// </summary>
    public class ClipsResponse {
        /// <summary>
        ///     Represents a response containing clips and associated pagination information.
        /// </summary>
        public ClipsResponse(ClipData[] data = null, string cursor = "") {
            Data = data ?? Array.Empty<ClipData>();
            Pagination = new PaginationObject(cursor);
        }

        /// <summary>
        ///     Represents data associated with a clip.
        /// </summary>
        public ClipData[] Data { get; }

        /// <summary>
        ///     Represents pagination information for navigating through a collection of data.
        /// </summary>
        public PaginationObject Pagination { get; }
    }

    /// <summary>
    ///     Represents the pagination data for API responses.
    /// </summary>
    public class PaginationObject {
        /// <summary>
        ///     Represents the pagination details for retrieving a batch of data, including the cursor for
        ///     additional fetch requests.
        /// </summary>
        public PaginationObject(string cursor = "") {
            Cursor = cursor;
        }

        /// <summary>
        ///     Represents pagination information used to retrieve the next set of data in a paginated API
        ///     response.
        /// </summary>
        public string Cursor { get; }
    }

    /// <summary>
    ///     Represents the settings to be applied for clip selection and filtering.
    /// </summary>
    public class ClipSettings {
        /// <summary>
        ///     Represents a manager for handling Twitch clips, including fetching, caching, and searching for
        ///     clips.
        /// </summary>
        public ClipSettings(bool featuredOnly, int maxClipSeconds, int clipAgeDays) {
            FeaturedOnly = featuredOnly;
            MaxClipSeconds = maxClipSeconds;
            ClipAgeDays = clipAgeDays;
        }

        /// <summary>
        ///     Indicates whether only featured clips should be included in the clip search.
        /// </summary>
        public bool FeaturedOnly { get; }

        /// <summary>
        ///     Gets the maximum duration, in seconds, allowed for a clip.
        /// </summary>
        public int MaxClipSeconds { get; }

        /// <summary>
        ///     Gets the maximum age of clips to fetch, in days.
        /// </summary>
        /// <remarks>
        ///     This property defines the upper limit on how old a clip can be, in days, to be included in the
        ///     results. It is used as a filter criterion when retrieving clips.
        /// </remarks>
        public int ClipAgeDays { get; }

        /// <summary>
        ///     Extracts individual properties of the <see cref="ClipSettings" /> instance for use in
        ///     deconstructed form.
        /// </summary>
        /// <param name="featuredOnly">
        ///     Indicates whether only featured clips should be considered.
        /// </param>
        /// <param name="maxClipSeconds">
        ///     The maximum duration of the clip in seconds.
        /// </param>
        /// <param name="clipAgeDays">
        ///     The maximum age of the clip, in days.
        /// </param>
        public void Deconstruct(out bool featuredOnly, out int maxClipSeconds, out int clipAgeDays) {
            featuredOnly = FeaturedOnly;
            maxClipSeconds = MaxClipSeconds;
            clipAgeDays = ClipAgeDays;
        }
    }

    /// <summary>
    ///     Represents cached data for a Twitch clip, including metadata about cache usage.
    /// </summary>
    private class CachedClipData {
        /// <summary>
        ///     Represents a manager responsible for handling Twitch clip functionality.
        /// </summary>
        public ClipData Clip { get; set; }

        /// <summary>
        ///     Gets or sets the frequency of searches for a specific clip in the cache.
        /// </summary>
        /// <remarks>
        ///     This property tracks how many times a specific clip has been searched for in the cache, aiding
        ///     in determining the usage patterns or relevance of cached data over time.
        /// </remarks>
        public int SearchFrequency { get; set; }

        /// <summary>
        ///     Gets or sets the date and time when the cached clip data was last accessed.
        /// </summary>
        /// <remarks>
        ///     This property is updated whenever the cached clip data is accessed to track its usage. Used to
        ///     manage cache expiration and cleanup operations.
        /// </remarks>
        public DateTime LastAccessed { get; set; }
    }
}