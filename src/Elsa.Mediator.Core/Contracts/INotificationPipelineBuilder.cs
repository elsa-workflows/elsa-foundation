namespace Elsa.Mediator.Core.Contracts;

/// <summary>Fluent builder for composing the notification middleware pipeline.</summary>
public interface INotificationPipelineBuilder
{
    IDictionary<string, object?> Properties { get; }

    IServiceProvider ApplicationServices { get; }

    INotificationPipelineBuilder Use(
        Func<NotificationMiddlewareDelegate, NotificationMiddlewareDelegate> middleware
    );

    NotificationMiddlewareDelegate Build();
}
