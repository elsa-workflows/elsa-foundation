using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Validations.Validators;

/// <summary>
/// Baseline validator. Detects workflows without a root activity. Composite activity start
/// semantics are owned by the composite activity, not by workflow-level start flags.
/// </summary>
public sealed class StartActivityValidator : IDraftValidator
{
    public ValueTask<IEnumerable<ValidationError>> Validate(IWorkflowDefinitionDraft draft, CancellationToken cancellationToken)
    {
        if (draft.State.RootActivity is not null)
            return ValueTask.FromResult(Enumerable.Empty<ValidationError>());

        IEnumerable<ValidationError> errors =
        [
            new ValidationError(
                Path: ValidationPaths.Workflow,
                Type: "RootActivity/Missing",
                Message: "Workflow has no root activity."
            )
        ];

        return ValueTask.FromResult(errors);
    }
}
