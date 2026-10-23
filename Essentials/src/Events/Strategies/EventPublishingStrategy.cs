using Elsa.Events.Core.Contracts;

namespace Elsa.Events.Strategies;

/// <summary>
/// Pre-built singleton instances of the baseline publishing strategies, passed into
/// <c>IEventPublisher.Publish(...)</c> by the <c>IInlineEventPublisher</c> / <c>IDeferredEventPublisher</c> faces.
/// </summary>
internal static class EventPublishingStrategy
{
    /// <summary>Awaited; handlers run in sequence; a throw CAN break the caller.</summary>
    public static readonly IEventPublishingStrategy Sequential = new SequentialProcessingStrategy();

    /// <summary>Fire-and-forget; the call returns before handlers run; failures are shielded.</summary>
    public static readonly IEventPublishingStrategy Background = new BackgroundProcessingStrategy();
}
