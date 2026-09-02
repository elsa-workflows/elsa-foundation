using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class InMemoryExecutionLivenessStateStore : InMemoryKeyedStateStore<InMemoryExecutionLivenessStateStore.ExecutionLivenessStateKey, ExecutionLivenessState>, IExecutionLivenessStateStore, IRuntimeRecoveryLivenessPageSource
{
    private readonly SemaphoreSlim _ownershipAtomicGate = new(1, 1);
    private readonly Dictionary<ExecutionLivenessStateKey, long> _revisions = new();

    public async ValueTask<ExecutionLivenessState> SaveAsync(ExecutionLivenessState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.OperationalStateId);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsOwnershipState(state))
            return SaveCore(state);

        return await ExecuteOwnershipAtomicAsync(
            _ => ValueTask.FromResult(SaveCore(state)),
            cancellationToken);
    }

    public async ValueTask<ExecutionLivenessStateWriteResult> TrySaveAsync(
        ExecutionLivenessState state,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ValidateState(state);
        if (expectedRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsOwnershipState(state))
            return TrySaveCore(state, expectedRevision);

        return await ExecuteOwnershipAtomicAsync(
            _ => ValueTask.FromResult(TrySaveCore(state, expectedRevision)),
            cancellationToken);
    }

    internal async ValueTask<TResult> ExecuteOwnershipAtomicAsync<TResult>(
        Func<CancellationToken, ValueTask<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        await _ownershipAtomicGate.WaitAsync(cancellationToken);
        try
        {
            return await action(cancellationToken);
        }
        finally
        {
            _ownershipAtomicGate.Release();
        }
    }

    internal static string GetOwnershipStateId(string workflowExecutionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        return $"ownership:{workflowExecutionId}";
    }

    private ExecutionLivenessState SaveCore(ExecutionLivenessState state)
    {
        var key = Key(state);
        return Mutate(key, _ =>
        {
            _revisions.TryGetValue(key, out var revision);
            _revisions[key] = checked(revision + 1);
            return (state, state);
        });
    }

    private ExecutionLivenessStateWriteResult TrySaveCore(
        ExecutionLivenessState state,
        long expectedRevision)
    {
        var key = Key(state);
        return ConditionalMutate(key, existing =>
        {
            _revisions.TryGetValue(key, out var currentRevision);
            if (expectedRevision == 0 && existing is not null)
                return (false, null, new ExecutionLivenessStateWriteResult(ExecutionLivenessStateWriteStatus.RevisionConflict, currentRevision));
            if (expectedRevision > 0 && existing is null)
                return (false, null, new ExecutionLivenessStateWriteResult(ExecutionLivenessStateWriteStatus.NotFound));
            if (expectedRevision > 0 && currentRevision != expectedRevision)
                return (false, null, new ExecutionLivenessStateWriteResult(ExecutionLivenessStateWriteStatus.RevisionConflict, currentRevision));

            var nextRevision = checked(currentRevision + 1);
            _revisions[key] = nextRevision;
            return (true, state, new ExecutionLivenessStateWriteResult(ExecutionLivenessStateWriteStatus.Saved, nextRevision));
        });
    }

    public ValueTask<ExecutionLivenessState?> FindAsync(string workflowExecutionId, string operationalStateId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationalStateId);
        cancellationToken.ThrowIfCancellationRequested();

        return new(Find(new ExecutionLivenessStateKey(workflowExecutionId, operationalStateId)));
    }

    public ValueTask<VersionedExecutionLivenessState?> FindVersionedAsync(
        string workflowExecutionId,
        string operationalStateId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workflowExecutionId, operationalStateId);
        cancellationToken.ThrowIfCancellationRequested();

        var key = new ExecutionLivenessStateKey(workflowExecutionId, operationalStateId);
        return new(Read<VersionedExecutionLivenessState?>(key, state =>
        {
            if (state is null)
                return null;
            return new VersionedExecutionLivenessState(state, _revisions[key]);
        }));
    }

    public async ValueTask<IReadOnlyCollection<ExecutionLivenessState>> ListAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken = default) =>
        await RuntimeOperationalStorePagingExtensions.ListAllAsync(this, workflowExecutionId, cancellationToken);

    public ValueTask<RuntimeStorePage<ExecutionLivenessState>> ListPageAsync(
        ExecutionLivenessStatePageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var states = Snapshot(key => key.WorkflowExecutionId == query.WorkflowExecutionId)
            .OrderBy(state => state.OperationalStateId, StringComparer.Ordinal)
            .ToArray();
        return new(CreatePage(query, states));
    }

    public async ValueTask<IReadOnlyCollection<ExecutionLivenessState>> ListAllAsync(
        CancellationToken cancellationToken = default) =>
        await RuntimeOperationalStorePagingExtensions.ListAllAsync(this, cancellationToken);

    public ValueTask<RuntimeStorePage<ExecutionLivenessState>> ListAllPageAsync(
        RuntimeStorePageRequest query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var cursor = ParseGlobalCursor(query.ContinuationToken);
        return new(ReadValues(states => CreateGlobalPage(query, states, cursor)));
    }

    ValueTask<RuntimeStorePage<ExecutionLivenessState>> IRuntimeRecoveryLivenessPageSource.ListRecoveryPageAsync(
        RuntimeRecoveryScanRequest request,
        RuntimeStorePageRequest query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var cursor = ParseRecoveryCursor(query.ContinuationToken);
        return new(ReadValues(states => CreateRecoveryPage(query, request, states, cursor)));
    }

    public readonly record struct ExecutionLivenessStateKey(string WorkflowExecutionId, string OperationalStateId);

    private static ExecutionLivenessStateKey Key(ExecutionLivenessState state)
    {
        ValidateState(state);
        return new(state.WorkflowExecutionId, state.OperationalStateId);
    }

    private static void ValidateState(ExecutionLivenessState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateIdentity(state.WorkflowExecutionId, state.OperationalStateId);
    }

    private static void ValidateIdentity(string workflowExecutionId, string operationalStateId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationalStateId);
    }

    private static bool IsOwnershipState(ExecutionLivenessState state) =>
        StringComparer.Ordinal.Equals(
            state.OperationalStateId,
            GetOwnershipStateId(state.WorkflowExecutionId));

    private static RuntimeStorePage<ExecutionLivenessState> CreatePage(
        RuntimeStorePageRequest query,
        IReadOnlyList<ExecutionLivenessState> states)
    {
        var offset = ParseOffset(query.ContinuationToken);
        var items = states.Skip(offset).Take(query.Limit).ToArray();
        var nextOffset = checked(offset + items.Length);
        return new(query, items, nextOffset < states.Count ? nextOffset.ToString() : null);
    }

    private static RuntimeStorePage<ExecutionLivenessState> CreateGlobalPage(
        RuntimeStorePageRequest query,
        IEnumerable<ExecutionLivenessState> states,
        GlobalPageCursor? cursor)
    {
        // Keep only the requested page plus one look-ahead row. This intentionally avoids OrderBy, whose deferred
        // implementation buffers every liveness state before Take can apply the page bound.
        var selected = new List<ExecutionLivenessState>(query.Limit + 1);
        foreach (var state in states)
        {
            if (cursor is not null && Compare(state, cursor) <= 0)
                continue;

            var insertionPoint = selected.BinarySearch(state, GlobalStateComparer.Instance);
            if (insertionPoint < 0)
                insertionPoint = ~insertionPoint;
            selected.Insert(insertionPoint, state);
            if (selected.Count > query.Limit + 1)
                selected.RemoveAt(selected.Count - 1);
        }

        var items = selected.Take(query.Limit).ToArray();
        var next = selected.Count > query.Limit
            ? EncodeGlobalCursor(items[^1])
            : null;
        return new(query, items, next);
    }

    private static RuntimeStorePage<ExecutionLivenessState> CreateRecoveryPage(
        RuntimeStorePageRequest query,
        RuntimeRecoveryScanRequest request,
        IEnumerable<ExecutionLivenessState> states,
        RecoveryPageCursor? cursor)
    {
        // The scanner's provider page is ordered by the recovery key, not the ordinary ID key. Keep only one page
        // plus a look-ahead row while evaluating every live value under the existing store lock; this prevents an
        // eligible tail row from being dropped at the 500-row boundary without materializing all state objects.
        var selected = new List<RecoverySelection>(query.Limit + 1);
        foreach (var state in states)
        {
            var eligibleAt = RuntimeRecoveryCandidateSelector.GetEligibleAt(state, request);
            if (eligibleAt is null || cursor is not null && Compare(eligibleAt.Value, state, cursor) <= 0)
                continue;

            var selection = new RecoverySelection(state, eligibleAt.Value);
            var insertionPoint = selected.BinarySearch(selection, RecoverySelectionComparer.Instance);
            if (insertionPoint < 0)
                insertionPoint = ~insertionPoint;
            selected.Insert(insertionPoint, selection);
            if (selected.Count > query.Limit + 1)
                selected.RemoveAt(selected.Count - 1);
        }

        var items = selected.Take(query.Limit).Select(selection => selection.State).ToArray();
        var next = selected.Count > query.Limit
            ? EncodeRecoveryCursor(selected[query.Limit - 1])
            : null;
        return new(query, items, next);
    }

    private static int Compare(ExecutionLivenessState state, GlobalPageCursor cursor)
    {
        var comparison = StringComparer.Ordinal.Compare(state.WorkflowExecutionId, cursor.WorkflowExecutionId);
        return comparison != 0
            ? comparison
            : StringComparer.Ordinal.Compare(state.OperationalStateId, cursor.OperationalStateId);
    }

    private static string EncodeGlobalCursor(ExecutionLivenessState state)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new GlobalPageCursor(
            state.WorkflowExecutionId,
            state.OperationalStateId));
        return Convert.ToBase64String(payload)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string EncodeRecoveryCursor(RecoverySelection selection)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new RecoveryPageCursor(
            selection.EligibleAt,
            selection.State.WorkflowExecutionId,
            selection.State.OperationalStateId));
        return Convert.ToBase64String(payload)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static GlobalPageCursor? ParseGlobalCursor(string? continuationToken)
    {
        if (continuationToken is null)
            return null;

        try
        {
            var base64 = continuationToken.Replace('-', '+').Replace('_', '/');
            base64 += (base64.Length % 4) switch
            {
                0 => "",
                2 => "==",
                3 => "=",
                _ => throw new FormatException()
            };
            var cursor = JsonSerializer.Deserialize<GlobalPageCursor>(Convert.FromBase64String(base64));
            if (cursor is null || string.IsNullOrWhiteSpace(cursor.WorkflowExecutionId) || string.IsNullOrWhiteSpace(cursor.OperationalStateId))
                throw new FormatException();
            return cursor;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw new ArgumentException("The execution-liveness page continuation is invalid.", nameof(continuationToken), exception);
        }
    }

    private static RecoveryPageCursor? ParseRecoveryCursor(string? continuationToken)
    {
        if (continuationToken is null)
            return null;

        try
        {
            var base64 = continuationToken.Replace('-', '+').Replace('_', '/');
            base64 += (base64.Length % 4) switch
            {
                0 => "",
                2 => "==",
                3 => "=",
                _ => throw new FormatException()
            };
            var cursor = JsonSerializer.Deserialize<RecoveryPageCursor>(Convert.FromBase64String(base64));
            if (cursor is null || string.IsNullOrWhiteSpace(cursor.WorkflowExecutionId) || string.IsNullOrWhiteSpace(cursor.OperationalStateId))
                throw new FormatException();
            return cursor;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw new ArgumentException("The execution-liveness recovery continuation is invalid.", nameof(continuationToken), exception);
        }
    }

    private static int Compare(DateTimeOffset eligibleAt, ExecutionLivenessState state, RecoveryPageCursor cursor)
    {
        var comparison = eligibleAt.CompareTo(cursor.EligibleAt);
        if (comparison != 0)
            return comparison;

        comparison = StringComparer.Ordinal.Compare(state.WorkflowExecutionId, cursor.WorkflowExecutionId);
        return comparison != 0
            ? comparison
            : StringComparer.Ordinal.Compare(state.OperationalStateId, cursor.OperationalStateId);
    }

    private static int ParseOffset(string? continuationToken)
    {
        if (continuationToken is null)
            return 0;
        if (!int.TryParse(continuationToken, out var offset) || offset < 0)
            throw new ArgumentException("The execution-liveness page continuation is invalid.", nameof(continuationToken));
        return offset;
    }

    private sealed record GlobalPageCursor(string WorkflowExecutionId, string OperationalStateId);

    private sealed record RecoveryPageCursor(
        DateTimeOffset EligibleAt,
        string WorkflowExecutionId,
        string OperationalStateId);

    private sealed record RecoverySelection(ExecutionLivenessState State, DateTimeOffset EligibleAt);

    private sealed class RecoverySelectionComparer : IComparer<RecoverySelection>
    {
        public static RecoverySelectionComparer Instance { get; } = new();

        public int Compare(RecoverySelection? x, RecoverySelection? y)
        {
            ArgumentNullException.ThrowIfNull(x);
            ArgumentNullException.ThrowIfNull(y);
            var comparison = x.EligibleAt.CompareTo(y.EligibleAt);
            if (comparison != 0)
                return comparison;

            comparison = StringComparer.Ordinal.Compare(x.State.WorkflowExecutionId, y.State.WorkflowExecutionId);
            return comparison != 0
                ? comparison
                : StringComparer.Ordinal.Compare(x.State.OperationalStateId, y.State.OperationalStateId);
        }
    }

    private sealed class GlobalStateComparer : IComparer<ExecutionLivenessState>
    {
        public static GlobalStateComparer Instance { get; } = new();

        public int Compare(ExecutionLivenessState? x, ExecutionLivenessState? y)
        {
            ArgumentNullException.ThrowIfNull(x);
            ArgumentNullException.ThrowIfNull(y);
            var comparison = StringComparer.Ordinal.Compare(x.WorkflowExecutionId, y.WorkflowExecutionId);
            return comparison != 0
                ? comparison
                : StringComparer.Ordinal.Compare(x.OperationalStateId, y.OperationalStateId);
        }
    }
}
