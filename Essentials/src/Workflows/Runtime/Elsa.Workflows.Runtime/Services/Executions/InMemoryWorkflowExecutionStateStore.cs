using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.Checkpoints;

namespace Elsa.Workflows.Runtime.Services.Executions;

public sealed class InMemoryWorkflowExecutionStateStore : InMemoryKeyedStateStore<string, WorkflowExecutionState>, IWorkflowExecutionStateStore
{
    private readonly string? _cursorScope;
    private readonly InMemoryRuntimeCheckpointStoreState _checkpointState;
    private readonly IExecutionLivenessStateStore? _executionLivenessStateStore;

    public InMemoryWorkflowExecutionStateStore()
        : this(new InMemoryRuntimeCheckpointStoreState(), new InMemoryExecutionLivenessStateStore(), null)
    {
    }

    /// <summary>Creates a history store whose cursors also bind an externally applied result-set predicate.</summary>
    public InMemoryWorkflowExecutionStateStore(string? cursorScope)
        : this(new InMemoryRuntimeCheckpointStoreState(), new InMemoryExecutionLivenessStateStore(), cursorScope)
    {
    }

    public InMemoryWorkflowExecutionStateStore(
        InMemoryRuntimeCheckpointStoreState checkpointState,
        IExecutionLivenessStateStore? executionLivenessStateStore,
        string? cursorScope = null)
        : base(StringComparer.Ordinal)
    {
        ArgumentNullException.ThrowIfNull(checkpointState);
        _checkpointState = checkpointState;
        _executionLivenessStateStore = executionLivenessStateStore;
        _cursorScope = cursorScope;
    }

    public async ValueTask<WorkflowExecutionState> SaveAsync(WorkflowExecutionState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        return await _checkpointState.ExecuteWriteGateAsync(async token =>
        {
            if (_executionLivenessStateStore is not InMemoryExecutionLivenessStateStore livenessStore)
                return Save(state.WorkflowExecutionId, state);

            return await livenessStore.ExecuteOwnershipAtomicAsync(async ownershipToken =>
            {
                ownershipToken.ThrowIfCancellationRequested();
                var ownership = await livenessStore.FindAsync(
                    state.WorkflowExecutionId,
                    RuntimeExecutionOwnershipStateId.For(state.WorkflowExecutionId),
                    ownershipToken);
                ThrowIfDeletionTombstoned(ownership);
                return Save(state.WorkflowExecutionId, state);
            }, token);
        }, cancellationToken);
    }

    public async ValueTask<bool> IsDeletionTombstonedAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();
        if (_executionLivenessStateStore is null) return false;
        var ownership = await _executionLivenessStateStore.FindAsync(workflowExecutionId,
            RuntimeExecutionOwnershipStateId.For(workflowExecutionId), cancellationToken);
        if (ownership?.Metadata.TryGetValue(RuntimeMetadataKeys.WorkflowExecutionDeletionTombstone, out var value) != true)
            return false;
        return value == "true" ? true : throw new InvalidDataException("The persisted workflow-execution deletion tombstone is corrupt.");
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

    public async ValueTask<bool> DeleteAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        return await _checkpointState.ExecuteWriteGateAsync(async token =>
        {
            if (Find(workflowExecutionId) is not { } current || await HasControlDependenciesAsync(current, token))
                return false;

            if (_executionLivenessStateStore is InMemoryExecutionLivenessStateStore inMemoryLivenessStore)
                return await inMemoryLivenessStore.ExecuteWorkflowDeletionAtomicAsync(
                    workflowExecutionId,
                    _ => ValueTask.FromResult(DeleteExecutionAndCancelOutstandingWork(workflowExecutionId)),
                    token);

            return DeleteExecutionAndCancelOutstandingWork(workflowExecutionId);
        }, cancellationToken);
    }

    internal InMemoryRuntimeCheckpointStoreState CheckpointState => _checkpointState;

    internal IExecutionLivenessStateStore? ExecutionLivenessStateStore => _executionLivenessStateStore;

    internal async ValueTask<WorkflowExecutionDeletionResult> TryDeleteTerminalAsync(
        WorkflowExecutionDeletionCandidate candidate,
        bool concurrencyTokenIsValid,
        Action cleanup,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(cleanup);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidate.State.WorkflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        if (_executionLivenessStateStore is not InMemoryExecutionLivenessStateStore livenessStore)
            return WorkflowExecutionDeletionResult.Unsupported;

        return await _checkpointState.ExecuteWriteGateAsync(async token =>
        {
            return await livenessStore.ExecuteOwnershipAtomicAsync(async ownershipToken =>
            {
                // Candidate validation and the deletion tombstone share both the checkpoint and ownership gates
                // with direct SaveAsync calls and checkpoint commits. Once cleanup starts, no supported writer can
                // replace this snapshot and cancellation cannot interrupt the synchronous purge.
                var current = Find(candidate.State.WorkflowExecutionId);
                if (current is null)
                    return WorkflowExecutionDeletionResult.NotFound;
                if (!concurrencyTokenIsValid || !ReferenceEquals(current, candidate.State))
                    return WorkflowExecutionDeletionResult.Changed;
                if (!current.Status.IsTerminal())
                    return WorkflowExecutionDeletionResult.NotTerminal;
                if (await HasControlDependenciesAsync(current, token))
                    return WorkflowExecutionDeletionResult.DependencyRetained;

                return await livenessStore.ExecuteWorkflowDeletionAtomicAsync(
                    current.WorkflowExecutionId,
                    _ =>
                    {
                        CancelOutstandingBackgroundWork(current.WorkflowExecutionId);
                        cleanup();
                        return ValueTask.FromResult(Remove(current.WorkflowExecutionId)
                            ? WorkflowExecutionDeletionResult.Deleted
                            : throw new InvalidOperationException("The workflow execution disappeared inside the deletion gate."));
                    },
                    CancellationToken.None);
            }, token);
        }, cancellationToken);
    }

    private async ValueTask<bool> HasControlDependenciesAsync(WorkflowExecutionState state, CancellationToken cancellationToken)
    {
        WorkflowDispatchRecord[] links;
        lock (_checkpointState.SyncRoot)
            links = _checkpointState.WorkflowDispatches.Values.Where(dispatch =>
                dispatch.Mode == WorkflowDispatchMode.WaitForCompletion &&
                (dispatch.Metadata.ContainsKey(RuntimeMetadataKeys.ChildControlConsumerKey) ||
                 dispatch.Metadata.ContainsKey(RuntimeMetadataKeys.ChildControlConsumerVersion))).ToArray();
        var incoming = links.Where(link => link.ChildWorkflowExecutionId == state.WorkflowExecutionId).ToArray();
        if (state.SystemMetadata.TryGetValue(RuntimeMetadataKeys.ChildControlDispatchId, out var hint) &&
            (incoming.Length != 1 || incoming[0].DispatchId != hint || incoming[0].ParentWorkflowExecutionId != state.ParentWorkflowExecutionId))
            return true;
        foreach (var child in SnapshotAll().Where(child => child.ParentWorkflowExecutionId == state.WorkflowExecutionId &&
                     child.SystemMetadata.ContainsKey(RuntimeMetadataKeys.ChildControlDispatchId)))
            if (!links.Any(link => link.ParentWorkflowExecutionId == state.WorkflowExecutionId &&
                    link.ChildWorkflowExecutionId == child.WorkflowExecutionId &&
                    link.DispatchId == child.SystemMetadata[RuntimeMetadataKeys.ChildControlDispatchId])) return true;
        foreach (var link in incoming)
            if (!await IsDeletionTombstonedAsync(link.ParentWorkflowExecutionId, cancellationToken))
                return true; // Absence alone cannot release exact caller/root authority.
        foreach (var link in links.Where(link => link.ParentWorkflowExecutionId == state.WorkflowExecutionId))
        {
            if (!state.Status.IsTerminal()) return true;
            var child = Find(link.ChildWorkflowExecutionId);
            if (child is null ? (link.Status != WorkflowDispatchStatus.DispatchFailed && !WorkflowDispatchLifecycle.WasCancelledBeforeAdmission(link)) : !child.Status.IsTerminal())
                return true;
            lock (_checkpointState.SyncRoot)
                if (_checkpointState.OutboxItems.Values.Any(item => item.Status != RuntimePostCommitOutboxStatus.Delivered &&
                    (item.Intent.WorkflowExecutionId == link.ChildWorkflowExecutionId ||
                     item.Intent.Metadata.TryGetValue(RuntimeMetadataKeys.DispatchId, out var id) && id == link.DispatchId)))
                    return true;
        }
        return false;
    }

    private bool DeleteExecutionAndCancelOutstandingWork(string workflowExecutionId)
    {
        CancelOutstandingBackgroundWork(workflowExecutionId);
        return Remove(workflowExecutionId);
    }

    private void CancelOutstandingBackgroundWork(string workflowExecutionId)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_checkpointState.SyncRoot)
        {
            // In-memory execution state is keyed only by execution ID, so deletion remains process-global and
            // tombstones every scoped ledger row for that execution ID. The EF store performs scoped deletion.
            var cancellations = _checkpointState.BackgroundActivityWorkItems
                .Where(pair => StringComparer.Ordinal.Equals(pair.Key.WorkflowExecutionId, workflowExecutionId) &&
                               pair.Value.Status is BackgroundActivityWorkStatus.Pending or
                                   BackgroundActivityWorkStatus.Claimed or
                                   BackgroundActivityWorkStatus.ResultReady)
                .Select(pair => (pair.Key, Item: BackgroundActivityWorkLifecycle.Cancel(pair.Value, now)))
                .ToArray();
            foreach (var cancellation in cancellations)
                _checkpointState.BackgroundActivityWorkItems[cancellation.Key] = cancellation.Item;
        }
    }

    private static void ThrowIfDeletionTombstoned(ExecutionLivenessState? ownership)
    {
        if (ownership?.Metadata.TryGetValue(RuntimeMetadataKeys.WorkflowExecutionDeletionTombstone, out var tombstone) != true)
            return;
        if (!StringComparer.Ordinal.Equals(tombstone, "true"))
            throw new InvalidDataException("The persisted workflow-execution deletion tombstone is corrupt.");
        throw new InvalidOperationException("A deleted workflow execution cannot be recreated by saving its execution state.");
    }

    private WorkflowExecutionStatePage QueryPage(
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

    private string EncodeCursor(
        WorkflowExecutionState state,
        WorkflowExecutionStatePageQuery query)
    {
        var value = string.Join(
            '|',
            "v1",
            WorkflowExecutionStateHistory.SortTimestamp(state).UtcTicks.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(Encoding.UTF8.GetBytes(state.WorkflowExecutionId)),
            PageCursorScope(query));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private InMemoryCursor DecodeCursor(
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
                !StringComparer.Ordinal.Equals(parts[3], PageCursorScope(query)))
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

    private string PageCursorScope(WorkflowExecutionStatePageQuery query)
    {
        var scope = WorkflowExecutionStateHistory.Scope(query);
        return _cursorScope is null
            ? scope
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{scope}:{_cursorScope}")));
    }

    private sealed record InMemoryCursor(DateTimeOffset SortTimestamp, string WorkflowExecutionId);
}
