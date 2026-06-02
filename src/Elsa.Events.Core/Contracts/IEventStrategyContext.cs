namespace Elsa.Events.Core.Contracts;

/// <summary>
/// Context passed to an <see cref="IEventPublishingStrategy"/>. Carries the original event
/// context plus the resolved handler set the strategy is dispatching to.
/// </summary>
public interface IEventStrategyContext
{
    IEventContext EventContext { get; }
    IEventHandler[] Handlers { get; }
    IServiceProvider ServiceProvider { get; }
    CancellationToken CancellationToken { get; }
}
