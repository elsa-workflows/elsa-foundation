using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Notifications.Core
{
    public interface INotificationStrategyContext
    {
        IServiceProvider ServiceProvider { get; }

        INotificationContext NotificationContext { get; }

        CancellationToken CancellationToken { get; }

        INotificationHandler[] Handlers { get; }
    }
}
