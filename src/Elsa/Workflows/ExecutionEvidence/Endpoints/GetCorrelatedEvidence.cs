using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Workflows.ExecutionEvidence.Contracts;
using Elsa.Workflows.ExecutionEvidence.Models;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.ExecutionEvidence.Endpoints;

/// <summary>
/// Returns evidence across every workflow execution carrying a correlation id. This is how a caller spans a parent
/// workflow and the children it dispatched without the module needing an evidence-session concept. It long-polls on the
/// same terms as the single-workflow read, because waiting for a child to appear is precisely the case where a caller
/// cannot know when to stop asking.
/// </summary>
internal sealed class GetCorrelatedEvidence(IExecutionEvidenceStore store)
    : ElsaEndpointWithoutRequest<ExecutionEvidencePage>
{
    public override void Configure()
    {
        Get(ExecutionEvidenceRoutes.Base);
        ConfigurePermissions();
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var correlationId = Query<string>("correlationId", isRequired: false);

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            await Send.StringAsync(
                "A non-empty 'correlationId' query parameter is required.",
                StatusCodes.Status400BadRequest,
                cancellation: cancellationToken);
            return;
        }

        var afterSequence = Query<long>("after", isRequired: false);
        var page = await EvidencePolling.ReadAsync(
            () => store.ListByCorrelation(correlationId, afterSequence),
            EvidencePolling.ClampWait(Query<int>("waitMs", isRequired: false)),
            cancellationToken);

        await Send.OkAsync(page, cancellationToken);
    }
}
