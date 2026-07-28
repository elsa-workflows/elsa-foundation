using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;

namespace Elsa.Workflows.Publishing.Api.Services;

internal static class ExpressionDraftSemanticValidation
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
            return new(
                ExpressionDraftValidationState.Unavailable,
                [],
                "expression-validation-unavailable");
        }
    }
}
