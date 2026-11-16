# Variable Reads And Writes Share One Versioned Lifecycle

Status: accepted (2026-11-12). It follows from analysis A-2. It completes the stale read-modify-write proposal of ADR 0090 §7,
rather than replacing it.

Its implementation is planned through the initiative's own specs (S-10), not through a program goal. If accepted, it
amends
[ADR 0045](0045-workflow-value-flow-uses-role-owned-bindings-and-immutable-invocation-records.md) §3 and §4,
[ADR 0048](0048-container-output-captures-resolve-through-producer-visible-variable-frames.md) §4, and the proposed
[ADR 0090](0090-activities-change-variables-as-a-unit-of-work-committed-with-the-step.md) §2, §6 and §7. It keeps
the transaction model of D-3. It keeps decision D-8 of the initiative (unordered writes get a warning, last commit wins)
for every write, blind or based on a stale read. A write on a stale base also raises a lost-update warning, and a node
may opt in to a strict mode that faults instead (decision 6).

## Context

The A-2 analysis traced how a variable lives on `main` (`d60fed26a`). The full trace, with file and line references,
timelines and the Elsa 3 comparison, is in the A-2 analysis. This section keeps what the decision needs.

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
  concurrent captures, but nothing enforces it. Spec 266 (S-5) turns the refusal into a warning and extends it
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
   ADR 0045 §3 says "retries and resumptions retain one stable logical `InvocationId` and the same input snapshot". The
   code already gives an operator retry a new id but keeps the snapshot, which matches neither that sentence nor D-2
   ("a re-run is a new execution with new values").
4. **ADR 0090 §7, as proposed, sees only part of it.** Its check covers variables read through the step's variable
   session. A step that reads `count` through its input and writes it through the session or a capture is not
   checked. Its value comparison also costs one serialization per checked variable.

### The root cause

Reads and writes were each designed carefully, but separately and at different times:

- ADR 0045 designed the read side (bindings and an immutable snapshot) for replay and determinism.
- ADR 0045's output-capture-edge amendment (§4) and ADR 0048 designed the write side (captures, atomic with the completion).
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
  variable's value digest is computed once per applied write and stored beside the version. The digest is taken over
  ADR 0091's canonical form (decision 7 there), so it compares meaning, not the stored text: two writers that write
  the same value with different escaping or notation produce the same digest.
- **Read set**: the variables an invocation actually read, each with the version it saw, and, for a node that opts in
  to `Fault` (decision 6), also the stored digest it saw. It has two parts:
  - the **input read set**: reads made while the inputs were materialized, committed with the input snapshot;
  - the **session reads**: reads made by the body through the variable session (ADR 0090), recorded per attempt.
- **Base**: for a write to variable X, the version of X in the writer's read set (with its digest where `Fault` is
  opted in). A write to a variable the invocation did not read has no base.
- **Blind write**: a write without a base.
- **Stale base**: a base whose version is no longer current when the write commits.
- **Lost update**: a write on a stale base. The writer built its value on data that another writer may have replaced.
  Which check decides it, and what follows, depends on the node's mode (decisions 5 and 6).
- **Write history**: the record of every applied write, and of every lost update with its outcome: the warning raised,
  or the fault.
- **Step**: as in ADR 0090: one invocation attempt, from invoke or resume to its next transition. Each evaluation of a
  structural activity is its own step (D-3).
- **Invocation**: one logical execution of a node, with one `InvocationId` and one input snapshot, across all its
  attempts, suspensions and resumes (ADR 0045 §3). The input read set, including bases advanced by the invocation's own
  writes (decision 4), belongs to the invocation and is persisted across suspension. Session reads belong to one step.

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
row with a digest of its value. Both are computed once, when a write is applied.

The commit rule (decision 5) runs first. Only a write that it applies, or a blind write, is then compared with the
current value: if their digests are equal, the write applies nothing and the version stays. So two branches that both
read 0 and both write 1 do not hide the second write as a no-op; the second is a lost update.

*Why.* The frame revision counts writes to every variable in the frame, so using it would report conflicts between
unrelated variables, while conflict handling must concern the variable in question. Storing the digest at
write time means a read records stored values and hashes nothing, and the exact check of decision 5 never has to
serialize a value it did not just write. The version also gives the write history its order for free.

### 3. A step records what it actually read

The read set records each variable the invocation read, with its stored version, and with its stored digest where the
node opts in to `Fault` (decision 6):

- During input materialization, the engine records every variable an input binding or input expression actually read:
  the input read set. Once D-7 is written into ADR 0089 (computed names fall back to a run-time lookup), this includes
  computed names, because the read is observed at run time, not predicted by static analysis. Until then, ADR 0089
  decision 5 and ADR 0090 §3 still refuse computed names, and only resolvable names occur (ADR 0045 §5 no longer
  forbids them).
- During execution, every session read is added to the attempt's session reads.
- A variable that was visible but not read is **not** in the read set.

The input read set is committed with the input snapshot and travels with background admission. Session reads are
recorded per attempt and are not carried to a new attempt.

These guarantees cover reads through Elsa's instrumented variable-access paths, including its bindings and session
APIs. Arbitrary host access outside those paths is not automatically tracked and must not be presented as fully
covered by the read set.

*Why.* Recording only actual reads avoids false conflicts: a blind write to a variable the step never read is not a
lost update. Observing reads at run time means computed names are covered without depending on static analysis, which
cannot see everything. Protection already requires tracking actual dynamic reads.

### 4. A step that reads and writes the same variable is a read-modify-write

When a step writes a variable that is in its read set, the write's base is the recorded version, whatever surface was
used for the read and for the write. That includes an input read with a capture write, and a session read with a
session write.

A newer session read must not erase an older input dependency that the invocation still relies on. For example, an
input reads `count = 0`, another branch writes 10, and a session then reads 10; a capture calculated from the original
input still writes against the older dependency. The newer read cannot hide that stale calculation from the commit
rule. The implementing spec defines how it represents and advances bases while preserving dependencies still in use.

*Why.* In neither surface can the engine see whether the written value depends on the read. A script can read `count`
only to log it, and then write an unrelated value. ADR 0090 §7 already treats "read, then changed" as a read-modify-write;
this applies the same rule to both surfaces. The conservative choice is correct here: a missed lost update is silent,
while a false one costs a warning by default (decision 6).

**A read alone never produces a write.** Only an explicit write enters this rule: a capture, a `Set`, or a session write
(an assignment, or in a script a change found by comparing JavaScript with JavaScript, ADR 0046 V9). The ADR 0090 amendment (C1 and C2) never turns a read into a write, so a step that only read X has
no write to X, and so no lost update on X. Under ADR 0090 as first written, every session read was tracked and
compared at commit, and a lossy adapter could turn a read into a write (see the evidence for the ADR 0090
amendment).

**An invocation never conflicts with its own writes.** When an invocation's own write to X is applied, its base for X
advances to the version that write produced, and that advanced base is persisted with the invocation. This covers an
invocation that commits a session change to X when it suspends and captures X when it completes, and ADR 0090 §6's
order within one commit (session changes first, captures last).

### 5. The commit rule

When a step commits a write to variable X:

1. **No base (blind write):** the write applies. Between unordered branches the last commit wins (D-8), and draft
   validation warns about unordered writes (spec 266).
2. **Base equals the current version:** the write applies.
3. **Stale base:** the write is a **lost update**, and the tracking follows the node's mode (decision 6):
   - **Default, light check.** The base version and the current version decide alone. The write applies, the last
     commit wins (D-8), and a lost-update warning is raised. A value that changed and returned to its base (for
     example 0 → 1 → 0) also raises the warning; no digest is compared.
   - **`Fault` opted in, exact check.** The current value's stored digest is compared with the base digest. If they
     are equal, the value moved and came back; nothing the writer relied on was lost. The write applies, and the write
     history records the version mismatch. If they differ, the step faults.

The rule is the same for inline and background steps, for captures and session writes, and for frames and rows (D-9).

D-8 holds for every write: an unordered write applies and the last commit wins, whether it is blind or based on a
stale read. A stale base adds the lost-update warning, unless the node opted in to `Fault`.

*Why.* The light check needs only the version a read saw and the version at commit, so the default stores no digest in
the read set and compares nothing. A false alarm, such as 0 → 1 → 0, costs little as a warning and a lot as a fault, so
the exact check runs only where a fault is the outcome. There, versions alone would fault on 0 → 1 → 0, where nothing
was lost, and comparing serialized values at every check (ADR 0090 §7) costs a serialization each time. Versions
first, with a comparison of two stored digests only on a mismatch, avoids both the false fault and the cost.

### 6. Lost-update handling: last write wins with a warning by default, `Fault` by opt-in

- **Default: last write wins, with a warning.** The write applies and the step continues. The engine raises a
  lost-update **warning**: an instance diagnostic shown in Studio and recorded in the write history (decision 13), not
  only a log line. It names the variable, the base version, the current version and both writers, the one whose value
  was overwritten and the one that wrote on the stale base, and it keeps the overwritten value so that it can be
  restored.
- **Opt-in: `Fault`.** A node or an activity type may opt in to `Fault` for strict read-modify-write: an activity type
  may declare it, and the workflow author may set it per node. Only these nodes use the exact check (decision 5). On a
  lost update the step faults with a diagnostic that names the variable, the base version, the current version and
  the writers. The faulted step's pending workflow changes do not apply (D-3); the incident and conflict records are
  retained under decisions 7 and 13.
- No other modes in this baseline. A merge policy is not part of this decision.

**Condition of the default.** The default holds only if decision 10 is proven for `External` and `ReplaySafe`
activities. If that proof fails, inline steps meet stale bases through Elsa's own hop structure, the plain two-branch
`count + 1` of Context item 1 gives 1 in ordinary inline workflows, and this default is reopened.

**Design-time pointer.** The publish-time warning for unordered writers (spec 266, ADR 0048 as amended) points the
author to the `Fault` opt-in for a step whose read-modify-write must be strict.

*Why.* Elsa runs what the engineer builds and gives guardrails and diagnostics, not restrictions: the simplest path is
the default, and advanced options stay possible. When branches run unordered, how they interfere is the workflow
author's design; the engine's job by default is to make an overwrite visible and repairable, which the warning and
the kept value do. Where a read-modify-write must never lose an update, the author opts in, and the step stops before
the wrong value spreads.

### 7. A fault goes through the existing incident path

A lost-update fault, on a node that opted in to `Fault` (decision 6), is an ordinary activity fault. It becomes an
incident and the workflow's incident strategy decides what happens (`FaultWorkflow`, `ContinueWithIncidents` or
`WaitForIntervention`; see `docs/runtime-fault-behavior.md`). Recovery uses the operator actions of decision 8:
retry, re-execute, or accept the recorded completion. No other recovery mechanism is introduced.

A lost-update fault is detected at commit, so the step's body has already run, and its external effects have already
happened. Rerunning the body repeats them; idempotency of external effects belongs to the workflow designer (D-2).
A retry keeps the `InvocationId`, so an activity that keys its idempotency on it deduplicates; a re-execution has a
new `InvocationId`, so its external effects repeat (decision 8). The incident therefore records the rejected
completion (under the existing protection rules), so that an operator can see what the body produced, for example a
payment id, and choose between the three actions of decision 8.

### 8. Operators retry, re-execute, or accept a recorded completion

Recovery and operator actions are kept apart:

- **Recovery** resumes the same invocation from durable state and keeps its committed snapshot and input read set:
  redelivery after a crash, before the step committed (ADR 0045 §3; committed input snapshots are retained on retry),
  and resume after a suspension (decision 11). A durably completed invocation is never run again
  because downstream scheduling was interrupted.
- **Retry** is an operator action on the same invocation. It keeps the `InvocationId`, the operation keys derived from
  it and the committed input snapshot, and starts a new attempt. It suits a transient failure, such as an HTTP 503,
  where an external service can deduplicate on the same idempotency key. After a lost-update fault a retry reads the
  same base and faults again while it remains stale and changed.
- **Re-execute** is an operator action that creates a new invocation: a new `InvocationId`, inputs materialized again
  from current committed state, and a new input read set, as Set, Merge and Reduce successors already do. Its external
  effects repeat on purpose. A reschedule through the alterations API
  ([ADR 0049](0049-runtime-alterations-use-snapshotted-atomic-jobs.md)), including one midway in the workflow, is a
  re-execution (D-2: "a re-run is a new execution with new values").
- **Accept recorded completion** is an operator action for a step whose body succeeded but whose commit conflicted
  (a lost-update fault). It does not run the body again. It revalidates the current state, scope, contracts and
  policies, then applies the complete recorded transition (write intents, captures, output changes and outcomes)
  atomically and duplicate-safely, as a recorded override. The original conflict and its resolution
  stay in the history. It does not apply to a body that faulted, and a returned result alone is not enough: the
  completion data it needs is retained under the existing protection rules.

In every case a new attempt starts with no session reads; it reads again through the session (ADR 0090 §6).

An operator chooses the action, and the API and Studio name them distinctly, so "retry" never silently means fresh
inputs.

This **amends ADR 0045 §3**. Its sentence "retries and resumptions retain one stable logical `InvocationId` and the
same input snapshot" becomes: recovery and retries of one invocation retain its `InvocationId` and snapshot; a
re-execution, including a reschedule, creates a new invocation with a new `InvocationId` and fresh inputs. Today's
code gives an operator retry a new id and keeps the snapshot (Context, item 3), which matches neither action; the
implementing spec splits it.

*Why.* A variable an operator corrected with `ModifyVariable` must be able to reach the step, and a lost-update fault
must be recoverable without reading the same outdated value again: that is re-execution. A transient failure should
be retried without changing inputs or idempotency keys: that is retry. And when the body's work is already done, for
example a payment made, the operator can keep it instead of repeating it: that is accepting the recorded completion.

### 9. A stale base is refreshed before background admission, under an explicit contract

Just before a background step is admitted, the coordinator compares its input read set with the current versions.
If a variable it read has changed since Start, the coordinator refreshes the inputs instead of sending the worker
outdated values. A refresh follows this contract:

- **Trigger.** Only a variable in the input read set that changed between Start and admission. Nothing else refreshes
  a committed snapshot; recorded snapshots stay fixed by default.
- **Identity.** A refresh is a new *evaluation* of the same invocation. The `InvocationId` stays; the evaluation gets
  its own sequence number, and idempotency keys derive from the evaluation. Input evaluation may call registered
  functions with external effects (D-5), so those effects repeat, deliberately: idempotency belongs to the designer
  (D-2).
- **Opt-out.** A node can turn refresh off. Its stale base is then handled at commit, as any other (decisions 5 and 6).
- **Recording.** The refreshed snapshot supersedes the first and links to it with the reason, for example
  "`count` v3 → v4". Both stay in the history, so inspection never shows values the body did not see, and the
  snapshot the body receives is immutable from then on (ADR 0045 §3). This follows the precedent of ADR 0045's
  per-pass re-materialization amendment (§3), which also keeps the pinned record when it re-materializes.

It covers only the gap between Start and admission. The long gap while the worker runs is covered by the commit rule
(decision 5). Inline steps do not need it once decision 10 holds.

*Why.* It prevents lost updates that can be seen coming: a warning by default, a fault where `Fault` is opted in. It
is not free of effects, because input evaluation can call effectful functions, so the contract makes the repetition
explicit and lets a node refuse it.

### 10. An inline step does not leave a gap between its reads and its body

The drain must not run another branch's work between an inline step's input materialization, body execution and
transition commit. How is left to the implementing spec. Fusion already proves that Schedule, Start and Invoke can
run in one dispatch for `ReplaySafe` leaves. The obstacle for `External` leaves is ADR 0032 R2: their
`ActivityAttemptClaimed` checkpoint is an immediate pre-activation flush (spec 095 FR-019). Required claim, ownership
fencing and durability checkpoints stay in
place. Several commits within one work item are allowed; the requirement is that no other branch's work item runs
between reading the inputs and committing the transition. No database transaction is held open while user code runs.
An inline step's bases are then current when its body runs, and inline steps meet the lost-update rule only through
suspension. The implementing spec must prove this sequence for both `External` and `ReplaySafe` activities. The
default of decision 6 depends on this proof.

*Why.* Today's inline lost update (Context, item 1) is a gap created by Elsa's own hop structure, not by the author's
design, and today it depends on the side-effect profile and persistence mode. Warnings about it would teach authors to
ignore warnings. Whether this can be done without breaking the claim and flush rules is to be verified in the spec.

### 11. A suspended invocation keeps its base

An invocation that suspends and later resumes keeps its committed snapshot and input read set (with any bases its own
writes advanced). The resumed step starts a fresh session with no session reads. Suspension does not automatically
refresh inputs or discard dependencies because time passed.

On resume, decision 5 applies to explicit writes: a write whose base is stale is a lost update, handled by the node's
mode (decision 6). A read alone produces no write and no lost update. The invocation's own writes still follow
decision 4. Long waits may make lost updates frequent; they do not introduce a different commit rule.

### 12. Operator alterations use per-variable versions

`ModifyVariable` records the variable's version when the operator requests the change, and applies only if it is
unchanged, replacing today's root-frame revision check. Today it can change workflow (root-frame) variables only;
whether it should reach container variables is outside this ADR. A later step that explicitly writes the same
variable against a stale base is a lost update under decision 5: by default the write applies with a warning that
keeps the operator's value so it can be restored, and under `Fault` the step faults. Reading the variable alone
produces no lost update. The operator's correction is therefore never silently overwritten.

### 13. Every write is recorded in the write history

Durable metadata is required for every applied write and conflict: the variable (scope and key), the writer (node,
invocation and attempt), the source (capture, intrinsic, session change, alteration or background result), the base
and new versions where applicable, the instance commit order and the lost-update outcome (warning or `Fault`).

Full historical values for inspection have configurable capture and retention under the existing protection and
redaction rules. Inspection distinguishes omitted, redacted and expired values from `null`.

Operational data is retained while required for recovery or repair, independently of optional inspection-value
capture and its retention settings: rejected-completion data, and the value a lost-update warning overwrote (decision
6). Turning inspection values off must not remove data still needed to accept a recorded completion or restore an
overwritten value.

This is the data source for variable write history in inspection, the commit-ordered journal and instance
comparison, and the runtime trace of overwrites, that is, spec S-6. It is not the persistence journal
proposed in [ADR 0084](0084-runtime-commit-journal.md), which is about commit cost.

The history may be derived from checkpoint commits rather than written as extra rows, provided it preserves every
intermediate write, including 0 → 1 → 0 within one buffered segment. Deriving history from only the final folded state
is insufficient. The implementing spec chooses the physical storage through measurements of large payloads and hot
loops under ADR 0032. Optional full-value capture does not make the durable metadata optional.

### 14. The whole variable is the unit of a write

A write replaces the whole value of a variable. Two branches that change different fields of one object both write the
whole object, so the second is a lost update under decision 5 (a warning by default). The guidance for authors is to
keep independent data in separate variables.

A merge policy is not part of this decision.

## Consequences

- **ADR 0045 §3.** Its retry sentence is amended (decision 8): attempts and resumptions of one invocation keep the id
  and the snapshot, as do operator retries; a re-execution, including a reschedule, is a new invocation with a new id
  and fresh inputs (decision 8). Before background admission a stale snapshot may be superseded by a new evaluation
  under the refresh contract (decision 9). A `While` pass that re-materializes under the per-pass re-materialization
  amendment records its own
  input read set for that evaluation.
- **ADR 0045 §4.** "Reads occur when the consumer invocation materializes its inputs" stays true, and those reads are
  now recorded with versions.
- **ADR 0089 decision 5, ADR 0090 §3.** They still refuse computed names (ADR 0045 §5 no longer does). D-7 changes that through the
  ADR 0089 amendment; decision 3 follows whatever that amendment allows.
- **ADR 0048 §4.** Commits stay totally ordered per execution (as amended with spec 266). Each variable gets its own
  version beside the frame revision. Captures write against the invocation's read set.
- **ADR 0090.** The session is the second surface over this mechanism (decision 1). Its base becomes the version,
  plus the digest where `Fault` is opted in (decision 5), its check covers input reads too (decision 4), and its
  `[VariableConflictPolicy]` becomes the lost-update mode of decision 6: a warning by default, with ADR 0090 §7's
  detect-and-fault as the `Fault` opt-in. The A-3 amendment of ADR 0090 proposes transactional writes from script and
  C# activity bodies as a general capability for native and imported workflows, rather than an Elsa 3 compatibility
  mode. This lifecycle holds for bindings and captures independently
  of which direct-access capabilities are enabled. Background-session capability requirements stay with ADR 0093.
- **Spec 266 (S-5).** Its publish-time warning covers all unordered writes, captures included, and points the author
  to the `Fault` opt-in (decision 6). At run time, D-8's last-commit-wins holds for every write; a stale base adds the
  lost-update warning.
- **The two-branch `count + 1`.** With inline steps gap-free (decision 10), two parallel inline branches give 2. Where
  two unordered branches still read a stale base, for example background steps or a suspension between read and
  write, each reading `count` and writing `count + 1` gives 1 with a lost-update warning. This is documented
  behaviour: the documentation says how to sequence the branches or aggregate their results, and when to opt in to
  `Fault`.
- **Merge and Reduce** still overwrite like Set. Giving them real semantics is separate work.
- **Cost.** One digest per applied write; a version per variable read, plus the stored digest for nodes that opt in
  to `Fault` (stored values, no hashing); one digest comparison on a version mismatch under `Fault` only; the write
  history, including the overwritten value a warning keeps. The read set enlarges every activity row, multiplied by
  loop iterations, and versions change the frame's content and the checkpoint fold. The spec measures all of this
  against large payloads and long loops.

## Required evidence for the implementing spec

- A test that reproduces today's inline lost update with `External` leaves (Context, item 1), failing before the change
  and passing after.
- Two branches that read 0 and both write 1: the second is a lost update (a warning by default), not a silent no-op.
- An invocation that writes X on suspension and captures X on completion does not conflict with itself.
- Read sets contain actual reads only, including computed names; an unread visible variable never causes a conflict.
- An input reads X at an older version, a session later reads a newer version, and a capture still uses the older
  input: the older dependency is retained and the stale write is detected. Tracking guarantees cover Elsa's
  instrumented access paths, not arbitrary host access.
- Across suspension, committed inputs and dependencies stay fixed and the resumed step gets a fresh session. A
  read-only step produces no lost update because a read variable changed; explicit writes follow decision 5 and the
  node's mode, and the invocation's own writes follow decision 4.
- A blind write follows D-8; an unchanged value writes nothing and keeps the version.
- By default a stale-base write applies, the last write wins, and the lost-update warning is an instance diagnostic in
  Studio and an entry in the write history that names the variable and both writers and keeps the overwritten value,
  which can be restored. The default uses the version check only and also warns on 0 → 1 → 0.
- `Fault` opted in per activity type and per node: 0 → 1 → 0 applies without a fault and is recorded; a real lost
  update faults.
- Spec 266's publish-time warning for unordered writers points to the `Fault` opt-in.
- The rule is identical for inline and background steps, captures and session writes, frames and rows.
- A retry keeps the `InvocationId` and snapshot; a re-execution and a reschedule materialize fresh inputs with a new
  id; crash redelivery and resume keep the snapshot. A successor that now goes through Start again does not activate
  its owned frames a second time.
- `ModifyVariable` then re-execute uses the corrected value; a step explicitly writing the same variable against a
  stale base follows decision 5. Reading the changed variable alone produces no lost update.
- Accept recorded completion applies the whole recorded transition atomically without running the body, revalidates
  first, is duplicate-safe, refuses a faulted body, and keeps the conflict and resolution in the history.
- The implementing spec defines and tests acceptance against the current invocation state, including cancellation,
  supersession and prior completion; valid target scopes and pinned contracts; availability of the complete protected
  transition; permission for the recorded override; and duplicate-safe writes and downstream scheduling. Repeated
  acceptance requests cannot schedule downstream work twice. Acceptance does not silently refresh inputs.
- A stale base is refreshed before background admission as a new evaluation with its own idempotency keys; the
  superseding snapshot is what the worker sees, the first stays in the history linked with the reason, and a node
  with refresh turned off meets the commit rule instead (a warning by default, a fault where `Fault` is opted in).
- A lost-update incident records the rejected result.
- Inline steps have no branch interleaving from input materialization through body execution and transition commit
  (decision 10), for `External` and `ReplaySafe` activities, with required claim, fencing and durability checkpoints
  kept. No database transaction is held open while user code runs. This proof is the condition of decision 6's
  default.
- The write history feeds spec S-6 under the existing protection rules. Durable metadata and intermediate writes
  survive checkpoint folding; inspection distinguishes omitted, redacted and expired values from `null`; required
  operational bases and completion data remain available with inspection-value capture disabled or expired.
- Cost measured against a large payload.

## Alternatives considered

**Background steps get no variable access (the starting point of the A-2 analysis).** Removes session reads from
background steps, but leaves the input-plus-capture lost update, inline and in the background, and the retry problem.
Not enough on its own. The general script/C# write capability proposed in the ADR 0090 amendment does not by itself establish background support;
background-session capability requirements stay with ADR 0093.

**Detect stale read-modify-write by value comparison only (ADR 0090 §7 as proposed).** Covers session reads only and
costs a serialization per checked variable. Its idea, detect and fault, is kept as the `Fault` opt-in.

**Fault by default, with a `LastWins` override.** Stops every lost update before the wrong value spreads. Not chosen
as the default: it turns the workflow author's design of unordered branches into a run-time failure, contradicts D-8
(a warning, never a refusal; last commit wins) and the principle of guardrails and diagnostics rather than
restrictions, and needs the exact check everywhere, where every false alarm costs a fault. It is kept as the `Fault`
opt-in.

**Last write wins with a warning, without an opt-in.** One behaviour and light tracking only. Not chosen: it drops a
strict read-modify-write mode that has real value, for example for a counter or a balance that must never lose an
update, while the opt-in costs the default nothing, because the exact check runs only where it is opted in.

The chosen design (decisions 5 and 6) gives guardrails and diagnostics by default and keeps the strict option
possible as an advanced choice.

**Per-variable locking or serialization.** Prevents lost updates by making later readers wait. Needs exact read and
write sets in advance (computed names make them "everything"), adds lease and deadlock handling, and removes the
parallelism background placement exists for.

**Order defined by design position.** Makes the final value of blind writes predictable from the canvas, but does not
prevent a stale read-modify-write, and changes D-8.

**Operations instead of values (increment, append).** Correct for parallel aggregation without faults, but only for
operations Elsa understands. A natural later step for Merge and Reduce; not a general answer.

**Field-level patches.** Rejected in ADR 0090: array patches by index land in the wrong place after another branch
changed the array, and merged values must be validated again.

**Three-way merge on conflict.** Not part of this decision (decision 14). The base digest and version identify the
base, but do not rebuild it, so such a policy would also have to retain the base value.
