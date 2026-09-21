namespace Elsa.Events.Core.Contracts;

/// <summary>Per-call context flowing through the event pipeline middleware.</summary>
public interface IEventContext
{
    IEvent Event { get; init; }

    /// <summary>The strategy this dispatch uses — chosen by the caller or the default.</summary>
    IEventPublishingStrategy Strategy { get; init; }

    IServiceProvider ServiceProvider { get; }

    CancellationToken CancellationToken { get; init; }
}
