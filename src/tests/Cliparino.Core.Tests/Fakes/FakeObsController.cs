using Cliparino.Core.Services;

namespace Cliparino.Core.Tests.Fakes;

public sealed class FakeObsController : IObsController {
    public bool IsConnected { get; private set; }
    public event EventHandler? Connected;
    public event EventHandler? Disconnected;
    public event EventHandler<string>? SceneCreated;
    public event EventHandler<string>? SourceCreated;
    public event EventHandler? ConfigurationDriftRepaired;

    public Task<bool> ConnectAsync(string host, int port, string password) {
        IsConnected = true;
        Connected?.Invoke(this, EventArgs.Empty);

        return Task.FromResult(true);
    }

    public Task DisconnectAsync() {
        IsConnected = false;
        Disconnected?.Invoke(this, EventArgs.Empty);

        return Task.CompletedTask;
    }

    public Task EnsureClipSceneAndSourceExistsAsync(string sceneName, string sourceName, string url, int width,
        int height) {
        SceneCreated?.Invoke(this, sceneName);
        SourceCreated?.Invoke(this, sourceName);

        return Task.CompletedTask;
    }

    public Task<bool> SetBrowserSourceUrlAsync(string sceneName, string sourceName, string url) {
        return Task.FromResult(true);
    }

    public Task<bool> RefreshBrowserSourceAsync(string sourceName) {
        return Task.FromResult(true);
    }

    public Task<bool> SetSourceVisibilityAsync(string sceneName, string sourceName, bool visible) {
        return Task.FromResult(true);
    }

    public Task<bool> CheckConfigurationDriftAsync(string sceneName, string sourceName, string expectedUrl,
        int expectedWidth, int expectedHeight) {
        return Task.FromResult(false);
    }

    public Task<bool> RepairConfigurationDriftAsync(string sourceName, string expectedUrl, int expectedWidth,
        int expectedHeight) {
        ConfigurationDriftRepaired?.Invoke(this, EventArgs.Empty);

        return Task.FromResult(true);
    }

    public Task<string?> GetCurrentSceneAsync() {
        return Task.FromResult<string?>("Scene");
    }

    public Task EnsureInputAudioConfigAsync(string inputName) {
        return Task.CompletedTask;
    }
}