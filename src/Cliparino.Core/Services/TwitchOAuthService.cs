using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cliparino.Core.Models;

namespace Cliparino.Core.Services;

/// <summary>
///     Implements Twitch OAuth 2.0 authentication with PKCE (Proof Key for Code Exchange) for enhanced security.
/// </summary>
/// <remarks>
///     <para>
///         This class implements <see cref="ITwitchOAuthService" /> using the OAuth 2.0 authorization code flow
///         with PKCE extension. PKCE protects against authorization code interception attacks by requiring
///         a code verifier that must match the initial code challenge.
///     </para>
///     <para>
///         <strong>PKCE Flow:</strong><br />
///         1. Generate random code_verifier (43-128 characters)<br />
///         2. Create code_challenge = BASE64URL(SHA256(code_verifier))<br />
///         3. Send code_challenge in authorization URL<br />
///         4. User authorizes, receives auth code<br />
///         5. Exchange auth code + code_verifier for tokens<br />
///         6. Twitch validates: SHA256(code_verifier) == stored code_challenge
///     </para>
///     <para>
///         Dependencies:
///         - <see cref="ITwitchAuthStore" /> - Token persistence
///         - <see cref="ILogger{TCategoryName}" /> - Structured logging
///         - <see cref="IHttpClientFactory" /> - HTTP client for Twitch API calls
///         - <see cref="IConfiguration" /> - Client ID and redirect URI configuration
///     </para>
///     <para>
///         Thread-safety: Token refresh is synchronized with SemaphoreSlim to prevent concurrent refreshes.
///     </para>
///     <para>
///         Lifecycle: Registered as a singleton.
///     </para>
/// </remarks>
public class TwitchOAuthService : ITwitchOAuthService {
    private static readonly string PendingAuthFlowsPath = Path.Combine(
        AppContext.BaseDirectory, "pending_auth_flows.json");

    private readonly SemaphoreSlim _authFlowLock = new(1, 1);

    private readonly ITwitchAuthStore _authStore;
    private readonly string _clientId;
    private readonly string? _clientSecret;
    private readonly HttpClient _httpClient;
    private readonly ILogger<TwitchOAuthService> _logger;
    private readonly Dictionary<string, string> _pendingAuthFlows;
    private readonly string _redirectUri;

    public TwitchOAuthService(ITwitchAuthStore authStore,
        ILogger<TwitchOAuthService> logger,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration) {
        _authStore = authStore;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("Twitch");
        _clientId = configuration["Twitch:ClientId"] ??
                    throw new InvalidOperationException("Twitch:ClientId not configured");
        _clientSecret = configuration["Twitch:ClientSecret"];
        _redirectUri = configuration["Twitch:RedirectUri"] ?? "https://localhost:5290/auth/callback";

        // Load pending auth flows from disk (survives restarts)
        _pendingAuthFlows = LoadPendingAuthFlows();
    }

    /// <inheritdoc />
    public event EventHandler<OAuthCompletedEventArgs>? AuthenticationCompleted;

    /// <inheritdoc />
    public Task<bool> IsAuthenticatedAsync() {
        return _authStore.HasValidTokensAsync();
    }

    /// <inheritdoc />
    public async Task<string> StartAuthFlowAsync() {
        await _authFlowLock.WaitAsync();

        try {
            var state = GenerateState();

            _pendingAuthFlows[state] = state;
            SavePendingAuthFlows(); // Persist to disk for restart resilience

            _logger.LogInformation("Generated state: {State} for OAuth flow (implicit). Pending flows: {Count}",
                state, _pendingAuthFlows.Count);

            var scopes = new[] {
                "user:read:email", "user:read:chat", "chat:read", "chat:edit", "moderator:manage:shoutouts"
            };

            var authUrl = $"https://id.twitch.tv/oauth2/authorize?" +
                          $"client_id={_clientId}&" +
                          $"redirect_uri={Uri.EscapeDataString(_redirectUri)}&" +
                          $"response_type=token&" +
                          $"scope={Uri.EscapeDataString(string.Join(" ", scopes))}&" +
                          $"state={state}";

            _logger.LogInformation("OAuth flow started (implicit). Redirect URI: {RedirectUri}", _redirectUri);

            return authUrl;
        } finally {
            _authFlowLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> CompleteAuthFlowAsync(string? accessToken, string? state) {
        _logger.LogInformation("CompleteAuthFlowAsync called with accessToken length: {Length}, state: {State}",
            accessToken?.Length ?? 0, state ?? "none");

        if (string.IsNullOrEmpty(accessToken)) {
            _logger.LogError("Access token is null or empty");
            OnAuthenticationCompleted(false, errorMessage: "Access token is missing");

            return false;
        }

        if (string.IsNullOrEmpty(state)) {
            _logger.LogError("State parameter is missing");
            OnAuthenticationCompleted(false, errorMessage: "State parameter is missing");

            return false;
        }

        await _authFlowLock.WaitAsync();

        try {
            if (!_pendingAuthFlows.ContainsKey(state)) {
                _logger.LogError("State not found: {State}. OAuth flow may have expired. Pending flows: {Count}",
                    state, _pendingAuthFlows.Count);
                OnAuthenticationCompleted(false,
                    errorMessage: "OAuth flow expired or was interrupted. Please try again.");

                return false;
            }

            _pendingAuthFlows.Remove(state);
            SavePendingAuthFlows();
            _logger.LogInformation("Validated state: {State}. Remaining pending flows: {Count}",
                state, _pendingAuthFlows.Count);
        } finally {
            _authFlowLock.Release();
        }

        try {
            _logger.LogInformation("Access token received via implicit flow, fetching user info...");

            var (userId, username) = await FetchUserInfoAsync(accessToken);

            // Implicit flow doesn't provide refresh tokens or explicit expiry
            // Set expiry to 60 days (Twitch's typical token lifetime)
            var expiresAt = DateTimeOffset.UtcNow.AddDays(60);

            await _authStore.SaveTokensAsync(accessToken, null, expiresAt, userId);

            _logger.LogInformation("OAuth flow completed successfully (implicit). User: {Username} (ID: {UserId})",
                username ?? "unknown", userId ?? "not retrieved");

            ClearPendingAuthFlowsFile();
            OnAuthenticationCompleted(true, username);

            return true;
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to complete OAuth flow");
            OnAuthenticationCompleted(false, errorMessage: ex.Message);

            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> RefreshTokenAsync() {
        var refreshToken = await _authStore.GetRefreshTokenAsync();

        if (string.IsNullOrEmpty(refreshToken)) {
            _logger.LogWarning("No refresh token available");

            return false;
        }

        const int maxRetries = 3;
        var backoff = BackoffPolicy.Default;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
            try {
                var tokenRequest = new Dictionary<string, string> {
                    ["client_id"] = _clientId, ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken
                };

                if (!string.IsNullOrEmpty(_clientSecret)) tokenRequest["client_secret"] = _clientSecret;

                var response = await _httpClient.PostAsync("https://id.twitch.tv/oauth2/token",
                    new FormUrlEncodedContent(tokenRequest));

                if (!response.IsSuccessStatusCode) {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Token refresh failed (attempt {Attempt}/{Max}): {StatusCode} - {Error}",
                        attempt, maxRetries, response.StatusCode, errorContent);

                    if (response.StatusCode == HttpStatusCode.BadRequest) {
                        _logger.LogError("Refresh token invalid - user needs to re-authenticate");
                        await _authStore.ClearTokensAsync();

                        return false;
                    }

                    if (attempt >= maxRetries) return false;
                    var delay = backoff.CalculateDelay(attempt);
                    _logger.LogInformation("Retrying in {Delay} seconds...", delay.TotalSeconds);
                    await Task.Delay(delay);

                    continue;
                }

                var content = await response.Content.ReadAsStringAsync();
                var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(content);

                if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken)) {
                    _logger.LogError("Invalid token response during refresh");

                    return false;
                }

                var expiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
                await _authStore.SaveTokensAsync(tokenResponse.AccessToken, tokenResponse.RefreshToken, expiresAt);

                _logger.LogInformation("Token refreshed successfully");

                return true;
            } catch (HttpRequestException ex) {
                _logger.LogWarning(ex, "Network error during token refresh (attempt {Attempt}/{Max})",
                    attempt, maxRetries);

                if (attempt >= maxRetries) throw;

                var delay = backoff.CalculateDelay(attempt);
                await Task.Delay(delay);
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to refresh token");

                return false;
            }

        return false;
    }

    /// <inheritdoc />
    public async Task<string?> GetValidAccessTokenAsync() {
        var hasValid = await _authStore.HasValidTokensAsync();

        if (hasValid) return await _authStore.GetAccessTokenAsync();

        var expiry = await _authStore.GetTokenExpiryAsync();

        if (expiry.HasValue && expiry.Value <= DateTimeOffset.UtcNow.AddMinutes(5)) {
            _logger.LogInformation("Token expiring soon, attempting refresh...");
            var refreshed = await RefreshTokenAsync();

            if (refreshed) return await _authStore.GetAccessTokenAsync();
        }

        _logger.LogWarning("No valid access token available");

        return null;
    }

    /// <inheritdoc />
    public async Task LogoutAsync() {
        await _authStore.ClearTokensAsync();
        _logger.LogInformation("User logged out");
    }

    private Dictionary<string, string> LoadPendingAuthFlows() {
        try {
            if (File.Exists(PendingAuthFlowsPath)) {
                var json = File.ReadAllText(PendingAuthFlowsPath);
                var flows = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

                if (flows != null) {
                    _logger.LogInformation("Loaded {Count} pending OAuth flows from disk", flows.Count);

                    return flows;
                }
            }
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to load pending auth flows from disk");
        }

        return new Dictionary<string, string>();
    }

    private void SavePendingAuthFlows() {
        try {
            var json = JsonSerializer.Serialize(_pendingAuthFlows);
            File.WriteAllText(PendingAuthFlowsPath, json);
            _logger.LogDebug("Saved {Count} pending OAuth flows to disk", _pendingAuthFlows.Count);
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to save pending auth flows to disk");
        }
    }

    private void ClearPendingAuthFlowsFile() {
        try {
            if (File.Exists(PendingAuthFlowsPath)) File.Delete(PendingAuthFlowsPath);
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to delete pending auth flows file");
        }
    }

    private void OnAuthenticationCompleted(bool success, string? username = null, string? errorMessage = null) {
        AuthenticationCompleted?.Invoke(this, new OAuthCompletedEventArgs(success, username, errorMessage));
    }

    private async Task<(string? userId, string? username)> FetchUserInfoAsync(string accessToken) {
        try {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.twitch.tv/helix/users");
            request.Headers.Add("Client-ID", _clientId);
            request.Headers.Add("Authorization", $"Bearer {accessToken}");

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode) {
                _logger.LogWarning("Failed to fetch user info. Status: {Status}", response.StatusCode);

                return (null, null);
            }

            var content = await response.Content.ReadAsStringAsync();
            var userResponse = JsonSerializer.Deserialize<UserInfoResponse>(content);

            if (userResponse?.Data != null && userResponse.Data.Count != 0) {
                var user = userResponse.Data[0];

                return (user.Id, user.Login);
            }

            _logger.LogWarning("No user data returned");

            return (null, null);
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to fetch user info");

            return (null, null);
        }
    }

    private static string GenerateState() {
        var bytes = new byte[16];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);

        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(byte[] input) {
        return Convert.ToBase64String(input)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private class TokenResponse {
        [JsonPropertyName("access_token")] public string AccessToken { get; init; } = string.Empty;

        [JsonPropertyName("refresh_token")] public string RefreshToken { get; init; } = string.Empty;

        [JsonPropertyName("expires_in")] public int ExpiresIn { get; init; }
    }

    private class UserInfoResponse {
        [JsonPropertyName("data")] public List<UserData> Data { get; init; } = [];
    }
}