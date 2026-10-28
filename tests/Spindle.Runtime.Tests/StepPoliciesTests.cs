using Spindle.Abstractions.Policies;
using Spindle.Abstractions.Steps;
using Xunit;

namespace Spindle.Runtime.Tests;

public class StepPoliciesTests
{
    private static readonly RetryPolicy NoJitter = new()
    {
        InitialDelay = TimeSpan.FromSeconds(2),
        MaxDelay = TimeSpan.FromHours(1),
        JitterFactor = 0
    };

    [Theory]
    [InlineData(RetryBackoffKind.Fixed, 1, 2)]
    [InlineData(RetryBackoffKind.Fixed, 4, 2)]
    [InlineData(RetryBackoffKind.Linear, 1, 2)]
    [InlineData(RetryBackoffKind.Linear, 3, 6)]
    [InlineData(RetryBackoffKind.Exponential, 1, 2)]
    [InlineData(RetryBackoffKind.Exponential, 2, 4)]
    [InlineData(RetryBackoffKind.Exponential, 4, 16)]
    public void GetRetryDelay_FollowsBackoffKind(RetryBackoffKind kind, int failedAttempt, int expectedSeconds)
    {
        var delay = StepPolicies.GetRetryDelay(NoJitter with { BackoffKind = kind }, failedAttempt, jitterSample: 0.5);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), delay);
    }

    [Theory]
    [InlineData(0.0, 8)]
    [InlineData(0.5, 10)]
    [InlineData(0.999999, 12)]
    public void GetRetryDelay_AppliesJitterWithinBounds(double sample, int expectedSeconds)
    {
        var policy = NoJitter with
        {
            BackoffKind = RetryBackoffKind.Fixed,
            InitialDelay = TimeSpan.FromSeconds(10),
            JitterFactor = 0.2
        };

        var delay = StepPolicies.GetRetryDelay(policy, failedAttempt: 1, sample);

        Assert.Equal(expectedSeconds, delay.TotalSeconds, precision: 3);
    }

    [Fact]
    public void GetRetryDelay_IsCappedAtMaxDelay()
    {
        var policy = NoJitter with { MaxDelay = TimeSpan.FromSeconds(5), JitterFactor = 1 };

        Assert.Equal(TimeSpan.FromSeconds(5), StepPolicies.GetRetryDelay(policy, failedAttempt: 3, jitterSample: 0.99));
    }

    [Theory]
    [InlineData(RetryBackoffKind.Linear)]
    [InlineData(RetryBackoffKind.Exponential)]
    public void GetRetryDelay_SaturatesInsteadOfOverflowing(RetryBackoffKind kind)
    {
        var policy = NoJitter with
        {
            BackoffKind = kind,
            InitialDelay = TimeSpan.FromDays(1000),
            MaxDelay = TimeSpan.MaxValue
        };

        Assert.Equal(TimeSpan.MaxValue, StepPolicies.GetRetryDelay(policy, int.MaxValue, jitterSample: 0.5));
    }

    [Fact]
    public void GetRetryDelay_IsZeroForZeroInitialDelay()
    {
        var policy = NoJitter with { InitialDelay = TimeSpan.Zero, JitterFactor = 1 };

        Assert.Equal(TimeSpan.Zero, StepPolicies.GetRetryDelay(policy, failedAttempt: 2000, jitterSample: 0.99));
    }

    [Fact]
    public void GetRetryDelay_WithFullJitter_NeverGoesNegative()
    {
        var policy = NoJitter with { JitterFactor = 1 };

        Assert.Equal(TimeSpan.Zero, StepPolicies.GetRetryDelay(policy, failedAttempt: 1, jitterSample: 0));
    }

    [Theory]
    [InlineData(null, 1, false)]
    [InlineData(1, 1, false)]
    [InlineData(3, 2, true)]
    [InlineData(3, 3, false)]
    public void CanRetry_CountsTheInitialAttempt(int? maxAttempts, int failedAttempt, bool expected)
    {
        var policy = maxAttempts is { } attempts ? new RetryPolicy { MaxAttempts = attempts } : null;

        Assert.Equal(expected, StepPolicies.CanRetry(policy, failedAttempt));
    }

    [Fact]
    public void Validate_AcceptsDefaultsAndNone()
    {
        StepPolicies.Validate(new StepOptions
        {
            Retry = new RetryPolicy(),
            Timeout = new TimeoutPolicy { Timeout = TimeSpan.FromSeconds(1) }
        });
        StepPolicies.Validate(new StepOptions { Retry = RetryPolicy.None });
        StepPolicies.Validate(new StepOptions { Retry = NoJitter with { InitialDelay = TimeSpan.Zero, MaxDelay = TimeSpan.Zero } });
    }

    public static TheoryData<RetryPolicy> InvalidRetryPolicies => new()
    {
        new RetryPolicy { MaxAttempts = 0 },
        new RetryPolicy { InitialDelay = TimeSpan.FromSeconds(-1) },
        new RetryPolicy { InitialDelay = TimeSpan.FromMinutes(2), MaxDelay = TimeSpan.FromMinutes(1) },
        new RetryPolicy { JitterFactor = -0.1 },
        new RetryPolicy { JitterFactor = 1.1 },
        new RetryPolicy { JitterFactor = double.NaN },
        new RetryPolicy { JitterFactor = double.PositiveInfinity },
        new RetryPolicy { BackoffKind = (RetryBackoffKind)42 }
    };

    [Theory]
    [MemberData(nameof(InvalidRetryPolicies))]
    public void Validate_RejectsInvalidRetryPolicy(RetryPolicy retry)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StepPolicies.Validate(new StepOptions { Retry = retry }));
    }

    public static TheoryData<TimeoutPolicy> InvalidTimeoutPolicies => new()
    {
        new TimeoutPolicy { Timeout = TimeSpan.Zero },
        new TimeoutPolicy { Timeout = TimeSpan.FromSeconds(-1) },
        new TimeoutPolicy { Timeout = TimeSpan.FromSeconds(1), CancellationGracePeriod = TimeSpan.FromSeconds(-1) },
        new TimeoutPolicy { Timeout = TimeSpan.FromSeconds(1), Behavior = (TimeoutBehavior)42 }
    };

    [Theory]
    [MemberData(nameof(InvalidTimeoutPolicies))]
    public void Validate_RejectsInvalidTimeoutPolicy(TimeoutPolicy timeout)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StepPolicies.Validate(new StepOptions { Timeout = timeout }));
    }

    [Fact]
    public void AddSaturating_ClampsAtMaxValue()
    {
        Assert.Equal(DateTimeOffset.MaxValue, StepPolicies.AddSaturating(DateTimeOffset.MaxValue.AddDays(-1), TimeSpan.FromDays(2)));
        Assert.Equal(TimeSpan.MaxValue, StepPolicies.AddSaturating(TimeSpan.MaxValue, TimeSpan.FromDays(1)));
    }
}
