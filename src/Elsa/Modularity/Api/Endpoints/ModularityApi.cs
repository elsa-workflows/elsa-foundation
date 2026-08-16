using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Modularity.Api.Authorization;
using Elsa.Modularity.Api.Constants;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Exceptions;
using Elsa.Modularity.Core.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using System.Text.Json;

namespace Elsa.Modularity.Api.Endpoints;

/// <summary>Maps the module-management surface using ordinary ASP.NET Core endpoints.</summary>
public static class ModularityApi
{
    private const string OwnerId = "Elsa.Modularity.Api";

    public static void MapModularityApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var applicationName = endpoints.ServiceProvider.GetService<IHostEnvironment>()?.ApplicationName
                               ?? typeof(ModularityApi).Assembly.GetName().Name!;
        var descriptionMethod = typeof(RequestDelegate).GetMethod(nameof(RequestDelegate.Invoke))
            ?? throw new InvalidOperationException("RequestDelegate.Invoke metadata is unavailable.");

        endpoints.MapGet(RouteConstants.DomainPrefix, HandleListAsync)
            .WithName("ElsaModularityApiEndpointsList")
            .WithTags(applicationName)
            .WithOwner(OwnerId)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequirePermission(ModuleManagementPermissionKeys.Read)
            .WithMetadata(descriptionMethod, Response(StatusCodes.Status200OK, typeof(FeatureCatalogResponse)), Unauthorized(), Forbidden());

        endpoints.MapPost(RouteConstants.GetRoute("apply"), HandleApplyAsync)
            .WithName("ElsaModularityApiEndpointsApply")
            .WithTags(applicationName)
            .WithOwner(OwnerId)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequirePermission(ModuleManagementPermissionKeys.Manage)
            .WithMetadata(descriptionMethod, Accepts(typeof(FeatureApplyRequest)), Response(StatusCodes.Status200OK, typeof(FeatureApplyResult)), Unauthorized(), Forbidden());
    }

    private static async Task HandleListAsync(HttpContext context)
    {
        var service = context.RequestServices.GetRequiredService<IFeatureManagementService>();
        await Results.Json(await service.GetCatalogAsync(context.RequestAborted)).ExecuteAsync(context);
    }

    private static async Task HandleApplyAsync(HttpContext context)
    {
        FeatureApplyRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<FeatureApplyRequest>(context.RequestAborted);
        }
        catch (JsonException exception)
        {
            await WriteLegacyErrorAsync(context, exception.Message, StatusCodes.Status400BadRequest);
            return;
        }

        if (request is null)
        {
            await WriteLegacyErrorAsync(context, "A request body is required.", StatusCodes.Status400BadRequest);
            return;
        }

        try
        {
            var service = context.RequestServices.GetRequiredService<IFeatureManagementService>();
            await Results.Json(await service.ApplyAsync(request, context.RequestAborted)).ExecuteAsync(context);
        }
        catch (FeatureCatalogRevisionConflictException exception)
        {
            await WriteLegacyErrorAsync(context, exception.Message, StatusCodes.Status409Conflict);
        }
        catch (ArgumentException exception)
        {
            await WriteLegacyErrorAsync(context, exception.Message, StatusCodes.Status400BadRequest);
        }
        catch (InvalidOperationException exception)
        {
            await WriteLegacyErrorAsync(context, exception.Message, StatusCodes.Status400BadRequest);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(ModularityApi))
                .LogError(exception, "Unexpected error occurred when applying feature management changes.");
            await WriteLegacyErrorAsync(context, "Unexpected error occurred", StatusCodes.Status500InternalServerError);
        }
    }

    private static Task WriteLegacyErrorAsync(HttpContext context, string message, int statusCode)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json; charset=utf-8";
        return context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            errors = new Dictionary<string, string[]> { ["generalErrors"] = [message] },
            message = "One or more errors occurred!",
            statusCode
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)), context.RequestAborted);
    }

    private static AcceptsMetadata Accepts(Type type) => new(["application/json"], type, false);

    private static ProducesResponseTypeMetadata Response(int statusCode, Type type) =>
        new(statusCode, type, ["application/json"]);

    private static ProducesResponseTypeMetadata Unauthorized() =>
        new(StatusCodes.Status401Unauthorized, typeof(void), []);

    private static ProducesResponseTypeMetadata Forbidden() =>
        new(StatusCodes.Status403Forbidden, typeof(void), []);
}
