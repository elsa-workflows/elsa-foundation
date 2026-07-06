using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Services;

/// <summary>
/// Validates the activity tree of a workflow definition being submitted: every node must carry a
/// non-empty node id and an activity version id. Shared by the EF Core and Groundwork
/// <c>ISubmitWorkflowDefinitionCommand</c> implementations, which previously carried this walk
/// verbatim (issue #417 item 5).
/// </summary>
public static class SubmittedActivityTreeValidator
{
    /// <param name="rootActivity">The root activity node of the submitted definition state.</param>
    /// <param name="activityStructureService">Used to project each node's children for the walk.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when the root is null, or any node has an empty node id or activity version id.
    /// </exception>
    public static void Validate(ActivityNode? rootActivity, IActivityStructureService activityStructureService)
    {
        if (rootActivity is null)
            throw new ArgumentException("Workflow definition state must specify a root activity.", nameof(rootActivity));

        var stack = new Stack<ActivityNode>();
        stack.Push(rootActivity);

        while (stack.Count > 0)
        {
            var node = stack.Pop();

            if (string.IsNullOrWhiteSpace(node.NodeId))
                throw new ArgumentException("Activity node id cannot be empty.", nameof(rootActivity));

            if (string.IsNullOrWhiteSpace(node.ActivityVersionId))
                throw new ArgumentException($"Activity node '{node.NodeId}' must specify an activity version id.", nameof(rootActivity));

            foreach (var child in activityStructureService.ProjectChildren(node).SelectMany(slot => slot.Activities))
                stack.Push(child);
        }
    }
}
