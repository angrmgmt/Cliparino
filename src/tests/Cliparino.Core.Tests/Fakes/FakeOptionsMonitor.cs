using Microsoft.Extensions.Options;

namespace Cliparino.Core.Tests.Fakes;

public sealed class FakeOptionsMonitor<T> : IOptionsMonitor<T> where T : class, new() {
    public T CurrentValue { get; set; } = new();

    public T Get(string? name) {
        return CurrentValue;
    }

    public IDisposable? OnChange(Action<T, string?> listener) {
        return null;
    }
}