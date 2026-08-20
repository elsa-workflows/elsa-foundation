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
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;

namespace Elsa.Workflows.Design.Api;

/// <summary>Maps the workflow design management surface using ordinary ASP.NET Core endpoints.</summary>
/// <summary>Draft lifecycle endpoints: read, replace, discard, promote, promotion preflight, and validations.</summary>
public static partial class WorkflowsDesignApi
{
    private static Task HandleGetDraftAsync(HttpContext context) =>
        RequestResult<GetDraft, WorkflowDraftView>(context, new(Route(context, "draftId") ?? string.Empty));

    private static async Task HandleReplaceDraftAsync(HttpContext context)
    {
        var request = await ReadJsonAsync<ReplaceDraft>(context);
        if (request is not null)
            request = request with { DraftId = Route(context, "draftId") ?? request.DraftId };
        await CommandResult<ReplaceDraft, WorkflowDraftView>(context, request);
    }

    private static async Task HandleDiscardDraftAsync(HttpContext context)
    {
        if (DeleteWithoutJsonBody(context))
        {
            await NoContentResult(context, new DiscardDraft(null, Route(context, "draftId") ?? string.Empty));
            return;
        }
        var request = await ReadJsonAsync<DiscardDraft>(context);
        if (request is not null)
            request = request with { DraftId = Route(context, "draftId") ?? request.DraftId };
        await NoContentResult(context, request);
    }

    private static async Task HandlePromoteDraftAsync(HttpContext context)
    {
        var request = await ReadJsonAsync<PromoteDraft>(context);
        if (request is not null)
            request = request with { DraftId = Route(context, "draftId") ?? request.DraftId };
        await CommandResult<PromoteDraft, WorkflowDefinitionVersionDetailsView>(context, request, StatusCodes.Status201Created, promote: true);
    }

    private static async Task HandlePromotionPreflightAsync(HttpContext context)
    {
        var request = await ReadJsonAsync<PreflightDraftPromotion>(context);
        if (request is not null)
            request = request with { DraftId = Route(context, "draftId") ?? request.DraftId };
        await RequestResult<PreflightDraftPromotion, PromotionPreflightAssessmentView>(context, request);
    }

    private static Task HandleDraftValidationsAsync(HttpContext context) =>
        RequestResult<GetDraftValidations, DraftValidationsView>(context, new(Route(context, "draftId") ?? string.Empty));
}
