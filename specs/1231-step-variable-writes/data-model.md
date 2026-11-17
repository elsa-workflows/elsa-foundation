# Data model: spec 1231 (S-7)

Types S-10 (#4316) builds are reused and only referenced here: `VariableReadSetEntry`, `VariableInputReadSet`,
`VariableWriteBase`, `VariableCommitRule`, `LostUpdateMode`, `VariableWriteRecord` (`Source = SessionChange`).

## StepVariableSession (per step, in memory)

| Field | Meaning |
|---|---|
| `InvocationId`, `AttemptId` | The step it belongs to. Dropped at the step's transition. |
| `SessionReads` | Ordered `VariableReadSetEntry` list for this attempt (version; digest under `Fault`). Not carried to a new attempt or across a suspension. |
| `PendingIntents` | One `WriteIntent` per variable, latest wins within the step. |
| `View` | Read-your-writes: committed value at first read, overlaid with pending intents. |

## WriteIntent

| Field | Meaning |
|---|---|
| `Variable` | Scope (frame) and key, resolved at publish (script, bound reference) or at run time (free name). |
| `Kind` | `WholeAssignment` or `MemberChange` (script only; changed paths from S-8). Decides the no-op equality (C4). |
| `Value` | The whole value: base plus changes (C3). |
| `Base` | `VariableWriteBase?` chosen per research R-3; null = blind. |
| `Writer` | Node, invocation, attempt. |
| `DroppedMembers` | C# only: members the read type dropped, for the FR-017 warning. |

Applied in the step's checkpoint commit before captures. Each produces a `VariableWriteRecord` (applied, no-op,
lost update with warning, or fault).

## InvocationOwnBases (persisted with the invocation)

| Field | Meaning |
|---|---|
| `Variable` → `VariableWriteBase` | The base an own applied write advanced to (ADR 0092 decision 4). Persisted with the activity execution state; restored on resume. |

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
