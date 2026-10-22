using Elsa.Etl.Core;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;

namespace Elsa.Etl.Tests;

public sealed class EtlHttpRetryPolicyTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-20T18:00:00Z");

    [Fact]
    public void Policy_and_attempt_identity_validation_reject_out_of_contract_values()
    {
        Assert.True(EtlHttpRetryPolicy.IsValid(Policy()));
        Assert.False(EtlHttpRetryPolicy.IsValid(Policy(requestTimeoutSeconds: 61)));
        Assert.True(EtlHttpRetryPolicy.IsAttemptId("0000000000000000000000000000000a"));
        Assert.False(EtlHttpRetryPolicy.IsAttemptId("0000000000000000000000000000000A"));
        Assert.False(EtlHttpRetryPolicy.IsAttemptId("00000000-0000-0000-0000-00000000000a"));
        Assert.True(EtlHttpRetryPolicy.IsUtc(Start));
        Assert.False(EtlHttpRetryPolicy.IsUtc(Start.ToOffset(TimeSpan.FromHours(1))));
    }

    [Fact]
    public void Deadline_overflow_is_refused_and_expiry_is_clamped_to_the_deadline()
    {
        var policy = Policy(deadlineSeconds: 10, requestTimeoutSeconds: 8);
        var nearMax = DateTimeOffset.MaxValue.AddSeconds(-5);

        Assert.False(EtlHttpRetryPolicy.TryGetDeadline(nearMax, policy, out _));

        var deadline = Start.AddSeconds(5);
        Assert.Equal(deadline, EtlHttpRetryPolicy.ClampExpiry(Start, deadline, policy));
        Assert.Equal(Start.AddSeconds(8), EtlHttpRetryPolicy.ClampExpiry(Start, Start.AddSeconds(20), policy));
    }

    [Fact]
    public void Retry_time_uses_capped_backoff_and_only_a_later_retry_after()
    {
        var policy = Policy(initialDelaySeconds: 2, maxDelaySeconds: 5);
        var completedAt = Start.AddSeconds(10);

        Assert.Equal(completedAt.AddSeconds(5),
            EtlHttpRetryPolicy.NextRetryAt(4, completedAt, completedAt.AddSeconds(3), policy));
        Assert.Equal(completedAt.AddSeconds(12),
            EtlHttpRetryPolicy.NextRetryAt(1, completedAt, completedAt.AddSeconds(12), policy));
        Assert.Equal(DateTimeOffset.MaxValue,
            EtlHttpRetryPolicy.NextRetryAt(1, DateTimeOffset.MaxValue.AddTicks(-1), null, policy));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(408, true)]
    [InlineData(429, true)]
    [InlineData(500, true)]
    [InlineData(502, true)]
    [InlineData(503, true)]
    [InlineData(504, true)]
    [InlineData(400, false)]
    [InlineData(501, false)]
    [InlineData(599, false)]
    public void Retryability_is_limited_to_uncertain_and_documented_transient_statuses(int? statusCode, bool expected)
    {
        Assert.Equal(expected, EtlHttpRetryPolicy.IsRetryable(statusCode));
    }

    private static EtlHttpPolicy Policy(int deadlineSeconds = 60, int requestTimeoutSeconds = 10,
        int initialDelaySeconds = 1, int maxDelaySeconds = 8) => new()
    {
        MaxAttempts = 5,
        DeadlineSeconds = deadlineSeconds,
        RequestTimeoutSeconds = requestTimeoutSeconds,
        InitialDelaySeconds = initialDelaySeconds,
        MaxDelaySeconds = maxDelaySeconds
    };
}
