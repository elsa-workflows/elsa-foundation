using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Constants;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Core.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Exceptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Elsa.Activities.Design.Api;

/// <summary>Maps the Activities Design REST surface using ordinary ASP.NET Core Minimal API endpoints.</summary>
public static class ActivitiesDesignApi
{
    private const string OwnerId = "Elsa.Activities.Design.Api";
    private const string JsonMediaType = "application/json";
    private const string AuthoringProblemJsonContentType = "application/json; charset=utf-8";
    private const string LegacyProblemJsonContentType = "application/problem+json";

    /// <summary>Maps all 38 Activities Design operations under the owner route group.</summary>
    public static RouteGroupBuilder MapActivitiesDesignApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/design/activities");

        // Availability, catalog, and capability routes retain the historical mediator error transport.
        Map(group.MapGet("/availability/settings", (RequestDelegate)HandleGetAvailabilitySettingsAsync), "AvailabilityGetSettings", ActivityDesignPermissions.Read, typeof(GetActivityAvailabilitySettings), typeof(ActivityAvailabilitySettings), ["*/*", "application/json"]);
        Map(group.MapGet("/availability/diagnostics", (RequestDelegate)HandleListAvailabilityDiagnosticsAsync), "AvailabilityListDiagnostics", ActivityDesignPermissions.Read, null, typeof(ActivityAvailabilityDiagnostics));
        Map(group.MapPut("/availability/settings", (RequestDelegate)HandleSaveAvailabilitySettingsAsync), "AvailabilitySaveSettings", ActivityDesignPermissions.Manage, typeof(SaveActivityAvailabilitySettings), typeof(ActivityAvailabilitySettings), [JsonMediaType]);
        Map(group.MapGet("/authoring-capabilities", (RequestDelegate)HandleGetAuthoringCapabilitiesAsync), "AuthoringCapabilitiesGet", ActivityDesignPermissions.Read, null, typeof(ActivityAuthoringCapabilitiesView));
        Map(group.MapGet("/catalog", (RequestDelegate)HandleListCatalogAsync), "CatalogList", ActivityDesignPermissions.Read, typeof(ListActivityAuthoringCatalog), typeof(ActivityAuthoringCatalogView), ["*/*", "application/json"]);

        // Definitions, drafts, forks, and their authoring operations.
        Map(group.MapPost("/definitions", (RequestDelegate)HandleCreateDefinitionAsync), "DefinitionsAdd", ActivityDesignPermissions.Manage, typeof(CreateReusableActivityDefinition), typeof(ReusableActivityDefinitionMutationView), [JsonMediaType], StatusCodes.Status201Created);
        Map(group.MapPost("/definitions/{definitionId}/fork-previews", (RequestDelegate)HandlePreviewForkAsync), "DefinitionsPreviewFork", ActivityDesignPermissions.Manage, typeof(PreviewReusableActivityFork), typeof(ActivityForkPreviewView), [JsonMediaType]);
        Map(group.MapGet("/definitions", (RequestDelegate)HandleListDefinitionsAsync), "DefinitionsList", ActivityDesignPermissions.Read, typeof(ListReusableActivityDefinitions), typeof(ActivityManagementPageView<ReusableActivityDefinitionManagementView>), ["*/*", "application/json"]);
        Map(group.MapGet("/definitions/{definitionId}", (RequestDelegate)HandleGetDefinitionAsync), "DefinitionsGet", ActivityDesignPermissions.Read, typeof(GetReusableActivityDefinition), typeof(ReusableActivityDefinitionManagementView), ["*/*", "application/json"]);
        Map(group.MapPatch("/definitions/{definitionId}", (RequestDelegate)HandleUpdateDefinitionAsync), "DefinitionsUpdate", ActivityDesignPermissions.Manage, typeof(UpdateReusableActivityDefinition), typeof(ActivityDefinitionIdentityView), [JsonMediaType]);
        Map(group.MapPut("/definitions/{definitionId}/recommendation", (RequestDelegate)HandleSetRecommendationAsync), "DefinitionsRecommendation", ActivityDesignPermissions.Manage, typeof(SetRecommendedReusableActivityVersion), typeof(ActivityDefinitionRecommendationView), [JsonMediaType]);
        Map(group.MapGet("/definitions/picker", (RequestDelegate)HandlePickerAsync), "DefinitionsPicker", ActivityDesignPermissions.Read, typeof(ListRecommendedActivityDefinitions), typeof(RecommendedActivityDefinitionPageView), ["*/*", "application/json"]);
        Map(group.MapGet("/definitions/{definitionId}/drafts", (RequestDelegate)HandleListDraftsAsync), "DefinitionsListDrafts", ActivityDesignPermissions.Read, typeof(ListReusableActivityDrafts), typeof(ActivityManagementPageView<ReusableActivityDraftManagementView>), ["*/*", "application/json"]);
        Map(group.MapPost("/definitions/{definitionId}/drafts", (RequestDelegate)HandleCreateDraftAsync), "DefinitionsAddDraft", ActivityDesignPermissions.Manage, typeof(CreateReusableActivityDraft), typeof(ReusableActivityDraftView), [JsonMediaType], StatusCodes.Status201Created);
        Map(group.MapGet("/definitions/{definitionId}/versions", (RequestDelegate)HandleListVersionsAsync), "DefinitionsListVersions", ActivityDesignPermissions.Read, typeof(ListReusableActivityVersions), typeof(ActivityManagementPageView<ReusableActivityVersionManagementView>), ["*/*", "application/json"]);

        Map(group.MapGet("/drafts/{draftId}", (RequestDelegate)HandleGetDraftAsync), "DraftsGet", ActivityDesignPermissions.Read, typeof(GetReusableActivityDraft), typeof(ReusableActivityDraftView), ["*/*", "application/json"]);
        Map(group.MapPut("/drafts/{draftId}", (RequestDelegate)HandleReplaceDraftAsync), "DraftsReplace", ActivityDesignPermissions.Manage, typeof(ReplaceReusableActivityDraft), typeof(ReusableActivityDraftView), [JsonMediaType]);
        Map(group.MapPatch("/drafts/{draftId}/presentation", (RequestDelegate)HandleUpdateDraftPresentationAsync), "DraftsUpdatePresentation", ActivityDesignPermissions.Manage, typeof(UpdateReusableActivityDraftPresentation), typeof(ReusableActivityDraftView), [JsonMediaType]);
        Map(group.MapPost("/drafts/{draftId}/conflict-copies", (RequestDelegate)HandleCreateConflictCopyAsync), "DraftsConflictCopy", ActivityDesignPermissions.Manage, typeof(CreateReusableActivityDraftConflictCopy), typeof(ReusableActivityDraftView), [JsonMediaType], StatusCodes.Status201Created);
        Map(group.MapPost("/drafts/{draftId}/validate", (RequestDelegate)HandleValidateDraftAsync), "DraftsValidate", ActivityDesignPermissions.Manage, typeof(ValidateReusableActivityDraft), typeof(ActivityDraftValidationView), [JsonMediaType]);
        Map(group.MapPost("/drafts/{draftId}/migrate-provider", (RequestDelegate)HandleMigrateProviderAsync), "DraftsMigrateProvider", ActivityDesignPermissions.Manage, typeof(MigrateReusableActivityDraft), typeof(ReusableActivityDraftView), [JsonMediaType], StatusCodes.Status201Created);
        Map(group.MapPost("/drafts/{draftId}/contract-proposals", (RequestDelegate)HandleProposeContractAsync), "DraftsProposeContract", ActivityDesignPermissions.Manage, typeof(ProposeReusableActivityContract), typeof(ActivityContractProposalView), [JsonMediaType]);
        Map(group.MapPost("/drafts/{draftId}/contract-proposals/apply", (RequestDelegate)HandleApplyContractProposalAsync), "DraftsApplyContractProposal", ActivityDesignPermissions.Manage, typeof(ApplyReusableActivityContractProposal), typeof(ReusableActivityDraftView), [JsonMediaType]);
        Map(group.MapDelete("/drafts/{draftId}", (RequestDelegate)HandleDiscardDraftAsync), "DraftsDiscard", ActivityDesignPermissions.Manage, typeof(DiscardReusableActivityDraft), typeof(void), ["*/*", "application/json"], StatusCodes.Status204NoContent);
        Map(group.MapPost("/drafts/{draftId}/diff", (RequestDelegate)HandlePreviewDraftDiffAsync), "DraftsDiff", ActivityDesignPermissions.Read, typeof(PreviewActivityDraftDiff), typeof(ActivityVersionDiffView), [JsonMediaType]);

        Map(group.MapPost("/fork-candidates/{candidateId}/apply", (RequestDelegate)HandleApplyForkAsync), "ForksApply", ActivityDesignPermissions.Manage, typeof(ApplyReusableActivityFork), typeof(ActivityForkReceiptView), [JsonMediaType], StatusCodes.Status201Created);
        Map(group.MapGet("/forks/{idempotencyKey}", (RequestDelegate)HandleGetForkStatusAsync), "ForksGetStatus", ActivityDesignPermissions.Read, typeof(GetReusableActivityForkStatus), typeof(ActivityForkReceiptView), ["*/*", "application/json"]);

        // Version, dependency, diff, and lifecycle operations.
        Map(group.MapGet("/versions/{versionId}/dependencies", (RequestDelegate)HandleGetDependenciesAsync), "VersionsDependencies", ActivityDesignPermissions.Read, typeof(GetActivityDependencies), typeof(ActivityDependencyPageView), ["*/*", "application/json"]);
        Map(group.MapGet("/versions/{fromVersionId}/diff/{toVersionId}", (RequestDelegate)HandleCompareVersionsAsync), "VersionsDiff", ActivityDesignPermissions.Read, typeof(CompareActivityVersions), typeof(ActivityVersionDiffView), ["*/*", "application/json"]);
        Map(group.MapGet("/versions/{versionId}", (RequestDelegate)HandleGetVersionAsync), "VersionsGet", ActivityDesignPermissions.Read, typeof(GetReusableActivityVersion), typeof(ReusableActivityVersionView), ["*/*", "application/json"]);
        Map(group.MapPost("/versions/{versionId}/retire", (RequestDelegate)HandleRetireVersionAsync), "VersionsRetire", ActivityDesignPermissions.Manage, typeof(RetireReusableActivityVersion), typeof(ReusableActivityVersionLifecycleView), [JsonMediaType]);
        Map(group.MapPost("/versions/{versionId}/restore", (RequestDelegate)HandleRestoreVersionAsync), "VersionsRestore", ActivityDesignPermissions.Manage, typeof(RestoreReusableActivityVersion), typeof(ReusableActivityVersionLifecycleView), [JsonMediaType]);
        Map(group.MapPost("/versions/{versionId}/revoke", (RequestDelegate)HandleRevokeVersionAsync), "VersionsRevoke", ActivityDesignPermissions.Manage, typeof(RevokeReusableActivityVersion), typeof(ReusableActivityVersionLifecycleView), [JsonMediaType]);

        // Upgrade plans are authoring operations with staged handoff and created-location semantics.
        Map(group.MapPost("/upgrade-plans", (RequestDelegate)HandleCreateUpgradePlanAsync), "UpgradePlansCreate", ActivityDesignPermissions.Manage, typeof(CreateActivityUpgradePlan), typeof(ActivityUpgradePlanView), [JsonMediaType], StatusCodes.Status201Created);
        Map(group.MapGet("/upgrade-plans/{planId}", (RequestDelegate)HandleGetUpgradePlanAsync), "UpgradePlansGet", ActivityDesignPermissions.Read, typeof(GetActivityUpgradePlan), typeof(ActivityUpgradePlanView), ["*/*", "application/json"]);
        Map(group.MapPost("/upgrade-plans/{planId}/apply", (RequestDelegate)HandleApplyUpgradePlanAsync), "UpgradePlansApply", ActivityDesignPermissions.Manage, typeof(ApplyActivityUpgradePlan), typeof(ActivityUpgradeApplyResultView), [JsonMediaType]);
        Map(group.MapGet("/upgrade-plans/{planId}/receipts/{receiptId}", (RequestDelegate)HandleGetUpgradeReceiptAsync), "UpgradePlansGetReceipt", ActivityDesignPermissions.Read, typeof(GetActivityUpgradeApplyReceipt), typeof(ActivityUpgradeApplyReceiptView), ["*/*", "application/json"]);
        Map(group.MapPost("/upgrade-plans/{planId}/refresh", (RequestDelegate)HandleRefreshUpgradePlanAsync), "UpgradePlansRefresh", ActivityDesignPermissions.Manage, typeof(RefreshActivityUpgradePlan), typeof(ActivityUpgradePlanView), [JsonMediaType], StatusCodes.Status201Created);

        return group;
    }

    private static void Map(
        IEndpointConventionBuilder builder,
        string operation,
        string permission,
        Type? requestType,
        Type responseType,
        string[]? accepts = null,
        int successStatus = StatusCodes.Status200OK) =>
        builder.WithModuleOperation(
                $"ElsaActivitiesDesignApiEndpoints{operation}",
                OwnerId,
                responseType,
                accepts is null ? null : requestType,
                accepts,
                successStatus)
            .RequirePermission(permission);

    private static Task HandleGetAvailabilitySettingsAsync(HttpContext context) =>
        LegacyRequestResult(context, new GetActivityAvailabilitySettings(Query(context, "scope")));

    private static Task HandleListAvailabilityDiagnosticsAsync(HttpContext context) =>
        LegacyRequestResult(context, new ListActivityAvailabilityDiagnostics());

    private static async Task HandleSaveAvailabilitySettingsAsync(HttpContext context)
    {
        var request = await ReadJsonAsync<SaveActivityAvailabilitySettings>(context);
        if (request is not null)
            await LegacyCommandResult(context, request);
    }

    private static Task HandleGetAuthoringCapabilitiesAsync(HttpContext context) =>
        LegacyRequestResult(context, new GetActivityAuthoringCapabilities());

    private static Task HandleListCatalogAsync(HttpContext context)
    {
        var availability = QueryEnum(context, "availability", ActivityCatalogAvailability.Addable);
        if (!availability.IsValid)
            return WriteQueryBindingProblemAsync(context, availability.Error!);
        return LegacyRequestResult(context, new ListActivityAuthoringCatalog(availability.Value));
    }

    private static async Task HandleCreateDefinitionAsync(HttpContext context)
    {
        var command = await ReadJsonAsync<CreateReusableActivityDefinition>(context);
        await AuthoringCommandResult(context, command, StatusCodes.Status201Created,
            static response => $"/{RouteConstants.GetRoute($"definitions/{response.Definition.DefinitionId}")}");
    }

    private static async Task HandlePreviewForkAsync(HttpContext context)
    {
        var command = await ReadJsonAsync<PreviewReusableActivityFork>(context);
        if (command is not null)
            command = command with { DefinitionId = Route(context, "definitionId") ?? command.DefinitionId };
        await AuthoringCommandResult(context, command);
    }

    private static Task HandleListDefinitionsAsync(HttpContext context)
    {
        var limit = QueryInt(context, "limit", 25);
        if (!limit.IsValid)
            return WriteQueryBindingProblemAsync(context, limit.Error!);
        return AuthoringRequestResult(context, new ListReusableActivityDefinitions(
            limit.Value,
            Query(context, "cursor"),
            Query(context, "search"),
            Query(context, "authority"),
            Query(context, "providerKey"),
            Query(context, "sort") ?? "identity-asc"));
    }

    private static Task HandleGetDefinitionAsync(HttpContext context) =>
        AuthoringRequestResult(context, new GetReusableActivityDefinition(Route(context, "definitionId") ?? string.Empty));

    private static async Task HandleUpdateDefinitionAsync(HttpContext context)
    {
        var command = await ReadJsonAsync<UpdateReusableActivityDefinition>(context);
        if (command is not null)
            command = command with { DefinitionId = Route(context, "definitionId") ?? command.DefinitionId };
        await AuthoringCommandResult(context, command);
    }

    private static async Task HandleSetRecommendationAsync(HttpContext context)
    {
        var command = await ReadJsonAsync<SetRecommendedReusableActivityVersion>(context);
        if (command is not null)
            command = command with { DefinitionId = Route(context, "definitionId") ?? command.DefinitionId };
        await AuthoringCommandResult(context, command);
    }

    private static Task HandlePickerAsync(HttpContext context)
    {
        var offset = QueryInt(context, "offset", 0);
        if (!offset.IsValid)
            return WriteQueryBindingProblemAsync(context, offset.Error!);

        var limit = QueryInt(context, "limit", 25);
        if (!limit.IsValid)
            return WriteQueryBindingProblemAsync(context, limit.Error!);
        return AuthoringRequestResult(context, new ListRecommendedActivityDefinitions(offset.Value, limit.Value));
    }

    private static Task HandleListDraftsAsync(HttpContext context)
    {
        var limit = QueryInt(context, "limit", 25);
        if (!limit.IsValid)
            return WriteQueryBindingProblemAsync(context, limit.Error!);
        return AuthoringRequestResult(context, new ListReusableActivityDrafts(
            Route(context, "definitionId") ?? string.Empty,
            limit.Value,
            Query(context, "cursor"),
            Query(context, "search"),
            Query(context, "providerKey"),
            Query(context, "status"),
            Query(context, "sort") ?? "identity-asc"));
    }

    private static async Task HandleCreateDraftAsync(HttpContext context)
    {
        var command = await ReadJsonAsync<CreateReusableActivityDraft>(context);
        if (command is not null)
            command = command with { DefinitionId = Route(context, "definitionId") ?? command.DefinitionId };
        await AuthoringCommandResult(context, command, StatusCodes.Status201Created,
            static response => $"/{RouteConstants.GetRoute($"drafts/{response.DraftId}")}");
    }

    private static Task HandleListVersionsAsync(HttpContext context)
    {
        var limit = QueryInt(context, "limit", 25);
        if (!limit.IsValid)
            return WriteQueryBindingProblemAsync(context, limit.Error!);
        return AuthoringRequestResult(context, new ListReusableActivityVersions(
            Route(context, "definitionId") ?? string.Empty,
            limit.Value,
            Query(context, "cursor"),
            Query(context, "search"),
            Query(context, "providerKey"),
            Query(context, "lifecycle"),
            Query(context, "sort") ?? "identity-asc"));
    }

    private static Task HandleGetDraftAsync(HttpContext context) =>
        AuthoringRequestResult(context, new GetReusableActivityDraft(Route(context, "draftId") ?? string.Empty));

    private static async Task HandleReplaceDraftAsync(HttpContext context)
    {
        var command = await ReadJsonAsync<ReplaceReusableActivityDraft>(context);
        if (command is not null)
            command = command with { DraftId = Route(context, "draftId") ?? command.DraftId };
        await AuthoringCommandResult(context, command);
    }

    private static async Task HandleUpdateDraftPresentationAsync(HttpContext context)
    {
        var command = await ReadJsonAsync<UpdateReusableActivityDraftPresentation>(context);
        if (command is not null)
            command = command with { DraftId = Route(context, "draftId") ?? command.DraftId };
        await AuthoringCommandResult(context, command);
    }

    private static async Task HandleCreateConflictCopyAsync(HttpContext context)
    {
        var command = await ReadJsonAsync<CreateReusableActivityDraftConflictCopy>(context);
        if (command is not null)
            command = command with { DraftId = Route(context, "draftId") ?? command.DraftId };
        await AuthoringCommandResult(context, command, StatusCodes.Status201Created,
            static response => $"/{RouteConstants.GetRoute($"drafts/{response.DraftId}")}");
    }

    private static async Task HandleValidateDraftAsync(HttpContext context)
    {
        var command = await ReadJsonAsync<ValidateReusableActivityDraft>(context);
        if (command is not null)
            command = command with { DraftId = Route(context, "draftId") ?? command.DraftId };
        await AuthoringCommandResult(context, command);
    }

    private static async Task HandleMigrateProviderAsync(HttpContext context)
    {
        var command = await ReadJsonAsync<MigrateReusableActivityDraft>(context);
        if (command is not null)
            command = command with { DraftId = Route(context, "draftId") ?? command.DraftId };
        await AuthoringCommandResult(context, command, StatusCodes.Status201Created,
            static response => $"/{RouteConstants.GetRoute($"drafts/{response.DraftId}")}");
    }

    private static async Task HandleProposeContractAsync(HttpContext context)
    {
        var request = await ReadJsonAsync<ProposeReusableActivityContract>(context);
        if (request is not null)
            request = request with { DraftId = Route(context, "draftId") ?? request.DraftId };
        await AuthoringRequestResult(context, request);
    }

    private static async Task HandleApplyContractProposalAsync(HttpContext context)
    {
        var command = await ReadJsonAsync<ApplyReusableActivityContractProposal>(context);
        if (command is not null)
            command = command with { DraftId = Route(context, "draftId") ?? command.DraftId };
        await AuthoringCommandResult(context, command);
    }

    private static async Task HandleDiscardDraftAsync(HttpContext context)
    {
        var command = await ReadJsonAsync<DiscardReusableActivityDraft>(context);
        if (command is not null)
            command = command with { DraftId = Route(context, "draftId") ?? command.DraftId };
        await AuthoringNoContentResult(context, command);
    }

    private static async Task HandlePreviewDraftDiffAsync(HttpContext context)
    {
        var request = await ReadJsonAsync<PreviewActivityDraftDiff>(context);
        if (request is not null)
            request = request with { DraftId = Route(context, "draftId") ?? request.DraftId };
        await AuthoringRequestResult(context, request);
    }

    private static async Task HandleApplyForkAsync(HttpContext context)
    {
        var command = await ReadJsonAsync<ApplyReusableActivityFork>(context);
        if (command is not null)
            command = command with { CandidateId = Route(context, "candidateId") ?? command.CandidateId };
        await AuthoringCommandResult(context, command, StatusCodes.Status201Created,
            static response => $"/{RouteConstants.GetRoute($"definitions/{response.Definition.DefinitionId}")}");
    }

    private static Task HandleGetForkStatusAsync(HttpContext context) =>
        AuthoringRequestResult(context, new GetReusableActivityForkStatus(Route(context, "idempotencyKey") ?? string.Empty));

    private static Task HandleGetDependenciesAsync(HttpContext context)
    {
        var transitive = QueryBool(context, "transitive");
        if (!transitive.IsValid)
            return WriteQueryBindingProblemAsync(context, transitive.Error!);

        var limit = QueryNullableInt(context, "limit");
        if (!limit.IsValid)
            return WriteQueryBindingProblemAsync(context, limit.Error!);
        return AuthoringRequestResult(context, new GetActivityDependencies(
            Route(context, "versionId") ?? string.Empty,
            Query(context, "direction") ?? "outbound",
            transitive.Value ?? false,
            Query(context, "include") ?? "versions",
            Query(context, "cursor"),
            limit.Value));
    }

    private static Task HandleCompareVersionsAsync(HttpContext context) =>
        AuthoringRequestResult(context, new CompareActivityVersions(
            Route(context, "fromVersionId") ?? string.Empty,
            Route(context, "toVersionId") ?? string.Empty));

    private static Task HandleGetVersionAsync(HttpContext context) =>
        AuthoringRequestResult(context, new GetReusableActivityVersion(Route(context, "versionId") ?? string.Empty));

    private static async Task HandleRetireVersionAsync(HttpContext context)
    {
        var command = await ReadJsonAsync<RetireReusableActivityVersion>(context);
        if (command is not null)
            command = command with { VersionId = Route(context, "versionId") ?? command.VersionId };
        await AuthoringCommandResult(context, command);
    }

    private static async Task HandleRestoreVersionAsync(HttpContext context)
    {
        var command = await ReadJsonAsync<RestoreReusableActivityVersion>(context);
        if (command is not null)
            command = command with { VersionId = Route(context, "versionId") ?? command.VersionId };
        await AuthoringCommandResult(context, command);
    }

    private static async Task HandleRevokeVersionAsync(HttpContext context)
    {
        var command = await ReadJsonAsync<RevokeReusableActivityVersion>(context);
        if (command is not null)
            command = command with { VersionId = Route(context, "versionId") ?? command.VersionId };
        await AuthoringCommandResult(context, command);
    }

    private static async Task HandleCreateUpgradePlanAsync(HttpContext context)
    {
        var command = await ReadJsonAsync<CreateActivityUpgradePlan>(context);
        await AuthoringCommandResult(context, command, StatusCodes.Status201Created,
            static response => $"/{RouteConstants.GetRoute($"upgrade-plans/{response.PlanId}")}");
    }

    private static Task HandleGetUpgradePlanAsync(HttpContext context) =>
        AuthoringRequestResult(context, new GetActivityUpgradePlan(Route(context, "planId") ?? string.Empty));

    private static async Task HandleApplyUpgradePlanAsync(HttpContext context)
    {
        var command = await ReadJsonAsync<ApplyActivityUpgradePlan>(context);
        if (command is not null)
            command = command with { PlanId = Route(context, "planId") ?? command.PlanId };
        await AuthoringCommandResult(context, command);
    }

    private static Task HandleGetUpgradeReceiptAsync(HttpContext context) =>
        AuthoringRequestResult(context, new GetActivityUpgradeApplyReceipt(
            Route(context, "planId") ?? string.Empty,
            Route(context, "receiptId") ?? string.Empty));

    private static async Task HandleRefreshUpgradePlanAsync(HttpContext context)
    {
        var command = await ReadJsonAsync<RefreshActivityUpgradePlan>(context);
        if (command is not null)
            command = command with { PlanId = Route(context, "planId") ?? command.PlanId };
        await AuthoringCommandResult(context, command, StatusCodes.Status201Created,
            static response => $"/{RouteConstants.GetRoute($"upgrade-plans/{response.PlanId}")}");
    }

    private static async Task AuthoringRequestResult<TResponse>(HttpContext context, IRequest<TResponse>? request)
        where TResponse : notnull
    {
        if (request is null)
            return;

        await AuthoringResult(context, request.GetType(),
            () => context.RequestServices.GetRequiredService<IRequestSender>().Send(request, context.RequestAborted));
    }

    private static async Task AuthoringCommandResult<TResponse>(
        HttpContext context,
        ICommand<TResponse>? command,
        int statusCode = StatusCodes.Status200OK,
        Func<TResponse, string?>? location = null)
        where TResponse : notnull
    {
        if (command is null)
            return;

        await AuthoringResult(
            context,
            command.GetType(),
            async () =>
            {
                var response = await context.RequestServices.GetRequiredService<ICommandSender>().Send(command, context.RequestAborted);
                if (location is not null)
                    context.Response.Headers.Location = location(response);
                return response;
            },
            statusCode);
    }

    private static async Task AuthoringNoContentResult<TCommand>(HttpContext context, TCommand? command)
        where TCommand : ICommand
    {
        if (command is null)
            return;

        try
        {
            await context.RequestServices.GetRequiredService<ICommandSender>().Send(command, context.RequestAborted);
            await Results.NoContent().ExecuteAsync(context);
        }
        catch (ActivityAuthoringException exception)
        {
            await WriteAuthoringProblemAsync(context, exception);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogUnexpected(context, exception, typeof(TCommand));
            await WriteAuthoringProblemAsync(context, ActivityProblemDetails.Unexpected(context));
        }
    }

    private static async Task AuthoringResult<TResponse>(
        HttpContext context,
        Type operationType,
        Func<Task<TResponse>> send,
        int statusCode = StatusCodes.Status200OK)
        where TResponse : notnull
    {
        try
        {
            await JsonResult(context, await send(), statusCode);
        }
        catch (ActivityAuthoringException exception)
        {
            await WriteAuthoringProblemAsync(context, exception);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogUnexpected(context, exception, operationType);
            await WriteAuthoringProblemAsync(context, ActivityProblemDetails.Unexpected(context));
        }
    }

    private static async Task LegacyRequestResult<TResponse>(HttpContext context, IRequest<TResponse> request)
        where TResponse : notnull
    {
        try
        {
            var response = await context.RequestServices.GetRequiredService<IRequestSender>().Send(request, context.RequestAborted);
            await JsonResult(context, response);
        }
        catch (EntityNotFoundException exception)
        {
            await WriteLegacyProblemAsync(context, exception.Message, StatusCodes.Status404NotFound);
        }
        catch (ArgumentException exception)
        {
            await WriteLegacyProblemAsync(context, exception.Message, StatusCodes.Status400BadRequest);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogUnexpected(context, exception, request.GetType());
            await WriteLegacyProblemAsync(context, "Unexpected error occurred.", StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task LegacyCommandResult<TResponse>(HttpContext context, ICommand<TResponse> command)
        where TResponse : notnull
    {
        try
        {
            var response = await context.RequestServices.GetRequiredService<ICommandSender>().Send(command, context.RequestAborted);
            await JsonResult(context, response);
        }
        catch (EntityNotFoundException exception)
        {
            await WriteLegacyProblemAsync(context, exception.Message, StatusCodes.Status404NotFound);
        }
        catch (ArgumentException exception)
        {
            await WriteLegacyProblemAsync(context, exception.Message, StatusCodes.Status400BadRequest);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogUnexpected(context, exception, command.GetType());
            await WriteLegacyProblemAsync(context, "Unexpected error occurred.", StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpContext context)
    {
        var contentType = context.Request.ContentType;
        if (string.IsNullOrWhiteSpace(contentType) ||
            !string.Equals(contentType.Split(';', 2)[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return default;
        }

        try
        {
            var serializerOptions = context.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
            var value = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, serializerOptions, context.RequestAborted);
            if (value is not null)
                return value;

            await WriteBindingProblemAsync(context, "A request body is required.");
        }
        catch (JsonException exception)
        {
            var message = exception.Message.Replace(" Path: $ |", string.Empty, StringComparison.Ordinal);
            await WriteBindingProblemAsync(context, message);
        }

        return default;
    }

    private static Task JsonResult<T>(HttpContext context, T value, int statusCode = StatusCodes.Status200OK, string contentType = JsonMediaType)
    {
        context.Response.StatusCode = statusCode;
        var options = context.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        var typeInfo = options.GetTypeInfo(typeof(T))
                       ?? throw new InvalidOperationException($"No source-generated JSON metadata exists for '{typeof(T).FullName}'.");
        return Results.Json(value, typeInfo, contentType).ExecuteAsync(context);
    }

    private static Task WriteAuthoringProblemAsync(HttpContext context, ActivityAuthoringException exception) =>
        WriteAuthoringProblemAsync(context, ActivityProblemDetails.From(exception, context));

    private static Task WriteAuthoringProblemAsync(HttpContext context, ActivityProblemDetailsView problem) =>
        JsonResult(context, problem, problem.Status, AuthoringProblemJsonContentType);

    private static Task WriteBindingProblemAsync(HttpContext context, string message) =>
        WriteLegacyProblemAsync(context, message, StatusCodes.Status400BadRequest, "serializerErrors");

    private static Task WriteQueryBindingProblemAsync(HttpContext context, QueryBindingError error)
    {
        var detail = $"Value [{error.RawValue}] is not valid for a [{error.TypeName}] property!";
        return WriteLegacyProblemAsync(context, detail, StatusCodes.Status400BadRequest, error.Name);
    }

    private static Task WriteLegacyProblemAsync(HttpContext context, string detail, int statusCode, string? errorName = null)
    {
        var problem = new ProblemDetails
        {
            Type = LegacyProblemType(statusCode),
            Title = LegacyProblemTitle(statusCode),
            Status = statusCode,
            Detail = detail,
            Instance = context.Request.Path
        };
        problem.Extensions["traceId"] = context.TraceIdentifier;
        problem.Extensions["errors"] = new[]
        {
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = errorName ?? "generalErrors",
                ["reason"] = detail
            }
        };
        return JsonResult(context, problem, statusCode, LegacyProblemJsonContentType);
    }

    private static string LegacyProblemType(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "https://www.rfc-editor.org/rfc/rfc7231#section-6.5.1",
        StatusCodes.Status404NotFound => "https://www.rfc-editor.org/rfc/rfc7231#section-6.5.4",
        StatusCodes.Status500InternalServerError => "https://www.rfc-editor.org/rfc/rfc7231#section-6.6.1",
        _ => "about:blank"
    };

    private static string LegacyProblemTitle(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "Bad Request",
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status500InternalServerError => "Internal Server Error",
        _ => "HTTP error"
    };

    private static void LogUnexpected(HttpContext context, Exception exception, Type operationType) =>
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(OwnerId)
            .LogError(exception, "Unexpected activity design operation failure for {OperationType}", operationType);

    private static string? Route(HttpContext context, string name) =>
        context.Request.RouteValues.TryGetValue(name, out var value) ? value?.ToString() : null;

    private static string? Query(HttpContext context, string name) =>
        context.Request.Query.TryGetValue(name, out var value) ? value.ToString() : null;

    private static QueryBinding<int> QueryInt(HttpContext context, string name, int fallback)
    {
        var raw = Query(context, name);
        if (raw is null)
            return new(fallback);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? new(value)
            : new(default, new(name, raw, nameof(Int32)));
    }

    private static QueryBinding<int?> QueryNullableInt(HttpContext context, string name)
    {
        var raw = Query(context, name);
        if (raw is null)
            return new(null);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? new(value)
            : new(default, new(name, raw, nameof(Int32)));
    }

    private static QueryBinding<bool?> QueryBool(HttpContext context, string name)
    {
        var raw = Query(context, name);
        if (raw is null)
            return new(null);
        return bool.TryParse(raw, out var value)
            ? new(value)
            : new(default, new(name, raw, nameof(Boolean)));
    }

    private static QueryBinding<T> QueryEnum<T>(HttpContext context, string name, T fallback) where T : struct, Enum
    {
        var raw = Query(context, name);
        if (raw is null)
            return new(fallback);
        return Enum.TryParse<T>(raw, true, out var value)
            ? new(value)
            : new(default, new(name, raw, typeof(T).Name));
    }

    private readonly record struct QueryBinding<T>(T Value, QueryBindingError? Error = null)
    {
        public bool IsValid => Error is null;
    }

    private sealed record QueryBindingError(string Name, string RawValue, string TypeName);
}
