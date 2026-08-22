using Elsa.Api.Endpoints;
using Elsa.Api.Mediator;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Models;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Design.Api.Endpoints.Structures;

/// <summary>Activity structure endpoints.</summary>
internal static class StructureEndpoints
{
    public static void Map(ModuleEndpointGroup api) =>
        api.MapRequest<ListActivityStructures, ActivityStructuresResponse>(
                HttpMethods.Get, RouteConstants.Structures, "StructuresList")
            .RequirePermission(WorkflowDesignPermissions.Read);
}
