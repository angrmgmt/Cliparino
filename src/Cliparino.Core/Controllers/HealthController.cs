using Cliparino.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace Cliparino.Core.Controllers;

/// <summary>
///     Exposes a health-check endpoint that summarizes the application and integration status.
/// </summary>
/// <remarks>
///     <para>
///         This endpoint is designed for local monitoring and diagnostics. It aggregates component status from
///         <see cref="IHealthReporter" /> (when available) and returns a compact JSON payload.
///     </para>
///     <para>
///         Routing: <c>GET /api/health</c>.
///     </para>
/// </remarks>
[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase {
    private readonly IHealthReporter? _healthReporter;
    private readonly ILogger<HealthController> _logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="HealthController" /> class.
    /// </summary>
    /// <param name="healthReporter">
    ///     Optional health reporter. When <see langword="null" />, the endpoint will return <c>unknown</c> status.
    /// </param>
    /// <param name="logger">Logger instance for structured diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger" /> is <see langword="null" />.</exception>
    public HealthController(
        IHealthReporter? healthReporter,
        ILogger<HealthController> logger
    ) {
        _healthReporter = healthReporter;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Returns a health summary for the application and its integrations.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    ///     <para><c>200 OK</c> with a JSON payload describing overall status and per-component details.</para>
    ///     <para><c>500 Internal Server Error</c> if the health reporter throws an unhandled exception.</para>
    /// </returns>
    /// <remarks>
    ///     The overall status is computed as:
    ///     <list type="bullet">
    ///         <item>
    ///             <description><c>unhealthy</c> if any component is unhealthy.</description>
    ///         </item>
    ///         <item>
    ///             <description><c>degraded</c> if no components are unhealthy and at least one is degraded.</description>
    ///         </item>
    ///         <item>
    ///             <description><c>healthy</c> otherwise.</description>
    ///         </item>
    ///     </list>
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
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