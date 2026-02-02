namespace Cliparino.Core.Services;

/// <summary>
///     Defines the contract for securely storing and retrieving Twitch OAuth tokens.
/// </summary>
/// <remarks>
///     <para>
///         This interface is implemented by <see cref="TwitchAuthStore" /> and is used by
///         <see cref="ITwitchOAuthService" /> to persist OAuth tokens between application sessions.
///     </para>
///     <para>
///         Key responsibilities:
///         - Store access tokens, refresh tokens, and expiry timestamps securely
///         - Retrieve tokens for use in API requests
///         - Validate token existence and expiry status
///         - Clear tokens on logout or token revocation
///         - Optionally store user ID for user context
///     </para>
///     <para>
///         <strong>Security Considerations:</strong><br />
///         Implementations should store tokens securely using platform-specific mechanisms:
///         - Windows: Data Protection API (DPAPI) or Credential Manager
///         - Encrypted files with appropriate permissions
///         - Never log or transmit tokens in plaintext
///     </para>
///     <para>
///         <strong>Storage Format:</strong><br />
///         Tokens are typically stored as key-value pairs:
///         - <c>access_token</c>: Bearer token for Twitch API requests
///         - <c>refresh_token</c>: Long-lived token for obtaining new access tokens
///         - <c>expires_at</c>: ISO 8601 timestamp when access token expires
///         - <c>user_id</c>: Twitch user ID (optional)
///     </para>
///     <para>
///         Thread-safety: All methods are async and must be thread-safe as tokens may be
///         accessed/updated concurrently during token refresh operations.
///     </para>
/// </remarks>
public interface ITwitchAuthStore {
    /// <summary>
    ///     Retrieves the stored access token.
    /// </summary>
    /// <returns>
    ///     A task containing the access token string, or null if no token is stored or retrieval failed.
    /// </returns>
    /// <remarks>
    ///     This method does not validate token expiry. Use <see cref="HasValidTokensAsync" /> to check
    ///     if the token is still valid, or use <see cref="ITwitchOAuthService.GetValidAccessTokenAsync" />
    ///     for automatic refresh handling.
    /// </remarks>
    Task<string?> GetAccessTokenAsync();

    /// <summary>
    ///     Retrieves the stored refresh token.
    /// </summary>
    /// <returns>
    ///     A task containing the refresh token string, or null if no token is stored or retrieval failed.
    /// </returns>
    /// <remarks>
    ///     Refresh tokens are long-lived and used to obtain new access tokens when the current
    ///     access token expires. They do not expire but can be revoked by the user or Twitch.
    /// </remarks>
    Task<string?> GetRefreshTokenAsync();

    /// <summary>
    ///     Stores OAuth tokens and their expiry information securely.
    /// </summary>
    /// <param name="accessToken">The Twitch access token to store</param>
    /// <param name="refreshToken">The Twitch refresh token to store</param>
    /// <param name="expiresAt">The UTC timestamp when the access token expires</param>
    /// <param name="userId">Optional Twitch user ID to store for user context</param>
    /// <returns>A task representing the async save operation</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="accessToken" /> or <paramref name="refreshToken" /> is null or empty
    /// </exception>
    /// <remarks>
    ///     <para>
    ///         This method atomically saves all token-related data. If the save operation fails partway through,
    ///         implementations should ensure no partial state is persisted.
    ///     </para>
    ///     <para>
    ///         The <paramref name="expiresAt" /> timestamp is typically set to the current time plus the
    ///         <c>expires_in</c> value returned by Twitch (usually 3600 seconds for access tokens).
    ///     </para>
    /// </remarks>
    Task SaveTokensAsync(string accessToken, string refreshToken, DateTimeOffset expiresAt, string? userId = null);

    /// <summary>
    ///     Retrieves the timestamp when the access token expires.
    /// </summary>
    /// <returns>
    ///     A task containing the UTC expiry timestamp, or null if no expiry is stored or retrieval failed.
    /// </returns>
    /// <remarks>
    ///     Compare this timestamp with <see cref="DateTimeOffset.UtcNow" /> to determine if the token
    ///     has expired and needs refreshing.
    /// </remarks>
    Task<DateTimeOffset?> GetTokenExpiryAsync();

    /// <summary>
    ///     Retrieves the stored Twitch user ID.
    /// </summary>
    /// <returns>
    ///     A task containing the Twitch user ID string, or null if no user ID is stored.
    /// </returns>
    /// <remarks>
    ///     The user ID is stored during <see cref="SaveTokensAsync" /> and can be used to identify
    ///     the authenticated user without making an API request.
    /// </remarks>
    Task<string?> GetUserIdAsync();

    /// <summary>
    ///     Clears all stored tokens and associated data.
    /// </summary>
    /// <returns>A task representing the async clear operation</returns>
    /// <remarks>
    ///     <para>
    ///         This method is called during logout operations. It removes:
    ///         - Access token
    ///         - Refresh token
    ///         - Expiry timestamp
    ///         - User ID (if stored)
    ///     </para>
    ///     <para>
    ///         After clearing, <see cref="HasValidTokensAsync" /> will return false and the user
    ///         must re-authenticate.
    ///     </para>
    /// </remarks>
    Task ClearTokensAsync();

    /// <summary>
    ///     Checks if valid tokens are stored (access token exists and either not expired or refresh token available).
    /// </summary>
    /// <returns>
    ///     A task containing true if valid tokens exist, or false if authentication is required.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         This method returns true if:
    ///         - Access token exists AND access token has not expired, OR
    ///         - Access token exists AND refresh token exists (can refresh expired access token)
    ///     </para>
    ///     <para>
    ///         Returns false if:
    ///         - No access token is stored
    ///         - Access token is expired AND no refresh token exists
    ///     </para>
    ///     <para>
    ///         This is a convenience method used by <see cref="ITwitchOAuthService.IsAuthenticatedAsync" />
    ///         to quickly check authentication status without attempting a refresh.
    ///     </para>
    /// </remarks>
    Task<bool> HasValidTokensAsync();
}