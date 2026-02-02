using Cliparino.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace Cliparino.Core.Controllers;

/// <summary>
///     Exposes endpoints for exporting diagnostic information to support troubleshooting.
/// </summary>
/// <remarks>
///     <para>
///         Routing: this controller is rooted at <c>/api/diagnostics/export</c>.
///     </para>
///     <para>
///         The underlying data is produced by <see cref="IDiagnosticsService" />. Export operations may perform I/O and
///         are
///         executed asynchronously. Consumers should treat these endpoints as potentially slow and avoid calling them on a
///         hot path.
///     </para>
/// </remarks>
[ApiController]
[Route("api/[controller]/export")]
public class DiagnosticsController(
    IDiagnosticsService diagnosticsService,
    ILogger<DiagnosticsController> logger)
    : ControllerBase {
    /// <summary>
    ///     Exports a plain-text diagnostics report.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    ///     <para><c>200 OK</c> with a <c>text/plain</c> diagnostics payload.</para>
    ///     <para><c>500 Internal Server Error</c> if diagnostics export fails.</para>
    /// </returns>
    [HttpGet("")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ExportDiagnosticsAsync(CancellationToken cancellationToken) {
        try {
            logger.LogInformation("Exporting diagnostics...");
            var diagnostics = await diagnosticsService.ExportDiagnosticsAsync(cancellationToken);

            return Content(diagnostics, "text/plain");
        } catch (Exception ex) {
            logger.LogError(ex, "Error exporting diagnostics");

            return StatusCode(500, "Error exporting diagnostics");
        }
    }

    /// <summary>
    ///     Exports diagnostics as a ZIP archive.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    ///     <para><c>200 OK</c> with an <c>application/zip</c> payload named using the current UTC timestamp.</para>
    ///     <para><c>500 Internal Server Error</c> if diagnostics export fails.</para>
    /// </returns>
    [HttpGet("zip")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ExportDiagnosticsZipAsync(CancellationToken cancellationToken) {
        try {
            logger.LogInformation("Exporting diagnostics as ZIP...");
            var zipBytes = await diagnosticsService.ExportDiagnosticsZipAsync(cancellationToken);

            var fileName = $"cliparino-diagnostics-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip";

            return File(zipBytes, "application/zip", fileName);
        } catch (Exception ex) {
            logger.LogError(ex, "Error exporting diagnostics ZIP");

            return StatusCode(500, "Error exporting diagnostics");
        }
    }
}