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
            logger.LogInformation("Starting OAuth flow...");
            var authUrl = await oauthService.StartAuthFlowAsync();
            logger.LogInformation("OAuth flow started, returning auth URL");

            return Ok(new { authUrl });
        } catch (Exception ex) {
            logger.LogError(ex, "Failed to start OAuth flow");

            return StatusCode(500, new { error = "Failed to start authentication" });
        }
    }

    /// <summary>
    ///     OAuth redirect callback endpoint invoked by Twitch after the user approves (or denies) authorization.
    ///     For implicit flow, the token is in the URL fragment and must be extracted by JavaScript.
    /// </summary>
    /// <param name="error">Optional error string when the user denied the request or Twitch returned an error.</param>
    /// <returns>
    ///     <para>
    ///         <c>200 OK</c> with a minimal <c>text/html</c> page that extracts the token from the URL fragment.
    ///     </para>
    /// </returns>
    /// <remarks>
    ///     This endpoint returns an HTML page with JavaScript that extracts the access_token from the URL fragment
    ///     and posts it to /auth/complete. This is necessary because URL fragments are not sent to the server.
    /// </remarks>
    [HttpGet("callback")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Callback([FromQuery] string? error) {
        if (!string.IsNullOrEmpty(error)) {
            logger.LogWarning("OAuth callback received error: {Error}", error);

            return Content($"""
                            <!DOCTYPE html>
                            <html>
                            <head><title>Authentication Failed</title></head>
                            <body style='background: #0071c5; color: white; font-family: sans-serif; text-align: center; padding: 50px;'>
                                <h1>❌ Authentication Failed</h1>
                                <p>Error: {error}</p>
                                <p>You can close this window.</p>
                            </body>
                            </html>
                            """, "text/html");
        }

        return Content("""
                       <!DOCTYPE html>
                       <html>
                       <head><title>Processing Authentication...</title></head>
                       <body style='background: #0071c5; color: white; font-family: sans-serif; text-align: center; padding: 50px;'>
                           <h1>🔄 Processing Authentication...</h1>
                           <p id="status">Completing authentication...</p>
                           <script>
                               (async () => {
                                   try {
                                       const fragment = window.location.hash.substring(1);
                                       const params = new URLSearchParams(fragment);
                                       const accessToken = params.get('access_token');
                                       const state = params.get('state');
                                       
                                       if (!accessToken) {
                                           document.getElementById('status').innerText = 'Error: No access token received';
                                           return;
                                       }
                                       
                                       const response = await fetch('/auth/complete', {
                                           method: 'POST',
                                           headers: { 'Content-Type': 'application/json' },
                                           body: JSON.stringify({ accessToken, state })
                                       });
                                       
                                       const result = await response.json();
                                       
                                       if (result.success) {
                                           document.body.innerHTML = `
                                               <h1>✅ Authentication Successful!</h1>
                                               <p>You can close this window and return to Cliparino.</p>
                                           `;
                                       } else {
                                           document.body.innerHTML = `
                                               <h1>❌ Authentication Failed</h1>
                                               <p>${result.error || 'Unknown error'}</p>
                                               <p>Please check the application logs for details.</p>
                                           `;
                                       }
                                   } catch (ex) {
                                       document.body.innerHTML = `
                                           <h1>❌ Authentication Error</h1>
                                           <p>An error occurred: ${ex.message}</p>
                                       `;
                                   }
                               })();
                           </script>
                       </body>
                       </html>
                       """, "text/html");
    }

    /// <summary>
    ///     Completes OAuth flow by receiving the access token from the callback page's JavaScript.
    /// </summary>
    [HttpPost("complete")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Complete([FromBody] CompleteAuthRequest request) {
        logger.LogInformation("Complete auth called with token length: {Length}, state: {State}",
            request.AccessToken?.Length ?? 0, request.State ?? "none");

        if (string.IsNullOrEmpty(request.AccessToken)) {
            logger.LogWarning("Complete auth missing access token");

            return Ok(new { success = false, error = "Missing access token" });
        }

        if (string.IsNullOrEmpty(request.State)) {
            logger.LogWarning("Complete auth missing state");

            return Ok(new { success = false, error = "Missing state parameter" });
        }

        try {
            var success = await oauthService.CompleteAuthFlowAsync(request.AccessToken, request.State);
            logger.LogInformation("CompleteAuthFlowAsync returned: {Success}", success);

            return Ok(new { success, error = success ? null : "Authentication failed" });
        } catch (Exception ex) {
            logger.LogError(ex, "Error completing OAuth flow");

            return Ok(new { success = false, error = ex.Message });
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

    public record CompleteAuthRequest(string? AccessToken, string? State);
}