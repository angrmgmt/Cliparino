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
using System.Text;
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
public class CPHInline : CPHInlineBase {
    private const int DefaultWidth = 1920;
    private const int DefaultHeight = 1080;
    private const string LastClipUrlKey = "last_clip_url";
    private const string NoUrlMessage = "No URL provided and no last clip URL found.";

    private const string CSSText = """
                                   
                                       div {
                                           background-color: #0071c5;
                                           background-color: rgba(0,113,197,1);
                                           margin: 0 auto;
                                           overflow: hidden;
                                       }
                                   
                                       #twitch-embed {
                                           display: none;
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
                                           background-color: rgba(4,34,57,1);
                                           border-radius: 5px;
                                           color: #ffb809;
                                           left: 15%;
                                           opacity: 0.5;
                                           padding: 10px;
                                           position: absolute;
                                           top: 85%;
                                       }
                                   
                                       .line1, .line2, .line3 {
                                           font-family: 'Recursive', monospace;
                                           font-size: 2em;
                                       }
                                       
                                   """;

// Introduced Constant
    private const string ConstClipDataError = "Unable to retrieve clip data.";

    private static readonly string[] HtmlElements = [
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
        "<iframe allowfullscreen autoplay=true controls=false height=1080 id=clip-iframe mute=false preload=metadata src=https://clips.twitch.tv/embed?clip={0}&parent=localhost title=Cliparino width=1920>",
        "</iframe>",
        "<div class=overlay-text id=overlay-text>",
        "<div class=line1>",
        "{1} doin' {2}",
        "</div>",
        "<div class=line2>",
        "{3}",
        "</div>",
        "<div class=line3>",
        "by {4}",
        "</div>",
        "</div>",
        "</div>",
        "</div>",
        "</body>",
        "</html>"
    ];

    private static readonly string HTMLText = string.Join(Environment.NewLine, HtmlElements);
    private readonly object _serverLock = new();
    private bool _loggingEnabled;
    private HttpListener _server;

    private static string GetErrorMessage(string methodName) {
        return $"An error occurred in {methodName}";
    }

    // ReSharper disable once UnusedMember.Global
    public bool Execute() {
        try {
            if (!TryGetCommand(out var command)) {
                if (_loggingEnabled) CPH.LogWarn("Command argument is missing.");

                return false;
            }

            GetInputArguments(out var input0, out var width, out var height);
            CPH.TryGetArg("logging", out _loggingEnabled);

            if (_loggingEnabled) CPH.LogInfo($"Executing command: {command}");

            switch (command.ToLower()) {
                case "!watch": HandleWatchCommand(input0, width, height); break;
                case "!so": HandleShoutoutCommand(input0); break;
                case "!replay": HandleReplayCommand(width, height); break;
                case "!stop": HandleStopCommand(); break;
                default:
                    if (_loggingEnabled) CPH.LogWarn($"Unknown command: {command}");

                    return false;
            }

            return true;
        } catch (Exception ex) {
            CPH.LogError($"An error occurred: {ex.Message}");

            return false;
        }
    }

    private void GetInputArguments(out string input0, out int width, out int height) {
        LogDebugIfEnabled("GetInputArguments called to get values of input0, width, and height");

        try {
            if (!CPH.TryGetArg("input0", out input0))
                throw new ArgumentException("Missing or invalid 'input0' argument.");

            if (!CPH.TryGetArg("width", out width)) width = DefaultWidth;
            if (!CPH.TryGetArg("height", out height)) height = DefaultHeight;
            width = width == 0 ? DefaultWidth : width;
            height = height == 0 ? DefaultHeight : height;
        } catch (Exception ex) {
            CPH.LogError($"An error occurred in GetInputArguments: {ex.Message}");

            input0 = null;
            width = 0;
            height = 0;
        }
    }

    private bool TryGetCommand(out string command) {
        return CPH.TryGetArg("command", out command);
    }

    private void HandleWatchCommand(string url, int width, int height) {
        LogDebugIfEnabled($"HandleWatchCommand called with url: {url}, width: {width}, height: {height}");

        Task.Run(async () => {
                     CPH.LogInfo("New Thread started to get clip info from Twitch.");

                     // Ensure Cliparino is set up in the current scene
                     EnsureCliparinoInCurrentScene();

                     try {
                         url = GetEffectiveUrl(url);

                         if (string.IsNullOrEmpty(url)) return;

                         if (_loggingEnabled) LogDebugWithUrl(width, height, url);

                         await SetupAndDisplayClip(url);
                     } catch (Exception ex) {
                         CPH.LogError($"{GetErrorMessage("HandleWatchCommand")}: {ex.Message}");
                     }
                 });
    }

    private string GetEffectiveUrl(string url) {
        LogDebugIfEnabled($"GetEffectiveUrl called with url: {url}");

        try {
            if (string.IsNullOrEmpty(url)) {
                url = CPH.GetGlobalVar<string>(LastClipUrlKey);

                if (string.IsNullOrEmpty(url)) {
                    if (_loggingEnabled) CPH.LogWarn(NoUrlMessage);

                    throw new InvalidOperationException("No valid URL available.");
                }
            }

            CPH.SetGlobalVar(LastClipUrlKey, url);

            return url;
        } catch (Exception ex) {
            CPH.LogError($"Error occurred in GetEffectiveUrl: {ex.Message}");

            return "about:blank";
        }
    }

    private void HandleShoutoutCommand(string user) {
        LogDebugIfEnabled($"HandleShoutoutCommand called with user: {user}");

        Task.Run(async () => {
                     CPH.LogInfo("New Thread started to get clip and clip info from Twitch.");

                     try {
                         if (string.IsNullOrEmpty(user)) {
                             if (_loggingEnabled) CPH.LogWarn("No user provided for shoutout.");

                             return;
                         }

                         TryGetArgs(out var message,
                                    out var featuredOnly,
                                    out var maxClipSecondsStr,
                                    out var clipAgeDaysStr);

                         var maxClipSeconds = GetMaxClipSeconds(maxClipSecondsStr);
                         var clipAgeDays = GetClipAgeDays(clipAgeDaysStr);
                         var clip = GetRandomClip(user, featuredOnly, maxClipSeconds, clipAgeDays);

                         if (clip == null) {
                             if (_loggingEnabled) CPH.LogWarn("No valid clip found for shoutout.");

                             return;
                         }

                         CPH.SetGlobalVar(LastClipUrlKey, clip.Url);

                         await SetupAndDisplayClip(clip.Url);

                         if (!string.IsNullOrEmpty(message))
                             if (_loggingEnabled && !string.IsNullOrEmpty(message)) {
                                 CPH.LogInfo("Sending confirmation of shoutout to chat.");

                                 var formattedMessage = FormatShoutoutMessage(message, user);

                                 CPH.SendMessage(formattedMessage);
                             }
                     } catch (Exception ex) {
                         CPH.LogError($"{GetErrorMessage("HandleShoutoutCommand")}: {ex.Message}");
                     }
                 });
    }

    private void TryGetArgs(out string message,
                            out bool featuredOnly,
                            out string maxClipSecondsStr,
                            out string clipAgeDaysStr) {
        CPH.TryGetArg("message", out message);
        CPH.TryGetArg("featuredOnly", out featuredOnly);
        CPH.TryGetArg("maxClipSeconds", out maxClipSecondsStr);
        CPH.TryGetArg("clipAgeDays", out clipAgeDaysStr);
    }

    private static int GetMaxClipSeconds(string maxClipSecondsStr) {
        return string.IsNullOrEmpty(maxClipSecondsStr) ? 30 : int.Parse(maxClipSecondsStr);
    }

    private static int GetClipAgeDays(string clipAgeDaysStr) {
        return string.IsNullOrEmpty(clipAgeDaysStr) ? 30 : int.Parse(clipAgeDaysStr);
    }

    private string FormatShoutoutMessage(string message, string user) {
        var lastGame = GetLastGamePlayed(user);

        return message.Replace("%userName%", user).Replace("%userGame%", lastGame);
    }

    private Clip GetRandomClip(string userId, bool featuredOnly, int maxClipSeconds, int clipAgeDays) {
        LogDebugUserClipRequest(userId, featuredOnly, maxClipSeconds, clipAgeDays);

        try {
            var twitchUser = FetchTwitchUser(userId);

            if (twitchUser == null) return LogAndReturnNull($"Twitch user not found for userId: {userId}");

            var clips = RetrieveClips(userId, clipAgeDays).ToList();

            if (!clips.Any())
                return LogAndReturnNull($"No clips found for userId: {userId} within {clipAgeDays} days.");

            var matchingClips = FilterClips(clips, featuredOnly, maxClipSeconds);

            if (!matchingClips.Any())
                return LogAndReturnNull($"No matching clips found for userId: {userId} with the specified filters.");

            var selectedClip = SelectRandomClip(matchingClips);

            LogDebugIfEnabled($"Selected clip: {selectedClip.Url}");

            return Clip.FromTwitchClip(selectedClip);
        } catch (Exception ex) {
            CPH.LogError($"{GetErrorMessage("GetRandomClip")}: {ex.Message}");

            return null;
        }
    }

    private Clip LogAndReturnNull(string message) {
        if (_loggingEnabled) CPH.LogWarn(message);

        return null;
    }

    private void LogDebugIfEnabled(string message) {
        if (_loggingEnabled) CPH.LogDebug(message);
    }

    private void LogDebugUserClipRequest(string userId, bool featuredOnly, int maxSeconds, int ageDays) {
        LogDebugIfEnabled($"Getting random clip for userId: {userId}, featuredOnly: {featuredOnly}, maxSeconds: {maxSeconds}, ageDays: {ageDays}");
    }

    private object FetchTwitchUser(string userId) {
        LogDebugIfEnabled($"FetchTwitchUser called with userId: {userId}");

        try {
            var twitchUser = CPH.TwitchGetExtendedUserInfoById(userId);

            if (twitchUser == null && _loggingEnabled) CPH.LogWarn($"Could not find Twitch userId: {userId}");

            return twitchUser ?? throw new InvalidOperationException($"User with ID '{userId}' not found.");
        } catch (Exception ex) {
            CPH.LogError($"{GetErrorMessage("FetchTwitchUser")}: {ex.Message}");

            throw;
        }
    }

    private IEnumerable<ClipData> RetrieveClips(string userId, int clipAgeDays) {
        LogDebugIfEnabled($"RetrieveClips called with userId: {userId}, clipAgeDays: {clipAgeDays}");

        try {
            var endDate = DateTime.UtcNow;
            var startDate = endDate.AddDays(-clipAgeDays);

            return CPH.GetClipsForUserById(userId, startDate, endDate);
        } catch (Exception ex) {
            CPH.LogError($"{GetErrorMessage("RetrieveClips")}: {ex.Message}");

            return null;
        }
    }

    private bool FilteredByCriteria(ClipData clip, bool featuredOnly, int maxSeconds) {
        LogDebugIfEnabled($"FilteredByCriteria called with clip: {clip}, featuredOnly: {featuredOnly}, maxSeconds: {maxSeconds}");

        try {
            return (!featuredOnly || clip.IsFeatured) && clip.Duration <= maxSeconds;
        } catch (Exception ex) {
            CPH.LogError($"{GetErrorMessage("FilteredByCriteria")}: {ex.Message}");

            return false;
        }
    }

    private List<ClipData> FilterClips(IEnumerable<ClipData> clips, bool featuredOnly, int maxSeconds) {
        return clips.Where(c => FilteredByCriteria(c, featuredOnly, maxSeconds)).ToList();
    }

    private static ClipData SelectRandomClip(List<ClipData> clips) {
        return clips[new Random().Next(clips.Count)];
    }

    private string GetLastGamePlayed(string userId) {
        LogDebugIfEnabled($"GetLastGamePlayed called with userId: {userId}");

        try {
            if (_loggingEnabled) CPH.LogInfo($"Getting last game played for userId: {userId}");

            var userInfo = GetTwitchUserOrLog(userId);

            if (userInfo == null) return "Unknown Game";

            return string.IsNullOrEmpty(userInfo.Game) ? "Unknown Game" : userInfo.Game;
        } catch (Exception ex) {
            CPH.LogError($"{GetErrorMessage(nameof(GetLastGamePlayed))}: {ex.Message}");

            return "Unknown Game";
        }
    }

    private dynamic GetTwitchUserOrLog(string userId) {
        var userInfo = CPH.TwitchGetExtendedUserInfoById(userId);

        if (userInfo == null && _loggingEnabled) CPH.LogWarn($"Could not find Twitch userId or channel info: {userId}");

        return userInfo;
    }

    private void HandleReplayCommand(int width, int height) {
        LogDebugIfEnabled($"HandleReplayCommand called with width: {width}, height: {height}");

        Task.Run(async () => {
                     try {
                         CPH.LogInfo("Handling replay command.");

                         var lastClipUrl = GetLastClipUrl();

                         if (string.IsNullOrEmpty(lastClipUrl)) return;

                         if (_loggingEnabled) LogClipInfo(width, height, lastClipUrl);

                         await DisplayClip(lastClipUrl);
                     } catch (Exception ex) {
                         CPH.LogError($"{GetErrorMessage(nameof(HandleReplayCommand))}: {ex.Message}");
                     }
                 });
    }

    private string GetLastClipUrl() {
        var url = CPH.GetGlobalVar<string>(LastClipUrlKey);

        if (string.IsNullOrEmpty(url) && _loggingEnabled) CPH.LogWarn("No last clip URL found for replay.");

        return url;
    }

    private void LogClipInfo(int width, int height, string url) {
        if (_loggingEnabled) CPH.LogInfo($"Setting browser source with URL: {url}, width: {width}, height: {height}");
    }

    private async Task DisplayClip(string url) {
        await SetupAndDisplayClip(url);
    }

    private void LogDebugWithUrl(int width, int height, string url) {
        if (_loggingEnabled) CPH.LogInfo($"Setting browser source with URL: {url}, width: {width}, height: {height}");
    }

    private void HandleStopCommand() {
        // Set the Browser Source URL to a blank page
        SetBrowserSource("about:blank");

        // Ensure to hide the "Player" source in the "Cliparino" scene
        HideSourceInScene("Cliparino", "Player");
    }

// Helper Method to Hide Source in a Given Scene
    private void HideSourceInScene(string sceneName, string sourceName) {
        try {
            SetSourceVisibility(sceneName, sourceName, false);
            if (_loggingEnabled) CPH.LogInfo($"Source '{sourceName}' in scene '{sceneName}' has been hidden.");
        } catch (Exception ex) {
            CPH.LogError($"Error in HideSourceInScene: {ex.Message}");
        }
    }

    private async Task SetupAndDisplayClip(string clipUrl, ClipData clipData = null) {
        try {
            clipData = await FetchValidClipData(clipData, clipUrl);

            if (clipData == null) return;

            HostClipWithDetails(clipUrl, clipData);
            ActivateBrowserSource(clipUrl);
        } catch (Exception ex) {
            LogErrorWithMethodName("SetupAndDisplayClip", ex);
        }
    }

    private async Task<ClipData> FetchValidClipData(ClipData clipData, string clipUrl) {
        clipData = await FetchClipDataIfNeeded(clipData, clipUrl);
        if (clipData == null) CPH.LogError(ConstClipDataError);

        return clipData;
    }

    private void HostClipWithDetails(string clipUrl, ClipData clipData) {
        var clipInfo = ExtractClipInfo(clipData);
        CreateAndHostClipPage(clipUrl, clipInfo.StreamerName, clipInfo.ClipTitle, clipInfo.CuratorName, clipData);
    }

    private static (string StreamerName, string ClipTitle, string CuratorName) ExtractClipInfo(ClipData clipData) {
        return GetClipInfo(clipData);
    }

    private void ActivateBrowserSource(string clipUrl) {
        SetBrowserSource(clipUrl);
    }

    private void LogErrorWithMethodName(string methodName, Exception ex) {
        CPH.LogError($"{GetErrorMessage(methodName)}: {ex.Message}");
    }

    private async Task<ClipData> FetchClipDataIfNeeded(ClipData clipData, string clipUrl) {
        if (clipData == null && !string.IsNullOrEmpty(clipUrl)) return await GetClipData(clipUrl);

        return clipData ?? throw new InvalidOperationException("Clip data is unavailable.");
    }

    private static (string StreamerName, string ClipTitle, string CuratorName) GetClipInfo(
        ClipData clipData,
        string defaultCuratorName = "Anonymous") {
        const string unknownStreamer = "Unknown Streamer";
        const string untitledClip = "Untitled Clip";

        string GetValueOrDefault(string value, string defaultValue) {
            return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
        }

        var streamerName = GetValueOrDefault(clipData?.BroadcasterName, unknownStreamer);
        var clipTitle = GetValueOrDefault(clipData?.Title, untitledClip);
        var curatorName = GetValueOrDefault(clipData?.CreatorName, defaultCuratorName);

        return (streamerName, clipTitle, curatorName);
    }

    private async Task<ClipData> GetClipData(string clipUrl) {
        var clientId = CPH.TwitchClientId;
        var clientSecret = CPH.TwitchOAuthToken;

        try {
            if (string.IsNullOrEmpty(clientSecret)) return null;

            if (string.IsNullOrEmpty(clientSecret))
                throw new InvalidOperationException("Twitch client secret is missing or invalid.");

            var clipId = clipUrl.Substring(clipUrl.LastIndexOf("/", StringComparison.Ordinal) + 1);

            return await GetClipAsync(clientId, clientSecret, clipId);
        } catch (Exception ex) {
            CPH.LogError($"{GetErrorMessage(nameof(GetClipData))}: {ex.Message}");
        }

        return null;
    }

    private static async Task<ClipData> GetClipAsync(string clientId, string clientSecret, string clipId) {
        using var client = new HttpClient();

        client.DefaultRequestHeaders.Add("Client-ID", clientId);
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {clientSecret}");

        var response = await client.GetAsync($"https://api.twitch.tv/helix/clips?id={clipId}");
        var content = await response.Content.ReadAsStringAsync();
        var apiResponse = JsonConvert.DeserializeObject<TwitchApiResponse<ClipData>>(content);

        return apiResponse.Data != null && apiResponse.Data.Any() ? apiResponse.Data.First() : null;
    }

    /// <summary>
    ///     Ensures the "Cliparino" source is added to the currently active scene in OBS if not already present.
    /// </summary>
    private void EnsureCliparinoInCurrentScene() {
        try {
            // Get the current scene name from OBS
            var currentScene = CPH.ObsGetCurrentScene();

            if (string.IsNullOrEmpty(currentScene)) {
                LogWarn("Could not retrieve the current active scene in OBS.");

                return;
            }

            // Check if "Cliparino" source exists in the current scene
            const string cliparinoSourceName = "Cliparino";

            if (!SourceExistsInScene(currentScene, cliparinoSourceName)) {
                // Add "Cliparino" source if it does not exist
                const string defaultUrl = "about:blank"; // Placeholder URL, or you can use the one currently in use

                AddBrowserSource(currentScene, cliparinoSourceName, defaultUrl);

                LogInfoIfEnabled($"'Cliparino' source added to scene '{currentScene}'.");
            }

            LogInfoIfEnabled($"'Cliparino' source already exists in the scene: '{currentScene}'.");
        } catch (Exception ex) {
            LogError($"Error in EnsureCliparinoInCurrentScene: {ex.Message}");
        }
    }

    /// <summary>
    ///     Ensures that the "Cliparino" source is added or updated in the target scene without switching scenes.
    /// </summary>
    private void SetBrowserSource(string baseUrl, string targetScene = null) {
        // Generate the full URL for the browser source with a unique nonce
        var sourceUrl = CreateSourceUrl(baseUrl);

        // Default to the current scene if no specific target is provided
        targetScene = targetScene
                      ?? CPH.ObsGetCurrentScene()
                      ?? throw new InvalidOperationException("Unable to retrieve target scene.");

        // Check if the "Cliparino" scene source exists in the target scene
        const string cliparinoSourceName = "Cliparino";

        if (!SourceExistsInScene(targetScene, cliparinoSourceName)) {
            // Add "Cliparino" scene source if not present already
            AddSceneSource(targetScene, cliparinoSourceName);
            LogInfoIfEnabled($"Added '{cliparinoSourceName}' scene source to '{targetScene}'.");
        }

        // Ensure the "Player" source within "Cliparino" is updated and visible
        if (!SourceExistsInScene("Cliparino", "Player")) {
            AddBrowserSource("Cliparino", "Player", sourceUrl);
            LogInfoIfEnabled("Added 'Player' browser source to 'Cliparino' scene.");
        } else {
            UpdateBrowserSource("Cliparino", "Player", sourceUrl);
            LogInfoIfEnabled("Updated 'Player' browser source with new URL.");
        }

        // Ensure "Cliparino" source is visible in the target scene
        ToggleSourceVisibility(targetScene,
                               cliparinoSourceName,
                               CPH.ObsIsSourceVisible(targetScene, cliparinoSourceName));
    }

    private void ToggleSourceVisibility(string targetScene, string sourceName, bool isVisible) {
        // Check if the scene exists
        if (!SceneExists(targetScene)) {
            LogInfoIfEnabled($"Scene '{targetScene}' does not exist, creating.");
            CreateScene("Cliparino");
            SetSourceVisibility(targetScene, sourceName, true);

            return;
        }

        // Check if the source exists in the target scene
        if (!SourceExistsInScene(targetScene, sourceName)) {
            LogInfoIfEnabled($"Source '{sourceName}' does not exist in scene '{targetScene}'.");
            AddSceneSource(targetScene, sourceName);
            SetSourceVisibility("Cliparino", "Player", true);

            return;
        }

        // Toggle visibility based on `isVisible` parameter
        try {
            if (isVisible) {
                CPH.ObsHideSource(targetScene, sourceName);
                LogDebugIfEnabled($"Source '{sourceName}' in scene '{targetScene}' is now hidden.");
            } else {
                CPH.ObsShowSource(targetScene, sourceName);
                LogDebugIfEnabled($"Source '{sourceName}' in scene '{targetScene}' is now visible.");
            }
        } catch (Exception ex) {
            // Log any errors encountered when toggling visibility
            LogDebugIfEnabled($"Error toggling visibility for source '{sourceName}' in scene '{targetScene}': {ex.Message}");
        }
    }

    private void AddSceneSource(string targetScene, string cliparinoSourceName) {
        // Data structure for the OBS request.
        var payload = new {
            requestType = "CreateSceneItem",
            requestData = new { sceneName = targetScene, sourceName = cliparinoSourceName, sceneItemEnabled = true }
        };

        // Sending the request using CPH.ObsSendRaw.
        CPH.ObsSendRaw(payload.requestType, JsonConvert.SerializeObject(payload.requestData));
    }

    private bool SourceExistsInScene(string sceneName, string sourceName) {
        try {
            // Construct the payload for the request
            var payload = new {
                requestType = "GetSceneItemId",
                requestData = new {
                    sceneName, sourceName, searchOffset = 0 // Default offset for the first matching source
                }
            };

            // Send the raw OBS request and get the response
            var response = CPH.ObsSendRaw(payload.requestType, JsonConvert.SerializeObject(payload.requestData));

            // Check if the response starts with "Error"
            if (response.StartsWith("Error"))
                CPH.LogError($"Error encountered: {response}");
            // Handle the error scenario
            else
                try {
                    // Parse response to check for "sceneItemId"
                    var result = JsonConvert.DeserializeObject<Dictionary<string, int>>(response);

                    if (result != null && result.TryGetValue("sceneItemId", out var sceneItemId)) {
                        CPH.LogInfo($"Valid sceneItemId found: {sceneItemId}");

                        return true;
                    }

                    CPH.LogInfo("Response does not contain a valid sceneItemId.");

                    return false;
                } catch (JsonException ex) {
                    CPH.LogError($"Failed to parse response: {ex.Message}");

                    throw new InvalidOperationException("Failed to parse OBS response.");
                }
        } catch (Exception ex) {
            // Log any errors encountered while querying the source
            CPH.LogError($"Error in SourceExistsInScene: {ex.Message}");

            return false;
        }

        // If an error occurred or no valid sceneItemId was returned, the source does not exist
        return false;
    }

    /// <summary>
    ///     Verifies if a scene exists in OBS.
    /// </summary>
    /// <param name="sceneName">
    ///     Name of the scene to check.
    /// </param>
    /// <returns>
    ///     True if the scene exists; otherwise, false.
    /// </returns>
    private bool SceneExists(string sceneName) {
        try {
            // Construct and send request to get the list of available scenes
            var payload = new { requestType = "GetSceneList" };
            var responseJson = CPH.ObsSendRaw(payload.requestType, "{}");

            if (!string.IsNullOrWhiteSpace(responseJson)) {
                // Deserialize the response JSON into a JObject
                var response = JsonConvert.DeserializeObject<JObject>(responseJson);

                // Check if the "scenes" property exists and is a JArray
                if (response != null
                    && response.TryGetValue("scenes", out var scenesToken)
                    && scenesToken is JArray scenesArray)
                    // Iterate through the array and check if the scene name exists
                    return scenesArray.Any(scene => string.Equals(scene["sceneName"]?.ToString(),
                                                                  sceneName,
                                                                  StringComparison.OrdinalIgnoreCase));

                LogWarn("Unable to retrieve the scenes list from OBS or the response format is invalid.");
            } else {
                LogWarn("Received an empty response from OBS.");
            }
        } catch (Exception ex) {
            // Log the error for troubleshooting
            LogError($"Error in SceneExists: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    ///     Creates a new scene in OBS using the "CreateScene" request type.
    /// </summary>
    /// <param name="sceneName">
    ///     Name of the scene to create.
    /// </param>
    private void CreateScene(string sceneName) {
        try {
            // Construct the payload to create a new scene
            var payload = new { requestType = "CreateScene", requestData = new { sceneName } };

            // Send the raw request to create the scene
            CPH.ObsSendRaw(payload.requestType, JsonConvert.SerializeObject(payload.requestData));

            CPH.LogInfo($"Scene '{sceneName}' has been created successfully.");
        } catch (Exception ex) {
            // Log any error that occurs while creating the scene
            LogError($"Error in CreateScene: {ex.Message}");
        }
    }

    /// <summary>
    ///     Adds a browser source to a specific scene in OBS using the "CreateInput" request type.
    /// </summary>
    /// <param name="sceneName">
    ///     Name of the scene where the source will be added.
    /// </param>
    /// <param name="sourceName">
    ///     Name of the browser source.
    /// </param>
    /// <param name="url">
    ///     URL to display in the browser source.
    /// </param>
    /// <param name="width">
    ///     Width of the browser source.
    /// </param>
    /// <param name="height">
    ///     Height of the browser source.
    /// </param>
    private void AddBrowserSource(string sceneName,
                                  string sourceName,
                                  string url,
                                  int width = DefaultWidth,
                                  int height = DefaultHeight) {
        try {
            // Construct the payload to add a browser source
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

            // Send the raw request to add the browser source
            CPH.ObsSendRaw(payload.requestType, JsonConvert.SerializeObject(payload.requestData));

            CPH.LogInfo($"Browser source '{sourceName}' added to scene '{sceneName}' with URL '{url}'.");
        } catch (Exception ex) {
            // Log any error that occurs while adding the browser source
            LogError($"Error in AddBrowserSource: {ex.Message}");
        }
    }

    /// <summary>
    ///     Updates a browser source's URL in OBS.
    /// </summary>
    /// <param name="sceneName">
    ///     Name of the scene containing the browser source.
    /// </param>
    /// <param name="sourceName">
    ///     Name of the browser source to update.
    /// </param>
    /// <param name="url">
    ///     New URL to set for the browser source.
    /// </param>
    private void UpdateBrowserSource(string sceneName, string sourceName, string url) {
        try {
            // Construct the payload to update the browser source
            var payload = new {
                requestType = "SetInputSettings",
                requestData = new { inputName = sourceName, inputSettings = new { url }, overlay = true }
            };

            // Send the raw request to update the browser source
            CPH.ObsSendRaw(payload.requestType, JsonConvert.SerializeObject(payload.requestData));

            CPH.LogInfo($"Browser source '{sourceName}' in scene '{sceneName}' updated with new URL '{url}'.");
        } catch (Exception ex) {
            // Log any error that occurs while updating the browser source
            LogError($"Error in UpdateBrowserSource: {ex.Message}");
        }
    }

    /// <summary>
    ///     Sets the visibility of a source within a scene.
    /// </summary>
    /// <param name="sceneName">
    ///     Name of the scene where the source is located.
    /// </param>
    /// <param name="sourceName">
    ///     Name of the source to change visibility for.
    /// </param>
    /// <param name="isVisible">
    ///     True to make the source visible; false to hide it.
    /// </param>
    private void SetSourceVisibility(string sceneName, string sourceName, bool isVisible) {
        try {
            // Construct the payload to set the visibility of a source
            var payload = new {
                requestType = "SetSceneItemEnabled",
                requestData = new { sceneName, itemName = sourceName, sceneItemEnabled = isVisible }
            };

            // Send the raw request to change source visibility
            CPH.ObsSendRaw(payload.requestType, JsonConvert.SerializeObject(payload.requestData));

            CPH.LogInfo($"Source '{sourceName}' in scene '{sceneName}' visibility set to '{isVisible}'.");
        } catch (Exception ex) {
            // Log any error that occurs while setting source visibility
            LogError($"Error in SetSourceVisibility: {ex.Message}");
        }
    }

    /// <summary>
    ///     Generates a unique URL with a nonce for browser source.
    /// </summary>
    /// <param name="baseUrl">
    ///     Base URL to append the nonce to.
    /// </param>
    /// <returns>
    ///     The generated URL with a nonce.
    /// </returns>
    private static string CreateSourceUrl(string baseUrl) {
        var nonce = GenerateNonce();

        return $"{baseUrl}?nonce={nonce}";
    }

    /// <summary>
    ///     Generates a cryptographically secure random nonce.
    /// </summary>
    /// <returns>
    ///     A 16-character Base64-encoded string.
    /// </returns>
    private static string GenerateNonce() {
        return Convert.ToBase64String(Guid.NewGuid().ToByteArray())
                      .Replace("+", "-")
                      .Replace("/", "_")
                      .Replace("=", "")
                      .Substring(0, 16);
    }

    /// <summary>
    ///     Logs a message if logging is enabled.
    /// </summary>
    /// <param name="message">
    ///     The message to log.
    /// </param>
    private void LogInfoIfEnabled(string message) {
        if (_loggingEnabled) CPH.LogInfo(message);
    }

    /// <summary>
    ///     Logs a warning message.
    /// </summary>
    /// <param name="message">
    ///     The warning message to log.
    /// </param>
    private void LogWarn(string message) {
        if (_loggingEnabled) CPH.LogWarn(message);
    }

    /// <summary>
    ///     Logs an error message.
    /// </summary>
    /// <param name="message">
    ///     The error message to log.
    /// </param>
    private void LogError(string message) {
        CPH.LogError(message);
    }

    private void CreateAndHostClipPage(string clipUrl,
                                       string streamerName,
                                       string clipTitle,
                                       string curatorName,
                                       ClipData clipData) {
        var nonce = GenerateNonce();
        var gameName = Task.Run(() => GetGameName(clipData.GameId ?? "")).Result;
        var htmlContent = GenerateHtmlContent(clipUrl, nonce, streamerName, gameName, clipTitle, curatorName);
        var server = ConfigureAndStartServer();

        Task.Run(async () => await ListenForRequests(server, htmlContent));
    }

    private static string GenerateHtmlContent(string clipUrl,
                                              string nonce,
                                              string streamerName,
                                              string gameName,
                                              string clipTitle,
                                              string curatorName) {
        return HTMLText.Replace("PrettyPlumpPineappleTriHard-ZWylxqlrhU1-fsdD", $"{clipUrl}?nonce={nonce}")
                       .Replace("Name", $"{streamerName}")
                       .Replace("Game", gameName)
                       .Replace("Clip title even if it's really long and stuff", clipTitle)
                       .Replace("ClipCurator", curatorName);
    }

    private HttpListener ConfigureAndStartServer() {
        lock (_serverLock) {
            CleanupServer(); // Ensure any previous instances are cleaned up
            _server = new HttpListener();

            try {
                if (!IsPortAvailable(8080)) throw new InvalidOperationException("Port 8080 is already in use.");

                _server.Prefixes.Add("http://localhost:8080/");
                _server.Start();
            } catch (HttpListenerException ex) {
                CPH.LogError($"Failed to start HttpListener on port 8080: {ex.Message}");
                CleanupServer();

                throw;
            } catch (Exception ex) {
                CPH.LogError($"Unexpected error in ConfigureAndStartServer: {ex.Message}");
                CleanupServer();

                throw;
            }

            return _server;
        }
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

    private async Task ListenForRequests(HttpListener server, string htmlContent) {
        if (_loggingEnabled)
            CPH.LogDebug($"ListenForRequests called with server {server} and htmlContent {htmlContent}.");

        try {
            while (server.IsListening) {
                var context = await server.GetContextAsync();
                var response = context.Response;

                response.Headers.Add("Access-Control-Allow-Origin", "*");
                response.Headers.Add("Access-Control-Allow-Methods", "GET, OPTIONS");
                response.Headers.Add("Access-Control-Allow-Headers", "*");

                if (context.Request.HttpMethod == "OPTIONS") {
                    response.StatusCode = 204;
                    response.Close();

                    continue;
                }

                var output = context.Request.Url.LocalPath switch {
                    "/index.css" => CSSText,
                    "/" => htmlContent,
                    _ => null
                };

                if (output == null) {
                    response.StatusCode = 404;
                    response.Close();

                    continue;
                }

                response.ContentType = context.Request.Url.LocalPath.EndsWith(".css") ? "text/css" : "text/html";

                var buffer = Encoding.UTF8.GetBytes(output);

                response.ContentLength64 = buffer.Length;

                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);

                response.Close();
            }
        } catch (HttpListenerException ex) when (ex.ErrorCode == 995) {
            // Listener closed
            if (_loggingEnabled) CPH.LogInfo("HttpListener was stopped safely.");
        } catch (Exception ex) {
            CPH.LogError($"{GetErrorMessage(nameof(ListenForRequests))}: {ex.Message}");
        }
    }

    private void DisposeServer() {
        try {
            if (_server == null) return;

            _server.Close();
            _server.Abort();

            if (_loggingEnabled) CPH.LogInfo("HttpListener has been disposed.");
        } catch (Exception ex) {
            CPH.LogError($"Error while disposing HttpListener: {ex.Message}");
        } finally {
            _server = null;
        }
    }

    private void StopServer() {
        lock (_serverLock) {
            if (_server == null) return;

            try {
                if (!_server.IsListening) return;

                _server.Stop();

                if (_loggingEnabled) CPH.LogInfo("HttpListener has been stopped.");
            } catch (Exception ex) {
                CPH.LogError($"Unexpected error while stopping the HttpListener: {ex.Message}");
            }
        }
    }

    private void CleanupServer() {
        lock (_serverLock) {
            if (_server == null) return;

            StopServer();
            DisposeServer();
        }
    }

    private async Task<string> GetGameName(string gameId) {
        if (string.IsNullOrEmpty(gameId)) {
            if (_loggingEnabled) CPH.LogWarn("Game ID is empty or null.");

            return "Unknown Game";
        }

        var clientId = CPH.TwitchClientId;
        var clientSecret = CPH.TwitchOAuthToken;

        try {
            using var client = new HttpClient();

            client.DefaultRequestHeaders.Add("Client-ID", clientId);
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {clientSecret}");

            var response = await client.GetAsync($"https://api.twitch.tv/helix/games?id={gameId}");
            var content = await response.Content.ReadAsStringAsync();

            var apiResponse = JsonConvert.DeserializeObject<TwitchApiResponse<GameData>>(content);

            if (apiResponse.Data != null && apiResponse.Data.Any())
                return apiResponse.Data.First().Name ?? "Unknown Game";
        } catch (Exception ex) {
            CPH.LogError($"{GetErrorMessage("GetGameName")}: {ex.Message}");
        }

        return "Unknown Game";
    }

    ~CPHInline() {
        CleanupServer();
    }

    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Local")]
    private class Clip {
        public string Url { get; private set; }
        public string Id { get; set; }
        public string BroadcasterId { get; set; }
        public string CreatorId { get; set; }
        public string CreatorName { get; set; }
        public string VideoId { get; set; }
        public string GameId { get; set; }
        public string Title { get; set; }
        public int ViewCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public double Duration { get; set; }
        public bool IsFeatured { get; set; }
        public string ThumbnailUrl { get; set; }

        public static Clip FromTwitchClip(ClipData twitchClip) {
            return new Clip {
                Url = twitchClip.Url,
                Id = twitchClip.Id,
                BroadcasterId = twitchClip.BroadcasterId,
                CreatorId = twitchClip.CreatorId.ToString(),
                CreatorName = twitchClip.CreatorName,
                VideoId = twitchClip.VideoId,
                GameId = twitchClip.GameId,
                Title = twitchClip.Title,
                ViewCount = twitchClip.ViewCount,
                CreatedAt = twitchClip.CreatedAt,
                Duration = twitchClip.Duration,
                IsFeatured = twitchClip.IsFeatured,
                ThumbnailUrl = twitchClip.ThumbnailUrl
            };
        }
    }

    private class TwitchApiResponse<T>(T[] data) {
        public T[] Data { get; } = data;
    }

    private class GameData {
        [JsonProperty("id")] public string Id { get; set; }

        [JsonProperty("name")] public string Name { get; set; }

        [JsonProperty("box_art_url")] public string BoxArtUrl { get; set; }
    }
}