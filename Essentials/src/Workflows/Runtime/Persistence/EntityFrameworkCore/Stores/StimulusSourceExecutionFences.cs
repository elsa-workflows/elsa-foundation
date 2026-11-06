using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Normal workflow deletion ownership shared by publication and ingress source acceptance.</summary>
internal static class StimulusSourceExecutionFences
{
    /// <summary>Lock existing roots before ownership, matching normal saves/deletion; preserve old-reader refusal.</summary>
    public static async ValueTask ProtectIngressAsync(RuntimeDbContext context, string scope, string workflowExecutionId,
        CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Ingress execution retention requires its source acceptance transaction.");
        await EfWorkflowExecutionStateStore.LockWorkflowExecutionForWriteAsync(context, scope, workflowExecutionId, cancellationToken);
        var id = EfWorkflowExecutionStateStore.CreateId(scope, workflowExecutionId);
        var row = await context.WorkflowExecutionStates.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (row is not null)
        {
            var state = EfWorkflowExecutionStateStore.ReadChecked(row, scope, workflowExecutionId);
            var version = ModelWorkWriteVersion.For(context, state, row.SchemaVersion);
            if (Version.Parse(version) < Version.Parse(RuntimeWorkflowExecutionEfModule.IngressRetentionSchemaVersion))
                throw new NotSupportedException("Ingress execution retention requires its admitted workflow reader version.");
            row.SchemaVersion = version;
            row.Revision = checked(row.Revision + 1);
        }
        // This CAS also excludes activity-only checkpoints and sticky deletion tombstones. If the
        // start has no execution yet, its ordinary future write uses the finalized family version.
        await FenceAsync(context, scope, workflowExecutionId, cancellationToken);
    }

    public static async ValueTask FenceAsync(RuntimeDbContext context, string scope, string workflowExecutionId,
        CancellationToken cancellationToken)
    {
        var row = await EfExecutionLivenessStateStore.WorkflowExecutionDeletionOwnershipRows(context, scope, workflowExecutionId)
            .SingleOrDefaultAsync(cancellationToken);
        if (EfExecutionLivenessStateStore.IsWorkflowExecutionDeletionTombstone(row, scope, workflowExecutionId))
            throw new InvalidDataException("A saved cohort execution dependency was retired before acceptance.");
        if (row is null)
        {
            var state = new ExecutionLivenessState(RuntimeExecutionOwnershipStateId.For(workflowExecutionId), workflowExecutionId,
                null, null, null, null, [], new Dictionary<string, string> { [RuntimeMetadataKeys.OwnershipFencingToken] = "0" });
            context.ExecutionLivenessStates.Add(EfExecutionLivenessStateStore.ToEntity(state, scope, 1));
            await context.SaveChangesAsync(cancellationToken);
            return;
        }
        var touched = await context.ExecutionLivenessStates.Where(candidate => candidate.Id == row.Id && candidate.Revision == row.Revision)
            .ExecuteUpdateAsync(update => update.SetProperty(candidate => candidate.Revision, row.Revision + 1), cancellationToken);
        if (touched != 1) throw new DbUpdateConcurrencyException("Cohort execution ownership changed before acceptance.");
    }
}
