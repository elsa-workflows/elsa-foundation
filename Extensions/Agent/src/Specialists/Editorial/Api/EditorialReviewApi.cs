using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Agent.Specialists.Editorial.Contracts;
using Elsa.Agent.Specialists.Editorial.Models;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NativeEndpoints;

namespace Elsa.Agent.Specialists.Editorial.Api;

/// <summary>Maps the one editorial review read and decision endpoint pair. It is not a task inbox.</summary>
public static class EditorialReviewApi
{
    internal const string OwnerId = "Elsa.Agent.Specialists.Editorial.Api";
    internal const string Route = "agent/editorial-reviews/{workflowExecutionId}/{activityExecutionId}";

    public static void MapEditorialReviewApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapEndpointGroup(OwnerId, EditorialReviewApiJsonContext.WireContext, jsonContentType: "application/json")
            .MapEndpointsFrom(typeof(EditorialReviewApi).Assembly);
    }
}

/// <summary>Read and decide permissions. Content approval grants no tool or effect permission.</summary>
internal sealed class EditorialReviewPermissionContributor : IPermissionContributor
{
    public string OwnerId => EditorialReviewApi.OwnerId;

    public IEnumerable<Permission> Contribute() =>
    [
        new(EditorialReviewIds.ReadPermission, "Read editorial reviews", "Agents", "Read an exact pending or resolved editorial review."),
        new(EditorialReviewIds.DecidePermission, "Decide editorial reviews", "Agents", "Approve or deny an exact article awaiting editorial review.")
    ];
}

/// <summary>Derives the caller from the trusted normalized principal; nothing in a request names the actor.</summary>
internal sealed class EditorialRequestCaller(IHttpContextAccessor http, NormalizedPrincipalValidator principals)
{
    public EditorialDecider Get()
    {
        if (http.HttpContext is not { } context || !principals.TryGetNormalizedPrincipal(context.User, out var principal))
            throw new UnauthorizedAccessException("The caller is not a trusted Elsa principal.");
        var tenants = principal.FindAll(IdentityClaimTypes.TenantId).Select(claim => claim.Value).Distinct(StringComparer.Ordinal).ToArray();
        var actor = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? principal.FindFirst("sub")?.Value;
        if (tenants.Length != 1 || string.IsNullOrWhiteSpace(tenants[0]) || string.IsNullOrWhiteSpace(actor))
            throw new UnauthorizedAccessException("The caller has no single tenant and subject.");
        return new(tenants[0], actor);
    }
}

public sealed record EditorialReviewRequest([property: JsonIgnore] string WorkflowExecutionId, [property: JsonIgnore] string ActivityExecutionId);

/// <summary>The decision body. The exact round and article hash pin the wait the person saw.</summary>
public sealed record EditorialDecisionRequest(
    [property: JsonIgnore] string WorkflowExecutionId,
    [property: JsonIgnore] string ActivityExecutionId,
    int Round,
    string ArticleHash,
    EditorialDecision Decision,
    string? Comment,
    string RequestId);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Metadata,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, UseStringEnumConverter = true)]
[JsonSerializable(typeof(EditorialReviewRequest))]
[JsonSerializable(typeof(EditorialDecisionRequest))]
[JsonSerializable(typeof(EditorialReviewView))]
[JsonSerializable(typeof(EditorialDecisionReceipt))]
internal partial class EditorialReviewApiJsonContext : JsonSerializerContext
{
    internal static EditorialReviewApiJsonContext WireContext => Wire.Context;

    private static class Wire
    {
        internal static readonly EditorialReviewApiJsonContext Context = new(new JsonSerializerOptions(Default.Options));
    }
}

/// <summary>Maps the API's known refusals to plain problems; anything else is an opaque server failure.</summary>
internal sealed class EditorialReviewFaultRenderer : IEndpointFaultRenderer
{
    public async ValueTask<bool> TryWriteAsync(HttpContext context, Exception exception)
    {
        if (exception is Elsa.Primitives.Exceptions.SchemaWriteRefusedException refusal)
        {
            // Preserve the framework's actionable schema/dormancy metadata and stable refusal code.
            await new Elsa.Api.AspNetCore.ElsaFallbackEndpointProblemWriter().WriteAsync(context, Elsa.Api.AspNetCore.SchemaWriteRefusalProblem.For(refusal));
            return true;
        }
        var (status, title) = exception switch
        {
            Elsa.Primitives.Exceptions.EntityNotFoundException => (StatusCodes.Status404NotFound, "No visible editorial review matches."),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "The caller has no single tenant and subject."),
            _ => (StatusCodes.Status500InternalServerError, "The editorial review operation failed.")
        };
        await Results.Problem(statusCode: status, title: title).ExecuteAsync(context);
        return true;
    }
}
