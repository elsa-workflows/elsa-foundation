using Elsa.Api.Endpoints;
using Microsoft.AspNetCore.Routing;

namespace Elsa.Workflows.Publishing.Api;

/// <summary>
/// Maps the Publishing REST surface using ordinary ASP.NET Core endpoints.
/// </summary>
/// <remarks>
/// This is the composition root only. Each endpoint declares its own route, contract, and permission
/// beside the handling it dispatches, under <c>Endpoints/</c>. The owner's serializer options carry
/// runtime configuration (camel-cased string enums), so the group receives a context constructed
/// over them, and the success content type is the module's published bare <c>application/json</c>.
/// </remarks>
public static class WorkflowsPublishingApi
{
    internal const string OwnerId = "Elsa.Workflows.Publishing.Api";

    public static void MapWorkflowsPublishingApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var api = endpoints.MapModuleEndpoints(
            OwnerId,
            WorkflowsPublishingJsonOptions.WireContext,
            jsonContentType: "application/json");

        // Endpoint classes are scanned from this module's own assembly: each declares its route,
        // metadata, and permission on itself under Endpoints/<Resource>/<Operation>/Endpoint.cs.
        api.MapEndpointsFrom(typeof(WorkflowsPublishingApi).Assembly);
    }
}
