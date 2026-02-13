namespace Cliparino.Core.Services;

/// <summary>
///     Defines the contract for aggregating and reporting health status of application components.
/// </summary>
/// <remarks>
///     <para>Implemented by <see cref="HealthReporter" /> and consumed by health check endpoints and diagnostics services.</para>
///     <para>
///         Key responsibilities:
///         <list type="bullet">
///             <item>Aggregate health status from all system components (Twitch integration, OBS controller, etc.)</item>
///             <item>Track component status transitions (Healthy → Degraded → Unhealthy)</item>
///             <item>Maintain last error messages and timestamps for troubleshooting</item>
///             <item>Record self-healing repair actions taken by supervisor services</item>
///         </list>
///     </para>
///     <para>
///         Thread-safety: All methods are thread-safe using concurrent collections. Multiple threads can report health
///         and repair actions simultaneously.
///     </para>
/// </remarks>
public interface IHealthReporter {
    /// <summary>
    ///     Asynchronously retrieves the current health status of all tracked components.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the async operation.</param>
    /// <returns>
    ///     A task representing the async operation, containing a dictionary mapping component names to their health status.
    ///     Returns an empty dictionary if no components have been reported yet.
    /// </returns>
    Task<Dictionary<string, ComponentHealth>> GetHealthStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Calculates the aggregate health status of all tracked components.
    /// </summary>
    /// <returns>The worst status among all components (Unhealthy > Degraded > Healthy).</returns>
    ComponentStatus GetAggregateStatus();

    /// <summary>
    ///     Reports the current health status of a component.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This method is typically called by component supervisors (e.g., <see cref="ObsHealthSupervisor" />)
    ///         after health checks or repair attempts.
    ///     </para>
    ///     <para>Non-Healthy status values trigger warning-level logging for monitoring and alerting.</para>
    /// </remarks>
    /// <param name="componentName">The name of the component being reported (e.g., "OBS", "TwitchEvents").</param>
    /// <param name="status">The current health status of the component.</param>
    /// <param name="lastError">Optional error message describing why the status is not Healthy. Null if no error.</param>
    void ReportHealth(string componentName, ComponentStatus status, string? lastError = null);

    /// <summary>
    ///     Occurs when a component's health status changes.
    /// </summary>
    event EventHandler<HealthChangedEventArgs>? HealthChanged;

    /// <summary>
    ///     Records a self-healing repair action taken by a supervisor service.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Repair actions are timestamped and appended to the component's history, limited to the 20 most recent actions.
    ///         This provides an audit trail for debugging recurring issues.
    ///     </para>
    ///     <para>Logged at Information level for visibility in diagnostic exports.</para>
    /// </remarks>
    /// <param name="componentName">The name of the component that was repaired.</param>
    /// <param name="action">
    ///     A description of the repair action taken (e.g., "Reconnecting WebSocket", "Recreating OBS
    ///     scenes").
    /// </param>
    void ReportRepairAction(string componentName, string action);
}

public record HealthChangedEventArgs(string ComponentName, ComponentStatus Status, string? LastError);

/// <summary>
///     Represents the complete health snapshot for a single component at a point in time.
/// </summary>
/// <remarks>
///     <para>
///         This record is immutable and updated using record copy-with-expressions by <see cref="HealthReporter" />.
///         It serves as the data model for both real-time health checks and diagnostic exports.
///     </para>
/// </remarks>
/// <param name="Status">The current operational status of the component.</param>
/// <param name="LastError">The most recent error message, if any. Null if the component is Healthy.</param>
/// <param name="LastChecked">UTC timestamp of the last health update.</param>
/// <param name="RepairActions">
///     Timestamped history of repair actions (up to 20 most recent). Null if no repairs have been
///     attempted.
/// </param>
public record ComponentHealth(
    ComponentStatus Status,
    string? LastError,
    DateTime LastChecked,
    List<string>? RepairActions = null);

/// <summary>
///     Defines the operational health status of a component.
/// </summary>
/// <remarks>
///     <para>
///         Status transitions typically follow: Healthy → Degraded (when failures are detected) → Unhealthy (after
///         repeated failures).
///         Self-healing services attempt to return components to Healthy status.
///     </para>
///     <para>Unknown is used for components that have not yet reported a health status.</para>
/// </remarks>
public enum ComponentStatus {
    /// <summary>Operational and meeting SLAs.</summary>
    Healthy,

    /// <summary>Partially operational but experiencing degraded performance or occasional errors.</summary>
    Degraded,

    /// <summary>Non-operational; service is unavailable or critical errors are recurring.</summary>
    Unhealthy,

    /// <summary>No health status has been reported yet.</summary>
    Unknown
}