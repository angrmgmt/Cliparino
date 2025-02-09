﻿/*  Cliparino is a clip player for Twitch.tv built to work with Streamer.bot.
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

// ReSharper disable RedundantUsingDirective
using Microsoft.CSharp;
using Microsoft.CSharp.RuntimeBinder;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
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

// ReSharper restore RedundantUsingDirective

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

    private const string CSSText = """
                                       div {
                                           background-color: #0071c5;
                                           background-color: rgba(0,113,197,1);
                                           margin: 0 auto;
                                           overflow: hidden;
                                       }
                                   
                                       #twitch-embed {
                                           display: block;
                                       }
                                   
                                       .iframe-container {
                                           height: 1080px;
                                           position: relative;
                                           width: 1920px;
                                       }
                                   
                                       #clip-iframe {
                                           height: 100%;
                                           left: 0;
                                           position: absolute;
                                           top: 0;
                                           width: 100%;
                                       }
                                   
                                       #overlay-text {
                                           background-color: #042239;
                                           background-color: rgba(4,34,57,0.7071);
                                           border-radius: 5px;
                                           color: #ffb809;
                                           left: 5%;
                                           opacity: 0.5;
                                           padding: 10px;
                                           position: absolute;
                                           top: 80%;
                                       }
                                   
                                       .line1, .line2, .line3 {
                                           font-family: 'Open Sans', sans-serif;
                                           font-size: 2em;
                                       }
                                       
                                       .line1 {
                                            font: normal 600 2em/1.2 'OpenDyslexic', 'Open Sans', sans-serif;
                                       }
                                       
                                       .line2 {
                                            font: normal 400 1.5em/1 'OpenDyslexic', 'Open Sans', sans-serif;
                                       }
                                       
                                       .line3 {
                                            font: italic 100 1em/1 'OpenDyslexic', 'Open Sans', sans-serif;
                                       }
                                   """;

    private const string ConstClipDataError = "Unable to retrieve clip data.";

    private const string HTMLText = """
                                    <!DOCTYPE html>
                                            <html lang="en">
                                            <head>
                                            <meta charset="utf-8">
                                            <link href="/index.css" rel="stylesheet" type="text/css">
                                            <meta name="viewport" content="width=device-width, initial-scale=1.0">
                                            <title>Cliparino</title>
                                            </head>
                                            <body>
                                            <div id="twitch-embed">
                                            <div class="iframe-container">
                                            <iframe allowfullscreen autoplay="true" controls="false" height="1080" id="clip-iframe" mute="false" preload="auto" src="https://clips.twitch.tv/embed?clip=[[clipId]]&nonce=[[nonce]]&autoplay=true&parent=localhost" title="Cliparino" width="1920">
                                            </iframe>
                                            <div class="overlay-text" id="overlay-text">
                                            <div class="line1">
                                            [[streamerName]] doin' [[gameName]]
                                            </div>
                                            <div class="line2">
                                            [[clipTitle]]
                                            </div>
                                            <div class="line3">
                                            by [[curatorName]]
                                            </div>
                                            </div>
                                            </div>
                                            </div>
                                            </body>
                                            </html>
                                    """;

    private const string MessagePrefix = "Cliparino :: ";

    private static readonly Dictionary<string, string> CORSHeaders = new() {
        { "Access-Control-Allow-Origin", "*" },
        { "Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS" },
        { "Access-Control-Allow-Headers", "*" }, {
            "Content-Security-Policy",
            "script-src 'nonce-[[nonce]]' 'strict-dynamic';\nobject-src 'none';\nbase-uri 'none'; frame-ancestors 'self' https://clips.twitch.tv;"
        }
    };

    private readonly Dictionary<string, ClipData> _clipDataCache = new();

    private readonly object _serverLock = new();
    private CancellationTokenSource _autoStopCancellationTokenSource;
    private string _htmlInMemory;
    private bool _loggingEnabled;
    private HttpListener _server;

    private void CancelCurrentToken() {
        _autoStopCancellationTokenSource?.Cancel();
        _autoStopCancellationTokenSource?.Dispose();
        _autoStopCancellationTokenSource = new CancellationTokenSource();
    }

    private static string GetErrorMessagePreamble(string methodName) {
        return $"An error occurred in {methodName}";
    }

    private T GetArgument<T>(string argName, T defaultValue = default) {
        return CPH.TryGetArg(argName, out T value) ? value : defaultValue;
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
    ///     A boolean indicating whether the script executed successfully. Returns
    ///     <c>
    ///         true
    ///     </c>
    ///     if execution succeeded; otherwise,
    ///     <c>
    ///         false
    ///     </c>
    ///     .
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

            if (string.IsNullOrEmpty(url)) {
                Log(LogLevel.Warn, "No valid clip URL provided. Aborting command.");

                return;
            }

            LogBrowserSourceSetup(url, width, height);

            var currentScene = CPH.ObsGetCurrentScene();

            if (string.IsNullOrWhiteSpace(currentScene)) {
                Log(LogLevel.Warn, "Unable to determine the current OBS scene. Aborting clip setup.");

                return;
            }

            await PrepareSceneForClipHostingAsync(currentScene);

            await ProcessAndHostClipDataAsync(url, null);
        } catch (Exception ex) {
            Log(LogLevel.Error, ex.Message);
        }
    }

    private async Task EnsureCliparinoInSceneAsync(string currentScene, string clipUrl = null) {
        const string sourceName = "Cliparino";

        try {
            Log(LogLevel.Debug, $"Entering {nameof(EnsureCliparinoInSceneAsync)}.");

            if (string.IsNullOrEmpty(currentScene)) {
                Log(LogLevel.Info, "Current scene is null or empty, fetching the current OBS scene.");
                currentScene = CPH.ObsGetCurrentScene();
            }

            if (string.IsNullOrEmpty(currentScene)) {
                Log(LogLevel.Error, "Failed to retrieve the current OBS scene.");

                throw new InvalidOperationException("Failed to retrieve the current OBS scene.");
            }

            if (!SceneExists(currentScene)) {
                Log(LogLevel.Info, $"Scene '{currentScene}' does not exist. Creating scene...");

                try {
                    CreateScene(currentScene);
                    Log(LogLevel.Debug, $"Scene '{currentScene}' created successfully.");
                } catch (Exception ex) {
                    Log(LogLevel.Error, $"Error creating scene '{currentScene}': {ex.Message}");

                    return;
                }
            }

            if (!SourceExistsInScene(currentScene, sourceName)) {
                Log(LogLevel.Info, $"Source '{sourceName}' does not exist in scene '{currentScene}'. Adding it...");
                var sourceUrl = CreateSourceUrl("http://localhost:8080/index.htm");

                try {
                    if ((string)GetCurrentSourceUrl("Cliparino", "Player") != sourceUrl) {
                        Log(LogLevel.Info, $"Updating browser source URL for 'Player' to '{sourceUrl}'.");
                        UpdateBrowserSource("Cliparino", "Player", sourceUrl);

                        var loadedUrl = GetCurrentSourceUrl("Cliparino", "Player");
                        Log(LogLevel.Info, $"Browser source 'Player' current URL: {loadedUrl}");
                        RefreshBrowserSource();
                    } else {
                        Log(LogLevel.Debug, "Browser source URL for 'Player' is already up-to-date.");
                    }

                    AddBrowserSource(currentScene, sourceName, sourceUrl);
                    Log(LogLevel.Debug, $"Added browser source '{sourceName}' to scene '{currentScene}'.");
                } catch (Exception ex) {
                    Log(LogLevel.Error, $"Error adding or updating browser source: {ex.Message}");

                    return;
                }
            } else {
                Log(LogLevel.Info,
                    $"Source '{sourceName}' already exists in scene '{currentScene}'. Updating browser source...");

                try {
                    UpdateBrowserSource(currentScene, sourceName, CreateSourceUrl("http://localhost:8080/index.htm"));
                    Log(LogLevel.Debug,
                        $"Browser source '{sourceName}' updated successfully in scene '{currentScene}'.");

                    var loadedUrl = GetCurrentSourceUrl("Cliparino", "Player");
                    Log(LogLevel.Info, $"Browser source 'Player' current URL: {loadedUrl}");
                    RefreshBrowserSource();
                } catch (Exception ex) {
                    Log(LogLevel.Error, $"Error updating browser source: {ex.Message}");

                    return;
                }
            }

            if (string.IsNullOrEmpty(clipUrl)) {
                Log(LogLevel.Debug, "Clip URL is null or empty, exiting function.");

                return;
            }

            Log(LogLevel.Debug, $"Fetching clip data for URL: {clipUrl}");

            try {
                var clipData = await GetClipData(clipUrl);
                var gameId = clipData?.GameId;

                if (string.IsNullOrEmpty(gameId)) {
                    Log(LogLevel.Error, "Clip data does not contain a valid Game ID.");

                    throw new InvalidOperationException("Clip data does not contain a valid Game ID.");
                }

                Log(LogLevel.Debug, $"Fetching Twitch data for Game ID: {gameId}");
                await FetchTwitchData<GameData>($"https://api.twitch.tv/helix/games?id={gameId}");
                Log(LogLevel.Info, "Twitch data fetched successfully.");
            } catch (Exception ex) {
                Log(LogLevel.Error, $"Error fetching clip data or Twitch data: {ex.Message}");
            }
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Unhandled exception in EnsureCliparinoInSceneAsync: {ex.Message}");
        } finally {
            Log(LogLevel.Debug, $"Exiting {nameof(EnsureCliparinoInSceneAsync)}.");
        }
    }

    private void LogBrowserSourceSetup(string url, int width, int height) {
        Log(LogLevel.Info, $"Setting browser source with URL: {url}, width: {width}, height: {height}");
    }

    private async Task HandleShoutoutCommandAsync(string user) {
        Log(LogLevel.Debug, $"{nameof(HandleShoutoutCommandAsync)} called with user: {user}");

        try {
            if (string.IsNullOrEmpty(user)) {
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

            if (string.IsNullOrEmpty(messageTemplate)) {
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
                                    + $"Give them a follow and catch some clips next time they go live!";
                CPH.SendMessage(noClipMessage);
            }

            var displayName = extendedUserInfo.UserName ?? user;
            var lastGame = extendedUserInfo.Game;

            string shoutoutMessage;

            if (clip == null) {
                if (string.IsNullOrEmpty(lastGame))
                    shoutoutMessage = $"Looks like @{displayName} hasn't streamed anything yet, "
                                      + $"but you might want to give that follow button a tickle anyway, just in case!";
                else
                    shoutoutMessage = $"Make sure to go check out @{displayName}! "
                                      + $"They were last streaming {lastGame} over at https://twitch.tv/{displayName}";
            } else {
                if (string.IsNullOrEmpty(lastGame)) {
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
            Log(LogLevel.Error, $"{GetErrorMessagePreamble("GetRandomClip")}: {ex.Message}");

            return null;
        }
    }

    private Clip LogAndReturnNull(string message) {
        Log(LogLevel.Warn, message);

        return null;
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
            Log(LogLevel.Error, $"{GetErrorMessagePreamble("FetchTwitchUser")}: {ex.Message}");

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
            Log(LogLevel.Error, $"{GetErrorMessagePreamble("RetrieveClips")}: {ex.Message}");

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
            Log(LogLevel.Error, $"{GetErrorMessagePreamble("FilteredByCriteria")}: {ex.Message}");

            return false;
        }
    }

    private static ClipData SelectRandomClip(List<ClipData> clips) {
        return clips[new Random().Next(clips.Count)];
    }

    private async Task HandleReplayCommandAsync(int width, int height) {
        Log(LogLevel.Debug, $"{nameof(HandleReplayCommandAsync)} called with width: {width}, height: {height}");

        try {
            var lastClipUrl = GetLastClipUrl();

            if (string.IsNullOrEmpty(lastClipUrl)) return;

            await ProcessAndHostClipDataAsync(lastClipUrl, null);
        } catch (Exception ex) {
            Log(LogLevel.Error, ex.Message);
        }
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

    private string ValidateClipUrl(string clipUrl, ClipData clipData) {
        if (!string.IsNullOrEmpty(clipUrl)) return clipUrl;

        clipUrl = clipData?.Url;

        if (!string.IsNullOrEmpty(clipUrl)) return clipUrl;

        Log(LogLevel.Error, "clipUrl is null or empty.");

        return null;
    }

    private async Task PrepareSceneForClipHostingAsync(string sceneName) {
        Log(LogLevel.Info, $"Preparing scene '{sceneName}' for clip hosting.");
        const string cliparinoSourceName = "Cliparino";
        const string playerSourceName = "Player";

        if (!EnsureSourceExistsAndIsVisible(sceneName, cliparinoSourceName)) return;

        if (!EnsureSourceExistsAndIsVisible(cliparinoSourceName, playerSourceName)) return;

        await ConfigureAudioForPlayerSourceAsync();
    }

    private void SetLastClipUrl(string url) {
        if (string.IsNullOrEmpty(url)) {
            Log(LogLevel.Warn, "Attempted to set an empty or null clip URL.");

            return;
        }

        CPH.SetGlobalVar(LastClipUrlKey, url);
        Log(LogLevel.Info, "Successfully set the last clip URL.");
    }

    private string GetLastClipUrl() {
        var url = CPH.GetGlobalVar<string>(LastClipUrlKey);

        if (string.IsNullOrEmpty(url)) Log(LogLevel.Warn, "No last clip URL found for replay.");

        return url;
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

    private void HandleStopCommand() {
        Log(LogLevel.Debug, $"{nameof(HandleStopCommand)} called, setting browser source page to blank layout.");

        CancelCurrentToken();
        Log(LogLevel.Info, "Cancelled ongoing auto-stop task.");

        var currentScene = CPH.ObsGetCurrentScene();
        const string cliparinoSourceName = "Cliparino";

        EnsureSourceExistsAndIsVisible(currentScene, cliparinoSourceName, false);

        SetBrowserSource("about:blank");
    }

    private async Task<ClipData> FetchValidClipDataWithCache(ClipData clipData, string clipUrl) {
        Log(LogLevel.Debug,
            $"FetchValidClipDataWithCache called with clipData: {JsonConvert.SerializeObject(clipData)}, clipUrl: {clipUrl}");


        lock (_clipDataCache) {
            if (_clipDataCache.TryGetValue(clipUrl, out var cachedClipData)) return cachedClipData;
        }

        if (clipData == null) {
            Log(LogLevel.Warn, "clipData is null. Attempting to fetch clip data using clipUrl.");

            if (string.IsNullOrEmpty(clipUrl) || !clipUrl.Contains("twitch.tv")) {
                Log(LogLevel.Error, $"Invalid clip URL provided: {clipUrl}");

                return null;
            }

            var clipId = ExtractClipIdFromUrl(clipUrl);

            if (string.IsNullOrEmpty(clipId)) {
                Log(LogLevel.Error, $"Invalid clip ID extracted from URL: {clipUrl}");

                return null;
            }


            var clip = await FetchClipById(clipId);

            if (clip != null) {
                clipData = clip.ToClipData(CPH);

                if (clipData == null) {
                    Log(LogLevel.Error, ConstClipDataError);

                    return null;
                }
            } else {
                Log(LogLevel.Error, $"{ConstClipDataError} for clip ID: {clipId}");

                return null;
            }
        }

        if (string.IsNullOrEmpty(clipData.Id) || string.IsNullOrEmpty(clipData.Url))
            Log(LogLevel.Error, "ClipData validation failed. Missing essential fields (ID or URL).");

        Log(LogLevel.Info, $"Successfully fetched clip data for clip ID: {clipData.Id}");

        clipData = await FetchClipDataIfNeeded(clipData, clipUrl);

        return clipData;
    }

    private string ExtractClipIdFromUrl(string clipUrl) {
        if (string.IsNullOrEmpty(clipUrl)) return null;

        try {
            var uri = new Uri(clipUrl);

            if (uri.Host.IndexOf("twitch.tv", StringComparison.OrdinalIgnoreCase) >= 0)
                return uri.Segments.LastOrDefault()?.Trim('/');
        } catch (Exception ex) {
            Log(LogLevel.Error, ex.Message);
        }

        return null;
    }

    private async Task HostClipWithDetailsAsync(string clipUrl, ClipData clipData) {
        Log(LogLevel.Debug,
            $"{nameof(HostClipWithDetailsAsync)} called with clipUrl: {clipUrl}, clipData: {JsonConvert.SerializeObject(clipData)}");

        try {
            if (string.IsNullOrEmpty(clipUrl)) {
                Log(LogLevel.Warn, "clipUrl is null or empty. Attempting to use clipData.Url.");
                clipUrl = clipData?.Url;

                if (string.IsNullOrEmpty(clipUrl)) {
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

    private async Task StartAutoStopTaskAsync(double duration) {
        try {
            CancelCurrentToken();

            using var cancellationTokenSource = _autoStopCancellationTokenSource;

            await Task.Delay(TimeSpan.FromSeconds(GetDurationWithSetupDelay((float)duration).TotalSeconds),
                             cancellationTokenSource.Token);

            if (!cancellationTokenSource.Token.IsCancellationRequested) {
                HandleStopCommand();
                Log(LogLevel.Info, "Auto-stop task completed successfully.");
            }
        } catch (OperationCanceledException) {
            Log(LogLevel.Info, "Auto-stop task cancelled gracefully.");
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Unexpected error in auto-stop task: {ex.Message}");
        }
    }

    private async Task CreateAndHostClipPageAsync(string clipUrl,
                                                  string streamerName,
                                                  string clipTitle,
                                                  string curatorName,
                                                  ClipData clipData) {
        Log(LogLevel.Debug,
            $"{nameof(CreateAndHostClipPageAsync)} called with clipUrl: {clipUrl}, streamerName: {streamerName}, "
            + $"clipTitle: {clipTitle}, curatorName: {curatorName}, clipData: {JsonConvert.SerializeObject(clipData)}");

        try {
            if (string.IsNullOrEmpty(clipUrl)) {
                Log(LogLevel.Error, "clipUrl cannot be null or empty. Ensure it is passed correctly.");

                return;
            }

            Log(LogLevel.Debug, "Extracting clip ID from clipUrl.");
            var clipId = ExtractClipId(clipUrl);

            if (string.IsNullOrEmpty(clipId)) {
                Log(LogLevel.Error, $"Failed to extract Clip ID from clipUrl: {clipUrl}");

                return;
            }

            Log(LogLevel.Info, $"Extracted Clip ID: {clipId}");
            var fetchGameNameTask = FetchGameNameAsync(clipData.GameId ?? "");

            Log(LogLevel.Debug, "Generating HTML content for clip page.");
            _htmlInMemory = GenerateHtmlContent(clipId, streamerName, "Loading Game Name...", clipTitle, curatorName);
            string gameName;

            try {
                gameName = await fetchGameNameTask;

                if (string.IsNullOrEmpty(gameName)) {
                    Log(LogLevel.Warn, "Game name is null or empty. Using default value: 'Unknown Game'");
                    gameName = "Unknown Game";
                }
            } catch (Exception ex) {
                Log(LogLevel.Error, $"Error while fetching game name: {ex.Message}");
                gameName = "Unknown Game";
            }

            Log(LogLevel.Info, $"Fetched game name: {gameName}");

            Log(LogLevel.Debug, "Configuring and serving the clip page.");
            ConfigureAndServe();
            Log(LogLevel.Info, "Clip page hosted successfully.");
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error occurred in {nameof(CreateAndHostClipPageAsync)}: {ex.Message}");
            Log(LogLevel.Debug, ex.StackTrace);
        } finally {
            Log(LogLevel.Debug, $"{nameof(CreateAndHostClipPageAsync)} execution finished.");
        }
    }

    private Task ConfigureAudioForPlayerSourceAsync() {
        var monitorTypePayload = GenerateSetAudioMonitorTypePayload();
        var monitorTypeResponse = CPH.ObsSendRaw(monitorTypePayload.RequestType,
                                                 JsonConvert.SerializeObject(monitorTypePayload.RequestData));

        if (string.IsNullOrEmpty(monitorTypeResponse) || monitorTypeResponse != "{}") {
            Log(LogLevel.Error, "Failed to set monitor type for the Player source.");

            return Task.CompletedTask;
        }

        var inputVolumePayload = GenerateSetInputVolumePayload();
        var inputVolumeResponse = CPH.ObsSendRaw(inputVolumePayload.RequestType,
                                                 JsonConvert.SerializeObject(inputVolumePayload.RequestData));


        if (string.IsNullOrEmpty(inputVolumeResponse) || inputVolumeResponse != "{}") {
            Log(LogLevel.Error, "Failed to set volume for the Player source.");

            return Task.CompletedTask;
        }

        var gainFilterPayload = GenerateGainFilterPayload();
        var gainFilterResponse = CPH.ObsSendRaw(gainFilterPayload.RequestType,
                                                JsonConvert.SerializeObject(gainFilterPayload.RequestData));

        if (string.IsNullOrEmpty(gainFilterResponse) || gainFilterResponse != "{}") {
            Log(LogLevel.Error, "Failed to add Gain filter to the Player source.");

            return Task.CompletedTask;
        }

        var compressorFilterPayload = GenerateCompressorFilterPayload();
        var compressorFilterResponse = CPH.ObsSendRaw(compressorFilterPayload.RequestType,
                                                      JsonConvert.SerializeObject(compressorFilterPayload.RequestData));

        if (string.IsNullOrEmpty(compressorFilterResponse) || compressorFilterResponse != "{}") {
            Log(LogLevel.Error, "Failed to add Compressor filter to the Player source.");

            return Task.CompletedTask;
        }

        Log(LogLevel.Info, "Audio configuration for Player source completed successfully.");

        return Task.CompletedTask;
    }

    private static Payload GenerateCompressorFilterPayload() {
        const string playerSourceName = "Player";
        const string compressorFilterName = "Compressor";

        return new Payload {
            RequestType = "CreateSourceFilter",
            RequestData = new {
                sourceName = playerSourceName,
                filterName = compressorFilterName,
                filterType = "compressor_filter",
                filterSettings = new {
                    ratio = 2.0,
                    threshold = -30.0,
                    attack_time = 69,
                    release_time = 120,
                    gain = 0.0
                }
            }
        };
    }

    private static Payload GenerateGainFilterPayload() {
        const string playerSourceName = "Player";
        const string gainFilterName = "Gain";

        return new Payload {
            RequestType = "CreateSourceFilter",
            RequestData = new {
                sourceName = playerSourceName,
                filterName = gainFilterName,
                filterType = "gain_filter",
                filterSettings = new { gain = 6.0 }
            }
        };
    }

    private static IPayload GenerateSetInputVolumePayload() {
        const string playerSourceName = "Player";

        return new Payload {
            RequestType = "SetInputVolume", RequestData = new { inputName = playerSourceName, inputVolumeDb = -6 }
        };
    }

    private static Payload GenerateSetAudioMonitorTypePayload() {
        const string playerSourceName = "Player";

        return new Payload {
            RequestType = "SetInputAudioMonitorType",
            RequestData = new { inputName = playerSourceName, monitorType = "OBS_MONITORING_TYPE_MONITOR_AND_OUTPUT" }
        };
    }

    private static (string StreamerName, string ClipTitle, string CuratorName) ExtractClipInfo(ClipData clipData) {
        return GetClipInfo(clipData);
    }

    private async Task<ClipData> FetchClipDataIfNeeded(ClipData clipData, string clipUrl) {
        Log(LogLevel.Debug,
            $"Entering {nameof(FetchClipDataIfNeeded)} with: clipData={(clipData != null ? JsonConvert.SerializeObject(clipData) : "null")}, clipUrl={(string.IsNullOrEmpty(clipUrl) ? "null" : clipUrl)}");

        if (clipData == null) {
            if (string.IsNullOrEmpty(clipUrl)) {
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

        if (string.IsNullOrEmpty(clipData.Id) || string.IsNullOrEmpty(clipData.Url)) {
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

    private static string GetValueOrDefault(string value, string defaultValue) {
        return string.IsNullOrEmpty(value) ? defaultValue : value;
    }

    private static TimeSpan GetDurationWithSetupDelay(float durationInSeconds) {
        return TimeSpan.FromSeconds(durationInSeconds + AdditionalHtmlSetupDelaySeconds);
    }

    private async Task<T> FetchTwitchData<T>(string endpoint) {
        var clientId = CPH.TwitchClientId;
        var clientSecret = CPH.TwitchOAuthToken;

        try {
            if (string.IsNullOrEmpty(clientSecret))
                throw new InvalidOperationException("Twitch client secret is missing or invalid.");

            using var client = new HttpClient();

            client.DefaultRequestHeaders.Add("Client-ID", clientId);
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {clientSecret}");

            var response = await client.GetAsync(endpoint);

            if (!response.IsSuccessStatusCode) {
                Log(LogLevel.Error, $"Twitch API request failed: {response.ReasonPhrase}");

                return default;
            }

            var content = await response.Content.ReadAsStringAsync();
            var apiResponse = JsonConvert.DeserializeObject<TwitchApiResponse<T>>(content);

            return apiResponse?.Data != null && apiResponse.Data.Any() ? apiResponse.Data.First() : default;
        } catch (Exception ex) {
            Log(LogLevel.Error, $"{GetErrorMessagePreamble(nameof(FetchTwitchData))}: {ex.Message}");

            return default;
        }
    }

    private async Task<Clip> FetchClipById(string clipId) {
        try {
            Log(LogLevel.Info, $"Fetching clip data for Clip ID: {clipId}");

            var endpoint = $"https://api.twitch.tv/helix/clips?id={clipId}";
            var clipData = await FetchTwitchData<ClipData>(endpoint);

            if (clipData == null) throw new Exception($"No data was returned for Clip ID: {clipId}");

            Log(LogLevel.Debug, $"Creating Clip entity for Clip ID: {clipId}");

            var clip = Clip.FromTwitchClip(clipData);

            lock (_clipDataCache) {
                _clipDataCache[clipId] = clip.ToClipData(CPH);
            }

            Log(LogLevel.Info, $"Successfully fetched and cached clip data for Clip ID: {clipId}");

            return clip;
        } catch (Exception ex) {
            Log(LogLevel.Error, ex.Message);

            return null;
        }
    }

    private async Task<string> FetchGameNameAsync(string gameId) {
        if (string.IsNullOrEmpty(gameId)) {
            Log(LogLevel.Warn, "Game ID is empty or null. Returning 'Unknown Game'.");

            return "Unknown Game";
        }

        try {
            var endpoint = $"https://api.twitch.tv/helix/games?id={gameId}";
            var gameData = await FetchTwitchData<GameData>(endpoint);

            if (gameData == null || string.IsNullOrEmpty(gameData.Name)) return "Unknown Game";

            return gameData.Name;
        } catch (Exception ex) {
            Log(LogLevel.Error, $"{GetErrorMessagePreamble(nameof(FetchGameNameAsync))}: {ex.Message}");
        }

        return "Unknown Game";
    }

    private async Task<ClipData> GetClipData(string clipUrl) {
        var clipId = clipUrl.Substring(clipUrl.LastIndexOf("/", StringComparison.Ordinal) + 1);

        return await FetchTwitchData<ClipData>($"https://api.twitch.tv/helix/clips?id={clipId}");
    }

    private async Task<string> EnsureCliparinoInCurrentSceneAsync(string currentScene, string clipUrl) {
        try {
            if (string.IsNullOrEmpty(currentScene)) currentScene = CPH.ObsGetCurrentScene();

            if (string.IsNullOrEmpty(currentScene)) {
                Log(LogLevel.Warn, "Current scene is empty or null.");

                return "Unknown Scene";
            }

            await EnsureCliparinoInSceneAsync(currentScene, clipUrl);
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error in EnsureCliparinoInCurrentSceneAsync: {ex.Message}");
        }


        try {
            var clipData = await GetClipData(clipUrl);
            var gameId = clipData?.GameId;
            var gameData = await FetchTwitchData<GameData>($"https://api.twitch.tv/helix/games?id={gameId}");

            return gameData?.Name ?? "Unknown Game";
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error fetching game name: {ex.Message}");

            return "Unknown Game";
        }
    }

    private object GetCurrentSourceUrl(string sceneName, string sourceName) {
        try {
            if (!SceneExists(sceneName)) {
                Log(LogLevel.Error, $"Scene '{sceneName}' does not exist.");

                return null;
            }

            if (!SourceExistsInScene(sceneName, sourceName)) {
                Log(LogLevel.Error, $"Source '{sourceName}' does not exist in scene '{sceneName}'.");

                return null;
            }

            var sourceProperties = GetSourceProperties(sceneName, sourceName);

            if (sourceProperties is Dictionary<string, object> dictionary && dictionary.TryGetValue("url", out var url))
                return url;

            Log(LogLevel.Error, $"Failed to retrieve URL for source '{sourceName}' in scene '{sceneName}'.");

            return null;
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Exception in {nameof(GetCurrentSourceUrl)}: {ex.Message}");

            return null;
        }
    }

    private object GetSourceProperties(string sceneName, string sourceName) {
        try {
            var payload = new { requestType = "GetInputSettings", requestData = new { inputName = sourceName } };
            var responseJson = CPH.ObsSendRaw(payload.requestType, JsonConvert.SerializeObject(payload.requestData));
            var response = JsonConvert.DeserializeObject<JObject>(responseJson);

            if (response != null && response.TryGetValue("inputSettings", out var inputSettingsToken))
                return inputSettingsToken.ToObject<Dictionary<string, object>>();

            Log(LogLevel.Warn, $"Failed to retrieve properties for source '{sourceName}' in scene '{sceneName}'.");

            return null;
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error in {nameof(GetSourceProperties)}: {ex.Message}");

            return null;
        }
    }

    private void SetBrowserSource(string baseUrl, string targetScene = null) {
        Log(LogLevel.Debug, $"SetBrowserSource was called for URL '{baseUrl}'.");

        var sourceUrl = CreateSourceUrl(baseUrl);
        var currentVisibility = CPH.ObsIsSourceVisible(targetScene, "Cliparino");

        Log(LogLevel.Debug, $"Cliparino visibility before update: {currentVisibility}");

        targetScene ??= CPH.ObsGetCurrentScene()
                        ?? throw new InvalidOperationException("Unable to retrieve target scene.");

        const string cliparinoSourceName = "Cliparino";

        if (!SourceExistsInScene(targetScene, cliparinoSourceName)) {
            AddSceneSource(targetScene, cliparinoSourceName);
            Log(LogLevel.Info, $"Added '{cliparinoSourceName}' scene source to '{targetScene}'.");
        } else {
            UpdateBrowserSource(targetScene, cliparinoSourceName, sourceUrl);

            if (baseUrl != "about:blank") return;

            Log(LogLevel.Info, "Hiding Cliparino source after setting 'about:blank'.");
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

        Log(LogLevel.Info, $"Refreshed browser source 'Player'.\nResponse: {response}");
    }

    private int GetSceneItemId(string targetScene, string sourceName) {
        try {
            const string requestType = "GetSceneItemId";
            var requestData = new { sceneName = targetScene, sourceName };
            var payload = new { requestType, requestData };
            var responseJson = CPH.ObsSendRaw(payload.requestType, JsonConvert.SerializeObject(payload.requestData));
            var response = JsonConvert.DeserializeObject<dynamic>(responseJson);

            if (response != null && response.sceneItemId != null && !response.ToString().Contains("Error"))
                return response.sceneItemId;

            Log(LogLevel.Warn, $"Scene item ID not found for source '{sourceName}' in scene '{targetScene}'.");
            Log(LogLevel.Debug, $"Response: {responseJson}");

            return -1;
        } catch (Exception ex) {
            Log(LogLevel.Error, ex.Message);

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
                    $"Failed to retrieve scene item ID for source '{sourceName}' in scene '{sceneName}'.");

                return false;
            }

            Log(LogLevel.Info, $"Scene item ID for source '{sourceName}' in scene '{sceneName}' is {sceneItemId}.");

            return true;
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error in SourceExistsInScene: {ex.Message}");

            return false;
        }
    }

    private bool SceneExists(string sceneName) {
        try {
            Log(LogLevel.Debug, $"Checking existence of scene '{sceneName}'.");

            var payload = new { requestType = "GetSceneList" };
            var responseJson = CPH.ObsSendRaw(payload.requestType, "{}");

            if (!string.IsNullOrWhiteSpace(responseJson)) {
                var response = JsonConvert.DeserializeObject<JObject>(responseJson);

                if (response != null
                    && response.TryGetValue("scenes", out var scenesToken)
                    && scenesToken is JArray scenesArray) {
                    var exists = scenesArray.Any(scene => string.Equals(scene["sceneName"]?.ToString(),
                                                                        sceneName,
                                                                        StringComparison.OrdinalIgnoreCase));
                    Log(LogLevel.Info, $"Scene existence check for '{sceneName}': {exists}. ");

                    return exists;
                }

                Log(LogLevel.Warn, "OBS response lacks valid 'scenes' property.");
            } else {
                Log(LogLevel.Warn, "Empty response received from OBS for scene validation.");
            }
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error verifying existence of scene '{sceneName}': {ex.Message}");
        }

        return false;
    }

    private void CreateScene(string sceneName) {
        try {
            var payload = new { requestType = "CreateScene", requestData = new { sceneName } };

            CPH.ObsSendRaw(payload.requestType, JsonConvert.SerializeObject(payload.requestData));

            Log(LogLevel.Info, $"Scene '{sceneName}' has been created successfully.");
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error in CreateScene: {ex.Message}");
        }
    }

    private void AddBrowserSource(string sceneName,
                                  string sourceName,
                                  string url,
                                  int width = DefaultWidth,
                                  int height = DefaultHeight) {
        Log(LogLevel.Debug, $"Update URL to OBS: {url}");

        try {
            var payload = new {
                requestType = "CreateInput",
                requestData = new {
                    sceneName,
                    inputName = sourceName,
                    inputKind = "browser_source",
                    inputSettings = new { url, width, height },
                    sceneItemEnabled = true
                }
            };

            CPH.ObsSendRaw(payload.requestType, JsonConvert.SerializeObject(payload.requestData));
            Log(LogLevel.Info, $"Browser source '{sourceName}' added to scene '{sceneName}' with URL '{url}'.");
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error in AddBrowserSource: {ex.Message}");
        }
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
            Log(LogLevel.Error, $"Error in UpdateBrowserSource: {ex.Message}");
        }
    }

    private static string CreateSourceUrl(string baseUrl, bool nonceEnabled = false) {
        return nonceEnabled ? $"{baseUrl}&nonce={GenerateNonce()}" : $"{baseUrl}";
    }

    private static string GenerateNonce() {
        return Convert.ToBase64String(Guid.NewGuid().ToByteArray())
                      .Replace("+", "-")
                      .Replace("/", "_")
                      .Replace("=", "")
                      .Substring(0, Math.Min(16, Guid.NewGuid().ToByteArray().Length));
    }

    private static string ExtractClipId(string clipUrl) {
        if (string.IsNullOrEmpty(clipUrl)) throw new ArgumentException("Clip URL cannot be null or empty.");

        var parts = clipUrl.Split('/');

        return parts.LastOrDefault(); // Return the last segment, which is the clip ID
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

    private void ConfigureAndServe() {
        try {
            lock (_serverLock) {
                CleanupServer();
                _server ??= new HttpListener();

                if (!IsPortAvailable(8080)) throw new InvalidOperationException("Port 8080 is already in use.");

                _server.Prefixes.Add("http://localhost:8080/");
                _server.Start();
                StartListening(_server);

                UpdateBrowserSource("Cliparino", "Player", "http://localhost:8080/index.htm");
                RefreshBrowserSource();
            }
        } catch (HttpListenerException ex) {
            Log(LogLevel.Error, $"Failed to start HttpListener on port 8080: {ex.Message}");
            CleanupServer();
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Unexpected error in ConfigureAndStartServer: {ex.Message}");
            CleanupServer();
        }
    }

    private static string ApplyCORSHeaders(HttpListenerResponse response) {
        var nonce = GenerateNonce();

        foreach (var header in CORSHeaders)
            response.Headers[header.Key] =
                header.Value.Replace("[[nonce]]", nonce).Replace("\r", "").Replace("\n", " ");

        return nonce;
    }

    private static bool IsPortAvailable(int port) {
        try {
            var listener = new TcpListener(IPAddress.Loopback, port);

            listener.Start();
            listener.Stop();

            return true;
        } catch (SocketException) {
            return false;
        }
    }

    private void StartListening(HttpListener server) {
        Task.Run(async () => {
                     HttpListenerResponse response = null;

                     try {
                         Log(LogLevel.Info, "HTTP server started on http://localhost:8080");

                         while (server.IsListening) {
                             var context = await server.GetContextAsync();

                             response = context.Response;

                             var requestPath = context.Request.Url?.AbsolutePath;
                             var nonce = ApplyCORSHeaders(response);

                             string responseText;
                             string contentType;
                             var htmlInMemory = GetHtmlInMemorySafe().Replace("[[nonce]]", nonce);

                             switch (requestPath) {
                                 case "/index.css":
                                     contentType = "text/css; charset=utf-8";
                                     responseText = CSSText;

                                     break;

                                 case "/":
                                 case "/index.htm":
                                     contentType = "text/html; charset=utf-8";
                                     responseText = htmlInMemory;

                                     break;

                                 default:
                                     responseText = "404 Not Found";
                                     contentType = "text/plain; charset=utf-8";
                                     response.StatusCode = 404;

                                     break;
                             }

                             var responseBytes = Encoding.UTF8.GetBytes(responseText);

                             response.ContentType = contentType;
                             response.ContentLength64 = responseBytes.Length;

                             await response.OutputStream.WriteAsync(responseBytes, 0, responseBytes.Length);

                             response.OutputStream.Close();
                         }
                     } catch (HttpListenerException ex) {
                         Log(LogLevel.Error, $"HttpListener error: {ex.Message}");
                     } catch (Exception ex) {
                         Log(LogLevel.Error, $"Error in StartListening: {ex.Message}");
                     } finally {
                         response?.OutputStream.Close();
                     }
                 });
    }

    private string GetHtmlInMemorySafe() {
        lock (_serverLock) {
            return _htmlInMemory;
        }
    }

    private void DisposeServer(HttpListener server = null) {
        try {
            lock (_serverLock) {
                server ??= _server;
            }

            if (server == null) return;

            server.Close();
            server.Abort();
            Log(LogLevel.Info, "HttpListener has been disposed.");
        } catch (Exception ex) {
            Log(LogLevel.Error, $"Error while disposing HttpListener: {ex.Message}");
        } finally {
            lock (_serverLock) {
                _server = null;
            }
        }
    }

    private void StopServer(HttpListener server = null) {
        lock (_serverLock) {
            server ??= _server;

            if (server == null) return;

            try {
                if (!server.IsListening) return;

                server.Stop();

                Log(LogLevel.Info, "HttpListener has been stopped.");
            } catch (Exception ex) {
                Log(LogLevel.Error, $"Unexpected error while stopping the HttpListener: {ex.Message}");
            }
        }
    }

    private void CleanupServer(HttpListener server = null) {
        lock (_serverLock) {
            server ??= _server;

            if (server == null) return;

            StopServer(server);
            DisposeServer(server);
        }
    }

    ~CPHInline() {
        CleanupServer();
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

    private enum LogLevel {
        Debug,
        Info,
        Warn,
        Error
    }

    private class ClipSettings(bool featuredOnly, int maxClipSeconds, int clipAgeDays) {
        public bool FeaturedOnly { get; } = featuredOnly;
        public int MaxClipSeconds { get; } = maxClipSeconds;
        public int ClipAgeDays { get; } = clipAgeDays;
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
        ///     Creates a
        ///     <see cref="Clip" />
        ///     object from a Twitch clip's raw JSON data.
        /// </summary>
        /// <param name="twitchClip">
        ///     A JSON object representing the raw Twitch clip data.
        /// </param>
        /// <returns>
        ///     An instance of the
        ///     <see cref="Clip" />
        ///     class with data populated from Twitch.
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
        ///     Creates a
        ///     <see cref="Clip" />
        ///     object from a
        ///     <see cref="ClipData" />
        ///     instance.
        /// </summary>
        /// <param name="twitchClipData">
        ///     An instance of the
        ///     <see cref="ClipData" />
        ///     class populated with clip information.
        /// </param>
        /// <returns>
        ///     An instance of
        ///     <see cref="Clip" />
        ///     created from the
        ///     <see cref="ClipData" />
        ///     .
        /// </returns>
        /// <exception cref="InvalidOperationException">
        ///     Thrown if the
        ///     <see cref="ClipData" />
        ///     is invalid or null.
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
        ///     Converts the
        ///     <see cref="Clip" />
        ///     object into a
        ///     <see cref="ClipData" />
        ///     representation for interaction with other parts of the system.
        /// </summary>
        /// <param name="cphInstance">
        ///     A reference to the parent
        ///     <see cref="CPHInline" />
        ///     instance.
        /// </param>
        /// <returns>
        ///     A new
        ///     <see cref="ClipData" />
        ///     object populated with data from this
        ///     <see cref="Clip" />
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
    }

    private class TwitchApiResponse<T>(T[] data) {
        public T[] Data { get; } = data;
    }

    private class GameData {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
    }

    private interface IPayload {
        public string RequestType { get; }
        public object RequestData { get; }
    }

    private class Payload : IPayload {
        public string RequestType { get; set; }
        public object RequestData { get; set; }
    }
}