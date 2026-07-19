using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Dashboard;

internal sealed class GetWorkflowPortfolio(IWorkflowPortfolioService service)
    : ElsaEndpointWithoutRequest<WorkflowPortfolioSnapshot>
{
    public override void Configure()
    {
        Get("/_elsa/workflows/dashboard/definitions");
        ConfigurePermissions(WorkflowsDashboardPermissions.Read);
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var tenantId = HttpContext.User.FindFirst(IdentityClaimTypes.TenantId)?.Value;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            await Send.StringAsync("An authenticated tenant scope is required.", StatusCodes.Status400BadRequest, cancellation: cancellationToken);
            return;
        }

        await Send.OkAsync(await service.QueryAsync(tenantId, cancellationToken), cancellationToken);
    }
}
