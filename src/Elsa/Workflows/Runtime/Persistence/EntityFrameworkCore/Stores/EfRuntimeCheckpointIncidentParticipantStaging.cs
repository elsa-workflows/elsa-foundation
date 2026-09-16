using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Stages one incident participant in a caller-owned checkpoint transaction.</summary>
/// <remarks>
/// This seam deliberately changes only the tracked EF graph. The checkpoint writer owns transaction creation,
/// <c>SaveChanges</c>, commit, rollback, and replay-marker ordering. The direct incident store remains responsible
/// for independent public writes; its projection and validation helpers are reused here so the two paths cannot
/// silently drift.
/// </remarks>
internal static class EfRuntimeCheckpointIncidentParticipantStaging
{
    public static async ValueTask StageIncidentAsync(
        BookmarkStateDbContext context,
        RuntimeStateChange<IncidentState> change,
        string scope,
        string expectedWorkflowExecutionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(change);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedWorkflowExecutionId);
        EfRuntimeOperationalStoreSupport.ValidateIdentity(expectedWorkflowExecutionId, nameof(expectedWorkflowExecutionId));
        EfIncidentStateStore.Validate(change.State);
        if (scope.Length > 256)
            throw new ArgumentException("Runtime persistence scope cannot exceed 256 UTF-16 code units.", nameof(scope));
        if (change.Operation is not (RuntimeStateChangeOperation.Append or RuntimeStateChangeOperation.Upsert or RuntimeStateChangeOperation.Delete))
            throw new InvalidOperationException("The EF checkpoint writer can only project incident append, upsert, or delete changes.");
        cancellationToken.ThrowIfCancellationRequested();

        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Incident state must be staged inside a caller-owned EF transaction.");

        var state = change.State;
        var id = EfRuntimeOperationalStoreSupport.CompositeId(scope, state.WorkflowExecutionId, state.IncidentId);

        // Load by immutable physical identity first. Filtering on projections would turn corruption into a false
        // insert/miss instead of allowing the authoritative content and projection checks to fail closed.
        var row = await context.IncidentStates.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (row is null)
        {
            if (change.Operation is RuntimeStateChangeOperation.Append or RuntimeStateChangeOperation.Upsert)
                context.IncidentStates.Add(EfIncidentStateStore.ToEntity(state, scope, id, 1));

            // Conditional delete of an absent incident is intentionally idempotent.
            return;
        }

        var existing = EfIncidentStateStore.ReadChecked(row, scope, state.WorkflowExecutionId, state.IncidentId);
        if (change.Operation == RuntimeStateChangeOperation.Delete)
        {
            context.IncidentStates.Remove(row);
            return;
        }

        if (change.Operation == RuntimeStateChangeOperation.Append)
            throw new InvalidOperationException($"Incident '{state.IncidentId}' already exists and cannot be appended again.");

        IncidentStateTransitionValidator.EnsureResolutionOutcomeIsWriteOnce(existing, state);
        EfIncidentStateStore.Copy(row, state, scope, checked(row.Revision + 1));
    }
}
