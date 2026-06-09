using Elsa.Events.Core.Contracts;
using Elsa.Primitives.Extensions;
using System.Reflection;

namespace Elsa.Events.Strategies;

public static class EventHandlerHelper
{
    public static MethodInfo GetHandleAsync(this Type eventType)
    {
        var handlerType = typeof(IEventHandler<>).MakeGenericType(eventType);
        return handlerType.GetMethod(nameof(IEventHandler<>.Handle))!;
    }

    public static Task InvokeAsync(this IEventHandler handler, MethodBase handleMethod, IEventContext eventContext)
    {
        var @event = eventContext.Event;
        var cancellationToken = eventContext.CancellationToken;
        return handleMethod.InvokeAndUnwrap<Task>(handler, [@event, cancellationToken]);
    }
}
