# Data model: spec 1231 (S-7)

Types S-10 (#4316) builds are reused and only referenced here: `VariableReadSetEntry`, `VariableInputReadSet`,
`VariableWriteBase`, `VariableCommitRule`, `LostUpdateMode`, `VariableWriteRecord` (`Source = SessionChange`).

## StepVariableSession (per attempt, in memory; `Elsa.Activities.Runtime/Services/StepVariables/`)

| Field | Meaning |
|---|---|
| `AttemptId` | The attempt it belongs to; a new attempt and every resume get a fresh session. |
| `Reads` | Ordered `VariableSessionRead` list for this attempt (below). Not carried to a new attempt or across a suspension. |
| Pending intents | One `StepVariableWriteIntent` per variable, latest wins within the step. |
| View | Read-your-writes: committed value at first read, overlaid with pending intents. |
| `PendingFault` | A host-side fault (withheld value, undeclared name, failed type check) raised even when the body catches it. |

## VariableSessionRead (`Elsa.Workflows.Runtime.Core`)

| Field | Meaning |
|---|---|
| `AttemptId` | The attempt that read. |
| `Entry` | `VariableReadSetEntry`: scope, key, version read; the digest only under `Fault` (spec 1221 FR-003). |
| `Policy` | The variable's `ValueProtectionPolicy` (spec 1222 FR-035). |

Saved on the invocation as `ActivityExecutionState.SessionReads`, inspection data only: kept on completion,
suspension, fault and cancellation, cleared for every successor.

## StepVariableWriteIntent

| Field | Meaning |
|---|---|
| Target | Scope (frame), key and `Name`, resolved at publish (bound reference) or at run time (free name). |
| `Kind` | `WholeAssignment` (C#) or `MemberChange` (script, from Phase 5; changed paths from S-8). Decides the no-op equality (C4). |
| `Value` | The whole value: base plus changes (C3). |
| `Base` | `VariableWriteBase?` chosen per research R-3; null = blind. |
| `OwnerInvocationId` | The invocation whose own-write base the applied write advances. |
| `DroppedMembers` | C# only: members the read type dropped, for the FR-017 warning on the write record. |

The writer (node, invocation, attempt) is set
at commit (`StepVariableCommit`). Intents apply in the step's checkpoint commit before captures; each produces a
`VariableWriteRecord` with source `SessionChange` (applied, no-op, lost update with warning, or fault).

## Own-write bases (persisted with the invocation)

| Field | Meaning |
|---|---|
| `ActivityExecutionState.OwnVariableBases` | `VariableReadSetEntry` list: the version an own applied write advanced each variable to (ADR 0092 decision 4). Restored on resume and kept on retry; cleared on re-execution. |

## Pinned node facts (publish → executable node)

| Fact | Source | Read by |
|---|---|---|
| `MayWriteSet` | script analysis (FR-019) or writable bound references (FR-024) | concurrent-write rule, overlap warning, inspection |
| `BodyTouchSet` (`RunJavaScript`) | pinned reads, writes, and the fallback flag | inspection, concurrent-write rule |
| `AccessesUndeclaredVariables` | activity-type attribute, pinned in the contract | free-name gate, concurrent-write rule ("every visible variable"), C9 |
| Variable-reference inputs | contract input kind `VariableReference` with `Direction` (`Read`, `ReadWrite`) and value type | binding validation, C9 |
| Lost-update mode | S-10 (`[ActivityLostUpdateMode]`, node setting) | the commit rule |

## Activity-facing types

| Type | Shape |
|---|---|
| `VariableReference<T>` | Input type, read and write. Bound to a variable declaration reference. |
| `ReadOnlyVariableReference<T>` | Input type, read only. `Set` throws. |
| `AccessesUndeclaredVariablesAttribute` | On an activity type; enables free names. |
| `IStepVariables` (`context.Variables`) | `T Get<T>(VariableReference<T>)`, `T Get<T>(ReadOnlyVariableReference<T>)`, `void Set<T>(VariableReference<T>, T)`, `T Get<T>(string)`, `void Set<T>(string, T)` (free names: marked types only). |
