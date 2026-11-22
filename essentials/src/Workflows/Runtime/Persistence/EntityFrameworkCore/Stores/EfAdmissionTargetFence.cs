using System.Data;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeOperationalStoreSupport;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Retains observed logical slot/source authority through a caller-owned atomic admission.</summary>
internal static class EfAdmissionTargetFence
{
    internal static async ValueTask<bool> LockSlotAsync(RuntimeDbContext context, string scope, string definitionId,
        string slotName, WorkflowActivationSlot? expected, CancellationToken cancellationToken)
    {
        RequireTransaction(context);
        var id = CompositeId(scope, WorkflowActivationSlotIdentity.Create(definitionId, slotName));
        var row = await context.WorkflowActivationSlots.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        // SERIALIZABLE retains the indexed absence until the admission's commit; no authority row is invented.
        if (row is null) return expected is null;
        if (expected is null || EfWorkflowActivationAuthority.Read(row, scope, definitionId, slotName) != expected) return false;
        return await context.WorkflowActivationSlots.Where(item => item.Id == id && item.Revision == row.Revision &&
                item.ContentJson == row.ContentJson && item.SchemaVersion == row.SchemaVersion)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.Revision, row.Revision), cancellationToken) == 1;
    }

    internal static async ValueTask<bool> LockSourceAsync(EfWorkflowExecutableSourceReferenceStore sources, string scope,
        WorkflowExecutableSourceReference expected, CancellationToken cancellationToken)
    {
        var context = sources.Context;
        RequireTransaction(context);
        var current = await sources.ReadPhysicalMaintenanceSourceAsync(scope, expected.SourceReferenceId, cancellationToken);
        if (current is not { } physical || !WorkflowExecutableSourceReferenceComparer.SameSnapshot(physical.Value, expected)) return false;
        var row = physical.Row;
        return await context.WorkflowExecutableSourceReferences.Where(item => item.Id == row.Id && item.Revision == row.Revision &&
                item.ContentJson == row.ContentJson && item.SchemaVersion == row.SchemaVersion && item.IncarnationId == row.IncarnationId)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.Revision, row.Revision), cancellationToken) == 1;
    }

    private static void RequireTransaction(RuntimeDbContext context)
    {
        if (context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Admission target observations require the caller's SERIALIZABLE transaction.");
    }
}
