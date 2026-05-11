using Elsa.Primitives.Extensions;
using Elsa.Notifications.Core;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Elsa.Notifications.Helpers
{
    public static class NotificationHandlerHelper
    {
        public static MethodInfo GetHandleAsync(this Type notificationType)
        {
            var handlerType = typeof(INotificationHandler<>).MakeGenericType(notificationType);
            return handlerType.GetMethod(nameof(INotificationHandler<>.HandleAsync))!;
        }

        public static Task InvokeAsync(this INotificationHandler handler, MethodBase handleMethod, INotificationContext notificationContext)
        {
            var notification = notificationContext.Notification;
            var cancellationToken = notificationContext.CancellationToken;
            return handleMethod.InvokeAndUnwrap<Task>(handler, [notification, cancellationToken]);
        }
    }
}
