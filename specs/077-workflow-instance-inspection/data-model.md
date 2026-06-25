# Data Model: Workflow Instance Inspection

## Workflow Instance

Represents one workflow execution selected for inspection.

Fields:
- `workflowExecutionId`: stable instance identifier.
- `status` / `subStatus`: current runtime state.
- `definitionId` / `definitionVersionId`: source definition identity.
- `artifactId` / `artifactVersion` / `artifactHash`: runnable artifact identity.
- `createdAt`, `startedAt`, `updatedAt`, `completedAt`: lifecycle timestamps.
- `correlationId`, `parentWorkflowExecutionId`, `tenantId`: optional context.
- `activityCount`, `incidentCount`: summary counters.

Relationships:
- Has many `Activity Execution Records`.
- Has many `Incidents`.
- References one `Workflow Definition Version Snapshot` by `definitionVersionId`.

## Workflow Definition Version Snapshot

Represents the authored workflow version used for inspection.

Fields:
- `id`: workflow definition version id.
- `version`: version label.
- `definition`: parent workflow definition summary.
- `state`: authored workflow state, including root activity.
- `layout`: designer metadata records keyed by activity node id.

Validation:
- Missing layout is allowed and treated as an empty layout.
- Missing version is surfaced as a non-blocking visualization failure while runtime evidence remains visible.

## Activity Execution Record

Represents one runtime activity execution summary row for the selected instance. Detailed per-execution evidence is owned by [Activity Execution Inspection](../079-activity-execution-inspection/spec.md).

Fields:
- `activityExecutionId`, `workflowExecutionId`.
- `executableNodeId`, `authoredActivityId`.
- `activityType`, `activityTypeVersion`.
- `status`, `subStatus`.
- `scheduledAt`, `startedAt`, `completedAt`.
- `executionSequence`, `firstCheckpointId`, `lastCheckpointId`.
- `schedulingActivityExecutionId`, `parentActivityExecutionId`.
- `branchId`, `iterationId`, `callStackDepth`.
- `bookmarkIds`, `incidentIds`.
- `faultCount`, `aggregateFaultCount`.
- `metadata`.

Relationships:
- Belongs to one `Workflow Instance`.
- May map to one graph node by `authoredActivityId`.
- May link to zero or more `Incidents`.
- May lazy-load one `Activity Execution Inspection Projection` by `activityExecutionId`.

## Incident

Represents runtime failure or blocking evidence for a workflow instance.

Fields:
- `incidentId`, `workflowExecutionId`.
- `activityExecutionId`, `executableNodeId`.
- `severity`, `status`, `resolutionAction`.
- `failureType`, `message`.
- `createdAt`, `resolvedAt`.
- `isBlocking`.
- `metadata`.

Relationships:
- Belongs to one `Workflow Instance`.
- May link to one `Activity Execution Record`.
- May map indirectly to one graph node through the linked activity execution.

## Instance Inspection Projection

Client-side composition used by Studio.

Fields:
- `instance`: selected workflow instance.
- `definitionVersion`: optional workflow definition version snapshot.
- `activities`: ordered activity execution records.
- `incidents`: ordered incidents.
- `selectedNodeId`: optional graph selection.
- `selectedActivityExecutionId`: optional activity-history selection.
- `selectedIncidentId`: optional incident selection.

Rules:
- Runtime evidence must remain visible if definition visualization fails.
- Graph nodes should show aggregate status based on related activity records and incidents.
- Selecting graph, activity, or incident evidence updates the correlated selection state.
