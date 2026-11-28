using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Exceptions;
using Elsa.Modularity.Core.Models;
using Elsa.Studio.Api.Authorization;
using global::Nuplane.Abstractions.PackageStoreProtection;

namespace Elsa.Studio.Hosting.Nuplane;

internal static class FeatureManagementEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/modularity/features");
        group.MapGet("", CatalogAsync)
            .RequireAuthorization(StudioBridgeAuthorization.ModuleManagementReadPolicyName);
        group.MapPost("/apply", ApplyAsync).RequireAuthorization(StudioBridgeAuthorization.ModuleManagementManagePolicyName);
    }

    private static async Task<IResult> CatalogAsync(IFeatureManagementService service, HttpContext context)
    {
        try
        {
            return Results.Ok(await service.GetCatalogAsync(context.RequestAborted));
        }
        catch (PackageStoreAdmissionException error)
        {
            return Unavailable(context, error);
        }
    }

    private static async Task<IResult> ApplyAsync(FeatureApplyRequest request, IFeatureManagementService service, HttpContext context)
    {
        var cancellationToken = context.RequestAborted;
        if (request.Features is null || request.Features.Any(feature => feature is null))
            return Results.BadRequest(new { Error = "Features must be a non-null list of feature states." });
        if (request.Features.Any(feature => string.IsNullOrWhiteSpace(feature.Id) || feature.Id.Contains(':') || feature.Id != feature.Id.Trim()))
            return Results.BadRequest(new { Error = "Feature IDs must be non-blank configuration path components without colons or surrounding whitespace." });
        if (request.Features.GroupBy(feature => feature.Id, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            return Results.BadRequest(new { Error = "Apply requests must not contain duplicate feature IDs." });
        try
        {
            return Results.Ok(await service.ApplyAsync(request, cancellationToken));
        }
        catch (FeatureCatalogRevisionConflictException error)
        {
            return Results.Conflict(new { Error = error.Message });
        }
        catch (FeatureActivationRefusedException error)
        {
            return Results.Conflict(new { Error = error.Message, error.Refusals });
        }
        catch (ConfigurationReadOnlyException error)
        {
            return Results.Conflict(new { Error = error.Message, Code = "configuration-read-only" });
        }
        catch (ArgumentException error)
        {
            return Results.BadRequest(new { Error = error.Message });
        }
        catch (StudioHostingException error)
        {
            return Results.Json(new { Error = error.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (PackageStoreAdmissionException error)
        {
            return Unavailable(context, error);
        }
    }

    /// <summary>
    /// The catalog reads installed package metadata under Nuplane's admission. An enrolled store's reconciliation cycle holds its
    /// non-waiting root lock, so a read meanwhile is refused; the client reads again shortly, as for the module registry.
    /// </summary>
    private static IResult Unavailable(HttpContext context, PackageStoreAdmissionException error)
    {
        context.Response.Headers.RetryAfter = "1";
        return Results.Json(new { Error = "The feature catalog is temporarily unavailable.", Reason = error.Reason.ToString() },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
