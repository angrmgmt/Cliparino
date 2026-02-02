namespace Cliparino.Core.Services;

public interface IObsController {
    bool IsConnected { get; }
    Task<bool> ConnectAsync(string host, int port, string password);

    Task DisconnectAsync();

    Task EnsureClipSceneAndSourceExistsAsync(string sceneName, string sourceName, string url, int width, int height);

    Task<bool> SetBrowserSourceUrlAsync(string sceneName, string sourceName, string url);

    Task<bool> RefreshBrowserSourceAsync(string sourceName);

    Task<bool> SetSourceVisibilityAsync(string sceneName, string sourceName, bool visible);

    Task<bool> CheckConfigurationDriftAsync(
        string sceneName, string sourceName, string expectedUrl, int expectedWidth, int expectedHeight
    );

    Task<string?> GetCurrentSceneAsync();

    event EventHandler? Connected;
    event EventHandler? Disconnected;
}