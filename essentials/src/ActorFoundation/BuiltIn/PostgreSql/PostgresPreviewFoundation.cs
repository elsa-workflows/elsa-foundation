using Kernel.Durable;
using Kernel.EntityFramework;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Foundation.Postgres;

/// <summary>
/// The Foundation's ingress and reads on a PostgreSQL Core Preview store: durable starts of the configured actor type,
/// ordinary sends to exact root references, and the reads of those roots. With <see cref="PostgresDirectRequests"/>, on a
/// store with held replies, a request starts a root or sends to one and holds its caller until the owner answers.
/// </summary>
public sealed class PostgresPreviewFoundation : Contract.IIngress, Contract.IActorReads
{
    private readonly PostgresRootAdmission admission;
    private readonly PostgresRootReads reads;
    private readonly Func<Contract.BoundScope, string, bool>? authorizeOperator;
    private readonly PostgresDirectRequests? directRequests;
    private readonly PostgresRootIngress ingress;

    /// <summary>The held requests of this Foundation, and the transport its owner answers through: null without held replies.</summary>
    public PostgresDirectRequests? Requests => directRequests;

    internal PostgresPreviewFoundation(PostgresRootAdmission admission, PostgresRootReads reads, string actorType,
        Func<Contract.BoundScope, string, bool>? authorizeOperator = null, PostgresDirectRequests? directRequests = null)
    {
        (this.admission, this.reads, this.authorizeOperator, this.directRequests) = (admission, reads, authorizeOperator, directRequests);
        ingress = new(admission, actorType);
    }

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
        directRequests is not null && target is Contract.Target.Ref or Contract.Target.Start
            ? directRequests.RequestAsync(scope, target, id, kind, payload, add, request, cancellationToken)
            : throw new NotSupportedException("This preview holds requests only with a reply-capable store and its direct requests.");

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
