using Elsa.Api.AspNetCore;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Validations.Core.Exceptions;
using Elsa.Workflows.Design.Validations.Core.Models;
using Microsoft.AspNetCore.Http;
using NativeEndpoints;

namespace Elsa.Workflows.Design.Api.Endpoints;

/// <summary>
/// Maps design domain exceptions onto response status codes.
/// </summary>
/// <remarks>
/// This replaces the per-endpoint try/catch ladders that were repeated across the request, command,
/// and no-content dispatch helpers. The mapping is domain knowledge, so it lives with the module that
/// defines these exceptions rather than in the shared endpoint layer.
/// </remarks>
internal sealed class WorkflowDesignExceptionTranslator : IEndpointExceptionTranslator
{
    public EndpointProblem? Translate(Exception exception) => exception switch
    {
        DraftHasValidationErrorsException validation => new(
            StatusCodes.Status409Conflict,
            ErrorsByPath(validation.Errors, generalError: validation.Message)),
        // The credential-literal refusal (spec 188, FR-008) is the caller's content, so it is a 400 with each finding
        // keyed by its input's path. It derives from ArgumentException, so it must match before that arm.
        CredentialLiteralRefusedException refusal => new(StatusCodes.Status400BadRequest, ErrorsByPath(refusal.Findings)),
        EntityNotFoundException => EndpointProblem.General(StatusCodes.Status404NotFound, exception.Message),
        WorkflowDefinitionVersionConflictException or
            WorkflowPromotionOperationConflictException or
            WorkflowDraftChangedException or
            WorkflowDefinitionNotSoftDeletedException =>
            EndpointProblem.General(StatusCodes.Status409Conflict, exception.Message),
        PermanentDeletionUnavailableException => EndpointProblem.General(StatusCodes.Status501NotImplemented, exception.Message),
        ArgumentException => EndpointProblem.General(StatusCodes.Status400BadRequest, exception.Message),
        _ => null
    };

    private static Dictionary<string, string[]> ErrorsByPath(IEnumerable<ValidationError> errors, string? generalError = null)
    {
        var byPath = errors
            .GroupBy(error => string.IsNullOrWhiteSpace(error.Path) ? "generalErrors" : error.Path, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(error => error.Message).ToArray(), StringComparer.Ordinal);
        if (generalError is not null)
            byPath.TryAdd("generalErrors", [generalError]);
        return byPath;
    }
}
