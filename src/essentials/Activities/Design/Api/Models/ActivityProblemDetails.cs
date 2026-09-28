using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Api.AspNetCore;
using Elsa.Primitives.Diagnostics;
using Elsa.Primitives.Exceptions;
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

    /// <summary>
    /// A schema write refusal (spec 180, FR-016a): a 409 whose error code is the refusal's stable code, and whose detail
    /// names the family and both versions.
    /// </summary>
    public static ActivityProblemDetailsView SchemaWriteRefused(SchemaWriteRefusedException refusal, HttpContext context) => new(
        Type(refusal.Code),
        "Schema write refused",
        SchemaWriteRefusalProblem.StatusCode,
        refusal.Message,
        context.Request.Path,
        refusal.Code,
        context.TraceIdentifier,
        []);

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
