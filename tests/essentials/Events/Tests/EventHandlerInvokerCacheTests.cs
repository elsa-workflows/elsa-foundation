using Xunit;
using Elsa.Events.Contexts;
using Elsa.Events.Core.Contracts;
using Elsa.Events.Strategies;

namespace Elsa.Events.Tests;

/// <summary>
/// IN-10: per-event-type handler resolution is compiled once and cached, replacing per-Publish
/// reflection (MakeGenericType + GetMethod + MethodBase.Invoke). Behaviour must be preserved:
/// correct dispatch, and handler exceptions surface unwrapped.
/// </summary>
public class EventHandlerInvokerCacheTests
{
    private sealed record TestEvent(string Value) : IEvent;
    private sealed record OtherEvent : IEvent;

    private sealed class RecordingHandler : IEventHandler<TestEvent>
    {
        public readonly List<string> Seen = [];
        public Task Handle(TestEvent @event, CancellationToken cancellationToken)
        {
            Seen.Add(@event.Value);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHandler : IEventHandler<TestEvent>
    {
        public Task Handle(TestEvent @event, CancellationToken cancellationToken)
            => throw new InvalidOperationException("boom");
    }

    [Fact]
    public void GetInvokerReturnsSameCachedDelegatePerEventType()
    {
        var first = EventHandlerHelper.GetInvoker(typeof(TestEvent));
        var second = EventHandlerHelper.GetInvoker(typeof(TestEvent));

        Assert.Same(first, second); // built once, cached — no per-call rebuild
        Assert.NotSame(first, EventHandlerHelper.GetInvoker(typeof(OtherEvent))); // keyed by event type
    }

    [Fact]
    public async Task InvokeDispatchesToTypedHandler()
    {
        var handler = new RecordingHandler();
        var context = new EventContext(new TestEvent("hello"), EventPublishingStrategy.Sequential, null!);

        await handler.Invoke(context);

        Assert.Equal(["hello"], handler.Seen);
    }

    [Fact]
    public async Task SequentialStrategyDispatchesToAllHandlersInOrder()
    {
        var a = new RecordingHandler();
        var b = new RecordingHandler();
        var eventContext = new EventContext(new TestEvent("x"), EventPublishingStrategy.Sequential, null!);
        var strategyContext = new EventStrategyContext(eventContext, [a, b], null!);

        await EventPublishingStrategy.Sequential.PublishAsync(strategyContext);

        Assert.Equal(["x"], a.Seen);
        Assert.Equal(["x"], b.Seen);
    }

    [Fact]
    public async Task HandlerExceptionSurfacesUnwrapped()
    {
        var handler = new ThrowingHandler();
        var context = new EventContext(new TestEvent("boom"), EventPublishingStrategy.Sequential, null!);

        // The compiled delegate calls Handle directly, so the handler's own exception propagates —
        // NOT a TargetInvocationException (which reflection-based MethodBase.Invoke would have wrapped).
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Invoke(context));
        Assert.Equal("boom", ex.Message);
    }
}
