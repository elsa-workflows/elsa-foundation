using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Triggers;

/// <summary>
/// The activation lifecycle both in-memory projection stores keep beside their rows: prepared, serving, and switched
/// off by a replacement. It holds no lock; each store calls it under its own.
/// </summary>
internal sealed class InMemoryActivationProjectionStates
{
    private readonly HashSet<string> _prepared = new(StringComparer.Ordinal);
    private readonly HashSet<string> _active = new(StringComparer.Ordinal);
    private readonly HashSet<string> _replaced = new(StringComparer.Ordinal);

    public bool IsPrepared(string activationId) => _prepared.Contains(activationId);

    public void Prepare(string activationId)
    {
        _prepared.Add(activationId);
        _active.Remove(activationId);
        _replaced.Remove(activationId);
    }

    /// <summary>Switches an activation on, or off; only an activation that was serving becomes replaced.</summary>
    public void Switch(string activationId, bool active)
    {
        if (active)
        {
            _active.Add(activationId);
            _replaced.Remove(activationId);
        }
        else if (_active.Remove(activationId))
            _replaced.Add(activationId);
    }

    public void Remove(string activationId)
    {
        _prepared.Remove(activationId);
        _active.Remove(activationId);
        _replaced.Remove(activationId);
    }

    public WorkflowActivationProjectionState Find(string activationId) =>
        !_prepared.Contains(activationId) ? WorkflowActivationProjectionState.Missing
        : _active.Contains(activationId) ? WorkflowActivationProjectionState.Active
        : _replaced.Contains(activationId) ? WorkflowActivationProjectionState.Replaced
        : WorkflowActivationProjectionState.Prepared;
}
