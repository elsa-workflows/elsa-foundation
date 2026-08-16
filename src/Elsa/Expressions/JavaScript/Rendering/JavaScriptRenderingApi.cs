using Elsa.Api.AspNetCore;
using Elsa.Expressions.JavaScript.Rendering.Core.Contracts;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Expressions.JavaScript.Rendering;

/// <summary>Maps the JavaScript declaration rendering surface using ordinary ASP.NET Core endpoints.</summary>
public static class JavaScriptRenderingApi
{
    private const string OwnerId = "Elsa.Expressions.JavaScript.Rendering";

    public static void MapJavaScriptRenderingApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var handler = new RequestDelegate(HandleRenderAsync);
        var descriptionMethod = typeof(RequestDelegate).GetMethod(nameof(RequestDelegate.Invoke))
            ?? throw new InvalidOperationException("RequestDelegate.Invoke metadata is unavailable.");

        endpoints.MapGet("javascript/documents/render", handler)
            .WithName("ElsaExpressionsJavaScriptRenderingEndpointsRenderEndpoint")
            .WithHostApplicationOpenApiTag(endpoints.ServiceProvider)
            .WithOwner(OwnerId)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequirePermission(JavaScriptRenderingPermissions.Render)
            .WithMetadata(
                descriptionMethod,
                new ProducesResponseTypeMetadata(StatusCodes.Status200OK, typeof(object), ["application/json"]),
                new ProducesResponseTypeMetadata(StatusCodes.Status500InternalServerError, typeof(object), ["application/json"]),
                new ProducesResponseTypeMetadata(StatusCodes.Status401Unauthorized, typeof(void), []),
                new ProducesResponseTypeMetadata(StatusCodes.Status403Forbidden, typeof(void), []));
    }

    private static async Task HandleRenderAsync(HttpContext context)
    {
        try
        {
            var factory = context.RequestServices.GetRequiredService<IJavaScriptDeclarationsDocumentFactory>();
            var renderer = context.RequestServices.GetRequiredService<IJavaScriptDeclarationsDocumentRenderer>();
            var document = await factory.Create(context.RequestAborted);
            var rendered = renderer.Render(document);
            await Results.Json(
                new JavaScriptRenderingSuccessResponse(true, rendered),
                JavaScriptRenderingJsonContext.Default.JavaScriptRenderingSuccessResponse,
                contentType: "application/json")
                .ExecuteAsync(context);
        }
        catch (Exception exception)
        {
            await Results.Json(
                    new JavaScriptRenderingFailureResponse(false, exception.Message),
                    JavaScriptRenderingJsonContext.Default.JavaScriptRenderingFailureResponse,
                    contentType: "application/json",
                    statusCode: StatusCodes.Status500InternalServerError)
                .ExecuteAsync(context);
        }
    }
}
