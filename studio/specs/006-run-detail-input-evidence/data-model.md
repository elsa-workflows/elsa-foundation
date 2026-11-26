# Data Model: Run Detail Input Evidence Workbench

## 1. PinnedInputSource

Studio-facing join of two immutable records: non-behavioral authored provenance from the Run's pinned Source Reference and behavioral compiled binding from its content-addressed executable.

| Field | Type | Meaning |
|---|---|---|
| `inputKey` | string? | `InputDefinition.ReferenceKey` within `executableNodeId`; null only for legacy records. |
| `executableNodeId` | string | Node scope for the ReferenceKey. |
| `inputName` | string | Display name at pin time. |
| `declaredType` | string? | Declared input type at pin time. |
| `authored` | AuthoredInputSource? | Frozen Source Reference sidecar representation. |
| `compiled` | RuntimeInputBindingView? | Structured executable runtime binding. |
| `isSensitive` | bool | End-to-end sensitivity marker. |
| `accessState` | allowed / redacted / permissionHidden / unavailable | Independent source authorization/result. |
| `unavailableReason` | string? | Safe machine-readable reason. |

### AuthoredInputSource

| Field | Type | Meaning |
|---|---|---|
| `kind` | expression / literal / variable / unknown | Authored semantic category. |
| `expressionType` | string? | Open Elsa expression type discriminator. |
| `value` | JsonElement? | Safe opaque source value when authorized; stored/re-emitted verbatim. |
| `displayHint` | string? | Non-authoritative renderer hint. |
| `metadata` | object | Extensible non-secret metadata. |

### RuntimeInputBindingView

Discriminated union retaining Literal, Expression, ActivityOutput, DurableValue, and Reference variants. It projects compiled behavior. Studio treats an unrecognized future API discriminator as an Unknown presentation variant; Foundation does not persist arbitrary unknown CLR binding objects.

## 2. InputEvaluationEvidence

One materialization/capture event for one activity execution input.

| Field | Type | Meaning |
|---|---|---|
| `inputKey` | string? | Stable source/evidence join identity; null for legacy evidence. |
| `inputName` | string | Display name captured at evaluation time. |
| `evaluationId` | string? | Replay-idempotency key; null for legacy evidence. |
| `evaluationPhase` | Invoke / BookmarkResume / ParentCompletion / Other / Unknown | Materialization context. |
| `evaluationSequence` | integer? | Monotonic per activity execution and input. Null for legacy evidence. |
| `capturedAt` | timestamp | Evidence capture time. |
| `captureMode` | None / MetadataOnly / DiagnosticSnapshot / Payload | Existing evidence mechanism, unchanged. |
| `state` | notCaptured / metadataOnly / captured / unavailable | Capture result. |
| `type` | RuntimeValueTypeDescriptor? | Existing alias-based runtime type descriptor. |
| `snapshot` | DiagnosticSnapshot? | Bounded safe tree. |
| `payload` | JSON? | Existing full-payload field only when capture mode/policy/authorization allow it. |
| `captureReason` | string? | Safe capture-policy reason. |
| `isSensitive` | bool | Propagated authored/runtime sensitivity. |
| `accessState` | allowed / redacted / permissionHidden / unavailable | Independent evidence authorization/result. |
| `failure` | InputEvaluationFailure? | Safe per-input failure projection. |
| `metadata` | object | Extensible non-secret provenance. |

### InputEvaluationFailure

| Field | Type | Meaning |
|---|---|---|
| `code` | string | Stable safe diagnostic code. |
| `message` | string | Sanitized operator-facing message. |
| `incidentId` | string? | Correlation identifier, never stack trace or secret. |

## 3. InputInspectionRow

Derived in Studio from declared inputs, executable sources, and runtime evidence.

| Field | Type | Derivation |
|---|---|---|
| `rowKey` | string | Stable key when available; namespaced compatibility key otherwise. |
| `inputKey` | string? | Canonical key only when trustworthy. |
| `name` | string | Preferred declaration, source, then evidence display name. |
| `declaredType` | string? | Declaration/source type. |
| `declaration` | object? | Declared activity input. |
| `source` | ExecutableInputSource? | Zero or one canonical source. |
| `evaluations` | InputEvaluationEvidence[] | Deterministically ordered evidence. |
| `latestEvaluation` | InputEvaluationEvidence? | Highest sequence; timestamp fallback only for legacy set. |
| `states` | InputInspectionState[] | Explicit missing/anomaly/compatibility flags. |

### InputInspectionState

`missingDeclaration`, `missingBinding`, `sourceUnavailable`, `compiledOnly`, `notEvaluated`, `orphanEvidence`, `unknownKey`, `legacyIdentity`, `duplicateKey`, `redacted`, `permissionHidden`, `metadataOnly`, `unavailable`, `unsupported`, `truncated`, `evaluationFailed`.

### Join rules

1. Canonical records within the selected executable node/activity execution with nonblank `inputKey` join only by exact ReferenceKey.
2. Duplicate source keys remain separate anomaly rows; do not silently choose a source.
3. Legacy records without keys may be grouped by exact captured name only into a clearly labelled compatibility row; this is never presented as canonical provenance.
4. Records present on only one side remain visible.
5. Evaluation order is sequence ascending when sequences exist. Mixed/legacy sets use sequence first, then timestamp, then stable arrival order and expose `legacyIdentity`.

## 4. RunWorkbenchLayoutState

| Field | Type | Rules |
|---|---|---|
| `containerWidth` | number | Observed workbench width. |
| `mode` | desktop / medium / narrow | Derived from container/canvas viability. |
| `desktopInspectorWidth` | number | Run-specific persisted value; clamp to 340..min(640, 50% container, container - 10 - 480). |
| `inspectorCollapsed` | bool | Desktop rail state. |
| `narrowView` | canvas / inspector | Mutually exclusive narrow surface. |
| `selectedActivityId` | string? | Selecting an activity opens inspector in medium/narrow. |

## State transitions

- Mode thresholds: desktop ≥830px; medium 480–829px; narrow <480px.
- Activity selected → desktop: inspector remains current collapsed/expanded state; medium: modal drawer opens and focuses its heading/close control; narrow: view becomes inspector.
- Container crosses desktop → medium: persist desktop width; remove resize semantics; modal drawer follows selection.
- Container crosses medium → narrow: drawer becomes non-modal focus view without losing selected activity.
- Medium drawer closes by close button, Escape, or backdrop and restores focus to the selected canvas node. If selection disappears, close and restore focus to the canvas heading.
- Source/evidence refresh: retain row expansion by stable row key; discard expansion only when row no longer exists.
