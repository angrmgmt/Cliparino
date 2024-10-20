using System.Drawing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Streamer.bot.Plugin.Interface.Model;
using Twitch.Common.Models.Api;

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

/// <summary>
///     Provides methods for logging messages with various log levels.
/// </summary>
public interface ILogger {
    void Log(string message, LogLevel level);
}

/// <summary>
///     The canonical primary class used in all Streamer.bot scripts.
///     This instance of the class is used to implement a clip player.
/// </summary>
public class CPHInline {
    /// <summary>
    ///     Gets or sets the current clip.
    /// </summary>
    private ClipData? CurrentClip { get; set; }

    /// <summary>
    ///     Gets or sets the clip player scene's dimensions (height and width).
    /// </summary>
    private (int height, int width) Dimensions { get; set; }

    /// <summary>
    ///     Gets or sets the last played clip.
    /// </summary>
    private ClipData? LastPlayedClip { get; set; }

    /// <summary>
    ///     The canonical Streamer.bot entry function in CPHInline. Executes the main command handler.
    /// </summary>
    /// <returns>
    ///     True if the command was handled successfully; otherwise, false.
    /// </returns>
    public bool Execute() {
        OBSManager.Logger.Log("Execute method called", LogLevel.Info);

        try {
            if (!TryParseArguments(out var mainCommand, out var input0, out var height, out var width)) {
                OBSManager.Logger.Log("Failed to parse arguments", LogLevel.Error);

                return false;
            }

            Dimensions = (height, width);
            OBSManager.VideoDimensions = Dimensions;
            mainCommand = mainCommand.ToLower();
            OBSManager.Init((s, s1) => CPH.ObsSendRaw(s, s1),
                            CPH.Wait,
                            () => CPH.ObsIsConnected(),
                            (s, s1, b) => CPH.ObsSetSourceVisibility(s, s1, b),
                            s => CPH.ObsSetScene(s));

            return HandleCommand(mainCommand, input0);
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Unhandled exception: {ex.Message}", LogLevel.Error);

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
        OBSManager.Logger.Log("CalculateDelay method called", LogLevel.Info);

        try {
            var duration = CurrentClip?.Duration ?? GetMaxClipDuration() * 1000;

            OBSManager.Logger.Log($"Calculated duration: {duration}", LogLevel.Info);

            return 2000 + (int)Math.Round(duration) * 1000;
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in CalculateDelay: {ex.Message}", LogLevel.Error);

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
    ///     aa
    ///     The converted ClipData object.
    /// </returns>
    private ClipData ConvertToClipData(JObject clipJObject) {
        OBSManager.Logger.Log("ConvertToClipData method called", LogLevel.Info);
        OBSManager.Logger.Log("Creating new JObject clipData from clipJObject[\"data\"]", LogLevel.Info);

        try {
            var clipDataToken = clipJObject["data"];

            if (clipDataToken == null) {
                OBSManager.Logger.Log("clipJObject[\"data\"] is null.", LogLevel.Error);

                return new ClipData();
            }

            OBSManager.Logger.Log("Creating new ClipData object clipData from JToken? clipDataToken", LogLevel.Info);

            var clipData = ExtractClipData(clipDataToken);

            return CreateClipData(clipData);
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception occurred in ConvertToClipData: {ex.Message}", LogLevel.Error);

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
        OBSManager.Logger.Log("CreateClipData method called", LogLevel.Info);

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
            OBSManager.Logger.Log($"Exception in CreateClipData: {ex.Message}", LogLevel.Error);

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
        OBSManager.Logger.Log("ExtractClipData method called", LogLevel.Info);

        try {
            var result = clipDataToken switch {
                JArray { Count: > 0 } clipDataArray => clipDataArray[0] as JObject,
                JObject clipDataObject => clipDataObject,
                _ => null
            };

            if (result is null) {
                OBSManager.Logger.Log("clipDataToken did not match any expected types.", LogLevel.Warn);
            }

            return result ?? new JObject();
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in ExtractClipData: {ex.Message}", LogLevel.Error);

            return new JObject();
        }
    }

    /// <summary>
    ///     Finds a specific clip using the provided API URL.
    /// </summary>
    /// <param name="apiURL">
    ///     The API URL to fetch the clip data from.
    /// </param>
    /// <returns>
    ///     The found ClipData object.
    /// </returns>
    private ClipData FindSpecificClip(string apiURL) {
        OBSManager.Logger.Log("FindSpecificClip method called", LogLevel.Info);

        try {
            var clip = GetClip(apiURL);

            return clip;
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in FindSpecificClip: {ex.Message}", LogLevel.Error);

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
        OBSManager.Logger.Log("GenerateClipEmbedUrl method called", LogLevel.Info);

        try {
            var embedUrl = CurrentClip != null ? CurrentClip.EmbedUrl : LastPlayedClip?.EmbedUrl;

            if (!string.IsNullOrEmpty(embedUrl)) {
                return $"{embedUrl}{GenerateQueryParams()}";
            }

            OBSManager.Logger.Log("Embed URL is empty.", LogLevel.Error);
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in GenerateClipEmbedUrl: {ex.Message}", LogLevel.Error);
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
        OBSManager.Logger.Log("GenerateHelixURL method called", LogLevel.Info);

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
                OBSManager.Logger.Log("Invalid clip URL format.", LogLevel.Error);

                return string.Empty;
            }

            var helixURL = $"https://api.twitch.tv/helix/clips?id={clipID}";
            var message = $"Helix URL generated as: {helixURL}";

            OBSManager.Logger.Log(message, LogLevel.Info);

            return helixURL;
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in GenerateHelixURL: {ex.Message}", LogLevel.Error);

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
        OBSManager.Logger.Log("GenerateQueryParams method called", LogLevel.Info);

        try {
            var queryParams =
                $"?allowfullscreen&autoplay=true&controls=false&height={Dimensions.height}&mute=false&parent=localhost&preload&width={Dimensions.width}";
            OBSManager.Logger.Log($"Generated query parameters: {queryParams}", LogLevel.Info);

            return queryParams;
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in GenerateQueryParams: {ex.Message}", LogLevel.Error);

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
        OBSManager.Logger.Log("GetAndPlayClipForChannel method called", LogLevel.Info);

        try {
            var clips = GetClips(userInfo.UserId, userInfo.UserName);

            OBSManager.Logger.Log($"Fetched {clips.Count} clips for user {userInfo.UserName}", LogLevel.Info);

            if (CPH.TryGetArg("featuredOnly", out bool featuredOnly) && featuredOnly) {
                OBSManager.Logger.Log("Filtering clips to include only featured ones", LogLevel.Info);
                clips = clips.Where(clip => clip.IsFeatured).ToList();
            }

            if (clips.Count != 0) {
                OBSManager.Logger.Log("Playing a random clip", LogLevel.Info);
                PlayRandomClip(clips);

                return true;
            }

            OBSManager.Logger.Log("No clips found to play", LogLevel.Warn);
            CPH.SendMessage($"Well, looks like @{userInfo.UserName} doesn't have any clips... yet. Let's love on them a bit and maybe even tickle that follow anyway!! https://twitch.tv/{userInfo.UserName}");

            return false;
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in GetAndPlayClipForChannel: {ex.Message}", LogLevel.Error);

            return false;
        }
    }

    /// <summary>
    ///     Fetches a clip from the provided API URL.
    /// </summary>
    /// <param name="apiUrl">
    ///     The API URL to fetch the clip data from.
    /// </param>
    /// <returns>
    ///     The fetched ClipData object.
    /// </returns>
    private ClipData GetClip(string apiUrl) {
        OBSManager.Logger.Log("GetClip method called", LogLevel.Info);

        var clientId = CPH.TwitchClientId;
        var accessToken = CPH.TwitchOAuthToken;

        try {
            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);

            request.Headers.Add("Client-Id", clientId);
            request.Headers.Add("Authorization", $"Bearer {accessToken}");
            OBSManager.Logger.Log($"Making HTTP request with API URL {apiUrl}", LogLevel.Info);
            OBSManager.Logger.Log($"Headers for request: {request.Headers}", LogLevel.Info);

            var response = client.SendAsync(request).Result;

            if (!response.IsSuccessStatusCode) {
                LogClipRequestError(response);

                return new ClipData();
            }

            OBSManager.Logger.Log("CLIPS Request successful.", LogLevel.Info);
            OBSManager.Logger.Log($"Response - Status Code: {response.StatusCode.ToString()}", LogLevel.Info);
            OBSManager.Logger.Log($"Response - Reason Phrase: {response.ReasonPhrase}", LogLevel.Info);
            OBSManager.Logger.Log($"Response - Headers: {response.Headers}", LogLevel.Info);

            var responseBody = response.Content.ReadAsStringAsync().Result;

            OBSManager.Logger.Log($"Response - Body: {responseBody}", LogLevel.Info);

            var clip = ConvertToClipData(ParseClips(responseBody));

            LogClipData(clip);
            CurrentClip = clip;

            return clip;
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in GetClip: {ex.Message}", LogLevel.Error);

            return new ClipData();
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
    private List<ClipData> GetClips(string? userId, string? userName) {
        OBSManager.Logger.Log("GetClips method called", LogLevel.Info);

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

                if (clips.Count > 0) {
                    return clips;
                }

                LogNoClipsMessage(userName, timeSpan);
            }
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in GetClips: {ex.Message}", LogLevel.Error);
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
        OBSManager.Logger.Log("GetMaxClipDuration method called", LogLevel.Info);

        try {
            return CPH.TryGetArg("maxClipSeconds", out string? maxClipDurationStr)
                       ? int.Parse(maxClipDurationStr ?? "30")
                       : 30;
        } catch (FormatException ex) {
            OBSManager.Logger.Log($"Failed to parse maxClipSeconds argument: {ex.Message}", LogLevel.Error);

            return 30;
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Unexpected error in GetMaxClipDuration: {ex.Message}", LogLevel.Error);

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
        OBSManager.Logger.Log("GetMostRecentClipUrlFromChat method called", LogLevel.Info);

        try {
            var clipUrl = CPH.GetGlobalVar<string>("last_clip_url");

            if (!string.IsNullOrEmpty(clipUrl)) {
                return clipUrl;
            }

            OBSManager.Logger.Log("No clip URL found in global variables.", LogLevel.Warn);

            return string.Empty;
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in GetMostRecentClipUrlFromChat: {ex.Message}", LogLevel.Error);

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
        OBSManager.Logger.Log("HandleCommand method called", LogLevel.Info);

        try {
            var commandHandlers = new Dictionary<string, Func<string, bool>>(StringComparer.OrdinalIgnoreCase) {
                { "!so", HandleShoutoutCommand },
                { "!watch", HandleWatchCommand },
                { "!stop", _ => HandleStopCommand() },
                { "!replay", _ => HandleReplayCommand() }
            };

            if (commandHandlers.TryGetValue(command, out var handler)) {
                return handler(input);
            }

            OBSManager.Logger.Log($"Unknown command: {command}", LogLevel.Warn);

            return false;
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in HandleCommand: {ex.Message}", LogLevel.Error);

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
        OBSManager.Logger.Log("HandleReplayCommand method called", LogLevel.Info);

        try {
            return ReplayLastClip();
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in HandleReplayCommand: {ex.Message}", LogLevel.Error);

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
        OBSManager.Logger.Log("HandleShoutoutCommand method called", LogLevel.Info);

        try {
            if (string.IsNullOrEmpty(channelName)) {
                OBSManager.Logger.Log("Channel name is empty or null.", LogLevel.Error);

                return false;
            }

            if (!TryGetUserInfo(channelName, out var userInfo)) {
                OBSManager.Logger.Log($"Failed to get user info for channel: {channelName}", LogLevel.Error);

                return false;
            }

            var message = PrepareMessage(userInfo);

            OBSManager.Logger.Log($"Shoutout message prepared: {message}", LogLevel.Info);
            CPH.SendMessage(message);

            return GetAndPlayClipForChannel(userInfo);
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in HandleShoutoutCommand: {ex.Message}", LogLevel.Error);

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
        OBSManager.Logger.Log("HandleStopCommand method called", LogLevel.Info);

        try {
            return StopClipPlayer();
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in HandleStopCommand: {ex.Message}", LogLevel.Error);

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
        OBSManager.Logger.Log("HandleWatchCommand method called", LogLevel.Info);

        try {
            if (string.IsNullOrEmpty(urlOrEmpty)) {
                var recentClipUrl = GetMostRecentClipUrlFromChat();

                if (string.IsNullOrEmpty(recentClipUrl)) {
                    OBSManager.Logger.Log("No recent clip URL found in chat.", LogLevel.Warn);

                    return false;
                }

                PlaySpecificClip(recentClipUrl);
            } else {
                PlaySpecificClip(urlOrEmpty);
            }

            return true;
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in HandleWatchCommand: {ex.Message}", LogLevel.Error);

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
        OBSManager.Logger.Log("LogClipData method called", LogLevel.Info);
        OBSManager.Logger.Log(clipData.ToString() ?? string.Empty, LogLevel.Info);
        OBSManager.Logger.Log($"Id = {clipData.Id}", LogLevel.Info);
        OBSManager.Logger.Log($"Url = {clipData.Url}", LogLevel.Info);
        OBSManager.Logger.Log($"EmbedUrl = {clipData.EmbedUrl}", LogLevel.Info);
        OBSManager.Logger.Log($"BroadcasterId = {clipData.BroadcasterId}", LogLevel.Info);
        OBSManager.Logger.Log($"BroadcasterName = {clipData.BroadcasterName}", LogLevel.Info);
        OBSManager.Logger.Log($"CreatorId = {clipData.CreatorId}", LogLevel.Info);
        OBSManager.Logger.Log($"CreatorName = {clipData.CreatorName}", LogLevel.Info);
        OBSManager.Logger.Log($"VideoId = {clipData.VideoId}", LogLevel.Info);
        OBSManager.Logger.Log($"GameId = {clipData.GameId}", LogLevel.Info);
        OBSManager.Logger.Log($"Language = {clipData.Language}", LogLevel.Info);
        OBSManager.Logger.Log($"Title = {clipData.Title}", LogLevel.Info);
        OBSManager.Logger.Log($"ViewCount = {clipData.ViewCount}", LogLevel.Info);
        OBSManager.Logger.Log($"CreatedAt = {clipData.CreatedAt}", LogLevel.Info);
        OBSManager.Logger.Log($"ThumbnailUrl = {clipData.ThumbnailUrl}", LogLevel.Info);
        OBSManager.Logger.Log($"Duration = {clipData.Duration}", LogLevel.Info);
        OBSManager.Logger.Log($"IsFeatured = {clipData.IsFeatured}", LogLevel.Info);
    }

    /// <summary>
    ///     Logs an error that occurred during a clip request.
    /// </summary>
    /// <param name="response">
    ///     The HTTP response that contains the error.
    /// </param>
    private void LogClipRequestError(HttpResponseMessage response) {
        try {
            OBSManager.Logger.Log("LogClipRequestError method called", LogLevel.Info);
            OBSManager.Logger.Log($"CLIPS Request failed with status code: {response.StatusCode}", LogLevel.Error);

            var errorMessage = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            OBSManager.Logger.Log($"CLIPS Error Message: {errorMessage}", LogLevel.Error);
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception occurred while logging clip request error: {ex.Message}", LogLevel.Error);
        }
    }

    /// <summary>
    ///     Logs a message indicating that a user has no clips based on the chosen period of time and
    ///     whether clips are to be limited to only featured clips.
    /// </summary>
    /// <param name="userName">
    ///     The name of the user/channel for whom a clip is to be shown.
    /// </param>
    /// <param name="timeSpan">
    ///     The range of creation dates over which clips are to be selected.
    /// </param>
    private void LogNoClipsMessage(string? userName, TimeSpan timeSpan) {
        OBSManager.Logger.Log("LogNoClipsMessage method called", LogLevel.Info);

        try {
            var message = timeSpan == TimeSpan.Zero
                              ? $"{userName} has no featured clips, pulling from last 24 hours."
                              : $"{userName} has no clips from the last {timeSpan.TotalDays} days, pulling from next period.";

            OBSManager.Logger.Log($"Message: {message}", LogLevel.Info);
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in LogNoClipsMessage: {ex.Message}", LogLevel.Error);
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
        OBSManager.Logger.Log("ParseClips method called", LogLevel.Info);

        try {
            return JObject.Parse(clipDataText);
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Error parsing clip data: {ex.Message}", LogLevel.Error);

            return new JObject();
        }
    }

    /// <summary>
    ///     Plays the given clip by setting the OBS browser source to the clip's embed URL, showing the
    ///     source, waiting for the clip's duration plus a buffer, and then hiding the source.
    /// </summary>
    /// <param name="clip">
    ///     The clip data for the clip to be played.
    /// </param>
    private void PlayClip(ClipData clip) {
        OBSManager.Logger.Log("PlayClip method called", LogLevel.Info);

        try {
            const string clipInfoText = "Clip Info";
            var embedUrl = GenerateClipEmbedUrl();
            var delay = CalculateDelay();

            if (delay == -1) {
                OBSManager.Logger.Log("Error calculating delay for clip playback", LogLevel.Error);

                return;
            }

            OBSManager.ManageClipPlayerContent(clip,
                                               clipInfoText,
                                               embedUrl,
                                               delay,
                                               (s, s1) => CPH.ObsSendRaw(s, s1),
                                               CPH.Wait,
                                               (s, s1, arg3) => CPH.ObsSetBrowserSource(s, s1, arg3),
                                               (s, s1) => CPH.ObsShowSource(s, s1),
                                               (s, s1) => CPH.ObsHideSource(s, s1));
            LastPlayedClip = clip;
            OBSManager.Logger.Log("Clip played successfully", LogLevel.Info);
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in PlayClip: {ex.Message}", LogLevel.Error);
        }
    }

    /// <summary>
    ///     Plays a random clip from the provided list of clips.
    /// </summary>
    /// <param name="clips">
    ///     A list of clip data from which a random clip will be selected and played.
    /// </param>
    private void PlayRandomClip(List<ClipData> clips) {
        OBSManager.Logger.Log("PlayRandomClip method called", LogLevel.Info);

        try {
            var random = new Random();
            var clip = clips[random.Next(clips.Count)];

            CurrentClip = clip;
            PlayClip(clip);
            OBSManager.Logger.Log("Random clip played successfully", LogLevel.Info);
        } catch (Exception ex) {
            OBSManager.Logger.Log($"An error occurred while playing a random clip: {ex.Message}", LogLevel.Error);
        }
    }

    /// <summary>
    ///     Plays a specific clip by its URL. This method generates the Twitch Helix API URL from the
    ///     provided clip URL, finds the specific clip data, and then plays the clip using the OBS
    ///     browser source.
    /// </summary>
    /// <param name="clipURL">
    ///     The URL of the clip to be played.
    /// </param>
    private void PlaySpecificClip(string clipURL) {
        OBSManager.Logger.Log("PlaySpecificClip method called", LogLevel.Info);

        try {
            var helixURL = GenerateHelixURL(clipURL);
            var specificClip = FindSpecificClip(helixURL);

            CurrentClip = specificClip;
            PlayClip(specificClip);
            LastPlayedClip = specificClip;
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in PlaySpecificClip: {ex.Message}", LogLevel.Error);
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
        OBSManager.Logger.Log("PrepareMessage method called", LogLevel.Info);

        try {
            if (!CPH.TryGetArg("message", out string message)) {
                OBSManager.Logger.Log("Failed to retrieve 'message' argument.", LogLevel.Warn);
                message = "Default message";
            }

            var preparedMessage = message.Replace("%userName%", userInfo.UserName).Replace("%userGame%", userInfo.Game);

            OBSManager.Logger.Log($"Message prepared: {preparedMessage}", LogLevel.Info);

            return preparedMessage;
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in PrepareMessage: {ex.Message}", LogLevel.Error);

            return string.Empty;
        }
    }

    /// <summary>
    ///     Replays the last played clip by setting the OBS browser source to the clip's embed URL,
    ///     showing the source, waiting for the clip's duration plus a buffer, and then hiding the source.
    /// </summary>
    /// <returns>
    ///     True if the last played clip was successfully replayed; otherwise, false.
    /// </returns>
    private bool ReplayLastClip() {
        OBSManager.Logger.Log("ReplayLastClip method called", LogLevel.Info);

        try {
            if (LastPlayedClip == null) {
                OBSManager.Logger.Log("No last played clip to replay.", LogLevel.Warn);

                return false;
            }

            CurrentClip = LastPlayedClip;
            PlayClip(LastPlayedClip);

            return true;
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in ReplayLastClip: {ex.Message}", LogLevel.Error);

            return false;
        }
    }

    /// <summary>
    ///     Stops the clip player by hiding the source in the current OBS scene and setting the browser
    ///     source to a blank page.
    /// </summary>
    /// <returns>
    ///     Returns <c>true</c> if the clip player was successfully stopped.
    /// </returns>
    private bool StopClipPlayer() {
        OBSManager.Logger.Log("StopClipPlayer method called", LogLevel.Info);

        try {
            CPH.ObsHideSource(CPH.ObsGetCurrentScene(), OBSManager.SceneName);
            CPH.ObsSetBrowserSource(OBSManager.SceneName, OBSManager.PlayerSourceName, "about:blank");
            OBSManager.Logger.Log("Clip player stopped.", LogLevel.Info);

            return true;
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Error stopping clip player: {ex.Message}", LogLevel.Error);

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
    ///     When this method returns, contains the user information associated with the specified target
    ///     user, if the user exists and the information is available; otherwise, the default value for
    ///     the type of the userInfo parameter. This parameter is passed uninitialized.
    /// </param>
    /// <returns>
    ///     <c>true</c> if the user information was successfully retrieved; otherwise, <c>false</c>.
    /// </returns>
    private bool TryGetUserInfo(string targetUser, out TwitchUserInfoEx userInfo) {
        OBSManager.Logger.Log("TryGetUserInfo method called", LogLevel.Info);

        try {
            userInfo = CPH.TwitchGetExtendedUserInfoByLogin(targetUser);

            return true;
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in TryGetUserInfo: {ex.Message}", LogLevel.Error);
            userInfo = new TwitchUserInfoEx();

            return false;
        }
    }

    /// <summary>
    ///     Attempts to parse the arguments required for executing a command.
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
        OBSManager.Logger.Log("TryParseArguments method called", LogLevel.Info);
        mainCommand = string.Empty;
        input0 = string.Empty;
        height = 0;
        width = 0;

        try {
            return CPH.TryGetArg("command", out mainCommand)
                   && CPH.TryGetArg("input0", out input0)
                   && CPH.TryGetArg("height", out height)
                   && CPH.TryGetArg("width", out width);
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in TryParseArguments: {ex.Message}", LogLevel.Error);
            mainCommand = string.Empty;
            input0 = string.Empty;
            height = 0;
            width = 0;

            return false;
        }
    }

    #region Pinhole Functions

    /// <summary>
    ///     Logs an error message to the Streamer.bot logging system.
    /// </summary>
    /// <param name="message">The error message to log.</param>
    public void LogError(string message) {
        OBSManager.Logger.Log("LogError method called", LogLevel.Info);

        try {
            CPH.LogError(message);
        } catch (Exception ex) {
            // This may not work... but we can try.
            OBSManager.Logger.Log($"Exception in LogError: {ex.Message}", LogLevel.Error);
        }
    }

    /// <summary>
    ///     Logs an informational message.
    /// </summary>
    /// <param name="message">The message to log.</param>
    public void LogInfo(string message) {
        OBSManager.Logger.Log("LogInfo method called", LogLevel.Info);

        try {
            CPH.LogInfo(message);
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in LogInfo: {ex.Message}", LogLevel.Error);
        }
    }

    /// <summary>
    ///     Logs a warning message to the Streamer.bot logging system.
    /// </summary>
    /// <param name="message">The warning message to log.</param>
    public void LogWarn(string message) {
        OBSManager.Logger.Log("LogWarn method called", LogLevel.Info);

        try {
            CPH.LogWarn(message);
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in LogWarn: {ex.Message}", LogLevel.Error);
        }
    }

    /// <summary>
    ///     Attempts to retrieve the value of an argument by its name.
    /// </summary>
    /// <typeparam name="T">The type of the argument to retrieve.</typeparam>
    /// <param name="argName">The name of the argument to retrieve.</param>
    /// <param name="value">
    ///     When this method returns, contains the value of the argument if the argument is found;
    ///     otherwise, the default value for the type of the <paramref name="value" /> parameter.
    ///     This parameter is passed uninitialized.
    /// </param>
    /// <returns>
    ///     <c>true</c> if the argument is found and successfully retrieved; otherwise, <c>false</c>.
    /// </returns>
    public bool TryGetArg<T>(string argName, out T? value) {
        OBSManager.Logger.Log($"TryGetArg method called with argument name: {argName}", LogLevel.Info);

        try {
            return CPH.TryGetArg(argName, out value);
        } catch (Exception ex) {
            OBSManager.Logger.Log($"Exception in TryGetArg method: {ex.Message}", LogLevel.Error);
            value = default;

            return false;
        }
    }

    #endregion Pinhole Functions
}

/// <summary>
///     Provides functionality to manage OBS (Open Broadcaster Software) operations, specifically tailored for
///     interoperability with Streamer.bot to implement a clip player.
/// </summary>
public static class OBSManager {
    /// <summary>
    ///     Gets the name of the clip player source.
    /// </summary>
    public const string PlayerSourceName = "Player";

    /// <summary>
    ///     Gets the name of the clip player scene.
    /// </summary>
    public const string SceneName = "Cliparino";

    /// <summary>
    ///     Gets the logger instance used for logging messages within the OBSManager.
    /// </summary>
    /// <value>
    ///     An instance of <see cref="ILogger" /> used for logging.
    /// </value>
    public static ILogger Logger { get; } = DefaultLogger.CreateInstance();

    /// <summary>
    ///     Gets or sets the dimensions of the video, represented as a tuple containing height and width.
    /// </summary>
    /// <value>
    ///     A tuple where the first item is the height and the second item is the width of the video.
    /// </value>
    public static (int height, int width) VideoDimensions { get; set; }

    /// <summary>
    ///     Gets the name of the channel name text source.
    /// </summary>
    private static string ChannelName { get; set; } = "";

    /// <summary>
    ///     Gets the name of the clip creator name text source.
    /// </summary>
    private static string ClipCreator { get; set; } = "";

    /// <summary>
    ///     Gets the name of the clip title text source.
    /// </summary>
    private static string ClipTitle { get; set; } = "";

    /// <summary>
    ///     Initializes the OBS Manager by managing the OBS clip player scene.
    /// </summary>
    /// <param name="obsSendRaw">Function to send raw OBS commands.</param>
    /// <param name="wait">Action to wait for a specified duration.</param>
    /// <param name="obsIsConnected">Function to check if OBS is connected.</param>
    /// <param name="obsSetSourceVisibility">Action to set the visibility of a source in OBS.</param>
    /// <param name="obsSetScene">Action to set the current scene in OBS.</param>
    public static void Init(Func<string, string, string> obsSendRaw,
                            Action<int> wait,
                            Func<bool> obsIsConnected,
                            Action<string, string, bool> obsSetSourceVisibility,
                            Action<string> obsSetScene) {
        Logger.Log("Init method called", LogLevel.Info);

        try {
            ManageOBS(obsSendRaw,
                      wait,
                      obsIsConnected,
                      obsSetSourceVisibility,
                      obsSetScene,
                      SceneName,
                      PlayerSourceName,
                      true);
        } catch (Exception ex) {
            Logger.Log($"Exception in Init method: {ex.Message}", LogLevel.Error);
        }
    }

    /// <summary>
    ///     Manages the content of the clip player by setting clip metadata, updating the clip info text source,
    ///     playing the clip, and resetting the clip player.
    /// </summary>
    /// <param name="clip">The clip data to be played.</param>
    /// <param name="clipInfoText">The text information about the clip.</param>
    /// <param name="embedUrl">The URL to embed the clip.</param>
    /// <param name="delay">The delay before playing the clip.</param>
    /// <param name="obsSendRaw">Function to send raw OBS commands.</param>
    /// <param name="wait">Action to wait for a specified duration.</param>
    /// <param name="obsSetBrowserSource">Action to set the browser source in OBS.</param>
    /// <param name="obsShowSource">Action to show a source in OBS.</param>
    /// <param name="obsHideSource">Action to hide a source in OBS.</param>
    public static void ManageClipPlayerContent(ClipData clip,
                                               string clipInfoText,
                                               string embedUrl,
                                               int delay,
                                               Func<string, string, string> obsSendRaw,
                                               Action<int> wait,
                                               Action<string, string, string> obsSetBrowserSource,
                                               Action<string, string> obsShowSource,
                                               Action<string, string> obsHideSource) {
        Logger.Log("ManageClipPlayerContent method called", LogLevel.Info);

        try {
            SetClipMetadata(clip);
            UpdateClipInfoTextSource(clipInfoText, obsSendRaw, wait);
            PlayClip(embedUrl, clipInfoText, delay, wait, obsSetBrowserSource, obsShowSource);
            ResetClipPlayer(clipInfoText, obsSetBrowserSource, obsHideSource);
        } catch (Exception ex) {
            Logger.Log($"Exception in ManageClipPlayerContent: {ex.Message}", LogLevel.Error);
        }
    }

    /// <summary>
    ///     Creates a new scene in OBS.
    /// </summary>
    /// <param name="sceneName">
    ///     The name of the scene to create.
    /// </param>
    /// <param name="obsSendRaw">
    ///     A function that sends a raw request to OBS.
    /// </param>
    private static void CreateScene(string sceneName, Func<string, string, string> obsSendRaw) {
        Logger.Log("CreateScene method called", LogLevel.Info);

        try {
            Logger.Log($"Scene '{sceneName}' not found. Creating it...", LogLevel.Info);
            obsSendRaw("CreateScene", $"{{\"sceneName\":\"{sceneName}\"}}");
        } catch (Exception ex) {
            Logger.Log($"Exception in CreateScene: '{sceneName}': {ex.Message}", LogLevel.Error);
        }
    }

    /// <summary>
    ///     Gets the dimensions of the video, including height and width.
    /// </summary>
    /// <returns>
    ///     A tuple containing the height and width of the video.
    /// </returns>
    private static (int height, int width) GetVideoDimensions() {
        Logger.Log("GetVideoDimensions method called", LogLevel.Info);

        try {
            return (VideoDimensions.height, VideoDimensions.width);
        } catch (Exception ex) {
            Logger.Log($"Exception in GetVideoDimensions: {ex.Message}", LogLevel.Error);

            return (0, 0);
        }
    }

    /// <summary>
    ///     Manages the OBS clip player scene and sources.
    /// </summary>
    /// <param name="obsSendRaw">A function that sends raw commands to OBS.</param>
    /// <param name="wait">An action that waits for a specified amount of time.</param>
    /// <param name="obsIsConnected">A function that checks if OBS is connected.</param>
    /// <param name="obsSetSourceVisibility">An action that sets the visibility of a source in a scene.</param>
    /// <param name="obsSetScene">An action that sets the current scene in OBS.</param>
    /// <param name="sceneName">The name of the scene to manage.</param>
    /// <param name="sourceName">The optional name of the source within the given scene to manage.</param>
    /// <param name="visible">An optional value that determines whether the source is visible.</param>
    /// <param name="createIfNotExist">
    ///     An optional value that determines whether the scene or source should be created if it
    ///     doesn't exist.
    /// </param>
    private static void ManageOBS(Func<string, string, string> obsSendRaw,
                                  Action<int> wait,
                                  Func<bool> obsIsConnected,
                                  Action<string, string, bool> obsSetSourceVisibility,
                                  Action<string> obsSetScene,
                                  string sceneName,
                                  string? sourceName = null,
                                  bool? visible = null,
                                  bool createIfNotExist = true) {
        Logger.Log("ManageOBS method called", LogLevel.Info);

        try {
            if (!obsIsConnected()) {
                Logger.Log("OBS is not connected", LogLevel.Error);

                return;
            }

            if (!SceneExists(sceneName, obsSendRaw)) {
                if (createIfNotExist) {
                    CreateScene(sceneName, obsSendRaw);
                    Logger.Log($"Scene '{sceneName}' created", LogLevel.Info);
                } else {
                    Logger.Log($"Scene '{sceneName}' does not exist", LogLevel.Warn);

                    return;
                }
            }

            if (sourceName != null) {
                if (!SourceExistsInScene(sceneName, sourceName, obsSendRaw)) {
                    Logger.Log($"Source '{sourceName}' does not exist in scene '{sceneName}', creating.",
                               LogLevel.Info);
                    ManagePlayerSourceInScene(sceneName, sourceName, obsSendRaw, wait);

                    return;
                }

                if (!visible.HasValue) {
                    return;
                }

                obsSetSourceVisibility(sceneName, sourceName, visible.Value);
                Logger.Log($"Source '{sourceName}' visibility set to '{visible}' in scene '{sceneName}'",
                           LogLevel.Info);
            } else {
                obsSetScene(sceneName);
                Logger.Log($"Scene set to '{sceneName}'", LogLevel.Info);
            }
        } catch (Exception ex) {
            Logger.Log($"Exception in ManageOBS: {ex.Message}", LogLevel.Error);
        }
    }

    /// <summary>
    ///     Manages the player source in a specified scene in OBS.
    /// </summary>
    /// <param name="sceneName">
    ///     The name of the scene where the source will be managed.
    /// </param>
    /// <param name="sourceName">
    ///     The name of the source to be managed.
    /// </param>
    /// <param name="obsSendRaw">
    ///     A function to send raw commands to OBS.
    /// </param>
    /// <param name="wait">
    ///     An action to wait for a specified duration.
    /// </param>
    /// <remarks>
    ///     Sets browser source managed volume to 1/2 max SPL (-6 dB) and reroutes audio to monitoring.
    /// </remarks>
    private static void ManagePlayerSourceInScene(string sceneName,
                                                  string sourceName,
                                                  Func<string, string, string> obsSendRaw,
                                                  Action<int> wait) {
        Logger.Log("ManagePlayerSourceInScene method called", LogLevel.Info);

        try {
            const string baseURL = "https://clips.twitch.tv/embed";
            const string urlQuery =
                "clip=CleanShyOrangeNinjaGrumpy-mwaI9sX_sWaq05Zi&parent=localhost&autoplay=true&mute=false";
            const string url = $"{baseURL}?{urlQuery}";
            var inputSettings = new {
                css =
                    "body { background-color: rgba(0, 0, 0, 0); margin: 0px auto; overflow: hidden; } .video-player__overlay, .tw-control-bar { display: none !important; }",
                GetVideoDimensions().height,
                reroute_audio = true,
                restart_when_active = true,
                url,
                GetVideoDimensions().width
            };
            var createInputData = new {
                sceneName,
                inputName = sourceName,
                inputKind = "browser_source",
                inputSettings,
                sceneItemEnabled = true
            };
            var setInputAudioMonitorTypeData = new {
                inputName = sourceName, monitorType = "OBS_MONITORING_TYPE_MONITOR_AND_OUTPUT"
            };
            var setInputVolumeData = new { inputName = sourceName, inputVolumeDb = -6 };

            obsSendRaw("CreateInput", JsonConvert.SerializeObject(createInputData));
            wait(200);
            obsSendRaw("SetInputAudioMonitorType", JsonConvert.SerializeObject(setInputAudioMonitorTypeData));
            wait(200);
            obsSendRaw("SetInputVolume", JsonConvert.SerializeObject(setInputVolumeData));
            Logger.Log($"Browser source '{sourceName}' created in '{sceneName}' scene.", LogLevel.Info);
        } catch (Exception ex) {
            Logger.Log($"Exception in ManagePlayerSourceInScene: {ex.Message}", LogLevel.Error);
        }
    }

    /// <summary>
    ///     Manages a text source in the specified OBS scene by creating or updating it with the given settings.
    /// </summary>
    /// <param name="sceneName">
    ///     The name of the scene where the text source will be managed.
    /// </param>
    /// <param name="sourceName">
    ///     The name of the text source to be created or updated.
    /// </param>
    /// <param name="obsSendRaw">
    ///     A function delegate to send raw OBS commands.
    /// </param>
    /// <param name="wait">
    ///     An action delegate to wait for a specified duration.
    /// </param>
    private static void ManageTextSourceInScene(string sceneName,
                                                string sourceName,
                                                Func<string, string, string> obsSendRaw,
                                                Action<int> wait) {
        Logger.Log("ManageTextSourceInScene method called", LogLevel.Info);

        try {
            var textColor = ColorTranslator.FromHtml("#0071C5");
            var backColor = ColorTranslator.FromHtml("#C55400");
            var outlineColor = Color.FromName("White");
            var inputSettings = new {
                align = "left",
                antialiasing = true,
                bk_color = ColorTranslator.ToWin32(backColor),
                bk_opacity = 10,
                chatlog = false,
                chatlog_lines = 6,
                color = textColor,
                extents = true,
                extents_cx = 384,
                extents_cy = 216,
                extents_wrap = false,
                font = new { face = "Open Sans", flags = "", size = 40, Style = "Regular" },
                gradient = false,
                gradient_color = 0,
                gradient_dir = 0,
                gradient_opacity = 0,
                opacity = 100,
                outline = true,
                outline_color = outlineColor,
                outline_opacity = 100,
                outline_size = 2,
                text = $"{ChannelName}\n{ClipTitle}\nby {ClipCreator}",
                transform = 3,
                valign = "center"
            };
            var createInputData = new {
                sceneName,
                inputName = sourceName,
                inputKind = "text_gdiplus_v3",
                inputSettings,
                sceneItemEnabled = true
            };
            var inputTransform = new {
                alignment = 1,
                boundsAlignment = 0,
                boundsHeight = 216,
                boundsType = "OBS_BOUNDS_NONE",
                boundsWidth = 384,
                cropBottom = 0,
                cropLeft = 0,
                cropRight = 0,
                cropToBounds = false,
                cropTop = 0,
                height = 216,
                positionX = 25,
                positionY = 947,
                rotation = 0,
                scaleX = 1,
                scaleY = 1,
                sourceHeight = 216,
                sourceWidth = 384,
                width = 384
            };

            if (!SourceExistsInScene(sceneName, sourceName, obsSendRaw)) {
                Logger.Log($"Object createInputData constructed:{Environment.NewLine}{JsonConvert.SerializeObject(inputSettings)}",
                           LogLevel.Info);
                obsSendRaw("CreateInput", JsonConvert.SerializeObject(createInputData));
                Logger.Log($"Text source '{sourceName}' created in scene: '{sceneName}'.", LogLevel.Info);
            } else {
                Logger.Log($"Text source '{sourceName}' already exists in scene: {sceneName}.", LogLevel.Info);
                obsSendRaw("SetInputSettings", JsonConvert.SerializeObject(inputSettings));
            }

            var itemID = obsSendRaw("GetSceneItemId",
                                    JsonConvert.SerializeObject(new { sceneName, sourceName, searchOffset = 0 }));
            var itemIndexData = new { sceneName, sceneItemId = itemID, sceneItemIndex = 1 };

            wait(200);
            obsSendRaw("SetSceneItemTransform", JsonConvert.SerializeObject(inputTransform));
            obsSendRaw("SetSceneItemIndex", JsonConvert.SerializeObject(itemIndexData));
        } catch (Exception ex) {
            Logger.Log($"Exception in ManageTextSourceInScene: {ex.Message}", LogLevel.Error);
        }
    }

    /// <summary>
    ///     Plays a clip in the OBS player.
    /// </summary>
    /// <param name="embedUrl">The URL of the clip to be played.</param>
    /// <param name="clipInfoText">The text information about the clip.</param>
    /// <param name="delay">The delay in milliseconds before the clip starts playing.</param>
    /// <param name="wait">An action that pauses execution for a specified duration.</param>
    /// <param name="obsSetBrowserSource">An action that sets the browser source in OBS.</param>
    /// <param name="obsShowSource">An action that shows a source in OBS.</param>
    private static void PlayClip(string embedUrl,
                                 string clipInfoText,
                                 int delay,
                                 Action<int> wait,
                                 Action<string, string, string> obsSetBrowserSource,
                                 Action<string, string> obsShowSource) {
        Logger.Log("PlayClip method called", LogLevel.Info);

        try {
            Logger.Log("Setting browser source to blank", LogLevel.Info);
            obsSetBrowserSource(SceneName, PlayerSourceName, "about:blank");
            wait(500);
            Logger.Log($"Setting browser source to {embedUrl}", LogLevel.Info);
            obsSetBrowserSource(SceneName, PlayerSourceName, embedUrl);
            Logger.Log("Showing player source", LogLevel.Info);
            obsShowSource(SceneName, PlayerSourceName);
            Logger.Log($"Showing clip info text: {clipInfoText}", LogLevel.Info);
            obsShowSource(SceneName, clipInfoText);
            Logger.Log($"Waiting for {delay} milliseconds", LogLevel.Info);
            wait(delay);
        } catch (Exception ex) {
            Logger.Log($"Exception in PlayClip: {ex.Message}", LogLevel.Error);
        }
    }

    /// <summary>
    ///     Resets the clip player by hiding the player source and clip information text source,
    ///     and setting the browser source to a blank page.
    /// </summary>
    /// <param name="clipInfoText">
    ///     The name of the text source displaying clip information.
    /// </param>
    /// <param name="obsSetBrowserSource">
    ///     Action to set the URL of a browser source in OBS.
    /// </param>
    /// <param name="obsHideSource">
    ///     Action to hide a source in OBS.
    /// </param>
    private static void ResetClipPlayer(string clipInfoText,
                                        Action<string, string, string> obsSetBrowserSource,
                                        Action<string, string> obsHideSource) {
        Logger.Log("ResetClipPlayer method called", LogLevel.Info);

        try {
            obsHideSource(SceneName, PlayerSourceName);
            obsHideSource(SceneName, clipInfoText);
            obsSetBrowserSource(SceneName, PlayerSourceName, "about:blank");
        } catch (Exception ex) {
            Logger.Log($"Exception in ResetClipPlayer: {ex.Message}", LogLevel.Error);
        }
    }

    /// <summary>
    ///     Checks if a scene with the specified name exists in OBS.
    /// </summary>
    /// <param name="sceneName">
    ///     The name of the scene to check for existence.
    /// </param>
    /// <param name="obsSendRaw">
    ///     A function that sends a raw command to OBS and returns the response as a string.
    /// </param>
    /// <returns>
    ///     <c>true</c> if the scene exists; otherwise, <c>false</c>.
    /// </returns>
    private static bool SceneExists(string sceneName, Func<string, string, string> obsSendRaw) {
        Logger.Log("SceneExists method called", LogLevel.Info);

        try {
            var sceneResponse = obsSendRaw("GetSceneList", "{}");
            var jsonResponse = JObject.Parse(sceneResponse);
            var scenes = jsonResponse["scenes"] as JArray;
            var sceneExists = scenes != null
                              && scenes.Any(scene => string.Equals((string?)scene["sceneName"] ?? string.Empty,
                                                                   sceneName,
                                                                   StringComparison.Ordinal));
            Logger.Log($"Scene '{sceneName}' exists: {sceneExists}", LogLevel.Info);

            return sceneExists;
        } catch (Exception ex) {
            Logger.Log($"Exception in SceneExists: {ex.Message}", LogLevel.Error);

            return false;
        }
    }

    /// <summary>
    ///     Sets the metadata for a given clip.
    /// </summary>
    /// <param name="clip">The clip data containing metadata information such as broadcaster name, title, and creator name.</param>
    private static void SetClipMetadata(ClipData clip) {
        Logger.Log("SetClipMetadata method called", LogLevel.Info);

        try {
            ChannelName = clip.BroadcasterName;
            ClipTitle = clip.Title;
            ClipCreator = clip.CreatorName;
        } catch (Exception ex) {
            Logger.Log($"Exception in SetClipMetadata: {ex.Message}", LogLevel.Error);
        }
    }

    /// <summary>
    ///     Checks if a specified source exists within a given scene in OBS.
    /// </summary>
    /// <param name="sceneName">
    ///     The name of the scene to check.
    /// </param>
    /// <param name="sourceName">
    ///     The name of the source to look for within the scene.
    /// </param>
    /// <param name="obsSendRaw"></param>
    /// <returns>
    ///     <c>true</c> if the source exists in the specified scene; otherwise, <c>false</c>.
    /// </returns>
    private static bool SourceExistsInScene(string sceneName,
                                            string sourceName,
                                            Func<string, string, string> obsSendRaw) {
        Logger.Log($"SourceExistsInScene method called for scene '{sceneName}' and source '{sourceName}'",
                   LogLevel.Info);

        try {
            var sceneItemResponse = obsSendRaw("GetSceneItemList", $"{{\"sceneName\":\"{sceneName}\"}}");
            var sceneItemJsonResponse = JObject.Parse(sceneItemResponse);
            var sceneItems = sceneItemJsonResponse["sceneItems"] as JArray;

            return sceneItems != null
                   && sceneItems.Any(item => string.Equals((string?)item["sourceName"] ?? string.Empty,
                                                           sourceName,
                                                           StringComparison.Ordinal));
        } catch (Exception ex) {
            Logger.Log($"Exception in SourceExistsInScene: {ex.Message}", LogLevel.Error);

            return false;
        }
    }

    /// <summary>
    ///     Updates the text source in the OBS scene with the provided clip information.
    /// </summary>
    /// <param name="clipInfoText">The text information about the clip to be displayed.</param>
    /// <param name="obsSendRaw">A function to send raw commands to OBS.</param>
    /// <param name="wait">An action to introduce a delay.</param>
    private static void UpdateClipInfoTextSource(string clipInfoText,
                                                 Func<string, string, string> obsSendRaw,
                                                 Action<int> wait) {
        Logger.Log("UpdateClipInfoTextSource method called", LogLevel.Info);

        try {
            ManageTextSourceInScene(SceneName, clipInfoText, obsSendRaw, wait);
        } catch (Exception ex) {
            Logger.Log($"Exception in UpdateClipInfoTextSource: {ex.Message}", LogLevel.Error);
        }
    }
}

/// <summary>
///     Wrapper class implementing the <see cref="ILogger" /> interface to handle user selection of logging.
/// </summary>
public class DefaultLogger : ILogger {
    private readonly CPHInline _cphInline;
    private bool? _logging;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DefaultLogger" /> class.
    /// </summary>
    private DefaultLogger() {
        _cphInline = new CPHInline();
    }

    /// <summary>
    ///     Gets the singleton instance of the <see cref="DefaultLogger" /> class.
    /// </summary>
    /// <value>
    ///     The singleton instance of the <see cref="DefaultLogger" /> class.
    /// </value>
    private static DefaultLogger Instance { get; } = new();

    /// <summary>
    ///     Gets a value indicating whether logging is enabled.
    /// </summary>
    /// <value>
    ///     <c>true</c> if logging is enabled; otherwise, <c>false</c>.
    /// </value>
    private bool Logging {
        get {
            if (_logging.HasValue) {
                return _logging.Value;
            }

            _logging = !_cphInline.TryGetArg("logging", out bool logging) || logging;

            return (bool)_logging;
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
    ///     Thrown when the specified <paramref name="level" /> is not a valid <see cref="LogLevel" />.
    /// </exception>
    public void Log(string message, LogLevel level) {
        _cphInline.LogInfo("Log method called");

        if (level != LogLevel.Error && !Logging) {
            return;
        }

        switch (level) {
            case LogLevel.Info:
                _cphInline.LogInfo(message);

                break;

            case LogLevel.Warn:
                _cphInline.LogWarn(message);

                break;

            case LogLevel.Error:
                _cphInline.LogError(message);

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(level),
                                                      level,
                                                      $"No member of Enum LogLevel matching {level.ToString()} exists.");
        }
    }

    /// <summary>
    ///     Factory method to create a new default instance of the <see cref="ILogger" /> interface.
    /// </summary>
    /// <returns>
    ///     A new instance of the <see cref="DefaultLogger" /> class.
    /// </returns>
    public static DefaultLogger CreateInstance() {
        return Instance;
    }
}