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
                case "!watch": return HandleWatchCommand(GetArgument(CPH, "input0", "")).GetAwaiter().GetResult();
                case "!so": return HandleShoutoutCommand(GetArgument(CPH, "input0", "")).GetAwaiter().GetResult();
                case "!replay": return HandleReplayCommand().GetAwaiter().GetResult();
                case "!stop": return HandleStopCommand().GetAwaiter().GetResult();
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

    private async Task<bool> HandleWatchCommand(string input) {
        _logger.Log(LogLevel.Debug, "Handling !watch command.");

        try {
            _logger.Log(LogLevel.Debug, $"Determining type of Input 0: {input}...");

            if (IsInvalidInput(input)) {
                _logger.Log(LogLevel.Debug, "Input 0 is invalid. Falling back to last clip...");

                return await ProcessLastClipFallback();
            }

            _logger.Log(LogLevel.Debug, "Input 0 is a valid URL, username, or search term. Checking for URL...");

            if (IsValidUrl(input)) {
                _logger.Log(LogLevel.Debug, "Input 0 is a valid URL. Processing...");

                return await ProcessClipByUrl(input);
            }

            _logger.Log(LogLevel.Debug, "Input 0 is not a valid URL. Checking username...");

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

    private static bool IsValidUrl(string input) {
        // Simple validation for well-formed URLs; adjust as needed for your specific project
        return Uri.IsWellFormedUriString(input, UriKind.Absolute) && input.Contains("twitch.tv");
    }

    private bool IsInvalidInput(string input) {
        if (!string.IsNullOrWhiteSpace(input) && !input.Equals("about:blank")) return false;

        _logger.Log(LogLevel.Warn, "No valid clip URL or search term provided.");

        return true;
    }

    private async Task<bool> ProcessLastClipFallback() {
        var lastClipUrl = _clipManager.GetLastClipUrl();

        if (!string.IsNullOrWhiteSpace(lastClipUrl)) {
            _logger.Log(LogLevel.Debug, "Last clip URL found. Processing...");

            return await ProcessClipByUrl(lastClipUrl);
        }

        _logger.Log(LogLevel.Error, "No clip URL or previous clip available.");

        return false;
    }

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

    private (string broadcasterId, string searchTerm) ResolveBroadcasterAndSearchTerm(string input) {
        input = input.Trim();

        var inputArgs = input.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
        var username = inputArgs[0].StartsWith("@") ? inputArgs[0].Substring(1) : null;
        var searchTerm = inputArgs.Length > 1
                             ? inputArgs[1]
                             : username == null
                                 ? inputArgs[0]
                                 : null;

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

    private async Task<bool> PlayClipAsync(ClipData clipData) {
        _httpManager.HostClip(clipData);

        await _obsSceneManager.PlayClipAsync(clipData);
        await Task.Delay((int)clipData.Duration * 1000 + 3000);
        await HandleStopCommand();

        _clipManager.SetLastClipUrl(clipData.Url);

        return true;
    }

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

    private async Task<bool> HandleShoutoutCommand(string username) {
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
            await HandleStopCommand();

            _clipManager.SetLastClipUrl(clipData.Url);

            return true;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error occurred while handling !so command.", ex);

            return false;
        }
    }

    private ClipManager.ClipSettings GetClipSettings() {
        var featuredOnly = GetArgument(CPH, "featuredOnly", false);
        var maxDuration = GetArgument(CPH, "maxClipSeconds", 30);
        var maxAgeDays = GetArgument(CPH, "clipAgeDays", 30);

        return new ClipManager.ClipSettings(featuredOnly, maxDuration, maxAgeDays);
    }

    private async Task<bool> HandleReplayCommand() {
        _logger.Log(LogLevel.Debug, "Handling !replay command.");

        try {
            var lastClipUrl = _clipManager.GetLastClipUrl();

            if (!string.IsNullOrEmpty(lastClipUrl)) {
                var clipData = await _clipManager.GetClipDataAsync(lastClipUrl);

                _httpManager.HostClip(clipData);

                await _obsSceneManager.PlayClipAsync(clipData);
                await Task.Delay((int)clipData.Duration * 1000 + 3000);
                await HandleStopCommand();

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
    public bool StopClip() {
        try {
            if (_logger == null) {
                CPH.LogError("Logger is null. Aborting stop command handling.");

                return false;
            }

            _logger.Log(LogLevel.Info, "Stopping clip.");

            HandleStopCommand().GetAwaiter().GetResult();

            return true;
        } catch (Exception ex) {
            _logger?.Log(LogLevel.Error, "Error occurred while stopping clip.", ex);

            return false;
        }
    }

    private async Task<bool> HandleStopCommand() {
        _logger.Log(LogLevel.Debug, "Handling !stop command.");

        try {
            await _obsSceneManager.StopClip();
            await _httpManager.StopHosting();

            _httpManager.Client.CancelPendingRequests();

            return true;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error occurred while handling !stop command.", ex);

            return false;
        }
    }

    public static T GetArgument<T>(IInlineInvokeProxy cph, string argName, T defaultValue = default) {
        return cph.TryGetArg(argName, out T value) ? value : defaultValue;
    }

    public static HttpManager GetHttpManager() {
        return _httpManager;
    }

    // Levenshtein Distance Calculation
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
    public bool CleanCache() {
        return _clipManager.CleanCache();
    }
}

public class Dimensions {
    public Dimensions(int height = 1080, int width = 1920) {
        Height = height;
        Width = width;
    }

    public int Height { get; }
    public int Width { get; }
}