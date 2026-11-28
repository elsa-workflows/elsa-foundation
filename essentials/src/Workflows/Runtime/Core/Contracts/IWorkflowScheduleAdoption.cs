using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Adds trusted Kernel schedule claims to an existing publication using its physical activation transaction.</summary>
public interface IWorkflowScheduleAdoption
{
    /// <summary>
    /// Verifies the exact slot and source, repairs an interrupted empty projection when necessary, and commits its
    /// schedule claims, ingress reservations and desired controls together. Returns false if authority moved.
    /// </summary>
    ValueTask<bool> TryAdoptSchedulesAsync(WorkflowActivationSlot slot, WorkflowExecutableSourceReference source,
        IReadOnlyCollection<WorkflowTriggerBinding> claims, IReadOnlyCollection<string> alsoServing,
        CancellationToken cancellationToken = default);
}
