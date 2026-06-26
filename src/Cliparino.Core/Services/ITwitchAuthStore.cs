namespace Cliparino.Core.Services;

/// <summary>
///     Defines the contract for securely storing and retrieving Twitch OAuth tokens.
/// </summary>
public interface ITwitchAuthStore {
    /// <summary>
    ///     Retrieves the stored access token.
    /// </summary>
    /// <returns>A task containing the access token, or null if not found.</returns>
    Task<string?> GetAccessTokenAsync();

    /// <summary>
    ///     Retrieves the stored refresh token.
    /// </summary>
    /// <returns>A task containing the refresh token, or null if not found.</returns>
    Task<string?> GetRefreshTokenAsync();

    /// <summary>
    ///     Retrieves the token expiry time.
    /// </summary>
    /// <returns>A task containing the expiry time, or null if not found.</returns>
    Task<DateTimeOffset?> GetTokenExpiryAsync();

    /// <summary>
    ///     Retrieves the stored Twitch user ID.
    /// </summary>
    /// <returns>A task containing the user ID, or null if not found.</returns>
    Task<string?> GetUserIdAsync();

    /// <summary>
    ///     Saves the OAuth tokens and expiry information.
    /// </summary>
    /// <param name="accessToken">The new access token.</param>
    /// <param name="refreshToken">The new refresh token (optional).</param>
    /// <param name="expiresAt">The token expiry time.</param>
    /// <param name="userId">The Twitch user ID (optional).</param>
    /// <returns>A task representing the async operation.</returns>
    Task SaveTokensAsync(string accessToken, string? refreshToken, DateTimeOffset expiresAt, string? userId = null);

    /// <summary>
    ///     Clears all stored tokens.
    /// </summary>
    /// <returns>A task representing the async operation.</returns>
    Task ClearTokensAsync();

    /// <summary>
    ///     Checks if valid tokens exist (either not expired or can be refreshed).
    /// </summary>
    /// <returns>A task containing true if valid tokens exist, otherwise false.</returns>
    Task<bool> HasValidTokensAsync();
}