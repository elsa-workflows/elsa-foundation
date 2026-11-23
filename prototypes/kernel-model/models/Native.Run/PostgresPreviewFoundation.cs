using Kernel.EntityFramework;
using Kernel.Foundation.InMemory;
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
    public async ValueTask<Contract.Admission> SendAsync(Contract.BoundScope scope, Contract.Target target, string id, string kind,
        string? payload = null, Contract.AddOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(kind);
        if (kind is Contract.Kinds.Suspend or Contract.Kinds.Resume)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (target is not Contract.Target.Ref exact || authorizeOperator?.Invoke(scope, kind) != true ||
                payload is not null || !FoundationMetadataLimits.IsOutsideMessageId(id) ||
                !PostgresRootAdmission.IsIssuedReferenceShape(exact.Actor.Id)) return new(Contract.AdmissionOutcome.Refused);
            var control = await admission.AdmitRootOperatorAsync(scope.Tenant, exact.Actor.Id, id, kind, options?.DueAt, options?.KeepFor, cancellationToken);
            return control is null ? new(Contract.AdmissionOutcome.Refused) :
                new(control.Duplicate ? Contract.AdmissionOutcome.Duplicate : Contract.AdmissionOutcome.Accepted, new Contract.ActorRef(control.Reference));
        }
        if (target is Contract.Target.Start start)
        {
            var spec = start.Spec;
            if (kind != Contract.Kinds.Start || payload is not null || spec.Type != actorType || spec.Ephemeral ||
                !Enum.IsDefined(spec.History))
                throw new NotSupportedException("The preview starts durable roots of its configured actor type.");
            if (!FoundationMetadataLimits.IsOutsideMessageId(id))
                return new(Contract.AdmissionOutcome.Refused);
            try
            {
                var accepted = await admission.AdmitAsync(scope.Tenant, id,
                    new(spec.Type, spec.TypeData, spec.Input, History: (History)spec.History),
                    options?.DueAt, options?.KeepFor, cancellationToken);
                return new(accepted.Duplicate ? Contract.AdmissionOutcome.Duplicate : Contract.AdmissionOutcome.Accepted,
                    new Contract.ActorRef(accepted.Reference));
            }
            catch (MetadataLimitExceeded)
            {
                return new(Contract.AdmissionOutcome.Refused);
            }
        }
        if (target is not Contract.Target.Ref direct || FoundationMetadataLimits.IsReservedKind(kind))
            throw new NotSupportedException("The preview supports ordinary input addressed to a root reference.");
        var admitted = await admission.AdmitDirectResultAsync(scope.Tenant, direct.Actor.Id, id, kind, payload,
            options?.DueAt, options?.KeepFor, cancellationToken);
        return admitted switch
        {
            PostgresRootAdmission.DirectResult.Accepted => new(Contract.AdmissionOutcome.Accepted, direct.Actor),
            PostgresRootAdmission.DirectResult.Duplicate => new(Contract.AdmissionOutcome.Duplicate, direct.Actor),
            PostgresRootAdmission.DirectResult.MissingTarget => new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.TargetMismatch),
            _ => new(Contract.AdmissionOutcome.Refused)
        };
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

    internal ValueTask<Contract.Page<WorkflowRunView>> ListRunsAsync(Contract.BoundScope scope, WorkflowRunQuery query,
        Contract.PageToken? from = null, CancellationToken cancellationToken = default) =>
        reads.ListRunsAsync(scope, actorType, query, from, cancellationToken);

    public ValueTask<Contract.Page<Contract.DeliveryView>> InboxAsync(Contract.BoundScope scope, Contract.ActorRef actor,
        Contract.PageToken? from = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Inbox inspection is outside this preview.");
}
