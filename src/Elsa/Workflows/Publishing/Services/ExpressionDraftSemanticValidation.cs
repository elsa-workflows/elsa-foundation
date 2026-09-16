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
            // Unavailable carries no diagnostics, so the code is the only channel left to describe the fault.
            // Without the exception type and message in it the caller reports a 503 that is true but says
            // nothing, and is indistinguishable from a validator that was never composed at all.
            return new(
                ExpressionDraftValidationState.Unavailable,
                [],
                $"expression-validation-unavailable: {exception.GetType().Name}: {exception.Message}");
        }
    }
}
