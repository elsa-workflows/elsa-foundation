# Data Model: Activity Execution Inspection

## ActivityExecutionState

Lifecycle and continuation state for one concrete activity execution.

Additional/clarified fields:

- `executionSequence`: deterministic per-workflow ordering value for activity executions.
- `provenance`: minimal `ActivitySchedulingProvenance` used for summaries, incident correlation, and checkpoint behavior.
- `status` / `subStatus`: scheduled, running, suspended, completed, faulted, cancelled, recovered or equivalent lifecycle state.

Rules:

- One activity execution id belongs to one workflow execution id.
- Multiple activity execution states may reference the same authored activity id.
- Scheduler-boundary lifecycle changes are checkpointed before dependent scheduler work is enqueued.

## ActivityExecutionInspectionProjection

Runtime-owned read model for inspecting one committed activity execution.

Fields:

- `activityExecutionId`
- `workflowExecutionId`
- `executableNodeId`
- `authoredActivityId`
- `activityType`
- `activityTypeVersion`
- `status`
- `subStatus`
- `executionSequence`
- `scheduledAt`
- `startedAt`
- `completedAt`
- `firstCheckpointId`
- `lastCheckpointId`
- `lastCommittedAt`
- `provenance`
- `outcomeNames`
- `bookmarks`
- `incidents`
- `valueSnapshots`
- `metadata`

Rules:

- Projection identity is `(workflowExecutionId, activityExecutionId)`.
- One current projection exists per committed activity execution.
- Projection changes are durable only when included in a persisted runtime checkpoint.
- Projection must not contain design documents or designer layout.

## ActivitySchedulingProvenance

Runtime-owned correlation data explaining why an activity execution was scheduled.

Fields:

- `parentActivityExecutionId`: structural owner when scheduled by a parent/composite.
- `schedulingActivityExecutionId`: temporal scheduler or predecessor activity execution.
- `schedulingWorkflowExecutionId`: workflow execution that scheduled this activity execution when different from the current workflow.
- `branchId`
- `iterationId`
- `executionPathId`
- `executionScopeId`
- `schedulingCause`
- `metadata`

Rules:

- Structural parent and temporal scheduler are distinct.
- `executionPathId` and `executionScopeId` are generic composite-control-flow correlation values.
- `branchId` and `iterationId` are simpler grouping aliases available to non-Flowchart composites.
- Flowchart may populate `iterationId` from its loop iteration key.

## ActivityExecutionValueSnapshot

Policy-governed evidence for one activity input or output observed during one activity execution.

Fields:

- `name`
- `subject`: activity input or activity output.
- `captureMode`: none, metadata only, or payload.
- `type`
- `capturedAt`
- `payload`
- `captureReason`
- `isSensitive`
- `metadata`

Rules:

- Payload is present only when runtime payload capture policy allows payload capture.
- Metadata-only snapshots must not include payload values.
- Denied snapshots may record name/type/capture reason when allowed by policy.

## ActivityExecutionBookmarkSummary

Inspection summary for a bookmark owned by an activity execution.

Fields:

- `bookmarkId`
- `resumeTargetId`
- `stimulusType`
- `stimulusHash`
- `createdAt`
- `expiresAt`
- `metadata`
- optional policy-governed `payload`

Rules:

- Bookmark summaries are inspection evidence, not the authoritative bookmark state.
- Bookmark payload follows runtime payload capture policy.

## ActivityExecutionIncidentSummary

Inspection summary for an incident linked to an activity execution.

Fields:

- `incidentId`
- `severity`
- `status`
- `resolutionAction`
- `failureType`
- `message`
- `createdAt`
- `resolvedAt`
- `isBlocking`
- `metadata`
- optional policy-governed diagnostic payload

Rules:

- Incident summaries are inspection evidence, not the authoritative incident state.
- Stack traces and diagnostic payloads follow incident/history capture policy.

## RuntimeCheckpointStateChangeSet

Atomic runtime checkpoint state-change envelope.

Additional lane:

- `activityExecutionInspections`: upserts/deletes/appends for `ActivityExecutionInspectionProjection`.

Rules:

- Inspection projection changes commit atomically with related lifecycle state changes.
- A skipped checkpoint does not persist inspection projection changes.
- Scheduler-boundary checkpoints cannot be skipped when they gate dependent scheduler work.
