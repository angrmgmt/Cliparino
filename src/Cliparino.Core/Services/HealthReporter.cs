using System.Collections.Concurrent;

namespace Cliparino.Core.Services;

/// <summary>
///     Aggregates and persists health status reports from all application components.
/// </summary>
/// <remarks>
///     <para>
///         This is the primary implementation of <see cref="IHealthReporter" />, using a
///         <see cref="ConcurrentDictionary{TKey,TValue}" />
///         to safely store component health across multiple threads without locking.
///     </para>
///     <para>
///         Dependencies: <see cref="ILogger{HealthReporter}" /> for warning and information level logging of health
///         transitions and repairs.
///     </para>
///     <para>
///         Thread-safety: Fully thread-safe. Concurrent dictionary operations ensure that multiple components can report
///         health simultaneously
///         from different threads without race conditions.
///     </para>
///     <para>
///         Lifecycle: Registered as Singleton in <see cref="Program" /> to maintain component health state across the
///         application lifetime.
///     </para>
/// </remarks>
public class HealthReporter : IHealthReporter {
    private readonly ConcurrentDictionary<string, ComponentHealth> _componentHealth = new();
    private readonly ILogger<HealthReporter> _logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="HealthReporter" /> class.
    /// </summary>
    /// <param name="logger">Logger instance for recording health status changes.</param>
    public HealthReporter(ILogger<HealthReporter> logger) {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<Dictionary<string, ComponentHealth>> GetHealthStatusAsync(
        CancellationToken cancellationToken = default
    ) {
        return Task.FromResult(_componentHealth.ToDictionary(x => x.Key, x => x.Value));
    }

    /// <inheritdoc />
    public void ReportHealth(string componentName, ComponentStatus status, string? lastError = null) {
        _componentHealth.AddOrUpdate(
            componentName,
            _ => new ComponentHealth(status, lastError, DateTime.UtcNow),
            (_, existing) => existing with {
                Status = status,
                LastError = lastError,
                LastChecked = DateTime.UtcNow
            }
        );

        if (status != ComponentStatus.Healthy)
            _logger.LogWarning(
                "Component {Component} health status: {Status} - {Error}",
                componentName, status, lastError ?? "No error"
            );
    }

    /// <inheritdoc />
    public void ReportRepairAction(string componentName, string action) {
        _componentHealth.AddOrUpdate(
            componentName,
            _ => new ComponentHealth(
                ComponentStatus.Unknown,
                null,
                DateTime.UtcNow,
                new List<string> { $"{DateTime.UtcNow:HH:mm:ss}: {action}" }
            ),
            (_, existing) => {
                var actions = existing.RepairActions?.ToList() ?? new List<string>();
                actions.Add($"{DateTime.UtcNow:HH:mm:ss}: {action}");

                if (actions.Count > 20)
                    actions = actions.TakeLast(20).ToList();

                return existing with { RepairActions = actions };
            }
        );

        _logger.LogInformation(
            "Component {Component} repair action: {Action}",
            componentName, action
        );
    }
}