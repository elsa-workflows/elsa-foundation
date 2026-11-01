using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Bpmn.Core;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NativeEndpoints;

namespace Elsa.Activities.Bpmn.Api.Endpoints;

internal static class BpmnHostingApi
{
    public const string Owner = "Elsa.Activities.Bpmn.Api";
    public const string Read = "bpmn:read";
    public const string Publish = "bpmn:publish";
    public const string Execute = "bpmn:execute";
    public const string CompleteHuman = "bpmn:complete-human";
    public const string Cancel = "bpmn:cancel";
    public const string RetryService = "bpmn:retry-service";
    public static void Map(IEndpointRouteBuilder endpoints) =>
        NativeEndpoints.Generated.ElsaActivitiesBpmnApiEndpoints.Map(
            endpoints.MapEndpointGroup(Owner, BpmnHostingJsonContext.Default, jsonContentType: "application/json; charset=utf-8", tag: "BPMN"));
    public static string Route(HttpContext context, string key) =>
        context.Request.RouteValues[key]?.ToString() ?? throw new ArgumentException("A required route identity is missing.");
    public static int CommandStatus(BpmnModelCommandOutcome result, bool start = false) =>
        result.Pending ? StatusCodes.Status202Accepted : result.Result?.GetProperty("status").GetString() == "Rejected" ? StatusCodes.Status409Conflict
            : start && !result.Replay ? StatusCodes.Status201Created : StatusCodes.Status200OK;
}

internal sealed class BpmnHostingPermissionContributor : IPermissionContributor
{
    public string OwnerId => BpmnHostingApi.Owner;
    public IEnumerable<Permission> Contribute() =>
    [
        new(BpmnHostingApi.Read, "Read BPMN executions", "BPMN", "Inspect model executions, human work and command outcomes."),
        new(BpmnHostingApi.Publish, "Publish BPMN models", "BPMN", "Publish supported immutable BPMN model material."),
        new(BpmnHostingApi.Execute, "Start BPMN executions", "BPMN", "Start an execution from an authorized live model source."),
        new(BpmnHostingApi.CompleteHuman, "Complete BPMN human work", "BPMN", "Complete an exact active human invocation and attempt."),
        new(BpmnHostingApi.Cancel, "Cancel BPMN executions", "BPMN", "Cancel an execution and retain its downstream settlement responsibility."),
        new(BpmnHostingApi.RetryService, "Retry a held BPMN service", "BPMN", "Retry an exact current held failure within its pinned attempt budget.")
    ];
}

internal sealed class BpmnApiCaller(IHttpContextAccessor contexts, NormalizedPrincipalValidator validator,
    IPermissionAuthorizationService authorization, IBpmnModelRuntime runtime)
{
    public BpmnModelCaller Current()
    {
        if (!validator.TryGetNormalizedPrincipal(contexts.HttpContext?.User ?? new(), out var principal)) throw new BpmnModelResourceMissingException();
        var subjects = principal.Claims.Where(claim => claim.Type is ClaimTypes.NameIdentifier or "sub")
            .Select(claim => claim.Value).Distinct(StringComparer.Ordinal).ToArray();
        var tenants = principal.FindAll(IdentityClaimTypes.TenantId).ToArray();
        if (subjects is not [{ Length: > 0 }] || tenants.Length > 1 || tenants.Any(claim => string.IsNullOrWhiteSpace(claim.Value)))
            throw new BpmnModelResourceMissingException();
        return new(subjects[0], tenants.SingleOrDefault()?.Value);
    }
    public async ValueTask<bool> CanReadAsync(object resource, CancellationToken cancellationToken)
    {
        var caller = Current();
        if (!validator.TryGetNormalizedPrincipal(contexts.HttpContext!.User, out var principal)) throw new BpmnModelResourceMissingException();
        return (await authorization.AuthorizeAsync(new(principal, BpmnHostingApi.Read, caller.TenantId, resource), cancellationToken)).Succeeded;
    }
    public async ValueTask<BpmnInspectionView> InspectAsync(string executionId, CancellationToken cancellationToken)
    {
        var identity = await ForExecutionAsync(executionId, BpmnHostingApi.Read, cancellationToken);
        var inspection = await runtime.InspectAsync(executionId, identity, cancellationToken);
        // A revision may advance between the first resource check and projection. Authorize
        // the exact returned snapshot before disclosing its graph, terminal result or work.
        if (inspection.State.ExecutionId != executionId || inspection.State.TenantId != identity.TenantId ||
            !await CanReadAsync(inspection.State, cancellationToken)) throw new BpmnModelResourceMissingException();
        return BpmnInspectionView.From(inspection);
    }
    public async ValueTask<BpmnModelCaller> ForExecutionAsync(string executionId, string permission, CancellationToken cancellationToken)
    {
        var caller = Current();
        var state = await runtime.FindExecutionAsync(executionId, caller, cancellationToken) ?? throw new BpmnModelResourceMissingException();
        if (!validator.TryGetNormalizedPrincipal(contexts.HttpContext!.User, out var principal)) throw new BpmnModelResourceMissingException();
        var decision = await authorization.AuthorizeAsync(new(principal, permission, caller.TenantId, state), cancellationToken);
        if (!decision.Succeeded) throw new BpmnModelResourceMissingException();
        return caller;
    }

    public async ValueTask<BpmnModelCommandOutcome> CommandAsync(string executionId, string requestId, CancellationToken cancellationToken)
    {
        var caller = Current();
        var state = await runtime.FindExecutionAsync(executionId, caller, cancellationToken);
        if (!validator.TryGetNormalizedPrincipal(contexts.HttpContext!.User, out var principal)) throw new BpmnModelResourceMissingException();
        // Pending admission has no execution state yet. Authorize its command identity
        // without first loading a retained outcome; this is not an execution-state projection.
        await RequireReadAsync((object?)state ?? new BpmnModelCommandOutcome(executionId, requestId, false, true, null));
        var result = await runtime.FindCommandAsync(executionId, requestId, caller, cancellationToken) ?? throw new BpmnModelResourceMissingException();
        if (state is null && (!result.Pending || result.Result is not null))
        {
            // Admission may have committed between the two reads. The resulting execution
            // must pass its own resource policy before its retained value is disclosed.
            var admitted = await runtime.FindExecutionAsync(executionId, caller, cancellationToken) ?? throw new BpmnModelResourceMissingException();
            await RequireReadAsync(admitted);
        }
        return result;

        async ValueTask RequireReadAsync(object resource)
        {
            var decision = await authorization.AuthorizeAsync(new(principal, BpmnHostingApi.Read, caller.TenantId, resource), cancellationToken);
            if (!decision.Succeeded) throw new BpmnModelResourceMissingException();
        }
    }
}

internal sealed class BpmnHostingFaultRenderer(IEndpointProblemWriter writer) : IEndpointFaultRenderer
{
    public async ValueTask<bool> TryWriteAsync(HttpContext context, Exception exception)
    {
        var problem = exception switch
        {
            BpmnModelResourceMissingException => EndpointProblem.General(StatusCodes.Status404NotFound, "The BPMN resource is not available."),
            RuntimeCheckpointReplayConflictException => EndpointProblem.General(StatusCodes.Status409Conflict, "The request id was already used with different content."),
            RuntimeSchedulerWorkUnavailableException unavailable => EndpointProblem.General(StatusCodes.Status503ServiceUnavailable, unavailable.SafeSummary),
            SchemaWriteRefusedException refused => SchemaWriteRefusalProblem.For(refused),
            BpmnModelValidationException invalid => EndpointProblem.General(StatusCodes.Status422UnprocessableEntity, invalid.Message),
            NotSupportedException unsupported => EndpointProblem.General(StatusCodes.Status422UnprocessableEntity, unsupported.Message),
            ArgumentException invalid => EndpointProblem.General(StatusCodes.Status400BadRequest, invalid.Message),
            JsonException => EndpointProblem.General(StatusCodes.Status400BadRequest, "The BPMN request is malformed."),
            _ => null
        };
        if (problem is null) return false;
        await writer.WriteAsync(context, problem);
        return true;
    }
}

internal sealed record PublishBpmnModelRequest(string RequestId, BpmnModelDefinition Definition);
internal sealed record BpmnAdmissionView(bool AdmitNewExecutions);
internal sealed record StartBpmnModelRequest(string SourceReferenceId, string RequestId, Dictionary<string, JsonElement>? Inputs = null);
internal sealed record GetBpmnExecutionRequest(string Id);
internal sealed record ListBpmnWorkRequest(string Id, int Limit = RuntimeStorePageRequest.DefaultLimit, string? ContinuationToken = null);
internal sealed record CompleteBpmnHumanRequest(string Id, string Invocation, string RequestId, string AttemptId, string[]? Outcomes = null);
internal sealed record GetBpmnCommandRequest(string Id, string Request);
internal sealed record CancelBpmnExecutionRequest(string Id, string RequestId, string? Reason = null);
internal sealed record ListBpmnIncidentsRequest(string Id, int Limit = RuntimeStorePageRequest.DefaultLimit, string? ContinuationToken = null);
internal sealed record RetryBpmnServiceRequest(string Id, string RequestId, string IncidentId, string InvocationId, string FailedAttemptId);
internal sealed record BpmnExecutionView(string ExecutionId, long Revision, string Status, RuntimeExecutionProtocol Protocol,
    WorkflowExecutableIdentity Executable, string MaterialIncarnationId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, JsonElement? Result);

[Get("bpmn/admission")]
[RequirePermission(BpmnHostingApi.Read)]
internal sealed class GetAdmissionEndpoint(IBpmnModelRuntime runtime, BpmnApiCaller caller) : ApiEndpointWithoutRequest<BpmnAdmissionView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetBpmnAdmission";
    public override Task<BpmnAdmissionView> HandleAsync(CancellationToken cancellationToken)
    { _ = caller.Current(); return Task.FromResult(new BpmnAdmissionView(runtime.AdmitNewExecutions)); }
}
internal sealed record BpmnInspectionView(BpmnExecutionView Execution, WorkflowExecutionAuthoritySnapshot Authority,
    string Profile, JsonElement Process, BpmnInspectionSource? Source, IReadOnlyList<BpmnInspectionWork> Work,
    IReadOnlyList<BpmnInspectionAttempt> Attempts, IReadOnlyList<BpmnServiceIncident> Incidents, IReadOnlyList<BpmnInspectionSettlement> Settlements)
{
    public static BpmnInspectionView From(BpmnModelInspection inspection)
    {
        var state = inspection.State;
        return new(new(state.ExecutionId, state.Revision, state.Lifecycle.ToString(), state.Protocol, state.Executable,
            state.MaterialIncarnationId, state.CreatedAt, state.UpdatedAt, state.TerminalResult), state.Authority,
            inspection.Profile, inspection.Process, inspection.Source, inspection.Work, inspection.Attempts, inspection.Incidents, inspection.Settlements);
    }
}

[Get("bpmn/executions/{id}/inspection")]
[RequirePermission(BpmnHostingApi.Read)]
internal sealed class InspectExecutionEndpoint(BpmnApiCaller caller) : ApiEndpoint<GetBpmnExecutionRequest, BpmnInspectionView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "InspectBpmnExecution";
    public override Task<BpmnInspectionView> HandleAsync(GetBpmnExecutionRequest request, CancellationToken cancellationToken) =>
        caller.InspectAsync(BpmnHostingApi.Route(HttpContext, "id"), cancellationToken).AsTask();
}

[Post("bpmn/artifacts")]
[RequirePermission(BpmnHostingApi.Publish)]
internal sealed class PublishEndpoint(IBpmnModelRuntime runtime, BpmnApiCaller caller) : ApiEndpoint<PublishBpmnModelRequest, BpmnPublishedModel>
{
    public override void Configure(ApiEndpointOptions options)
    { options.Operation = "PublishBpmnModel"; options.Accepts = ["application/json"]; options.BodyMode = EndpointBodyMode.RequiredWithContentTypeAndPayload; }
    public override async Task<BpmnPublishedModel> HandleAsync(PublishBpmnModelRequest request, CancellationToken cancellationToken) =>
        await runtime.PublishAsync(request.Definition, request.RequestId, caller.Current(), cancellationToken);
}

[Post("bpmn/executions")]
[RequirePermission(BpmnHostingApi.Execute)]
internal sealed class StartEndpoint(IBpmnModelRuntime runtime, BpmnApiCaller caller) : ApiEndpointWithResult<StartBpmnModelRequest, BpmnModelCommandOutcome>
{
    public override void Configure(ApiEndpointOptions options)
    { options.Operation = "StartBpmnModel"; options.Accepts = ["application/json"]; options.BodyMode = EndpointBodyMode.RequiredWithContentTypeAndPayload; }
    public override async Task<EndpointResult<BpmnModelCommandOutcome>> HandleAsync(StartBpmnModelRequest request, CancellationToken cancellationToken)
    {
        var result = await runtime.StartAsync(request.SourceReferenceId, request.RequestId, request.Inputs, caller.Current(), cancellationToken);
        return EndpointResult.Status(BpmnHostingApi.CommandStatus(result, start: true), result);
    }
}

[Get("bpmn/executions/{id}")]
[RequirePermission(BpmnHostingApi.Read)]
internal sealed class GetExecutionEndpoint(IBpmnModelRuntime runtime, BpmnApiCaller caller) : ApiEndpoint<GetBpmnExecutionRequest, BpmnExecutionView>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetBpmnExecution";
    public override async Task<BpmnExecutionView> HandleAsync(GetBpmnExecutionRequest request, CancellationToken cancellationToken)
    {
        var id = BpmnHostingApi.Route(HttpContext, "id");
        var identity = await caller.ForExecutionAsync(id, BpmnHostingApi.Read, cancellationToken);
        var state = (await runtime.FindExecutionAsync(id, identity, cancellationToken))!;
        return new(state.ExecutionId, state.Revision, state.Lifecycle.ToString(), state.Protocol, state.Executable,
            state.MaterialIncarnationId, state.CreatedAt, state.UpdatedAt, state.TerminalResult);
    }
}

[Get("bpmn/executions/{id}/work")]
[RequirePermission(BpmnHostingApi.Read)]
internal sealed class ListWorkEndpoint(IBpmnModelRuntime runtime, BpmnApiCaller caller) : ApiEndpoint<ListBpmnWorkRequest, RuntimeStorePage<BpmnHumanWork>>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "ListBpmnHumanWork";
    public override async Task<RuntimeStorePage<BpmnHumanWork>> HandleAsync(ListBpmnWorkRequest request, CancellationToken cancellationToken)
    {
        var id = BpmnHostingApi.Route(HttpContext, "id");
        return await runtime.ListWorkAsync(id, new(request.Limit, request.ContinuationToken),
            await caller.ForExecutionAsync(id, BpmnHostingApi.Read, cancellationToken), cancellationToken);
    }
}

[Post("bpmn/executions/{id}/work/{invocation}/complete")]
[RequirePermission(BpmnHostingApi.CompleteHuman)]
internal sealed class CompleteHumanEndpoint(IBpmnModelRuntime runtime, BpmnApiCaller caller) : ApiEndpointWithResult<CompleteBpmnHumanRequest, BpmnModelCommandOutcome>
{
    public override void Configure(ApiEndpointOptions options)
    { options.Operation = "CompleteBpmnHumanWork"; options.Accepts = ["application/json"]; options.BodyMode = EndpointBodyMode.RequiredWithContentTypeAndPayload; }
    public override async Task<EndpointResult<BpmnModelCommandOutcome>> HandleAsync(CompleteBpmnHumanRequest request, CancellationToken cancellationToken)
    {
        var id = BpmnHostingApi.Route(HttpContext, "id");
        var result = await runtime.CompleteHumanAsync(id, request.RequestId, BpmnHostingApi.Route(HttpContext, "invocation"), request.AttemptId,
            request.Outcomes, await caller.ForExecutionAsync(id, BpmnHostingApi.CompleteHuman, cancellationToken), cancellationToken);
        return EndpointResult.Status(BpmnHostingApi.CommandStatus(result), result);
    }
}

[Post("bpmn/executions/{id}/cancel")]
[RequirePermission(BpmnHostingApi.Cancel)]
internal sealed class CancelEndpoint(IBpmnModelRuntime runtime, BpmnApiCaller caller) : ApiEndpointWithResult<CancelBpmnExecutionRequest, BpmnModelCommandOutcome>
{
    public override void Configure(ApiEndpointOptions options)
    { options.Operation = "CancelBpmnExecution"; options.Accepts = ["application/json"]; options.BodyMode = EndpointBodyMode.RequiredWithContentTypeAndPayload; }
    public override async Task<EndpointResult<BpmnModelCommandOutcome>> HandleAsync(CancelBpmnExecutionRequest request, CancellationToken cancellationToken)
    {
        var id = BpmnHostingApi.Route(HttpContext, "id");
        var identity = await caller.ForExecutionAsync(id, BpmnHostingApi.Cancel, cancellationToken);
        var result = await runtime.CancelAsync(id, request.RequestId, request.Reason, identity, cancellationToken);
        return EndpointResult.Status(BpmnHostingApi.CommandStatus(result), result);
    }
}

[Get("bpmn/executions/{id}/incidents")]
[RequirePermission(BpmnHostingApi.Read)]
internal sealed class ListIncidentsEndpoint(IBpmnModelRuntime runtime, BpmnApiCaller caller) : ApiEndpoint<ListBpmnIncidentsRequest, RuntimeStorePage<BpmnServiceIncident>>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "ListBpmnServiceIncidents";
    public override async Task<RuntimeStorePage<BpmnServiceIncident>> HandleAsync(ListBpmnIncidentsRequest request, CancellationToken cancellationToken)
    {
        var id = BpmnHostingApi.Route(HttpContext, "id");
        var identity = await caller.ForExecutionAsync(id, BpmnHostingApi.Read, cancellationToken);
        return await runtime.ListIncidentsAsync(id, new(request.Limit, request.ContinuationToken), identity, cancellationToken);
    }
}

[Post("bpmn/executions/{id}/retry-service")]
[RequirePermission(BpmnHostingApi.RetryService)]
internal sealed class RetryServiceEndpoint(IBpmnModelRuntime runtime, BpmnApiCaller caller) : ApiEndpointWithResult<RetryBpmnServiceRequest, BpmnModelCommandOutcome>
{
    public override void Configure(ApiEndpointOptions options)
    { options.Operation = "RetryBpmnService"; options.Accepts = ["application/json"]; options.BodyMode = EndpointBodyMode.RequiredWithContentTypeAndPayload; }
    public override async Task<EndpointResult<BpmnModelCommandOutcome>> HandleAsync(RetryBpmnServiceRequest request, CancellationToken cancellationToken)
    {
        var id = BpmnHostingApi.Route(HttpContext, "id");
        // Authorize the current execution before either a fresh retry or retained-result replay.
        var identity = await caller.ForExecutionAsync(id, BpmnHostingApi.RetryService, cancellationToken);
        var result = await runtime.RetryServiceAsync(id, request.RequestId, request.IncidentId, request.InvocationId, request.FailedAttemptId, identity, cancellationToken);
        return EndpointResult.Status(BpmnHostingApi.CommandStatus(result), result);
    }
}

[Get("bpmn/executions/{id}/commands/{request}")]
[RequirePermission(BpmnHostingApi.Read)]
internal sealed class GetCommandEndpoint(BpmnApiCaller caller) : ApiEndpoint<GetBpmnCommandRequest, BpmnModelCommandOutcome>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "GetBpmnCommand";
    public override async Task<BpmnModelCommandOutcome> HandleAsync(GetBpmnCommandRequest request, CancellationToken cancellationToken)
    {
        var id = BpmnHostingApi.Route(HttpContext, "id");
        return await caller.CommandAsync(id, BpmnHostingApi.Route(HttpContext, "request"), cancellationToken);
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(PublishBpmnModelRequest))]
[JsonSerializable(typeof(StartBpmnModelRequest))]
[JsonSerializable(typeof(GetBpmnExecutionRequest))]
[JsonSerializable(typeof(ListBpmnWorkRequest))]
[JsonSerializable(typeof(CompleteBpmnHumanRequest))]
[JsonSerializable(typeof(GetBpmnCommandRequest))]
[JsonSerializable(typeof(CancelBpmnExecutionRequest))]
[JsonSerializable(typeof(ListBpmnIncidentsRequest))]
[JsonSerializable(typeof(RetryBpmnServiceRequest))]
[JsonSerializable(typeof(BpmnPublishedModel))]
[JsonSerializable(typeof(BpmnModelCommandOutcome))]
[JsonSerializable(typeof(BpmnExecutionView))]
[JsonSerializable(typeof(BpmnInspectionView))]
[JsonSerializable(typeof(BpmnAdmissionView))]
[JsonSerializable(typeof(RuntimeStorePage<BpmnHumanWork>))]
[JsonSerializable(typeof(RuntimeStorePage<BpmnServiceIncident>))]
[JsonSerializable(typeof(BpmnCatalogRequest))]
[JsonSerializable(typeof(BpmnCatalogPage<BpmnPublicationView>))]
[JsonSerializable(typeof(BpmnCatalogPage<BpmnExecutionView>))]
internal partial class BpmnHostingJsonContext : JsonSerializerContext;
