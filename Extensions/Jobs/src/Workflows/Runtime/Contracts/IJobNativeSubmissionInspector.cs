using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Jobs.Workflows.Runtime.Contracts;

/// <summary>Proves the exact submission worker completion and canonical protected Jobs origin before an owning model advances.</summary>
public interface IJobNativeSubmissionInspector
{
    /// <summary>Returns null only for a worker whose root is not SubmitJob. Pending, missing or conflicting owned evidence remains unavailable.</summary>
    ValueTask<JobNativeSubmissionEvidence?> InspectSubmissionWorkerAsync(WorkflowExecutionState worker, CancellationToken cancellationToken = default);
}

/// <summary>A checked native completion and its actual canonical admission, or a positive never-admitted cancellation/fault.</summary>
public sealed record JobNativeSubmissionEvidence(JobWorkflowOwner Owner, WorkflowExecutableIdentity WorkerExecutable,
    string WorkerExecutionId, string InvocationId, string NodeId, ActivityInvocationContractIdentity Contract,
    string? NativeAttemptId, ActivityExecutionStatus NativeStatus, SubmitJobResult? Completion,
    JobNativeSubmissionDisposition Disposition, string? JobId, string? RequestSha256, JobOrigin? Origin,
    JobAcceptance? Acceptance)
{
    /// <summary>Positive committed owner-cancellation phase, including cancellation before a first native attempt exists.</summary>
    public string? NativeCancellationReceiptId { get; init; }
}

public enum JobNativeSubmissionDisposition
{
    Accepted = 0,
    CancelledBeforeAdmission = 1,
    RejectedBeforeAdmission = 2
}

/// <summary>Optional model-owned lifetime proof for an independent submission after its helper worker completes.</summary>
public interface IJobWorkflowSubmissionLifetimeSource
{
    ValueTask<JobWorkflowSubmissionLifetime> InspectSubmissionLifetimeAsync(JobWorkflowOwner owner, CancellationToken cancellationToken = default);
}

/// <summary>Exact retained owner lifecycle. Terminal states require a nonblank durable model receipt.</summary>
public sealed record JobWorkflowSubmissionLifetime(JobWorkflowSubmissionLifetimeStatus Status, string? ModelReceiptId);

public enum JobWorkflowSubmissionLifetimeStatus
{
    Active = 0,
    Completed = 1,
    Faulted = 2,
    Cancelled = 3
}
