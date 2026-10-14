namespace Elsa.Events.Core.Contracts;

/// <summary>Per-call context flowing through the event pipeline middleware.</summary>
public interface IEventContext
{
    IEvent Event { get; init; }

    /// <summary>The strategy this dispatch uses, chosen by the delivery face the event was published through.</summary>
    internal IEventPublishingStrategy Strategy { get; init; }

    IServiceProvider ServiceProvider { get; }

    CancellationToken CancellationToken { get; init; }
}
