# Contract: Workflow Test Runs

## Start workflow test run

Starts a designer test run for an existing workflow definition version.

```http
POST /workflows/publishing/workflows/{versionId}/test-runs
```

### Path parameters

| Name | Description |
|---|---|
| `versionId` | Workflow definition version to capture and run for testing |

### Request body

The initial slice accepts an empty body. Future iterations may add workflow input values and designer-supplied correlation metadata.

### Accepted response

Status: `202 Accepted`

```json
{
  "testRunId": "testrun-...",
  "definitionId": "definition-1",
  "definitionVersionId": "version-1",
  "artifactId": "test-artifact-...",
  "workflowExecutionId": "wfexec-...",
  "status": "DispatchAccepted",
  "commandDispatchStatus": "Accepted",
  "reason": null,
  "expiresAt": "2026-06-24T12:00:00Z"
}
```

### Rejected response

Status: `400 Bad Request` for invalid workflow content or unknown source version.

```json
{
  "testRunId": "testrun-...",
  "definitionId": "definition-1",
  "definitionVersionId": "version-1",
  "artifactId": null,
  "workflowExecutionId": null,
  "status": "Rejected",
  "commandDispatchStatus": null,
  "reason": "Workflow version has no root activity to publish.",
  "expiresAt": null
}
```

### Contract notes

- Test-run artifacts are not returned by durable published executable listing.
- Normal runtime execute-by-artifact-id remains a production/durable execution path and does not accept transient test-run artifacts.
- Runtime execution receives a pinned executable identity; it does not load workflow design state.

## Start workflow draft snapshot test run

Starts a designer test run from a caller-supplied workflow draft snapshot without creating a durable workflow definition version.

```http
POST /publishing/workflows/drafts/test-runs
```

### Request body

| Name | Description |
|---|---|
| `definitionId` | Durable workflow definition identity that owns the draft/snapshot. |
| `snapshotId` | Non-durable caller-generated snapshot identity for this designer test input. |
| `state` | Workflow definition state snapshot to compile for this test run. |
| `artifactVersion` | Optional artifact version label; defaults to `draft`. |

```json
{
  "definitionId": "definition-1",
  "snapshotId": "designer-snapshot-1",
  "artifactVersion": "draft",
  "state": {
    "variables": [],
    "rootActivity": {},
    "inputs": [],
    "outputs": [],
    "workflowActivityOptions": null,
    "strategyOptions": null
  }
}
```

### Accepted response

Status: `202 Accepted`

Response shape remains `WorkflowTestRunView`. For draft snapshots, `definitionVersionId` is a synthetic source identity in the form `draft:{snapshotId}`. It is not a durable workflow definition version id and must not be used with version-history APIs.

```json
{
  "testRunId": "testrun-...",
  "definitionId": "definition-1",
  "definitionVersionId": "draft:designer-snapshot-1",
  "artifactId": "test-artifact-...",
  "workflowExecutionId": "wfexec-...",
  "status": "DispatchAccepted",
  "commandDispatchStatus": "Accepted",
  "reason": null,
  "expiresAt": "2026-06-24T12:00:00Z"
}
```

### Rejected response

Status: `400 Bad Request` for invalid workflow content.

Rejected responses also use `WorkflowTestRunView`; the synthetic `definitionVersionId` keeps the rejection correlated to the submitted snapshot without creating durable version history.
