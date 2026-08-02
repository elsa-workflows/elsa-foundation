using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Http;
using Elsa.Primitives.Diagnostics;

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
    IReadOnlyList<ActivityExecutionProblemDiagnosticView> Diagnostics,
    ActivityExecutionCursorProblemView? Cursor);

public sealed record ActivityExecutionCursorProblemView(
    string CursorClass,
    string BoundaryBinding,
    string QueryBinding,
    string AccessBinding,
    bool Recoverable,
    string RecoveryAction);

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
            ActivityErrorCodes.RequestInvalid,
            "Invalid activity execution request",
            detail,
            cancellationToken);

    public static Task ForbiddenAsync(HttpContext context, CancellationToken cancellationToken) =>
        WriteAsync(
            context,
            StatusCodes.Status403Forbidden,
            "activity.value-payload.forbidden",
            "Activity execution value payload resolution denied",
            "A separate value-payload resolution permission is required.",
            cancellationToken);

    public static Task ValueUnavailableAsync(HttpContext context, CancellationToken cancellationToken) =>
        WriteAsync(
            context,
            StatusCodes.Status409Conflict,
            "activity.value-payload.unavailable",
            "Activity execution value payload unavailable",
            "Runtime did not capture a payload for this value evidence.",
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
                exception.Metadata,
                cancellationToken),
            ActivityExecutionHierarchyCursorFailure.Expired => WriteAsync(
                context,
                StatusCodes.Status410Gone,
                ActivityErrorCodes.CursorExpired,
                "Activity execution cursor expired",
                "The activity execution hierarchy snapshot used by this cursor is no longer available.",
                exception.Metadata,
                cancellationToken),
            _ => WriteAsync(
                context,
                StatusCodes.Status400BadRequest,
                ActivityErrorCodes.RequestInvalid,
                "Invalid activity execution cursor",
                "The activity execution hierarchy cursor is invalid.",
                null,
                cancellationToken)
        };

    public static Task UnexpectedAsync(HttpContext context, CancellationToken cancellationToken) =>
        WriteAsync(
            context,
            StatusCodes.Status500InternalServerError,
            ActivityErrorCodes.OperationFailed,
            "Activity execution inspection failed",
            "The activity execution inspection operation failed.",
            cancellationToken);

    private static async Task WriteAsync(
        HttpContext context,
        int status,
        string errorCode,
        string title,
        string detail,
        ActivityExecutionCursorFailureMetadata? cursor,
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
            [],
            cursor is null
                ? null
                : new(
                    cursor.CursorClass,
                    cursor.BoundaryBinding.ToString(),
                    cursor.QueryBinding.ToString(),
                    cursor.AccessBinding.ToString(),
                    cursor.Recoverable,
                    cursor.RecoveryAction));
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await JsonSerializer.SerializeAsync(context.Response.Body, response, JsonOptions, cancellationToken);
    }

    private static Task WriteAsync(
        HttpContext context,
        int status,
        string errorCode,
        string title,
        string detail,
        CancellationToken cancellationToken) =>
        WriteAsync(context, status, errorCode, title, detail, null, cancellationToken);

    private static string Type(string errorCode) =>
        $"https://elsa.dev/problems/{errorCode.Replace('.', '-')}";
}
