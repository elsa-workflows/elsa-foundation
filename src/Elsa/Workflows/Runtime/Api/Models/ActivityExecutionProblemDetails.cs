using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Runtime.Api.Models;

/// <summary>RFC 7807 response used by Runtime-owned activity execution inspection endpoints.</summary>
public sealed record ActivityExecutionProblemDetailsView(
    string Type,
    string Title,
    int Status,
    string Detail,
    string Instance,
    string ErrorCode,
    string TraceId,
    IReadOnlyList<ActivityExecutionProblemDiagnosticView> Diagnostics);

/// <summary>Safe diagnostic extension point. Inspection request failures currently return an empty list.</summary>
public sealed record ActivityExecutionProblemDiagnosticView(string Code, string Message, string Severity);

internal static class ActivityExecutionProblemDetails
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static Task NotFoundAsync(HttpContext context, CancellationToken cancellationToken) =>
        WriteAsync(
            context,
            StatusCodes.Status404NotFound,
            "activity.execution.not-found",
            "Activity execution not found",
            "The requested activity execution was not found.",
            cancellationToken);

    public static Task InvalidRequestAsync(HttpContext context, string detail, CancellationToken cancellationToken) =>
        WriteAsync(
            context,
            StatusCodes.Status400BadRequest,
            "activity.request.invalid",
            "Invalid activity execution request",
            detail,
            cancellationToken);

    public static Task CursorAsync(
        HttpContext context,
        ActivityExecutionHierarchyCursorException exception,
        CancellationToken cancellationToken) =>
        exception.Failure switch
        {
            ActivityExecutionHierarchyCursorFailure.BindingMismatch => WriteAsync(
                context,
                StatusCodes.Status409Conflict,
                "activity.cursor.binding-mismatch",
                "Activity execution cursor does not match",
                "The activity execution hierarchy cursor does not belong to this query or authorization scope.",
                cancellationToken),
            ActivityExecutionHierarchyCursorFailure.Expired => WriteAsync(
                context,
                StatusCodes.Status410Gone,
                "activity.cursor.expired",
                "Activity execution cursor expired",
                "The activity execution hierarchy snapshot used by this cursor is no longer available.",
                cancellationToken),
            _ => WriteAsync(
                context,
                StatusCodes.Status400BadRequest,
                "activity.request.invalid",
                "Invalid activity execution cursor",
                "The activity execution hierarchy cursor is invalid.",
                cancellationToken)
        };

    public static Task UnexpectedAsync(HttpContext context, CancellationToken cancellationToken) =>
        WriteAsync(
            context,
            StatusCodes.Status500InternalServerError,
            "activity.operation.failed",
            "Activity execution inspection failed",
            "The activity execution inspection operation failed.",
            cancellationToken);

    private static async Task WriteAsync(
        HttpContext context,
        int status,
        string errorCode,
        string title,
        string detail,
        CancellationToken cancellationToken)
    {
        var response = new ActivityExecutionProblemDetailsView(
            Type(errorCode),
            title,
            status,
            detail,
            context.Request.Path,
            errorCode,
            context.TraceIdentifier,
            []);
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await JsonSerializer.SerializeAsync(context.Response.Body, response, JsonOptions, cancellationToken);
    }

    private static string Type(string errorCode) =>
        $"https://elsa.dev/problems/{errorCode.Replace('.', '-')}";
}
