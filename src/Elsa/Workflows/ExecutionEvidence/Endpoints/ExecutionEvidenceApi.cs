using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.ExecutionEvidence.Authorization;
using Elsa.Workflows.ExecutionEvidence.Contracts;
using Elsa.Workflows.ExecutionEvidence.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Workflows.ExecutionEvidence.Endpoints;

/// <summary>Maps the execution-evidence query and cleanup surface.</summary>
public static class ExecutionEvidenceApi
{
    public static void MapExecutionEvidenceApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var applicationName = endpoints.ServiceProvider.GetService<IHostEnvironment>()?.ApplicationName
                               ?? typeof(ExecutionEvidenceApi).Assembly.GetName().Name!;
        var descriptionMethod = typeof(RequestDelegate).GetMethod(nameof(RequestDelegate.Invoke))
            ?? throw new InvalidOperationException("RequestDelegate.Invoke metadata is unavailable.");
        var response = new ProducesResponseTypeMetadata(StatusCodes.Status200OK, typeof(ExecutionEvidencePage), ["application/json"]);
        var unauthorized = new ProducesResponseTypeMetadata(StatusCodes.Status401Unauthorized, typeof(void), []);
        var forbidden = new ProducesResponseTypeMetadata(StatusCodes.Status403Forbidden, typeof(void), []);

        endpoints.MapGet(ExecutionEvidenceRoutes.Base, HandleCorrelatedAsync)
            .WithName("ElsaWorkflowsExecutionEvidenceEndpointsGetCorrelatedEvidence")
            .WithTags(applicationName)
            .WithOwner(ExecutionEvidencePermissionKeys.OwnerId)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequirePermission(ExecutionEvidencePermissionKeys.Read)
            .WithMetadata(descriptionMethod, response, unauthorized, forbidden);

        endpoints.MapGet(ExecutionEvidenceRoutes.ByWorkflow, HandleWorkflowAsync)
            .WithName("ElsaWorkflowsExecutionEvidenceEndpointsGetWorkflowEvidence")
            .WithTags(applicationName)
            .WithOwner(ExecutionEvidencePermissionKeys.OwnerId)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequirePermission(ExecutionEvidencePermissionKeys.Read)
            .WithMetadata(descriptionMethod, response, unauthorized, forbidden);

        endpoints.MapDelete(ExecutionEvidenceRoutes.Base, HandleDeleteAsync)
            .WithName("ElsaWorkflowsExecutionEvidenceEndpointsDeleteEvidence")
            .WithTags(applicationName)
            .WithOwner(ExecutionEvidencePermissionKeys.OwnerId)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequirePermission(ExecutionEvidencePermissionKeys.Delete)
            .WithMetadata(
                descriptionMethod,
                new ProducesResponseTypeMetadata(StatusCodes.Status204NoContent, typeof(void), []),
                unauthorized,
                forbidden);
    }

    private static async Task HandleCorrelatedAsync(HttpContext context)
    {
        var correlationId = Query(context, "correlationId");
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            context.Response.OnStarting(static state =>
            {
                ((HttpResponse)state).Headers.Remove("Content-Length");
                return Task.CompletedTask;
            }, context.Response);
            await Results.Text("A non-empty 'correlationId' query parameter is required.", statusCode: StatusCodes.Status400BadRequest)
                .ExecuteAsync(context);
            return;
        }

        var store = context.RequestServices.GetRequiredService<IExecutionEvidenceStore>();
        var page = await EvidencePolling.ReadAsync(
            () => store.ListByCorrelation(correlationId, QueryLong(context, "after")),
            EvidencePolling.ClampWait(QueryInt(context, "waitMs")),
            context.RequestAborted);
        await Results.Json(
            page,
            ExecutionEvidenceJsonContext.Default.ExecutionEvidencePage,
            contentType: "application/json; charset=utf-8").ExecuteAsync(context);
    }

    private static async Task HandleWorkflowAsync(HttpContext context)
    {
        var workflowExecutionId = context.Request.RouteValues.TryGetValue("workflowExecutionId", out var value)
            ? value?.ToString()
            : null;
        if (string.IsNullOrWhiteSpace(workflowExecutionId))
        {
            await Results.Text("A non-empty workflow execution id is required.", statusCode: StatusCodes.Status400BadRequest)
                .ExecuteAsync(context);
            return;
        }

        var store = context.RequestServices.GetRequiredService<IExecutionEvidenceStore>();
        var page = await EvidencePolling.ReadAsync(
            () => store.List(workflowExecutionId, QueryLong(context, "after")),
            EvidencePolling.ClampWait(QueryInt(context, "waitMs")),
            context.RequestAborted);
        await Results.Json(
            page,
            ExecutionEvidenceJsonContext.Default.ExecutionEvidencePage,
            contentType: "application/json; charset=utf-8").ExecuteAsync(context);
    }

    private static async Task HandleDeleteAsync(HttpContext context)
    {
        var workflowExecutionId = Query(context, "workflowExecutionId");
        var all = QueryBool(context, "all");
        if (string.IsNullOrWhiteSpace(workflowExecutionId) && !all)
        {
            await Results.Text(
                    "Supply 'workflowExecutionId' to drop one workflow execution's evidence, or 'all=true' to drop every workflow execution's evidence on this host.",
                    statusCode: StatusCodes.Status400BadRequest)
                .ExecuteAsync(context);
            return;
        }

        context.RequestServices.GetRequiredService<IExecutionEvidenceStore>().Clear(
            string.IsNullOrWhiteSpace(workflowExecutionId) ? null : workflowExecutionId);
        await Results.NoContent().ExecuteAsync(context);
    }

    private static string? Query(HttpContext context, string key) =>
        context.Request.Query.TryGetValue(key, out var values) ? values.ToString() : null;

    private static long QueryLong(HttpContext context, string key) =>
        long.TryParse(Query(context, key), out var value) ? value : 0;

    private static int QueryInt(HttpContext context, string key) =>
        int.TryParse(Query(context, key), out var value) ? value : 0;

    private static bool QueryBool(HttpContext context, string key) =>
        bool.TryParse(Query(context, key), out var value) && value;
}
