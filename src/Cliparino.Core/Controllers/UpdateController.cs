using Cliparino.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace Cliparino.Core.Controllers;

/// <summary>
///     Exposes endpoints for checking whether a newer Cliparino release is available.
/// </summary>
/// <remarks>
///     <para>
///         Routing: <c>/api/update</c>.
///     </para>
///     <para>
///         Release lookup and version comparison are performed by <see cref="IUpdateChecker" />.
///         The underlying implementation may contact a remote service (for example GitHub releases) and should be treated
///         as
///         potentially slow; consumers should call these endpoints sparingly.
///     </para>
/// </remarks>
[ApiController]
[Route("api/[controller]")]
public class UpdateController : ControllerBase {
    private readonly ILogger<UpdateController> _logger;
    private readonly IUpdateChecker _updateChecker;

    /// <summary>
    ///     Initializes a new instance of the <see cref="UpdateController" /> class.
    /// </summary>
    /// <param name="updateChecker">Update checker used to query remote release metadata.</param>
    /// <param name="logger">Logger instance for structured diagnostics.</param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="updateChecker" /> or <paramref name="logger" /> is <see langword="null" />.
    /// </exception>
    public UpdateController(
        IUpdateChecker updateChecker,
        ILogger<UpdateController> logger
    ) {
        _updateChecker = updateChecker ?? throw new ArgumentNullException(nameof(updateChecker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Checks for the latest available release and returns update metadata.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    ///     <para><c>200 OK</c> with update metadata (or a message indicating the check could not be completed).</para>
    ///     <para><c>500 Internal Server Error</c> if an unhandled exception occurs.</para>
    /// </returns>
    [HttpGet("check")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
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

    /// <summary>
    ///     Returns the version of the currently running application as reported by <see cref="IUpdateChecker" />.
    /// </summary>
    /// <returns><c>200 OK</c> with <c>{ version }</c>.</returns>
    [HttpGet("current")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetCurrentVersion() {
        return Ok(new { version = _updateChecker.CurrentVersion });
    }
}