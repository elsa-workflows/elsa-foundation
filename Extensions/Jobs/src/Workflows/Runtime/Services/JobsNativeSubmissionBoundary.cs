using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Activities;
using Elsa.Jobs.Workflows.Runtime.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Jobs.Workflows.Runtime.Services;

internal static class JobsNativeSubmissionBoundary
{
    internal const string ActivityType = "Elsa.Jobs.Workflows.Runtime.Activities.SubmitJob";
    internal const string MetadataKey = "jobs.native.submission.v1";
    internal const string StartKind = "Elsa.Jobs.Submit.v1";
    internal const string OwnershipKind = "Elsa.Jobs.SubmissionOwnership.v1";
    internal const string CancelKind = "Elsa.Jobs.SubmissionCancel.v1";
    internal const string OriginKind = "elsa.jobs.native-submission.v1";
    internal const string PendingAdmission = "PendingAdmission";
    internal const string CancellationMetadataKey = "jobs.native.submission-owner-cancel.v1";

    internal static string CancellationMarker(WorkflowExecutionState parent, ActivityExecutionState activity)
    {
        if (parent.Status != WorkflowExecutionStatus.Cancelled || parent.CompletedAt is null) throw JobsNativeBoundary.Invalid();
        return CancellationMetadataKey + ":" + JobsNativeBoundary.Hash(new { Scope = new JobScope(parent.Partition?.Value ??
            throw JobsNativeBoundary.Invalid(), parent.TenantId), parent.PinnedExecutable, parent.WorkflowExecutionId,
            ParentStatus = parent.Status, ParentCompletedAt = parent.CompletedAt,
            activity.InvocationId, activity.Execution.ExecutableNodeId, activity.ContractIdentity, activity.CompletedAt,
            Attempts = activity.Attempts?.OrderBy(attempt => attempt.Ordinal).ToArray() });
    }

    internal static void Validate(JobNativeSubmission value)
    {
        if (value is null || value.FormatVersion != 1 || value.Executable is null || value.Contract is null || value.Owner is null || value.HandlerPin is null ||
            value.Input is null || value.Input.Schema != value.HandlerPin.InputSchema || value.Contract.ActivityTypeKey != ActivityType ||
            value.HandlerPin.AssociationMode is not (JobAssociationMode.Independent or JobAssociationMode.Detached)) throw JobsNativeBoundary.Invalid();
        JobsNativeBoundary.ValidatePin(value.HandlerPin); JobsNativeBoundary.Scope(value.Scope);
        foreach (var field in new[] { value.Executable.ArtifactId, value.Executable.DefinitionId, value.Executable.DefinitionVersionId,
                     value.Executable.ArtifactVersion, value.WorkflowExecutionId, value.InvocationId, value.OperationKey,
                     value.InitialAttemptId, value.NodeId, value.Contract.ContractVersion, value.Contract.SchemaFingerprint,
                     value.Owner.ModelKind, value.Owner.DefinitionId, value.Owner.DefinitionVersion, value.Owner.ExecutionId,
                     value.Owner.InvocationId, value.Owner.MaterialId }) JobsNativeBoundary.Name(field);
        JobsNativeBoundary.Digest(value.Executable.ArtifactHash); JobsNativeBoundary.Digest(value.Owner.MaterialSha256);
        if (value.Owner.FormatVersion != 1 || value.Owner.Scope != value.Scope ||
            JobPayloadLimits.GetByteCount(value.Input) > JobPayloadLimits.MaximumBytes) throw JobsNativeBoundary.Invalid();
        if (value.Owner.Origin is { } origin)
        {
            if (origin.Scope.Value != value.Scope.PersistencePartition || origin.WorkerExecutionId != value.WorkflowExecutionId ||
                origin.ExecutionId != value.Owner.ExecutionId || origin.InvocationId != value.Owner.InvocationId ||
                !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(origin.WorkerExecutable, value.Executable) ||
                origin.Executable.ArtifactId != value.Owner.MaterialId || origin.Executable.ArtifactHash != value.Owner.MaterialSha256 ||
                value.Owner.ModelAddress is not { ValueKind: System.Text.Json.JsonValueKind.Object } model ||
                System.Runtime.InteropServices.JsonMarshal.GetRawUtf8Value(model).Length > 32_768) throw JobsNativeBoundary.Invalid();
        }
        else if (value.Owner.ModelKind != "elsa.native" || value.Owner.ExecutionId != value.WorkflowExecutionId ||
            value.Owner.InvocationId != value.InvocationId || value.Owner.DefinitionId != value.Executable.DefinitionId ||
            value.Owner.DefinitionVersion != value.Executable.DefinitionVersionId || value.Owner.MaterialId != value.Executable.ArtifactId ||
            value.Owner.MaterialSha256 != value.Executable.ArtifactHash || value.Owner.ModelAddress is not null) throw JobsNativeBoundary.Invalid();
    }

    internal static JobNativeSubmissionAdmission Admission(JobNativeSubmission submission, DateTimeOffset committedAt)
    {
        Validate(submission);
        var owner = submission.Owner; var pin = submission.HandlerPin;
        var association = new JobAssociation(owner.ModelKind, owner.DefinitionId, owner.DefinitionVersion, owner.ExecutionId,
            owner.InvocationId, owner.MaterialId, owner.MaterialSha256, pin.AssociationMode, null);
        var request = new JobRequest(pin.Handler, submission.Input, pin.ResultSchema, pin.Queue, pin.Policy, submission.NotBefore, association);
        var digest = JobRequestDigest.Compute(request);
        var originId = JobsNativeBoundary.Hash(new { Format = OriginKind, submission.Scope, owner.ModelKind, owner.ExecutionId,
            submission.WorkflowExecutionId, submission.OperationKey });
        return new(1, submission, request, digest, new(OriginKind, owner.ExecutionId, originId, digest),
            "job-submission-" + originId, "jobs-submission:" + originId, committedAt);
    }

    internal static SubmitJobResult Receipt(JobNativeSubmission submission)
    {
        var admission = Admission(submission, DateTimeOffset.UnixEpoch);
        return new(1, admission.ProposedJobId, PendingAdmission, JobsNativeBoundary.Write(submission));
    }

    internal static async ValueTask<(SubmitJobResult Receipt, JobNativeSubmissionAdmission Admission)> ReadCompletionAsync(
        ActivityExecutionState activity, IExternalPayloadStore? external, CancellationToken cancellationToken)
    {
        if (activity.Status != ActivityExecutionStatus.Completed || activity.Execution.ActivityType != ActivityType ||
            activity.ContractIdentity is not { ActivityTypeKey: ActivityType } ||
            activity.Completion is not { } completion || completion.InvocationId != activity.InvocationId ||
            completion.ContractFingerprint != activity.ContractIdentity.SchemaFingerprint || completion.OutcomeKey != "Done" ||
            completion.Result.Type != new ValueTypeDescriptor(typeof(SubmitJobResult).FullName!) || completion.Result.Presence != ValuePresence.Present ||
            activity.BookmarkIds.Count != 0 || activity.TriggerRegistrations?.Count > 0 || activity.TriggerDeliveries?.Count > 0 ||
            activity.PrivateState is not null) throw JobsNativeBoundary.Invalid();
        var json = completion.Result.InlineValue ?? (completion.Result.ExternalReference is { } reference && external is not null
            ? await external.ReadAsync(reference, cancellationToken) : throw JobsNativeBoundary.Invalid());
        var receipt = JobsNativeBoundary.Read<SubmitJobResult>(json);
        var submission = JobsNativeBoundary.Read<JobNativeSubmission>(receipt.Submission);
        var admission = Admission(submission, completion.CompletedAt);
        MatchActivity(activity, submission);
        if (!JobsNativeBoundary.Equivalent(receipt, Receipt(submission))) throw JobsNativeBoundary.Invalid();
        return (receipt, admission);
    }

    internal static JobNativeSubmissionAdmission ReadAdmission(ActivityExecutionState activity)
    {
        if (!activity.Metadata.TryGetValue(MetadataKey, out var json)) throw JobsNativeBoundary.Invalid();
        var admission = JobsNativeBoundary.ReadString<JobNativeSubmissionAdmission>(json);
        if (admission is null || admission.FormatVersion != 1 || admission.Submission is null ||
            !JobsNativeBoundary.Equivalent(admission, Admission(admission.Submission, admission.CommittedAt))) throw JobsNativeBoundary.Invalid();
        MatchActivity(activity, admission.Submission);
        return admission;
    }

    internal static void MatchActivity(ActivityExecutionState activity, JobNativeSubmission submission)
    {
        Validate(submission);
        if (activity.Execution.ActivityType != ActivityType || activity.Execution.WorkflowExecutionId != submission.WorkflowExecutionId ||
            activity.InvocationId != submission.InvocationId || activity.OperationKey != submission.OperationKey ||
            activity.Execution.ExecutableNodeId != submission.NodeId || !JobsNativeBoundary.Equivalent(activity.ContractIdentity, submission.Contract))
            throw JobsNativeBoundary.Invalid();
    }

    internal static void MatchParent(WorkflowExecutionState parent, JobNativeSubmission submission)
    {
        if (parent.WorkflowExecutionId != submission.WorkflowExecutionId || parent.Partition?.Value != submission.Scope.PersistencePartition ||
            parent.TenantId != submission.Scope.TenantId || !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(parent.PinnedExecutable, submission.Executable) ||
            (parent.ModelWorkOrigin is null) != (submission.Owner.Origin is null) ||
            parent.ModelWorkOrigin is { } origin && !origin.IsEquivalentTo(submission.Owner.Origin)) throw JobsNativeBoundary.Invalid();
    }

    internal static RuntimePostCommitIntent Intent(JobNativeSubmissionAdmission admission, string kind)
    {
        if (kind is not (StartKind or OwnershipKind or CancelKind) || kind is (OwnershipKind or CancelKind) &&
            admission.Submission.HandlerPin.AssociationMode != JobAssociationMode.Independent) throw JobsNativeBoundary.Invalid();
        var id = "jobs-submission:" + kind + ":" + admission.Origin.OriginId;
        return new(id, admission.Submission.WorkflowExecutionId, kind, admission.CommittedAt,
            admission.Submission.InvocationId, id, JobsNativeBoundary.Write(new JobNativeSubmissionIntent(1, admission)));
    }

    internal static void EnsureOrigin(JobNativeSubmissionAdmission admission, JobOriginSnapshot origin)
    {
        if (!JobsNativeBoundary.Equivalent(origin.Origin, admission.Origin) || origin.RequestSha256 != admission.RequestSha256 ||
            origin.Acceptance is null && !origin.Cancelled && !origin.Retired ||
            origin.Acceptance is { } accepted && accepted.JobId != admission.ProposedJobId) throw JobsNativeBoundary.Invalid();
    }
}
