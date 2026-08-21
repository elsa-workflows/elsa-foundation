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
/// <summary>Authoring-support endpoints: activity input options, scoped-variable analysis, activity structures, and expression tooling.</summary>
public static partial class WorkflowsDesignApi
{
    private static async Task HandleActivityInputOptionsAsync(HttpContext context)
    {
        var request = await ReadJsonAsync<ActivityInputOptionsRequest>(context);
        if (request is null)
            return;

        request = request with
        {
            ActivityVersionId = Route(context, "activityVersionId") ?? request.ActivityVersionId,
            InputName = Route(context, "inputName") ?? request.InputName
        };
        context.Response.Headers.CacheControl = "no-store";
        var result = await context.RequestServices.GetRequiredService<ActivityInputOptionsAuthoringService>().ResolveAsync(
            request.ActivityVersionId, request.InputName, request.NodeId, request.WorkflowState, context.RequestAborted);
        await JsonResult(context, result.StatusCode == StatusCodes.Status200OK
            ? new ActivityInputOptionsResponse(result.Options!)
            : new ActivityInputOptionsResponse(Error: result.Error, Code: result.Code),
            result.StatusCode);
    }

    private static async Task HandleScopedVariableAnalysisAsync(HttpContext context)
    {
        var request = await ReadJsonAsync<AnalyzeScopedVariablesRequest>(context);
        if (request is null)
            return;
        if (request.State is null)
        {
            await WriteLegacyErrorAsync(context, "A workflow definition state is required.", StatusCodes.Status400BadRequest);
            return;
        }
        if (request.NodeId is not null && string.IsNullOrWhiteSpace(request.NodeId))
        {
            await WriteLegacyErrorAsync(context, "The selected activity node id cannot be empty.", StatusCodes.Status400BadRequest);
            return;
        }

        var authoring = context.RequestServices.GetRequiredService<ScopedVariableAuthoringContract>();
        var state = request.State.ToState();
        await JsonResult(context, new ScopedVariableAnalysisResponse(
            authoring.GetVisibleVariables(state, request.NodeId), authoring.GetShadowingWarnings(state)));
    }

    private static Task HandleStructuresAsync(HttpContext context) =>
        RequestResult<ListActivityStructures, ActivityStructuresResponse>(context, new());

    private static Task HandleExpressionDescriptorsAsync(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        var providers = context.RequestServices.GetServices<IExpressionToolingProvider>()
            .OrderBy(provider => provider.ExpressionType, StringComparer.Ordinal)
            .Select(provider =>
            {
                var assembly = provider.GetType().Assembly.GetName();
                return new ExpressionToolingDescriptor(provider.ExpressionType, assembly.Name ?? provider.GetType().Namespace ?? provider.ExpressionType, assembly.Version?.ToString() ?? "0.0.0.0", provider.SupportedVersion, provider.DeclaredCapabilities);
            }).ToArray();
        var result = providers.Length == 0
            ? ExpressionToolingOutcome<IReadOnlyList<ExpressionToolingDescriptor>>.SupportedEmpty(providers, ExpressionToolingContractVersion.V1, "descriptors", "descriptors")
            : ExpressionToolingOutcome<IReadOnlyList<ExpressionToolingDescriptor>>.Success(providers, ExpressionToolingContractVersion.V1, "descriptors", "descriptors");
        return JsonResult(context, new ExpressionToolingDescriptorsResponse(result));
    }

    private static async Task HandleExpressionContextAsync(HttpContext context)
    {
        var request = await ReadJsonAsync<ExpressionToolingContextRequest>(context);
        if (request is null)
            return;
        context.Response.Headers.CacheControl = "no-store";
        var result = await ExpressionToolingApiHandlers.ResolveContextAsync(context, request);
        await JsonResult(context, new ExpressionToolingContextResponse(result));
    }

    private static async Task HandleExpressionSymbolsAsync(HttpContext context)
    {
        var request = await ReadJsonAsync<ExpressionToolingContextRequest>(context);
        if (request is null)
            return;
        context.Response.Headers.CacheControl = "no-store";
        var result = await ExpressionToolingApiHandlers.SearchSymbolsAsync(context, request);
        await JsonResult(context, new ExpressionToolingOperationResponse<ExpressionToolingItems>(result));
    }

    private static async Task HandleExpressionCompletionsAsync(HttpContext context)
    {
        var request = await ReadJsonAsync<ExpressionToolingCompletionRequest>(context);
        if (request is null)
            return;
        context.Response.Headers.CacheControl = "no-store";
        var result = await ExpressionToolingApiHandlers.CompleteAsync(context, request);
        await JsonResult(context, new ExpressionToolingOperationResponse<ExpressionToolingItems>(result));
    }

    private static async Task HandleExpressionHoverAsync(HttpContext context)
    {
        var request = await ReadJsonAsync<ExpressionToolingHoverRequest>(context);
        if (request is null)
            return;
        context.Response.Headers.CacheControl = "no-store";
        var result = await ExpressionToolingApiHandlers.HoverAsync(context, request);
        await JsonResult(context, new ExpressionToolingOperationResponse<ExpressionHover>(result));
    }

    private static async Task HandleExpressionValidateAsync(HttpContext context)
    {
        var request = await ReadJsonAsync<ExpressionToolingSourceRequest>(context);
        if (request is null)
            return;
        context.Response.Headers.CacheControl = "no-store";
        var result = await ExpressionToolingApiHandlers.ValidateAsync(context, request);
        await JsonResult(context, new ExpressionToolingOperationResponse<ExpressionDiagnosticSet>(result));
    }
}
