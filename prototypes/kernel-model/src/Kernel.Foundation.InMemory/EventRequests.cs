using Kernel.Foundation.Contracts;
using Durable = Kernel.Durable;

namespace Kernel.Foundation.InMemory;

/// <summary>
/// The event route of the built-in foundation's bound ingress. Cancellation releases the reply registration.
/// A request's deliveries remain eligible once they are committed.
/// </summary>
internal sealed class EventRequests(Durable.InMemoryStore store, Durable.Requests requests, string node)
{
    public async ValueTask<Reply> RequestAsync(BoundScope scope, Target.Event target, string id, string? payload,
        AddOptions? options, RequestOptions request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();
        if (id.StartsWith('~')) return Error("invalid event id");
        if (options?.DueAt is not null) return Error("event requests cannot set a due time");

        var now = store.FoundationClock();
        if (request.Deadline <= now) return Error("timeout");
        var until = options?.KeepFor is { Ticks: > 0 } keep
            ? checked(now + (long)keep.TotalMilliseconds)
            : now - 1;
        var @event = new Durable.EventRow($"{scope.Tenant}_{target.CorrelationKey}", id, payload, until);
        var open = requests.OpenEvent(id, request.Deadline);
        try
        {
            var accepted = store.PublishRequest(@event, open.ReplyAddress(node, request.ReleaseBeforeCommit), cancellationToken);
            if (accepted is null) return Error("no waiter");

            var completion = requests.CompletionOf(open);
            while (!completion.IsCompleted)
            {
                var remaining = (decimal)request.Deadline - store.FoundationClock();
                if (remaining <= 0)
                {
                    requests.Timeout(open);
                    break;
                }
                var delay = TimeSpan.FromMilliseconds((double)Math.Min(remaining, 1000));
                try { return ToContract(await completion.WaitAsync(delay, cancellationToken)); }
                catch (TimeoutException) { }  // Read the shared clock again, which can step back.
            }
            return ToContract(await completion.WaitAsync(cancellationToken));
        }
        finally
        {
            requests.Disconnect(open);
        }
    }

    private static Reply Error(string error) => new(null, null, Error: error);

    private static Reply ToContract(Durable.Reply reply) =>
        new(reply.Channel, reply.Item, (ActorStatus?)reply.Status, reply.Error);
}
