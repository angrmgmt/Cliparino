namespace Cliparino.Core.Services;

public interface IDiagnosticsService {
    Task<string> ExportDiagnosticsAsync(CancellationToken cancellationToken = default);

    Task<byte[]> ExportDiagnosticsZipAsync(CancellationToken cancellationToken = default);
}