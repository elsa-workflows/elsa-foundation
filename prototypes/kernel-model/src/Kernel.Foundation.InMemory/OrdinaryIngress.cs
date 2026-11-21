using Kernel.Foundation.Contracts;
using Durable = Kernel.Durable;

namespace Kernel.Foundation.InMemory;

/// <summary>The bound one-way routes that target one exact actor life.</summary>
internal sealed class OrdinaryIngress(Durable.InMemoryStore store)
{
    public ValueTask<Admission> SendAsync(BoundScope scope, Target.Start target, string id, AddOptions? options = null,
        CancellationToken cancellationToken = default, string? replyTo = null)
    {
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Durable.InMemoryStore.IsOutsideMessageId(id) || !HeaderLimits.FitsTypeData(target.Spec.TypeData)) return new(new Admission(AdmissionOutcome.Refused));

        var receipt = store.AdmitGeneratedFoundationStart(scope.Tenant, id, Start(target.Spec), options?.DueAt, options?.KeepFor, replyTo);
        var outcome = receipt.Duplicate ? AdmissionOutcome.Duplicate : AdmissionOutcome.Accepted;
        return new(new Admission(outcome, new ActorRef(receipt.Reference)));
    }

    public ValueTask<Admission> SendAsync(BoundScope scope, Target.Ref target, string id, string kind, string? payload = null,
        AddOptions? options = null, CancellationToken cancellationToken = default, string? replyTo = null)
    {
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Durable.InMemoryStore.IsOutsideMessageId(id) || !IsOrdinary(kind))
            return new(new Admission(AdmissionOutcome.Refused));

        var added = store.AddFoundationMessage(scope.Tenant, target.Actor.Id, id, kind, payload, options?.DueAt, options?.KeepFor,
            replyTo: replyTo);
        return new(added switch
        {
            true => new Admission(AdmissionOutcome.Accepted, target.Actor),
            false => new Admission(AdmissionOutcome.Duplicate, target.Actor),
            null => new Admission(AdmissionOutcome.Refused)
        });
    }

    public ValueTask<Admission> SendAsync(BoundScope scope, Target.Key target, string id, string kind, string? payload = null,
        AddOptions? options = null, CancellationToken cancellationToken = default, string? replyTo = null)
    {
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();
        if (target.Spec is { } start ? !HeaderLimits.FitsTypeData(start.TypeData) : !IsOrdinary(kind)) return new(new Admission(AdmissionOutcome.Refused));

        var receipt = store.AdmitFoundationKey(scope.Tenant, target.CallerKey, id, kind, payload,
            target.Spec is { } spec ? Start(spec) : null, options?.DueAt, options?.KeepFor, replyTo);
        return new(receipt.Outcome switch
        {
            Durable.InMemoryStore.KeyAdmissionOutcome.Accepted => new Admission(AdmissionOutcome.Accepted, new ActorRef(receipt.Reference!)),
            Durable.InMemoryStore.KeyAdmissionOutcome.Duplicate => new Admission(AdmissionOutcome.Duplicate, new ActorRef(receipt.Reference!)),
            Durable.InMemoryStore.KeyAdmissionOutcome.Repeat => new Admission(AdmissionOutcome.Refused, Reason: RefusalReason.Repeat),
            _ => new Admission(AdmissionOutcome.Refused)
        });
    }

    private static bool IsOrdinary(string kind) => !kind.StartsWith("kernel.", StringComparison.Ordinal) &&
        kind is not (Kinds.Start or Kinds.JobFinished or Kinds.Event or Kinds.ChildFinished);

    private static Durable.StartSpec Start(StartSpec spec) =>
        new(spec.Type, spec.TypeData, spec.Input, Ephemeral: spec.Ephemeral, History: (Durable.History)spec.History);
}
