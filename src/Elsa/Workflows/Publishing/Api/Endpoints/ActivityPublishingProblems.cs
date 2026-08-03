using Elsa.Workflows.Publishing.Api.Services;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Microsoft.AspNetCore.Http;
using Elsa.Primitives.Diagnostics;

namespace Elsa.Workflows.Publishing.Api.Endpoints;

internal sealed record ActivityPublishingDiagnosticView(
    string Code,
    string Severity,
    string Message,
    ActivityDiagnosticSubject Subject,
    ActivityDiagnosticLocation? Location,
    string? Remediation,
    IReadOnlyDictionary<string, string> Metadata);

internal sealed record ActivityPublishingProblemDetails(
    string Type,
    string Title,
    int Status,
    string Detail,
    string Instance,
    string ErrorCode,
    string TraceId,
    IReadOnlyList<ActivityPublishingDiagnosticView> Diagnostics);

internal static class ActivityPublishingProblems
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public static ActivityPublishingProblemDetails Rejected(
        ActivityPublicationRejectedException exception,
        HttpContext context,
        string defaultTitle)
    {
        var status = StatusCode(exception);
        return new(
            Type(exception.ErrorCode),
            Title(exception.ErrorCode, defaultTitle),
            status,
            exception.Message,
            context.Request.Path.Value ?? string.Empty,
            exception.ErrorCode,
            context.TraceIdentifier,
            ActivityDiagnosticOrderer.Order(exception.Diagnostics).Select(ToView).ToArray());
    }

    public static ActivityPublishingProblemDetails Unexpected(HttpContext context) => new(
        Type(ActivityErrorCodes.OperationFailed),
        "Activity operation failed",
        StatusCodes.Status500InternalServerError,
        "The activity operation failed.",
        context.Request.Path.Value ?? string.Empty,
        ActivityErrorCodes.OperationFailed,
        context.TraceIdentifier,
        []);

    public static async Task WriteAsync(
        HttpResponse response,
        ActivityPublishingProblemDetails problem,
        CancellationToken cancellationToken)
    {
        response.StatusCode = problem.Status;
        response.ContentType = "application/problem+json";
        await JsonSerializer.SerializeAsync(response.Body, problem, JsonOptions, cancellationToken);
    }

    private static int StatusCode(ActivityPublicationRejectedException exception) => exception.ErrorCode switch
    {
        ActivityErrorCodes.RequestInvalid => StatusCodes.Status400BadRequest,
        "activity.tenant.reference-denied" => StatusCodes.Status403Forbidden,
        "activity.draft.not-found" => StatusCodes.Status404NotFound,
        "activity.definition.content-authority" or
        "activity.draft.stale-revision" or
        "activity.definition.stale-head" or
        ActivityErrorCodes.VersionConflict or
        ActivityErrorCodes.PublicationConflict or
        "activity.draft.not-active" or
        "activity.draft.stale-layout" or
        "activity.draft.layout-not-found" => StatusCodes.Status409Conflict,
        _ when exception.IsConflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status422UnprocessableEntity
    };

    private static string Title(string errorCode, string defaultTitle) => errorCode switch
    {
        ActivityErrorCodes.RequestInvalid => "Activity request is invalid",
        "activity.tenant.reference-denied" => "Activity reference is forbidden",
        "activity.draft.not-found" => "Activity draft was not found",
        _ when errorCode.Contains("stale", StringComparison.Ordinal) || errorCode.Contains("conflict", StringComparison.Ordinal) =>
            "Activity operation conflict",
        _ => defaultTitle
    };

    private static string Type(string errorCode) =>
        $"https://elsa.dev/problems/{errorCode.Replace('.', '-')}";

    private static ActivityPublishingDiagnosticView ToView(ActivityDiagnostic diagnostic) => new(
        diagnostic.Code,
        diagnostic.Severity.ToString(),
        diagnostic.Message,
        diagnostic.Subject,
        diagnostic.Location,
        diagnostic.Remediation,
        diagnostic.Metadata ?? EmptyMetadata);
}
