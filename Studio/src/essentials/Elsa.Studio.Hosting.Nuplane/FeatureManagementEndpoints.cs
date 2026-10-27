using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Exceptions;
using Elsa.Modularity.Core.Models;
using Elsa.Studio.Api.Authorization;

namespace Elsa.Studio.Hosting.Nuplane;

internal static class FeatureManagementEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/modularity/features");
        group.MapGet("", async (IFeatureManagementService service, CancellationToken token) => Results.Ok(await service.GetCatalogAsync(token)))
            .RequireAuthorization(StudioBridgeAuthorization.ModuleManagementReadPolicyName);
        group.MapPost("/apply", ApplyAsync).RequireAuthorization(StudioBridgeAuthorization.ModuleManagementManagePolicyName);
    }

    private static async Task<IResult> ApplyAsync(FeatureApplyRequest request, IFeatureManagementService service, CancellationToken cancellationToken)
    {
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
    }
}
