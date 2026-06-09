using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Persistence.EFCore.Extensions;

public static class WorkflowDefinitionDraftStateExtensions
{
    public static WorkflowDefinitionState WithMutatedActivity(this WorkflowDefinitionState state, string nodeId, Func<ActivityNode, ActivityNode> mutate)
    {
        var activities = state.Activities
            .Select(a => a.NodeId == nodeId ? mutate(a) : a)
            .ToArray();

        return state with { Activities = activities };
    }
}
