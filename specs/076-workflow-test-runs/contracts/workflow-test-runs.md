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
