namespace Elsa.Studio.Core.Events;

public interface IStudioEventHandler<in TEvent> where TEvent : IStudioEvent
{
    Task Handle(TEvent @event, CancellationToken cancellationToken);
}

