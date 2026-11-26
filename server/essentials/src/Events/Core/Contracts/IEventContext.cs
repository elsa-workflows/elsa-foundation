namespace Elsa.Events.Core.Contracts;

/// <summary>Per-call context flowing through the event pipeline middleware.</summary>
public interface IEventContext
{
    IEvent Event { get; init; }

    IServiceProvider ServiceProvider { get; }

    CancellationToken CancellationToken { get; init; }
}
