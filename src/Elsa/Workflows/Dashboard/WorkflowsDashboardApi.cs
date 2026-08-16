using System.Globalization;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Dashboard;

/// <summary>Maps the workflow dashboard read surface using ordinary ASP.NET Core endpoints.</summary>
public static class WorkflowsDashboardApi
{
    private const string OwnerId = "Elsa.Workflows.Dashboard";

    public static void MapWorkflowsDashboardApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RequestDelegate portfolio = HandleWorkflowPortfolioAsync;
        RequestDelegate health = HandleWorkflowRunHealthAsync;
        var descriptionMethod = typeof(RequestDelegate).GetMethod(nameof(RequestDelegate.Invoke))
            ?? throw new InvalidOperationException("RequestDelegate.Invoke metadata is unavailable.");

        endpoints.MapGet("/_elsa/workflows/dashboard/definitions", portfolio)
            .WithName("ElsaWorkflowsDashboardGetWorkflowPortfolio")
            .WithHostApplicationOpenApiTag(endpoints.ServiceProvider)
            .WithOwner(OwnerId)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequireAnyPermission(PermissionKey.Wildcard, WorkflowsDashboardPermissions.Read)
            .WithMetadata(
                descriptionMethod,
                Response(StatusCodes.Status200OK, typeof(WorkflowPortfolioSnapshot)),
                Unauthorized(),
                Forbidden());

        endpoints.MapGet("/_elsa/workflows/dashboard/runs", health)
            .WithName("ElsaWorkflowsDashboardGetWorkflowRunHealth")
            .WithHostApplicationOpenApiTag(endpoints.ServiceProvider)
            .WithOwner(OwnerId)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequireAnyPermission(PermissionKey.Wildcard, WorkflowsDashboardPermissions.Read)
            .WithMetadata(
                descriptionMethod,
                Response(StatusCodes.Status200OK, typeof(WorkflowRunHealthSnapshot)),
                Unauthorized(),
                Forbidden());
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

    private static ProducesResponseTypeMetadata Response(int statusCode, Type bodyType) =>
        new(statusCode, bodyType, ["application/json"]);

    private static ProducesResponseTypeMetadata Unauthorized() =>
        new(StatusCodes.Status401Unauthorized, typeof(void), []);

    private static ProducesResponseTypeMetadata Forbidden() =>
        new(StatusCodes.Status403Forbidden, typeof(void), []);
}
