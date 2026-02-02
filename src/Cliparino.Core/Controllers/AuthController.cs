using Cliparino.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace Cliparino.Core.Controllers;

[ApiController]
[Route("auth")]
public class AuthController(ITwitchOAuthService oauthService, ILogger<AuthController> logger)
    : ControllerBase {
    [HttpGet("login")]
    public async Task<IActionResult> Login() {
        try {
            var authUrl = await oauthService.StartAuthFlowAsync();

            return Ok(new { authUrl });
        } catch (Exception ex) {
            logger.LogError(ex, "Failed to start OAuth flow");

            return StatusCode(500, new { error = "Failed to start authentication" });
        }
    }

    [HttpGet("callback")]
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

    [HttpGet("status")]
    public async Task<IActionResult> Status() {
        var isAuthenticated = await oauthService.IsAuthenticatedAsync();

        return Ok(new { isAuthenticated });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout() {
        await oauthService.LogoutAsync();

        return Ok(new { message = "Logged out successfully" });
    }
}