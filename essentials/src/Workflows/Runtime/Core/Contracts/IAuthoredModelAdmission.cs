using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Admits an authored model root while protecting its selected slot/source through actual queue commit.</summary>
/// <remarks>The provider must qualify the actual shared queue, slot, source and material boundary. Ordinary model
/// queue support alone is insufficient. Material-root leases remain the runtime router's responsibility.</remarks>
public interface IAuthoredModelAdmission
{
    ValueTask<WorkflowExecutionCommandDispatchResult> AdmitAsync(
        AuthoredModelAdmissionRequest request,
        CancellationToken cancellationToken = default) =>
        cancellationToken.IsCancellationRequested ? ValueTask.FromCanceled<WorkflowExecutionCommandDispatchResult>(cancellationToken) :
            ValueTask.FromException<WorkflowExecutionCommandDispatchResult>(new NotSupportedException("Authored model admission requires a protected slot/source/queue participant."));
}
