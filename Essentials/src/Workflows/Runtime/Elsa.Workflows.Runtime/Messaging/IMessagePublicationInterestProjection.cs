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
    void RequireModelComposition(IWorkflowExecutableStore material, IWorkflowExecutableRootWriteLeaseManager leases,
        TimeProvider clock, IWorkflowActivationSwitch activationSwitch, IWorkflowTriggerBindingStore bindings, IRecurringTriggerScheduleStore? schedules, IPersistenceAccessContextAccessor access) =>
        throw new NotSupportedException("This publication owner does not support model material.");
    ValueTask PrepareModelAsync(RuntimeModelMaterial requested, WorkflowExecutableSourceReference expected,
        string slotName, CancellationToken ct) => ValueTask.FromException(new NotSupportedException("This publication owner does not support model material."));
    ValueTask PrepareAsync(WorkflowExecutable requested, string activationId, string slotId, string slotName, CancellationToken ct);
    ValueTask<WorkflowActivationProjectionState> FindStateAsync(string activationId, CancellationToken ct);
    ValueTask<IReadOnlyCollection<string>> ListServingActivationIdsAsync(string slotId, CancellationToken ct);
}
