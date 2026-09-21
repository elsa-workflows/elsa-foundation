using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Validations.Validators;

/// <summary>
/// Contributes known expression errors to the ordinary draft-validation lifecycle.
/// Tooling unavailability is intentionally left to consequential-operation policy.
/// </summary>
public sealed class ExpressionDraftValidator(IExpressionDraftSemanticValidator semanticValidator) : IDraftValidator
{
    public async ValueTask<IEnumerable<ValidationError>> Validate(
        IWorkflowDefinitionDraft draft,
        CancellationToken cancellationToken)
    {
        var result = await semanticValidator.ValidateAsync(draft.State, draft.Id, cancellationToken);
        if (result.State == ExpressionDraftValidationState.Valid)
            return [];

        var errors = result.Diagnostics
            .Where(diagnostic => diagnostic.Severity == ExpressionDiagnosticSeverity.Error)
            .Select(diagnostic => new ValidationError(
                diagnostic.AuthoredPath ?? "$workflow",
                $"Expressions/{diagnostic.Code}",
                diagnostic.Message))
            .ToList();
        if (errors.Count == 0)
        {
            errors.Add(new(
                "$workflow",
                $"Expressions/Validation/{result.State}",
                $"Expression validation did not produce a valid full-draft result ({result.State})."));
        }
        return errors;
    }
}
