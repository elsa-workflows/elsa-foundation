using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Persistence.EFCore.Extensions;

public static class WorkflowDefinitionDraftStateExtensions
{
    public static WorkflowDefinitionState WithMutatedActivity(this WorkflowDefinitionState state, string nodeId, Func<ActivityNode, ActivityNode> mutate, IActivityStructureService activityStructureService)
    {
        return state with { RootActivity = Mutate(state.RootActivity, nodeId, mutate, activityStructureService) };
    }

    private static ActivityNode? Mutate(ActivityNode? node, string nodeId, Func<ActivityNode, ActivityNode> mutate, IActivityStructureService activityStructureService)
    {
        if (node is null)
            return null;

        var updated = node.NodeId == nodeId ? mutate(node) : node;
        var childProjections = activityStructureService.ProjectChildren(updated);
        if (childProjections.Count == 0)
            return updated;

        var updatedProjections = childProjections
            .Select(slot => slot with
            {
                Activities = slot.Activities
                    .Select(activity => Mutate(activity, nodeId, mutate, activityStructureService)!)
                    .ToArray()
            })
            .ToArray();

        return activityStructureService.ReplaceChildren(updated, updatedProjections);
    }
}
