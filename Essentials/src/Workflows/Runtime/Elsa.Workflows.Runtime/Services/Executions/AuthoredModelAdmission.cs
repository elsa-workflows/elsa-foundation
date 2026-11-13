using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Executions;

internal sealed class AuthoredModelAdmission(RuntimeExecutionProtocolRouter? router = null, IAuthoredModelQueue? participant = null) : IAuthoredModelAdmission
{
    public ValueTask<WorkflowExecutionCommandDispatchResult> AdmitAsync(AuthoredModelAdmissionRequest request,
        CancellationToken cancellationToken = default) => participant is null || router is null
        ? ValueTask.FromException<WorkflowExecutionCommandDispatchResult>(new NotSupportedException("The selected runtime stores have no protected authored model queue participant."))
        : router.AdmitAuthoredAsync(request, participant, cancellationToken);
}
