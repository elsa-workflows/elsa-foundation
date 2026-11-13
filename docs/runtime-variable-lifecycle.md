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

## Suspension

A suspended step keeps the input snapshot and read set it committed. It is not refreshed because time passed. When
it resumes and writes a variable it read, the commit rule judges that write against the base it read before it
suspended. A variable it only read never produces a lost update.

## `ModifyVariable`

The alteration records the target variable's version when the job is captured and applies only if that variable is
still at that version. A write to another variable in the same frame does not conflict with it. The operator's value
is versioned with an `Alteration` writer, so a later write on a stale base names the operator's change in its
warning and keeps the operator's value.

## What is not covered yet

- Read sets cover Elsa's instrumented read paths: variable-read bindings, declared expression parameters and the
  ambient variables an expression references. An expression with computed access (`variables[x]`, `getVariable(x)`)
  is handed every visible variable, so all of them enter its read set: the conservative direction, which can raise a
  warning (or, under `Fault`, a fault) for a variable it did not use. Arbitrary host access is not tracked.
- A `ModifyVariable` leaves no write-history record of its own; its version carries an `Alteration` writer, and the
  alteration job keeps its own outcome.
- Variables of a closed scope (graph rows) are read and written outside the root frame; their writes are blind.
- The operator retry / re-execute / accept-recorded-completion split and the background admission refresh are
  delivered separately (see the spec's tasks).
- The write history is data only; the inspection surfaces that present it are spec S-6.
