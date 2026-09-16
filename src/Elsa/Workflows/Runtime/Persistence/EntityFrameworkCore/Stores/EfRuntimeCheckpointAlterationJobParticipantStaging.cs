using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Stages alteration-job terminal evidence in a caller-owned checkpoint transaction.</summary>
/// <remarks>
/// The checkpoint writer owns transaction creation, SaveChanges, commit, rollback, and the durable marker. This
/// participant only loads and validates the authoritative job projection, then changes the tracked row. The direct
/// alteration store remains responsible for independent public writes; its projection and terminal-fence helpers are
/// reused here so the checkpoint and direct paths cannot drift.
/// </remarks>
internal static class EfRuntimeCheckpointAlterationJobParticipantStaging
{
    public static async ValueTask StageAsync(
        BookmarkStateDbContext context,
        WorkflowAlterationJobTerminalChange change,
        string scope,
        string expectedWorkflowExecutionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(change);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedWorkflowExecutionId);
        EfRuntimeOperationalStoreSupport.ValidateIdentity(expectedWorkflowExecutionId, nameof(expectedWorkflowExecutionId));
        if (scope.Length > 256)
            throw new ArgumentException("Runtime persistence scope cannot exceed 256 UTF-16 code units.", nameof(scope));
        cancellationToken.ThrowIfCancellationRequested();

        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Alteration-job terminal evidence must be staged inside a caller-owned EF transaction.");

        var id = EfWorkflowAlterationStore.Id(scope, change.JobId);
        // Resolve by immutable physical identity first. Projection predicates would turn a corrupt row into a false
        // missing row and allow an incorrect insert or no-op instead of failing closed.
        var row = await context.WorkflowAlterationJobs.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException($"Alteration job '{change.JobId}' was not found.");
        var existing = EfWorkflowAlterationStore.ReadJob(row, scope, change.JobId);

        WorkflowAlterationTerminalEvidence.Validate(existing, change, expectedWorkflowExecutionId);
        if (WorkflowAlterationTerminalEvidence.IsTerminal(existing.Status))
            return;

        var terminal = new WorkflowAlterationJobState(
            existing.JobId,
            existing.PlanId,
            existing.WorkflowExecutionId,
            existing.TenantPartition,
            existing.CaptureOrdinal,
            change.Status,
            existing.Claim,
            existing.AttemptCount,
            change.Outcomes.ToArray(),
            change.CheckpointCommitId,
            change.SafeFailure,
            existing.CreatedAt,
            existing.StartedAt,
            change.CompletedAt,
            checked(existing.Revision + 1),
            existing.CapturedConcurrency);
        EfWorkflowAlterationStore.CopyJob(row, terminal, scope);
    }
}
