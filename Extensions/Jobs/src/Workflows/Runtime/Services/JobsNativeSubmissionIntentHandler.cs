using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Contracts;
using Elsa.Jobs.Workflows.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Jobs.Workflows.Runtime.Services;

internal sealed class JobsNativeSubmissionIntentHandler(IJobCommands jobs, IJobQueries queries,
    IWorkflowExecutionStateStore workflows, IActivityExecutionStateStore activities, IPersistenceAccessContextAccessor access,
    IEnumerable<IJobWorkflowAssociationSource> associations, JobsNativeSourceVerifier verifier, IExternalPayloadStore? external = null)
    : IRuntimePostCommitDurableSourceIntentHandler
{
    internal static JobNativeSubmissionAdmission Read(RuntimePostCommitIntent intent)
    {
        if (intent.Payload is not { } payload || intent.Kind is not (JobsNativeSubmissionBoundary.StartKind or
                JobsNativeSubmissionBoundary.OwnershipKind or JobsNativeSubmissionBoundary.CancelKind)) throw JobsNativeBoundary.Invalid();
        var value = JobsNativeBoundary.Read<JobNativeSubmissionIntent>(payload);
        if (value.FormatVersion != 1 || value.Admission is not { } admission || admission.FormatVersion != 1 ||
            !JobsNativeBoundary.Equivalent(admission, JobsNativeSubmissionBoundary.Admission(admission.Submission, admission.CommittedAt)) ||
            !intent.IsEquivalentTo(JobsNativeSubmissionBoundary.Intent(admission, intent.Kind))) throw JobsNativeBoundary.Invalid();
        return admission;
    }

    public async ValueTask HandleAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default)
    {
        await verifier.VerifyAsync(source, cancellationToken);
        await HandleIntentAsync(source.Intent, cancellationToken);
    }

    internal async ValueTask HandleIntentAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
    {
        try
        {
            var admission = Read(intent); var submission = admission.Submission;
            access.Current.EnsureScope(new(submission.Scope.PersistencePartition));
            if (intent.Kind == JobsNativeSubmissionBoundary.CancelKind)
            {
                await CancelAsync(admission, cancellationToken); return;
            }
            var origin = await queries.FindOriginAsync(submission.Scope, admission.Origin, cancellationToken);
            if (origin is not null) JobsNativeSubmissionBoundary.EnsureOrigin(admission, origin);
            if (intent.Kind == JobsNativeSubmissionBoundary.StartKind && (origin is { Acceptance: not null } or { Cancelled: true, Retired: false })) return;
            var parent = await workflows.FindAsync(submission.WorkflowExecutionId, cancellationToken) ?? throw Unavailable();
            JobsNativeSubmissionBoundary.MatchParent(parent, submission);
            var activity = await activities.FindAsync(submission.WorkflowExecutionId, submission.InvocationId, cancellationToken) ?? throw Unavailable();
            if (!JobsNativeBoundary.Equivalent(JobsNativeSubmissionBoundary.ReadAdmission(activity), admission)) throw JobsNativeBoundary.Invalid();
            var (_, completed) = await JobsNativeSubmissionBoundary.ReadCompletionAsync(activity, external, cancellationToken);
            if (!JobsNativeBoundary.Equivalent(completed, admission)) throw JobsNativeBoundary.Invalid();
            if (submission.HandlerPin.AssociationMode == JobAssociationMode.Independent)
            {
                var lifetime = await LifetimeAsync(parent, submission.Owner, cancellationToken);
                if (lifetime.Status == JobWorkflowSubmissionLifetimeStatus.Cancelled)
                {
                    await CancelAsync(admission, cancellationToken); return;
                }
                if (intent.Kind == JobsNativeSubmissionBoundary.OwnershipKind)
                {
                    if (lifetime.Status == JobWorkflowSubmissionLifetimeStatus.Active ||
                        origin is not ({ Acceptance: not null } or { Cancelled: true, Retired: false })) throw Unavailable();
                    return; // Normal completion/fault ends inherited cancellation, never the accepted job.
                }
            }
            var result = await jobs.AdmitAsync(new(submission.Scope, admission.ProposedJobId, admission.ProducerKey,
                admission.RequestSha256, admission.Request, admission.Origin), cancellationToken);
            if (result.Status is JobCommandStatus.Applied or JobCommandStatus.Replayed)
            {
                if (result.Acceptance?.JobId != admission.ProposedJobId) throw JobsNativeBoundary.Invalid();
                return;
            }
            if (result.Status == JobCommandStatus.OriginCancelled) return;
            if (result.Status is JobCommandStatus.Conflict or JobCommandStatus.OriginRetired) throw JobsNativeBoundary.Invalid();
            throw Unavailable();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (RuntimePostCommitDeliveryException) { throw; }
        catch (Exception error)
        {
            // A conflicting canonical ID is never relabelled as acceptance. The required source
            // remains owed and retains its exact handoff for operator reconciliation.
            throw new RuntimePostCommitDeliveryException(PostCommitFailureKind.Transient, "jobs.native.submission-unavailable",
                "The committed Jobs submission or owner responsibility remains owed.", error);
        }
    }

    private async ValueTask<JobWorkflowSubmissionLifetime> LifetimeAsync(WorkflowExecutionState parent, JobWorkflowOwner owner, CancellationToken cancellationToken)
    {
        if (owner.Origin is { } origin)
        {
            if (new JobsNativeOwnerResolver(associations).Select(origin) is not IJobWorkflowSubmissionLifetimeSource source) throw Unavailable();
            var observed = await source.InspectSubmissionLifetimeAsync(owner, cancellationToken);
            if (!Enum.IsDefined(observed.Status) || observed.Status != JobWorkflowSubmissionLifetimeStatus.Active &&
                string.IsNullOrWhiteSpace(observed.ModelReceiptId)) throw JobsNativeBoundary.Invalid();
            return observed;
        }
        if (!Enum.IsDefined(parent.Status)) throw JobsNativeBoundary.Invalid();
        if (!parent.Status.IsTerminal()) return new(JobWorkflowSubmissionLifetimeStatus.Active, null);
        if (parent.CompletedAt is not { } ended) throw JobsNativeBoundary.Invalid();
        var status = parent.Status switch
        {
            WorkflowExecutionStatus.Completed => JobWorkflowSubmissionLifetimeStatus.Completed,
            WorkflowExecutionStatus.Faulted => JobWorkflowSubmissionLifetimeStatus.Faulted,
            WorkflowExecutionStatus.Cancelled => JobWorkflowSubmissionLifetimeStatus.Cancelled,
            _ => throw JobsNativeBoundary.Invalid()
        };
        return new(status, "jobs-submission-owner:" + JobsNativeBoundary.Hash(new { owner.Scope, parent.WorkflowExecutionId,
            parent.PinnedExecutable, parent.Status, Ended = ended }));
    }

    private async ValueTask CancelAsync(JobNativeSubmissionAdmission admission, CancellationToken cancellationToken)
    {
        if (admission.Submission.HandlerPin.AssociationMode != JobAssociationMode.Independent) throw JobsNativeBoundary.Invalid();
        var operation = new JobOperation("jobs-submission-cancel:" + admission.Origin.OriginId, "jobs.native",
            "The exact owning model explicitly cancelled its independent Jobs submission.");
        var result = await jobs.CancelOriginAsync(new(admission.Submission.Scope, admission.Origin, admission.RequestSha256, operation), cancellationToken);
        if (result.Status is JobCommandStatus.Applied or JobCommandStatus.Replayed or JobCommandStatus.OriginCancelled or JobCommandStatus.Terminal) return;
        throw Unavailable();
    }

    private static RuntimeSchedulerWorkUnavailableException Unavailable() => new("jobs.native.submission-unavailable",
        "The exact native Jobs submission or active owner responsibility remains owed.");
}
