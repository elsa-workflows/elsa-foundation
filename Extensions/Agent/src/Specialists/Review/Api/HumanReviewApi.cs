using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Agent.Specialists.Review.Contracts;
using Elsa.Agent.Specialists.Review.Models;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NativeEndpoints;

namespace Elsa.Agent.Specialists.Review.Api;

/// <summary>Maps the one human review read and decision endpoint pair. It is not a task inbox.</summary>
public static class HumanReviewApi
{
    internal const string OwnerId = "Elsa.Agent.Specialists.Review.Api";
    internal const string Route = "agent/reviews/{workflowExecutionId}/{activityExecutionId}";

    public static void MapHumanReviewApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapEndpointGroup(OwnerId, HumanReviewApiJsonContext.WireContext, jsonContentType: "application/json")
            .MapEndpointsFrom(typeof(HumanReviewApi).Assembly);
    }
}

/// <summary>Read and decide permissions. Content approval grants no tool or effect permission.</summary>
internal sealed class HumanReviewPermissionContributor : IPermissionContributor
{
    public string OwnerId => HumanReviewApi.OwnerId;

    public IEnumerable<Permission> Contribute() =>
    [
        new(HumanReviewIds.ReadPermission, "Read agent reviews", "Agents", "Read exact content awaiting, or resolved by, a human review."),
        new(HumanReviewIds.DecidePermission, "Decide agent reviews", "Agents", "Approve or deny exact content awaiting a human review.")
    ];
}

/// <summary>Derives the caller from the trusted normalized principal; nothing in a request names the actor.</summary>
internal sealed class ReviewRequestCaller(IHttpContextAccessor http, NormalizedPrincipalValidator principals)
{
    public ReviewDecider Get()
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

public sealed record HumanReviewRequest([property: JsonIgnore] string WorkflowExecutionId, [property: JsonIgnore] string ActivityExecutionId);

/// <summary>The decision body. The exact content hash pins the wait the person saw.</summary>
public sealed record ReviewDecisionRequest(
    [property: JsonIgnore] string WorkflowExecutionId,
    [property: JsonIgnore] string ActivityExecutionId,
    string ContentHash,
    ReviewDecision Decision,
    string? Comment,
    string RequestId);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Metadata,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, UseStringEnumConverter = true)]
[JsonSerializable(typeof(HumanReviewRequest))]
[JsonSerializable(typeof(ReviewDecisionRequest))]
[JsonSerializable(typeof(HumanReviewView))]
[JsonSerializable(typeof(ReviewDecisionReceipt))]
internal partial class HumanReviewApiJsonContext : JsonSerializerContext
{
    internal static HumanReviewApiJsonContext WireContext => Wire.Context;

    private static class Wire
    {
        internal static readonly HumanReviewApiJsonContext Context = new(new JsonSerializerOptions(Default.Options));
    }
}

/// <summary>Maps the API's known refusals to plain problems; anything else is an opaque server failure.</summary>
internal sealed class HumanReviewFaultRenderer : IEndpointFaultRenderer
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
            Elsa.Primitives.Exceptions.EntityNotFoundException => (StatusCodes.Status404NotFound, "No visible review matches."),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "The caller has no single tenant and subject."),
            _ => (StatusCodes.Status500InternalServerError, "The review operation failed.")
        };
        await Results.Problem(statusCode: status, title: title).ExecuteAsync(context);
        return true;
    }
}
