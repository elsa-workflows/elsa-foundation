using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Validations.Validators;

/// <summary>
/// Baseline validator (Unit C FR-033). Detects duplicate variable names within the
/// workflow-scope variable bag (<c>WorkflowDefinitionState.Variables</c>). Comparison is
/// case-insensitive (<c>MyVar</c> and <c>myvar</c> collide). One error per offending name,
/// regardless of how many definitions share it.
/// </summary>
public sealed class VariableUniquenessValidator : IDomainEventHandler<OnDraftValidating>
{
    public ValueTask Handle(OnDraftValidating domainEvent, CancellationToken cancellationToken)
    {
        var duplicates = domainEvent.Draft.State.Variables
            .GroupBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (var group in duplicates)
        {
            domainEvent.AddValidationError(new ValidationError(
                Path: $"$workflow/variables/{group.Key}",
                Type: "Variables/Uniqueness",
                Message: $"Variable name '{group.Key}' is declared {group.Count()} times (case-insensitive). Variable names must be unique within the workflow."
            ));
        }

        return ValueTask.CompletedTask;
    }
}
