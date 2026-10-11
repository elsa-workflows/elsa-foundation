# Contract: Input Evaluation Evidence

The existing `ActivityExecutionInspectionValueSnapshotView` remains wire-compatible. Input snapshots add identity and ordered materialization provenance without redefining current subject, capture mode, type, state, payload, or Diagnostic Snapshot semantics.

```json
{
  "name": "Recipient",
  "subject": "ActivityInput",
  "inputKey": "input-reference-key",
  "evaluationId": "replay-idempotency-key",
  "evaluationPhase": "BookmarkResume",
  "evaluationSequence": 2,
  "captureMode": "DiagnosticSnapshot",
  "state": "captured",
  "type": { "alias": "String" },
  "capturedAt": "2026-07-16T10:15:00Z",
  "payload": null,
  "snapshot": {
    "kind": "string",
    "preview": "safe bounded value",
    "length": 18,
    "truncated": false
  },
  "captureReason": "",
  "isSensitive": false,
  "accessState": "allowed",
  "failure": null,
  "metadata": {}
}
```

## Preserved wire semantics

- `subject` remains `ActivityInput` for input evidence.
- `captureMode` remains `None | MetadataOnly | DiagnosticSnapshot | Payload`.
- `state` remains `notCaptured | metadataOnly | captured | unavailable`.
- `type` remains `RuntimeValueTypeDescriptor`.
- `payload` and `snapshot` retain their current mutually selected API behavior.
- Runtime Payload References remain leaves inside a Diagnostic Snapshot under ADR 0009; they are not a capture mode and are not resolved by this feature.

## Evaluation identity and ordering

- `inputKey` is the input `ReferenceKey` within the activity execution's executable-node scope.
- `evaluationId` is derived from the scheduler work-item/idempotency identity, phase, activity execution, and input key. Replayed work reuses it.
- `evaluationSequence` is positive and monotonic per `(activityExecutionId, inputKey)`.
- Deduplication by `evaluationId` and sequence allocation occur atomically with checkpoint/state persistence.
- Timestamp is evidence and never substitutes for new-record identity.

## Evaluation phases

- `Invoke`: initial activity materialization.
- `BookmarkResume`: materialization when a bookmark resumes.
- `ParentCompletion`: materialization triggered after parent completion.
- `Other`: known extension phase carried with metadata.
- `Unknown`: missing/legacy or unrecognized provenance.

## Per-input materialization result

Materialization returns a result for every requested input. Successful values continue into the successful snapshot collection. A failure is sanitized and persisted before the activity faults:

```json
{
  "code": "input_evaluation_failed",
  "message": "The input expression could not be evaluated.",
  "incidentId": "inc-opaque"
}
```

No exception type, stack trace, source fragment, secret value, or unbounded payload is included.

## Authorization and compatibility

- Evidence access requires `workflow-runtime.read`, independently of `workflow-publishing.read` source access.
- When denied, payload, snapshot, payload-reference leaves, failure/diagnostic message, and metadata are omitted server-side; identity and `accessState: "permissionHidden"` may remain.
- Missing `inputKey`, evaluation ID, phase, sequence, or access state indicates legacy evidence. Studio may display a compatibility row but must not claim canonical source correlation.
