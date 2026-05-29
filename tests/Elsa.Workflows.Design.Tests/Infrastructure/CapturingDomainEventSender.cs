using Elsa.Mediator.Core.Contracts;
using System.Collections.Concurrent;

namespace Elsa.Workflows.Design.Tests.Infrastructure;

/// <summary>
/// Stub <see cref="IDomainEventSender"/> that captures every event passed to <see cref="Send"/>.
/// Used to assert that the mutation pipeline dispatches the expected gate event and to let
/// tests simulate validator contributions via <see cref="OnSend"/>.
/// </summary>
public sealed class CapturingDomainEventSender : IDomainEventSender
{
    private readonly ConcurrentQueue<IDomainEvent> _events = new();

    public IReadOnlyList<IDomainEvent> CapturedEvents => _events.ToArray();

    public T? LastOf<T>() where T : class, IDomainEvent => _events.OfType<T>().LastOrDefault();

    /// <summary>
    /// Optional hook invoked on every <see cref="Send"/> call before the event is enqueued.
    /// Tests use it to simulate validator contributions (e.g. add a <c>ValidationError</c> to
    /// an <c>OnDraftValidating</c> instance) without spinning up the full
    /// <c>IDomainEventPipeline</c> + handler-invoker middleware.
    /// </summary>
    public Action<IDomainEvent>? OnSend { get; set; }

    public Task Send(IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        OnSend?.Invoke(domainEvent);
        _events.Enqueue(domainEvent);
        return Task.CompletedTask;
    }
}
