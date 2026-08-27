using Elsa.Api.AspNetCore;
using NativeEndpoints;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Elsa.Workflows.Runtime.JavaScript;

/// <summary>Maps JavaScript activity execution using ordinary ASP.NET Core endpoints.</summary>
public static class JavaScriptExecutionApi
{
    private const string OwnerId = "Elsa.Workflows.Runtime.JavaScript";

    public static void MapJavaScriptExecutionApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // The published document tags this surface with the host application name, resolved at
        // composition time exactly as the hand-written mapper did.
        var applicationName = endpoints.ServiceProvider.GetService<IHostEnvironment>()?.ApplicationName;
        var api = endpoints.MapEndpointGroup(
            OwnerId,
            JavaScriptExecutionJsonContext.Default,
            jsonContentType: "application/json",
            tag: string.IsNullOrWhiteSpace(applicationName) ? null : applicationName);

        // The published document declares a plain object 200/500 pair plus the legacy problem 400,
        // under an operation id that predates the naming scheme, so the operation stays on the
        // group's raw seam with its own body reading and writes.
        api.MapUnboundOperation(
                "POST", "javascript/execute", "RunJavaScript",
                typeof(object), StatusCodes.Status200OK, null, HandleExecuteAsync,
                name: "ElsaWorkflowsRuntimeJavaScriptActivitiesRunJavaScriptEndpoint")
            .RequirePermission(JavaScriptExecutionPermissions.Execute)
            .WithMetadata(
                new AcceptsMetadata(["application/json"], typeof(RequestModel), false),
                new ProducesResponseTypeMetadata(StatusCodes.Status400BadRequest, typeof(JavaScriptExecutionProblemDetailsResponse), ["application/problem+json"]),
                new ProducesResponseTypeMetadata(StatusCodes.Status500InternalServerError, typeof(object), ["application/json"]));
    }

    private static async Task HandleExecuteAsync(HttpContext context)
    {
        var request = (RequestModel?)null;
        try
        {
            request = await context.Request.ReadFromJsonAsync(
                JavaScriptExecutionJsonContext.Default.RequestModel,
                context.RequestAborted);
        }
        catch (JsonException exception)
        {
            await WriteRequestProblemDetailsAsync(context, NormalizeJsonExceptionMessage(exception.Message));
            return;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Script))
        {
            await Results.Json(
                    new JavaScriptExecutionErrorResponse("Script is null"),
                    JavaScriptExecutionJsonContext.Default.JavaScriptExecutionErrorResponse,
                    contentType: "application/json",
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
                    JavaScriptExecutionJsonContext.Default.JavaScriptExecutionSuccessResponse,
                    contentType: "application/json")
                .ExecuteAsync(context);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Elsa.Workflows.Runtime.JavaScript")
                .LogError(exception, "JavaScript script execution failed.");
            await Results.Json(
                    new JavaScriptExecutionFailureResponse(false, exception.Message),
                    JavaScriptExecutionJsonContext.Default.JavaScriptExecutionFailureResponse,
                    contentType: "application/json",
                    statusCode: StatusCodes.Status500InternalServerError)
                .ExecuteAsync(context);
        }
    }

    private static Task WriteRequestProblemDetailsAsync(HttpContext context, string detail) =>
        Results.Json(
                new JavaScriptExecutionProblemDetailsResponse(
                    detail,
                    [new JavaScriptExecutionProblemError("serializerErrors", detail)],
                    context.Request.Path.Value ?? "",
                    StatusCodes.Status400BadRequest,
                    "Bad Request",
                    context.TraceIdentifier,
                    "https://www.rfc-editor.org/rfc/rfc7231#section-6.5.1"),
                JavaScriptExecutionJsonContext.Default.JavaScriptExecutionProblemDetailsResponse,
                contentType: "application/problem+json",
                statusCode: StatusCodes.Status400BadRequest).ExecuteAsync(context);

    private static string NormalizeJsonExceptionMessage(string message)
    {
        return message.Replace(" Path: $ | ", " ", StringComparison.Ordinal);
    }
}
