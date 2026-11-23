using Elsa.Api.AspNetCore;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Exceptions;
using Elsa.Modularity.Core.Models;
using Elsa.Studio.Api.Authorization;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;

namespace Elsa.Studio.Hosting.Nuplane;

internal static class FeatureManagementEndpoints
{
    private static readonly JsonSerializerOptions Json = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = Elsa.Serialization.Core.JsonBaseline.CreateCopy();
        Elsa.Serialization.Core.StrictDateTimeConverters.Configure(options, apiMode: true);
        options.MakeReadOnly();
        return options;
    }

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/modularity/features").WithElsaJsonResponseHeaders();
        group.MapGet("", async (IFeatureManagementService service, CancellationToken token) => Results.Json(await service.GetCatalogAsync(token), Json))
            .RequireAuthorization(StudioBridgeAuthorization.ModuleManagementReadPolicyName);
        group.MapPost("/apply", ApplyAsync).Accepts<FeatureApplyRequest>("application/json")
            .RequireAuthorization(StudioBridgeAuthorization.ModuleManagementManagePolicyName);
    }

    private static async Task<IResult> ApplyAsync(HttpContext context, IFeatureManagementService service, CancellationToken cancellationToken)
    {
        if (context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == false)
            return Results.Json(new { Error = "The feature apply request body is required." }, Json, statusCode: StatusCodes.Status400BadRequest);
        if (!context.Request.HasJsonContentType()) return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        FeatureApplyRequest? request;
        try { request = await context.Request.ReadFromJsonAsync<FeatureApplyRequest>(Json, cancellationToken); }
        catch (JsonException) { return Results.Json(new { Error = "The feature apply request is not valid JSON." }, Json, statusCode: StatusCodes.Status400BadRequest); }
        if (request?.Features is null || request.Features.Any(feature => feature is null))
            return Results.Json(new { Error = "Features must be a non-null list of feature states." }, Json, statusCode: StatusCodes.Status400BadRequest);
        if (request.Features.Any(feature => string.IsNullOrWhiteSpace(feature.Id) || feature.Id.Contains(':') || feature.Id != feature.Id.Trim()))
            return Results.Json(new { Error = "Feature IDs must be non-blank configuration path components without colons or surrounding whitespace." }, Json, statusCode: StatusCodes.Status400BadRequest);
        if (request.Features.GroupBy(feature => feature.Id, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            return Results.Json(new { Error = "Apply requests must not contain duplicate feature IDs." }, Json, statusCode: StatusCodes.Status400BadRequest);
        try
        {
            return Results.Json(await service.ApplyAsync(request, cancellationToken), Json);
        }
        catch (FeatureCatalogRevisionConflictException error)
        {
            return Results.Json(new { Error = error.Message }, Json, statusCode: StatusCodes.Status409Conflict);
        }
        catch (FeatureActivationRefusedException error)
        {
            return Results.Json(new { Error = error.Message, error.Refusals }, Json, statusCode: StatusCodes.Status409Conflict);
        }
        catch (ConfigurationReadOnlyException error)
        {
            return Results.Json(new { Error = error.Message, Code = "configuration-read-only" }, Json, statusCode: StatusCodes.Status409Conflict);
        }
        catch (ArgumentException error)
        {
            return Results.Json(new { Error = error.Message }, Json, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (StudioHostingException error)
        {
            return Results.Json(new { Error = error.Message }, Json, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
