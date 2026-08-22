using Elsa.Api.Mediator;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Design.Api.Endpoints.Versions;

/// <summary>Workflow definition version endpoints: list, ingest, and read.</summary>
internal static class VersionEndpoints
{
    private static readonly string[] AcceptsAnyOrJson = ["*/*", "application/json"];
    private static readonly string[] AcceptsJson = ["application/json"];

    public static void Map(ModuleEndpointGroup api)
    {
        api.MapRequest<ListDefinitionVersions, IEnumerable<WorkflowDefinitionVersionSummary>>(
                HttpMethods.Get, RouteConstants.GetRoute("definitions/{definitionId}/versions"), "VersionsList", accepts: AcceptsAnyOrJson)
            .RequirePermission(WorkflowDesignPermissions.Read);

        api.MapCommand<AddVersion, WorkflowDefinitionVersionDetailsView>(
                HttpMethods.Post, RouteConstants.GetRoute("versions/ingest"), "VersionsAdd", accepts: AcceptsJson)
            .RequirePermission(WorkflowDesignPermissions.Manage);

        api.MapRequest<GetVersion, WorkflowDefinitionVersionDetailsView>(
                HttpMethods.Get, RouteConstants.GetRoute("versions/{versionId}"), "VersionsGet", accepts: AcceptsAnyOrJson)
            .RequirePermission(WorkflowDesignPermissions.Read);
    }
}
