using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Stores split continuation state for concrete activity executions.
/// </summary>
public interface IActivityExecutionStateStore
{
    /// <summary>
    /// Inserts or replaces state for the concrete activity execution key.
    /// </summary>
    ValueTask<ActivityExecutionState> SaveAsync(ActivityExecutionState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the state for the given workflow execution ID and activity execution ID, or <see langword="null"/> if not found.
    /// </summary>
    ValueTask<ActivityExecutionState?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the number of activity execution states owned by one workflow execution without materializing
    /// their payloads. Durable providers should implement this as a provider-side count over the workflow index.
    /// </summary>
    async ValueTask<long> CountAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        long count = 0;
        string? continuationToken = null;
        do
        {
            var page = await ListPageAsync(
                new ActivityExecutionStatePageQuery(workflowExecutionId, continuationToken: continuationToken),
                cancellationToken);
            count += page.Items.Count;
            continuationToken = page.NextContinuationToken;
        } while (continuationToken is not null);

        return count;
    }

    /// <summary>
    /// Returns one finite page of activity execution states for the given workflow execution ID.
    /// </summary>
    ValueTask<RuntimeStorePage<ActivityExecutionState>> ListPageAsync(
        ActivityExecutionStatePageQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one finite page of activity execution states directly parented within the
    /// given workflow execution. This is the parent-scoped read the whole-workflow <see cref="ListPageAsync"/>
    /// would otherwise be filtered down to; it lets callers that only need a composite's direct children (e.g. the Parallel
    /// fork/join counting the completed branches) avoid loading and filtering every activity-execution state in the workflow.
    /// The result is exactly the subset of <see cref="ListPageAsync"/> whose
    /// <see cref="ActivityExecutionState.ParentActivityExecutionId"/> equals
    /// <see cref="ActivityExecutionStateParentPageQuery.ParentActivityExecutionId"/>.
    /// </summary>
    ValueTask<RuntimeStorePage<ActivityExecutionState>> ListByParentPageAsync(
        ActivityExecutionStateParentPageQuery query,
        CancellationToken cancellationToken = default);
    /// <summary>
    /// Spec 1229 FR-016: the rows of one workflow execution whose commit-sequence range reaches
    /// <see cref="ActivityExecutionStateCommitSequenceQuery.FromCommitSequence"/> (their
    /// <see cref="ActivityExecutionState.LastCommitSequence"/> is at or above it), ordered by
    /// <see cref="ActivityExecutionState.FirstCommitSequence"/> and then by activity execution id, after the query's keyset
    /// position, at most <see cref="ActivityExecutionStateCommitSequenceQuery.Limit"/> rows. Rows without a range are not
    /// returned. Durable providers should filter and order on store columns; this default reads every row.
    /// </summary>
    async ValueTask<IReadOnlyList<ActivityExecutionState>> ListByCommitSequenceAsync(
        ActivityExecutionStateCommitSequenceQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var rows = await this.ListAllAsync(query.WorkflowExecutionId, cancellationToken);
        return rows
            .Where(row => row.FirstCommitSequence is not null && row.LastCommitSequence >= query.FromCommitSequence)
            .Where(row => query.After is not { } after ||
                          row.FirstCommitSequence > after.FirstCommitSequence ||
                          row.FirstCommitSequence == after.FirstCommitSequence &&
                          StringComparer.Ordinal.Compare(row.Execution.ActivityExecutionId, after.ActivityExecutionId) > 0)
            .OrderBy(row => row.FirstCommitSequence)
            .ThenBy(row => row.Execution.ActivityExecutionId, StringComparer.Ordinal)
            .Take(query.Limit)
            .ToArray();
    }

    /// <summary>
    /// Spec 1229 (research R-2): the rows of one workflow execution without a commit-sequence range (older rows, and rows
    /// committed only outside an execution lease). Inspection reads them once and orders what they record as approximate.
    /// </summary>
    async ValueTask<IReadOnlyList<ActivityExecutionState>> ListWithoutCommitSequenceAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        var rows = await this.ListAllAsync(workflowExecutionId, cancellationToken);
        return rows.Where(row => row.FirstCommitSequence is null).ToArray();
    }

    /// <summary>
    /// Spec 1229 FR-009: the sum of <see cref="ActivityExecutionState.LostUpdateWarningCount"/> over one workflow
    /// execution's rows. Durable providers should sum a store column; this default reads every row.
    /// </summary>
    async ValueTask<long> SumLostUpdateWarningCountAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        var rows = await this.ListAllAsync(workflowExecutionId, cancellationToken);
        return rows.Sum(row => (long)(row.LostUpdateWarningCount ?? ActivityExecutionState.CountLostUpdateWarnings(row.VariableWrites) ?? 0));
    }
}

/// <summary>The keyset position after which <see cref="IActivityExecutionStateStore.ListByCommitSequenceAsync"/> continues.</summary>
public readonly record struct ActivityExecutionStateCommitSequenceKey(long FirstCommitSequence, string ActivityExecutionId);

/// <summary>A query for the rows whose commit-sequence range reaches <paramref name="FromCommitSequence"/> (spec 1229 FR-016).</summary>
public sealed record ActivityExecutionStateCommitSequenceQuery
{
    public ActivityExecutionStateCommitSequenceQuery(
        string workflowExecutionId,
        long fromCommitSequence,
        int limit,
        ActivityExecutionStateCommitSequenceKey? after = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        if (after is { } key)
            ArgumentException.ThrowIfNullOrWhiteSpace(key.ActivityExecutionId, nameof(after));
        WorkflowExecutionId = workflowExecutionId;
        FromCommitSequence = fromCommitSequence;
        Limit = limit;
        After = after;
    }

    public string WorkflowExecutionId { get; }
    public long FromCommitSequence { get; }
    public int Limit { get; }
    public ActivityExecutionStateCommitSequenceKey? After { get; }
}
