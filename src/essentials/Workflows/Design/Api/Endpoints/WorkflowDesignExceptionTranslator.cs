using Elsa.Api.AspNetCore;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
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
        DraftHasValidationErrorsException validation => Validation(validation),
        EntityNotFoundException => EndpointProblem.General(StatusCodes.Status404NotFound, exception.Message),
        WorkflowDefinitionVersionConflictException or
            WorkflowPromotionOperationConflictException or
            WorkflowDefinitionNotSoftDeletedException =>
            EndpointProblem.General(StatusCodes.Status409Conflict, exception.Message),
        PermanentDeletionUnavailableException => EndpointProblem.General(StatusCodes.Status501NotImplemented, exception.Message),
        ArgumentException => EndpointProblem.General(StatusCodes.Status400BadRequest, exception.Message),
        _ => null
    };

    private static EndpointProblem Validation(DraftHasValidationErrorsException exception)
    {
        var errors = exception.Errors
            .GroupBy(error => string.IsNullOrWhiteSpace(error.Path) ? "generalErrors" : error.Path, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(error => error.Message).ToArray(), StringComparer.Ordinal);
        errors.TryAdd("generalErrors", [exception.Message]);
        return new(StatusCodes.Status409Conflict, errors);
    }
}
