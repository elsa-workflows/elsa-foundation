using Kernel.Foundation.Contracts;
using Durable = Kernel.Durable;

namespace Kernel.Foundation.InMemory;

/// <summary>
/// The event route of the built-in foundation's bound ingress. Cancellation releases the reply registration.
/// A request's deliveries remain eligible once they are committed.
/// </summary>
internal sealed class EventIngress(Durable.InMemoryStore store, Durable.Requests requests, string node)
{
    public ValueTask<Admission> SendAsync(BoundScope scope, Target.Event target, string id, string? payload,
        AddOptions? options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();
        if (id.StartsWith('~')) return new(new Admission(AdmissionOutcome.Refused, Reason: RefusalReason.InvalidEventId));
        if (options?.DueAt is not null) return new(new Admission(AdmissionOutcome.Refused));
        var accepted = store.PublishFoundationEvent(scope.Tenant, target.CorrelationKey, id, payload, options?.KeepFor, null, cancellationToken);
        return new(new Admission(accepted == true ? AdmissionOutcome.Accepted : AdmissionOutcome.Duplicate));
    }

    public async ValueTask<Reply> RequestAsync(BoundScope scope, Target.Event target, string id, string? payload,
        AddOptions? options, RequestOptions request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();
        if (id.StartsWith('~')) return Error("invalid event id");
        if (options?.DueAt is not null) return Error("event requests cannot set a due time");

        var now = store.FoundationClock();
        if (request.Deadline <= now) return Error("timeout");
        var open = requests.OpenBound(id, request.Deadline, scope.Tenant, request.Ambient);
        try
        {
            var accepted = store.PublishFoundationEvent(scope.Tenant, target.CorrelationKey, id, payload, options?.KeepFor,
                open.ReplyAddress(node, request.ReleaseBeforeCommit), cancellationToken);
            if (accepted is null) return Error("no waiter");

            return await RequestReplies.WaitAsync(store, requests, open, scope, null, cancellationToken);
        }
        finally
        {
            requests.Disconnect(open);
        }
    }

    public ValueTask SetStartAsync(BoundScope scope, string correlationKey, string wait, StartSpec? spec,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();
        if (spec is not null) HeaderLimits.ValidateStart(spec.Type, spec.TypeData);
        var start = spec is null ? null : new Durable.StartSpec(spec.Type, spec.TypeData, spec.Input,
            Ephemeral: spec.Ephemeral, History: (Durable.History)spec.History);
        store.SetFoundationEventStart(scope.Tenant, correlationKey, wait, start);
        return default;
    }

    private static Reply Error(string error) => new(null, null, Error: error);

}
