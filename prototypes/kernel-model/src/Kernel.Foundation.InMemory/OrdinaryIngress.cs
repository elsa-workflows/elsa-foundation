using Kernel.Foundation.Contracts;
using Durable = Kernel.Durable;

namespace Kernel.Foundation.InMemory;

/// <summary>The bound one-way routes for a generated start and an exact-reference send.</summary>
internal sealed class OrdinaryIngress(Durable.InMemoryStore store)
{
    public ValueTask<Admission> SendAsync(BoundScope scope, Target.Start target, string id, AddOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();
        if (id.StartsWith('~')) return new(new Admission(AdmissionOutcome.Refused));

        var spec = target.Spec;
        var start = new Durable.StartSpec(spec.Type, spec.TypeData, spec.Input, Ephemeral: spec.Ephemeral, History: (Durable.History)spec.History);
        var receipt = store.AdmitGeneratedFoundationStart(scope.Tenant, id, start, options?.DueAt, options?.KeepFor);
        var outcome = receipt.Duplicate ? AdmissionOutcome.Duplicate : AdmissionOutcome.Accepted;
        return new(new Admission(outcome, new ActorRef(receipt.Reference)));
    }

    public ValueTask<Admission> SendAsync(BoundScope scope, Target.Ref target, string id, string kind, string? payload = null,
        AddOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();
        if (id.StartsWith('~') || kind.StartsWith("kernel.", StringComparison.Ordinal) ||
            kind is Kinds.Start or Kinds.JobFinished or Kinds.Event or Kinds.ChildFinished)
            return new(new Admission(AdmissionOutcome.Refused));

        var added = store.AddFoundationMessage(scope.Tenant, target.Actor.Id, id, kind, payload, options?.DueAt, options?.KeepFor);
        return new(added switch
        {
            true => new Admission(AdmissionOutcome.Accepted, target.Actor),
            false => new Admission(AdmissionOutcome.Duplicate, target.Actor),
            null => new Admission(AdmissionOutcome.Refused)
        });
    }
}
