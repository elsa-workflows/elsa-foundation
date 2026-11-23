using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;

namespace Elsa.Workflows.Runtime.Services.Alterations;

/// <summary>
/// Whether a job may take a terminal change, decided against the job a store read inside its own atomic boundary. Every
/// alteration and checkpoint store calls this one rule.
/// </summary>
public static class WorkflowAlterationTerminalEvidence
{
    /// <summary>
    /// A running job accepts the change only from its current claimant. A terminal job accepts only an identical replay of
    /// the evidence it already holds. When the change travels in a workflow checkpoint, the job must belong to that
    /// checkpoint's workflow execution.
    /// </summary>
    public static void Validate(
        WorkflowAlterationJobState job,
        WorkflowAlterationJobTerminalChange change,
        string? checkpointWorkflowExecutionId = null)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(change);

        if (checkpointWorkflowExecutionId is not null &&
            !StringComparer.Ordinal.Equals(job.WorkflowExecutionId, checkpointWorkflowExecutionId))
            throw new RuntimeCheckpointCommitValidationException(
                $"Alteration job '{change.JobId}' belongs to workflow '{job.WorkflowExecutionId}', not '{checkpointWorkflowExecutionId}'.");

        if (IsTerminal(job.Status))
        {
            if (!EvidenceEquals(job, change))
                throw new InvalidOperationException("Terminal alteration evidence conflicts with the stored result.");
            return;
        }

        if (job.Status != WorkflowAlterationJobStatus.Running ||
            job.Claim is null ||
            !StringComparer.Ordinal.Equals(job.Claim.Token, change.ClaimToken))
            throw new WorkflowAlterationClaimFenceException(job.JobId);
    }

    public static bool IsTerminal(WorkflowAlterationJobStatus status) =>
        status is WorkflowAlterationJobStatus.Succeeded or WorkflowAlterationJobStatus.Failed or WorkflowAlterationJobStatus.Cancelled;

    private static bool EvidenceEquals(WorkflowAlterationJobState job, WorkflowAlterationJobTerminalChange change) =>
        StringComparer.Ordinal.Equals(job.CheckpointCommitId, change.CheckpointCommitId) &&
        job.Status == change.Status &&
        job.CompletedAt == change.CompletedAt &&
        job.SafeFailure == change.SafeFailure &&
        OutcomesEqual(job.Outcomes, change.Outcomes);

    /// <summary>Outcomes compare by value in ordinal order; their metadata dictionaries compare by content, not reference.</summary>
    private static bool OutcomesEqual(IReadOnlyCollection<WorkflowAlterationOutcome> left, IReadOnlyCollection<WorkflowAlterationOutcome> right)
    {
        if (left.Count != right.Count)
            return false;
        return left.OrderBy(outcome => outcome.Ordinal).Zip(right.OrderBy(outcome => outcome.Ordinal)).All(pair =>
            pair.First.Ordinal == pair.Second.Ordinal &&
            StringComparer.Ordinal.Equals(pair.First.Kind, pair.Second.Kind) &&
            pair.First.SchemaVersion == pair.Second.SchemaVersion &&
            pair.First.Status == pair.Second.Status &&
            StringComparer.Ordinal.Equals(pair.First.Code, pair.Second.Code) &&
            StringComparer.Ordinal.Equals(pair.First.Message, pair.Second.Message) &&
            pair.First.RecordedAt == pair.Second.RecordedAt &&
            RuntimeModelMetadata.AreEqual(pair.First.StructuralMetadata, pair.Second.StructuralMetadata));
    }
}
