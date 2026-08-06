using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class InMemoryExecutionLivenessStateStore : InMemoryKeyedStateStore<InMemoryExecutionLivenessStateStore.ExecutionLivenessStateKey, ExecutionLivenessState>, IExecutionLivenessStateStore
{
    private readonly Dictionary<ExecutionLivenessStateKey, long> _revisions = new();

    public override bool IsAffected(InMemoryCheckpointMutationPlan plan) =>
        plan.OperationalStateIds.Count > 0 || plan.HasExpectedFence;

    private protected override bool IsCheckpointKey(ExecutionLivenessStateKey key, InMemoryCheckpointMutationPlan scope) =>
        StringComparer.Ordinal.Equals(key.WorkflowExecutionId, scope.WorkflowExecutionId) &&
        scope.OperationalStateIds.Contains(key.OperationalStateId);

    private protected override IEnumerable<ExecutionLivenessStateKey> CheckpointKeys(InMemoryCheckpointMutationPlan scope) =>
        scope.OperationalStateIds.Select(id => new ExecutionLivenessStateKey(scope.WorkflowExecutionId, id))
            .Concat(scope.HasExpectedFence
                ? [new ExecutionLivenessStateKey(scope.WorkflowExecutionId, GetOwnershipStateId(scope.WorkflowExecutionId))]
                : []);

    private protected override object CaptureCheckpointAdditionalState(InMemoryCheckpointMutationPlan scope, IReadOnlySet<ExecutionLivenessStateKey> keys) =>
        new RevisionSnapshot(keys.ToHashSet(), _revisions.Where(entry => keys.Contains(entry.Key)).ToDictionary());

    protected override void RestoreCheckpointAdditionalState(object? snapshot)
    {
        var revisions = (RevisionSnapshot)snapshot!;
        foreach (var key in revisions.Keys)
            _revisions.Remove(key);
        foreach (var entry in revisions.Revisions)
            _revisions[entry.Key] = entry.Value;
    }

    private sealed record RevisionSnapshot(
        HashSet<ExecutionLivenessStateKey> Keys,
        Dictionary<ExecutionLivenessStateKey, long> Revisions);

    public ValueTask<ExecutionLivenessState> SaveAsync(ExecutionLivenessState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.OperationalStateId);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(SaveCore(state));
    }

    public ValueTask<ExecutionLivenessStateWriteResult> TrySaveAsync(
        ExecutionLivenessState state,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ValidateState(state);
        if (expectedRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(TrySaveCore(state, expectedRevision));
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

        var states = SnapshotAll()
            .OrderBy(state => state.WorkflowExecutionId, StringComparer.Ordinal)
            .ThenBy(state => state.OperationalStateId, StringComparer.Ordinal)
            .ToArray();
        return new(CreatePage(query, states));
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

    private static RuntimeStorePage<ExecutionLivenessState> CreatePage(
        RuntimeStorePageRequest query,
        IReadOnlyList<ExecutionLivenessState> states)
    {
        var offset = ParseOffset(query.ContinuationToken);
        var items = states.Skip(offset).Take(query.Limit).ToArray();
        var nextOffset = checked(offset + items.Length);
        return new(query, items, nextOffset < states.Count ? nextOffset.ToString() : null);
    }

    private static int ParseOffset(string? continuationToken)
    {
        if (continuationToken is null)
            return 0;
        if (!int.TryParse(continuationToken, out var offset) || offset < 0)
            throw new ArgumentException("The execution-liveness page continuation is invalid.", nameof(continuationToken));
        return offset;
    }
}
