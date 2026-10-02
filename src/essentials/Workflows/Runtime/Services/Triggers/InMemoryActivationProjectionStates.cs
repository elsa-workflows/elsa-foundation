using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Triggers;

/// <summary>
/// The activation lifecycle both in-memory projection stores keep beside their rows: prepared, serving, and switched
/// off by a replacement. It applies the rules the EF stores apply, so the in-memory stores behave the same (#2193). It
/// holds no lock; each store calls it under its own.
/// </summary>
public sealed class InMemoryActivationProjectionStates
{
    private readonly HashSet<string> _prepared = new(StringComparer.Ordinal);
    private readonly HashSet<string> _active = new(StringComparer.Ordinal);
    private readonly HashSet<string> _replaced = new(StringComparer.Ordinal);

    /// <summary>
    /// Records <paramref name="activationId"/> as prepared. An activation whose projection serves, or has served and is
    /// still stored, is refused: its projection must be deleted (<see cref="Remove"/>) before it is prepared again, as
    /// in the EF stores.
    /// </summary>
    public void Prepare(string activationId, string projection)
    {
        if (Find(activationId) is WorkflowActivationProjectionState.Active or WorkflowActivationProjectionState.Replaced)
            throw new InvalidOperationException(
                $"Activation '{activationId}' cannot be prepared again: its {projection} projection serves or has served, and is still stored. Delete it (DeleteByActivationAsync) first.");

        _prepared.Add(activationId);
    }

    /// <summary>
    /// Switches <paramref name="activationId"/> on and <paramref name="replacedActivationId"/> off, with the rules of the
    /// projection stores' <c>ActivateAsync</c> (<see cref="CheckActivation"/>).
    /// </summary>
    /// <returns><see langword="true"/> when the store must switch its rows.</returns>
    public bool Activate(string activationId, string? replacedActivationId, string projection)
    {
        if (!CheckActivation(activationId, replacedActivationId, projection))
            return false;

        _active.Add(activationId);
        _replaced.Remove(activationId);
        if (Distinct(activationId, replacedActivationId) && _active.Remove(replacedActivationId!))
            _replaced.Add(replacedActivationId!);
        return true;
    }

    /// <summary>
    /// Refuses a switch the projection stores refuse, and changes nothing. The candidate is checked first: once it serves
    /// and its replaced activation does not, the switch is made. A candidate that does not serve yet may not replace an
    /// activation that no longer serves, and a candidate that serves beside its replaced activation is refused too.
    /// </summary>
    /// <returns><see langword="true"/> when the switch is still to be made; <see langword="false"/> when it is made.</returns>
    public bool CheckActivation(string activationId, string? replacedActivationId, string projection)
    {
        var candidate = Find(activationId);
        if (candidate == WorkflowActivationProjectionState.Missing)
            throw new InvalidOperationException($"Activation '{activationId}' has no prepared {projection} projection.");

        var replaced = Distinct(activationId, replacedActivationId) ? Find(replacedActivationId!) : (WorkflowActivationProjectionState?)null;
        if (candidate == WorkflowActivationProjectionState.Active)
        {
            if (replaced == WorkflowActivationProjectionState.Active)
                throw new InvalidOperationException($"Activation '{activationId}' is active while replaced activation '{replacedActivationId}' is still active.");
            return false;
        }

        if (replaced is { } state && state != WorkflowActivationProjectionState.Active)
            throw new InvalidOperationException($"Activation '{activationId}' cannot replace a {projection} projection that is missing or no longer active.");
        return true;
    }

    public void Remove(string activationId)
    {
        _prepared.Remove(activationId);
        _active.Remove(activationId);
        _replaced.Remove(activationId);
    }

    public bool Serves(string activationId) => Find(activationId) == WorkflowActivationProjectionState.Active;

    public WorkflowActivationProjectionState Find(string activationId) =>
        !_prepared.Contains(activationId) ? WorkflowActivationProjectionState.Missing
        : _active.Contains(activationId) ? WorkflowActivationProjectionState.Active
        : _replaced.Contains(activationId) ? WorkflowActivationProjectionState.Replaced
        : WorkflowActivationProjectionState.Prepared;

    private static bool Distinct(string activationId, string? replacedActivationId) =>
        replacedActivationId is not null && !StringComparer.Ordinal.Equals(replacedActivationId, activationId);
}
