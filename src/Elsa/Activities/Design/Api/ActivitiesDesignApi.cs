using Elsa.Api.Endpoints;
using Microsoft.AspNetCore.Routing;

namespace Elsa.Activities.Design.Api;

/// <summary>
/// Maps the Activities Design REST surface using ordinary ASP.NET Core endpoints.
/// </summary>
/// <remarks>
/// This is the composition root only. Each of the 38 operations declares its route, contract,
/// permission, and failure-shape family on its own class under <c>Endpoints/</c>. The owner's
/// serializer options carry runtime configuration (camel-cased string enums), so the group receives
/// a context constructed over them, and the success content type is the module's published bare
/// <c>application/json</c>.
/// </remarks>
public static class ActivitiesDesignApi
{
    internal const string OwnerId = "Elsa.Activities.Design.Api";

    /// <summary>Maps all 38 Activities Design operations for the owning module.</summary>
    public static void MapActivitiesDesignApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var api = endpoints.MapModuleEndpoints(
            OwnerId,
            ActivitiesDesignJsonOptions.WireContext,
            jsonContentType: "application/json");

        // Endpoint classes are scanned from this module's own assembly: each declares its route,
        // metadata, and permission on itself under Endpoints/<Resource>/<Operation>/Endpoint.cs.
        api.MapEndpointsFrom(typeof(ActivitiesDesignApi).Assembly);
    }
}
