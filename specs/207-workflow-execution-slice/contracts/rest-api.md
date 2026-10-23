# REST API Contract: Workflow Execution Vertical Slice

## Existing Design Calls

### Create Workflow Definition

`POST /design/workflows/definitions`

Request:

```json
{
  "name": "Monday Demo",
  "description": "Sequential WriteLine workflow"
}
```

Response includes:
- `definition.id`
- current draft state

### Create Workflow Version

`POST /design/workflows/versions`

Request:

```json
{
  "definitionId": "<definition-id>",
  "state": {
    "variables": [],
    "activityConnections": [],
    "activities": [],
    "inputs": [],
    "outputs": [],
    "workflowActivityOptions": null,
    "strategyOptions": null
  }
}
```

Response includes:
- `id`
- `version`
- `state`

## New Publishing Call

### Publish Workflow Version

`POST /publishing/workflows/{versionId}/publish`

Request body: empty JSON object or omitted.

Success response:

```json
{
  "artifactId": "artifact-...",
  "definitionId": "definition-...",
  "definitionVersionId": "version-...",
  "artifactVersion": "1.0.0",
  "artifactHash": "sha256:...",
  "nodeCount": 2,
  "edgeCount": 1,
  "startNodeIds": ["node-start"]
}
```

Failure response:
- Endpoint framework error response with diagnostic message.
- Message must identify missing version, unknown activity row, unsupported graph shape, or unsupported input binding.

## New Runtime Call

### Execute Published Artifact

`POST /runtime/workflows/{artifactId}/execute`

Request body: empty JSON object or omitted.

Success response:

```json
{
  "workflowExecutionId": "wfexec-...",
  "artifactId": "artifact-...",
  "status": "Completed",
  "startedAt": "2026-06-10T00:00:00Z",
  "completedAt": "2026-06-10T00:00:00Z",
  "activities": [
    {
      "activityExecutionId": "actexec-...",
      "executableNodeId": "node-start",
      "activityType": "Elsa.Activities.Primitives.Activities.WriteLine",
      "status": "Completed",
      "startedAt": "2026-06-10T00:00:00Z",
      "completedAt": "2026-06-10T00:00:00Z",
      "error": null
    }
  ],
  "error": null
}
```

Failure response:
- Unknown artifact id returns a deterministic not-found style diagnostic.
- Activity failure returns a `Faulted` execution result when execution starts and an activity throws.
