using Elsa.Api.AspNetCore;
using Microsoft.AspNetCore.Http;
using NativeEndpoints;

namespace Elsa.Workflows.Publishing.Api.Endpoints;

/// <summary>
/// The Publishing owner's established error shape: RFC 7807 fields plus the FastEndpoints-era
/// <c>traceId</c> and <c>errors</c> extensions, written as <c>application/problem+json</c>.
/// </summary>
internal sealed record WorkflowPublishingLegacyProblem(
    string Type,
    string Title,
    int Status,
    string Detail,
    string Instance,
    string TraceId,
    IReadOnlyList<WorkflowPublishingLegacyProblemError> Errors);

internal sealed record WorkflowPublishingLegacyProblemError(string Name, string Reason);

internal sealed class WorkflowPublishingProblemWriter : IEndpointProblemWriter
{
    private const string ProblemJsonMediaType = "application/problem+json";

    public Task WriteAsync(HttpContext context, EndpointProblem problem)
    {
        var errors = problem.Errors
            .SelectMany(entry => entry.Value.Select(reason => new WorkflowPublishingLegacyProblemError(entry.Key, reason)))
            .ToArray();
        var payload = new WorkflowPublishingLegacyProblem(
            LegacyProblemType(problem.StatusCode),
            LegacyProblemTitle(problem.StatusCode),
            problem.StatusCode,
            errors.FirstOrDefault()?.Reason ?? string.Empty,
            context.Request.Path,
            context.TraceIdentifier,
            errors);
        var typeInfo = WorkflowsPublishingJsonOptions.WireContext.WorkflowPublishingLegacyProblem;
        return Results.Json(payload, typeInfo, statusCode: problem.StatusCode, contentType: ProblemJsonMediaType).ExecuteAsync(context);
    }

    private static string LegacyProblemType(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "https://www.rfc-editor.org/rfc/rfc7231#section-6.5.1",
        StatusCodes.Status404NotFound => "https://www.rfc-editor.org/rfc/rfc7231#section-6.5.4",
        StatusCodes.Status409Conflict => "https://www.rfc-editor.org/rfc/rfc7231#section-6.5.8",
        StatusCodes.Status500InternalServerError => "https://www.rfc-editor.org/rfc/rfc7231#section-6.5.1",
        _ => "about:blank"
    };

    private static string LegacyProblemTitle(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "Bad Request",
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status500InternalServerError => "One or more errors occurred.",
        _ => "HTTP error"
    };
}
