using Cliparino.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace Cliparino.Core.Controllers;

[ApiController]
[Route("api/[controller]/export")]
public class DiagnosticsController(
    IDiagnosticsService diagnosticsService,
    ILogger<DiagnosticsController> logger)
    : ControllerBase {
    [HttpGet("")]
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

    [HttpGet("zip")]
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