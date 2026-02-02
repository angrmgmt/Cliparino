using Cliparino.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace Cliparino.Core.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UpdateController : ControllerBase {
    private readonly ILogger<UpdateController> _logger;
    private readonly IUpdateChecker _updateChecker;

    public UpdateController(
        IUpdateChecker updateChecker,
        ILogger<UpdateController> logger
    ) {
        _updateChecker = updateChecker;
        _logger = logger;
    }

    [HttpGet("check")]
    public async Task<IActionResult> CheckForUpdatesAsync(CancellationToken cancellationToken) {
        try {
            var updateInfo = await _updateChecker.CheckForUpdatesAsync(cancellationToken);

            if (updateInfo == null)
                return Ok(
                    new {
                        currentVersion = _updateChecker.CurrentVersion,
                        updateAvailable = false,
                        message = "Could not check for updates"
                    }
                );

            return Ok(
                new {
                    currentVersion = _updateChecker.CurrentVersion,
                    latestVersion = updateInfo.LatestVersion,
                    updateAvailable = updateInfo.IsNewer,
                    releaseUrl = updateInfo.ReleaseUrl,
                    publishedAt = updateInfo.PublishedAt,
                    description = updateInfo.Description
                }
            );
        } catch (Exception ex) {
            _logger.LogError(ex, "Error checking for updates");

            return StatusCode(500, "Error checking for updates");
        }
    }

    [HttpGet("current")]
    public IActionResult GetCurrentVersion() {
        return Ok(new { version = _updateChecker.CurrentVersion });
    }
}