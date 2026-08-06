namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Shared lock + dictionary core for the in-memory runtime state stores (#415 item 4). Each store
/// previously hand-rolled the identical <c>object _syncRoot</c> + <c>Dictionary&lt;TKey,TState&gt;</c> +
/// locked Save/Find/List/Remove pattern, so a change to the locking discipline (or a switch of the
/// backing collection) had to be repeated in every copy and could silently miss one. Centralizing the
/// primitives here keeps every locked access in one place while each store keeps its exact public
/// contract by delegating to these <see langword="protected"/> members.
/// </summary>
/// <remarks>
/// The store keeps its own key derivation (composite keys are private record structs) and its own
/// argument validation, then calls into these primitives. All primitives take the store lock; callers
/// must not hold it when calling in, and must not leak the returned collections back under the lock.
/// </remarks>
public abstract class InMemoryKeyedStateStore<TKey, TState> : IInMemoryCheckpointTransactionParticipant
    where TKey : notnull
    where TState : class
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<TKey, TState> _states;

    public InMemoryCheckpointParticipantGate TransactionGate { get; } = new();

    /// <summary>Initializes the store, optionally with a key comparer (e.g. <see cref="StringComparer.Ordinal"/>).</summary>
    protected InMemoryKeyedStateStore(IEqualityComparer<TKey>? keyComparer = null)
    {
        _states = new(keyComparer);
    }

    /// <summary>Upserts <paramref name="state"/> under <paramref name="key"/> and returns it.</summary>
    protected TState Save(TKey key, TState state)
    {
        using var gate = TransactionGate.Enter();
        lock (_syncRoot)
        {
            _states[key] = state;
            return state;
        }
    }

    /// <summary>Adds <paramref name="state"/> under <paramref name="key"/> only if absent; returns whether it was added.</summary>
    protected bool TryAdd(TKey key, TState state)
    {
        using var gate = TransactionGate.Enter();
        lock (_syncRoot)
            return _states.TryAdd(key, state);
    }

    /// <summary>Returns the state under <paramref name="key"/>, or <see langword="null"/> if absent.</summary>
    protected TState? Find(TKey key)
    {
        using var gate = TransactionGate.Enter();
        lock (_syncRoot)
        {
            _states.TryGetValue(key, out var state);
            return state;
        }
    }

    /// <summary>Reads one value and projects it while holding the same lock used by all mutations.</summary>
    protected TResult Read<TResult>(TKey key, Func<TState?, TResult> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        using var gate = TransactionGate.Enter();
        lock (_syncRoot)
        {
            _states.TryGetValue(key, out var state);
            return read(state);
        }
    }

    /// <summary>
    /// Atomically derives a replacement and result from the current value. The replacement is committed before the
    /// result is returned, allowing specialized in-memory stores to model provider compare-and-swap semantics.
    /// </summary>
    protected TResult Mutate<TResult>(TKey key, Func<TState?, (TState State, TResult Result)> mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        using var gate = TransactionGate.Enter();
        lock (_syncRoot)
        {
            _states.TryGetValue(key, out var current);
            var (state, result) = mutation(current);
            _states[key] = state;
            return result;
        }
    }

    /// <summary>
    /// Atomically inspects the current value and optionally commits a replacement. This models a rejected conditional
    /// provider write without forcing callers to re-enter the store lock or rewrite the unchanged value.
    /// </summary>
    protected TResult ConditionalMutate<TResult>(
        TKey key,
        Func<TState?, (bool ShouldSave, TState? State, TResult Result)> mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        using var gate = TransactionGate.Enter();
        lock (_syncRoot)
        {
            _states.TryGetValue(key, out var current);
            var (shouldSave, state, result) = mutation(current);
            if (shouldSave)
                _states[key] = state ?? throw new InvalidOperationException("A conditional mutation cannot save a null state.");
            return result;
        }
    }

    /// <summary>Removes the entry under <paramref name="key"/>; returns whether it existed.</summary>
    protected bool Remove(TKey key)
    {
        using var gate = TransactionGate.Enter();
        lock (_syncRoot)
            return _states.Remove(key);
    }

    /// <summary>Materializes every stored value under the lock into a stable snapshot array.</summary>
    protected IReadOnlyCollection<TState> SnapshotAll()
    {
        using var gate = TransactionGate.Enter();
        lock (_syncRoot)
            return _states.Values.ToArray();
    }

    /// <summary>Materializes the stored values whose key satisfies <paramref name="keyPredicate"/> into a snapshot array.</summary>
    protected IReadOnlyCollection<TState> Snapshot(Func<TKey, bool> keyPredicate)
    {
        using var gate = TransactionGate.Enter();
        lock (_syncRoot)
        {
            return _states
                .Where(entry => keyPredicate(entry.Key))
                .Select(entry => entry.Value)
                .ToArray();
        }
    }

    /// <summary>Materializes the stored values that satisfy <paramref name="valuePredicate"/> into a snapshot array.</summary>
    protected IReadOnlyCollection<TState> SnapshotValues(Func<TState, bool> valuePredicate)
    {
        using var gate = TransactionGate.Enter();
        lock (_syncRoot)
        {
            return _states.Values
                .Where(valuePredicate)
                .ToArray();
        }
    }

    /// <summary>Reads the live value collection under the store lock without first duplicating the whole store.</summary>
    protected TResult ReadValues<TResult>(Func<IEnumerable<TState>, TResult> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        using var gate = TransactionGate.Enter();
        lock (_syncRoot)
            return read(_states.Values);
    }

    /// <summary>Captures derived mutable indexes that participate in the same checkpoint rollback.</summary>
    protected virtual object? CaptureCheckpointAdditionalState() => null;

    /// <summary>Restores derived mutable indexes captured by <see cref="CaptureCheckpointAdditionalState"/>.</summary>
    protected virtual void RestoreCheckpointAdditionalState(object? snapshot)
    {
    }

    /// <summary>Selects only keys this checkpoint can mutate, preserving unrelated concurrent writes on rollback.</summary>
    private protected virtual bool IsCheckpointKey(TKey key, InMemoryCheckpointMutationPlan plan) => false;

    public virtual bool IsAffected(InMemoryCheckpointMutationPlan plan) =>
        CheckpointKeys(plan).Any() || Snapshot(key => IsCheckpointKey(key, plan)).Count > 0;

    object IInMemoryCheckpointTransactionParticipant.CaptureCheckpointState(InMemoryCheckpointMutationPlan plan)
    {
        lock (_syncRoot)
        {
            var keys = _states.Keys.Where(key => IsCheckpointKey(key, plan)).ToHashSet(_states.Comparer);
            foreach (var key in CheckpointKeys(plan))
                keys.Add(key);
            var states = _states.Where(entry => keys.Contains(entry.Key)).ToDictionary(entry => entry.Key, entry => entry.Value, _states.Comparer);
            return new CheckpointSnapshot(keys, states, CaptureCheckpointAdditionalState(plan, keys));
        }
    }

    void IInMemoryCheckpointTransactionParticipant.RestoreCheckpointState(object snapshot)
    {
        var checkpoint = (CheckpointSnapshot)snapshot;
        lock (_syncRoot)
        {
            foreach (var key in checkpoint.Keys)
                _states.Remove(key);
            foreach (var entry in checkpoint.States)
                _states[entry.Key] = entry.Value;
            RestoreCheckpointAdditionalState(checkpoint.AdditionalState);
        }
    }

    private sealed record CheckpointSnapshot(
        HashSet<TKey> Keys,
        Dictionary<TKey, TState> States,
        object? AdditionalState);

    private protected virtual IEnumerable<TKey> CheckpointKeys(InMemoryCheckpointMutationPlan plan) => [];

    private protected virtual object? CaptureCheckpointAdditionalState(InMemoryCheckpointMutationPlan plan, IReadOnlySet<TKey> keys) =>
        CaptureCheckpointAdditionalState();
}
