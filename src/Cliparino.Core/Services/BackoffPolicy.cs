namespace Cliparino.Core.Services;

/// <summary>
///     Implements an exponential backoff strategy with jitter for reconnection attempts.
/// </summary>
/// <remarks>
///     <para>Used to prevent "thundering herd" problems when multiple services attempt to reconnect simultaneously.</para>
///     <para>
///         The delay grows exponentially with the number of attempts, capped by a maximum value, and randomized by a
///         jitter factor.
///     </para>
///     <para>Thread-safety: Stateless and thread-safe. Uses <see cref="Random.Shared" /> for jitter calculation.</para>
/// </remarks>
public class BackoffPolicy {
    private readonly int _baseDelaySeconds;
    private readonly double _jitterFactor;
    private readonly int _maxDelaySeconds;

    /// <summary>
    ///     Initializes a new instance of the <see cref="BackoffPolicy" /> class.
    /// </summary>
    /// <param name="baseDelaySeconds">The initial delay for the first attempt in seconds.</param>
    /// <param name="maxDelaySeconds">The maximum allowed delay in seconds.</param>
    /// <param name="jitterFactor">The percentage of jitter to apply (0.0 to 1.0).</param>
    public BackoffPolicy(int baseDelaySeconds = 2,
        int maxDelaySeconds = 300,
        double jitterFactor = 0.3) {
        _baseDelaySeconds = baseDelaySeconds;
        _maxDelaySeconds = maxDelaySeconds;
        _jitterFactor = jitterFactor;
    }

    /// <summary>
    ///     Gets a default backoff policy (2s base, 300s max, 0.3 jitter).
    /// </summary>
    public static BackoffPolicy Default => new();

    /// <summary>
    ///     Gets a fast backoff policy (1s base, 30s max).
    /// </summary>
    public static BackoffPolicy Fast => new(1, 30);

    /// <summary>
    ///     Gets a slow backoff policy (5s base, 600s max).
    /// </summary>
    public static BackoffPolicy Slow => new(5, 600);

    /// <summary>
    ///     Calculates the delay for a given attempt number.
    /// </summary>
    /// <param name="attemptNumber">The number of consecutive failed attempts.</param>
    /// <returns>A <see cref="TimeSpan" /> representing the calculated delay.</returns>
    public TimeSpan CalculateDelay(int attemptNumber) {
        var exponentialDelay = Math.Min(_baseDelaySeconds * Math.Pow(2, attemptNumber),
            _maxDelaySeconds);

        var jitterRange = exponentialDelay * _jitterFactor;
        var jitter = (Random.Shared.NextDouble() * 2 - 1) * jitterRange;

        var totalDelay = Math.Max(1, exponentialDelay + jitter);

        return TimeSpan.FromSeconds(totalDelay);
    }
}