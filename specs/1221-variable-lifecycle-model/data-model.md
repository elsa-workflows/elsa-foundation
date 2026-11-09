# Data Model: Variable lifecycle model

Entities the lifecycle adds or changes. Field lists are logical, not storage layouts; the physical shape of the write
history is Open question B.

## VariableVersion (per variable, in a frame or row)

Carried in `VariableFrameState` (and the row store), one per variable.

| Field | Meaning |
|---|---|
| `Version` | Per-variable counter; incremented on each applied write that changes the value. |
| `Digest` | Value digest over ADR 0091's canonical form, computed once per applied write. |

- Rules: computed only when a write is applied; independent of other variables in the frame; the frame revision stays
  for ordering and is not used for conflict detection.
- Upcast: a variable with no stored version reads as version 0 with no digest (old records).

## ReadSetEntry

One actual, instrumented read.

| Field | Meaning |
|---|---|
| `VariableRef` | Scope + key of the variable read. |
| `Version` | The version seen at read time. |
| `Digest` | The digest seen at read time. |

## ReadSet (per invocation / per attempt)

| Part | Lifetime | Notes |
|---|---|---|
| `InputReadSet` | Committed with the input snapshot; travels with background admission; immutable for the invocation (bases the invocation's own writes advanced are recorded against it). | Reads made during input materialization. |
| `SessionReads` | Per attempt; dropped on a new attempt. | Reads made by the body through the variable session. |

- A visible-but-unread variable is absent. Only Elsa's instrumented access paths are covered.

## Base

Derived, not stored separately: for a write to X, the (version, digest) of X in the writer's read set (the entry the
write was built on). Absent → blind write. Input bases are immutable per invocation; the invocation's own applied write
advances its own base for X.

## WriteHistoryRecord

One per applied write and per conflict. Mandatory metadata always; value and operational data conditional.

| Field | Required? | Meaning |
|---|---|---|
| `VariableRef` | yes | Scope + key. |
| `Writer` | yes | Node, invocation, attempt. |
| `Source` | yes | capture / intrinsic / session change / alteration / background result. |
| `BaseVersion`, `NewVersion` | yes where applicable | For ordering and conflict display. |
| `CommitOrder` | yes | Instance commit order. |
| `Resolution` | yes for conflicts | `Fault` or `LastWins`. |
| `Value` | optional | Full historical value; configurable capture + retention; protected/redacted; omitted/redacted/expired distinguished from `null`. |
| `OperationalData` | conditional | Base snapshot / rejected-completion data retained while an enabled merge or recovery needs it, independent of inspection-value settings. |

- Invariant: every intermediate write is preserved, including `0 → 1 → 0` within one buffered segment. Deriving from the
  final folded state only is insufficient. Physical storage is Open question B.
- Feeds S-6 (#3290–#3292). Not ADR 0084's commit journal.

## InputSnapshot / Evaluation (changed)

The immutable materialized inputs of an invocation (ADR 0045 §3). A **refresh** before background admission creates a
new *evaluation*: same `InvocationId`, its own sequence number and idempotency keys, a new input read set; it supersedes
and links to the prior snapshot with a reason, and the superseded snapshot stays in the history. The snapshot the worker
receives is immutable thereafter.

## State transitions — the commit rule

For a write to X at commit:

```
has base?
├─ no  → apply (blind; D-8 last commit wins)            → write history (blind)
└─ yes → base.version == current.version?
         ├─ yes → apply                                  → write history
         └─ no  → base.digest == current.digest?
                  ├─ yes → apply (value returned, 0→1→0) → write history (version mismatch)
                  └─ no  → LOST UPDATE
                           ├─ policy Fault    → incident (rejected completion recorded)
                           └─ policy LastWins → apply     → write history (LastWins)
```

Identical for frames and rows, inline and background, captures and session writes.

## Operator actions (recovery vs. operator)

| Action | InvocationId | Inputs | Body |
|---|---|---|---|
| Recovery (crash redelivery, resume) | kept | kept snapshot + read set | not re-run if durably completed |
| Retry | kept | kept snapshot | new attempt runs |
| Re-execute / reschedule | **new** | **fresh** materialization, new input read set | runs; effects repeat |
| Accept recorded completion | kept | n/a | **not run**; recorded transition applied atomically after revalidation |
