# Contracts: Activity Execution Inspection

## Runtime API Contract

### Get workflow instance details

Purpose: return lightweight instance evidence for graph and timeline summaries.

Request:

```http
GET /runtime/workflows/instances/{workflowExecutionId}
```

Response behavior:

- Returns workflow instance summary, activity execution summaries, and incident summaries.
- Activity execution summaries include execution count and status evidence needed for graph aggregation.
- Full value snapshots are not loaded by default.

### Get activity execution detail

Purpose: return committed inspection evidence for one selected activity execution.

Request:

```http
GET /runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}
```

Response `200 OK`:

```json
{
  "activityExecutionId": "actexec-1",
  "workflowExecutionId": "wfexec-1",
  "executableNodeId": "node-a",
  "authoredActivityId": "activity-a",
  "activityType": "test/activity",
  "activityTypeVersion": "1.0.0",
  "status": "Completed",
  "subStatus": null,
  "executionSequence": 42,
  "scheduledAt": "2026-06-25T08:00:00Z",
  "startedAt": "2026-06-25T08:00:01Z",
  "completedAt": "2026-06-25T08:00:02Z",
  "firstCheckpointId": "checkpoint:schedule:actexec-1",
  "lastCheckpointId": "checkpoint:complete:actexec-1",
  "lastCommittedAt": "2026-06-25T08:00:02Z",
  "provenance": {
    "parentActivityExecutionId": "actexec-flowchart",
    "schedulingActivityExecutionId": "actexec-previous",
    "schedulingWorkflowExecutionId": "wfexec-1",
    "branchId": null,
    "iterationId": "node-a:2",
    "executionPathId": "path:12",
    "executionScopeId": "scope:9",
    "schedulingCause": "continuation",
    "metadata": {}
  },
  "outcomeNames": ["Done"],
  "bookmarks": [],
  "incidents": [],
  "valueSnapshots": [
    {
      "name": "Message",
      "subject": "ActivityInput",
      "captureMode": "MetadataOnly",
      "type": {
        "typeName": "System.String"
      },
      "capturedAt": "2026-06-25T08:00:01Z",
      "payload": null,
      "captureReason": "Default metadata-only activity input capture",
      "isSensitive": false,
      "metadata": {}
    }
  ],
  "metadata": {}
}
```

Response behavior:

- `404 Not Found` when the workflow execution or activity execution does not exist.
- Payload fields are null unless payload capture policy allowed payload capture.
- The endpoint resolves only within the selected workflow execution in the first slice.

## Runtime Store Contract

### IActivityExecutionInspectionStore

Purpose: read current inspection projection per concrete activity execution.

Operations:

- `FindAsync(string workflowExecutionId, string activityExecutionId)`
- `ListSummariesAsync(string workflowExecutionId)`

### IActivityExecutionInspectionWriter

Purpose: persist current inspection projection per concrete activity execution from the checkpoint writer lane.

Operations:

- `SaveAsync(ActivityExecutionInspectionProjection projection)`

Rules:

- Store key is `(workflowExecutionId, activityExecutionId)`.
- `ListSummariesAsync(workflowExecutionId)` returns committed inspection summaries for an instance without loading snapshot payloads.
- Storage providers must preserve deterministic ordering fields.

## Checkpoint Contract

`RuntimeCheckpointStateChangeSet` includes:

```text
ActivityExecutionInspections: IReadOnlyCollection<RuntimeStateChange<ActivityExecutionInspectionProjection>>
```

Rules:

- State id must match `ActivityExecutionInspectionProjection.ActivityExecutionId`.
- Inspection changes are written by checkpoint writers only when the checkpoint is persisted.
- Scheduler-boundary handlers use post-commit scheduler intents for dependent scheduler work.

## Studio Consumer Contract

Selection rules:

- Graph node summary groups activity executions by `authoredActivityId`.
- Graph node badge shows execution count and aggregate status summary.
- Selecting a graph node lists concrete activity executions for that authored activity.
- Selecting a concrete activity execution lazy-loads the activity execution detail endpoint.
- Selecting an incident highlights the linked activity execution and its authored node when available.
