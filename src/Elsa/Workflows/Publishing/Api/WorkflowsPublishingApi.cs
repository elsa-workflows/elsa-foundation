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
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
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

    /// <summary>Longest a single interpolated executable-export filename segment may be before it is truncated.</summary>
    private const int MaximumFileNameSegmentLength = 96;

    /// <summary>Maps all 23 Publishing operations.</summary>
    public static RouteGroupBuilder MapWorkflowsPublishingApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup(string.Empty);
        var description = typeof(RequestDelegate).GetMethod(nameof(RequestDelegate.Invoke))
            ?? throw new InvalidOperationException("RequestDelegate.Invoke metadata is unavailable.");

        Map(group.MapGet("/publishing/activities", (RequestDelegate)HandleListActivitiesAsync), "List", WorkflowPublishingPermissions.Read,
            typeof(IEnumerable<ConstructableActivityView>), description, typeof(ListConstructableActivities), [AnyMediaType, JsonMediaType]);
        Map(group.MapGet("/publishing/activities/{activityId}/construct", (RequestDelegate)HandleConstructActivityAsync), "Construct", WorkflowPublishingPermissions.Read,
            typeof(ConstructedActivityView), description, typeof(ConstructActivity), [AnyMediaType, JsonMediaType]);
        Map(group.MapGet("/publishing/incident-strategies", (RequestDelegate)HandleListIncidentStrategiesAsync), "ListIncidentStrategies", WorkflowPublishingPermissions.Read,
            typeof(IncidentStrategiesResponse), description);
        Map(group.MapGet("/publishing/value-conversion/profiles", (RequestDelegate)HandleListValueConversionProfilesAsync), "ListValueConversionProfiles", WorkflowPublishingPermissions.Read,
            typeof(ValueConversionProfilesResponse), description);

        Map(group.MapPost(RouteConstants.WorkflowPreflight, (RequestDelegate)HandleWorkflowPreflightAsync), "PreflightWorkflowPublicationEndpoint", WorkflowPublishingPermissions.Read,
            typeof(PublicationPreflightView), description, typeof(PreflightWorkflowPublication));
        Map(group.MapPost(RouteConstants.WorkflowSnapshotPreflight, (RequestDelegate)HandleWorkflowSnapshotPreflightAsync), "PreflightWorkflowPublicationSnapshotEndpoint", WorkflowPublishingPermissions.Read,
            typeof(PublicationSnapshotPreflightView), description, typeof(PreflightWorkflowPublicationSnapshot));
        // T117: slot READS are runtime-owned and live at runtime/workflows/activation-slots/... Publishing keeps
        // only the lifecycle commands below, whose response still joins the publication journal -- that join stays a
        // publishing concern because only publishing holds the journal.
        Map(group.MapDelete(RouteConstants.WorkflowSlot, (RequestDelegate)HandleUnpublishSlotAsync), "UnpublishPublicationSlotEndpoint", WorkflowPublishingPermissions.Manage,
            typeof(PublicationSlotView), description, typeof(UnpublishPublicationSlotRequest), [AnyMediaType, JsonMediaType]);
        Map(group.MapPost(RouteConstants.WorkflowSlotRestore, (RequestDelegate)HandleRestoreSlotAsync), "RestorePublicationSlotEndpoint", WorkflowPublishingPermissions.Manage,
            typeof(PublicationSlotView), description, typeof(RestorePublicationSlotRequest));
        Map(group.MapGet(RouteConstants.WorkflowPolicy, (RequestDelegate)HandleGetPolicyAsync), "GetWorkflowPublicationPolicyEndpoint", WorkflowPublishingPermissions.Read,
            typeof(PublicationPolicyView), description, typeof(GetWorkflowPublicationPolicy), [AnyMediaType, JsonMediaType]);
        Map(group.MapPut(RouteConstants.WorkflowPolicy, (RequestDelegate)HandleSetPolicyAsync), "SetWorkflowPublicationPolicyEndpoint", WorkflowPublishingPermissions.Manage,
            typeof(PublicationPolicyView), description, typeof(SetWorkflowPublicationPolicy));
        Map(group.MapPost(RouteConstants.WorkflowPublish, (RequestDelegate)HandlePublishWorkflowAsync), "PublishWorkflowEndpoint", WorkflowPublishingPermissions.Manage,
            typeof(PublishedWorkflowView), description, typeof(PublishWorkflowRequest));
        Map(group.MapGet(RouteConstants.WorkflowExecutableExport, (RequestDelegate)HandleExportExecutableClosureAsync), "ExportWorkflowExecutableClosureEndpoint", WorkflowPublishingPermissions.Read,
            typeof(WorkflowArtifactClosure), description);
        Map(group.MapPost(RouteConstants.VersionedWorkflowTestRuns, (RequestDelegate)HandleStartWorkflowTestRunAsync), "TestRunsStart", WorkflowPublishingPermissions.Manage,
            typeof(WorkflowTestRunView), description, typeof(StartWorkflowTestRun));
        Map(group.MapPost(RouteConstants.WorkflowDraftTestRuns, (RequestDelegate)HandleStartWorkflowDraftTestRunAsync), "TestRunsStartDraft", WorkflowPublishingPermissions.Manage,
            typeof(WorkflowTestRunView), description, typeof(StartWorkflowDraftTestRun));
        Map(group.MapPost(RouteConstants.GetRoute("preflight"), (RequestDelegate)HandleRuntimePreflightAsync), "RuntimeRequirementPreflightEndpoint", WorkflowPublishingPermissions.Read,
            typeof(RuntimeRequirementPreflightView), description, typeof(RunRuntimeRequirementPreflight));

        Map(group.MapPost("/design/activities/drafts/{draftId}/publication-preflight", (RequestDelegate)HandleActivityPreflightAsync), "PreflightActivityDraftPublicationEndpoint", WorkflowPublishingPermissions.Read,
            typeof(ActivityPublicationPreflightView), description, typeof(PreflightActivityDraftPublication));
        Map(group.MapPost("/design/activities/drafts/{draftId}/publish", (RequestDelegate)HandlePublishActivityAsync), "PublishActivityDraftEndpoint", WorkflowPublishingPermissions.Manage,
            typeof(ActivityPublicationReceiptView), description, typeof(PublishActivityDraft));
        Map(group.MapGet("/design/activities/publications/{idempotencyKey}", (RequestDelegate)HandleGetActivityReceiptAsync), "GetActivityPublicationReceiptEndpoint", WorkflowPublishingPermissions.Read,
            typeof(ActivityPublicationReceiptView), description);
        Map(group.MapPost("/publishing/activity-drafts/{draftId}/test-runs", (RequestDelegate)HandleStartActivityTestRunAsync), "ActivityDraftTestRunEndpoint", WorkflowPublishingPermissions.Manage,
            typeof(ActivityDraftTestRunView), description, typeof(StartActivityDraftTestRun));
        Map(group.MapGet("/publishing/activity-test-runs/{testRunId}", (RequestDelegate)HandleGetActivityTestRunAsync), "GetActivityDraftTestRunEndpoint", WorkflowPublishingPermissions.Manage,
            typeof(ActivityDraftTestRunView), description);
        Map(group.MapGet("/publishing/activity-drafts/{draftId}/test-runs/idempotency/{idempotencyKey}", (RequestDelegate)HandleGetActivityTestRunByIdempotencyAsync), "GetActivityDraftTestRunByIdempotencyKeyEndpoint", WorkflowPublishingPermissions.Manage,
            typeof(ActivityDraftTestRunView), description);
        Map(group.MapPost("/publishing/activity-test-runs/{testRunId}/cancel", (RequestDelegate)HandleCancelActivityTestRunAsync), "CancelActivityDraftTestRunEndpoint", WorkflowPublishingPermissions.Manage,
            typeof(ActivityDraftTestRunView), description);

        return group;
    }

    private static void Map(
        IEndpointConventionBuilder builder,
        string operation,
        string permission,
        Type responseType,
        System.Reflection.MethodInfo description,
        Type? requestType = null,
        string[]? requestContentTypes = null)
    {
        var metadata = new List<object>
        {
            new ProducesResponseTypeMetadata(StatusCodes.Status200OK, responseType, [JsonMediaType]),
            new ProducesResponseTypeMetadata(StatusCodes.Status401Unauthorized, typeof(void), []),
            new ProducesResponseTypeMetadata(StatusCodes.Status403Forbidden, typeof(void), [])
        };
        if (requestType is not null)
            metadata.Add(new AcceptsMetadata(requestContentTypes ?? [JsonMediaType], requestType, false));

        builder.WithName($"ElsaWorkflowsPublishingApiEndpoints{operation}")
            .WithTags(OwnerId)
            .WithOwner(OwnerId)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequirePermission(permission)
            .WithMetadata(metadata.ToArray())
            .WithMetadata(description)
            .RequireStableOpenApi();
    }

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



    private static Task HandleUnpublishSlotAsync(HttpContext context) =>
        HandleSlotLifecycleAsync(context, new UnpublishPublicationSlot(
            Route(context, "definitionId") ?? string.Empty, Route(context, "slotName") ?? string.Empty));

    private static Task HandleRestoreSlotAsync(HttpContext context) =>
        HandleSlotLifecycleAsync(context, new RestorePublicationSlot(
            Route(context, "definitionId") ?? string.Empty, Route(context, "slotName") ?? string.Empty));

    private static async Task HandleSlotLifecycleAsync<TRequest>(HttpContext context, TRequest request)
        where TRequest : IRequest<WorkflowActivationSlot>
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

    /// <summary>
    /// <c>GET publishing/workflows/{versionId}/executable-export</c> — the portable closure for one Published
    /// workflow definition version, returned inline as a download (FR-B-010a).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The GET route binds to the <c>download</c> target only.</b> GET is a safe method; receipt-producing
    /// targets (a folder writer, a blob push) are external side effects that crawlers, retries and caches may
    /// repeat. There is no target selector in v1 — a side-effecting target arrives with its own POST command
    /// surface carrying an explicit idempotency contract.
    /// </para>
    /// <para>
    /// <b>Producing the closure and deciding where it goes stay separate.</b> The factory is destination-agnostic
    /// and the target owns encoding, so this handler is only the HTTP binding: it selects the safe target, maps the
    /// factory's exception taxonomy onto status codes, and names the file.
    /// </para>
    /// </remarks>
    private static async Task HandleExportExecutableClosureAsync(HttpContext context)
    {
        var versionId = Route(context, "versionId");

        // The route constraint is `.+`, so a blank-but-present segment ("%20") reaches here. It names no version,
        // which is the 404 case rather than a raw ArgumentException out of the factory.
        if (string.IsNullOrWhiteSpace(versionId))
        {
            await WriteLegacyProblemAsync(context, "Cannot export a workflow definition version: no version was named.",
                StatusCodes.Status404NotFound);
            return;
        }

        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(OwnerId);
        var target = context.RequestServices.GetServices<IWorkflowArtifactExportTarget>().FirstOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.TargetId, DownloadWorkflowArtifactExportTarget.Id));
        if (target is null)
        {
            logger.LogError(
                "No export target named '{TargetId}' is registered; the publishing API feature must contribute it.",
                DownloadWorkflowArtifactExportTarget.Id);
            await WriteLegacyProblemAsync(context, "Workflow artifact export is not available on this engine.",
                StatusCodes.Status500InternalServerError);
            return;
        }

        WorkflowArtifactClosure closure;
        WorkflowArtifactExportDelivery delivery;
        try
        {
            var closureFactory = context.RequestServices.GetRequiredService<IWorkflowArtifactClosureFactory>();
            closure = await closureFactory.CreateAsync(versionId, context.RequestAborted);
            delivery = await DeliverAsync(target, closure, versionId, context.RequestAborted);
        }
        catch (WorkflowArtifactClosureSourceNotFoundException exception)
        {
            await WriteLegacyProblemAsync(context, exception.Message, StatusCodes.Status404NotFound);
            return;
        }
        catch (WorkflowArtifactClosureNotPublishedException exception)
        {
            // Distinct from "unknown": the version exists, it was simply never published, and FR-B-011 forbids
            // exporting the expiring TestRun snapshot it does have.
            await WriteLegacyProblemAsync(context, exception.Message, StatusCodes.Status409Conflict);
            return;
        }
        catch (IncompleteWorkflowArtifactClosureException exception)
        {
            // Every unresolved id is named as its own error entry, so a client renders "these are missing" without
            // parsing the summary sentence.
            await WriteLegacyProblemAsync(context, exception.Message, StatusCodes.Status409Conflict, additionalReasons:
                exception.MissingArtifactIds
                    .Select(artifactId => $"Dependency artifact '{artifactId}' is missing from the executable store.")
                    .ToArray());
            return;
        }
        catch (WorkflowArtifactClosureCycleException exception)
        {
            // Store corruption, not a client mistake: no content-addressed compiler can form a back edge.
            logger.LogError(exception, "Corrupt executable dependency graph while exporting '{VersionId}'.", versionId);
            await WriteLegacyProblemAsync(context, exception.Message, StatusCodes.Status500InternalServerError);
            return;
        }
        catch (WorkflowArtifactClosureException exception)
        {
            // The storage member of the family, plus any future one: an answer about the engine, never the caller.
            // §2.23.5 already wrapped the provider's own exception, so the inner detail stays in the log.
            logger.LogError(exception, "Workflow artifact export failed for '{VersionId}'.", versionId);
            await WriteLegacyProblemAsync(context, exception.Message, StatusCodes.Status500InternalServerError);
            return;
        }

        if (delivery.Kind != WorkflowArtifactExportDeliveryKind.InlinePayload || delivery.Payload is not { } payload)
        {
            // A receipt here would mean a side-effecting target answered a safe method, which is the one thing
            // this route exists to prevent.
            logger.LogError(
                "Export target '{TargetId}' returned a '{Kind}' delivery; this route serves inline payloads only.",
                delivery.TargetId,
                delivery.Kind);
            await WriteLegacyProblemAsync(context, "Workflow artifact export produced an unexpected delivery.",
                StatusCodes.Status500InternalServerError);
            return;
        }

        // The payload is already the encoded envelope, so it is written through rather than re-serialized. The
        // safe-name rules below reduce both interpolated segments to [A-Za-z0-9._-], which is why quoting the
        // header value here cannot be escaped out of.
        context.Response.Headers.ContentDisposition = $"attachment; filename=\"{CreateFileName(closure)}\"";
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = JsonMediaType;
        context.Response.ContentLength = payload.Length;
        await context.Response.Body.WriteAsync(payload, context.RequestAborted);
    }

    /// <summary>
    /// Runs the delivery, wrapping the codec's <c>JsonException</c> per §2.23.5 — this boundary owns the version id
    /// the codec deliberately does not, which is exactly why the codec leaves the wrap to its caller.
    /// </summary>
    private static async Task<WorkflowArtifactExportDelivery> DeliverAsync(
        IWorkflowArtifactExportTarget target,
        WorkflowArtifactClosure closure,
        string versionId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await target.DeliverAsync(closure, cancellationToken);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or WorkflowArtifactClosureException))
        {
            throw new WorkflowArtifactClosureStorageException(
                versionId,
                $"encode the exported closure envelope through target '{target.TargetId}'",
                exception);
        }
    }

    /// <summary>
    /// The download name shared with elsa-foundation-studio#493: <c>{definitionId}-{artifactVersion}-closure.json</c>.
    /// </summary>
    /// <remarks>
    /// Both interpolated segments come from stored artifact identity and land in a response header this handler
    /// writes by hand, so both are reduced to a conservative filename alphabet first. A definition id carrying a
    /// quote, a path separator or a newline would otherwise be echoed straight onto the wire.
    /// </remarks>
    internal static string CreateFileName(WorkflowArtifactClosure closure)
    {
        ArgumentNullException.ThrowIfNull(closure);

        var root = closure.Artifacts.FirstOrDefault(artifact =>
            StringComparer.Ordinal.Equals(artifact.Identity.ArtifactId, closure.RootArtifactId));
        var definitionId = SafeNameSegment(root?.Identity.DefinitionId, "workflow");
        var artifactVersion = SafeNameSegment(root?.Identity.ArtifactVersion, "unversioned");
        return $"{definitionId}-{artifactVersion}-closure.json";
    }

    private static string SafeNameSegment(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            var safe = char.IsAsciiLetterOrDigit(character) || character is '.' or '_' ? character : '-';

            // Collapse runs of substituted characters so a segment of separators cannot pad the name out.
            if (safe == '-' && builder.Length > 0 && builder[^1] == '-')
                continue;

            builder.Append(safe);
        }

        // Leading/trailing dots would produce a hidden file or a relative-path lookalike; leading/trailing dashes
        // would double up against the literal separators in the template.
        var sanitized = builder.ToString().Trim('-', '.');
        if (sanitized.Length > MaximumFileNameSegmentLength)
            sanitized = sanitized[..MaximumFileNameSegmentLength].TrimEnd('-', '.');

        return sanitized.Length == 0 ? fallback : sanitized;
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

    /// <summary>
    /// Writes one legacy problem document. <paramref name="additionalReasons"/> precede the summary
    /// <paramref name="detail"/> in <c>errors[]</c>, which is how an operation that has several independent
    /// reasons — one per missing dependency artifact, say — names each of them without a client parsing the
    /// summary sentence.
    /// </summary>
    private static Task WriteLegacyProblemAsync(
        HttpContext context,
        string detail,
        int statusCode,
        string? errorName = null,
        IReadOnlyCollection<string>? additionalReasons = null)
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
        problem.Extensions["errors"] = (additionalReasons ?? [])
            .Append(detail)
            .Select(reason => new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = errorName ?? "generalErrors",
                ["reason"] = reason
            })
            .ToArray();
        return JsonResult(context, problem, statusCode, ProblemJsonMediaType);
    }

    private static async ValueTask<PublicationRecord?> ResolveVisiblePublicationAsync(
        WorkflowActivationSlot slot,
        IPublicationRecordStore publicationStore,
        CancellationToken cancellationToken)
    {
        // T117 renamed the field: the slot points at the live *activation*, and on a publishing-owned slot that
        // activation id is the publication id. Falling through to the slot's journal covers the imported case,
        // where the activation has no publication record at all.
        if (slot.ActiveActivationId is { } activeActivationId)
            return await publicationStore.FindAsync(activeActivationId, cancellationToken);
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
