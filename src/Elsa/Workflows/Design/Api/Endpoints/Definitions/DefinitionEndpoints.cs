using Elsa.Api.AspNetCore;
using Elsa.Api.Mediator;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Models;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions;

/// <summary>Workflow definition endpoints: list, create, read, submit, update, delete, and restore.</summary>
internal static class DefinitionEndpoints
{
    private static readonly string[] AcceptsAnyOrJson = ["*/*", "application/json"];
    private static readonly string[] AcceptsJson = ["application/json"];

    public static void Map(ModuleEndpointGroup api)
    {
        api.MapRequest<ListDefinitions, WorkflowDefinitionListView>(
                HttpMethods.Get, RouteConstants.Definitions, "DefinitionsList", accepts: AcceptsAnyOrJson)
            .RequirePermission(WorkflowDesignPermissions.Read);

        api.MapCommand<AddDefinition, WorkflowDefinitionDetailsView>(
                HttpMethods.Post, RouteConstants.Definitions, "DefinitionsAdd", accepts: AcceptsJson)
            .RequirePermission(WorkflowDesignPermissions.Manage);

        api.MapRequest<GetDefinition, WorkflowDefinitionDetailsView>(
                HttpMethods.Get, RouteConstants.GetRoute("definitions/{definitionId}"), "DefinitionsGet", accepts: AcceptsAnyOrJson)
            .RequirePermission(WorkflowDesignPermissions.Read);

        api.MapCommand<SubmitDefinition, SubmittedWorkflowDefinitionView>(
                HttpMethods.Post, RouteConstants.GetRoute("definitions/submit"), "DefinitionsSubmit", accepts: AcceptsJson)
            .RequirePermission(WorkflowDesignPermissions.Manage);

        api.MapRequest<GetWorkflowDefinitionSubmitSchema, WorkflowDefinitionSubmitSchemaView>(
                HttpMethods.Get, RouteConstants.DefinitionSubmitSchema, "DefinitionsSubmitSchema")
            .RequirePermission(WorkflowDesignPermissions.Read);

        api.MapCommand<SoftDeleteDefinition>(
                HttpMethods.Delete, RouteConstants.GetRoute("definitions/{definitionId}"), "DefinitionsDelete", accepts: AcceptsAnyOrJson)
            .RequirePermission(WorkflowDesignPermissions.Manage);

        api.MapCommand<UpdateDefinitionMetadata, WorkflowDefinitionDetailsView>(
                HttpMethods.Patch, RouteConstants.GetRoute("definitions/{definitionId}"), "DefinitionsUpdate", accepts: AcceptsJson)
            .RequirePermission(WorkflowDesignPermissions.Manage);

        api.MapCommand<RestoreDefinition>(
                HttpMethods.Post, RouteConstants.GetRoute("definitions/{definitionId}/restore"), "DefinitionsRestore",
                bodyMode: EndpointBodyMode.RequiredWithContentType, accepts: AcceptsJson)
            .RequirePermission(WorkflowDesignPermissions.Manage);

        api.MapCommand<DeleteDefinitionPermanently>(
                HttpMethods.Delete, RouteConstants.GetRoute("definitions/{definitionId}/permanent"), "DefinitionsDeletePermanently", accepts: AcceptsAnyOrJson)
            .RequirePermission(WorkflowDesignPermissions.Manage);

    }
}
