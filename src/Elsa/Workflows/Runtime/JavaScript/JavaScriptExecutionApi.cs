using System.Text.Json;
using Elsa.Api.AspNetCore;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.JavaScript;

/// <summary>Maps JavaScript activity execution using ordinary ASP.NET Core endpoints.</summary>
public static class JavaScriptExecutionApi
{
    private const string OwnerId = "Elsa.Workflows.Runtime.JavaScript";

    public static void MapJavaScriptExecutionApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RequestDelegate handler = HandleExecuteAsync;
        var descriptionMethod = typeof(RequestDelegate).GetMethod(nameof(RequestDelegate.Invoke))
            ?? throw new InvalidOperationException("RequestDelegate.Invoke metadata is unavailable.");

        endpoints.MapPost("javascript/execute", handler)
            .WithName("ElsaWorkflowsRuntimeJavaScriptActivitiesRunJavaScriptEndpoint")
            .WithHostApplicationOpenApiTag(endpoints.ServiceProvider)
            .WithOwner(OwnerId)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequireAnyPermission(PermissionKey.Wildcard, JavaScriptExecutionPermissions.Execute)
            .WithMetadata(
                descriptionMethod,
                new AcceptsMetadata(["application/json"], typeof(RequestModel), false),
                new ProducesResponseTypeMetadata(StatusCodes.Status200OK, typeof(object), ["application/json"]),
                new ProducesResponseTypeMetadata(StatusCodes.Status400BadRequest, typeof(object), ["application/json"]),
                new ProducesResponseTypeMetadata(StatusCodes.Status500InternalServerError, typeof(object), ["application/json"]),
                new ProducesResponseTypeMetadata(StatusCodes.Status401Unauthorized, typeof(void), []),
                new ProducesResponseTypeMetadata(StatusCodes.Status403Forbidden, typeof(void), []));
    }

    private static async Task HandleExecuteAsync(HttpContext context)
    {
        RequestModel? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync(
                JavaScriptExecutionJsonContext.Default.RequestModel,
                context.RequestAborted);
        }
        catch (JsonException)
        {
            await Results.Json(
                    new JavaScriptExecutionErrorResponse("The request body is invalid."),
                    JavaScriptExecutionJsonContext.Default.JavaScriptExecutionErrorResponse,
                    statusCode: StatusCodes.Status400BadRequest)
                .ExecuteAsync(context);
            return;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Script))
        {
            await Results.Json(
                    new JavaScriptExecutionErrorResponse("Script is null"),
                    JavaScriptExecutionJsonContext.Default.JavaScriptExecutionErrorResponse,
                    statusCode: StatusCodes.Status400BadRequest)
                .ExecuteAsync(context);
            return;
        }

        try
        {
            var evaluator = context.RequestServices.GetRequiredService<IJavaScriptScriptEvaluator>();
            using var argumentsDocument = JsonDocument.Parse("{}");
            var arguments = argumentsDocument.RootElement.Clone();
            var result = await evaluator.EvaluateAsync(
                new JavaScriptScriptEvaluationRequest(request.Script, arguments, context.RequestAborted));
            await Results.Json(
                    new JavaScriptExecutionSuccessResponse(true, result),
                    JavaScriptExecutionJsonContext.Default.JavaScriptExecutionSuccessResponse)
                .ExecuteAsync(context);
        }
        catch (Exception exception)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Elsa.Workflows.Runtime.JavaScript")
                .LogError(exception, "JavaScript script execution failed.");
            await Results.Json(
                    new JavaScriptExecutionFailureResponse(false, exception.Message),
                    JavaScriptExecutionJsonContext.Default.JavaScriptExecutionFailureResponse,
                    statusCode: StatusCodes.Status500InternalServerError)
                .ExecuteAsync(context);
        }
    }

}
