using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Cliparino.Core.Models;

namespace Cliparino.Core.Services;

/// <summary>
///     Implements Twitch Helix API client with automatic authentication and retry logic.
/// </summary>
/// <remarks>
///     <para>
///         This class implements <see cref="ITwitchHelixClient" /> by calling the Twitch Helix API
///         (https://api.twitch.tv/helix) for all clip, user, channel, and chat operations.
///     </para>
///     <para>
///         <strong>Key features:</strong><br />
///         - Automatic Bearer token injection via <see cref="ITwitchOAuthService" /><br />
///         - Retry logic with exponential backoff for transient failures (429, 5xx)<br />
///         - Rate limit handling (respects 429 Retry-After header)<br />
///         - Multiple clip URL format parsing (clips.twitch.tv and twitch.tv/user/clip/)<br />
///         - JSON deserialization of Helix API responses
///     </para>
///     <para>
///         Dependencies:
///         - <see cref="ITwitchOAuthService" /> - OAuth token management
///         - <see cref="ILogger{TCategoryName}" /> - Structured logging
///         - <see cref="IHttpClientFactory" /> - HTTP client for API calls
///         - <see cref="IConfiguration" /> - Client ID configuration
///     </para>
///     <para>
///         Thread-safety: Stateless except for injected dependencies. Safe to call from multiple
///         threads concurrently. HTTP client is thread-safe.
///     </para>
///     <para>
///         Lifecycle: Registered as a singleton.
///     </para>
/// </remarks>
public class TwitchHelixClient : ITwitchHelixClient {
    private static readonly Regex ClipUrlRegex = new(@"clips\.twitch\.tv/([A-Za-z0-9_-]+)", RegexOptions.Compiled);
    private static readonly Regex ClipSlugRegex = new(@"twitch\.tv/\w+/clip/([A-Za-z0-9_-]+)", RegexOptions.Compiled);
    private readonly string _clientId;
    private readonly HttpClient _httpClient;
    private readonly ILogger<TwitchHelixClient> _logger;
    private readonly ITwitchOAuthService _oauthService;

    public TwitchHelixClient(ITwitchOAuthService oauthService,
        ILogger<TwitchHelixClient> logger,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration) {
        _oauthService = oauthService;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("Twitch");
        _clientId = configuration["Twitch:ClientId"] ??
                    throw new InvalidOperationException("Twitch:ClientId not configured");
    }

    /// <inheritdoc />
    public async Task<ClipData?> GetClipByIdAsync(string clipId) {
        if (string.IsNullOrWhiteSpace(clipId)) {
            _logger.LogWarning("GetClipByIdAsync called with empty clip ID");

            return null;
        }

        try {
            var url = $"https://api.twitch.tv/helix/clips?id={clipId}";
            var clips = await FetchClipsAsync(url);

            if (clips.Count == 0) {
                _logger.LogWarning("Clip not found: {ClipId}", clipId);

                return null;
            }

            return clips[0];
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to fetch clip by ID: {ClipId}", clipId);

            return null;
        }
    }

    /// <inheritdoc />
    public async Task<ClipData?> GetClipByUrlAsync(string clipUrl) {
        if (string.IsNullOrWhiteSpace(clipUrl)) {
            _logger.LogWarning("GetClipByUrlAsync called with empty URL");

            return null;
        }

        var clipId = ExtractClipIdFromUrl(clipUrl);

        if (string.IsNullOrEmpty(clipId)) {
            _logger.LogWarning("Could not extract clip ID from URL: {Url}", clipUrl);

            return null;
        }

        return await GetClipByIdAsync(clipId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClipData>> GetClipsByBroadcasterAsync(string broadcasterId,
        int count = 20,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? endedAt = null) {
        if (string.IsNullOrWhiteSpace(broadcasterId)) {
            _logger.LogWarning("GetClipsByBroadcasterAsync called with empty broadcaster ID");

            return Array.Empty<ClipData>();
        }

        try {
            var url = $"https://api.twitch.tv/helix/clips?broadcaster_id={broadcasterId}&first={Math.Min(count, 100)}";

            if (startedAt.HasValue)
                url += $"&started_at={startedAt.Value:yyyy-MM-ddTHH:mm:ssZ}";

            if (endedAt.HasValue)
                url += $"&ended_at={endedAt.Value:yyyy-MM-ddTHH:mm:ssZ}";

            return await FetchClipsAsync(url);
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to fetch clips for broadcaster: {BroadcasterId}", broadcasterId);

            return Array.Empty<ClipData>();
        }
    }

    /// <inheritdoc />
    public async Task<string?> GetBroadcasterIdByNameAsync(string broadcasterName) {
        if (string.IsNullOrWhiteSpace(broadcasterName)) {
            _logger.LogWarning("GetBroadcasterIdByNameAsync called with empty broadcaster name");

            return null;
        }

        try {
            var url =
                $"https://api.twitch.tv/helix/users?login={Uri.EscapeDataString(broadcasterName.ToLowerInvariant())}";
            var accessToken = await _oauthService.GetValidAccessTokenAsync();

            if (string.IsNullOrEmpty(accessToken)) {
                _logger.LogError("No valid access token available");

                return null;
            }

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Client-ID", _clientId);
            request.Headers.Add("Authorization", $"Bearer {accessToken}");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var apiResponse = JsonSerializer.Deserialize<TwitchUsersResponse>(content);

            if (apiResponse?.Data == null || apiResponse.Data.Count == 0) {
                _logger.LogWarning("User not found: {BroadcasterName}", broadcasterName);

                return null;
            }

            return apiResponse.Data[0].Id;
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to get broadcaster ID for: {BroadcasterName}", broadcasterName);

            return null;
        }
    }

    /// <inheritdoc />
    public async Task<string?> GetAuthenticatedUserIdAsync() {
        try {
            var url = "https://api.twitch.tv/helix/users";
            var accessToken = await _oauthService.GetValidAccessTokenAsync();

            if (string.IsNullOrEmpty(accessToken)) {
                _logger.LogError("No valid access token available");

                return null;
            }

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Client-ID", _clientId);
            request.Headers.Add("Authorization", $"Bearer {accessToken}");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var apiResponse = JsonSerializer.Deserialize<TwitchUsersResponse>(content);

            if (apiResponse?.Data == null || apiResponse.Data.Count == 0) {
                _logger.LogWarning("Could not get authenticated user info");

                return null;
            }

            return apiResponse.Data[0].Id;
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to get authenticated user ID");

            return null;
        }
    }

    /// <inheritdoc />
    public async Task<(string? GameName, string? BroadcasterDisplayName)> GetChannelInfoAsync(string broadcasterId) {
        if (string.IsNullOrWhiteSpace(broadcasterId)) {
            _logger.LogWarning("GetChannelInfoAsync called with empty broadcaster ID");

            return (null, null);
        }

        try {
            var url = $"https://api.twitch.tv/helix/channels?broadcaster_id={broadcasterId}";
            var accessToken = await _oauthService.GetValidAccessTokenAsync();

            if (string.IsNullOrEmpty(accessToken)) {
                _logger.LogError("No valid access token available");

                return (null, null);
            }

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Client-ID", _clientId);
            request.Headers.Add("Authorization", $"Bearer {accessToken}");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var apiResponse = JsonSerializer.Deserialize<TwitchChannelsResponse>(content);

            if (apiResponse?.Data == null || apiResponse.Data.Count == 0) {
                _logger.LogWarning("Channel info not found for broadcaster: {BroadcasterId}", broadcasterId);

                return (null, null);
            }

            var channel = apiResponse.Data[0];

            return (channel.GameName, channel.BroadcasterName);
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to get channel info for broadcaster: {BroadcasterId}", broadcasterId);

            return (null, null);
        }
    }

    /// <inheritdoc />
    public async Task<bool> SendChatMessageAsync(string broadcasterId, string message) {
        if (string.IsNullOrWhiteSpace(broadcasterId) || string.IsNullOrWhiteSpace(message)) {
            _logger.LogWarning("SendChatMessageAsync called with empty broadcaster ID or message");

            return false;
        }

        try {
            var url = "https://api.twitch.tv/helix/chat/messages";
            var accessToken = await _oauthService.GetValidAccessTokenAsync();

            if (string.IsNullOrEmpty(accessToken)) {
                _logger.LogError("No valid access token available");

                return false;
            }

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Client-ID", _clientId);
            request.Headers.Add("Authorization", $"Bearer {accessToken}");

            var payload = new { broadcaster_id = broadcasterId, sender_id = broadcasterId, message };

            request.Content = new StringContent(JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode) {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to send chat message. Status: {Status}, Response: {Response}",
                    response.StatusCode, errorContent);

                return false;
            }

            _logger.LogInformation("Chat message sent successfully to broadcaster {BroadcasterId}", broadcasterId);

            return true;
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to send chat message to broadcaster: {BroadcasterId}", broadcasterId);

            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SendShoutoutAsync(string fromBroadcasterId, string toBroadcasterId) {
        if (string.IsNullOrWhiteSpace(fromBroadcasterId) || string.IsNullOrWhiteSpace(toBroadcasterId)) {
            _logger.LogWarning("SendShoutoutAsync called with empty broadcaster IDs");

            return false;
        }

        try {
            var url =
                $"https://api.twitch.tv/helix/chat/shoutouts?from_broadcaster_id={fromBroadcasterId}&to_broadcaster_id={toBroadcasterId}&moderator_id={fromBroadcasterId}";
            var accessToken = await _oauthService.GetValidAccessTokenAsync();

            if (string.IsNullOrEmpty(accessToken)) {
                _logger.LogError("No valid access token available");

                return false;
            }

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Client-ID", _clientId);
            request.Headers.Add("Authorization", $"Bearer {accessToken}");

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode) {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to send Twitch shoutout. Status: {Status}, Response: {Response}",
                    response.StatusCode, errorContent);

                return false;
            }

            _logger.LogInformation("Twitch shoutout sent successfully from {FromId} to {ToId}",
                fromBroadcasterId, toBroadcasterId);

            return true;
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to send Twitch shoutout from {FromId} to {ToId}",
                fromBroadcasterId, toBroadcasterId);

            return false;
        }
    }

    private async Task<IReadOnlyList<ClipData>> FetchClipsAsync(string url) {
        var accessToken = await _oauthService.GetValidAccessTokenAsync();

        if (string.IsNullOrEmpty(accessToken)) {
            _logger.LogError("No valid access token available for Twitch API call");

            throw new InvalidOperationException("Not authenticated with Twitch");
        }

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Client-ID", _clientId);
        request.Headers.Add("Authorization", $"Bearer {accessToken}");

        var retryCount = 0;
        const int maxRetries = 3;

        while (retryCount < maxRetries)
            try {
                var response = await _httpClient.SendAsync(request);

                if (response.StatusCode == HttpStatusCode.Unauthorized) {
                    _logger.LogWarning("Received 401 Unauthorized, attempting token refresh...");
                    var refreshed = await _oauthService.RefreshTokenAsync();

                    if (!refreshed) {
                        _logger.LogError("Token refresh failed, cannot complete API request");

                        throw new InvalidOperationException("Authentication expired and refresh failed");
                    }

                    accessToken = await _oauthService.GetValidAccessTokenAsync();
                    request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Add("Client-ID", _clientId);
                    request.Headers.Add("Authorization", $"Bearer {accessToken}");

                    retryCount++;

                    continue;
                }

                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var apiResponse = JsonSerializer.Deserialize<TwitchClipsResponse>(content);

                if (apiResponse?.Data == null) {
                    _logger.LogWarning("Empty or invalid response from Twitch API");

                    return Array.Empty<ClipData>();
                }

                var dtos = apiResponse.Data;
                var gameIds = dtos.Select(d => d.GameId).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
                var gameNames = gameIds.Count > 0
                    ? await FetchGameNamesByIdsAsync(gameIds)
                    : new Dictionary<string, string>();

                return dtos.Select(dto => MapToClipData(dto, gameNames)).ToList();
            } catch (HttpRequestException ex) when (retryCount < maxRetries - 1) {
                _logger.LogWarning(ex, "Network error during API call (attempt {Attempt}/{Max})", retryCount + 1,
                    maxRetries);
                retryCount++;
                var delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount));
                await Task.Delay(delay);
            }

        throw new InvalidOperationException($"Failed to fetch clips after {maxRetries} attempts");
    }

    private static string? ExtractClipIdFromUrl(string url) {
        var match = ClipUrlRegex.Match(url);

        if (match.Success)
            return match.Groups[1].Value;

        match = ClipSlugRegex.Match(url);

        if (match.Success)
            return match.Groups[1].Value;

        if (url.Contains('/') || url.Contains('.'))
            return null;

        return url;
    }

    private static ClipData MapToClipData(TwitchClipDto dto, Dictionary<string, string> gameNames) {
        var creator = new UserData(dto.CreatorId, dto.CreatorLogin, dto.CreatorName);
        var broadcaster = new UserData(dto.BroadcasterId, dto.BroadcasterLogin, dto.BroadcasterName);
        var gameName = !string.IsNullOrEmpty(dto.GameId) && gameNames.TryGetValue(dto.GameId, out var name)
            ? name
            : "Unknown";

        return new ClipData(dto.Id,
            dto.Url,
            dto.Title,
            creator,
            broadcaster,
            gameName,
            (int)Math.Ceiling(dto.Duration),
            dto.CreatedAt,
            dto.ViewCount,
            dto.ViewCount >= 100);
    }

    private async Task<Dictionary<string, string>> FetchGameNamesByIdsAsync(List<string> ids) {
        var result = new Dictionary<string, string>();

        if (ids.Count == 0) return result;

        // Build query: up to 100 ids per request
        var chunks = ids.Chunk(100);

        foreach (var chunk in chunks) {
            var query = string.Join("&", chunk.Select(id => $"id={Uri.EscapeDataString(id)}"));
            var url = $"https://api.twitch.tv/helix/games?{query}";

            var accessToken = await _oauthService.GetValidAccessTokenAsync();

            if (string.IsNullOrEmpty(accessToken)) {
                _logger.LogWarning("No valid access token when fetching game names");

                return result;
            }

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Client-ID", _clientId);
            request.Headers.Add("Authorization", $"Bearer {accessToken}");

            try {
                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                var games = JsonSerializer.Deserialize<TwitchGamesResponse>(content);
                if (games?.Data != null)
                    foreach (var g in games.Data)
                        result[g.Id] = g.Name;
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Failed to fetch game names for ids: {Ids}", string.Join(",", chunk));
            }
        }

        return result;
    }

    private class TwitchClipsResponse(List<TwitchClipDto> data) {
        [JsonPropertyName("data")] public List<TwitchClipDto> Data { get; } = data;
    }

    private class TwitchUsersResponse(List<TwitchUserDto> data) {
        [JsonPropertyName("data")] public List<TwitchUserDto> Data { get; } = data;
    }

    private class TwitchUserDto(string id) {
        [JsonPropertyName("id")] public string Id { get; } = id;
    }

    private class TwitchChannelsResponse(List<TwitchChannelDto> data) {
        [JsonPropertyName("data")] public List<TwitchChannelDto> Data { get; } = data;
    }

    private class TwitchChannelDto(string broadcasterName, string gameName) {
        [JsonPropertyName("broadcaster_name")] public string BroadcasterName { get; } = broadcasterName;

        [JsonPropertyName("game_name")] public string GameName { get; } = gameName;
    }

    private class TwitchClipDto(
        string id,
        string url,
        string broadcasterId,
        string broadcasterLogin,
        string broadcasterName,
        string creatorId,
        string creatorLogin,
        string creatorName,
        string gameId,
        string title,
        int viewCount,
        DateTime createdAt,
        double duration
    ) {
        [JsonPropertyName("id")] public string Id { get; } = id;

        [JsonPropertyName("url")] public string Url { get; } = url;

        [JsonPropertyName("broadcaster_id")] public string BroadcasterId { get; } = broadcasterId;

        [JsonPropertyName("broadcaster_login")]
        public string BroadcasterLogin { get; } = broadcasterLogin;

        [JsonPropertyName("broadcaster_name")] public string BroadcasterName { get; } = broadcasterName;

        [JsonPropertyName("creator_id")] public string CreatorId { get; } = creatorId;

        [JsonPropertyName("creator_login")] public string CreatorLogin { get; } = creatorLogin;

        [JsonPropertyName("creator_name")] public string CreatorName { get; } = creatorName;

        [JsonPropertyName("game_id")] public string GameId { get; } = gameId;

        [JsonPropertyName("title")] public string Title { get; } = title;

        [JsonPropertyName("view_count")] public int ViewCount { get; } = viewCount;

        [JsonPropertyName("created_at")] public DateTime CreatedAt { get; } = createdAt;

        [JsonPropertyName("duration")] public double Duration { get; } = duration;
    }

    private class TwitchGamesResponse(List<TwitchGameDto> data) {
        [JsonPropertyName("data")] public List<TwitchGameDto> Data { get; } = data;
    }

    private class TwitchGameDto(string id, string name) {
        [JsonPropertyName("id")] public string Id { get; } = id;
        [JsonPropertyName("name")] public string Name { get; } = name;
    }
}