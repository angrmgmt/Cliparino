using Cliparino.Core.Services;
using Xunit;

namespace Cliparino.Core.Tests;

public class BackoffPolicyTests {
    [Fact]
    public void CalculateDelay_FirstAttempt_IsCorrect() {
        // Arrange
        var policy = new BackoffPolicy(2, 300, 0);

        // Act
        var delay = policy.CalculateDelay(0);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(2), delay);
    }

    [Fact]
    public void CalculateDelay_SecondAttempt_IsCorrect() {
        // Arrange
        var policy = new BackoffPolicy(2, 300, 0);

        // Act
        var delay = policy.CalculateDelay(1);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(4), delay);
    }

    [Fact]
    public void CalculateDelay_RespectsMaxDelay() {
        // Arrange
        var policy = new BackoffPolicy(2, 10, 0);

        // Act
        var delay = policy.CalculateDelay(5); // 2 * 2^5 = 64

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(10), delay);
    }

    [Fact]
    public void CalculateDelay_WithJitter_IsWithinRange() {
        // Arrange
        var baseDelay = 10;
        var jitterFactor = 0.5;
        var policy = new BackoffPolicy(baseDelay, 100, jitterFactor);

        // Act
        var delay = policy.CalculateDelay(0);

        // Assert
        var minExpected = baseDelay * (1 - jitterFactor);
        var maxExpected = baseDelay * (1 + jitterFactor);

        Assert.InRange(delay.TotalSeconds, minExpected, maxExpected);
    }
}