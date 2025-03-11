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
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Streamer.bot.Common.Events;
using Streamer.bot.Plugin.Interface;
using Streamer.bot.Plugin.Interface.Enums;
using Streamer.bot.Plugin.Interface.Model;
using Twitch.Common.Models.Api;

#endregion

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

    public async Task<ClipManager.ClipsResponse> FetchClipsAsync(string broadcasterId, string cursor = null) {
        var url = BuildClipsApiUrl(broadcasterId, cursor);

        _logger.Log(LogLevel.Debug, $"Calling Twitch API with URL: {url}");


        var request = new HttpRequestMessage(HttpMethod.Get, url);

        try {
            request.Headers.Add("Client-Id", $"{_oauthInfo.TwitchClientId}");
            request.Headers.Add("Authorization", $"Bearer {_oauthInfo.TwitchOAuthToken}");

            var response = await HTTPManager.Client.SendAsync(request);

            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();

            _logger.Log(LogLevel.Debug, $"API Response: {responseBody}");

            return JsonConvert.DeserializeObject<ClipManager.ClipsResponse>(responseBody);
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error occurred while fetching clips.", ex);

            return null;
        } finally {
            _logger.Log(LogLevel.Info, "Finished fetching clips.");
        }
    }

    private static string BuildClipsApiUrl(string broadcasterId, string cursor = "", int first = 20) {
        var url = $"https://api.twitch.tv/helix/clips?broadcaster_id={broadcasterId}";

        if (!string.IsNullOrWhiteSpace(cursor))
            url += $"&after={cursor}";
        else
            url += $"&first={first}";

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
        LogHttpManagerValidity();

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

    private static void ValidateEndpoint(string endpoint) {
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentNullException(nameof(endpoint), "Endpoint cannot be null or empty.");
    }

    private void ValidateOAuthInfo() {
        if (string.IsNullOrWhiteSpace(_oauthInfo.TwitchClientId)
            || string.IsNullOrWhiteSpace(_oauthInfo.TwitchOAuthToken))
            throw new InvalidOperationException("Twitch OAuth information is missing or invalid.");
    }

    private void LogHttpManagerValidity() {
        var httpManValid = HTTPManager != null;
        var httpClientValid = HTTPManager?.Client != null;
        var baseUriValid = HTTPManager?.Client?.BaseAddress != null;
        var validConditionsMet = httpManValid && httpClientValid && baseUriValid;

        _logger.Log(LogLevel.Debug, $"Checking validity of HTTP manager components... {validConditionsMet}");
        _logger.Log(LogLevel.Debug,
                    $"HTTP Manager: {httpManValid}, HTTP Client: {httpClientValid}, Base URI: {baseUriValid}");
    }

    private string GetCompleteUrl(string endpoint) {
        var baseAddress = HTTPManager?.Client?.BaseAddress?.ToString() ?? "https://www.google.com";

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

    public class GameData {
        [JsonProperty("box_art_url")] public string BoxArtUrl { get; set; }
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
    }
}