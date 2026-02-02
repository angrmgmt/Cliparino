using Cliparino.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace Cliparino.Core.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase {
    private readonly IHealthReporter? _healthReporter;
    private readonly ILogger<HealthController> _logger;

    public HealthController(
        IHealthReporter? healthReporter,
        ILogger<HealthController> logger
    ) {
        _healthReporter = healthReporter;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetHealthAsync(CancellationToken cancellationToken) {
        if (_healthReporter == null) return Ok(new { status = "unknown", message = "Health reporter not available" });

        try {
            var healthStatus = await _healthReporter.GetHealthStatusAsync(cancellationToken);

            var overallStatus = healthStatus.Values.Any(h => h.Status == ComponentStatus.Unhealthy)
                ? ComponentStatus.Unhealthy
                : healthStatus.Values.Any(h => h.Status == ComponentStatus.Degraded)
                    ? ComponentStatus.Degraded
                    : ComponentStatus.Healthy;

            return Ok(
                new {
                    status = overallStatus.ToString().ToLowerInvariant(),
                    timestamp = DateTime.UtcNow,
                    components = healthStatus.ToDictionary(
                        kvp => kvp.Key,
                        kvp => new {
                            status = kvp.Value.Status.ToString().ToLowerInvariant(),
                            lastChecked = kvp.Value.LastChecked,
                            lastError = kvp.Value.LastError,
                            repairActions = kvp.Value.RepairActions
                        }
                    )
                }
            );
        } catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving health status");

            return StatusCode(500, "Error retrieving health status");
        }
    }
}