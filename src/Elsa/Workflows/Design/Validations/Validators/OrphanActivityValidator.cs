using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Validations.Validators;

/// <summary>
/// Baseline validator. Detects orphaned child activities inside activity-owned composition
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
        if (owner?.Composition is not { } composition)
            return;

        var connections = composition.Connections.ToList();
        var startActivityNodeId = composition.StartActivityNodeId;

        foreach (var node in composition.Activities)
        {
            var hasInbound = connections.Any(c => c.Target.ActivityNodeId == node.NodeId);
            var hasOutbound = connections.Any(c => c.Source.ActivityNodeId == node.NodeId);
            if (hasInbound || hasOutbound || string.Equals(startActivityNodeId, node.NodeId, StringComparison.Ordinal))
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
