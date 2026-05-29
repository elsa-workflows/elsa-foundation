using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Validations.Validators;

/// <summary>
/// Baseline validator (Unit C FR-033). Detects workflows with zero or more than one
/// root-level activity marked <see cref="Core.Models.ActivityNode.IsStart"/>. Workflows
/// MUST have exactly one start. Nested child activities are container-driven; the start
/// concept is workflow-scoped, so the check is root-level only.
/// </summary>
public sealed class StartActivityValidator : IDomainEventHandler<OnDraftValidating>
{
    public ValueTask Handle(OnDraftValidating domainEvent, CancellationToken cancellationToken)
    {
        var startCount = domainEvent.Draft.State.Activities.Count(a => a.IsStart);

        if (startCount == 1)
            return ValueTask.CompletedTask;

        var message = startCount == 0
            ? "Workflow has no start activity. Exactly one activity must be marked as the start."
            : $"Workflow has {startCount} start activities. Exactly one activity must be marked as the start.";

        domainEvent.AddValidationError(new ValidationError(
            Path: "$workflow",
            Type: "Graph/StartActivity",
            Message: message
        ));

        return ValueTask.CompletedTask;
    }
}
