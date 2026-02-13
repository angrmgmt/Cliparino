using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Cliparino.Core.Services;

/// <summary>
///     Stores Twitch OAuth tokens securely using Windows Data Protection API (DPAPI).
/// </summary>
/// <remarks>
///     <para>
///         This class implements <see cref="ITwitchAuthStore" /> by persisting tokens to an encrypted file
///         in the user's LocalApplicationData folder. Tokens are encrypted using Windows DPAPI, which
///         ties encryption to the current user account.
///     </para>
///     <para>
///         <strong>Storage location:</strong><br />
///         <c>%LOCALAPPDATA%\Cliparino\tokens.dat</c>
///     </para>
///     <para>
///         <strong>Security model:</strong><br />
///         - Tokens encrypted with ProtectedData.Protect() (DPAPI)<br />
///         - Encryption key derived from user's Windows login credentials<br />
///         - Tokens can only be decrypted by the same user on the same machine<br />
///         - File permissions restrict access to current user
///     </para>
///     <para>
///         <strong>Caching:</strong><br />
///         Tokens are cached in memory after the first load to avoid repeated file I/O and decryption.
///         Cache is cleared when tokens are updated or cleared.
///     </para>
///     <para>
///         Dependencies:
///         - <see cref="ILogger{TCategoryName}" /> - Structured logging
///     </para>
///     <para>
///         Thread-safety: File I/O is not explicitly synchronized. Concurrent access may result in
///         last-write-wins behavior. This is acceptable given typical usage patterns (single-threaded token refresh).
///     </para>
///     <para>
///         Lifecycle: Registered as a singleton.
///     </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public class TwitchAuthStore : ITwitchAuthStore {
    private readonly ILogger<TwitchAuthStore> _logger;
    private readonly string _storagePath;
    private TokenData? _cachedTokens;

    public TwitchAuthStore(ILogger<TwitchAuthStore> logger) {
        _logger = logger;
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var cliparinoFolder = Path.Combine(appDataPath, "Cliparino");
        Directory.CreateDirectory(cliparinoFolder);
        _storagePath = Path.Combine(cliparinoFolder, "tokens.dat");
    }

    /// <inheritdoc />
    [SupportedOSPlatform("windows")]
    public async Task<string?> GetAccessTokenAsync() {
        var tokens = await LoadTokensAsync();

        return tokens?.AccessToken;
    }

    /// <inheritdoc />
    [SupportedOSPlatform("windows")]
    public async Task<string?> GetRefreshTokenAsync() {
        var tokens = await LoadTokensAsync();

        return tokens?.RefreshToken;
    }

    /// <inheritdoc />
    [SupportedOSPlatform("windows")]
    public async Task<DateTimeOffset?> GetTokenExpiryAsync() {
        var tokens = await LoadTokensAsync();

        return tokens?.ExpiresAt;
    }

    /// <inheritdoc />
    [SupportedOSPlatform("windows")]
    public async Task<string?> GetUserIdAsync() {
        var tokens = await LoadTokensAsync();

        return tokens?.UserId;
    }

    /// <inheritdoc />
    [SupportedOSPlatform("windows")]
    public async Task SaveTokensAsync(string accessToken, string? refreshToken, DateTimeOffset expiresAt,
        string? userId = null) {
        try {
            var existingTokens = await LoadTokensAsync();

            var tokenData = new TokenData {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresAt = expiresAt,
                UserId = userId ?? existingTokens?.UserId
            };

            var json = JsonSerializer.Serialize(tokenData);
            var plainBytes = Encoding.UTF8.GetBytes(json);
            var encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);

            await File.WriteAllBytesAsync(_storagePath, encryptedBytes);
            _cachedTokens = tokenData;

            _logger.LogInformation("Tokens saved securely. Expires at: {ExpiresAt}, UserId: {UserId}",
                expiresAt, tokenData.UserId ?? "not set");
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to save tokens");

            throw;
        }
    }

    /// <inheritdoc />
    public async Task ClearTokensAsync() {
        try {
            if (File.Exists(_storagePath)) File.Delete(_storagePath);
            _cachedTokens = null;
            _logger.LogInformation("Tokens cleared");
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to clear tokens");
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    [SupportedOSPlatform("windows")]
    public async Task<bool> HasValidTokensAsync() {
        var tokens = await LoadTokensAsync();

        if (tokens == null)
            return false;

        // Access token must exist
        if (string.IsNullOrEmpty(tokens.AccessToken))
            return false;

        // Either token is not expired, OR we have a refresh token to get a new one
        var isNotExpired = tokens.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5);
        var hasRefreshToken = !string.IsNullOrEmpty(tokens.RefreshToken);

        return isNotExpired || hasRefreshToken;
    }

    [SupportedOSPlatform("windows")]
    private async Task<TokenData?> LoadTokensAsync() {
        if (_cachedTokens != null)
            return _cachedTokens;

        if (!File.Exists(_storagePath))
            return null;

        try {
            var encryptedBytes = await File.ReadAllBytesAsync(_storagePath);
            var plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(plainBytes);
            _cachedTokens = JsonSerializer.Deserialize<TokenData>(json);

            return _cachedTokens;
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to load tokens");

            return null;
        }
    }

    private class TokenData {
        public string AccessToken { get; init; } = string.Empty;
        public string? RefreshToken { get; init; } = string.Empty;
        public DateTimeOffset ExpiresAt { get; init; }
        public string? UserId { get; init; }
    }
}