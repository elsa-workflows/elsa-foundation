namespace Elsa.Activities.DispatchWorkflow.Runtime.Constants;

/// <summary>Stable wire and authoring identifiers owned by the DispatchWorkflow activity family.</summary>
public static class DispatchWorkflowConstants
{
    public const string ActivityType = "Elsa.DispatchWorkflow";
    public const string WorkflowDefinitionOptionsKey = "DispatchWorkflow.WorkflowDefinitions";
    public const string PinnedTargetMetadataKey = "Elsa.DispatchWorkflow.PinnedTarget";
    public const string StartChildIntentKind = "Elsa.Activities.DispatchWorkflow.StartChild";
    public const string CancelChildIntentKind = "Elsa.Activities.DispatchWorkflow.CancelChild";
    public const string ResumeParentIntentKind = "Elsa.Activities.DispatchWorkflow.ResumeParent";
    public const string WaitStimulusType = "Elsa.Activities.DispatchWorkflow.ChildCompleted";
    public const string CompletionResumeTargetId = "resume-target:dispatch-workflow-completed";
}
