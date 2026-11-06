using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Workflows.Publishing.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalEndpoints;

namespace Elsa.Agent.Specialists.Inspection.Api;

/// <summary>
/// Maps the specialist evidence reads. They reuse the generic draft Test Run's identity and authorization and add only the
/// specialist's observable evidence; nothing here starts, continues or cancels work, or returns protected turn material.
/// </summary>
public static class SpecialistInspectionApi
{
    internal const string OwnerId = "Elsa.Agent.Specialists.Inspection.Api";
    internal const string TestRunEvidenceRoute = "agent/specialists/test-runs/{testRunId}/evidence";
    internal const string InvocationEvidenceRoute = "agent/specialists/invocations/{workflowExecutionId}/{activityExecutionId}/evidence";
    // Owned by the Workflows Runtime API (WorkflowRuntimePermissions.WorkflowRuntimeRead), which guards its activity execution reads.
    internal const string RuntimeRead = "workflows/runtime:read";

    public static void MapSpecialistInspectionApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapEndpointGroup(OwnerId, SpecialistInspectionApiJsonContext.WireContext, jsonContentType: "application/json")
            .MapEndpointsFrom(typeof(SpecialistInspectionApi).Assembly);
    }
}

public sealed record SpecialistTestRunEvidenceRequest([property: JsonIgnore] string TestRunId);

/// <summary>
/// The generic Test Run's exact candidate and status, with the evidence of the specialist it placed as its root.
/// <see cref="Invocation"/> is null until the run has a root invocation, or when the draft is not a specialist.
/// <see cref="ValuesPermitted"/> is false when the caller may not see captured values; the result's value is then left out.
/// </summary>
public sealed record SpecialistTestRunEvidenceView(string TestRunId, string DraftId, long DraftRevision, string? ArtifactId,
    string WorkflowExecutionId, string Status, SpecialistInvocationEvidence? Invocation, bool ValuesPermitted);

public sealed record SpecialistInvocationEvidenceRequest([property: JsonIgnore] string WorkflowExecutionId, [property: JsonIgnore] string ActivityExecutionId);

/// <summary>One runtime occurrence's specialist evidence; the result's value is left out unless <see cref="ValuesPermitted"/>.</summary>
public sealed record SpecialistInvocationEvidenceView(SpecialistInvocationEvidence Invocation, bool ValuesPermitted);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Metadata,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, UseStringEnumConverter = true)]
[JsonSerializable(typeof(SpecialistTestRunEvidenceRequest))]
[JsonSerializable(typeof(SpecialistTestRunEvidenceView))]
[JsonSerializable(typeof(SpecialistInvocationEvidenceRequest))]
[JsonSerializable(typeof(SpecialistInvocationEvidenceView))]
internal partial class SpecialistInspectionApiJsonContext : JsonSerializerContext
{
    internal static SpecialistInspectionApiJsonContext WireContext => Wire.Context;

    private static class Wire
    {
        internal static readonly SpecialistInspectionApiJsonContext Context = new(new JsonSerializerOptions(Default.Options));
    }
}

/// <summary>
/// Renders the generic Test Run's refusals with their stable codes: an unknown or invisible run is 404 and a foreign tenant
/// reference 403. An occurrence the caller may not see or inspect is 404. A schema write refusal keeps its own 409 answer. Anything else, including a journal failure, is an
/// opaque server failure.
/// </summary>
internal sealed class SpecialistInspectionFaultRenderer : IEndpointFaultRenderer
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
            await Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No visible specialist invocation matches.").ExecuteAsync(context);
            return true;
        }
        await Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "The specialist evidence read failed.").ExecuteAsync(context);
        return true;
    }
}
