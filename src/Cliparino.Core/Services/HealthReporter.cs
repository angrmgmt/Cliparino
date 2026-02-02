using System.Collections.Concurrent;

namespace Cliparino.Core.Services;

public class HealthReporter : IHealthReporter {
    private readonly ConcurrentDictionary<string, ComponentHealth> _componentHealth = new();
    private readonly ILogger<HealthReporter> _logger;

    public HealthReporter(ILogger<HealthReporter> logger) {
        _logger = logger;
    }

    public Task<Dictionary<string, ComponentHealth>> GetHealthStatusAsync(
        CancellationToken cancellationToken = default
    ) {
        return Task.FromResult(_componentHealth.ToDictionary(x => x.Key, x => x.Value));
    }

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