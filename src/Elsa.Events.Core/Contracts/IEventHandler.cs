namespace Elsa.Events.Core.Contracts;

/// <summary>Non-generic marker for DI registration scanning.</summary>
public interface IEventHandler
{
}

/// <summary>Handles an event of type <typeparamref name="T"/>.</summary>
public interface IEventHandler<in T> : IEventHandler where T : IEvent
{
    Task Handle(T @event, CancellationToken cancellationToken);
}
