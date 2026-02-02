namespace Cliparino.Core.Services;

/// <summary>
///     Defines the contract for checking for and retrieving available application updates.
/// </summary>
/// <remarks>
///     <para>
///         Implemented by <see cref="UpdateChecker" /> and typically invoked by <see cref="PeriodicUpdateCheckService" />
///         to provide background update checking capability.
///     </para>
///     <para>
///         Key responsibilities:
///         <list type="bullet">
///             <item>Query the configured release channel (typically GitHub releases API)</item>
///             <item>Compare current version with latest available version</item>
///             <item>Provide metadata about newer versions (URL, release notes, published date)</item>
///         </list>
///     </para>
///     <para>Thread-safety: Thread-safe. HTTP requests are independent and concurrent-safe.</para>
/// </remarks>
public interface IUpdateChecker {
    /// <summary>
    ///     Gets the version string of the currently running application.
    /// </summary>
    /// <remarks>
    ///     <para>Extracted from the assembly version at runtime. Format is typically "major.minor.patch" (e.g., "2.0.1").</para>
    /// </remarks>
    /// <value>The current version string.</value>
    string CurrentVersion { get; }

    /// <summary>
    ///     Asynchronously checks for available updates by querying the configured release channel.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Queries the GitHub API (by default) or a configured custom release endpoint to retrieve information
    ///         about the latest available release. Compares versions and returns metadata only if a newer version is
    ///         available.
    ///     </para>
    ///     <para>
    ///         If the update check fails (network error, API error, version parsing error), logs the error and returns null
    ///         rather than throwing an exception. This ensures update checks are non-blocking.
    ///     </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token for the async operation.</param>
    /// <returns>
    ///     A task representing the async operation, containing an <see cref="UpdateInfo" /> record if a newer version is
    ///     available,
    ///     or null if the current version is up-to-date, the check failed, or the network is unavailable.
    /// </returns>
    Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
///     Contains metadata about an available update, including version and release information.
/// </summary>
/// <remarks>
///     <para>
///         This record is returned by <see cref="IUpdateChecker.CheckForUpdatesAsync" /> to provide information
///         about newer versions available on the release channel.
///     </para>
///     <para>
///         Usage: Consumers should check <see cref="IsNewer" /> before presenting update information to users.
///         The <see cref="ReleaseUrl" /> can be opened in the user's browser to download or view release notes.
///     </para>
/// </remarks>
/// <param name="LatestVersion">The version string of the latest release (e.g., "2.0.1"). Already has "v" prefix stripped.</param>
/// <param name="ReleaseUrl">The URL to the release page (typically GitHub releases page). Safe to use as href in UI.</param>
/// <param name="Description">Optional release notes or changelog from the release. May be null or empty for pre-releases.</param>
/// <param name="PublishedAt">UTC timestamp when the release was published.</param>
/// <param name="IsNewer">
///     True if <see cref="LatestVersion" /> is newer than the currently running version. Use to gate
///     update prompts.
/// </param>
public record UpdateInfo(
    string LatestVersion,
    string ReleaseUrl,
    string? Description,
    DateTime PublishedAt,
    bool IsNewer);