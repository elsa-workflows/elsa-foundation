using Elsa.Etl.Core;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Stores;

/// <summary>Shared bounded HTTP retry and attempt-bound mechanics for ETL fetches and deliveries.</summary>
internal static class EtlHttpRetryPolicy
{
    private static readonly HashSet<int> RetryableStatusCodes = [408, 429, 500, 502, 503, 504];

    internal static bool IsValid(EtlHttpPolicy? policy) => policy is not null && policy.MaxAttempts > 0 &&
        policy.DeadlineSeconds > 0 && policy.RequestTimeoutSeconds > 0 &&
        policy.RequestTimeoutSeconds <= policy.DeadlineSeconds && policy.InitialDelaySeconds > 0 &&
        policy.MaxDelaySeconds >= policy.InitialDelaySeconds && policy.MaxDelaySeconds <= policy.DeadlineSeconds;

    internal static bool IsAttemptId(string? value) => value is { Length: 32 } &&
        Guid.TryParseExact(value, "N", out var parsed) && StringComparer.Ordinal.Equals(parsed.ToString("N"), value);

    internal static bool IsUtc(DateTimeOffset value) => value.Offset == TimeSpan.Zero;

    internal static bool IsRetryable(int? statusCode) => statusCode is null || RetryableStatusCodes.Contains(statusCode.Value);

    /// <summary>Calculates an attempt deadline while preserving invalid-on-overflow admission behavior.</summary>
    internal static bool TryGetDeadline(DateTimeOffset startedAt, EtlHttpPolicy policy, out DateTimeOffset deadline)
    {
        try
        {
            deadline = startedAt.AddSeconds(policy.DeadlineSeconds);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            deadline = default;
            return false;
        }
    }

    /// <summary>Clamps one request expiry to the overall deadline without overflowing near DateTimeOffset.MaxValue.</summary>
    internal static DateTimeOffset ClampExpiry(DateTimeOffset startedAt, DateTimeOffset deadline, EtlHttpPolicy policy)
    {
        var timeoutTicks = TimeSpan.FromSeconds(policy.RequestTimeoutSeconds).Ticks;
        var remainingTicks = deadline.Ticks - startedAt.Ticks;
        return timeoutTicks >= remainingTicks
            ? deadline
            : new DateTimeOffset(startedAt.Ticks + timeoutTicks, TimeSpan.Zero);
    }

    /// <summary>Returns the later of capped exponential backoff and a future Retry-After instant.</summary>
    internal static DateTimeOffset NextRetryAt(int attemptNumber, DateTimeOffset completedAt,
        DateTimeOffset? retryAfter, EtlHttpPolicy policy)
    {
        var backoffAt = AddSecondsSaturated(completedAt, BackoffSeconds(attemptNumber, policy));
        return retryAfter is not null && retryAfter.Value > completedAt && retryAfter.Value > backoffAt
            ? retryAfter.Value
            : backoffAt;
    }

    internal static DateTimeOffset AddSecondsSaturated(DateTimeOffset value, int seconds)
    {
        try { return value.AddSeconds(seconds); }
        catch (ArgumentOutOfRangeException) { return DateTimeOffset.MaxValue; }
    }

    private static int BackoffSeconds(int attemptNumber, EtlHttpPolicy policy)
    {
        long delay = policy.InitialDelaySeconds;
        for (var i = 1; i < attemptNumber && delay < policy.MaxDelaySeconds; i++)
            delay = Math.Min(policy.MaxDelaySeconds, delay * 2);
        return (int)Math.Min(delay, policy.MaxDelaySeconds);
    }
}
