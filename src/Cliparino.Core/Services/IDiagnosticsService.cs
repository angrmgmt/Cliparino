namespace Cliparino.Core.Services;

/// <summary>
///     Defines the contract for collecting and exporting comprehensive diagnostic data.
/// </summary>
/// <remarks>
///     <para>Implemented by <see cref="DiagnosticsService" /> and typically invoked by diagnostic HTTP endpoints.</para>
///     <para>
///         Key responsibilities:
///         <list type="bullet">
///             <item>Aggregate system information (OS version, runtime, machine name)</item>
///             <item>Export application configuration with automatic redaction of sensitive keys</item>
///             <item>Collect recent log files with sensitive data filtering</item>
///             <item>Include component health status (when available via <see cref="IHealthReporter" />)</item>
///             <item>Package diagnostics as both plaintext and ZIP archive formats</item>
///         </list>
///     </para>
///     <para>
///         Sensitive Data Handling: Configuration values and logs are automatically scanned for common sensitive keys
///         (password, token, secret, key, apikey, etc.) and redacted before export. This enables safe sharing of
///         diagnostics
///         without exposing credentials.
///     </para>
/// </remarks>
public interface IDiagnosticsService {
    /// <summary>
    ///     Asynchronously exports comprehensive diagnostics as a plaintext report.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The report includes system information, redacted configuration, health status, and recent log entries.
    ///         Useful for direct viewing or debugging over remote support sessions.
    ///     </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token for the async operation.</param>
    /// <returns>
    ///     A task representing the async operation, containing a formatted plaintext diagnostics report.
    /// </returns>
    Task<string> ExportDiagnosticsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Asynchronously exports comprehensive diagnostics as a ZIP archive containing multiple files.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The ZIP archive contains:
    ///         <list type="bullet">
    ///             <item>diagnostics.txt - Full plaintext report</item>
    ///             <item>appsettings-redacted.json - Configuration snapshot (sensitive values redacted)</item>
    ///             <item>logs/* - Up to 3 most recent log files (redacted)</item>
    ///         </list>
    ///     </para>
    ///     <para>Useful for automated uploads to bug tracking systems or archiving for historical analysis.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token for the async operation.</param>
    /// <returns>
    ///     A task representing the async operation, containing the ZIP archive as a byte array.
    ///     The caller is responsible for writing to disk or transmitting as needed.
    /// </returns>
    Task<byte[]> ExportDiagnosticsZipAsync(CancellationToken cancellationToken = default);
}