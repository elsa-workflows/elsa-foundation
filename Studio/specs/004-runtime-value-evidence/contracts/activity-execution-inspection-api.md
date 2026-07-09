# Contract: Activity Execution Inspection Value Evidence

## Purpose

This contract refines the activity execution inspection response used by the Workflow Instance / Run details Activity inspector. It extends the existing inspection endpoint with explicit Diagnostic Snapshot evidence for activity inputs and outputs.

## Existing Endpoint

`GET /runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}`

Studio currently calls this endpoint through `getActivityExecutionInspection` in `src/Elsa.Studio.Workflows/Client/src/api/workflows.ts`.

## Response Shape

The existing activity execution metadata remains backward-compatible. Value evidence is returned in `valueSnapshots` or the implementation's successor field if backend naming changes during implementation.

```json
{
  "activityExecutionId": "actexec-1",
  "workflowExecutionId": "wfexec-1",
  "executableNodeId": "node-write-line",
  "authoredActivityId": "activity-write-line",
  "activityType": "Elsa.WriteLine",
  "activityTypeVersion": "1",
  "status": "Completed",
  "subStatus": null,
  "executionSequence": 3,
  "scheduledAt": "2026-07-09T07:30:00Z",
  "startedAt": "2026-07-09T07:30:01Z",
  "completedAt": "2026-07-09T07:30:01Z",
  "outcomeNames": [],
  "bookmarks": [],
  "incidents": [],
  "valueSnapshots": [
    {
      "name": "Text",
      "subject": "ActivityInput",
      "captureMode": "DiagnosticSnapshot",
      "state": "captured",
      "type": {
        "displayName": "String",
        "typeName": "System.String",
        "alias": "string"
      },
      "capturedAt": "2026-07-09T07:30:01Z",
      "snapshot": {
        "kind": "string",
        "typeName": "String",
        "preview": "Hello from runtime",
        "length": 18,
        "truncated": false
      },
      "captureReason": "Diagnostic snapshot captured by runtime diagnostics policy.",
      "isSensitive": false,
      "metadata": {}
    }
  ],
  "metadata": {}
}
```

## Backward Compatibility

- Older responses may omit `state` and `snapshot`.
- Older responses may include `payload` when `captureMode` is `Payload`.
- Studio must continue to render metadata-only and omitted evidence without treating missing fields as fatal.
- Diagnostic Snapshot mode must not rely on raw `payload` display.

## Evidence States

- `captured`: evidence is present and can be rendered.
- `notCaptured`: policy did not capture this value.
- `metadataOnly`: only metadata is available.
- `permissionHidden`: evidence exists but current user cannot view it.
- `unavailable`: evidence cannot be loaded or is malformed.

## Subject Handling

The first Studio inspector slice renders:

- `ActivityInput`
- `ActivityOutput`

Other subjects may be present but should be summarized or ignored until a dedicated UI exists.

## Error Handling

- Endpoint failure must keep the Activity metadata section visible and show a value evidence unavailable message.
- Malformed snapshot nodes must render as unsupported/error nodes.
- Unknown future node kinds must not break the Activity inspector.
