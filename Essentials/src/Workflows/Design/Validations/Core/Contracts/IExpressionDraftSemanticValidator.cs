using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Validations.Core.Contracts;

public enum ExpressionDraftValidationState { Valid, Errors, Unavailable, Unauthorized, Incompatible, Stale, Canceled }

public sealed record ExpressionDraftValidationResult(ExpressionDraftValidationState State, IReadOnlyList<ExpressionDiagnostic> Diagnostics, string? Code = null);

/// <summary>Strict full-authored-state expression validation for consequential operations.</summary>
public interface IExpressionDraftSemanticValidator
{
    ValueTask<ExpressionDraftValidationResult> ValidateAsync(WorkflowDefinitionState state, string documentScope, CancellationToken cancellationToken);
}
