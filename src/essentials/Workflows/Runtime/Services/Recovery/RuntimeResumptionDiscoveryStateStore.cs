using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Recovery;

/// <summary>
/// Process-local <see cref="RuntimeResumptionDiscoveryState"/> per persistence scope, retained between resumption sweeps.
/// </summary>
/// <remarks>
/// The state is a fairness hint, not a record of what is owed: queued work stays in the durable queue, so a process
/// restart, or the eviction of a scope beyond <see cref="MaximumEntries"/>, only restarts that scope's backlog walk.
/// </remarks>
public sealed class RuntimeResumptionDiscoveryStateStore
{
    public const int MaximumEntries = 1024;

    private readonly object _gate = new();
    private readonly Dictionary<string, RuntimeResumptionDiscoveryState> _states = new(StringComparer.Ordinal);
    private readonly Queue<string> _insertionOrder = new();

    public RuntimeResumptionDiscoveryState Get(string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        lock (_gate)
            return _states.TryGetValue(scope, out var state) ? state : new RuntimeResumptionDiscoveryState();
    }

    public void Set(string scope, RuntimeResumptionDiscoveryState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentNullException.ThrowIfNull(state);
        lock (_gate)
        {
            if (!_states.ContainsKey(scope))
            {
                while (_states.Count >= MaximumEntries && _insertionOrder.TryDequeue(out var oldest))
                    _states.Remove(oldest);
                _insertionOrder.Enqueue(scope);
            }

            _states[scope] = state;
        }
    }
}
