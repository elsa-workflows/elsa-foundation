using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Notifications.Core
{
    /// <summary>
    /// Represents a delegate for a notification middleware.
    /// </summary>
    public delegate ValueTask NotificationMiddlewareDelegate(INotificationContext context);
}
