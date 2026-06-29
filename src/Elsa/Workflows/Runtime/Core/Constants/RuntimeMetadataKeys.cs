namespace Elsa.Workflows.Runtime.Core.Constants;

public static class RuntimeMetadataKeys
{
    public const string WorkflowStartedAt = "runtime.workflowStartedAt";
    public const string CompletionOutcomeNames = "runtime.completionOutcomeNames";
    public const string CheckpointRequirement = "runtime.checkpointRequirement";
    public const string CheckpointRequirementMandatory = "Mandatory";
    public const string ActivityExecutionId = "runtime.activityExecutionId";
    public const string BookmarkId = "runtime.bookmarkId";
    public const string CheckpointReason = "runtime.checkpointReason";
    public const string ChildExecutableNodeId = "runtime.childExecutableNodeId";
    public const string CommandId = "runtime.commandId";
    public const string CommandKind = "runtime.commandKind";
    public const string CompletedChildActivityExecutionId = "runtime.completedChildActivityExecutionId";
    public const string CreateBookmarkSchedulerWorkItemId = "runtime.createBookmarkSchedulerWorkItemId";
    public const string ExecutableArtifactHash = "runtime.executableArtifactHash";
    public const string ExecutableArtifactId = "runtime.executableArtifactId";
    public const string ExecutableArtifactVersion = "runtime.executableArtifactVersion";
    public const string ExecutableNodeId = "runtime.executableNodeId";
    public const string FaultMessage = "runtime.faultMessage";
    public const string FaultSubStatus = "runtime.faultSubStatus";
    public const string FaultType = "runtime.faultType";
    public const string FlowchartExecutionScopeId = "flowchart.executionScopeId";
    public const string IncidentId = "runtime.incidentId";
    public const string InvokeReason = "runtime.invokeReason";
    public const string InvokeSchedulerWorkItemId = "runtime.invokeSchedulerWorkItemId";
    public const string InvokeSkipped = "runtime.invokeSkipped";
    public const string OutputName = "runtime.outputName";

    /// <summary>
    /// Metadata key on a durable value carrying a workflow variable value. Its presence marks the durable
    /// value as a persisted workflow variable (rather than an activity output capture) and its value is the
    /// variable name, mirroring how <see cref="OutputName"/> tags activity-output durable values. Read by
    /// <see cref="Elsa.Workflows.Runtime.Core.Services.RuntimeInputBindingStateProjection.ProjectWorkflowVariables"/>
    /// to rebuild the <c>variables.*</c> snapshot for input materialization.
    /// </summary>
    public const string VariableName = "runtime.variableName";

    /// <summary>
    /// Metadata key on a durable value carrying a workflow input value. Its presence marks the durable value
    /// as a persisted workflow input and its value is the input name, mirroring <see cref="OutputName"/>. Read
    /// by <see cref="Elsa.Workflows.Runtime.Core.Services.RuntimeInputBindingStateProjection.ProjectWorkflowInputs"/>
    /// to rebuild the <c>input.*</c> snapshot for input materialization.
    /// </summary>
    public const string InputName = "runtime.inputName";
    public const string ParentActivityExecutionId = "runtime.parentActivityExecutionId";

    /// <summary>
    /// Metadata key on a container activity execution carrying the JSON snapshot of its
    /// container-scoped variable values (ADR 0027, #210). The single source of truth for a concrete
    /// container execution's variable values: written when the scope mutates and read to rebuild the
    /// scope for descendant input evaluation and on resume.
    /// </summary>
    public const string ScopedVariableValues = "runtime.scopedVariableValues";

    /// <summary>
    /// Set on a container activity execution's state when its scope has completed (ADR 0027, #210):
    /// the scope's values are no longer live for runtime expressions, and the rebuilt
    /// <see cref="Elsa.Expressions.Core.Models.VariableScope"/> is marked completed.
    /// </summary>
    public const string ScopedVariableScopeCompleted = "runtime.scopedVariableScopeCompleted";

    /// <summary>
    /// Set by a loop owner (<c>For</c>/<c>ForEach</c>/<c>While</c>/<c>Do</c>, #264–#267) in the body
    /// child's scheduling-provenance metadata: the loop owner's executable node id, used as the declaring
    /// scope id of the per-iteration variable scope (#259, ADR 0028) the runtime layers onto the body.
    /// </summary>
    public const string LoopIterationOwnerNodeId = "runtime.loop.iterationOwnerNodeId";

    /// <summary>The authored name / reference key of the loop's per-iteration current-item variable.</summary>
    public const string LoopIterationItemName = "runtime.loop.iterationItemName";

    /// <summary>The JSON-encoded current-item value the loop publishes for this pass.</summary>
    public const string LoopIterationItemValue = "runtime.loop.iterationItemValue";

    /// <summary>Optional authored name / reference key of the loop's zero-based iteration-index variable.</summary>
    public const string LoopIterationIndexName = "runtime.loop.iterationIndexName";

    /// <summary>The JSON-encoded zero-based iteration index the loop publishes for this pass.</summary>
    public const string LoopIterationIndexValue = "runtime.loop.iterationIndexValue";

    public const string ParentCompletionReason = "runtime.parentCompletionReason";
    public const string ParentCompletionSchedulerWorkItemId = "runtime.parentCompletionSchedulerWorkItemId";
    public const string PinnedArtifactId = "runtime.pinnedArtifactId";
    public const string Reason = "runtime.reason";
    public const string ResumeReason = "runtime.resumeReason";
    public const string ResumeSchedulerWorkItemId = "runtime.resumeSchedulerWorkItemId";
    public const string ResumeTargetId = "runtime.resumeTargetId";
    public const string ScheduleReason = "runtime.scheduleReason";
    public const string SchedulerWorkItemId = "runtime.schedulerWorkItemId";
    public const string StartReason = "runtime.startReason";
    public const string StartSchedulerWorkItemId = "runtime.startSchedulerWorkItemId";
    public const string StimulusHash = "runtime.stimulusHash";
    public const string StimulusType = "runtime.stimulusType";
    public const string SuspendReason = "runtime.suspendReason";
}
