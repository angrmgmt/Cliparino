// Cliparino is a clip player for Twitch.tv built to work with Streamer.bot.
// Copyright (C) 2024 Scott Mongrain $angrmgmt@gmail.com
//
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
//
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301
// USA

#region

using System;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Streamer.bot.Common.Events;
using Streamer.bot.Plugin.Interface;
using Streamer.bot.Plugin.Interface.Enums;
using Streamer.bot.Plugin.Interface.Model;
using Twitch.Common.Models.Api;

#endregion

namespace Cliparino;
// for testing only, remove in production

/// <summary>
///     The canonical primary class used in all Streamer.bot scripts. This instance of the class is used to implement a
///     clip player.
/// </summary>
/// <remarks>
///     SupressMessage attributes are used to suppress warnings that are not relevant during production, but which occur
///     during testing. These attributes can be removed in production. Further, the CPHInline class is already derived
///     from CPHInlineBase, so it should not be derived from CPHInlineBase again in production.
/// </remarks>
[SuppressMessage("ReSharper", "MemberCanBeMadeStatic.Local")]
[SuppressMessage("Performance", "CA1822:Mark members as static")]
[SuppressMessage("ReSharper", "UseIndexFromEndExpression")]
[SuppressMessage("ReSharper", "ArrangeThisQualifier")]
public class CPHInline : CPHInlineBase {
    private readonly IOBSManager _obsManager;

    /// <summary>
    ///     Initializes a new instance of the
    ///     <see cref="CPHInline" />
    ///     class, but specifically for testing purposes.
    /// </summary>
    /// <param name="cph">
    /// </param>
    /// <remarks>
    ///     This constructor is used to inject a mock
    ///     <see cref="IInlineInvokeProxy" />
    ///     instance for testing. In production scenarios, Streamer.bot initializes the
    ///     <see cref="IInlineInvokeProxy" />
    ///     instance automatically. In the production environment, this should be removed.
    /// </remarks>
    public CPHInline(IInlineInvokeProxy cph) {
        CPH = cph;
        ICPHStore cphStore = new CPHStoreWrapper();

        if (!cphStore.IsInitialized) cphStore.Init(CPH);

        _obsManager = new OBSManager(cphStore.GetCPH());
    }

    /// <summary>
    ///     Gets or sets the current clip.
    /// </summary>
    private ClipData CurrentClip { get; set; }

    /// <summary>
    ///     Gets or sets the clip player scene's dimensions (height and width).
    /// </summary>
    private (int height, int width) Dimensions { get; set; }

    /// <summary>
    ///     Gets or sets the last played clip.
    /// </summary>
    private ClipData LastPlayedClip { get; set; }

    /// <summary>
    ///     The instance of the Channel Points Helper (CPH) representing Streamer.bot and its features.
    /// </summary>
    public new IInlineInvokeProxy CPH { get; }

    /// <summary>
    ///     Initializes the CPHInline instance.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if
    ///     <see cref="IInlineInvokeProxy" />
    ///     <see cref="CPHInlineBase.CPH" />
    ///     has not been initialized.
    /// </exception>
    /// <remarks>
    ///     This is called from Streamer.bot prior to the
    ///     <see cref="Execute" />
    ///     method.
    /// </remarks>
    public void Init() {
        try {
            CPHStore.Init(CPH);
        } catch (InvalidOperationException ex) {
            OBSManager.Logger.Log($"Exception in CPHInline.Init: {ex.Message}", OBSManager.Logger.LogLevel.Error);
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Unexpected error in CPHInline.Init: {ex.Message}",
                                  OBSManager.Logger.LogLevel.Error);
        }
    }

    /// <summary>
    ///     The canonical Streamer.bot entry function in CPHInline. Executes the main command handler.
    /// </summary>
    /// <returns>
    ///     True if the command was handled successfully; otherwise, false.
    /// </returns>
    public bool Execute() {
        OBSManager.Logger.Log("Execute method called", OBSManager.Logger.LogLevel.Info);

        try {
            if (!TryParseArguments(out var mainCommand, out var input0, out var height, out var width)) {
                OBSManager.Logger.Log("Failed to parse arguments", OBSManager.Logger.LogLevel.Error);

                return false;
            }

            Dimensions = (height, width);
            OBSManager.VideoDimensions = (height, width);
            mainCommand = mainCommand.ToLower();
            _obsManager.Init();

            return HandleCommand(mainCommand, input0);
        } catch (AggregateException ex) {
            foreach (var innerException in ex.InnerExceptions) {
                // Log each individual exception
                OBSManager.Logger.Log($"Execute method - Inner exception: {innerException.Message}",
                                      OBSManager.Logger.LogLevel.Error);
                OBSManager.Logger.Log(innerException.StackTrace, OBSManager.Logger.LogLevel.Error);
            }

            return false;
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception occurred in Execute: {ex.Message}", OBSManager.Logger.LogLevel.Error);

            return false;
        }
    }

    /// <summary>
    ///     Calculates the delay based on the current clip's duration and a two-second buffer.
    /// </summary>
    /// <returns>
    ///     The calculated delay in milliseconds.
    /// </returns>
    private int CalculateDelay() {
        OBSManager.Logger.Log("CalculateDelay method called", OBSManager.Logger.LogLevel.Info);

        try {
            const int buffer = 2000;
            var duration = CurrentClip?.Duration ?? GetMaxClipDuration() * 1000;
            var delay = buffer + (int)Math.Round(duration) * 1000;

            OBSManager.Logger.Log($"Total clip duration: {duration} s", OBSManager.Logger.LogLevel.Info);
            OBSManager.Logger.Log($"Calculated delay: {delay} ms ({delay / 1000} s)", OBSManager.Logger.LogLevel.Info);

            return delay;
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in CalculateDelay: {ex.Message}", OBSManager.Logger.LogLevel.Error);

            return -1;
        }
    }

    /// <summary>
    ///     Converts a JObject to a ClipData object.
    /// </summary>
    /// <param name="clipJObject">
    ///     The JObject representing the clip data.
    /// </param>
    /// <returns>
    ///     The converted ClipData object.
    /// </returns>
    private ClipData ConvertToClipData(JObject clipJObject) {
        OBSManager.Logger.Log("ConvertToClipData method called", OBSManager.Logger.LogLevel.Info);
        OBSManager.Logger.Log("Creating new JObject clipData from clipJObject[\"data\"]",
                              OBSManager.Logger.LogLevel.Info);

        try {
            var clipDataToken = clipJObject["data"];

            if (clipDataToken == null) {
                OBSManager.Logger.Log("clipJObject[\"data\"] is null.", OBSManager.Logger.LogLevel.Error);

                return new ClipData();
            }

            OBSManager.Logger.Log("Creating new ClipData object clipData from JToken? clipDataToken",
                                  OBSManager.Logger.LogLevel.Info);

            var clipData = ExtractClipData(clipDataToken);

            return CreateClipData(clipData);
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception occurred in ConvertToClipData: {ex.Message}",
                                  OBSManager.Logger.LogLevel.Error);

            return new ClipData();
        }
    }

    /// <summary>
    ///     Creates a ClipData object from a JObject.
    /// </summary>
    /// <param name="clipData">
    ///     The JObject representing the clip data.
    /// </param>
    /// <returns>
    ///     The created ClipData object.
    /// </returns>
    private ClipData CreateClipData(JObject clipData) {
        OBSManager.Logger.Log("CreateClipData method called", OBSManager.Logger.LogLevel.Info);

        try {
            return new ClipData {
                Id = clipData["id"]?.ToString() ?? string.Empty,
                Url = clipData["url"]?.ToString() ?? string.Empty,
                EmbedUrl = clipData["embed_url"]?.ToString() ?? string.Empty,
                BroadcasterId = clipData["broadcaster_id"]?.ToString() ?? "0",
                BroadcasterName = clipData["broadcaster_name"]?.ToString() ?? string.Empty,
                CreatorId = clipData["creator_id"]?.ToObject<int>() ?? 0,
                CreatorName = clipData["creator_name"]?.ToString() ?? string.Empty,
                VideoId = clipData["video_id"]?.ToString() ?? string.Empty,
                GameId = clipData["game_id"]?.ToString() ?? string.Empty,
                Language = clipData["language"]?.ToString() ?? string.Empty,
                Title = clipData["title"]?.ToString() ?? string.Empty,
                ViewCount = clipData["view_count"]?.ToObject<int>() ?? 0,
                CreatedAt = clipData["created_at"]?.ToObject<DateTime>() ?? DateTime.MinValue,
                ThumbnailUrl = clipData["thumbnail_url"]?.ToString() ?? string.Empty,
                Duration = clipData["duration"]?.ToObject<float>() ?? 0.0f,
                IsFeatured = clipData["is_featured"]?.ToObject<bool>() ?? false
            };
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in CreateClipData: {ex.Message}", OBSManager.Logger.LogLevel.Error);

            return new ClipData();
        }
    }

    /// <summary>
    ///     Extracts clip data from a JToken.
    /// </summary>
    /// <param name="clipDataToken">
    ///     The JToken representing the clip data.
    /// </param>
    /// <returns>
    ///     The extracted JObject representing the clip data.
    /// </returns>
    private JObject ExtractClipData(JToken clipDataToken) {
        OBSManager.Logger.Log("ExtractClipData method called", OBSManager.Logger.LogLevel.Info);

        try {
            var result = clipDataToken switch {
                JArray { Count: > 0 } clipDataArray => clipDataArray[0] as JObject,
                JObject clipDataObject => clipDataObject,
                _ => null
            };

            if (result is null)
                OBSManager.Logger.Log("clipDataToken did not match any expected types.",
                                      OBSManager.Logger.LogLevel.Warn);

            return result ?? new JObject();
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in ExtractClipData: {ex.Message}", OBSManager.Logger.LogLevel.Error);

            return new JObject();
        }
    }

    /// <summary>
    ///     Finds a specific clip using the provided API URL.
    /// </summary>
    /// <param name="apiURL">
    ///     The API URL from which to fetch the clip data.
    /// </param>
    /// <returns>
    ///     The found ClipData object.
    /// </returns>
    private async Task<ClipData> FindSpecificClipAsync(string apiURL) {
        OBSManager.Logger.Log("FindSpecificClipAsync method called", OBSManager.Logger.LogLevel.Info);

        try {
            var clip = await GetClipAsync(apiURL);

            return clip;
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in FindSpecificClipAsync: {ex.Message}",
                                  OBSManager.Logger.LogLevel.Error);

            return new ClipData();
        }
    }

    /// <summary>
    ///     Generates the embed URL for the current or last played clip.
    /// </summary>
    /// <returns>
    ///     The generated embed URL.
    /// </returns>
    private string GenerateClipEmbedUrl() {
        OBSManager.Logger.Log("GenerateClipEmbedUrl method called", OBSManager.Logger.LogLevel.Info);

        try {
            var embedUrl = CurrentClip != null ? CurrentClip.EmbedUrl : LastPlayedClip?.EmbedUrl;

            if (!string.IsNullOrEmpty(embedUrl)) return $"{embedUrl}{GenerateQueryParams()}";

            OBSManager.Logger.Log("Embed URL is empty.", OBSManager.Logger.LogLevel.Error);
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in GenerateClipEmbedUrl: {ex.Message}", OBSManager.Logger.LogLevel.Error);
        }

        return "about:blank";
    }

    /// <summary>
    ///     Generates the Helix API URL for a given clip URL.
    /// </summary>
    /// <param name="clipURL">
    ///     The clip URL.
    /// </param>
    /// <returns>
    ///     The generated Helix API URL.
    /// </returns>
    private string GenerateHelixURL(string clipURL) {
        OBSManager.Logger.Log("GenerateHelixURL method called", OBSManager.Logger.LogLevel.Info);

        try {
            string[] clipIDs;
            string clipID;

            if (clipURL.Contains("clips.twitch.tv")) {
                clipIDs = clipURL.Split('/');
                clipID = clipIDs[clipIDs.Length - 1];
            } else if (clipURL.Contains("twitch.tv") && clipURL.Contains("/clip/")) {
                clipIDs = clipURL.Split('/');
                clipID = clipIDs[clipIDs.Length - 1];
            } else {
                OBSManager.Logger.Log("Invalid clip URL format.", OBSManager.Logger.LogLevel.Error);

                return string.Empty;
            }

            var helixURL = $"https://api.twitch.tv/helix/clips?id={clipID}";
            var message = $"Helix URL generated as: {helixURL}";

            OBSManager.Logger.Log(message, OBSManager.Logger.LogLevel.Info);

            return helixURL;
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in GenerateHelixURL: {ex.Message}", OBSManager.Logger.LogLevel.Error);

            return string.Empty;
        }
    }

    /// <summary>
    ///     Generates query parameters for the clip embed URL.
    /// </summary>
    /// <returns>
    ///     The generated query parameters.
    /// </returns>
    private string GenerateQueryParams() {
        OBSManager.Logger.Log("GenerateQueryParams method called", OBSManager.Logger.LogLevel.Info);

        try {
            var queryParams =
                $"&allowfullscreen&autoplay=true&controls=false&height={Dimensions.height}&mute=false&parent=localhost&preload&width={Dimensions.width}";
            OBSManager.Logger.Log($"Generated query parameters: {queryParams}", OBSManager.Logger.LogLevel.Info);

            return queryParams;
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in GenerateQueryParams: {ex.Message}", OBSManager.Logger.LogLevel.Error);

            return string.Empty;
        }
    }

    /// <summary>
    ///     Gets and plays a clip for a specific channel.
    /// </summary>
    /// <param name="userInfo">
    ///     The user information of the channel.
    /// </param>
    /// <returns>
    ///     True if a clip was found and played; otherwise, false.
    /// </returns>
    private bool GetAndPlayClipForChannel(TwitchUserInfoEx userInfo) {
        OBSManager.Logger.Log("GetAndPlayClipForChannel method called", OBSManager.Logger.LogLevel.Info);

        try {
            var clips = GetClips(userInfo.UserId, userInfo.UserName);

            OBSManager.Logger.Log($"Fetched {clips.Count} clips for user {userInfo.UserName}",
                                  OBSManager.Logger.LogLevel.Info);

            if (CPH.TryGetArg("featuredOnly", out bool featuredOnly) && featuredOnly) {
                OBSManager.Logger.Log("Filtering clips to include only featured ones", OBSManager.Logger.LogLevel.Info);
                clips = clips.Where(clip => clip.IsFeatured).ToList();
            }

            if (clips.Count != 0) {
                OBSManager.Logger.Log("Playing a random clip", OBSManager.Logger.LogLevel.Info);
                PlayRandomClip(clips);

                return true;
            }

            OBSManager.Logger.Log("No clips found to play", OBSManager.Logger.LogLevel.Warn);
            CPH.SendMessage($"Well, looks like @{userInfo.UserName} doesn't have any clips... yet. Let's love on "
                            + $"them a bit and maybe even tickle that follow anyway!! https://twitch.tv/{userInfo.UserName}");

            return false;
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in GetAndPlayClipForChannel: {ex.Message}",
                                  OBSManager.Logger.LogLevel.Error);

            return false;
        }
    }

    /// <summary>
    ///     Fetches data from the provided API URL and processes it using the specified function.
    /// </summary>
    /// <typeparam name="T">
    ///     The type of the result to be returned.
    /// </typeparam>
    /// <param name="apiUrl">
    ///     The API URL to fetch the data from.
    /// </param>
    /// <param name="processResponse">
    ///     A function to process the response body and return the desired result.
    /// </param>
    /// <returns>
    ///     A task that represents the asynchronous operation. The task result contains the processed data.
    /// </returns>
    private async Task<T> FetchDataAsync<T>(string apiUrl, Func<string, T> processResponse) {
        OBSManager.Logger.Log("FetchDataAsync method called", OBSManager.Logger.LogLevel.Info);

        try {
            using var client = InitClient(apiUrl, out var request);
            var response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode) {
                LogRequestError(response);

                return default;
            }

            var responseBody = await response.Content.ReadAsStringAsync();

            return processResponse(responseBody);
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in FetchDataAsync: {ex.Message}", OBSManager.Logger.LogLevel.Error);

            return default;
        }
    }

    /// <summary>
    ///     Asynchronously fetches a clip from the provided API URL.
    /// </summary>
    /// <param name="apiUrl">
    ///     The API URL to fetch the clip data from.
    /// </param>
    /// <returns>
    ///     A task that represents the asynchronous operation. The task result contains the fetched ClipData object.
    /// </returns>
    private async Task<ClipData> GetClipAsync(string apiUrl) {
        return await FetchDataAsync(apiUrl,
                                    responseBody => {
                                        var clip = ConvertToClipData(ParseClips(responseBody));

                                        LogClipData(clip);
                                        CurrentClip = clip;

                                        return clip;
                                    });
    }

    /// <summary>
    ///     Fetches the name of the game associated with the given game ID.
    /// </summary>
    /// <param name="gameID">
    ///     The ID of the game to fetch the name for.
    /// </param>
    /// <returns>
    ///     The name of the game, or an empty string if the game could not be found.
    /// </returns>
    private async Task<string> GetGameAsync(string gameID) {
        var apiURL = $"https://api.twitch.tv/helix/games?id={gameID}";

        return await FetchDataAsync(apiURL,
                                    responseBody => {
                                        var name = JObject.Parse(responseBody)["data"]?[0]?["name"]?.ToString();

                                        OBSManager.Logger.Log($"Game name: {name}", OBSManager.Logger.LogLevel.Info);

                                        return name ?? string.Empty;
                                    });
    }

    /// <summary>
    ///     Initializes an HttpClient with the specified API URL and request headers.
    /// </summary>
    /// <param name="apiURL">
    ///     The API URL to which the request will be made.
    /// </param>
    /// <param name="request">
    ///     The HttpRequestMessage object to be initialized.
    /// </param>
    /// <returns>
    ///     The initialized HttpClient object.
    /// </returns>
    private HttpClient InitClient(string apiURL, out HttpRequestMessage request) {
        HttpClient client = null;

        try {
            var clientId = CPH.TwitchClientId;
            var accessToken = CPH.TwitchOAuthToken;

            client = new HttpClient();
            request = new HttpRequestMessage(HttpMethod.Get, apiURL);

            request.Headers.Add("Client-Id", clientId);
            request.Headers.Add("Authorization", $"Bearer {accessToken}");
            OBSManager.Logger.Log($"Making HTTP request with API URL {apiURL}", OBSManager.Logger.LogLevel.Info);
            OBSManager.Logger.Log($"Headers for request: {request.Headers}", OBSManager.Logger.LogLevel.Info);

            return client;
        } catch {
            client?.Dispose();

            throw;
        }
    }

    /// <summary>
    ///     Gets a list of clips for a specific user.
    /// </summary>
    /// <param name="userId">
    ///     The user ID.
    /// </param>
    /// <param name="userName">
    ///     The username.
    /// </param>
    /// <returns>
    ///     A list of ClipData objects.
    /// </returns>
    private List<ClipData> GetClips(string userId, string userName) {
        OBSManager.Logger.Log("GetClips method called", OBSManager.Logger.LogLevel.Info);

        var timeSpans = new List<TimeSpan> {
            TimeSpan.Zero,
            TimeSpan.FromDays(1),
            TimeSpan.FromDays(7),
            TimeSpan.FromDays(30),
            TimeSpan.FromDays(365),
            TimeSpan.FromDays(3000)
        };

        try {
            foreach (var timeSpan in timeSpans) {
                var clips = CPH.GetClipsForUserById(userId, timeSpan, timeSpan == TimeSpan.Zero ? false : null);

                if (clips.Count > 0) return clips;

                LogNoClipsMessage(userName, timeSpan);
            }
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in GetClips: {ex.Message}", OBSManager.Logger.LogLevel.Error);
        }

        return [];
    }

    /// <summary>
    ///     Gets the maximum clip duration from the arguments.
    /// </summary>
    /// <returns>
    ///     The maximum clip duration in seconds.
    /// </returns>
    private int GetMaxClipDuration() {
        OBSManager.Logger.Log("GetMaxClipDuration method called", OBSManager.Logger.LogLevel.Info);

        try {
            return CPH.TryGetArg("maxClipSeconds", out string maxClipDurationStr)
                       ? int.Parse(maxClipDurationStr ?? "30")
                       : 30;
        } catch (FormatException ex) {
            OBSManager.Logger.Log($"Failed to parse maxClipSeconds argument: {ex.Message}",
                                  OBSManager.Logger.LogLevel.Error);

            return 30;
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Unexpected error in GetMaxClipDuration: {ex.Message}",
                                  OBSManager.Logger.LogLevel.Error);

            return 30;
        }
    }

    /// <summary>
    ///     Gets the most recent clip URL from the chat.
    /// </summary>
    /// <returns>
    ///     The most recent clip URL.
    /// </returns>
    private string GetMostRecentClipUrlFromChat() {
        OBSManager.Logger.Log("GetMostRecentClipUrlFromChat method called", OBSManager.Logger.LogLevel.Info);

        try {
            var clipUrl = CPH.GetGlobalVar<string>("last_clip_url");

            if (!string.IsNullOrEmpty(clipUrl)) return clipUrl;

            OBSManager.Logger.Log("No clip URL found in global variables.", OBSManager.Logger.LogLevel.Warn);

            return string.Empty;
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in GetMostRecentClipUrlFromChat: {ex.Message}",
                                  OBSManager.Logger.LogLevel.Error);

            return string.Empty;
        }
    }

    /// <summary>
    ///     Handles the specified command with the given input.
    /// </summary>
    /// <param name="command">
    ///     The command to handle.
    /// </param>
    /// <param name="input">
    ///     The input associated with the command.
    /// </param>
    /// <returns>
    ///     True if the command was handled successfully; otherwise, false.
    /// </returns>
    private bool HandleCommand(string command, string input) {
        OBSManager.Logger.Log("HandleCommand method called", OBSManager.Logger.LogLevel.Info);

        try {
            var commandHandlers = new Dictionary<string, Func<string, bool>>(StringComparer.OrdinalIgnoreCase) {
                { "!so", HandleShoutoutCommand },
                { "!watch", HandleWatchCommand },
                { "!stop", _ => HandleStopCommand() },
                { "!replay", _ => HandleReplayCommand() }
            };

            if (commandHandlers.TryGetValue(command, out var handler)) return handler(input);

            OBSManager.Logger.Log($"Unknown command: {command}", OBSManager.Logger.LogLevel.Warn);

            return false;
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in HandleCommand: {ex.Message}", OBSManager.Logger.LogLevel.Error);

            return false;
        }
    }

    /// <summary>
    ///     Handles the replay command.
    /// </summary>
    /// <returns>
    ///     True if the replay was successful; otherwise, false.
    /// </returns>
    private bool HandleReplayCommand() {
        OBSManager.Logger.Log("HandleReplayCommand method called", OBSManager.Logger.LogLevel.Info);

        try {
            return ReplayLastClip();
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in HandleReplayCommand: {ex.Message}", OBSManager.Logger.LogLevel.Error);

            return false;
        }
    }

    /// <summary>
    ///     Handles the shoutout command.
    /// </summary>
    /// <param name="channelName">
    ///     The name of the channel to shout out.
    /// </param>
    /// <returns>
    ///     True if the shoutout was successful; otherwise, false.
    /// </returns>
    private bool HandleShoutoutCommand(string channelName) {
        OBSManager.Logger.Log("HandleShoutoutCommand method called", OBSManager.Logger.LogLevel.Info);

        try {
            if (string.IsNullOrEmpty(channelName)) {
                OBSManager.Logger.Log("Channel name is empty or null.", OBSManager.Logger.LogLevel.Error);

                return false;
            }

            if (!TryGetUserInfo(channelName, out var userInfo)) {
                OBSManager.Logger.Log($"Failed to get user info for channel: {channelName}",
                                      OBSManager.Logger.LogLevel.Error);

                return false;
            }

            var message = PrepareMessage(userInfo);

            OBSManager.Logger.Log($"Shoutout message prepared: {message}", OBSManager.Logger.LogLevel.Info);
            CPH.SendMessage(message);

            return GetAndPlayClipForChannel(userInfo);
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in HandleShoutoutCommand: {ex.Message}",
                                  OBSManager.Logger.LogLevel.Error);

            return false;
        }
    }

    /// <summary>
    ///     Handles the stop command.
    /// </summary>
    /// <returns>
    ///     True if the stop command was successful; otherwise, false.
    /// </returns>
    private bool HandleStopCommand() {
        OBSManager.Logger.Log("HandleStopCommand method called.", OBSManager.Logger.LogLevel.Info);

        try {
            return OBSManager.PlayerSource.StopClipPlayer();
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in HandleStopCommand: {ex.Message}", OBSManager.Logger.LogLevel.Error);

            return false;
        }
    }

    /// <summary>
    ///     Handles the watch command.
    /// </summary>
    /// <param name="urlOrEmpty">
    ///     The URL of the clip to watch, or an empty string to watch the most recent clip.
    /// </param>
    /// <returns>
    ///     True if the watch command was successful; otherwise, false.
    /// </returns>
    private bool HandleWatchCommand(string urlOrEmpty) {
        OBSManager.Logger.Log("HandleWatchCommand method called", OBSManager.Logger.LogLevel.Info);

        try {
            if (string.IsNullOrEmpty(urlOrEmpty)) {
                var recentClipUrl = GetMostRecentClipUrlFromChat();

                if (string.IsNullOrEmpty(recentClipUrl)) {
                    OBSManager.Logger.Log("No recent clip URL found in chat.", OBSManager.Logger.LogLevel.Warn);

                    return false;
                }

                PlaySpecificClip(recentClipUrl);
            } else {
                PlaySpecificClip(urlOrEmpty);
            }

            return true;
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in HandleWatchCommand: {ex.Message}", OBSManager.Logger.LogLevel.Error);

            return false;
        }
    }

    /// <summary>
    ///     Logs the details of a ClipData object.
    /// </summary>
    /// <param name="clipData">
    ///     The ClipData object to log.
    /// </param>
    private void LogClipData(ClipData clipData) {
        OBSManager.Logger.Log("LogClipData method called", OBSManager.Logger.LogLevel.Info);
        OBSManager.Logger.Log(JsonConvert.SerializeObject(clipData, Formatting.Indented),
                              OBSManager.Logger.LogLevel.Info);
        OBSManager.Logger.Log($"Id = {clipData.Id}", OBSManager.Logger.LogLevel.Info);
        OBSManager.Logger.Log($"Url = {clipData.Url}", OBSManager.Logger.LogLevel.Info);
        OBSManager.Logger.Log($"EmbedUrl = {clipData.EmbedUrl}", OBSManager.Logger.LogLevel.Info);
        OBSManager.Logger.Log($"BroadcasterId = {clipData.BroadcasterId}", OBSManager.Logger.LogLevel.Info);
        OBSManager.Logger.Log($"BroadcasterName = {clipData.BroadcasterName}", OBSManager.Logger.LogLevel.Info);
        OBSManager.Logger.Log($"CreatorId = {clipData.CreatorId}", OBSManager.Logger.LogLevel.Info);
        OBSManager.Logger.Log($"CreatorName = {clipData.CreatorName}", OBSManager.Logger.LogLevel.Info);
        OBSManager.Logger.Log($"VideoId = {clipData.VideoId}", OBSManager.Logger.LogLevel.Info);
        OBSManager.Logger.Log($"GameId = {clipData.GameId}", OBSManager.Logger.LogLevel.Info);
        OBSManager.Logger.Log($"Language = {clipData.Language}", OBSManager.Logger.LogLevel.Info);
        OBSManager.Logger.Log($"Title = {clipData.Title}", OBSManager.Logger.LogLevel.Info);
        OBSManager.Logger.Log($"ViewCount = {clipData.ViewCount}", OBSManager.Logger.LogLevel.Info);
        OBSManager.Logger.Log($"CreatedAt = {clipData.CreatedAt}", OBSManager.Logger.LogLevel.Info);
        OBSManager.Logger.Log($"ThumbnailUrl = {clipData.ThumbnailUrl}", OBSManager.Logger.LogLevel.Info);
        OBSManager.Logger.Log($"Duration = {clipData.Duration}", OBSManager.Logger.LogLevel.Info);
        OBSManager.Logger.Log($"IsFeatured = {clipData.IsFeatured}", OBSManager.Logger.LogLevel.Info);
    }

    /// <summary>
    ///     Logs an error that occurred during an HTTP request.
    /// </summary>
    /// <param name="response">
    ///     The HTTP response that contains the error.
    /// </param>
    private void LogRequestError(HttpResponseMessage response) {
        try {
            OBSManager.Logger.Log("LogRequestError method called", OBSManager.Logger.LogLevel.Info);
            OBSManager.Logger.Log($"HTTP Request failed with status code: {response.StatusCode}",
                                  OBSManager.Logger.LogLevel.Error);

            var errorMessage = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            OBSManager.Logger.Log($"HTTP Error Message: {errorMessage}", OBSManager.Logger.LogLevel.Error);
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception occurred in LogRequestError: {ex.Message}",
                                  OBSManager.Logger.LogLevel.Error);
        }
    }

    /// <summary>
    ///     Logs a message indicating that a user has no clips based on the chosen period of time and whether clips are to be
    ///     limited to only featured clips.
    /// </summary>
    /// <param name="userName">
    ///     The name of the user/channel for whom a clip is to be shown.
    /// </param>
    /// <param name="timeSpan">
    ///     The range of creation dates over which clips are to be selected.
    /// </param>
    private void LogNoClipsMessage(string userName, TimeSpan timeSpan) {
        OBSManager.Logger.Log("LogNoClipsMessage method called", OBSManager.Logger.LogLevel.Info);

        try {
            var message = timeSpan == TimeSpan.Zero
                              ? $"{userName} has no featured clips, pulling from last 24 hours."
                              : $"{userName} has no clips from the last {timeSpan.TotalDays} days, pulling from next period.";

            OBSManager.Logger.Log($"Message: {message}", OBSManager.Logger.LogLevel.Info);
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in LogNoClipsMessage: {ex.Message}", OBSManager.Logger.LogLevel.Error);
        }
    }

    /// <summary>
    ///     Parses the clips' data from a JSON string.
    /// </summary>
    /// <param name="clipDataText">
    ///     The JSON string representing the clips' data.
    /// </param>
    /// <returns>
    ///     The parsed JObject representing the clips' data.
    /// </returns>
    private JObject ParseClips(string clipDataText) {
        OBSManager.Logger.Log("ParseClips method called", OBSManager.Logger.LogLevel.Info);

        try {
            return JObject.Parse(clipDataText);
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Error parsing clip data: {ex.Message}", OBSManager.Logger.LogLevel.Error);

            return new JObject();
        }
    }

    /// <summary>
    ///     Asynchronously plays the given clip by setting the OBS browser source to the clip's embed URL, showing the source,
    ///     waiting for the clip's duration plus a buffer, and then hiding the source.
    /// </summary>
    /// <param name="clip">
    ///     The clip data for the clip to be played.
    /// </param>
    /// <returns>
    ///     A task that represents the asynchronous operation.
    /// </returns>
    private async Task PlayClipAsync(ClipData clip) {
        OBSManager.Logger.Log("PlayClipAsync method called", OBSManager.Logger.LogLevel.Info);

        try {
            const string clipInfoText = "Clip Info";
            var embedUrl = GenerateClipEmbedUrl();
            var delay = CalculateDelay();

            if (delay == -1) {
                OBSManager.Logger.Log("Error calculating delay for clip playback", OBSManager.Logger.LogLevel.Error);

                return;
            }

            var gameName = await GetGameAsync(clip.GameId);

            _obsManager.ManageClipPlayerContent(clip, gameName, clipInfoText, embedUrl, delay);
            LastPlayedClip = clip;
            OBSManager.Logger.Log("Clip played successfully", OBSManager.Logger.LogLevel.Info);
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in PlayClipAsync: {ex.Message}", OBSManager.Logger.LogLevel.Error);
        }
    }

    /// <summary>
    ///     Plays a random clip from the provided list of clips.
    /// </summary>
    /// <param name="clips">
    ///     A list of clip data from which a random clip will be selected and played.
    /// </param>
    private void PlayRandomClip(List<ClipData> clips) {
        OBSManager.Logger.Log("PlayRandomClip method called", OBSManager.Logger.LogLevel.Info);

        try {
            var random = new Random();
            var clip = clips[random.Next(clips.Count)];

            CurrentClip = clip;
            _ = Task.Run(() => PlayClipAsync(clip));
            OBSManager.Logger.Log("Random clip played successfully", OBSManager.Logger.LogLevel.Info);
        } catch (Exception ex) {
            OBSManager.Logger.Log($"An error occurred while playing a random clip: {ex.Message}",
                                  OBSManager.Logger.LogLevel.Error);
        }
    }

    /// <summary>
    ///     Plays a specific clip by its URL. This method generates the Twitch Helix API URL from the provided clip URL, finds
    ///     the specific clip data, and then plays the clip using the OBS browser source.
    /// </summary>
    /// <param name="clipURL">
    ///     The URL of the clip to be played.
    /// </param>
    private void PlaySpecificClip(string clipURL) {
        OBSManager.Logger.Log("PlaySpecificClip method called", OBSManager.Logger.LogLevel.Info);

        try {
            var helixURL = GenerateHelixURL(clipURL);
            var clipTask = Task.Run(() => FindSpecificClipAsync(helixURL));
            var specificClip = clipTask.Result;

            CurrentClip = specificClip;

            _ = Task.Run(() => PlayClipAsync(specificClip));

            LastPlayedClip = specificClip;
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in PlaySpecificClip: {ex.Message}", OBSManager.Logger.LogLevel.Error);
        }
    }

    /// <summary>
    ///     Prepares a message by replacing placeholders with the corresponding user information.
    /// </summary>
    /// <param name="userInfo">
    ///     The user information to be used in the message.
    /// </param>
    /// <returns>
    ///     A string with the placeholders replaced by the user's information.
    /// </returns>
    private string PrepareMessage(TwitchUserInfoEx userInfo) {
        OBSManager.Logger.Log("PrepareMessage method called", OBSManager.Logger.LogLevel.Info);

        try {
            if (!CPH.TryGetArg("message", out string message)) {
                OBSManager.Logger.Log("Failed to retrieve 'message' argument.", OBSManager.Logger.LogLevel.Warn);
                message = "Default message";
            }

            var preparedMessage = message.Replace("%userName%", userInfo.UserName).Replace("%userGame%", userInfo.Game);

            OBSManager.Logger.Log($"Message prepared: {preparedMessage}", OBSManager.Logger.LogLevel.Info);

            return preparedMessage;
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in PrepareMessage: {ex.Message}", OBSManager.Logger.LogLevel.Error);

            return string.Empty;
        }
    }

    /// <summary>
    ///     Replays the last played clip by setting the OBS browser source to the clip's embed URL, showing the source, waiting
    ///     for the clip's duration plus a buffer, and then hiding the source.
    /// </summary>
    /// <returns>
    ///     True if the last played clip was successfully replayed; otherwise, false.
    /// </returns>
    private bool ReplayLastClip() {
        OBSManager.Logger.Log("ReplayLastClip method called", OBSManager.Logger.LogLevel.Info);

        try {
            if (LastPlayedClip == null) {
                OBSManager.Logger.Log("No last played clip to replay.", OBSManager.Logger.LogLevel.Warn);

                return false;
            }

            CurrentClip = LastPlayedClip;
            _ = Task.Run(() => PlayClipAsync(LastPlayedClip));

            return true;
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in ReplayLastClip: {ex.Message}", OBSManager.Logger.LogLevel.Error);

            return false;
        }
    }

    /// <summary>
    ///     Attempts to retrieve user information for a specified target user.
    /// </summary>
    /// <param name="targetUser">
    ///     The username of the target user.
    /// </param>
    /// <param name="userInfo">
    ///     When this method returns, contains the user information associated with the specified target user, if the user
    ///     exists and the information is available; otherwise, the default value for the type of the userInfo parameter. This
    ///     parameter is passed uninitialized.
    /// </param>
    /// <returns>
    ///     <c>
    ///         true
    ///     </c>
    ///     if the user information was successfully retrieved; otherwise,
    ///     <c>
    ///         false
    ///     </c>
    ///     .
    /// </returns>
    private bool TryGetUserInfo(string targetUser, out TwitchUserInfoEx userInfo) {
        OBSManager.Logger.Log("TryGetUserInfo method called", OBSManager.Logger.LogLevel.Info);

        try {
            userInfo = CPH.TwitchGetExtendedUserInfoByLogin(targetUser);

            return true;
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in TryGetUserInfo: {ex.Message}", OBSManager.Logger.LogLevel.Error);
            userInfo = new TwitchUserInfoEx();

            return false;
        }
    }

    /// <summary>
    ///     Attempts to parse the arguments required for executing the selected command.
    /// </summary>
    /// <param name="mainCommand">
    ///     The main command to be executed.
    /// </param>
    /// <param name="input0">
    ///     The first input argument for the command.
    /// </param>
    /// <param name="height">
    ///     The height parameter for the command.
    /// </param>
    /// <param name="width">
    ///     The width parameter for the command.
    /// </param>
    /// <returns>
    ///     <c>true</c> if all arguments were successfully parsed; otherwise, <c>false</c>.
    /// </returns>
    private bool TryParseArguments(out string mainCommand, out string input0, out int height, out int width) {
        OBSManager.Logger.Log("TryParseArguments method called", OBSManager.Logger.LogLevel.Info);

        try {
            if (!TryGetArgumentValues(out mainCommand, out input0, out var heightStr, out var widthStr)) {
                OBSManager.Logger.Log("Failed to get argument values", OBSManager.Logger.LogLevel.Error);
                ResetOutputParameters(out mainCommand, out input0, out height, out width);

                return false;
            }

            if (!int.TryParse(heightStr, out height) || height <= 0) {
                OBSManager.Logger.Log("Invalid height value: must be a positive integer",
                                      OBSManager.Logger.LogLevel.Error);
                ResetOutputParameters(out mainCommand, out input0, out height, out width);

                return false;
            }

            if (int.TryParse(widthStr, out width) && width > 0) return true;

            OBSManager.Logger.Log("Invalid width value: must be a positive integer", OBSManager.Logger.LogLevel.Error);
            ResetOutputParameters(out mainCommand, out input0, out height, out width);

            return false;
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in TryParseArguments: {ex.Message}", OBSManager.Logger.LogLevel.Error);
            ResetOutputParameters(out mainCommand, out input0, out height, out width);

            return false;
        }
    }

    /// <summary>
    ///     Resets the output parameters to their default values.
    /// </summary>
    /// <param name="mainCommand">
    ///     The main command string to be reset.
    /// </param>
    /// <param name="input0">
    ///     The input string to be reset.
    /// </param>
    /// <param name="height">
    ///     The height value to be reset.
    /// </param>
    /// <param name="width">
    ///     The width value to be reset.
    /// </param>
    private void ResetOutputParameters(out string mainCommand, out string input0, out int height, out int width) {
        mainCommand = string.Empty;
        input0 = string.Empty;
        height = 0;
        width = 0;
    }

    /// <summary>
    ///     Attempts to retrieve argument values from the provided source and outputs them as strings.
    /// </summary>
    /// <param name="mainCommand">
    ///     The main command argument to retrieve and output.
    /// </param>
    /// <param name="input0">
    ///     The input argument to retrieve and output.
    /// </param>
    /// <param name="height">
    ///     The height argument to retrieve and output as a string.
    /// </param>
    /// <param name="width">
    ///     The width argument to retrieve and output as a string.
    /// </param>
    /// <returns>
    ///     <c>true</c> if all argument values were successfully retrieved; otherwise, <c>FALSE</c>.
    /// </returns>
    private bool TryGetArgumentValues(out string mainCommand, out string input0, out string height, out string width) {
        ResetOutputParameters(out mainCommand, out input0, out var heightValue, out var widthValue);

        // The parameters height and width here need to be of type string
        height = heightValue.ToString();
        width = widthValue.ToString();

        var commandExists = CPH.TryGetArg("command", out mainCommand);
        var input0Exists = CPH.TryGetArg("input0", out input0);
        var heightExists = CPH.TryGetArg("height", out height);
        var widthExists = CPH.TryGetArg("width", out width);

        OBSManager.Logger
                  .Log($"commandExists: {commandExists}, input0Exists: {input0Exists}, heightExists: {heightExists}, widthExists: {widthExists}",
                       OBSManager.Logger.LogLevel.Info);

        return commandExists && input0Exists && heightExists && widthExists;
    }
}

/// <summary>
///     Provides methods to manage the clip player in OBS.
/// </summary>
public interface ICPHStore {
    bool IsInitialized { get; }
    void Init(IInlineInvokeProxy cph);
    IInlineInvokeProxy GetCPH();
}

/// <summary>
///     Provides methods to initialize and retrieve the stored CPH instance for inline invocation.
/// </summary>
/// a
public static class CPHStore {
    private static IInlineInvokeProxy _cph;
    private static readonly object Lock = new();
    public static bool IsInitialized => _cph != null;
    public static bool IsInLogging { get; set; }

    /// <summary>
    ///     Initializes the CPHStore with an instance of IInlineInvokeProxy.
    /// </summary>
    /// <param name="cph">
    ///     The instance of IInlineInvokeProxy to store.
    /// </param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown if the provided IInlineInvokeProxy instance is null.
    /// </exception>
    public static void Init(IInlineInvokeProxy cph) {
        try {
            if (cph == null) throw new ArgumentNullException(nameof(cph), "CPH instance must not be null.");

            lock (Lock) {
                if (_cph != null) return;

                _cph = cph;
                OBSManager.Logger.Log("CPH instance initialized successfully.", OBSManager.Logger.LogLevel.Info);
            }
        } catch (ArgumentNullException ex) {
            OBSManager.Logger.Log($"Exception in CPHStore.Init: {ex.Message}", OBSManager.Logger.LogLevel.Error);
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Unexpected error in CPHStore.Init: {ex.Message}", OBSManager.Logger.LogLevel.Error);
        }
    }

    /// <summary>
    ///     Retrieves the CPH instance stored in the CPHStore.
    /// </summary>
    /// <returns>
    ///     The
    ///     <see cref="Streamer.bot.Plugin.Interface.IInlineInvokeProxy" />
    ///     stored in the CPHStore.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if the
    ///     <see cref="IInlineInvokeProxy" />
    ///     has not been initialized.
    /// </exception>
    public static IInlineInvokeProxy GetCPH() {
        try {
            lock (Lock) {
                if (_cph == null) throw new InvalidOperationException("CPH instance has not been initialized.");

                return _cph;
            }
        } catch (InvalidOperationException ex) {
            OBSManager.Logger.Log($"Exception in GetCPH: {ex.Message}", OBSManager.Logger.LogLevel.Error);

            return null;
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Unexpected error in GetCPH: {ex.Message}", OBSManager.Logger.LogLevel.Error);

            return null;
        }
    }
}

/// <summary>
///     Wrapper class for the CPHStore to implement the ICPHStore interface.
/// </summary>
public class CPHStoreWrapper : ICPHStore {
    /// <summary>
    ///     Initializes the CPHStore with an instance of IInlineInvokeProxy.
    /// </summary>
    /// <param name="cph">
    ///     The instance of IInlineInvokeProxy to store.
    /// </param>
    public void Init(IInlineInvokeProxy cph) {
        CPHStore.Init(cph);
    }

    /// <summary>
    ///     Retrieves the CPH instance stored in the CPHStore.
    /// </summary>
    /// <returns>
    ///     The IInlineInvokeProxy stored in the CPHStore.
    /// </returns>
    public IInlineInvokeProxy GetCPH() {
        return CPHStore.GetCPH();
    }

    /// <summary>
    ///     Gets a value indicating whether the CPHStore is initialized.
    /// </summary>
    public bool IsInitialized => CPHStore.IsInitialized;
}

/// <summary>
///     Defines methods for managing OBS (Open Broadcaster Software) operations.
/// </summary>
public interface IOBSManager {
    /// <summary>
    ///     Initializes the OBS manager.
    /// </summary>
    void Init();

    /// <summary>
    ///     Manages the content of the clip player by setting metadata, creating a webpage, starting the server, and playing
    ///     the clip.
    /// </summary>
    /// <param name="clip">
    ///     The clip data containing metadata to be set.
    /// </param>
    /// <param name="gameName">
    ///     The name of the game associated with the clip.
    /// </param>
    /// <param name="clipInfoText">
    ///     The text information about the clip.
    /// </param>
    /// <param name="embedUrl">
    ///     The URL of the clip to be embedded.
    /// </param>
    /// <param name="delay">
    ///     The delay in milliseconds before playing the clip.
    /// </param>
    void ManageClipPlayerContent(ClipData clip, string gameName, string clipInfoText, string embedUrl, int delay);

    /// <summary>
    ///     Shows a source in OBS based on the provided request.
    /// </summary>
    /// <typeparam name="T">
    ///     The type of the request object.
    /// </typeparam>
    /// <param name="request">
    ///     The request object containing scene and source information.
    /// </param>
    /// <param name="response">
    ///     The response from OBS.
    /// </param>
    /// <returns>
    ///     True if the source was shown successfully; otherwise, false.
    /// </returns>
    bool OBSShow<T>(T request, out string response);

    /// <summary>
    ///     Hides a source in OBS based on the provided request.
    /// </summary>
    /// <typeparam name="T">
    ///     The type of the request object.
    /// </typeparam>
    /// <param name="request">
    ///     The request object containing scene and source information.
    /// </param>
    /// <param name="response">
    ///     The response from OBS.
    /// </param>
    /// <returns>
    ///     True if the source was hidden successfully; otherwise, false.
    /// </returns>
    bool OBSHide<T>(T request, out string response);
}

/// <summary>
///     Provides functionality to manage OBS (Open Broadcaster Software) operations, specifically tailored for
///     interoperability with Streamer.bot to implement a clip player.
/// </summary>
/// <remarks>
///     This class includes methods to initialize OBS, manage scenes and sources, and handle clip playback.
/// </remarks>
public class OBSManager : IOBSManager {
    private const string OBSNotConnected = "OBS is not connected";

    /// <summary>
    ///     The name of the player source in OBS.
    /// </summary>
    private const string PlayerSourceName = "Player";

    /// <summary>
    ///     The name of the scene used in OBS.
    /// </summary>
    private const string SceneName = "Cliparino";

    /// <summary>
    ///     The name of the clip information source in OBS.
    /// </summary>
    public const string ClipInfo = "Clip Info";

    /// <summary>
    ///     The instance of
    ///     <see cref="IInlineInvokeProxy" />
    ///     used for OBS operations.
    /// </summary>
    private readonly IInlineInvokeProxy _cph;

    /// <summary>
    ///     Initializes a new instance of the
    ///     <see cref="OBSManager" />
    ///     class.
    /// </summary>
    /// <param name="cph">
    ///     The instance of
    ///     <see cref="IInlineInvokeProxy" />
    ///     to be used for OBS operations.
    /// </param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown if the provided
    ///     <paramref name="cph" />
    ///     is null.
    /// </exception>
    public OBSManager(IInlineInvokeProxy cph) {
        try {
            _cph = cph ?? throw new ArgumentNullException(nameof(cph), "CPH instance must not be null.;");
        } catch (ArgumentNullException ex) {
            Logger.Log($"Exception in OBSManager constructor: {ex.Message}", Logger.LogLevel.Error);
        } catch (Exception ex) {
            Logger.Log($"Unexpected error in OBSManager constructor: {ex.Message}", Logger.LogLevel.Error);
        }
    }

    /// <summary>
    ///     Gets or sets the video dimensions (height and width) for the clip player source.
    /// </summary>
    public static (int height, int width) VideoDimensions { get; set; }

    /// <summary>
    ///     Gets or sets the name of the channel.
    /// </summary>
    private static string ChannelName { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the name of the clip curator.
    /// </summary>
    private static string ClipCurator { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the title of the clip.
    /// </summary>
    private static string ClipTitle { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the name of the game associated with the clip.
    /// </summary>
    private static string GameName { get; } = string.Empty;

    /// <summary>
    ///     Initializes the OBSManager.
    /// </summary>
    /// <remarks>
    ///     This method sets up the OBS scene and player source. It should be called before any other OBS operations.
    /// </remarks>
    public void Init() {
        LogInfo("Init method called");

        try {
            ManageOBS(SceneName, PlayerSourceName, true);
        } catch (Exception ex) {
            LogError($"Exception in Init method: {ex.Message}");
        }
    }

    /// <summary>
    ///     Manages the content of the clip player by setting metadata, creating a webpage, starting the server, and playing
    ///     the clip.
    /// </summary>
    /// <param name="clip">
    ///     The clip data containing metadata to be set.
    /// </param>
    /// <param name="gameName">
    ///     The name of the game associated with the clip.
    /// </param>
    /// <param name="clipInfoText">
    ///     The text information about the clip.
    /// </param>
    /// <param name="embedUrl">
    ///     The URL of the clip to be embedded.
    /// </param>
    /// <param name="delay">
    ///     The delay in milliseconds before playing the clip.
    /// </param>
    /// <remarks>
    ///     This method sets the clip metadata, creates a webpage, starts the HTTP server, and plays the clip. If an exception
    ///     occurs, it logs the error and resets the clip player.
    /// </remarks>
    public void ManageClipPlayerContent(ClipData clip,
                                        string gameName,
                                        string clipInfoText,
                                        string embedUrl,
                                        int delay) {
        LogInfo("ManageClipPlayerContent method called");
        SetClipMetadata(clip);

        try {
            if (!CreateWebPageAndStartServer()) return;

            PlayClip(embedUrl, clipInfoText, delay);
            LogInfo("Website level: RealUltimatePower.com! We're up!");
        } catch (Exception ex) {
            HandleClipPlayerException(ex, clipInfoText);
        }
    }

    /// <summary>
    ///     Shows a source in OBS based on the provided request.
    /// </summary>
    /// <typeparam name="T">
    ///     The type of the request object.
    /// </typeparam>
    /// <param name="request">
    ///     The request object containing scene and source information.
    /// </param>
    /// <param name="response">
    ///     The response from OBS.
    /// </param>
    /// <returns>
    ///     True if the source was shown successfully; otherwise, false.
    /// </returns>
    /// <remarks>
    ///     This method checks if the request is a dictionary containing scene and source information, and then shows the
    ///     source in OBS.
    /// </remarks>
    public bool OBSShow<T>(T request, out string response) {
        Logger.Log("OBSShow method called", Logger.LogLevel.Info);
        response = string.Empty;

        if (request is IDictionary<string, object> dynamicRequest
            && dynamicRequest.TryGetValue("scene", out var scene)
            && dynamicRequest.TryGetValue("source", out var source))
            return DataBroker.OBSShow(scene as string, source as string, out response);

        Logger.Log($"OBS responded with {response}",
                   string.IsNullOrEmpty(response) ? Logger.LogLevel.Error : Logger.LogLevel.Info);

        return false;
    }

    /// <summary>
    ///     Hides a source in OBS based on the provided request.
    /// </summary>
    /// <typeparam name="T">
    ///     The type of the request object.
    /// </typeparam>
    /// <param name="request">
    ///     The request object containing scene and source information.
    /// </param>
    /// <param name="response">
    ///     The response from OBS.
    /// </param>
    /// <returns>
    ///     True if the source was hidden successfully; otherwise, false.
    /// </returns>
    /// <remarks>
    ///     This method checks if the request is a dictionary containing scene and source information, and then hides the
    ///     source in OBS.
    /// </remarks>
    public bool OBSHide<T>(T request, out string response) {
        Logger.Log("OBSHide method called", Logger.LogLevel.Info);
        response = string.Empty;

        if (request is IDictionary<string, object> dynamicRequest
            && dynamicRequest.TryGetValue("scene", out var scene)
            && dynamicRequest.TryGetValue("source", out var source))
            return DataBroker.OBSHide(scene as string, source as string, out response);

        Logger.Log($"OBS responded with {response}",
                   string.IsNullOrEmpty(response) ? Logger.LogLevel.Error : Logger.LogLevel.Info);

        return false;
    }

    /// <summary>
    ///     Handles exceptions that occur during the clip player operation.
    /// </summary>
    /// <param name="ex">
    ///     The exception that was thrown.
    /// </param>
    /// <param name="clipInfoText">
    ///     Information about the clip being played when the exception occurred.
    /// </param>
    /// <remarks>
    ///     This method also resets the clip player if an exception occurs.
    /// </remarks>
    private void HandleClipPlayerException(Exception ex, string clipInfoText) {
        LogError($"Exception in ManageClipPlayerContent: {ex.Message}");
        ResetClipPlayer(clipInfoText);
    }

    /// <summary>
    ///     Gets the video dimensions.
    /// </summary>
    /// <returns>
    ///     A tuple containing the height and width of the video. If an exception occurs, it logs the error and returns (0, 0).
    /// </returns>
    /// <remarks>
    ///     This method retrieves the video dimensions from the
    ///     <see cref="VideoDimensions" />
    ///     property.
    /// </remarks>
    public static (int height, int width) GetVideoDimensions() {
        LogInfo("GetVideoDimensions method called");

        try {
            return VideoDimensions;
        } catch (Exception ex) {
            LogError($"Exception in GetVideoDimensions: {ex.Message}");

            return (0, 0);
        }
    }

    /// <summary>
    ///     Manages the OBS scene and source.
    /// </summary>
    /// <param name="sceneName">
    ///     The name of the scene to manage.
    /// </param>
    /// <param name="sourceName">
    ///     The name of the source to manage (optional).
    /// </param>
    /// <param name="visible">
    ///     The visibility state of the source (optional).
    /// </param>
    /// <remarks>
    ///     This method manages the OBS scene and source by checking if the scene exists and creating it if necessary. It also
    ///     manages the source within the scene based on the provided parameters.
    /// </remarks>
    private void ManageOBS(string sceneName, string sourceName = null, bool? visible = null) {
        LogInfo("ManageOBS method called");

        try {
            if (!IsOBSConnected()) return;

            if (EnsureSceneExists(sceneName)) return;

            HandleSourceOrSceneSwitch(sceneName, sourceName, visible);
        } catch (Exception ex) {
            LogError($"Exception in ManageOBS: {ex.Message}");
        }
    }

    /// <summary>
    ///     Handles the switching of a source or scene within OBS.
    /// </summary>
    /// <param name="sceneName">
    ///     The name of the scene to switch to or manage a source within.
    /// </param>
    /// <param name="sourceName">
    ///     The name of the source within the scene to manage. If null, the scene will be switched.
    /// </param>
    /// <param name="visible">
    ///     The visibility state to set for the source. If null, no visibility change is made.
    /// </param>
    private void HandleSourceOrSceneSwitch(string sceneName, string sourceName, bool? visible) {
        if (sourceName != null)
            ManageSourceInScene(sceneName, sourceName, visible);
        else
            SetScene(sceneName);
    }

    /// <summary>
    ///     Ensures that a scene with the specified name exists in OBS. If the scene does not exist, it is created.
    /// </summary>
    /// <param name="sceneName">
    ///     The name of the scene to check or create.
    /// </param>
    /// <returns>
    ///     Returns
    ///     <c>
    ///         true
    ///     </c>
    ///     if the scene already exists;
    ///     <c>
    ///         false
    ///     </c>
    ///     if the scene is created.
    /// </returns>
    private static bool EnsureSceneExists(string sceneName) {
        if (SceneExists(sceneName)) return false;

        CreateScene(sceneName);
        LogInfo($"Scene '{sceneName}' created");

        return false;
    }

    /// <summary>
    ///     Manages the scene or player source within the specified scene.
    /// </summary>
    /// <param name="sceneName">
    ///     The name of the scene.
    /// </param>
    /// <param name="sourceName">
    ///     The name of the source.
    /// </param>
    /// <param name="visible">
    ///     The visibility state of the source (optional).
    /// </param>
    /// <remarks>
    ///     This method checks if the source exists within the specified scene and creates it if necessary. It also sets the
    ///     visibility of the source based on the provided parameter.
    /// </remarks>
    private void ManageSourceInScene(string sceneName, string sourceName, bool? visible) {
        if (!SourceExistsInScene(sceneName, sourceName)) {
            LogInfo($"Source '{sourceName}' does not exist in scene '{sceneName}', creating.");

            // Check if the source is a Browser source or Scene source
            if (IsBrowserSource(sourceName)) {
                ManageBrowserSourceInScene();
            } else if (IsSceneSource(sourceName)) {
                ManageCliparinoSceneInScene(sceneName);
            } else {
                throw new InvalidOperationException("Unknown source type");
            }
        }

        if (visible.HasValue) {
            SetSourceVisibility(sceneName, sourceName, visible.Value);
        }
    }

    /// <summary>
    ///     Manages the player source in the specified scene.
    /// </summary>
    /// <param name="sceneName">
    ///     The name of the scene.
    /// </param>
    /// <param name="sourceName">
    ///     The name of the source.
    /// </param>
    /// <remarks>
    ///     This method creates a browser source in the specified scene with the given settings. It also sets the audio monitor
    ///     type and volume for the source.
    /// </remarks>
    private static void ManagePlayerSourceInScene(string sceneName, string sourceName) {
        LogInfo("ManagePlayerSourceInScene method called");

        try {
            var url = GenerateClipUrl();
            var inputSettings = CreateInputSettings(url);
            var createInputData = CreateInputData(sceneName, sourceName, inputSettings);
            var setInputAudioMonitorTypeData = CreateInputAudioMonitorTypeData(sourceName);
            var setInputVolumeData = CreateInputVolumeData(sourceName);

            OBSSendRaw(new OBSRequest(OBSRequestRawType.Inputs.CreateInput, createInputData), out _);
            OBSSendRaw(new OBSRequest(OBSRequestRawType.Inputs.SetInputAudioMonitorType, setInputAudioMonitorTypeData),
                       out _);
            OBSSendRaw(new OBSRequest(OBSRequestRawType.Inputs.SetInputVolume, setInputVolumeData), out _);

            LogInfo($"Browser source '{sourceName}' created in '{sceneName}' scene.");
        } catch (Exception ex) {
            LogError($"Exception in ManagePlayerSourceInScene: {ex.Message}");
        }
    }

    private static object CreateInputVolumeData(string sourceName) {
        return new { inputName = sourceName, inputVolumeDb = -6 };
    }

    /// <summary>
    ///     Creates the data required to set the audio monitor type for a specified input source in OBS.
    /// </summary>
    /// <param name="sourceName">
    ///     The name of the input source for which the audio monitor type is to be set.
    /// </param>
    /// <returns>
    ///     An object containing the input name and monitor type settings for the specified source.
    /// </returns>
    private static object CreateInputAudioMonitorTypeData(string sourceName) {
        return new { inputName = sourceName, monitorType = "OBS_MONITORING_TYPE_MONITOR_AND_OUTPUT" };
    }

    /// <summary>
    ///     Creates a formatted input data object for the OBS (Open Broadcaster Software) scene with the specified parameters.
    /// </summary>
    /// <param name="sceneName">
    ///     The name of the OBS scene where the input is to be added.
    /// </param>
    /// <param name="sourceName">
    ///     The name of the source to be created or modified within the scene.
    /// </param>
    /// <param name="inputSettings">
    ///     Settings specific to the input, such as the type of source and its properties.
    /// </param>
    /// <returns>
    ///     A formatted input data object containing all the necessary attributes to create or modify an OBS source.
    /// </returns>
    private static object CreateInputData(string sceneName, string sourceName, object inputSettings) {
        return new {
            sceneName,
            inputName = sourceName,
            inputKind = "browser_source",
            inputSettings,
            sceneItemEnabled = true,
            overlay = false
        };
    }

    /// <summary>
    ///     Creates the settings object for the input for a given URL.
    /// </summary>
    /// <param name="url">
    ///     The URL to be set in the input settings.
    /// </param>
    /// <returns>
    ///     An object containing the configuration settings for the input.
    /// </returns>
    private static object CreateInputSettings(string url) {
        return new {
            css = WebpageCreator.CSSText,
            VideoDimensions.height,
            reroute_audio = true,
            restart_when_active = true,
            url,
            VideoDimensions.width
        };
    }

    /// <summary>
    ///     Generates a URL for embedding a Twitch clip.
    /// </summary>
    /// <returns>
    ///     A string representing the complete URL to embed a specific Twitch clip with predefined settings.
    /// </returns>
    private static string GenerateClipUrl() {
        const string baseURL = "https://clips.twitch.tv/embed";
        const string urlQuery =
            "clip=CleanShyOrangeNinjaGrumpy-mwaI9sX_sWaq05Zi&parent=localhost&autoplay=true&mute=false";

        return $"{baseURL}?{urlQuery}";
    }

    /// <summary>
    ///     Creates a webpage and starts the HTTP server.
    /// </summary>
    /// <returns>
    ///     True if the webpage was created and the server started successfully; otherwise, false.
    /// </returns>
    /// <remarks>
    ///     This method creates a webpage using the provided channel name, game name, and clip curator. It also starts the HTTP
    ///     server.
    /// </remarks>
    private static bool CreateWebPageAndStartServer() {
        var isPageCreated = WebpageCreator.CreateWebPage(ChannelName, GameName, ClipCurator, ClipTitle);
        var isHTTPServerUp = StartHTTPServer();

        if (!isPageCreated) {
            LogWarn($"Failed to create web page with ChannelName: {ChannelName}, GameName: {GameName}, ClipCurator: {ClipCurator}");

            return false;
        }

        if (isHTTPServerUp) return false;

        LogWarn("Failed to start HTTP server.");

        return false;
    }

    /// <summary>
    ///     Starts the HTTP server.
    /// </summary>
    /// <returns>
    ///     <c>
    ///         true
    ///     </c>
    ///     if the server started successfully; otherwise,
    ///     <c>
    ///         false
    ///     </c>
    ///     .
    /// </returns>
    private static bool StartHTTPServer() {
        try {
            var httpServer = SimpleHttpServer.Instance.Value;
            var result = httpServer.Init();

            if (!result) LogWarn("Failed to start HTTP server.");

            return result;
        } catch (Exception ex) {
            LogError($"Exception in OBSManager.StartHTTPServer: {ex.Message}");

            return false;
        }
    }

    /// <summary>
    ///     Plays a clip by setting the OBS browser source to the clip's embed URL, showing the source, waiting for the clip's
    ///     duration plus a buffer, and then hiding the source.
    /// </summary>
    /// <param name="embedUrl">
    ///     The URL of the clip to be embedded.
    /// </param>
    /// <param name="clipInfoText">
    ///     The text information about the clip.
    /// </param>
    /// <param name="delay">
    ///     The delay in milliseconds before playing the clip.
    /// </param>
    private void PlayClip(string embedUrl, string clipInfoText, int delay) {
        LogInfo("PlayClip method called");

        try {
            SetBrowserSource("about:blank");
            Wait(500);
            SetBrowserSource(embedUrl);
            ManageSceneSource(true);
            ShowSource(PlayerSourceName);
            ShowSource(clipInfoText);
            Wait(delay);
        } catch (Exception ex) {
            LogError($"Exception in PlayClip: {ex.Message}");
        }
    }

    /// <summary>
    ///     Checks if OBS is connected.
    /// </summary>
    /// <returns>
    ///     True if OBS is connected; otherwise, false.
    /// </returns>
    private bool IsOBSConnected() {
        if (_cph.ObsIsConnected()) return true;

        LogError(OBSNotConnected);

        return false;
    }

    /// <summary>
    ///     Sets the current scene in OBS.
    /// </summary>
    /// <param name="sceneName">
    ///     The name of the scene to set.
    /// </param>
    private void SetScene(string sceneName) {
        _cph.ObsSetScene(sceneName);
        LogInfo($"Scene set to '{sceneName}'");
    }

    /// <summary>
    ///     Sets the visibility of a source in a specific scene in OBS.
    /// </summary>
    /// <param name="sceneName">
    ///     The name of the scene containing the source.
    /// </param>
    /// <param name="sourceName">
    ///     The name of the source to set visibility for.
    /// </param>
    /// <param name="visible">
    ///     The visibility state to set for the source.
    /// </param>
    private void SetSourceVisibility(string sceneName, string sourceName, bool visible) {
        _cph.ObsSetSourceVisibility(sceneName, sourceName, visible);
        LogInfo($"Source '{sourceName}' visibility set to '{visible}' in scene '{sceneName}'");
    }

    /// <summary>
    ///     Sets the URL of the browser source in OBS.
    /// </summary>
    /// <param name="url">
    ///     The URL to set for the browser source.
    /// </param>
    private void SetBrowserSource(string url) {
        _cph.ObsSetBrowserSource(SceneName, PlayerSourceName, url);
    }

    /// <summary>
    ///     Shows a source in a specific scene in OBS.
    /// </summary>
    /// <param name="sourceName">
    ///     The name of the source to show.
    /// </param>
    private void ShowSource(string sourceName) {
        _cph.ObsShowSource(SceneName, sourceName);
    }

    /// <summary>
    ///     Waits for a specified amount of time.
    /// </summary>
    /// <param name="milliseconds">
    ///     The amount of time to wait, in milliseconds.
    /// </param>
    private void Wait(int milliseconds) {
        _cph.Wait(milliseconds);
    }

    // This method simulates checking if a source is a Browser source
    private bool IsBrowserSource(string sourceName) {
        // Your custom logic to determine if a source is a Browser source
        return sourceName.Contains("Player");
    }

// This method simulates checking if a source is a Scene source
    private bool IsSceneSource(string sourceName) {
        // Your custom logic to determine if a source is a Scene source
        return sourceName.Contains("Cliparino");
    }

    /// <summary>
    ///     Manages the existence and identity of a browser source within a scene in OBS.
    /// </summary>
    /// <remarks>
    ///     This method ensures that the browser source is properly added and configured within the specified OBS scene. It is primarily used when the source to be managed is of type Browser.
    /// </remarks>
    private void ManageBrowserSourceInScene() {
        var playerSource = PlayerSource.Create();

        if (PlayerSource.SourceUUIDIsMatch()) {
            return;
        }

        LogInfo("Player source is not identical to the original, updating UUID");
        playerSource.UpdateUUID();
    }

    /// <summary>
    ///     Manages the existence and identity of the Cliparino scene within another OBS scene.
    /// </summary>
    /// <param name="sceneName">
    ///     The name of the OBS scene within which the Cliparino scene is to be managed.
    /// </param>
    private void ManageCliparinoSceneInScene(string sceneName) {
        var cliparinoScene = CliparinoScene.Create();

        if (CliparinoScene.SceneUUIDIsMatch()) {
            if (cliparinoScene.Parents.Contains(sceneName)) {
                return;
            }
        }

        cliparinoScene.Parents.Add(sceneName);
    }

    /// <summary>
    ///     Logs an informational message.
    /// </summary>
    /// <param name="message">
    ///     The message to log.
    /// </param>
    private static void LogInfo(string message) {
        Logger.Log(message, Logger.LogLevel.Info);
    }

    /// <summary>
    ///     Logs a warning message.
    /// </summary>
    /// <param name="message">
    ///     The message to log.
    /// </param>
    private static void LogWarn(string message) {
        Logger.Log(message, Logger.LogLevel.Warn);
    }

    /// <summary>
    ///     Logs an error message.
    /// </summary>
    /// <param name="message">
    ///     The message to log.
    /// </param>
    private static void LogError(string message) {
        Logger.Log(message, Logger.LogLevel.Error);
    }

    /// <summary>
    ///     Manages the visibility of the scene source in OBS.
    /// </summary>
    /// <param name="isSetup">
    ///     Indicates whether to set up or tear down the scene source.
    /// </param>
    private void ManageSceneSource(bool isSetup) {
        var currentScene = _cph.ObsGetCurrentScene();

        if (isSetup) {
            if (!SourceExistsInScene(currentScene, SceneName)) {
                _cph.ObsSendRaw("CreateSceneItem",
                                JsonConvert.SerializeObject(new {
                                    sceneName = currentScene, sourceName = SceneName, sceneItemEnabled = true
                                }));
                Logger.Log($"Scene '{SceneName}' added to scene '{currentScene}'", Logger.LogLevel.Info);
            }

            if (!_cph.ObsIsSourceVisible(currentScene, SceneName)) {
                _cph.ObsShowSource(currentScene, SceneName);
                Logger.Log($"Scene '{SceneName}' made visible in scene '{currentScene}'", Logger.LogLevel.Info);
            }
        } else {
            _cph.ObsHideSource(currentScene, SceneName);
            Logger.Log($"Scene '{SceneName}' hidden in scene '{currentScene}'", Logger.LogLevel.Info);
        }
    }

    /// <summary>
    ///     Resets the clip player by hiding the player source and setting the browser source to blank.
    /// </summary>
    /// <param name="clipInfoText">
    ///     The text information about the clip.
    /// </param>
    private void ResetClipPlayer(string clipInfoText) {
        Logger.Log("ResetClipPlayer method called", Logger.LogLevel.Info);

        try {
            _cph.ObsHideSource(SceneName, PlayerSourceName);
            _cph.ObsHideSource(SceneName, clipInfoText);
            _cph.ObsSetBrowserSource(SceneName, PlayerSourceName, "about:blank");
        } catch (Exception ex) {
            Logger.Log($"Exception in ResetClipPlayer: {ex.Message}", Logger.LogLevel.Error);
        }
    }

    /// <summary>
    ///     Checks if a scene with the specified name exists in OBS.
    /// </summary>
    /// <param name="sceneName">
    ///     The name of the scene to check for existence.
    /// </param>
    /// <returns>
    ///     True if the scene exists; otherwise, false.
    /// </returns>
    /// <remarks>
    ///     This method sends a request to OBS to retrieve the list of scenes and checks if the specified scene name is
    ///     present.
    /// </remarks>
    private static bool SceneExists(string sceneName) {
        Logger.Log("SceneExists method called", Logger.LogLevel.Info);

        try {
            _ = OBSSendRaw(new OBSRequest(OBSRequestRawType.Scenes.GetSceneList, "{}"), out var sceneResponse);
            var jsonResponse = JObject.Parse(sceneResponse);
            var scenes = jsonResponse["scenes"] as JArray;
            var sceneExists = scenes != null
                              && scenes.Any(scene => string.Equals((string)scene["sceneName"] ?? string.Empty,
                                                                   sceneName,
                                                                   StringComparison.Ordinal));

            Logger.Log($"Scene '{sceneName}' exists: {sceneExists}", Logger.LogLevel.Info);

            return sceneExists;
        } catch (Exception ex) {
            Logger.Log($"Exception in SceneExists: {ex.Message}", Logger.LogLevel.Error);

            return false;
        }
    }

    /// <summary>
    ///     Sets the metadata for the current clip.
    /// </summary>
    /// <param name="clip">
    ///     The clip data containing metadata to be set.
    /// </param>
    /// <remarks>
    ///     This method sets the broadcaster name, clip title, and clip curator name based on the provided clip data.
    /// </remarks>
    private static void SetClipMetadata(ClipData clip) {
        Logger.Log("SetClipMetadata method called", Logger.LogLevel.Info);

        try {
            ChannelName = clip.BroadcasterName;
            ClipTitle = clip.Title;
            ClipCurator = clip.CreatorName;
        } catch (Exception ex) {
            Logger.Log($"Exception in SetClipMetadata: {ex.Message}", Logger.LogLevel.Error);
        }
    }

    private static bool SourceExistsInScene(string sceneName, string sourceName) {
        Logger.Log($"SourceExistsInScene method called for scene '{sceneName}' and source '{sourceName}'",
                   Logger.LogLevel.Info);

        try {
            _ = OBSSendRaw(new OBSRequest(OBSRequestRawType.SceneItems.GetSceneItemList,
                                          JsonConvert.SerializeObject(new { sceneName })),
                           out var sceneItemResponse);
            var jsonResponse = JObject.Parse(sceneItemResponse);
            var sceneItems = jsonResponse["sceneItems"] as JArray;
            var sourceExists = sceneItems != null
                               && sceneItems.Any(item => string.Equals((string)item["sourceName"] ?? string.Empty,
                                                                       sourceName,
                                                                       StringComparison.Ordinal));

            Logger.Log($"Source '{sourceName}' exists in scene '{sceneName}': {sourceExists}", Logger.LogLevel.Info);

            return sourceExists;
        } catch (Exception ex) {
            Logger.Log($"Exception in SourceExistsInScene: {ex.Message}", Logger.LogLevel.Error);

            return false;
        }
    }

    /// <summary>
    ///     Sends a raw request to OBS and logs the response.
    /// </summary>
    /// <param name="request">
    ///     The OBS request to be sent.
    /// </param>
    /// <param name="response">
    ///     The response from OBS.
    /// </param>
    /// <returns>
    ///     True if the request was successful; otherwise, false.
    /// </returns>
    /// <remarks>
    ///     This method logs the response from OBS and handles any exceptions that occur during the request.
    /// </remarks>
    private static bool OBSSendRaw(OBSRequest request, out string response) {
        Logger.Log("OBSSendRaw method called", Logger.LogLevel.Info);
        response = string.Empty;

        try {
            var (requestType, jRequest) = TransformRequestParameters(request);
            var success = DataBroker.OBSSendRaw(requestType as string, jRequest, out response);

            Logger.Log($"Response from OBS: {response}", Logger.LogLevel.Info);

            return success;
        } catch (Exception ex) {
            Logger.Log($"Exception in OBSManager.OBSSendRaw {ex}", Logger.LogLevel.Error);
            Logger.Log($"Response from OBS: {response}", Logger.LogLevel.Info);

            return false;
        }
    }

    private static (object requestType, string jRequest) TransformRequestParameters(OBSRequest request) {
        var requestType = request.RequestType;
        var jRequest = JsonConvert.SerializeObject(request.RequestData);

        return (requestType, jRequest);
    }

    /// <summary>
    ///     Creates a new scene in OBS with the specified name.
    /// </summary>
    /// <param name="sceneName">
    ///     The name of the scene to create.
    /// </param>
    /// <remarks>
    ///     This method logs the creation process and sends a request to OBS to create the scene.
    /// </remarks>
    private static void CreateScene(string sceneName) {
        Logger.Log("CreateScene method called", Logger.LogLevel.Info);

        try {
            Logger.Log($"Scene '{sceneName}' not found. Creating it...", Logger.LogLevel.Info);
            OBSSendRaw(new OBSRequest(OBSRequestRawType.Scenes.CreateScene, $"{{\"sceneName\":\"{sceneName}\"}}"),
                       out _);
        } catch (Exception ex) {
            Logger.Log($"Exception in CreateScene: '{sceneName}': {ex.Message}", Logger.LogLevel.Error);
        }
    }

    /// <summary>
    ///     Represents raw types of OBS requests.
    /// </summary>
    /// <remarks>
    ///     These enums correspond to the raw request types in the OBS WebSocket API provided by Streamer.bot. The
    ///     documentation for the API can be found within the OBS Raw Generator at https://obs-raw.streamer.bot/.
    /// </remarks>
    private static class OBSRequestRawType {
        /// <summary>
        ///     Enum for input-related raw requests.
        /// </summary>
        public enum Inputs {
            /// <summary>
            ///     Request to set settings of an input. The request data takes the form of:
            ///         {
            ///             "sceneName": null,
            ///             "sourceName": null,
            ///             "searchOffset": 0
            ///         }
            /// </summary>
            SetInputSettings,

            /// <summary>
            ///     Request to create an input. The request data takes the form of:
            ///         {
            ///             "inputName": string,
            ///             "inputSettings": JObject,
            ///             "overlay": bool
            ///         }
            /// </summary>
            CreateInput,

            /// <summary>
            ///     Enum member for setting the audio monitoring type in an input The request data takes the form of:
            ///        {
            ///             "inputName": string
            ///        }
            /// </summary>
            SetInputAudioMonitorType,

            /// <summary>
            ///     Request to set the input volume. The input data takes the form of:
            ///         {
            ///             "inputName": string,
            ///             "inputVolumeMul": double,
            ///             "inputVolumeDb": double
            ///         }
            /// </summary>
            SetInputVolume
        }

        /// <summary>
        ///     Enum for scene item-related raw requests.
        /// </summary>
        public enum SceneItems {
            /// <summary>
            ///     Requests the ID of a scene item, specified by its name and the scene in which it appears. The
            ///     request data takes the form of:
            ///         {
            ///             "sceneName": null,
            ///             "sourceName": null,
            ///             "searchOffset": 0
            ///         }
            /// </summary>
            GetSceneItemId,

            /// <summary>
            ///     Request to set the enabled state of a scene item. The request data takes the form of:
            ///     {
            ///         "sceneName": string,
            ///         "sceneItemId": 0,
            ///         "sceneItemEnabled": bool
            ///     }
            /// </summary>
            SetSceneItemEnabled,

            /// <summary>
            ///     Gets a list of the scene items in a scene. The request data takes the form of:
            ///     {
            ///         "sceneName": string
            ///     }
            /// </summary>
            GetSceneItemList
        }

        /// <summary>
        ///     Enum for scene-related raw requests.
        /// </summary>
        public enum Scenes {
            /// <summary>
            ///     Request to create a scene.
            /// </summary>
            CreateScene,

            /// <summary>
            ///     Requests the current program scene (as opposed to the current preview scene). Similar to using ObsGetCurrentScene()
            ///     in the Streamer.bot OBS API. The request data takes the form of:
            ///         {
            ///             "sceneName": string
            ///         }
            /// </summary>
            GetCurrentProgramScene,

            /// <summary>
            ///     Request to get the list of scenes. The request data takes the form of an empty object literal.
            /// </summary>
            GetSceneList,

            /// <summary>
            ///     Request to remove a scene. The request data takes the form of:
            ///         {
            ///             "sceneName": string
            ///         }
            /// </summary>
            RemoveScene
        }
    }

    /// <summary>
    ///     Provides functionality to manage the OBS scene source, Cliparino.
    /// </summary>
    public class CliparinoScene {
        private static CliparinoScene _instance;

        /// <summary>
        ///     Constructor for the Cliparino scene that hosts the player.
        /// </summary>
        /// <remarks>
        ///     This class is a singleton that creates a new instance of the Cliparino scene and sets the scene UUID.
        /// </remarks>
        static CliparinoScene() {
            _instance = Create();
            SceneUuid = GetSceneUUID();
        }

        /// <summary>
        ///     Gets or sets the list of parent scene names.
        /// </summary>
        public List<string> Parents { get; } = [];

        /// <summary>
        ///     Gets or sets the UUID of the scene.
        /// </summary>
        private static string SceneUuid { get; set; }

        /// <summary>
        ///     Creates or retrieves the singleton instance of the CliparinoScene.
        /// </summary>
        /// <returns>
        ///     An instance of <see cref="CliparinoScene"/>
        /// </returns>
        public static CliparinoScene Create() {
            return _instance ??= new CliparinoScene();
        }

        /// <summary>
        ///     Creates an OBS request object with the specified request type.
        /// </summary>
        /// <param name="requestType">
        ///     The type of the OBS request.
        /// </param>
        /// <returns>
        ///     An
        ///     <see cref="OBSRequest" />
        ///     object if the request type is valid; otherwise,
        ///     <c>
        ///         null
        ///     </c>
        ///     .
        /// </returns>
        private static OBSRequest CreateRequest(object requestType) {
            return requestType == null ? null : new OBSRequest(requestType, new { });
        }

        /// <summary>
        ///     Removes the current scene from OBS.
        /// </summary>
        /// <returns>
        ///     <c>
        ///         true
        ///     </c>
        ///     if the scene was successfully removed; otherwise,
        ///     <c>
        ///         false
        ///     </c>
        ///     .
        /// </returns>
        /// <remarks>
        ///     This method logs the removal process and handles any exceptions that occur.
        /// </remarks>
        public static bool Remove() {
            Logger.Log("Scene.Remove method called", Logger.LogLevel.Info);

            try {
                if (SceneName != null && SceneUuid != null) {
                    var requestObject = CreateRequest(OBSRequestRawType.Scenes.RemoveScene);
                    var success = OBSSendRaw(requestObject, out var response);

                    Logger.Log($"OBS responded with {response}", Logger.LogLevel.Info);

                    return success;
                }
            } catch (Exception ex) {
                Logger.Log($"Exception in CliparinoScene.Remove {ex.Message}", Logger.LogLevel.Error);

                return false;
            }

            return false;
        }

        /// <summary>
        ///     Retrieves the UUID of the scene with the specified name.
        /// </summary>
        /// <remarks>
        ///     This method sends a request to OBS to get the list of scenes and searches for the scene with the specified name. If
        ///     the scene is found, its UUID is stored in the
        ///     <see cref="SceneUuid" />
        ///     property.
        /// </remarks>
        private static string GetSceneUUID() {
            var req = CreateRequest(OBSRequestRawType.Scenes.GetSceneList);

            if (!OBSSendRaw(req, out var response)) return null;
            if (JObject.Parse(response)["scenes"] is not JArray scenes) return null;

            foreach (var scene in scenes) {
                if (scene["sceneName"]?.ToString() != SceneName) continue;

                return scene["sceneUuid"]?.ToString();
            }

            return Guid.Empty.ToString();
        }

        public static bool SceneUUIDIsMatch() {
            return string.Equals(GetSceneUUID(), SceneUuid);
        }

        public void UpdateUUID() {
            SceneUuid = GetSceneUUID();
        }
    }

    /// <summary>
    ///     Provides functionality to manage the OBS player source.
    /// </summary>
    public class PlayerSource {
        private static PlayerSource _instance;

        /// <summary>
        ///     Static constructor to initialize the PlayerSource class.
        /// </summary>
        private PlayerSource() {
            try {
                Logger.Log("PlayerSource constructor called", Logger.LogLevel.Info);

                if (!SceneExists(SceneName)) CreateScene(SceneName);

                if (!SourceExistsInScene(SceneName, PlayerSourceName))
                    ManagePlayerSourceInScene(SceneName, PlayerSourceName);

                SourceUuid = GetSourceUUID();

                Logger.Log("PlayerSource initialized successfully", Logger.LogLevel.Info);
            } catch (Exception ex) {
                Logger.Log($"Error initializing PlayerSource: {ex.Message}", Logger.LogLevel.Error);
            }
        }

        /// <summary>
        ///     The UUID of the player source.
        /// </summary>
        public static string SourceUuid { get; private set; }

        public static PlayerSource Create() {
            if (_instance != null) return _instance;

            _instance = new PlayerSource();
            Logger.Log("PlayerSource singleton instance created", Logger.LogLevel.Info);

            return _instance;
        }

        /// <summary>
        ///     Stops the clip player by hiding the source and setting the browser source to blank.
        /// </summary>
        /// <returns>
        ///     True if the clip player was stopped successfully; otherwise, false.
        /// </returns>
        /// <remarks>
        ///     Uses the raw version of ObsGetCurrentScene, GetCurrentProgramScene, in order to allow the function to remain
        ///     static.
        /// </remarks>
        public static bool StopClipPlayer() {
            Logger.Log("StopClipPlayer method called", Logger.LogLevel.Info);

            try {
                OBSSendRaw(new OBSRequest(OBSRequestRawType.Scenes.GetCurrentProgramScene, new { }),
                           out var currentScene);
                OBSSendRaw(new OBSRequest(OBSRequestRawType.SceneItems.GetSceneItemId, new { }), out var sceneItemID);
                OBSSendRaw(new OBSRequest(OBSRequestRawType.SceneItems.SetSceneItemEnabled,
                                          new {
                                              sceneName = currentScene,
                                              sceneItemId = sceneItemID,
                                              sceneItemEnabled = false
                                          }),
                           out _);
                OBSSendRaw(new OBSRequest(OBSRequestRawType.Inputs.SetInputSettings,
                                          new {
                                              inputName = PlayerSourceName,
                                              inputSettings = new { url = "about:blank" },
                                              overlay = true
                                          }),
                           out _);
                Logger.Log("Clip player stopped.", Logger.LogLevel.Info);

                return true;
            } catch (Exception ex) {
                Logger.Log($"Error stopping clip player: {ex.Message}", Logger.LogLevel.Error);

                return false;
            }
        }

        /// <summary>
        ///     Creates an OBS request object with the specified request type.
        /// </summary>
        /// <param name="requestType">
        ///     The type of the OBS request.
        /// </param>
        /// <returns>
        ///     An
        ///     <see cref="OBSRequest" />
        ///     object if the request type is valid; otherwise,
        ///     <c>
        ///         null
        ///     </c>
        ///     .
        /// </returns>
        private static OBSRequest CreateRequest(object requestType) {
            return requestType == null ? null : new OBSRequest(requestType, new { });
        }

        /// <summary>
        ///     Retrieves the UUID of the player source from the current scene.
        /// </summary>
        public static string GetSourceUUID() {
            var req = CreateRequest(OBSRequestRawType.SceneItems.GetSceneItemList);

            if (!OBSSendRaw(req, out var response)) return null;
            if (JObject.Parse(response)["sceneItems"] is not JArray sceneItems) return null;

            foreach (var item in sceneItems) {
                if (item["sourceName"]?.ToString() != PlayerSourceName) continue;

                return item["sourceUuid"]?.ToString();
            }

            return Guid.Empty.ToString();
        }

        public static bool SourceUUIDIsMatch() {
            return string.Equals(GetSourceUUID(), SourceUuid);
        }

        public void UpdateUUID() {
            SourceUuid = GetSourceUUID();
        }
    }

    /// <summary>
    ///     Represents a request to be sent to OBS.
    /// </summary>
    private class OBSRequest {
        /// <summary>
        ///     Initializes a new instance of the
        ///     <see cref="OBSRequest" />
        ///     class.
        /// </summary>
        /// <param name="requestType">
        ///     The type of the OBS request.
        /// </param>
        /// <param name="requestData">
        ///     The data to be included with the request.
        /// </param>
        public OBSRequest(object requestType, object requestData) {
            if (IsValidRequestType(requestType))
                RequestType = requestType;
            else
                Logger.Log("Invalid request type", Logger.LogLevel.Error);

            RequestData = requestData;
        }

        /// <summary>
        ///     The type of the OBS request.
        /// </summary>
        public object RequestType { get; }

        /// <summary>
        ///     The data to be included with the OBS request.
        /// </summary>
        public object RequestData { get; }

        /// <summary>
        ///     Determines whether the specified request type is valid.
        /// </summary>
        /// <param name="requestType">
        ///     The request type to validate.
        /// </param>
        /// <returns>
        ///     <c>
        ///         true
        ///     </c>
        ///     if the request type is valid; otherwise,
        ///     <c>
        ///         false
        ///     </c>
        ///     .
        /// </returns>
        private static bool IsValidRequestType(object requestType) {
            var validTypes = new[] {
                typeof(OBSRequestRawType.Inputs),
                typeof(OBSRequestRawType.SceneItems),
                typeof(OBSRequestRawType.Scenes),
            };

            return validTypes.Any(type => Enum.IsDefined(type, requestType));
        }
    }

    /// <summary>
    ///     Provides methods to interact with OBS (Open Broadcaster Software) through raw requests.
    /// </summary>
    private static class DataBroker {
        /// <summary>
        ///     Sends a raw request to OBS and logs the response.
        /// </summary>
        /// <param name="requestType">
        /// </param>
        /// <param name="jRequest">
        /// </param>
        /// <param name="response">
        ///     The response from OBS.
        /// </param>
        /// <returns>
        ///     True if the request was successful; otherwise, false.
        /// </returns>
        public static bool OBSSendRaw(string requestType, string jRequest, out string response) {
            Logger.Log("DataBroker.OBSSendRaw method called", Logger.LogLevel.Info);

            try {
                response = CPHStore.IsInitialized ? CPHStore.GetCPH().ObsSendRaw(requestType, jRequest) : null;

                if (string.IsNullOrEmpty(response) || response.Contains("Error")) {
                    Logger.Log($"OBS either didn't respond or had an error. Response: {response}",
                               Logger.LogLevel.Error);

                    return false;
                }

                Logger.Log($"OBS responded with {response}", Logger.LogLevel.Info);

                return true;
            } catch (Exception ex) {
                Logger.Log($"Exception occurred in DataBroker.OBSSendRaw: {ex.Message}", Logger.LogLevel.Error);

                response = string.Empty;

                return false;
            }
        }

        /// <summary>
        ///     Shows a source in OBS.
        /// </summary>
        /// <param name="scene">
        ///     The name of the scene containing the source.
        /// </param>
        /// <param name="source">
        ///     The name of the source to show.
        /// </param>
        /// <param name="response">
        ///     The response from OBS.
        /// </param>
        /// <returns>
        ///     True if the source was shown successfully; otherwise, false.
        /// </returns>
        public static bool OBSShow(string scene, string source, out string response) {
            return ToggleSourceVisibility(scene, source, true, out response);
        }

        /// <summary>
        ///     Hides a source in OBS.
        /// </summary>
        /// <param name="scene">
        ///     The name of the scene containing the source.
        /// </param>
        /// <param name="source">
        ///     The name of the source to hide.
        /// </param>
        /// <param name="response">
        ///     The response from OBS.
        /// </param>
        /// <returns>
        ///     True if the source was hidden successfully; otherwise, false.
        /// </returns>
        public static bool OBSHide(string scene, string source, out string response) {
            return ToggleSourceVisibility(scene, source, false, out response);
        }

        /// <summary>
        ///     Toggles the visibility of a source in OBS.
        /// </summary>
        /// <param name="scene">
        ///     The name of the scene containing the source.
        /// </param>
        /// <param name="source">
        ///     The name of the source to toggle visibility for.
        /// </param>
        /// <param name="setVisible">
        ///     The visibility state to set for the source.
        /// </param>
        /// <param name="response">
        ///     The response from OBS.
        /// </param>
        /// <returns>
        ///     True if the visibility was toggled successfully; otherwise, false.
        /// </returns>
        private static bool ToggleSourceVisibility(string scene, string source, bool setVisible, out string response) {
            Logger.Log($"DataBroker.ToggleSourceVisibility method called with setVisible = {setVisible}",
                       Logger.LogLevel.Info);

            try {
                if (setVisible) {
                    if (CPHStore.IsInitialized) {
                        CPHStore.GetCPH().ObsShowSource(scene, source);
                    } else {
                        response = "";

                        return false;
                    }
                } else {
                    if (CPHStore.IsInitialized) {
                        CPHStore.GetCPH().ObsHideSource(scene, source);
                    } else {
                        response = "";

                        return false;
                    }
                }

                if (!CPHStore.IsInitialized) {
                    response = "";

                    return false;
                }

                var isVisible = CPHStore.GetCPH().ObsIsSourceVisible(scene, source);

                if (isVisible == setVisible && PlayerSource.SourceUUIDIsMatch()) {
                    Logger.Log($"OBS reports that {source} visibility is {setVisible}.", Logger.LogLevel.Info);
                    response = PlayerSource.SourceUuid ?? PlayerSource.GetSourceUUID();

                    return true;
                }

                Logger.Log($"OBS reports that {source} visibility is {setVisible}.", Logger.LogLevel.Warn);
                response = Guid.Empty.ToString();

                return false;
            } catch (Exception ex) {
                Logger.Log($"Exception in OBSShow: {ex.Message}", Logger.LogLevel.Error);

                response = Guid.Empty.ToString();

                return false;
            }
        }
    }

    /// <summary>
    ///     Wrapper class built to handle user selection of logging.
    /// </summary>
    public static class Logger {
        /// <summary>
        ///     Specifies the severity level of a log message.
        /// </summary>
        public enum LogLevel {
            /// <summary>
            ///     Informational messages that represent the normal functioning of the application.
            /// </summary>
            Info,

            /// <summary>
            ///     Warning messages that indicate a potential issue or important event.
            /// </summary>
            Warn,

            /// <summary>
            ///     Error messages that indicate a failure in the application.
            /// </summary>
            Error
        }

        private static bool? _logging;

        /// <summary>
        ///     Gets a value indicating whether logging is enabled.
        /// </summary>
        /// <value>
        ///     <c>
        ///         true
        ///     </c>
        ///     if logging is enabled; otherwise,
        ///     <c>
        ///         false
        ///     </c>
        ///     .
        /// </value>
        private static bool Logging {
            get {
                if (_logging.HasValue) return _logging.Value;

                _logging = !CPHStore.GetCPH().TryGetArg("logging", out bool logging) || logging;

                return _logging.Value;
            }
        }

        /// <summary>
        ///     Logs a message with a specified log level.
        /// </summary>
        /// <param name="message">
        ///     The message to log.
        /// </param>
        /// <param name="level">
        ///     The severity level of the log message.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        ///     Thrown when the specified
        ///     <paramref name="level" />
        ///     is not a valid
        ///     <see cref="Logger.LogLevel" />
        ///     .
        /// </exception>
        /// <remarks>
        ///     As of right now, only
        ///     <c>
        ///         Logger.LogLevel.Info
        ///     </c>
        ///     and
        ///     <c>
        ///         Logger.LogLevel.Warn
        ///     </c>
        ///     will be disabled if `logging = false`. Prepends a label to the message to make Cliparino's log messages distinct.
        /// </remarks>
        public static void Log(string message, LogLevel level) {
            if (level != LogLevel.Error && !Logging) return;

            const string label = "Cliparino";
            var levelText = level switch {
                LogLevel.Error => "ERR",
                LogLevel.Warn => "WRN",
                _ => "INF"
            };

            var prefix = $"{label} :: [{levelText}]: ";
            var cph = CPHStore.GetCPH();

            if (cph == null) {
                Console.WriteLine($"{prefix}An error occurred, and was about to be logged, but CPH was null.\n{message}");

                return;
            }

            try {
                Action<string> logAction = level switch {
                    LogLevel.Info => cph.LogInfo,
                    LogLevel.Warn => cph.LogWarn,
                    LogLevel.Error => cph.LogError,
                    _ => throw new ArgumentOutOfRangeException(nameof(level),
                                                               level,
                                                               $"No member of enum Logger.LogLevel matching {level.ToString()} exists.")
                };

                // Using a local variable to prevent recursive logging causing stack overflow
                var isInLogging = CPHStore.IsInLogging;

                if (!isInLogging) {
                    CPHStore.IsInLogging = true; // Setting a flag to indicate logging is in progress
                    logAction($"{prefix}{message}");
                    CPHStore.IsInLogging = false; // Resetting the flag after logging
                } else {
                    Console.WriteLine($"Avoided stack overflow during logging: {prefix}{message}");
                }
            } catch (Exception ex) {
                Console.WriteLine($"Logging failed: {ex.Message}");
                Console.WriteLine($"{prefix}{message}");
            }

#if DEBUG
            Console.WriteLine($"{prefix}{message}");
#endif
        }
    }
}

/// <summary>
///     Provides methods to create and manage web pages for Cliparino.
/// </summary>
public static class WebpageCreator {
    /// <summary>
    ///     The CSS text used for the web page.
    /// </summary>
    public static readonly string CSSText = new CSSDocument {
        new CSSRule("body") {
            { "background-color", "#0071c5" },
            { "background-color", "rgba(0,113,197,1)" },
            { "margin", "0 auto" },
            { "overflow", "hidden" }
        },
        new CSSRule(".video-player__overlay,.tw-control-bar") { { "display", "none" } },
        new CSSRule(".iframe-container") { { "height", "1080px" }, { "position", "relative" }, { "width", "1920px" } },
        new CSSRule("iframe") {
            { "height", "100%" },
            { "left", "0" },
            { "position", "absolute" },
            { "top", "0" },
            { "width", "100%" }
        },
        new CSSRule(".overlay-text") {
            { "background-color", "#042239" },
            { "background-color", "rgba(4,34,57,1)" },
            { "border-radius", "5px" },
            { "color", "#ffb809" },
            { "left", "15%" },
            { "opacity", "0.5" },
            { "padding", "10px" },
            { "position", "absolute" },
            { "top", "85%" }
        },
        new CSSRule(".overlay-texts .line1,.overlay-texts .line2,.overlay-texts .line3") {
            { "font-family", "'Recursive',monospace" }, { "font-size", "2em" }
        }
    }.Serialize();

    /// <summary>
    ///     The HTML text used for the web page.
    /// </summary>
    private static readonly string HTMLText = string.Join("\n",
                                                          "<!DOCTYPE html>",
                                                          "<html lang=en>",
                                                          "<head>",
                                                          "<link href=/index.css rel=stylesheet type=text/css>",
                                                          "<link href=https://fonts.googleapis.com/css2?family=Recursive:slnt,wght,CASL,CRSV,MONO@-15..0,300..1000,0..1,0..1,0..1&display=swap rel=stylesheet type=text/css>",
                                                          "<meta charset=UTF-8>",
                                                          "<meta content=width=device-width, initial-scale=1.0 name=viewport>",
                                                          "<title>Cliparino</title>",
                                                          "</head>",
                                                          "<body>",
                                                          "<div id=twitch-embed>",
                                                          "<div class=iframe-container>",
                                                          "<iframe allowfullscreen autoplay=true controls=false height=1080 id=clip-iframe mute=false preload=metadata src=https://clips.twitch.tv/embed?clip=PrettyPlumpPineappleTriHard-ZWylxqlrhU1-fsdD&parent=localhost title=Cliparino width=1920>",
                                                          "</iframe>",
                                                          "<div class=overlay-text id=overlay-text>",
                                                          "<div class=line1>",
                                                          "Name doin' Game",
                                                          "</div>",
                                                          "<div class=line2>",
                                                          "Clip title even if it's really long and stuff ",
                                                          "</div>",
                                                          "<div class=line3>",
                                                          "by ClipCurator",
                                                          "</div>",
                                                          "</div>",
                                                          "</div>",
                                                          "</div>",
                                                          "</body>",
                                                          "</html>");

    /// <summary>
    ///     Generates a nonce string for security purposes.
    /// </summary>
    /// <param name="length">
    ///     The length of the nonce string.
    /// </param>
    /// <returns>
    ///     The generated nonce string.
    /// </returns>
    public static string GenerateNonce(int length = 16) {
        using var rng = new RNGCryptoServiceProvider();
        var byteArray = new byte[length];

        rng.GetBytes(byteArray);

        return Convert.ToBase64String(byteArray).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>
    ///     Creates a web page with the specified channel name, game name, and clip curator.
    /// </summary>
    /// <param name="channelName">
    ///     The name of the channel.
    /// </param>
    /// <param name="gameName">
    ///     The name of the game.
    /// </param>
    /// <param name="clipCurator">
    ///     The name of the clip curator.
    /// </param>
    /// <param name="clipTitle">
    ///     The title of the clip.
    /// </param>
    /// <returns>
    ///     True if the web page was created successfully; otherwise, false.
    /// </returns>
    public static bool CreateWebPage(string channelName, string gameName, string clipCurator, string clipTitle) {
        try {
            var html = HTMLText.Replace("Name doin' Game", $"{channelName} doin' {gameName}")
                               .Replace("Clip title even if it's really long and stuff", clipTitle)
                               .Replace("by ClipCurator", $"by {clipCurator}");
            var css = CSSText;
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var indexHtmlPath = Path.Combine(localAppData, "Cliparino", "Index.html");
            var indexCssPath = Path.Combine(localAppData, "Cliparino", "Index.css");

            Directory.CreateDirectory(Path.GetDirectoryName(indexHtmlPath) ?? string.Empty);

            File.WriteAllText(indexHtmlPath, html);
            OBSManager.Logger.Log("Created index.html file.", OBSManager.Logger.LogLevel.Info);

            File.WriteAllText(indexCssPath, css);
            OBSManager.Logger.Log("Created index.css file.", OBSManager.Logger.LogLevel.Info);

            return true;
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in CreateWebPage: {ex.Message}", OBSManager.Logger.LogLevel.Error);

            return false;
        }
    }

    /// <summary>
    ///     Represents a CSS file with content and file name.
    /// </summary>
    public class CSSFile {
        /// <summary>
        ///     Initializes a new instance of the
        ///     <see cref="CSSFile" />
        ///     class.
        /// </summary>
        /// <param name="content">
        ///     The content of the CSS file.
        /// </param>
        /// <param name="fileName">
        ///     The file name of the CSS file.
        /// </param>
        public CSSFile(string content, string fileName) {
            Content = content;
            FileName = fileName;
            WriteCSSFile();
        }

        /// <summary>
        ///     Gets or sets the content of the CSS file.
        /// </summary>
        private string Content { get; }

        /// <summary>
        ///     Gets the local application data folder path.
        /// </summary>
        private string LocalAppData { get; } =
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        /// <summary>
        ///     Gets or sets the file name of the CSS file.
        /// </summary>
        private string FileName { get; }

        /// <summary>
        ///     Writes the CSS file to the local application data folder.
        /// </summary>
        private void WriteCSSFile() {
            var path = Path.Combine(LocalAppData, "Cliparino", FileName);
            File.WriteAllText(path, Content);
        }
    }

    /// <summary>
    ///     Represents a CSS rule with a selector and styles.
    /// </summary>
    private class CSSRule : IEnumerable<KeyValuePair<string, string>> {
        /// <summary>
        ///     Initializes a new instance of the
        ///     <see cref="CSSRule" />
        ///     class.
        /// </summary>
        /// <param name="selector">
        ///     The selector of the CSS rule.
        /// </param>
        public CSSRule(string selector) {
            Selector = selector;
        }

        /// <summary>
        ///     Gets or sets the selector of the CSS rule.
        /// </summary>
        private string Selector { get; }

        /// <summary>
        ///     The styles of the CSS rule.
        /// </summary>
        private Dictionary<string, string> Styles { get; } = new();

        /// <inheritdoc />
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() {
            return Styles.GetEnumerator();
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() {
            return GetEnumerator();
        }

        /// <summary>
        ///     Adds a style to the CSS rule.
        /// </summary>
        /// <param name="property">
        ///     The property of the style.
        /// </param>
        /// <param name="value">
        ///     The value of the style.
        /// </param>
        public void Add(string property, string value) {
            Styles[property] = value;
        }

        /// <summary>
        ///     Serializes the CSS rule to a string.
        /// </summary>
        /// <returns>
        ///     The serialized CSS rule.
        /// </returns>
        public string Serialize() {
            var sb = new StringBuilder();

            sb.Append(Selector).Append("{");

            foreach (var style in Styles) sb.Append(style.Key).Append(":").Append(style.Value).Append(";");

            sb.Append("}");

            return sb.ToString();
        }
    }

    /// <summary>
    ///     Represents a CSS document with a collection of CSS rules.
    /// </summary>
    private class CSSDocument : IEnumerable<CSSRule> {
        /// <summary>
        ///     Gets the collection of CSS rules.
        /// </summary>
        private List<CSSRule> Rules { get; } = [];

        /// <inheritdoc />
        public IEnumerator<CSSRule> GetEnumerator() {
            return Rules.GetEnumerator();
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() {
            return GetEnumerator();
        }

        /// <summary>
        ///     Adds a CSS rule to the document.
        /// </summary>
        /// <param name="rule">
        ///     The CSS rule to add.
        /// </param>
        public void Add(CSSRule rule) {
            Rules.Add(rule);
        }

        /// <summary>
        ///     Serializes the CSS document to a string.
        /// </summary>
        /// <returns>
        ///     The serialized CSS document.
        /// </returns>
        public string Serialize() {
            var sb = new StringBuilder();

            foreach (var rule in Rules) sb.Append(rule.Serialize());

            return sb.ToString();
        }
    }

    /// <summary>
    ///     Represents an HTML file with content and file name.
    /// </summary>
    public class HTMLFile {
        /// <summary>
        ///     Initializes a new instance of the
        ///     <see cref="HTMLFile" />
        ///     class.
        /// </summary>
        /// <param name="content">
        ///     The content of the HTML file.
        /// </param>
        /// <param name="fileName">
        ///     The file name of the HTML file.
        /// </param>
        public HTMLFile(string content, string fileName) {
            Content = content;
            FileName = fileName;
            WriteHTMLFile();
        }

        /// <summary>
        ///     Gets or sets the content of the HTML file.
        /// </summary>
        private string Content { get; }

        /// <summary>
        ///     Gets the local application data folder path.
        /// </summary>
        private string LocalAppData { get; } =
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        /// <summary>
        ///     Gets or sets the file name of the HTML file.
        /// </summary>
        private string FileName { get; }

        /// <summary>
        ///     Writes the HTML file to the local application data folder.
        /// </summary>
        private void WriteHTMLFile() {
            var path = Path.Combine(LocalAppData, "Cliparino", FileName);
            File.WriteAllText(path, Content);
        }
    }
}

/// <summary>
///     Provides basic methods to initialize and stop a simple HTTP server.
/// </summary>
public interface ISimpleHttpServer {
    bool Init();
    void Stop();
}

/// <summary>
/// Provides functionality to start and stop a simple HTTP server using HttpListener.
/// This is implemented as a singleton pattern.
/// </summary>
public class SimpleHttpServer : ISimpleHttpServer {
    private static readonly HttpListener Listener = new();

    private static readonly Dictionary<string, string> Headers = new() {
        { "Access-Control-Allow-Origin", "*" },
        { "Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS" },
        { "Content-Security-Policy", "script-src 'nonce-x';\nobject-src 'none';\n base-uri 'none';" }
    };

    private static readonly string[] Prefixes = ["http://localhost:8080/", "https://localhost:8080/"];
    public static readonly Lazy<SimpleHttpServer> Instance = new(() => new SimpleHttpServer());

    private SimpleHttpServer() {
        if (!HttpListener.IsSupported)
            throw new NotSupportedException("HttpListener is not supported on this platform.");

        AddPrefixesToListener();
    }

    public bool Init() {
        return Start();
    }

    public void Stop() {
        Listener.Stop();
        Listener.Close();
    }

    private static void AddPrefixesToListener() {
        foreach (var prefix in Prefixes) Listener.Prefixes.Add(prefix);
    }

    /// <summary>
    ///     Starts the HTTP server and begins listening for incoming requests.
    /// </summary>
    /// <returns>
    ///     A boolean value indicating whether the server was started successfully.
    /// </returns>
    /// <remarks>
    ///     This method initializes and begins running the HTTP listener in a new task so that it can handle incoming HTTP
    ///     requests asynchronously.
    /// </remarks>
    private static bool Start() {
        Listener.Start();
        Console.WriteLine("Server started...");

        Task.Run(() => {
                     while (Listener.IsListening)
                         try {
                             var context = Listener.GetContext();

                             ProcessRequest(context);
                         } catch (HttpListenerException) {
                             break;
                         } catch (Exception ex) {
                             OBSManager.Logger.Log($"Exception in server loop: {ex.Message}",
                                                   OBSManager.Logger.LogLevel.Error);
                         }
                 });

        return true;
    }

    /// <summary>
    ///     Processes the incoming HTTP request, generates and sends the response.
    /// </summary>
    /// <param name="context">
    ///     The
    ///     <see cref="HttpListenerContext" />
    ///     object that encapsulates both the request and the response.
    /// </param>
    /// <exception cref="FileNotFoundException">
    ///     Thrown if the "index.html" file is not found.
    /// </exception>
    /// <exception cref="Exception">
    ///     Thrown if there is an error during the processing of the request.
    /// </exception>
    /// <remarks>
    ///     The method reads the "index.html" file to generate the response body, sets response headers, and writes the
    ///     response back to the client.
    /// </remarks>
    private static void ProcessRequest(HttpListenerContext context) {
        try {
            var responseString = File.ReadAllText("index.html");
            var buffer = Encoding.UTF8.GetBytes(responseString);

            context.Response.ContentLength64 = buffer.Length;
            context.Response.ContentType = "text/html";

            foreach (var header in Headers)
                context.Response.AddHeader(header.Key,
                                           header.Key == "Content-Security-Policy"
                                               ? header.Value.Replace("x", WebpageCreator.GenerateNonce())
                                               : header.Value);

            using var output = context.Response.OutputStream;

            output.Write(buffer, 0, buffer.Length);
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in ProcessRequest: {ex.Message}", OBSManager.Logger.LogLevel.Error);
        }
    }
}