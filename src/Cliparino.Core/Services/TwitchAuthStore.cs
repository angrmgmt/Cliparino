using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Cliparino.Core.Services;

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

    [SupportedOSPlatform("windows")]
    public async Task<string?> GetAccessTokenAsync() {
        var tokens = await LoadTokensAsync();

        return tokens?.AccessToken;
    }

    [SupportedOSPlatform("windows")]
    public async Task<string?> GetRefreshTokenAsync() {
        var tokens = await LoadTokensAsync();

        return tokens?.RefreshToken;
    }

    [SupportedOSPlatform("windows")]
    public async Task<DateTimeOffset?> GetTokenExpiryAsync() {
        var tokens = await LoadTokensAsync();

        return tokens?.ExpiresAt;
    }

    [SupportedOSPlatform("windows")]
    public async Task<string?> GetUserIdAsync() {
        var tokens = await LoadTokensAsync();

        return tokens?.UserId;
    }

    [SupportedOSPlatform("windows")]
    public async Task SaveTokensAsync(
        string accessToken, string refreshToken, DateTimeOffset expiresAt, string? userId = null
    ) {
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

            _logger.LogInformation(
                "Tokens saved securely. Expires at: {ExpiresAt}, UserId: {UserId}",
                expiresAt, tokenData.UserId ?? "not set"
            );
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to save tokens");

            throw;
        }
    }

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

    [SupportedOSPlatform("windows")]
    public async Task<bool> HasValidTokensAsync() {
        var tokens = await LoadTokensAsync();

        if (tokens == null)
            return false;

        return !string.IsNullOrEmpty(tokens.AccessToken)
               && !string.IsNullOrEmpty(tokens.RefreshToken)
               && tokens.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5);
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
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTimeOffset ExpiresAt { get; set; }
        public string? UserId { get; set; }
    }
}