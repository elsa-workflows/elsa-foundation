namespace Elsa.Events.Strategies;

/// <summary>
/// Pre-built singleton instances of the three baseline publishing strategies. Callers pass
/// these into <c>IEventPublisher.Publish(...)</c> to override the per-call dispatch behaviour
/// without resolving a strategy from DI.
/// </summary>
public static class EventPublishingStrategy
{
    /// <summary>Default. Awaited; handlers run in sequence; a throw CAN break the caller.</summary>
    public static readonly SequentialProcessingStrategy Sequential = new();

    /// <summary>Awaited fan-out; handlers run in parallel.</summary>
    public static readonly ParallelProcessingStrategy Parallel = new();

    /// <summary>Fire-and-forget; the call returns before handlers run; failures are shielded.</summary>
    public static readonly BackgroundProcessingStrategy Background = new();
}
