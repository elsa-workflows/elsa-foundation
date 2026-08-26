using Elsa.Api.Endpoints;
using Elsa3.Activities.Design.Import.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Security.Claims;
using System.Text.Json;

namespace Elsa3.Activities.Design.Import.Endpoints;

/// <summary>Maps the Elsa 3 reusable-activity import surface using ordinary ASP.NET Core endpoints.</summary>
public static class ReusableActivityImportApi
{
    internal const string OwnerId = "Elsa3.Activities.Design.Import";

    public static void MapReusableActivityImportApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // The published documents tag this surface with the host application name, resolved at
        // composition time exactly as the hand-written mapper did.
        var applicationName = endpoints.ServiceProvider.GetService<IHostEnvironment>()?.ApplicationName
                              ?? typeof(ReusableActivityImportApi).Assembly.GetName().Name!;
        var api = endpoints.MapModuleEndpoints(
            OwnerId,
            ReusableActivityImportJsonContext.Default,
            jsonContentType: "application/json; charset=utf-8",
            tag: applicationName);

        api.MapEndpointsFrom(typeof(ReusableActivityImportApi).Assembly);
    }
}

public sealed record ReusableActivityImportSelectionRequest(string PlanId, IReadOnlyCollection<string> SelectedSourceVersionIds);

public sealed record ReusableActivityImportApplyHttpRequest(string PlanId, IReadOnlyCollection<string> SelectedSourceVersionIds, string IdempotencyKey);

internal static class Elsa3ImportPermissions
{
    public const string Read = "elsa3-import.read";
    public const string Manage = "elsa3-import.manage";
}

public static class ReusableActivityImportHttp
{
    private const string ElsaTenantClaim = "elsa.identity.tenant_id";
    private const string ConventionalTenantClaim = "tenant_id";

    public static ReusableActivityImportAccessScope Scope(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? user.FindFirst("sub")?.Value
                     ?? user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userId))
            throw new ReusableActivityImportNotFoundException("The authorized Elsa 3 import caller has no stable user identity.");
        var tenantId = user.FindFirst(ElsaTenantClaim)?.Value ?? user.FindFirst(ConventionalTenantClaim)?.Value;
        return new(tenantId, userId);
    }

    public static Task WriteProblemAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code, title) = exception switch
        {
            ReusableActivityImportNotFoundException => (404, "elsa3.import.not-found", "Elsa 3 import resource not found"),
            ReusableActivityImportExpiredException => (410, "elsa3.import.collection-expired", "Elsa 3 import collection expired"),
            ReusableActivityImportIdempotencyConflictException => (409, "elsa3.import.idempotency-conflict", "Idempotency key conflict"),
            ReusableActivityImportCollisionException => (409, "elsa3.import.identity-collision", "Import identity collision"),
            ReusableActivityImportValidationException => (422, "elsa3.import.validation-failed", "Elsa 3 import validation failed"),
            ReusableActivityImportPayloadException => (400, "elsa3.import.payload-invalid", "Elsa 3 import payload invalid"),
            ArgumentException => (400, "elsa3.import.request-invalid", "Elsa 3 import request invalid"),
            _ => (500, "elsa3.import.unexpected", "Elsa 3 import failed")
        };
        var problem = new ReusableActivityImportProblem(
            status,
            $"https://elsa.dev/problems/{code}",
            title,
            status == 500 ? "The Elsa 3 import could not be completed." : exception.Message,
            context.Request.Path,
            code,
            exception is ReusableActivityImportValidationException validation ? validation.Diagnostics : null);
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsync(
            JsonSerializer.Serialize(problem, ReusableActivityImportJsonContext.Default.ReusableActivityImportProblem),
            cancellationToken);
    }
}
