# Data Model: Runtime Value Evidence

## Runtime Value Evidence Level

Represents the ordered amount of runtime value evidence requested or allowed.

Fields:

- `value`: `Off | Metadata | DiagnosticSnapshot | Payload`
- `order`: numeric ordering used for host-policy caps
- `displayName`: user-facing label
- `description`: concise UI explanation

Validation:

- Unknown values are invalid.
- `Payload` is valid only when Host Policy and permissions allow it.

## Runtime Evidence Subject

Represents a class of capturable runtime evidence.

Fields:

- `id`: stable API identifier such as `workflowInputs`, `workflowOutputs`, `activityInputs`, `activityOutputs`, `incidents`, `diagnostics`, `containerVariables`, or `durableValues`
- `displayName`: user-facing label
- `description`: subject explanation for settings UI
- `supportsDiagnosticSnapshot`: whether Diagnostic Snapshot capture is meaningful for the subject

Validation:

- Unknown subject identifiers are invalid.
- Subject-specific maximums cannot exceed the global Host Policy maximum.

## Workflow Runtime Diagnostics Setting

The Studio-managed requested setting for Workflows runtime diagnostics.

Fields:

- `defaultLevel`: requested Runtime Value Evidence Level
- `subjectOverrides`: optional map of subject id to requested level
- `updatedAt`: last settings update time when provided by backend
- `updatedBy`: safe user identifier when provided by backend

Relationships:

- Combined with Host Policy to produce Effective Runtime Diagnostics Setting.
- Applies prospectively to future capture events only.

Validation:

- `defaultLevel` is required.
- Each override subject must be known.
- Each override level must be known.

## Effective Runtime Diagnostics Setting

Backend-computed setting after applying Host Policy and permission constraints.

Fields:

- `defaultLevel`: effective Runtime Value Evidence Level
- `subjectOverrides`: optional map of subject id to effective level
- `limitationReasons`: displayable reasons explaining caps or disabled capabilities

Relationships:

- Derived from Workflow Runtime Diagnostics Setting and Host Policy.
- Used by runtime capture decisions.

Validation:

- Effective levels must be less than or equal to requested levels and Host Policy maximums.

## Host Policy

Deployment-owned maximum allowed evidence policy.

Fields:

- `maximumLevel`: global maximum Runtime Value Evidence Level
- `subjectMaximums`: optional map of subject id to lower maximum level
- `limitationReasons`: displayable reasons for Studio
- `snapshotLimits`: configured limits for Diagnostic Snapshot generation

Validation:

- `maximumLevel` is required.
- Subject maximums must not exceed the global maximum.
- Snapshot limits must be positive and bounded.

## Diagnostic Snapshot

A bounded, policy-sanitized representation of a runtime value.

Fields:

- `kind`: discriminant for node shape
- `typeName`: optional safe type label
- `displayName`: optional safe UI label
- `metadata`: optional safe display metadata
- kind-specific fields described in [contracts/diagnostic-snapshot.md](contracts/diagnostic-snapshot.md)

Relationships:

- Stored as the evidence payload for `DiagnosticSnapshot` capture mode.
- May contain child Diagnostic Snapshot nodes.
- May contain reserved Runtime Payload Reference leaves in future slices.

Validation:

- Total serialized size must not exceed Host Policy limits.
- Redacted values must not include original values.
- Error messages must be safe and must not include payload values.
- Unknown future kinds must remain renderable as unsupported nodes.

## Activity Execution Value Evidence

Captured value evidence attached to an activity execution.

Fields:

- `name`: input or output name
- `subject`: `ActivityInput` or `ActivityOutput` for the first Studio inspector slice
- `level`: effective Runtime Value Evidence Level used at capture time
- `state`: `captured | notCaptured | metadataOnly | permissionHidden | unavailable`
- `type`: optional runtime value type descriptor
- `capturedAt`: capture timestamp
- `snapshot`: Diagnostic Snapshot when `level` is `DiagnosticSnapshot`
- `captureReason`: reason produced by policy
- `isSensitive`: whether source metadata marked the value sensitive
- `metadata`: safe display metadata

Relationships:

- Belongs to one activity execution inspection.
- Rendered by the workflow run Activity inspector.

Validation:

- `snapshot` must be present only when state is `captured` and level is `DiagnosticSnapshot`.
- Full raw payload must not be included for Diagnostic Snapshot evidence.
- Permission-hidden evidence must not include value material.

## Runtime Payload Reference

Reserved protected pointer to payload material not stored inline.

Fields:

- `referenceKind`: `secret | blob | artifact | external`
- `referenceId`: stable opaque reference identifier
- `displayName`: safe display label
- `contentType`: optional safe MIME type
- `size`: optional byte size
- `resolution`: current resolution availability and reason

Relationships:

- May appear as a Diagnostic Snapshot leaf in a future provider-backed slice.

Validation:

- Provider internals, filesystem paths, connection strings, storage keys, and protected values must not be exposed.
- Resolution requires separate permissions and audit in a future slice.

## State Transitions

Workflow Runtime Diagnostics Setting:

```text
requested -> effective
requested changes -> future capture events only
existing evidence -> immutable
```

Activity Execution Value Evidence:

```text
not captured -> captured at runtime -> immutable inspection evidence
captured -> rendered if viewer has permission
captured -> permissionHidden at API/UI read time when viewer lacks access
```
