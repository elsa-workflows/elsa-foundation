using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Replacement contract: physical participant for an authored root's exact ordinary queue row, selected slot/source and material grants.</summary>
/// <remarks>Implementations qualify the actual selected stores and commit in one provider boundary. A copied
/// publication snapshot or an ordinary queue acknowledgement alone grants no start authority.</remarks>
public interface IAuthoredModelQueue
{
    ValueTask<WorkflowExecutionCommandDispatchResult> AdmitAsync(AuthoredModelAdmissionRequest request,
        RuntimeSchedulerWorkItem work, IReadOnlyList<WorkflowExecutableRootWriteLease> grants,
        CancellationToken cancellationToken = default);
}
