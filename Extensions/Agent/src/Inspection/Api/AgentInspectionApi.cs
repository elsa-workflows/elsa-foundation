using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Agent.Core.Models;
using Elsa.Workflows.Publishing.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalEndpoints;

namespace Elsa.Agent.Inspection.Api;

/// <summary>
/// Maps the agent evidence reads. They reuse the generic draft Test Run's identity and authorization and add only the
/// agent's observable evidence; nothing here starts, continues or cancels work, or returns protected turn material.
/// </summary>
public static class AgentInspectionApi
{
    internal const string OwnerId = "Elsa.Agent.Inspection.Api";
    internal const string TestRunEvidenceRoute = "agent/test-runs/{testRunId}/evidence";
    internal const string InvocationEvidenceRoute = "agent/invocations/{workflowExecutionId}/{activityExecutionId}/evidence";
    // Owned by the Workflows Runtime API (WorkflowRuntimePermissions.WorkflowRuntimeRead), which guards its activity execution reads.
    internal const string RuntimeRead = "workflows/runtime:read";

    public static void MapAgentInspectionApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapEndpointGroup(OwnerId, AgentInspectionApiJsonContext.WireContext, jsonContentType: "application/json")
            .MapEndpointsFrom(typeof(AgentInspectionApi).Assembly);
    }
}

public sealed record AgentTestRunEvidenceRequest([property: JsonIgnore] string TestRunId);

/// <summary>
/// The generic Test Run's exact candidate and status, with the evidence of the agent it placed as its root.
/// <see cref="Invocation"/> is null until the run has a root invocation, or when the draft is not an agent.
/// <see cref="ValuesPermitted"/> is false when the caller may not see captured values; the result's value is then left out.
/// </summary>
public sealed record AgentTestRunEvidenceView(string TestRunId, string DraftId, long DraftRevision, string? ArtifactId,
    string WorkflowExecutionId, string Status, AgentInvocationEvidence? Invocation, bool ValuesPermitted);

public sealed record AgentInvocationEvidenceRequest([property: JsonIgnore] string WorkflowExecutionId, [property: JsonIgnore] string ActivityExecutionId);

/// <summary>One runtime occurrence's agent evidence; the result's value is left out unless <see cref="ValuesPermitted"/>.</summary>
public sealed record AgentInvocationEvidenceView(AgentInvocationEvidence Invocation, bool ValuesPermitted);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Metadata,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AgentTestRunEvidenceRequest))]
[JsonSerializable(typeof(AgentTestRunEvidenceView))]
[JsonSerializable(typeof(AgentInvocationEvidenceRequest))]
[JsonSerializable(typeof(AgentInvocationEvidenceView))]
internal partial class AgentInspectionApiJsonContext : JsonSerializerContext
{
    internal static AgentInspectionApiJsonContext WireContext => Wire.Context;

    private static class Wire
    {
        internal static readonly AgentInspectionApiJsonContext Context = new(new JsonSerializerOptions(Default.Options));
    }
}

/// <summary>
/// Renders the generic Test Run's refusals with their stable codes: an unknown or invisible run is 404 and a foreign tenant
/// reference 403. An occurrence the caller may not see or inspect is 404. A schema write refusal keeps its own 409 answer. Anything else, including a journal failure, is an
/// opaque server failure.
/// </summary>
internal sealed class AgentInspectionFaultRenderer : IEndpointFaultRenderer
{
    public async ValueTask<bool> TryWriteAsync(HttpContext context, Exception exception)
    {
        if (exception is Elsa.Primitives.Exceptions.SchemaWriteRefusedException refusal)
        {
            // Preserve the framework's actionable schema/dormancy metadata and stable refusal code.
            await new Elsa.Api.AspNetCore.ElsaFallbackEndpointProblemWriter().WriteAsync(context, Elsa.Api.AspNetCore.SchemaWriteRefusalProblem.For(refusal));
            return true;
        }
        if (exception is ActivityPublicationRejectedException rejected)
        {
            var status = rejected.ErrorCode switch
            {
                "activity.test-run.not-found" => StatusCodes.Status404NotFound,
                "activity.tenant.reference-denied" => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status422UnprocessableEntity
            };
            await Results.Problem(statusCode: status, title: rejected.Message,
                extensions: new Dictionary<string, object?> { ["errorCode"] = rejected.ErrorCode }).ExecuteAsync(context);
            return true;
        }
        if (exception is Elsa.Primitives.Exceptions.EntityNotFoundException)
        {
            await Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No visible agent invocation matches.").ExecuteAsync(context);
            return true;
        }
        await Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "The agent evidence read failed.").ExecuteAsync(context);
        return true;
    }
}
