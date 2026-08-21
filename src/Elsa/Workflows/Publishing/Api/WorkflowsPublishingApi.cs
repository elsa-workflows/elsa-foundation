using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Diagnostics;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Constants;
using Elsa.Workflows.Publishing.Api.Endpoints;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Exceptions;
using Elsa.Workflows.Publishing.Handlers;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using PublishWorkflowCommand = Elsa.Workflows.Publishing.Core.Requests.PublishWorkflow;

namespace Elsa.Workflows.Publishing.Api;

/// <summary>Maps the Publishing REST surface using ordinary ASP.NET Core Minimal APIs.</summary>
public static class WorkflowsPublishingApi
{
    private const string OwnerId = "Elsa.Workflows.Publishing.Api";
    private const string AnyMediaType = "*/*";
    private const string JsonMediaType = "application/json";
    private const string ProblemJsonMediaType = "application/problem+json";

    /// <summary>Maps all 23 Publishing operations.</summary>
    public static RouteGroupBuilder MapWorkflowsPublishingApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup(string.Empty);

        Map(group.MapGet("/publishing/activities", (RequestDelegate)HandleListActivitiesAsync), "List", WorkflowPublishingPermissions.Read,
            typeof(IEnumerable<ConstructableActivityView>), typeof(ListConstructableActivities), [AnyMediaType, JsonMediaType]);
        Map(group.MapGet("/publishing/activities/{activityId}/construct", (RequestDelegate)HandleConstructActivityAsync), "Construct", WorkflowPublishingPermissions.Read,
            typeof(ConstructedActivityView), typeof(ConstructActivity), [AnyMediaType, JsonMediaType]);
        Map(group.MapGet("/publishing/incident-strategies", (RequestDelegate)HandleListIncidentStrategiesAsync), "ListIncidentStrategies", WorkflowPublishingPermissions.Read,
            typeof(IncidentStrategiesResponse));
        Map(group.MapGet("/publishing/value-conversion/profiles", (RequestDelegate)HandleListValueConversionProfilesAsync), "ListValueConversionProfiles", WorkflowPublishingPermissions.Read,
            typeof(ValueConversionProfilesResponse));

        Map(group.MapPost(RouteConstants.WorkflowPreflight, (RequestDelegate)HandleWorkflowPreflightAsync), "PreflightWorkflowPublicationEndpoint", WorkflowPublishingPermissions.Read,
            typeof(PublicationPreflightView), typeof(PreflightWorkflowPublication));
        Map(group.MapPost(RouteConstants.WorkflowSnapshotPreflight, (RequestDelegate)HandleWorkflowSnapshotPreflightAsync), "PreflightWorkflowPublicationSnapshotEndpoint", WorkflowPublishingPermissions.Read,
            typeof(PublicationSnapshotPreflightView), typeof(PreflightWorkflowPublicationSnapshot));
        Map(group.MapGet(RouteConstants.WorkflowSlots, (RequestDelegate)HandleListSlotsAsync), "ListPublicationSlotsEndpoint", WorkflowPublishingPermissions.Read,
            typeof(PublicationSlotListResponse), typeof(ListPublicationSlots), [AnyMediaType, JsonMediaType]);
        Map(group.MapGet(RouteConstants.WorkflowSlot, (RequestDelegate)HandleGetSlotAsync), "GetPublicationSlotEndpoint", WorkflowPublishingPermissions.Read,
            typeof(PublicationSlotView), typeof(GetPublicationSlot), [AnyMediaType, JsonMediaType]);
        Map(group.MapDelete(RouteConstants.WorkflowSlot, (RequestDelegate)HandleUnpublishSlotAsync), "UnpublishPublicationSlotEndpoint", WorkflowPublishingPermissions.Manage,
            typeof(PublicationSlotView), typeof(UnpublishPublicationSlotRequest), [AnyMediaType, JsonMediaType]);
        Map(group.MapPost(RouteConstants.WorkflowSlotRestore, (RequestDelegate)HandleRestoreSlotAsync), "RestorePublicationSlotEndpoint", WorkflowPublishingPermissions.Manage,
            typeof(PublicationSlotView), typeof(RestorePublicationSlotRequest));
        Map(group.MapGet(RouteConstants.WorkflowPolicy, (RequestDelegate)HandleGetPolicyAsync), "GetWorkflowPublicationPolicyEndpoint", WorkflowPublishingPermissions.Read,
            typeof(PublicationPolicyView), typeof(GetWorkflowPublicationPolicy), [AnyMediaType, JsonMediaType]);
        Map(group.MapPut(RouteConstants.WorkflowPolicy, (RequestDelegate)HandleSetPolicyAsync), "SetWorkflowPublicationPolicyEndpoint", WorkflowPublishingPermissions.Manage,
            typeof(PublicationPolicyView), typeof(SetWorkflowPublicationPolicy));
        Map(group.MapPost(RouteConstants.WorkflowPublish, (RequestDelegate)HandlePublishWorkflowAsync), "PublishWorkflowEndpoint", WorkflowPublishingPermissions.Manage,
            typeof(PublishedWorkflowView), typeof(PublishWorkflowRequest));
        Map(group.MapPost(RouteConstants.VersionedWorkflowTestRuns, (RequestDelegate)HandleStartWorkflowTestRunAsync), "TestRunsStart", WorkflowPublishingPermissions.Manage,
            typeof(WorkflowTestRunView), typeof(StartWorkflowTestRun));
        Map(group.MapPost(RouteConstants.WorkflowDraftTestRuns, (RequestDelegate)HandleStartWorkflowDraftTestRunAsync), "TestRunsStartDraft", WorkflowPublishingPermissions.Manage,
            typeof(WorkflowTestRunView), typeof(StartWorkflowDraftTestRun));
        Map(group.MapPost(RouteConstants.GetRoute("preflight"), (RequestDelegate)HandleRuntimePreflightAsync), "RuntimeRequirementPreflightEndpoint", WorkflowPublishingPermissions.Read,
            typeof(RuntimeRequirementPreflightView), typeof(RunRuntimeRequirementPreflight));

        Map(group.MapPost("/design/activities/drafts/{draftId}/publication-preflight", (RequestDelegate)HandleActivityPreflightAsync), "PreflightActivityDraftPublicationEndpoint", WorkflowPublishingPermissions.Read,
            typeof(ActivityPublicationPreflightView), typeof(PreflightActivityDraftPublication));
        Map(group.MapPost("/design/activities/drafts/{draftId}/publish", (RequestDelegate)HandlePublishActivityAsync), "PublishActivityDraftEndpoint", WorkflowPublishingPermissions.Manage,
            typeof(ActivityPublicationReceiptView), typeof(PublishActivityDraft));
        Map(group.MapGet("/design/activities/publications/{idempotencyKey}", (RequestDelegate)HandleGetActivityReceiptAsync), "GetActivityPublicationReceiptEndpoint", WorkflowPublishingPermissions.Read,
            typeof(ActivityPublicationReceiptView));
        Map(group.MapPost("/publishing/activity-drafts/{draftId}/test-runs", (RequestDelegate)HandleStartActivityTestRunAsync), "ActivityDraftTestRunEndpoint", WorkflowPublishingPermissions.Manage,
            typeof(ActivityDraftTestRunView), typeof(StartActivityDraftTestRun));
        Map(group.MapGet("/publishing/activity-test-runs/{testRunId}", (RequestDelegate)HandleGetActivityTestRunAsync), "GetActivityDraftTestRunEndpoint", WorkflowPublishingPermissions.Manage,
            typeof(ActivityDraftTestRunView));
        Map(group.MapGet("/publishing/activity-drafts/{draftId}/test-runs/idempotency/{idempotencyKey}", (RequestDelegate)HandleGetActivityTestRunByIdempotencyAsync), "GetActivityDraftTestRunByIdempotencyKeyEndpoint", WorkflowPublishingPermissions.Manage,
            typeof(ActivityDraftTestRunView));
        Map(group.MapPost("/publishing/activity-test-runs/{testRunId}/cancel", (RequestDelegate)HandleCancelActivityTestRunAsync), "CancelActivityDraftTestRunEndpoint", WorkflowPublishingPermissions.Manage,
            typeof(ActivityDraftTestRunView));

        return group;
    }

    private static void Map(
        IEndpointConventionBuilder builder,
        string operation,
        string permission,
        Type responseType,
        Type? requestType = null,
        string[]? requestContentTypes = null) =>
        builder.WithModuleOperation(
                $"ElsaWorkflowsPublishingApiEndpoints{operation}",
                OwnerId,
                responseType,
                requestType,
                requestContentTypes ?? [JsonMediaType])
            .RequirePermission(permission);

    private static Task HandleListActivitiesAsync(HttpContext context) =>
        LegacyRequestResult(context, new ListConstructableActivities(Query(context, "consumerKey")));

    private static Task HandleConstructActivityAsync(HttpContext context) =>
        LegacyRequestResult(context, new ConstructActivity(Route(context, "activityId") ?? string.Empty));

    private static Task HandleListIncidentStrategiesAsync(HttpContext context) =>
        LegacyRequestResult(context, new Elsa.Workflows.Publishing.Api.Requests.ListIncidentStrategies());

    private static Task HandleListValueConversionProfilesAsync(HttpContext context) =>
        LegacyRequestResult(context, new Elsa.Workflows.Publishing.Api.Requests.ListValueConversionProfiles());

    private static async Task HandleWorkflowPreflightAsync(HttpContext context)
    {
        var binding = await ReadJsonAsync<PreflightWorkflowPublication>(context);
        if (!binding.Succeeded || binding.Value is null)
            return;
        var request = binding.Value with { VersionId = Route(context, "versionId") ?? binding.Value.VersionId };

        try
        {
            var timeProvider = context.RequestServices.GetRequiredService<TimeProvider>();
            var now = timeProvider.GetUtcNow();
            var compiler = context.RequestServices.GetRequiredService<IWorkflowExecutableCompiler>();
            var executable = await compiler.CompileAsync(
                new WorkflowExecutableCompileRequest(
                    request.VersionId,
                    WorkflowExecutableReferenceScope.Published,
                    now,
                    now,
                    ExpiresAt: null,
                    "artifact-",
                    new Dictionary<string, string> { ["slice"] = "workflow-execution-vertical-slice" }),
                context.RequestAborted);
            var plan = await context.RequestServices.GetRequiredService<WorkflowPublicationPreflightReader>().EvaluateAsync(
                executable,
                RequestIntent(request.Action, request.SlotName),
                request.ExpectedPublicationId,
                $"preflight:{request.VersionId}",
                context.RequestAborted);
            var resolved = plan.ResolvedAction;
            await JsonResult(context, new PublicationPreflightView(
                resolved.WorkflowDefinitionId,
                resolved.WorkflowDefinitionVersionId,
                resolved.SlotName,
                PublicationContract.ToView(resolved.Action),
                PublicationContract.ToView(resolved.PolicySource),
                resolved.PolicyRevision,
                plan.Result.CanActivate,
                plan.Result.Changes.Select(PublicationTriggerChangeView.From).ToArray(),
                plan.Result.Conflicts.Select(PublicationTriggerConflictView.From).ToArray()));
        }
        catch (PublicationPolicyResolutionException exception)
        {
            await WriteLegacyProblemAsync(context, exception.Message,
                exception.Code == "expected_publication_mismatch" ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest);
        }
        catch (Exception exception) when (ValueConversionPublicationProblems.TryFind(exception, out var conversion))
        {
            await ValueConversionPublicationProblems.WriteAsync(context.Response,
                ValueConversionPublicationProblems.Create(conversion, context, request.VersionId), context.RequestAborted);
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
            LogUnexpected(context, exception, typeof(PreflightWorkflowPublication));
            await WriteLegacyProblemAsync(context, "Unexpected error occurred", StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task HandleWorkflowSnapshotPreflightAsync(HttpContext context)
    {
        var binding = await ReadJsonAsync<PreflightWorkflowPublicationSnapshot>(context);
        if (!binding.Succeeded || binding.Value is null)
            return;
        var request = binding.Value;

        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(request.DefinitionId);
            ArgumentNullException.ThrowIfNull(request.State);
            ArgumentNullException.ThrowIfNull(request.Layout);
            var reviews = context.RequestServices.GetRequiredService<PublicationSnapshotReviewService>();
            var candidateHash = reviews.ComputeCandidateHash(request.State, request.Layout);
            var snapshotId = $"snapshot:{candidateHash}";
            var now = context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
            var executable = await context.RequestServices.GetRequiredService<IWorkflowExecutableCompiler>().CompileAsync(
                new WorkflowExecutableCompileRequest(snapshotId, WorkflowExecutableReferenceScope.Published, now, null, null,
                    "artifact-", new Dictionary<string, string> { ["slice"] = "workflow-publication-snapshot-preflight" })
                {
                    Source = new WorkflowExecutableCompileSource(request.DefinitionId, snapshotId, "snapshot", request.State,
                        "WorkflowDefinitionSnapshot", snapshotId, SourceVersion: null)
                }, context.RequestAborted);
            var plan = await context.RequestServices.GetRequiredService<WorkflowPublicationPreflightReader>().EvaluateAsync(
                executable, RequestIntent(request.Action, request.SlotName), request.ExpectedPublicationId,
                $"preflight:{candidateHash}", context.RequestAborted);
            var issued = await reviews.IssueAsync(candidateHash, plan,
                request.Action is { } action ? PublicationIntentContract.ToModel(action) : null,
                request.SlotName, request.ExpectedPublicationId, PublicationRequestTenant.Resolve(context.User), context.RequestAborted);
            var resolved = plan.ResolvedAction;
            await JsonResult(context, new PublicationSnapshotPreflightView(
                issued.PreflightToken, issued.CandidateHash, resolved.WorkflowDefinitionId, VersionId: null,
                resolved.SlotName, PublicationContract.ToView(resolved.Action), PublicationContract.ToView(resolved.PolicySource),
                resolved.PolicyRevision, plan.Result.CanActivate,
                plan.CandidateClaims.Select(PublicationTriggerClaimView.From).ToArray(),
                plan.Result.Changes.Select(PublicationTriggerChangeView.From).ToArray(),
                plan.Result.Conflicts.Select(PublicationTriggerConflictView.From).ToArray()));
        }
        catch (PublicationPolicyResolutionException exception)
        {
            await WriteLegacyProblemAsync(context, exception.Message,
                exception.Code == "expected_publication_mismatch" ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest);
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
            LogUnexpected(context, exception, typeof(PreflightWorkflowPublicationSnapshot));
            await WriteLegacyProblemAsync(context, "Unexpected error occurred", StatusCodes.Status500InternalServerError);
        }
    }

    private static Task HandleListSlotsAsync(HttpContext context) =>
        ExecuteWithLegacyProblemAsync(context, typeof(ListPublicationSlots), async () =>
        {
            var slotStore = context.RequestServices.GetRequiredService<IPublicationSlotStore>();
            var publicationStore = context.RequestServices.GetRequiredService<IPublicationRecordStore>();
            var slots = await slotStore.ListByDefinitionAsync(Route(context, "definitionId") ?? string.Empty, context.RequestAborted);
            var views = new List<PublicationSlotView>(slots.Count);
            foreach (var slot in slots)
                views.Add(PublicationSlotView.From(slot, await ResolveVisiblePublicationAsync(slot, publicationStore, context.RequestAborted)));
            await JsonResult(context, new PublicationSlotListResponse(views));
        });

    private static Task HandleGetSlotAsync(HttpContext context) =>
        ExecuteWithLegacyProblemAsync(context, typeof(GetPublicationSlot), async () =>
        {
            var definitionId = Route(context, "definitionId") ?? string.Empty;
            var slotName = Route(context, "slotName") ?? string.Empty;
            var slotStore = context.RequestServices.GetRequiredService<IPublicationSlotStore>();
            var slot = await slotStore.FindAsync(definitionId, slotName, context.RequestAborted);
            if (slot is null)
            {
                await WriteLegacyProblemAsync(context, $"Publication slot '{slotName}' was not found for workflow '{definitionId}'.", StatusCodes.Status404NotFound);
                return;
            }

            var publicationStore = context.RequestServices.GetRequiredService<IPublicationRecordStore>();
            await JsonResult(context, PublicationSlotView.From(slot,
                await ResolveVisiblePublicationAsync(slot, publicationStore, context.RequestAborted)));
        });

    private static Task HandleUnpublishSlotAsync(HttpContext context) =>
        HandleSlotLifecycleAsync(context, new UnpublishPublicationSlot(
            Route(context, "definitionId") ?? string.Empty, Route(context, "slotName") ?? string.Empty));

    private static Task HandleRestoreSlotAsync(HttpContext context) =>
        HandleSlotLifecycleAsync(context, new RestorePublicationSlot(
            Route(context, "definitionId") ?? string.Empty, Route(context, "slotName") ?? string.Empty));

    private static async Task HandleSlotLifecycleAsync<TRequest>(HttpContext context, TRequest request)
        where TRequest : IRequest<PublicationSlot>
    {
        try
        {
            var slot = await context.RequestServices.GetRequiredService<IRequestSender>().Send(request, context.RequestAborted);
            var publicationStore = context.RequestServices.GetRequiredService<IPublicationRecordStore>();
            await JsonResult(context, PublicationSlotView.From(slot,
                await ResolveVisiblePublicationAsync(slot, publicationStore, context.RequestAborted)));
        }
        catch (PublicationActivationException exception)
        {
            await WriteLegacyProblemAsync(context, exception.Message, StatusCodes.Status409Conflict);
        }
        catch (InvalidOperationException exception) when (IsMissingSlot(exception))
        {
            await WriteLegacyProblemAsync(context, exception.Message, StatusCodes.Status404NotFound);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogUnexpected(context, exception, typeof(TRequest));
            await WriteLegacyProblemAsync(context, "Unexpected error occurred", StatusCodes.Status500InternalServerError);
        }
    }

    private static Task HandleGetPolicyAsync(HttpContext context) =>
        ExecuteWithLegacyProblemAsync(context, typeof(GetWorkflowPublicationPolicy), async () =>
        {
            var definitionId = Route(context, "definitionId") ?? string.Empty;
            var store = context.RequestServices.GetRequiredService<IPublicationPolicyStore>();
            var policy = await store.FindAsync(definitionId, context.RequestAborted);
            if (policy is not null)
            {
                await JsonResult(context, PublicationPolicyView.From(definitionId, policy, PublicationPolicySource.Workflow));
                return;
            }

            var timeProvider = context.RequestServices.GetRequiredService<TimeProvider>();
            var hostPolicy = await store.FindAsync(null, context.RequestAborted)
                ?? new PublicationPolicy(null, PublicationPolicyDefaultAction.ReplaceDefaultSlot, "default", 0, timeProvider.GetUtcNow());
            await JsonResult(context, PublicationPolicyView.From(definitionId, hostPolicy, PublicationPolicySource.Host));
        });

    private static Task HandleSetPolicyAsync(HttpContext context) =>
        ExecuteWithLegacyProblemAsync(context, typeof(SetWorkflowPublicationPolicy), async () =>
        {
            var binding = await ReadJsonAsync<SetWorkflowPublicationPolicy>(context);
            if (!binding.Succeeded || binding.Value is null)
                return;
            var request = binding.Value with { DefinitionId = Route(context, "definitionId") ?? binding.Value.DefinitionId };
            var policy = new PublicationPolicy(request.DefinitionId, PublicationPolicyContract.ToModel(request.DefaultAction),
                request.DefaultSlotName, request.ExpectedRevision, context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow());
            var result = await context.RequestServices.GetRequiredService<IPublicationPolicyStore>()
                .TrySaveAsync(policy, request.ExpectedRevision, context.RequestAborted);
            if (!result.Succeeded)
            {
                await WriteLegacyProblemAsync(context, "The workflow publication policy revision changed.", StatusCodes.Status409Conflict);
                return;
            }

            await JsonResult(context, PublicationPolicyView.From(request.DefinitionId, result.Policy, PublicationPolicySource.Workflow));
        });

    private static async Task ExecuteWithLegacyProblemAsync(
        HttpContext context,
        Type operationType,
        Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogUnexpected(context, exception, operationType);
            await WriteLegacyProblemAsync(context, "Unexpected error occurred", StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task HandlePublishWorkflowAsync(HttpContext context)
    {
        var binding = await ReadJsonAsync<PublishWorkflowRequest>(context, allowNull: true);
        if (!binding.Succeeded)
            return;
        var versionId = Route(context, "versionId") ?? binding.Value?.VersionId ?? string.Empty;
        var request = (binding.Value ?? new PublishWorkflowRequest(versionId)) with { VersionId = versionId };

        try
        {
            var response = await context.RequestServices.GetRequiredService<IRequestSender>().Send(
                new PublishWorkflowCommand(request.VersionId,
                    request.Action is { } action ? PublicationIntentContract.ToModel(action) : null,
                    request.SlotName, request.ExpectedPublicationId, request.PreflightToken,
                    PublicationRequestTenant.Resolve(context.User)), context.RequestAborted);
            await JsonResult(context, response, response.WasCreated ? StatusCodes.Status201Created : StatusCodes.Status200OK);
        }
        catch (EntityNotFoundException exception)
        {
            await WriteLegacyProblemAsync(context, exception.Message, StatusCodes.Status404NotFound);
        }
        catch (PublicationPreflightConflictException exception)
        {
            await WriteLegacyProblemAsync(context, exception.Message, StatusCodes.Status409Conflict);
        }
        catch (PublicationSnapshotReviewException exception)
        {
            await WriteLegacyProblemAsync(context, exception.Message, StatusCodes.Status409Conflict);
        }
        catch (PublicationActivationException exception)
        {
            await WriteLegacyProblemAsync(context, exception.Message, StatusCodes.Status409Conflict);
        }
        catch (PublicationPolicyResolutionException exception)
        {
            await WriteLegacyProblemAsync(context, exception.Message,
                exception.Code == "expected_publication_mismatch" ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest);
        }
        catch (ExpressionPublicationValidationException exception)
        {
            await ExpressionPublicationValidationProblems.WriteAsync(context.Response,
                ExpressionPublicationValidationProblems.Create(exception, context), context.RequestAborted);
        }
        catch (Exception exception) when (ValueConversionPublicationProblems.TryFind(exception, out var conversion))
        {
            await ValueConversionPublicationProblems.WriteAsync(context.Response,
                ValueConversionPublicationProblems.Create(conversion, context, request.VersionId), context.RequestAborted);
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
            LogUnexpected(context, exception, typeof(PublishWorkflowRequest));
            await WriteLegacyProblemAsync(context, "Unexpected error occurred", StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task HandleStartWorkflowTestRunAsync(HttpContext context)
    {
        var binding = await ReadJsonAsync<StartWorkflowTestRun>(context);
        if (!binding.Succeeded || binding.Value is null)
            return;
        await LegacyRequestResult(context, binding.Value with
        {
            VersionId = Route(context, "versionId") ?? binding.Value.VersionId
        });
    }

    private static async Task HandleStartWorkflowDraftTestRunAsync(HttpContext context)
    {
        var binding = await ReadJsonAsync<StartWorkflowDraftTestRun>(context);
        if (binding.Succeeded && binding.Value is not null)
            await LegacyRequestResult(context, binding.Value);
    }

    private static async Task HandleRuntimePreflightAsync(HttpContext context)
    {
        var binding = await ReadJsonAsync<RunRuntimeRequirementPreflight>(context);
        if (!binding.Succeeded || binding.Value is null)
            return;
        try
        {
            await JsonResult(context, await context.RequestServices.GetRequiredService<IRequestSender>()
                .Send(binding.Value, context.RequestAborted));
        }
        catch (RuntimeRequirementPreflightRequestException exception)
        {
            await JsonResult(context, new RuntimePreflightProblemDetails(
                "https://elsa.dev/problems/activity-request-invalid", "Runtime requirement preflight request is invalid",
                StatusCodes.Status400BadRequest, exception.Message, context.Request.Path, ActivityErrorCodes.RequestInvalid,
                context.TraceIdentifier, []), StatusCodes.Status400BadRequest, ProblemJsonMediaType);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogUnexpected(context, exception, typeof(RunRuntimeRequirementPreflight));
            await JsonResult(context, new RuntimePreflightProblemDetails(
                "https://elsa.dev/problems/activity-operation-failed", "Activity operation failed",
                StatusCodes.Status500InternalServerError, "The Runtime requirement preflight failed.", context.Request.Path,
                ActivityErrorCodes.OperationFailed, context.TraceIdentifier, []),
                StatusCodes.Status500InternalServerError, ProblemJsonMediaType);
        }
    }

    private static async Task HandleActivityPreflightAsync(HttpContext context)
    {
        var binding = await ReadJsonAsync<PreflightActivityDraftPublication>(context);
        if (!binding.Succeeded || binding.Value is null)
            return;
        await ActivityPublicationResult(context,
            binding.Value with { DraftId = Route(context, "draftId") ?? binding.Value.DraftId },
            "Activity publication preflight was rejected");
    }

    private static async Task HandlePublishActivityAsync(HttpContext context)
    {
        var binding = await ReadJsonAsync<PublishActivityDraft>(context, allowNull: true);
        if (!binding.Succeeded)
            return;
        var boundRequest = binding.Value ?? new PublishActivityDraft(string.Empty, 0, null, null!, null!, null!);
        var request = boundRequest with { DraftId = Route(context, "draftId") ?? boundRequest.DraftId };
        await ActivityPublicationResult(context, request, "Activity publication was rejected", StatusCodes.Status201Created,
            static response => $"/design/activities/publications/{Uri.EscapeDataString(response.IdempotencyKey)}");
    }

    private static Task HandleGetActivityReceiptAsync(HttpContext context) =>
        ActivityPublicationResult(context,
            new GetActivityPublicationReceipt(Route(context, "idempotencyKey") ?? string.Empty),
            "Activity publication receipt lookup was rejected");

    private static async Task ActivityPublicationResult<TResponse>(
        HttpContext context,
        IRequest<TResponse> request,
        string title,
        int successStatus = StatusCodes.Status200OK,
        Func<TResponse, string?>? location = null)
        where TResponse : notnull
    {
        try
        {
            var response = await context.RequestServices.GetRequiredService<IRequestSender>().Send(request, context.RequestAborted);
            if (location is not null)
                context.Response.Headers.Location = location(response);
            await JsonResult(context, response, successStatus);
        }
        catch (ActivityPublicationRejectedException exception)
        {
            await ActivityPublishingProblems.WriteAsync(context.Response,
                ActivityPublishingProblems.Rejected(exception, context, title), context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogUnexpected(context, exception, request.GetType());
            await ActivityPublishingProblems.WriteAsync(context.Response,
                ActivityPublishingProblems.Unexpected(context), context.RequestAborted);
        }
    }

    private static async Task HandleStartActivityTestRunAsync(HttpContext context)
    {
        var binding = await ReadJsonAsync<StartActivityDraftTestRun>(context);
        if (!binding.Succeeded || binding.Value is null)
            return;
        await ActivityTestRunResult(context,
            binding.Value with { DraftId = Route(context, "draftId") ?? binding.Value.DraftId },
            StatusCodes.Status202Accepted);
    }

    private static Task HandleGetActivityTestRunAsync(HttpContext context) =>
        ActivityTestRunResult(context, new GetActivityDraftTestRun(Route(context, "testRunId") ?? string.Empty));

    private static Task HandleGetActivityTestRunByIdempotencyAsync(HttpContext context) =>
        ActivityTestRunResult(context, new GetActivityDraftTestRunByIdempotencyKey(
            Route(context, "draftId") ?? string.Empty, Route(context, "idempotencyKey") ?? string.Empty));

    private static Task HandleCancelActivityTestRunAsync(HttpContext context) =>
        ActivityTestRunResult(context, new CancelActivityDraftTestRun(Route(context, "testRunId") ?? string.Empty),
            StatusCodes.Status202Accepted);

    private static async Task ActivityTestRunResult(
        HttpContext context,
        IRequest<ActivityDraftTestRunView> request,
        int successStatus = StatusCodes.Status200OK)
    {
        try
        {
            var response = await context.RequestServices.GetRequiredService<IRequestSender>().Send(request, context.RequestAborted);
            await JsonResult(context, response, successStatus);
        }
        catch (ActivityPublicationRejectedException exception)
        {
            await ActivityPublishingProblems.WriteAsync(context.Response,
                ActivityPublishingProblems.Rejected(exception, context, "Activity draft Test Run rejected"), context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogUnexpected(context, exception, request.GetType());
            await ActivityPublishingProblems.WriteAsync(context.Response,
                ActivityPublishingProblems.Unexpected(context), context.RequestAborted);
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
            await WriteLegacyProblemAsync(context, "Unexpected error occurred", StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<JsonBinding<T>> ReadJsonAsync<T>(HttpContext context, bool allowNull = false)
    {
        var contentType = context.Request.ContentType;
        if (string.IsNullOrWhiteSpace(contentType) ||
            !string.Equals(contentType.Split(';', 2)[0].Trim(), JsonMediaType, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return new(false, default);
        }

        try
        {
            var options = JsonOptions(context);
            var value = (T?)await JsonSerializer.DeserializeAsync(context.Request.Body, typeof(T), options, context.RequestAborted);
            if (value is not null || allowNull)
                return new(true, value);
            await WriteBindingProblemAsync(context, "A request body is required.");
        }
        catch (JsonException exception)
        {
            await WriteBindingProblemAsync(context,
                exception.Message.Replace(" Path: $ |", string.Empty, StringComparison.Ordinal));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException)
        {
            LogUnexpected(context, exception, typeof(T));
            await WriteLegacyProblemAsync(context, "Unexpected error occurred", StatusCodes.Status500InternalServerError);
        }

        return new(false, default);
    }

    private static Task JsonResult<T>(
        HttpContext context,
        T value,
        int statusCode = StatusCodes.Status200OK,
        string contentType = JsonMediaType)
    {
        var typeInfo = JsonOptions(context).GetTypeInfo(typeof(T))
            ?? throw new InvalidOperationException($"No JSON metadata exists for '{typeof(T).FullName}'.");
        return Results.Json(value, typeInfo, statusCode: statusCode, contentType: contentType).ExecuteAsync(context);
    }

    private static Task WriteBindingProblemAsync(HttpContext context, string message) =>
        WriteLegacyProblemAsync(context, message, StatusCodes.Status400BadRequest, "serializerErrors");

    private static Task WriteLegacyProblemAsync(
        HttpContext context,
        string detail,
        int statusCode,
        string? errorName = null)
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
        return JsonResult(context, problem, statusCode, ProblemJsonMediaType);
    }

    private static async ValueTask<PublicationRecord?> ResolveVisiblePublicationAsync(
        PublicationSlot slot,
        IPublicationRecordStore publicationStore,
        CancellationToken cancellationToken)
    {
        if (slot.ActivePublicationId is { } activePublicationId)
            return await publicationStore.FindAsync(activePublicationId, cancellationToken);
        return (await publicationStore.ListBySlotAsync(slot.SlotId, cancellationToken))
            .OrderByDescending(publication => publication.CreatedAt)
            .ThenByDescending(publication => publication.PublicationId, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static PublicationRequestIntent? RequestIntent(PublicationActionView? action, string? slotName) =>
        action is { } requestedAction
            ? new PublicationRequestIntent(PublicationIntentContract.ToModel(requestedAction), slotName)
            : slotName is not null
                ? new PublicationRequestIntent(PublicationAction.Replace, slotName)
                : null;

    private static bool IsMissingSlot(InvalidOperationException exception) =>
        exception.Message.Contains("does not exist", StringComparison.Ordinal) ||
        exception.Message.Contains("no retired publication", StringComparison.Ordinal) ||
        exception.Message.Contains("unavailable", StringComparison.Ordinal) ||
        exception.Message.Contains("no source reference", StringComparison.Ordinal);

    private static JsonSerializerOptions JsonOptions(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()
            .Value.SerializerOptions;

    private static string? Route(HttpContext context, string name) =>
        context.Request.RouteValues.TryGetValue(name, out var value) ? value?.ToString() : null;

    private static string? Query(HttpContext context, string name) =>
        context.Request.Query.TryGetValue(name, out var value) ? value.ToString() : null;

    private static void LogUnexpected(HttpContext context, Exception exception, Type operationType) =>
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(OwnerId)
            .LogError(exception, "Unexpected Publishing operation failure for {OperationType}", operationType);

    private static string LegacyProblemType(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "https://www.rfc-editor.org/rfc/rfc7231#section-6.5.1",
        StatusCodes.Status404NotFound => "https://www.rfc-editor.org/rfc/rfc7231#section-6.5.4",
        StatusCodes.Status409Conflict => "https://www.rfc-editor.org/rfc/rfc7231#section-6.5.8",
        StatusCodes.Status500InternalServerError => "https://www.rfc-editor.org/rfc/rfc7231#section-6.5.1",
        _ => "about:blank"
    };

    private static string LegacyProblemTitle(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "Bad Request",
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status500InternalServerError => "One or more errors occurred.",
        _ => "HTTP error"
    };

    private readonly record struct JsonBinding<T>(bool Succeeded, T? Value);

}
