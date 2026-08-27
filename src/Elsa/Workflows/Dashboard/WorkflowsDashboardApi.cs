using Elsa.Api.AspNetCore;
using System.Globalization;
using NativeEndpoints;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Workflows.Dashboard;

/// <summary>Maps the workflow dashboard read surface using ordinary ASP.NET Core endpoints.</summary>
public static class WorkflowsDashboardApi
{
    private const string OwnerId = "Elsa.Workflows.Dashboard";

    public static void MapWorkflowsDashboardApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // The published document tags this surface with the host application name, resolved at
        // composition time exactly as the hand-written mapper did.
        var applicationName = endpoints.ServiceProvider.GetService<IHostEnvironment>()?.ApplicationName;
        var api = endpoints.MapEndpointGroup(
            OwnerId,
            WorkflowsDashboardJsonContext.Default,
            jsonContentType: "application/json",
            tag: string.IsNullOrWhiteSpace(applicationName) ? null : applicationName);

        // The published error contract is a plain-text 400 carrying the exact validation message,
        // and both operation ids predate the naming scheme, so the operations stay on the group's
        // raw seam with their own query parsing and writes.
        api.MapUnboundOperation(
                "GET", "/_elsa/workflows/dashboard/definitions", "GetWorkflowPortfolio",
                typeof(WorkflowPortfolioSnapshot), StatusCodes.Status200OK, null, HandleWorkflowPortfolioAsync,
                name: "ElsaWorkflowsDashboardGetWorkflowPortfolio")
            .RequirePermission(WorkflowsDashboardPermissions.Read);

        api.MapUnboundOperation(
                "GET", "/_elsa/workflows/dashboard/runs", "GetWorkflowRunHealth",
                typeof(WorkflowRunHealthSnapshot), StatusCodes.Status200OK, null, HandleWorkflowRunHealthAsync,
                name: "ElsaWorkflowsDashboardGetWorkflowRunHealth")
            .RequirePermission(WorkflowsDashboardPermissions.Read);
    }

    private static async Task HandleWorkflowPortfolioAsync(HttpContext context)
    {
        var tenantId = context.User.FindFirst(IdentityClaimTypes.TenantId)?.Value;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync("An authenticated tenant scope is required.", context.RequestAborted);
            return;
        }

        var service = context.RequestServices.GetRequiredService<IWorkflowPortfolioService>();
        var snapshot = await service.QueryAsync(tenantId, context.RequestAborted);
        await Results.Json(snapshot, WorkflowsDashboardJsonContext.Default.WorkflowPortfolioSnapshot, contentType: "application/json").ExecuteAsync(context);
    }

    private static async Task HandleWorkflowRunHealthAsync(HttpContext context)
    {
        try
        {
            var request = context.Request.Query;
            if (!DateTimeOffset.TryParse(request["from"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var from))
                throw new WorkflowRunHealthQueryException("A valid ISO-8601 'from' instant is required.");
            if (!DateTimeOffset.TryParse(request["to"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var to))
                throw new WorkflowRunHealthQueryException("A valid ISO-8601 'to' instant is required.");
            if (!Enum.TryParse<WorkflowRunHealthBucketSize>(request["bucket"], true, out var bucket))
                throw new WorkflowRunHealthQueryException("Bucket must be 'hour' or 'day'.");

            var tenantId = context.User.FindFirst(IdentityClaimTypes.TenantId)?.Value;
            if (string.IsNullOrWhiteSpace(tenantId))
                throw new WorkflowRunHealthQueryException("An authenticated tenant scope is required.");

            var includeTestRunsValue = request["includeTestRuns"].ToString();
            if (!string.IsNullOrEmpty(includeTestRunsValue) && !bool.TryParse(includeTestRunsValue, out _))
                throw new WorkflowRunHealthQueryException("'includeTestRuns' must be true or false when provided.");

            var includeTestRuns = bool.TryParse(includeTestRunsValue, out var include) && include;
            var query = new WorkflowRunHealthQuery(
                from,
                to,
                request["timeZone"].ToString(),
                bucket,
                tenantId,
                includeTestRuns);
            var service = context.RequestServices.GetRequiredService<IWorkflowRunHealthService>();
            var snapshot = await service.QueryAsync(query, context.RequestAborted);
            await Results.Json(snapshot, WorkflowsDashboardJsonContext.Default.WorkflowRunHealthSnapshot, contentType: "application/json").ExecuteAsync(context);
        }
        catch (WorkflowRunHealthQueryException exception)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync(exception.Message, context.RequestAborted);
        }
    }
}
