using Elsa.Api.AspNetCore;
using Microsoft.AspNetCore.Http;
using NativeEndpoints;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Publishing.Api.Endpoints;

/// <summary>
/// The Publishing owner's established error shape: RFC 7807 fields plus the FastEndpoints-era
/// <c>traceId</c> and <c>errors</c> extensions, written as <c>application/problem+json</c>.
/// </summary>
/// <remarks>
/// <see cref="ErrorCode"/> is additive (issue #1699): it is populated only for the coded publishing
/// failures <see cref="WorkflowPublishingFaultRenderer"/> renders, and omitted from the wire entirely
/// for every uncoded problem, so existing clients see no difference.
/// </remarks>
internal sealed record WorkflowPublishingLegacyProblem(
    string Type,
    string Title,
    int Status,
    string Detail,
    string Instance,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ErrorCode,
    string TraceId,
    IReadOnlyList<WorkflowPublishingLegacyProblemError> Errors);

internal sealed record WorkflowPublishingLegacyProblemError(string Name, string Reason);

/// <summary>
/// The single builder and writer for <see cref="WorkflowPublishingLegacyProblem"/>, shared by the
/// <see cref="IEndpointProblemWriter"/> path (no <c>errorCode</c>) and
/// <see cref="WorkflowPublishingFaultRenderer"/>'s coded-failure arm (with an <c>errorCode</c>), so the
/// shape is assembled and serialized in exactly one place.
/// </summary>
internal static class WorkflowPublishingLegacyProblems
{
    private const string ProblemJsonMediaType = "application/problem+json";

    public static WorkflowPublishingLegacyProblem Build(HttpContext context, EndpointProblem problem, string? errorCode = null)
    {
        var errors = problem.Errors
            .SelectMany(entry => entry.Value.Select(reason => new WorkflowPublishingLegacyProblemError(entry.Key, reason)))
            .ToArray();
        return new WorkflowPublishingLegacyProblem(
            LegacyProblemType(problem.StatusCode),
            LegacyProblemTitle(problem.StatusCode),
            problem.StatusCode,
            errors.FirstOrDefault()?.Reason ?? string.Empty,
            context.Request.Path,
            errorCode,
            context.TraceIdentifier,
            errors);
    }

    public static Task WriteAsync(HttpContext context, WorkflowPublishingLegacyProblem payload)
    {
        var typeInfo = WorkflowsPublishingJsonOptions.WireContext.WorkflowPublishingLegacyProblem;
        return Results.Json(payload, typeInfo, statusCode: payload.Status, contentType: ProblemJsonMediaType).ExecuteAsync(context);
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

internal sealed class WorkflowPublishingProblemWriter : IEndpointProblemWriter
{
    public Task WriteAsync(HttpContext context, EndpointProblem problem) =>
        WorkflowPublishingLegacyProblems.WriteAsync(context, WorkflowPublishingLegacyProblems.Build(context, problem));
}
