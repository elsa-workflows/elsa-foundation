using Elsa.Api.AspNetCore;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Api.Services;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;

namespace Elsa.Workflows.Design.Api;

/// <summary>Maps the workflow design management surface using ordinary ASP.NET Core endpoints.</summary>
public static partial class WorkflowsDesignApi
{
    private const string OwnerId = "Elsa.Workflows.Design.Api";
    private const string JsonContentType = "application/json; charset=utf-8";
    private const string ProblemJsonContentType = "application/problem+json; charset=utf-8";

    public static void MapWorkflowsDesignApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);


        Map(endpoints.MapPost(RouteConstants.ActivityInputOptions, (RequestDelegate)HandleActivityInputOptionsAsync), "AuthoringResolveActivityInputOptions", WorkflowDesignPermissions.Read, typeof(ActivityInputOptionsRequest), typeof(ActivityInputOptionsResponse), accepts: ["application/json"]);
        Map(endpoints.MapGet(RouteConstants.Definitions, (RequestDelegate)HandleListDefinitionsAsync), "DefinitionsList", WorkflowDesignPermissions.Read, typeof(ListDefinitions), typeof(WorkflowDefinitionListView), accepts: ["*/*", "application/json"]);
        Map(endpoints.MapPost(RouteConstants.Definitions, (RequestDelegate)HandleAddDefinitionAsync), "DefinitionsAdd", WorkflowDesignPermissions.Manage, typeof(AddDefinition), typeof(WorkflowDefinitionDetailsView), accepts: ["application/json"]);
        Map(endpoints.MapPost(RouteConstants.GetRoute("definitions/submit"), (RequestDelegate)HandleSubmitDefinitionAsync), "DefinitionsSubmit", WorkflowDesignPermissions.Manage, typeof(SubmitDefinition), typeof(SubmittedWorkflowDefinitionView), accepts: ["application/json"]);
        Map(endpoints.MapGet(RouteConstants.DefinitionSubmitSchema, (RequestDelegate)HandleSubmitSchemaAsync), "DefinitionsSubmitSchema", WorkflowDesignPermissions.Read, null, typeof(WorkflowDefinitionSubmitSchemaView));
        Map(endpoints.MapGet(RouteConstants.GetRoute("definitions/{definitionId}"), (RequestDelegate)HandleGetDefinitionAsync), "DefinitionsGet", WorkflowDesignPermissions.Read, typeof(GetDefinition), typeof(WorkflowDefinitionDetailsView), accepts: ["*/*", "application/json"]);
        Map(endpoints.MapDelete(RouteConstants.GetRoute("definitions/{definitionId}"), (RequestDelegate)HandleDeleteDefinitionAsync), "DefinitionsDelete", WorkflowDesignPermissions.Manage, typeof(SoftDeleteDefinition), null, accepts: ["*/*", "application/json"], noContent: true);
        Map(endpoints.MapPatch(RouteConstants.GetRoute("definitions/{definitionId}"), (RequestDelegate)HandleUpdateDefinitionAsync), "DefinitionsUpdate", WorkflowDesignPermissions.Manage, typeof(UpdateDefinitionMetadata), typeof(WorkflowDefinitionDetailsView), accepts: ["application/json"]);
        Map(endpoints.MapDelete(RouteConstants.GetRoute("definitions/{definitionId}/permanent"), (RequestDelegate)HandleDeleteDefinitionPermanentlyAsync), "DefinitionsDeletePermanently", WorkflowDesignPermissions.Manage, typeof(DeleteDefinitionPermanently), null, accepts: ["*/*", "application/json"], noContent: true);
        Map(endpoints.MapPost(RouteConstants.GetRoute("definitions/{definitionId}/restore"), (RequestDelegate)HandleRestoreDefinitionAsync), "DefinitionsRestore", WorkflowDesignPermissions.Manage, typeof(RestoreDefinition), null, accepts: ["application/json"], noContent: true);
        Map(endpoints.MapGet(RouteConstants.GetRoute("definitions/{definitionId}/versions"), (RequestDelegate)HandleListVersionsAsync), "VersionsList", WorkflowDesignPermissions.Read, typeof(ListDefinitionVersions), typeof(IEnumerable<WorkflowDefinitionVersionSummary>), accepts: ["*/*", "application/json"]);
        Map(endpoints.MapGet(RouteConstants.GetRoute("drafts/{draftId}"), (RequestDelegate)HandleGetDraftAsync), "DraftsGet", WorkflowDesignPermissions.Read, typeof(GetDraft), typeof(WorkflowDraftView), accepts: ["*/*", "application/json"]);
        Map(endpoints.MapPut(RouteConstants.GetRoute("drafts/{draftId}"), (RequestDelegate)HandleReplaceDraftAsync), "DraftsReplace", WorkflowDesignPermissions.Manage, typeof(ReplaceDraft), typeof(WorkflowDraftView), accepts: ["application/json"]);
        Map(endpoints.MapDelete(RouteConstants.GetRoute("drafts/{draftId}"), (RequestDelegate)HandleDiscardDraftAsync), "DraftsDiscard", WorkflowDesignPermissions.Manage, typeof(DiscardDraft), null, accepts: ["*/*", "application/json"], noContent: true);
        Map(endpoints.MapPost(RouteConstants.GetRoute("drafts/{draftId}/promote"), (RequestDelegate)HandlePromoteDraftAsync), "DraftsPromote", WorkflowDesignPermissions.Manage, typeof(PromoteDraft), typeof(WorkflowDefinitionVersionDetailsView), accepts: ["application/json"]);
        Map(endpoints.MapPost(RouteConstants.GetRoute("drafts/{draftId}/promotion-preflight"), (RequestDelegate)HandlePromotionPreflightAsync), "DraftsPromotionPreflight", WorkflowDesignPermissions.Manage, typeof(PreflightDraftPromotion), typeof(PromotionPreflightAssessmentView), accepts: ["application/json"]);
        Map(endpoints.MapGet(RouteConstants.GetRoute("drafts/{draftId}/validations"), (RequestDelegate)HandleDraftValidationsAsync), "DraftsValidations", WorkflowDesignPermissions.Read, typeof(GetDraftValidations), typeof(DraftValidationsView), accepts: ["*/*", "application/json"]);
        Map(endpoints.MapPost(RouteConstants.ExpressionToolingCompletions, (RequestDelegate)HandleExpressionCompletionsAsync), "AuthoringCompleteExpressionTooling", WorkflowDesignPermissions.Read, typeof(ExpressionToolingCompletionRequest), typeof(ExpressionToolingOperationResponse<ExpressionToolingItems>), accepts: ["application/json"]);
        Map(endpoints.MapPost(RouteConstants.ExpressionToolingContext, (RequestDelegate)HandleExpressionContextAsync), "AuthoringResolveExpressionToolingContext", WorkflowDesignPermissions.Read, typeof(ExpressionToolingContextRequest), typeof(ExpressionToolingContextResponse), accepts: ["application/json"]);
        Map(endpoints.MapGet(RouteConstants.ExpressionToolingDescriptors, (RequestDelegate)HandleExpressionDescriptorsAsync), "AuthoringDescribeExpressionTooling", WorkflowDesignPermissions.Read, null, typeof(ExpressionToolingDescriptorsResponse));
        Map(endpoints.MapPost(RouteConstants.ExpressionToolingHover, (RequestDelegate)HandleExpressionHoverAsync), "AuthoringHoverExpressionTooling", WorkflowDesignPermissions.Read, typeof(ExpressionToolingHoverRequest), typeof(ExpressionToolingOperationResponse<ExpressionHover>), accepts: ["application/json"]);
        Map(endpoints.MapPost(RouteConstants.ExpressionToolingSymbols, (RequestDelegate)HandleExpressionSymbolsAsync), "AuthoringSearchExpressionToolingSymbols", WorkflowDesignPermissions.Read, typeof(ExpressionToolingContextRequest), typeof(ExpressionToolingOperationResponse<ExpressionToolingItems>), accepts: ["application/json"]);
        Map(endpoints.MapPost(RouteConstants.ExpressionToolingValidate, (RequestDelegate)HandleExpressionValidateAsync), "AuthoringValidateExpressionTooling", WorkflowDesignPermissions.Read, typeof(ExpressionToolingSourceRequest), typeof(ExpressionToolingOperationResponse<ExpressionDiagnosticSet>), accepts: ["application/json"]);
        Map(endpoints.MapPost(RouteConstants.ScopedVariableAnalysis, (RequestDelegate)HandleScopedVariableAnalysisAsync), "AuthoringAnalyzeScopedVariables", WorkflowDesignPermissions.Read, typeof(AnalyzeScopedVariablesRequest), typeof(ScopedVariableAnalysisResponse), accepts: ["application/json"]);
        Map(endpoints.MapGet(RouteConstants.Structures, (RequestDelegate)HandleStructuresAsync), "StructuresList", WorkflowDesignPermissions.Read, null, typeof(ActivityStructuresResponse));
        Map(endpoints.MapPost(RouteConstants.GetRoute("versions/ingest"), (RequestDelegate)HandleAddVersionAsync), "VersionsAdd", WorkflowDesignPermissions.Manage, typeof(AddVersion), typeof(WorkflowDefinitionVersionDetailsView), accepts: ["application/json"]);
        Map(endpoints.MapGet(RouteConstants.GetRoute("versions/{versionId}"), (RequestDelegate)HandleGetVersionAsync), "VersionsGet", WorkflowDesignPermissions.Read, typeof(GetVersion), typeof(WorkflowDefinitionVersionDetailsView), accepts: ["*/*", "application/json"]);
    }


    private static void Map(
        IEndpointConventionBuilder builder,
        string operation,
        string permission,
        Type? requestType,
        Type? responseType,
        string[]? accepts = null,
        bool noContent = false,
        int responseStatus = StatusCodes.Status200OK) =>
        builder.WithModuleOperation(
                $"ElsaWorkflowsDesignApiEndpoints{operation}",
                OwnerId,
                noContent ? null : responseType,
                accepts is null ? null : requestType,
                accepts,
                noContent ? StatusCodes.Status204NoContent : responseStatus)
            .RequirePermission(permission);
}
