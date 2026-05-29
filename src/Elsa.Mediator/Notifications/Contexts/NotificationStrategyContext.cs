using Elsa.Mediator.Core.Contracts;

namespace Elsa.Mediator.Notifications.Contexts;

/// <inheritdoc cref="INotificationStrategyContext" />
public sealed record NotificationStrategyContext(
    INotificationContext NotificationContext,
    INotificationHandler[] Handlers,
    IServiceProvider ServiceProvider,
    CancellationToken CancellationToken = default
)
    : INotificationStrategyContext;
