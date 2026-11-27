using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Adds Event registration to an existing publication under its exact slot and source fences.</summary>
internal interface IWorkflowEventAdoption
{
    ValueTask<bool> TryAdoptEventsAsync(WorkflowActivationSlot slot, WorkflowExecutableSourceReference source,
        IReadOnlyList<WorkflowTriggerBinding> claims, CancellationToken cancellationToken);
}
