using Elsa.Api.AspNetCore;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Exceptions;
using Elsa.Modularity.Core.Models;
using Elsa.Studio.Api.Authorization;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using global::Nuplane.Abstractions.PackageStoreProtection;

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
        group.MapGet("", CatalogAsync)
            .RequireAuthorization(StudioBridgeAuthorization.ModuleManagementReadPolicyName);
        group.MapPost("/apply", ApplyAsync).Accepts<FeatureApplyRequest>("application/json")
            .RequireAuthorization(StudioBridgeAuthorization.ModuleManagementManagePolicyName);
    }

    private static async Task<IResult> CatalogAsync(IFeatureManagementService service, HttpContext context)
    {
        try
        {
            return Results.Json(await service.GetCatalogAsync(context.RequestAborted), Json);
        }
        catch (PackageStoreAdmissionException error)
        {
            return Unavailable(context, error);
        }
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
            Json, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
