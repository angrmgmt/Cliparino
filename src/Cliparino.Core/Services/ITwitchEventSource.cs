using Cliparino.Core.Models;

namespace Cliparino.Core.Services;

public interface ITwitchEventSource : IAsyncDisposable {
    bool IsConnected { get; }

    string SourceName { get; }
    IAsyncEnumerable<TwitchEvent> StreamEventsAsync(CancellationToken cancellationToken = default);

    Task ConnectAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);
}