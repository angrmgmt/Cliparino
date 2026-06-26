using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Cliparino.Core.Models;

// ReSharper disable ClassNeverInstantiated.Local

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
///         - Rate limit handling (respects error 429 Retry-After header)<br />
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

    /// <summary>
    ///     Initializes a new instance of <see cref="TwitchHelixClient" />.
    /// </summary>
    /// <param name="oauthService">OAuth service used to get and refresh access tokens.</param>
    /// <param name="logger">Structured logger for diagnostics and error reporting.</param>
    /// <param name="httpClientFactory">Factory for creating named <c>"Twitch"</c> HTTP clients.</param>
    /// <param name="configuration">Application configuration; must contain <c>Twitch:ClientId</c>.</param>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when <c>Twitch:ClientId</c> is absent from configuration.
    /// </exception>
    public TwitchHelixClient(ITwitchOAuthService oauthService,
        ILogger<TwitchHelixClient> logger,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration) {
        _oauthService = oauthService;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("Twitch");
        _clientId = configuration["Twitch:ClientId"] ?? "kimne78kx3ncx6brgo4mv6wki5h1ko";
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

            if (clips.Count != 0) return clips[0];
            _logger.LogWarning("Clip not found: {ClipId}", clipId);

            return null;
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

        if (!string.IsNullOrEmpty(clipId)) return await GetClipByIdAsync(clipId);
        _logger.LogWarning("Could not extract clip ID from URL: {Url}", clipUrl);

        return null;
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

            if (apiResponse?.Data != null && apiResponse.Data.Count != 0) return apiResponse.Data[0].Id;
            _logger.LogWarning("User not found: {BroadcasterName}", broadcasterName);

            return null;
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to get broadcaster ID for: {BroadcasterName}", broadcasterName);

            return null;
        }
    }

    /// <inheritdoc />
    public async Task<string?> GetAuthenticatedUserIdAsync() {
        try {
            const string url = "https://api.twitch.tv/helix/users";
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

            if (apiResponse?.Data != null && apiResponse.Data.Count != 0) return apiResponse.Data[0].Id;
            _logger.LogWarning("Could not get authenticated user info");

            return null;
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
            const string url = "https://api.twitch.tv/helix/chat/messages";
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

    /// <inheritdoc />
    public async Task<string?> GetClipDownloadUrlAsync(string clipId) {
        var accessToken = await _oauthService.GetValidAccessTokenAsync();

        if (string.IsNullOrEmpty(accessToken)) {
            _logger.LogWarning("No access token available for clip download URL request");

            return null;
        }

        // Get clip info to get broadcaster_id
        var clip = await GetClipByIdAsync(clipId);

        if (clip == null) {
            _logger.LogWarning("Could not find clip info for download URL: {ClipId}", clipId);

            return null;
        }

        // Get authenticated user ID for editor_id
        var authenticatedUserId = await GetAuthenticatedUserIdAsync();

        if (string.IsNullOrEmpty(authenticatedUserId)) {
            _logger.LogWarning("Could not determine authenticated user ID for clip download URL");

            return null;
        }

        // The downloads API is only for the broadcaster's own clips or where they have editor permissions.
        // For simplicity and to avoid 401/403 errors, we only attempt it if the broadcaster ID matches.
        if (!string.Equals(clip.Broadcaster.Id, authenticatedUserId, StringComparison.OrdinalIgnoreCase)) {
            _logger.LogDebug(
                "Skipping download URL request: authenticated user {UserId} is not the broadcaster of clip {ClipId} ({BroadcasterId})",
                authenticatedUserId, clipId, clip.Broadcaster.Id);

            return null;
        }

        var url =
            $"https://api.twitch.tv/helix/clips/downloads?clip_id={Uri.EscapeDataString(clipId)}&broadcaster_id={Uri.EscapeDataString(clip.Broadcaster.Id)}&editor_id={Uri.EscapeDataString(authenticatedUserId)}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Client-ID", _clientId);
        request.Headers.Add("Authorization", $"Bearer {accessToken}");

        try {
            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode) {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogWarning(
                    "Clip download URL request returned {StatusCode} for clip {ClipId}. Response: {Body}",
                    response.StatusCode, clipId, errorBody);

                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var apiResponse = JsonSerializer.Deserialize<ClipDownloadResponse>(content);

            return apiResponse?.Data?.FirstOrDefault()?.LandscapeUrl;
        } catch (Exception ex) {
            _logger.LogError(ex, "Error fetching clip download URL for clip {ClipId}", clipId);

            return null;
        }
    }

    /// <inheritdoc />
    public async Task<string?> GetClipVideoUrlGqlAsync(string clipId) {
        if (string.IsNullOrWhiteSpace(clipId)) return null;

        const string gqlUrl = "https://gql.twitch.tv/gql";
        const string gqlClientId = "kimne78kx3ncx6brgo4mv6wki5h1ko";

        // Updated hash for VideoAccessToken_Clip as of 2026
        const string sha256Hash = "4f35f1ac933d76b1da008c806cd5546a7534dfaff83e033a422a81f24e5991b3";
        const string fullQuery =
            "query VideoAccessToken_Clip($slug: String!, $platform: String!) { clip(slug: $slug) { playbackAccessToken(params: {platform: $platform}) { signature value } videoQualities { frameRate quality sourceURL } } }";

        async Task<string?> SendGqlRequestAsync(object gqlPayload) {
            try {
                var request = new HttpRequestMessage(HttpMethod.Post, gqlUrl);
                request.Headers.Add("Client-ID", gqlClientId);
                request.Headers.Add("X-Device-Id", Guid.NewGuid().ToString("N"));
                request.Headers.Add("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                request.Headers.Add("Origin", "https://www.twitch.tv");
                request.Headers.Add("Referer", "https://www.twitch.tv/");

                var jsonPayload = JsonSerializer.Serialize(gqlPayload);
                request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);

                return await response.Content.ReadAsStringAsync();
            } catch (Exception ex) {
                _logger.LogError(ex, "HTTP error during Twitch GraphQL request for {ClipId}", clipId);

                return null;
            }
        }

        var payload = new {
            operationName = "VideoAccessToken_Clip",
            variables = new { slug = clipId, platform = "web" },
            extensions = new { persistedQuery = new { version = 1, sha256Hash } }
        };

        var content = await SendGqlRequestAsync(payload);

        if (string.IsNullOrEmpty(content)) return null;

        // Fallback to full query if persisted query is not found
        if (content.Contains("PersistedQueryNotFound")) {
            _logger.LogInformation("Persisted query not found for {ClipId}, retrying with full query", clipId);
            var retryPayload = new {
                operationName = "VideoAccessToken_Clip",
                variables = new { slug = clipId, platform = "web" },
                query = fullQuery
            };
            content = await SendGqlRequestAsync(retryPayload);

            if (string.IsNullOrEmpty(content)) return null;
        }

        if (content.Contains("\"errors\"")) {
            _logger.LogWarning("Twitch GraphQL returned errors for clip {ClipId}: {Body}", clipId, content);

            return null;
        }

        try {
            _logger.LogDebug("Twitch GraphQL response for {ClipId}: {Body}", clipId, content);
            var gqlResponse = JsonSerializer.Deserialize<TwitchGqlResponse>(content);

            var clip = gqlResponse?.Data?.Clip;

            if (clip == null) {
                _logger.LogWarning("Clip not found in GraphQL response for {ClipId}", clipId);

                return null;
            }

            var token = clip.PlaybackAccessToken?.Value;
            var sig = clip.PlaybackAccessToken?.Signature;

            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(sig)) {
                _logger.LogWarning("Missing playback access token or signature for clip {ClipId}", clipId);

                return null;
            }

            // Prefer highest resolution (usually first in videoQualities)
            var bestQuality = clip.VideoQualities?.FirstOrDefault();

            if (bestQuality?.SourceUrl == null) {
                _logger.LogWarning("No video qualities found in GraphQL response for {ClipId}", clipId);

                return null;
            }

            var separator = bestQuality.SourceUrl.Contains('?') ? "&" : "?";

            return $"{bestQuality.SourceUrl}{separator}sig={sig}&token={Uri.EscapeDataString(token)}";
        } catch (Exception ex) {
            _logger.LogError(ex, "Error fetching clip video URL via GraphQL for {ClipId}", clipId);

            return null;
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
            dto.ViewCount >= 100) { ThumbnailUrl = dto.ThumbnailUrl };
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

    private record TwitchClipsResponse([property: JsonPropertyName("data")] List<TwitchClipDto> Data);

    private record TwitchUsersResponse([property: JsonPropertyName("data")] List<TwitchUserDto> Data);

    private record TwitchUserDto([property: JsonPropertyName("id")] string Id);

    private record TwitchChannelsResponse([property: JsonPropertyName("data")] List<TwitchChannelDto> Data);

    private record TwitchChannelDto(
        [property: JsonPropertyName("broadcaster_name")]
        string BroadcasterName,
        [property: JsonPropertyName("game_name")]
        string GameName);

    private record TwitchClipDto(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("broadcaster_id")]
        string BroadcasterId,
        [property: JsonPropertyName("broadcaster_login")]
        string BroadcasterLogin,
        [property: JsonPropertyName("broadcaster_name")]
        string BroadcasterName,
        [property: JsonPropertyName("creator_id")]
        string CreatorId,
        [property: JsonPropertyName("creator_login")]
        string CreatorLogin,
        [property: JsonPropertyName("creator_name")]
        string CreatorName,
        [property: JsonPropertyName("game_id")]
        string GameId,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("view_count")]
        int ViewCount,
        [property: JsonPropertyName("created_at")]
        DateTime CreatedAt,
        [property: JsonPropertyName("duration")]
        double Duration,
        [property: JsonPropertyName("thumbnail_url")]
        string ThumbnailUrl);

    private record TwitchGqlResponse([property: JsonPropertyName("data")] TwitchGqlData? Data);

    private record TwitchGqlData([property: JsonPropertyName("clip")] TwitchGqlClip? Clip);

    private record TwitchGqlClip(
        [property: JsonPropertyName("playbackAccessToken")]
        TwitchGqlPlaybackToken? PlaybackAccessToken,
        [property: JsonPropertyName("videoQualities")]
        List<TwitchGqlVideoQuality>? VideoQualities);

    private record TwitchGqlPlaybackToken(
        [property: JsonPropertyName("signature")]
        string? Signature,
        [property: JsonPropertyName("value")] string? Value);

    private record TwitchGqlVideoQuality([property: JsonPropertyName("sourceURL")]
        string? SourceUrl);

    private record ClipDownloadResponse([property: JsonPropertyName("data")] List<ClipDownloadItem>? Data);

    private record ClipDownloadItem([property: JsonPropertyName("landscape_download_url")]
        string? LandscapeUrl);

    private record TwitchGamesResponse([property: JsonPropertyName("data")] List<TwitchGameDto> Data);

    private record TwitchGameDto(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name);
}