using Elsa.Api.AspNetCore;
using Microsoft.AspNetCore.Http;

namespace Elsa.Activities.Design.Api.Endpoints;

/// <summary>
/// The owner's historical mediator error transport: RFC 7807 fields plus the FastEndpoints-era
/// <c>traceId</c> and <c>errors</c> extensions, written as <c>application/problem+json</c>.
/// </summary>
internal sealed record ActivitiesDesignLegacyProblem(
    string Type,
    string Title,
    int Status,
    string Detail,
    string Instance,
    string TraceId,
    IReadOnlyList<ActivitiesDesignLegacyProblemError> Errors)
{
    internal const string ContentType = "application/problem+json";

    public static ActivitiesDesignLegacyProblem Create(HttpContext context, string detail, int statusCode, string? errorName = null) => new(
        ProblemType(statusCode),
        ProblemTitle(statusCode),
        statusCode,
        detail,
        context.Request.Path,
        context.TraceIdentifier,
        [new(errorName ?? "generalErrors", detail)]);

    public Task WriteAsync(HttpContext context) =>
        Results.Json(this, ActivitiesDesignJsonOptions.WireContext.ActivitiesDesignLegacyProblem, statusCode: Status, contentType: ContentType)
            .ExecuteAsync(context);

    internal static string ProblemType(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "https://www.rfc-editor.org/rfc/rfc7231#section-6.5.1",
        StatusCodes.Status404NotFound => "https://www.rfc-editor.org/rfc/rfc7231#section-6.5.4",
        StatusCodes.Status500InternalServerError => "https://www.rfc-editor.org/rfc/rfc7231#section-6.6.1",
        _ => "about:blank"
    };

    internal static string ProblemTitle(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "Bad Request",
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status500InternalServerError => "Internal Server Error",
        _ => "HTTP error"
    };
}

internal sealed record ActivitiesDesignLegacyProblemError(string Name, string Reason);

/// <summary>
/// Writes binder-produced problems — malformed or missing bodies and strict typed-query failures —
/// in the owner's legacy shape. Dispatch failures never reach this writer; they are owned end to end
/// by <see cref="ActivitiesDesignFaultRenderer"/>.
/// </summary>
internal sealed class ActivitiesDesignProblemWriter : IEndpointProblemWriter
{
    public Task WriteAsync(HttpContext context, EndpointProblem problem)
    {
        var errors = problem.Errors
            .SelectMany(entry => entry.Value.Select(reason => new ActivitiesDesignLegacyProblemError(entry.Key, reason)))
            .ToArray();
        var payload = new ActivitiesDesignLegacyProblem(
            ActivitiesDesignLegacyProblem.ProblemType(problem.StatusCode),
            ActivitiesDesignLegacyProblem.ProblemTitle(problem.StatusCode),
            problem.StatusCode,
            errors.FirstOrDefault()?.Reason ?? string.Empty,
            context.Request.Path,
            context.TraceIdentifier,
            errors);
        return payload.WriteAsync(context);
    }
}
