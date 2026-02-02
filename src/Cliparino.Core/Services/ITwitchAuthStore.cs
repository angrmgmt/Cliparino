namespace Cliparino.Core.Services;

public interface ITwitchAuthStore {
    Task<string?> GetAccessTokenAsync();
    Task<string?> GetRefreshTokenAsync();
    Task SaveTokensAsync(string accessToken, string refreshToken, DateTimeOffset expiresAt, string? userId = null);
    Task<DateTimeOffset?> GetTokenExpiryAsync();
    Task<string?> GetUserIdAsync();
    Task ClearTokensAsync();
    Task<bool> HasValidTokensAsync();
}