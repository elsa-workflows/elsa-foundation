namespace Elsa.Workflows.Design.Core.Models;



/// <param name="UsableAsActivity"> Used to decide if the workflow can be used as an activity. </param>
/// <param name="AutoUpdateConsumingWorkflows"> Used to decide if the consuming workflows should be updated automatically to use the last published version of the workflow when it is published. </param>
/// <param name="ActivityCategory"> The category to use when the workflow is used as an activity. </param>
public sealed record WorkflowActivityOptions(
    bool? UsableAsActivity,
    bool? AutoUpdateConsumingWorkflows,
    string? ActivityCategory,
    IEnumerable<string>? Outcomes = null
);
