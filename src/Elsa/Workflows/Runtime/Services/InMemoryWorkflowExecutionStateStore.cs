using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class InMemoryWorkflowExecutionStateStore() : InMemoryKeyedStateStore<string, WorkflowExecutionState>(StringComparer.Ordinal), IWorkflowExecutionStateStore
{
    public override bool IsAffected(InMemoryCheckpointMutationPlan plan) => plan.MutatesWorkflowExecution;

    private protected override bool IsCheckpointKey(string key, InMemoryCheckpointMutationPlan scope) =>
        scope.MutatesWorkflowExecution && StringComparer.Ordinal.Equals(key, scope.WorkflowExecutionId);

    private protected override IEnumerable<string> CheckpointKeys(InMemoryCheckpointMutationPlan scope) =>
        scope.MutatesWorkflowExecution ? [scope.WorkflowExecutionId] : [];

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

    public ValueTask<WorkflowExecutionAlterationCapturePage> QueryAlterationCapturePageAsync(
        WorkflowExecutionAlterationCaptureQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        return new(ReadValues(states => QueryAlterationCapturePage(states, query)));
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

    private static WorkflowExecutionAlterationCapturePage QueryAlterationCapturePage(
        IEnumerable<WorkflowExecutionState> states,
        WorkflowExecutionAlterationCaptureQuery query)
    {
        var after = query.Cursor is null ? null : DecodeAlterationCaptureCursor(query.Cursor, query);
        var items = states
            .Where(state => StringComparer.Ordinal.Equals(state.TenantId, query.TenantPartition))
            .Where(state => state.Authority is { } authority &&
                            WorkflowExecutionAuthoritySnapshot.Matches(
                                authority,
                                query.SystemIdentity,
                                query.RootInitiator,
                                query.AuthorityMetadata))
            .Where(state => MatchesAlterationSelector(state, query.Selector))
            .OrderBy(state => state.TenantId, StringComparer.Ordinal)
            .ThenBy(state => state.WorkflowExecutionId, StringComparer.Ordinal)
            .Where(state => after is null || StringComparer.Ordinal.Compare(state.WorkflowExecutionId, after) > 0)
            .Take(checked(query.PageSize + 1))
            .ToArray();
        var hasNext = items.Length > query.PageSize;
        if (hasNext)
            items = items[..query.PageSize];

        return new WorkflowExecutionAlterationCapturePage(
            items,
            hasNext ? EncodeAlterationCaptureCursor(items[^1].WorkflowExecutionId, query) : null,
            hasNext);
    }

    private static bool MatchesAlterationSelector(WorkflowExecutionState state, WorkflowAlterationQuerySelector selector)
    {
        var query = new WorkflowExecutionStatePageQuery(
            PageSize: 1,
            DefinitionId: selector.DefinitionId,
            Status: selector.Status,
            RunKind: selector.RunKind,
            From: selector.From,
            To: selector.To,
            CorrelationId: selector.CorrelationId,
            WorkflowExecutionId: selector.WorkflowExecutionId,
            ArtifactId: selector.ArtifactId);
        return WorkflowExecutionStateHistory.Matches(state, query);
    }

    private static string EncodeAlterationCaptureCursor(string executionId, WorkflowExecutionAlterationCaptureQuery query)
    {
        var value = string.Join('|', "v1", Convert.ToBase64String(Encoding.UTF8.GetBytes(executionId)), AlterationCaptureScope(query));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string DecodeAlterationCaptureCursor(string cursor, WorkflowExecutionAlterationCaptureQuery query)
    {
        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(base64)).Split('|');
            if (parts.Length != 3 || parts[0] != "v1" || !StringComparer.Ordinal.Equals(parts[2], AlterationCaptureScope(query)))
                throw new FormatException();
            return Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]));
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new ArgumentException("The alteration capture cursor is invalid or does not belong to this query.", nameof(cursor), exception);
        }
    }

    private static string AlterationCaptureScope(WorkflowExecutionAlterationCaptureQuery query) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            query.TenantPartition,
            query.SystemIdentity,
            query.RootInitiator,
            query.AuthorityPartitionKey,
            AuthorityMetadata = query.AuthorityMetadata.OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new { entry.Key, entry.Value }),
            query.Selector.DefinitionId,
            query.Selector.Status,
            query.Selector.RunKind,
            FromTicks = query.Selector.From?.UtcTicks,
            ToTicks = query.Selector.To?.UtcTicks,
            query.Selector.CorrelationId,
            query.Selector.WorkflowExecutionId,
            query.Selector.ArtifactId,
            query.Selector.MatchAllAuthorized
        })));

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
