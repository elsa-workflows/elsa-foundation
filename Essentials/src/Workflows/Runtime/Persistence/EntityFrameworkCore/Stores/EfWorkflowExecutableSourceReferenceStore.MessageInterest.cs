using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed partial class EfWorkflowExecutableSourceReferenceStore
{
    // Internal physical fence for the qualified publication switch, never an independent save/transaction.
    internal async ValueTask<bool> StageMessageInterestFenceAsync(string scope, string sourceReferenceId,
        string expectedIncarnation, CancellationToken cancellationToken)
    {
        if (RequireScope() != scope || context.Database.CurrentTransaction is null)
            throw new NotSupportedException("Publication reference fencing requires its actual scoped owner transaction.");
        _ = MessageInterestWriteVersion.Require(context, RuntimeArtifactEfModule.SchemaFamily, RuntimeArtifactEfModule.SchemaVersion);
        var current = await ReadPhysicalMaintenanceSourceAsync(scope, sourceReferenceId, cancellationToken);
        if (current is null || current.Value.Row.IncarnationId != expectedIncarnation) return false;
        var row = current.Value.Row;
        if (row.Revision > long.MaxValue - 2) throw new InvalidOperationException("The reference revision cannot advance.");
        if (context.ChangeTracker.Entries<WorkflowExecutableSourceReferenceEntity>().Any(entry => entry.Entity.Id == row.Id))
            throw new NotSupportedException("Reference fencing must precede owner reference staging.");
        // A real revision increment has portable affected-row semantics, including MySQL. It is still in the
        // caller's transaction and rolls back with the slot/projections; the canonical reference content is unchanged.
        return await RowsOf(scope, sourceReferenceId).Where(candidate => candidate.Id == row.Id &&
                candidate.IncarnationId == expectedIncarnation && candidate.Revision == row.Revision && candidate.SchemaVersion == row.SchemaVersion)
            .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.Revision, row.Revision + 1), cancellationToken) == 1;
    }
}
