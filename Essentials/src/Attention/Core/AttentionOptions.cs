namespace Elsa.Attention.Core;

public sealed class AttentionOptions
{
    public TimeSpan ContributorTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public int MaxItemsPerContributor { get; set; } = 5;

    public int MaxDownstreamCallsPerContributor { get; set; } = 2;

    public bool ExposeRestrictedSummaries { get; set; }

    public Dictionary<string, Dictionary<string, string>> ContributorThresholdOverrides { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AttentionExecutionBudget(int maxDownstreamCalls)
{
    private int _remaining = maxDownstreamCalls;

    public int RemainingDownstreamCalls => Math.Max(Volatile.Read(ref _remaining), 0);

    public void ConsumeDownstreamCall()
    {
        if (Interlocked.Decrement(ref _remaining) < 0)
            throw new AttentionBudgetExceededException();
    }
}

public sealed class AttentionBudgetExceededException()
    : Exception("The attention contributor exceeded its downstream call budget.");

public sealed class AttentionQueryException(string message) : Exception(message);
