using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Extensions;
using System.Reflection;

namespace Elsa.Mediator.Notifications.Helpers;

public static class NotificationHandlerHelper
{
    public static MethodInfo GetHandleAsync(this Type notificationType)
    {
        var handlerType = typeof(INotificationHandler<>).MakeGenericType(notificationType);
        return handlerType.GetMethod(nameof(INotificationHandler<>.Handle))!;
    }

    public static Task InvokeAsync(this INotificationHandler handler, MethodBase handleMethod, INotificationContext notificationContext)
    {
        var notification = notificationContext.Notification;
        var cancellationToken = notificationContext.CancellationToken;
        return handleMethod.InvokeAndUnwrap<Task>(handler, [notification, cancellationToken]);
    }
}
