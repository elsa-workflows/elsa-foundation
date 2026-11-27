using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Exceptions;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Publishing.Api.Endpoints;

internal static class ExpressionPublicationValidationProblems
{
    public static ExpressionPublicationValidationProblemDetails Create(
        ExpressionPublicationValidationException exception,
        HttpContext context) => new(
        $"https://elsa.dev/problems/{exception.Code}",
        "Workflow publication expression validation was rejected",
        StatusCode(exception.State),
        exception.Message,
        context.Request.Path.Value ?? string.Empty,
        exception.Code,
        context.TraceIdentifier,
        exception.State.ToString().ToLowerInvariant(),
        exception.Diagnostics.Select(diagnostic => new ExpressionPublicationValidationDiagnosticView(
            diagnostic.Code,
            diagnostic.Severity,
            diagnostic.Message,
            diagnostic.DocumentRevision,
            diagnostic.Range,
            diagnostic.AuthoredPath)).ToArray());

    public static async Task WriteAsync(
        HttpResponse response,
        ExpressionPublicationValidationProblemDetails problem,
        CancellationToken cancellationToken)
    {
        response.StatusCode = problem.Status;
        response.ContentType = "application/problem+json";
        await System.Text.Json.JsonSerializer.SerializeAsync(
            response.Body,
            problem,
            WorkflowsPublishingJsonContext.Default.ExpressionPublicationValidationProblemDetails,
            cancellationToken);
    }

    private static int StatusCode(ExpressionDraftValidationState state) => state switch
    {
        ExpressionDraftValidationState.Errors => StatusCodes.Status422UnprocessableEntity,
        ExpressionDraftValidationState.Unavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status409Conflict
    };
}
