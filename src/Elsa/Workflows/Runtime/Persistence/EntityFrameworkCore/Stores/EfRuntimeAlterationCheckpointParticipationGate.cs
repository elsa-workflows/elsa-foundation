using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Correlates an alteration callback with the same EF checkpoint writer and DbContext.</summary>
/// <remarks>
/// A context-free callback is not a transaction enlistment handle. Only the actual EF checkpoint writer may mark
/// participation, after its marker is durable; unrelated or differently scoped EF checkpoint callbacks fail before
/// provider I/O. The alteration store then verifies both the terminal row and marker before returning.
/// </remarks>
internal sealed class EfRuntimeAlterationCheckpointParticipationGate : IDisposable
{
    private static readonly AsyncLocal<EfRuntimeAlterationCheckpointParticipationGate?> Active = new();
    private readonly RuntimeDbContext _context;
    private readonly WorkflowAlterationJobTerminalChange _change;
    private readonly string _scope;
    private bool _participated;

    private EfRuntimeAlterationCheckpointParticipationGate(
        RuntimeDbContext context, WorkflowAlterationJobTerminalChange change, string scope)
    {
        _context = context;
        _change = change;
        _scope = scope;
    }

    public static EfRuntimeAlterationCheckpointParticipationGate Begin(
        RuntimeDbContext context, WorkflowAlterationJobTerminalChange change, string scope)
    {
        if (Active.Value is not null)
            throw new InvalidOperationException("A nested EF alteration checkpoint callback cannot establish an independent atomic boundary.");
        var gate = new EfRuntimeAlterationCheckpointParticipationGate(context, change, scope);
        Active.Value = gate;
        return gate;
    }

    public static void Validate(RuntimeDbContext context, RuntimeCheckpointCommit commit, string scope)
    {
        var gate = Active.Value;
        if (gate is null)
            return;
        if (!ReferenceEquals(gate._context, context) ||
            !StringComparer.Ordinal.Equals(gate._scope, scope) ||
            commit.StateChanges.AlterationJobTerminalChange is not { } terminal ||
            !StringComparer.Ordinal.Equals(commit.CommitId, gate._change.CheckpointCommitId) ||
            !StringComparer.Ordinal.Equals(terminal.JobId, gate._change.JobId) ||
            !StringComparer.Ordinal.Equals(terminal.ClaimToken, gate._change.ClaimToken) ||
            !StringComparer.Ordinal.Equals(terminal.CheckpointCommitId, gate._change.CheckpointCommitId) ||
            terminal.Status != gate._change.Status ||
            terminal.CompletedAt != gate._change.CompletedAt ||
            !Equals(terminal.SafeFailure, gate._change.SafeFailure) ||
            !terminal.Outcomes.SequenceEqual(gate._change.Outcomes))
            throw new InvalidOperationException("The alteration checkpoint callback must use this EF context and matching terminal evidence.");
    }

    public static void MarkDurable(RuntimeDbContext context, RuntimeCheckpointCommit commit, string scope)
    {
        var gate = Active.Value;
        if (gate is null)
            return;
        Validate(context, commit, scope);
        gate._participated = true;
    }

    public static void RejectIndependentTerminalWrite(RuntimeDbContext context)
    {
        if (Active.Value is { } gate && ReferenceEquals(gate._context, context))
            throw new InvalidOperationException("An alteration checkpoint callback cannot terminalize its EF job outside the shared checkpoint transaction.");
    }

    public async ValueTask VerifyDurableAsync(CancellationToken cancellationToken)
    {
        if (!_participated)
            throw new InvalidOperationException("The alteration checkpoint callback did not commit through the shared EF checkpoint writer.");

        var markerId = EfRuntimeOperationalStoreSupport.CompositeId(_scope, _change.CheckpointCommitId);
        var marker = await _context.RuntimeCheckpointCommits.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == markerId, cancellationToken)
            ?? throw new InvalidOperationException("The alteration checkpoint callback did not leave a durable EF marker.");
        _ = EfRuntimeCheckpointCommitStore.ReadChecked(marker, _scope, _change.CheckpointCommitId);

        var jobId = EfWorkflowAlterationStore.Id(_scope, _change.JobId);
        var row = await _context.WorkflowAlterationJobs.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == jobId, cancellationToken)
            ?? throw new InvalidOperationException("The alteration checkpoint callback did not terminalize the EF job.");
        var job = EfWorkflowAlterationStore.ReadJob(row, _scope, _change.JobId);
        WorkflowAlterationTerminalEvidence.Validate(job, _change);
        if (job.Status is not (WorkflowAlterationJobStatus.Succeeded or WorkflowAlterationJobStatus.Failed or WorkflowAlterationJobStatus.Cancelled))
            throw new InvalidOperationException("The alteration checkpoint callback did not persist terminal job evidence.");
        if (!StringComparer.Ordinal.Equals(EfRuntimeOperationalStoreSupport.Decode(marker.WorkflowExecutionId), job.WorkflowExecutionId))
            throw new InvalidDataException("The alteration checkpoint marker belongs to a different workflow execution.");
    }

    public void Dispose()
    {
        if (ReferenceEquals(Active.Value, this))
            Active.Value = null;
    }
}
