using Elsa.Api.AspNetCore;
using NativeEndpoints;
using Elsa.Expressions.JavaScript.Rendering.Core.Contracts;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Expressions.JavaScript.Rendering;

/// <summary>Maps the JavaScript declaration rendering surface using ordinary ASP.NET Core endpoints.</summary>
public static class JavaScriptRenderingApi
{
    internal const string OwnerId = "Elsa.Expressions.JavaScript.Rendering";

    public static void MapJavaScriptRenderingApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // The published document tags this surface with the host application name, resolved at
        // composition time exactly as the hand-written mapper did.
        var applicationName = endpoints.ServiceProvider.GetService<IHostEnvironment>()?.ApplicationName;
        var api = endpoints.MapEndpointGroup(
            OwnerId,
            JavaScriptRenderingJsonContext.Default,
            jsonContentType: "application/json",
            tag: string.IsNullOrWhiteSpace(applicationName) ? null : applicationName);

        // The published document declares plain object bodies for both 200 and the message-carrying
        // 500 (the legacy projection), so the operation stays on the group's raw seam and keeps its
        // own writes instead of adopting a typed endpoint-class schema.
        api.MapUnboundOperation(
                "GET", "javascript/documents/render", "RenderEndpoint",
                typeof(object), StatusCodes.Status200OK, null, HandleRenderAsync)
            .RequirePermission(JavaScriptRenderingPermissions.Render)
            .WithMetadata(new ProducesResponseTypeMetadata(StatusCodes.Status500InternalServerError, typeof(object), ["application/json"]));
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
        catch (Exception exception) when (exception is not OperationCanceledException)
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
