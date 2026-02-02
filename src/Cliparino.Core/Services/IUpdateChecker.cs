namespace Cliparino.Core.Services;

public interface IUpdateChecker {
    string CurrentVersion { get; }
    Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default);
}

public record UpdateInfo(
    string LatestVersion,
    string ReleaseUrl,
    string? Description,
    DateTime PublishedAt,
    bool IsNewer);