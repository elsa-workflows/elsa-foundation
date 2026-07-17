using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Constants;

/// <summary>Stable wire and authoring identifiers owned by the DispatchWorkflow activity family.</summary>
public static class DispatchWorkflowConstants
{
    public const string ActivityType = "Elsa.DispatchWorkflow";
    public const string WorkflowDefinitionOptionsKey = "DispatchWorkflow.WorkflowDefinitions";
    public const string PinnedTargetMetadataKey = "Elsa.DispatchWorkflow.PinnedTarget";
    public const string StartChildIntentKind = WorkflowDispatchLifecycle.StartChildIntentKind;
    public const string CancelChildIntentKind = "Elsa.Activities.DispatchWorkflow.CancelChild";
    public const string ResumeParentIntentKind = WorkflowDispatchLifecycle.ResumeParentIntentKind;
    public const string WaitStimulusType = "Elsa.Activities.DispatchWorkflow.ChildCompleted";
    public const string ParentResumeProviderId = "runtime.dispatch-workflow";
    public const string CompletionResumeTargetId = "resume-target:dispatch-workflow-completed";
}
