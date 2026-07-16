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
    public const string CancellationReason = "runtime.cancellationReason";
    public const string ChildExecutableNodeId = "runtime.childExecutableNodeId";
    public const string ChildFaulted = "runtime.childFaulted";
    public const string CommandId = "runtime.commandId";
    public const string CommandKind = "runtime.commandKind";
    public const string CompletedChildActivityExecutionId = "runtime.completedChildActivityExecutionId";
    public const string CreateBookmarkSchedulerWorkItemId = "runtime.createBookmarkSchedulerWorkItemId";
    public const string ExecutableArtifactHash = "runtime.executableArtifactHash";
    public const string ExecutableArtifactId = "runtime.executableArtifactId";
    public const string ExecutableArtifactVersion = "runtime.executableArtifactVersion";
    public const string ExecutableNodeId = "runtime.executableNodeId";
    public const string FaultInnerMessage = "runtime.faultInnerMessage";
    public const string FaultInnerType = "runtime.faultInnerType";
    public const string FaultMessage = "runtime.faultMessage";
    public const string FaultStackTrace = "runtime.faultStackTrace";
    public const string FaultSubStatus = "runtime.faultSubStatus";
    public const string FaultType = "runtime.faultType";
    public const string FlowchartExecutionScopeId = "flowchart.executionScopeId";
    public const string IncidentId = "runtime.incidentId";
    public const string InvokeReason = "runtime.invokeReason";
    public const string InvokeSchedulerWorkItemId = "runtime.invokeSchedulerWorkItemId";
    public const string InvokeSkipped = "runtime.invokeSkipped";
    public const string OutputName = "runtime.outputName";

    /// <summary>
    /// Passive correlation identifier threaded through stimulus routing (W7). Stamped as metadata on bookmarks,
    /// trigger bindings, and dispatch envelopes so a caller can scope a stimulus to a correlation without the
    /// engine owning a correlation subsystem. Absent when no correlation was supplied.
    /// </summary>
    public const string CorrelationId = "runtime.correlationId";

    /// <summary>
    /// Operational-state metadata key carrying the highest execution-ownership fencing token ever issued for a
    /// workflow execution (RT-2). Preserved across a lease release so a subsequent acquisition always issues a
    /// strictly greater token and a token is never reused. See
    /// <c>RuntimeExecutionOwnershipService</c> (engine package).
    /// </summary>
    public const string OwnershipFencingToken = "runtime.ownership.fencingToken";

    /// <summary>
    /// System-metadata key on a workflow execution carrying the workflow instance name (#260). Set by the
    /// <c>SetName</c> leaf control activity through <see cref="Elsa.Workflows.Runtime.Core.Contracts.IRuntimeActivityExecutionContext.SetInstanceName"/>;
    /// the engine folds it into the activity-completed checkpoint's workflow-execution state change, mirroring
    /// how the correlation id is persisted. Absent when no name has been assigned.
    /// </summary>
    public const string InstanceName = "runtime.instanceName";

    /// <summary>
    /// Metadata key on a durable value carrying a workflow identity slot (correlation id / instance name) for the
    /// execution-time expression carrier (ADR 0030 identity), tagged with the slot name (<c>"correlationId"</c> or
    /// <c>"instanceName"</c>). A <c>Correlate</c>/<c>SetName</c> leaf projects the assigned value into a durable
    /// value under a reserved <c>identity:</c> value id, so any activity invocation — including a concurrent sibling
    /// branch — re-lists it and populates <c>getCorrelationId()</c> / <c>getWorkflowInstanceName()</c> without a
    /// per-invocation workflow-execution-state read (spec 083 review). Distinct from <see cref="InstanceName"/>,
    /// which is the persisted system-metadata home read for querying; both are written in the same commit.
    /// </summary>
    public const string IdentityName = "runtime.identityName";

    /// <summary>
    /// Metadata key on a durable value carrying a workflow variable value. Its presence marks the durable
    /// value as a persisted workflow variable and its value is the
    /// variable name, mirroring how <see cref="OutputName"/> tags activity-output durable values. Read by
    /// <c>RuntimeInputBindingStateProjection.ProjectWorkflowVariables</c> (engine package)
    /// to rebuild the <c>variables.*</c> snapshot for input materialization.
    /// </summary>
    public const string VariableName = "runtime.variableName";

    /// <summary>
    /// Metadata key on a durable value carrying a workflow input value. Its presence marks the durable value
    /// as a persisted workflow input and its value is the input name, mirroring <see cref="OutputName"/>. Read
    /// by <c>RuntimeInputBindingStateProjection.ProjectWorkflowInputs</c> (engine package)
    /// to rebuild the <c>input.*</c> snapshot for input materialization.
    /// </summary>
    public const string InputName = "runtime.inputName";
    /// <summary>
    /// Metadata key on the durable value carrying the start stimulus payload (spec 089 A). Its value is the
    /// reserved slot name <c>RuntimeWorkflowStateSeed.StimulusInputSlotName</c>. This is a dedicated channel,
    /// deliberately distinct from <see cref="InputName"/>: the stimulus payload never shares the workflow-input
    /// namespace, so it cannot collide with an author-declared input and cannot be injected through the
    /// caller-facing inputs bag of the execute API. Read by
    /// <c>RuntimeInputBindingStateProjection.ProjectStimulusInput</c> (engine package).
    /// </summary>
    public const string StimulusInputName = "runtime.stimulusInputName";
    /// <summary>
    /// Metadata key on the durable value carrying the executable node id of the trigger that started this
    /// execution (spec 089 D). Its value is the reserved slot name
    /// <c>RuntimeWorkflowStateSeed.TriggerNodeIdSlotName</c>. Like <see cref="StimulusInputName"/> this is a
    /// dedicated, spoof-proof channel — deliberately distinct from <see cref="InputName"/>: the trigger-node
    /// identity never shares the workflow-input namespace, so it cannot collide with an author-declared input
    /// and cannot be injected through the caller-facing inputs bag of the execute API. A mid-flow HttpEndpoint
    /// receives it as typed invocation identity to tell whether it is the node that triggered this run and so
    /// should complete rather than suspend. Read by
    /// <c>RuntimeInputBindingStateProjection.ProjectTriggerNodeId</c>. Absent for direct (non-stimulus) runs.
    /// </summary>
    public const string TriggerNodeId = "runtime.triggerNodeId";
    /// <summary>
    /// Metadata key marking a durable value's payload as sensitive (<c>"true"</c>). Read by the workflow-output
    /// read projection (#254 Seam R1): <c>RuntimeWorkflowOutputStateProjection</c> (engine package)
    /// threads it as <c>IsSensitive</c> into the <c>IRuntimePayloadCapturePolicy</c> consult, so a sensitive-marked
    /// output surfaces on the read edge as an explicit redacted marker even under a payload-capturing policy.
    /// Nothing writes this key yet — <c>SetWorkflowOutput</c> carries no sensitivity channel — it is the reserved
    /// read-side contract for when a producer does.
    /// </summary>
    public const string IsSensitive = "runtime.isSensitive";

    /// <summary>Compatibility metadata used when a role-owned envelope is projected from legacy durable-value state.</summary>
    public const string RequiresEncryption = "runtime.requiresEncryption";

    /// <summary>Compatibility metadata used when a role-owned envelope is projected from legacy durable-value state.</summary>
    public const string RedactionMode = "runtime.redactionMode";

    /// <summary>Compatibility metadata used when a role-owned envelope is projected from legacy durable-value state.</summary>
    public const string RetentionPolicy = "runtime.retentionPolicy";

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
