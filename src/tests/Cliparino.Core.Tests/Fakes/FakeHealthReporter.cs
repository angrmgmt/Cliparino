using System.Collections.Concurrent;
using Cliparino.Core.Services;

namespace Cliparino.Core.Tests.Fakes;

public sealed class FakeHealthReporter : IHealthReporter {
    public ConcurrentDictionary<string, ComponentHealth> HealthEntries { get; } = new();
    public ConcurrentDictionary<string, List<string>> RepairActions { get; } = new();

    public event EventHandler<HealthChangedEventArgs>? HealthChanged;

    public Task<Dictionary<string, ComponentHealth>> GetHealthStatusAsync(
        CancellationToken cancellationToken = default) {
        return Task.FromResult(new Dictionary<string, ComponentHealth>(HealthEntries));
    }

    public void ReportHealth(string componentName, ComponentStatus status, string? lastError = null) {
        HealthEntries[componentName] = new ComponentHealth(status, lastError, DateTime.UtcNow);
        HealthChanged?.Invoke(this, new HealthChangedEventArgs(componentName, status, lastError));
    }

    public ComponentStatus GetAggregateStatus() {
        if (HealthEntries.IsEmpty) return ComponentStatus.Unknown;
        var statuses = HealthEntries.Values.Select(x => x.Status).ToList();

        if (statuses.Any(s => s == ComponentStatus.Unhealthy)) return ComponentStatus.Unhealthy;
        if (statuses.Any(s => s == ComponentStatus.Degraded)) return ComponentStatus.Degraded;
        if (statuses.All(s => s == ComponentStatus.Healthy)) return ComponentStatus.Healthy;

        return ComponentStatus.Unknown;
    }

    public void ReportRepairAction(string componentName, string action) {
        RepairActions.AddOrUpdate(componentName,
            [action],
            (_, list) => {
                list.Add(action);

                return list;
            });
    }
}