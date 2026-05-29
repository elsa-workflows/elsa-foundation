using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Validations.Validators;

/// <summary>
/// Baseline validator (Unit C FR-033). Detects root-level activities with no inbound and no
/// outbound <c>ActivityConnection</c>. The activity marked <c>IsStart</c> is excluded —
/// a start activity has no inbound by definition. Connections live on
/// <c>WorkflowDefinitionState.ActivityConnections</c> at the workflow level only; nested
/// child activities are container-driven, not connection-driven, and are not part of this
/// check.
/// </summary>
public sealed class OrphanActivityValidator : IDomainEventHandler<OnDraftValidating>
{
    public ValueTask Handle(OnDraftValidating domainEvent, CancellationToken cancellationToken)
    {
        var state = domainEvent.Draft.State;
        var connections = state.ActivityConnections.ToList();

        foreach (var node in state.Activities)
        {
            if (node.IsStart)
                continue;

            var hasInbound = connections.Any(c => c.Target.ActivityNodeId == node.NodeId);
            var hasOutbound = connections.Any(c => c.Source.ActivityNodeId == node.NodeId);

            if (hasInbound || hasOutbound)
                continue;

            domainEvent.AddValidationError(new ValidationError(
                Path: node.NodeId,
                Type: "Graph/OrphanActivity",
                Message: $"Activity '{node.NodeId}' has no inbound or outbound connection."
            ));
        }

        return ValueTask.CompletedTask;
    }
}
