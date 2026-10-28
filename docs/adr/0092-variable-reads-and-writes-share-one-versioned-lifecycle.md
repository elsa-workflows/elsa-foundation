# Variable Reads And Writes Share One Versioned Lifecycle

Status: proposed (2026-10-28). Joey Barten (@j03y-nxxbz) drafted it from analysis A-2 on epic
[#3052](https://github.com/elsa-workflows/elsa-foundation/issues/3052). Acceptance needs agreement with Sipke Schoorstra
(@sfmskywalker) and Frans van Ek (@FransVanEk). It completes Sipke's stale read-modify-write proposal (ADR 0090 §7
and his decision record R4, 25 October), rather than replacing it.

Part of epic #3052. Implementing it needs an active program whose scope covers runtime value flow
([program-goal registry](../program-goals/README.md#registry)). If accepted, it amends
[ADR 0045](0045-workflow-value-flow-uses-role-owned-bindings-and-immutable-invocation-records.md) §3 and §4,
[ADR 0048](0048-container-output-captures-resolve-through-producer-visible-variable-frames.md) §4, and the proposed
[ADR 0090](0090-activities-change-variables-as-a-unit-of-work-committed-with-the-step.md) §2, §6 and §7. It keeps
the transaction model of D-3. It narrows decision D-8 on the epic (unordered writes get a warning, last commit wins)
to **blind** writes: a write that is based on a stale read is handled by decision 6 instead.

## Context

The A-2 analysis traced how a variable lives on `main` (`d60fed26a`). The full trace, with file and line references,
timelines and the Elsa 3 comparison, is in the A-2 analysis on the epic. This section keeps what the decision needs.

### How reads and writes work today

- **Reads.** A step reads variables in one place: when its inputs are materialized, in the `StartActivity` work item
  (`WorkflowStartActivitySchedulerWorkHandler.cs:279-304`). The result is the immutable input snapshot of ADR 0045 §3.
  The activity never sees a variable, only that snapshot. Nothing records which version of a variable was read.
- **Writes.** Output captures apply when the step completes, in the later `InvokeActivity` work item
  (`WorkflowInvokeActivitySchedulerWorkHandler.cs:595-606`). Set, Merge and Reduce read and write in their own
  `StartActivity` work item (`WorkflowIntrinsicExecutor.cs:99-155`). Merge and Reduce currently overwrite exactly like
  Set. An operator's `ModifyVariable` checks the root frame's revision it captured
  (`ModifyVariableAlterationHandler.cs:27-29`); it can change root-frame (workflow) variables only.
- **No stale check.** Captures and Set reload the current state and write with the frame's *current* revision
  (`RuntimeOutputCaptureProjection.cs:105`, `WorkflowIntrinsicExecutor.cs:127`). Nothing compares what a step read
  with what is there when it commits.
- **The drain.** One FIFO queue per execution, one work item at a time. A leaf takes separate hops: Schedule, Start
  (inputs read), Invoke (body runs, captures commit). Parallel branches interleave hop by hop. The exception is
  fusion: a `ReplaySafe` inline leaf inside a live coalescing burst runs Schedule, Start and Invoke in one dispatch
  (`ReplaySafeFusionDriver.cs`, on by default). Unclassified activities have the fail-safe `External` profile
  (ADR 0032 R1) and are not fused.
- **Publish-time checks.** Draft validation refuses potentially concurrent Set, Merge and Reduce writes
  (`ValueFlow/ConcurrentWrite`, `ValueFlowValidator.cs:282-294`), not captures. ADR 0048 §5 requires refusing
  concurrent captures, but nothing enforces it. Spec 266 (PR #3286, S-5) turns the refusal into a warning and extends it
  to captures.

### What goes wrong

1. **A lost update exists today, inline, without any diagnostic.** Two parallel branches each take `count` as an input
   and capture `count + 1` back, using `External` (unclassified) activities. `Parallel` schedules both branches at once
   (`Parallel.cs:99-102`), so both Start hops read 0 before either Invoke hop commits, and the result is 1. Draft
   validation does not see it, because `ValueFlow/ConcurrentWrite` checks Set, Merge and Reduce only. With `ReplaySafe`
   activities inside a coalescing burst, fusion runs each branch in one dispatch and the result is 2, so today the
   outcome depends on the activities' side-effect profile and the persistence mode. This was established by reading the
   code; it has not been reproduced by a test (see *Required evidence*).
2. **Background placement widens the same gap** to however long the worker runs. Its inputs are read on the
   coordinator before admission, and its result is applied later through captures.
3. **A retry reuses an outdated starting value.** `RetryFaultedActivity` and `RescheduleActivity` create a successor with
   a new invocation id that copies the old input snapshot (`ActivitySuccessorStaging.NewSuccessor`). For a CLR activity
   the successor starts as `Running`, so `StartActivity` skips materialization
   (`WorkflowStartActivitySchedulerWorkHandler.cs:138-144`); Set, Merge and Reduce successors already re-materialize. An
   operator who corrects a variable with `ModifyVariable` and then retries gets a retry that still uses the old value.
   ADR 0045 §3 does not require this: its "same input snapshot" applies to attempts of one invocation, and the successor
   is a new invocation. The copy is code behaviour that conflicts with D-2.
4. **ADR 0090 §7, as proposed, sees only part of it.** Its check covers variables read through the step's variable
   session. A step that reads `count` through its input and writes it through the session or a capture is not
   checked. Its value comparison also costs one serialization per checked variable.

### The root cause

Reads and writes were each designed carefully, but separately and at different times:

- ADR 0045 designed the read side (bindings and an immutable snapshot) for replay and determinism.
- ADR 0045's #1004 amendment and ADR 0048 designed the write side (captures, atomic with the completion).
- ADR 0090 proposes a third channel (the variable session) with its own base and its own check.

Nothing defines how a write relates to the read it was based on. Every write applies "on top of whatever is committed
now". The lost update, the background window, the retry problem and ADR 0090's gap are all symptoms of that missing
link. They should be solved once, at that level, not one by one.

### What Elsa 3 did

Elsa 3 kept variables as live in-memory objects on one thread with a FIFO scheduler. Parallel inline `count++` ended
at 2, but only because each activity read and wrote in one go. Nothing recorded who wrote what, or in which order.
Background activities ran on a detached copy of the instance. With the built-in `WorkflowInstance` storage driver their
variable writes landed in that copy and were lost; only a custom external storage driver persisted them, outside the
instance (`BackgroundActivityInvoker.cs:30-45` on `release/3.6.3`). Elsa 4 aims for predictable results, *with* a
record.

## Terms

- **Version**: a per-variable counter. Each applied write that changes the variable's value increments it. The
  variable's value digest is computed once per applied write and stored beside the version.
- **Read set**: the variables an invocation actually read, each with the version and stored digest it saw. It has two
  parts:
  - the **input read set**: reads made while the inputs were materialized, committed with the input snapshot;
  - the **session reads**: reads made by the body through the variable session (ADR 0090), recorded per attempt.
- **Base**: for a write to variable X, the version and digest of X in the writer's read set. A write to a variable the
  invocation did not read has no base.
- **Blind write**: a write without a base.
- **Lost update**: a write with a base whose version is no longer current *and* whose value has since changed. The
  writer built its value on data that another writer has replaced.
- **Write history**: the record of every applied write, and of every lost update and how it was resolved.
- **Step**: as in ADR 0090: one invocation attempt, from invoke or resume to its next transition. Each evaluation of a
  structural activity is its own step (D-3, Sipke's follow-up 6). The input read set belongs to the invocation and is
  shared by all its attempts; session reads belong to one attempt.

## Decision

### 1. Two authoring surfaces, one mechanism

Authors keep two ways to work with variables:

- **Bindings**: the workflow author binds variables to inputs and captures outputs into variables. Reusable
  activities do not know variable names.
- **The variable session** (ADR 0090): script and C# code names variables itself, for example `variables.count`.

Both surfaces use one variable-access mechanism underneath. Every read and every write goes through it:

| Reader or writer | Read recorded when | Write applied when |
|---|---|---|
| Input bindings and input expressions | inputs are materialized | not a writer |
| Variable session (scripts, C#) | the read happens | the step commits |
| Output captures | not a reader | the step commits |
| Set, Merge, Reduce | their value is evaluated | their own commit |
| Operator alterations (`ModifyVariable`) | the operator requests it | the alteration commits |
| Background step results | inputs are materialized on the coordinator | the result is applied |

*Why.* A rule that holds for one surface but not the other is a hole an author falls into without knowing it. The
surfaces differ in who knows the variable name, which is a good reason for two surfaces. It is no reason for two
mechanisms.

### 2. Every variable has a version

Each variable has its own version, independent of other variables in the same frame or scope, stored in the frame or
row with a digest of its value. Both are computed once, when a write is applied. A write whose digest equals the
current digest applies nothing and does not increment the version.

*Why.* The frame revision counts writes to every variable in the frame, so using it would report conflicts between
unrelated variables (Sipke's R4: "conflict handling must concern the variable in question"). Storing the digest at
write time means a read records two stored values and hashes nothing, and the commit rule (decision 5) never has to
serialize a value it did not just write. The version also gives the write history its order for free.

### 3. A step records what it actually read

The read set records each variable the invocation read, with its stored version and digest:

- During input materialization, the engine records every variable an input binding or input expression actually read:
  the input read set. Once D-7 is written into ADR 0089 (computed names fall back to a run-time lookup), this includes
  computed names, because the read is observed at run time, not predicted by static analysis. Until then, ADR 0045 §5,
  ADR 0089 decision 5 and ADR 0090 §3 still refuse computed names, and only resolvable names occur.
- During execution, every session read is added to the attempt's session reads.
- A variable that was visible but not read is **not** in the read set.

The input read set is committed with the input snapshot and travels with background admission. Session reads are
recorded per attempt and are not carried to a new attempt.

*Why.* Recording only actual reads avoids false conflicts: a blind write to a variable the step never read is not a
lost update. Observing reads at run time means computed names are covered without depending on static analysis, which
cannot see everything. Sipke's follow-up 8 already requires tracking actual dynamic reads for protection.

### 4. A step that reads and writes the same variable is a read-modify-write

When a step writes a variable that is in its read set, the write's base is the recorded version, whatever surface was
used for the read and for the write. That includes an input read with a capture write, and a session read with a
session write.

*Why.* In neither surface can the engine see whether the written value depends on the read. A script can read `count`
only to log it, and then write an unrelated value. ADR 0090 §7 already treats "read, then changed" as a read-modify-write;
this applies the same rule to both surfaces. The conservative choice is correct here: a missed lost update is silent,
while a false one is visible and can be overridden (decision 6).

**A step never conflicts with its own writes.** When an invocation's own write to X is applied, its base for X advances
to the version that write produced. This covers a step that commits a session change to X when it suspends and
captures X when it completes, and ADR 0090 §6's order within one commit (session changes first, captures last).

### 5. The commit rule

When a step commits a write to variable X:

1. **No base (blind write):** the write applies. Between unordered branches the last commit wins (D-8), and draft
   validation warns about unordered writes (spec 266).
2. **Base equals the current version:** the write applies.
3. **Base is older, but the current value's digest equals the base digest:** the value moved and came back (for
   example 0 → 1 → 0). Nothing the writer relied on was lost. The write applies, and the write history records the
   version mismatch.
4. **Base is older and the value differs:** this is a **lost update**. The step's lost-update policy decides
   (decision 6).

The rule is the same for inline and background steps, for captures and session writes, and for frames and rows (D-9).

*Why.* Versions alone would fault on 0 → 1 → 0, where nothing was lost. Comparing serialized values at every check
(ADR 0090 §7) costs a serialization each time. Versions first, with a comparison of two stored digests only on a
mismatch, avoids both the false fault and the cost.

This narrows D-8 to blind writes. Before, every unordered write to one variable was "last commit wins". Now a write
that is based on a stale read is a lost update, even when the other write was blind.

### 6. Lost-update policy: fault by default, last-wins by explicit choice

- **Default: `Fault`.** The step faults with a diagnostic that names the variable, the base version, the current
  version and the writers. A faulted step saves nothing (D-3).
- **Override: `LastWins`.** The write applies anyway. An activity type may declare it as its default (for example an
  aggregator), and the workflow author may set it per node. A `LastWins` overwrite is always recorded in the write
  history and shown in inspection. It is never silent.
- No other policies. A "warn and continue" policy is not offered: with the write history, `LastWins` is already
  visible, so a third option would add choice without adding information. Whether even `LastWins` should exist, or the
  engine should allow one strict behaviour only, is open question 3.

*Why.* A lost update is almost never what the author meant, so stopping before the wrong value spreads is the safer
default (Sipke's position, agreed by Joey). Some designs genuinely accept last-wins; they say so explicitly.

### 7. A fault goes through the existing incident path

A lost-update fault is an ordinary activity fault. It becomes an incident and the workflow's incident strategy decides
what happens (`FaultWorkflow`, `ContinueWithIncidents` or `WaitForIntervention`; see `docs/runtime-fault-behavior.md`).
Recovery uses the alterations API's retry. No new recovery mechanism is introduced.

A lost-update fault is detected at commit, so the step's body has already run, and its external effects have already
happened. A retry runs the body again; idempotency of external effects belongs to the workflow designer (D-2). The
incident therefore records the rejected result (under the existing protection rules), so that an operator can see what
the body produced, for example a payment id, and decide between a retry and applying it. How an operator applies a
recorded result is left to the implementing spec (open question 11).

### 8. An operator retry reads fresh inputs

A retry or reschedule requested by an operator through the alterations API is a new execution (D-2: "a re-run is a new
execution with new values"). Its successor already gets a new invocation id; it now also materializes its inputs again
from current committed state, with a new input read set, as Set, Merge and Reduce successors already do.

This differs from **recovery of the same invocation**, which keeps its committed snapshot and input read set:

- redelivery after a crash, before the step committed (ADR 0045 §3, Sipke's follow-up 2: "retain committed input
  snapshots on retry");
- resume after a suspension (same invocation, see decision 11).

In both cases a new attempt starts with no session reads; it reads again through the session (ADR 0090 §6).

This aligns the code with D-2 and with ADR 0045 §3, whose "same input snapshot" applies to attempts of one invocation,
not to a successor.

*Why.* Without this, a lost-update fault followed by a retry would read the same outdated value and fault again, or,
with `LastWins`, lose the update again. And a variable an operator corrected would never reach the retried step. The
distinction keeps Sipke's follow-up 2 for what it was meant for, recovery, while an operator's retry gets the values
the operator just fixed.

### 9. A stale base is refreshed before background admission

Just before a background step is admitted, the coordinator compares its input read set with the current versions. If
any base is already stale, it materializes the inputs again, instead of sending the worker outdated values. This is
safe because nothing has run yet.

The refreshed snapshot is recorded as a new snapshot that supersedes the first, with its own read set. The first stays
in the history, so inspection never shows values the body did not see, and the snapshot the body receives is immutable
from then on (ADR 0045 §3). This follows the precedent of the #977 amendment, which also keeps the pinned record when
it re-materializes.

It covers only the gap between Start and admission. The long gap while the worker runs is covered by the commit rule
(decision 5). Inline steps do not need it once decision 10 holds.

*Why.* It prevents lost-update faults that can be seen coming, at the last moment where refreshing has no side effects.

### 10. An inline step does not leave a gap between its reads and its body

The drain must not commit another branch's work between an inline step's input materialization and its body. How is
left to the implementing spec. Fusion already proves that Schedule, Start and Invoke can run in one dispatch for
`ReplaySafe` leaves. The obstacle for `External` leaves is ADR 0032 R2: their `ActivityAttemptClaimed` checkpoint is an
immediate pre-activation flush (spec 095 FR-019). Several commits within one work item are allowed; the requirement is
only that no other branch's work item runs in between. An inline step's bases are then current when its body runs, and
inline steps meet the lost-update rule only through suspension.

*Why.* Today's inline lost update (Context, item 1) is a gap created by Elsa's own hop structure, not by the author's
design, and today it depends on the side-effect profile and persistence mode. Warnings about it would teach authors to
ignore warnings. Whether this can be done without breaking the claim and flush rules is to be verified in the spec
(open question 7).

### 11. A suspended step keeps its base

A step that suspends and later resumes keeps its committed snapshot and read set. If a variable in its read set was
changed by someone else meanwhile, its commit is a lost update under decision 5. This is correct, but in long-running
workflows it may be frequent. Open question 8 asks whether this needs a different answer.

### 12. Operator alterations use per-variable versions

`ModifyVariable` records the variable's version when the operator requests the change, and applies only if it is
unchanged, replacing today's root-frame revision check. Today it can change workflow (root-frame) variables only;
whether it should reach container variables is outside this ADR. A step whose read set includes the variable, and that commits
after the operator's change, sees a lost update under decision 5. The operator's correction is therefore protected
from being silently overwritten.

### 13. Every write is recorded in the write history

Each applied write records: the variable (scope and key), the writer (node, invocation, attempt, and whether it was a
capture, an intrinsic, a session change, an alteration or a background result), the base version, the new version, the
commit sequence, and the value under the existing protection and redaction rules. Lost updates are recorded with their
resolution (`Fault` or `LastWins`).

This is the data source for variable write history in inspection (#3290), the commit-ordered journal and instance
comparison (#3291), and the runtime trace of overwrites (#3292), that is, spec S-6. It is not the persistence journal
proposed in [ADR 0084](0084-runtime-commit-journal.md), which is about commit cost.

The history may be derived from the checkpoint commits rather than written as extra rows, and recording values may be
opt-in with a retention limit, so hot loops under ADR 0032 do not pay for it by default. The implementing spec chooses,
with measurements (open question 12).

### 14. A variable is the unit

A write replaces the whole value of a variable. Two branches that change different fields of one object both write the
whole object, so the second is a lost update under decision 5. The guidance for authors is to keep independent data in
separate variables.

A three-way merge on conflict (base, mine, theirs; merge when different fields changed, conflict when the same field
did) would let such changes combine. It is deliberately **not** part of this decision. It needs the core lifecycle to
be in place and agreed first, and evidence on its cost for large values. It is listed under *Alternatives*.

## Consequences

- **ADR 0045 §3.** Recovery of the same invocation keeps the snapshot, as today. An operator retry or reschedule, which
  is a new invocation, materializes again (decision 8). Before background admission a stale snapshot may be
  superseded by a new one (decision 9). A `While` pass that re-materializes under the #977 amendment records its own
  input read set for that evaluation.
- **ADR 0045 §4.** "Reads occur when the consumer invocation materializes its inputs" stays true, and those reads are
  now recorded with versions.
- **ADR 0045 §5, ADR 0089 decision 5, ADR 0090 §3.** They still refuse computed names. D-7 changes that through the
  ADR 0089 amendment; decision 3 follows whatever that amendment allows.
- **ADR 0048 §4.** Commits stay totally ordered per execution (as amended on PR #3286). Each variable gets its own
  version beside the frame revision. Captures write against the step's read set.
- **ADR 0090.** The session is the second surface over this mechanism (decision 1). Its base becomes version plus
  digest (decision 5), its check covers input reads too (decision 4), and its `[VariableConflictPolicy]` becomes the
  lost-update policy of decision 6. Whether background steps get a session at all, and whether scripts may write
  variables generally or only in a compatibility mode, stay with A-3 and ADR 0093.
- **Spec 266 (#3286, S-5).** Its publish-time warning covers all unordered writes, captures included, and stays as it
  is. At run time, D-8's last-commit-wins now applies to blind writes only. A follow-up may add a stronger publish-time
  diagnostic for an unordered step that reads and writes the same variable.
- **Merge and Reduce** still overwrite like Set. Giving them real semantics is separate work.
- **Cost.** One digest per applied write; a version and digest per variable read (stored values, no hashing); one
  digest comparison on a version mismatch; the write history. The read set enlarges every activity row, multiplied by
  loop iterations, and versions change the frame's content and the checkpoint fold. The spec measures all of this
  against large payloads and long loops.

## Required evidence for the implementing spec

- A test that reproduces today's inline lost update with `External` leaves (Context, item 1), failing before the change
  and passing after.
- A step that writes X on suspension and captures X on completion does not conflict with itself.
- Read sets contain actual reads only, including computed names; an unread visible variable never causes a conflict.
- A blind write follows D-8; an unchanged value writes nothing and keeps the version.
- 0 → 1 → 0 applies without a fault and is recorded; a real lost update faults by default.
- `LastWins` per activity type and per node, always recorded and visible.
- The rule is identical for inline and background steps, captures and session writes, frames and rows.
- An operator retry and reschedule materialize fresh inputs; crash redelivery and resume keep the snapshot. A
  successor that now goes through Start again does not activate its owned frames a second time.
- `ModifyVariable` then retry uses the corrected value; a step committing over an operator's change faults.
- A stale base is refreshed before background admission; the superseding snapshot is what the worker sees, and the
  first stays in the history.
- A lost-update incident records the rejected result.
- Inline steps have no gap between input materialization and the body (decision 10), with the claim and flush rules
  of ADR 0032 kept.
- The write history feeds #3290–#3292 under the existing protection rules.
- Cost measured against a large payload.

## Alternatives considered

**Background steps get no variable access (Joey's starting point for A-2).** Removes session reads from background
steps, but leaves the input-plus-capture lost update, inline and in the background, and the retry problem. Not enough on
its own; whether background steps get a session is still decided in A-3.

**Detect stale read-modify-write by value comparison only (ADR 0090 §7 as proposed).** Covers session reads only and
costs a serialization per checked variable. Its idea, detect and fault with an override, is kept.

**Per-variable locking or serialization.** Prevents lost updates by making later readers wait. Needs exact read and
write sets in advance (computed names make them "everything"), adds lease and deadlock handling, and removes the
parallelism background placement exists for.

**Order defined by design position.** Makes the final value of blind writes predictable from the canvas, but does not
prevent a stale read-modify-write, and changes D-8.

**Operations instead of values (increment, append).** Correct for parallel aggregation without faults, but only for
operations Elsa understands. A natural later step for Merge and Reduce; not a general answer.

**Field-level patches.** Rejected in ADR 0090: array patches by index land in the wrong place after another branch
changed the array, and merged values must be validated again.

**Three-way merge on conflict.** Promising, and compatible with this design, because the base digest and the base
version are already recorded. Deferred until this lifecycle is agreed and its cost is measured (decision 14).

## Questions for the reviewers

@sfmskywalker @FransVanEk, each question has the proposed answer in the decision it names. Please agree, or say what
you would change.

1. **One mechanism.** Do you agree that bindings and the variable session are two authoring surfaces over one
   variable-access mechanism, with one read-set and write rule (decision 1)? If you see a reason to keep two
   mechanisms, that reason is what we most need to hear.
2. **What is a read?** Actual reads during input materialization and during execution, computed names included;
   visible but unread variables excluded (decision 3). And: is "read and write the same variable in one step" always a
   read-modify-write, even when the engine cannot see a dependency (decision 4)?
3. **Strict or options?** Default `Fault`, with `LastWins` per activity type and per node, and nothing else
   (decision 6). Or should there be one strict behaviour only, which is simpler to learn, test and support?
4. **Version plus digest** instead of value comparison alone, with 0 → 1 → 0 applying without a fault (decision 5).
5. **Retry.** An operator retry or reschedule materializes fresh inputs, while crash recovery and resume keep the
   snapshot (decision 8). Sipke, does this keep the intent of your follow-up 2?
6. **Refresh before background admission** instead of faulting later, recorded as a superseding snapshot (decision 9).
7. **No gap for inline steps** (decision 10). Do you see a reason in the claim and flush rules (ADR 0032 R2) why an
   `External` leaf's inputs cannot be materialized right before its body, as fusion already does for `ReplaySafe`
   leaves?
8. **Suspended steps** keep their base, so a long wait can end in a lost-update fault (decision 11). Is that the right
   answer for long-running workflows?
9. **Granularity.** The whole variable now, and a three-way merge later (decision 14).
10. **Scope with A-3.** If writing variables from scripts becomes a compatibility mode only, this ADR still holds for
    bindings and captures. Do you agree it is independent of A-3's answer?
11. **A rejected result.** The incident records the result a faulted step produced (decision 7). Should an operator be
    able to apply it as `LastWins`, instead of only retrying the body and repeating its external effects?
12. **Write history cost.** Extra rows per write, or derived from checkpoint commits; values always, or opt-in with a
    retention limit (decision 13)?
