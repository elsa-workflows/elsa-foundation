# Contract: withheld values and masking

Proposed behavior for FR-010 to FR-012. Decisions are in [research R3, R8 and R9](../research.md).

## Withheld envelope

- `ValuePresence.Withheld` is a fourth presence. A withheld envelope carries no inline, external or transient
  payload, and carries a `WithheldValue`.
- `WithheldValueKind.SecretReference`: produced by the materializer for every `SecretRead` binding. Carries the
  reference and the string-to-target conversion plan. Re-resolved at each activation.
- `WithheldValueKind.PolicyRequiresEncryption`: produced by `RuntimeExternalEnvelopeStorage.RewriteAsync` for any
  other present value whose effective policy requires encryption. Not recoverable. Activation refuses it with
  `VF-ACT-010`. This is a backstop: publish refuses literal and expression bindings on encryption-required inputs
  (`VF-ACT-011`), so only paths that skip publish (runtime artifact import, research R13) or future producers reach
  it.

## Surfaces and what they show

| Surface (FR-011) | Source of truth | Shows for a secret-bound input |
|---|---|---|
| Persisted activity execution state (`ContentJson`) | committed `ActivityExecutionState.InputSnapshot` | the withheld envelope |
| Persisted workflow instance state | `WorkflowExecutionState`, durable values | no secret value; `SecretRead` cannot target variables, outputs or graph boundary values, because publish refuses `Secret` bindings on intrinsics, graph activities and checkpoint participants (`VF-ACT-012`, research R12) |
| Execution evidence | `ExecutionEvidenceCheckpointEnricher` over commits | a withheld disposition with the reference name; `DescribeContent` never reads a value from a withheld envelope |
| Run inspector: activity inputs | `ActivityExecutionInspection.BuildInputValueSnapshots` | `isSensitive: true`, value absent, a withheld marker with the reference name |
| Run inspector: executable bindings | `WorkflowExecutableInspector` | the reference (name, type, scope) even though the binding is sensitive; references are not material |
| Diagnostic snapshots | `DefaultDiagnosticSnapshotFactory` via `ActivityExecutionInspection` and `RuntimeContainerVariableEvidence` | nothing from the value: the payload capture policy captures nothing for sensitive payloads, and a withheld envelope has no value to capture |

Each surface must tolerate `ValuePresence.Withheld` without throwing. A surface that throws on it would fault the
activity before activation, because `BuildInputValueSnapshots` runs on the invoke path before the activator.

## Commit backstop (FR-010)

`RuntimeCheckpointCommitValidator.Validate` refuses, with `RuntimeCheckpointCommitValidationException` and
`VF-ACT-005`, any commit containing a `ValueEnvelope` with `Presence == Present`, an inline or external payload, and
`Policy.RequiresEncryption == true`. It scans activity execution states (input snapshot, completion), durable
values and inspection projections. The message names the state id and value key, never the value.

## Masking (FR-012)

| Text source | Mechanism |
|---|---|
| Exception thrown from activation or activity code (invoke, resume, structural evaluation) | the handler's existing fault boundary replaces it with `SecretMaskedException` before recording, logging or tracing |
| Fault message and incident text | `IRuntimeFaultCapturePolicy.Capture` receives the masked exception; reports the original exception type name |
| Fault classification | not text, but it must survive masking: `SecretMaskedException` implements `IRuntimeFaultClassification` and copies `IsRetryable` and `FailureCode` from the wrapped exception, so a masked `StoreUnavailable` stays transient |
| Activity-authored `ActivityFault.Message` | masked before `ActivityFaultProjection.ToNormalized` |
| Runtime log lines that include the exception | receive the masked exception object |
| Runtime spans | carry the exception type only today (`WorkflowSchedulerDrainer`); a test pins that no exception message is added |

The marker is `[secret:<reference name>]`. Values are matched ordinally in raw and JSON-escaped form. The mask lives
in the work item's DI scope and is never persisted or logged.

Not covered by masking in phase 0 (spec assumption): text an activity writes to the console or its own logger, values
it returns as outputs, and values it places in private state or bookmark payloads. For the built-ins, publish refuses a
`Secret` binding on every input found returned, copied or persisted (research R3a IP14 to IP22), so the remaining
cases are `WriteLine.Text` on the console and a server that reflects `SendHttpRequest`'s `Authorization` value in its
response (research R17).
