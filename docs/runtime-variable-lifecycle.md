# How variable reads and writes are judged

**Who this is for:** contributors working on the Elsa 4 runtime, and authors who place activities in parallel.

A write to a workflow variable is judged against the read it was built on. Nothing a writer relied on is overwritten
without a record. This page describes how the runtime does that. The decisions are in
[ADR 0092](adr/0092-variable-reads-and-writes-share-one-versioned-lifecycle.md); the requirements are in
[spec 1221](../specs/1221-variable-lifecycle-model/spec.md).

## Words you will meet

| Word | Meaning |
|---|---|
| Version | A per-variable counter. Every applied write that changes the value adds one. A variable nobody has written yet is at version 0. |
| Digest | A hash of the value's canonical JSON form, stored with the version when a write applies. A default-mode read never hashes; a `Fault` node that reads a variable nobody has written yet hashes its stored value once. A value stored outside the document is digested by its reference, so the same value at two references counts as different. |
| Input read set | The variables a step's input bindings and expressions actually read, with the version each one had. Committed with the step's input snapshot. |
| Base | The version a write was built on: the read-set entry for that variable. A write to a variable the step never read has no base and is a *blind* write. |
| Lost update | A write whose base is no longer the current version: someone else wrote the variable after this step read it. |
| Write history | One record per applied write and per lost update, kept on the writing step's activity execution state. |
| Commit sequence | The instance-wide number of the checkpoint that committed a write or a lifecycle transition. It orders writes across all frames and rows of the instance. |

## The commit rule

Every write site applies the same rule: an output capture (on completion or on resume), a `Set`/`Merge`/`Reduce`
intrinsic, and a `ModifyVariable` alteration. An intrinsic reads its value and commits it in one dispatch, so its write
is blind (nothing can move the variable in between), and a `ModifyVariable` is checked against its captured version
before it commits (see below); both are versioned like any other write.

| Case | What happens |
|---|---|
| Blind write (no base) | Applies. Among unordered writers, the last commit wins. |
| Base is the current version | Applies. |
| Stale base, default mode | Applies (the last write wins) and records a lost-update warning naming both writers. When the write changed the value, the warning keeps the value it overwrote so it can be restored. |
| Stale base, `Fault` mode, value came back (`0 → 1 → 0`) | Applies. The version mismatch is recorded. |
| Stale base, `Fault` mode, value changed | Does not apply. The step faults through the incident path (see [runtime fault behaviour](runtime-fault-behavior.md#special-case-a-lost-update-on-a-fault-node)). |

After a write applies, a value equal to the current one changes nothing and keeps the version. It also leaves the
frame's revision unchanged, so the frame revision no longer signals "something was written" for an equal value. A
frame stored before versions existed has no digest for its current values, so its first write to a variable hashes
both the stored and the new value. Two steps that both read
`0` and both write `1` are still two writers: the second is a lost update, and the warning says so.

## Inline steps leave no gap

An inline leaf, `External` or `ReplaySafe`, runs its Schedule, Start (input materialization) and Invoke (body and
completion) as one fused span. No other branch's work runs in between, so two parallel `count + 1` leaves total `2`
and record no lost update. An `External` leaf's attempt claim is still committed before its body runs, as an
intermediate commit inside the span. Code: `ReplaySafeFusionDriver.IsFusableNode`.

The body runs while the span's Schedule work item is the claimed one. The drainer renews that claim for as long as the
handler runs (`WorkflowSchedulerDrainer.DispatchWithClaimRenewalAsync`), as it renews an Invoke item's claim on the
step-by-step path, so a long body does not let the Schedule item be redelivered while it is still running.

The guarantee holds while the drain's coalescing session is active, which every drain starts with. After a boundary
that ends the session part-way through a drain, or on a host that turns fusion off, later leaves run step by step and
can meet a stale base. The commit rule then makes it visible as a lost-update warning; it is never silent.

## Opting in to strict read-modify-write

The default suits most workflows: unordered branches are the author's design, and the warning keeps the overwrite
repairable. Opt in to `Fault` where a lost update must never apply, such as a counter or a balance:

- on the activity type: `[ActivityLostUpdateMode(LostUpdateMode.Fault)]`;
- on one node: the node's `LostUpdateMode` setting.

Either one opts the node in. Publishing pins the mode in the executable node (`runtime.lostUpdateMode`), so changing
it takes effect on the next published version. Only `Fault` nodes store digests in their read sets.

## How to avoid lost updates

- Sequence the branches that update the same variable, or let each branch write its own variable and combine them
  afterwards.
- Keep independent data in separate variables. Two branches that change different fields of one object both write
  the whole object, so the second is a lost update.

## Background steps run on fresh inputs

A background step reads its inputs at Start, on the coordinator, and is admitted later. If a variable those inputs read
has a newer version at admission, the coordinator reads the inputs again as a new evaluation of the same invocation
(`InputSnapshot.Evaluation`), and the worker receives that snapshot. The superseded snapshot stays on the activity
state (`InputRefreshes`) with the variables that changed, for example `count v0 → v1`, and the admitted snapshot is not
changed again. The refresh covers only the gap between Start and admission: two background steps admitted on the same
value still meet the commit rule when their results arrive. Code: `BackgroundActivityCoordinator.RefreshInputsAsync`.

A node can turn the refresh off (`DisableBackgroundInputRefresh`, pinned as `runtime.backgroundInputRefresh=off`). The
worker then runs on the inputs Start read, and a stale base meets the commit rule: a warning by default, a fault under
`Fault`.

## Suspension

A suspended step keeps the input snapshot and read set it committed. It is not refreshed because time passed. When
it resumes and writes a variable it read, the commit rule judges that write against the base it read before it
suspended. A variable it only read never produces a lost update.

## `ModifyVariable`

The alteration records the target variable's version when the job is captured and applies only if that variable is
still at that version. A write to another variable in the same frame does not conflict with it. The operator's value
is versioned with an `Alteration` writer, so a later write on a stale base names the operator's change in its
warning and keeps the operator's value. The job's outcome keeps a write record for the change (versions and writer,
no value), numbered with the alteration's checkpoint.

## Write history

Every applied write and every lost update leaves a `VariableWriteRecord` on the writing step's activity state: the
variable (scope and key), the writer (node, invocation, attempt), the source (capture, intrinsic, background result,
alteration, accepted completion), the base and new versions, the outcome and the commit sequence. A `ModifyVariable`
keeps its record on the alteration job's outcome.

- **Order.** The committer numbers every checkpoint of an instance under its execution lease: the lease's fencing token
  (one higher on every acquisition) followed by the checkpoint's ordinal within the lease. Each new record, and each
  lifecycle transition of an activity (`Transitions`: scheduled, started, suspended, resumed, completed, faulted,
  cancelled, superseded), carries that number. Numbers grow with commit order and are not dense. A record also names its frame and
  the digest of the value it left, and an activity row keeps the first and last number on it (`FirstCommitSequence`,
  `LastCommitSequence`). A write of an equal value leaves no record unless it was a lost update. A coalescing drain numbers each
  logical checkpoint before folding them, so writes inside one folded segment keep their own order. Code:
  `RuntimeCommitSequenceStamper`.
- **Values.** The value a leaf's capture or an intrinsic wrote is an inspection value of the writing step (the other
  write sites are listed under *What is not covered yet*) (subject `VariableWrite`; name =
  variable key, phase = scope id, sequence = the version it produced), decided by the payload capture policy under the
  `variableWrites` diagnostics setting. It is off unless that setting itself is given a level: the default level for the
  other subjects does not apply to it. A value not captured has no payload and its
  capture mode says why; a sensitive one is withheld; retention applies as for any inspection value. None of these
  reads as `null`.
- **Operational data.** The value a lost-update warning overwrote (`OverwrittenValue`), a refused write
  (`RejectedValue`) and a refused completion (`RecordedCompletion`) stay on the activity state whatever the capture
  policy says, so a warning can be repaired (write the kept value back with `ModifyVariable`) and a refusal accepted.

## Closed scopes

The variables of a closed scope (a graph activity's body) are rows, not a frame. They follow the same rules: a row
carries its version, digest and writer, a body node's read records them, and a capture into a row is judged by the
commit rule against them. A refused row write refuses the completion as a refused frame write does.

## Retry, re-execute, accept

An operator recovers a faulted step that waits for intervention in one of three ways (ADR 0092 decision 8 as amended
2026-11-16). Each replaces the faulted row with a successor row.

| Action | Alteration | Identity | Inputs | Body |
|---|---|---|---|---|
| Retry | `RetryFaultedActivity/1` | the same logical invocation (`LogicalInvocationId`, operation keys) | the committed snapshot and its read set | runs again |
| Re-execute | `ReExecuteFaultedActivity/1`, and `RescheduleActivity/1` | a new logical invocation | materialized again from current committed state | runs again |
| Accept | `AcceptRecordedCompletion/1` | the same logical invocation | not used | does not run |

A retry suits a transient failure, where an external service deduplicates on the same key. Its write is judged against
the base it read before, so a variable an operator changed meanwhile is overwritten with a lost-update warning that
keeps the operator's value. A re-execution sees the corrected value. Activity code reads the logical identity as
`context.LogicalInvocationId` (stable across retries, new for a re-execution), or the operation key.

Accept is for a `Fault` step whose body succeeded but whose commit was refused as a lost update. The faulted row keeps
that refused completion (`RecordedCompletion`: result, outcome selection and every variable write). Accepting it
checks that the pinned contract is unchanged and that each target variable is still declared with the same type and
protection. It does not refuse a variable that changed since: the override keeps the value it overwrote. It then commits the successor as completed with
the recorded result, and applies each recorded write as an override (`AcceptedOverride`) that keeps the value it
overwrote. The parent hears of the completion once; a second request finds the source superseded and is refused. The
faulted row keeps the refused write, so the conflict stays in the history. A completion that also produced durable
outputs, a workflow dispatch, a published stimulus or a parent or ancestor notification is not recorded, because an alteration cannot replay those
effects, and accepting it is refused (`NoRecordedCompletion`).

## Inspecting the lifecycle

Spec 1229 (S-6) reads the write history back. No projection or history table is added: the read model is derived
when read from the write records, the lifecycle stamps and the alteration records above, plus two things this spec
stores: the scheduling causes on provenance (`CausedBy`) and, per activity row, its lost-update count and
commit-sequence range as store columns. Every route uses the existing permission for
activity-execution inspection; values follow the sensitive-value rules.

| Route under `runtime/workflows/instances/{id}` | Returns |
|---|---|
| `GET …/variables` | each variable instance (frame and key): scope, frame kind, current version, last writer, last commit sequence, value state |
| `GET …/variable-writes?frameId&scopeId&variableKey&source&outcome&overwrites&cursor&take` | the write history in commit order, paged |
| `GET …/journal?cursor&take` | the commit-ordered journal: per commit, the activity transitions (with their run-time path), input refreshes and applied writes |
| `GET …/compare/{otherId}` | two instances of one executable version compared by run-time path and digest; any other pair is `409` with `differentExecutable` |
| `GET …/variable-writes/{entryId}/payload?value=written\|overwritten\|refused` | one value, under the payload permission, audited like the value-evidence payload read |
| `GET …` (instance details) | `lostUpdateWarningCount`: the instance's lost-update warnings, summed from a store column |

- **Order.** Entries are ordered by commit sequence, then by position in the commit (frame revision, time). Records
  without a commit sequence (older records, commits made outside an execution lease) come first, ordered by frame
  revision and time, and are marked `orderApproximate`. A page cursor is a commit sequence and a position within the
  commit, so a page that ends inside a commit continues exactly there.
- **Applied and current value.** A write is applied when it changed the version; a refused `Fault` write is listed but
  not applied, and so is a stale-base write of the value already there (it still counts as a lost update). The applied
  entry whose new version is the variable instance's current version is marked `isCurrentValue`. An accepted override
  links to the refused write it resolves (`resolvesEntryId`); both stay in the history. `ModifyVariable` changes are
  listed with source `alteration` in their commit position.
- **Concurrent overwrites.** An applied write that replaced the previous applied write of the same variable instance is
  a concurrent overwrite when that write does not happen before the overwriting invocation. *Happens before* is read
  from scheduling provenance: walking back from the invocation's scheduling over the activity that scheduled it and the
  *scheduling causes* (`ActivitySchedulingProvenance.CausedBy`: the completed child of a parent callback or a loop's
  next iteration, the arrivals that satisfied a Flowchart join), and from a completed container to the children that
  completed before it, a commit of the earlier writer at or after its write must be reached. So sequences, loops,
  joins and a `Parallel` that completed order their writes; parallel branches and a child a container scheduled before
  writing do not. Writes of one logical invocation (its retries) never overwrite each other; an alteration write is
  never marked as an overwrite, and a later write that was not scheduled after it is. An applied lost update is one
  kind of concurrent overwrite (`isLostUpdate`). The rule covers scripts, C# writes and run-time Flowchart paths alike,
  because it reads provenance, not structure. Code: `VariableLifecycleInspection`.
- **Value states.** A value is `captured`, `omitted` (not captured), `redacted` (captured, the caller may not see it) or
  `expired` (removed after capture); none of them is `null`, and a captured `null` is a value read through the payload
  route. The written value is the writer's `VariableWrite` value evidence; the overwritten and refused values are
  operational data on the record (`isOperational`). A written value's digest is shown with its state (none for a hidden
  value); the comparison shows digests only.
- **Lost-update count.** Each activity row keeps the number of its lost-update warnings (`LostUpdateWarningCount`),
  set when the row is committed; the store keeps it and the row's commit-sequence range as columns (the range indexed
  with the instance), so the instance details sum it without reading row documents. No incident is raised: an incident ends a coalescing session.
- **Run-time path.** An activity execution is matched across instances by the executable node ids from the root down,
  each with its occurrence under its parent (in order of scheduling), never by row ids.
- **Cost.** The run time pays only for the scheduling causes and the per-row count. Each read loads the instance's rows
  once; spec 1229's measurement (T022) is in its tasks and on its PR.

Studio has a client for these routes and the history, journal and comparison views
(`VariableLifecycleViews.tsx`). Placing them on the instance view, with the lost-update badge that opens the filtered
history, follows the activity-level Studio work of spec 1221 (#4735).

## What is not covered yet

- Read sets cover Elsa's instrumented read paths: variable-read bindings, declared expression parameters and the
  ambient variables an expression references. An expression with computed access (`variables[x]`, `getVariable(x)`)
  is handed every visible variable, so all of them enter its read set: the conservative direction, which can raise a
  warning (or, under `Fault`, a fault) for a variable it did not use. Arbitrary host access is not tracked.
- An intrinsic cannot write a closed scope's variables yet; only captures write rows.
- A value snapshot is kept for captures of a leaf (on completion and on resume, including background results) and
  for intrinsic writes to the root and container frames. Captures into closed-scope rows, captures made while a
  composite completes and accepted overrides have their write record, but no value snapshot yet. (Loop iteration
  frames are read-only to intrinsics.)
- Two captures of one completion into the same closed-scope variable are both judged against the stored row; the
  second does not see the first.
- A closed-scope variable row carries no protection policy of its own, so its write record takes the protection of the
  captured value.
- A record of a closed-scope row write has no frame revision; its `CommitOrder` is the version the write produced.
- The committer remembers, per lease, the stamps of activities still in flight; an entry goes when the activity's
  terminal commit is persisted or the lease ends.
- An alteration replay is matched on the job's terminal evidence, which does not compare the outcome's write records.
- A commit that may have been stored but whose commit call failed, resubmitted in the same lease after other commits,
  is numbered again; a byte-identical resubmission then no longer matches the stored copy.
- A commit resubmitted byte for byte under a later lease is numbered again, so it no longer matches the stored copy.
  Rebuilt commits already differ by their timestamps.
- A `Fault` refusal on resume or on a composite's completion records only the refused write, not a completion to
  accept.
- A BPMN joining gateway records no scheduling causes yet (Flowchart joins do), so a write after it can be marked as a
  concurrent overwrite of a branch that arrived at the gateway before it.
- A commit made outside an execution lease is not numbered, so what it records reads as order unknown.
- The concurrent-overwrite walk labels an edge with the commit that created the scheduled row, which comes after the
  scheduler's decision. A scheduler that writes a variable between scheduling a child and that child's row being
  created is read as ordered before the child, so the child's later overwrite of that write is not marked.
- An operator's `ModifyVariable` value is not on its write record (only in the protected alteration request), so the
  history shows it as not captured.
- Rows committed before the inspection columns existed count no lost updates in the instance details; their records
  are still read.
