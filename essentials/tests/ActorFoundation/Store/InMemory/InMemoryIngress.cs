using Kernel.Foundation.Contracts;
using Durable = Kernel.Durable;

namespace Kernel.Foundation.InMemory;

/// <summary>The built-in in-memory ingress over the existing bound actor and event admission routes.</summary>
internal sealed class InMemoryIngress(Durable.InMemoryStore store, Durable.Requests requests, string node,
    Func<BoundScope, string, bool>? authorizeOperator = null, Durable.Worker? inlineOwner = null) : IIngress
{
    private readonly OrdinaryIngress _ordinary = new(store);
    private readonly EventIngress _event = new(store, requests, node);
    private readonly OperatorIngress _operator = new(store, authorizeOperator);

    public ValueTask<Admission> SendAsync(BoundScope scope, Target target, string id, string kind, string? payload = null,
        AddOptions? options = null, CancellationToken cancellationToken = default) =>
        target is Target.Event events
            ? _event.SendAsync(scope, events, id, payload, options, cancellationToken)
            : SendToActor(scope, target, id, kind, payload, options, cancellationToken);

    public async ValueTask<Reply> RequestAsync(BoundScope scope, Target target, string id, string kind, string? payload,
        AddOptions? add, RequestOptions request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();
        if (target is Target.Event events)
            return await _event.RequestAsync(scope, events, id, payload, add, request, cancellationToken);
        if (add?.DueAt is not null) return new(null, null, Error: "requests cannot set a due time");
        if (request.Deadline <= store.FoundationClock()) return new(null, null, Error: "timeout");

        var open = requests.OpenBound(id, request.Deadline, scope.Tenant, request.Ambient);
        try
        {
            var replyTo = open.ReplyAddress(node, request.ReleaseBeforeCommit);
            var admission = request.Inline && target is Target.Start { Spec.Ephemeral: true } start && inlineOwner?.Node == node
                ? await _ordinary.SendAsync(scope, start, id, add, cancellationToken, replyTo, inlineOwner)
                : await SendToActor(scope, target, id, kind, payload, add, cancellationToken, replyTo);
            if (admission.Outcome == AdmissionOutcome.Refused)
                return new(null, null, Error: admission.Reason switch
                {
                    RefusalReason.Repeat => "repeat",
                    RefusalReason.Conflict => "conflict",
                    RefusalReason.StaleSelection => "stale selection",
                    RefusalReason.TargetMismatch => "target",
                    _ => "missing"
                });
            if (kind is Kinds.Discard or Kinds.Recover && admission.Outcome == AdmissionOutcome.Duplicate)
                return new(null, null, Error: "repeat");
            return await RequestReplies.WaitAsync(store, requests, open, scope, admission.Actor, cancellationToken);
        }
        finally
        {
            requests.Disconnect(open);
        }
    }

    public ValueTask SetStartAsync(BoundScope scope, string key, string wait, StartSpec? spec, CancellationToken cancellationToken = default) =>
        _event.SetStartAsync(scope, key, wait, spec, cancellationToken);

    internal ValueTask<Admission> AdmitRetentionDeleteAsync(BoundScope scope, ActorRef actor, string selection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();
        var receipt = store.AdmitRetentionDelete(scope.Tenant, actor.Id, selection);
        return new(OperatorIngress.AdmissionFor(receipt, missingIsTargetMismatch: true));
    }

    private ValueTask<Admission> SendToActor(BoundScope scope, Target target, string id, string kind, string? payload,
        AddOptions? options, CancellationToken cancellationToken, string? replyTo = null) =>
        Kinds.IsCommand(kind) && target is Target.Ref or Target.Key { Spec: null }
            ? _operator.SendAsync(scope, target, id, kind, payload, options, cancellationToken, replyTo)
            : target switch
        {
            Target.Ref exact => _ordinary.SendAsync(scope, exact, id, kind, payload, options, cancellationToken, replyTo),
            Target.Key key => _ordinary.SendAsync(scope, key, id, kind, payload, options, cancellationToken, replyTo),
            Target.Start start => _ordinary.SendAsync(scope, start, id, options, cancellationToken, replyTo),
            _ => throw new ArgumentException("The admission requires an actor target.", nameof(target))
        };
}
