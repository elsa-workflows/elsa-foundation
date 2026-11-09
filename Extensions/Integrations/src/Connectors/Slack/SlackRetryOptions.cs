namespace Elsa.Integrations.Connectors.Slack;

/// <summary>
/// Bounded 429 retry. Honours <c>Retry-After</c> in seconds, caps each wait and the total wait,
/// and never delays past the activity cancellation token.
/// </summary>
internal sealed class SlackRetryOptions
{
    public static SlackRetryOptions Default { get; } = new();

    /// <summary>Total HTTP attempts, including the first send. Default 3.</summary>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>Per-wait ceiling when Slack asks for a longer <c>Retry-After</c>. Default 30s.</summary>
    public TimeSpan MaxRetryAfter { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Ceiling for the sum of waits across retries. Default 60s.</summary>
    public TimeSpan MaxTotalWait { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Wait used when Slack omits <c>Retry-After</c>.</summary>
    public TimeSpan DefaultRetryAfter { get; init; } = TimeSpan.FromSeconds(1);

    public Func<TimeSpan, CancellationToken, Task>? DelayAsync { get; init; }
}
