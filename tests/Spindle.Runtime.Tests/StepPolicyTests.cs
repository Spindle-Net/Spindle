using Spindle.Abstractions.Policies;
using Spindle.Abstractions.Steps;
using Xunit;

namespace Spindle.Runtime.Tests;

public sealed class StepPolicyTests
{
    [Fact]
    public void ExponentialBackoffSaturatesWithoutOverflow()
    {
        var policy = RetryPolicy.Exponential(int.MaxValue) with { JitterFactor = 0 };
        Assert.Equal(policy.MaxDelay, StepPolicy.Delay(policy, int.MaxValue));
        Assert.Equal(TimeSpan.Zero, StepPolicy.Delay(policy with { InitialDelay = TimeSpan.Zero }, int.MaxValue));
        Assert.Equal(DateTimeOffset.MaxValue, StepPolicy.DueAt(DateTimeOffset.UtcNow, TimeSpan.MaxValue));
    }

    [Fact]
    public void JitterStaysWithinBoundsAndHonorsCap()
    {
        var policy = RetryPolicy.Exponential(3) with { InitialDelay = TimeSpan.FromSeconds(10),
            MaxDelay = TimeSpan.FromSeconds(11), JitterFactor = 0.2 };
        for (var i = 0; i < 100; i++)
            Assert.InRange(StepPolicy.Delay(policy, 1), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(11));
    }

    [Fact]
    public void RejectsInvalidTimeoutPolicies()
    {
        foreach (var timeout in new[] {
            new TimeoutPolicy { Timeout = TimeSpan.Zero },
            new TimeoutPolicy { Timeout = TimeSpan.FromSeconds(1), CancellationGracePeriod = TimeSpan.FromTicks(-1) },
            new TimeoutPolicy { Timeout = TimeSpan.FromSeconds(1), Behavior = (TimeoutBehavior)99 } })
            Assert.Throws<ArgumentOutOfRangeException>(() => StepPolicy.Validate(new StepOptions { Timeout = timeout }));
    }
}
