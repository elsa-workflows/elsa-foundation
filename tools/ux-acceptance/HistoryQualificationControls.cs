using System.Globalization;
using CShells.DependencyInjection;
using CShells.Lifecycle;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Modularity.Api.Authorization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

/// <summary>Finite operations on the real ports of an owned, disposable qualification host.</summary>
internal static class HistoryQualificationControls
{
    internal static void MapEndpoints(IEndpointRouteBuilder endpoints, IShellRegistry registry)
    {
        endpoints.MapPost("/__qualification/history/{workflowExecutionId}/purge",
                (string workflowExecutionId, HttpContext http, CancellationToken token) =>
                    PurgeAsync(registry, workflowExecutionId, http, token))
            .WithHostOwner("Elsa.Workbench")
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequirePermission(ModuleManagementPermissionKeys.Manage);
    }

    private static async Task<IResult> PurgeAsync(IShellRegistry registry, string workflowExecutionId,
        HttpContext http, CancellationToken token)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (string.IsNullOrWhiteSpace(workflowExecutionId) || workflowExecutionId.Length > 64)
            return Results.BadRequest();
        var shell = await registry.GetOrActivateAsync("default", token);
        await using var scope = shell.BeginScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<IPersistenceAccessContextBinder>().Bind(
            PersistenceAccessContext.Scoped(new PersistenceScope("default")));
        var state = await services.GetRequiredService<IWorkflowExecutionStateStore>()
            .FindAsync(workflowExecutionId, token);
        if (state is null || state.TenantId is not (null or "default")) return Results.NotFound();
        // A running fixture must settle through its actual public runtime route first.
        if (!state.Status.IsTerminal()) return Results.Conflict(new { reason = "run-not-terminal" });
        var reader = services.GetRequiredService<IRuntimeHistoryStore>();
        var before = await reader.FindHeadAsync(workflowExecutionId, token);
        if (before is null) return Results.NotFound();
        var purgedThrough = await services.GetRequiredService<IRuntimeHistoryRetentionStore>()
            .PurgeThroughAsync(workflowExecutionId, before.LastSequence, token);
        var after = await reader.FindHeadAsync(workflowExecutionId, token);
        return Results.Json(new { workflowExecutionId,
            beforeLastSequence = before.LastSequence.ToString(CultureInfo.InvariantCulture),
            purgedThrough = purgedThrough.ToString(CultureInfo.InvariantCulture),
            afterLastSequence = after?.LastSequence.ToString(CultureInfo.InvariantCulture),
            afterPurgedThrough = after?.PurgedThroughSequence.ToString(CultureInfo.InvariantCulture) });
    }
}
