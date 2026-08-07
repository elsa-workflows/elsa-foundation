using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Workflows.ExecutionEvidence.Contracts;
using Elsa.Workflows.ExecutionEvidence.Models;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.ExecutionEvidence.Endpoints;

/// <summary>
/// Returns one workflow execution's evidence after a cursor, optionally long-polling until there is something to
/// return.
/// </summary>
internal sealed class GetWorkflowEvidence(IExecutionEvidenceStore store)
    : ElsaEndpointWithoutRequest<ExecutionEvidencePage>
{
    public override void Configure()
    {
        Get(ExecutionEvidenceRoutes.ByWorkflow);
        ConfigurePermissions();
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var workflowExecutionId = Route<string>("workflowExecutionId");

        if (string.IsNullOrWhiteSpace(workflowExecutionId))
        {
            await Send.StringAsync(
                "A non-empty workflow execution id is required.",
                StatusCodes.Status400BadRequest,
                cancellation: cancellationToken);
            return;
        }

        var afterSequence = Query<long>("after", isRequired: false);
        var page = await EvidencePolling.ReadAsync(
            () => store.List(workflowExecutionId, afterSequence),
            EvidencePolling.ClampWait(Query<int>("waitMs", isRequired: false)),
            cancellationToken);

        await Send.OkAsync(page, cancellationToken);
    }
}
