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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Streamer.bot.Common.Events;
using Streamer.bot.Plugin.Interface;
using Streamer.bot.Plugin.Interface.Enums;
using Streamer.bot.Plugin.Interface.Model;
using Twitch.Common.Models.Api;

#endregion

// ReSharper disable once UnusedType.Global
// ReSharper disable once ClassNeverInstantiated.Global
/// <summary>
///     The main class for handling Twitch clip operations including watch, shoutout, and replay
///     commands. Interacts with Twitch and OBS APIs to stream or display clip content.
/// </summary>
public class CPHInline : CPHInlineBase {
    private static HttpManager _httpManager;
    public static Dimensions Dimensions;

    private readonly string[] _wordsOfAffirmation = {
        "yes",
        "yep",
        "yeah",
        "yar",
        "go ahead",
        "yup",
        "sure",
        "fine",
        "okay",
        "ok",
        "play it",
        "alright",
        "alrighty",
        "alrighties",
        "seemsgood",
        "thumbsup"
    };

    private readonly string[] _wordsOfDenial = {
        "no",
        "nay",
        "nope",
        "nah",
        "nar",
        "naw",
        "not sure",
        "not okay",
        "not ok",
        "okayn't",
        "yesn't",
        "not alright",
        "thumbsdown"
    };

    private ClipManager _clipManager;
    private bool _initialized;
    private bool _isModApproved;
    private CPHLogger _logger;
    private bool _loggingEnabled;
    private ObsSceneManager _obsSceneManager;
    private TwitchApiManager _twitchApiManager;

    // ReSharper disable once UnusedMember.Global
    /// <summary>
    ///     Executes the primary command handling logic. Initializes components and handles commands such
    ///     as "watch", "shoutout", "replay", and "stop", interacting with Twitch and OBS APIs as needed.
    /// </summary>
    /// <returns>
    ///     True if the command is handled successfully; otherwise, false.
    /// </returns>
    /// <remarks>
    ///     This is the main entry point for the software and requires a public bool signature with no
    ///     parameters.
    /// </remarks>
    public bool Execute() {
        InitializeComponents();

        if (_logger == null) {
            CPH.LogDebug("Logger is null. Attempting to reinitialize.");

            try {
                _logger = new CPHLogger(CPH, false);
                _logger.Log(LogLevel.Debug, "Logger reinitialized successfully.");
            } catch (Exception ex) {
                CPH.LogError($"Logger failed to reinitialize. {ex.Message ?? "Unknown error"}\n{ex.StackTrace}");

                return false;
            }
        }

        if (_obsSceneManager == null || _clipManager == null || _twitchApiManager == null || _httpManager == null) {
            _logger.Log(LogLevel.Error, "One or more dependencies are null after initialization.");

            return false;
        }

        _logger.Log(LogLevel.Debug, "Execute started.");

        try {
            var command = GetArgument(CPH, "command", "");

            if (string.IsNullOrWhiteSpace(command)) {
                _logger.Log(LogLevel.Error, "Command argument is missing.");

                return false;
            }

            _logger.Log(LogLevel.Info, $"Executing command: {command}");

            switch (command.ToLower()) {
                case "!watch": return HandleWatchCommandAsync(GetArgument(CPH, "input0", "")).GetAwaiter().GetResult();
                case "!so": return HandleShoutoutCommandAsync(GetArgument(CPH, "input0", "")).GetAwaiter().GetResult();
                case "!replay": return HandleReplayCommandAsync().GetAwaiter().GetResult();
                case "!stop": return HandleStopCommandAsync().GetAwaiter().GetResult();
                default:
                    _logger.Log(LogLevel.Warn, $"Unknown command received: {command}");

                    return false;
            }
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Critical failure during execution.", ex);

            return false;
        } finally {
            _logger.Log(LogLevel.Debug, "Execute completed.");
        }
    }

    /// <summary>
    ///     Instantiates and, where applicable, provides default values for Cliparino's various components.
    /// </summary>
    private void InitializeComponents() {
        if (_initialized) return;

        CPH.LogInfo("Cliparino :: InitializeComponents :: Initializing Cliparino components...");
        _loggingEnabled = GetArgument(CPH, "logging", false);

        var height = GetArgument(CPH, "height", 1080);
        var width = GetArgument(CPH, "width", 1920);

        Dimensions = new Dimensions(height, width);

        try {
            _logger = new CPHLogger(CPH, _loggingEnabled);
            _logger?.Log(LogLevel.Debug, "Logger initialized successfully.");
            _twitchApiManager = new TwitchApiManager(CPH, _logger);

            if (_twitchApiManager != null) _logger?.Log(LogLevel.Debug, "TwitchApiManager initialized successfully.");

            _httpManager = new HttpManager(_logger, _twitchApiManager);

            if (_httpManager != null) _logger?.Log(LogLevel.Debug, "HttpManager initialized successfully.");

            _clipManager = new ClipManager(CPH, _logger, _twitchApiManager);

            if (_clipManager != null) _logger?.Log(LogLevel.Debug, "ClipManager initialized successfully.");

            _obsSceneManager = new ObsSceneManager(CPH, _logger);

            if (_obsSceneManager != null) _logger?.Log(LogLevel.Debug, "ObsSceneManager initialized successfully.");

            _httpManager.StartServer();

            _initialized = true;
            _logger?.Log(LogLevel.Info, "Cliparino components initialized successfully.");
        } catch (Exception ex) {
            _logger?.Log(LogLevel.Error, "Initialization encountered an error", ex);
            CPH.LogError($"Critical failure during initialization. {ex.Message ?? "Unknown error"}\n{ex.StackTrace}");
        }
    }

    public void Dispose() {
        _httpManager.StopHosting().GetAwaiter().GetResult();
    }

    /// <summary>
    ///     Handles the "!watch" command to watch Twitch clips. Determines the input type (e.g., URL,
    ///     username, or search term) and processes the request accordingly. Provides fallback to the last
    ///     clip if input is invalid.
    /// </summary>
    /// <param name="input">
    ///     The input provided by the user, which can include a Twitch clip URL, a username, or a search
    ///     term.
    /// </param>
    /// <returns>
    ///     A task representing the operation, with a boolean result indicating whether the command
    ///     execution was successful.
    /// </returns>
    private async Task<bool> HandleWatchCommandAsync(string input) {
        _logger.Log(LogLevel.Debug, "Handling !watch command.");

        try {
            _logger.Log(LogLevel.Debug, $"Determining type of Input 0: {input}...");

            if (IsInvalidInput(input)) {
                _logger.Log(LogLevel.Debug, "Input 0 is invalid. Falling back to last clip...");

                return await ProcessLastClipFallback();
            }

            if (IsUsername(input)) {
                _logger.Log(LogLevel.Debug, "Input 0 is a username.");
            } else {
                _logger.Log(LogLevel.Debug, "Input 0 is a valid URL or search term. Checking for URL...");

                if (IsValidUrl(input)) {
                    _logger.Log(LogLevel.Debug, "Input 0 is a valid URL. Processing...");

                    return await ProcessClipByUrl(input);
                }
            }

            var (broadcasterId, searchTerm) = ResolveBroadcasterAndSearchTerm(input);

            if (string.IsNullOrEmpty(broadcasterId)) {
                CPH.SendMessage("Unable to resolve channel by username. Please try again with a valid username or URL.");

                return false;
            }

            _logger.Log(LogLevel.Debug, "Input reconciled. Searching for clips...");

            return await SearchAndPlayClip(broadcasterId, searchTerm);
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error occurred while handling !watch command.", ex);

            return false;
        }
    }

    private static bool IsUsername(string input) {
        return input.Trim().StartsWith("@");
    }

    /// <summary>
    ///     Validates whether the provided string is a well-formed URL pointing to twitch.tv.
    /// </summary>
    /// <param name="input">
    ///     The string to validate as a URL.
    /// </param>
    /// <returns>
    ///     True if the provided string is a well-formed absolute URL and contains "twitch.tv"; otherwise,
    ///     false.
    /// </returns>
    private static bool IsValidUrl(string input) {
        return Uri.IsWellFormedUriString(input, UriKind.Absolute) && input.Contains("twitch.tv");
    }

    /// <summary>
    ///     Determines if the provided input is invalid based on certain criteria, such as being empty,
    ///     whitespace, or the value "about:blank".
    /// </summary>
    /// <param name="input">
    ///     The input string to check for validity.
    /// </param>
    /// <returns>
    ///     True if the input is deemed invalid, otherwise false.
    /// </returns>
    private bool IsInvalidInput(string input) {
        if (!string.IsNullOrWhiteSpace(input) && !input.Equals("about:blank")) return false;

        _logger.Log(LogLevel.Warn, "No valid clip URL or search term provided.");

        return true;
    }

    /// <summary>
    ///     Attempts to process the last known clip URL as a fallback when the provided input or clip
    ///     search fails.
    /// </summary>
    /// <returns>
    ///     A task that resolves to a boolean indicating whether processing the last clip was successful.
    /// </returns>
    private async Task<bool> ProcessLastClipFallback() {
        var lastClipUrl = _clipManager.GetLastClipUrl();

        if (!string.IsNullOrWhiteSpace(lastClipUrl)) {
            _logger.Log(LogLevel.Debug, "Last clip URL found. Processing...");

            return await ProcessClipByUrl(lastClipUrl);
        }

        _logger.Log(LogLevel.Error, "No clip URL or previous clip available.");

        return false;
    }

    /// <summary>
    ///     Processes a given Twitch clip URL by retrieving clip data and attempting to play it using
    ///     relevant APIs. Logs debug and error information based on the operation's outcome.
    /// </summary>
    /// <param name="url">
    ///     The URL of the Twitch clip to process.
    /// </param>
    /// <returns>
    ///     A task representing the asynchronous operation. The result is a boolean indicating whether
    ///     processing and playback were successful.
    /// </returns>
    private async Task<bool> ProcessClipByUrl(string url) {
        _logger.Log(LogLevel.Debug, "Valid URL received. Accessing clip data...");

        try {
            var clipData = await _clipManager.GetClipDataAsync(url);

            if (clipData != null) {
                _logger.Log(LogLevel.Debug, "Clip data retrieved successfully.");

                return await PlayClipAsync(clipData);
            }

            _logger.Log(LogLevel.Warn, "Clip data could not be retrieved.");
            CPH.SendMessage("Unable to retrieve clip data. Please try again with a valid URL.");

            return false;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error occurred while processing clip by URL.", ex);

            return false;
        }
    }

    /// <summary>
    ///     Resolves the broadcaster ID and search term from the given input.
    /// </summary>
    /// <param name="input">
    ///     The user-provided input, which could be a username, a search term, or both.
    /// </param>
    /// <returns>
    ///     A tuple containing the broadcaster ID and a search term. If the username is not resolved, it
    ///     returns null for the broadcaster ID and for the search term.
    /// </returns>
    /// <remarks>
    ///     In the case that no valid broadcaster/channel name was supplied, falls back to this channel's
    ///     broadcaster.
    /// </remarks>
    private (string broadcasterId, string searchTerm) ResolveBroadcasterAndSearchTerm(string input) {
        input = input.Trim();

        var inputArgs = input.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
        var username = IsUsername(inputArgs[0]) ? inputArgs[0] : CPH.TwitchGetBroadcaster().UserName;
        var searchTerm = inputArgs.Length > 1 ? inputArgs[1] : inputArgs[0];

        if (string.IsNullOrEmpty(username)) {
            var broadcaster = CPH.TwitchGetBroadcaster();

            _logger.Log(LogLevel.Debug, $"Using current broadcaster: {broadcaster.UserName}");

            return (broadcaster.UserId, searchTerm);
        }

        var userInfo = CPH.TwitchGetExtendedUserInfoByLogin(username);

        if (userInfo == null) {
            _logger.Log(LogLevel.Warn, $"Could not resolve username: {username}");

            return (null, null);
        }

        _logger.Log(LogLevel.Debug, $"Resolved Broadcaster ID: {userInfo.UserId} for username: {username}");

        return (userInfo.UserId, searchTerm);
    }

    /// <summary>
    ///     Provides functionality to manage Twitch clips, including retrieval from cache and searching.
    /// </summary>
    private async Task<bool> SearchAndPlayClip(string broadcasterId, string searchInput) {
        var cachedClip = ClipManager.GetFromCache(searchInput);
        ClipData bestClip;

        if (cachedClip != null) {
            _logger.Log(LogLevel.Debug, "Found cached clip.");

            bestClip = cachedClip;
        } else {
            _logger.Log(LogLevel.Debug, "No cached clip found. Searching for clip...");
            bestClip = await _clipManager.SearchClipsWithThresholdAsync(broadcasterId, searchInput);

            if (bestClip == null) {
                CPH.SendMessage("No matching clip was found. Please refine your search.");

                return false;
            }
        }

        await RequestClipApproval(bestClip);

        if (_isModApproved) return await PlayClipAsync(bestClip);

        return _isModApproved;
    }

    /// <summary>
    ///     Plays the given clip asynchronously by interacting with HTTP and OBS managers.
    /// </summary>
    /// <param name="clipData">
    ///     The data of the clip to be played, including its URL, duration, and title.
    /// </param>
    /// <returns>
    ///     A task that represents the asynchronous operation. The task result contains a boolean
    ///     indicating whether the clip was successfully played.
    /// </returns>
    private async Task<bool> PlayClipAsync(ClipData clipData) {
        _httpManager.HostClip(clipData);

        await _obsSceneManager.PlayClipAsync(clipData);
        await Task.Delay((int)clipData.Duration * 1000 + 3000);
        await HandleStopCommandAsync();

        _clipManager.SetLastClipUrl(clipData.Url);

        return true;
    }

    /// <summary>
    ///     Requests approval for a specific clip from moderators, provides the clip details to moderators,
    ///     and waits for their response within a specified time frame.
    /// </summary>
    /// <param name="clip">
    ///     The clip data that is being submitted for moderator approval.
    /// </param>
    /// <returns>
    ///     A task that resolves to void.
    /// </returns>
    private async Task RequestClipApproval(ClipData clip) {
        CPH.TwitchReplyToMessage($"Did you mean this clip? {clip.Url}", GetArgument(CPH, "messageId", ""));
        CPH.SendMessage("I'll wait a minute for a mod to approve or deny this clip, starting now.");
        CPH.EnableAction("Mod Approval");

        var cancellationTokenSource = new CancellationTokenSource();
        var token = cancellationTokenSource.Token;

        try {
            await WaitForApproval(token);

            CPH.SendMessage(!_isModApproved
                                ? "Time's up! The clip wasn't approved, maybe next time!"
                                : "The clip has been approved!");
        } catch (TaskCanceledException) {
            _logger.Log(LogLevel.Debug, "Approval task canceled.");
        } finally {
            cancellationTokenSource.Dispose();
            CPH.DisableAction("Mod Approval");
        }
    }

    /// <summary>
    ///     Waits for a mod's approval of a clip or times out after a specified duration.
    /// </summary>
    /// <param name="token">
    ///     The cancellation token used to cancel the waiting operation.
    /// </param>
    /// <returns>
    ///     A Task that represents the asynchronous operation of waiting for approval or timeout.
    /// </returns>
    private async Task WaitForApproval(CancellationToken token) {
        var approvalTask = Task.Run(async () => {
                                        while (!_isModApproved && !token.IsCancellationRequested)
                                            await Task.Delay(500, token);
                                    },
                                    token);

        var timeoutTask = Task.Delay(60000, token);

        await Task.WhenAny(approvalTask, timeoutTask);

        if (!token.IsCancellationRequested) token.ThrowIfCancellationRequested();
    }

    // ReSharper disable once UnusedMember.Global
    /// <summary>
    ///     Determines if a moderator has approved or denied an action based on the content of the message
    ///     that triggered it. Updates and returns the internal approval state.
    /// </summary>
    /// <returns>
    ///     true if a moderator has approved the action, false if denied or if no affirmative words are
    ///     detected.
    /// </returns>
    public bool IsModApproved() {
        var isMod = GetArgument(CPH, "isModerator", false);

        if (!isMod) return _isModApproved;

        var message = GetArgument(CPH, "message", "");

        if (_wordsOfDenial.Any(word => message.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0))
            _isModApproved = false;
        else if (_wordsOfAffirmation.Any(word => message.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0))
            _isModApproved = true;

        return _isModApproved;
    }

    /// <summary>
    ///     Handles the `!so` command (shoutout) by sending a shoutout to a Twitch username, playing a
    ///     random clip associated with that user, and managing related OBS scene operations.
    /// </summary>
    /// <param name="username">
    ///     The username of the Twitch user to shout out.
    /// </param>
    /// <returns>
    ///     A task that represents the asynchronous operation. The task result is a boolean indicating
    ///     whether the command executed successfully.
    /// </returns>
    private async Task<bool> HandleShoutoutCommandAsync(string username) {
        _logger.Log(LogLevel.Debug, "Handling !so command.");

        try {
            if (string.IsNullOrWhiteSpace(username)) {
                _logger.Log(LogLevel.Warn, "Shoutout command received without a valid username.");

                return false;
            }

            var clipSettings = GetClipSettings();
            var clipData = await _clipManager.GetRandomClipAsync(username, clipSettings);

            if (clipData == null) {
                _logger.Log(LogLevel.Warn, $"No valid clip found for {username}. Skipping playback.");

                return false;
            }

            var message = GetArgument(CPH, "soMessage", "");

            _twitchApiManager.SendShoutout(username, message);
            _httpManager.HostClip(clipData);

            await _obsSceneManager.PlayClipAsync(clipData);
            await Task.Delay((int)clipData.Duration * 1000 + 3000);
            await HandleStopCommandAsync();

            _clipManager.SetLastClipUrl(clipData.Url);

            return true;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error occurred while handling !so command.", ex);

            return false;
        }
    }

    /// <summary>
    ///     Retrieves the clip settings used for clip selection and playback.
    /// </summary>
    /// <returns>
    ///     A <see cref="ClipManager.ClipSettings" /> object containing the clip preferences such as
    ///     whether only featured clips are allowed, the maximum clip duration in seconds, and the maximum
    ///     age of the clip in days.
    /// </returns>
    private ClipManager.ClipSettings GetClipSettings() {
        var featuredOnly = GetArgument(CPH, "featuredOnly", false);
        var maxDuration = GetArgument(CPH, "maxClipSeconds", 30);
        var maxAgeDays = GetArgument(CPH, "clipAgeDays", 30);

        return new ClipManager.ClipSettings(featuredOnly, maxDuration, maxAgeDays);
    }

    /// <summary>
    ///     Handles the '!replay' command by retrieving the last captured Twitch clip url and playing it
    ///     again through OBS if a valid clip is found.
    /// </summary>
    /// <returns>
    ///     true if the replay operation is successfully executed; otherwise, false if an error occurs, or
    ///     no valid clip is available.
    /// </returns>
    private async Task<bool> HandleReplayCommandAsync() {
        _logger.Log(LogLevel.Debug, "Handling !replay command.");

        try {
            var lastClipUrl = _clipManager.GetLastClipUrl();

            if (!string.IsNullOrEmpty(lastClipUrl)) {
                var clipData = await _clipManager.GetClipDataAsync(lastClipUrl);

                _httpManager.HostClip(clipData);

                await _obsSceneManager.PlayClipAsync(clipData);
                await Task.Delay((int)clipData.Duration * 1000 + 3000);
                await HandleStopCommandAsync();

                return true;
            }

            _logger.Log(LogLevel.Warn, "No clip available for replay.");

            return false;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error occurred while handling !replay command.", ex);

            return false;
        }
    }

    // ReSharper disable once UnusedMember.Global
    /// <summary>
    ///     Stops the currently active clip playback process and logs the operation status.
    /// </summary>
    /// <returns>
    ///     A boolean indicating whether the clip stop operation was successful (true) or not (false).
    /// </returns>
    public bool StopClip() {
        try {
            if (_logger == null) {
                CPH.LogError("Logger is null. Aborting stop command handling.");

                return false;
            }

            _logger.Log(LogLevel.Info, "Stopping clip.");

            HandleStopCommandAsync().GetAwaiter().GetResult();

            return true;
        } catch (Exception ex) {
            _logger?.Log(LogLevel.Error, "Error occurred while stopping clip.", ex);

            return false;
        }
    }

    /// <summary>
    ///     Handles the logic for stopping an ongoing clip, invoked by the "!stop" command.
    /// </summary>
    /// <returns>
    ///     A task representing the asynchronous operation. The task result contains a boolean indicating
    ///     whether the operation was completed successfully.
    /// </returns>
    private async Task<bool> HandleStopCommandAsync() {
        _logger.Log(LogLevel.Debug, "Handling !stop command.");

        try {
            await _obsSceneManager.StopClip();
            // await _httpManager.StopHosting();
            //
            // _httpManager.Client.CancelPendingRequests();

            return true;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error occurred while handling !stop command.", ex);

            return false;
        }
    }

    /// <summary>
    ///     Retrieves a specified argument from the IInlineInvokeProxy instance and returns its value. If
    ///     the argument is not found, a default value is returned.
    /// </summary>
    /// <typeparam name="T">
    ///     The type of the argument to be retrieved.
    /// </typeparam>
    /// <param name="cph">
    ///     The IInlineInvokeProxy instance from which to retrieve the argument.
    /// </param>
    /// <param name="argName">
    ///     The name of the argument to be retrieved.
    /// </param>
    /// <param name="defaultValue">
    ///     The default value to return if the argument is not found.
    /// </param>
    /// <returns>
    ///     The value of the specified argument, or the default value if the argument is not found.
    /// </returns>
    /// <remarks>
    ///     Serves a wrapper for <see cref="IInlineInvokeProxy.TryGetArg" /> that returns its resulting
    ///     argument directly instead of as an "out" parameter.
    /// </remarks>
    public static T GetArgument<T>(IInlineInvokeProxy cph, string argName, T defaultValue = default) {
        return cph.TryGetArg(argName, out T value) ? value : defaultValue;
    }

    /// <summary>
    ///     Retrieves the instance of <see cref="HttpManager" />.
    /// </summary>
    /// <returns>
    ///     The current instance of <see cref="HttpManager" />.
    /// </returns>
    public static HttpManager GetHttpManager() {
        return _httpManager;
    }

    /// <summary>
    ///     Calculates the Levenshtein distance between two strings, which measures the minimum number of
    ///     single-character edits (insertions, deletions, or substitutions) required to change one string
    ///     into the other.
    /// </summary>
    /// <param name="source">
    ///     The source string for comparison.
    /// </param>
    /// <param name="target">
    ///     The target string for comparison.
    /// </param>
    /// <returns>
    ///     The Levenshtein distance between the source and target strings.
    /// </returns>
    /// <remarks>
    ///     Deprecated in favor of word-based similarity. Retained for posterity.
    /// </remarks>
    public static int CalculateLevenshteinDistance(string source, string target) {
        var m = source.Length;
        var n = target.Length;
        var matrix = new int[m + 1, n + 1];

        for (var i = 0; i <= m; i++) matrix[i, 0] = i;
        for (var j = 0; j <= n; j++) matrix[0, j] = j;

        for (var i = 1; i <= m; i++) {
            for (var j = 1; j <= n; j++) {
                var cost = target[j - 1] == source[i - 1] ? 0 : 1;

                matrix[i, j] = Math.Min(Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                                        matrix[i - 1, j - 1] + cost);
            }
        }

        return matrix[m, n];
    }

    // ReSharper disable once UnusedMember.Global
    /// <summary>
    ///     Cleans the cache of stored clip data. Removes entries from the cache that are deemed invalid or
    ///     outdated.
    /// </summary>
    /// <returns>
    ///     Returns true if the cache was cleaned successfully; otherwise, false.
    /// </returns>
    public bool CleanCache() {
        return _clipManager.CleanCache();
    }
}

/// <summary>
///     Provides dimension information for Cliparino display. Namely, the height and width.
/// </summary>
public class Dimensions {
    public Dimensions(int height = 1080, int width = 1920) {
        Height = height;
        Width = width;
    }

    /// <summary>
    ///     Gets the height dimension of the object, typically representing a vertical measurement in
    ///     pixels.
    /// </summary>
    public int Height { get; }

    /// <summary>
    ///     Gets the width of the dimensions, represented as an integer value.
    /// </summary>
    public int Width { get; }
}