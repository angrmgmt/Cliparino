namespace Cliparino.Core.Services;

/// <summary>
///     Defines the contract for managing Twitch OAuth 2.0 authentication flow and token lifecycle.
/// </summary>
/// <remarks>
///     <para>
///         This interface is implemented by <see cref="TwitchOAuthService" /> and is used throughout
///         the application to authenticate with Twitch and maintain valid access tokens.
///     </para>
///     <para>
///         Key responsibilities:
///         - Initiate OAuth 2.0 authorization code flow
///         - Exchange authorization codes for access/refresh tokens
///         - Refresh expired access tokens automatically
///         - Validate token status and provide valid tokens on demand
///         - Handle logout and token revocation
///     </para>
///     <para>
///         <strong>OAuth 2.0 Flow:</strong><br />
///         1. <see cref="StartAuthFlowAsync" /> generates authorization URL for user<br />
///         2. User authorizes app in browser and is redirected back with auth code<br />
///         3. <see cref="CompleteAuthFlowAsync" /> exchanges code for access/refresh tokens<br />
///         4. Tokens stored via <see cref="ITwitchAuthStore" /><br />
///         5. <see cref="GetValidAccessTokenAsync" /> returns valid token, refreshing if needed<br />
///         6. <see cref="RefreshTokenAsync" /> called automatically when token expires
///     </para>
///     <para>
///         Token Lifecycle: Access tokens expire after ~4 hours. Refresh tokens are long-lived.
///         This service automatically refreshes access tokens using the refresh token when needed.
///     </para>
///     <para>
///         Thread-safety: All methods are async and thread-safe. Token refresh operations are
///         internally synchronized to prevent race conditions.
///     </para>
/// </remarks>
public interface ITwitchOAuthService {
    /// <summary>
    ///     Checks if the user is currently authenticated with valid Twitch tokens.
    /// </summary>
    /// <returns>
    ///     A task containing true if valid tokens exist (access token not expired or refresh token available),
    ///     or false if authentication is required.
    /// </returns>
    /// <remarks>
    ///     This method checks <see cref="ITwitchAuthStore" /> for stored tokens and validates their expiry.
    ///     Does not attempt to refresh tokens; use <see cref="GetValidAccessTokenAsync" /> for that.
    /// </remarks>
    Task<bool> IsAuthenticatedAsync();

    /// <summary>
    ///     Initiates the OAuth 2.0 authorization flow and returns the authorization URL.
    /// </summary>
    /// <returns>
    ///     A task containing the Twitch authorization URL that the user should open in a browser.
    ///     The URL includes the client ID, redirect URI, scopes, and PKCE challenge.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         The returned URL follows this format:<br />
    ///         <c>
    ///             https://id.twitch.tv/oauth2/authorize?client_id=...&amp;redirect_uri=...&amp;response_type=code&amp;
    ///             scope=...
    ///         </c>
    ///     </para>
    ///     <para>
    ///         Required Twitch scopes:
    ///         - <c>user:read:email</c> - Read user email
    ///         - <c>chat:read</c> - Read chat messages
    ///         - <c>chat:edit</c> - Send chat messages
    ///         - <c>moderator:manage:shoutouts</c> - Send shoutouts
    ///         - <c>clips:read</c> - Read clip data
    ///     </para>
    ///     <para>
    ///         After the user authorizes, Twitch redirects to the configured redirect URI with an
    ///         authorization code that must be passed to <see cref="CompleteAuthFlowAsync" />.
    ///     </para>
    /// </remarks>
    Task<string> StartAuthFlowAsync();

    /// <summary>
    ///     Completes the OAuth flow by exchanging an authorization code for access and refresh tokens.
    /// </summary>
    /// <param name="authCode">The authorization code received from Twitch's redirect</param>
    /// <returns>
    ///     A task containing true if the token exchange succeeded and tokens were saved,
    ///     or false if the exchange failed (invalid code, network error, etc.).
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         This method calls the Twitch token endpoint:<br />
    ///         <c>POST https://id.twitch.tv/oauth2/token</c><br />
    ///         with the authorization code and PKCE verifier.
    ///     </para>
    ///     <para>
    ///         On success, the access token, refresh token, and expiry time are stored via
    ///         <see cref="ITwitchAuthStore.SaveTokensAsync" />.
    ///     </para>
    ///     <para>
    ///         Common failure reasons:
    ///         - Invalid or expired authorization code
    ///         - PKCE challenge/verifier mismatch
    ///         - Network errors
    ///         - Invalid client ID or secret
    ///     </para>
    /// </remarks>
    Task<bool> CompleteAuthFlowAsync(string authCode);

    /// <summary>
    ///     Refreshes the access token using the stored refresh token.
    /// </summary>
    /// <returns>
    ///     A task containing true if the token was refreshed successfully,
    ///     or false if the refresh failed (no refresh token, invalid token, network error).
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         This method calls the Twitch token endpoint:<br />
    ///         <c>POST https://id.twitch.tv/oauth2/token</c><br />
    ///         with <c>grant_type=refresh_token</c>.
    ///     </para>
    ///     <para>
    ///         On success, new access and refresh tokens are stored via <see cref="ITwitchAuthStore" />.
    ///         Twitch may issue a new refresh token during this process.
    ///     </para>
    ///     <para>
    ///         If refresh fails (e.g., refresh token revoked), the user must re-authenticate via
    ///         <see cref="StartAuthFlowAsync" /> and <see cref="CompleteAuthFlowAsync" />.
    ///     </para>
    /// </remarks>
    Task<bool> RefreshTokenAsync();

    /// <summary>
    ///     Gets a valid access token, automatically refreshing if the current token is expired.
    /// </summary>
    /// <returns>
    ///     A task containing a valid access token string, or null if no authentication exists
    ///     or refresh failed.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         This is the primary method used by <see cref="ITwitchHelixClient" /> to obtain tokens
    ///         for API requests. It handles token expiry transparently:
    ///         - If access token is valid (not expired), returns it immediately
    ///         - If access token is expired but refresh token exists, calls <see cref="RefreshTokenAsync" /> first
    ///         - If no tokens exist or refresh fails, returns null
    ///     </para>
    ///     <para>
    ///         Token refresh is synchronized internally to prevent multiple concurrent refresh attempts.
    ///     </para>
    /// </remarks>
    Task<string?> GetValidAccessTokenAsync();

    /// <summary>
    ///     Logs out the user by clearing all stored tokens and optionally revoking them with Twitch.
    /// </summary>
    /// <returns>A task representing the async logout operation</returns>
    /// <remarks>
    ///     <para>
    ///         This method:
    ///         1. Optionally revokes the access token with Twitch (prevents further use)
    ///         2. Clears all tokens from <see cref="ITwitchAuthStore" />
    ///         3. Invalidates any cached token state
    ///     </para>
    ///     <para>
    ///         After logout, the user must re-authenticate via <see cref="StartAuthFlowAsync" />.
    ///     </para>
    /// </remarks>
    Task LogoutAsync();
}