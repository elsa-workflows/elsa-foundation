using MinimalEndpoints;
using Microsoft.AspNetCore.Routing;

namespace Elsa.Workflows.Runtime.Api;

/// <summary>
/// Maps the Runtime REST surface using ordinary ASP.NET Core endpoints.
/// </summary>
/// <remarks>
/// This is the composition root only. Each endpoint declares its route, contract, permission, and
/// failure family beside the dispatch it performs, under <c>Endpoints/</c>. The owner writes with
/// its own source-generated serializer context and the published success content type carries the
/// charset, so both are passed here explicitly. Routes are absolute and deliberately slashless —
/// the frozen endpoint manifest pins the historical pattern text.
/// </remarks>
public static class WorkflowsRuntimeApi
{
    internal const string OwnerId = "Elsa.Workflows.Runtime.Api";

    public static void MapWorkflowsRuntimeApi(IEndpointRouteBuilder endpoints)
    {
        // Endpoint classes are scanned from this module's own assembly: each declares its route,
        // metadata, and permission on itself under Endpoints/<Resource>/<Operation>/Endpoint.cs.
        MapGroup(endpoints).MapEndpointsFrom(typeof(WorkflowsRuntimeApi).Assembly);
    }

    internal static void MapRetainedEndpoints(IEndpointRouteBuilder endpoints, bool includeExecute, bool includeDelete, bool includeInputSources)
    {
        var api = MapGroup(endpoints);
        api.MapEndpoint<Endpoints.Executables.List.Endpoint>();
        api.MapEndpoint<Endpoints.Executables.Get.Endpoint>();
        api.MapEndpoint<Endpoints.Executables.Provenance.Endpoint>();
        if (includeInputSources) api.MapEndpoint<Endpoints.Executables.InputSources.Endpoint>();
        if (includeExecute) api.MapEndpoint<Endpoints.Executables.Execute.Endpoint>();
        api.MapEndpoint<Endpoints.ActivationSlots.List.Endpoint>();
        api.MapEndpoint<Endpoints.ActivationSlots.Get.Endpoint>();
        api.MapEndpoint<Endpoints.Instances.List.Endpoint>();
        api.MapEndpoint<Endpoints.Instances.ListPage.Endpoint>();
        api.MapEndpoint<Endpoints.Instances.Get.Endpoint>();
        if (includeDelete) api.MapEndpoint<Endpoints.Instances.Delete.Endpoint>();
        api.MapEndpoint<Endpoints.ActivityExecutions.List.Endpoint>();
        api.MapEndpoint<Endpoints.ActivityExecutions.Get.Endpoint>();
        api.MapEndpoint<Endpoints.History.Page.Endpoint>();
        api.MapEndpoint<Endpoints.History.Occurrences.Endpoint>();
    }

    private static EndpointGroup MapGroup(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints.MapEndpointGroup(OwnerId, WorkflowsRuntimeJsonContext.Default,
            jsonContentType: "application/json; charset=utf-8");
    }
}
