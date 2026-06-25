# Contracts: Workflow Instance Inspection

## Backend API Contract

### Get workflow definition version details

Purpose: return the authored state and designer layout for a workflow definition version.

Request:

```http
GET /design/workflows/versions/{versionId}
```

Response `200 OK`:

```json
{
  "id": "01K...",
  "version": "1.0.0",
  "definition": {
    "id": "01K...",
    "name": "Order workflow",
    "description": "Routes an order",
    "createdAt": "2026-06-24T10:00:00Z",
    "lastModifiedAt": "2026-06-24T10:05:00Z"
  },
  "state": {
    "variables": [],
    "rootActivity": {
      "nodeId": "root",
      "activityVersionId": "flowchart-v1",
      "inputs": [],
      "outputs": [],
      "structure": {
        "kind": "elsa.flowchart.structure",
        "schemaVersion": "1.0",
        "payload": {
          "activities": []
        }
      }
    },
    "inputs": [],
    "outputs": []
  },
  "layout": [
    {
      "nodeId": "node-a",
      "x": 120,
      "y": 80,
      "width": null,
      "height": null,
      "additionalProperties": null
    }
  ]
}
```

Response behavior:
- Missing layout returns `"layout": []`.
- Missing version follows the existing endpoint error behavior for not-found entities.
- The response does not include runtime execution records.

### Get workflow instance details

Purpose: existing runtime-owned instance details contract remains the source for lightweight runtime evidence.

Request:

```http
GET /runtime/workflows/instances/{workflowExecutionId}
```

Response:
- Existing `instance`, `activities`, and `incidents` fields remain unchanged.
- No design state or layout is added to this contract in this feature.
- Activity rows are treated as summaries; detailed value snapshots and per-execution evidence are loaded from the Activity Execution Inspection detail endpoint introduced by spec 079.

### Get activity execution detail

Purpose: runtime-owned detail contract for one selected concrete activity execution.

Request:

```http
GET /runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}
```

Response:
- Returns committed activity execution inspection evidence from [Activity Execution Inspection](../../079-activity-execution-inspection/contracts/activity-execution-inspection.md).
- Payload visibility follows runtime payload capture policy.

## Studio UI Contract

### Route

```text
/workflows/instances/{workflowExecutionId}
```

Behavior:
- Loads runtime instance details by workflow execution id.
- Loads detailed activity execution evidence only when a concrete execution is selected.
- Loads workflow definition version details by `instance.definitionVersionId`.
- Loads the activity catalog needed to render the designer canvas.
- Displays a read-only graph when definition version state is available.
- Displays runtime summary, activity history, and incidents even if definition version visualization fails.

### Selection correlation

Selection rules:
- Selecting a graph node filters or highlights activity executions where `authoredActivityId` matches the node id and shows execution count/status aggregation.
- Selecting an activity execution highlights the graph node whose id matches `authoredActivityId`.
- Selecting an incident highlights the linked activity execution and graph node when the incident can be correlated.
- Unmatched activity executions and incidents remain visible in fallback sections.
