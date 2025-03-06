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
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
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
    private const int DefaultWidth = 1920;
    private const int DefaultHeight = 1080;

    private readonly Dictionary<string, ClipData> _clipDataCache = new Dictionary<string, ClipData>();
    private CancellationTokenSource _autoStopCancellationTokenSource;
    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    private CancellationTokenSource _clipCancellationTokenSource = new CancellationTokenSource();
    private TwitchApiClient _twitchApiClient;
    private CPHLogger _logger;
    private ObsManager _obsManager;
    private CliparinoCleanupManager _cleanupManager;
    private ClipManager _clipManager;
    private HttpManager _httpManager;

    #region Initialization & Core Execution

    /// <summary>
    ///     Initializes the CPHInline class, setting up the dependencies and preparing the environment for
    ///     operations.
    /// </summary>
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public void Init() {
        var loggingEnabled = GetArgument("logging", false);

        // Initialize dependencies
        _logger = new CPHLogger(CPH, loggingEnabled);
        _httpManager = new HttpManager(_logger);
        _obsManager = new ObsManager(CPH, _logger);
        _cleanupManager = new CliparinoCleanupManager(CPH, _logger);
        _clipManager = new ClipManager(_logger, _httpManager);
        _twitchApiClient =
            new TwitchApiClient(_httpClient, new OAuthInfo(CPH.TwitchClientId, CPH.TwitchOAuthToken), _logger);
        PathBuddy.EnsureCliparinoFolderExists();
        PathBuddy.SetLogger(_logger);
    }

    /// <summary>
    ///     Executes the main logic for Cliparino commands such as watch, shoutout, replay, and stop.
    ///     <returns>
    ///         True if the operation succeeds, otherwise false.
    ///     </returns>
    /// </summary>
    public bool Execute() {
        _logger.Log(LogLevel.Debug, $"{nameof(Execute)} for Cliparino started.");

        try {
            _clipCancellationTokenSource?.Cancel();
            _clipCancellationTokenSource = new CancellationTokenSource();

            var token = _clipCancellationTokenSource.Token;

            if (!TryGetCommand(out var command)) {
                _logger.Log(LogLevel.Warn, "Command argument is missing.");

                return false;
            }

            EnsureCliparinoInCurrentSceneAsync(null, token).GetAwaiter().GetResult();

            var input0 = GetArgument("input0", string.Empty);
            var width = GetArgument("width", DefaultWidth);
            var height = GetArgument("height", DefaultHeight);

            _logger.Log(LogLevel.Info, $"Executing command: {command}");

            switch (command.ToLower()) {
                case "!watch": HandleWatchCommandAsync(input0, width, height, token).GetAwaiter().GetResult(); break;
                case "!so": HandleShoutoutCommandAsync(input0, token).GetAwaiter().GetResult(); break;
                case "!replay": HandleReplayCommandAsync(width, height, token).GetAwaiter().GetResult(); break;
                case "!stop": HandleStopCommandAsync(token).GetAwaiter().GetResult(); break;
                default:
                    _logger.Log(LogLevel.Warn, $"Unknown command: {command}");

                    return false;
            }

            return true;
        } catch (OperationCanceledException) {
            _logger.Log(LogLevel.Warn, "Cliparino operation was cancelled.");

            return false;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"An error occurred: {ex.Message}");

            return false;
        } finally {
            _logger.Log(LogLevel.Debug, $"{nameof(Execute)} for Cliparino completed.");
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

    //TODO: Document the remaining methods in this region

    #endregion

    #region Chat Command Handlers

    /// <summary>
    ///     Handles the 'shoutout' command (!so), fetching clip information for the specified user and
    ///     generating a shoutout message in chat.
    /// </summary>
    /// <param name="user">
    ///     The Twitch username of the target user.
    /// </param>
    /// <param name="token">
    ///     Cancellation token to handle operation cancellation.
    /// </param>
    private async Task HandleShoutoutCommandAsync(string user, CancellationToken token) {
        _logger.Log(LogLevel.Debug, $"{nameof(HandleShoutoutCommandAsync)} initiated for user: '{user}'");

        try {
            user = SanitizeUsername(user);

            if (string.IsNullOrEmpty(user)) {
                _logger.Log(LogLevel.Warn, "No valid username provided for shoutout.");

                return;
            }

            var extendedUserInfo = FetchExtendedUserInfo(user);

            if (extendedUserInfo == null) return;

            var messageTemplate = GetShoutoutMessageTemplate();
            var clip = await TryFetchClipAsync(extendedUserInfo.UserId, token);

            if (token.IsCancellationRequested) return;

            await HandleShoutoutMessageAsync(extendedUserInfo, messageTemplate, clip, token);
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"Error occurred in {nameof(HandleShoutoutCommandAsync)}: {ex.Message}");
        }
    }

    /// <summary>
    ///     Handles the 'watch' command (!watch), playing a specified Twitch clip in the OBS browser
    ///     source.
    /// </summary>
    /// <param name="url">
    ///     The Twitch clip URL to play.
    /// </param>
    /// <param name="width">
    ///     Width of the clip display area.
    /// </param>
    /// <param name="height">
    ///     Height of the clip display area.
    /// </param>
    /// <param name="token">
    ///     Cancellation token to handle operation cancellation.
    /// </param>
    private async Task HandleWatchCommandAsync(string url, int width, int height, CancellationToken token) {
        _logger.Log(LogLevel.Debug,
                    $"{nameof(HandleWatchCommandAsync)} received request to play clip with URL: '{url}'");

        try {
            (width, height) = ValidateDimensions(width, height);
            url = ValidateClipUrl(url, null);

            if (string.IsNullOrWhiteSpace(url)) {
                _logger.Log(LogLevel.Warn, "Invalid clip URL provided. Aborting watch command.");

                return;
            }

            var clipData = await _clipManager.GetClipData(url);

            if (clipData == null) {
                _logger.Log(LogLevel.Error, "Failed to retrieve clip data for the provided URL.");

                return;
            }

            _logger.Log(LogLevel.Info, $"Now playing clip: {clipData.Title}, curated by {clipData.CreatorName}");
            await HostClipDataAsync(clipData, url, width, height, token);
        } catch (OperationCanceledException) {
            _logger.Log(LogLevel.Warn, "Watch command was cancelled.");
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"Error in {nameof(HandleWatchCommandAsync)}: {ex.Message}");
        }
    }

    /// <summary>
    ///     Handles the 'replay' command (!replay), loading the last played Twitch clip for replay in OBS.
    /// </summary>
    /// <param name="width">
    ///     Width of the clip display area.
    /// </param>
    /// <param name="height">
    ///     Height of the clip display area.
    /// </param>
    /// <param name="token">
    ///     Cancellation token to handle operation cancellation.
    /// </param>
    private async Task HandleReplayCommandAsync(int width, int height, CancellationToken token) {
        _logger.Log(LogLevel.Debug, $"{nameof(HandleReplayCommandAsync)} called to replay the last clip.");

        try {
            var lastClipUrl = GetLastClipUrl();

            if (!string.IsNullOrEmpty(lastClipUrl))
                await ProcessAndHostClipDataAsync(lastClipUrl, null, token);
            else
                _logger.Log(LogLevel.Warn, "No last clip URL found to replay.");
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"Error in {nameof(HandleReplayCommandAsync)}: {ex.Message}");
        }
    }

    /// <summary>
    ///     Handles the 'stop' command (!stop), stopping any currently playing clips in OBS.
    /// </summary>
    /// <param name="token">
    ///     Cancellation token to handle operation cancellation.
    /// </param>
    private async Task HandleStopCommandAsync(CancellationToken token) {
        _logger.Log(LogLevel.Debug, $"{nameof(HandleStopCommandAsync)} called to stop playback.");

        try {
            CancelCurrentToken();

            _logger.Log(LogLevel.Debug, $"Token cancellation requested: {token.IsCancellationRequested}");

            // Stop the browser source activity
            EnsureClipSourceHidden();
            await SetBrowserSourceAsync("about:blank");

            _logger.Log(LogLevel.Info, "Clip playback successfully stopped.");
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"Error in {nameof(HandleStopCommandAsync)}: {ex.Message}");
        }
    }

    #endregion

    #region Twitch API Interaction

    /// <summary>
    ///     Represents a client for interacting with the Twitch API, specifically for features like
    ///     fetching clips, games, and other data. Requires a valid OAuth token and client ID for
    ///     authentication.
    /// </summary>
    private class TwitchApiClient {
        private readonly string _authToken;
        private readonly string _clientId;
        private readonly HttpClient _httpClient;
        private readonly LogDelegate _log;

        /// <summary>
        ///     Initializes a new instance of the <see cref="TwitchApiClient" /> class.
        /// </summary>
        /// <param name="httpClient">
        ///     The HTTP client used for making API requests.
        /// </param>
        /// <param name="oAuthInfo">
        ///     An object containing the Twitch OAuth token and client ID.
        /// </param>
        /// <param name="log">
        ///     Logging delegate for capturing debugging or error information.
        /// </param>
        /// <exception cref="ArgumentNullException">
        ///     Thrown if any of the required parameters are null.
        /// </exception>
        public TwitchApiClient(HttpClient httpClient, OAuthInfo oAuthInfo, CPHLogger log) {
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
        ///     Configures HTTP request headers with the necessary authorization and client ID. This is called
        ///     internally before each API request.
        /// </summary>
        private void ConfigureHttpRequestHeaders() {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Client-ID", _clientId);
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_authToken}");
        }

        /// <summary>
        ///     Sends an asynchronous GET request to the specified Twitch API endpoint.
        /// </summary>
        /// <param name="endpoint">
        ///     The endpoint of the Twitch API (relative to the base URL).
        /// </param>
        /// <param name="completeUrl">
        ///     The complete URL, used for logging purposes.
        /// </param>
        /// <returns>
        ///     The response content as a string, or null if the request fails.
        /// </returns>
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
        ///     Fetches data from a Twitch API endpoint and deserializes the response into the specified type.
        /// </summary>
        /// <typeparam name="T">
        ///     The type of data to deserialize the response into.
        /// </typeparam>
        /// <param name="endpoint">
        ///     The endpoint to query on the Twitch API.
        /// </param>
        /// <returns>
        ///     The first object in the response, or the default value if no data is found.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     Thrown if the endpoint is null or empty.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        ///     Thrown if the OAuth token is invalid or missing.
        /// </exception>
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

                // Return the first item in the data array, if any.
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
        ///     Fetches a Twitch clip by its unique ID.
        /// </summary>
        /// <param name="clipId">
        ///     The unique Twitch clip ID.
        /// </param>
        /// <returns>
        ///     The <see cref="ClipData" /> object for the clip, or null if not found.
        /// </returns>
        public Task<ClipData> FetchClipById(string clipId) {
            return FetchDataAsync<ClipData>($"clips?id={clipId}");
        }

        /// <summary>
        ///     Fetches game information by the game's unique ID.
        /// </summary>
        /// <param name="gameId">
        ///     The game's ID to query.
        /// </param>
        /// <returns>
        ///     The <see cref="GameInfo" /> object for the game, or null if not found.
        /// </returns>
        public Task<GameInfo> FetchGameById(string gameId) {
            return FetchDataAsync<GameInfo>($"games?id={gameId}");
        }
    }

    /// <summary>
    ///     Holds the Twitch Client ID and OAuth Token needed for API interactions.
    /// </summary>
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

    /// <summary>
    ///     Represents a generic response from the Twitch API.
    /// </summary>
    /// <typeparam name="T">
    ///     The type of data contained in the response.
    /// </typeparam>
    public class TwitchApiResponse<T> {
        public TwitchApiResponse(T[] data) {
            Data = data ?? Array.Empty<T>();
        }

        /// <summary>
        ///     The array of data items returned by the API.
        /// </summary>
        public T[] Data { get; }
    }

    /// <summary>
    ///     Represents game information returned by the Twitch API.
    /// </summary>
    public class GameData {
        [JsonProperty("id")] public string Id { get; set; }

        [JsonProperty("name")] public string Name { get; set; }
    }

    /// <summary>
    ///     Fetches the name of a Twitch game using its unique ID.
    /// </summary>
    /// <param name="gameId">
    ///     The ID of the game to fetch.
    /// </param>
    /// <param name="token">
    ///     Cancellation token to handle operation cancellation.
    /// </param>
    /// <returns>
    ///     The name of the game, or "Unknown Game" if not found.
    /// </returns>
    private async Task<string> FetchGameNameAsync(string gameId, CancellationToken token) {
        if (string.IsNullOrWhiteSpace(gameId)) {
            _logger.Log(LogLevel.Warn, "Game ID is empty or null. Returning 'Unknown Game'.");

            return "Unknown Game";
        }

        if (token.IsCancellationRequested) return await Task.FromCanceled<string>(token);

        try {
            var gameData = await _twitchApiClient.FetchGameById(gameId);

            return string.IsNullOrWhiteSpace(gameData?.Name) ? "Unknown Game" : gameData.Name;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"{CreateErrorPreamble()}: {ex.Message}");

            return "Unknown Game";
        }
    }

    private static ClipInfo ExtractClipInfo(ClipData clipData) {
        //TODO: implement more often, this is a fuller object
        var clipInfo = GetClipInfo(clipData);

        return clipInfo;
    }

    #endregion

    #region Utilities

    private const int NonceLength = 16;

    /// <summary>
    ///     A utility class for managing scoped access to a <see cref="SemaphoreSlim" />. Ensures proper
    ///     release of the semaphore when the scope is disposed.
    /// </summary>
    private class ScopedSemaphore : IDisposable {
        private readonly LogDelegate _log;
        private readonly SemaphoreSlim _semaphore;
        private bool _hasLock;

        public ScopedSemaphore(SemaphoreSlim semaphore, LogDelegate log) {
            _semaphore = semaphore ?? throw new ArgumentNullException(nameof(semaphore));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _semaphore.Wait();
            _hasLock = true;
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

        /// <summary>
        ///     Asynchronously acquires a semaphore lock and returns the scoped semaphore for management.
        /// </summary>
        /// <param name="semaphore">
        ///     The semaphore to acquire.
        /// </param>
        /// <param name="log">
        ///     The logging method for error handling.
        /// </param>
        /// <returns>
        ///     A <see cref="ScopedSemaphore" /> instance.
        /// </returns>
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
    ///     Tries to acquire a semaphore, logging operations and handling timeouts.
    /// </summary>
    private async Task<bool> TryAcquireSemaphore(SemaphoreSlim semaphore, string name, int timeout = 10) {
        _logger.Log(LogLevel.Debug, $"Attempting to acquire semaphore '{name}' with a timeout of {timeout} seconds...");

        try {
            if (await semaphore.WaitAsync(TimeSpan.FromSeconds(timeout))) {
                _logger.Log(LogLevel.Debug, $"Semaphore '{name}' successfully acquired.");

                return true;
            } else {
                _logger.Log(LogLevel.Warn, $"Semaphore '{name}' acquisition timed out after {timeout} seconds.");

                return false;
            }
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error,
                        $"An exception occurred while attempting to acquire semaphore '{name}': {ex.Message}");

            return false;
        } finally {
            // No additional cleanup is performed here since releasing is handled explicitly elsewhere in the code.
            // This ensures that the semaphore doesn't get released prematurely or incorrectly.
            _logger.Log(LogLevel.Debug, $"Exiting {nameof(TryAcquireSemaphore)} for semaphore '{name}'.");
        }
    }

    /// <summary>
    ///     Executes an action with thread-safety by acquiring a semaphore before execution and releasing
    ///     it afterward.
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

    private bool TryGetCommand(out string command) {
        return CPH.TryGetArg("command", out command);
    }

    /// <summary>
    ///     Retrieves and sanitizes the username argument.
    /// </summary>
    private static string SanitizeUsername(string user) {
        return string.IsNullOrWhiteSpace(user) ? null : user.Trim().TrimStart('@').ToLowerInvariant();
    }

    /// <summary>
    ///     Fetches extended Twitch user information by login. Handles potential null responses gracefully.
    /// </summary>
    private TwitchUserInfoEx FetchExtendedUserInfo(string user) {
        var extendedUserInfo = CPH.TwitchGetExtendedUserInfoByLogin(user);

        if (extendedUserInfo == null) {
            _logger.Log(LogLevel.Warn, $"No extended user info found for: {user}");

            return null;
        }

        _logger.Log(LogLevel.Debug, $"Fetched extended user info: {JsonConvert.SerializeObject(extendedUserInfo)}");

        return extendedUserInfo;
    }

    private string GetShoutoutMessageTemplate() {
        var messageTemplate = GetArgument("message", string.Empty);

        if (string.IsNullOrWhiteSpace(messageTemplate)) {
            _logger.Log(LogLevel.Warn, "Message template is missing or empty. Using default template.");
            messageTemplate =
                "Check out [[userName]], they were last streaming [[userGame]] on https://twitch.tv/[[userName]]";
        }

        _logger.Log(LogLevel.Debug, $"Using shoutout template: {messageTemplate}");

        return messageTemplate;
    }

    /// <summary>
    ///     Attempts to fetch a Twitch clip for a given user ID. Handles cancellations and exceptions
    ///     gracefully.
    /// </summary>
    private async Task<Clip> TryFetchClipAsync(string userId, CancellationToken token) {
        _logger.Log(LogLevel.Debug, $"{nameof(TryFetchClipAsync)} called with userId: {userId}");

        try {
            var clipSettings = new ClipSettings(GetArgument("featuredOnly", false),
                                                GetArgument("maxClipSeconds", 30),
                                                GetArgument("clipAgeDays", 30));

            if (token.IsCancellationRequested) return null;

            var clip = await Task.Run(() => GetRandomClip(userId, clipSettings), token);

            if (clip == null) _logger.Log(LogLevel.Warn, $"No clips found for user with ID: {userId}");

            return clip;
        } catch (OperationCanceledException) {
            _logger.Log(LogLevel.Warn, "Clip fetch operation was cancelled.");

            return null;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"Error in {nameof(TryFetchClipAsync)}: {ex.Message}");

            return null;
        }
    }

    private async Task HandleShoutoutMessageAsync(TwitchUserInfoEx userInfo,
                                                  string template,
                                                  Clip clip,
                                                  CancellationToken token) {
        var shoutoutMessage = GetShoutoutMessage(userInfo, template, clip);

        _logger.Log(LogLevel.Info, $"Sending shoutout message to chat: {shoutoutMessage}");

        if (clip != null)
            await ProcessAndHostClipDataAsync(clip.Url, clip.ToClipData(CPH), token);
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

    /// <summary>
    ///     Validates width and height dimensions, falling back to default values if invalid.
    /// </summary>
    private (int Width, int Height) ValidateDimensions(int width, int height) {
        if (width > 0 && height > 0) return (width, height);

        _logger.Log(LogLevel.Warn, "Invalid width or height provided. Falling back to default values.");

        return (DefaultWidth, DefaultHeight);
    }

    /// <summary>
    ///     Generates a URL-safe nonce string of a specified length.
    /// </summary>
    private static string CreateNonce() {
        var guidBytes = Guid.NewGuid().ToByteArray();
        var base64Nonce = Convert.ToBase64String(guidBytes);

        return SanitizeNonce(base64Nonce).Substring(0, NonceLength);
    }

    /// <summary>
    ///     Sanitizes a nonce string to replace unsafe characters for use in URLs or headers.
    /// </summary>
    private static string SanitizeNonce(string nonce) {
        return nonce.Replace("+", "-").Replace("/", "_").Replace("=", "_");
    }

    /// <summary>
    ///     Adds CORS headers to an HTTP response. Supports CSP (Content-Security-Policy) using a nonce.
    ///     Returns the generated nonce for use in content.
    /// </summary>
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
    ///     Checks whether a specified port is available for use.
    /// </summary>
    private bool IsPortAvailable(int port) {
        _logger.Log(LogLevel.Debug, $"Starting {nameof(IsPortAvailable)} check for port {port}.");

        try {
            _logger.Log(LogLevel.Debug, $"Invoking {nameof(IdentifyPortConflict)} for port {port}.");
            IdentifyPortConflict(port);

            var listener = new TcpListener(IPAddress.Loopback, port);

            listener.Start();
            listener.Stop();

            _logger.Log(LogLevel.Info, $"Port {port} is available.");

            return true;
        } catch (SocketException ex) {
            _logger.Log(LogLevel.Warn, $"Port {port} is not available: {ex.Message}");

            return false;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"Unexpected error while checking port: {ex.Message}");

            return false;
        } finally {
            _logger.Log(LogLevel.Debug, $"Exiting {nameof(IsPortAvailable)} for port {port}.");
        }
    }

    private void IdentifyPortConflict(int port) {
        var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
        var activeTcpListeners = ipGlobalProperties.GetActiveTcpListeners();
        var activeTcpConnections = ipGlobalProperties.GetActiveTcpConnections();

        var isPortInUse = activeTcpListeners.Any(endpoint => endpoint.Port == port)
                          || activeTcpConnections.Any(connection => connection.LocalEndPoint.Port == port);

        _logger.Log(isPortInUse ? LogLevel.Warn : LogLevel.Info,
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

    private async Task HostClipWithDetailsAsync(string clipUrl, ClipData clipData, CancellationToken token) {
        _logger.Log(LogLevel.Debug, $"{nameof(HostClipWithDetailsAsync)} called with clipUrl: {clipUrl}");

        try {
            clipUrl = GetValueOrDefault(clipUrl, clipData?.Url);

            if (string.IsNullOrWhiteSpace(clipUrl)) {
                _logger.Log(LogLevel.Error, "clipUrl could not be resolved. Aborting.");

                return;
            }

            SetLastClipUrl(clipUrl);

            // Introduce a timeout to prevent Cliparino from blocking indefinitely
            using (var timeoutToken = new CancellationTokenSource(TimeSpan.FromSeconds(15))) {
                using (var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutToken.Token)) {
                    await ExecuteWithSemaphore(ServerSemaphore,
                                               "ServerSetup",
                                               async () => {
                                                   await PrepareSceneForClipHostingAsync(linkedToken.Token);

                                                   _logger.Log(LogLevel.Info, "Extracting clip info from ClipData.");

                                                   var clipInfo = ExtractClipInfo(clipData);

                                                   _logger.Log(LogLevel.Info, "Creating and hosting clip page.");

                                                   await CreateAndHostClipPageAsync(clipData, linkedToken.Token);
                                               });

                    if (clipData != null) {
                        _logger.Log(LogLevel.Debug, "Starting auto-stop task for playback.");

                        await StartAutoStopTaskAsync(clipData.Duration);
                    }
                }
            }
        } catch (OperationCanceledException) {
            _logger.Log(LogLevel.Warn, "Clip hosting operation was cancelled due to timeout.");
        } finally {
            await CleanupServer();

            _logger.Log(LogLevel.Debug, $"{nameof(HostClipWithDetailsAsync)} exiting.");
        }
    }

    private static T GetValueOrDefault<T>(T value, T defaultValue = default) {
        if (value is string stringValue) return !string.IsNullOrEmpty(stringValue) ? value : defaultValue;

        return value != null ? value : defaultValue;
    }

    private async Task StartAutoStopTaskAsync(double duration) {
        try {
            CancelCurrentToken();

            using (await ScopedSemaphore.WaitAsync(TokenSemaphore, Log)) {
                await Task.Delay(TimeSpan.FromSeconds(GetDurationWithSetupDelay((float)duration).TotalSeconds),
                                 _autoStopCancellationTokenSource.Token);

                if (!_autoStopCancellationTokenSource.Token.IsCancellationRequested) {
                    await HandleStopCommandAsync(_autoStopCancellationTokenSource.Token);

                    _logger.Log(LogLevel.Info, "Auto-stop task completed successfully.");
                }
            }
        } catch (OperationCanceledException) {
            _logger.Log(LogLevel.Info, "Auto-stop task cancelled gracefully.");
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"Unexpected error in auto-stop task: {ex.Message}");
        }
    }

#endregion