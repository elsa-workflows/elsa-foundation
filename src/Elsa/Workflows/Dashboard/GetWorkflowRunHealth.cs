using System.Globalization;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Dashboard;

internal sealed class GetWorkflowRunHealth(IWorkflowRunHealthService service)
    : ElsaEndpointWithoutRequest<WorkflowRunHealthSnapshot>
{
    public override void Configure()
    {
        Get("/_elsa/workflows/dashboard/runs");
        ConfigurePermissions(WorkflowsDashboardPermissions.Read);
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        try
        {
            var query = ParseQuery();
            await Send.OkAsync(await service.QueryAsync(query, cancellationToken), cancellationToken);
        }
        catch (WorkflowRunHealthQueryException exception)
        {
            await Send.StringAsync(exception.Message, StatusCodes.Status400BadRequest, cancellation: cancellationToken);
        }
    }

    private WorkflowRunHealthQuery ParseQuery()
    {
        var request = HttpContext.Request.Query;
        if (!DateTimeOffset.TryParse(request["from"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var from))
            throw new WorkflowRunHealthQueryException("A valid ISO-8601 'from' instant is required.");
        if (!DateTimeOffset.TryParse(request["to"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var to))
            throw new WorkflowRunHealthQueryException("A valid ISO-8601 'to' instant is required.");
        if (!Enum.TryParse<WorkflowRunHealthBucketSize>(request["bucket"], true, out var bucket))
            throw new WorkflowRunHealthQueryException("Bucket must be 'hour' or 'day'.");

        var tenantId = HttpContext.User.FindFirst(IdentityClaimTypes.TenantId)?.Value;
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new WorkflowRunHealthQueryException("An authenticated tenant scope is required.");

        var includeTestRunsValue = request["includeTestRuns"].ToString();
        if (!string.IsNullOrEmpty(includeTestRunsValue) && !bool.TryParse(includeTestRunsValue, out _))
            throw new WorkflowRunHealthQueryException("'includeTestRuns' must be true or false when provided.");
        var includeTestRuns = bool.TryParse(includeTestRunsValue, out var include) && include;
        return new(from, to, request["timeZone"].ToString(), bucket, tenantId, includeTestRuns);
    }
}
