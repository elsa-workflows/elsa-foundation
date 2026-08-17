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
public static class WorkflowsDesignApi
{
    private const string OwnerId = "Elsa.Workflows.Design.Api";
    private const string JsonContentType = "application/json; charset=utf-8";
    private const string ProblemJsonContentType = "application/problem+json; charset=utf-8";

    public static void MapWorkflowsDesignApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var descriptionMethod = typeof(RequestDelegate).GetMethod(nameof(RequestDelegate.Invoke))
            ?? throw new InvalidOperationException("RequestDelegate.Invoke metadata is unavailable.");

        RequestDelegate activityInputOptions = static context => HandleActivityInputOptionsAsync(context);
        RequestDelegate listDefinitions = static context => HandleListDefinitionsAsync(context);
        RequestDelegate addDefinition = static context => HandleAddDefinitionAsync(context);
        RequestDelegate submitDefinition = static context => HandleSubmitDefinitionAsync(context);
        RequestDelegate submitSchema = static context => HandleSubmitSchemaAsync(context);
        RequestDelegate getDefinition = static context => HandleGetDefinitionAsync(context);
        RequestDelegate deleteDefinition = static context => HandleDeleteDefinitionAsync(context);
        RequestDelegate updateDefinition = static context => HandleUpdateDefinitionAsync(context);
        RequestDelegate deleteDefinitionPermanently = static context => HandleDeleteDefinitionPermanentlyAsync(context);
        RequestDelegate restoreDefinition = static context => HandleRestoreDefinitionAsync(context);
        RequestDelegate listVersions = static context => HandleListVersionsAsync(context);
        RequestDelegate getDraft = static context => HandleGetDraftAsync(context);
        RequestDelegate replaceDraft = static context => HandleReplaceDraftAsync(context);
        RequestDelegate discardDraft = static context => HandleDiscardDraftAsync(context);
        RequestDelegate promoteDraft = static context => HandlePromoteDraftAsync(context);
        RequestDelegate promotionPreflight = static context => HandlePromotionPreflightAsync(context);
        RequestDelegate draftValidations = static context => HandleDraftValidationsAsync(context);
        RequestDelegate expressionCompletions = static context => HandleExpressionCompletionsAsync(context);
        RequestDelegate expressionContext = static context => HandleExpressionContextAsync(context);
        RequestDelegate expressionDescriptors = static context => HandleExpressionDescriptorsAsync(context);
        RequestDelegate expressionHover = static context => HandleExpressionHoverAsync(context);
        RequestDelegate expressionSymbols = static context => HandleExpressionSymbolsAsync(context);
        RequestDelegate expressionValidate = static context => HandleExpressionValidateAsync(context);
        RequestDelegate scopedVariableAnalysis = static context => HandleScopedVariableAnalysisAsync(context);
        RequestDelegate structures = static context => HandleStructuresAsync(context);
        RequestDelegate addVersion = static context => HandleAddVersionAsync(context);
        RequestDelegate getVersion = static context => HandleGetVersionAsync(context);

        Map(endpoints.MapPost(RouteConstants.ActivityInputOptions, activityInputOptions), "AuthoringResolveActivityInputOptions", WorkflowDesignPermissions.Read, typeof(ActivityInputOptionsRequest), typeof(ActivityInputOptionsResponse), descriptionMethod, accepts: ["application/json"]);
        Map(endpoints.MapGet(RouteConstants.Definitions, listDefinitions), "DefinitionsList", WorkflowDesignPermissions.Read, typeof(ListDefinitions), typeof(WorkflowDefinitionListView), descriptionMethod, accepts: ["*/*", "application/json"]);
        Map(endpoints.MapPost(RouteConstants.Definitions, addDefinition), "DefinitionsAdd", WorkflowDesignPermissions.Manage, typeof(AddDefinition), typeof(WorkflowDefinitionDetailsView), descriptionMethod, accepts: ["application/json"]);
        Map(endpoints.MapPost(RouteConstants.GetRoute("definitions/submit"), submitDefinition), "DefinitionsSubmit", WorkflowDesignPermissions.Manage, typeof(SubmitDefinition), typeof(SubmittedWorkflowDefinitionView), descriptionMethod, accepts: ["application/json"]);
        Map(endpoints.MapGet(RouteConstants.DefinitionSubmitSchema, submitSchema), "DefinitionsSubmitSchema", WorkflowDesignPermissions.Read, null, typeof(WorkflowDefinitionSubmitSchemaView), descriptionMethod);
        Map(endpoints.MapGet(RouteConstants.GetRoute("definitions/{definitionId}"), getDefinition), "DefinitionsGet", WorkflowDesignPermissions.Read, typeof(GetDefinition), typeof(WorkflowDefinitionDetailsView), descriptionMethod, accepts: ["*/*", "application/json"]);
        Map(endpoints.MapDelete(RouteConstants.GetRoute("definitions/{definitionId}"), deleteDefinition), "DefinitionsDelete", WorkflowDesignPermissions.Manage, typeof(SoftDeleteDefinition), null, descriptionMethod, accepts: ["*/*", "application/json"], noContent: true);
        Map(endpoints.MapPatch(RouteConstants.GetRoute("definitions/{definitionId}"), updateDefinition), "DefinitionsUpdate", WorkflowDesignPermissions.Manage, typeof(UpdateDefinitionMetadata), typeof(WorkflowDefinitionDetailsView), descriptionMethod, accepts: ["application/json"]);
        Map(endpoints.MapDelete(RouteConstants.GetRoute("definitions/{definitionId}/permanent"), deleteDefinitionPermanently), "DefinitionsDeletePermanently", WorkflowDesignPermissions.Manage, typeof(DeleteDefinitionPermanently), null, descriptionMethod, accepts: ["*/*", "application/json"], noContent: true);
        Map(endpoints.MapPost(RouteConstants.GetRoute("definitions/{definitionId}/restore"), restoreDefinition), "DefinitionsRestore", WorkflowDesignPermissions.Manage, typeof(RestoreDefinition), null, descriptionMethod, accepts: ["application/json"], noContent: true);
        Map(endpoints.MapGet(RouteConstants.GetRoute("definitions/{definitionId}/versions"), listVersions), "VersionsList", WorkflowDesignPermissions.Read, typeof(ListDefinitionVersions), typeof(IEnumerable<WorkflowDefinitionVersionSummary>), descriptionMethod, accepts: ["*/*", "application/json"]);
        Map(endpoints.MapGet(RouteConstants.GetRoute("drafts/{draftId}"), getDraft), "DraftsGet", WorkflowDesignPermissions.Read, typeof(GetDraft), typeof(WorkflowDraftView), descriptionMethod, accepts: ["*/*", "application/json"]);
        Map(endpoints.MapPut(RouteConstants.GetRoute("drafts/{draftId}"), replaceDraft), "DraftsReplace", WorkflowDesignPermissions.Manage, typeof(ReplaceDraft), typeof(WorkflowDraftView), descriptionMethod, accepts: ["application/json"]);
        Map(endpoints.MapDelete(RouteConstants.GetRoute("drafts/{draftId}"), discardDraft), "DraftsDiscard", WorkflowDesignPermissions.Manage, typeof(DiscardDraft), null, descriptionMethod, accepts: ["*/*", "application/json"], noContent: true);
        Map(endpoints.MapPost(RouteConstants.GetRoute("drafts/{draftId}/promote"), promoteDraft), "DraftsPromote", WorkflowDesignPermissions.Manage, typeof(PromoteDraft), typeof(WorkflowDefinitionVersionDetailsView), descriptionMethod, accepts: ["application/json"]);
        Map(endpoints.MapPost(RouteConstants.GetRoute("drafts/{draftId}/promotion-preflight"), promotionPreflight), "DraftsPromotionPreflight", WorkflowDesignPermissions.Manage, typeof(PreflightDraftPromotion), typeof(PromotionPreflightAssessmentView), descriptionMethod, accepts: ["application/json"]);
        Map(endpoints.MapGet(RouteConstants.GetRoute("drafts/{draftId}/validations"), draftValidations), "DraftsValidations", WorkflowDesignPermissions.Read, typeof(GetDraftValidations), typeof(DraftValidationsView), descriptionMethod, accepts: ["*/*", "application/json"]);
        Map(endpoints.MapPost(RouteConstants.ExpressionToolingCompletions, expressionCompletions), "AuthoringCompleteExpressionTooling", WorkflowDesignPermissions.Read, typeof(ExpressionToolingCompletionRequest), typeof(ExpressionToolingOperationResponse<ExpressionToolingItems>), descriptionMethod, accepts: ["application/json"]);
        Map(endpoints.MapPost(RouteConstants.ExpressionToolingContext, expressionContext), "AuthoringResolveExpressionToolingContext", WorkflowDesignPermissions.Read, typeof(ExpressionToolingContextRequest), typeof(ExpressionToolingContextResponse), descriptionMethod, accepts: ["application/json"]);
        Map(endpoints.MapGet(RouteConstants.ExpressionToolingDescriptors, expressionDescriptors), "AuthoringDescribeExpressionTooling", WorkflowDesignPermissions.Read, null, typeof(ExpressionToolingDescriptorsResponse), descriptionMethod);
        Map(endpoints.MapPost(RouteConstants.ExpressionToolingHover, expressionHover), "AuthoringHoverExpressionTooling", WorkflowDesignPermissions.Read, typeof(ExpressionToolingHoverRequest), typeof(ExpressionToolingOperationResponse<ExpressionHover>), descriptionMethod, accepts: ["application/json"]);
        Map(endpoints.MapPost(RouteConstants.ExpressionToolingSymbols, expressionSymbols), "AuthoringSearchExpressionToolingSymbols", WorkflowDesignPermissions.Read, typeof(ExpressionToolingContextRequest), typeof(ExpressionToolingOperationResponse<ExpressionToolingItems>), descriptionMethod, accepts: ["application/json"]);
        Map(endpoints.MapPost(RouteConstants.ExpressionToolingValidate, expressionValidate), "AuthoringValidateExpressionTooling", WorkflowDesignPermissions.Read, typeof(ExpressionToolingSourceRequest), typeof(ExpressionToolingOperationResponse<ExpressionDiagnosticSet>), descriptionMethod, accepts: ["application/json"]);
        Map(endpoints.MapPost(RouteConstants.ScopedVariableAnalysis, scopedVariableAnalysis), "AuthoringAnalyzeScopedVariables", WorkflowDesignPermissions.Read, typeof(AnalyzeScopedVariablesRequest), typeof(ScopedVariableAnalysisResponse), descriptionMethod, accepts: ["application/json"]);
        Map(endpoints.MapGet(RouteConstants.Structures, structures), "StructuresList", WorkflowDesignPermissions.Read, null, typeof(ActivityStructuresResponse), descriptionMethod);
        Map(endpoints.MapPost(RouteConstants.GetRoute("versions/ingest"), addVersion), "VersionsAdd", WorkflowDesignPermissions.Manage, typeof(AddVersion), typeof(WorkflowDefinitionVersionDetailsView), descriptionMethod, accepts: ["application/json"]);
        Map(endpoints.MapGet(RouteConstants.GetRoute("versions/{versionId}"), getVersion), "VersionsGet", WorkflowDesignPermissions.Read, typeof(GetVersion), typeof(WorkflowDefinitionVersionDetailsView), descriptionMethod, accepts: ["*/*", "application/json"]);
    }

    private static void Map(
        IEndpointConventionBuilder builder,
        string operation,
        string permission,
        Type? requestType,
        Type? responseType,
        System.Reflection.MethodInfo descriptionMethod,
        string[]? accepts = null,
        bool noContent = false,
        int responseStatus = StatusCodes.Status200OK)
    {
        var metadata = new List<object>
        {
            Response(noContent ? StatusCodes.Status204NoContent : responseStatus, noContent ? typeof(void) : responseType!),
            Unauthorized(),
            Forbidden()
        };
        if (accepts is not null && requestType is not null)
            metadata.Add(new AcceptsMetadata(accepts, requestType, false));

        builder.WithName($"ElsaWorkflowsDesignApiEndpoints{operation}")
            .WithTags(OwnerId)
            .WithOwner(OwnerId)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequirePermission(permission)
            .WithMetadata(metadata.ToArray())
            .WithMetadata(descriptionMethod)
            .RequireStableOpenApi();
    }

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

    private static async Task HandleListDefinitionsAsync(HttpContext context)
    {
        var query = context.Request.Query;
        var request = new ListDefinitions(
            Query(query, "id"), Query(query, "name"), Query(query, "searchTerm"), Query(query, "description"), NullableBool(query, "tenantAgnostic"), Query(query, "state"));
        await RequestResult<ListDefinitions, WorkflowDefinitionListView>(context, request);
    }

    private static async Task HandleAddDefinitionAsync(HttpContext context) =>
        await CommandResult<AddDefinition, WorkflowDefinitionDetailsView>(context, await ReadJsonAsync<AddDefinition>(context));

    private static async Task HandleSubmitDefinitionAsync(HttpContext context) =>
        await CommandResult<SubmitDefinition, SubmittedWorkflowDefinitionView>(context, await ReadJsonAsync<SubmitDefinition>(context));

    private static Task HandleSubmitSchemaAsync(HttpContext context) =>
        RequestResult<GetWorkflowDefinitionSubmitSchema, WorkflowDefinitionSubmitSchemaView>(context, new());

    private static Task HandleGetDefinitionAsync(HttpContext context) =>
        RequestResult<GetDefinition, WorkflowDefinitionDetailsView>(context, new(Route(context, "definitionId") ?? string.Empty));

    private static async Task HandleDeleteDefinitionAsync(HttpContext context)
    {
        if (DeleteWithoutJsonBody(context))
        {
            await NoContentResult(context, new SoftDeleteDefinition(null, Route(context, "definitionId") ?? string.Empty));
            return;
        }
        var request = await ReadJsonAsync<SoftDeleteDefinition>(context);
        if (request is not null)
            request = request with { DefinitionId = Route(context, "definitionId") ?? request.DefinitionId };
        await NoContentResult(context, request);
    }

    private static async Task HandleUpdateDefinitionAsync(HttpContext context)
    {
        var request = await ReadJsonAsync<UpdateDefinitionMetadata>(context);
        if (request is not null)
            request = request with { DefinitionId = Route(context, "definitionId") ?? request.DefinitionId };
        await CommandResult<UpdateDefinitionMetadata, WorkflowDefinitionDetailsView>(context, request);
    }

    private static async Task HandleDeleteDefinitionPermanentlyAsync(HttpContext context)
    {
        if (DeleteWithoutJsonBody(context))
        {
            await NoContentResult(context, new DeleteDefinitionPermanently(null, Route(context, "definitionId") ?? string.Empty));
            return;
        }
        var request = await ReadJsonAsync<DeleteDefinitionPermanently>(context);
        if (request is not null)
            request = request with { DefinitionId = Route(context, "definitionId") ?? request.DefinitionId };
        await NoContentResult(context, request);
    }

    private static async Task HandleRestoreDefinitionAsync(HttpContext context)
    {
        if (string.IsNullOrWhiteSpace(context.Request.ContentType) ||
            !string.Equals(context.Request.ContentType.Split(';', 2)[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return;
        }
        var request = await ReadJsonAsync<RestoreDefinition>(context);
        if (request is not null)
            request = request with { DefinitionId = Route(context, "definitionId") ?? request.DefinitionId };
        await NoContentResult(context, request);
    }

    private static Task HandleListVersionsAsync(HttpContext context) =>
        RequestResult<ListDefinitionVersions, IEnumerable<WorkflowDefinitionVersionSummary>>(context, new(Route(context, "definitionId") ?? string.Empty));

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

    private static async Task HandleAddVersionAsync(HttpContext context) =>
        await CommandResult<AddVersion, WorkflowDefinitionVersionDetailsView>(context, await ReadJsonAsync<AddVersion>(context));

    private static Task HandleGetVersionAsync(HttpContext context) =>
        RequestResult<GetVersion, WorkflowDefinitionVersionDetailsView>(context, new(Route(context, "versionId") ?? string.Empty));

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

    private static async Task<TResponse?> SendRequestAsync<TRequest, TResponse>(HttpContext context, TRequest request)
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        var sender = context.RequestServices.GetRequiredService<IRequestSender>();
        return await sender.Send(request, context.RequestAborted);
    }

    private static async Task SendCommandAsync<TCommand>(HttpContext context, TCommand command)
        where TCommand : ICommand =>
        await context.RequestServices.GetRequiredService<ICommandSender>().Send(command, context.RequestAborted);

    private static async Task RequestResult<TRequest, TResponse>(HttpContext context, TRequest? request)
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        if (request is null)
            return;
        try
        {
            await JsonResult(context, await SendRequestAsync<TRequest, TResponse>(context, request));
        }
        catch (EntityNotFoundException exception)
        {
            await WriteLegacyErrorAsync(context, exception.Message, StatusCodes.Status404NotFound);
        }
        catch (ArgumentException exception)
        {
            await WriteLegacyErrorAsync(context, exception.Message, StatusCodes.Status400BadRequest);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogUnexpected(context, exception, typeof(TRequest));
            await WriteLegacyErrorAsync(context, "Unexpected error occurred", StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task CommandResult<TCommand, TResponse>(HttpContext context, TCommand? command, int statusCode = StatusCodes.Status200OK, bool promote = false)
        where TCommand : ICommand<TResponse>
        where TResponse : notnull
    {
        if (command is null)
            return;
        try
        {
            var response = await context.RequestServices.GetRequiredService<ICommandSender>().Send(command, context.RequestAborted);
            await JsonResult(context, response, statusCode);
        }
        catch (DraftHasValidationErrorsException exception) when (promote)
        {
            var errors = exception.Errors
                .GroupBy(error => string.IsNullOrWhiteSpace(error.Path) ? "generalErrors" : error.Path, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Select(error => error.Message).ToArray(), StringComparer.Ordinal);
            errors.TryAdd("generalErrors", [exception.Message]);
            await WriteValidationErrorAsync(context, errors, StatusCodes.Status409Conflict);
        }
        catch (EntityNotFoundException exception)
        {
            await WriteLegacyErrorAsync(context, exception.Message, StatusCodes.Status404NotFound);
        }
        catch (WorkflowDefinitionVersionConflictException exception) when (promote)
        {
            await WriteLegacyErrorAsync(context, exception.Message, StatusCodes.Status409Conflict);
        }
        catch (WorkflowPromotionOperationConflictException exception) when (promote)
        {
            await WriteLegacyErrorAsync(context, exception.Message, StatusCodes.Status409Conflict);
        }
        catch (WorkflowDefinitionNotSoftDeletedException exception)
        {
            await WriteLegacyErrorAsync(context, exception.Message, StatusCodes.Status409Conflict);
        }
        catch (PermanentDeletionUnavailableException exception)
        {
            await WriteLegacyErrorAsync(context, exception.Message, StatusCodes.Status501NotImplemented);
        }
        catch (ArgumentException exception)
        {
            await WriteLegacyErrorAsync(context, exception.Message, StatusCodes.Status400BadRequest);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogUnexpected(context, exception, typeof(TCommand));
            await WriteLegacyErrorAsync(context, "Unexpected error occurred", StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task NoContentResult<TCommand>(HttpContext context, TCommand? command)
        where TCommand : ICommand
    {
        if (command is null)
            return;
        try
        {
            await SendCommandAsync(context, command);
            await Results.NoContent().ExecuteAsync(context);
        }
        catch (EntityNotFoundException exception)
        {
            await WriteLegacyErrorAsync(context, exception.Message, StatusCodes.Status404NotFound);
        }
        catch (WorkflowDefinitionNotSoftDeletedException exception)
        {
            await WriteLegacyErrorAsync(context, exception.Message, StatusCodes.Status409Conflict);
        }
        catch (PermanentDeletionUnavailableException exception)
        {
            await WriteLegacyErrorAsync(context, exception.Message, StatusCodes.Status501NotImplemented);
        }
        catch (ArgumentException exception)
        {
            await WriteLegacyErrorAsync(context, exception.Message, StatusCodes.Status400BadRequest);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogUnexpected(context, exception, typeof(TCommand));
            await WriteLegacyErrorAsync(context, "Unexpected error occurred", StatusCodes.Status500InternalServerError);
        }
    }

    private static async ValueTask<T?> ReadJsonAsync<T>(HttpContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.Request.ContentType) &&
            !string.Equals(context.Request.ContentType.Split(';', 2)[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase))
        {
            await WriteLegacyErrorAsync(context, "The request content type must be application/json.", StatusCodes.Status415UnsupportedMediaType);
            return default;
        }

        try
        {
            var request = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, WorkflowsDesignJsonContext.Default.Options, context.RequestAborted);
            if (request is not null)
                return request;
        }
        catch (JsonException exception)
        {
            var message = exception.Message.Replace(" Path: $ |", "", StringComparison.Ordinal);
            await WriteBindingErrorAsync(context, message, StatusCodes.Status400BadRequest);
            return default;
        }

        await WriteLegacyErrorAsync(context, "A request body is required.", StatusCodes.Status400BadRequest);
        return default;
    }

    private static Task JsonResult<T>(HttpContext context, T value, int statusCode = StatusCodes.Status200OK, string contentType = JsonContentType)
    {
        context.Response.StatusCode = statusCode;
        var typeInfo = WorkflowsDesignJsonContext.Default.GetTypeInfo(typeof(T))
                       ?? throw new InvalidOperationException($"No source-generated JSON metadata exists for '{typeof(T).FullName}'.");
        return Results.Json(value, typeInfo, contentType).ExecuteAsync(context);
    }

    private static Task WriteLegacyErrorAsync(HttpContext context, string message, int statusCode)
    {
        context.Response.StatusCode = statusCode;
        return JsonResult(context, new WorkflowDesignError(
            new Dictionary<string, string[]>(StringComparer.Ordinal) { ["generalErrors"] = [message] },
            "One or more errors occurred!", statusCode), statusCode, ProblemJsonContentType);
    }

    private static Task WriteValidationErrorAsync(HttpContext context, IReadOnlyDictionary<string, string[]> errors, int statusCode) =>
        JsonResult(context, new WorkflowDesignError(errors, "One or more errors occurred!", statusCode), statusCode, ProblemJsonContentType);

    private static Task WriteBindingErrorAsync(HttpContext context, string message, int statusCode) =>
        JsonResult(context, new WorkflowDesignError(
            new Dictionary<string, string[]>(StringComparer.Ordinal) { ["serializerErrors"] = [message] },
            "One or more errors occurred!", statusCode), statusCode, ProblemJsonContentType);

    private static ProducesResponseTypeMetadata Response(int statusCode, Type type) =>
        new(statusCode, type, type == typeof(void) ? [] : ["application/json"]);

    private static ProducesResponseTypeMetadata Unauthorized() =>
        new(StatusCodes.Status401Unauthorized, typeof(void), []);

    private static ProducesResponseTypeMetadata Forbidden() =>
        new(StatusCodes.Status403Forbidden, typeof(void), []);

    private static string? Route(HttpContext context, string key) =>
        context.Request.RouteValues.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static bool DeleteWithoutJsonBody(HttpContext context) =>
        HttpMethods.IsDelete(context.Request.Method) &&
        (string.IsNullOrWhiteSpace(context.Request.ContentType) ||
         !string.Equals(context.Request.ContentType.Split(';', 2)[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase));

    private static string? Query(IQueryCollection query, string key) =>
        query.TryGetValue(key, out var value) ? value.ToString() : null;

    private static bool? NullableBool(IQueryCollection query, string key) =>
        bool.TryParse(Query(query, key), out var value) ? value : null;

    private static void LogUnexpected(HttpContext context, Exception exception, Type requestType) =>
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(WorkflowsDesignApi))
            .LogError(exception, "Unexpected error occurred when handling request '{type}'", requestType);
}

internal sealed record WorkflowDesignError(
    IReadOnlyDictionary<string, string[]> Errors,
    string Message,
    int StatusCode);
