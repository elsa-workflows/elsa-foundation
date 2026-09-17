using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Stages the incident participants of one caller-owned checkpoint transaction.</summary>
/// <remarks>
/// This seam deliberately changes only the tracked EF graph. The checkpoint writer owns transaction creation,
/// <c>SaveChanges</c>, commit, rollback, and replay-marker ordering. The direct incident store remains responsible
/// for independent public writes; its projection and validation helpers are reused here so the two paths cannot
/// silently drift.
/// </remarks>
internal static class EfRuntimeCheckpointIncidentParticipantStaging
{
    public static async ValueTask StageIncidentsAsync(
        BookmarkStateDbContext context,
        IReadOnlyCollection<RuntimeStateChange<IncidentState>> changes,
        string scope,
        string expectedWorkflowExecutionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Count == 0)
            return;

        var staged = changes
            .Select(change => Validated(context, change, scope, expectedWorkflowExecutionId, cancellationToken))
            .ToArray();

        // Load by immutable physical identity first. Filtering on projections would turn corruption into a false
        // insert/miss instead of allowing the authoritative content and projection checks to fail closed. One read
        // covers every incident of this commit; an id it does not return is the same "row is null" case as before.
        var rows = await EfRuntimeCheckpointParticipantRows.LoadAsync(
            staged.Select(entry => entry.Id).ToArray(),
            batch => context.IncidentStates.Where(row => batch.Contains(row.Id)),
            row => row.Id,
            cancellationToken);

        foreach (var (change, id) in staged)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Stage(context, change, scope, id, rows.GetValueOrDefault(id));
        }
    }

    private static (RuntimeStateChange<IncidentState> Change, string Id) Validated(
        BookmarkStateDbContext context,
        RuntimeStateChange<IncidentState> change,
        string scope,
        string expectedWorkflowExecutionId,
        CancellationToken cancellationToken)
    {
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
        return (change, EfRuntimeOperationalStoreSupport.CompositeId(scope, state.WorkflowExecutionId, state.IncidentId));
    }

    private static void Stage(
        BookmarkStateDbContext context,
        RuntimeStateChange<IncidentState> change,
        string scope,
        string id,
        IncidentStateEntity? row)
    {
        var state = change.State;
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
            IncidentStateTransitionValidator.EnsureAppendTargetIsAbsent(existing, state);

        IncidentStateTransitionValidator.EnsureResolutionOutcomeIsWriteOnce(existing, state);
        EfIncidentStateStore.Copy(row, state, scope, checked(row.Revision + 1));
    }
}
