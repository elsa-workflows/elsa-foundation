using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Triggers;

/// <summary>
/// What <c>InMemoryWorkflowActivationSwitch</c> drives in each in-memory projection store (#2230). Every member except
/// <see cref="SyncRoot"/> runs under that lock, which the switch holds together with the slot authority's and the other
/// store's, so a slot transition and its projection switch are one step to every reader.
/// </summary>
internal interface IInMemoryActivationProjection
{
    object SyncRoot { get; }

    /// <summary>Where the activation's projection stands in the store.</summary>
    WorkflowActivationProjectionState State(string activationId);

    /// <summary>Throws as the store's <c>ActivateAsync</c> would refuse the switch, and changes nothing.</summary>
    void CheckSwitch(string activationId, string? replacedActivationId);

    /// <summary>Switches as the store's <c>ActivateAsync</c> does, once <see cref="CheckSwitch"/> allowed it.</summary>
    void Switch(string activationId, string? replacedActivationId);

    /// <summary>Deletes the activation's projection, as the store's <c>DeleteByActivationAsync</c> does.</summary>
    void Delete(string activationId);
}
