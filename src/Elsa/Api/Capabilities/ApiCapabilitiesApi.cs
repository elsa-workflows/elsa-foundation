using Elsa.Api.AspNetCore;
using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Authorization;
using Elsa.Api.Capabilities.Models;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Api.Capabilities;

/// <summary>Maps the API capabilities surface using ordinary ASP.NET Core endpoints.</summary>
public static class ApiCapabilitiesApi
{
    private const string OwnerId = "Elsa.Api.Capabilities";

    public static void MapApiCapabilitiesApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RequestDelegate handler = HandleGetCapabilitiesAsync;
        var descriptionMethod = typeof(RequestDelegate).GetMethod(nameof(RequestDelegate.Invoke))
            ?? throw new InvalidOperationException("RequestDelegate.Invoke metadata is unavailable.");

        endpoints.MapGet("/capabilities", handler)
            .WithName("ElsaApiCapabilitiesEndpointsGetCapabilities")
            .WithHostApplicationOpenApiTag(endpoints.ServiceProvider)
            .WithOwner(OwnerId)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequireAnyPermission(PermissionKey.Wildcard, ApiCapabilitiesPermissions.Read)
            .WithMetadata(
                descriptionMethod,
                Response(StatusCodes.Status200OK, typeof(ApiCapabilitiesDocument)),
                Unauthorized(),
                Forbidden());
    }

    private static async Task HandleGetCapabilitiesAsync(HttpContext context)
    {
        var document = await context.RequestServices
            .GetRequiredService<IApiCapabilityCatalog>()
            .GetAsync(context.RequestAborted);
        await Results.Json(document, ApiCapabilitiesJsonContext.Default.ApiCapabilitiesDocument, contentType: "application/json").ExecuteAsync(context);
    }

    private static ProducesResponseTypeMetadata Response(int statusCode, Type bodyType) =>
        new(statusCode, bodyType, ["application/json"]);

    private static ProducesResponseTypeMetadata Unauthorized() =>
        new(StatusCodes.Status401Unauthorized, typeof(void), []);

    private static ProducesResponseTypeMetadata Forbidden() =>
        new(StatusCodes.Status403Forbidden, typeof(void), []);
}
