using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
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

        Map(endpoints.MapGet(RouteConstants.GetRoute("instances/{workflowExecutionId}"), GetInstanceAsync), "GetInstance", PermissionNames.WorkflowRuntimeRead, typeof(GetWorkflowInstanceResponse), description);
        Map(endpoints.MapGet(RouteConstants.Instances, ListInstancesAsync), "ListInstances", PermissionNames.WorkflowRuntimeRead, typeof(IReadOnlyCollection<WorkflowInstanceSummaryView>), description);
        Map(endpoints.MapGet(RouteConstants.InstancesPage, ListInstancesPageAsync), "ListInstancesPage", PermissionNames.WorkflowRuntimeRead, typeof(WorkflowInstanceListView), description);
        Map(endpoints.MapGet(RouteConstants.Executables, ListExecutablesAsync), "ListExecutables", PermissionNames.WorkflowRuntimeRead, typeof(WorkflowExecutablesListView), description);
        Map(endpoints.MapGet(RouteConstants.Executable, GetExecutableAsync), "GetExecutable", PermissionNames.WorkflowRuntimeRead, typeof(WorkflowExecutableDetailsView), description);
        Map(endpoints.MapGet(RouteConstants.ExecutableInputSources, GetExecutableInputSourcesAsync), "GetExecutableInputSources", PermissionNames.WorkflowPublishingRead, typeof(WorkflowExecutableInputSourcesView), description);
        Map(endpoints.MapGet(RouteConstants.ExecutableProvenance, GetExecutableProvenanceAsync), "GetExecutableProvenance", PermissionNames.WorkflowRuntimeRead, typeof(ExecutableProvenanceView), description);
        Map(endpoints.MapPost(RouteConstants.GetRoute("executables/{artifactId}/execute"), ExecuteAsync), "Execute", PermissionNames.WorkflowRuntimeExecute, typeof(WorkflowExecutionStartDispatchView), description, typeof(ExecuteWorkflow));
        Map(endpoints.MapPost(RouteConstants.GetRoute("stimuli"), DispatchStimulusAsync), "DispatchStimulus", PermissionNames.WorkflowRuntimeExecute, typeof(DispatchStimulusResponse), description, typeof(DispatchStimulus));
        Map(endpoints.MapGet(RouteConstants.Dispatches, ListDispatchesAsync), "ListDispatches", PermissionNames.WorkflowRuntimeRead, typeof(IReadOnlyCollection<WorkflowDispatchView>), description);
        Map(endpoints.MapGet(RouteConstants.Dispatch, GetDispatchAsync), "GetDispatch", PermissionNames.WorkflowRuntimeRead, typeof(GetWorkflowDispatchResponse), description);
        Map(endpoints.MapPost(RouteConstants.DispatchRedrive, RedriveDispatchAsync), "RedriveDispatch", PermissionNames.WorkflowRuntimeManage, typeof(WorkflowDispatchRedriveView), description, typeof(RedriveWorkflowDispatch));
        Map(endpoints.MapGet(RouteConstants.GetRoute("instances/{workflowExecutionId}/activity-executions/{activityExecutionId}"), GetActivityExecutionAsync), "GetActivityExecution", PermissionNames.WorkflowRuntimeRead, typeof(ActivityExecutionInspectionView), description);
        Map(endpoints.MapGet(RouteConstants.GetRoute("instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/descendants"), GetActivityDescendantsAsync), "GetActivityDescendants", PermissionNames.WorkflowRuntimeRead, typeof(ActivityExecutionHierarchyPageView), description);
        Map(endpoints.MapGet(RouteConstants.GetRoute("instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/layout"), GetActivityLayoutAsync), "GetActivityLayout", PermissionNames.WorkflowRuntimeRead, typeof(ActivityExecutionLayoutView), description);
        Map(endpoints.MapGet(RouteConstants.GetRoute("instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/value-evidence/{evidenceId}/payload"), GetActivityValuePayloadAsync), "GetActivityValuePayload", PermissionNames.WorkflowRuntimeRead, typeof(ActivityExecutionValuePayloadReadResult), description);
        Map(endpoints.MapGet(RouteConstants.GetRoute("instances/{workflowExecutionId}/incidents"), ListIncidentsAsync), "ListIncidents", PermissionNames.WorkflowRuntimeRead, typeof(ListIncidentsResponse), description);
        Map(endpoints.MapGet(RouteConstants.RuntimeDiagnosticsSettings, GetDiagnosticsAsync), "GetDiagnostics", PermissionNames.WorkflowRuntimeRead, typeof(RuntimeDiagnosticsSettingsView), description);
        Map(endpoints.MapPut(RouteConstants.RuntimeDiagnosticsSettings, SaveDiagnosticsAsync), "SaveDiagnostics", PermissionNames.WorkflowRuntimeManage, typeof(RuntimeDiagnosticsSettingsView), description, typeof(SaveRuntimeDiagnosticsSettings));
        Map(endpoints.MapPost(AlterationRouteConstants.Plans, SubmitAlterationAsync), "SubmitAlteration", PermissionNames.WorkflowRuntimeManage, typeof(WorkflowAlterationPlanSubmissionView), description, typeof(SubmitWorkflowAlterationPlan));
        Map(endpoints.MapGet(AlterationRouteConstants.Plan, GetAlterationAsync), "GetAlteration", PermissionNames.WorkflowRuntimeRead, typeof(WorkflowAlterationPlanView), description);
        Map(endpoints.MapGet(AlterationRouteConstants.JobsPage, PageAlterationJobsAsync), "PageAlterationJobs", PermissionNames.WorkflowRuntimeRead, typeof(WorkflowAlterationJobPageView), description);
        Map(endpoints.MapGet(AlterationRouteConstants.Job, GetAlterationJobAsync), "GetAlterationJob", PermissionNames.WorkflowRuntimeRead, typeof(WorkflowAlterationJobView), description);
        Map(endpoints.MapPost(AlterationRouteConstants.Cancel, CancelAlterationAsync), "CancelAlteration", PermissionNames.WorkflowRuntimeManage, typeof(WorkflowAlterationPlanView), description);
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
            description,
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
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
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
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
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
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
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
        catch (ArgumentException exception) { await ProblemAsync(context, StatusCodes.Status400BadRequest, exception.Message); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
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
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
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

    private static async Task GetActivityExecutionAsync(HttpContext context) =>
        await ExecuteWrappedRequestAsync(
            context,
            new GetActivityExecution(Route(context, "workflowExecutionId") ?? string.Empty, Route(context, "activityExecutionId") ?? string.Empty),
            WorkflowsRuntimeJsonContext.Default.GetActivityExecutionResponse,
            response => response.ActivityExecution,
            WorkflowsRuntimeJsonContext.Default.ActivityExecutionInspectionView);

    private static async Task GetActivityDescendantsAsync(HttpContext context) =>
        await ExecuteWrappedRequestAsync(
            context,
            new GetActivityExecutionDescendants(Route(context, "workflowExecutionId") ?? string.Empty, Route(context, "activityExecutionId") ?? string.Empty, Query(context, "cursor"), QueryInt(context, "limit"), Query(context, "include")),
            WorkflowsRuntimeJsonContext.Default.GetActivityExecutionDescendantsResponse,
            response => response.Page,
            WorkflowsRuntimeJsonContext.Default.ActivityExecutionHierarchyPageView);

    private static async Task GetActivityLayoutAsync(HttpContext context) =>
        await ExecuteWrappedRequestAsync(
            context,
            new GetActivityExecutionLayout(Route(context, "workflowExecutionId") ?? string.Empty, Route(context, "activityExecutionId") ?? string.Empty),
            WorkflowsRuntimeJsonContext.Default.GetActivityExecutionLayoutResponse,
            response => response.Layout,
            WorkflowsRuntimeJsonContext.Default.ActivityExecutionLayoutView);

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
                context.Response.StatusCode = status;
        }
        catch (ArgumentException exception) { await ProblemAsync(context, StatusCodes.Status400BadRequest, exception.Message); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception exception) { await UnexpectedAsync(context, exception, "reading activity value payload"); }
    }

    private static async Task ListIncidentsAsync(HttpContext context) =>
        await ExecuteRequestAsync(context, new ListIncidents(Route(context, "workflowExecutionId") ?? string.Empty, QueryBool(context, "blockingOnly") ?? false), WorkflowsRuntimeJsonContext.Default.ListIncidentsResponse);

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
            { await ProblemAsync(context, StatusCodes.Status400BadRequest, "The Idempotency-Key header is required."); return; }
            var result = await Sender(context).Send(request, context.RequestAborted);
            context.Response.Headers.Location = result.Links.Self;
            await JsonAsync(context, result, WorkflowsRuntimeJsonContext.Default.WorkflowAlterationPlanSubmissionView, StatusCodes.Status202Accepted);
        }
        catch (WorkflowAlterationAdmissionRejectedException exception)
        {
            context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling((exception.RetryAfter ?? TimeSpan.FromSeconds(1)).TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            await ProblemAsync(context, StatusCodes.Status429TooManyRequests, "Runtime alteration admission is temporarily at capacity.");
        }
        catch (WorkflowAlterationIdempotencyConflictException) { await ProblemAsync(context, StatusCodes.Status409Conflict, "The idempotency key is already associated with a different alteration request."); }
        catch (ArgumentOutOfRangeException exception) { await ProblemAsync(context, StatusCodes.Status400BadRequest, exception.Message, code: "InvalidIdempotencyKey"); }
        catch (InvalidOperationException) { await ProblemAsync(context, StatusCodes.Status422UnprocessableEntity, "The alteration request is invalid."); }
        catch (ArgumentException) { await ProblemAsync(context, StatusCodes.Status422UnprocessableEntity, "The alteration request is invalid."); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception exception) { await UnexpectedAsync(context, exception, "submitting runtime alteration plan"); }
    }

    private static async Task GetAlterationAsync(HttpContext context) =>
        await ExecuteRequestAsync(context, new GetWorkflowAlterationPlan(Route(context, "planId") ?? string.Empty), WorkflowsRuntimeJsonContext.Default.WorkflowAlterationPlanView);

    private static async Task PageAlterationJobsAsync(HttpContext context) =>
        await ExecuteRequestAsync(context, new PageWorkflowAlterationJobs(Route(context, "planId") ?? string.Empty, QueryInt(context, "take"), Query(context, "cursor")), WorkflowsRuntimeJsonContext.Default.WorkflowAlterationJobPageView);

    private static async Task GetAlterationJobAsync(HttpContext context) =>
        await ExecuteRequestAsync(context, new GetWorkflowAlterationJob(Route(context, "planId") ?? string.Empty, Route(context, "jobId") ?? string.Empty), WorkflowsRuntimeJsonContext.Default.WorkflowAlterationJobView);

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
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception exception) { await UnexpectedAsync(context, exception, "cancelling runtime alteration plan"); }
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
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception exception) { await UnexpectedAsync(context, exception, "reading runtime resource"); }
    }

    private static async Task ExecuteRequestAsync<TRequest, TResponse>(HttpContext context, TRequest request, JsonTypeInfo<TResponse> responseInfo)
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        try
        {
            var result = await Sender(context).Send(request, context.RequestAborted);
            await JsonAsync(context, result, responseInfo);
        }
        catch (WorkflowAlterationResourceNotFoundException) { context.Response.StatusCode = StatusCodes.Status404NotFound; }
        catch (ArgumentOutOfRangeException exception) { await ProblemAsync(context, StatusCodes.Status400BadRequest, exception.Message); }
        catch (ArgumentException exception) { await ProblemAsync(context, StatusCodes.Status400BadRequest, exception.Message); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception exception) { await UnexpectedAsync(context, exception, "handling runtime request"); }
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
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception exception) { await UnexpectedAsync(context, exception, "handling runtime command"); }
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpContext context, JsonTypeInfo<T> typeInfo)
    {
        if (context.Request.ContentType is { Length: > 0 } contentType && !contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            await ProblemAsync(context, StatusCodes.Status415UnsupportedMediaType, "The request content type is not supported.");
            return default;
        }
        if (context.Request.ContentType is null && context.Request.ContentLength is > 0)
        {
            await ProblemAsync(context, StatusCodes.Status415UnsupportedMediaType, "The request content type is not supported.");
            return default;
        }
        try
        {
            if (context.Request.ContentLength is 0)
                return JsonSerializer.Deserialize("{}", typeInfo);
            return await JsonSerializer.DeserializeAsync(context.Request.Body, typeInfo, context.RequestAborted);
        }
        catch (JsonException exception)
        {
            await ProblemAsync(context, StatusCodes.Status400BadRequest, exception.Message, new Dictionary<string, string[]> { ["serializerErrors"] = [exception.Message] });
            return default;
        }
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
