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
using System.IO;
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
using static System.Environment;
using static System.Environment.SpecialFolder;

#endregion

// ReSharper disable once UnusedType.Global
// ReSharper disable once ClassNeverInstantiated.Global
/// <summary>
///     Represents a custom inline script handler for the Streamer.bot environment. Handles commands,
///     interactions, and execution logic specific to Cliparino's Twitch clip management.
/// </summary>
public class CPHInline : CPHInlineBase {
    private const int DefaultWidth = 1920;
    private const int DefaultHeight = 1080;
    private const int AdditionalHtmlSetupDelaySeconds = 3;
    private const string CliparinoSourceName = "Cliparino";
    private const string PlayerSourceName = "Player";
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
    private string _lastClipUrl;
    private Task _listeningTask;
    private bool _loggingEnabled;
    private HttpListener _server;

    private TwitchApiClient _twitchApiClient;
    // Kerb advised me to type soup; I acquiesced. I'm sorry.

    #region Initialization & Core Execution

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public void Init() {
        _httpClient = new HttpClient();
        _twitchApiClient =
            new TwitchApiClient(_httpClient, new OAuthInfo(CPH.TwitchClientId, CPH.TwitchOAuthToken), Log);
        _clipManager = new ClipManager(_twitchApiClient, Log, CPH);
        PathBuddy.EnsureCliparinoFolderExists();
        PathBuddy.SetLogger(Log);
    }

    // ReSharper disable once UnusedMember.Global
    /// <summary>
    ///     Executes the primary logic for the `CPHInline` object. This is the main entry point for the
    ///     script, called by Streamer.bot during execution.
    /// </summary>
    /// <remarks>
    ///     Streamer.bot invokes this method to execute the user's custom logic. The method processes chat
    ///     commands such as `!watch`, `!so`, `!replay`, or `!stop` to control playback of Twitch clips and
    ///     OBS overlay behavior.
    /// </remarks>
    /// <returns>
    ///     A boolean indicating whether the script executed successfully. Returns <c>true</c> if execution
    ///     succeeded; otherwise, <c>false</c>.
    /// </returns>
    public bool Execute() {
        Log(LogLevel.Debug, $"{nameof(Execute)} for Cliparino started.");

        try {
            if (!TryGetCommand(out var command)) {
                Log(LogLevel.Warn, "Command argument is missing.");

                return false;
            }

            EnsureCliparinoInCurrentSceneAsync(null).GetAwaiter().GetResult();

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
    /// <param name="userId">
    ///     The unique identifier of the user for whom the clip is being retrieved.
    /// </param>
    /// <param name="clipSettings">
    ///     The <see cref="ClipSettings" /> object containing filters such as
    ///     <paramref name="clipSettings.featuredOnly" />, <paramref name="clipSettings.maxClipSeconds" />,
    ///     and <paramref name="clipSettings.clipAgeDays" />.
    /// </param>
    /// <returns>
    ///     A <see cref="Clip" /> object representing the random clip that matches the specified filters,
    ///     or <c>null</c> if no matching clip is found.
    /// </returns>
    /// <exception cref="Exception">
    ///     Thrown if an error occurs during the retrieval process.
    /// </exception>
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
    /// <param name="clips">
    ///     A collection of <see cref="ClipData" /> objects representing the available clips.
    /// </param>
    /// <param name="featuredOnly">
    ///     A <c>bool</c> indicating whether only featured clips should be considered.
    /// </param>
    /// <param name="maxSeconds">
    ///     The maximum duration (in seconds) of the clip to be considered.
    /// </param>
    /// <param name="userId">
    ///     The unique identifier of the user for whom the clip is being retrieved.
    /// </param>
    /// <returns>
    ///     A <see cref="Clip" /> object representing the matching clip, or <c>null</c> if no matching clip
    ///     is found.
    /// </returns>
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
    /// <param name="userId">
    ///     The unique identifier of the Twitch user to be retrieved.
    /// </param>
    /// <returns>
    ///     A dynamic object representing the Twitch user, or <c>null</c> if the user could not be found.
    /// </returns>
    /// <exception cref="Exception">
    ///     Thrown if an error occurs during the retrieval process.
    /// </exception>
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
    /// <param name="userId">
    ///     The unique identifier of the user for whom the clips are being retrieved.
    /// </param>
    /// <param name="clipAgeDays">
    ///     The maximum age (in days) of the clips to be retrieved.
    /// </param>
    /// <returns>
    ///     A collection of <see cref="ClipData" /> objects representing the retrieved clips.
    /// </returns>
    /// <exception cref="Exception">
    ///     Thrown if an error occurs during the retrieval process.
    /// </exception>
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
    /// <param name="clips">
    ///     A collection of <see cref="ClipData" /> objects to be filtered.
    /// </param>
    /// <param name="featuredOnly">
    ///     A <c>bool</c> indicating whether only featured clips should be included.
    /// </param>
    /// <param name="maxSeconds">
    ///     The maximum duration (in seconds) of the clips to be included.
    /// </param>
    /// <returns>
    ///     A list of <see cref="ClipData" /> objects that match the specified criteria.
    /// </returns>
    private static List<ClipData> FilterClips(IEnumerable<ClipData> clips, bool featuredOnly, int maxSeconds) {
        return clips.Where(c => (!featuredOnly || c.IsFeatured) && c.Duration <= maxSeconds).ToList();
    }

    /// <summary>
    ///     Selects a random clip from the provided list.
    /// </summary>
    /// <param name="clips">
    ///     A list of <see cref="ClipData" /> objects from which a random clip will be selected.
    /// </param>
    /// <returns>
    ///     A <see cref="ClipData" /> object representing the randomly selected clip.
    /// </returns>
    private static ClipData SelectRandomClip(List<ClipData> clips) {
        return clips[new Random().Next(clips.Count)];
    }

    /// <summary>
    ///     Fetches a valid clip's data, utilizing caching to speed up frequent requests.
    /// </summary>
    /// <param name="clipData">
    ///     The <see cref="ClipData" /> object containing the clip's details.
    /// </param>
    /// <param name="clipUrl">
    ///     The URL of the clip to be fetched.
    /// </param>
    /// <returns>
    ///     A <see cref="ClipData" /> object representing the fetched clip, or <c>null</c> if the clip
    ///     could not be fetched or validated.
    /// </returns>
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
    /// <param name="clipUrl">
    ///     The URL of the clip to be fetched.
    /// </param>
    /// <returns>
    ///     A <see cref="ClipData" /> object representing the fetched clip, or <c>null</c> if the clip
    ///     could not be fetched.
    /// </returns>
    /// <exception cref="Exception">
    ///     Thrown if an error occurs during the retrieval process.
    /// </exception>
    private async Task<ClipData> FetchClipDataFromUrl(string clipUrl) {
        if (string.IsNullOrWhiteSpace(clipUrl) || !clipUrl.Contains("twitch.tv")) {
            Log(LogLevel.Error, $"Invalid clip URL provided: {clipUrl}");

            return null;
        }

        try {
            var clipData = await _clipManager.GetClipData(clipUrl);

            if (clipData != null && !string.IsNullOrWhiteSpace(clipData.Id))
                return await _clipManager.GetClipDataById(clipData.Id);

            Log(LogLevel.Error, $"Failed to fetch ClipData or invalid clip ID for URL: {clipUrl}");

            return null;
        } catch (Exception ex) {
            Log(LogLevel.Error, $"{CreateErrorPreamble()}: {ex.Message}");

            return null;
        }
    }

    /// <summary>
    ///     Validates and resolves a clip URL from either the provided URL or existing clip data.
    /// </summary>
    /// <param name="clipUrl">
    ///     The URL of the clip to be validated.
    /// </param>
    /// <param name="clipData">
    ///     The <see cref="ClipData" /> object containing the clip's details.
    /// </param>
    /// <returns>
    ///     A <c>string</c> representing the validated clip URL.
    /// </returns>
    private string ValidateClipUrl(string clipUrl, ClipData clipData) {
        return !string.IsNullOrWhiteSpace(clipUrl)
                   ? clipUrl
                   : clipData?.Url ?? LogErrorAndReturn<string>("clipUrl is null or empty.");
    }

    /// <summary>
    ///     Logs an error message and returns the specified default value.
    /// </summary>
    /// <typeparam name="T">
    ///     The type of the default value to be returned.
    /// </typeparam>
    /// <param name="error">
    ///     The error message to be logged.
    /// </param>
    /// <returns>
    ///     The default value of type <typeparamref name="T" />.
    /// </returns>
    private T LogErrorAndReturn<T>(string error) {
        Log(LogLevel.Error, error);

        return default;
    }

    /// <summary>
    ///     Stores the last known clip URL for replay or further actions.
    /// </summary>
    /// <param name="url">
    ///     The URL of the clip to be stored.
    /// </param>
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
    /// <returns>
    ///     A <c>string</c> representing the last stored clip URL, or <c>null</c> if no URL is stored.
    /// </returns>
    private string GetLastClipUrl() {
        if (string.IsNullOrWhiteSpace(_lastClipUrl)) return null;

        var url = CPH.GetGlobalVar<string>(LastClipUrlKey);
        if (string.IsNullOrWhiteSpace(url)) Log(LogLevel.Warn, "No last clip URL found for replay.");

        return url;
    }

    /// <summary>
    ///     Extracts detailed clip information such as streamer name, clip title, and curator name.
    /// </summary>
    /// <param name="clipData">
    ///     The <see cref="ClipData" /> object containing the clip's details.
    /// </param>
    /// <param name="defaultCuratorName">
    ///     The default name to be used if the curator's name is not available.
    /// </param>
    /// <returns>
    ///     A tuple containing the streamer name, clip title, and curator name.
    /// </returns>
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
    /// <param name="durationInSeconds">
    ///     The duration of the clip in seconds.
    /// </param>
    /// <returns>
    ///     A <see cref="TimeSpan" /> representing the effective duration of the clip.
    /// </returns>
    private static TimeSpan GetDurationWithSetupDelay(float durationInSeconds) {
        return TimeSpan.FromSeconds(durationInSeconds + AdditionalHtmlSetupDelaySeconds);
    }

    /// <summary>
    ///     Manages operations related to Twitch clips, including retrieving clip data and extracting clip
    ///     identifiers from provided URLs.
    /// </summary>
    /// <remarks>
    ///     This class interacts with the Twitch API to fetch clip information and provides utility methods
    ///     for handling Twitch clip data.
    /// </remarks>
    private class ClipManager {
        private readonly Dictionary<string, ClipData> _clipCache = new Dictionary<string, ClipData>();
        private readonly IInlineInvokeProxy _cph;
        private readonly LogDelegate _log;
        private readonly TwitchApiClient _twitchApiClient;

        /// <summary>
        ///     Provides methods for managing and interacting with Twitch clips.
        /// </summary>
        /// <remarks>
        ///     This class is used to handle the retrieval, parsing, and management of clip data from Twitch.
        ///     It uses an instance of <see cref="TwitchApiClient" /> to communicate with the Twitch API and a
        ///     logging delegate to record operations.
        /// </remarks>
        public ClipManager(TwitchApiClient twitchApiClient, LogDelegate log, IInlineInvokeProxy cph) {
            _twitchApiClient = twitchApiClient ?? throw new ArgumentNullException(nameof(twitchApiClient));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _cph = cph ?? throw new ArgumentNullException(nameof(cph));
        }

        /// <summary>
        ///     Retrieves metadata about a Twitch clip based on its URL.
        /// </summary>
        /// <param name="clipUrl">
        ///     The URL of the Twitch clip. This URL must contain a valid clip identifier.
        /// </param>
        /// <returns>
        ///     A <see cref="ClipData" /> object containing metadata about the clip, such as its title,
        ///     creator, or game information, if retrieval succeeds; otherwise, <c>null</c>.
        /// </returns>
        /// <exception cref="ArgumentException">
        ///     Thrown when the <paramref name="clipUrl" /> is invalid, null, or does not contain a
        ///     recognizable clip identifier.
        /// </exception>
        /// <remarks>
        ///     This method extracts the clip ID from the provided URL using the <see cref="ExtractClipId" />
        ///     method, then fetches the corresponding clip data via the Twitch API. If the API call fails, or
        ///     if the clip metadata cannot be retrieved, the method will return <c>null</c>.
        /// </remarks>
        /// <seealso cref="ClipManager.ExtractClipId" />
        /// <seealso cref="ClipManager.GetClipDataById" />
        public async Task<ClipData> GetClipData(string clipUrl) {
            var clipId = ExtractClipId(clipUrl);

            if (string.IsNullOrWhiteSpace(clipId)) throw new ArgumentException("Invalid clip URL.", nameof(clipUrl));

            return await GetClipDataInternal(clipId, async () => await _twitchApiClient.FetchClipById(clipId));
        }

        /// <summary>
        ///     Retrieves detailed clip data using the specified clip ID by querying the Twitch API.
        /// </summary>
        /// <param name="clipId">
        ///     The unique identifier of the Twitch clip to retrieve data for.
        /// </param>
        /// <returns>
        ///     A <see cref="ClipData" /> object containing detailed information about the requested clip, or
        ///     <c>null</c> if the clip data could not be retrieved or does not exist.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     Thrown when the <paramref name="clipId" /> parameter is <c>null</c> or empty.
        /// </exception>
        /// <exception cref="HttpRequestException">
        ///     Thrown when there is an error communicating with the Twitch API.
        /// </exception>
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

        /// <summary>
        ///     Extracts the clip ID from a specified Twitch clip URL.
        /// </summary>
        /// <param name="clipUrl">
        ///     The full URL of the Twitch clip, from which the clip ID will be extracted.
        /// </param>
        /// <returns>
        ///     A <c>string</c> representing the extracted clip ID if the URL is valid and parsing succeeds; otherwise,
        ///     <c>null</c>.
        /// </returns>
        /// <exception cref="ArgumentException">
        ///     Thrown when <paramref name="clipUrl" /> is null, empty, or contains only whitespace.
        /// </exception>
        /// <exception cref="UriFormatException">
        ///     Thrown when <paramref name="clipUrl" /> is not a valid URI and could not be parsed properly.
        /// </exception>
        /// <remarks>
        ///     This method attempts to extract the clip ID by analyzing the structure of a Twitch URL. If the
        ///     URL contains a valid Twitch domain and follows the typical segment-based format, the clip ID is
        ///     retrieved from the last segment. If the URL format is invalid, the method falls back to a
        ///     simple split-and-trim operation. If neither method succeeds, the method logs an appropriate
        ///     error or warning message and returns <c>null</c>.
        /// </remarks>
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

        /// <summary>
        ///     Retrieves clip data for the specified clip ID by either loading it from the cache or fetching
        ///     it using the provided <paramref name="fetchClipDataFunc" /> delegate.
        /// </summary>
        /// <param name="clipId">
        ///     The unique identifier of the clip to retrieve. Must not be null, empty, or consist only of
        ///     white-space.
        /// </param>
        /// <param name="fetchClipDataFunc">
        ///     A delegate to asynchronously fetch clip data from an external source if the clip is not already
        ///     cached. The delegate cannot be null.
        /// </param>
        /// <returns>
        ///     The <see cref="ClipData" /> object corresponding to the given <paramref name="clipId" />.
        ///     Returns the cached clip data if available; otherwise, the data fetched by
        ///     <paramref name="fetchClipDataFunc" />. Returns <c>null</c> if no data could be fetched.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     Thrown if <paramref name="clipId" /> or <paramref name="fetchClipDataFunc" /> is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        ///     Thrown if <paramref name="clipId" /> is empty or consists only of white-space.
        /// </exception>
        /// <remarks>
        ///     The method ensures that clip data for a specific <paramref name="clipId" /> is maintained in a
        ///     local cache to minimize redundant calls to the external data source. If the data is not found
        ///     in the cache, it invokes the provided <paramref name="fetchClipDataFunc" /> delegate to
        ///     retrieve the data. Once retrieved, the data is added to the cache for future use.
        /// </remarks>
        private async Task<ClipData> GetClipDataInternal(string clipId, Func<Task<ClipData>> fetchClipDataFunc) {
            if (_clipCache.TryGetValue(clipId, out var cachedClip)) return cachedClip;

            var clipData = await fetchClipDataFunc();

            if (clipData != null) _clipCache[clipId] = clipData;

            return clipData;
        }
    }

    /// <summary>
    ///     Encapsulates settings and filtering options for retrieving Twitch clips.
    /// </summary>
    /// <remarks>
    ///     This class is designed to specify constraints such as whether only featured clips should be
    ///     included, the maximum duration of clips in seconds, and how recent the clips should be in terms
    ///     of days.
    /// </remarks>
    private class ClipSettings {
        /// <summary>
        ///     Represents settings to configure the behavior for selecting and fetching Twitch clips.
        /// </summary>
        /// <remarks>
        ///     This class encapsulates configurations such as whether to only include featured clips, the
        ///     maximum duration of clips in seconds, and the maximum age of clips in days.
        /// </remarks>
        public ClipSettings(bool featuredOnly, int maxClipSeconds, int clipAgeDays) {
            FeaturedOnly = featuredOnly;
            MaxClipSeconds = maxClipSeconds;
            ClipAgeDays = clipAgeDays;
        }

        /// <summary>
        ///     Gets a value indicating whether only featured clips should be considered.
        /// </summary>
        /// <value>
        ///     <c>true</c> if only featured clips are considered; otherwise, <c>false</c>.
        /// </value>
        /// <remarks>
        ///     This property is part of the <see cref="ClipSettings" /> class, which provides configuration
        ///     values for managing and filtering Twitch clips.
        /// </remarks>
        public bool FeaturedOnly { get; }

        /// <summary>
        ///     Gets the maximum allowed duration for a clip, in seconds.
        /// </summary>
        /// <value>
        ///     An integer representing the maximum clip duration, in seconds.
        /// </value>
        /// <remarks>
        ///     This property imposes a limit on the length of clips that can be processed. Clips exceeding
        ///     this duration will be excluded or truncated based on the implementation logic.
        /// </remarks>
        public int MaxClipSeconds { get; }

        /// <summary>
        ///     Gets the maximum age, in days, of clips that should be considered valid. Clips older than this
        ///     value will be excluded from processing.
        /// </summary>
        /// <value>
        ///     An integer representing the maximum age of clips, in days.
        /// </value>
        /// <remarks>
        ///     This property is used to filter out outdated clips during operations. Ensure the value is a
        ///     non-negative integer to avoid unexpected behavior.
        /// </remarks>
        public int ClipAgeDays { get; }

        /// <summary>
        ///     Deconstructs the <see cref="ClipSettings" /> into its constituent components.
        /// </summary>
        /// <param name="featuredOnly">
        ///     When set to <c>true</c>, indicates that only featured clips should be included.
        /// </param>
        /// <param name="maxClipSeconds">
        ///     Specifies the maximum allowable length of the clip in seconds.
        /// </param>
        /// <param name="clipAgeDays">
        ///     Denotes the maximum age of the clip in days.
        /// </param>
        /// <remarks>
        ///     This method simplifies access to the individual properties of <see cref="ClipSettings" /> by
        ///     enabling tuple-style assignment. It is particularly useful in scenarios where multiple
        ///     configuration values are used together, such as when filtering clips.
        /// </remarks>
        public void Deconstruct(out bool featuredOnly, out int maxClipSeconds, out int clipAgeDays) {
            featuredOnly = FeaturedOnly;
            maxClipSeconds = MaxClipSeconds;
            clipAgeDays = ClipAgeDays;
        }
    }

    /// <summary>
    ///     Represents a single Twitch clip's data model, including information such as the URL, creator
    ///     details, game details, and more.
    /// </summary>
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public class Clip {
        /// <summary>
        ///     The unique identifier for the clip.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        ///     The direct URL to view the clip.
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        ///     The embeddable URL for the clip. Used when embedding the clip in web-based overlays or pages.
        /// </summary>
        public string EmbedUrl { get; set; }

        /// <summary>
        ///     The Twitch broadcaster's unique ID that the clip is associated with.
        /// </summary>
        public string BroadcasterId { get; set; }

        /// <summary>
        ///     The Twitch broadcaster's display name that the clip is associated with.
        /// </summary>
        public string BroadcasterName { get; set; }

        /// <summary>
        ///     The unique identifier of the clip's creator.
        /// </summary>
        public int CreatorId { get; set; }

        /// <summary>
        ///     The display name of the clip's creator.
        /// </summary>
        public string CreatorName { get; set; }

        /// <summary>
        ///     The ID of the video the clip is derived from, i.e., the stream's VoD ID.
        /// </summary>
        public string VideoId { get; set; }

        /// <summary>
        ///     The ID of the game that was being streamed when the clip was created.
        /// </summary>
        public string GameId { get; set; }

        /// <summary>
        ///     The language of the clip's content (e.g., "en" for English).
        /// </summary>
        public string Language { get; set; }

        /// <summary>
        ///     The title of the clip.
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        ///     The total number of views for the clip.
        /// </summary>
        public int ViewCount { get; set; }

        /// <summary>
        ///     The time when the clip was created (UTC).
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        ///     The URL to the clip's thumbnail image.
        /// </summary>
        public string ThumbnailUrl { get; set; }

        /// <summary>
        ///     The duration of the clip in seconds.
        /// </summary>
        public float Duration { get; set; }

        /// <summary>
        ///     A value indicating whether the clip is featured content on Twitch.
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
        ///     A new <see cref="ClipData" /> object populated with data from this <see cref="Clip" />
        ///     instance.
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
        ///     Maps values from a <see cref="JObject" /> or <see cref="ClipData" /> to the given
        ///     <see cref="Clip" /> instance.
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

    /// <summary>
    ///     Ensures that the Cliparino source is properly configured and displayed within the specified
    ///     scene in the streaming software's active setup.
    /// </summary>
    /// <param name="currentScene">
    ///     The name of the scene to ensure the Cliparino source exists in. If null or empty, the method
    ///     will retrieve and use the current active scene.
    /// </param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="currentScene" /> is null or empty after processing.
    /// </exception>
    /// <exception cref="Exception">
    ///     Thrown if any errors occur while ensuring the Cliparino source is present.
    /// </exception>
    /// <remarks>
    ///     This method primarily handles configuration for the Cliparino Twitch clip source in OBS or
    ///     other compatible streaming software. It ensures the configured source is present in the
    ///     specified scene.
    /// </remarks>
    private async Task EnsureCliparinoInCurrentSceneAsync(string currentScene) {
        try {
            currentScene = EnsureSceneIsNotNullOrEmpty(currentScene);

            await EnsureCliparinoInSceneAsync(currentScene);
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error in {nameof(EnsureCliparinoInCurrentSceneAsync)}: {ex.Message}");
        }
    }

    /// <summary>
    ///     Ensures that the provided scene name is not <c>null</c>, empty, or whitespace. If the input is
    ///     invalid, attempts to retrieve the name of the current active scene from OBS. If no valid scene
    ///     can be determined, logs a warning and throws an exception.
    /// </summary>
    /// <param name="currentScene">
    ///     The name of the scene to verify. This may be <c>null</c>, empty, or contain only whitespace.
    /// </param>
    /// <returns>
    ///     The name of the valid scene. If the input scene is invalid but the current OBS scene is
    ///     retrievable, that scene name is returned.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when no valid scene name can be determined.
    /// </exception>
    /// <remarks>
    ///     This method first validates the provided <paramref name="currentScene" />. If it is invalid,
    ///     the method queries the current active scene from OBS. If no valid scene is found, a warning is
    ///     logged and an exception is raised to indicate a critical failure.
    /// </remarks>
    private string EnsureSceneIsNotNullOrEmpty(string currentScene) {
        if (string.IsNullOrWhiteSpace(currentScene)) currentScene = CPH.ObsGetCurrentScene();

        if (!string.IsNullOrWhiteSpace(currentScene)) return currentScene;

        Log(LogLevel.Warn, "Current scene is empty or null.");

        throw new InvalidOperationException("Current scene is required.");
    }

    /// <summary>
    ///     Ensures that the clip browser source, represented by a specific OBS source, is hidden in the
    ///     currently active scene within OBS.
    /// </summary>
    /// <remarks>
    ///     This method interacts with OBS via Streamer.bot's scripting API. It checks for the presence of
    ///     the "Cliparino" source and ensures it is set to a hidden state in the current scene. If the
    ///     source is missing, no action is performed beyond logging.
    /// </remarks>
    /// <exception cref="Exception">
    ///     Thrown if OBS-related functionality encounters an issue such as being disconnected or if the
    ///     source cannot be identified.
    /// </exception>
    private void EnsureClipSourceHidden() {
        var currentScene = CPH.ObsGetCurrentScene();
        const string clipSourceName = "Cliparino";

        Log(LogLevel.Info,
            EnsureSourceExistsAndIsVisible(currentScene, clipSourceName, false)
                ? $"{nameof(EnsureClipSourceHidden)} reports {clipSourceName} is visible."
                : $"{nameof(EnsureClipSourceHidden)} reports {clipSourceName} is hidden.");
    }

    /// <summary>
    ///     Hosts a Twitch clip in the configured OBS scene by setting up the necessary scene settings and
    ///     processing the clip metadata asynchronously.
    /// </summary>
    /// <param name="clipData">
    ///     The clip data object containing metadata about the clip, such as the title and creator's name.
    /// </param>
    /// <param name="url">
    ///     The URL of the Twitch clip to host.
    /// </param>
    /// <param name="width">
    ///     The desired width for the clip's browser source within the OBS scene.
    /// </param>
    /// <param name="height">
    ///     The desired height for the clip's browser source within the OBS scene.
    /// </param>
    /// <returns>
    ///     A task that represents the asynchronous operation of hosting the clip.
    /// </returns>
    /// <remarks>
    ///     This method interacts with the OBS scene to set up a browser source for displaying the Twitch
    ///     clip. If the current OBS scene cannot be determined, the method logs a warning and aborts the
    ///     setup. The clip processing flow includes modifying the scene and applying the clip data.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if the method encounters an invalid state during clip processing, such as an invalid
    ///     clip URL or missing clip metadata.
    /// </exception>
    /// <seealso cref="PrepareSceneForClipHostingAsync" />
    /// <seealso cref="ProcessAndHostClipDataAsync(string, ClipData)" />
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

    /// <summary>
    ///     Ensures that the "Cliparino" source is present in the specified scene. If the source does not
    ///     exist in the scene, it will be added.
    /// </summary>
    /// <param name="currentScene">
    ///     The name of the current scene where the "Cliparino" source should be validated or added. This
    ///     cannot be null or empty and must refer to an existing scene in OBS.
    /// </param>
    /// <remarks>
    ///     This method checks the presence of the "Cliparino" source within the specified scene. If the
    ///     "Cliparino" source is absent, the method adds it programmatically. Otherwise, it logs a debug
    ///     message indicating the source already exists.
    /// </remarks>
    /// <exception cref="ArgumentException">
    ///     Thrown when the <paramref name="currentScene" /> parameter is null or empty.
    /// </exception>
    /// <exception cref="Exception">
    ///     Thrown if an error occurs while interacting with the streaming software API or modifying the
    ///     scene's sources.
    /// </exception>
    /// <seealso cref="EnsureSceneIsNotNullOrEmpty" />
    /// <seealso cref="AddCliparinoSourceToSceneAsync" />
    private async Task EnsureCliparinoInSceneAsync(string currentScene) {
        try {
            if (!IsCliparinoSourceInScene(currentScene, CliparinoSourceName))
                await AddCliparinoSourceToSceneAsync(currentScene, CliparinoSourceName);
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error in {nameof(EnsureCliparinoInSceneAsync)}: {ex.Message}");
        }
    }

    /// <summary>
    ///     Determines whether the specified source is present in the given OBS scene.
    /// </summary>
    /// <param name="scene">
    ///     The name of the OBS scene to check for the source presence.
    /// </param>
    /// <param name="sourceName">
    ///     The name of the source to check for presence within the specified scene.
    /// </param>
    /// <returns>
    ///     A boolean value indicating whether the specified source is present in the provided scene.
    ///     Returns <c>true</c> if the source is present; otherwise, <c>false</c>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown if <paramref name="scene" /> or <paramref name="sourceName" /> is <c>null</c> or empty.
    /// </exception>
    private bool IsCliparinoSourceInScene(string scene, string sourceName) {
        if (string.IsNullOrWhiteSpace(scene))
            throw new ArgumentNullException(nameof(scene), "Scene name cannot be null or empty.");

        if (string.IsNullOrWhiteSpace(sourceName))
            throw new ArgumentNullException(nameof(sourceName), "Source name cannot be null or empty.");

        try {
            var sourceExistsInScene = SourceExistsInScene(scene, sourceName);

            if (sourceExistsInScene) {
                Log(LogLevel.Info, $"{nameof(IsCliparinoSourceInScene)} reports source is in scene.");

                return true;
            }

            Log(LogLevel.Warn, $"Cliparino source is not in scene '{scene}'.");

            return false;
        } catch (Exception ex) {
            Log(LogLevel.Error, $"{CreateErrorPreamble()} {ex.Message}.");

            return false;
        }
    }

    /// <summary>
    ///     Adds a Cliparino source to the specified OBS scene asynchronously. This method ensures the
    ///     specified source is integrated into the provided scene if it is not already present.
    /// </summary>
    /// <param name="scene">
    ///     The name of the OBS scene where the Cliparino source should be added. This parameter
    ///     corresponds to the target scene's identifier in OBS.
    /// </param>
    /// <param name="sourceName">
    ///     The name of the Cliparino source to be added to the specified scene. This represents the
    ///     identifier OBS uses to manage sources in a scene.
    /// </param>
    /// <returns>
    ///     A <see cref="Task" /> object representing the asynchronous operation. The task completes when
    ///     the source has been successfully added to the scene or if the operation encounters an error.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if the scene or source name is invalid, or if the operation is performed on an invalid
    ///     OBS or scene state.
    /// </exception>
    /// <remarks>
    ///     This method leverages asynchronous execution to offload the source addition operation, avoiding
    ///     potential UI thread blocking. Ensure that the provided scene and source name match existing
    ///     entries in the OBS configuration to avoid runtime errors.
    /// </remarks>
    private async Task AddCliparinoSourceToSceneAsync(string scene, string sourceName) {
        await Task.Run(() => AddSceneSource(scene, sourceName));

        Log(LogLevel.Info, $"Cliparino source added to scene '{scene}'.");
    }

    /// <summary>
    ///     Prepares the OBS scene for hosting Twitch clip playback by ensuring the necessary scene and
    ///     source configurations are set up and ready for use.
    /// </summary>
    /// <remarks>
    ///     This method performs essential setup tasks for the OBS scene, including verifying the presence
    ///     of the "Cliparino" scene, ensuring the "Player" source within the scene is visible, and
    ///     configuring audio playback settings. It is called as part of the clip-hosting pipeline.
    /// </remarks>
    /// <returns>
    ///     A task representing the asynchronous operation to prepare the scene.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if required scene or source configurations cannot be prepared or validated.
    /// </exception>
    private async Task PrepareSceneForClipHostingAsync() {
        Log(LogLevel.Info, $"Preparing scene '{CliparinoSourceName}' for clip hosting.");

        if (!SceneExists(CliparinoSourceName)) CreateScene(CliparinoSourceName);

        EnsurePlayerSourceIsVisible(CliparinoSourceName, PlayerSourceName);

        await ConfigureAudioForPlayerSourceAsync();
    }

    /// <summary>
    ///     Ensures that a specified source within a given scene is visible. If the source does not exist,
    ///     it adds the source as a browser source and sets it to the default URL.
    /// </summary>
    /// <param name="sceneName">
    ///     The name of the scene in which the source visibility is to be ensured.
    /// </param>
    /// <param name="sourceName">
    ///     The name of the source to verify or add.
    /// </param>
    /// <remarks>
    ///     This method first checks whether the source exists and is visible within the provided scene. If
    ///     the source is absent, it adds a browser source with the default URL and ensures visibility.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when either <paramref name="sceneName" /> or <paramref name="sourceName" /> is
    ///     <c>null</c> or empty.
    /// </exception>
    private void EnsurePlayerSourceIsVisible(string sceneName, string sourceName) {
        if (CPH.ObsIsSourceVisible(sceneName, sourceName)) return;

        Log(LogLevel.Info, $"Setting source '{sourceName}' to visible in scene '{sceneName}'.");
        CPH.ObsSetSourceVisibility(sceneName, sourceName, true);

        Log(LogLevel.Warn, $"The source '{sourceName}' does not exist in scene '{sceneName}'. Attempting to add.");
        AddBrowserSource(sceneName, sourceName, "http://localhost:8080/index.htm");
    }

    /// <summary>
    ///     Processes and hosts clip data asynchronously. This method validates the clip data (if not
    ///     already provided), processes it, and sets up appropriate resources to host the clip in the OBS
    ///     environment.
    /// </summary>
    /// <param name="clipUrl">
    ///     The URL of the clip to be processed and hosted. Must be a valid non-empty string representing a
    ///     Twitch clip URL.
    /// </param>
    /// <param name="clipData">
    ///     An instance of <see cref="ClipData" /> containing pre-fetched clip information. Can be
    ///     <c>null</c>, in which case the method will attempt to fetch and validate clip data using the
    ///     provided <paramref name="clipUrl" />.
    /// </param>
    /// <remarks>
    ///     This method attempts to process available clip data and then initiate the hosting process in
    ///     the context of OBS browser sources. If no clip data is provided, it uses the clip URL to fetch
    ///     data. Logs errors if validation fails or an unexpected exception occurs.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="clipUrl" /> is <c>null</c> or an empty string.
    /// </exception>
    /// <exception cref="Exception">
    ///     Logs any uncaught exceptions that occur during execution to aid with debugging. Does not
    ///     rethrow the exception as the error is logged for script execution consistency.
    /// </exception>
    /// <returns>
    ///     A task representing the asynchronous operation. Tasks complete after the clip has been
    ///     processed successfully or an error is logged.
    /// </returns>
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

    /// <summary>
    ///     Ensures that a specified source exists in the given scene and optionally makes it visible. If
    ///     the source does not exist, it attempts to add the source to the scene. Optionally, sets the
    ///     visibility of the source to the provided value.
    /// </summary>
    /// <param name="sceneName">
    ///     The name of the scene in which to check or add the source.
    /// </param>
    /// <param name="sourceName">
    ///     The name of the source to ensure exists and is optionally made visible.
    /// </param>
    /// <param name="setVisible">
    ///     Indicates whether the source should be set to visible. Defaults to <c>true</c>.
    /// </param>
    /// <returns>
    ///     A boolean indicating the success of the operation. Returns <c>true</c> if the source exists or
    ///     was successfully added, and (where applicable) visibility was configured successfully. Returns
    ///     <c>false</c> if the source could not be added or configured.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="sceneName" /> or <paramref name="sourceName" /> is <c>null</c>.
    /// </exception>
    /// <remarks>
    ///     This method is commonly used to manage scene and source consistency for OBS, ensuring that
    ///     critical sources are present and appropriately visible during execution.
    /// </remarks>
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

        if (CPH.ObsIsSourceVisible(sceneName, sourceName)) return true;

        Log(LogLevel.Info, $"Setting source '{sourceName}' to visible in scene '{sceneName}'.");
        CPH.ObsSetSourceVisibility(sceneName, sourceName, true);

        return true;
    }

    /// <summary>
    ///     Sets the browser source for a specified OBS scene or the current active scene to display
    ///     content from the given base URL.
    /// </summary>
    /// <param name="baseUrl">
    ///     The base URL to set as the browser source. This URL is transformed into the applicable format
    ///     for the source.
    /// </param>
    /// <param name="targetScene">
    ///     The name of the OBS scene to update. If <c>null</c>, the currently active scene is targeted
    ///     instead.
    /// </param>
    /// <remarks>
    ///     This method updates or adds a browser source to the specified OBS scene. If the target scene is
    ///     not provided and cannot be determined, an <see cref="InvalidOperationException" /> is thrown.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the target scene is not provided and the current active scene cannot be retrieved.
    /// </exception>
    private void SetBrowserSource(string baseUrl, string targetScene = null) {
        Log(LogLevel.Debug, $"SetBrowserSource was called for URL '{baseUrl}'.");

        var sourceUrl = CreateSourceUrl(baseUrl);
        if (targetScene == null) targetScene = CPH.ObsGetCurrentScene();

        if (string.IsNullOrEmpty(targetScene)) throw new InvalidOperationException("Unable to retrieve target scene.");

        UpdateOrAddBrowserSource(targetScene, sourceUrl, "Cliparino", baseUrl);
    }

    /// <summary>
    ///     Updates an existing browser source in the specified OBS scene or adds a new browser source if
    ///     it does not exist. Handles visibility settings when <paramref name="baseUrl" /> is set to
    ///     "about:blank".
    /// </summary>
    /// <param name="targetScene">
    ///     The target OBS scene where the browser source should be updated or added.
    /// </param>
    /// <param name="sourceUrl">
    ///     The URL to be set for the browser source.
    /// </param>
    /// <param name="sourceName">
    ///     The name of the browser source to update or add.
    /// </param>
    /// <param name="baseUrl">
    ///     The base URL that is used to determine visibility behavior for the browser source.
    /// </param>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if the target OBS scene cannot be retrieved or updated appropriately.
    /// </exception>
    /// <remarks>
    ///     This method ensures that the appropriate browser source exists in the scene and applies
    ///     visibility settings based on the value of <paramref name="baseUrl" />.
    /// </remarks>
    private void UpdateOrAddBrowserSource(string targetScene, string sourceUrl, string sourceName, string baseUrl) {
        if (!SourceExistsInScene(targetScene, sourceName)) {
            AddSceneSource(targetScene, sourceName);
            Log(LogLevel.Info, $"Added '{sourceName}' scene source to '{targetScene}'.");
        } else {
            UpdateBrowserSource(targetScene, sourceName, sourceUrl);

            if (baseUrl != "about:blank") return;

            Log(LogLevel.Info, "Hiding Cliparino source after setting 'about:blank'.");
            CPH.ObsSetSourceVisibility(targetScene, sourceName, false);
        }
    }

    /// <summary>
    ///     Refreshes the browser source with the name "Player" via an OBS WebSocket command. This method
    ///     sends a "PressInputPropertiesButton" request to OBS, targeting the "refreshnocache" property of
    ///     the "Player" browser source input to forcefully reload its content.
    /// </summary>
    /// <remarks>
    ///     This method uses OBS WebSocket API to refresh the state of the browser source without changing
    ///     its URL, enabling updates for dynamic content. The "refreshnocache" property is a standard
    ///     mechanism for forcing browser source refreshes in OBS.
    /// </remarks>
    /// <exception cref="Exception">
    ///     Thrown if the OBS WebSocket request fails or returns an error response.
    /// </exception>
    /// <seealso cref="ConfigureBrowserSource(string, string, string)" />
    /// <seealso cref="UpdateBrowserSource(string, string, string)" />
    private void RefreshBrowserSource() {
        var payload = new {
            requestType = "PressInputPropertiesButton",
            requestData = new { inputName = "Player", propertyName = "refreshnocache" }
        };

        var response = CPH.ObsSendRaw(payload.requestType, JsonConvert.SerializeObject(payload.requestData));
        Log(LogLevel.Info, $"Refreshed browser source 'Player'. Response: {response}");
    }

    /// <summary>
    ///     Updates the specified browser source in OBS with a new URL. Adjusts the browser source's
    ///     settings based on the provided parameters and logs the operation's status.
    /// </summary>
    /// <param name="sceneName">
    ///     The name of the scene containing the browser source to update.
    /// </param>
    /// <param name="sourceName">
    ///     The name of the browser source to update within the specified scene.
    /// </param>
    /// <param name="url">
    ///     The new URL to be set for the browser source.
    /// </param>
    /// <exception cref="System.Exception">
    ///     Thrown if an error occurs while interacting with OBS or updating the browser source settings.
    /// </exception>
    /// <remarks>
    ///     This method sends a raw request to OBS to update the input settings of a browser source. It
    ///     logs detailed messages about the operation and ensures visibility changes if necessary. If the
    ///     URL is set to "about:blank," further operations like hiding the source may be performed.
    /// </remarks>
    /// <seealso cref="IInlineInvokeProxy.ObsSendRaw(string, string, int)" />
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

    /// <summary>
    ///     Configures the audio settings for the Player source. This includes modifying monitor type,
    ///     input volume, and applying audio filters such as Gain and Compressor.
    /// </summary>
    /// <remarks>
    ///     This method uses OBS WebSocket API calls to set audio properties for the Player source. If any
    ///     step fails, appropriate warnings or errors are logged, and the method completes the operation
    ///     without throwing exceptions.
    /// </remarks>
    /// <returns>
    ///     A <see cref="Task" /> representing the asynchronous operation. This method completes without
    ///     returning additional data or propagating exceptions.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if there is a critical error in OBS WebSocket configuration, or if required
    ///     configurations are not valid (not currently used but should be accounted for in extended
    ///     implementations).
    /// </exception>
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

    /// <summary>
    ///     Generates the payload required to configure a compressor filter for the OBS input source named
    ///     "Player".
    /// </summary>
    /// <remarks>
    ///     This method creates an <see cref="IPayload" /> object containing the necessary parameters to
    ///     set the compressor filter. These settings include threshold, ratio, attack, release, and makeup
    ///     gain values.
    /// </remarks>
    /// <returns>
    ///     An <see cref="IPayload" /> object containing the request type and relevant settings for
    ///     configuring the compressor filter on the "Player" input source.
    /// </returns>
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

    /// <summary>
    ///     Generates a payload to set the gain filter for the 'Player' audio source in OBS.
    /// </summary>
    /// <param name="gainValue">
    ///     The desired gain value to be applied to the 'Player' audio source. Represents the amount of
    ///     audio gain to be set.
    /// </param>
    /// <returns>
    ///     An instance of <see cref="IPayload" /> that contains the necessary properties to apply the gain
    ///     filter settings in OBS.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     Thrown if the <paramref name="gainValue" /> is outside the acceptable range defined by OBS.
    /// </exception>
    /// <remarks>
    ///     This method constructs and returns a payload in the format expected by OBS to adjust audio
    ///     gain. It is intended for internal use when configuring audio settings programmatically.
    /// </remarks>
    private static IPayload GenerateGainFilterPayload(double gainValue) {
        return new Payload {
            RequestType = "SetInputSettings",
            RequestData = new { inputName = "Player", inputSettings = new { gain = gainValue } }
        };
    }

    /// <summary>
    ///     Creates a payload for setting the input volume of a specified source in OBS.
    /// </summary>
    /// <param name="volumeValue">
    ///     The volume level to set for the input source. This value should be within the acceptable range
    ///     for OBS input volume settings.
    /// </param>
    /// <returns>
    ///     An object implementing <see cref="IPayload" /> that contains the request type and data required
    ///     for the OBS WebSocket API to set the input volume for the "Player" source.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown if the <paramref name="volumeValue" /> is outside the acceptable range defined by OBS.
    /// </exception>
    /// <remarks>
    ///     The payload is designed to work with the OBS WebSocket API and includes the request type
    ///     "SetInputVolume" along with the input source name "Player" and the volume value to be applied.
    ///     This method assumes the caller has already validated the input volume value to avoid runtime
    ///     errors.
    /// </remarks>
    /// <seealso cref="Payload" />
    /// <seealso cref="IPayload" />
    private static IPayload GenerateSetInputVolumePayload(double volumeValue) {
        return new Payload {
            RequestType = "SetInputVolume",
            RequestData = new { inputName = "Player", inputSettings = new { volume = volumeValue } }
        };
    }

    /// <summary>
    ///     Generates a payload to set the audio monitoring type for a specific input in OBS.
    /// </summary>
    /// <param name="monitorType">
    ///     The desired monitor type for the audio source. Valid values depend on the OBS settings (e.g.,
    ///     "monitorOff", "monitorOnly", or "monitorAndOutput").
    /// </param>
    /// <returns>
    ///     An instance of <see cref="IPayload" /> containing the request type and data required to
    ///     configure the audio monitoring type in OBS.
    /// </returns>
    /// <remarks>
    ///     This method constructs the payload used for communicating with the OBS WebSocket to change the
    ///     audio monitoring settings of the audio input named "Player".
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    ///     Thrown if <paramref name="monitorType" /> is null.
    /// </exception>
    private static IPayload GenerateSetAudioMonitorTypePayload(string monitorType) {
        return new Payload {
            RequestType = "SetAudioMonitorType",
            RequestData = new { inputName = "Player", inputSettings = new { monitorType } }
        };
    }

    /// <summary>
    ///     Retrieves the ID of a specific source within a given OBS scene. This is used to find and
    ///     interact with specific items in the OBS scene graph programmatically.
    /// </summary>
    /// <param name="sceneName">
    ///     The name of the OBS scene containing the source.
    /// </param>
    /// <param name="sourceName">
    ///     The name of the source within the specified scene for which the ID is to be retrieved.
    /// </param>
    /// <returns>
    ///     An integer representing the unique identifier of the specified scene item if it exists. Returns
    ///     <c>-1</c> if the item cannot be found or an error occurs.
    /// </returns>
    /// <exception cref="Exception">
    ///     Thrown when an error occurs during the OBS request operation.
    /// </exception>
    /// <remarks>
    ///     This method communicates with OBS via Streamer.bot's OBSRaw API. If the specified source does
    ///     not exist in the scene or an invalid scene/source name is provided, the method will log a
    ///     warning message and return <c>-1</c>.
    /// </remarks>
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

    /// <summary>
    ///     Adds a source to a specified OBS scene using the Streamer.bot OBS integration. If the source
    ///     does not exist in the scene, it will be created and enabled.
    /// </summary>
    /// <param name="targetScene">
    ///     The name of the scene in which the source should be added. This represents an OBS scene as
    ///     recognized in the OBS application.
    /// </param>
    /// <param name="sourceName">
    ///     The name of the source to be added to the specified scene. This represents an existing source
    ///     configured within OBS.
    /// </param>
    /// <remarks>
    ///     This method uses the Streamer.bot API for communication with OBS, issuing a raw request to add
    ///     the source to the specified scene. The source will be enabled and ready for immediate use upon
    ///     successful execution.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    ///     Thrown if either <paramref name="targetScene" /> or <paramref name="sourceName" /> is null or
    ///     empty.
    /// </exception>
    /// <seealso cref="IInlineInvokeProxy.ObsSendRaw(string, string, int)" />
    private void AddSceneSource(string targetScene, string sourceName) {
        var payload = new Payload {
            RequestType = "CreateSceneItem",
            RequestData = new { sceneName = targetScene, sourceName, sceneItemEnabled = true }
        };

        CPH.ObsSendRaw(payload.RequestType, JsonConvert.SerializeObject(payload.RequestData));
    }

    /// <summary>
    ///     Determines whether a specified source exists within a given scene in OBS.
    /// </summary>
    /// <param name="sceneName">
    ///     The name of the OBS scene to search for the source.
    /// </param>
    /// <param name="sourceName">
    ///     The name of the source to check within the specified scene.
    /// </param>
    /// <returns>
    ///     <c>true</c> if the source exists in the specified scene; otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    ///     This method retrieves the ID of the specified source from the scene using
    ///     <see cref="GetSceneItemId" />. If the retrieved ID is not equal to <c>-1</c>, the source is
    ///     considered to exist. Otherwise, it is considered missing.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    ///     Thrown if <paramref name="sceneName" /> or <paramref name="sourceName" /> is <c>null</c> or
    ///     empty.
    /// </exception>
    /// <seealso cref="EnsureSourceExistsAndIsVisible" />
    /// <seealso cref="UpdateOrAddBrowserSource" />
    private bool SourceExistsInScene(string sceneName, string sourceName) {
        var sceneItemId = GetSceneItemId(sceneName, sourceName);

        return sceneItemId != -1;
    }

    /// <summary>
    ///     Determines whether a specific OBS scene exists by querying the OBS WebSocket server.
    /// </summary>
    /// <param name="sceneName">
    ///     The name of the scene to check for existence.
    /// </param>
    /// <returns>
    ///     <c>true</c> if the scene with the specified <paramref name="sceneName" /> exists; otherwise,
    ///     <c>false</c>.
    /// </returns>
    /// <exception cref="JsonSerializationException">
    ///     Thrown when an error occurs during the deserialization of the OBS WebSocket response.
    /// </exception>
    /// <exception cref="Exception">
    ///     Thrown when an unexpected error occurs during the method execution, such as communication
    ///     issues with OBS.
    /// </exception>
    /// <remarks>
    ///     This method uses the OBS WebSocket API to retrieve a list of scenes and checks whether the
    ///     specified scene is present in the list. If the scene does not exist, a warning message is
    ///     logged. In the event of an exception, an error message is logged, and <c>false</c> is returned.
    /// </remarks>
    private bool SceneExists(string sceneName) {
        try {
            var sceneExists = false;
            var response = JsonConvert.DeserializeObject<dynamic>(CPH.ObsSendRaw("GetSceneList", "{}"));

            Log(LogLevel.Debug, $"The result of the scene list request was {JsonConvert.SerializeObject(response)}");

            var scenes = response?.scenes;

            Log(LogLevel.Debug, $"Scenes pulled from OBS: {JsonConvert.SerializeObject(scenes)}");

            if (scenes != null)
                foreach (var scene in scenes) {
                    if ((string)scene.sceneName != sceneName) continue;

                    sceneExists = true;

                    break;
                }

            if (!sceneExists) Log(LogLevel.Warn, $"Scene '{sceneName}' does not exist.");

            Log(LogLevel.Debug, $"Scene {sceneName} exists: {sceneExists}");

            return sceneExists;
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error in SceneExists: {ex.Message}");

            return false;
        }
    }

    /// <summary>
    ///     Creates a new scene in OBS Studio if it does not already exist, using the given scene name.
    /// </summary>
    /// <param name="sceneName">
    ///     The name of the scene to be created. This must be a valid scene name in OBS Studio.
    /// </param>
    /// <remarks>
    ///     This method uses raw OBS WebSocket communication to request the creation of the specified
    ///     scene. If the scene already exists, no action is taken and no new scene is created. If the
    ///     scene creation fails, an error message is logged in the Streamer.bot log output.
    /// </remarks>
    /// <exception cref="Exception">
    ///     Thrown when an error occurs during the OBS WebSocket communication process. The exception
    ///     message contains details about the failure.
    /// </exception>
    private void CreateScene(string sceneName) {
        try {
            var payload = new Payload { RequestType = "CreateScene", RequestData = new { sceneName } };

            CPH.ObsSendRaw(payload.RequestType, JsonConvert.SerializeObject(payload.RequestData));
            Log(LogLevel.Info, $"Scene '{sceneName}' created successfully.");
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error in CreateScene: {ex.Message}");
        }
    }

    /// <summary>
    ///     Adds a browser source to a specified scene in OBS. This method creates a new browser source in
    ///     the given scene with a specified name and URL if it does not already exist.
    /// </summary>
    /// <param name="sceneName">
    ///     The name of the scene to which the browser source will be added. This corresponds to the scene
    ///     defined within the OBS Studio environment.
    /// </param>
    /// <param name="sourceName">
    ///     The name of the browser source to be added to the specified scene. This must be unique within
    ///     the given scene.
    /// </param>
    /// <param name="url">
    ///     The URL to be used for the browser source. Defaults to "about:blank" if no URL is specified.
    /// </param>
    /// <remarks>
    ///     Uses the Streamer.bot OBS integration and sends a raw request to OBS to create the specified
    ///     browser source. If an error occurs, it is caught and logged for diagnostic purposes.
    /// </remarks>
    /// <exception cref="Exception">
    ///     Thrown if the OBS integration fails to process the request or if there are issues creating the
    ///     browser source.
    /// </exception>
    private void AddBrowserSource(string sceneName, string sourceName, string url = "about:blank") {
        //TODO: Consider a new class that wraps Cliparino scene and keeps track of the state
        try {
            var payload = new Payload {
                RequestType = "CreateSource", RequestData = new { sceneName, sourceName, url, type = "browser_source" }
            };

            var response = CPH.ObsSendRaw(payload.RequestType, JsonConvert.SerializeObject(payload.RequestData));
            Log(LogLevel.Info,
                $"Browser source '{sourceName}' added to scene '{sceneName}' with URL '{url}'.\nResponse: {response}");
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error in AddBrowserSource: {ex.Message}");
        }
    }

    /// <summary>
    ///     Generates a formatted URL string for embedding a Twitch clip into a browser source. The method
    ///     appends the given <paramref name="clipUrl" /> to the Twitch player embed base URL with autoplay
    ///     enabled, suitable for use in media playback scenarios.
    /// </summary>
    /// <param name="clipUrl">
    ///     The unique identifier for the Twitch clip. Must be a valid, non-empty string representing the
    ///     Twitch clip URL or clip identifier needed by the Twitch embed player.
    /// </param>
    /// <returns>
    ///     A string containing the full Twitch embedding URL with necessary query parameters, such as
    ///     "autoplay=true".
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when the <paramref name="clipUrl" /> is null or empty.
    /// </exception>
    /// <remarks>
    ///     The returned URL is used in scenarios such as updating an OBS browser source for Twitch clip
    ///     playback. Ensure a valid URL is passed; otherwise, the generated embed URL might not function
    ///     correctly. This method is part of the core functionality for embedding Twitch clips within the
    ///     Streamer.bot environment.
    /// </remarks>
    private static string CreateSourceUrl(string clipUrl) {
        return $"https://player.twitch.tv/?clip={clipUrl}&autoplay=true";
    }

    #endregion

    #region Twitch API Interaction

    /// <summary>
    ///     Provides functionality to interact with the Twitch API, including retrieving clip and game
    ///     information.
    /// </summary>
    /// <remarks>
    ///     This class serves as a client to perform authenticated requests to the Twitch API using a
    ///     specified HTTP client, OAuth credentials, and a logging mechanism.
    /// </remarks>
    private class TwitchApiClient {
        private readonly string _authToken;
        private readonly string _clientId;
        private readonly HttpClient _httpClient;
        private readonly LogDelegate _log;

        /// <summary>
        ///     Represents a client for interacting with the Twitch API. Provides methods for fetching Twitch
        ///     data such as clips and game information.
        /// </summary>
        /// <remarks>
        ///     This class requires both a valid Twitch client ID and an OAuth token to authenticate API
        ///     requests. It is initialized with an <see cref="HttpClient" />, OAuth information, and a logging
        ///     delegate for capturing important events or errors during execution. The base URL for all Twitch
        ///     API requests is set to "https://api.twitch.tv/helix/".
        /// </remarks>
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

        /// <summary>
        ///     Configures the HTTP request headers for the <see cref="TwitchApiClient" /> instance.
        /// </summary>
        /// <remarks>
        ///     This method initializes and sets the necessary headers, including the `Client-ID` and
        ///     `Authorization` tokens, required for communicating with the Twitch API.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        ///     Thrown if the method is called before the required client credentials (_clientId or _authToken)
        ///     are initialized.
        /// </exception>
        private void ConfigureHttpRequestHeaders() {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Client-ID", _clientId);
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_authToken}");
        }

        /// <summary>
        ///     Sends an asynchronous HTTP GET request to a specified endpoint and processes the response.
        /// </summary>
        /// <param name="endpoint">
        ///     The relative path of the API endpoint to which the HTTP request will be sent. This parameter
        ///     must not be null, empty, or whitespace.
        /// </param>
        /// <param name="completeUrl">
        ///     The full URL of the API endpoint, including the base address and the provided endpoint path.
        ///     This is used for logging and debugging purposes.
        /// </param>
        /// <returns>
        ///     A <c>string</c> containing the response content if the request is successful; otherwise, <c>null</c> if
        ///     the request fails or returns an unsuccessful status code.
        /// </returns>
        /// <exception cref="HttpRequestException">
        ///     Thrown when there is an HTTP protocol-level error, such as a connection failure or DNS
        ///     resolution issue.
        /// </exception>
        /// <remarks>
        ///     This method sets up headers for the HTTP request using
        ///     <see cref="ConfigureHttpRequestHeaders" />, sends a GET request using
        ///     <see cref="HttpClient.GetAsync(string)" />, and processes the response content. Logging is
        ///     enabled at various levels to provide insight into the request and response processing.
        /// </remarks>
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

        /// <summary>
        ///     Sends an asynchronous GET request to the specified Twitch API endpoint and attempts to
        ///     deserialize the response into an object of type <typeparamref name="T" />.
        /// </summary>
        /// <typeparam name="T">
        ///     The type to which the API response should be deserialized.
        /// </typeparam>
        /// <param name="endpoint">
        ///     The relative URI of the Twitch API endpoint to query. It cannot be null, empty, or whitespace.
        /// </param>
        /// <returns>
        ///     An instance of type <typeparamref name="T" /> containing the deserialized data if the request
        ///     succeeds, or the <c>default</c> value of <typeparamref name="T" /> if the request fails or no data is returned.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     Thrown when the <paramref name="endpoint" /> is null, empty, or consists only of whitespace.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        ///     Thrown when the OAuth token for Twitch API access is null, empty, or invalid.
        /// </exception>
        /// <remarks>
        ///     This method constructs the full URL by combining the base address of the HTTP client with the
        ///     supplied <paramref name="endpoint" />. It sends the GET request asynchronously, logs the
        ///     process for debugging purposes, and processes any exceptions (e.g., network errors or JSON
        ///     deserialization errors).
        /// </remarks>
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

        /// <summary>
        ///     Fetches clip data from Twitch using the provided clip ID.
        /// </summary>
        /// <param name="clipId">
        ///     The unique identifier of the Twitch clip to fetch. This value must not be null, empty, or
        ///     contain only whitespace.
        /// </param>
        /// <returns>
        ///     A <see cref="ClipData" /> object representing the clip information retrieved from the Twitch
        ///     API.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     Thrown if <paramref name="clipId" /> is null or empty.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        ///     Thrown if the OAuth token is missing or invalid.
        /// </exception>
        /// <exception cref="JsonException">
        ///     Thrown if there is an error in deserializing the response from the Twitch API.
        /// </exception>
        /// <exception cref="Exception">
        ///     Thrown if any unexpected error occurs while fetching data from the Twitch API.
        /// </exception>
        /// <remarks>
        ///     This method constructs a request to the Twitch API endpoint for clip data using the specified
        ///     clip ID. It relies on an instance of <see cref="HttpClient" /> that holds the base URL of the
        ///     Twitch Helix API and OAuth credentials.
        /// </remarks>
        /// <seealso cref="TwitchApiClient.FetchDataAsync{T}(string)" />
        public Task<ClipData> FetchClipById(string clipId) {
            return FetchDataAsync<ClipData>($"clips?id={clipId}");
        }

        /// <summary>
        ///     Fetches detailed information about a game from the Twitch API using its unique identifier.
        /// </summary>
        /// <param name="gameId">
        ///     The unique identifier of the game to be retrieved. This parameter cannot be null, empty, or
        ///     whitespace.
        /// </param>
        /// <returns>
        ///     A <see cref="GameInfo" /> object containing information about the specified game if successful;
        ///     otherwise, <c>null</c>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     Thrown when the <paramref name="gameId" /> parameter is null, empty, or whitespace.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        ///     Thrown if the Twitch OAuth token is missing or invalid.
        /// </exception>
        /// <exception cref="HttpRequestException">
        ///     Thrown if there is an error during the HTTP request to the Twitch API.
        /// </exception>
        /// <exception cref="JsonException">
        ///     Thrown if there is an error parsing the JSON response from the Twitch API.
        /// </exception>
        /// <remarks>
        ///     This method constructs the appropriate API request to retrieve game details from the Twitch API
        ///     and processes the response. The caller is responsible for handling null results which indicate
        ///     that the requested game information was not found or the request failed.
        /// </remarks>
        public Task<GameInfo> FetchGameById(string gameId) {
            return FetchDataAsync<GameInfo>($"games?id={gameId}");
        }
    }

    /// <summary>
    ///     Represents OAuth information necessary for authenticating API requests, providing the Twitch
    ///     client ID and OAuth token.
    /// </summary>
    private class OAuthInfo {
        /// <summary>
        ///     Represents OAuth authentication information required for accessing the Twitch API.
        /// </summary>
        /// <remarks>
        ///     This class encapsulates the necessary data to authenticate API requests, including the Twitch
        ///     Client ID and OAuth token issued for the corresponding application.
        /// </remarks>
        public OAuthInfo(string twitchClientId, string twitchOAuthToken) {
            TwitchClientId = twitchClientId
                             ?? throw new ArgumentNullException(nameof(twitchClientId), "Client ID cannot be null.");
            TwitchOAuthToken = twitchOAuthToken
                               ?? throw new ArgumentNullException(nameof(twitchOAuthToken),
                                                                  "OAuth token cannot be null.");
        }

        /// <summary>
        ///     Gets the Twitch Client ID used for authenticating API requests to the Twitch Helix API.
        /// </summary>
        /// <value>
        ///     A <see cref="string" /> representing the client ID for the Twitch API.
        /// </value>
        /// <remarks>
        ///     The client ID is a mandatory credential required by the Twitch API for verifying application
        ///     access. This property is immutable and must be provided during construction of
        ///     <see cref="OAuthInfo" />.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        ///     Throws if a null value is passed to the constructor of the <see cref="OAuthInfo" /> class.
        /// </exception>
        /// <seealso cref="OAuthInfo" />
        public string TwitchClientId { get; }

        /// <summary>
        ///     Gets the Twitch OAuth token used for authenticating API requests to Twitch.
        /// </summary>
        /// <value>
        ///     The OAuth token as a <see cref="string" />.
        /// </value>
        /// <remarks>
        ///     The OAuth token is required for making authorized requests to the Twitch API. This value must
        ///     not be null, empty, or contain only whitespace. It is validated during initialization.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        ///     Thrown when the OAuth token is null.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        ///     Thrown when the OAuth token is an empty or whitespace string.
        /// </exception>
        public string TwitchOAuthToken { get; }
    }

    /// <summary>
    ///     Represents a response received from the Twitch API that contains a collection of data items.
    ///     This class is generic and adapts to the specific type of data being returned by the API
    ///     endpoint.
    /// </summary>
    /// <typeparam name="T">
    ///     Type of the data item(s) contained within the response, representing the structure of the
    ///     returned data as defined by the Twitch API.
    /// </typeparam>
    public class TwitchApiResponse<T> {
        /// <summary>
        ///     Represents a generic response from Twitch API calls that returns a collection of objects of
        ///     type <typeparamref name="T" />.
        /// </summary>
        /// <typeparam name="T">
        ///     The type of objects contained in the <c>Data</c> property.
        /// </typeparam>
        public TwitchApiResponse(T[] data) {
            Data = data ?? Array.Empty<T>();
        }

        /// <summary>
        ///     Represents the data returned from the Twitch API as an array of objects of the specified type
        ///     <typeparamref name="T" />.
        /// </summary>
        /// <typeparam name="T">
        ///     The type of objects contained in the array returned by the API.
        /// </typeparam>
        /// <value>
        ///     Contains the deserialized data objects. If no data is returned, this property will hold an
        ///     empty array.
        /// </value>
        /// <remarks>
        ///     This property is designed to store the main content retrieved from a specific endpoint of the
        ///     Twitch API.
        /// </remarks>
        public T[] Data { get; }
    }

    /// <summary>
    ///     Represents game data including essential information such as the game ID and name.
    /// </summary>
    /// <remarks>
    ///     This class is primarily used for deserialization of JSON data retrieved from external APIs or
    ///     data sources containing information about games.
    /// </remarks>
    public class GameData {
        /// <summary>
        ///     The unique identifier associated with the entity.
        /// </summary>
        /// <value>
        ///     A <see cref="string" /> representing the unique identifier.
        /// </value>
        /// <remarks>
        ///     The <c>Id</c> property is commonly used to differentiate objects within a collection or to
        ///     reference entities in external APIs or databases.
        /// </remarks>
        [JsonProperty("id")]
        public string Id { get; set; }

        /// <summary>
        ///     The name of the game.
        /// </summary>
        /// <value>
        ///     A <c>string</c> representing the game's name.
        /// </value>
        [JsonProperty("name")]
        public string Name { get; set; }
    }

    /// <summary>
    ///     Asynchronously fetches the name of a game based on its game ID from the Twitch API. If the game
    ///     ID is null, empty, or invalid, or if an error occurs during the request, it will return
    ///     "Unknown Game".
    /// </summary>
    /// <param name="gameId">
    ///     The unique identifier of the game for which to retrieve the name. This value should not be
    ///     null, empty, or consist solely of whitespace.
    /// </param>
    /// <returns>
    ///     A string representing the name of the game, retrieved via the Twitch API. Returns "Unknown
    ///     Game" if the game ID is invalid, or if the API response is null, empty, or an error occurs.
    /// </returns>
    /// <exception cref="Exception">
    ///     May log and handle any exceptions that occur during the API request. Does not propagate
    ///     exceptions, as errors are handled internally and a default value of "Unknown Game" is returned
    ///     instead.
    /// </exception>
    /// <remarks>
    ///     This method relies on the <c>_twitchApiClient</c> field to make API requests and assumes the client is properly initialized and accessible.
    /// </remarks>
    /// <seealso cref="CPHInline.CreateAndHostClipPageAsync(string, string, string, string, ClipData)" />
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

    /// <summary>
    ///     Extracts specific details about a Twitch clip, including the streamer's name, the title of the
    ///     clip, and the name of the user who curated the clip.
    /// </summary>
    /// <param name="clipData">
    ///     An instance of <see cref="ClipData" /> containing information about the Twitch clip. This
    ///     parameter is used to retrieve various details about the clip.
    /// </param>
    /// <returns>
    ///     A tuple containing the streamer's name, the title of the clip, and the curator's name:
    ///     <list type="bullet">
    ///         <item>
    ///             <description>
    ///                 <c>StreamerName</c>: The name of the streamer who created the clip.
    ///             </description>
    ///         </item> <item>
    ///             <description>
    ///                 <c>ClipTitle</c>: The title of the clip provided by Twitch.
    ///             </description>
    ///         </item> <item>
    ///             <description>
    ///                 <c>CuratorName</c>: The name of the viewer who created or curated the clip, or a default value if
    ///                 unavailable.
    ///             </description>
    ///         </item>
    ///     </list>
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when the <paramref name="clipData" /> parameter is <c>null</c>.
    /// </exception>
    /// <remarks>
    ///     This method delegates its logic to the <see cref="GetClipInfo" /> method to fetch and format
    ///     the details. It provides a simplified way to retrieve key clip attributes for further
    ///     processing.
    /// </remarks>
    private static (string StreamerName, string ClipTitle, string CuratorName) ExtractClipInfo(ClipData clipData) {
        return GetClipInfo(clipData);
    }

    #endregion

    #region Server & Local Hosting

    /// <summary>
    ///     Semaphore used to synchronize access to server-related operations, ensuring that only one
    ///     thread can execute setup and token initialization procedures at a time.
    /// </summary>
    /// <remarks>
    ///     This semaphore is initialized with a maximum concurrency level of one, enforcing
    ///     single-threaded execution for critical server setup and management activities.
    /// </remarks>
    private static readonly SemaphoreSlim ServerLockSemaphore = new SemaphoreSlim(1, 1);

    /// <summary>
    ///     A thread-safe semaphore mechanism used to synchronize access to server-related operations
    ///     within the <see cref="CPHInline" /> class. Ensures that only one thread can access critical
    ///     sections of server setup, cleanup, or other operations requiring mutual exclusion at a time.
    /// </summary>
    /// <remarks>
    ///     Used in methods such as <see cref="ConfigureAndServe" />, <see cref="GetHtmlInMemorySafe" />,
    ///     and <see cref="CleanupServer" /> to prevent race conditions and manage concurrent access
    ///     effectively.
    /// </remarks>
    /// <threadsafety>
    ///     This semaphore is instantiated with an initial and maximum concurrency level of 1, ensuring
    ///     mutual exclusivity for all operations that use it.
    /// </threadsafety>
    private static readonly SemaphoreSlim ServerSemaphore = new SemaphoreSlim(1, 1);

    /// <summary>
    ///     Represents a semaphore mechanism named 'TokenSemaphore' used for the coordination of access to
    ///     shared resources within the Cliparino's multithreaded operations. This instance enforces a
    ///     concurrency limit of one thread at a time.
    /// </summary>
    /// <remarks>
    ///     TokenSemaphore is instantiated with an initial and maximum count of 1, thereby ensuring that
    ///     only one concurrent operation is allowed. It is designed for use cases where thread-safe
    ///     synchronization is critical, particularly during token-related operations and cancellation
    ///     logic.
    /// </remarks>
    private static readonly SemaphoreSlim TokenSemaphore = new SemaphoreSlim(1, 1);

    /// <summary>
    ///     Configures the server environment and initiates the required services for serving HTTP content.
    ///     Ensures proper cleanup and error handling during initialization.
    /// </summary>
    /// <remarks>
    ///     This method performs necessary server setup operations, including port validation, semaphore
    ///     handling, and server configuration. It handles exceptions to prevent residual server states and
    ///     logs any issues encountered during the setup process.
    /// </remarks>
    /// <returns>
    ///     A task representing the asynchronous operation. Returns <c>true</c> if the server was
    ///     successfully configured and is ready to serve; otherwise, <c>false</c>.
    /// </returns>
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

        Log(LogLevel.Debug, $"{nameof(ConfigureAndServe)} executed successfully: Server setup complete.");

        return true;
    }

    /// <summary>
    ///     Sets up the local HTTP server and initializes required Twitch OAuth tokens for handling
    ///     Cliparino functionality. Ensures server configuration, token semaphore setup, and browser
    ///     source configuration are completed successfully.
    /// </summary>
    /// <remarks>
    ///     This method initializes the server listener, prepares the token semaphore for managing
    ///     concurrent clip interactions, and configures the client-facing browser source. It is intended
    ///     for internal use during the server's startup sequence.
    /// </remarks>
    /// <returns>
    ///     A task that represents the asynchronous operation. The task will return <c>true</c> if the
    ///     server and tokens were successfully set up, or <c>false</c> if an error occurred during the process.
    /// </returns>
    private async Task SetupServerAndTokens() {
        using (new ScopedSemaphore(ServerLockSemaphore, Log)) {
            InitializeServer();

            if (!await SetupTokenSemaphore()) return;

            _listeningTask = StartListening(_server, _cancellationTokenSource.Token);
            ConfigureBrowserSource("Cliparino", "Player", "http://localhost:8080/index.htm");
        }
    }

    /// <summary>
    ///     Validates the availability of a specified TCP port on the local machine. Ensures the port is
    ///     not already in use before proceeding with further operations.
    /// </summary>
    /// <param name="port">
    ///     The port number to validate. The value must fall within the range of valid TCP ports (0-65535).
    /// </param>
    /// <remarks>
    ///     Throws an <see cref="InvalidOperationException" /> if the specified port is already in use. The
    ///     method internally uses <see cref="IsPortAvailable" /> to determine port availability.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the specified port is already in use.
    /// </exception>
    private void ValidatePortAvailability(int port) {
        if (!IsPortAvailable(port)) throw new InvalidOperationException($"Port {port} is already in use.");
    }

    /// <summary>
    ///     Initializes the HTTP server for handling incoming requests within the Cliparino plugin. Sets up
    ///     the listener, binds to the specified prefix, and starts the server for local communication.
    /// </summary>
    /// <remarks>
    ///     This method creates an instance of <see cref="HttpListener" /> if it is not already
    ///     initialized, adds the HTTP prefix "http://localhost:8080/" to listen for incoming requests, and
    ///     starts the server. It logs the initialization status at the Info level.
    /// </remarks>
    /// <exception cref="HttpListenerException">
    ///     Thrown if there is an issue starting or accessing the HTTP server.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if the server is already started or its configuration state is invalid.
    /// </exception>
    private void InitializeServer() {
        if (_server != null) return;

        _server = new HttpListener();
        _server.Prefixes.Add("http://localhost:8080/");
        _server.Start();
        Log(LogLevel.Info, "Server initialized.");
    }

    /// <summary>
    ///     Sets up and initializes the semaphore used to manage access to the token setup process. Ensures
    ///     that no concurrent execution occurs while initializing cancellation tokens.
    /// </summary>
    /// <remarks>
    ///     This method acquires the <see cref="TokenSemaphore" /> before proceeding with the token
    ///     initialization logic. It prevents concurrent execution of critical sections that involve token
    ///     creation or reset. This is particularly important for ensuring thread safety and avoiding race
    ///     conditions in multithreaded scenarios.
    /// </remarks>
    /// <returns>
    ///     A task that represents the asynchronous operation. The task result is <c>true</c> if the
    ///     semaphore was acquired and the token setup succeeded; otherwise, <c>false</c>.
    /// </returns>
    private async Task<bool> SetupTokenSemaphore() {
        return await ExecuteWithSemaphore(TokenSemaphore,
                                          nameof(TokenSemaphore),
                                          () =>
                                              Task.FromResult(_cancellationTokenSource =
                                                                  new CancellationTokenSource()));
    }

    /// <summary>
    ///     Configures a browser source within OBS by updating its URL and refreshing the source.
    /// </summary>
    /// <param name="name">
    ///     The name of the scene in OBS where the browser source resides.
    /// </param>
    /// <param name="player">
    ///     The name of the browser source in the specified scene.
    /// </param>
    /// <param name="url">
    ///     The new URL to assign to the browser source.
    /// </param>
    private void ConfigureBrowserSource(string name, string player, string url) {
        UpdateBrowserSource(name, player, url);
        RefreshBrowserSource();
        Log(LogLevel.Info, "Browser source configured.");
    }

    #endregion

    #region Request Handling

    /// <summary>
    ///     Represents the default HTML content to be returned when an error occurs while generating or
    ///     retrieving HTML in memory for use in the application.
    /// </summary>
    /// <remarks>
    ///     This constant provides a basic HTML error page as a fallback mechanism to ensure that the
    ///     caller receives meaningful feedback if the in-memory HTML content is unavailable or invalid.
    /// </remarks>
    private const string HTMLErrorPage = "<h1>Error Generating HTML Content</h1>";

    /// <summary>
    ///     Represents the string response used to indicate that a requested resource was not found.
    /// </summary>
    /// <remarks>
    ///     This constant is primarily used within the HTTP request handling logic to provide a
    ///     standardized response body for 404 errors.
    /// </remarks>
    private const string NotFoundResponse = "404 Not Found";

    /// <summary>
    ///     Starts listening for incoming HTTP requests on the provided <see cref="HttpListener" />
    ///     instance. This method continuously processes incoming requests and forwards them to the
    ///     appropriate handler until the <see cref="CancellationToken" /> is triggered or the server stops
    ///     listening.
    /// </summary>
    /// <param name="server">
    ///     The <see cref="HttpListener" /> instance that serves as the HTTP server accepting incoming
    ///     requests.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token used to signal the cancellation of the listening operation. When the token is
    ///     triggered, the method will exit the listening loop and stop processing new requests.
    /// </param>
    /// <returns>
    ///     A task that represents the asynchronous operation of listening for and handling HTTP requests.
    /// </returns>
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

    /// <summary>
    ///     Handles incoming HTTP requests to the server, processes the request path, and generates an
    ///     appropriate response based on the request's URL or parameters.
    /// </summary>
    /// <param name="context">
    ///     The <see cref="HttpListenerContext" /> associated with the incoming HTTP request. This contains
    ///     the request and response objects needed to read incoming data and send back the response.
    /// </param>
    /// <returns>
    ///     A <see cref="Task" /> representing the asynchronous operation of handling the request and
    ///     sending back the response.
    /// </returns>
    /// <remarks>
    ///     This method applies necessary CORS headers to the response, determines the appropriate resource
    ///     based on the request URL (e.g., serving HTML or CSS files), and writes the response data to the
    ///     client. If an error occurs during the operation, it logs the exception with relevant details.
    /// </remarks>
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

    /// <summary>
    ///     Writes the provided textual response to the specified HTTP listener response object with the
    ///     specified content type.
    /// </summary>
    /// <param name="responseText">
    ///     The text content to include in the HTTP response body. This is typically the response generated
    ///     by the server to return to the client.
    /// </param>
    /// <param name="response">
    ///     The <see cref="HttpListenerResponse" /> object used to send the HTTP response to the client.
    ///     This is responsible for constructing and sending the HTTP headers and body to the caller.
    /// </param>
    /// <param name="contentType">
    ///     The MIME type of the content being returned. Examples include "text/html; charset=utf-8" and
    ///     "text/plain", indicating the type of data being sent in the response.
    /// </param>
    /// <returns>
    ///     A task that represents the asynchronous execution of writing the response. The task completes
    ///     once the response is fully written to the output stream.
    /// </returns>
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

    /// <summary>
    ///     Asynchronously generates an HTML page for embedding a Twitch clip and hosts it on an in-memory
    ///     server.
    /// </summary>
    /// <param name="clipUrl">
    ///     The URL of the Twitch clip to be displayed on the generated page.
    /// </param>
    /// <param name="streamerName">
    ///     The name of the streamer featured in the clip.
    /// </param>
    /// <param name="clipTitle">
    ///     The title of the Twitch clip being hosted.
    /// </param>
    /// <param name="curatorName">
    ///     The name of the curator who created the clip.
    /// </param>
    /// <param name="clipData">
    ///     Additional metadata associated with the Twitch clip, represented as a <see cref="ClipData" />
    ///     object.
    /// </param>
    /// <returns>
    ///     A task representing the asynchronous operation. No value is returned.
    /// </returns>
    /// <remarks>
    ///     This method extracts necessary data from the provided Twitch clip URL and metadata, generates
    ///     an HTML page containing the clip details, and serves it via an in-memory HTTP server. It logs
    ///     debug and error information throughout the process. If successful, the HTML page will be served
    ///     for viewing.
    /// </remarks>
    private async Task CreateAndHostClipPageAsync(string clipUrl,
                                                  string streamerName,
                                                  string clipTitle,
                                                  string curatorName,
                                                  ClipData clipData) {
        try {
            var clipInfo = new {
                clipUrl,
                streamerName,
                clipTitle,
                curatorName,
                clipData
            };

            Log(LogLevel.Debug,
                $"{nameof(CreateAndHostClipPageAsync)} called with parameters: {JsonConvert.SerializeObject(clipInfo)}");

            if (string.IsNullOrWhiteSpace(clipUrl)) {
                Log(LogLevel.Error, "clipUrl cannot be null or empty. Ensure it is passed correctly.");

                return;
            }

            var clipId = _clipManager.ExtractClipId(clipUrl);
            var gameName = await FetchGameNameAsync(clipData.GameId);

            _htmlInMemory = GenerateHtmlContent(clipId, streamerName, gameName, clipTitle, curatorName);
            Log(LogLevel.Debug, "Generated HTML content stored in memory.");
            LogHtmlContent(_htmlInMemory);

            var isConfigured = await ConfigureAndServe();

            if (isConfigured)
                Log(LogLevel.Info, "Server configured and ready to serve HTML content.");
            else
                Log(LogLevel.Error, "Failed to configure server.");
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error occurred in {nameof(CreateAndHostClipPageAsync)}: {ex.Message}");
            Log(LogLevel.Debug, ex.StackTrace);
        } finally {
            await CleanupServer();
            Log(LogLevel.Debug, $"{nameof(CreateAndHostClipPageAsync)} execution finished.");
        }
    }

    private static class PathBuddy {
        private static readonly string AppDataFolder = Path.Combine(GetFolderPath(ApplicationData), "Cliparino");
        private static LogDelegate _log;

        public static string GetCliparinoFolderPath() {
            return AppDataFolder;
        }

        public static void SetLogger(LogDelegate log) {
            _log = log;
        }

        public static void EnsureCliparinoFolderExists() {
            if (!Directory.Exists(AppDataFolder)) {
                Directory.CreateDirectory(AppDataFolder);
                _log(LogLevel.Debug, $"Created Cliparino folder at {AppDataFolder}.");
            } else {
                _log(LogLevel.Info, $"Cliparino folder exists at {AppDataFolder}.");
            }
        }
    }

    private void LogHtmlContent(string htmlContent) {
        var cliparinoPagePath = Path.Combine(PathBuddy.GetCliparinoFolderPath(), "cliparino.html");

        File.WriteAllText(cliparinoPagePath, htmlContent);
        Log(LogLevel.Info, $"Generated HTML for clip playback, written to {cliparinoPagePath}");
    }

    /// <summary>
    ///     Generates HTML content for embedding a Twitch clip, embedding metadata such as streamer name,
    ///     game name, clip title, and curator name. Ensures that all inputs are safely encoded for HTML
    ///     usage.
    /// </summary>
    /// <param name="clipId">
    ///     The unique identifier of the Twitch clip. If null or empty, a default clip ID will be used.
    /// </param>
    /// <param name="streamerName">
    ///     The name of the streamer who created the clip. If null, "Unknown Streamer" is used as a
    ///     fallback.
    /// </param>
    /// <param name="gameName">
    ///     The name of the game associated with the clip. If null, "Unknown Game" is used as a fallback.
    /// </param>
    /// <param name="clipTitle">
    ///     The title of the clip. If null, "Untitled Clip" is used as a fallback.
    /// </param>
    /// <param name="curatorName">
    ///     The name of the user who curated or created the clip. If null, "Anonymous" is used as a
    ///     fallback.
    /// </param>
    /// <returns>
    ///     A string containing the generated HTML content with properly encoded and replaced placeholders
    ///     for the clip's metadata.
    /// </returns>
    private string GenerateHtmlContent(string clipId,
                                       string streamerName,
                                       string gameName,
                                       string clipTitle,
                                       string curatorName) {
        // ReSharper disable once StringLiteralTypo - hash innit
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

    /// <summary>
    ///     Retrieves the HTML template stored in memory in a thread-safe manner.
    /// </summary>
    /// <remarks>
    ///     This method ensures the safe and synchronized access of the in-memory HTML template. If the
    ///     stored HTML string is null or empty, it logs a warning and returns a default error response.
    ///     The method uses a semaphore to enforce thread safety.
    /// </remarks>
    /// <returns>
    ///     The in-memory HTML template as a string if available; otherwise, a default error HTML template
    ///     is returned.
    /// </returns>
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

    /// <summary>
    ///     Cleans up server-related resources, including canceling operations, stopping and disposing the
    ///     server, and releasing associated resources.
    /// </summary>
    /// <param name="server">
    ///     An optional <see cref="HttpListener" /> instance representing the server to be cleaned up. If
    ///     not provided, the active server instance is used.
    /// </param>
    /// <returns>
    ///     A task that represents the asynchronous cleanup operation.
    /// </returns>
    /// <remarks>
    ///     This method ensures that all server operations are properly canceled, resources are released,
    ///     and the server is stopped and disposed to prevent potential resource leaks.
    /// </remarks>
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

    /// <summary>
    ///     Cancels all ongoing asynchronous operations associated with the current instance. Ensures
    ///     proper handling of shared resources and synchronization during the cancellation process.
    /// </summary>
    /// <remarks>
    ///     This method cancels all operations by signaling the cancellation token and releasing any
    ///     allocated resources. It uses a scoped semaphore to synchronize access and log the cancellation
    ///     process. Exceptions that occur during cancellation are logged but do not propagate upstream.
    /// </remarks>
    /// <returns>
    ///     A <see cref="Task" /> representing the asynchronous cancellation operation. The task completes
    ///     once all operations have been successfully canceled or an error occurs during the process.
    /// </returns>
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

    /// <summary>
    ///     Retrieves a single instance of the HTTP listener server for cleanup operations. Ensures
    ///     thread-safe access to the <c>_server</c> instance and resets it to null after retrieval.
    /// </summary>
    /// <param name="server">
    ///     A specific server instance to be used instead of the internally managed <c>_server</c>. If <c>null</c>, the internal <c>_server</c> instance is used. If no instance is available, a warning is logged, and the method returns
    ///     <c>null</c>.
    /// </param>
    /// <returns>
    ///     The <see cref="HttpListener" /> instance to be cleaned up. Returns <c>null</c> if no server
    ///     instance is available for cleanup.
    /// </returns>
    private HttpListener TakeServerInstance(HttpListener server) {
        lock (ServerLock) {
            if (_server == null && server == null) Log(LogLevel.Warn, "No server instance available for cleanup.");

            var instance = server ?? _server;

            // Ensure the server is nullified regardless of whether it was passed or taken.
            _server = null;

            return instance;
        }
    }

    /// <summary>
    ///     Handles cleanup of resources associated with the listening task. Waits for the task to
    ///     complete, handles potential exceptions, and resets the listening task reference.
    /// </summary>
    /// <remarks>
    ///     This method is part of the private utility functionality in <see cref="CPHInline" /> used to
    ///     ensure proper disposal of asynchronous operations during the server shutdown sequence. It
    ///     processes the currently assigned listening task and writes logs for debugging and error
    ///     tracking during the cleanup process.
    /// </remarks>
    /// <returns>
    ///     A task representing the asynchronous operation. The task completes when the listening task
    ///     cleanup is finished or if an exception occurs.
    /// </returns>
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

    /// <summary>
    ///     Stops and disposes of the specified instance of <see cref="HttpListener" />. Ensures that the
    ///     server is properly stopped and its resources are released to prevent memory leaks or resource
    ///     contention.
    /// </summary>
    /// <param name="serverInstance">
    ///     The instance of <see cref="HttpListener" /> to stop and dispose. If <c>null</c>, no action is
    ///     taken, and a log message indicates that there is no server instance to process.
    /// </param>
    /// <remarks>
    ///     This method performs the following actions: <list type="number">
    ///         <item>
    ///             <description>
    ///                 Stops the provided <see cref="HttpListener" /> instance to terminate any active
    ///                 connections or listeners.
    ///             </description>
    ///         </item> <item>
    ///             <description>
    ///                 Disposes of the instance to release any allocated resources. If an exception occurs
    ///                 during this process, it is caught and logged with an error message.
    ///             </description>
    ///         </item>
    ///     </list>
    /// </remarks>
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

    /// <summary>
    ///     Represents the length of the nonce used for satisfying the CORS policy requirements for clip
    ///     embedding.
    /// </summary>
    private const int NonceLength = 16;

    /// <summary>
    ///     Specifies the predefined message prefix, used to prefix log messages for the clip management
    ///     system.
    /// </summary>
    private const string MessagePrefix = "Cliparino :: ";

    /// <summary>
    ///     Encapsulates a <see cref="SemaphoreSlim" /> within a scoped context to ensure proper
    ///     acquisition and release of locks. Provides a mechanism to manage critical sections dynamically,
    ///     avoiding semaphore leakage and handling exceptions during semaphore release.
    /// </summary>
    private class ScopedSemaphore : IDisposable {
        private readonly LogDelegate _log;
        private readonly SemaphoreSlim _semaphore;
        private bool _hasLock;

        /// <summary>
        ///     A utility class that ensures a semaphore is acquired when instantiated and released when
        ///     disposed, providing scoped management of semaphore locks to simplify and enforce the proper use
        ///     pattern.
        /// </summary>
        /// <remarks>
        ///     Intended for use with asynchronous and synchronous operations that require controlled access to
        ///     shared resources. The semaphore is entered upon the creation of an instance and exited when the
        ///     instance is disposed of. This ensures proper cleanup of locks even in the event of exceptions.
        /// </remarks>
        public ScopedSemaphore(SemaphoreSlim semaphore, LogDelegate log) {
            _semaphore = semaphore ?? throw new ArgumentNullException(nameof(semaphore));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _semaphore.Wait();
            _hasLock = true;
        }

        /// <summary>
        ///     Releases all resources held by the <see cref="ScopedSemaphore" />. Ensures the semaphore is
        ///     properly released if it was acquired successfully.
        /// </summary>
        /// <remarks>
        ///     This method handles various edge cases that could occur during the release of the semaphore,
        ///     such as semaphore being disposed or already at maximum count. It logs any issues encountered
        ///     during the release process using the provided log delegate.
        /// </remarks>
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

        /// <summary>
        ///     Asynchronously waits to acquire a lock on the provided semaphore, ensuring thread-safe access
        ///     to shared resources within the <see cref="ScopedSemaphore" /> class.
        /// </summary>
        /// <param name="semaphore">
        ///     The <see cref="SemaphoreSlim" /> instance to acquire, preventing concurrent access to a
        ///     critical section of code.
        /// </param>
        /// <param name="log">
        ///     A delegate used to log information, warnings, or errors encountered during the semaphore
        ///     operation.
        /// </param>
        /// <returns>
        ///     A <see cref="Task" /> that resolves to an instance of <see cref="ScopedSemaphore" />. This
        ///     instance allows for deterministic release of the semaphore when disposed.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     Thrown when the <paramref name="semaphore" /> or <paramref name="log" /> parameter is null.
        /// </exception>
        public static async Task<ScopedSemaphore> WaitAsync(SemaphoreSlim semaphore, LogDelegate log) {
            if (semaphore == null) throw new ArgumentNullException(nameof(semaphore));

            if (log == null) throw new ArgumentNullException(nameof(log));

            var scopedSemaphore = new ScopedSemaphore(semaphore, log) { _hasLock = false };

            await semaphore.WaitAsync();

            scopedSemaphore._hasLock = true;

            return scopedSemaphore;
        }
    }

    /// <summary>
    ///     Attempts to acquire a semaphore within a specified timeout. This method is designed for use in
    ///     asynchronous workflows and ensures thread-safe access to shared resources.
    /// </summary>
    /// <param name="semaphore">
    ///     The semaphore to be acquired. Represents a resource that controls access to a finite number of
    ///     concurrent threads.
    /// </param>
    /// <param name="name">
    ///     A string identifier for the semaphore, used for logging and debugging purposes.
    /// </param>
    /// <param name="timeout">
    ///     The duration in seconds to wait for the semaphore to be acquired before timing out. Defaults to
    ///     10 seconds.
    /// </param>
    /// <returns>
    ///     A task returning a boolean indicating whether the semaphore was successfully acquired. Returns
    ///     <c>true</c> if the semaphore was acquired within the timeout; otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    ///     This method logs the process of acquiring the semaphore, including success, timeout, or errors.
    ///     It doesn't release the semaphore; the caller must handle this explicitly.
    /// </remarks>
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
    ///     Executes an asynchronous action within the given semaphore to ensure thread-safe operation.
    /// </summary>
    /// <param name="semaphore">
    ///     The semaphore used to control access to the critical section of code.
    /// </param>
    /// <param name="name">
    ///     The identifying name of the semaphore, primarily for logging purposes.
    /// </param>
    /// <param name="action">
    ///     The asynchronous action to be executed within the semaphore's critical section.
    /// </param>
    /// <returns>
    ///     A task representing the asynchronous operation, returning a boolean indicating whether the
    ///     action was executed successfully. Returns <c>true</c> if the operation succeeded, or <c>false</c> if acquiring the semaphore failed.
    /// </returns>
    private async Task<bool> ExecuteWithSemaphore(SemaphoreSlim semaphore, string name, Func<Task> action) {
        if (!await TryAcquireSemaphore(semaphore, name)) return false;

        try {
            await action();

            return true;
        } finally {
            semaphore.Release();
        }
    }

    /// <summary>
    ///     Logs a message with the specified log level, format, and caller information. This method allows
    ///     tailored logging for debugging, informational updates, warnings, or errors within the
    ///     application.
    /// </summary>
    /// <param name="level">
    ///     The severity level of the log message. Can be one of Debug, Info, Warn, or Error, determining
    ///     how the message is processed and displayed in Streamer.bot logs.
    /// </param>
    /// <param name="messageBody">
    ///     The content of the message to log. This is the primary information logged to help developers
    ///     and users understand the current state or issues during execution.
    /// </param>
    /// <param name="caller">
    ///     The name of the method or source from which the log message originated. This is included to
    ///     provide context when tracking or debugging log outputs. Defaults to the calling method's name.
    /// </param>
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

    /// <summary>
    ///     Defines a delegate for logging messages with specific log levels and contextual information
    ///     about the caller. Used across various components to standardize logging and provide detailed
    ///     diagnostics.
    /// </summary>
    /// <param name="level">
    ///     The severity level of the log message.
    /// </param>
    /// <param name="messageBody">
    ///     The content of the log message.
    /// </param>
    /// <param name="caller">
    ///     The name of the calling method, automatically populated by the runtime.
    /// </param>
    private delegate void LogDelegate(LogLevel level, string messageBody, [CallerMemberName] string caller = "");

    /// <summary>
    ///     Describes the severity levels of log messages in Cliparino's logging system. This is used to
    ///     define and categorize the importance or type of log, aiding in debugging or diagnostics.
    /// </summary>
    private enum LogLevel {
        /// <summary>
        ///     Represents a log level used to output debugging information during the development or
        ///     troubleshooting process. This level is intended for verbose and detailed logging to aid in
        ///     diagnosing issues or verifying application behavior.
        /// </summary>
        Debug,

        /// <summary>
        ///     Represents a logging level intended to provide informational messages that highlight the
        ///     progress or state of the application. Typically used for general operational insights or status
        ///     updates during normal operation.
        /// </summary>
        Info,

        /// <summary>
        ///     Represents a logging level indicating a warning condition. Used to log potential issues or
        ///     unexpected behaviors that do not necessarily cause an error but may require attention.
        /// </summary>
        Warn,

        /// <summary>
        ///     Represents a log level used to indicate an error event, typically involving a failure or issue
        ///     that prevents normal operation.
        /// </summary>
        Error
    }

    /// <summary>
    ///     Attempts to retrieve the command argument provided to the script execution. Commands are
    ///     typically passed through external triggers or chat inputs within the Streamer.bot environment.
    /// </summary>
    /// <param name="command">
    ///     An output parameter that contains the command string if the argument is successfully retrieved.
    ///     The command typically represents an action such as `!watch`, `!so`, `!replay`, or `!stop`.
    /// </param>
    /// <returns>
    ///     A boolean value indicating whether the command was successfully retrieved. Returns <c>true</c>
    ///     if the command exists; otherwise, <c>false</c>.
    /// </returns>
    private bool TryGetCommand(out string command) {
        return CPH.TryGetArg("command", out command);
    }

    /// <summary>
    ///     Sanitizes a given Twitch username by trimming whitespace, removing leading "@" symbols, and
    ///     converting the string to lowercase.
    /// </summary>
    /// <param name="user">
    ///     The username to be sanitized. This parameter may include leading "@" symbols, uppercase
    ///     characters, or extra whitespace, which will be removed by this method.
    /// </param>
    /// <returns>
    ///     A sanitized string representing the username without leading "@" symbols, extra whitespace, or
    ///     uppercase letters. Returns <c>null</c> if the input is null, empty, or consists only of
    ///     whitespace.
    /// </returns>
    private static string SanitizeUsername(string user) {
        return string.IsNullOrWhiteSpace(user) ? null : user.Trim().TrimStart('@').ToLowerInvariant();
    }

    /// <summary>
    ///     Fetches extended user information for a specified Twitch username. This includes additional
    ///     metadata about the user that is not available in standard Twitch API responses.
    /// </summary>
    /// <param name="user">
    ///     The username of the Twitch user whose extended information is being fetched.
    /// </param>
    /// <returns>
    ///     An instance of <see cref="TwitchUserInfoEx" /> containing extended user information if
    ///     available; otherwise, <c>null</c> if no data is found for the specified user.
    /// </returns>
    private TwitchUserInfoEx FetchExtendedUserInfo(string user) {
        var extendedUserInfo = CPH.TwitchGetExtendedUserInfoByLogin(user);

        if (extendedUserInfo == null) {
            Log(LogLevel.Warn, $"No extended user info found for: {user}");

            return null;
        }

        Log(LogLevel.Debug, $"Fetched extended user info: {JsonConvert.SerializeObject(extendedUserInfo)}");

        return extendedUserInfo;
    }

    /// <summary>
    ///     Retrieves the template for the shoutout message. This template is used to format the shoutout
    ///     text dynamically based on the streamed game, username, and other user-specific information.
    /// </summary>
    /// <remarks>
    ///     Attempts to fetch a custom "message" argument from Streamer.bot; if none is provided, it
    ///     defaults to a predefined template. The method ensures the appropriate fallback if the custom
    ///     template is empty or null.
    /// </remarks>
    /// <returns>
    ///     A string containing the message template. This template includes placeholders like [[userName]]
    ///     and [[userGame]] that will be resolved at runtime with information about the target user.
    /// </returns>
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

    /// <summary>
    ///     Attempts to retrieve a random Twitch clip for a given user based on specified settings.
    /// </summary>
    /// <param name="userId">
    ///     The unique identifier of the user whose clip is being fetched.
    /// </param>
    /// <returns>
    ///     A <c>Clip</c> object representing the retrieved Twitch clip if found; otherwise, <c>null</c>.
    /// </returns>
    private Clip TryFetchClip(string userId) {
        var clipSettings = new ClipSettings(GetArgument("featuredOnly", false),
                                            GetArgument("maxClipSeconds", 30),
                                            GetArgument("clipAgeDays", 30));
        var clip = GetRandomClip(userId, clipSettings);

        if (clip == null) Log(LogLevel.Warn, $"No clips found for user with ID: {userId}");

        return clip;
    }

    /// <summary>
    ///     Asynchronously handles the logic for generating, logging, and sending a Twitch shoutout
    ///     message.
    /// </summary>
    /// <param name="userInfo">
    ///     The extended user information object containing details about the user to be shouted out. This
    ///     parameter includes properties such as the user's ID, username, and other metadata fetched from
    ///     Twitch.
    /// </param>
    /// <param name="template">
    ///     A string template used for generating the shoutout message. The template may include
    ///     placeholders that will be dynamically replaced with user-specific or clip-specific values.
    /// </param>
    /// <param name="clip">
    ///     An optional <see cref="Clip" /> object representing a Twitch clip associated with the user. If
    ///     provided, the clip information will enhance the shoutout message. If null, a fallback message
    ///     will be sent indicating the absence of clips.
    /// </param>
    /// <returns>
    ///     A task representing the asynchronous operation. The task completes once the shoutout message
    ///     has been processed and sent, including any additional logic for handling Twitch clip data.
    /// </returns>
    /// <remarks>
    ///     This method constructs a shoutout message by combining the user information, a message
    ///     template, and optional clip details. It logs the generated message and sends it to chat via
    ///     Streamer.bot's API. If a clip is provided, the method also processes the clip's data for
    ///     hosting and chat playback. In the absence of a clip, a default follow prompt is sent.
    /// </remarks>
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

    /// <summary>
    ///     Generates a custom shoutout message for a Twitch user, based on the provided user information,
    ///     message template, and optional clip data.
    /// </summary>
    /// <param name="userInfo">
    ///     Contains information about the Twitch user, such as their username, login name, last-streamed
    ///     game, etc.
    /// </param>
    /// <param name="template">
    ///     A string template containing placeholders (e.g., [[userName]], [[userGame]]) to be replaced
    ///     with the relevant user information.
    /// </param>
    /// <param name="clip">
    ///     Optional. A clip object representing the user's Twitch clip data. If null, the method generates
    ///     a message without clip-related details.
    /// </param>
    /// <returns>
    ///     A string containing the formatted shoutout message with placeholders replaced by actual user
    ///     data.
    /// </returns>
    private static string GetShoutoutMessage(TwitchUserInfoEx userInfo, string template, Clip clip) {
        var displayName = userInfo.UserName ?? userInfo.UserLogin;
        var lastGame = userInfo.Game ?? "nothing yet";

        if (clip == null)
            return string.IsNullOrWhiteSpace(userInfo.Game)
                       ? $"Looks like @{displayName} hasn't streamed anything yet, but you might want to give that follow button a tickle anyway, just in case!"
                       : $"Make sure to go check out @{displayName}! They were last streaming {lastGame} over at https://twitch.tv/{displayName}";

        return template.Replace("[[userName]]", displayName).Replace("[[userGame]]", lastGame);
    }

    /// <summary>
    ///     Validates the provided dimensions for width and height and ensures they are positive values. If
    ///     invalid values are provided, defaults are returned instead.
    /// </summary>
    /// <param name="width">
    ///     The width value to validate.
    /// </param>
    /// <param name="height">
    ///     The height value to validate.
    /// </param>
    /// <returns>
    ///     A tuple containing the validated width and height values. If invalid input values are given
    ///     (e.g., non-positive numbers), the default width and height are returned.
    /// </returns>
    private (int Width, int Height) ValidateDimensions(int width, int height) {
        if (width > 0 && height > 0) return (width, height);

        Log(LogLevel.Warn, "Invalid width or height provided. Falling back to default values.");

        return (DefaultWidth, DefaultHeight);
    }

    /// <summary>
    ///     Generates a secure, random nonce string for use in content security policies or other areas
    ///     requiring a concise, unique identifier.
    /// </summary>
    /// <remarks>
    ///     The nonce is a Base64-encoded GUID, sanitized to remove unsupported characters for certain
    ///     applications, and trimmed to a fixed length. This ensures compatibility and uniqueness while
    ///     adhering to specific formatting requirements.
    /// </remarks>
    /// <returns>
    ///     A string representation of the sanitized, fixed-length nonce.
    /// </returns>
    private static string CreateNonce() {
        var guidBytes = Guid.NewGuid().ToByteArray();
        var base64Nonce = Convert.ToBase64String(guidBytes);

        return SanitizeNonce(base64Nonce).Substring(0, NonceLength);
    }

    /// <summary>
    ///     Modifies a given nonce string by replacing specific characters to ensure it conforms to a valid
    ///     and safe format.
    /// </summary>
    /// <param name="nonce">
    ///     The original nonce string that may contain characters such as '+', '/' or '=' requiring
    ///     sanitization.
    /// </param>
    /// <returns>
    ///     A sanitized version of the input nonce string where '+' is replaced with '-', '/' is replaced
    ///     with '_', and '=' is replaced with '_'.
    /// </returns>
    private static string SanitizeNonce(string nonce) {
        return nonce.Replace("+", "-").Replace("/", "_").Replace("=", "_");
    }

    /// <summary>
    ///     Applies Cross-Origin Resource Sharing (CORS) headers to the given HTTP response, ensuring
    ///     compliance with security policies for interacting with external resources.
    /// </summary>
    /// <param name="response">
    ///     The <see cref="HttpListenerResponse" /> object to which the CORS headers will be applied.
    /// </param>
    /// <returns>
    ///     A unique nonce string generated for Content-Security-Policy headers to enhance resource
    ///     integrity and security.
    /// </returns>
    private static string ApplyCORSHeaders(HttpListenerResponse response) {
        var nonce = CreateNonce();

        foreach (var header in CORSHeaders)
            response.Headers[header.Key] = header.Value
                                                 .Replace("[[nonce]]", nonce)
                                                 .Replace("\r", "")
                                                 .Replace("\n", " ");

        return nonce;
    }

    /// <summary>
    ///     Determines if the specified TCP port is available for use on the local machine.
    /// </summary>
    /// <param name="port">
    ///     The port number to check for availability. The value must be within the range of valid TCP
    ///     ports (0-65535).
    /// </param>
    /// <returns>
    ///     A boolean indicating whether the specified port is available. Returns <c>true</c> if the port
    ///     is available, or <c>false</c> if it is already in use or an error occurs during the check.
    /// </returns>
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

    /// <summary>
    ///     Checks for conflicts on a specific network port by examining active TCP listeners and
    ///     connections.
    /// </summary>
    /// <param name="port">
    ///     The network port to be checked for conflicts. This should be an integer within the valid port
    ///     range (0-65535).
    /// </param>
    /// <remarks>
    ///     This method inspects both active TCP listeners and active TCP connections to determine if the
    ///     specified port is currently in use. If a conflict is detected, a warning log is generated;
    ///     otherwise, an informational log confirms the port's availability.
    /// </remarks>
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

    /// <summary>
    ///     Cancels the currently active cancellation token if one exists and initializes a new token
    ///     source for future use.
    /// </summary>
    /// <remarks>
    ///     This method is responsible for managing the lifecycle of the auto-stop cancellation token. It
    ///     cancels and disposes of the current token source to ensure no lingering tasks remain active,
    ///     then creates a new <see cref="CancellationTokenSource" /> for future tasks relying on the
    ///     token. This can be used to interrupt ongoing asynchronous operations safely.
    /// </remarks>
    private void CancelCurrentToken() {
        var tokenSource = _autoStopCancellationTokenSource;

        tokenSource?.Cancel();
        tokenSource?.Dispose();
        _autoStopCancellationTokenSource = new CancellationTokenSource();
    }

    /// <summary>
    ///     Creates a preamble message to provide context when an error occurs. This method includes the
    ///     name of the caller method where the error was triggered.
    /// </summary>
    /// <param name="caller">
    ///     An optional parameter that captures the name of the method that invoked the CreateErrorPreamble
    ///     method. Defaults to the name of the caller at compile time if not specified.
    /// </param>
    /// <returns>
    ///     A string containing the error preamble message, including the name of the caller method.
    /// </returns>
    private static string CreateErrorPreamble([CallerMemberName] string caller = "") {
        return $"An error occurred in {caller}";
    }

    /// <summary>
    ///     Hosts a Twitch clip with the provided details, creating and displaying a clip page with
    ///     extracted information within Streamer.bot's configured environment.
    /// </summary>
    /// <param name="clipUrl">
    ///     The URL of the Twitch clip to be hosted. This parameter may be used to fetch the clip data if
    ///     the <paramref name="clipData" /> is null or incomplete.
    /// </param>
    /// <param name="clipData">
    ///     An object containing detailed information about the Twitch clip, including metadata such as the
    ///     clip title, streamer name, curator name, and duration.
    /// </param>
    /// <returns>
    ///     A Task representing the asynchronous operation for hosting the clip. No result is returned.
    /// </returns>
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
    ///     Retrieves the specified value or provides a fallback when the value is null or empty.
    /// </summary>
    /// <typeparam name="T">
    ///     The type of the value and default value to be returned.
    /// </typeparam>
    /// <param name="value">
    ///     The value to be checked and potentially returned if it is not null or empty.
    /// </param>
    /// <param name="defaultValue">
    ///     The fallback value to return if the specified value is null or empty. Defaults to the type's
    ///     default value.
    /// </param>
    /// <returns>
    ///     The specified value if it is not null or empty; otherwise, the provided fallback value.
    /// </returns>
    private static T GetValueOrDefault<T>(T value, T defaultValue = default) {
        if (value is string stringValue) return !string.IsNullOrEmpty(stringValue) ? value : defaultValue;

        return value != null ? value : defaultValue;
    }

    /// <summary>
    ///     Initiates an asynchronous task that automatically stops the clip playback after a specified
    ///     duration.
    /// </summary>
    /// <param name="duration">
    ///     The duration, in seconds, after which the playback should automatically stop. Must be a
    ///     non-negative value.
    /// </param>
    /// <returns>
    ///     A task representing the asynchronous operation. The task completes when the playback is either
    ///     stopped successfully or the task is canceled.
    /// </returns>
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

    /// <summary>
    ///     Represents a structure for defining a payload object used in interactions or data passing,
    ///     typically containing request type and accompanying request data.
    /// </summary>
    private interface IPayload {
        /// <summary>
        ///     The type of the request being sent.
        /// </summary>
        string RequestType { get; }

        /// <summary>
        ///     The data associated with a request.
        /// </summary>
        object RequestData { get; }
    }

    /// <summary>
    ///     Represents the payload structure used to communicate with the OBS WebSocket API or other API
    ///     endpoints. Encapsulates a request type and corresponding data payload in a structured format.
    /// </summary>
    private class Payload : IPayload {
        /// <summary>
        ///     The type of request being handled.
        /// </summary>
        public string RequestType { get; set; }

        /// <summary>
        ///     The data payload for a request. This property is commonly used to define the input parameters
        ///     required for executing API or system commands.
        /// </summary>
        public object RequestData { get; set; }
    }

    #endregion
}