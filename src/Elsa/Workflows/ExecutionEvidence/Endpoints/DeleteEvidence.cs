using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Workflows.ExecutionEvidence.Contracts;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.ExecutionEvidence.Endpoints;

/// <summary>
/// Drops evidence for one workflow execution, or — only when the caller explicitly opts in — for all of them. The
/// opt-in exists because the buffer is shared: on a host running two suites at once, an unqualified wipe destroys the
/// other suite's in-flight evidence, and that must not be the easiest call to make.
/// </summary>
internal sealed class DeleteEvidence(IExecutionEvidenceStore store) : ElsaEndpointWithoutRequest
{
    public override void Configure()
    {
        Delete(ExecutionEvidenceRoutes.Base);
        ConfigurePermissions();
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var workflowExecutionId = Query<string>("workflowExecutionId", isRequired: false);
        var all = Query<bool>("all", isRequired: false);

        if (string.IsNullOrWhiteSpace(workflowExecutionId))
        {
            if (!all)
            {
                await Send.StringAsync(
                    "Supply 'workflowExecutionId' to drop one workflow execution's evidence, or 'all=true' to drop every workflow execution's evidence on this host.",
                    StatusCodes.Status400BadRequest,
                    cancellation: cancellationToken);
                return;
            }

            store.Clear(null);
        }
        else
        {
            store.Clear(workflowExecutionId);
        }

        await Send.NoContentAsync(cancellationToken);
    }
}
