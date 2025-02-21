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

    private const string ConstClipDataError = "Unable to retrieve clip data.";

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

    private const string HTMLErrorPage = "<h1>Error Generating HTML Content</h1>";
    private const string MessagePrefix = "Cliparino :: ";

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

    private static readonly SemaphoreSlim ServerLockSemaphore = new SemaphoreSlim(1, 1);
    private static readonly SemaphoreSlim ServerSemaphore = new SemaphoreSlim(1, 1);
    private static readonly SemaphoreSlim TokenSemaphore = new SemaphoreSlim(1, 1);

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

    private bool TryGetCommand(out string command) {
        return CPH.TryGetArg("command", out command);
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
            if (string.IsNullOrWhiteSpace(user)) {
                Log(LogLevel.Warn, "No user provided for shoutout.");

                return;
            }

            user = user.Trim().TrimStart('@').ToLowerInvariant();
            Log(LogLevel.Debug, $"Sanitized and normalized username for shoutout: {user}");

            var extendedUserInfo = CPH.TwitchGetExtendedUserInfoByLogin(user);

            if (extendedUserInfo == null) {
                Log(LogLevel.Warn, $"No extended user info found for: {user}");

                return;
            }

            Log(LogLevel.Debug, $"Fetched extended user info: {JsonConvert.SerializeObject(extendedUserInfo)}");

            var messageTemplate = GetArgument("message", string.Empty);

            if (string.IsNullOrWhiteSpace(messageTemplate)) {
                Log(LogLevel.Warn, "Message template is missing or empty. Using default template.");
                messageTemplate =
                    "Check out [[userName]], they were last streaming [[userGame]] on https://twitch.tv/[[userName]]";
            }

            Log(LogLevel.Debug, $"Using shoutout template: {messageTemplate}");
            var clipSettings = new ClipSettings(GetArgument("featuredOnly", false),
                                                GetArgument("maxClipSeconds", 30),
                                                GetArgument("clipAgeDays", 30));
            var clip = GetRandomClip(extendedUserInfo.UserId, clipSettings);

            if (clip != null) {
                Log(LogLevel.Info, $"Selected clip for shoutout: {clip.Url}");

                await ProcessAndHostClipDataAsync(clip.Url, clip.ToClipData(CPH));
            } else {
                Log(LogLevel.Warn, $"No clips found for user: {extendedUserInfo.UserName}");

                var noClipMessage = $"It looks like there aren't any clips for {extendedUserInfo.UserName}... yet. "
                                    + "Give them a follow and catch some clips next time they go live!";
                CPH.SendMessage(noClipMessage);
            }

            var displayName = extendedUserInfo.UserName ?? user;
            var lastGame = extendedUserInfo.Game;

            string shoutoutMessage;

            if (clip == null) {
                if (string.IsNullOrWhiteSpace(lastGame))
                    shoutoutMessage = $"Looks like @{displayName} hasn't streamed anything yet, "
                                      + "but you might want to give that follow button a tickle anyway, just in case!";
                else
                    shoutoutMessage = $"Make sure to go check out @{displayName}! "
                                      + $"They were last streaming {lastGame} over at https://twitch.tv/{displayName}";
            } else {
                if (string.IsNullOrWhiteSpace(lastGame)) {
                    Log(LogLevel.Info, $"No last game found for {displayName}, adjusting placeholders.");
                    lastGame = "nothing yet";
                }

                shoutoutMessage = messageTemplate.Replace("[[userName]]", displayName)
                                                 .Replace("[[userGame]]", lastGame);
            }

            Log(LogLevel.Info, $"Sending shoutout message to chat: {shoutoutMessage}");
            CPH.SendMessage(shoutoutMessage);
        } catch (Exception ex) {
            Log(LogLevel.Error, ex.Message);
        }
    }

    private async Task HandleWatchCommandAsync(string url, int width, int height) {
        Log(LogLevel.Debug,
            $"{nameof(HandleWatchCommandAsync)} called with arguments: url='{url}', width={width}, height={height}");

        try {
            if (width <= 0 || height <= 0) {
                Log(LogLevel.Warn, "Invalid width or height provided. Falling back to default values.");
                width = DefaultWidth;
                height = DefaultHeight;
            }

            url = ValidateClipUrl(url, null);

            if (string.IsNullOrWhiteSpace(url)) {
                Log(LogLevel.Warn, "No valid clip URL provided. Aborting command.");

                return;
            }

            var clipData = await _clipManager.GetClipData(url);

            if (clipData == null) {
                Log(LogLevel.Error, "Failed to retrieve clip data.");

                return;
            }

            Log(LogLevel.Info, $"Now playing: {clipData.Title} clipped by {clipData.CreatorName}");

            LogBrowserSourceSetup(url, width, height);

            var currentScene = CPH.ObsGetCurrentScene();

            if (string.IsNullOrWhiteSpace(currentScene)) {
                Log(LogLevel.Warn, "Unable to determine the current OBS scene. Aborting clip setup.");

                return;
            }

            await PrepareSceneForClipHostingAsync(currentScene);

            await ProcessAndHostClipDataAsync(url, clipData);
        } catch (Exception ex) {
            Log(LogLevel.Error, ex.Message);
        }
    }

    private async Task HandleReplayCommandAsync(int width, int height) {
        Log(LogLevel.Debug, $"{nameof(HandleReplayCommandAsync)} called with width: {width}, height: {height}");

        try {
            var lastClipUrl = GetLastClipUrl();

            if (string.IsNullOrWhiteSpace(lastClipUrl)) return;

            await ProcessAndHostClipDataAsync(lastClipUrl, null);
        } catch (Exception ex) {
            Log(LogLevel.Error, ex.Message);
        }
    }

    private void HandleStopCommand() {
        Log(LogLevel.Debug, $"{nameof(HandleStopCommand)} called, setting browser source page to blank layout.");

        CancelCurrentToken();
        Log(LogLevel.Info, "Cancelled ongoing auto-stop task.");

        var currentScene = CPH.ObsGetCurrentScene();
        const string cliparinoSourceName = "Cliparino";

        EnsureSourceExistsAndIsVisible(currentScene, cliparinoSourceName, false);

        SetBrowserSource("about:blank");
    }

    #endregion

    #region Clip Management

    private Clip GetRandomClip(string userId, ClipSettings clipSettings) {
        var (featuredOnly, maxClipSeconds, clipAgeDays) =
            (clipSettings.FeaturedOnly, clipSettings.MaxClipSeconds, clipSettings.ClipAgeDays);

        Log(LogLevel.Debug,
            $"Getting random clip for userId: {userId}, featuredOnly: {featuredOnly}, maxSeconds: {maxClipSeconds}, ageDays: {clipAgeDays}");

        try {
            var twitchUser = FetchTwitchUser(userId);

            if (twitchUser == null) return LogAndReturnNull($"Twitch user not found for userId: {userId}");

            var validPeriods = new[] { 1, 7, 30, 365, 36500 };

            foreach (var period in validPeriods.Where(p => p >= clipAgeDays)) {
                var clips = RetrieveClips(userId, period).ToList();

                if (!clips.Any()) {
                    Log(LogLevel.Debug, $"No clips found for period: {period} days");

                    continue;
                }

                var matchingClips = FilterClips(clips, featuredOnly, maxClipSeconds);

                if (matchingClips.Any()) {
                    var selectedClip = SelectRandomClip(matchingClips);
                    Log(LogLevel.Debug, $"Selected clip: {selectedClip.Url}");

                    return Clip.FromTwitchClip(selectedClip);
                }

                Log(LogLevel.Debug,
                    $"No matching clips found for userId: {userId} with period: {period} days and featuredOnly: {featuredOnly}");

                if (!featuredOnly) continue;

                matchingClips = FilterClips(clips, false, maxClipSeconds);

                if (matchingClips.Any()) {
                    var selectedClip = SelectRandomClip(matchingClips);
                    Log(LogLevel.Debug, $"Selected clip without featuredOnly: {selectedClip.Url}");

                    return Clip.FromTwitchClip(selectedClip);
                }

                Log(LogLevel.Debug,
                    $"No matching clips found without featuredOnly for userId: {userId} in period: {period} days");
            }

            return
                LogAndReturnNull($"No clips found for userId: {userId} after exhausting all periods and filter combinations.");
        } catch (Exception ex) {
            Log(LogLevel.Error, $"{CreateErrorPreamble("GetRandomClip")}: {ex.Message}");

            return null;
        }
    }

    private dynamic FetchTwitchUser(string userId) {
        Log(LogLevel.Debug, $"FetchTwitchUser called with userId: {userId}");

        try {
            var twitchUser = CPH.TwitchGetExtendedUserInfoById(userId);

            if (twitchUser != null) {
                Log(LogLevel.Info, $"Successfully fetched Twitch user with userId: {userId}");
                Log(LogLevel.Debug, $"Exiting {nameof(FetchTwitchUser)}.");

                return twitchUser;
            }

            Log(LogLevel.Warn, $"Could not find Twitch userId: {userId}");

            return null;
        } catch (Exception ex) {
            Log(LogLevel.Error, $"{CreateErrorPreamble("FetchTwitchUser")}: {ex.Message}");

            return null;
        } finally {
            Log(LogLevel.Debug, $"Finally block executed for {nameof(FetchTwitchUser)}.");
        }
    }

    private IEnumerable<ClipData> RetrieveClips(string userId, int clipAgeDays) {
        Log(LogLevel.Debug, $"RetrieveClips called with userId: {userId}, clipAgeDays: {clipAgeDays}");

        try {
            var endDate = DateTime.UtcNow;
            var startDate = endDate.AddDays(-clipAgeDays);

            return CPH.GetClipsForUserById(userId, startDate, endDate);
        } catch (Exception ex) {
            Log(LogLevel.Error, $"{CreateErrorPreamble("RetrieveClips")}: {ex.Message}");

            return null;
        }
    }

    private List<ClipData> FilterClips(IEnumerable<ClipData> clips, bool featuredOnly, int maxSeconds) {
        return clips.Where(c => FilteredByCriteria(c, featuredOnly, maxSeconds)).ToList();
    }

    private bool FilteredByCriteria(ClipData clip, bool featuredOnly, int maxSeconds) {
        Log(LogLevel.Debug,
            $"FilteredByCriteria called with clip: {clip.Id}, featuredOnly: {featuredOnly}, maxSeconds: {maxSeconds}");

        try {
            return (!featuredOnly || clip.IsFeatured) && clip.Duration <= maxSeconds;
        } catch (Exception ex) {
            Log(LogLevel.Error, $"{CreateErrorPreamble("FilteredByCriteria")}: {ex.Message}");

            return false;
        }
    }

    private static ClipData SelectRandomClip(List<ClipData> clips) {
        return clips[new Random().Next(clips.Count)];
    }

    private async Task<ClipData> FetchValidClipDataWithCache(ClipData clipData, string clipUrl) {
        Log(LogLevel.Debug,
            $"FetchValidClipDataWithCache called with clipData: {JsonConvert.SerializeObject(clipData)}, clipUrl: {clipUrl}");

        lock (_clipDataCache) {
            var isClipCached = _clipDataCache.TryGetValue(clipUrl, out var cachedClipData);
            var clipId = clipData?.Id;

            if (isClipCached) return cachedClipData;

            _clipDataCache.Add(clipId ?? string.Empty, clipData);
        }

        if (clipData == null) {
            Log(LogLevel.Warn, "clipData is null. Attempting to fetch clip data using clipUrl.");

            if (string.IsNullOrWhiteSpace(clipUrl) || !clipUrl.Contains("twitch.tv")) {
                Log(LogLevel.Error, $"Invalid clip URL provided: {clipUrl}");

                return null;
            }

            clipData = await _clipManager.GetClipData(clipUrl);

            var clipId = clipData?.Id;

            if (string.IsNullOrWhiteSpace(clipId)) {
                Log(LogLevel.Error, $"Invalid clip ID extracted from URL: {clipUrl}");

                return null;
            }

            clipData = await _clipManager.GetClipDataById(clipId);

            if (clipData == null) {
                Log(LogLevel.Error, $"{ConstClipDataError} for clip ID: {clipId}");

                return null;
            }
        }

        if (string.IsNullOrWhiteSpace(clipData.Id) || string.IsNullOrWhiteSpace(clipData.Url))
            Log(LogLevel.Error, "ClipData validation failed. Missing essential fields (ID or URL).");

        Log(LogLevel.Info, $"Successfully fetched clip data for clip ID: {clipData.Id}");

        clipData = await FetchClipDataIfNeeded(clipData, clipUrl);

        return clipData;
    }

    private string ValidateClipUrl(string clipUrl, ClipData clipData) {
        if (!string.IsNullOrWhiteSpace(clipUrl)) return clipUrl;

        clipUrl = clipData?.Url;

        if (!string.IsNullOrWhiteSpace(clipUrl)) return clipUrl;

        Log(LogLevel.Error, "clipUrl is null or empty.");

        return null;
    }

    private void SetLastClipUrl(string url) {
        if (string.IsNullOrWhiteSpace(url)) {
            Log(LogLevel.Warn, "Attempted to set an empty or null clip URL.");

            return;
        }

        _lastClipUrl = url;
        CPH.SetGlobalVar(LastClipUrlKey, url);
        Log(LogLevel.Info, "Successfully set the last clip URL.");
    }

    private string GetLastClipUrl() {
        if (string.IsNullOrWhiteSpace(_lastClipUrl)) return null;

        var url = CPH.GetGlobalVar<string>(LastClipUrlKey);

        if (string.IsNullOrWhiteSpace(url)) Log(LogLevel.Warn, "No last clip URL found for replay.");

        return url;
    }

    private async Task<ClipData> FetchClipDataIfNeeded(ClipData clipData, string clipUrl) {
        Log(LogLevel.Debug,
            $"Entering {nameof(FetchClipDataIfNeeded)} with: clipData={(clipData != null ? JsonConvert.SerializeObject(clipData) : "null")}, clipUrl={(string.IsNullOrWhiteSpace(clipUrl) ? "null" : clipUrl)}");

        if (clipData == null) {
            if (string.IsNullOrWhiteSpace(clipUrl)) {
                Log(LogLevel.Warn, "ClipData is null and no clip URL was provided.");

                return null;
            }

            try {
                var fetchedData = await GetClipData(clipUrl);

                if (fetchedData == null) Log(LogLevel.Warn, $"Failed to fetch ClipData for URL: {clipUrl}");

                return fetchedData;
            } catch (Exception ex) {
                Log(LogLevel.Error, $"Error fetching ClipData for URL {clipUrl}: {ex.Message}");

                return null;
            }
        }

        if (string.IsNullOrWhiteSpace(clipData.Id) || string.IsNullOrWhiteSpace(clipData.Url)) {
            Log(LogLevel.Warn, "ClipData contains invalid or missing fields (ID or URL).");

            return null;
        }

        Log(LogLevel.Debug,
            $"Exiting {nameof(FetchClipDataIfNeeded)} successfully with valid ClipData (ID: {clipData.Id}, URL: {clipData.Url})");

        return clipData;
    }

    private static (string StreamerName, string ClipTitle, string CuratorName) GetClipInfo(ClipData clipData,
                                                                                           string defaultCuratorName =
                                                                                               "Anonymous") {
        const string unknownStreamer = "Unknown Streamer";
        const string untitledClip = "Untitled Clip";

        var streamerName = GetValueOrDefault(clipData?.BroadcasterName, unknownStreamer);
        var clipTitle = GetValueOrDefault(clipData?.Title, untitledClip);
        var curatorName = GetValueOrDefault(clipData?.CreatorName, defaultCuratorName);

        return (streamerName, clipTitle, curatorName);
    }

    private async Task<ClipData> GetClipData(string clipUrl) {
        var clipId = clipUrl.Substring(clipUrl.LastIndexOf("/", StringComparison.Ordinal) + 1);

        return await _clipManager.GetClipData($"https://api.twitch.tv/helix/clips?id={clipId}");
    }

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

            if (string.IsNullOrWhiteSpace(clipId)) throw new ArgumentException("Invalid clip URL", nameof(clipUrl));

            if (_clipCache.TryGetValue(clipId, out var cachedClip)) return cachedClip;

            var clip = await _twitchApiClient.FetchClipById(clipId);

            if (clip != null) _clipCache[clipId] = clip;

            return clip;
        }

        public async Task<ClipData> GetClipDataById(string clipId) {
            if (_clipCache.TryGetValue(clipId, out var cachedClip)) return cachedClip;

            var rawClip = await _twitchApiClient.FetchDataAsync<JObject>($"clips?id={clipId}");

            if (rawClip != null) {
                var clipData = Clip.FromTwitchClip(rawClip).ToClipData(_cph);

                if (clipData != null) _clipCache[clipId] = clipData;

                return clipData;
            }

            _log(LogLevel.Warn, $"Clip data not found for ID: {clipId}");

            return null;
        }

        private string ExtractClipId(string clipUrl) {
            if (string.IsNullOrWhiteSpace(clipUrl))
                throw new ArgumentException("Clip URL cannot be null or empty.", nameof(clipUrl));

            try {
                var uri = new Uri(clipUrl);

                if (uri.Host.IndexOf("twitch.tv", StringComparison.OrdinalIgnoreCase) >= 0)
                    return uri.Segments.LastOrDefault()?.Trim('/');
            } catch (UriFormatException) {
                _log(LogLevel.Warn, "Invalid URL format, attempting fallback parsing.");

                var parts = clipUrl.Split('/');

                return parts.LastOrDefault()?.Trim('/');
            } catch (Exception ex) {
                _log(LogLevel.Error, $"Error extracting clip ID: {ex.Message}");
            }

            return null;
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
        /// <exception cref="InvalidOperationException">
        ///     Thrown if the provided JSON object is invalid or null.
        /// </exception>
        public static Clip FromTwitchClip(JObject twitchClip) {
            if (twitchClip != null)
                return new Clip {
                    Id = (string)twitchClip["id"],
                    Url = (string)twitchClip["url"],
                    EmbedUrl = (string)twitchClip["embed_url"],
                    BroadcasterId = (string)twitchClip["broadcaster_id"],
                    BroadcasterName = (string)twitchClip["broadcaster_name"],
                    CreatorId = (int)twitchClip["creator_id"],
                    CreatorName = (string)twitchClip["creator_name"],
                    VideoId = (string)twitchClip["video_id"],
                    GameId = (string)twitchClip["game_id"],
                    Language = (string)twitchClip["language"],
                    Title = (string)twitchClip["title"],
                    ViewCount = (int)twitchClip["view_count"],
                    CreatedAt = (DateTime)twitchClip["created_at"],
                    ThumbnailUrl = (string)twitchClip["thumbnail_url"],
                    Duration = (float)twitchClip["duration"],
                    IsFeatured = (bool)twitchClip["is_featured"]
                };

            throw new InvalidOperationException("Invalid twitch clip format.");
        }

        /// <summary>
        ///     Creates a <see cref="Clip" /> object from a <see cref="ClipData" /> instance.
        /// </summary>
        /// <param name="twitchClipData">
        ///     An instance of the <see cref="ClipData" /> class populated with clip information.
        /// </param>
        /// <returns>
        ///     An instance of <see cref="Clip" /> created from the <see cref="ClipData" /> .
        /// </returns>
        /// <exception cref="InvalidOperationException">
        ///     Thrown if the <see cref="ClipData" /> is invalid or null.
        /// </exception>
        public static Clip FromTwitchClip(ClipData twitchClipData) {
            if (twitchClipData != null)
                return new Clip {
                    Id = twitchClipData.Id,
                    Url = twitchClipData.Url,
                    EmbedUrl = twitchClipData.EmbedUrl,
                    BroadcasterId = twitchClipData.BroadcasterId,
                    BroadcasterName = twitchClipData.BroadcasterName,
                    CreatorId = twitchClipData.CreatorId,
                    CreatorName = twitchClipData.CreatorName,
                    VideoId = twitchClipData.VideoId,
                    GameId = twitchClipData.GameId,
                    Language = twitchClipData.Language,
                    Title = twitchClipData.Title,
                    ViewCount = twitchClipData.ViewCount,
                    CreatedAt = twitchClipData.CreatedAt,
                    ThumbnailUrl = twitchClipData.ThumbnailUrl,
                    Duration = twitchClipData.Duration,
                    IsFeatured = twitchClipData.IsFeatured
                };

            throw new InvalidOperationException("Invalid twitch clip data format.");
        }

        /// <summary>
        ///     Converts the <see cref="Clip" /> object into a <see cref="ClipData" /> representation for interaction with other
        ///     parts of the system.
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

        public JObject ParseClipData(string rawClipData) {
            return JsonConvert.DeserializeObject<JObject>(rawClipData);
        }
    }

    #endregion

    #region OBS Management

    private async Task<string> EnsureCliparinoInCurrentSceneAsync(string currentScene, string clipUrl) {
        try {
            if (string.IsNullOrWhiteSpace(currentScene)) currentScene = CPH.ObsGetCurrentScene();

            if (string.IsNullOrWhiteSpace(currentScene)) {
                Log(LogLevel.Warn, "Current scene is empty or null.");

                return "Unknown Scene";
            }

            EnsureCliparinoInSceneAsync(currentScene);
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error in EnsureCliparinoInCurrentSceneAsync: {ex.Message}");
        }

        try {
            var clipData = await GetClipData(clipUrl);
            var gameId = clipData?.GameId;
            var gameData = await _twitchApiClient.FetchGameById(gameId);

            return gameData?.Name ?? "Unknown Game";
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error fetching game name: {ex.Message}");

            return "Unknown Game";
        }
    }

    private async Task EnsureCliparinoInSceneAsync(string currentScene) {
        try {
            const string cliparinoSourceName = "Cliparino";

            if (CPH.ObsIsSourceVisible(currentScene, cliparinoSourceName)) {
                Log(LogLevel.Debug, $"Cliparino source already exists in scene '{currentScene}'.");

                return;
            }

            AddSceneSource(currentScene, cliparinoSourceName);
            Log(LogLevel.Info, $"Cliparino source added to scene '{currentScene}'.");
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error in EnsureCliparinoInSceneAsync: {ex.Message}");
        }
    }

    private async Task PrepareSceneForClipHostingAsync(string sceneName) {
        Log(LogLevel.Info, $"Preparing scene \'{sceneName}\' for clip hosting.");
        const string cliparinoSourceName = "Cliparino";
        const string playerSourceName = "Player";

        if (!EnsureSourceExistsAndIsVisible(sceneName, cliparinoSourceName)) return;

        if (!EnsureSourceExistsAndIsVisible(cliparinoSourceName, playerSourceName)) return;

        await ConfigureAudioForPlayerSourceAsync();
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
        Log(LogLevel.Debug, $"SetBrowserSource was called for URL \'{baseUrl}\'.");

        var sourceUrl = CreateSourceUrl(baseUrl);
        var currentVisibility = CPH.ObsIsSourceVisible(targetScene, "Cliparino");

        Log(LogLevel.Debug, $"Cliparino visibility before update: {currentVisibility}");

        if (targetScene == null) {
            targetScene = CPH.ObsGetCurrentScene();

            if (targetScene == null) throw new InvalidOperationException("Unable to retrieve target scene.");
        }

        const string cliparinoSourceName = "Cliparino";

        if (!SourceExistsInScene(targetScene, cliparinoSourceName)) {
            AddSceneSource(targetScene, cliparinoSourceName);
            Log(LogLevel.Info, $"Added \'{cliparinoSourceName}\' scene source to \'{targetScene}\'.");
        } else {
            UpdateBrowserSource(targetScene, cliparinoSourceName, sourceUrl);

            if (baseUrl != "about:blank") return;

            Log(LogLevel.Info, "Hiding Cliparino source after setting \'about:blank\'.");
            CPH.ObsSetSourceVisibility(targetScene, cliparinoSourceName, false);
        }

        currentVisibility = CPH.ObsIsSourceVisible(targetScene, "Cliparino");
        Log(LogLevel.Debug, $"Cliparino visibility after update: {currentVisibility}");
    }

    private void RefreshBrowserSource() {
        var payload = new {
            requestType = "PressInputPropertiesButton",
            requestData = new { inputName = "Player", propertyName = "refreshnocache" }
        };
        var response = CPH.ObsSendRaw(payload.requestType, JsonConvert.SerializeObject(payload.requestData));

        Log(LogLevel.Info, $"Refreshed browser source \'Player\'. Response: {response}");
    }

    private void UpdateBrowserSource(string sceneName, string sourceName, string url) {
        Log(LogLevel.Debug, $"Update URL to OBS: {url}");

        try {
            var payload = new {
                requestType = "SetInputSettings",
                requestData = new { inputName = sourceName, inputSettings = new { url }, overlay = true }
            };

            var response = CPH.ObsSendRaw(payload.requestType, JsonConvert.SerializeObject(payload.requestData));

            Log(LogLevel.Info,
                $"Browser source \'{sourceName}\' in scene \'{sceneName}\' updated with new URL \'{url}\'.");
            Log(LogLevel.Debug, $"Response from OBS: {response}");

            RefreshBrowserSource();
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error in UpdateBrowserSource: {ex.Message}");
        }
    }

    private async Task ConfigureAudioForPlayerSourceAsync() {
        try {
            var payload = new {
                requestType = "SetInputSettings",
                requestData = new {
                    inputName = "Player", inputSettings = new { volume = 1.0, audioSourceType = "player" }
                }
            };

            var response = CPH.ObsSendRaw(payload.requestType, JsonConvert.SerializeObject(payload.requestData));

            Log(LogLevel.Info, $"Audio for 'Player' source configured successfully.\nResponse: {response}");
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error in ConfigureAudioForPlayerSourceAsync: {ex.Message}");
        }
    }

    private object GenerateCompressorFilterPayload(string clipUrl) {
        return new {
            inputName = "Player",
            inputSettings = new {
                threshold = -15.0,
                ratio = 4.0,
                attack = 1.0,
                release = 50.0,
                makeUpGain = 0.0
            }
        };
    }

    private object GenerateGainFilterPayload(double gainValue) {
        return new { inputName = "Player", inputSettings = new { gain = gainValue } };
    }

    private object GenerateSetInputVolumePayload(double volumeValue) {
        return new { inputName = "Player", inputSettings = new { volume = volumeValue } };
    }

    private object GenerateSetAudioMonitorTypePayload(string monitorType) {
        return new { inputName = "Player", inputSettings = new { monitorType } };
    }

    private int GetSceneItemId(string sceneName, string sourceName) {
        try {
            var sceneItemId = CPH.ObsGetSceneItemId(sceneName, sourceName);

            if (sceneItemId == -1) {
                Log(LogLevel.Warn,
                    $"Failed to retrieve scene item ID for source '{sourceName}' in scene '{sceneName}'.");

                return sceneItemId;
            }

            return sceneItemId;
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error in GetSceneItemId: {ex.Message}");

            return -1;
        }
    }

    private void AddSceneSource(string targetScene, string cliparinoSourceName) {
        var payload = new {
            requestType = "CreateSceneItem",
            requestData = new { sceneName = targetScene, sourceName = cliparinoSourceName, sceneItemEnabled = true }
        };

        CPH.ObsSendRaw(payload.requestType, JsonConvert.SerializeObject(payload.requestData));
    }

    private bool SourceExistsInScene(string sceneName, string sourceName) {
        try {
            var sceneItemId = GetSceneItemId(sceneName, sourceName);

            if (sceneItemId == -1) {
                Log(LogLevel.Warn,
                    $"Failed to retrieve scene item ID for source \'{sourceName}\' in scene \'{sceneName}\'.");

                return false;
            }

            return true;
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error checking if source exists in scene: {ex.Message}");

            return false;
        }
    }

    private bool SceneExists(string sceneName) {
        try {
            var sceneExists = CPH.ObsSceneExists(sceneName);

            if (!sceneExists) {
                Log(LogLevel.Warn, $"Scene '{sceneName}' does not exist.");
            }

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

    private void AddBrowserSource(string sceneName, string sourceName, string url) {
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

    private string CreateSourceUrl(string clipUrl) {
        return $"https://player.twitch.tv/?clip={clipUrl}&autoplay=true";
    }

    #endregion
    
    #region Twitch API Interaction
    
    public class TwitchApiClient {
        private readonly HttpClient _httpClient;
        private readonly LogDelegate _log;
        private readonly string _clientId;
        private readonly string _authToken;

        public TwitchApiClient(HttpClient httpClient, OAuthInfo oAuthInfo, LogDelegate log) {
            _httpClient = httpClient
                          ?? throw new ArgumentNullException(nameof(httpClient), "HTTP client cannot be null.");
            _httpClient.BaseAddress = new Uri("https://api.twitch.tv/helix/");
            _log = log ?? throw new ArgumentNullException(nameof(log), "Log delegate cannot be null.");
            _clientId = oAuthInfo.TwitchClientId;
            _authToken = oAuthInfo.TwitchOAuthToken;

            if (string.IsNullOrWhiteSpace(_authToken))
                throw new InvalidOperationException("Twitch OAuth token is missing or invalid.");
        }

        public async Task<T> FetchDataAsync<T>(string endpoint) {
            if (string.IsNullOrWhiteSpace(endpoint))
                throw new ArgumentNullException(nameof(endpoint), "Endpoint cannot be null or empty.");
            if (string.IsNullOrWhiteSpace(_authToken))
                throw new InvalidOperationException("Twitch OAuth token is missing or invalid.");

            var completeUrl = new Uri(_httpClient.BaseAddress, endpoint).ToString();

            try {
                _log(LogLevel.Debug, $"Preparing to make GET request to endpoint: {completeUrl}");

                // Clear and set headers for the HTTP client
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Client-ID", _clientId);
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_authToken}");

                _log(LogLevel.Debug, "HTTP headers set successfully. Initiating request...");

                // Perform the actual HTTP GET request
                var response = await _httpClient.GetAsync(endpoint);

                // Log the response status
                _log(LogLevel.Debug, $"Received response: {response.StatusCode} ({(int)response.StatusCode})");

                if (!response.IsSuccessStatusCode) {
                    // Log the error with details about the failed response
                    _log(LogLevel.Error,
                         $"Request to Twitch API failed: {response.ReasonPhrase} "
                         + $"(Status Code: {(int)response.StatusCode}, URL: {completeUrl})");

                    return default;
                }

                // Extract and log response content
                var content = await response.Content.ReadAsStringAsync();

                _log(LogLevel.Debug, $"Response content: {content}");

                // Deserialize response
                var apiResponse = JsonConvert.DeserializeObject<TwitchApiResponse<T>>(content);

                if (apiResponse?.Data != null && apiResponse.Data.Length > 0) {
                    _log(LogLevel.Info, "Successfully retrieved and deserialized data from the Twitch API.");

                    return apiResponse.Data[0];
                }

                _log(LogLevel.Warn, $"No data returned from the Twitch API endpoint: {completeUrl}");

                return default;
            } catch (HttpRequestException httpEx) {
                // Log HTTP-specific exceptions
                _log(LogLevel.Error, $"HTTP request error while calling {completeUrl}: {httpEx.Message}");

                return default;
            } catch (JsonException jsonEx) {
                // Log JSON deserialization exceptions
                _log(LogLevel.Error, $"JSON deserialization error for response from {completeUrl}: {jsonEx.Message}");

                return default;
            } catch (Exception ex) {
                // Log any unexpected exceptions
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

    public class OAuthInfo {
        public OAuthInfo(string twitchClientId, string twitchOAuthToken) {
            TwitchClientId = twitchClientId;

            if (TwitchClientId == null)
                throw new ArgumentNullException(nameof(twitchClientId), "Client ID cannot be null.");

            TwitchOAuthToken = twitchOAuthToken;

            if (TwitchOAuthToken == null)
                throw new ArgumentNullException(nameof(twitchOAuthToken), "OAuth token cannot be null.");
        }

        /// <summary>
        ///     The Twitch Client ID required for API authentication.
        /// </summary>
        public string TwitchClientId { get; }

        /// <summary>
        ///     The Twitch OAuth token required for API authentication and authorization.
        /// </summary>
        public string TwitchOAuthToken { get; }
    }

    public class TwitchApiResponse<T> {
        public T[] Data { get; }

        public TwitchApiResponse(T[] data) {
            Data = data ?? Array.Empty<T>();
        }
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

            if (gameData == null || string.IsNullOrWhiteSpace(gameData.Name)) return "Unknown Game";

            return gameData.Name;
        } catch (Exception ex) {
            Log(LogLevel.Error, $"{GetErrorMessagePreamble(nameof(FetchGameNameAsync))}: {ex.Message}");
        }

        return "Unknown Game";
    }

    private static (string StreamerName, string ClipTitle, string CuratorName) ExtractClipInfo(ClipData clipData) {
        return GetClipInfo(clipData);
    }
    
    #endregion

    #region Server & Local Hosting

    private async Task ConfigureAndServe() {
        try {
            await CleanupServer();
            ValidatePortAvailability(8080);

            if (!await TryAcquireSemaphore(ServerSemaphore, nameof(ServerSemaphore))) return;

            using (var serverLock =
                   await ScopedSemaphore.AcquireAsync(ServerLockSemaphore, nameof(ServerLockSemaphore))) {
                InitializeServer();

                if (!await SetupTokenSemaphore()) return;

                _listeningTask = StartListening(_server, _cancellationTokenSource.Token);
                ConfigureBrowserSource("Cliparino", "Player", "http://localhost:8080/index.htm");
            }
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Configuration failed: {ex.Message}");
            await CleanupServer();
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
        if (!await TryAcquireSemaphore(TokenSemaphore, nameof(TokenSemaphore))) return false;

        try {
            _cancellationTokenSource = new CancellationTokenSource();

            return true;
        } finally {
            TokenSemaphore.Release();
        }
    }

    private void ConfigureBrowserSource(string name, string player, string url) {
        UpdateBrowserSource(name, player, url);
        RefreshBrowserSource();
        Log(LogLevel.Info, "Browser source configured.");
    }

    #endregion

    #region Request Handling

    private async Task StartListening(HttpListener server, CancellationToken cancellationToken) {
        Log(LogLevel.Info, "HTTP server started on http://localhost:8080");

        while (server.IsListening && !cancellationToken.IsCancellationRequested) {
            HttpListenerContext context = null;

            try {
                context = await server.GetContextAsync(); // Wait for incoming request
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
            string responseText;
            string contentType;
            var nonce = ApplyCORSHeaders(context.Response);

            // Select content based on request path.
            var requestPath = context.Request.Url?.AbsolutePath ?? string.Empty;

            switch (requestPath) {
                case "/index.css":
                    responseText = CSSText;
                    contentType = "text/css; charset=utf-8";

                    break;
                case "/":
                case "/index.htm":
                    var htmlTemplate = await GetHtmlInMemorySafe();
                    responseText = htmlTemplate.Replace("[[nonce]]", nonce);
                    contentType = "text/html; charset=utf-8";

                    break;
                default:
                    responseText = "404 Not Found";
                    contentType = "text/plain; charset=utf-8";
                    context.Response.StatusCode = (int)HttpStatusCode.NotFound;

                    break;
            }

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
    
    private async Task CreateAndHostClipPageAsync(string clipUrl, string streamerName, string clipTitle, string curatorName, ClipData clipData) {
        Log(LogLevel.Debug, $"{nameof(CreateAndHostClipPageAsync)} called with clipUrl: {clipUrl}, streamerName: {streamerName}, clipTitle: {clipTitle}, curatorName: {curatorName}, clipData: {JsonConvert.SerializeObject(clipData)}");

        try {
            if (string.IsNullOrWhiteSpace(clipUrl)) {
                Log(LogLevel.Error, "clipUrl cannot be null or empty. Ensure it is passed correctly.");
                return;
            }

            Log(LogLevel.Debug, "Extracting clip ID from clipUrl.");
            // Assuming fetchClipData and other processing happens here...

            // Assigning the HTML content to _htmlInMemory
            _htmlInMemory = GenerateHtmlContent(clipId, streamerName, gameName, clipTitle, curatorName);

            Log(LogLevel.Debug, $"Generated HTML content and stored in _htmlInMemory: {_htmlInMemory}");

            // More processing for hosting the clip page would continue here...

        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error occurred in {nameof(CreateAndHostClipPageAsync)}: {ex.Message}");
            Log(LogLevel.Debug, ex.StackTrace);
        } finally {
            Log(LogLevel.Debug, $"{nameof(CreateAndHostClipPageAsync)} execution finished.");
        }
    }

    private string GenerateHtmlContent(string clipId, string streamerName, string gameName, string clipTitle, string curatorName) {
    const string defaultClipID = "SoftHilariousNigiriPipeHype-2itiu1ZeL77SAPRM";
    var safeClipId = clipId ?? defaultClipID;
    var safeStreamerName = WebUtility.HtmlEncode(streamerName) ?? "Unknown Streamer";
    var safeGameName = WebUtility.HtmlEncode(gameName) ?? "Unknown Game";
    var safeClipTitle = WebUtility.HtmlEncode(clipTitle) ?? "Untitled Clip";
    var safeCuratorName = WebUtility.HtmlEncode(curatorName) ?? "Anonymous";

    Log(LogLevel.Debug, $"HTML template: {HTMLText}\n, HTML replacement(s): {safeClipId}");

    var htmlContent = HTMLText.Replace("[[clipId]]", safeClipId)
                              .Replace("[[streamerName]]", safeStreamerName)
                              .Replace("[[gameName]]", safeGameName)
                              .Replace("[[clipTitle]]", safeClipTitle)
                              .Replace("[[curatorName]]", safeCuratorName);

    Log(LogLevel.Info, "Created HTML content for clip page.");
    Log(LogLevel.Debug, $"Generated HTML content: {htmlContent}");

    return htmlContent;
}

    private async Task<string> GetHtmlInMemorySafe() {
        Log(LogLevel.Debug, "Attempting to retrieve HTML template from memory.");

        if (!await TryAcquireSemaphore(ServerSemaphore, nameof(GetHtmlInMemorySafe))) {
            Log(LogLevel.Error, "Failed to acquire semaphore in GetHtmlInMemorySafe.");

            return HTMLErrorPage;
        }

        try {
            if (string.IsNullOrWhiteSpace(_htmlInMemory)) {
                Log(LogLevel.Warn, "_htmlInMemory is null or empty. Returning default error response.");

                return HTMLErrorPage;
            }

            Log(LogLevel.Info, "HTML template retrieved successfully from memory.");

            return _htmlInMemory;
        } catch (Exception ex) {
            Log(LogLevel.Error, $"An error occurred while retrieving the HTML from memory: {ex.Message}");
            Log(LogLevel.Debug, ex.StackTrace);

            return HTMLErrorPage;
        } finally {
            ServerSemaphore.Release();
            Log(LogLevel.Debug, "Semaphore released in GetHtmlInMemorySafe.");
        }
    }

    #endregion

    #region Cleanup

    private async Task CleanupServer(HttpListener server = null) {
        Log(LogLevel.Debug, "Entering CleanupServer.");

        if (!await AcquireSemaphoreAndLog(ServerSemaphore, "CleanupServer")) return;

        try {
            await CancelAllOperations();
            var serverInstance = GetSafeServerInstance(server);
            await CleanupListeningTask();
            StopAndDisposeServer(serverInstance);
        } finally {
            ServerSemaphore.Release();
        }

        Log(LogLevel.Debug, "Exiting CleanupServer.");
    }

    private async Task CancelAllOperations() {
        if (!await AcquireSemaphoreAndLog(TokenSemaphore, "CancelAllOperations")) return;

        try {
            _cancellationTokenSource?.Cancel();
            Log(LogLevel.Debug, "All ongoing operations canceled.");
        } finally {
            TokenSemaphore.Release();
        }
    }

    private HttpListener GetSafeServerInstance(HttpListener server) {
        lock (ServerLock) {
            if (_server == null && server == null) Log(LogLevel.Warn, "No server instance available for cleanup.");

            var instance = server ?? _server;
            _server = null;

            return instance;
        }
    }

    private async Task CleanupListeningTask() {
        if (_listeningTask == null) return;

        Log(LogLevel.Debug, "Cleaning up listening task.");

        try {
            await _listeningTask;
        } catch (OperationCanceledException) {
            Log(LogLevel.Info, "Listening task gracefully canceled.");
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error cleaning up listening task: {ex.Message}");
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
            Log(LogLevel.Info, "Server stopped and disposed.");
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error stopping or disposing server: {ex.Message}");
        }
    }

    #endregion

    #region Utilities

    public class ScopedSemaphore : IDisposable {
        private readonly SemaphoreSlim _semaphore;
        // private readonly Delegate _log;
        
        public ScopedSemaphore(SemaphoreSlim semaphore, Delegate log) {
            _semaphore = semaphore ?? throw new ArgumentNullException(nameof(semaphore));
            // _log = log;
            _semaphore.Wait();
        }

        public void Dispose() {
            try {
                _semaphore.Release();
            } catch (ObjectDisposedException) {
                // _log(LogLevel.Warn, "Semaphore has been disposed.");
            } catch (SemaphoreFullException ) {
                // do stuff
            } catch (Exception ex) {
                // more stuff, probably palliative at this point
            }
        }
    }

    private async Task<bool> TryAcquireSemaphore(SemaphoreSlim semaphore, string context) {
        if (!await semaphore.WaitAsync(TimeSpan.FromSeconds(10))) {
            Log(LogLevel.Error, $"Semaphore timeout: {context}");

            return false;
        }

        Log(LogLevel.Debug, $"Semaphore acquired: {context}");

        return true;
    }

    private async Task<bool> AcquireSemaphoreAndLog(SemaphoreSlim semaphore, string context) {
        if (await semaphore.WaitAsync(TimeSpan.FromSeconds(10))) return true;

        Log(LogLevel.Error, $"Timeout acquiring semaphore: {context}");

        return false;

    }

    private const int NonceLength = 16;

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

    private static string CreateNonce() {
        var guidBytes = Guid.NewGuid().ToByteArray();
        var base64Nonce = Convert.ToBase64String(guidBytes);

        return SanitizeNonce(base64Nonce).Substring(0, NonceLength);
    }

    private static string SanitizeNonce(string nonce) {
        return nonce.Replace("+", "-").Replace("/", "_").Replace("=", "");
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
        try {
            IdentifyPortConflict(port);

            var listener = new TcpListener(IPAddress.Loopback, port);

            listener.Start();
            listener.Stop();

            return true;
        } catch (SocketException) {
            return false;
        }
    }

    private void IdentifyPortConflict(int port) {
        var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
        var activeTcpListeners = ipGlobalProperties.GetActiveTcpListeners();
        var activeTcpConnections = ipGlobalProperties.GetActiveTcpConnections();

        var isPortInUse = activeTcpListeners.Any(endpoint => endpoint.Port == port)
                          || activeTcpConnections.Any(connection => connection.LocalEndPoint.Port == port);

        if (isPortInUse)
            Log(LogLevel.Warn,
                $"Conflict detected on port {port}. This port is currently in use. Check active processes or services.");
        else
            Log(LogLevel.Info, $"Port {port} is currently free and usable.");
    }

    private void CancelCurrentToken() {
        _autoStopCancellationTokenSource?.Cancel();
        _autoStopCancellationTokenSource?.Dispose();
        _autoStopCancellationTokenSource = new CancellationTokenSource();
    }

    private static string CreateErrorPreamble(string methodName) {
        return $"An error occurred in {methodName}";
    }

    private void LogBrowserSourceSetup(string url, int width, int height) {
        Log(LogLevel.Info, $"Setting browser source with URL: {url}, width: {width}, height: {height}");
    }

    private Clip LogAndReturnNull(string message) {
        Log(LogLevel.Warn, message);

        return null;
    }

    private async Task HostClipWithDetailsAsync(string clipUrl, ClipData clipData) {
        Log(LogLevel.Debug,
            $"{nameof(HostClipWithDetailsAsync)} called with clipUrl: {clipUrl}, clipData: {JsonConvert.SerializeObject(clipData)}");

        try {
            if (string.IsNullOrWhiteSpace(clipUrl)) {
                Log(LogLevel.Warn, "clipUrl is null or empty. Attempting to use clipData.Url.");
                clipUrl = clipData?.Url;

                if (string.IsNullOrWhiteSpace(clipUrl)) {
                    Log(LogLevel.Error, "clipUrl could not be resolved. Aborting.");

                    return;
                }
            }

            SetLastClipUrl(clipUrl);
            var currentScene = CPH.ObsGetCurrentScene();
            await PrepareSceneForClipHostingAsync(currentScene);

            Log(LogLevel.Info, "Extracting clip info from ClipData.");
            var clipInfo = ExtractClipInfo(clipData);

            Log(LogLevel.Info, "Creating and hosting clip page with details.");
            await CreateAndHostClipPageAsync(clipUrl,
                                             clipInfo.StreamerName,
                                             clipInfo.ClipTitle,
                                             clipInfo.CuratorName,
                                             clipData);

            Log(LogLevel.Debug, "Starting auto-stop task for playback.");
            await StartAutoStopTaskAsync(clipData.Duration);
        } catch (Exception ex) {
            Log(LogLevel.Error, $"{GetErrorMessagePreamble(nameof(HostClipWithDetailsAsync))}: {ex.Message}");
        } finally {
            Log(LogLevel.Debug, $"{nameof(HostClipWithDetailsAsync)} exiting.");
        }
    }

    private static string GetValueOrDefault(string value, string defaultValue) {
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
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

    private async Task<bool> TryAcquireSemaphore(SemaphoreSlim semaphore, string name, int timeout = 10) {
        if (await semaphore.WaitAsync(TimeSpan.FromSeconds(timeout))) {
            Log(LogLevel.Debug, $"{name} acquired.");

            return true;
        }

        Log(LogLevel.Error, $"{name} acquisition timed out.");

        return false;
    }

    #endregion
}