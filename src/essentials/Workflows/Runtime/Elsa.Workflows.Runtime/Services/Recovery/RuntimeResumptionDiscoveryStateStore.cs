using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Recovery;

/// <summary>
/// Process-local <see cref="RuntimeResumptionDiscoveryState"/> per persistence scope, retained between resumption sweeps,
/// and the host's record of which queue types it has already warned about.
/// </summary>
/// <remarks>
/// The state is a fairness hint, not a record of what is owed: queued work stays in the durable queue, so a process
/// restart, or the eviction of a scope beyond <see cref="MaximumEntries"/>, only restarts that scope's backlog walk.
/// Eviction takes the scope used least recently, so scopes that are swept keep their walk however many there are.
/// </remarks>
public sealed class RuntimeResumptionDiscoveryStateStore
{
    public const int MaximumEntries = 1024;

    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<(string Scope, RuntimeResumptionDiscoveryState State)>> _states = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Scope, RuntimeResumptionDiscoveryState State)> _recency = new();
    private readonly HashSet<Type> _warnedQueueTypes = [];

    public RuntimeResumptionDiscoveryState Get(string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        lock (_gate)
        {
            if (!_states.TryGetValue(scope, out var node))
                return new RuntimeResumptionDiscoveryState();

            Touch(node);
            return node.Value.State;
        }
    }

    public void Set(string scope, RuntimeResumptionDiscoveryState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentNullException.ThrowIfNull(state);
        lock (_gate)
        {
            if (_states.TryGetValue(scope, out var node))
            {
                node.Value = (scope, state);
                Touch(node);
                return;
            }

            while (_states.Count >= MaximumEntries && _recency.First is { } leastRecent)
            {
                _recency.RemoveFirst();
                _states.Remove(leastRecent.Value.Scope);
            }

            _states[scope] = _recency.AddLast((scope, state));
        }
    }

    /// <summary>Returns <see langword="true"/> the first time it is asked about <paramref name="queueType"/>.</summary>
    public bool FirstWarningFor(Type queueType)
    {
        ArgumentNullException.ThrowIfNull(queueType);
        lock (_gate)
            return _warnedQueueTypes.Add(queueType);
    }

    private void Touch(LinkedListNode<(string Scope, RuntimeResumptionDiscoveryState State)> node)
    {
        _recency.Remove(node);
        _recency.AddLast(node);
    }
}
