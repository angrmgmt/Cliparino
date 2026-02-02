using Cliparino.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace Cliparino.Core.Controllers;

/// <summary>
///     Exposes HTTP endpoints for initiating and completing the Twitch OAuth authentication flow.
/// </summary>
/// <remarks>
///     <para>
///         Routing: this controller is rooted at <c>/auth</c> (for example <c>GET /auth/login</c> and
///         <c>GET /auth/callback</c>).
///         The callback route must match the redirect URL configured in the Twitch developer console.
///     </para>
///     <para>
///         The OAuth workflow is implemented by <see cref="ITwitchOAuthService" />. This controller is intentionally thin
///         and
///         returns either JSON (for API-style calls) or a minimal HTML page for the browser-based callback.
///     </para>
/// </remarks>
[ApiController]
[Route("auth")]
public class AuthController(ITwitchOAuthService oauthService, ILogger<AuthController> logger)
    : ControllerBase {
    /// <summary>
    ///     Starts the OAuth flow and returns the authorization URL that the user should open in a browser.
    /// </summary>
    /// <returns>
    ///     <para><c>200 OK</c> with <c>{ authUrl }</c> when the authorization URL is generated successfully.</para>
    ///     <para><c>500 Internal Server Error</c> if the authorization URL cannot be generated.</para>
    /// </returns>
    [HttpGet("login")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Login() {
        try {
            var authUrl = await oauthService.StartAuthFlowAsync();

            return Ok(new { authUrl });
        } catch (Exception ex) {
            logger.LogError(ex, "Failed to start OAuth flow");

            return StatusCode(500, new { error = "Failed to start authentication" });
        }
    }

    /// <summary>
    ///     OAuth redirect callback endpoint invoked by Twitch after the user approves (or denies) authorization.
    /// </summary>
    /// <param name="code">Authorization code from Twitch, if the user approved the request.</param>
    /// <param name="state">Optional state parameter originally generated for CSRF protection.</param>
    /// <param name="error">Optional error string when the user denied the request or Twitch returned an error.</param>
    /// <returns>
    ///     <para>
    ///         <c>200 OK</c> with a minimal <c>text/html</c> page indicating success or failure when the callback is handled.
    ///     </para>
    ///     <para><c>400 Bad Request</c> if the callback is missing the authorization code.</para>
    ///     <para><c>500 Internal Server Error</c> if the OAuth flow cannot be completed.</para>
    /// </returns>
    /// <remarks>
    ///     This endpoint returns an HTML response so it can be opened directly in a browser window during OAuth.
    ///     On success, tokens are persisted by <see cref="ITwitchOAuthService" /> (via its configured storage mechanism).
    /// </remarks>
    [HttpGet("callback")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code, [FromQuery] string? state,
        [FromQuery] string? error
    ) {
        if (!string.IsNullOrEmpty(error)) {
            logger.LogWarning("OAuth callback received error: {Error}", error);

            return Content(
                $"""

                                 <!DOCTYPE html>
                                 <html>
                                 <head><title>Authentication Failed</title></head>
                                 <body style='background: #0071c5; color: white; font-family: sans-serif; text-align: center; padding: 50px;'>
                                     <h1>❌ Authentication Failed</h1>
                                     <p>Error: {error}</p>
                                     <p>You can close this window.</p>
                                 </body>
                                 </html>
                 """, "text/html"
            );
        }

        if (string.IsNullOrEmpty(code)) {
            logger.LogWarning("OAuth callback missing authorization code");

            return BadRequest("Missing authorization code");
        }

        try {
            var success = await oauthService.CompleteAuthFlowAsync(code);

            return Content(
                success
                    ? """

                                          <!DOCTYPE html>
                                          <html>
                                          <head><title>Authentication Successful</title></head>
                                          <body style='background: #0071c5; color: white; font-family: sans-serif; text-align: center; padding: 50px;'>
                                              <h1>✅ Authentication Successful!</h1>
                                              <p>You can close this window and return to Cliparino.</p>
                                          </body>
                                          </html>
                      """
                    : """

                                          <!DOCTYPE html>
                                          <html>
                                          <head><title>Authentication Failed</title></head>
                                          <body style='background: #0071c5; color: white; font-family: sans-serif; text-align: center; padding: 50px;'>
                                              <h1>❌ Authentication Failed</h1>
                                              <p>Unable to complete authentication. Please try again.</p>
                                          </body>
                                          </html>
                      """, "text/html"
            );
        } catch (Exception ex) {
            logger.LogError(ex, "Error completing OAuth flow");

            return StatusCode(500, "Authentication failed");
        }
    }

    /// <summary>
    ///     Returns whether Cliparino currently considers itself authenticated with Twitch.
    /// </summary>
    /// <returns><c>200 OK</c> with <c>{ isAuthenticated }</c>.</returns>
    [HttpGet("status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Status() {
        var isAuthenticated = await oauthService.IsAuthenticatedAsync();

        return Ok(new { isAuthenticated });
    }

    /// <summary>
    ///     Logs out by clearing persisted authentication state, if supported by the configured OAuth store.
    /// </summary>
    /// <returns><c>200 OK</c> when logout completes.</returns>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout() {
        await oauthService.LogoutAsync();

        return Ok(new { message = "Logged out successfully" });
    }
}