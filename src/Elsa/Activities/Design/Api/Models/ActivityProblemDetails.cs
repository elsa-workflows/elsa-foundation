using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Microsoft.AspNetCore.Http;

namespace Elsa.Activities.Design.Api.Models;

/// <summary>Shared RFC 7807 extension shape for every reusable-activity Design failure.</summary>
public sealed record ActivityProblemDetailsView(
    string Type,
    string Title,
    int Status,
    string Detail,
    string Instance,
    string ErrorCode,
    string TraceId,
    IReadOnlyList<ActivityDiagnostic> Diagnostics);

public static class ActivityProblemDetails
{
    public static ActivityProblemDetailsView From(ActivityAuthoringException exception, HttpContext context)
    {
        if (exception.StatusCode >= StatusCodes.Status500InternalServerError)
            return Unexpected(context);

        return new(
            Type(exception.ErrorCode),
            exception.Title,
            exception.StatusCode,
            exception.Message,
            context.Request.Path,
            exception.ErrorCode,
            context.TraceIdentifier,
            ActivityDiagnosticOrderer.Order(exception.Diagnostics));
    }

    public static ActivityProblemDetailsView Unexpected(HttpContext context) => new(
        Type("activity.operation.failed"),
        "Activity operation failed",
        StatusCodes.Status500InternalServerError,
        "The activity operation failed.",
        context.Request.Path,
        "activity.operation.failed",
        context.TraceIdentifier,
        []);

    public static string Type(string errorCode) =>
        $"https://elsa.dev/problems/{errorCode.Replace('.', '-')}";
}
