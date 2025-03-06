#region

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Streamer.bot.Plugin.Interface;
using Streamer.bot.Plugin.Interface.Enums;
using Streamer.bot.Plugin.Interface.Model;
using Streamer.bot.Common.Events;
using Twitch.Common.Models.Api;

#endregion

public class ClipManager {
    private readonly HttpManager _httpManager;
    private readonly CPHLogger _logger;

    public ClipManager(CPHLogger logger, HttpManager httpManager) {
        _logger = logger;
        _httpManager = httpManager;
    }

    public async Task<ClipData> TryFetchClipAsync(string userId) {
        _logger.Log(LogLevel.Debug, $"Fetching clip for user {userId}.");

        var response = await _httpManager.GetAsync($"https://api.twitch.tv/helix/clips?broadcaster_id={userId}");

        if (string.IsNullOrWhiteSpace(response)) {
            _logger.Log(LogLevel.Warn, $"No valid clip found for user {userId}.");

            return new ClipData(); // Handle null fallback explicitly
        }

        var clip = JsonConvert.DeserializeObject<ClipData>(response);

        if (clip == null || string.IsNullOrWhiteSpace(clip.Url)) {
            _logger.Log(LogLevel.Warn, $"Invalid clip data received for user {userId}.");

            return new ClipData(); // Handle null fallback explicitly
        }

        _logger.Log(LogLevel.Info, $"Selected clip: {clip.Url}");

        return clip;
    }

    public TimeSpan GetDurationWithBuffer(float clipDuration) {
        const int buffer = 5;

        return TimeSpan.FromSeconds(clipDuration + buffer);
    }
}

internal class MiscClipMethods {
    private const string LastClipUrlKey = "last_clip_url";

    private const int AdditionalHtmlSetupDelaySeconds = 3;
    private string _lastClipUrl;

    private Clip GetRandomClip(string userId, ClipSettings clipSettings) {
        clipSettings.Deconstruct(out var featuredOnly, out var maxClipSeconds, out var clipAgeDays);

        _logger.Log(LogLevel.Debug,
                    $"Getting random clip for userId: {userId}, featuredOnly: {featuredOnly}, maxSeconds: {maxClipSeconds}, ageDays: {clipAgeDays}");

        try {
            var twitchUser = FetchTwitchUser(userId);

            if (twitchUser == null) {
                _logger.Log(LogLevel.Warn, $"Twitch user not found for userId: {userId}");

                return null;
            }

            var validPeriods = new[] { 1, 7, 30, 365, 36500 };

            foreach (var period in validPeriods.Where(p => p >= clipAgeDays)) {
                var clips = RetrieveClips(userId, period).ToList();

                if (!clips.Any()) continue;

                var clip = GetMatchingClip(clips, featuredOnly, maxClipSeconds, userId);

                if (clip != null) return clip;
            }

            _logger.Log(LogLevel.Warn,
                        $"No clips found for userId: {userId} after exhausting all periods and filter combinations.");

            return null;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"{CreateErrorPreamble()}: {ex.Message}");

            return null;
        }
    }

    private Clip GetMatchingClip(IEnumerable<ClipData> clips, bool featuredOnly, int maxSeconds, string userId) {
        clips = clips.ToList();

        var matchingClips = FilterClips(clips, featuredOnly, maxSeconds);

        if (matchingClips.Any()) {
            var selectedClip = SelectRandomClip(matchingClips);
            _logger.Log(LogLevel.Debug, $"Selected clip: {selectedClip.Url}");

            return Clip.FromTwitchClip(selectedClip);
        }

        _logger.Log(LogLevel.Debug, $"No matching clips found with the current filter (featuredOnly: {featuredOnly}).");

        if (!featuredOnly) return null;

        matchingClips = FilterClips(clips, false, maxSeconds);

        if (matchingClips.Any()) {
            var selectedClip = SelectRandomClip(matchingClips);
            _logger.Log(LogLevel.Debug, $"Selected clip without featuredOnly: {selectedClip.Url}");

            return Clip.FromTwitchClip(selectedClip);
        }

        _logger.Log(LogLevel.Debug, $"No matching clips found without featuredOnly for userId: {userId}");

        return null;
    }

    private dynamic FetchTwitchUser(string userId) {
        _logger.Log(LogLevel.Debug, $"FetchTwitchUser called with userId: {userId}");

        try {
            var twitchUser = CPH.TwitchGetExtendedUserInfoById(userId);

            if (twitchUser != null) {
                _logger.Log(LogLevel.Info, $"Successfully fetched Twitch user with userId: {userId}");

                return twitchUser;
            }

            _logger.Log(LogLevel.Warn, $"Could not find Twitch userId: {userId}");

            return null;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"{CreateErrorPreamble()}: {ex.Message}");

            return null;
        }
    }

    private IEnumerable<ClipData> RetrieveClips(string userId, int clipAgeDays) {
        _logger.Log(LogLevel.Debug, $"RetrieveClips called with userId: {userId}, clipAgeDays: {clipAgeDays}");

        try {
            var endDate = DateTime.UtcNow;
            var startDate = endDate.AddDays(-clipAgeDays);

            return CPH.GetClipsForUserById(userId, startDate, endDate);
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"{CreateErrorPreamble()}: {ex.Message}");

            return Array.Empty<ClipData>();
        }
    }

    private static List<ClipData> FilterClips(IEnumerable<ClipData> clips, bool featuredOnly, int maxSeconds) {
        return clips.Where(c => (!featuredOnly || c.IsFeatured) && c.Duration <= maxSeconds).ToList();
    }

    private static ClipData SelectRandomClip(List<ClipData> clips) {
        return clips[new Random().Next(clips.Count)];
    }

    private async Task<ClipData> FetchValidClipDataWithCache(ClipData clipData, string clipUrl) {
        _logger.Log(LogLevel.Debug,
                    $"FetchValidClipDataWithCache called with clipData: {JsonConvert.SerializeObject(clipData)}, clipUrl: {clipUrl}");

        lock (_clipDataCache) {
            if (_clipDataCache.TryGetValue(clipUrl, out var cachedClip)) return cachedClip;

            var clipId = clipData?.Id;
            if (!string.IsNullOrWhiteSpace(clipId)) _clipDataCache[clipId] = clipData;
        }

        if (clipData == null) {
            clipData = await FetchClipDataFromUrl(clipUrl);

            if (clipData == null) return null;
        }

        if (string.IsNullOrWhiteSpace(clipData.Id) || string.IsNullOrWhiteSpace(clipData.Url)) {
            _logger.Log(LogLevel.Error, "ClipData validation failed. Missing essential fields (ID or URL).");

            return null;
        }

        _logger.Log(LogLevel.Info, $"Successfully fetched clip data for clip ID: {clipData.Id}");

        return clipData;
    }

    private async Task<ClipData> FetchClipDataFromUrl(string clipUrl) {
        if (string.IsNullOrWhiteSpace(clipUrl) || !clipUrl.Contains("twitch.tv")) {
            _logger.Log(LogLevel.Error, $"Invalid clip URL provided: {clipUrl}");

            return null;
        }

        try {
            var clipData = await _clipManager.GetClipData(clipUrl);

            if (clipData != null && !string.IsNullOrWhiteSpace(clipData.Id))
                return await _clipManager.GetClipDataById(clipData.Id);

            _logger.Log(LogLevel.Error, $"Failed to fetch ClipData or invalid clip ID for URL: {clipUrl}");

            return null;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"{CreateErrorPreamble()}: {ex.Message}");

            return null;
        }
    }

    private string ValidateClipUrl(string clipUrl, ClipData clipData) {
        return !string.IsNullOrWhiteSpace(clipUrl)
                   ? clipUrl
                   : clipData?.Url ?? LogErrorAndReturn<string>("clipUrl is null or empty.");
    }

    private T LogErrorAndReturn<T>(string error) {
        _logger.Log(LogLevel.Error, error);

        return default;
    }

    private void SetLastClipUrl(string url) {
        if (string.IsNullOrWhiteSpace(url)) {
            _logger.Log(LogLevel.Warn, "Attempted to set an empty or null clip URL.");

            return;
        }

        _lastClipUrl = url;
        CPH.SetGlobalVar(LastClipUrlKey, url);
        _logger.Log(LogLevel.Info, $"Successfully set the last clip URL to: {url}");
    }

    private string GetLastClipUrl() {
        if (string.IsNullOrWhiteSpace(_lastClipUrl)) return null;

        var url = CPH.GetGlobalVar<string>(LastClipUrlKey);
        if (string.IsNullOrWhiteSpace(url)) Log(LogLevel.Warn, "No last clip URL found for replay.");

        return url;
    }

    private static ClipInfo GetClipInfo(ClipData clipData) {
        const string unknownStreamer = "Unknown Streamer";
        const string untitledClip = "Untitled Clip";
        const string defaultCuratorName = "Unknown Curator";

        var clipUrl = GetValueOrDefault(clipData?.Url, "about:blank");
        var streamerName = GetValueOrDefault(clipData?.BroadcasterName, unknownStreamer);
        var clipTitle = GetValueOrDefault(clipData?.Title, untitledClip);
        var curatorName = GetValueOrDefault(clipData?.CreatorName, defaultCuratorName);

        return new ClipInfo {
            ClipUrl = clipUrl,
            StreamerName = streamerName,
            ClipTitle = clipTitle,
            CuratorName = curatorName,
            ClipData = clipData
        };
    }

    private static TimeSpan GetDurationWithSetupDelay(float durationInSeconds) {
        return TimeSpan.FromSeconds(durationInSeconds + AdditionalHtmlSetupDelaySeconds);
    }

    private class ClipManagerOld {
        private readonly Dictionary<string, ClipData> _clipCache = new Dictionary<string, ClipData>();
        private readonly IInlineInvokeProxy _cph;
        private readonly LogDelegate _log;
        private readonly TwitchApiClient _twitchApiClient;

        // public ClipManagerOld(TwitchApiClient twitchApiClient, LogDelegate log, IInlineInvokeProxy cph) {
        //     _twitchApiClient = twitchApiClient ?? throw new ArgumentNullException(nameof(twitchApiClient));
        //     _log = log ?? throw new ArgumentNullException(nameof(log));
        //     _cph = cph ?? throw new ArgumentNullException(nameof(cph));
        // }

        public async Task<ClipData> GetClipData(string clipUrl) {
            var clipId = ExtractClipId(clipUrl);

            if (string.IsNullOrWhiteSpace(clipId)) {
                _log(LogLevel.Warn, $"Invalid clip URL: {clipUrl}");

                return null;
            }

            var clipData = await GetClipDataInternal(clipId, async () => await _twitchApiClient.FetchClipById(clipId));

            if (clipData == null) _log(LogLevel.Warn, $"Failed to fetch clip data for URL: {clipUrl}");

            return clipData;
        }

        public async Task<ClipData> GetClipDataById(string clipId) {
            return await GetClipDataInternal(clipId,
                                             async () => {
                                                 var rawClip =
                                                     await _twitchApiClient
                                                         .FetchDataAsync<JObject>($"clips?id={clipId}");

                                                 return rawClip != null
                                                            ? Clip.FromTwitchClip(rawClip).ToClipData(_cph)
                                                            : null;
                                             });
        }

        private string ExtractClipId(string clipUrl) {
            if (string.IsNullOrWhiteSpace(clipUrl))
                throw new ArgumentException("Clip URL cannot be null or empty.", nameof(clipUrl));

            try {
                var uri = new Uri(clipUrl);

                if (uri.Host.IndexOf("twitch.tv", StringComparison.OrdinalIgnoreCase) >= 0)
                    return uri.Segments.LastOrDefault()?.Trim('/');
            } catch (UriFormatException) {
                _log(LogLevel.Warn, "Invalid URL format. Attempting fallback parsing.");

                return clipUrl.Split('/').LastOrDefault()?.Trim('/');
            } catch (Exception ex) {
                _log(LogLevel.Error, $"Error extracting clip ID: {ex.Message}");
            }

            return null;
        }

        private async Task<ClipData> GetClipDataInternal(string clipId, Func<Task<ClipData>> fetchClipDataFunc) {
            if (_clipCache.TryGetValue(clipId, out var cachedClip)) return cachedClip;

            var clipData = await fetchClipDataFunc();

            if (clipData != null) _clipCache[clipId] = clipData;

            return clipData;
        }
    }

    private class ClipSettings {
        public ClipSettings(bool featuredOnly, int maxClipSeconds, int clipAgeDays) {
            FeaturedOnly = featuredOnly;
            MaxClipSeconds = maxClipSeconds;
            ClipAgeDays = clipAgeDays;
        }

        public bool FeaturedOnly { get; }

        public int MaxClipSeconds { get; }

        public int ClipAgeDays { get; }

        public void Deconstruct(out bool featuredOnly, out int maxClipSeconds, out int clipAgeDays) {
            featuredOnly = FeaturedOnly;
            maxClipSeconds = MaxClipSeconds;
            clipAgeDays = ClipAgeDays;
        }
    }

    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public class Clip {
        public string Id { get; set; }

        public string Url { get; set; }

        public string EmbedUrl { get; set; }

        public string BroadcasterId { get; set; }

        public string BroadcasterName { get; set; }

        public int CreatorId { get; set; }

        public string CreatorName { get; set; }

        public string VideoId { get; set; }

        public string GameId { get; set; }

        public string Language { get; set; }

        public string Title { get; set; }

        public int ViewCount { get; set; }

        public DateTime CreatedAt { get; set; }

        public string ThumbnailUrl { get; set; }

        public float Duration { get; set; }

        public bool IsFeatured { get; set; }

        public static Clip FromTwitchClip(JObject twitchClip) {
            if (twitchClip == null) throw new ArgumentNullException(nameof(twitchClip));

            return MapClip(new Clip(), twitchClip);
        }

        public static Clip FromTwitchClip(ClipData twitchClipData) {
            if (twitchClipData == null) throw new ArgumentNullException(nameof(twitchClipData));

            return MapClip(new Clip(), twitchClipData);
        }

        public ClipData ToClipData(IInlineInvokeProxy cphInstance) {
            try {
                return new ClipData {
                    Id = Id,
                    Url = Url,
                    EmbedUrl = EmbedUrl,
                    BroadcasterId = BroadcasterId,
                    BroadcasterName = BroadcasterName,
                    CreatorId = CreatorId,
                    CreatorName = CreatorName,
                    VideoId = VideoId,
                    GameId = GameId,
                    Language = Language,
                    Title = Title,
                    ViewCount = ViewCount,
                    CreatedAt = CreatedAt,
                    ThumbnailUrl = ThumbnailUrl,
                    Duration = Duration,
                    IsFeatured = IsFeatured
                };
            } catch (Exception ex) {
                cphInstance.LogError($"Cliparino :: {nameof(ToClipData)} :: {ex.Message}");

                return null;
            }
        }

        public JObject ParseClipData(string rawClipData) {
            return JsonConvert.DeserializeObject<JObject>(rawClipData);
        }

        private static Clip MapClip<TSource>(Clip clip, TSource source) {
            switch (source) {
                case JObject jObject:
                    clip.Id = (string)jObject["id"];
                    clip.Url = (string)jObject["url"];
                    clip.EmbedUrl = (string)jObject["embed_url"];
                    clip.BroadcasterId = (string)jObject["broadcaster_id"];
                    clip.BroadcasterName = (string)jObject["broadcaster_name"];
                    clip.CreatorId = (int)jObject["creator_id"];
                    clip.CreatorName = (string)jObject["creator_name"];
                    clip.VideoId = (string)jObject["video_id"];
                    clip.GameId = (string)jObject["game_id"];
                    clip.Language = (string)jObject["language"];
                    clip.Title = (string)jObject["title"];
                    clip.ViewCount = (int)jObject["view_count"];
                    clip.CreatedAt = (DateTime)jObject["created_at"];
                    clip.ThumbnailUrl = (string)jObject["thumbnail_url"];
                    clip.Duration = (float)jObject["duration"];
                    clip.IsFeatured = (bool)jObject["is_featured"];

                    break;

                case ClipData clipData:
                    clip.Id = clipData.Id;
                    clip.Url = clipData.Url;
                    clip.EmbedUrl = clipData.EmbedUrl;
                    clip.BroadcasterId = clipData.BroadcasterId;
                    clip.BroadcasterName = clipData.BroadcasterName;
                    clip.CreatorId = clipData.CreatorId;
                    clip.CreatorName = clipData.CreatorName;
                    clip.VideoId = clipData.VideoId;
                    clip.GameId = clipData.GameId;
                    clip.Language = clipData.Language;
                    clip.Title = clipData.Title;
                    clip.ViewCount = clipData.ViewCount;
                    clip.CreatedAt = clipData.CreatedAt;
                    clip.ThumbnailUrl = clipData.ThumbnailUrl;
                    clip.Duration = clipData.Duration;
                    clip.IsFeatured = clipData.IsFeatured;

                    break;
            }

            return clip;
        }
    }
}