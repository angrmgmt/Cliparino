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
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301
    USA
*/

#region

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Streamer.bot.Common.Events;
using Streamer.bot.Plugin.Interface;
using Streamer.bot.Plugin.Interface.Enums;
using Streamer.bot.Plugin.Interface.Model;
using Twitch.Common.Models.Api;

#endregion

// ReSharper disable once UnusedType.Global
// ReSharper disable once ClassNeverInstantiated.Global
/// <summary>
///     Represents a custom inline script handler for the Streamer.bot environment. Handles commands, interactions, and
///     execution logic specific to Cliparino's Twitch clip management.
/// </summary>
public class CPHInline : CPHInlineBase {
    private const int DefaultWidth = 1920;
    private const int DefaultHeight = 1080;
    private const int AdditionalHtmlSetupDelaySeconds = 3;
    private const string LastClipUrlKey = "last_clip_url";

    private const string CSSText = "div {\n"
                                   + "    background-color: #0071c5;\n"
                                   + "    background-color: rgba(0,113,197,1);\n"
                                   + "    margin: 0 auto;\n"
                                   + "    overflow: hidden;\n"
                                   + "}\n\n"
                                   + "#twitch-embed {\n"
                                   + "    display: block;\n"
                                   + "}\n\n"
                                   + ".iframe-container {\n"
                                   + "    height: 1080px;\n"
                                   + "    position: relative;\n"
                                   + "    width: 1920px;\n"
                                   + "}\n\n"
                                   + "#clip-iframe {\n"
                                   + "    height: 100%;\n"
                                   + "    left: 0;\n"
                                   + "    position: absolute;\n"
                                   + "    top: 0;\n"
                                   + "    width: 100%;\n"
                                   + "}\n\n"
                                   + "#overlay-text {\n"
                                   + "    background-color: #042239;\n"
                                   + "    background-color: rgba(4,34,57,0.7071);\n"
                                   + "    border-radius: 5px;\n"
                                   + "    color: #ffb809;\n"
                                   + "    left: 5%;\n"
                                   + "    opacity: 0.5;\n"
                                   + "    padding: 10px;\n"
                                   + "    position: absolute;\n"
                                   + "    top: 80%;\n"
                                   + "}\n\n"
                                   + ".line1, .line2, .line3 {\n"
                                   + "    font-family: 'Open Sans', sans-serif;\n"
                                   + "    font-size: 2em;\n"
                                   + "}\n\n"
                                   + ".line1 {\n"
                                   + "    font: normal 600 2em/1.2 'OpenDyslexic', 'Open Sans', sans-serif;\n"
                                   + "}\n\n"
                                   + ".line2 {\n"
                                   + "    font: normal 400 1.5em/1 'OpenDyslexic', 'Open Sans', sans-serif;\n"
                                   + "}\n\n"
                                   + ".line3 {\n"
                                   + "    font: italic 100 1em/1 'OpenDyslexic', 'Open Sans', sans-serif;\n"
                                   + "}";

    private const string HTMLText = "<!DOCTYPE html>\n"
                                    + "<html lang=\"en\">\n"
                                    + "<head>\n"
                                    + "<meta charset=\"utf-8\">\n"
                                    + "<link href=\"/index.css\" rel=\"stylesheet\" type=\"text/css\">\n"
                                    + "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">\n"
                                    + "<title>Cliparino</title>\n"
                                    + "</head>\n"
                                    + "<body>\n"
                                    + "<div id=\"twitch-embed\">\n"
                                    + "<div class=\"iframe-container\">\n"
                                    + "<iframe allowfullscreen autoplay=\"true\" controls=\"false\" height=\"1080\" id=\"clip-iframe\" mute=\"false\" preload=\"auto\" src=\"https://clips.twitch.tv/embed?clip=[[clipId]]&nonce=[[nonce]]&autoplay=true&parent=localhost\" title=\"Cliparino\" width=\"1920\">\n"
                                    + "</iframe>\n"
                                    + "<div class=\"overlay-text\" id=\"overlay-text\">\n"
                                    + "<div class=\"line1\">\n"
                                    + "[[streamerName]] doin' a heckin' [[gameName]] stream\n"
                                    + "</div>\n"
                                    + "<div class=\"line2\">\n"
                                    + "[[clipTitle]]\n"
                                    + "</div>\n"
                                    + "<div class=\"line3\">\n"
                                    + "by [[curatorName]]\n"
                                    + "</div>\n"
                                    + "</div>\n"
                                    + "</div>\n"
                                    + "</div>\n"
                                    + "</body>\n"
                                    + "</html>";

    private static readonly Dictionary<string, string> CORSHeaders = new Dictionary<string, string> {
        { "Access-Control-Allow-Origin", "*" },
        { "Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS" },
        { "Access-Control-Allow-Headers", "*" }, {
            "Content-Security-Policy",
            "script-src 'nonce-[[nonce]]' 'strict-dynamic';\nobject-src 'none';\nbase-uri 'none'; frame-ancestors 'self' https://clips.twitch.tv;"
        }
    };

    private static readonly object ServerLock = new object();
    private static string _htmlInMemory;

    private readonly Dictionary<string, ClipData> _clipDataCache = new Dictionary<string, ClipData>();
    private CancellationTokenSource _autoStopCancellationTokenSource;

    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    private ClipManager _clipManager;
    private HttpClient _httpClient;
    private Task _listeningTask;
    private bool _loggingEnabled;
    private HttpListener _server;
    private TwitchApiClient _twitchApiClient;
    private string _lastClipUrl;

    #region Initialization & Core Execution

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public void Init() {
        _httpClient = new HttpClient();
        _twitchApiClient =
            new TwitchApiClient(_httpClient, new OAuthInfo(CPH.TwitchClientId, CPH.TwitchOAuthToken), Log);
        _clipManager = new ClipManager(_twitchApiClient, Log, CPH);
    }

    // ReSharper disable once UnusedMember.Global
    /// <summary>
    ///     Executes the primary logic for the `CPHInline` object. This is the main entry point for the script, called by
    ///     Streamer.bot during execution.
    /// </summary>
    /// <remarks>
    ///     Streamer.bot invokes this method to execute the user's custom logic. The method processes chat commands such as
    ///     `!watch`, `!so`, `!replay`, or `!stop` to control playback of Twitch clips and OBS overlay behavior.
    /// </remarks>
    /// <returns>
    ///     A boolean indicating whether the script executed successfully. Returns <c>true</c> if execution succeeded;
    ///     otherwise, <c>false</c>.
    /// </returns>
    public bool Execute() {
        Log(LogLevel.Debug, $"{nameof(Execute)} for Cliparino started.");

        try {
            if (!TryGetCommand(out var command)) {
                Log(LogLevel.Warn, "Command argument is missing.");

                return false;
            }

            EnsureCliparinoInCurrentSceneAsync(null, string.Empty).GetAwaiter().GetResult();

            var input0 = GetArgument("input0", string.Empty);
            var width = GetArgument("width", DefaultWidth);
            var height = GetArgument("height", DefaultHeight);

            _loggingEnabled = GetArgument("logging", false);

            Log(LogLevel.Info, $"Executing command: {command}");

            switch (command.ToLower()) {
                case "!watch": HandleWatchCommandAsync(input0, width, height).GetAwaiter().GetResult(); break;

                case "!so": HandleShoutoutCommandAsync(input0).GetAwaiter().GetResult(); break;

                case "!replay": HandleReplayCommandAsync(width, height).GetAwaiter().GetResult(); break;

                case "!stop": HandleStopCommand(); break;

                default:
                    Log(LogLevel.Warn, $"Unknown command: {command}");

                    return false;
            }

            return true;
        } catch (Exception ex) {
            Log(LogLevel.Error, $"An error occurred: {ex.Message}");

            return false;
        } finally {
            Log(LogLevel.Debug, $"{nameof(Execute)} for Cliparino completed.");
        }
    }

    // ReSharper disable once UnusedMember.Local
    private async Task DisposeAsync() {
        await CleanupServer();
    }

    ~CPHInline() {
        if (_server.Equals(null)) return;

        _server.Close();
        _server.Abort();
        ((IDisposable)_server).Dispose();
    }

    #endregion

    #region Chat Command Handlers

    private T GetArgument<T>(string argName, T defaultValue = default) {
        return CPH.TryGetArg(argName, out T value) ? value : defaultValue;
    }

    private async Task HandleShoutoutCommandAsync(string user) {
        Log(LogLevel.Debug, $"{nameof(HandleShoutoutCommandAsync)} called with user: {user}");

        try {
            user = SanitizeUsername(user);

            if (string.IsNullOrEmpty(user)) {
                Log(LogLevel.Warn, "No valid user provided for shoutout.");

                return;
            }

            var extendedUserInfo = FetchExtendedUserInfo(user);

            if (extendedUserInfo == null) return;

            var messageTemplate = GetShoutoutMessageTemplate();
            var clip = TryFetchClip(extendedUserInfo.UserId);

            await HandleShoutoutMessageAsync(extendedUserInfo, messageTemplate, clip);
        } catch (Exception ex) {
            Log(LogLevel.Error, ex.Message);
        }
    }

    private async Task HandleWatchCommandAsync(string url, int width, int height) {
        Log(LogLevel.Debug,
            $"{nameof(HandleWatchCommandAsync)} called with url='{url}', width={width}, height={height}");

        try {
            (width, height) = ValidateDimensions(width, height);

            url = ValidateClipUrl(url, null);

            if (string.IsNullOrEmpty(url)) {
                Log(LogLevel.Warn, "No valid clip URL provided. Aborting command.");

                return;
            }

            var clipData = await _clipManager.GetClipData(url);

            if (clipData == null) {
                Log(LogLevel.Error, "Failed to retrieve clip data.");

                return;
            }

            Log(LogLevel.Info, $"Now playing: {clipData.Title} clipped by {clipData.CreatorName}");
            await HostClipDataAsync(clipData, url, width, height);
        } catch (Exception ex) {
            Log(LogLevel.Error, ex.Message);
        }
    }

    private async Task HandleReplayCommandAsync(int width, int height) {
        Log(LogLevel.Debug, $"{nameof(HandleReplayCommandAsync)} called with width={width}, height={height}");

        try {
            var lastClipUrl = GetLastClipUrl();

            if (!string.IsNullOrEmpty(lastClipUrl)) await ProcessAndHostClipDataAsync(lastClipUrl, null);
        } catch (Exception ex) {
            Log(LogLevel.Error, ex.Message);
        }
    }

    private void HandleStopCommand() {
        Log(LogLevel.Debug, $"{nameof(HandleStopCommand)} called, setting browser source page to a blank layout.");
        CancelCurrentToken();

        Log(LogLevel.Info, "Cancelled ongoing auto-stop task.");
        EnsureClipSourceHidden();
        SetBrowserSource("about:blank");
    }

    #endregion

    #region Clip Management

    /// <summary>
    ///     Retrieves a random clip for the given user, applying specified filters.
    /// </summary>
    private Clip GetRandomClip(string userId, ClipSettings clipSettings) {
        clipSettings.Deconstruct(out var featuredOnly, out var maxClipSeconds, out var clipAgeDays);

        Log(LogLevel.Debug,
            $"Getting random clip for userId: {userId}, featuredOnly: {featuredOnly}, maxSeconds: {maxClipSeconds}, ageDays: {clipAgeDays}");

        try {
            var twitchUser = FetchTwitchUser(userId);

            if (twitchUser == null) {
                Log(LogLevel.Warn, $"Twitch user not found for userId: {userId}");

                return null;
            }

            var validPeriods = new[] { 1, 7, 30, 365, 36500 };

            foreach (var period in validPeriods.Where(p => p >= clipAgeDays)) {
                var clips = RetrieveClips(userId, period).ToList();

                if (!clips.Any()) continue;

                var clip = GetMatchingClip(clips, featuredOnly, maxClipSeconds, userId);

                if (clip != null) return clip;
            }

            Log(LogLevel.Warn,
                $"No clips found for userId: {userId} after exhausting all periods and filter combinations.");

            return null;
        } catch (Exception ex) {
            Log(LogLevel.Error, $"{CreateErrorPreamble()}: {ex.Message}");

            return null;
        }
    }

    /// <summary>
    ///     Attempts to find a matching clip based on provided criteria.
    /// </summary>
    private Clip GetMatchingClip(IEnumerable<ClipData> clips, bool featuredOnly, int maxSeconds, string userId) {
        clips = clips.ToList();

        var matchingClips = FilterClips(clips, featuredOnly, maxSeconds);

        if (matchingClips.Any()) {
            var selectedClip = SelectRandomClip(matchingClips);
            Log(LogLevel.Debug, $"Selected clip: {selectedClip.Url}");

            return Clip.FromTwitchClip(selectedClip);
        }

        Log(LogLevel.Debug, $"No matching clips found with the current filter (featuredOnly: {featuredOnly}).");

        if (!featuredOnly) return null;

        matchingClips = FilterClips(clips, false, maxSeconds);

        if (matchingClips.Any()) {
            var selectedClip = SelectRandomClip(matchingClips);
            Log(LogLevel.Debug, $"Selected clip without featuredOnly: {selectedClip.Url}");

            return Clip.FromTwitchClip(selectedClip);
        }

        Log(LogLevel.Debug, $"No matching clips found without featuredOnly for userId: {userId}");

        return null;
    }

    /// <summary>
    ///     Retrieves the Twitch user, logging appropriate details.
    /// </summary>
    private dynamic FetchTwitchUser(string userId) {
        Log(LogLevel.Debug, $"FetchTwitchUser called with userId: {userId}");

        try {
            var twitchUser = CPH.TwitchGetExtendedUserInfoById(userId);

            if (twitchUser != null) {
                Log(LogLevel.Info, $"Successfully fetched Twitch user with userId: {userId}");

                return twitchUser;
            }

            Log(LogLevel.Warn, $"Could not find Twitch userId: {userId}");

            return null;
        } catch (Exception ex) {
            Log(LogLevel.Error, $"{CreateErrorPreamble()}: {ex.Message}");

            return null;
        }
    }

    /// <summary>
    ///     Retrieves clips for the given user based on clip age days.
    /// </summary>
    private IEnumerable<ClipData> RetrieveClips(string userId, int clipAgeDays) {
        Log(LogLevel.Debug, $"RetrieveClips called with userId: {userId}, clipAgeDays: {clipAgeDays}");

        try {
            var endDate = DateTime.UtcNow;
            var startDate = endDate.AddDays(-clipAgeDays);

            return CPH.GetClipsForUserById(userId, startDate, endDate);
        } catch (Exception ex) {
            Log(LogLevel.Error, $"{CreateErrorPreamble()}: {ex.Message}");

            return Array.Empty<ClipData>();
        }
    }

    /// <summary>
    ///     Filters clips based on the specified criteria.
    /// </summary>
    private static List<ClipData> FilterClips(IEnumerable<ClipData> clips, bool featuredOnly, int maxSeconds) {
        return clips.Where(c => (!featuredOnly || c.IsFeatured) && c.Duration <= maxSeconds).ToList();
    }

    /// <summary>
    ///     Selects a random clip from the provided list.
    /// </summary>
    private static ClipData SelectRandomClip(List<ClipData> clips) {
        return clips[new Random().Next(clips.Count)];
    }

    /// <summary>
    ///     Fetches a valid clip's data, utilizing caching to speed up frequent requests.
    /// </summary>
    private async Task<ClipData> FetchValidClipDataWithCache(ClipData clipData, string clipUrl) {
        Log(LogLevel.Debug,
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
            Log(LogLevel.Error, "ClipData validation failed. Missing essential fields (ID or URL).");

            return null;
        }

        Log(LogLevel.Info, $"Successfully fetched clip data for clip ID: {clipData.Id}");

        return clipData;
    }

    /// <summary>
    ///     Fetches clip data for a specific URL. Logs errors for invalid or incomplete fetches.
    /// </summary>
    private async Task<ClipData> FetchClipDataFromUrl(string clipUrl) {
        if (string.IsNullOrWhiteSpace(clipUrl) || !clipUrl.Contains("twitch.tv")) {
            Log(LogLevel.Error, $"Invalid clip URL provided: {clipUrl}");

            return null;
        }

        try {
            var clipData = await _clipManager.GetClipData(clipUrl);

            if (clipData == null || string.IsNullOrWhiteSpace(clipData.Id)) {
                Log(LogLevel.Error, $"Failed to fetch ClipData or invalid clip ID for URL: {clipUrl}");

                return null;
            }

            return await _clipManager.GetClipDataById(clipData.Id);
        } catch (Exception ex) {
            Log(LogLevel.Error, $"{CreateErrorPreamble()}: {ex.Message}");

            return null;
        }
    }

    /// <summary>
    ///     Validates and resolves a clip URL from either the provided URL or existing clip data.
    /// </summary>
    private string ValidateClipUrl(string clipUrl, ClipData clipData) {
        return !string.IsNullOrWhiteSpace(clipUrl)
                   ? clipUrl
                   : clipData?.Url ?? LogErrorAndReturn<string>("clipUrl is null or empty.");
    }

    /// <summary>
    ///     Logs an error message and returns the specified default value.
    /// </summary>
    private T LogErrorAndReturn<T>(string error) {
        Log(LogLevel.Error, error);

        return default;
    }

    /// <summary>
    ///     Stores the last known clip URL for replay or further actions.
    /// </summary>
    private void SetLastClipUrl(string url) {
        if (string.IsNullOrWhiteSpace(url)) {
            Log(LogLevel.Warn, "Attempted to set an empty or null clip URL.");

            return;
        }

        _lastClipUrl = url;
        CPH.SetGlobalVar(LastClipUrlKey, url);
        Log(LogLevel.Info, $"Successfully set the last clip URL to: {url}");
    }

    /// <summary>
    ///     Retrieves the last stored clip URL for replay.
    /// </summary>
    private string GetLastClipUrl() {
        if (string.IsNullOrWhiteSpace(_lastClipUrl)) return null;

        var url = CPH.GetGlobalVar<string>(LastClipUrlKey);
        if (string.IsNullOrWhiteSpace(url)) Log(LogLevel.Warn, "No last clip URL found for replay.");

        return url;
    }

    /// <summary>
    ///     Extracts detailed clip information such as streamer name, clip title, and curator name.
    /// </summary>
    private static (string StreamerName, string ClipTitle, string CuratorName) GetClipInfo(
        ClipData clipData,
        string defaultCuratorName = "Anonymous") {
        const string unknownStreamer = "Unknown Streamer";
        const string untitledClip = "Untitled Clip";

        var streamerName = GetValueOrDefault(clipData?.BroadcasterName, unknownStreamer);
        var clipTitle = GetValueOrDefault(clipData?.Title, untitledClip);
        var curatorName = GetValueOrDefault(clipData?.CreatorName, defaultCuratorName);

        return (streamerName, clipTitle, curatorName);
    }

    /// <summary>
    ///     Determines the effective duration for a clip, including any setup delays.
    /// </summary>
    private static TimeSpan GetDurationWithSetupDelay(float durationInSeconds) {
        return TimeSpan.FromSeconds(durationInSeconds + AdditionalHtmlSetupDelaySeconds);
    }

    private class ClipManager {
        private readonly TwitchApiClient _twitchApiClient;
        private readonly LogDelegate _log;
        private readonly IInlineInvokeProxy _cph;
        private readonly Dictionary<string, ClipData> _clipCache = new Dictionary<string, ClipData>();

        public ClipManager(TwitchApiClient twitchApiClient, LogDelegate log, IInlineInvokeProxy cph) {
            _twitchApiClient = twitchApiClient ?? throw new ArgumentNullException(nameof(twitchApiClient));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _cph = cph ?? throw new ArgumentNullException(nameof(cph));
        }

        public async Task<ClipData> GetClipData(string clipUrl) {
            var clipId = ExtractClipId(clipUrl);

            if (string.IsNullOrWhiteSpace(clipId)) throw new ArgumentException("Invalid clip URL.", nameof(clipUrl));

            return await GetClipDataInternal(clipId, async () => await _twitchApiClient.FetchClipById(clipId));
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

        public string ExtractClipId(string clipUrl) {
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
        public bool FeaturedOnly { get; }
        public int MaxClipSeconds { get; }
        public int ClipAgeDays { get; }

        public ClipSettings(bool featuredOnly, int maxClipSeconds, int clipAgeDays) {
            FeaturedOnly = featuredOnly;
            MaxClipSeconds = maxClipSeconds;
            ClipAgeDays = clipAgeDays;
        }

        public void Deconstruct(out bool featuredOnly, out int maxClipSeconds, out int clipAgeDays) {
            featuredOnly = FeaturedOnly;
            maxClipSeconds = MaxClipSeconds;
            clipAgeDays = ClipAgeDays;
        }
    }

    /// <summary>
    ///     Represents a single Twitch clip's data model, including information such as the URL, creator details, game details,
    ///     and more.
    /// </summary>
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public class Clip {
        /// <summary>
        ///     Gets or sets the unique identifier for the clip.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        ///     Gets or sets the direct URL to view the clip.
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        ///     Gets or sets the embeddable URL for the clip. Used when embedding the clip in web-based overlays or pages.
        /// </summary>
        public string EmbedUrl { get; set; }

        /// <summary>
        ///     Gets or sets the Twitch broadcaster's unique ID that the clip is associated with.
        /// </summary>
        public string BroadcasterId { get; set; }

        /// <summary>
        ///     Gets or sets the Twitch broadcaster's display name that the clip is associated with.
        /// </summary>
        public string BroadcasterName { get; set; }

        /// <summary>
        ///     Gets or sets the unique identifier of the clip's creator.
        /// </summary>
        public int CreatorId { get; set; }

        /// <summary>
        ///     Gets or sets the display name of the clip's creator.
        /// </summary>
        public string CreatorName { get; set; }

        /// <summary>
        ///     Gets or sets the ID of the video the clip is derived from, i.e., the stream's VoD ID.
        /// </summary>
        public string VideoId { get; set; }

        /// <summary>
        ///     Gets or sets the ID of the game that was being streamed when the clip was created.
        /// </summary>
        public string GameId { get; set; }

        /// <summary>
        ///     Gets or sets the language of the clip's content (e.g., "en" for English).
        /// </summary>
        public string Language { get; set; }

        /// <summary>
        ///     Gets or sets the title of the clip.
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        ///     Gets or sets the total number of views for the clip.
        /// </summary>
        public int ViewCount { get; set; }

        /// <summary>
        ///     Gets or sets the time when the clip was created (UTC).
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        ///     Gets or sets the URL to the clip's thumbnail image.
        /// </summary>
        public string ThumbnailUrl { get; set; }

        /// <summary>
        ///     Gets or sets the duration of the clip in seconds.
        /// </summary>
        public float Duration { get; set; }

        /// <summary>
        ///     Gets or sets a value indicating whether the clip is featured content on Twitch.
        /// </summary>
        public bool IsFeatured { get; set; }

        /// <summary>
        ///     Creates a <see cref="Clip" /> object from a Twitch clip's raw JSON data.
        /// </summary>
        /// <param name="twitchClip">
        ///     A JSON object representing the raw Twitch clip data.
        /// </param>
        /// <returns>
        ///     An instance of the <see cref="Clip" /> class with data populated from Twitch.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     Thrown if the provided JSON object is null.
        /// </exception>
        public static Clip FromTwitchClip(JObject twitchClip) {
            if (twitchClip == null) throw new ArgumentNullException(nameof(twitchClip));

            return MapClip(new Clip(), twitchClip);
        }

        /// <summary>
        ///     Creates a <see cref="Clip" /> object from a <see cref="ClipData" /> instance.
        /// </summary>
        /// <param name="twitchClipData">
        ///     An instance of the <see cref="ClipData" /> class populated with clip information.
        /// </param>
        /// <returns>
        ///     An instance of <see cref="Clip" /> created from the <see cref="ClipData" />.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     Thrown if <see cref="ClipData" /> is null.
        /// </exception>
        public static Clip FromTwitchClip(ClipData twitchClipData) {
            if (twitchClipData == null) throw new ArgumentNullException(nameof(twitchClipData));

            return MapClip(new Clip(), twitchClipData);
        }

        /// <summary>
        ///     Converts the current <see cref="Clip" /> object into a <see cref="ClipData" /> representation.
        /// </summary>
        /// <param name="cphInstance">
        ///     A reference to the parent <see cref="CPHInline" /> instance.
        /// </param>
        /// <returns>
        ///     A new <see cref="ClipData" /> object populated with data from this <see cref="Clip" /> instance.
        /// </returns>
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

        /// <summary>
        ///     Parses the raw string data into a JSON object.
        /// </summary>
        /// <param name="rawClipData">
        ///     The raw string data.
        /// </param>
        /// <returns>
        ///     A <see cref="JObject" /> with parsed data.
        /// </returns>
        public JObject ParseClipData(string rawClipData) {
            return JsonConvert.DeserializeObject<JObject>(rawClipData);
        }

        /// <summary>
        ///     Maps values from a <see cref="JObject" /> or <see cref="ClipData" /> to the given <see cref="Clip" /> instance.
        /// </summary>
        /// <typeparam name="TSource">
        ///     The source type, either <see cref="JObject" /> or <see cref="ClipData" />.
        /// </typeparam>
        /// <param name="clip">
        ///     The target <see cref="Clip" /> instance.
        /// </param>
        /// <param name="source">
        ///     The source object to map from.
        /// </param>
        /// <returns>
        ///     The updated <see cref="Clip" /> instance.
        /// </returns>
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

    #endregion

    #region OBS Management

    private async Task<string> EnsureCliparinoInCurrentSceneAsync(string currentScene, string clipUrl) {
        try {
            currentScene = EnsureSceneIsNotNullOrEmpty(currentScene);

            await EnsureCliparinoInSceneAsync(currentScene);
            var gameName = await GetGameNameFromClipUrlAsync(clipUrl);

            return gameName;
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error in {nameof(EnsureCliparinoInCurrentSceneAsync)}: {ex.Message}");

            return "Unknown Game";
        }
    }

    private string EnsureSceneIsNotNullOrEmpty(string currentScene) {
        if (string.IsNullOrWhiteSpace(currentScene)) currentScene = CPH.ObsGetCurrentScene();

        if (!string.IsNullOrWhiteSpace(currentScene)) return currentScene;

        Log(LogLevel.Warn, "Current scene is empty or null.");

        throw new InvalidOperationException("Current scene is required.");
    }

    private void EnsureClipSourceHidden() {
        var currentScene = CPH.ObsGetCurrentScene();
        const string clipSourceName = "Cliparino";
        EnsureSourceExistsAndIsVisible(currentScene, clipSourceName, false);
    }

    private async Task HostClipDataAsync(ClipData clipData, string url, int width, int height) {
        Log(LogLevel.Info, $"Setting browser source with URL: {url}, width: {width}, height: {height}");
        var currentScene = CPH.ObsGetCurrentScene();

        if (string.IsNullOrWhiteSpace(currentScene)) {
            Log(LogLevel.Warn, "Unable to determine the current OBS scene. Aborting clip setup.");

            return;
        }

        await PrepareSceneForClipHostingAsync();
        await ProcessAndHostClipDataAsync(url, clipData);
    }

    private async Task<string> GetGameNameFromClipUrlAsync(string clipUrl) {
        try {
            var clipData = await _clipManager.GetClipData(clipUrl);

            if (clipData == null) return "Unknown Game";

            var gameData = await _twitchApiClient.FetchGameById(clipData.GameId);

            return gameData?.Name ?? "Unknown Game";
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error fetching game name: {ex.Message}");

            return "Unknown Game";
        }
    }

    private async Task EnsureCliparinoInSceneAsync(string currentScene) {
        try {
            const string cliparinoSourceName = "Cliparino";
            if (!IsCliparinoSourceInScene(currentScene, cliparinoSourceName))
                await AddCliparinoSourceToSceneAsync(currentScene, cliparinoSourceName);
            else
                Log(LogLevel.Debug, $"Cliparino source already exists in scene '{currentScene}'.");
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error in {nameof(EnsureCliparinoInSceneAsync)}: {ex.Message}");
        }
    }

    private bool IsCliparinoSourceInScene(string scene, string sourceName) {
        return CPH.ObsIsSourceVisible(scene, sourceName);
    }

    private async Task AddCliparinoSourceToSceneAsync(string scene, string sourceName) {
        await Task.Run(() => AddSceneSource(scene, sourceName));
        Log(LogLevel.Info, $"Cliparino source added to scene '{scene}'.");
    }

    private async Task PrepareSceneForClipHostingAsync() {
        const string cliparinoSourceName = "Cliparino";
        const string playerSourceName = "Player";

        Log(LogLevel.Info, $"Preparing scene '{cliparinoSourceName}' for clip hosting.");
        EnsureCliparinoSceneExists(cliparinoSourceName);
        EnsurePlayerSourceIsVisible(cliparinoSourceName, playerSourceName);

        await ConfigureAudioForPlayerSourceAsync();
    }

    private void EnsureCliparinoSceneExists(string sceneName) {
        if (!SceneExists(sceneName)) {
            CreateScene(sceneName);
            Log(LogLevel.Info, $"Scene '{sceneName}' did not exist and was successfully created.");
            AddSceneSource(CPH.ObsGetCurrentScene(), sceneName);
        }
    }

    private void EnsurePlayerSourceIsVisible(string sceneName, string sourceName) {
        if (!EnsureSourceExistsAndIsVisible(sceneName, sourceName))
            AddBrowserSource(sceneName, sourceName, "http://localhost:8080/index.htm");
    }

    private async Task ProcessAndHostClipDataAsync(string clipUrl, ClipData clipData) {
        try {
            Log(LogLevel.Info, $"Processing and hosting clip with URL: {clipUrl}");

            var validatedClipData = clipData ?? await FetchValidClipDataWithCache(null, clipUrl);

            if (validatedClipData == null) {
                Log(LogLevel.Error, "Validated clip data is null. Aborting hosting process.");

                return;
            }

            await HostClipWithDetailsAsync(clipUrl, validatedClipData);
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error in {nameof(ProcessAndHostClipDataAsync)}: {ex.Message}");
        }
    }

    private bool EnsureSourceExistsAndIsVisible(string sceneName, string sourceName, bool setVisible = true) {
        Log(LogLevel.Debug, $"Ensuring source '{sourceName}' exists and is visible in scene '{sceneName}'.");

        if (!SourceExistsInScene(sceneName, sourceName)) {
            Log(LogLevel.Warn, $"The source '{sourceName}' does not exist in scene '{sceneName}'. Attempting to add.");
            AddSceneSource(sceneName, sourceName);

            if (!SourceExistsInScene(sceneName, sourceName)) {
                Log(LogLevel.Error, $"Failed to create the '{sourceName}' source in the '{sceneName}' scene.");

                return false;
            }
        }

        if (!setVisible) return true;

        Log(LogLevel.Info, $"Setting source '{sourceName}' to visible in scene '{sceneName}'.");
        CPH.ObsSetSourceVisibility(sceneName, sourceName, true);

        return true;
    }

    private void SetBrowserSource(string baseUrl, string targetScene = null) {
        Log(LogLevel.Debug, $"SetBrowserSource was called for URL '{baseUrl}'.");

        var sourceUrl = CreateSourceUrl(baseUrl);
        if (targetScene == null) targetScene = CPH.ObsGetCurrentScene();

        if (string.IsNullOrEmpty(targetScene)) throw new InvalidOperationException("Unable to retrieve target scene.");

        UpdateOrAddBrowserSource(targetScene, sourceUrl, "Cliparino", baseUrl);
    }

    private void UpdateOrAddBrowserSource(string targetScene, string sourceUrl, string sourceName, string baseUrl) {
        if (!SourceExistsInScene(targetScene, sourceName)) {
            AddSceneSource(targetScene, sourceName);
            Log(LogLevel.Info, $"Added '{sourceName}' scene source to '{targetScene}'.");
        } else {
            UpdateBrowserSource(targetScene, sourceName, sourceUrl);

            if (baseUrl == "about:blank") {
                Log(LogLevel.Info, "Hiding Cliparino source after setting 'about:blank'.");
                CPH.ObsSetSourceVisibility(targetScene, sourceName, false);
            }
        }
    }

    private void RefreshBrowserSource() {
        var payload = new {
            requestType = "PressInputPropertiesButton",
            requestData = new { inputName = "Player", propertyName = "refreshnocache" }
        };

        var response = CPH.ObsSendRaw(payload.requestType, JsonConvert.SerializeObject(payload.requestData));
        Log(LogLevel.Info, $"Refreshed browser source 'Player'. Response: {response}");
    }

    private void UpdateBrowserSource(string sceneName, string sourceName, string url) {
        Log(LogLevel.Debug, $"Update URL to OBS: {url}");

        try {
            var payload = new {
                requestType = "SetInputSettings",
                requestData = new { inputName = sourceName, inputSettings = new { url }, overlay = true }
            };

            var response = CPH.ObsSendRaw(payload.requestType, JsonConvert.SerializeObject(payload.requestData));
            Log(LogLevel.Info, $"Browser source '{sourceName}' in scene '{sceneName}' updated with new URL '{url}'.");
            Log(LogLevel.Debug, $"Response from OBS: {response}");

            RefreshBrowserSource();
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error in {nameof(UpdateBrowserSource)}: {ex.Message}");
        }
    }

    private Task ConfigureAudioForPlayerSourceAsync() {
        var monitorTypePayload = GenerateSetAudioMonitorTypePayload("monitorAndOutput");
        var monitorTypeResponse = CPH.ObsSendRaw(monitorTypePayload.RequestType,
                                                 JsonConvert.SerializeObject(monitorTypePayload.RequestData));

        if (string.IsNullOrEmpty(monitorTypeResponse) || monitorTypeResponse != "{}") {
            Log(LogLevel.Error, "Failed to set monitor type for the Player source.");

            return Task.CompletedTask;
        }

        var inputVolumePayload = GenerateSetInputVolumePayload(0);
        var inputVolumeResponse = CPH.ObsSendRaw(inputVolumePayload.RequestType,
                                                 JsonConvert.SerializeObject(inputVolumePayload.RequestData));


        if (string.IsNullOrEmpty(inputVolumeResponse) || inputVolumeResponse != "{}") {
            Log(LogLevel.Warn, "Failed to set volume for the Player source.");

            return Task.CompletedTask;
        }

        var gainFilterPayload = GenerateGainFilterPayload(0);
        var gainFilterResponse = CPH.ObsSendRaw(gainFilterPayload.RequestType,
                                                JsonConvert.SerializeObject(gainFilterPayload.RequestData));

        if (string.IsNullOrEmpty(gainFilterResponse) || gainFilterResponse != "{}") {
            Log(LogLevel.Warn, "Failed to add Gain filter to the Player source.");

            return Task.CompletedTask;
        }

        var compressorFilterPayload = GenerateCompressorFilterPayload();
        var compressorFilterResponse = CPH.ObsSendRaw(compressorFilterPayload.RequestType,
                                                      JsonConvert.SerializeObject(compressorFilterPayload.RequestData));

        if (string.IsNullOrEmpty(compressorFilterResponse) || compressorFilterResponse != "{}") {
            Log(LogLevel.Warn, "Failed to add Compressor filter to the Player source.");

            return Task.CompletedTask;
        }

        Log(LogLevel.Info, "Audio configuration for Player source completed successfully.");

        return Task.CompletedTask;
    }

    private static IPayload GenerateCompressorFilterPayload() {
        return new Payload {
            RequestType = "SetInputSettings",
            RequestData = new {
                inputName = "Player",
                inputSettings = new {
                    threshold = -15.0,
                    ratio = 4.0,
                    attack = 1.0,
                    release = 50.0,
                    makeUpGain = 0.0
                }
            }
        };
    }

    private static IPayload GenerateGainFilterPayload(double gainValue) {
        return new Payload {
            RequestType = "SetInputSettings",
            RequestData = new { inputName = "Player", inputSettings = new { gain = gainValue } }
        };
    }

    private static IPayload GenerateSetInputVolumePayload(double volumeValue) {
        return new Payload {
            RequestType = "SetInputVolume",
            RequestData = new { inputName = "Player", inputSettings = new { volume = volumeValue } }
        };
    }

    private static IPayload GenerateSetAudioMonitorTypePayload(string monitorType) {
        return new Payload {
            RequestType = "SetAudioMonitorType",
            RequestData = new { inputName = "Player", inputSettings = new { monitorType } }
        };
    }

    private int GetSceneItemId(string sceneName, string sourceName) {
        try {
            var requestData = new { sceneName, sourceName, searchOffset = 0 };
            var payload = new Payload { RequestType = "GetSceneItemId", RequestData = requestData };

            var sceneItemId = CPH.ObsSendRaw(payload.RequestType, JsonConvert.SerializeObject(payload.RequestData));

            if (sceneItemId != "Error: No scene items were found in the specified scene by that name or offset.")
                return int.TryParse(sceneItemId, out var result) ? result : -1;

            Log(LogLevel.Warn, $"Failed to retrieve scene item ID for source '{sourceName}' in scene '{sceneName}'.");

            return -1;
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error in GetSceneItemId: {ex.Message}");

            return -1;
        }
    }

    private void AddSceneSource(string targetScene, string sourceName) {
        var payload = new {
            requestType = "CreateSceneItem",
            requestData = new { sceneName = targetScene, sourceName, sceneItemEnabled = true }
        };

        CPH.ObsSendRaw(payload.requestType, JsonConvert.SerializeObject(payload.requestData));
    }

    private bool SourceExistsInScene(string sceneName, string sourceName) {
        var sceneItemId = GetSceneItemId(sceneName, sourceName);

        return sceneItemId != -1;
    }

    private bool SceneExists(string sceneName) {
        try {
            var sceneExists = false;
            var response = JsonConvert.DeserializeObject<dynamic>(CPH.ObsSendRaw("GetSceneList", "{}"));
            var scenes = response?.scenes;

            if (scenes != null)
                foreach (var scene in scenes) {
                    if ((string)scene.name != sceneName) continue;

                    sceneExists = true;

                    break;
                }

            if (!sceneExists) Log(LogLevel.Warn, $"Scene '{sceneName}' does not exist.");

            return sceneExists;
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error in SceneExists: {ex.Message}");

            return false;
        }
    }

    private void CreateScene(string sceneName) {
        try {
            var payload = new { requestType = "CreateScene", requestData = new { sceneName } };

            CPH.ObsSendRaw(payload.requestType, JsonConvert.SerializeObject(payload.requestData));
            Log(LogLevel.Info, $"Scene '{sceneName}' created successfully.");
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error in CreateScene: {ex.Message}");
        }
    }

    private void AddBrowserSource(string sceneName, string sourceName, string url = "about:blank") {
        try {
            var payload = new {
                requestType = "CreateSource", requestData = new { sceneName, sourceName, url, type = "browser_source" }
            };

            var response = CPH.ObsSendRaw(payload.requestType, JsonConvert.SerializeObject(payload.requestData));
            Log(LogLevel.Info,
                $"Browser source '{sourceName}' added to scene '{sceneName}' with URL '{url}'.\nResponse: {response}");
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error in AddBrowserSource: {ex.Message}");
        }
    }

    private static string CreateSourceUrl(string clipUrl) {
        return $"https://player.twitch.tv/?clip={clipUrl}&autoplay=true";
    }

    #endregion

    #region Twitch API Interaction

    private class TwitchApiClient {
        private readonly HttpClient _httpClient;
        private readonly LogDelegate _log;
        private readonly string _clientId;
        private readonly string _authToken;

        public TwitchApiClient(HttpClient httpClient, OAuthInfo oAuthInfo, LogDelegate log) {
            _httpClient = httpClient
                          ?? throw new ArgumentNullException(nameof(httpClient), "HTTP client cannot be null.");
            _httpClient.BaseAddress = new Uri("https://api.twitch.tv/helix/");
            _log = log ?? throw new ArgumentNullException(nameof(log), "Log delegate cannot be null.");
            _clientId = oAuthInfo?.TwitchClientId
                        ?? throw new ArgumentNullException(nameof(oAuthInfo.TwitchClientId),
                                                           "Client ID cannot be null.");
            _authToken = oAuthInfo.TwitchOAuthToken
                         ?? throw new ArgumentNullException(nameof(oAuthInfo.TwitchOAuthToken),
                                                            "OAuth token cannot be null.");

            if (string.IsNullOrWhiteSpace(_authToken))
                throw new InvalidOperationException("Twitch OAuth token is missing or invalid.");
        }

        private void ConfigureHttpRequestHeaders() {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Client-ID", _clientId);
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_authToken}");
        }

        private async Task<string> SendHttpRequestAsync(string endpoint, string completeUrl) {
            try {
                ConfigureHttpRequestHeaders();
                _log(LogLevel.Debug, "HTTP headers set successfully. Initiating request...");
                var response = await _httpClient.GetAsync(endpoint);
                _log(LogLevel.Debug, $"Received response: {response.StatusCode} ({(int)response.StatusCode})");

                if (response.IsSuccessStatusCode) return await response.Content.ReadAsStringAsync();

                _log(LogLevel.Error,
                     $"Request to Twitch API failed: {response.ReasonPhrase} "
                     + $"(Status Code: {(int)response.StatusCode}, URL: {completeUrl})");

                return null;
            } catch (HttpRequestException ex) {
                _log(LogLevel.Error, $"HTTP request error while calling {completeUrl}: {ex.Message}");

                return null;
            }
        }

        public async Task<T> FetchDataAsync<T>(string endpoint) {
            if (string.IsNullOrWhiteSpace(endpoint))
                throw new ArgumentNullException(nameof(endpoint), "Endpoint cannot be null or empty.");
            if (string.IsNullOrWhiteSpace(_authToken))
                throw new InvalidOperationException("Twitch OAuth token is missing or invalid.");

            var completeUrl = new Uri(_httpClient.BaseAddress, endpoint).ToString();
            _log(LogLevel.Debug, $"Preparing to make GET request to endpoint: {completeUrl}");

            try {
                var content = await SendHttpRequestAsync(endpoint, completeUrl);

                if (string.IsNullOrWhiteSpace(content)) return default;

                _log(LogLevel.Debug, $"Response content: {content}");
                var apiResponse = JsonConvert.DeserializeObject<TwitchApiResponse<T>>(content);

                if (apiResponse?.Data != null && apiResponse.Data.Length > 0) {
                    _log(LogLevel.Info, "Successfully retrieved and deserialized data from the Twitch API.");

                    return apiResponse.Data[0];
                }

                _log(LogLevel.Warn, $"No data returned from the Twitch API endpoint: {completeUrl}");

                return default;
            } catch (JsonException ex) {
                _log(LogLevel.Error, $"JSON deserialization error for response from {completeUrl}: {ex.Message}");

                return default;
            } catch (Exception ex) {
                _log(LogLevel.Error, $"Unexpected error in {nameof(FetchDataAsync)}: {ex.Message}");

                return default;
            } finally {
                _log(LogLevel.Debug, $"{nameof(FetchDataAsync)} execution complete for endpoint.");
            }
        }

        public Task<ClipData> FetchClipById(string clipId) {
            return FetchDataAsync<ClipData>($"clips?id={clipId}");
        }

        public Task<GameInfo> FetchGameById(string gameId) {
            return FetchDataAsync<GameInfo>($"games?id={gameId}");
        }
    }

    private class OAuthInfo {
        public OAuthInfo(string twitchClientId, string twitchOAuthToken) {
            TwitchClientId = twitchClientId
                             ?? throw new ArgumentNullException(nameof(twitchClientId), "Client ID cannot be null.");
            TwitchOAuthToken = twitchOAuthToken
                               ?? throw new ArgumentNullException(nameof(twitchOAuthToken),
                                                                  "OAuth token cannot be null.");
        }

        public string TwitchClientId { get; }
        public string TwitchOAuthToken { get; }
    }

    public class TwitchApiResponse<T> {
        public TwitchApiResponse(T[] data) {
            Data = data ?? Array.Empty<T>();
        }

        public T[] Data { get; }
    }

    public class GameData {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
    }

    private async Task<string> FetchGameNameAsync(string gameId) {
        if (string.IsNullOrWhiteSpace(gameId)) {
            Log(LogLevel.Warn, "Game ID is empty or null. Returning 'Unknown Game'.");

            return "Unknown Game";
        }

        try {
            var gameData = await _twitchApiClient.FetchGameById(gameId);

            return string.IsNullOrWhiteSpace(gameData?.Name) ? "Unknown Game" : gameData.Name;
        } catch (Exception ex) {
            Log(LogLevel.Error, $"{CreateErrorPreamble()}: {ex.Message}");

            return "Unknown Game";
        }
    }

    private static (string StreamerName, string ClipTitle, string CuratorName) ExtractClipInfo(ClipData clipData) {
        return GetClipInfo(clipData);
    }

    #endregion

    #region Server & Local Hosting

    private static readonly SemaphoreSlim ServerLockSemaphore = new SemaphoreSlim(1, 1);
    private static readonly SemaphoreSlim ServerSemaphore = new SemaphoreSlim(1, 1);
    private static readonly SemaphoreSlim TokenSemaphore = new SemaphoreSlim(1, 1);

    private async Task<bool> ConfigureAndServe() {
        try {
            await CleanupServer();
            ValidatePortAvailability(8080);

            if (!await ExecuteWithSemaphore(ServerSemaphore, nameof(ServerSemaphore), SetupServerAndTokens))
                return false;
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Configuration failed: {ex.Message}");
            await CleanupServer();

            return false;
        }

        return true;
    }

    private async Task SetupServerAndTokens() {
        using (new ScopedSemaphore(ServerLockSemaphore, Log)) {
            InitializeServer();

            if (!await SetupTokenSemaphore()) return;

            _listeningTask = StartListening(_server, _cancellationTokenSource.Token);
            ConfigureBrowserSource("Cliparino", "Player", "http://localhost:8080/index.htm");
        }
    }

    private void ValidatePortAvailability(int port) {
        if (!IsPortAvailable(port)) throw new InvalidOperationException($"Port {port} is already in use.");
    }

    private void InitializeServer() {
        if (_server != null) return;

        _server = new HttpListener();
        _server.Prefixes.Add("http://localhost:8080/");
        _server.Start();
        Log(LogLevel.Info, "Server initialized.");
    }

    private async Task<bool> SetupTokenSemaphore() {
        return await ExecuteWithSemaphore(TokenSemaphore,
                                          nameof(TokenSemaphore),
                                          () =>
                                              Task.FromResult(_cancellationTokenSource =
                                                                  new CancellationTokenSource()));
    }

    private void ConfigureBrowserSource(string name, string player, string url) {
        UpdateBrowserSource(name, player, url);
        RefreshBrowserSource();
        Log(LogLevel.Info, "Browser source configured.");
    }

    #endregion

    #region Request Handling

    private const string HTMLErrorPage = "<h1>Error Generating HTML Content</h1>";
    private const string NotFoundResponse = "404 Not Found";

    private async Task StartListening(HttpListener server, CancellationToken cancellationToken) {
        Log(LogLevel.Info, "HTTP server started on http://localhost:8080");

        while (server.IsListening && !cancellationToken.IsCancellationRequested) {
            HttpListenerContext context = null;

            try {
                context = await server.GetContextAsync();
                await HandleRequest(context);
            } catch (HttpListenerException ex) {
                Log(LogLevel.Warn, $"Listener exception: {ex.Message}");
            } catch (Exception ex) {
                Log(LogLevel.Error, $"Unexpected error: {ex.Message}\n{ex.StackTrace}");
            } finally {
                context?.Response.Close();
            }
        }
    }

    private async Task HandleRequest(HttpListenerContext context) {
        try {
            var requestPath = context.Request.Url?.AbsolutePath ?? string.Empty;
            var nonce = ApplyCORSHeaders(context.Response);

            string responseText;
            string contentType;
            HttpStatusCode statusCode;

            switch (requestPath) {
                case "/index.css":
                    (responseText, contentType, statusCode) = (CSSText, "text/css; charset=utf-8", HttpStatusCode.OK);

                    break;
                case "/":
                case "/index.htm":
                    (responseText, contentType, statusCode) = (GetHtmlInMemorySafe().Replace("[[nonce]]", nonce),
                                                               "text/html; charset=utf-8", HttpStatusCode.OK);

                    break;
                default:
                    (responseText, contentType, statusCode) =
                        (NotFoundResponse, "text/plain; charset=utf-8", HttpStatusCode.NotFound);

                    break;
            }

            context.Response.StatusCode = (int)statusCode;

            await WriteResponse(responseText, context.Response, contentType);
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error handling request: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private async Task WriteResponse(string responseText, HttpListenerResponse response, string contentType) {
        try {
            response.ContentType = contentType;

            var buffer = Encoding.UTF8.GetBytes(responseText);

            response.ContentLength64 = buffer.Length;

            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error writing response: {ex.Message}");
        }
    }

    private async Task CreateAndHostClipPageAsync(string clipUrl,
                                                  string streamerName,
                                                  string clipTitle,
                                                  string curatorName,
                                                  ClipData clipData) {
        Log(LogLevel.Debug,
            $"{nameof(CreateAndHostClipPageAsync)} called with parameters: {JsonConvert.SerializeObject(new { clipUrl, streamerName, clipTitle, curatorName, clipData })}");

        try {
            if (string.IsNullOrWhiteSpace(clipUrl)) {
                Log(LogLevel.Error, "clipUrl cannot be null or empty. Ensure it is passed correctly.");

                return;
            }

            var clipId = _clipManager.ExtractClipId(clipUrl);
            var gameName = await FetchGameNameAsync(clipData.GameId);

            _htmlInMemory = GenerateHtmlContent(clipId, streamerName, gameName, clipTitle, curatorName);

            Log(LogLevel.Debug, $"Generated HTML content stored in memory: {_htmlInMemory}");

            var isConfigured = await ConfigureAndServe();

            if (isConfigured)
                Log(LogLevel.Info, "Server configured and ready to serve HTML content.");
            else
                Log(LogLevel.Error, "Failed to configure server.");
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error occurred in {nameof(CreateAndHostClipPageAsync)}: {ex.Message}");
            Log(LogLevel.Debug, ex.StackTrace);
        } finally {
            Log(LogLevel.Debug, $"{nameof(CreateAndHostClipPageAsync)} execution finished.");
        }
    }

    private string GenerateHtmlContent(string clipId,
                                       string streamerName,
                                       string gameName,
                                       string clipTitle,
                                       string curatorName) {
        const string defaultClipID = "SoftHilariousNigiriPipeHype-2itiu1ZeL77SAPRM";
        var safeClipId = clipId ?? defaultClipID;
        var safeStreamerName = WebUtility.HtmlEncode(streamerName) ?? "Unknown Streamer";
        var safeGameName = WebUtility.HtmlEncode(gameName) ?? "Unknown Game";
        var safeClipTitle = WebUtility.HtmlEncode(clipTitle) ?? "Untitled Clip";
        var safeCuratorName = WebUtility.HtmlEncode(curatorName) ?? "Anonymous";

        Log(LogLevel.Debug, $"Generating HTML content for clip ID: {safeClipId}");

        return HTMLText.Replace("[[clipId]]", safeClipId)
                       .Replace("[[streamerName]]", safeStreamerName)
                       .Replace("[[gameName]]", safeGameName)
                       .Replace("[[clipTitle]]", safeClipTitle)
                       .Replace("[[curatorName]]", safeCuratorName);
    }

    private string GetHtmlInMemorySafe() {
        Log(LogLevel.Debug, "Attempting to retrieve HTML template from memory.");

        using (new ScopedSemaphore(ServerSemaphore, Log)) {
            if (string.IsNullOrWhiteSpace(_htmlInMemory)) {
                Log(LogLevel.Warn, "_htmlInMemory is null or empty. Returning default error response.");

                return HTMLErrorPage;
            }

            Log(LogLevel.Info, "Successfully retrieved HTML template from memory.");

            return _htmlInMemory;
        }
    }

    #endregion

    #region Cleanup

    private async Task CleanupServer(HttpListener server = null) {
        Log(LogLevel.Debug, "Entering CleanupServer.");

        using (new ScopedSemaphore(ServerSemaphore, Log)) {
            try {
                await CancelAllOperationsAsync();
                var serverInstance = TakeServerInstance(server);
                await CleanupListeningTaskAsync();
                StopAndDisposeServer(serverInstance);
            } catch (Exception ex) {
                Log(LogLevel.Error, $"Unexpected error during CleanupServer: {ex.Message}");
            }
        }

        Log(LogLevel.Debug, "Exiting CleanupServer.");
    }

    private async Task CancelAllOperationsAsync() {
        if (_cancellationTokenSource == null) {
            Log(LogLevel.Debug, "Cancellation token source is already null.");

            return;
        }

        using (await ScopedSemaphore.WaitAsync(TokenSemaphore, Log)) {
            try {
                _cancellationTokenSource.Cancel();
                Log(LogLevel.Debug, "All ongoing operations canceled.");
            } catch (Exception ex) {
                Log(LogLevel.Error, $"Error while canceling operations: {ex.Message}");
            }
        }
    }

    private HttpListener TakeServerInstance(HttpListener server) {
        lock (ServerLock) {
            if (_server == null && server == null) Log(LogLevel.Warn, "No server instance available for cleanup.");

            var instance = server ?? _server;

            // Ensure the server is nullified regardless of whether it was passed or taken.
            _server = null;

            return instance;
        }
    }

    private async Task CleanupListeningTaskAsync() {
        if (_listeningTask == null) {
            Log(LogLevel.Debug, "No listening task to cleanup.");

            return;
        }

        Log(LogLevel.Debug, "Cleaning up listening task.");

        try {
            await _listeningTask;
        } catch (OperationCanceledException) {
            Log(LogLevel.Info, "Listening task gracefully canceled.");
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error cleaning up listening task: {ex.Message}");
        } finally {
            _listeningTask = null;
        }
    }

    private void StopAndDisposeServer(HttpListener serverInstance) {
        if (serverInstance == null) {
            Log(LogLevel.Info, "No server instance to stop or dispose.");

            return;
        }

        try {
            serverInstance.Stop();
            serverInstance.Close();
            Log(LogLevel.Info, "Server successfully stopped and disposed.");
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error stopping or disposing server: {ex.Message}");
        }
    }

    #endregion

    #region Utilities

    private const int NonceLength = 16;
    private const string MessagePrefix = "Cliparino :: ";

    private class ScopedSemaphore : IDisposable {
        private readonly SemaphoreSlim _semaphore;
        private readonly LogDelegate _log;
        private bool _hasLock;

        public ScopedSemaphore(SemaphoreSlim semaphore, LogDelegate log) {
            _semaphore = semaphore ?? throw new ArgumentNullException(nameof(semaphore));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _semaphore.Wait();
            _hasLock = true;
        }

        public static async Task<ScopedSemaphore> WaitAsync(SemaphoreSlim semaphore, LogDelegate log) {
            if (semaphore == null) throw new ArgumentNullException(nameof(semaphore));

            if (log == null) throw new ArgumentNullException(nameof(log));

            var scopedSemaphore = new ScopedSemaphore(semaphore, log) { _hasLock = false };

            await semaphore.WaitAsync();

            scopedSemaphore._hasLock = true;

            return scopedSemaphore;
        }

        public void Dispose() {
            if (!_hasLock) return;

            try {
                _semaphore?.Release();
            } catch (ObjectDisposedException) {
                _log(LogLevel.Warn, "Semaphore has been disposed.");
            } catch (SemaphoreFullException) {
                _log(LogLevel.Warn, "Semaphore full exception occurred during release.");
            } catch (Exception ex) {
                _log(LogLevel.Error, $"Unexpected exception while releasing semaphore: {ex.Message}");
            }
        }
    }

    private async Task<bool> TryAcquireSemaphore(SemaphoreSlim semaphore, string name, int timeout = 10) {
        Log(LogLevel.Debug, $"Attempting to acquire semaphore '{name}' with a timeout of {timeout} seconds...");

        try {
            if (await semaphore.WaitAsync(TimeSpan.FromSeconds(timeout))) {
                Log(LogLevel.Debug, $"Semaphore '{name}' successfully acquired.");

                return true;
            } else {
                Log(LogLevel.Warn, $"Semaphore '{name}' acquisition timed out after {timeout} seconds.");

                return false;
            }
        } catch (Exception ex) {
            Log(LogLevel.Error, $"An exception occurred while attempting to acquire semaphore '{name}': {ex.Message}");

            return false;
        } finally {
            // No additional cleanup is performed here since releasing is handled explicitly elsewhere in the code.
            // This ensures that the semaphore doesn't get released prematurely or incorrectly.
            Log(LogLevel.Debug, $"Exiting {nameof(TryAcquireSemaphore)} for semaphore '{name}'.");
        }
    }

    /// <summary>
    ///     Executes the given action within a semaphore, ensuring thread-safe behavior.
    /// </summary>
    private async Task<bool> ExecuteWithSemaphore(SemaphoreSlim semaphore, string name, Func<Task> action) {
        if (!await TryAcquireSemaphore(semaphore, name)) return false;

        try {
            await action();

            return true;
        } finally {
            semaphore.Release();
        }
    }

    private void Log(LogLevel level, string messageBody, [CallerMemberName] string caller = "") {
        if (caller == null) throw new ArgumentNullException(nameof(caller));

        if (!_loggingEnabled && level != LogLevel.Error) return;

        var message = $"{MessagePrefix}{caller} :: {messageBody}";

        switch (level) {
            case LogLevel.Debug: CPH.LogDebug(message); break;
            case LogLevel.Info: CPH.LogInfo(message); break;
            case LogLevel.Warn: CPH.LogWarn(message); break;
            case LogLevel.Error: CPH.LogError(message); break;
            default: throw new ArgumentOutOfRangeException(nameof(level), level, null);
        }
    }

    private delegate void LogDelegate(LogLevel level, string messageBody, [CallerMemberName] string caller = "");

    private enum LogLevel {
        Debug,
        Info,
        Warn,
        Error
    }

    private bool TryGetCommand(out string command) {
        return CPH.TryGetArg("command", out command);
    }

    private static string SanitizeUsername(string user) {
        return string.IsNullOrWhiteSpace(user) ? null : user.Trim().TrimStart('@').ToLowerInvariant();
    }

    private TwitchUserInfoEx FetchExtendedUserInfo(string user) {
        var extendedUserInfo = CPH.TwitchGetExtendedUserInfoByLogin(user);

        if (extendedUserInfo == null) {
            Log(LogLevel.Warn, $"No extended user info found for: {user}");

            return null;
        }

        Log(LogLevel.Debug, $"Fetched extended user info: {JsonConvert.SerializeObject(extendedUserInfo)}");

        return extendedUserInfo;
    }

    private string GetShoutoutMessageTemplate() {
        var messageTemplate = GetArgument("message", string.Empty);

        if (string.IsNullOrWhiteSpace(messageTemplate)) {
            Log(LogLevel.Warn, "Message template is missing or empty. Using default template.");
            messageTemplate =
                "Check out [[userName]], they were last streaming [[userGame]] on https://twitch.tv/[[userName]]";
        }

        Log(LogLevel.Debug, $"Using shoutout template: {messageTemplate}");

        return messageTemplate;
    }

    private Clip TryFetchClip(string userId) {
        var clipSettings = new ClipSettings(GetArgument("featuredOnly", false),
                                            GetArgument("maxClipSeconds", 30),
                                            GetArgument("clipAgeDays", 30));
        var clip = GetRandomClip(userId, clipSettings);

        if (clip == null) Log(LogLevel.Warn, $"No clips found for user with ID: {userId}");

        return clip;
    }

    private async Task HandleShoutoutMessageAsync(TwitchUserInfoEx userInfo, string template, Clip clip) {
        var shoutoutMessage = GetShoutoutMessage(userInfo, template, clip);

        Log(LogLevel.Info, $"Sending shoutout message to chat: {shoutoutMessage}");

        if (clip != null)
            await ProcessAndHostClipDataAsync(clip.Url, clip.ToClipData(CPH));
        else
            CPH.SendMessage($"It looks like there aren't any clips for {userInfo.UserName}... yet. "
                            + "Give them a follow and catch some clips next time they go live!");

        CPH.SendMessage(shoutoutMessage);
    }

    private static string GetShoutoutMessage(TwitchUserInfoEx userInfo, string template, Clip clip) {
        var displayName = userInfo.UserName ?? userInfo.UserLogin;
        var lastGame = userInfo.Game ?? "nothing yet";

        if (clip == null)
            return string.IsNullOrWhiteSpace(userInfo.Game)
                       ? $"Looks like @{displayName} hasn't streamed anything yet, but you might want to give that follow button a tickle anyway, just in case!"
                       : $"Make sure to go check out @{displayName}! They were last streaming {lastGame} over at https://twitch.tv/{displayName}";

        return template.Replace("[[userName]]", displayName).Replace("[[userGame]]", lastGame);
    }

    private (int Width, int Height) ValidateDimensions(int width, int height) {
        if (width > 0 && height > 0) return (width, height);

        Log(LogLevel.Warn, "Invalid width or height provided. Falling back to default values.");

        return (DefaultWidth, DefaultHeight);
    }

    private static string CreateNonce() {
        var guidBytes = Guid.NewGuid().ToByteArray();
        var base64Nonce = Convert.ToBase64String(guidBytes);

        return SanitizeNonce(base64Nonce).Substring(0, NonceLength);
    }

    private static string SanitizeNonce(string nonce) {
        return nonce.Replace("+", "-").Replace("/", "_").Replace("=", "_");
    }

    private static string ApplyCORSHeaders(HttpListenerResponse response) {
        var nonce = CreateNonce();

        foreach (var header in CORSHeaders)
            response.Headers[header.Key] = header.Value
                                                 .Replace("[[nonce]]", nonce)
                                                 .Replace("\r", "")
                                                 .Replace("\n", " ");

        return nonce;
    }

    private bool IsPortAvailable(int port) {
        Log(LogLevel.Debug, $"Starting {nameof(IsPortAvailable)} check for port {port}.");

        try {
            Log(LogLevel.Debug, $"Invoking {nameof(IdentifyPortConflict)} for port {port}.");
            IdentifyPortConflict(port);

            var listener = new TcpListener(IPAddress.Loopback, port);

            Log(LogLevel.Debug, $"Created TcpListener for port {port}");
            listener.Start();
            Log(LogLevel.Info, $"Successfully started TcpListener on port {port}. Port is available.");
            listener.Stop();

            return true;
        } catch (SocketException ex) {
            Log(LogLevel.Warn, $"Port {port} is not available. Details: {ex.Message}");

            return false;
        } catch (Exception ex) {
            Log(LogLevel.Error, $"An unexpected error occurred while checking port {port}: {ex.Message}");

            return false;
        } finally {
            Log(LogLevel.Debug, $"Exiting {nameof(IsPortAvailable)} for port {port}.");
        }
    }

    private void IdentifyPortConflict(int port) {
        var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
        var activeTcpListeners = ipGlobalProperties.GetActiveTcpListeners();
        var activeTcpConnections = ipGlobalProperties.GetActiveTcpConnections();

        var isPortInUse = activeTcpListeners.Any(endpoint => endpoint.Port == port)
                          || activeTcpConnections.Any(connection => connection.LocalEndPoint.Port == port);

        Log(isPortInUse ? LogLevel.Warn : LogLevel.Info,
            isPortInUse
                ? $"Conflict detected on port {port}. This port is currently in use. Check active processes or services."
                : $"Port {port} is currently free and usable.");
    }

    private void CancelCurrentToken() {
        var tokenSource = _autoStopCancellationTokenSource;

        tokenSource?.Cancel();
        tokenSource?.Dispose();
        _autoStopCancellationTokenSource = new CancellationTokenSource();
    }

    private static string CreateErrorPreamble([CallerMemberName] string caller = "") {
        return $"An error occurred in {caller}";
    }

    private async Task HostClipWithDetailsAsync(string clipUrl, ClipData clipData) {
        Log(LogLevel.Debug,
            $"{nameof(HostClipWithDetailsAsync)} called with clipUrl: {clipUrl}, clipData: {JsonConvert.SerializeObject(clipData)}");

        try {
            clipUrl = GetValueOrDefault(clipUrl, clipData?.Url);

            if (string.IsNullOrWhiteSpace(clipUrl)) {
                Log(LogLevel.Error, "clipUrl could not be resolved. Aborting.");

                return;
            }

            SetLastClipUrl(clipUrl);

            await PrepareSceneForClipHostingAsync();

            Log(LogLevel.Info, "Extracting clip info from ClipData.");

            var clipInfo = ExtractClipInfo(clipData);

            Log(LogLevel.Info, "Creating and hosting clip page with details.");

            await CreateAndHostClipPageAsync(clipUrl,
                                             clipInfo.StreamerName,
                                             clipInfo.ClipTitle,
                                             clipInfo.CuratorName,
                                             clipData);

            Log(LogLevel.Debug, "Starting auto-stop task for playback.");

            if (clipData != null) await StartAutoStopTaskAsync(clipData.Duration);
        } catch (Exception ex) {
            Log(LogLevel.Error, $"{CreateErrorPreamble()}: {ex.Message}");
        } finally {
            Log(LogLevel.Debug, $"{nameof(HostClipWithDetailsAsync)} exiting.");
        }
    }

    /// <summary>
    ///     Retrieves the specified value, or provides a fallback when the value is null or empty.
    /// </summary>
    private static T GetValueOrDefault<T>(T value, T defaultValue = default) {
        if (value is string stringValue) return !string.IsNullOrEmpty(stringValue) ? value : defaultValue;

        return value != null ? value : defaultValue;
    }

    private async Task StartAutoStopTaskAsync(double duration) {
        try {
            CancelCurrentToken();

            using (var cancellationTokenSource = _autoStopCancellationTokenSource) {
                await Task.Delay(TimeSpan.FromSeconds(GetDurationWithSetupDelay((float)duration).TotalSeconds),
                                 cancellationTokenSource.Token);

                if (!cancellationTokenSource.Token.IsCancellationRequested) {
                    HandleStopCommand();
                    Log(LogLevel.Info, "Auto-stop task completed successfully.");
                }
            }
        } catch (OperationCanceledException) {
            Log(LogLevel.Info, "Auto-stop task cancelled gracefully.");
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Unexpected error in auto-stop task: {ex.Message}");
        }
    }

    private interface IPayload {
        string RequestType { get; }
        object RequestData { get; }
    }

    private class Payload : IPayload {
        public string RequestType { get; set; }
        public object RequestData { get; set; }
    }

    #endregion
}