using Elsa.Notifications.Core;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Notifications.Contexts
{
    /// <summary>
    /// Represents a context for publishing events.
    /// </summary>
    /// <param name="NotificationContext">The notification to publish.</param>
    /// <param name="Handlers">The handlers to publish the notification to.</param>
    /// <param name="Logger">The logger.</param>
    /// <param name="ServiceProvider">The service provider to resolve services from.</param>
    /// <param name="CancellationToken">The cancellation token.</param>
    public sealed record NotificationStrategyContext(
        INotificationContext NotificationContext,
        INotificationHandler[] Handlers,
        ILogger Logger,
        IServiceProvider ServiceProvider,
        CancellationToken CancellationToken = default
    )
    : INotificationStrategyContext;
}
