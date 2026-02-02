using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Streamer.bot.Common.Events;
using Streamer.bot.Plugin.Interface.Enums;
using Streamer.bot.Plugin.Interface.Model;
using Streamer.bot.Plugin.Interface;
using System.Collections.Generic;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System;
using Twitch.Common.Models.Api;

public class CPHInline {
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
        if (!ValidationHelper.ValidateDependencies(_obsSceneManager, _clipManager, _twitchApiManager, _httpManager)) {
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
            var input = GetArgument(CPH, "rawInput", "");
            _logger.Log(LogLevel.Info, $"Executing command: {command}");
            switch (command.ToLower()) {
                case "!watch": return HandleWatchCommandAsync(input).GetAwaiter().GetResult();
                case "!so": return HandleShoutoutCommandAsync(input).GetAwaiter().GetResult();
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
    private void InitializeComponents() {
        if (_initialized) return;
        CPH.LogInfo("Cliparino :: InitializeComponents :: Initializing Cliparino components...");
        _loggingEnabled = ConfigurationManager.GetLoggingEnabled(CPH);
        Dimensions = ConfigurationManager.GetDimensions(CPH);
        try {
            _logger = new CPHLogger(CPH, _loggingEnabled);
            _logger?.Log(LogLevel.Debug, "Logger initialized successfully.");
            var managers = ManagerFactory.CreateManagers(CPH, _logger);
            _twitchApiManager = managers.twitchApi;
            _httpManager = managers.http;
            _clipManager = managers.clip;
            _obsSceneManager = managers.obs;
            if (!ManagerFactory.ValidateManagers(managers, _logger)) {
                return;
            }
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
    private async Task<bool> HandleWatchCommandAsync(string input) {
        _logger.Log(LogLevel.Info, "Handling !watch command.");
        _logger.Log(LogLevel.Debug, $"Processing input: {input}");
        try {
            _logger.Log(LogLevel.Debug, "Testing input validity...");
            if (!ValidationHelper.IsValidInput(input)) {
                _logger.Log(LogLevel.Debug, "Input is invalid. Falling back to last clip...");
                return await ProcessLastClipFallback();
            }
            _logger.Log(LogLevel.Debug, "Testing input for username...");
            if (ValidationHelper.IsUsername(input)) {
                _logger.Log(LogLevel.Debug, "Input is a username.");
            } else {
                _logger.Log(LogLevel.Debug, "Input is a valid URL or search term. Checking for URL...");
                if (ValidationHelper.IsValidTwitchUrl(input)) {
                    _logger.Log(LogLevel.Debug, "Input is a valid URL. Processing...");
                    return await ProcessClipByUrl(input);
                }
            }
            var searchResult = InputProcessor.ParseBroadcastSearch(input, CPH, _logger);
            if (!searchResult.IsValid) {
                CPH.SendMessage(CliparinoConstants.Messages.UnableToResolveChannel);
                return false;
            }
            if (string.IsNullOrWhiteSpace(searchResult.SearchTerm)) {
                CPH.SendMessage(CliparinoConstants.Messages.ProvideValidSearchTerm);
                return false;
            }
            _logger.Log(LogLevel.Debug, "Input reconciled. Searching for clips...");
            return await SearchAndPlayClip(searchResult.BroadcasterId, searchResult.SearchTerm);
        } catch (NullReferenceException ex) {
            _logger.Log(LogLevel.Error, "Null reference exception occurred while handling !watch command.", ex);
            return false;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error occurred while handling !watch command.", ex);
            return false;
        } finally {
            _isModApproved = false;
        }
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
            CPH.SendMessage(CliparinoConstants.Messages.UnableToRetrieveClipData);
            return false;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error occurred while processing clip by URL.", ex);
            return false;
        }
    }
    private async Task<bool> SearchAndPlayClip(string broadcasterId, string searchInput) {
        _logger.Log(LogLevel.Debug, $"Searching for clip for ID: {broadcasterId} and search term: {searchInput}...");
        var cachedClip = ClipManager.GetFromCache(searchInput);
        ClipData bestClip;
        if (cachedClip != null) {
            _logger.Log(LogLevel.Debug, "Found cached clip.");
            bestClip = cachedClip;
        } else {
            _logger.Log(LogLevel.Debug, "No cached clip found. Searching for clip...");
            bestClip = await _clipManager.SearchClipsWithThresholdAsync(broadcasterId, searchInput);
            if (bestClip == null) {
                CPH.SendMessage(CliparinoConstants.Messages.NoClipFound);
                return false;
            }
        }
        await RequestClipApproval(bestClip);
        if (_isModApproved) return await PlayClipAsync(bestClip);
        return _isModApproved;
    }
    private async Task<bool> PlayClipAsync(ClipData clipData, string clipType = "clip") {
        return await ExecuteClipPlaybackWorkflow(clipData, clipType);
    }
    private async Task<bool> ExecuteClipPlaybackWorkflow(ClipData clipData, string clipType) {
        if (clipData == null) {
            _logger.Log(LogLevel.Error, $"No {clipType} data provided for playback.");
            return false;
        }
        // Host the clip
        var hostSuccess = _httpManager.HostClip(clipData);
        if (!hostSuccess) {
            _logger.Log(LogLevel.Error, $"Failed to prepare {clipType} '{clipData.Title}' for hosting. Aborting playback.");
            return false;
        }
        // Play the clip in OBS
        var playSuccess = await _obsSceneManager.PlayClipAsync(clipData);
        if (!playSuccess) {
            _logger.Log(LogLevel.Error, $"Failed to play {clipType} '{clipData.Title}' in OBS. Aborting playback.");
            return false;
        }
        // Wait for clip duration + buffer time
        await Task.Delay((int)clipData.Duration * 1000 + CliparinoConstants.Timing.ClipEndBufferMs);
        // Stop the clip
        var stopSuccess = await HandleStopCommandAsync();
        if (!stopSuccess) {
            _logger.Log(LogLevel.Warn, $"{clipType} playback completed but there were issues stopping the clip.");
        }
        // Update last clip URL
        _clipManager.SetLastClipUrl(clipData.Url);
        return true;
    }
    private async Task RequestClipApproval(ClipData clip) {
        CPH.TwitchReplyToMessage(string.Format(CliparinoConstants.Messages.ClipApprovalPrompt, clip.Url), ConfigurationManager.GetMessageId(CPH));
        CPH.SendMessage(CliparinoConstants.Messages.ApprovalWaitMessage);
        CPH.EnableAction("Mod Approval");
        var cancellationTokenSource = new CancellationTokenSource();
        var token = cancellationTokenSource.Token;
        try {
            await WaitForApproval(token);
            CPH.SendMessage(!_isModApproved
                                ? CliparinoConstants.Messages.ApprovalTimeoutMessage
                                : CliparinoConstants.Messages.ApprovalSuccessMessage);
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
                                            await Task.Delay(CliparinoConstants.Timing.ApprovalCheckIntervalMs, token);
            },
            token);
        var timeoutTask = Task.Delay(CliparinoConstants.Timing.ApprovalTimeoutMs, token);
        await Task.WhenAny(approvalTask, timeoutTask);
        if (!token.IsCancellationRequested) token.ThrowIfCancellationRequested();
    }
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
    private async Task<bool> HandleShoutoutCommandAsync(string username) {
        _logger.Log(LogLevel.Debug, "Handling !so command.");
        try {
            if (string.IsNullOrWhiteSpace(username)) {
                _logger.Log(LogLevel.Warn, "Shoutout command received without a valid username.");
                return false;
            }
            username = ValidationHelper.SanitizeUsername(username);
            var clipSettings = ConfigurationManager.GetClipSettings(CPH);
            var clipData = await _clipManager.GetRandomClipAsync(username, clipSettings);
            if (clipData == null) {
                _logger.Log(LogLevel.Warn, $"No valid clip found for {username}. Skipping playback.");
                CPH.SendMessage(CliparinoConstants.Messages.NoClipAvailableForReplay);
                return false;
            }
            var message = ConfigurationManager.GetShoutoutMessage(CPH);
            _twitchApiManager.SendShoutout(username, message);
            return await ExecuteClipPlaybackWorkflow(clipData, "shoutout clip");
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error occurred while handling !so command.", ex);
            return false;
        }
    }
    private async Task<bool> HandleReplayCommandAsync() {
        _logger.Log(LogLevel.Debug, "Handling !replay command.");
        try {
            var lastClipUrl = _clipManager.GetLastClipUrl();
            if (!string.IsNullOrEmpty(lastClipUrl)) {
                var clipData = await _clipManager.GetClipDataAsync(lastClipUrl);
                if (clipData == null) {
                    _logger.Log(LogLevel.Error, "Failed to retrieve clip data for replay.");
                    return false;
                }
                return await ExecuteClipPlaybackWorkflow(clipData, "replay clip");
            }
            _logger.Log(LogLevel.Warn, "No clip available for replay.");
            return false;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error occurred while handling !replay command.", ex);
            return false;
        }
    }
    public bool StopClip() {
        try {
            if (_logger == null) {
                CPH.LogError("Logger is null. Aborting stop command handling.");
                return false;
            }
            _logger.Log(LogLevel.Info, "Stopping clip.");
            var result = HandleStopCommandAsync().GetAwaiter().GetResult();
            if (result)
                _logger.Log(LogLevel.Info, "Clip stop operation completed successfully.");
            else
                _logger.Log(LogLevel.Error, "Clip stop operation failed.");
            return result;
        } catch (Exception ex) {
            _logger?.Log(LogLevel.Error, "Error occurred while stopping clip.", ex);
            return false;
        }
    }
    private async Task<bool> HandleStopCommandAsync() {
        _logger.Log(LogLevel.Debug, "Handling !stop command.");
        try {
            var stopSuccess = await _obsSceneManager.StopClip();
            if (!stopSuccess) {
                _logger.Log(LogLevel.Error, "Failed to stop clip in OBS.");
                return false;
            }
            await _httpManager.StopHosting();
            _httpManager.Client.CancelPendingRequests();
            _logger.Log(LogLevel.Info, "Successfully stopped clip playback.");
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
    public bool CleanCache() {
        return _clipManager.CleanCache();
    }
}
public class Dimensions {
    public Dimensions(int height = CliparinoConstants.Display.DefaultHeight, int width = CliparinoConstants.Display.DefaultWidth) {
        Height = height;
        Width = width;
    }
    public int Height { get; }
    public int Width { get; }
}

public static class CliparinoConstants {
    public static class Http {
        public const int BasePort = 8080;
        public const int MaxPortRetries = 10;
        public const int NonceLength = 16;
        public const string HelixApiUrl = "https://api.twitch.tv/helix/";
        public const string InactiveUrl = "about:blank";
    }
    public static class Obs {
        public const string CliparinoSourceName = "Cliparino";
        public const string PlayerSourceName = "Player";
    }
    public static class Timing {
        public const int ClipEndBufferMs = 3000;
        public const int ApprovalTimeoutMs = 60000;
        public const int ApprovalCheckIntervalMs = 500;
        public const int DefaultRetryDelayMs = 500;
    }
    public static class Logging {
        public const string MessagePrefix = "Cliparino :: ";
    }
    public static class Display {
        public const int DefaultHeight = 1080;
        public const int DefaultWidth = 1920;
    }
    public static class Clips {
        public const int DefaultMaxClipSeconds = 30;
        public const int DefaultClipAgeDays = 30;
        public const bool DefaultFeaturedOnly = false;
    }
    public static class WindowsApi {
        public const uint WM_LBUTTONDOWN = 0x0201;
        public const uint WM_LBUTTONUP = 0x0202;
        public const uint WM_RBUTTONDOWN = 0x0204;
        public const uint WM_RBUTTONUP = 0x0205;
    }
    public static class Messages {
        public const string NoClipFound = "No matching clip was found. Please refine your search.";
        public const string UnableToRetrieveClipData = "Unable to retrieve clip data. Please try again with a valid URL.";
        public const string UnableToResolveChannel = "Unable to resolve channel by username. Please try again with a valid username or URL.";
        public const string ProvideValidSearchTerm = "Please provide a valid search term to find a clip.";
        public const string NoClipAvailableForReplay = "No clip available for replay.";
        public const string ApprovalWaitMessage = "I'll wait a minute for a mod to approve or deny this clip, starting now.";
        public const string ApprovalTimeoutMessage = "Time's up! The clip wasn't approved, maybe next time!";
        public const string ApprovalSuccessMessage = "The clip has been approved!";
        public const string ClipApprovalPrompt = "Did you mean this clip? {0}";
    }
}

public static class ValidationHelper {
    public static bool IsValidTwitchUrl(string input) {
        return Uri.IsWellFormedUriString(input, UriKind.Absolute) && input.Contains("twitch.tv");
    }
    public static bool IsUsername(string input) {
        return !string.IsNullOrWhiteSpace(input) && input.StartsWith("@");
    }
    public static bool IsValidInput(string input) {
        return !string.IsNullOrWhiteSpace(input) && !input.Equals(CliparinoConstants.Http.InactiveUrl);
    }
    public static string SanitizeUsername(string username) {
        if (string.IsNullOrWhiteSpace(username)) return username;
        return username.StartsWith("@") ? username.Substring(1) : username;
    }
    public static bool ValidateDependencies(params object[] dependencies) {
        foreach (var dependency in dependencies) {
            if (dependency == null) return false;
        }
        return true;
    }
}

public static class RetryHelper {
    public static async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation,
        int maxRetries = 3,
        int baseDelayMs = CliparinoConstants.Timing.DefaultRetryDelayMs,
        CPHLogger logger = null,
        string operationName = "operation",
        CancellationToken cancellationToken = default) {
        var lastException = (Exception)null;
        for (var attempt = 0; attempt <= maxRetries; attempt++) {
            try {
                return await operation();
            } catch (HttpRequestException ex) when (attempt < maxRetries) {
                lastException = ex;
                var delay = CalculateDelay(attempt, baseDelayMs);
                logger?.Log(LogLevel.Warn, $"Attempt {attempt + 1} failed for {operationName}. Retrying in {delay}ms...");
                await Task.Delay(delay, cancellationToken);
            } catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested) {
                logger?.Log(LogLevel.Debug, $"Operation {operationName} was cancelled.");
                throw;
            } catch (Exception ex) when (attempt < maxRetries && IsRetryableException(ex)) {
                lastException = ex;
                var delay = CalculateDelay(attempt, baseDelayMs);
                logger?.Log(LogLevel.Warn, $"Attempt {attempt + 1} failed for {operationName}. Retrying in {delay}ms...");
                await Task.Delay(delay, cancellationToken);
            }
        }
        logger?.Log(LogLevel.Error, $"All retry attempts failed for {operationName}.");
        throw lastException ?? new InvalidOperationException($"Operation {operationName} failed after {maxRetries} retries.");
    }
    private static int CalculateDelay(int attempt, int baseDelayMs) {
        var exponentialDelay = baseDelayMs * Math.Pow(2, attempt);
        var jitter = new Random().Next(0, (int)(exponentialDelay * 0.1)); // 10% jitter
        return (int)Math.Min(exponentialDelay + jitter, 30000); // Cap at 30 seconds
    }
    private static bool IsRetryableException(Exception exception) {
        return exception is HttpRequestException ||
               exception is TaskCanceledException ||
               exception is TimeoutException;
    }
}

public static class HttpResponseBuilder {
    public static void BuildResponse(
        HttpListenerContext context,
        string content,
        string contentType = "text/html",
        int statusCode = 200,
        Dictionary<string, string> additionalHeaders = null,
        string nonce = null) {
        try {
            var response = context.Response;
            // Set status code
            response.StatusCode = statusCode;
            // Set content type
            response.ContentType = contentType;
            // Add standard headers
            AddStandardHeaders(response, nonce);
            // Add additional headers if provided
            if (additionalHeaders != null) {
                foreach (var header in additionalHeaders) {
                    response.Headers.Add(header.Key, header.Value);
                }
            }
            // Write content
            var buffer = Encoding.UTF8.GetBytes(content);
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.Close();
        } catch (Exception) {
            // Log error but don't throw to avoid breaking the HTTP listener
            try {
                context.Response.StatusCode = 500;
                context.Response.Close();
            } catch {
                // Ignore errors during error handling
            }
        }
    }
    public static void BuildJsonResponse(
        HttpListenerContext context,
        string jsonContent,
        int statusCode = 200,
        string nonce = null) {
        BuildResponse(context, jsonContent, "application/json", statusCode, null, nonce);
    }
    public static void BuildErrorResponse(
        HttpListenerContext context,
        int statusCode,
        string errorMessage,
        CPHLogger logger = null) {
        logger?.Log(LogLevel.Error, $"HTTP {statusCode}: {errorMessage}");
        var errorContent = $@"
        <!DOCTYPE html>
        <html>
        <head>
            <title>Error {statusCode}</title>
            <style>
                body {{ font-family: Arial, sans-serif; margin: 40px; }}
                .error {{ color: #d32f2f; }}
                .code {{ font-family: monospace; background: #f5f5f5; padding: 2px 4px; }}
            </style>
        </head>
        <body>
            <h1 class=""error"">Error {statusCode}</h1>
            <p>{errorMessage}</p>
            <p><small>Cliparino HTTP Server</small></p>
        </body>
        </html>";
        BuildResponse(context, errorContent, "text/html", statusCode);
    }
    private static void AddStandardHeaders(HttpListenerResponse response, string nonce = null) {
        // CORS headers
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "*");
        // Cache control headers
        response.Headers.Add("Cache-Control", "no-cache, no-store, must-revalidate");
        response.Headers.Add("Pragma", "no-cache");
        response.Headers.Add("Expires", "0");
        // Security headers
        if (!string.IsNullOrEmpty(nonce)) {
            response.Headers.Add("Content-Security-Policy", 
                $"script-src 'nonce-{nonce}' 'strict-dynamic'; object-src 'none'; base-uri 'none'; frame-ancestors 'self' https://clips.twitch.tv;");
        }
    }
    public static string GenerateNonce(int length = CliparinoConstants.Http.NonceLength) {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var random = new Random();
        var result = new char[length];
        for (var i = 0; i < length; i++) {
            result[i] = chars[random.Next(chars.Length)];
        }
        return new string(result);
    }
}

public static class ConfigurationManager {
    public static Dimensions GetDimensions(IInlineInvokeProxy cph) {
        var height = CPHInline.GetArgument(cph, "height", CliparinoConstants.Display.DefaultHeight);
        var width = CPHInline.GetArgument(cph, "width", CliparinoConstants.Display.DefaultWidth);
        return new Dimensions(height, width);
    }
    public static bool GetLoggingEnabled(IInlineInvokeProxy cph) {
        return CPHInline.GetArgument(cph, "logging", false);
    }
    public static ClipManager.ClipSettings GetClipSettings(IInlineInvokeProxy cph) {
        var featuredOnly = CPHInline.GetArgument(cph, "featuredOnly", CliparinoConstants.Clips.DefaultFeaturedOnly);
        var maxDuration = CPHInline.GetArgument(cph, "maxClipSeconds", CliparinoConstants.Clips.DefaultMaxClipSeconds);
        var maxAgeDays = CPHInline.GetArgument(cph, "clipAgeDays", CliparinoConstants.Clips.DefaultClipAgeDays);
        return new ClipManager.ClipSettings(featuredOnly, maxDuration, maxAgeDays);
    }
    public static string GetShoutoutMessage(IInlineInvokeProxy cph) {
        return CPHInline.GetArgument(cph, "soMessage", "");
    }
    public static string GetMessageId(IInlineInvokeProxy cph) {
        return CPHInline.GetArgument(cph, "messageId", "");
    }
}

public static class ErrorHandler {
    public static T HandleError<T>(CPHLogger logger, Func<T> action, string operationName, T defaultValue = default) {
        try {
            return action();
        } catch (Exception ex) {
            logger.Log(LogLevel.Error, $"Error occurred during {operationName}.", ex);
            return defaultValue;
        }
    }
    public static async Task<T> HandleErrorAsync<T>(CPHLogger logger, Func<Task<T>> action, string operationName, T defaultValue = default) {
        try {
            return await action();
        } catch (Exception ex) {
            logger.Log(LogLevel.Error, $"Error occurred during {operationName}.", ex);
            return defaultValue;
        }
    }
    public static bool HandleError(CPHLogger logger, Action action, string operationName) {
        try {
            action();
            return true;
        } catch (Exception ex) {
            logger.Log(LogLevel.Error, $"Error occurred during {operationName}.", ex);
            return false;
        }
    }
    public static async Task<bool> HandleErrorAsync(CPHLogger logger, Func<Task> action, string operationName) {
        try {
            await action();
            return true;
        } catch (Exception ex) {
            logger.Log(LogLevel.Error, $"Error occurred during {operationName}.", ex);
            return false;
        }
    }
}

public static class InputProcessor {
    public class BroadcastSearchResult {
        public string BroadcasterId { get; set; }
        public string SearchTerm { get; set; }
        public bool IsValid => !string.IsNullOrWhiteSpace(BroadcasterId);
    }
    public static BroadcastSearchResult ParseBroadcastSearch(string input, IInlineInvokeProxy cph, CPHLogger logger) {
        var result = new BroadcastSearchResult();
        if (string.IsNullOrWhiteSpace(input)) {
            logger?.Log(LogLevel.Warn, "Input is empty. No broadcaster or search term can be resolved.");
            return result;
        }
        input = input.Trim();
        // Split the input into parts, assuming format: "@username searchTerm" or just "searchTerm"
        var inputParts = input.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
        var firstPart = inputParts[0];
        var secondPart = inputParts.Length > 1 ? inputParts[1] : string.Empty;
        if (ValidationHelper.IsUsername(firstPart)) {
            // If the first part is a username, resolve it
            var username = ValidationHelper.SanitizeUsername(firstPart);
            var userInfo = cph.TwitchGetExtendedUserInfoByLogin(username);
            if (userInfo != null) {
                result.BroadcasterId = userInfo.UserId;
                result.SearchTerm = secondPart;
                logger?.Log(LogLevel.Debug, $"Resolved Broadcaster ID: {result.BroadcasterId} for username: {username}");
            } else {
                logger?.Log(LogLevel.Warn, $"Could not resolve username: {username}");
            }
        } else {
            // Fall back to the current broadcaster if no valid username is provided
            var broadcaster = cph.TwitchGetBroadcaster();
            if (broadcaster != null) {
                result.BroadcasterId = broadcaster.UserId;
                result.SearchTerm = input; // Entire input is the search term
                logger?.Log(LogLevel.Debug, $"Using current broadcaster: {broadcaster.UserName}");
            }
        }
        return result;
    }
    public static string ExtractLastUrlSegment(string url) {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        try {
            var uri = new Uri(url);
            var segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            return segments.LastOrDefault() ?? string.Empty;
        } catch {
            return string.Empty;
        }
    }
    public static InputType DetectInputType(string input) {
        if (string.IsNullOrWhiteSpace(input)) return InputType.Invalid;
        if (ValidationHelper.IsValidTwitchUrl(input)) return InputType.Url;
        if (ValidationHelper.IsUsername(input)) return InputType.Username;
        return InputType.SearchTerm;
    }
    public enum InputType {
        Invalid,
        Url,
        Username,
        SearchTerm
    }
}

public static class ManagerFactory {
    public static (TwitchApiManager twitchApi, HttpManager http, ClipManager clip, ObsSceneManager obs) CreateManagers(
        IInlineInvokeProxy cph, CPHLogger logger) {
        try {
            logger.Log(LogLevel.Debug, "Creating TwitchApiManager...");
            var twitchApiManager = new TwitchApiManager(cph, logger);
            logger.Log(LogLevel.Debug, "Creating HttpManager...");
            var httpManager = new HttpManager(cph, logger, twitchApiManager);
            logger.Log(LogLevel.Debug, "Creating ClipManager...");
            var clipManager = new ClipManager(cph, logger, twitchApiManager);
            logger.Log(LogLevel.Debug, "Creating ObsSceneManager...");
            var obsSceneManager = new ObsSceneManager(cph, logger, httpManager);
            logger.Log(LogLevel.Debug, "All managers created successfully.");
            return (twitchApiManager, httpManager, clipManager, obsSceneManager);
        } catch (Exception ex) {
            logger.Log(LogLevel.Error, "Failed to create managers.", ex);
            throw;
        }
    }
    public static bool ValidateManagers(
        (TwitchApiManager twitchApi, HttpManager http, ClipManager clip, ObsSceneManager obs) managers,
        CPHLogger logger) {
        var isValid = ValidationHelper.ValidateDependencies(
            managers.twitchApi, 
            managers.http, 
            managers.clip, 
            managers.obs);
        if (!isValid) {
            logger.Log(LogLevel.Error, "One or more managers failed validation.");
        } else {
            logger.Log(LogLevel.Debug, "All managers validated successfully.");
        }
        return isValid;
    }
}

public class CPHLogger {
    private const string MessagePrefix = CliparinoConstants.Logging.MessagePrefix;
    private readonly IInlineInvokeProxy _cph;
    private readonly bool _loggingEnabled;
    public CPHLogger(IInlineInvokeProxy cph, bool loggingEnabled) {
        _cph = cph;
        _loggingEnabled = loggingEnabled;
    }
    public void Log(LogLevel level, string messageBody, Exception ex = null, [CallerMemberName] string caller = "") {
        if (caller == null) throw new ArgumentNullException(nameof(caller));
        if (!_loggingEnabled && level != LogLevel.Error) return;
        var message = $"{MessagePrefix}{caller} :: {messageBody}";
        switch (level) {
            case LogLevel.Debug: _cph.LogDebug(message); break;
            case LogLevel.Info: _cph.LogInfo(message); break;
            case LogLevel.Warn: _cph.LogWarn(message); break;
            case LogLevel.Error: LogError(message, ex); break;
            default: throw new ArgumentOutOfRangeException(nameof(level), level, null);
        }
    }
    private void LogError(string message, Exception ex = null) {
        if (ex == null) {
            _cph.LogError(message);
        } else {
            _cph.LogError($"{message}\nException: {ex.Message}");
            _cph.LogDebug($"StackTrace: {ex.StackTrace}");
        }
    }
}
public enum LogLevel {
    Debug,
    Info,
    Warn,
    Error
}

public class ObsSceneManager {
    private const string CliparinoSourceName = CliparinoConstants.Obs.CliparinoSourceName;
    private const string PlayerSourceName = CliparinoConstants.Obs.PlayerSourceName;
    private const string InactiveUrl = CliparinoConstants.Http.InactiveUrl;
    private readonly IInlineInvokeProxy _cph;
    private readonly HttpManager _httpManager;
    private readonly CPHLogger _logger;
    public ObsSceneManager(IInlineInvokeProxy cph, CPHLogger logger, HttpManager httpManager) {
        _cph = cph ?? throw new ArgumentNullException(nameof(cph));
        _logger = logger;
        _httpManager = httpManager ?? throw new ArgumentNullException(nameof(httpManager));
    }
    private static Dimensions Dimensions => CPHInline.Dimensions;
    public async Task<bool> PlayClipAsync(ClipData clipData) {
        if (clipData == null) {
            _logger.Log(LogLevel.Error, "No clip data provided.");
            return false;
        }
        var scene = CurrentScene();
        if (string.IsNullOrWhiteSpace(scene)) {
            _logger.Log(LogLevel.Error, "Unable to determine current OBS scene.");
            return false;
        }
        _logger.Log(LogLevel.Info, $"Playing clip '{clipData.Title}' ({clipData.Url}).");
        var setupSuccess = SetUpCliparino();
        if (!setupSuccess) {
            _logger.Log(LogLevel.Error, "Failed to set up Cliparino. Cannot play clip.");
            return false;
        }
        var showSuccess = ShowCliparino(scene);
        if (!showSuccess) {
            _logger.Log(LogLevel.Error, "Failed to show Cliparino in OBS. Cannot play clip.");
            return false;
        }
        var browserSourceSuccess = await SetBrowserSourceAsync(_httpManager.ServerUrl);
        if (!browserSourceSuccess) {
            _logger.Log(LogLevel.Error, "Failed to set browser source URL. Clip will not play properly.");
            return false;
        }
        await LogPlayerState();
        _logger.Log(LogLevel.Info, $"Successfully initiated playback of clip '{clipData.Title}'.");
        return true;
    }
    public async Task<bool> StopClip() {
        _logger.Log(LogLevel.Info, "Stopping clip playback.");
        try {
            await LogPlayerState();
            var browserSourceSuccess = await SetBrowserSourceAsync(InactiveUrl);
            if (!browserSourceSuccess)
                _logger.Log(LogLevel.Error, "Failed to clear browser source URL when stopping clip.");
            var hideSuccess = HideCliparino(CurrentScene());
            if (!hideSuccess) _logger.Log(LogLevel.Error, "Failed to hide Cliparino when stopping clip.");
            var overallSuccess = browserSourceSuccess && hideSuccess;
            if (overallSuccess)
                _logger.Log(LogLevel.Info, "Successfully stopped clip playback.");
            else
                _logger.Log(LogLevel.Warn, "Clip playback stopped with some issues.");
            return overallSuccess;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error stopping clip playback.", ex);
            return false;
        }
    }
    private async Task LogPlayerState() {
        await Task.Delay(1000);
        var browserSourceUrl = GetPlayerUrl().Contains("Error") ? "No URL found." : GetPlayerUrl();
        var isBrowserVisible = _cph.ObsIsSourceVisible(CliparinoSourceName, PlayerSourceName);
        _logger.Log(LogLevel.Debug,
                    $"Browser Source '{PlayerSourceName}' details - URL: {browserSourceUrl}, Visible: {isBrowserVisible}");
    }
    private string GetPlayerUrl() {
        try {
            var playerSettings = GetPlayerSettings();
            if (playerSettings == null) {
                _logger.Log(LogLevel.Error, "Failed to retrieve player settings.");
                return "Error: Failed to retrieve player settings.";
            }
            var playerUrl = playerSettings["inputSettings"]?["url"]?.ToString();
            if (string.IsNullOrWhiteSpace(playerUrl)) {
                _logger.Log(LogLevel.Warn, "Player URL is empty or not found in settings.");
                return "Error: No URL found in player settings.";
            }
            _logger.Log(LogLevel.Debug, $"Player URL: {playerUrl}");
            return playerUrl;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error retrieving player URL.", ex);
            return "Error: Exception occurred while retrieving URL.";
        }
    }
    private JObject GetPlayerSettings() {
        try {
            var payload = new Payload {
                RequestType = "GetInputSettings", RequestData = new { inputName = PlayerSourceName }
            };
            var response = _cph.ObsSendRaw(payload.RequestType, JsonConvert.SerializeObject(payload.RequestData));
            if (!ValidateObsOperation("GetInputSettings", response, $"get settings for input '{PlayerSourceName}'")) {
                _logger.Log(LogLevel.Error, $"Failed to get settings for Player source '{PlayerSourceName}'.");
                return null;
            }
            var settings = JsonConvert.DeserializeObject<JObject>(response);
            _logger.Log(LogLevel.Debug, $"Successfully retrieved settings for Player source '{PlayerSourceName}'.");
            return settings;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"Error retrieving settings for Player source '{PlayerSourceName}'.", ex);
            return null;
        }
    }
    private string CurrentScene() {
        var currentScene = _cph.ObsGetCurrentScene();
        if (string.IsNullOrEmpty(currentScene)) _logger.Log(LogLevel.Error, "Could not find current scene.");
        return currentScene;
    }
    private bool ShowCliparino(string scene) {
        try {
            _logger.Log(LogLevel.Debug, $"Showing Cliparino sources in scene '{scene}'.");
            if (!_cph.ObsIsSourceVisible(scene, CliparinoSourceName)) {
                _logger.Log(LogLevel.Debug, $"Making '{CliparinoSourceName}' visible in scene '{scene}'.");
                _cph.ObsSetSourceVisibility(scene, CliparinoSourceName, true);
                // Verify the operation was successful
                if (!_cph.ObsIsSourceVisible(scene, CliparinoSourceName)) {
                    _logger.Log(LogLevel.Error, $"Failed to make '{CliparinoSourceName}' visible in scene '{scene}'.");
                    return false;
                }
                _logger.Log(LogLevel.Debug, $"Successfully made '{CliparinoSourceName}' visible in scene '{scene}'.");
            }
            if (!_cph.ObsIsSourceVisible(CliparinoSourceName, PlayerSourceName)) {
                _logger.Log(LogLevel.Debug, $"Making '{PlayerSourceName}' visible in '{CliparinoSourceName}'.");
                _cph.ObsSetSourceVisibility(CliparinoSourceName, PlayerSourceName, true);
                // Verify the operation was successful
                if (!_cph.ObsIsSourceVisible(CliparinoSourceName, PlayerSourceName)) {
                    _logger.Log(LogLevel.Error,
                                $"Failed to make '{PlayerSourceName}' visible in '{CliparinoSourceName}'.");
                    return false;
                }
                _logger.Log(LogLevel.Debug,
                            $"Successfully made '{PlayerSourceName}' visible in '{CliparinoSourceName}'.");
            }
            _logger.Log(LogLevel.Debug, "Successfully made Cliparino sources visible.");
            return true;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error while showing Cliparino in OBS.", ex);
            return false;
        }
    }
    private bool HideCliparino(string scene) {
        try {
            _logger.Log(LogLevel.Debug, $"Hiding Cliparino sources in scene '{scene}'.");
            if (_cph.ObsIsSourceVisible(scene, CliparinoSourceName)) {
                _logger.Log(LogLevel.Debug, $"Hiding '{CliparinoSourceName}' in scene '{scene}'.");
                _cph.ObsSetSourceVisibility(scene, CliparinoSourceName, false);
                // Verify the operation was successful
                if (_cph.ObsIsSourceVisible(scene, CliparinoSourceName)) {
                    _logger.Log(LogLevel.Error, $"Failed to hide '{CliparinoSourceName}' in scene '{scene}'.");
                    return false;
                }
                _logger.Log(LogLevel.Debug, $"Successfully hid '{CliparinoSourceName}' in scene '{scene}'.");
            }
            if (_cph.ObsIsSourceVisible(CliparinoSourceName, PlayerSourceName)) {
                _logger.Log(LogLevel.Debug, $"Hiding '{PlayerSourceName}' in '{CliparinoSourceName}'.");
                _cph.ObsSetSourceVisibility(CliparinoSourceName, PlayerSourceName, false);
                // Verify the operation was successful
                if (_cph.ObsIsSourceVisible(CliparinoSourceName, PlayerSourceName)) {
                    _logger.Log(LogLevel.Error, $"Failed to hide '{PlayerSourceName}' in '{CliparinoSourceName}'.");
                    return false;
                }
                _logger.Log(LogLevel.Debug, $"Successfully hid '{PlayerSourceName}' in '{CliparinoSourceName}'.");
            }
            _logger.Log(LogLevel.Debug, "Successfully hid Cliparino sources.");
            return true;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error while hiding Cliparino in OBS.", ex);
            return false;
        }
    }
    private async Task<bool> SetBrowserSourceAsync(string url) {
        try {
            _logger.Log(LogLevel.Debug, $"Setting Player URL to '{url}'.");
            await Task.Run(() => { _cph.ObsSetBrowserSource(CliparinoSourceName, PlayerSourceName, url); });
            // Verify the URL was actually set by checking the current player URL
            // We'll give it a moment to update since this is async
            await Task.Delay(100);
            var currentUrl = GetPlayerUrl();
            if (currentUrl != null && !currentUrl.StartsWith("Error:") && currentUrl.Contains(url)) {
                _logger.Log(LogLevel.Info, $"Successfully set browser source URL to '{url}'.");
                return true;
            } else {
                _logger.Log(LogLevel.Error,
                            $"Failed to set browser source URL to '{url}'. Current URL: '{currentUrl}'. Clip may not play.");
                return false;
            }
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error setting OBS browser source.", ex);
            return false;
        }
    }
    private bool SetUpCliparino() {
        try {
            _logger.Log(LogLevel.Debug, "Setting up Cliparino in OBS.");
            if (!CliparinoExists()) {
                _logger.Log(LogLevel.Info, "Adding Cliparino scene to OBS.");
                if (!CreateCliparinoScene()) {
                    _logger.Log(LogLevel.Error, "Failed to create Cliparino scene.");
                    return false;
                }
            }
            if (!PlayerExists()) {
                _logger.Log(LogLevel.Info, "Adding Player source to Cliparino scene.");
                if (!AddPlayerToCliparino()) {
                    _logger.Log(LogLevel.Error, "Failed to add Player source to Cliparino scene.");
                    return false;
                }
                _logger.Log(LogLevel.Info, $"Configuring audio for source: {PlayerSourceName}");
                if (!ConfigureAudioSettings())
                    _logger.Log(LogLevel.Warn, "Audio configuration failed, but continuing with setup.");
            }
            if (!CliparinoInCurrentScene()) {
                _logger.Log(LogLevel.Info, "Adding Cliparino to current scene.");
                if (!AddCliparinoToScene()) {
                    _logger.Log(LogLevel.Error, "Failed to add Cliparino to current scene.");
                    return false;
                }
            }
            _logger.Log(LogLevel.Info, "Cliparino setup completed successfully.");
            return true;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error setting up Cliparino in OBS.", ex);
            return false;
        }
    }
    private bool SourceIsInScene(string scene, string source) {
        try {
            var response = GetSceneItemId(scene, source);
            // Handle null response (error occurred)
            if (response == null) {
                _logger.Log(LogLevel.Debug,
                            $"Could not determine if source '{source}' exists in scene '{scene}' due to error.");
                return false;
            }
            // Handle sentinel value indicating not found
            if (response is string && response == "not_found") {
                _logger.Log(LogLevel.Debug, $"Source '{source}' does not exist in scene '{scene}'.");
                return false;
            }
            // Try to get the scene item ID
            var itemId = response.sceneItemId;
            var exists = itemId != null && (int)itemId > 0;
            _logger.Log(LogLevel.Debug,
                        $"Source '{source}' in scene '{scene}': {(exists ? "exists" : "does not exist")} (ID: {itemId}).");
            return exists;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"Error checking if source '{source}' exists in scene '{scene}'.", ex);
            return false;
        }
    }
    private bool PlayerExists() {
        return SourceIsInScene(CliparinoSourceName, PlayerSourceName);
    }
    private bool CliparinoInCurrentScene() {
        return SourceIsInScene(CurrentScene(), CliparinoSourceName);
    }
    private dynamic GetSceneItemId(string sceneName, string sourceName) {
        try {
            var payload = new Payload {
                RequestType = "GetSceneItemId", RequestData = new { sceneName, sourceName, searchOffset = 0 }
            };
            var rawResponse = _cph.ObsSendRaw(payload.RequestType, JsonConvert.SerializeObject(payload.RequestData));
            // Note: GetSceneItemId may return an error response if the item doesn't exist, which is expected behavior
            // We'll validate but not log errors for this case since it's used to check existence
            if (rawResponse == null) {
                _logger.Log(LogLevel.Debug,
                            $"GetSceneItemId returned null for source '{sourceName}' in scene '{sceneName}'.");
                return null;
            }
            var response = JsonConvert.DeserializeObject<dynamic>(rawResponse);
            // Check if the response indicates an error (source not found)
            if (response?.error != null) {
                _logger.Log(LogLevel.Debug,
                            $"Source '{sourceName}' not found in scene '{sceneName}': {response.error}");
                return "not_found"; // Return a sentinel value to indicate not found
            }
            _logger.Log(LogLevel.Debug,
                        $"Successfully retrieved scene item ID for source '{sourceName}' in scene '{sceneName}'.");
            return response;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error,
                        $"Error retrieving scene item ID for source '{sourceName}' in scene '{sceneName}'.",
                        ex);
            return null;
        }
    }
    private bool AddCliparinoToScene() {
        try {
            var currentScene = CurrentScene();
            var payload = new Payload {
                RequestType = "CreateSceneItem",
                RequestData = new {
                    sceneName = currentScene, sourceName = CliparinoSourceName, sceneItemEnabled = true
                }
            };
            var response = _cph.ObsSendRaw(payload.RequestType, JsonConvert.SerializeObject(payload.RequestData));
            if (!ValidateObsOperation("CreateSceneItem", response, $"add Cliparino to scene '{currentScene}'"))
                return false;
            // Double-check that Cliparino is actually in the current scene
            if (CliparinoInCurrentScene()) {
                _logger.Log(LogLevel.Info, $"Added Cliparino to scene '{currentScene}'.");
                return true;
            } else {
                _logger.Log(LogLevel.Error,
                            $"Adding Cliparino to scene '{currentScene}' appeared successful but Cliparino is not in the scene.");
                return false;
            }
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error adding Cliparino to current scene.", ex);
            return false;
        }
    }
    private bool AddPlayerToCliparino() {
        try {
            if (Dimensions == null) {
                _logger.Log(LogLevel.Error, "CPHInline.Dimensions is null. Cannot add Player to Cliparino.");
                return false;
            }
            var height = Dimensions.Height;
            var width = Dimensions.Width;
            _logger.Log(LogLevel.Debug, $"Adding Player source to Cliparino with dimensions: {width}x{height}.");
            var inputSettings = new {
                fps = 60,
                fps_custom = true,
                height,
                reroute_audio = true,
                restart_when_active = true,
                shutdown = true,
                url = InactiveUrl,
                webpage_control_level = 2,
                width
            };
            var payload = new Payload {
                RequestType = "CreateInput",
                RequestData = new {
                    sceneName = CliparinoSourceName,
                    inputName = PlayerSourceName,
                    inputKind = "browser_source",
                    inputSettings,
                    sceneItemEnabled = true
                }
            };
            var response = _cph.ObsSendRaw(payload.RequestType, JsonConvert.SerializeObject(payload.RequestData));
            if (!ValidateObsOperation("CreateInput",
                                      response,
                                      $"create browser source '{PlayerSourceName}' in scene '{CliparinoSourceName}'"))
                return false;
            // Double-check that the player actually exists
            if (PlayerExists()) {
                _logger.Log(LogLevel.Info,
                            $"Browser source '{PlayerSourceName}' added to scene '{CliparinoSourceName}' with URL '{InactiveUrl}'.");
                return true;
            } else {
                _logger.Log(LogLevel.Error,
                            $"Browser source '{PlayerSourceName}' creation appeared successful but source does not exist.");
                return false;
            }
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "An error occurred while adding the Player source to Cliparino.", ex);
            return false;
        }
    }
    private bool CreateCliparinoScene() {
        try {
            var payload = new Payload {
                RequestType = "CreateScene", RequestData = new { sceneName = CliparinoSourceName }
            };
            var response = _cph.ObsSendRaw(payload.RequestType, JsonConvert.SerializeObject(payload.RequestData));
            if (!ValidateObsOperation("CreateScene", response, $"create scene '{CliparinoSourceName}'")) return false;
            // Double-check that the scene actually exists
            if (CliparinoExists()) {
                _logger.Log(LogLevel.Info, $"Scene '{CliparinoSourceName}' created successfully.");
                return true;
            } else {
                _logger.Log(LogLevel.Error,
                            $"Scene '{CliparinoSourceName}' creation appeared successful but scene does not exist.");
                return false;
            }
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"Error in CreateCliparinoScene: {ex.Message}", ex);
            return false;
        }
    }
    private bool CliparinoExists() {
        try {
            return SceneExists(CliparinoSourceName);
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error checking if Cliparino exists in OBS.", ex);
            return false;
        }
    }
    private bool SceneExists(string sceneName) {
        try {
            var rawResponse = _cph.ObsSendRaw("GetSceneList", "{}");
            if (!ValidateObsOperation("GetSceneList", rawResponse, "retrieve scene list")) {
                _logger.Log(LogLevel.Error, "Failed to retrieve scene list from OBS.");
                return false;
            }
            var response = JsonConvert.DeserializeObject<dynamic>(rawResponse);
            var scenes = response?.scenes;
            if (scenes == null) {
                _logger.Log(LogLevel.Error, "Scene list is null or empty in OBS response.");
                return false;
            }
            _logger.Log(LogLevel.Debug, $"Found {((IEnumerable<dynamic>)scenes).Count()} scenes in OBS.");
            var sceneExists = false;
            foreach (var scene in scenes) {
                if ((string)scene.sceneName != sceneName) continue;
                sceneExists = true;
                break;
            }
            _logger.Log(LogLevel.Debug,
                        sceneExists
                            ? $"Scene '{sceneName}' exists in OBS."
                            : $"Scene '{sceneName}' does not exist in OBS.");
            return sceneExists;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"An error occurred while checking if scene '{sceneName}' exists.", ex);
            return false;
        }
    }
    private bool ConfigureAudioSettings() {
        if (!PlayerExists()) {
            _logger.Log(LogLevel.Warn, "Cannot configure audio settings because Player source doesn't exist.");
            return false;
        }
        try {
            var allSuccessful = true;
            // Set the monitor type
            var monitorTypePayload = GenerateSetInputAudioMonitorTypePayload("OBS_MONITORING_TYPE_MONITOR_AND_OUTPUT");
            var monitorTypeResponse = _cph.ObsSendRaw(monitorTypePayload.RequestType,
                                                      JsonConvert.SerializeObject(monitorTypePayload.RequestData));
            if (!ValidateObsOperation("SetInputAudioMonitorType",
                                      monitorTypeResponse,
                                      "set monitor type for Player source")) {
                _logger.Log(LogLevel.Error, "Failed to set monitor type for the Player source.");
                allSuccessful = false;
            }
            // Set input volume
            var inputVolumePayload = GenerateSetInputVolumePayload(-12);
            var inputVolumeResponse = _cph.ObsSendRaw(inputVolumePayload.RequestType,
                                                      JsonConvert.SerializeObject(inputVolumePayload.RequestData));
            if (!ValidateObsOperation("SetInputVolume", inputVolumeResponse, "set volume for Player source")) {
                _logger.Log(LogLevel.Warn, "Failed to set volume for the Player source.");
                allSuccessful = false;
            }
            // Add gain filter
            var gainFilterPayload = GenerateGainFilterPayload(3);
            var gainFilterResponse = _cph.ObsSendRaw(gainFilterPayload.RequestType,
                                                     JsonConvert.SerializeObject(gainFilterPayload.RequestData));
            if (!ValidateObsOperation("CreateSourceFilter", gainFilterResponse, "add Gain filter to Player source")) {
                _logger.Log(LogLevel.Warn, "Failed to add Gain filter to the Player source.");
                allSuccessful = false;
            }
            // Add compressor filter
            var compressorFilterPayload = GenerateCompressorFilterPayload();
            var compressorFilterResponse = _cph.ObsSendRaw(compressorFilterPayload.RequestType,
                                                           JsonConvert.SerializeObject(compressorFilterPayload
                                                                                           .RequestData));
            if (!ValidateObsOperation("CreateSourceFilter",
                                      compressorFilterResponse,
                                      "add Compressor filter to Player source")) {
                _logger.Log(LogLevel.Warn, "Failed to add Compressor filter to the Player source.");
                allSuccessful = false;
            }
            if (allSuccessful)
                _logger.Log(LogLevel.Info, "Audio configuration for Player source completed successfully.");
            else
                _logger.Log(LogLevel.Warn, "Audio configuration completed with some failures.");
            return allSuccessful;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "An error occurred during audio configuration setup.", ex);
            return false;
        }
    }
    private static IPayload GenerateCompressorFilterPayload() {
        return new Payload {
            RequestType = "CreateSourceFilter",
            RequestData = new {
                sourceName = "Player",
                filterName = "Compressor",
                filterKind = "compressor_filter",
                filterSettings = new {
                    attack_time = 69,
                    output_gain = 0,
                    ratio = 4,
                    release_time = 120,
                    sidechain_source = "Mic/Aux",
                    threshold = -28
                }
            }
        };
    }
    private static IPayload GenerateGainFilterPayload(double gainValue) {
        return new Payload {
            RequestType = "CreateSourceFilter",
            RequestData = new {
                sourceName = "Player",
                filterName = "Gain",
                filterKind = "gain_filter",
                filterSettings = new { db = gainValue }
            }
        };
    }
    private static IPayload GenerateSetInputVolumePayload(double volumeValue) {
        return new Payload {
            RequestType = "SetInputVolume", RequestData = new { inputName = "Player", inputVolumeDb = volumeValue }
        };
    }
    private static IPayload GenerateSetInputAudioMonitorTypePayload(string monitorType) {
        return new Payload {
            RequestType = "SetInputAudioMonitorType", RequestData = new { inputName = "Player", monitorType }
        };
    }
    private bool ValidateObsOperation(string operationName, object response, string operationDescription) {
        try {
            if (response == null) {
                _logger.Log(LogLevel.Error,
                            $"OBS operation '{operationName}' returned null response while attempting to {operationDescription}.");
                return false;
            }
            var responseString = response.ToString();
            // Empty string or whitespace typically indicates success for many OBS operations
            if (string.IsNullOrWhiteSpace(responseString)) {
                _logger.Log(LogLevel.Debug,
                            $"OBS operation '{operationName}' completed successfully (empty response) for: {operationDescription}.");
                return true;
            }
            // Check for common success patterns
            if (responseString == "{}" || responseString.Equals("true", StringComparison.OrdinalIgnoreCase)) {
                _logger.Log(LogLevel.Debug,
                            $"OBS operation '{operationName}' completed successfully for: {operationDescription}.");
                return true;
            }
            // Try to parse as JSON to check for error indicators
            try {
                var jsonResponse = JsonConvert.DeserializeObject<JObject>(responseString);
                // Check for common error patterns in OBS WebSocket responses
                if (jsonResponse?["error"] != null || jsonResponse?["status"] != null) {
                    var errorMsg = jsonResponse["error"]?.ToString() ?? jsonResponse["status"]?.ToString();
                    _logger.Log(LogLevel.Error,
                                $"OBS operation '{operationName}' failed while attempting to {operationDescription}. Error: {errorMsg}");
                    return false;
                }
                // If we have a structured response without explicit errors, consider it successful
                _logger.Log(LogLevel.Debug,
                            $"OBS operation '{operationName}' completed with structured response for: {operationDescription}.");
                return true;
            } catch {
                // If JSON parsing fails, check if the response looks like an error message
                if (responseString.ToLower().Contains("error") || responseString.ToLower().Contains("fail")) {
                    _logger.Log(LogLevel.Error,
                                $"OBS operation '{operationName}' failed while attempting to {operationDescription}. Response: {responseString}");
                    return false;
                }
                // For non-JSON responses that don't look like errors, assume success but log for investigation
                _logger.Log(LogLevel.Debug,
                            $"OBS operation '{operationName}' completed with non-JSON response for: {operationDescription}. Response: {responseString}");
                return true;
            }
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error,
                        $"Error validating OBS operation '{operationName}' response while attempting to {operationDescription}.",
                        ex);
            return false;
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
}

public class ClipManager {
    private static readonly Dictionary<string, CachedClipData> ClipCache = new Dictionary<string, CachedClipData>();
    private readonly object _cacheLock = new object();
    private readonly IInlineInvokeProxy _cph;
    private readonly CPHLogger _logger;
    private readonly TwitchApiManager _twitchApiManager;
    private string _lastClipUrl;
    public ClipManager(IInlineInvokeProxy cph, CPHLogger logger, TwitchApiManager twitchApiManager) {
        _cph = cph ?? throw new ArgumentNullException(nameof(cph));
        _logger = logger;
        _twitchApiManager = twitchApiManager;
    }
    public string GetLastClipUrl() {
        return _lastClipUrl ?? _cph.GetGlobalVar<string>("last_clip_url");
    }
    public void SetLastClipUrl(string url) {
        if (string.IsNullOrWhiteSpace(url)) return;
        _lastClipUrl = url;
        _cph.SetGlobalVar("last_clip_url", url);
    }
    public async Task<ClipData> GetRandomClipAsync(string userName, ClipSettings clipSettings) {
        clipSettings.Deconstruct(out var featuredOnly, out var maxClipSeconds, out var clipAgeDays);
        _logger.Log(LogLevel.Debug,
                    $"Getting random clip for userName: {userName}, featuredOnly: {featuredOnly}, maxSeconds: {maxClipSeconds}, ageDays: {clipAgeDays}");
        try {
            var twitchUser = _cph.TwitchGetExtendedUserInfoByLogin(userName);
            if (twitchUser == null) {
                _logger.Log(LogLevel.Warn, $"Twitch user not found for userName: {userName}");
                return null;
            }
            var userId = twitchUser.UserId;
            var validPeriods = new[] { 1, 7, 30, 365, 36500 };
            return await Task.Run(() => {
                                      foreach (var period in validPeriods.Where(p => p >= clipAgeDays)) {
                                          var clips = RetrieveClips(userId, period).ToList();
                                          if (!clips.Any()) continue;
                                          var clip = GetMatchingClip(clips, featuredOnly, maxClipSeconds, userId);
                                          if (clip != null) return clip;
                                      }
                                      _logger.Log(LogLevel.Warn,
                                                  $"No clips found for userName: {userName} after exhausting all periods and filter combinations.");
                                      return null;
                                  });
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "An error occurred while getting random clip.", ex);
            return null;
        }
    }
    private IEnumerable<ClipData> RetrieveClips(string userId, int clipAgeDays) {
        _logger.Log(LogLevel.Debug, $"RetrieveClips called with userId: {userId}, clipAgeDays: {clipAgeDays}");
        try {
            var endDate = DateTime.UtcNow;
            var startDate = endDate.AddDays(-clipAgeDays);
            return _cph.GetClipsForUserById(userId, startDate, endDate);
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "An error occurred while retrieving clips.", ex);
            return Array.Empty<ClipData>();
        }
    }
    private ClipData GetMatchingClip(IEnumerable<ClipData> clips, bool featuredOnly, int maxSeconds, string userId) {
        clips = clips.ToList();
        var matchingClips = FilterClips(clips, featuredOnly, maxSeconds);
        if (matchingClips.Any()) {
            var selectedClip = SelectRandomClip(matchingClips);
            _logger.Log(LogLevel.Debug, $"Selected clip: {selectedClip.Url}");
            return selectedClip;
        }
        _logger.Log(LogLevel.Debug, $"No matching clips found with the current filter (featuredOnly: {featuredOnly}).");
        if (!featuredOnly) return null;
        matchingClips = FilterClips(clips, false, maxSeconds);
        if (matchingClips.Any()) {
            var selectedClip = SelectRandomClip(matchingClips);
            _logger.Log(LogLevel.Debug, $"Selected clip without featuredOnly: {selectedClip.Url}");
            return selectedClip;
        }
        _logger.Log(LogLevel.Debug, $"No matching clips found without featuredOnly for userId: {userId}");
        return null;
    }
    private static List<ClipData> FilterClips(IEnumerable<ClipData> clips, bool featuredOnly, int maxSeconds) {
        return clips.Where(c => (!featuredOnly || c.IsFeatured) && c.Duration <= maxSeconds).ToList();
    }
    private static ClipData SelectRandomClip(List<ClipData> clips) {
        return clips[new Random().Next(clips.Count)];
    }
    public async Task<ClipData> GetClipDataAsync(string clipUrl) {
        if (string.IsNullOrWhiteSpace(clipUrl)) {
            _logger.Log(LogLevel.Error, "Invalid clip URL provided.");
            return null;
        }
        var clipId = ExtractClipId(clipUrl);
        if (!string.IsNullOrWhiteSpace(clipId)) return await FetchClipDataAsync(clipId);
        _logger.Log(LogLevel.Error, "Could not extract clip ID from URL.");
        return null;
    }
    private static string ExtractClipId(string clipUrl) {
        var segments = clipUrl.Split('/');
        return segments.Length > 0 ? segments.Last().Trim() : null;
    }
    private async Task<ClipData> FetchClipDataAsync(string clipId) {
        try {
            var clipData = await _twitchApiManager.FetchClipById(clipId);
            if (clipData == null) {
                _logger.Log(LogLevel.Error, $"No clip data found for ID {clipId}.");
                return null;
            }
            _logger.Log(LogLevel.Info, $"Retrieved clip '{clipData.Title}' by {clipData.CreatorName}.");
            return clipData;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error retrieving clip data.", ex);
            return null;
        }
    }
    public async Task<ClipData> SearchClipsWithThresholdAsync(string broadcasterId, string searchTerm) {
        _logger.Log(LogLevel.Debug,
                    $"Searching clips for broadcaster ID: {broadcasterId} using search term: '{searchTerm}'");
        try {
            string cursor = null;
            var iterationCount = 0;
            const int maxPages = 50;
            ClipData bestMatch = null;
            double bestMatchScore = 0;
            do {
                iterationCount++;
                _logger.Log(LogLevel.Debug, $"Fetching clips. Page: {iterationCount}, Cursor: {cursor ?? "<null>"}");
                var response = await _twitchApiManager.FetchClipsAsync(broadcasterId, cursor);
                if (response?.Data == null || response.Data.Length == 0) {
                    _logger.Log(LogLevel.Debug, "No clips returned from API.");
                    break;
                }
                _logger.Log(LogLevel.Debug, $"Raw pagination object: {response.Pagination}");
                _logger.Log(LogLevel.Debug, $"Fetched {response.Data.Length} clips from Twitch API.");
                // Process clips looking for matches
                var searchWords = PreprocessText(searchTerm);
                foreach (var clip in response.Data) {
                    var clipTitleWords = PreprocessText(clip.Title);
                    var similarity = ComputeWordOverlap(searchWords, clipTitleWords);
                    _logger.Log(LogLevel.Debug, $"Clip '{clip.Title}': similarity score: {similarity}");
                    // If we find an exact match, return it immediately
                    if (similarity >= 0.99) {
                        _logger.Log(LogLevel.Debug, "Found exact match!");
                        AddToCache(searchTerm, clip);
                        return clip;
                    }
                    // Otherwise, keep track of the best match so far
                    if (!(similarity > bestMatchScore)) continue;
                    bestMatchScore = similarity;
                    bestMatch = clip;
                }
                cursor = response.Pagination?.Cursor;
                if (string.IsNullOrEmpty(cursor)) {
                    _logger.Log(LogLevel.Debug, "No cursor provided by API for next page.");
                    break;
                }
                _logger.Log(LogLevel.Debug, $"Next cursor: {cursor}");
                if (iterationCount < maxPages) continue;
                _logger.Log(LogLevel.Debug, "Reached maximum number of pages to search.");
                break;
            } while (true);
            // If we found any decent match, return it
            if (bestMatch != null && bestMatchScore >= 0.5) {
                _logger.Log(LogLevel.Debug, $"Returning best match found (score: {bestMatchScore})");
                AddToCache(searchTerm, bestMatch);
                return bestMatch;
            }
            _logger.Log(LogLevel.Warn, "No matching clip found after searching all pages.");
            return null;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error occurred while searching for clip.", ex);
            return null;
        }
    }
    private static List<string> PreprocessText(string text) {
        return text.ToLowerInvariant()
                   .Split(new[] { ' ', '\t', '\n', '\r', '.', ',', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                   .ToList();
    }
    private static double ComputeWordOverlap(List<string> searchWords, List<string> clipTitleWords) {
        if (searchWords == null || clipTitleWords == null || searchWords.Count == 0 || clipTitleWords.Count == 0)
            return 0;
        // For each search word, check if any clip title word contains it
        var matchCount =
            searchWords.Count(searchWord => clipTitleWords.Any(titleWord => titleWord.Contains(searchWord)));
        return (double)matchCount / searchWords.Count;
    }
    public static ClipData GetFromCache(string searchTerm) {
        if (!ClipCache.TryGetValue(searchTerm, out var cachedData)) return null;
        cachedData.LastAccessed = DateTime.UtcNow;
        cachedData.SearchFrequency++;
        return cachedData.Clip;
    }
    private void AddToCache(string searchTerm, ClipData clipData) {
        ClipCache[searchTerm] =
            new CachedClipData { Clip = clipData, SearchFrequency = 1, LastAccessed = DateTime.UtcNow };
        _logger.Log(LogLevel.Debug, $"Added '{clipData.Title}' to cache for search term: {searchTerm}");
    }
    public bool CleanCache() {
        try {
            lock (_cacheLock) {
                var expirationTime = TimeSpan.FromDays(CPHInline.GetArgument(_cph, "ClipCacheExpirationDays", 30));
                var now = DateTime.UtcNow;
                var toRemove = ClipCache.Where(entry => now - entry.Value.LastAccessed > expirationTime)
                                        .Select(entry => entry.Key)
                                        .ToList();
                foreach (var key in toRemove) {
                    _logger.Log(LogLevel.Debug, $"Removing expired cache item: {key}");
                    ClipCache.Remove(key);
                }
                return true;
            }
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error cleaning cache.", ex);
            return false;
        }
    }
    public class ClipsResponse {
        public ClipsResponse() {
            Data = Array.Empty<ClipData>();
            Pagination = new PaginationObject();
        }
        public ClipsResponse(ClipData[] data = null, string cursor = "") {
            Data = data ?? Array.Empty<ClipData>();
            Pagination = new PaginationObject(cursor);
        }
        [JsonProperty("data")]
        public ClipData[] Data { get; set; }
        [JsonProperty("pagination")]
        public PaginationObject Pagination { get; set; }
    }
    public class PaginationObject {
        public PaginationObject(string cursor = "") {
            Cursor = cursor;
        }
        [JsonProperty("cursor")]
        public string Cursor { get; set; }
        public override string ToString() {
            return JsonConvert.SerializeObject(this);
        }
    }
    public class ClipSettings {
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
    private class CachedClipData {
        public ClipData Clip { get; set; }
        public int SearchFrequency { get; set; }
        public DateTime LastAccessed { get; set; }
    }
}

public class TwitchApiManager {
    private readonly IInlineInvokeProxy _cph;
    private readonly CPHLogger _logger;
    private readonly OAuthInfo _oauthInfo;
    public TwitchApiManager(IInlineInvokeProxy cph, CPHLogger logger) {
        _cph = cph ?? throw new ArgumentNullException(nameof(cph));
        _logger = logger;
        _oauthInfo = new OAuthInfo(cph.TwitchClientId, cph.TwitchOAuthToken);
    }
    private static HttpManager HTTPManager => CPHInline.GetHttpManager();
    public void SendShoutout(string username, string message) {
        if (string.IsNullOrWhiteSpace(username)) {
            _logger.Log(LogLevel.Error, "No username provided for shoutout.");
            return;
        }
        username = ValidationHelper.SanitizeUsername(username);
        var user = _cph.TwitchGetExtendedUserInfoByLogin(username);
        if (user == null) {
            _logger.Log(LogLevel.Error, $"No user found with name '{username}'.");
            return;
        }
        var shoutoutMessage = !string.IsNullOrWhiteSpace(message) ? FormatShoutoutMessage(user, message) : "";
        _cph.SendMessage(shoutoutMessage);
        _cph.TwitchSendShoutoutByLogin(username);
        _logger.Log(LogLevel.Info, $"Shoutout sent for {username}.");
    }
    public async Task<ClipManager.ClipsResponse> FetchClipsAsync(string broadcasterId, string cursor = null) {
        var url = BuildClipsApiUrl(broadcasterId, cursor);
        _logger.Log(LogLevel.Debug, $"Calling Twitch API with URL: {url}");
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        try {
            ConfigureHttpRequestHeaders();
            var response = await HTTPManager.Client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var responseBody = await response.Content.ReadAsStringAsync();
            _logger.Log(LogLevel.Debug, $"API Response: {responseBody}");
            return JsonConvert.DeserializeObject<ClipManager.ClipsResponse>(responseBody);
        } catch (HttpRequestException ex) {
            _logger.Log(LogLevel.Error, "Error occurred while fetching clips.", ex);
            if (ex.Data.Count <= 0) return null;
            _logger.Log(LogLevel.Debug, "Details:\n{ex.Data.ToString()}");
            foreach (DictionaryEntry de in ex.Data) _logger.Log(LogLevel.Debug, $"Key: {de.Key}, Value: {de.Value}");
            return null;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error occurred while fetching clips.", ex);
            return null;
        } finally {
            _logger.Log(LogLevel.Info, "Finished fetching clips.");
        }
    }
    private static string BuildClipsApiUrl(string broadcasterId, string cursor = "", int first = 20) {
        var url = $"https://api.twitch.tv/helix/clips?broadcaster_id={broadcasterId}&first={first}";
        if (!string.IsNullOrWhiteSpace(cursor)) url += $"&after={cursor}";
        return url;
    }
    private static string FormatShoutoutMessage(TwitchUserInfoEx user, string message) {
        return message.Replace("[[userName]]", user.UserName).Replace("[[userGame]]", user.Game);
    }
    public async Task<ClipData> FetchClipById(string clipId) {
        return await FetchDataAsync<ClipData>($"clips?id={clipId}");
    }
    public async Task<GameData> FetchGameById(string gameId) {
        return await FetchDataAsync<GameData>($"games?id={gameId}");
    }
    private async Task<T> FetchDataAsync<T>(string endpoint) {
        ValidateEndpoint(endpoint);
        ValidateOAuthInfo();
        var completeUrl = GetCompleteUrl(endpoint);
        _logger.Log(LogLevel.Debug, $"Preparing to make GET request to endpoint: {completeUrl}");
        try {
            var content = await SendHttpRequestAsync(endpoint);
            if (string.IsNullOrWhiteSpace(content)) return default;
            _logger.Log(LogLevel.Debug, $"Response content: {content}");
            return DeserializeApiResponse<T>(content, endpoint);
        } catch (JsonException ex) {
            _logger.Log(LogLevel.Error, $"JSON deserialization error for response from {endpoint}.", ex);
            return default;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"Unexpected error in FetchDataAsync for endpoint: {endpoint}", ex);
            return default;
        }
    }
    public async Task<GameData> GetGameDataAsync(string gameId) {
        try {
            var endpoint = $"games?id={gameId}";
            var content = await SendHttpRequestAsync(endpoint);
            if (string.IsNullOrEmpty(content)) {
                _logger.Log(LogLevel.Error, $"Failed to retrieve game data for ID: {gameId}");
                return null;
            }
            var response = JsonConvert.DeserializeObject<GameResponse>(content);
            return response?.Data?.FirstOrDefault();
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"Error retrieving game data for ID: {gameId}", ex);
            return null;
        }
    }
    private static void ValidateEndpoint(string endpoint) {
        const string blankClipTemplate = "clips?id=";
        const string blankGameTemplate = "games?id=";
        if (string.IsNullOrWhiteSpace(endpoint)
            || endpoint.Equals(blankClipTemplate)
            || endpoint.Equals(blankGameTemplate))
            throw new ArgumentNullException(nameof(endpoint), "Endpoint cannot be null or empty.");
    }
    private void ValidateOAuthInfo() {
        if (string.IsNullOrWhiteSpace(_oauthInfo.TwitchClientId)
            || string.IsNullOrWhiteSpace(_oauthInfo.TwitchOAuthToken))
            throw new InvalidOperationException("Twitch OAuth information is missing or invalid.");
    }
    private static string GetCompleteUrl(string endpoint) {
        var baseAddress = HTTPManager?.Client?.BaseAddress?.ToString() ?? "https://www.google.com/search?q=";
        return new Uri(new Uri(baseAddress), endpoint).ToString();
    }
    private T DeserializeApiResponse<T>(string content, string endpoint) {
        var apiResponse = JsonConvert.DeserializeObject<TwitchApiResponse<T>>(content);
        if (apiResponse?.Data != null && apiResponse.Data.Length > 0) {
            _logger.Log(LogLevel.Info, "Successfully retrieved and deserialized data from Twitch API.");
            return apiResponse.Data[0];
        }
        _logger.Log(LogLevel.Error, $"No data returned from Twitch API for endpoint: {endpoint}");
        return default;
    }
    private void ConfigureHttpRequestHeaders() {
        var client = HTTPManager.Client;
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("Client-ID", _oauthInfo.TwitchClientId);
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_oauthInfo.TwitchOAuthToken}");
    }
    private async Task<string> SendHttpRequestAsync(string endpoint, int maxRetries = 1) {
        const int timeoutSeconds = 30;
        const string baseUrl = "https://api.twitch.tv/helix/";
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds))) {
            var completeUrl = endpoint.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                                  ? endpoint
                                  : $"{baseUrl}{endpoint}";
            for (var attempt = 0; attempt <= maxRetries; attempt++)
                try {
                    ConfigureHttpRequestHeaders();
                    LogRequestHeaders();
                    _logger.Log(LogLevel.Debug, $"Making request to Twitch API: {endpoint}");
                    using (var request = new HttpRequestMessage(HttpMethod.Get, completeUrl)) {
                        using (var response = await HTTPManager.Client.SendAsync(request, cts.Token)) {
                            var responseBody = await response.Content.ReadAsStringAsync();
                            _logger.Log(LogLevel.Debug,
                                        $"Received response: {response.StatusCode} ({(int)response.StatusCode})\nContent: {responseBody}");
                            if (response.IsSuccessStatusCode) return responseBody;
                            // Handle specific status codes
                            if (!await HandleErrorStatusCode(response,
                                                             responseBody,
                                                             attempt,
                                                             maxRetries,
                                                             endpoint,
                                                             cts.Token))
                                break;
                        }
                    }
                } catch (TaskCanceledException ex) {
                    _logger.Log(LogLevel.Error,
                                $"Request timed out while calling {endpoint}. Attempt {attempt + 1} of {maxRetries + 1}",
                                ex);
                    if (attempt == maxRetries) return null;
                } catch (HttpRequestException ex) {
                    _logger.Log(LogLevel.Error,
                                $"HTTP request error while calling {endpoint}. Attempt {attempt + 1} of {maxRetries + 1}",
                                ex);
                    if (attempt == maxRetries) return null;
                } catch (OperationCanceledException ex) {
                    _logger.Log(LogLevel.Error,
                                $"Operation was canceled while calling {endpoint}. Attempt {attempt + 1} of {maxRetries + 1}",
                                ex);
                    if (attempt == maxRetries) return null;
                }
            return null;
        }
    }
    private void LogRequestHeaders() {
        foreach (var header in HTTPManager.Client.DefaultRequestHeaders)
            _logger.Log(LogLevel.Debug,
                        header.Key == "Authorization"
                            ? $"Header: {header.Key}: {string.Join(",", OAuthInfo.ObfuscateString(_oauthInfo.TwitchOAuthToken))}"
                            : $"Header: {header.Key}: {string.Join(",", header.Value)}");
    }
    private async Task<bool> HandleErrorStatusCode(HttpResponseMessage response,
                                                   string responseBody,
                                                   int attempt,
                                                   int maxRetries,
                                                   string endpoint,
                                                   CancellationToken token) {
        const int defaultRetryDelaySeconds = 5;
        switch ((int)response.StatusCode) {
            case 401: // Unauthorized
                if (attempt == maxRetries) return false;
                _logger.Log(LogLevel.Debug, "Received 401, attempting to refresh token...");
                // Force Streamer Bot to refresh the token by making it perform a Twitch API call
                var refreshSuccess = await TriggerStreamerBotTokenRefresh();
                if (!refreshSuccess) {
                    _logger.Log(LogLevel.Error, "Failed to trigger token refresh through Streamer Bot.");
                    return false;
                }
                var newToken = _cph.TwitchOAuthToken;
                if (newToken == _oauthInfo.TwitchOAuthToken) {
                    _logger.Log(LogLevel.Warn,
                                "Refreshed token is identical to current token - refresh may have failed or token may still be valid.");
                    return false;
                }
                var oldTokenObfuscated = OAuthInfo.ObfuscateString(_oauthInfo.TwitchOAuthToken);
                var newTokenObfuscated = OAuthInfo.ObfuscateString(newToken);
                _oauthInfo.TwitchOAuthToken = newToken;
                _logger.Log(LogLevel.Info,
                            $"Token has been refreshed successfully. Old: {oldTokenObfuscated} -> New: {newTokenObfuscated}");
                return true;
            case 429: // Too Many Requests
                if (attempt == maxRetries) return false;
                var retryAfter = response.Headers.RetryAfter;
                await Task.Delay(retryAfter?.Delta ?? TimeSpan.FromSeconds(defaultRetryDelaySeconds), token);
                return true;
        }
        _logger.Log(LogLevel.Error,
                    $"Request to Twitch API failed: {response.ReasonPhrase} (Status Code: {(int)response.StatusCode}, URL: {endpoint})");
        _logger.Log(LogLevel.Debug, $"Response content: {responseBody}");
        if (attempt == maxRetries)
            throw new
                HttpRequestException($"Request to Twitch API failed: {response.ReasonPhrase} (Status Code: {(int)response.StatusCode})");
        return false;
    }
    private async Task<bool> TriggerStreamerBotTokenRefresh() {
        try {
            _logger.Log(LogLevel.Debug, "Attempting to trigger Streamer Bot token refresh...");
            // Strategy 1: Try to get broadcaster info using common broadcaster usernames or fallback
            var triggerSuccess = await TryTriggerRefreshWithApiCall();
            if (triggerSuccess) {
                _logger.Log(LogLevel.Debug, "Successfully triggered Streamer Bot API call for token refresh.");
                // Give Streamer Bot a moment to process the token refresh
                await Task.Delay(TimeSpan.FromMilliseconds(500));
                return true;
            }
            _logger.Log(LogLevel.Warn, "All token refresh trigger attempts failed.");
            return false;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Failed to trigger Streamer Bot token refresh.", ex);
            return false;
        }
    }
    public async Task<bool> RefreshTokenAsync() {
        _logger.Log(LogLevel.Info, "Manual token refresh requested.");
        var currentToken = _oauthInfo.TwitchOAuthToken;
        var refreshSuccess = await TriggerStreamerBotTokenRefresh();
        if (!refreshSuccess) {
            _logger.Log(LogLevel.Error, "Manual token refresh failed to trigger Streamer Bot refresh.");
            return false;
        }
        var newToken = _cph.TwitchOAuthToken;
        if (newToken == currentToken) {
            _logger.Log(LogLevel.Info, "Manual token refresh completed - token was already current.");
            return true; // This is actually a success - the token was already valid
        }
        _oauthInfo.TwitchOAuthToken = newToken;
        _logger.Log(LogLevel.Info,
                    $"Manual token refresh successful. Token updated from {OAuthInfo.ObfuscateString(currentToken)} to {OAuthInfo.ObfuscateString(newToken)}");
        return true;
    }
    private async Task<bool> TryTriggerRefreshWithApiCall() {
        // Try to derive broadcaster username from the client context
        // This is a heuristic approach - in most cases, Streamer Bot runs for the broadcaster
        try {
            // Strategy 1: Try to use a well-known Twitch username that's likely to exist
            // and won't cause issues (like 'twitch' - the official Twitch account)
            var testUser = await Task.Run(() => _cph.TwitchGetExtendedUserInfoByLogin("twitch"));
            if (testUser != null) {
                _logger.Log(LogLevel.Debug, "Token refresh triggered using test user lookup.");
                return true;
            }
        } catch (Exception ex) {
            _logger.Log(LogLevel.Debug, $"Test user lookup failed: {ex.Message}");
        }
        // Strategy 2: If we can't use a test user, try to trigger any other Twitch API method
        // that might cause token refresh - this is a fallback approach
        try {
            // Use the existing shoutout functionality, but with a no-op approach
            // We'll attempt to get user info for a known invalid user to trigger the API call
            // without actually affecting anything
            _ = await Task.Run(() => _cph.TwitchGetExtendedUserInfoByLogin("__cliparino_token_refresh_trigger__"));
            // This call will likely return null, but it will trigger Streamer Bot to make a Twitch API call
            _logger.Log(LogLevel.Debug, "Token refresh triggered using fallback method.");
            return true;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Debug, $"Fallback token refresh trigger failed: {ex.Message}");
        }
        return false;
    }
}
public class OAuthInfo {
    public OAuthInfo(string twitchClientId, string twitchOAuthToken) {
        TwitchClientId = twitchClientId
                         ?? throw new ArgumentNullException(nameof(twitchClientId), "Client ID cannot be null.");
        TwitchOAuthToken = twitchOAuthToken
                           ?? throw new ArgumentNullException(nameof(twitchOAuthToken), "OAuth token cannot be null.");
    }
    public string TwitchClientId { get; set; }
    public string TwitchOAuthToken { get; set; }
    public override string ToString() {
        return $"Client ID: {TwitchClientId}, OAuth Token: {ObfuscateString(TwitchOAuthToken)}";
    }
    public static string ObfuscateString(string input) {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        const string obfuscationSymbol = "...";
        var visibleLength = Math.Max(1, input.Length / 3);
        var prefixLength = visibleLength / 2;
        var suffixLength = visibleLength - prefixLength;
        return input.Length <= visibleLength + obfuscationSymbol.Length
                   ? obfuscationSymbol
                   : $"{input.Substring(0, prefixLength)}{obfuscationSymbol}{input.Substring(input.Length - suffixLength)}";
    }
}
public class TwitchApiResponse<T> {
    public TwitchApiResponse(T[] data) {
        Data = data ?? Array.Empty<T>();
    }
    public T[] Data { get; }
}
public class GameResponse {
    [JsonProperty("data")] public List<GameData> Data { get; set; }
}
public class GameData {
    [JsonProperty("id")] public string Id { get; set; }
    [JsonProperty("name")] public string Name { get; set; }
    [JsonProperty("box_art_url")] public string BoxArtUrl { get; set; }
}

public class HttpManager {
    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Point {
        public int X;
        public int Y;
    }
    private static readonly Dictionary<string, uint> WinMsg = new Dictionary<string, uint> {
        { "WM_LBUTTONDOWN", CliparinoConstants.WindowsApi.WM_LBUTTONDOWN },
        { "WM_LBUTTONUP", CliparinoConstants.WindowsApi.WM_LBUTTONUP },
        { "WM_RBUTTONDOWN", CliparinoConstants.WindowsApi.WM_RBUTTONDOWN },
        { "WM_RBUTTONUP", CliparinoConstants.WindowsApi.WM_RBUTTONUP }
    };
    private const int NonceLength = CliparinoConstants.Http.NonceLength;
    private const int BasePort = CliparinoConstants.Http.BasePort;
    private const int MaxPortRetries = CliparinoConstants.Http.MaxPortRetries;
    private const string HelixApiUrl = CliparinoConstants.Http.HelixApiUrl;
    private const string CSSText = @"
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
        font: normal 400 1.5em/1.2 'OpenDyslexic', 'Open Sans', sans-serif;
    }
    .line3 {
        font: italic 100 1em/1 'OpenDyslexic', 'Open Sans', sans-serif;
    }
    ";
    private const string HTMLText = @"
    <!DOCTYPE html>
    <html lang=""en"">
    <head>
    <meta charset=""utf-8"">
    <link href=""/index.css"" rel=""stylesheet"" type=""text/css"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Cliparino</title>
    </head>
    <body>
    <div id=""twitch-embed"">
        <div class=""iframe-container"">
            <iframe allowfullscreen autoplay=""true"" controls=""false"" height=""1080"" id=""clip-iframe"" mute=""false"" preload=""auto"" 
                src=""https://clips.twitch.tv/embed?clip=[[clipId]]&nonce=[[nonce]]&autoplay=true&parent=localhost"" title=""Cliparino"" width=""1920"">
            </iframe>
            <div class=""overlay-text"" id=""overlay-text"">
                <div class=""line1"">[[streamerName]] doin' a heckin' [[gameName]] stream</div>
                <div class=""line2"">[[clipTitle]]</div>
                <div class=""line3"">by [[curatorName]]</div>
            </div>
        </div>
    </div>
    <script>
        let contentWarningDetected = false;
        let obsIntegrationAttempted = false;
        // Enhanced content warning detection with server-side automation
        const handleContentWarning = async (detectionMethod = 'unknown') => {
            if (contentWarningDetected) return;
            contentWarningDetected = true;
            console.log(`[Cliparino] Content warning detected via ${detectionMethod}`);
            // Notify the main application about content warning (server handles automation)
            try {
                const response = await fetch('/content-warning-detected', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ 
                        clipId: '[[clipId]]',
                        detectionMethod: detectionMethod,
                        timestamp: new Date().toISOString()
                    })
                });
                const result = await response.json();
                if (result && result.obsAutomation === true) {
                    showContentWarningNotification('OBS automation active');
                } else {
                    showContentWarningNotification('Manual interaction required');
                }
            } catch (error) {
                console.log('[Cliparino] Content warning notification failed:', error);
                showContentWarningNotification('Manual interaction required');
            }
        };
        const showContentWarningNotification = (method = 'detected') => {
            const notification = document.createElement('div');
            notification.id = 'content-warning-notification';
            notification.style.cssText = `
                position: absolute;
                top: 10%;
                right: 5%;
                background: rgba(255, 184, 9, 0.9);
                color: #042239;
                padding: 15px;
                border-radius: 5px;
                font-family: 'Open Sans', sans-serif;
                font-size: 1.1em;
                z-index: 1000;
                max-width: 320px;
                box-shadow: 0 4px 8px rgba(0,0,0,0.3);
                text-align: center;
            `;
            if (method === 'OBS automation active') {
                notification.innerHTML = `
                    <strong>✓ Content Warning Handled</strong><br>
                    <small>OBS automation is processing...</small>
                `;
                notification.style.background = 'rgba(76, 175, 80, 0.9)';
                notification.style.color = 'white';
            } else {
                notification.innerHTML = `
                    <strong>⚠ Content Warning</strong><br>
                    Right-click Browser Source in OBS<br>
                    → Select ""Interact""<br>
                    → Click through warning<br>
                    <small>Usually only needed once per session</small>
                `;
            }
            document.body.appendChild(notification);
            // Auto-hide after 12 seconds
            setTimeout(() => {
                if (notification.parentNode) {
                    notification.parentNode.removeChild(notification);
                }
            }, 12000);
        };
        // Detection strategies
        const detectContentWarning = () => {
            const iframe = document.getElementById('clip-iframe');
            if (!iframe) return;
            // Method 1: Try direct DOM access (will fail but worth trying)
            try {
                const iframeDoc = iframe.contentWindow.document;
                const warningSelectors = [
                    '.content-warning-overlay',
                    '.content-classification-gate-overlay', 
                    '.content-gate-overlay',
                    '.mature-content-overlay',
                    '[data-a-target=""content-classification-gate-overlay""]'
                ];
                for (const selector of warningSelectors) {
                    const element = iframeDoc.querySelector(selector);
                    if (element) {
                        handleContentWarning('DOM-access');
                        return;
                    }
                }
            } catch (e) {
                // Expected cross-origin error
            }
            // Method 2: Check iframe loading patterns
            if (iframe.src.includes('error=') || iframe.src.includes('warning=')) {
                handleContentWarning('URL-parameter');
                return;
            }
            // Method 3: Check for loading delays (content warnings often cause delays)
            const checkDelay = () => {
                if (!iframe.contentWindow || iframe.contentWindow.location.href === 'about:blank') {
                    handleContentWarning('loading-delay');
                }
            };
            setTimeout(checkDelay, 4000); // Check after 4 seconds
        };
        // Set up detection
        const iframe = document.getElementById('clip-iframe');
        // Listen for iframe load events
        iframe.addEventListener('load', () => {
            setTimeout(detectContentWarning, 1000);
            setTimeout(detectContentWarning, 3000);
        });
        // OBS Browser Source detection (informational only)
        if (window.obsstudio) {
            console.log('[Cliparino] OBS Browser Source environment detected');
        }
        // PostMessage listener for future Twitch integration
        window.addEventListener('message', (event) => {
            if (event.origin !== 'https://clips.twitch.tv') return;
            if (event.data && (event.data.type === 'mature-content-gate' || event.data.type === 'content-warning')) {
                handleContentWarning('postMessage');
            }
        });
        // Initial check
        setTimeout(detectContentWarning, 2000);
    </script>
    </body>
    </html>
    ";
    private readonly IInlineInvokeProxy _cph;
    private readonly CPHLogger _logger;
    private readonly TwitchApiManager _twitchApiManager;
    public readonly HttpClient Client;
    private ClipData _clipData;
    private int _currentPort = BasePort;
    private HttpListener _listener;
    public HttpManager(IInlineInvokeProxy cph, CPHLogger logger, TwitchApiManager twitchApiManager) {
        _cph = cph ?? throw new ArgumentNullException(nameof(cph));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _twitchApiManager = twitchApiManager;
        Client = new HttpClient { BaseAddress = new Uri(HelixApiUrl) };
    }
    public string ServerUrl { get; private set; } = $"http://localhost:{BasePort}/";
    private bool IsServerRunning => _listener?.IsListening == true;
    private static Dictionary<string, string> GenerateCORSHeaders(string nonce) {
        return new Dictionary<string, string> {
            { "Access-Control-Allow-Origin", "*" },
            { "Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS" },
            { "Access-Control-Allow-Headers", "*" }, {
                "Content-Security-Policy",
                $"script-src 'nonce-{nonce}' 'strict-dynamic'; object-src 'none'; base-uri 'none'; frame-ancestors 'self' https://clips.twitch.tv;"
            }
        };
    }
    private static Dictionary<string, string> GenerateCacheControlHeaders() {
        return new Dictionary<string, string> {
            { "Cache-Control", "no-cache, no-store, must-revalidate" }, { "Pragma", "no-cache" }, { "Expires", "0" }
        };
    }
    private async Task<bool> AutomateContentWarningHandling() {
        try {
            _logger.Log(LogLevel.Info, "Attempting to automate content warning handling via OBS...");
            // Strategy 1: Try full automation with Windows API (most comprehensive)
            var clickResult = await AutomateContentWarningClick();
            if (clickResult) {
                _logger.Log(LogLevel.Info, "Content warning automated successfully via Windows API");
                return true;
            }
            // Strategy 2: Try to refresh the browser source to potentially bypass the warning
            var refreshResult = await RefreshBrowserSource();
            if (refreshResult) {
                _logger.Log(LogLevel.Info, "Browser source refreshed successfully");
                return true;
            }
            // Strategy 3: Try to simulate interaction with the browser source
            var interactResult = await TriggerBrowserSourceInteract();
            if (interactResult) {
                _logger.Log(LogLevel.Info, "Browser source interaction triggered successfully");
                return true;
            }
            // Strategy 4: Try to toggle browser source visibility (sometimes helps with loading issues)
            var toggleResult = await ToggleBrowserSourceVisibility();
            if (toggleResult) {
                _logger.Log(LogLevel.Info, "Browser source visibility toggled successfully");
                return true;
            }
            _logger.Log(LogLevel.Debug, "All OBS automation strategies failed or unavailable");
            return false;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Warn, "Error during OBS automation attempt", ex);
            return false;
        }
    }
    private Task<bool> RefreshBrowserSource() {
        try {
            // Get the current scene first
            var currentScene = _cph.ObsGetCurrentScene();
            if (string.IsNullOrEmpty(currentScene)) {
                _logger.Log(LogLevel.Debug, "Could not get current OBS scene");
                return Task.FromResult(false);
            }
            // Try to refresh the browser source using OBS WebSocket
            var refreshPayload = new {
                sourceName = "Player" // This matches the actual browser source name in ObsSceneManager
            };
            var result = _cph.ObsSendRaw("RefreshBrowserSource", JsonConvert.SerializeObject(refreshPayload));
            if (result == null) return Task.FromResult(false);
            _logger.Log(LogLevel.Debug, "Browser source refresh command sent successfully");
            return Task.FromResult(true);
        } catch (Exception ex) {
            _logger.Log(LogLevel.Debug, "Error refreshing browser source", ex);
            return Task.FromResult(false);
        }
    }
    private Task<bool> TriggerBrowserSourceInteract() {
        try {
            // Note: This is experimental - OBS WebSocket may not support programmatic interaction
            var interactPayload = new {
                sourceName = "Player",
                interact = true
            };
            var result = _cph.ObsSendRaw("TriggerBrowserSourceInteract", JsonConvert.SerializeObject(interactPayload));
            if (result == null) return Task.FromResult(false);
            _logger.Log(LogLevel.Debug, "Browser source interaction trigger sent");
            return Task.FromResult(true);
        } catch (Exception ex) {
            _logger.Log(LogLevel.Debug, "Error triggering browser source interaction", ex);
            return Task.FromResult(false);
        }
    }
    private async Task<bool> ToggleBrowserSourceVisibility() {
        try {
            var currentScene = _cph.ObsGetCurrentScene();
            if (string.IsNullOrEmpty(currentScene)) {
                return false;
            }
            const string cliparinoSourceName = "Cliparino";
            const string playerSourceName = "Player";
            // Toggle visibility (hide then show after a short delay)
            _cph.ObsSetSourceVisibility(cliparinoSourceName, playerSourceName, false);
            await Task.Delay(500); // Short delay
            _cph.ObsSetSourceVisibility(cliparinoSourceName, playerSourceName, true);
            _logger.Log(LogLevel.Debug, "Browser source visibility toggled");
            return true;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Debug, "Error toggling browser source visibility", ex);
            return false;
        }
    }
    private async Task<bool> AutomateContentWarningClick() {
        try {
            _logger.Log(LogLevel.Info, "Attempting full automation of content warning click...");
            // Step 1: Try to trigger OBS interact mode programmatically
            var interactTriggered = await TriggerObsInteractMode();
            if (!interactTriggered) {
                _logger.Log(LogLevel.Debug, "Could not trigger OBS interact mode programmatically");
                return false;
            }
            // Step 2: Wait for an interact window to open
            await Task.Delay(2000);
            // Step 3: Find and click the content warning button
            var clickSuccessful = await ClickContentWarningButton();
            if (!clickSuccessful) {
                _logger.Log(LogLevel.Debug, "Could not locate or click content warning button");
                return false;
            }
            // Step 4: Close the Interact window
            await CloseObsInteractWindow();
            _logger.Log(LogLevel.Info, "Content warning automation completed successfully!");
            return true;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error during content warning automation", ex);
            return false;
        }
    }
    private async Task<bool> TriggerObsInteractMode() {
        try {
            // Method 1: Try using OBS WebSocket if available
            var obsPayload = new {
                sourceName = "Player",
                action = "interact"
            };
            var result = _cph.ObsSendRaw("TriggerInteract", JsonConvert.SerializeObject(obsPayload));
            if (result != null) {
                _logger.Log(LogLevel.Debug, "OBS interact mode triggered via WebSocket");
                return true;
            }
            // Method 2: Try Windows automation to right-click on the browser source
            var obsWindow = FindObsWindow();
            if (obsWindow == IntPtr.Zero) return false;
            var browserSourceCoordinates = await FindBrowserSourceCoordinates(obsWindow);
            if (!browserSourceCoordinates.HasValue) return false;
            // Right-click on the browser source
            await SimulateRightClick(obsWindow, browserSourceCoordinates.Value);
            await Task.Delay(200);
            // Click on the "Interact" menu item (approximate position)
            var interactPosition = new Point {
                X = browserSourceCoordinates.Value.X + 50,
                Y = browserSourceCoordinates.Value.Y + 100
            };
            await SimulateLeftClick(obsWindow, interactPosition);
            _logger.Log(LogLevel.Debug, "OBS interact mode triggered via Windows automation");
            return true;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Debug, "Error triggering OBS interact mode", ex);
            return false;
        }
    }
    private async Task<bool> ClickContentWarningButton() {
        try {
            // Look for an OBS Interact window
            var interactWindow = FindWindow(null, "Interact");
            if (interactWindow == IntPtr.Zero) {
                // Try alternate window title
                interactWindow = FindWindow(null, "Browser Source - Interact");
            }
            if (interactWindow == IntPtr.Zero) {
                _logger.Log(LogLevel.Debug, "Could not find OBS interact window");
                return false;
            }
            // Get window dimensions
            if (!GetWindowRect(interactWindow, out var windowRect)) {
                return false;
            }
            // Content warning buttons are typically in the center-bottom area
            // We'll try multiple common positions where these buttons appear
            var buttonPositions = new[] {
                new Point { X = (windowRect.Right - windowRect.Left) / 2, Y = (windowRect.Bottom - windowRect.Top) * 3 / 4 },
                new Point { X = (windowRect.Right - windowRect.Left) / 2, Y = (windowRect.Bottom - windowRect.Top) * 2 / 3 },
                new Point { X = (windowRect.Right - windowRect.Left) / 2 + 100, Y = (windowRect.Bottom - windowRect.Top) * 3 / 4 },
                new Point { X = (windowRect.Right - windowRect.Left) / 2 - 100, Y = (windowRect.Bottom - windowRect.Top) * 3 / 4 }
            };
            foreach (var position in buttonPositions) {
                await SimulateLeftClick(interactWindow, position);
                await Task.Delay(500);
                // Check if the click was successful (the window might close or change)
                if (GetWindowRect(interactWindow, out _)) continue;
                _logger.Log(LogLevel.Debug, $"Content warning button clicked successfully at position {position.X}, {position.Y}");
                return true;
            }
            return false;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Debug, "Error clicking content warning button", ex);
            return false;
        }
    }
    private static async Task SimulateLeftClick(IntPtr hWnd, Point position) {
        var lParam = (IntPtr)((position.Y << 16) | position.X);
        PostMessage(hWnd, WinMsg["WM_LBUTTONDOWN"], IntPtr.Zero, lParam);
        await Task.Delay(50);
        PostMessage(hWnd, WinMsg["WM_LBUTTONUP"], IntPtr.Zero, lParam);
    }
    private static async Task SimulateRightClick(IntPtr hWnd, Point position) {
        var lParam = (IntPtr)((position.Y << 16) | position.X);
        PostMessage(hWnd, WinMsg["WM_RBUTTONDOWN"], IntPtr.Zero, lParam);
        await Task.Delay(50);
        PostMessage(hWnd, WinMsg["WM_RBUTTONUP"], IntPtr.Zero, lParam);
    }
    private IntPtr FindObsWindow() {
        var obsWindow = FindWindow(null, "OBS Studio");
        if (obsWindow == IntPtr.Zero) {
            // Try alternate window titles
            obsWindow = FindWindow(null, "OBS");
        }
        return obsWindow;
    }
    private Task<Point?> FindBrowserSourceCoordinates(IntPtr obsWindow) {
        try {
            if (!GetWindowRect(obsWindow, out var obsRect)) {
                return Task.FromResult<Point?>(null);
            }
            // Browser sources are typically in the preview area
            // This is an approximation - you may need to adjust based on OBS layout
            var estimatedPosition = new Point {
                X = (obsRect.Right - obsRect.Left) / 2,
                Y = (obsRect.Bottom - obsRect.Top) / 3
            };
            return Task.FromResult<Point?>(estimatedPosition);
        } catch (Exception ex) {
            _logger.Log(LogLevel.Debug, "Error finding browser source coordinates", ex);
            return Task.FromResult<Point?>(null);
        }
    }
    private async Task CloseObsInteractWindow() {
        try {
            var interactWindow = FindWindow(null, "Interact");
            if (interactWindow == IntPtr.Zero) {
                interactWindow = FindWindow(null, "Browser Source - Interact");
            }
            if (interactWindow != IntPtr.Zero) {
                // Send Alt+F4 to close the window
                PostMessage(interactWindow, 0x0104, (IntPtr)0x73, IntPtr.Zero); // WM_SYSKEYDOWN, VK_F4
                await Task.Delay(50);
                PostMessage(interactWindow, 0x0105, (IntPtr)0x73, IntPtr.Zero); // WM_SYSKEYUP, VK_F4
            }
        } catch (Exception ex) {
            _logger.Log(LogLevel.Debug, "Error closing OBS interact window", ex);
        }
    }
    public void StartServer() {
        try {
            // Check if the server is already running
            if (IsServerRunning) {
                _logger.Log(LogLevel.Info, $"HTTP server is already running at {ServerUrl}");
                return;
            }
            // Clean up any existing listener that isn't running
            if (_listener != null) {
                try {
                    _listener.Close();
                } catch {
                    // Ignore cleanup errors
                }
                _listener = null;
            }
            var portAttempts = 0;
            _currentPort = BasePort;
            while (portAttempts < MaxPortRetries) {
                try {
                    ServerUrl = $"http://localhost:{_currentPort}/";
                    _listener = new HttpListener();
                    _listener.Prefixes.Add(ServerUrl);
                    _listener.Start();
                    _listener.BeginGetContext(HandleRequest, null);
                    _logger.Log(LogLevel.Info, $"HTTP server started successfully at {ServerUrl}");
                    return;
                } catch (HttpListenerException ex) when (IsPortInUseError(ex)) {
                    // Port is in use, try the next port
                    CleanupListener();
                    _currentPort++;
                    portAttempts++;
                    if (portAttempts < MaxPortRetries)
                        _logger.Log(LogLevel.Warn,
                                    $"Port {_currentPort - 1} is in use (Error: {ex.ErrorCode}), trying port {_currentPort}...");
                } catch (Exception ex) {
                    CleanupListener();
                    _logger.Log(LogLevel.Error,
                                $"Unexpected error starting HTTP server on port {_currentPort}: {ex.Message}",
                                ex);
                    throw;
                }
            }
            var errorMessage =
                $"Failed to start HTTP server after trying ports {BasePort} to {_currentPort - 1}. All ports are in use.";
            _logger.Log(LogLevel.Error, errorMessage);
            throw new InvalidOperationException(errorMessage);
        } catch (Exception ex) when (!(ex is InvalidOperationException)) {
            _logger.Log(LogLevel.Error, "Failed to start HTTP server.", ex);
            throw;
        }
    }
    private static bool IsPortInUseError(HttpListenerException ex) {
        // Common error codes for port in use:
        // 32 = ERROR_SHARING_VIOLATION
        //   (The process cannot access the file because it is being used by another process)
        // 183 = ERROR_ALREADY_EXISTS
        //   (Cannot create a file when that file already exists)
        // 10048 = WSAEADDRINUSE
        //   (Address already in use)
        return ex.ErrorCode == 32 || ex.ErrorCode == 183 || ex.ErrorCode == 10048;
    }
    private void CleanupListener() {
        if (_listener == null) return;
        try {
            _listener.Close();
        } catch {
            // Ignore cleanup errors
        }
        _listener = null;
    }
    private bool TestServerResponse() {
        if (!IsServerRunning) {
            _logger.Log(LogLevel.Warn, "TestServerResponse: Server is not running.");
            return false;
        }
        try {
            using (var client = new HttpClient()) {
                client.Timeout = TimeSpan.FromSeconds(5);
                var response = client.GetAsync(ServerUrl).Result;
                var isSuccess = response.IsSuccessStatusCode;
                if (isSuccess)
                    _logger.Log(LogLevel.Debug, $"Server response test successful: {response.StatusCode}");
                else
                    _logger.Log(LogLevel.Warn, $"Server response test failed: {response.StatusCode}");
                return isSuccess;
            }
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"Server response test failed with exception: {ex.Message}");
            return false;
        }
    }
    public bool ValidateServerReadiness() {
        try {
            // Check if the server is running
            if (!IsServerRunning) {
                _logger.Log(LogLevel.Error, "Server readiness check failed: HTTP server is not running.");
                return false;
            }
            // Test if the server responds to requests
            if (!TestServerResponse()) {
                _logger.Log(LogLevel.Error,
                            "Server readiness check failed: HTTP server is not responding to requests.");
                return false;
            }
            // Check if clip data is available
            if (_clipData == null) {
                _logger.Log(LogLevel.Warn, "Server readiness check: No clip data loaded, but server is ready.");
                return true; // Server is ready, just no clip loaded yet
            }
            _logger.Log(LogLevel.Info, $"Server readiness check passed: Ready to serve clips at {ServerUrl}");
            return true;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Server readiness check failed with exception.", ex);
            return false;
        }
    }
    public async Task StopHosting() {
        _logger.Log(LogLevel.Info, "Stopping clip hosting and HTTP server...");
        await Task.Run(() => {
                           try {
                               Client.CancelPendingRequests();
                               CleanupListener();
                               _logger.Log(LogLevel.Info, "HTTP server stopped successfully.");
                           } catch (Exception ex) {
                               _logger.Log(LogLevel.Warn, "Error occurred while stopping HTTP server.", ex);
                           }
                       });
    }
    public bool HostClip(ClipData clipData) {
        try {
            if (clipData == null) {
                _logger.Log(LogLevel.Error, "Cannot host clip: clip data is null.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(clipData.Id)) {
                _logger.Log(LogLevel.Error, "Cannot host clip: clip ID is null or empty.");
                return false;
            }
            // Critical validation: Ensure the HTTP server is running before hosting
            if (!IsServerRunning) {
                _logger.Log(LogLevel.Error,
                            "Cannot host clip: HTTP server is not running. Attempting to start server...");
                try {
                    StartServer();
                    if (!IsServerRunning) {
                        _logger.Log(LogLevel.Error, "Cannot host clip: Failed to start HTTP server.");
                        return false;
                    }
                } catch (Exception ex) {
                    _logger.Log(LogLevel.Error, "Cannot host clip: Failed to start HTTP server.", ex);
                    return false;
                }
            }
            if (string.IsNullOrWhiteSpace(clipData.Title))
                _logger.Log(LogLevel.Warn, "Clip title is null or empty, but proceeding with hosting.");
            _clipData = clipData;
            _logger.Log(LogLevel.Info,
                        $"Successfully prepared clip '{clipData.Title}' (ID: {clipData.Id}) for hosting at {ServerUrl}");
            return true;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error while preparing clip for hosting.", ex);
            return false;
        }
    }
    private void HandleRequest(IAsyncResult result) {
        if (_listener == null || !_listener.IsListening) return;
        var context = _listener.EndGetContext(result);
        _listener.BeginGetContext(HandleRequest, null);
        try {
            var requestPath = context.Request.Url.AbsolutePath;
            _logger.Log(LogLevel.Debug, $"Received HTTP request: {requestPath}");
            switch (requestPath) {
                case "/index.css": Task.Run(() => ServeCSS(context)); break;
                case "/favicon.ico": context.Response.StatusCode = 204; break;
                case "/content-warning-detected":
                    if (context.Request.HttpMethod == "POST") {
                        Task.Run(() => HandleContentWarningNotification(context));
                    } else {
                        context.Response.StatusCode = 405; // Method not allowed
                    }
                    break;
                case "/index.html":
                case "/":
                    Task.Run(() => ServeHTML(context));
                    break;
                default: HandleError(context, 404, $"Unknown resource requested: {requestPath}"); break;
            }
        } catch (Exception ex) {
            HandleError(context, 500, "Error while handling request.", ex);
        }
    }
    private async Task HandleContentWarningNotification(HttpListenerContext context) {
        try {
            // Read the request body
            string requestBody;
            using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding)) {
                requestBody = await reader.ReadToEndAsync();
            }
            // Parse the JSON request
            var warningData = JsonConvert.DeserializeObject<dynamic>(requestBody);
            var clipId = warningData?.clipId?.ToString();
            var detectionMethod = warningData?.detectionMethod?.ToString();
            _logger.Log(LogLevel.Info, $"Content warning detected for clip {clipId} via {detectionMethod}");
            // Attempt OBS automation
            var obsResult = await AutomateContentWarningHandling();
            if (obsResult) {
                _logger.Log(LogLevel.Info, "OBS automation successful for content warning");
            } else {
                _logger.Log(LogLevel.Info, "=== CONTENT WARNING DETECTED ===");
                _logger.Log(LogLevel.Info, $"Clip: {clipId}");
                _logger.Log(LogLevel.Info, "To continue:");
                _logger.Log(LogLevel.Info, "1. In OBS, right-click the Browser Source");
                _logger.Log(LogLevel.Info, "2. Select 'Interact'");
                _logger.Log(LogLevel.Info, "3. Click through the age verification");
                _logger.Log(LogLevel.Info, "4. Close the interact window");
                _logger.Log(LogLevel.Info, "===============================");
            }
            // Send response
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            var responseData = JsonConvert.SerializeObject(new { success = true, obsAutomation = obsResult });
            var buffer = Encoding.UTF8.GetBytes(responseData);
            await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error handling content warning notification", ex);
            context.Response.StatusCode = 500;
        } finally {
            context.Response.Close();
        }
    }
    private static async Task ServeCSS(HttpListenerContext context) {
        context.Response.ContentType = "text/css";
        var buffer = Encoding.UTF8.GetBytes(CSSText);
        await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        context.Response.Close();
    }
    private async Task ServeHTML(HttpListenerContext context) {
        try {
            string responseString;
            (responseString, context) = await SetUpSite(context);
            var buffer = Encoding.UTF8.GetBytes(responseString);
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            context.Response.OutputStream.Close();
            context.Response.Close();
        } catch (Exception ex) {
            HandleError(context, 500, "Error while serving HTML.", ex);
        }
    }
    private async Task<(string responseString, HttpListenerContext context)> SetUpSite(HttpListenerContext context) {
        var nonce = CreateNonce();
        context = ReadyHeaders(nonce, context);
        return (await PreparePage(nonce), context);
    }
    private async Task<string> PreparePage(string nonce = null, ClipData clipData = null) {
        try {
            // Validate input based on which mode we're in
            if (clipData != null)
                _clipData = clipData; // Store for consistency with existing implementation
            else if (_clipData == null) throw new InvalidOperationException("Clip data cannot be null.");
            if (string.IsNullOrWhiteSpace(nonce))
                throw new ArgumentNullException(nameof(nonce), "Nonce cannot be null or empty.");
            // Fetch game data with fallback handling
            var gameData = await _twitchApiManager.GetGameDataAsync(_clipData.GameId);
            var gameName = gameData?.Name;
            if (string.IsNullOrWhiteSpace(gameName)) {
                gameName = "Unknown Game";
                _logger.Log(LogLevel.Warn, $"Could not fetch game name for game ID {_clipData.GameId}");
            }
            _logger.Log(LogLevel.Debug, $"Preparing page for clip '{_clipData.Id}'...");
            return HTMLText.Replace("[[clipId]]", _clipData.Id)
                           .Replace("[[nonce]]", nonce)
                           .Replace("[[streamerName]]", _clipData.BroadcasterName)
                           .Replace("[[gameName]]", gameName)
                           .Replace("[[clipTitle]]", _clipData.Title)
                           .Replace("[[curatorName]]", _clipData.CreatorName);
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error while preparing page", ex);
            throw; // Maintain the exception chain for higher-level handling
        }
    }
    private static HttpListenerContext ReadyHeaders(string nonce, HttpListenerContext context) {
        var headers = new List<Dictionary<string, string>> { GenerateCORSHeaders(nonce), GenerateCacheControlHeaders() }
                      .SelectMany(dict => dict)
                      .ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var header in headers) context.Response.Headers[header.Key] = header.Value;
        return context;
    }
    private static string CreateNonce() {
        var guidBytes = Guid.NewGuid().ToByteArray();
        var base64Nonce = Convert.ToBase64String(guidBytes);
        return SanitizeNonce(base64Nonce).Substring(0, NonceLength);
    }
    private static string SanitizeNonce(string nonce) {
        return nonce.Replace("+", "-").Replace("/", "_").Replace("=", "_");
    }
    private void HandleError(HttpListenerContext context,
                             int statusCode,
                             string errorMessage,
                             Exception exception = null) {
        try {
            _logger.Log(LogLevel.Error, errorMessage, exception);
            context.Response.StatusCode = statusCode;
            context.Response.Close();
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Failed to send HTTP error response.", ex);
        }
    }
}

