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
using System.Collections;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Streamer.bot.Common.Events;
using Streamer.bot.Plugin.Interface;
using Streamer.bot.Plugin.Interface.Enums;
using Streamer.bot.Plugin.Interface.Model;
using Twitch.Common.Models.Api;

#endregion

/// <summary>
///     A manager for interacting with Twitch API functionalities such as sending shoutouts, fetching
///     clips, games, and handling OAuth information.
/// </summary>
public class TwitchApiManager {
    private readonly IInlineInvokeProxy _cph;
    private readonly CPHLogger _logger;
    private readonly OAuthInfo _oauthInfo;

    public TwitchApiManager(IInlineInvokeProxy cph, CPHLogger logger) {
        _cph = cph ?? throw new ArgumentNullException(nameof(cph));
        _logger = logger;
        _oauthInfo = new OAuthInfo(cph.TwitchClientId, cph.TwitchOAuthToken);
    }

    /// <summary>
    ///     A dynamic, live reference to the <see cref="HttpManager" /> instance that is currently in use
    ///     in Cliparino.
    /// </summary>
    private static HttpManager HTTPManager => CPHInline.GetHttpManager();

    /// <summary>
    ///     Sends a shoutout message to a specified Twitch user.
    /// </summary>
    /// <param name="username">
    ///     The Twitch username of the user to give a shoutout to.
    /// </param>
    /// <param name="message">
    ///     An optional custom message for the shoutout. Use placeholders for user info.
    /// </param>
    /// <remarks>
    ///     If no <paramref name="message" /> is provided, a default shoutout message will be used.
    /// </remarks>
    public void SendShoutout(string username, string message) {
        if (string.IsNullOrWhiteSpace(username)) {
            _logger.Log(LogLevel.Error, "No username provided for shoutout.");

            return;
        }

        username = username.TrimStart('@');

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

    /// <summary>
    ///     Fetches Twitch clips based on a broadcaster ID and optional pagination cursor.
    /// </summary>
    /// <param name="broadcasterId">
    ///     The ID of the Twitch broadcaster whose clips are being fetched.
    /// </param>
    /// <param name="cursor">
    ///     Optional pagination cursor for retrieving the next set of clips.
    /// </param>
    /// <returns>
    ///     A task that represents the asynchronous operation. The task result contains a
    ///     <see cref="ClipManager.ClipsResponse" /> object, or <c>null</c> if an error occurred.
    /// </returns>
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

    /// <summary>
    ///     Builds a Twitch API URL for fetching clips based on broadcaster ID and pagination cursor.
    /// </summary>
    /// <param name="broadcasterId">
    ///     The ID of the broadcaster.
    /// </param>
    /// <param name="cursor">
    ///     An optional pagination cursor.
    /// </param>
    /// <param name="first">
    ///     The maximum number of clips to fetch (default is 20).
    /// </param>
    /// <returns>
    ///     A complete URL string for the Twitch Clips API.
    /// </returns>
    private static string BuildClipsApiUrl(string broadcasterId, string cursor = "", int first = 20) {
        var url = $"https://api.twitch.tv/helix/clips?broadcaster_id={broadcasterId}&first={first}";

        if (!string.IsNullOrWhiteSpace(cursor)) url += $"&after={cursor}";

        return url;
    }

    /// <summary>
    ///     Formats a shoutout message using placeholders for the Twitch user data.
    /// </summary>
    /// <param name="user">
    ///     The Twitch user information.
    /// </param>
    /// <param name="message">
    ///     The message template containing placeholders.
    /// </param>
    /// <returns>
    ///     A formatted shoutout message.
    /// </returns>
    private static string FormatShoutoutMessage(TwitchUserInfoEx user, string message) {
        return message.Replace("[[userName]]", user.UserName).Replace("[[userGame]]", user.Game);
    }

    /// <summary>
    ///     Fetches information about a Twitch clip by its ID.
    /// </summary>
    /// <param name="clipId">
    ///     The ID of the clip to fetch.
    /// </param>
    /// <returns>
    ///     A task that represents the asynchronous operation. `The task result contains a
    ///     <see cref="ClipData" /> object with information about the clip.
    /// </returns>
    public async Task<ClipData> FetchClipById(string clipId) {
        return await FetchDataAsync<ClipData>($"clips?id={clipId}");
    }

    /// <summary>
    ///     Fetches information about a Twitch game by its ID.
    /// </summary>
    /// <param name="gameId">
    ///     The ID of the game to fetch.
    /// </param>
    /// <returns>
    ///     A task that represents the asynchronous operation. The task result contains a
    ///     <see cref="GameData" /> object with information about the game.
    /// </returns>
    public async Task<GameData> FetchGameById(string gameId) {
        return await FetchDataAsync<GameData>($"games?id={gameId}");
    }

    /// <summary>
    ///     Fetches generic data from a specified Twitch API endpoint.
    /// </summary>
    /// <typeparam name="T">
    ///     The expected type of the response data.
    /// </typeparam>
    /// <param name="endpoint">
    ///     The API endpoint to fetch data from.
    /// </param>
    /// <returns>
    ///     A task that represents the asynchronous operation. The task result contains an object of type
    ///     <typeparamref name="T" />, or the default value if an error occurred.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown if <paramref name="endpoint" /> is null or empty.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if Twitch OAuth information is invalid.
    /// </exception>
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

    /// <summary>
    ///     Validates that the given API endpoint is not null or empty.
    /// </summary>
    /// <param name="endpoint">
    ///     The API endpoint to validate.
    /// </param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown if the <paramref name="endpoint" /> is null or empty.
    /// </exception>
    private static void ValidateEndpoint(string endpoint) {
        const string blankClipTemplate = "clips?id=";
        const string blankGameTemplate = "games?id=";

        if (string.IsNullOrWhiteSpace(endpoint)
            || endpoint.Equals(blankClipTemplate)
            || endpoint.Equals(blankGameTemplate))
            throw new ArgumentNullException(nameof(endpoint), "Endpoint cannot be null or empty.");
    }

    /// <summary>
    ///     Validates that the Twitch OAuth information (Client ID and OAuth Token) is valid.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if the Twitch Validates that the TwiOAuth information is missing or invalid.
    /// </exception>
    private void ValidateOAuthInfo() {
        if (string.IsNullOrWhiteSpace(_oauthInfo.TwitchClientId)
            || string.IsNullOrWhiteSpace(_oauthInfo.TwitchOAuthToken))
            throw new InvalidOperationException("Twitch OAuth information is missing or invalid.");
    }

    /// <summary>
    ///     Constructs a complete URL by combining the base address and the provided endpoint.
    /// </summary>
    /// <param name="endpoint">
    ///     The endpoint to append to the base address.
    /// </param>
    /// <returns>
    ///     A <see cref="string" /> representing the fully constructed URL.
    /// </returns>
    /// <remarks>
    ///     If the base address is null, a default value of "https://www.google.com" is used.
    /// </remarks>
    private static string GetCompleteUrl(string endpoint) {
        var baseAddress = HTTPManager?.Client?.BaseAddress?.ToString() ?? "https://www.google.com/search?q=";

        return new Uri(new Uri(baseAddress), endpoint).ToString();
    }

    /// <summary>
    ///     Deserializes the content of an API response into a specific type.
    /// </summary>
    /// <typeparam name="T">
    ///     The type to deserialize the API response content into.
    /// </typeparam>
    /// <param name="content">
    ///     The JSON content returned from the API response.
    /// </param>
    /// <param name="endpoint">
    ///     The endpoint associated with the API response.
    /// </param>
    /// <returns>
    ///     The deserialized object of type <typeparamref name="T" />. If no data is returned, the default
    ///     value of <typeparamref name="T" /> is returned.
    /// </returns>
    /// <remarks>
    ///     Logs the result of the deserialization process, with an error message logged if no data is
    ///     available.
    /// </remarks>
    private T DeserializeApiResponse<T>(string content, string endpoint) {
        var apiResponse = JsonConvert.DeserializeObject<TwitchApiResponse<T>>(content);

        if (apiResponse?.Data != null && apiResponse.Data.Length > 0) {
            _logger.Log(LogLevel.Info, "Successfully retrieved and deserialized data from Twitch API.");

            return apiResponse.Data[0];
        }

        _logger.Log(LogLevel.Error, $"No data returned from Twitch API for endpoint: {endpoint}");

        return default;
    }

    /// <summary>
    ///     Configures HTTP request headers for the HTTP client.
    /// </summary>
    /// <remarks>
    ///     Clears any existing headers and sets the "Client-ID" and "Authorization" headers based on the
    ///     OAuth credentials.
    /// </remarks>
    private void ConfigureHttpRequestHeaders() {
        var client = HTTPManager.Client;

        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("Client-ID", _oauthInfo.TwitchClientId);
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_oauthInfo.TwitchOAuthToken}");
    }

    /// <summary>
    ///     Sends an asynchronous HTTP GET request to the specified endpoint.
    /// </summary>
    /// <param name="endpoint">
    ///     The endpoint to which the HTTP GET request will be made.
    /// </param>
    /// <returns>
    ///     A <see cref="Task{TResult}" /> representing the response content as a <see cref="string" />.
    /// </returns>
    /// <exception cref="HttpRequestException">
    ///     Thrown when the request fails with an unsuccessful status code.
    /// </exception>
    /// <remarks>
    ///     Logs detailed information about the headers, response, and errors encountered during the
    ///     request.
    /// </remarks>
    private async Task<string> SendHttpRequestAsync(string endpoint) {
        try {
            ConfigureHttpRequestHeaders();

            foreach (var header in HTTPManager.Client.DefaultRequestHeaders)
                _logger.Log(LogLevel.Debug,
                            header.Key == "Authorization"
                                ? $"Header: {header.Key}: {string.Join(",", OAuthInfo.ObfuscateString(_oauthInfo.TwitchOAuthToken))}"
                                : $"Header: {header.Key}: {string.Join(",", header.Value)}");

            _logger.Log(LogLevel.Debug, "HTTP headers set successfully. Initiating request...");

            var response = await HTTPManager.Client.GetAsync(endpoint);
            var responseBody = await response.Content.ReadAsStringAsync();

            _logger.Log(LogLevel.Debug,
                        $"Received response: {response.StatusCode} ({(int)response.StatusCode})\nContent: {responseBody}");

            if (response.IsSuccessStatusCode) return responseBody;

            _logger.Log(LogLevel.Error,
                        $"Request to Twitch API failed: {response.ReasonPhrase} (Status Code: {(int)response.StatusCode}, URL: {endpoint})");

            throw new
                HttpRequestException($"Request to Twitch API failed: {response.ReasonPhrase} (Status Code: {(int)response.StatusCode})");
        } catch (HttpRequestException ex) {
            _logger.Log(LogLevel.Error, $"HTTP request error while calling {endpoint}.", ex);

            return null;
        }
    }

    /// <summary>
    ///     Represents the OAuth information required for authentication with the Twitch API.
    /// </summary>
    private class OAuthInfo {
        public OAuthInfo(string twitchClientId, string twitchOAuthToken) {
            TwitchClientId = twitchClientId
                             ?? throw new ArgumentNullException(nameof(twitchClientId), "Client ID cannot be null.");
            TwitchOAuthToken = twitchOAuthToken
                               ?? throw new ArgumentNullException(nameof(twitchOAuthToken),
                                                                  "OAuth token cannot be null.");
        }

        /// <summary>
        ///     Gets the Twitch Client ID.
        /// </summary>
        public string TwitchClientId { get; }

        /// <summary>
        ///     Gets the Twitch OAuth token.
        /// </summary>
        public string TwitchOAuthToken { get; }

        /// <summary>
        ///     Returns a string representation of the OAuth information, with the OAuth token obfuscated.
        /// </summary>
        /// <returns>
        ///     A <see cref="string" /> representing the obfuscated OAuth information.
        /// </returns>
        public override string ToString() {
            return $"Client ID: {TwitchClientId}, OAuth Token: {ObfuscateString(TwitchOAuthToken)}";
        }

        /// <summary>
        ///     Obfuscates a given string for security purposes.
        /// </summary>
        /// <param name="input">
        ///     The string to obfuscate.
        /// </param>
        /// <returns>
        ///     A <see cref="string" /> with the middle portion obfuscated or empty if the input is null or
        ///     empty.
        /// </returns>
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

    /// <summary>
    ///     Represents a response from the Twitch API with a data object of type <typeparamref name="T" />.
    /// </summary>
    /// <typeparam name="T">
    ///     The type of object contained in the data property of the response.
    /// </typeparam>
    public class TwitchApiResponse<T> {
        public TwitchApiResponse(T[] data) {
            Data = data ?? Array.Empty<T>();
        }

        /// <summary>
        ///     Gets the data retrieved from the Twitch API response.
        /// </summary>
        public T[] Data { get; }
    }

    /// <summary>
    ///     Represents a game data object retrieved from the Twitch API.
    /// </summary>
    public class GameData {
        /// <summary>
        ///     Gets or sets the URL of the game's box art.
        /// </summary>
        [JsonProperty("box_art_url")]
        public string BoxArtUrl { get; set; }

        /// <summary>
        ///     Gets or sets the ID of the game.
        /// </summary>
        [JsonProperty("id")]
        public string Id { get; set; }

        /// <summary>
        ///     Gets or sets the name of the game.
        /// </summary>
        [JsonProperty("name")]
        public string Name { get; set; }
    }
}