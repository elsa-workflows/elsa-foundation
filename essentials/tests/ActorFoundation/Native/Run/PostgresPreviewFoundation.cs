using Kernel.EntityFramework;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

/// <summary>Public Foundation surfaces for the opt-in PostgreSQL direct-input preview.</summary>
internal sealed class PostgresPreviewFoundation(
    PostgresRootAdmission admission,
    PostgresRootReads reads,
    string actorType,
    Func<Contract.BoundScope, string, bool>? authorizeOperator = null,
    PostgresDirectRequests? directRequests = null) : Contract.IIngress, Contract.IActorReads
{
    private readonly PostgresRootIngress ingress = new(admission, actorType);

    public async ValueTask<Contract.Admission> SendAsync(Contract.BoundScope scope, Contract.Target target, string id, string kind,
        string? payload = null, Contract.AddOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(kind);
        if (kind is Contract.Kinds.Suspend or Contract.Kinds.Resume or Contract.Kinds.Terminate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (target is not Contract.Target.Ref exact || authorizeOperator?.Invoke(scope, kind) != true ||
                payload is not null || !FoundationMetadataLimits.IsOutsideMessageId(id) ||
                !PostgresRootAdmission.IsIssuedReferenceShape(exact.Actor.Id)) return new(Contract.AdmissionOutcome.Refused);
            var control = await admission.AdmitRootOperatorAsync(scope.Tenant, exact.Actor.Id, id, kind, options?.DueAt, options?.KeepFor, cancellationToken);
            return control is null ? new(Contract.AdmissionOutcome.Refused) :
                new(control.Duplicate ? Contract.AdmissionOutcome.Duplicate : Contract.AdmissionOutcome.Accepted, new Contract.ActorRef(control.Reference));
        }
        return await ingress.SendAsync(scope, target, id, kind, payload, options, cancellationToken);
    }

    public ValueTask<Contract.ActorView?> GetAsync(Contract.BoundScope scope, Contract.ActorRef actor,
        CancellationToken cancellationToken = default) => reads.GetAsync(scope, actor, cancellationToken);

    public ValueTask<Contract.Page<Contract.JournalEntry>> TailAsync(Contract.BoundScope scope, Contract.ActorRef actor,
        Contract.PageToken? from = null, CancellationToken cancellationToken = default) =>
        reads.TailAsync(scope, actor, from, cancellationToken);

    public ValueTask<Contract.Reply> RequestAsync(Contract.BoundScope scope, Contract.Target target, string id, string kind,
        string? payload, Contract.AddOptions? add, Contract.RequestOptions request, CancellationToken cancellationToken = default) =>
        directRequests is not null && target is Contract.Target.Ref exact
            ? directRequests.RequestAsync(scope, exact.Actor, id, kind, payload, add, request, cancellationToken)
            : throw new NotSupportedException("This preview supports held direct requests only when the reply profile is enabled.");

    public ValueTask SetStartAsync(Contract.BoundScope scope, string key, string wait, Contract.StartSpec? spec,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Event starts are outside this preview.");

    public ValueTask<Contract.Page<Contract.ActorView>> ListAsync(Contract.BoundScope scope, Contract.ActorQuery query,
        Contract.PageToken? from = null, CancellationToken cancellationToken = default) =>
        reads.ListAsync(scope, query, from, cancellationToken);

    public ValueTask<Contract.Page<Contract.DeliveryView>> InboxAsync(Contract.BoundScope scope, Contract.ActorRef actor,
        Contract.PageToken? from = null, CancellationToken cancellationToken = default) =>
        reads.InboxAsync(scope, actor, from, cancellationToken);
}
