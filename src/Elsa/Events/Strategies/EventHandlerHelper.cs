using Elsa.Events.Core.Contracts;
using System.Collections.Concurrent;
using System.Linq.Expressions;

namespace Elsa.Events.Strategies;

public static class EventHandlerHelper
{
    // Per-event-type compiled invoker. Rebuilding typeof(IEventHandler<>).MakeGenericType + GetMethod and
    // then dispatching through MethodBase.Invoke on every Publish was avoidable per-call reflection on a
    // hot path (IN-10). The compiled delegate closes over nothing, casts the handler and event to their
    // closed generic types, and calls Handle directly — no reflection, no args-array allocation per
    // dispatch, and exceptions surface unwrapped (the handler's own exception, not a
    // TargetInvocationException), matching the previous InvokeAndUnwrap behaviour.
    private static readonly ConcurrentDictionary<Type, Func<IEventHandler, IEvent, CancellationToken, Task>> InvokerCache = new();

    /// <summary>
    /// Invokes <c>IEventHandler&lt;TEvent&gt;.Handle</c> on <paramref name="handler"/> for the event
    /// carried by <paramref name="eventContext"/>, using a cached compiled delegate keyed by event type.
    /// </summary>
    public static Task Invoke(this IEventHandler handler, IEventContext eventContext)
    {
        var invoker = GetInvoker(eventContext.Event.GetType());
        return invoker(handler, eventContext.Event, eventContext.CancellationToken);
    }

    /// <summary>
    /// Returns the cached compiled invoker for <paramref name="eventType"/>, building it once on first use.
    /// </summary>
    public static Func<IEventHandler, IEvent, CancellationToken, Task> GetInvoker(Type eventType)
        => InvokerCache.GetOrAdd(eventType, static t => BuildInvoker(t));

    private static Func<IEventHandler, IEvent, CancellationToken, Task> BuildInvoker(Type eventType)
    {
        var handlerType = typeof(IEventHandler<>).MakeGenericType(eventType);
        var handleMethod = handlerType.GetMethod(nameof(IEventHandler<>.Handle))!;

        var handlerParam = Expression.Parameter(typeof(IEventHandler), "handler");
        var eventParam = Expression.Parameter(typeof(IEvent), "event");
        var cancellationTokenParam = Expression.Parameter(typeof(CancellationToken), "cancellationToken");

        var call = Expression.Call(
            Expression.Convert(handlerParam, handlerType),
            handleMethod,
            Expression.Convert(eventParam, eventType),
            cancellationTokenParam
        );

        return Expression
            .Lambda<Func<IEventHandler, IEvent, CancellationToken, Task>>(call, handlerParam, eventParam, cancellationTokenParam)
            .Compile();
    }
}
