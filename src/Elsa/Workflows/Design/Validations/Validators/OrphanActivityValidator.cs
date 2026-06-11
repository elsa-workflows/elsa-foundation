using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Validations.Validators;

/// <summary>
/// Baseline validator. Detects orphaned child activities inside activity-owned graph slots
/// state. The workflow root activity itself is never an orphan.
/// </summary>
public sealed class OrphanActivityValidator : IDraftValidator
{
    public ValueTask<IEnumerable<ValidationError>> Validate(IWorkflowDefinitionDraft draft, CancellationToken cancellationToken)
    {
        var errors = new List<ValidationError>();
        CheckComposition(draft.State.RootActivity, errors);

        return ValueTask.FromResult<IEnumerable<ValidationError>>(errors);
    }

    private static void CheckComposition(ActivityNode? owner, ICollection<ValidationError> errors)
    {
        if (owner?.ChildSlots is null)
            return;

        var childSlots = owner.ChildSlots.ToArray();
        var connections = (owner.ConnectionSlots ?? [])
            .SelectMany(slot => slot.Connections)
            .ToList();
        foreach (var node in childSlots.SelectMany(slot => slot.Activities))
        {
            var hasInbound = connections.Any(c => c.Target.ActivityNodeId == node.NodeId);
            var hasOutbound = connections.Any(c => c.Source.ActivityNodeId == node.NodeId);
            if (connections.Count == 0 || hasInbound || hasOutbound)
            {
                CheckComposition(node, errors);
                continue;
            }
            
            errors.Add(new ValidationError(
                Path: node.NodeId,
                Type: "Graph/OrphanActivity",
                Message: $"Activity '{node.NodeId}' has no inbound or outbound connection."
            ));

            CheckComposition(node, errors);
        }
    }
}
