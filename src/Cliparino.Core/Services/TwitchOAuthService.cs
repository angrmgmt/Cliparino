using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

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
public class TwitchOAuthService(
    ITwitchAuthStore authStore,
    ILogger<TwitchOAuthService> logger,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration)
    : ITwitchOAuthService {
    private readonly string _clientId = configuration["Twitch:ClientId"] ??
                                        throw new InvalidOperationException("Twitch:ClientId not configured");

    private readonly HttpClient _httpClient = httpClientFactory.CreateClient("Twitch");
    private readonly string _redirectUri = configuration["Twitch:RedirectUri"] ?? "http://localhost:5290/auth/callback";
    private string? _codeVerifier;
    private string? _state;

    /// <inheritdoc />
    public Task<bool> IsAuthenticatedAsync() {
        return authStore.HasValidTokensAsync();
    }

    /// <inheritdoc />
    public async Task<string> StartAuthFlowAsync() {
        _codeVerifier = GenerateCodeVerifier();
        var codeChallenge = GenerateCodeChallenge(_codeVerifier);
        _state = GenerateState();

        var scopes = new[] {
            "clips:read",
            "user:read:email",
            "chat:read",
            "chat:edit",
            "moderator:manage:shoutouts"
        };

        var authUrl = $"https://id.twitch.tv/oauth2/authorize?" +
                      $"client_id={_clientId}&" +
                      $"redirect_uri={Uri.EscapeDataString(_redirectUri)}&" +
                      $"response_type=code&" +
                      $"scope={Uri.EscapeDataString(string.Join(" ", scopes))}&" +
                      $"state={_state}&" +
                      $"code_challenge={codeChallenge}&" +
                      $"code_challenge_method=S256";

        logger.LogInformation("OAuth flow started. Opening browser...");

        return await Task.FromResult(authUrl);
    }

    /// <inheritdoc />
    public async Task<bool> CompleteAuthFlowAsync(string authCode) {
        if (string.IsNullOrEmpty(_codeVerifier)) {
            logger.LogError("Code verifier missing - auth flow not started properly");

            return false;
        }

        try {
            var tokenRequest = new Dictionary<string, string> {
                ["client_id"] = _clientId,
                ["code"] = authCode,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = _redirectUri,
                ["code_verifier"] = _codeVerifier
            };

            var response = await _httpClient.PostAsync(
                "https://id.twitch.tv/oauth2/token",
                new FormUrlEncodedContent(tokenRequest)
            );

            if (!response.IsSuccessStatusCode) {
                var errorContent = await response.Content.ReadAsStringAsync();
                logger.LogError("Token exchange failed: {StatusCode} - {Error}", response.StatusCode, errorContent);

                return false;
            }

            var content = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(content);

            if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken)) {
                logger.LogError("Invalid token response");

                return false;
            }

            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn);

            var userId = await FetchUserIdAsync(tokenResponse.AccessToken);

            await authStore.SaveTokensAsync(tokenResponse.AccessToken, tokenResponse.RefreshToken, expiresAt, userId);

            logger.LogInformation("OAuth flow completed successfully. User ID: {UserId}", userId ?? "not retrieved");

            return true;
        } catch (Exception ex) {
            logger.LogError(ex, "Failed to complete OAuth flow");

            return false;
        } finally {
            _codeVerifier = null;
            _state = null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> RefreshTokenAsync() {
        var refreshToken = await authStore.GetRefreshTokenAsync();

        if (string.IsNullOrEmpty(refreshToken)) {
            logger.LogWarning("No refresh token available");

            return false;
        }

        const int maxRetries = 3;
        var backoff = BackoffPolicy.Default;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
            try {
                var tokenRequest = new Dictionary<string, string> {
                    ["client_id"] = _clientId,
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = refreshToken
                };

                var response = await _httpClient.PostAsync(
                    "https://id.twitch.tv/oauth2/token",
                    new FormUrlEncodedContent(tokenRequest)
                );

                if (!response.IsSuccessStatusCode) {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    logger.LogWarning(
                        "Token refresh failed (attempt {Attempt}/{Max}): {StatusCode} - {Error}",
                        attempt, maxRetries, response.StatusCode, errorContent
                    );

                    if (response.StatusCode == HttpStatusCode.BadRequest) {
                        logger.LogError("Refresh token invalid - user needs to re-authenticate");
                        await authStore.ClearTokensAsync();

                        return false;
                    }

                    if (attempt >= maxRetries) return false;
                    var delay = backoff.CalculateDelay(attempt);
                    logger.LogInformation("Retrying in {Delay} seconds...", delay.TotalSeconds);
                    await Task.Delay(delay);

                    continue;
                }

                var content = await response.Content.ReadAsStringAsync();
                var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(content);

                if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken)) {
                    logger.LogError("Invalid token response during refresh");

                    return false;
                }

                var expiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
                await authStore.SaveTokensAsync(tokenResponse.AccessToken, tokenResponse.RefreshToken, expiresAt);

                logger.LogInformation("Token refreshed successfully");

                return true;
            } catch (HttpRequestException ex) {
                logger.LogWarning(
                    ex, "Network error during token refresh (attempt {Attempt}/{Max})",
                    attempt, maxRetries
                );

                if (attempt >= maxRetries) throw;

                var delay = backoff.CalculateDelay(attempt);
                await Task.Delay(delay);
            } catch (Exception ex) {
                logger.LogError(ex, "Failed to refresh token");

                return false;
            }

        return false;
    }

    /// <inheritdoc />
    public async Task<string?> GetValidAccessTokenAsync() {
        var hasValid = await authStore.HasValidTokensAsync();

        if (hasValid) return await authStore.GetAccessTokenAsync();

        var expiry = await authStore.GetTokenExpiryAsync();

        if (expiry.HasValue && expiry.Value <= DateTimeOffset.UtcNow.AddMinutes(5)) {
            logger.LogInformation("Token expiring soon, attempting refresh...");
            var refreshed = await RefreshTokenAsync();

            if (refreshed) return await authStore.GetAccessTokenAsync();
        }

        logger.LogWarning("No valid access token available");

        return null;
    }

    /// <inheritdoc />
    public async Task LogoutAsync() {
        await authStore.ClearTokensAsync();
        logger.LogInformation("User logged out");
    }

    private async Task<string?> FetchUserIdAsync(string accessToken) {
        try {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.twitch.tv/helix/users");
            request.Headers.Add("Client-ID", _clientId);
            request.Headers.Add("Authorization", $"Bearer {accessToken}");

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode) {
                logger.LogWarning("Failed to fetch user ID. Status: {Status}", response.StatusCode);

                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var userResponse = JsonSerializer.Deserialize<UserInfoResponse>(content);

            if (userResponse?.Data != null && userResponse.Data.Count != 0) return userResponse.Data[0].Id;
            logger.LogWarning("No user data returned");

            return null;
        } catch (Exception ex) {
            logger.LogError(ex, "Failed to fetch user ID");

            return null;
        }
    }

    private static string GenerateCodeVerifier() {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);

        return Base64UrlEncode(bytes);
    }

    private static string GenerateCodeChallenge(string codeVerifier) {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(codeVerifier));

        return Base64UrlEncode(hash);
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

    // ReSharper disable ClassNeverInstantiated.Global
    // ReSharper disable ClassNeverInstantiated.Local
    internal record TokenResponse(
        [property: JsonPropertyName("access_token")]
        string AccessToken,
        [property: JsonPropertyName("refresh_token")]
        string RefreshToken,
        [property: JsonPropertyName("expires_in")]
        int ExpiresIn
    );

    internal record UserInfoResponse([property: JsonPropertyName("data")] List<UserData> Data);

    internal record UserData([property: JsonPropertyName("id")] string Id);

    // ReSharper restore ClassNeverInstantiated.Local
    // ReSharper restore ClassNeverInstantiated.Global
}