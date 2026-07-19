using System.Globalization;
using System.Text;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class InMemoryWorkflowExecutionStateStore() : InMemoryKeyedStateStore<string, WorkflowExecutionState>(StringComparer.Ordinal), IWorkflowExecutionStateStore
{
    public ValueTask<WorkflowExecutionState> SaveAsync(WorkflowExecutionState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        return new(Save(state.WorkflowExecutionId, state));
    }

    public ValueTask<WorkflowExecutionState?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        return new(Find(workflowExecutionId));
    }

    public ValueTask<IReadOnlyCollection<WorkflowExecutionState>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return new(SnapshotAll());
    }

    public ValueTask<WorkflowExecutionStatePage> QueryPageAsync(
        WorkflowExecutionStatePageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        return new(ReadValues(states => QueryPage(states, query)));
    }

    public ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyCollection<string> artifactIds = SnapshotAll()
            .Select(x => x.PinnedExecutable.ArtifactId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new(artifactIds);
    }

    public ValueTask<bool> DeleteAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        return new(Remove(workflowExecutionId));
    }

    private static WorkflowExecutionStatePage QueryPage(
        IEnumerable<WorkflowExecutionState> states,
        WorkflowExecutionStatePageQuery query)
    {
        var comparer = Comparer<WorkflowExecutionState>.Create(WorkflowExecutionStateHistory.Compare);
        var candidates = new SortedSet<WorkflowExecutionState>(comparer);
        var cursor = query.Cursor is null ? null : DecodeCursor(query.Cursor, query);
        var limit = checked(query.PageSize + 1);
        long total = 0;

        foreach (var state in states)
        {
            if (!WorkflowExecutionStateHistory.Matches(state, query))
                continue;

            total++;
            if (cursor is not null)
            {
                var timestamp = cursor.SortTimestamp.CompareTo(WorkflowExecutionStateHistory.SortTimestamp(state));
                var comparison = timestamp != 0
                    ? timestamp
                    : StringComparer.Ordinal.Compare(state.WorkflowExecutionId, cursor.WorkflowExecutionId);
                if (comparison <= 0)
                    continue;
            }

            candidates.Add(state);
            if (candidates.Count <= limit)
                continue;
            candidates.Remove(candidates.Max!);
        }

        var hasExtra = candidates.Count > query.PageSize;
        if (hasExtra)
            candidates.Remove(candidates.Max!);

        var items = candidates.ToArray();
        return new(
            items,
            hasExtra && items.Length > 0
                ? EncodeCursor(items[^1], query)
                : null,
            hasExtra,
            total);
    }

    private static string EncodeCursor(
        WorkflowExecutionState state,
        WorkflowExecutionStatePageQuery query)
    {
        var value = string.Join(
            '|',
            "v1",
            WorkflowExecutionStateHistory.SortTimestamp(state).UtcTicks.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(Encoding.UTF8.GetBytes(state.WorkflowExecutionId)),
            WorkflowExecutionStateHistory.Scope(query));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static InMemoryCursor DecodeCursor(
        string cursor,
        WorkflowExecutionStatePageQuery query)
    {
        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(base64)).Split('|');
            if (parts.Length != 4 ||
                parts[0] != "v1" ||
                !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ||
                !StringComparer.Ordinal.Equals(parts[3], WorkflowExecutionStateHistory.Scope(query)))
            {
                throw new FormatException();
            }

            return new(
                new DateTimeOffset(ticks, TimeSpan.Zero),
                Encoding.UTF8.GetString(Convert.FromBase64String(parts[2])));
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new ArgumentException(
                "The workflow execution history cursor is invalid or does not belong to this query.",
                nameof(cursor),
                exception);
        }
    }

    private sealed record InMemoryCursor(DateTimeOffset SortTimestamp, string WorkflowExecutionId);
}
