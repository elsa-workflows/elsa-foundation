using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Runtime.Api.Commands;
using Elsa.Workflows.Runtime.Api.Constants;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Handlers.Alterations;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Models.Alterations;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Api.Requests.Alterations;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Alterations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using PermissionNames = Elsa.Workflows.Runtime.Api.Authorization.WorkflowRuntimePermissions;

namespace Elsa.Workflows.Runtime.Api;

/// <summary>Maps the Runtime REST surface with ordinary ASP.NET Core Minimal API endpoints.</summary>
public static class WorkflowsRuntimeApi
{
    private const string OwnerId = "Elsa.Workflows.Runtime.Api";
    private const string Tag = "Elsa.Workflows.Runtime.Api";
    private const string Json = "application/json";
    private const string ProblemJson = "application/problem+json";

    public static void MapWorkflowsRuntimeApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var description = typeof(RequestDelegate).GetMethod(nameof(RequestDelegate.Invoke))
            ?? throw new InvalidOperationException("RequestDelegate.Invoke metadata is unavailable.");

        Map(endpoints.MapGet(RouteConstants.GetRoute("instances/{workflowExecutionId}"), (RequestDelegate)GetInstanceAsync), "GetInstance", PermissionNames.WorkflowRuntimeRead, typeof(WorkflowInstanceDetailsView), description);
        Map(endpoints.MapGet(RouteConstants.Instances, (RequestDelegate)ListInstancesAsync), "ListInstances", PermissionNames.WorkflowRuntimeRead, typeof(IReadOnlyCollection<WorkflowInstanceSummaryView>), description);
        Map(endpoints.MapGet(RouteConstants.InstancesPage, (RequestDelegate)ListInstancesPageAsync), "ListInstancesPage", PermissionNames.WorkflowRuntimeRead, typeof(WorkflowInstanceListView), description);
        Map(endpoints.MapGet(RouteConstants.Executables, (RequestDelegate)ListExecutablesAsync), "ListExecutables", PermissionNames.WorkflowRuntimeRead, typeof(WorkflowExecutablesListView), description);
        Map(endpoints.MapGet(RouteConstants.Executable, (RequestDelegate)GetExecutableAsync), "GetExecutable", PermissionNames.WorkflowRuntimeRead, typeof(WorkflowExecutableDetailsView), description);
        Map(endpoints.MapGet(RouteConstants.ExecutableInputSources, (RequestDelegate)GetExecutableInputSourcesAsync), "GetExecutableInputSources", PermissionNames.WorkflowPublishingRead, typeof(WorkflowExecutableInputSourcesView), description);
        Map(endpoints.MapGet(RouteConstants.ExecutableProvenance, (RequestDelegate)GetExecutableProvenanceAsync), "GetExecutableProvenance", PermissionNames.WorkflowRuntimeRead, typeof(ExecutableProvenanceView), description);
        Map(endpoints.MapPost(RouteConstants.GetRoute("executables/{artifactId}/execute"), (RequestDelegate)ExecuteAsync), "Execute", PermissionNames.WorkflowRuntimeExecute, typeof(WorkflowExecutionStartDispatchView), description, typeof(ExecuteWorkflow));
        Map(endpoints.MapPost(RouteConstants.GetRoute("stimuli"), (RequestDelegate)DispatchStimulusAsync), "DispatchStimulus", PermissionNames.WorkflowRuntimeExecute, typeof(DispatchStimulusResponse), description, typeof(DispatchStimulus));
        Map(endpoints.MapGet(RouteConstants.Dispatches, (RequestDelegate)ListDispatchesAsync), "ListDispatches", PermissionNames.WorkflowRuntimeRead, typeof(IReadOnlyCollection<WorkflowDispatchView>), description);
        Map(endpoints.MapGet(RouteConstants.Dispatch, (RequestDelegate)GetDispatchAsync), "GetDispatch", PermissionNames.WorkflowRuntimeRead, typeof(WorkflowDispatchView), description);
        Map(endpoints.MapPost(RouteConstants.DispatchRedrive, (RequestDelegate)RedriveDispatchAsync), "RedriveDispatch", PermissionNames.WorkflowRuntimeManage, typeof(WorkflowDispatchRedriveView), description, typeof(RedriveWorkflowDispatch));
        Map(endpoints.MapGet(RouteConstants.GetRoute("instances/{workflowExecutionId}/activity-executions/{activityExecutionId}"), (RequestDelegate)GetActivityExecutionAsync), "GetActivityExecution", PermissionNames.WorkflowRuntimeRead, typeof(ActivityExecutionInspectionView), description);
        Map(endpoints.MapGet(RouteConstants.GetRoute("instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/descendants"), (RequestDelegate)GetActivityDescendantsAsync), "GetActivityDescendants", PermissionNames.WorkflowRuntimeRead, typeof(ActivityExecutionHierarchyPageView), description);
        Map(endpoints.MapGet(RouteConstants.GetRoute("instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/layout"), (RequestDelegate)GetActivityLayoutAsync), "GetActivityLayout", PermissionNames.WorkflowRuntimeRead, typeof(ActivityExecutionLayoutView), description);
        Map(endpoints.MapGet(RouteConstants.GetRoute("instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/value-evidence/{evidenceId}/payload"), (RequestDelegate)GetActivityValuePayloadAsync), "GetActivityValuePayload", PermissionNames.WorkflowRuntimeRead, typeof(ActivityExecutionValuePayloadView), description);
        Map(endpoints.MapGet(RouteConstants.GetRoute("instances/{workflowExecutionId}/incidents"), (RequestDelegate)ListIncidentsAsync), "ListIncidents", PermissionNames.WorkflowRuntimeRead, typeof(ListIncidentsResponse), description);
        Map(endpoints.MapGet(RouteConstants.RuntimeDiagnosticsSettings, (RequestDelegate)GetDiagnosticsAsync), "GetDiagnostics", PermissionNames.WorkflowRuntimeRead, typeof(RuntimeDiagnosticsSettingsView), description);
        Map(endpoints.MapPut(RouteConstants.RuntimeDiagnosticsSettings, (RequestDelegate)SaveDiagnosticsAsync), "SaveDiagnostics", PermissionNames.WorkflowRuntimeManage, typeof(RuntimeDiagnosticsSettingsView), description, typeof(SaveRuntimeDiagnosticsSettings));
        Map(endpoints.MapPost(AlterationRouteConstants.Plans, (RequestDelegate)SubmitAlterationAsync), "SubmitAlteration", PermissionNames.WorkflowRuntimeManage, typeof(WorkflowAlterationPlanSubmissionView), description, typeof(SubmitWorkflowAlterationPlan));
        Map(endpoints.MapGet(AlterationRouteConstants.Plan, (RequestDelegate)GetAlterationAsync), "GetAlteration", PermissionNames.WorkflowRuntimeRead, typeof(WorkflowAlterationPlanView), description);
        Map(endpoints.MapGet(AlterationRouteConstants.JobsPage, (RequestDelegate)PageAlterationJobsAsync), "PageAlterationJobs", PermissionNames.WorkflowRuntimeRead, typeof(WorkflowAlterationJobPageView), description);
        Map(endpoints.MapGet(AlterationRouteConstants.Job, (RequestDelegate)GetAlterationJobAsync), "GetAlterationJob", PermissionNames.WorkflowRuntimeRead, typeof(WorkflowAlterationJobView), description);
        Map(endpoints.MapPost(AlterationRouteConstants.Cancel, (RequestDelegate)CancelAlterationAsync), "CancelAlteration", PermissionNames.WorkflowRuntimeManage, typeof(WorkflowAlterationPlanView), description);
    }

    private static void Map(
        IEndpointConventionBuilder builder,
        string operation,
        string permission,
        Type responseType,
        System.Reflection.MethodInfo description,
        Type? requestType = null)
    {
        var metadata = new List<object>
        {
            new ProducesResponseTypeMetadata(StatusCodes.Status200OK, responseType, [Json]),
            new ProducesResponseTypeMetadata(StatusCodes.Status401Unauthorized, typeof(void), []),
            new ProducesResponseTypeMetadata(StatusCodes.Status403Forbidden, typeof(void), [])
        };
        if (requestType is not null)
            metadata.Add(new AcceptsMetadata([Json], requestType, false));

        builder.WithName($"ElsaWorkflowsRuntimeApiEndpoints{operation}")
            .WithTags(Tag)
            .WithOwner(OwnerId)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequirePermission(permission)
            .WithMetadata(metadata.ToArray());

        // Keep the API Explorer description method as the final MethodInfo metadata.
        // EndpointMetadataCollection.GetMetadata<T>() selects the last matching item;
        // using a stable framework MethodInfo prevents API Explorer from rooting this
        // owner assembly through a handler MethodInfo.
        builder.WithMetadata(description);
        builder.Finally(static endpointBuilder =>
        {
            for (var index = endpointBuilder.Metadata.Count - 1; index >= 0; index--)
            {
                if (endpointBuilder.Metadata[index] is System.Runtime.CompilerServices.AsyncStateMachineAttribute
                    or System.Diagnostics.DebuggerStepThroughAttribute)
                {
                    endpointBuilder.Metadata.RemoveAt(index);
                }
            }
        });
        builder.RequireStableOpenApi();
    }

    private static async Task GetInstanceAsync(HttpContext context)
    {
        try
        {
            var result = await Sender(context).Send(new GetWorkflowInstance(Route(context, "workflowExecutionId") ?? string.Empty,
                QueryInt(context, "activityPageSize") ?? RuntimeStorePageRequest.DefaultLimit,
                Query(context, "activityContinuationToken")), context.RequestAborted);
            if (result.Instance is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            await JsonAsync(context, result.Instance, WorkflowsRuntimeJsonContext.Default.WorkflowInstanceDetailsView);
        }
        catch (ArgumentException exception) { await ProblemAsync(context, StatusCodes.Status400BadRequest, exception.Message); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { await UnexpectedAsync(context, exception, "reading workflow instance"); }
    }

    private static async Task ListInstancesAsync(HttpContext context) =>
        await ListInstancesCoreAsync(context, legacy: true);

    private static async Task ListInstancesPageAsync(HttpContext context) =>
        await ListInstancesCoreAsync(context, legacy: false);

    private static async Task ListInstancesCoreAsync(HttpContext context, bool legacy)
    {
        try
        {
            var request = new ListWorkflowInstances(Query(context, "status"), Query(context, "definitionId"), Query(context, "correlationId"), QueryInt(context, "take"), Query(context, "cursor"), Query(context, "workflowExecutionId"), Query(context, "artifactId"), QueryDate(context, "from"), QueryDate(context, "to"), Query(context, "runKind"));
            if (legacy)
            {
                var result = await Sender(context).Send(request.ForLegacyArray(), context.RequestAborted);
                await JsonAsync(context, result.Items, WorkflowsRuntimeJsonContext.Default.IReadOnlyCollectionWorkflowInstanceSummaryView);
            }
            else
            {
                var result = await Sender(context).Send(request, context.RequestAborted);
                await JsonAsync(context, result, WorkflowsRuntimeJsonContext.Default.WorkflowInstanceListView);
            }
        }
        catch (ArgumentException exception) { await ProblemAsync(context, StatusCodes.Status400BadRequest, exception.Message); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { await UnexpectedAsync(context, exception, "listing workflow instances"); }
    }

    private static async Task ListExecutablesAsync(HttpContext context)
    {
        try
        {
            var scope = Enum.TryParse<WorkflowExecutableListScope>(Query(context, "scope"), true, out var parsed) ? parsed : WorkflowExecutableListScope.Published;
            var result = await Sender(context).Send(new ListWorkflowExecutables(scope, QueryBool(context, "includeRetired") ?? false), context.RequestAborted);
            await JsonAsync(context, result, WorkflowsRuntimeJsonContext.Default.WorkflowExecutablesListView);
        }
        catch (ArgumentException exception) { await ProblemAsync(context, StatusCodes.Status400BadRequest, exception.Message); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { await UnexpectedAsync(context, exception, "listing workflow executables"); }
    }

    private static async Task GetExecutableAsync(HttpContext context) =>
        await ExecuteRequestAsync(context, new GetWorkflowExecutable(Route(context, "artifactId") ?? string.Empty, Query(context, "ref")), WorkflowsRuntimeJsonContext.Default.WorkflowExecutableDetailsView);

    private static async Task GetExecutableInputSourcesAsync(HttpContext context) =>
        await ExecuteRequestAsync(context, new GetWorkflowExecutableInputSources(Route(context, "artifactId") ?? string.Empty, Route(context, "sourceReferenceId") ?? string.Empty), WorkflowsRuntimeJsonContext.Default.WorkflowExecutableInputSourcesView);

    private static async Task GetExecutableProvenanceAsync(HttpContext context) =>
        await ExecuteRequestAsync(context, new GetWorkflowExecutableProvenance(Route(context, "artifactId") ?? string.Empty), WorkflowsRuntimeJsonContext.Default.ExecutableProvenanceView);

    private static async Task ExecuteAsync(HttpContext context)
    {
        var request = await ReadJsonAsync(context, WorkflowsRuntimeJsonContext.Default.ExecuteWorkflow);
        if (request is null)
            return;
        request = request with { ArtifactId = Route(context, "artifactId") ?? request.ArtifactId };
        try
        {
            var result = await Sender(context).Send(request, context.RequestAborted);
            if (result.Shed)
            {
                context.Response.Headers.RetryAfter = Math.Max(1, result.RetryAfterSeconds ?? 1).ToString(CultureInfo.InvariantCulture);
                await JsonAsync(context, result, WorkflowsRuntimeJsonContext.Default.WorkflowExecutionStartDispatchView, StatusCodes.Status429TooManyRequests);
                return;
            }
            var status = string.Equals(result.CommandDispatchStatus, nameof(WorkflowExecutionCommandDispatchStatus.Rejected), StringComparison.Ordinal) ? StatusCodes.Status409Conflict : StatusCodes.Status200OK;
            await JsonAsync(context, result, WorkflowsRuntimeJsonContext.Default.WorkflowExecutionStartDispatchView, status);
        }
        catch (WorkflowExecutableNotFoundException exception) { await ProblemAsync(context, StatusCodes.Status400BadRequest, exception.Message); }
        catch (WorkflowExecutableReferenceRejectedException exception) { await ProblemAsync(context, StatusCodes.Status409Conflict, exception.Message); }
        catch (ArgumentException) { await ProblemAsync(context, StatusCodes.Status400BadRequest, "Invalid execute request."); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { await UnexpectedAsync(context, exception, "executing workflow"); }
    }

    private static async Task DispatchStimulusAsync(HttpContext context)
    {
        var request = await ReadJsonAsync(context, WorkflowsRuntimeJsonContext.Default.DispatchStimulus);
        if (request is null)
            return;
        await ExecuteRequestAsync(context, request, WorkflowsRuntimeJsonContext.Default.DispatchStimulusResponse);
    }

    private static async Task ListDispatchesAsync(HttpContext context)
    {
        try
        {
            var request = new ListWorkflowDispatches(Query(context, "parentWorkflowExecutionId"), Query(context, "childWorkflowExecutionId"), Query(context, "status"), QueryInt(context, "take"))
            {
                AfterCreatedAt = QueryDate(context, "afterCreatedAt"),
                AfterDispatchId = Query(context, "afterDispatchId")
            };
            var result = await Sender(context).Send(request, context.RequestAborted);
            await JsonAsync(context, result, WorkflowsRuntimeJsonContext.Default.IReadOnlyCollectionWorkflowDispatchView);
        }
        catch (ArgumentException exception) { await ProblemAsync(context, StatusCodes.Status400BadRequest, exception.Message); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { await UnexpectedAsync(context, exception, "listing workflow dispatches"); }
    }

    private static async Task GetDispatchAsync(HttpContext context) =>
        await ExecuteWrappedRequestAsync(
            context,
            new GetWorkflowDispatch(Route(context, "dispatchId") ?? string.Empty),
            WorkflowsRuntimeJsonContext.Default.GetWorkflowDispatchResponse,
            response => response.Dispatch,
            WorkflowsRuntimeJsonContext.Default.WorkflowDispatchView);

    private static async Task RedriveDispatchAsync(HttpContext context)
    {
        var request = await ReadJsonAsync(context, WorkflowsRuntimeJsonContext.Default.RedriveWorkflowDispatch);
        if (request is null)
            return;
        request = request with { DispatchId = Route(context, "dispatchId") ?? request.DispatchId };
        await ExecuteRequestAsync(context, request, WorkflowsRuntimeJsonContext.Default.WorkflowDispatchRedriveView);
    }

    private static async Task GetActivityExecutionAsync(HttpContext context)
    {
        try
        {
            var response = await Sender(context).Send(
                new GetActivityExecution(Route(context, "workflowExecutionId") ?? string.Empty, Route(context, "activityExecutionId") ?? string.Empty),
                context.RequestAborted);
            if (response.ActivityExecution is null)
                await ActivityExecutionProblemDetails.NotFoundAsync(context, context.RequestAborted);
            else
                await JsonAsync(context, response.ActivityExecution, WorkflowsRuntimeJsonContext.Default.ActivityExecutionInspectionView);
        }
        catch (ArgumentException exception) { await ActivityExecutionProblemDetails.InvalidRequestAsync(context, exception.Message, context.RequestAborted); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(OwnerId).LogError(exception, "Unexpected error while reading activity execution.");
            await ActivityExecutionProblemDetails.UnexpectedAsync(context, context.RequestAborted);
        }
    }

    private static async Task GetActivityDescendantsAsync(HttpContext context)
    {
        try
        {
            var response = await Sender(context).Send(
                new GetActivityExecutionDescendants(Route(context, "workflowExecutionId") ?? string.Empty, Route(context, "activityExecutionId") ?? string.Empty, Query(context, "cursor"), QueryInt(context, "limit"), Query(context, "include")),
                context.RequestAborted);
            if (response.Page is null)
                await ActivityExecutionProblemDetails.NotFoundAsync(context, context.RequestAborted);
            else
                await JsonAsync(context, response.Page, WorkflowsRuntimeJsonContext.Default.ActivityExecutionHierarchyPageView);
        }
        catch (ActivityExecutionHierarchyCursorException exception) { await ActivityExecutionProblemDetails.CursorAsync(context, exception, context.RequestAborted); }
        catch (ArgumentException exception) { await ActivityExecutionProblemDetails.InvalidRequestAsync(context, exception.Message, context.RequestAborted); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(OwnerId).LogError(exception, "Unexpected error while reading activity execution descendants.");
            await ActivityExecutionProblemDetails.UnexpectedAsync(context, context.RequestAborted);
        }
    }

    private static async Task GetActivityLayoutAsync(HttpContext context)
    {
        try
        {
            var response = await Sender(context).Send(
                new GetActivityExecutionLayout(Route(context, "workflowExecutionId") ?? string.Empty, Route(context, "activityExecutionId") ?? string.Empty),
                context.RequestAborted);
            if (response.Layout is null)
                await ActivityExecutionProblemDetails.NotFoundAsync(context, context.RequestAborted);
            else
                await JsonAsync(context, response.Layout, WorkflowsRuntimeJsonContext.Default.ActivityExecutionLayoutView);
        }
        catch (ArgumentException exception) { await ActivityExecutionProblemDetails.InvalidRequestAsync(context, exception.Message, context.RequestAborted); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(OwnerId).LogError(exception, "Unexpected error while reading activity execution layout.");
            await ActivityExecutionProblemDetails.UnexpectedAsync(context, context.RequestAborted);
        }
    }

    private static async Task GetActivityValuePayloadAsync(HttpContext context)
    {
        try
        {
            var response = await Sender(context).Send(new GetActivityExecutionValuePayload(Route(context, "workflowExecutionId") ?? string.Empty, Route(context, "activityExecutionId") ?? string.Empty, Route(context, "evidenceId") ?? string.Empty), context.RequestAborted);
            var status = response.Result.Outcome switch
            {
                ActivityExecutionValuePayloadReadOutcome.Resolved => StatusCodes.Status200OK,
                ActivityExecutionValuePayloadReadOutcome.Denied => StatusCodes.Status403Forbidden,
                ActivityExecutionValuePayloadReadOutcome.Unavailable => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status404NotFound
            };
            if (response.Result.Value is not null)
                await JsonAsync(context, response.Result.Value, WorkflowsRuntimeJsonContext.Default.ActivityExecutionValuePayloadView, status);
            else
            {
                switch (response.Result.Outcome)
                {
                    case ActivityExecutionValuePayloadReadOutcome.Denied:
                        await ActivityExecutionProblemDetails.ForbiddenAsync(context, context.RequestAborted);
                        break;
                    case ActivityExecutionValuePayloadReadOutcome.Unavailable:
                        await ActivityExecutionProblemDetails.ValueUnavailableAsync(context, context.RequestAborted);
                        break;
                    default:
                        await ActivityExecutionProblemDetails.NotFoundAsync(context, context.RequestAborted);
                        break;
                }
            }
        }
        catch (ArgumentException exception) { await ActivityExecutionProblemDetails.InvalidRequestAsync(context, exception.Message, context.RequestAborted); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(OwnerId).LogError(exception, "Unexpected error while resolving activity execution value evidence.");
            await ActivityExecutionProblemDetails.UnexpectedAsync(context, context.RequestAborted);
        }
    }

    private static async Task ListIncidentsAsync(HttpContext context)
    {
        try
        {
            var result = await Sender(context).Send(new ListIncidents(Route(context, "workflowExecutionId") ?? string.Empty, QueryBool(context, "blockingOnly") ?? false), context.RequestAborted);
            if (!result.WorkflowExists)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            await JsonAsync(context, result, WorkflowsRuntimeJsonContext.Default.ListIncidentsResponse);
        }
        catch (ArgumentException exception) { await ProblemAsync(context, StatusCodes.Status400BadRequest, exception.Message); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { await UnexpectedAsync(context, exception, "listing workflow incidents"); }
    }

    private static async Task GetDiagnosticsAsync(HttpContext context) =>
        await ExecuteRequestAsync(context, new GetRuntimeDiagnosticsSettings(Query(context, "scope")), WorkflowsRuntimeJsonContext.Default.RuntimeDiagnosticsSettingsView);

    private static async Task SaveDiagnosticsAsync(HttpContext context)
    {
        var request = await ReadJsonAsync(context, WorkflowsRuntimeJsonContext.Default.SaveRuntimeDiagnosticsSettings);
        if (request is null)
            return;
        await ExecuteCommandAsync(context, request, WorkflowsRuntimeJsonContext.Default.RuntimeDiagnosticsSettingsView);
    }

    private static async Task SubmitAlterationAsync(HttpContext context)
    {
        var request = await ReadJsonAsync(context, WorkflowsRuntimeJsonContext.Default.SubmitWorkflowAlterationPlan);
        if (request is null)
            return;
        request = request with { IdempotencyKey = context.Request.Headers["Idempotency-Key"].ToString() };
        try
        {
            if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            { await AlterationProblemAsync(context, "MissingIdempotencyKey", "The Idempotency-Key header is required."); return; }
            if (request.IdempotencyKey.Length > SubmitWorkflowAlterationPlanRequestHandler.MaximumIdempotencyKeyLength)
            { await AlterationProblemAsync(context, "InvalidIdempotencyKey", "The Idempotency-Key header must not exceed 256 characters."); return; }
            var result = await Sender(context).Send(request, context.RequestAborted);
            context.Response.Headers.Location = result.Links.Self;
            await JsonAsync(context, result, WorkflowsRuntimeJsonContext.Default.WorkflowAlterationPlanSubmissionView, StatusCodes.Status202Accepted);
        }
        catch (WorkflowAlterationAdmissionRejectedException exception)
        {
            context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling((exception.RetryAfter ?? TimeSpan.FromSeconds(1)).TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            await AlterationProblemAsync(context, "AlterationAdmissionBackpressure", "Runtime alteration admission is temporarily at capacity.", StatusCodes.Status429TooManyRequests);
        }
        catch (WorkflowAlterationIdempotencyConflictException) { await AlterationProblemAsync(context, "AlterationIdempotencyConflict", "The idempotency key is already associated with a different alteration request.", StatusCodes.Status409Conflict); }
        catch (ArgumentOutOfRangeException) { await AlterationProblemAsync(context, "InvalidIdempotencyKey", "The alteration request is invalid.", StatusCodes.Status400BadRequest); }
        catch (InvalidOperationException) { await AlterationProblemAsync(context, "InvalidAlterationRequest", "The alteration request is invalid.", StatusCodes.Status422UnprocessableEntity); }
        catch (ArgumentException) { await AlterationProblemAsync(context, "InvalidAlterationRequest", "The alteration request is invalid.", StatusCodes.Status422UnprocessableEntity); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(OwnerId).LogError(exception, "Unexpected error while submitting runtime alteration plan.");
            await AlterationProblemAsync(context, "UnexpectedError", "Unexpected error occurred.", StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task GetAlterationAsync(HttpContext context) =>
        await ExecuteRequestAsync(context, new GetWorkflowAlterationPlan(Route(context, "planId") ?? string.Empty), WorkflowsRuntimeJsonContext.Default.WorkflowAlterationPlanView, "InvalidAlterationPlanId", "The alteration plan identifier is invalid.", true);

    private static async Task PageAlterationJobsAsync(HttpContext context) =>
        await ExecuteRequestAsync(context, new PageWorkflowAlterationJobs(Route(context, "planId") ?? string.Empty, QueryInt(context, "take"), Query(context, "cursor")), WorkflowsRuntimeJsonContext.Default.WorkflowAlterationJobPageView, "InvalidAlterationJobsPage", "The alteration jobs page request is invalid.", true);

    private static async Task GetAlterationJobAsync(HttpContext context) =>
        await ExecuteRequestAsync(context, new GetWorkflowAlterationJob(Route(context, "planId") ?? string.Empty, Route(context, "jobId") ?? string.Empty), WorkflowsRuntimeJsonContext.Default.WorkflowAlterationJobView, "InvalidAlterationJobId", "The alteration job identifier is invalid.", true);

    private static async Task CancelAlterationAsync(HttpContext context) =>
        await CancelAlterationCoreAsync(context);

    private static async Task CancelAlterationCoreAsync(HttpContext context)
    {
        try
        {
            var result = await Sender(context).Send(new CancelWorkflowAlterationPlan(Route(context, "planId") ?? string.Empty), context.RequestAborted);
            await JsonAsync(context, result.Plan, WorkflowsRuntimeJsonContext.Default.WorkflowAlterationPlanView,
                result.IsTerminalNoOp ? StatusCodes.Status200OK : StatusCodes.Status202Accepted);
        }
        catch (WorkflowAlterationResourceNotFoundException) { context.Response.StatusCode = StatusCodes.Status404NotFound; }
        catch (ArgumentException) { await AlterationProblemAsync(context, "InvalidAlterationPlanId", "The alteration plan identifier is invalid."); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(OwnerId).LogError(exception, "Unexpected error while cancelling runtime alteration plan.");
            await AlterationProblemAsync(context, "UnexpectedError", "Unexpected error occurred.", StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task ExecuteWrappedRequestAsync<TRequest, TWrapper, TBody>(
        HttpContext context,
        TRequest request,
        JsonTypeInfo<TWrapper> wrapperInfo,
        Func<TWrapper, TBody?> select,
        JsonTypeInfo<TBody> bodyInfo)
        where TRequest : IRequest<TWrapper>
        where TWrapper : notnull
        where TBody : class
    {
        try
        {
            var wrapper = await Sender(context).Send(request, context.RequestAborted);
            var body = select(wrapper);
            if (body is null)
            { context.Response.StatusCode = StatusCodes.Status404NotFound; return; }
            await JsonAsync(context, body, bodyInfo);
        }
        catch (ArgumentException exception) { await ProblemAsync(context, StatusCodes.Status400BadRequest, exception.Message); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { await UnexpectedAsync(context, exception, "reading runtime resource"); }
    }

    private static async Task ExecuteRequestAsync<TRequest, TResponse>(HttpContext context, TRequest request, JsonTypeInfo<TResponse> responseInfo, string? argumentErrorCode = null, string? argumentErrorMessage = null, bool alterationProblems = false)
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        try
        {
            var result = await Sender(context).Send(request, context.RequestAborted);
            await JsonAsync(context, result, responseInfo);
        }
        catch (WorkflowAlterationResourceNotFoundException) { context.Response.StatusCode = StatusCodes.Status404NotFound; }
        catch (EntityNotFoundException) { context.Response.StatusCode = StatusCodes.Status404NotFound; }
        catch (ArgumentOutOfRangeException exception)
        {
            if (argumentErrorCode is null)
                await ProblemAsync(context, StatusCodes.Status400BadRequest, exception.Message);
            else
                await AlterationProblemAsync(context, argumentErrorCode, argumentErrorMessage ?? exception.Message);
        }
        catch (ArgumentException exception)
        {
            if (argumentErrorCode is null)
                await ProblemAsync(context, StatusCodes.Status400BadRequest, exception.Message);
            else
                await AlterationProblemAsync(context, argumentErrorCode, argumentErrorMessage ?? exception.Message);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            if (alterationProblems)
            {
                context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(OwnerId).LogError(exception, "Unexpected error while handling runtime alteration request.");
                await AlterationProblemAsync(context, "UnexpectedError", "Unexpected error occurred.", StatusCodes.Status500InternalServerError);
            }
            else
                await UnexpectedAsync(context, exception, "handling runtime request");
        }
    }

    private static async Task ExecuteCommandAsync<TCommand, TResponse>(HttpContext context, TCommand command, JsonTypeInfo<TResponse> responseInfo)
        where TCommand : ICommand<TResponse>
        where TResponse : notnull
    {
        try
        {
            var result = await Commands(context).Send(command, context.RequestAborted);
            await JsonAsync(context, result, responseInfo);
        }
        catch (ArgumentException exception) { await ProblemAsync(context, StatusCodes.Status400BadRequest, exception.Message); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { await UnexpectedAsync(context, exception, "handling runtime command"); }
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpContext context, JsonTypeInfo<T> typeInfo)
    {
        if (context.Request.ContentType is not { Length: > 0 } contentType || !contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return default;
        }
        try
        {
            var result = await JsonSerializer.DeserializeAsync(context.Request.Body, typeInfo, context.RequestAborted);
            if (result is null)
            {
                context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
                return default;
            }
            return result;
        }
        catch (JsonException exception)
        {
            var message = exception.Message.Replace(" Path: $ |", string.Empty, StringComparison.Ordinal);
            await ValidationProblemAsync(context, new Dictionary<string, string[]> { ["serializerErrors"] = [message] });
            return default;
        }
    }

    private static async Task AlterationProblemAsync(HttpContext context, string code, string message, int status = StatusCodes.Status400BadRequest)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = $"{Json}; charset=utf-8";
        await JsonSerializer.SerializeAsync(context.Response.Body, new WorkflowAlterationProblemView(code, message, status), WorkflowsRuntimeJsonContext.Default.WorkflowAlterationProblemView, context.RequestAborted);
    }

    private static async Task ValidationProblemAsync(HttpContext context, IReadOnlyDictionary<string, string[]> errors)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = $"{ProblemJson}; charset=utf-8";
        await JsonSerializer.SerializeAsync(context.Response.Body, new RuntimeValidationProblemDetails(errors, "One or more errors occurred!", StatusCodes.Status400BadRequest), WorkflowsRuntimeJsonContext.Default.RuntimeValidationProblemDetails, context.RequestAborted);
    }

    private static async Task JsonAsync<T>(HttpContext context, T value, JsonTypeInfo<T> typeInfo, int status = StatusCodes.Status200OK)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = $"{Json}; charset=utf-8";
        await JsonSerializer.SerializeAsync(context.Response.Body, value, typeInfo, context.RequestAborted);
    }

    private static async Task ProblemAsync(HttpContext context, int status, string detail, IReadOnlyDictionary<string, string[]>? errors = null, string? code = null)
    {
        var problem = new RuntimeProblemDetails($"https://elsa.dev/problems/runtime-request", "Runtime request failed", status, detail, null, errors, code);
        context.Response.StatusCode = status;
        context.Response.ContentType = $"{ProblemJson}; charset=utf-8";
        await JsonSerializer.SerializeAsync(context.Response.Body, problem, WorkflowsRuntimeJsonContext.Default.RuntimeProblemDetails, context.RequestAborted);
    }

    private static async Task UnexpectedAsync(HttpContext context, Exception exception, string operation)
    {
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(OwnerId).LogError(exception, "Unexpected error while {Operation}.", operation);
        await ProblemAsync(context, StatusCodes.Status500InternalServerError, "Unexpected error occurred.");
    }

    private static IRequestSender Sender(HttpContext context) => context.RequestServices.GetRequiredService<IRequestSender>();
    private static ICommandSender Commands(HttpContext context) => context.RequestServices.GetRequiredService<ICommandSender>();
    private static string? Route(HttpContext context, string name) => context.Request.RouteValues.TryGetValue(name, out var value) ? value?.ToString() : null;
    private static string? Query(HttpContext context, string name) => context.Request.Query.TryGetValue(name, out var value) ? value.ToString() : null;
    private static int? QueryInt(HttpContext context, string name) => int.TryParse(Query(context, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    private static bool? QueryBool(HttpContext context, string name) => bool.TryParse(Query(context, name), out var value) ? value : null;
    private static DateTimeOffset? QueryDate(HttpContext context, string name) => DateTimeOffset.TryParse(Query(context, name), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value) ? value : null;
}
