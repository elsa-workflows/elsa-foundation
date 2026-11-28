using MinimalEndpoints;
using Microsoft.AspNetCore.Routing;

namespace Elsa.Workflows.Runtime.Api;

/// <summary>
/// Maps the Runtime REST surface using ordinary ASP.NET Core endpoints.
/// </summary>
/// <remarks>
/// This is the composition root only. Each endpoint declares its route, contract, permission, and
/// failure family beside the dispatch it performs. The shared endpoint group lives in
/// <c>Elsa.Workflows.Runtime.Api.Http</c>. The owner writes with
/// its own source-generated serializer context and the published success content type carries the
/// charset, so both are passed here explicitly. Routes are absolute and deliberately slashless —
/// the frozen endpoint manifest pins the historical pattern text.
/// </remarks>
public static class WorkflowsRuntimeApi
{
    internal const string OwnerId = RuntimeHttpApi.OwnerId;

    public static void MapWorkflowsRuntimeApi(IEndpointRouteBuilder endpoints)
    {
        var api = MapGroup(endpoints);
        RuntimeHttpApi.MapEndpoints(api, includeExecute: true, includeDelete: true, includeInputSources: true);
        UserTasks.UserTaskHttpApi.MapEndpoints(api, includeAssignmentTargets: true);
        api.MapEndpointsFrom(typeof(WorkflowsRuntimeApi).Assembly);
    }

    // An earlier KernelApi package can still be paired with this package by NuGet.
    internal static void MapRetainedEndpoints(IEndpointRouteBuilder endpoints, bool includeExecute, bool includeDelete, bool includeInputSources) =>
        RuntimeHttpApi.MapRetainedEndpoints(endpoints, includeExecute, includeDelete, includeInputSources);

    private static EndpointGroup MapGroup(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints.MapEndpointGroup(OwnerId, WorkflowsRuntimeJsonContext.Default,
            jsonContentType: "application/json; charset=utf-8");
    }
}
