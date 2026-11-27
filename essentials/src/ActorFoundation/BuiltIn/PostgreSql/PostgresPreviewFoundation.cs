using Kernel.Durable;
using Kernel.EntityFramework;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Foundation.Postgres;

/// <summary>
/// The Foundation's ingress and reads on a PostgreSQL Core Preview store: durable starts of the configured actor type,
/// ordinary sends to exact root references, and the reads of those roots. With <see cref="PostgresDirectRequests"/>, on a
/// store with held replies, a request starts a root or sends to one and holds its caller until the owner answers.
/// </summary>
public sealed class PostgresPreviewFoundation : Contract.IIngress, Contract.IActorReads, Contract.IActorTerminalOutputReads,
    Contract.IActorSequencedJournalReads
{
    private readonly PostgresRootAdmission admission;
    private readonly PostgresRootReads reads;
    private readonly Func<Contract.BoundScope, string, bool>? authorizeOperator;
    private readonly Func<Contract.BoundScope, Contract.ActorRef, string, bool>? authorizeExactOperator;
    private readonly PostgresDirectRequests? directRequests;
    private readonly PostgresRootIngress ingress;

    /// <summary>The held requests of this Foundation, and the transport its owner answers through: null without held replies.</summary>
    public PostgresDirectRequests? Requests => directRequests;

    internal PostgresPreviewFoundation(PostgresRootAdmission admission, PostgresRootReads reads, string actorType,
        Func<Contract.BoundScope, string, bool>? authorizeOperator = null, PostgresDirectRequests? directRequests = null,
        Func<Contract.BoundScope, Contract.ActorRef, string, bool>? authorizeExactOperator = null)
    {
        (this.admission, this.reads, this.authorizeOperator, this.directRequests) = (admission, reads, authorizeOperator, directRequests);
        this.authorizeExactOperator = authorizeExactOperator;
        ingress = new(admission, actorType,
            (scope, actor) => authorizeExactOperator?.Invoke(scope, actor, Contract.Kinds.Terminate) == true,
            (scope, actor) => authorizeExactOperator?.Invoke(scope, actor, Contract.Kinds.Delete) == true,
            authorizeRecover: (scope, actor) => authorizeExactOperator?.Invoke(scope, actor, Contract.Kinds.Recover) == true);
    }

    public async ValueTask<Contract.Admission> SendAsync(Contract.BoundScope scope, Contract.Target target, string id, string kind,
        string? payload = null, Contract.AddOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(kind);
        if (kind is Contract.Kinds.Suspend or Contract.Kinds.Resume || kind == Contract.Kinds.Terminate && payload is null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (target is not Contract.Target.Ref exact || payload is not null || !FoundationMetadataLimits.IsOutsideMessageId(id) ||
                !PostgresRootAdmission.IsIssuedReferenceShape(exact.Actor.Id) ||
                (authorizeExactOperator?.Invoke(scope, exact.Actor, kind) ?? authorizeOperator?.Invoke(scope, kind)) != true)
                return new(Contract.AdmissionOutcome.Refused);
            try
            {
                var control = await admission.AdmitRootOperatorAsync(scope.Tenant, exact.Actor.Id, id, kind, options?.DueAt,
                    options?.KeepFor, cancellationToken, requireExactTarget: authorizeExactOperator is not null);
                return control is null ? new(Contract.AdmissionOutcome.Refused) :
                    new(control.Duplicate ? Contract.AdmissionOutcome.Duplicate : Contract.AdmissionOutcome.Accepted, new Contract.ActorRef(control.Reference));
            }
            catch (RootAdmissionRefused refusal) { return refusal.Admission; }
        }
        return await ingress.SendAsync(scope, target, id, kind, payload, options, cancellationToken);
    }

    ValueTask<Contract.ActorTerminalOutput?> Contract.IActorTerminalOutputReads.ReadTerminalOutputAsync(Contract.BoundScope scope, Contract.ActorRef actor,
        long endedAt, bool includeValues, CancellationToken cancellationToken) =>
        reads.ReadTerminalOutputAsync(scope, actor, endedAt, includeValues, cancellationToken);

    bool Contract.IActorSequencedJournalReads.SupportsSequencedJournal => reads.SupportsSequencedJournal;

    ValueTask<Contract.ActorSequencedJournalPage?> Contract.IActorSequencedJournalReads.ReadSequencedJournalAsync(
        Contract.BoundScope scope, Contract.ActorRef actor, long afterSequence, long? upperSequence, int limit,
        CancellationToken cancellationToken) => reads.ReadSequencedJournalAsync(scope, actor, afterSequence, upperSequence, limit, cancellationToken);

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
