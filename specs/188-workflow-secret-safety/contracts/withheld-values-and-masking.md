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

The marker is `[secret:<reference name>]`. Values are matched ordinally in three forms: raw (as written), as the
default JSON encoder writes it, and as the relaxed JSON encoder (`UnsafeRelaxedJsonEscaping`) writes it. Other forms
are not matched: URL-encoded, base64, trimmed, case-changed or substring forms of a value pass through. Where two
registered values overlap only partly, the one-pass, longest-first replacement can leave a fragment of the shorter one
next to the marker of the other. `Exception.Data` is not carried into the masked exception at all. The lower bound is
one character: only an empty value is ignored, because an empty string cannot be told apart in text, while any
minimum length above one would let a short value through unmasked. The mask lives in the work item's DI scope and is
never persisted or logged by the default implementation.

As built (slice 8), where the code settled what the table leaves open:

- **Contract.** `IRuntimeSecretMask` is a replacement contract with a scoped default (`DefaultRuntimeSecretMask`); a
  second registration fails shell activation (`MultipleRuntimeSecretMasksException`), as the secret resolver's does.
  Besides `Register` and `Mask` it has `HasRegistrations` and `Release`, which the fault boundaries need: the first to
  decide whether to replace an exception, the second to release the values. Both stay on the contract (review round
  1). `HasRegistrations` cannot be derived from `Mask`, because an exception is replaced whenever a value is
  registered, whether or not its text contains one. `Release` is kept because the reviewers accepted releasing once
  the outcome is recorded rather than at scope disposal, and a replacement must see that call to honour it; releasing
  only through `DefaultRuntimeSecretMask` would leave a replacement holding values until the scope ends. The contract
  documents what a replacement owes: `HasRegistrations` answers `true` from the first non-empty registration until
  `Release`, and a replacement that answers `false` while holding a value turns masking off for that execution
  (fail-open); one that ignores `Release` holds values until the scope ends and masks no less.
- **Registration.** The activator's resolution step (`ActivitySecretInputResolver`) registers each value under
  `ActivityActivationRequest.ActivityExecutionId`, a member slice 8 adds, as soon as it resolves, so a later reference
  failing in the same activation is masked (T094).
- **When an exception is replaced.** Whenever any value is registered for the execution, whether or not its text
  contains one, because an exception can carry a value where the mask cannot see it. Two exceptions: an exception the
  activation-failure handler classifies (a missing storage driver, activity consumer or secret resolver) is handed on
  as it is, because its text is built from deployment identifiers and replacing it would turn a deployment problem
  that parks the activity into a fault; and the cancellation a cancellation arm rethrows is not replaced, so it stays a
  cancellation. When the arm's disposal also failed it throws an aggregate instead, which is not a cancellation and
  which the drainer records as a handler fault: the aggregate holds a masked copy of the cancellation (an
  `OperationCanceledException` for the same token, with the masked message and inner chain) and the masked disposal
  failures (review round 1; the arm still rethrows the original cancellation, unmasked, when no disposal failed).
- **Codes are not masked.** `SecretMaskedException` copies the failure code as it is, and a returned fault's code,
  category and fault type are kept as the activity set them (review round 1). Masking a short value that occurs in a
  code (a value `e` in `StoreUnavailable`) would corrupt the persisted code. A secret resolution failure code is
  validated as an ASCII code name (`RuntimeSecretResolution.Failure`); a code, category or fault type that other
  activity code chooses is persisted as it chose it, like its outputs.
- **Where.** In the invoke and resume handlers, every arm that records a thrown exception records through one method
  per handler (`RecordFaultAsync`), which masks the exception. The returned-fault arm masks `ActivityFault.Message` once
  before `ToNormalized` and records through the unmasked core (`RecordMaskedFaultAsync`), so the durable fault and the
  incident carry the same text: masking the exception built from the masked fault again would mask a value occurring
  inside the inserted marker (value `api` against reference `payments.api-key`). In the parent completion and parent
  notification handlers, every arm that records a thrown exception records through `RecordParentFaultAsync`, which
  masks it, and the returned-fault arm masks the fault once and builds its request from that masked fault directly.
  When the parent completion handler has no checkpoint committer it rethrows the masked exception instead of recording.
- **Lifetime.** Each handler releases the execution's values once it has recorded the outcome, not at lease disposal:
  every fault arm disposes the lease before it records, and the activator disposes the lease of a failed activation
  before the handler sees the failure (the T094 case), so values released at disposal would be gone exactly when needed.
- **Short values.** Only an empty value is ignored. Every non-empty value is masked however short: a minimum length
  would let a short secret through silently, while masking a one-character value only makes the text hard to read.
- **Log lines.** No runtime log line on these paths includes the recorded exception today; T073 renders the exception
  the boundary hands on through `RecordingLogger` to prove that one that did would show the marker. The canary
  (slice 9) captures every log category in a full host.

Known limits of slice 8, recorded and not fixed (review round 1):

- The activation-failure pass-through is keyed on exception type: `ActivityActivationFailureHandler.Classify` matches
  `ActivityResolutionException` among others, a public, unsealed type, so activity code that throws it (or a subclass)
  carrying a value bypasses masking and is recorded as an activation failure with its text as written.
- `SecretMaskedException` does not carry `IActivityFaultCausation`. Only the graph recovery exception implements it
  today, and graph activities cannot bind secrets (`VF-ACT-012`), so no masked exception loses a causation it had.
- The span test pins the activity fault path only (`FaultIncidentExecutionTests`); the drainer's handler-fault catch
  is left to slice 9's injected scenario S9.

Not covered by masking in phase 0 (spec assumption): text an activity writes to the console or its own logger, values
it returns as outputs, and values it places in private state or bookmark payloads. For the built-ins, publish refuses a
`Secret` binding on every input found returned, copied or persisted (research R3a IP14 to IP22), so the remaining
cases are `WriteLine.Text` on the console and a server that reflects `SendHttpRequest`'s `Authorization` value in its
response (research R17).
