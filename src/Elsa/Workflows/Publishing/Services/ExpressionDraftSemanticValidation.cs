using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;

namespace Elsa.Workflows.Publishing.Services;

public static class ExpressionDraftSemanticValidation
{
    public static async ValueTask<ExpressionDraftValidationResult> ValidateSafelyAsync(
        IExpressionDraftSemanticValidator validator,
        WorkflowDefinitionState state,
        string documentScope,
        CancellationToken cancellationToken)
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
            // THE EXCEPTION USED TO VANISH HERE, AND WITH IT THE ONLY DESCRIPTION OF WHY PUBLISHING FAILED.
            // Callers turn Unavailable into a 503 whose detail reads "Expression validation is unavailable;
            // publication is blocked" and whose diagnostics array is empty - true, unactionable, and
            // indistinguishable from an unregistered validator. Nothing logged it either, so the only way to
            // learn the cause was to edit this file. Carry the exception into the code the caller reports.
            return new(
                ExpressionDraftValidationState.Unavailable,
                [],
                $"expression-validation-unavailable: {exception.GetType().Name}: {exception.Message}");
        }
    }
}
