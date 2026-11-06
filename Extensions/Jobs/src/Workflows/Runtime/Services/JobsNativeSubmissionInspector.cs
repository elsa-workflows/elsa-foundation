using Elsa.Activities.Runtime.Core.Models;
using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Contracts;
using Elsa.Jobs.Workflows.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using ActivityTransitionKind = Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind;

namespace Elsa.Jobs.Workflows.Runtime.Services;

internal sealed class JobsNativeSubmissionInspector(IJobQueries jobs, JobsNativeDeliveryInspector roots,
    IPersistenceOperationScopeFactory scopes, IEnumerable<IJobWorkflowAssociationSource> associations, IExternalPayloadStore? external = null)
    : IJobNativeSubmissionInspector
{
    public async ValueTask<JobNativeSubmissionEvidence?> InspectSubmissionWorkerAsync(WorkflowExecutionState worker, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = await scopes.CreateAsync(new(worker.Partition?.Value ?? throw JobsNativeBoundary.Invalid()), cancellationToken);
            return await scope.ServiceProvider.GetRequiredService<JobsNativeSubmissionInspector>().InspectInScopeAsync(worker, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (JobException) { throw; }
        catch (Exception error) { throw Unavailable(error); }
    }

    internal async ValueTask<JobNativeSubmissionEvidence?> InspectInScopeAsync(WorkflowExecutionState worker, CancellationToken cancellationToken)
    {
        var root = await roots.FindRootInScopeAsync(worker, cancellationToken, JobsNativeSubmissionBoundary.ActivityType);
        if (root is null) return null;
        if (!root.Metadata.ContainsKey(JobsNativeSubmissionBoundary.MetadataKey))
        {
            var rejected = PreAdmissionFailure(worker, root);
            var cancelled = PreAdmissionCancellation(worker, root);
            if (!rejected && !cancelled) throw Unavailable();
            var owner = await new JobsNativeOwnerResolver(associations).ResolveAsync(worker, root, cancellationToken);
            var final = root.Attempts?.OrderBy(attempt => attempt.Ordinal).LastOrDefault();
            return new(owner, worker.PinnedExecutable, worker.WorkflowExecutionId, root.InvocationId,
                root.Execution.ExecutableNodeId, root.ContractIdentity!, final?.AttemptId, root.Status, null,
                rejected ? JobNativeSubmissionDisposition.RejectedBeforeAdmission : JobNativeSubmissionDisposition.CancelledBeforeAdmission,
                null, null, null, null)
                { NativeCancellationReceiptId = cancelled ? root.Metadata[JobsNativeSubmissionBoundary.CancellationMetadataKey] : null };
        }
        var admission = JobsNativeSubmissionBoundary.ReadAdmission(root);
        JobsNativeSubmissionBoundary.MatchParent(worker, admission.Submission);
        var (receipt, completed) = await JobsNativeSubmissionBoundary.ReadCompletionAsync(root, external, cancellationToken);
        if (root.Status != ActivityExecutionStatus.Completed || !JobsNativeBoundary.Equivalent(admission, completed)) throw JobsNativeBoundary.Invalid();
        var finalAttempt = FinalAttempt(root, ActivityTransitionKind.Complete);
        if (root.Completion!.AttemptId != finalAttempt.AttemptId || admission.Submission.InitialAttemptId != finalAttempt.AttemptId ||
            root.Completion.CompletedAt < finalAttempt.StartedAt || root.Completion.CompletedAt > finalAttempt.EndedAt)
            throw JobsNativeBoundary.Invalid();
        var origin = await jobs.FindOriginAsync(admission.Submission.Scope, admission.Origin, cancellationToken) ?? throw Unavailable();
        JobsNativeSubmissionBoundary.EnsureOrigin(admission, origin);
        JobNativeSubmissionDisposition disposition;
        if (origin.Acceptance is { } accepted)
        {
            var actual = await jobs.FindAsync(admission.Submission.Scope, accepted.JobId, cancellationToken) ?? throw Unavailable();
            if (actual.Scope != admission.Submission.Scope || actual.JobId != admission.ProposedJobId || actual.UnavailableDiagnostic is not null ||
                actual.RequestSha256 != admission.RequestSha256 || JobsNativeBoundary.RetainedRequest(actual) is not { Association.Continuation: null } metadata ||
                !JobsNativeBoundary.Equivalent(metadata, JobRequestMetadata.From(admission.Request))) throw JobsNativeBoundary.Invalid();
            disposition = JobNativeSubmissionDisposition.Accepted;
        }
        else if (origin.Cancelled && !origin.Retired) disposition = JobNativeSubmissionDisposition.CancelledBeforeAdmission;
        else throw Unavailable();
        return new(admission.Submission.Owner, admission.Submission.Executable, admission.Submission.WorkflowExecutionId,
            admission.Submission.InvocationId, admission.Submission.NodeId, admission.Submission.Contract, finalAttempt.AttemptId,
            root.Status, receipt, disposition, admission.ProposedJobId, admission.RequestSha256, admission.Origin, origin.Acceptance);
    }

    internal static bool PreAdmissionFailure(WorkflowExecutionState parent, ActivityExecutionState activity)
    {
        if (parent.Status != WorkflowExecutionStatus.Faulted || activity.Status != ActivityExecutionStatus.Faulted ||
            !EmptyBeforeAdmission(activity) || activity.Fault is not { } fault ||
            fault.Code != JobsNativeBoundary.PreAdmissionFaultCode || fault.ExceptionType != typeof(ActivityFault).FullName) return false;
        try
        {
            var final = FinalAttempt(activity, ActivityTransitionKind.Fault);
            return parent.CompletedAt is { } ended && activity.CompletedAt <= ended &&
                fault.Metadata.GetValueOrDefault(ActivityFault.FaultTypeMetadataKey) == JobsNativeBoundary.PreAdmissionMarker(parent, activity, final.AttemptId);
        }
        catch (JobException) { return false; }
    }

    internal static bool PreAdmissionCancellation(WorkflowExecutionState parent, ActivityExecutionState activity)
    {
        if (parent.Status != WorkflowExecutionStatus.Cancelled || activity.Status != ActivityExecutionStatus.Cancelled ||
            !EmptyBeforeAdmission(activity) || !activity.Metadata.TryGetValue(JobsNativeSubmissionBoundary.CancellationMetadataKey, out var marker)) return false;
        try
        {
            if (activity.Attempts is { Count: > 0 }) _ = FinalAttempt(activity, ActivityTransitionKind.Cancel);
            return marker == JobsNativeSubmissionBoundary.CancellationMarker(parent, activity);
        }
        catch (JobException) { return false; }
    }

    private static bool EmptyBeforeAdmission(ActivityExecutionState activity) =>
        activity.Execution.ActivityType == JobsNativeSubmissionBoundary.ActivityType && activity.PrivateState is null && activity.Completion is null &&
        !activity.Metadata.ContainsKey(JobsNativeSubmissionBoundary.MetadataKey) &&
        activity.ContractIdentity is { ActivityTypeKey: JobsNativeSubmissionBoundary.ActivityType } &&
        activity.BookmarkIds.Count == 0 && activity.TriggerRegistrations?.Count is not > 0 && activity.TriggerDeliveries?.Count is not > 0;

    private static ActivityAttempt FinalAttempt(ActivityExecutionState activity, ActivityTransitionKind kind)
    {
        if (activity.Attempts is not { Count: > 0 and <= 32 } attempts) throw JobsNativeBoundary.Invalid();
        var ordered = attempts.OrderBy(attempt => attempt.Ordinal).ToArray();
        if (ordered.Select(attempt => attempt.Ordinal).Distinct().Count() != ordered.Length ||
            ordered.Select(attempt => attempt.AttemptId).Distinct(StringComparer.Ordinal).Count() != ordered.Length ||
            ordered.Any(attempt => attempt.InvocationId != activity.InvocationId || attempt.Reason == ActivityAttemptReason.Resume ||
                attempt.TriggerDeliveryId is not null || attempt.EndedAt is null) ||
            ordered.Take(ordered.Length - 1).Any(attempt => attempt.TransitionKind != ActivityTransitionKind.Fault)) throw JobsNativeBoundary.Invalid();
        for (var i = 1; i < ordered.Length; i++) if (ordered[i - 1].EndedAt > ordered[i].StartedAt) throw JobsNativeBoundary.Invalid();
        var final = ordered[^1];
        if (final.TransitionKind != kind || final.EndedAt != activity.CompletedAt) throw JobsNativeBoundary.Invalid();
        return final;
    }

    private static JobException Unavailable(Exception? inner = null) => inner is null
        ? new("jobs.native.submission-unavailable", "The exact native Jobs submission acceptance remains unconfirmed.")
        : new("jobs.native.submission-unavailable", "The exact native Jobs submission acceptance remains unconfirmed.", inner);
}
