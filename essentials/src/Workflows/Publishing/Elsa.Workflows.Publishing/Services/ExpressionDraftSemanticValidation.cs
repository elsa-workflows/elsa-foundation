using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Publishing.Services;

public static class ExpressionDraftSemanticValidation
{
    /// <summary>The stable code every Unavailable outcome reports. Callers match on it; the cause goes to the log.</summary>
    public const string UnavailableCode = "expression-validation-unavailable";

    public static async ValueTask<ExpressionDraftValidationResult> ValidateSafelyAsync(
        IExpressionDraftSemanticValidator validator,
        WorkflowDefinitionState state,
        string documentScope,
        CancellationToken cancellationToken,
        ILogger? logger = null)
    {
        try
        {
            return await validator.ValidateAsync(state, documentScope, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or
            StackOverflowException or
            AccessViolationException))
        {
            // Unavailable is reported to callers as a stable code with no diagnostics: the safe diagnostic
            // subset deliberately excludes provider internals, and a validator's exception message is exactly
            // that. The log is the channel that carries the cause, so the fault is recoverable from the server
            // rather than only by editing this file.
            logger?.LogError(
                exception,
                "Expression draft validation failed for document scope {DocumentScope}; publication is blocked and reported as '{Code}'",
                documentScope,
                UnavailableCode);
            return new(
                ExpressionDraftValidationState.Unavailable,
                [],
                UnavailableCode);
        }
    }
}
