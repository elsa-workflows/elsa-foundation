namespace Elsa.Mediator.Core.Contracts;

/// <summary>
/// Context passed to an <see cref="IEventPublishingStrategy"/>. Carries the original
/// notification context plus the resolved handler set the strategy is dispatching to.
/// </summary>
public interface INotificationStrategyContext
{
    INotificationContext NotificationContext { get; }
    INotificationHandler[] Handlers { get; }
    IServiceProvider ServiceProvider { get; }
    CancellationToken CancellationToken { get; }
}
