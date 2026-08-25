using Elsa.Api.Endpoints;
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
        ArgumentNullException.ThrowIfNull(endpoints);

        var api = endpoints.MapModuleEndpoints(
            OwnerId,
            WorkflowsRuntimeJsonContext.Default,
            jsonContentType: "application/json; charset=utf-8");

        // Endpoint classes are scanned from this module's own assembly: each declares its route,
        // metadata, and permission on itself under Endpoints/<Resource>/<Operation>/Endpoint.cs.
        api.MapEndpointsFrom(typeof(WorkflowsRuntimeApi).Assembly);
    }
}
