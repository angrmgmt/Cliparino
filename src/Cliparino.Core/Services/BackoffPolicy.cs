namespace Cliparino.Core.Services;

public class BackoffPolicy {
    private readonly int _baseDelaySeconds;
    private readonly double _jitterFactor;
    private readonly int _maxDelaySeconds;

    public BackoffPolicy(
        int baseDelaySeconds = 2,
        int maxDelaySeconds = 300,
        double jitterFactor = 0.3
    ) {
        _baseDelaySeconds = baseDelaySeconds;
        _maxDelaySeconds = maxDelaySeconds;
        _jitterFactor = jitterFactor;
    }

    public static BackoffPolicy Default => new();

    public static BackoffPolicy Fast => new(1, 30);

    public static BackoffPolicy Slow => new(5, 600);

    public TimeSpan CalculateDelay(int attemptNumber) {
        var exponentialDelay = Math.Min(
            _baseDelaySeconds * Math.Pow(2, attemptNumber),
            _maxDelaySeconds
        );

        var jitterRange = exponentialDelay * _jitterFactor;
        var jitter = (Random.Shared.NextDouble() * 2 - 1) * jitterRange;

        var totalDelay = Math.Max(1, exponentialDelay + jitter);

        return TimeSpan.FromSeconds(totalDelay);
    }
}