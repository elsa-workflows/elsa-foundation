using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Stages the activity-execution participants of one caller-owned checkpoint transaction.</summary>
/// <remarks>
/// The direct activity-execution store owns independent writes and clears the change tracker around them. A
/// checkpoint must retain all participants in one unit of work, so this seam deliberately only changes EF tracking
/// state. SaveChanges, transaction creation, commit, rollback, and the checkpoint fence remain the caller's
/// responsibility.
/// </remarks>
internal static class EfRuntimeCheckpointActivityExecutionParticipantStaging
{
    public static async ValueTask StageActivityExecutionsAsync(
        RuntimeDbContext context,
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionState>> changes,
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

        // Load by immutable physical identity first. Filtering by projections would turn a corrupt row into a false
        // insert/miss instead of allowing the authoritative content and projection checks to fail closed. One read
        // covers every participant of this type; an id it does not return is the same "row is null" case as before.
        var rows = await EfRuntimeCheckpointParticipantRows.LoadAsync(
            staged.Select(entry => entry.Id).ToArray(),
            batch => context.ActivityExecutionStates.Where(row => batch.Contains(row.Id)),
            row => row.Id,
            cancellationToken);

        foreach (var (change, id) in staged)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Stage(context, change, scope, id, rows.GetValueOrDefault(id));
        }
    }

    private static (RuntimeStateChange<ActivityExecutionState> Change, string Id) Validated(
        RuntimeDbContext context,
        RuntimeStateChange<ActivityExecutionState> change,
        string scope,
        string expectedWorkflowExecutionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedWorkflowExecutionId);
        ActivityExecutionEfSupport.ValidateIdentityLength(expectedWorkflowExecutionId, nameof(expectedWorkflowExecutionId));
        ActivityExecutionEfSupport.Validate(change.State);
        if (change.Operation is not (RuntimeStateChangeOperation.Append or RuntimeStateChangeOperation.Upsert or RuntimeStateChangeOperation.Delete))
            throw new InvalidOperationException("The EF checkpoint writer can only project activity execution append, upsert, or delete changes.");
        cancellationToken.ThrowIfCancellationRequested();

        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Activity execution state must be staged inside a caller-owned EF transaction.");

        var state = change.State;
        return (change, ActivityExecutionEfSupport.CreateId(
            "state",
            scope,
            state.Execution.WorkflowExecutionId,
            state.Execution.ActivityExecutionId));
    }

    private static void Stage(
        RuntimeDbContext context,
        RuntimeStateChange<ActivityExecutionState> change,
        string scope,
        string id,
        ActivityExecutionStateEntity? row)
    {
        var state = change.State;
        if (row is null)
        {
            if (change.Operation is RuntimeStateChangeOperation.Append or RuntimeStateChangeOperation.Upsert)
                context.ActivityExecutionStates.Add(EfActivityExecutionStateStore.ToEntity(
                    state,
                    scope,
                    id,
                    ActivityExecutionEfSupport.NewRevision()));

            // Conditional deletion of an absent activity execution is intentionally idempotent.
            return;
        }

        var previous = EfActivityExecutionStateStore.ReadChecked(
            row,
            scope,
            state.Execution.WorkflowExecutionId,
            state.Execution.ActivityExecutionId);
        if (change.Operation == RuntimeStateChangeOperation.Delete)
        {
            context.ActivityExecutionStates.Remove(row);
            return;
        }

        if (change.Operation == RuntimeStateChangeOperation.Append)
            throw new InvalidOperationException($"Activity execution '{state.Execution.ActivityExecutionId}' already exists and cannot be appended again.");

        EfActivityExecutionStateStore.CopyToEntity(
            row,
            state,
            scope,
            id,
            checked(row.Revision + 1),
            previous);
    }
}
