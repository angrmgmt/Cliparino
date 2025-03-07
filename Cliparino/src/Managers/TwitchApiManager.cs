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
    private readonly HttpManager _httpManager;
    private readonly CPHLogger _logger;
    private readonly OAuthInfo _oauthInfo;

    public TwitchApiManager(IInlineInvokeProxy cph, CPHLogger logger, HttpManager httpManager) {
        _cph = cph ?? throw new ArgumentNullException(nameof(cph));
        _logger = logger;
        _httpManager = httpManager;
        _oauthInfo = new OAuthInfo(cph.TwitchClientId, cph.TwitchOAuthToken);
    }

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
        _logger.Log(LogLevel.Info, $"Shoutout sent for {username}.");
    }

    private static string FormatShoutoutMessage(TwitchUserInfoEx user, string message) {
        return message.Replace("[[userName]]", user.UserName).Replace("[[userGameName]]", user.Game);
    }

    public async Task<ClipData> FetchClipById(string clipId) {
        return await FetchDataAsync<ClipData>($"clips?id={clipId}");
    }

    public async Task<GameData> FetchGameById(string gameId) {
        return await FetchDataAsync<GameData>($"games?id={gameId}");
    }

    private async Task<T> FetchDataAsync<T>(string endpoint) {
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentNullException(nameof(endpoint), "Endpoint cannot be null or empty.");

        if (string.IsNullOrWhiteSpace(_oauthInfo.TwitchClientId)
            || string.IsNullOrWhiteSpace(_oauthInfo.TwitchOAuthToken))
            throw new InvalidOperationException("Twitch OAuth information is missing or invalid.");

        var completeUrl = new Uri(_httpManager.Client.BaseAddress, endpoint).ToString();

        _logger.Log(LogLevel.Debug, $"Preparing to make GET request to endpoint: {completeUrl}");

        try {
            var content = await SendHttpRequestAsync(endpoint);

            if (string.IsNullOrWhiteSpace(content)) return default;

            _logger.Log(LogLevel.Debug, $"Response content: {content}");
            var apiResponse = JsonConvert.DeserializeObject<TwitchApiResponse<T>>(content);

            if (apiResponse?.Data != null && apiResponse.Data.Length > 0) {
                _logger.Log(LogLevel.Info, "Successfully retrieved and deserialized data from Twitch API.");

                return apiResponse.Data[0];
            }

            _logger.Log(LogLevel.Error, $"No data returned from Twitch API for endpoint: {endpoint}");

            return default;
        } catch (JsonException ex) {
            _logger.Log(LogLevel.Error, $"JSON deserialization error for response from {endpoint}.", ex);

            return default;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"Unexpected error in FetchDataAsync for endpoint: {endpoint}", ex);

            return default;
        }
    }

    private void ConfigureHttpRequestHeaders() {
        var client = _httpManager.Client;

        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("Client-ID", _oauthInfo.TwitchClientId);
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_oauthInfo.TwitchOAuthToken}");
    }

    private async Task<string> SendHttpRequestAsync(string endpoint) {
        try {
            ConfigureHttpRequestHeaders();
            _logger.Log(LogLevel.Debug, "HTTP headers set successfully. Initiating request...");

            var response = await _httpManager.Client.GetAsync(endpoint);

            _logger.Log(LogLevel.Debug, $"Received response: {response.StatusCode} ({(int)response.StatusCode})");

            if (response.IsSuccessStatusCode) return await response.Content.ReadAsStringAsync();

            _logger.Log(LogLevel.Error,
                        $"Request to Twitch API failed: {response.ReasonPhrase} (Status Code: {(int)response.StatusCode}, URL: {endpoint})");

            return null;
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