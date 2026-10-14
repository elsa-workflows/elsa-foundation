using Elsa.Events.Core.Contracts;

namespace Elsa.Events.Strategies;

/// <summary>
/// Pre-built singleton instances of the baseline publishing strategies. Callers pass these into
/// <c>IEventPublisher.Publish(...)</c> to override the per-call dispatch behavior without resolving
/// a strategy from DI. Most call sites should prefer the intent-revealing
/// <c>IInlineEventPublisher</c> / <c>IDeferredEventPublisher</c> faces over selecting a strategy here.
/// </summary>
internal static class EventPublishingStrategy
{
    /// <summary>Default. Awaited; handlers run in sequence; a throw CAN break the caller.</summary>
    public static readonly IEventPublishingStrategy Sequential = new SequentialProcessingStrategy();

    /// <summary>Fire-and-forget; the call returns before handlers run; failures are shielded.</summary>
    public static readonly IEventPublishingStrategy Background = new BackgroundProcessingStrategy();
}
