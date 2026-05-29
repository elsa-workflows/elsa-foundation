namespace Elsa.Mediator.Core.Contracts;

/// <summary>Non-generic marker for DI registration scanning.</summary>
public interface INotificationHandler
{
}

/// <summary>Handles a notification of type <typeparamref name="T"/>.</summary>
public interface INotificationHandler<in T> : INotificationHandler where T : INotification
{
    Task Handle(T notification, CancellationToken cancellationToken);
}
