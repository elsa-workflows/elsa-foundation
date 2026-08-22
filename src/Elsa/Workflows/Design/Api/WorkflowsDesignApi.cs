using Elsa.Api.Mediator;
using Elsa.Workflows.Design.Api.Endpoints.Authoring;
using Elsa.Workflows.Design.Api.Endpoints.Drafts;
using Elsa.Workflows.Design.Api.Endpoints.Structures;
using Elsa.Workflows.Design.Api.Endpoints.Versions;
using Microsoft.AspNetCore.Routing;

namespace Elsa.Workflows.Design.Api;

/// <summary>
/// Maps the workflow design surface using ordinary ASP.NET Core endpoints.
/// </summary>
/// <remarks>
/// This is the composition root only. Each endpoint declares its own route, contract, and permission
/// beside the handler it dispatches to, under <c>Endpoints/</c>.
/// </remarks>
public static class WorkflowsDesignApi
{
    internal const string OwnerId = "Elsa.Workflows.Design.Api";

    public static void MapWorkflowsDesignApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var api = endpoints.MapModuleEndpoints(OwnerId, WorkflowsDesignJsonContext.Default);

        // Endpoint classes are scanned from this module's own assembly: each declares its route,
        // metadata, and permission on itself under Endpoints/<Resource>/<Operation>/Endpoint.cs.
        api.MapEndpointsFrom(typeof(WorkflowsDesignApi).Assembly, Constants.RouteConstants.DomainPrefix);
        VersionEndpoints.Map(api);
        DraftEndpoints.Map(api);
        StructureEndpoints.Map(api);
        AuthoringEndpoints.Map(api);
    }
}
