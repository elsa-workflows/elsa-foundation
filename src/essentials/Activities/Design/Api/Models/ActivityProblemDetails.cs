using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Primitives.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Elsa.Activities.Design.Api.Models;

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
            ActivityDiagnosticOrderer.Order(exception.Diagnostics),
            exception.Recovery);
    }

    public static ActivityProblemDetailsView Unexpected(HttpContext context) => new(
        Type(ActivityErrorCodes.OperationFailed),
        "Activity operation failed",
        StatusCodes.Status500InternalServerError,
        "The activity operation failed.",
        context.Request.Path,
        ActivityErrorCodes.OperationFailed,
        context.TraceIdentifier,
        []);

    public static string Type(string errorCode) =>
        $"https://elsa.dev/problems/{errorCode.Replace('.', '-')}";
}
