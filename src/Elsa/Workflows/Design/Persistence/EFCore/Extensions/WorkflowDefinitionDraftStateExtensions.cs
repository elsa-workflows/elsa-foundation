using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Persistence.EFCore.Extensions;

public static class WorkflowDefinitionDraftStateExtensions
{
    public static WorkflowDefinitionState WithMutatedActivity(this WorkflowDefinitionState state, string nodeId, Func<ActivityNode, ActivityNode> mutate)
    {
        return state with { RootActivity = Mutate(state.RootActivity, nodeId, mutate) };
    }

    private static ActivityNode? Mutate(ActivityNode? node, string nodeId, Func<ActivityNode, ActivityNode> mutate)
    {
        if (node is null)
            return null;

        var updated = node.NodeId == nodeId ? mutate(node) : node;
        if (updated.ChildSlots is null)
            return updated;

        var childSlots = updated.ChildSlots
            .Select(slot => slot with
            {
                Activities = slot.Activities
                    .Select(activity => Mutate(activity, nodeId, mutate)!)
                    .ToArray()
            })
            .ToArray();

        return updated with { ChildSlots = childSlots };
    }
}
