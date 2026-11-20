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
| `Digest` | The stored digest seen at read time. **Only for a node whose lost-update mode is `Fault`**; absent in the default mode (light tracking). |

## ReadSet (per invocation / per attempt)

| Part | Lifetime | Notes |
|---|---|---|
| `InputReadSet` | Committed with the input snapshot; travels with background admission; immutable for the invocation (bases the invocation's own writes advanced are recorded against it). | Reads made during input materialization. |
| `SessionReads` | Per attempt; dropped on a new attempt. | Reads made by the body through the variable session. |

- A visible-but-unread variable is absent. Only Elsa's instrumented access paths are covered.

## LostUpdateMode (per node, pinned)

| Value | Set by | Tracking | On a stale base |
|---|---|---|---|
| `Default` | — | light: version only | apply (last write wins) + lost-update warning |
| `Fault` | activity type declaration, or per-node setting | exact: version, then stored digest on a mismatch | digests equal → apply + record mismatch; differ → fault |

- Resolved at design time from the activity type and the node's setting, pinned in the published executable.

## Base

Derived, not stored separately: for a write to X, the version of X in the writer's read set (plus its digest for a
`Fault` node), from the entry the write was built on. Absent → blind write. Input bases are immutable per invocation;
the invocation's own applied write advances its own base for X.

## WriteHistoryRecord

One per applied write and per lost update. Mandatory metadata always; value and operational data conditional.

| Field | Required? | Meaning |
|---|---|---|
| `VariableRef` | yes | Scope + key. |
| `Writer` | yes | Node, invocation, attempt. |
| `Source` | yes | capture / intrinsic / session change / alteration / background result. |
| `BaseVersion`, `NewVersion` | yes where applicable | For ordering and lost-update display. |
| `CommitOrder` | yes | Instance commit order. |
| `LostUpdate` | yes for lost updates | Outcome `Warning` or `Fault`; the overwritten writer and the stale-base writer. |
| `Value` | optional | Full historical value; configurable capture + retention; protected/redacted; omitted/redacted/expired distinguished from `null`. |
| `OperationalData` | conditional | Rejected-completion data (`Fault`), and the value a lost-update warning overwrote, retained while required for recovery or repair, independent of inspection-value settings. |

- Invariant: every intermediate write is preserved, including `0 → 1 → 0` within one buffered segment. Deriving from the
  final folded state only is insufficient. Physical storage is Open question B.
- The lost-update warning is also an instance diagnostic shown in Studio; the write-history record is its durable form.
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
├─ no  → apply (blind; D-8 last commit wins)                       → write history (blind)
└─ yes → base.version == current.version?
         ├─ yes → apply                                             → write history
         └─ no  → LOST UPDATE (stale base); node mode?
                  ├─ Default → apply (last write wins, D-8)          → lost-update warning: instance diagnostic
                  │                                                    + write history, both writers, overwritten
                  │                                                    value kept (0→1→0 also warns)
                  └─ Fault   → base.digest == current.digest?
                               ├─ yes → apply (value returned)        → write history (version mismatch)
                               └─ no  → fault → incident (rejected completion recorded)
then, for an applied write: new.digest == current.digest → apply nothing, version unchanged
```

Identical for frames and rows, inline and background, captures and session writes.

## Operator actions (recovery vs. operator)

| Action | InvocationId | Inputs | Body |
|---|---|---|---|
| Recovery (crash redelivery, resume) | kept | kept snapshot + read set | not re-run if durably completed |
| Retry | kept | kept snapshot | new attempt runs |
| Re-execute / reschedule | **new** | **fresh** materialization, new input read set | runs; effects repeat |
| Accept recorded completion (after a `Fault` lost update) | kept | n/a | **not run**; recorded transition applied atomically after revalidation |
