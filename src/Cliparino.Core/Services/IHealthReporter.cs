namespace Cliparino.Core.Services;

public interface IHealthReporter {
    Task<Dictionary<string, ComponentHealth>> GetHealthStatusAsync(CancellationToken cancellationToken = default);

    void ReportHealth(string componentName, ComponentStatus status, string? lastError = null);

    void ReportRepairAction(string componentName, string action);
}

public record ComponentHealth(
    ComponentStatus Status,
    string? LastError,
    DateTime LastChecked,
    List<string>? RepairActions = null);

public enum ComponentStatus {
    Healthy,
    Degraded,
    Unhealthy,
    Unknown
}