namespace Elsa.Mediator.Core.Contracts;

/// <summary>Per-call context flowing through the notification pipeline middleware.</summary>
public interface INotificationContext
{
    INotification Notification { get; init; }

    /// <summary>The strategy this dispatch uses — chosen by the caller or the default.</summary>
    IEventPublishingStrategy NotificationStrategy { get; init; }

    IServiceProvider ServiceProvider { get; }

    CancellationToken CancellationToken { get; init; }
}
