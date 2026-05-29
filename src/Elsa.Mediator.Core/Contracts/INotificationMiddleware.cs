namespace Elsa.Mediator.Core.Contracts;

/// <summary>A middleware component composed into the notification pipeline.</summary>
public interface INotificationMiddleware
{
    ValueTask Invoke(INotificationContext context);
}
