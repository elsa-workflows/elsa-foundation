using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Messaging;

/// <summary>Internal owner preparation/read seam. Presence is not complete coverage or Message capability.</summary>
/// <remarks>The owning switch/backend implements its own physical staging and actual-store qualification.
/// No arbitrary mutation callback, recipient policy, common source plan or public plugin SPI is exposed here.</remarks>
internal interface IMessagePublicationInterestProjection
{
    void RequireActualComposition(IWorkflowActivationAuthority authority, IWorkflowActivationSwitch activationSwitch,
        IWorkflowExecutableSourceReferenceStore references);
    ValueTask PrepareAsync(WorkflowExecutable requested, string activationId, string slotId, string slotName, CancellationToken ct);
    ValueTask<WorkflowActivationProjectionState> FindStateAsync(string activationId, CancellationToken ct);
    ValueTask<IReadOnlyCollection<string>> ListServingActivationIdsAsync(string slotId, CancellationToken ct);
}
