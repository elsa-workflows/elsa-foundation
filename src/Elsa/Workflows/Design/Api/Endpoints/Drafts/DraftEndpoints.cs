using Elsa.Api.Endpoints;
using Elsa.Api.Mediator;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Models;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Design.Api.Endpoints.Drafts;

/// <summary>Workflow draft endpoints: read, replace, discard, promote, preflight, and validations.</summary>
internal static class DraftEndpoints
{
    private static readonly string[] AcceptsAnyOrJson = ["*/*", "application/json"];
    private static readonly string[] AcceptsJson = ["application/json"];

    public static void Map(ModuleEndpointGroup api)
    {
        api.MapRequest<GetDraft, WorkflowDraftView>(
                HttpMethods.Get, RouteConstants.GetRoute("drafts/{draftId}"), "DraftsGet", accepts: AcceptsAnyOrJson)
            .RequirePermission(WorkflowDesignPermissions.Read);

        api.MapCommand<ReplaceDraft, WorkflowDraftView>(
                HttpMethods.Put, RouteConstants.GetRoute("drafts/{draftId}"), "DraftsReplace", accepts: AcceptsJson)
            .RequirePermission(WorkflowDesignPermissions.Manage);

        api.MapCommand<DiscardDraft>(
                HttpMethods.Delete, RouteConstants.GetRoute("drafts/{draftId}"), "DraftsDiscard", accepts: AcceptsAnyOrJson)
            .RequirePermission(WorkflowDesignPermissions.Manage);

        api.MapCommand<PromoteDraft, WorkflowDefinitionVersionDetailsView>(
                HttpMethods.Post, RouteConstants.GetRoute("drafts/{draftId}/promote"), "DraftsPromote",
                // The route returns 201, but the published document declares 200. Correcting the
                // document is a contract change, tracked separately from this refactor.
                accepts: AcceptsJson, successStatus: StatusCodes.Status201Created,
                documentedStatus: StatusCodes.Status200OK)
            .RequirePermission(WorkflowDesignPermissions.Manage);

        api.MapRequest<PreflightDraftPromotion, PromotionPreflightAssessmentView>(
                HttpMethods.Post, RouteConstants.GetRoute("drafts/{draftId}/promotion-preflight"), "DraftsPromotionPreflight", accepts: AcceptsJson)
            .RequirePermission(WorkflowDesignPermissions.Manage);

        api.MapRequest<GetDraftValidations, DraftValidationsView>(
                HttpMethods.Get, RouteConstants.GetRoute("drafts/{draftId}/validations"), "DraftsValidations", accepts: AcceptsAnyOrJson)
            .RequirePermission(WorkflowDesignPermissions.Read);
    }
}
