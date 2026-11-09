# Feature Specification: Variable lifecycle model

**Feature Branch**: `claude/3052-s10-variable-lifecycle-f662ac`

**Created**: 2026-11-09

**Status**: Draft — **provisional**. Approval waits on the ADR PR listed under *Approval*.

**Initiative**: part of [#3052](https://github.com/elsa-workflows/elsa-foundation/issues/3052), spec row **S-10**,
analysis **A-2**, decisions **D-8** (narrowed) and **D-9**.

**Issues**: none filed yet. The S-10 intake on the #3052 board holds the checks this spec settles. This spec feeds the
write-history inspection work (#3290, #3291, #3292 → S-6) with its data model but does not build those surfaces.

**Input**: A variable's reads and writes share one versioned lifecycle. Every variable has its own version and a value
digest; every invocation records what it actually read; a write carries the base it was built on; a write on a stale,
changed base is a lost update that faults by default, with an explicit last-wins override. Operator retry, re-execution
and accept-recorded-completion are distinct actions; background steps run on fresh inputs; inline steps leave no gap
between reading and committing; suspended steps keep their base; and every applied write and conflict is recorded.

## Approval

> **Provisional.** This spec may be drafted now (see [initiatives](../../docs/contributing/initiatives.md#phases)).
> It cannot be approved until every ADR it applies is agreed. If an ADR changes before agreement, this spec follows it,
> and findings against the ADR flow back to its PR as comments (recorded here under *Open questions*).

**Which ADR text this spec applies.** ADR 0092 exists as the draft on #3411 with @sfmskywalker's review clarifications
stacked in #3778. This spec applies **#3778's text** (the `#3411` head with #3778 merged), at the commits named below,
as if it were agreed. Where ADR 0092 amends ADR 0045, ADR 0048 or ADR 0090, this spec applies those amendments as ADR
0092 states them; otherwise the versions on the initiative branch apply. The answers in *Open questions* hold only on
that basis: if the draft changes, they are rechecked.

| ADR PR | What this spec takes from it | Draft applied | State on 2026-11-09 |
|---|---|---|---|
| [#3411](https://github.com/elsa-workflows/elsa-foundation/pull/3411) + [#3778](https://github.com/elsa-workflows/elsa-foundation/pull/3778), **ADR 0092** (variable reads and writes share one versioned lifecycle) | Decisions 1–14: two surfaces/one mechanism, per-variable version + digest, the read set (input + session), read-modify-write base, the commit rule, Fault/LastWins policy, the incident path, the retry/re-execute/accept split (amends ADR 0045 §3), the background-admission refresh contract (D-9), the no-inline-gap guarantee, the suspended-keeps-base contract, `ModifyVariable` per-variable versions, and the write-history data model | #3411 head with #3778 merged | ADR 0092 is `proposed`. #3778 applies @sfmskywalker's clarifications across all 12 reviewer questions and is a draft stacked on #3411, not yet merged. Awaiting @sfmskywalker's and @FransVanEk's agreement on the final text |

**Decisions settled with the coordinator while drafting (recorded here, to be closed in the ADR text on #3411):**
- **Reviewer Q3 / open question 3 (strict-only vs. `Fault`+`LastWins`).** Decided: `Fault` default with a `LastWins`
  override per activity type and per node — @sfmskywalker's position ("detect and fault, with an override"), agreed by
  @j03y-nxxbz. This spec builds it. The ADR should strike the "or strict-only?" framing in decision 6.
- **Reviewer Q8 / open question 8 (suspended keeps its base).** Agreed as @sfmskywalker clarified decision 11:
  resume keeps the committed snapshot and input read set, starts a fresh session, does not auto-refresh because time
  passed, and only an explicit write against a stale, changed base faults. No resume-side refresh is built here (D-9
  background admission stays the only refresh). The ADR should strike the "is this right?" framing in decision 11.

**Related, not blocking:**
- [#3425](https://github.com/elsa-workflows/elsa-foundation/pull/3425) (A-3, ADR 0090 amendment) proposes general
  transactional script/C# writes. This lifecycle holds for bindings and captures independently of which direct-access
  capabilities are enabled; it does not depend on #3425.
- **ADR 0093** ([#3412](https://github.com/elsa-workflows/elsa-foundation/pull/3412)) decides *which* activities may run
  in the background (eligibility → S-11). This spec builds the background-admission **refresh** (D-9) that applies once an
  activity is admitted; it does not decide eligibility.
- **Spec 266** ([#3286](https://github.com/elsa-workflows/elsa-foundation/pull/3286), S-5) keeps its publish-time
  warning for unordered writes. At run time, D-8's last-commit-wins now applies to **blind** writes only; this spec
  narrows it.
- **Spec S-6** (#3290–#3292) builds the inspection surfaces over the write history this spec records.

**Approval plan**: the initiative's approvers, @sfmskywalker and @FransVanEk (technical architects), approve the final
text; @j03y-nxxbz is the author/coordinator and records agreement in a PR comment (no self-approval).
**Implementation**: not before every spec of #3052 is defined and the initiative branch lands, as #3052 agreed.

## Rule and boundary

**Rule**: a write is judged against the read it was built on. Nothing a writer relied on is overwritten silently.

**Terms** (from ADR 0092):
- *Version*: a per-variable counter; each applied write that changes the value increments it. A value *digest* over
  ADR 0091's canonical form is stored beside the version, computed once per applied write.
- *Read set*: the variables an invocation actually read, each with the version and digest it saw. Two parts: the
  *input read set* (reads during input materialization, committed with the input snapshot, carried to background), and
  the *session reads* (reads by the body through the variable session, recorded per attempt).
- *Base*: for a write to X, the version and digest of X in the writer's read set. A write to a variable the invocation
  did not read is a *blind write*, with no base.
- *Lost update*: a write whose base version is no longer current **and** whose current value's digest differs from the
  base digest.
- *Write history*: the record of every applied write and of every conflict and its resolution.

**In scope**: per-variable version and digest in frames and rows; the input and session read sets and how bases are
represented and advanced; the commit rule for inline and background steps, captures and session writes, frames and
rows; the `Fault`/`LastWins` lost-update policy and its routing through the existing incident path; the operator
retry / re-execute / accept-recorded-completion split and the fix to successor staging (including the amendment of
ADR 0045 §3); the background-admission refresh contract (D-9); the no-inline-gap guarantee for `External` and
`ReplaySafe` inline leaves; the suspended-keeps-base contract; `ModifyVariable` per-variable versioning; and the
durable write-history data model that S-6 reads.

**Out of scope** (named so the boundary is explicit): the inspection surfaces of S-6 (#3290–#3292); background
*eligibility* criteria (ADR 0093 → S-11); the conservative JSON-object merge policy (ADR 0092 decision 14, a planned
follow-up ADR); real `Merge`/`Reduce` semantics (they still overwrite like `Set`); general transactional script/C#
writes (A-3, #3425); and the publish-time warning changes (spec 266, S-5).

## User Scenarios & Testing *(mandatory)*

### User Story 1 — A parallel read-modify-write is caught, not lost (Priority: P1)

Two parallel branches each take `count` as an input and capture `count + 1` back. Today, with `External`
(unclassified) activities, both read the same value before either commits and one increment is lost, silently. A
workflow author expects either the right total or a visible failure, never a silent wrong number.

**Why this priority**: this is the defect A-2 exists to fix and the reproduction that proves ADR 0092 works in code.
Everything else in the spec builds on the version/read-set/commit machinery this story exercises.

**Independent Test**: a test with two parallel `External` leaves reading `count` and capturing `count + 1`. It fails
before the change (result `1`, no diagnostic) and, after, the second commit is a lost update that faults by default
with a diagnostic naming the variable, both versions and the writers. A matched test with two branches that both read
`0` and both write `1` shows the second is a lost update, not a silently discarded no-op.

**Acceptance Scenarios**:

1. **Given** two parallel `External` branches that read `count = 0` and capture `count + 1`, **When** both commit,
   **Then** the second commit faults as a lost update (default policy) and the increment is not silently lost.
2. **Given** a variable at version *v* read by a step, **When** another writer advances it and the step then writes on
   base *v* with a different value, **Then** the write is a lost update.
3. **Given** a value that moved `0 → 1 → 0` between a step's read and its commit, **When** the step writes, **Then** the
   write applies without a fault and the version mismatch is recorded.
4. **Given** a step that never read X, **When** it writes X, **Then** the write is blind (D-8, last commit wins) and
   never faults as a lost update.

---

### User Story 2 — An operator's correction reaches the step (Priority: P1)

An operator corrects a workflow variable with `ModifyVariable` after a step faulted, then recovers the step. They
expect the corrected value to be used. Today an operator "retry" is given a new invocation id but copies the old input
snapshot, so the correction never reaches the retried step.

**Why this priority**: the retry/re-execute confusion is a correctness defect called out in A-2 and the ADR's evidence
list; it also defines the recovery contract every other story relies on.

**Independent Test**: a test that corrects X with `ModifyVariable`, then re-executes the step, and asserts the step
sees the corrected X; a second test that retries the same invocation and asserts the snapshot and `InvocationId` are
kept; a third that accepts a recorded completion without re-running the body.

**Acceptance Scenarios**:

1. **Given** a variable corrected with `ModifyVariable`, **When** the operator **re-executes** the step, **Then** the
   step materializes fresh inputs with a new `InvocationId` and uses the corrected value.
2. **Given** a transient failure, **When** the operator **retries**, **Then** the same `InvocationId` and committed
   input snapshot are kept and a new attempt runs with no session reads.
3. **Given** a step whose body succeeded but whose commit was a lost-update fault, **When** the operator **accepts the
   recorded completion**, **Then** the recorded transition is applied atomically without re-running the body, after
   revalidation, duplicate-safely, with the original conflict kept in the history.
4. **Given** a successor that now re-enters input materialization, **When** it starts, **Then** its owned frames are
   not activated a second time.

---

### User Story 3 — An author can accept last-wins explicitly (Priority: P2)

An author of an aggregator, or a specific node, knows that overwriting is intended and does not want a fault. They set
`LastWins` and the overwrite applies and is recorded.

**Why this priority**: without the override, intended overwrites would force authors into incident handling; with it,
the strict default stays usable. It depends on US1's commit machinery.

**Independent Test**: a `LastWins` activity type and a per-node `LastWins` setting each let a stale-base write apply,
and each applied overwrite appears in the write history as a recorded `LastWins` resolution.

**Acceptance Scenarios**:

1. **Given** an activity type that declares `LastWins`, **When** it writes on a stale, changed base, **Then** the write
   applies and is recorded as `LastWins`, never silently.
2. **Given** a per-node `LastWins` setting, **When** that node writes on a stale, changed base, **Then** the write
   applies and is recorded.
3. **Given** the default policy, **When** a lost update occurs, **Then** the step faults through the ordinary incident
   path and the workflow's incident strategy decides what happens.

---

### User Story 4 — Inline steps lose nothing to the engine's own hop structure (Priority: P2)

An author places two activities in parallel without marking any concurrency policy. They expect the engine's internal
scheduling not to create a lost update that their design did not. Inline steps must leave no gap between reading inputs
and committing.

**Why this priority**: today's inline lost update is created by Elsa's hop structure, not the author's design, so a
warning would be noise; the gap must be closed. It depends on US1 and is the spec's main feasibility risk (OQ7).

**Independent Test**: for both `External` and `ReplaySafe` inline leaves, a test shows no other branch's work item runs
between input materialization, body execution and the transition commit, while the claim, ownership-fencing and
durability checkpoints still occur and no database transaction is held open around user code.

**Acceptance Scenarios**:

1. **Given** an inline `ReplaySafe` leaf, **When** it runs, **Then** no other branch's work item interleaves between its
   input read and its transition commit (fusion already provides this).
2. **Given** an inline `External` leaf, **When** it runs, **Then** no other branch's work item interleaves between its
   input read and its transition commit, and its claim/fence/durability checkpoints still occur as intermediate commits
   within one work item.
3. **Given** an inline step with no suspension, **When** it commits, **Then** its bases are current and it meets the
   lost-update rule without faulting.

---

### User Story 5 — A background step runs on fresh inputs (Priority: P2)

A step placed in the background reads its inputs on the coordinator before admission. If a variable it read changed
before the worker starts, the worker should see the fresh value rather than fault predictably at commit.

**Why this priority**: it prevents lost-update faults that can be seen coming, within an explicit, effect-aware
contract. It depends on US1's read sets and versions.

**Independent Test**: a test where a variable in the input read set changes between Start and admission; the coordinator
refreshes the inputs as a new evaluation (same `InvocationId`, its own sequence and idempotency keys), the worker sees
the refreshed snapshot, the first snapshot stays in the history linked with the reason, and a node with refresh turned
off instead faults or applies `LastWins` at commit.

**Acceptance Scenarios**:

1. **Given** a background step whose input variable changed between Start and admission, **When** it is admitted,
   **Then** its inputs are refreshed as a new evaluation and the worker sees the current value.
2. **Given** a node with refresh turned off, **When** its input variable changed, **Then** no refresh happens and the
   stale base is handled at commit under the commit rule.
3. **Given** a refresh, **When** it occurs, **Then** the superseding snapshot is immutable from then on and the
   superseded snapshot stays in the history linked with the reason (for example "`count` v3 → v4").

---

### User Story 6 — A suspended workflow resumes safely (Priority: P3)

A workflow waits (a timer, an event) and resumes later. It keeps the inputs it committed and the dependencies it read;
it does not silently refresh because time passed, and it does not fault merely because a variable it read changed.

**Why this priority**: long-running workflows are common; the contract must be defined, but it is a direct application
of the commit rule rather than new machinery.

**Independent Test**: a resumed step keeps its committed snapshot and input read set, starts a fresh session, does not
fault on a read-only change, and faults only on an explicit write against a stale, changed base.

**Acceptance Scenarios**:

1. **Given** a suspended invocation, **When** it resumes, **Then** it keeps its snapshot and input read set (with any
   bases its own writes advanced) and starts a fresh session with no session reads.
2. **Given** a variable the resumed step only read changed during the wait, **When** the step commits no write to it,
   **Then** there is no lost-update fault.
3. **Given** the resumed step writes a variable on a stale, changed base, **When** it commits, **Then** the commit rule
   applies exactly as for an inline step.

---

### User Story 7 — Every write is on the record (Priority: P3)

An operator or author needs to see who wrote a variable, in what order, and how any conflict was resolved. This spec
records the data; S-6 presents it.

**Why this priority**: the data must be captured from the first implementation or it cannot be reconstructed later; the
presentation is a separate spec.

**Independent Test**: after a run with captures, session writes, an alteration and a resolved conflict, the durable
write history holds the required metadata for each, preserves every intermediate write (including `0 → 1 → 0` within one
buffered segment), and distinguishes omitted, redacted and expired values from `null`.

**Acceptance Scenarios**:

1. **Given** a sequence of writes to X, **When** the run completes, **Then** the write history records each write's
   variable, writer, source, base and new versions, instance commit order and conflict resolution.
2. **Given** inspection values turned off or expired, **When** an operator accepts a recorded completion or an enabled
   merge runs, **Then** the operational data it needs is still available.
3. **Given** history derived from checkpoint commits rather than extra rows, **When** a value moved `0 → 1 → 0` within
   one buffered segment, **Then** both intermediate writes are still preserved.

### Edge Cases

- An invocation writes X on suspension (session change) and captures X on completion: it must not conflict with its own
  earlier write; its base for X advances to the version its own write produced and is persisted with the invocation.
- A newer session read of X returns a value a capture does not depend on: the capture still writes against the older
  input dependency it was built on, and the newer read does not let a stale calculation pass the commit rule.
- A computed variable name (`getVariable(name)`) is read: the read is observed at run time and enters the read set once
  ADR 0089's D-7 amendment allows computed names; until then only resolvable names occur and are recorded.
- Arbitrary host access outside Elsa's instrumented variable-access paths is not tracked and is not presented as covered
  by the read set.
- An unchanged value is written (same digest): the write applies nothing and the version stays, but two branches that
  both read `0` and both write `1` are not hidden as no-ops — the second is a lost update.
- A background result keeps its admission time; result ordering may differ between the EF queue (orders by it) and the
  in-memory queue (does not). This spec records the behavior; any change is out of scope (S-11 intake item 5).

## Requirements *(mandatory)*

### Functional Requirements

**Versions, digests and read sets**

- **FR-001**: Each variable MUST carry its own version (a counter) and a value digest over ADR 0091's canonical form,
  stored in its frame or row, each computed once when a write is applied, independent of other variables in the frame.
- **FR-002**: During input materialization, the engine MUST record every variable an input binding or expression
  actually read, with the version and digest seen, as the input read set; it MUST be committed with the input snapshot
  and travel with background admission.
- **FR-003**: During execution, the engine MUST record every session read in the attempt's session reads; session reads
  MUST NOT be carried to a new attempt.
- **FR-004**: A variable that was visible but not read MUST NOT appear in any read set. Tracking is guaranteed only for
  Elsa's instrumented variable-access paths (bindings and the session API).

**Base and read-modify-write**

- **FR-005**: When a step writes a variable in its read set, the write's base MUST be the recorded version/digest for
  that variable, whatever surface read it or writes it (input read + capture; session read + session write).
- **FR-006**: The input read set's bases MUST remain immutable for the invocation; a later session read MUST NOT erase
  an older input dependency still relied on. A write MUST take the base of the read it was built on.
- **FR-007**: An invocation MUST NOT conflict with its own writes: when its write to X applies, its base for X advances
  to the version produced and is persisted with the invocation (covering a session change on suspension and a capture
  on completion, in ADR 0090 §6's order).
- **FR-008**: A read alone MUST NOT produce a write; only an explicit write (a capture, a `Set`, or a session write)
  enters the read-modify-write rule.

**The commit rule**

- **FR-009**: On commit of a write to X, the engine MUST apply the commit rule: blind write → apply (D-8, last commit
  wins); base equals current version → apply; base older but current digest equals base digest → apply and record the
  version mismatch; base older and digest differs → lost update.
- **FR-010**: The commit rule MUST be identical for inline and background steps, captures and session writes, and
  frames and rows.

**Lost-update policy and incidents**

- **FR-011**: The default lost-update policy MUST be `Fault`, with a diagnostic naming the variable, the base version,
  the current version and the writers; the faulted step's pending workflow changes MUST NOT apply.
- **FR-012**: `LastWins` MUST be available as an override per activity type and per node; a `LastWins` overwrite MUST
  always be recorded in the write history and surfaced in inspection, never silent.
- **FR-013**: A lost-update fault MUST go through the existing incident path and strategy; no new recovery mechanism is
  introduced. The incident MUST record the rejected completion under the existing protection rules.

**Operator actions and successor staging**

- **FR-014**: The engine MUST provide three distinct operator actions — **retry** (same `InvocationId`, kept input
  snapshot, new attempt), **re-execute/reschedule** (new `InvocationId`, inputs materialized from current committed
  state, new input read set), and **accept recorded completion** (apply the recorded transition atomically without
  re-running the body) — named distinctly in the API and Studio.
- **FR-015**: Successor staging MUST be fixed so an operator retry no longer silently copies the old snapshot onto a new
  invocation id; a re-execution MUST materialize fresh inputs. This amends ADR 0045 §3 as ADR 0092 decision 8 states.
- **FR-016**: A successor that re-enters input materialization MUST NOT activate its owned frames a second time.
- **FR-017**: Accept-recorded-completion MUST revalidate current state, scope, pinned contracts and policies; apply the
  complete recorded transition (write intents, captures, output changes, outcomes) atomically and duplicate-safely;
  refuse a faulted body; keep the conflict and its resolution in the history; and not silently refresh inputs. Repeated
  acceptance requests MUST NOT schedule downstream work twice.

**Background admission refresh (D-9)**

- **FR-018**: Before a background step is admitted, the coordinator MUST compare its input read set with current
  versions and, for any variable that changed since Start, refresh the inputs as a new **evaluation** of the same
  invocation (same `InvocationId`, its own sequence number, idempotency keys derived from the evaluation).
- **FR-019**: A node MUST be able to turn refresh off; its stale base is then handled at commit under the commit rule.
- **FR-020**: A refresh MUST supersede the first snapshot, link to it with the reason, keep both in the history, and the
  snapshot the worker receives MUST be immutable thereafter. Refresh MUST cover only the Start-to-admission gap.

**No inline gap (D-10)**

- **FR-021**: The engine MUST NOT run another branch's work item between an inline step's input materialization, body
  execution and transition commit, for both `External` and `ReplaySafe` leaves, while preserving the required claim,
  ownership-fencing and durability checkpoints and holding no database transaction around user code. (Feasibility for
  `External` is *Open question A*; the spec proves it or feeds the finding back to #3411.)

**Suspension (D-11)**

- **FR-022**: A suspended invocation MUST keep its committed snapshot and input read set (with bases its own writes
  advanced), resume with a fresh session, not auto-refresh because time passed, and fault on resume only for an
  explicit write against a stale, changed base.

**Operator alterations (D-12)**

- **FR-023**: `ModifyVariable` MUST record the variable's version when requested and apply only if it is unchanged,
  replacing today's root-frame revision check; reading the variable alone MUST NOT fault.

**Write history (D-13)**

- **FR-024**: The engine MUST durably record, for every applied write and every conflict, the variable (scope + key),
  the writer (node, invocation, attempt), the source (capture, intrinsic, session change, alteration, background
  result), the base and new versions where applicable, the instance commit order, and the conflict resolution.
- **FR-025**: Full historical values for inspection MUST be configurable (capture and retention) under the existing
  protection and redaction rules; inspection MUST distinguish omitted, redacted and expired values from `null`.
- **FR-026**: Operational base snapshots and rejected-completion data MUST be retained while required for an enabled
  merge or recovery, independently of inspection-value capture and its retention; turning inspection values off MUST
  NOT remove data still needed to accept a recorded completion.
- **FR-027**: The write history MUST preserve every intermediate write (including `0 → 1 → 0` within one buffered
  segment); deriving it from only the final folded state is insufficient. The physical storage (extra rows vs. derived
  from checkpoint commits) is chosen by measurement (*Open question B*).

### Key Entities

- **Variable version**: the per-variable counter plus value digest, stored with the variable in its frame or row.
- **Read set**: the input read set (committed with the input snapshot, travels to background) and the session reads
  (per attempt), each entry a (variable, version, digest).
- **Base**: the (version, digest) a write is judged against; absent for a blind write.
- **Write-history record**: durable metadata for one applied write or conflict, with optional retained value and
  operational data.
- **Input snapshot / evaluation**: the immutable materialized inputs of an invocation; a refresh creates a new
  evaluation that supersedes and links to the prior snapshot.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A parallel read-modify-write that silently loses an update today produces either the correct result or a
  visible lost-update fault — never a silent wrong value — across `External` and `ReplaySafe` activities and both
  persistence modes. (0 silent lost updates in the reproduction suite.)
- **SC-002**: An operator correction via `ModifyVariable` reaches a re-executed step in 100% of cases; a retry keeps the
  same inputs in 100% of cases; the two actions are never conflated.
- **SC-003**: Every applied write and every conflict appears in the write history with its required metadata;
  reconstructing who wrote a variable, in what order, and how conflicts resolved succeeds for 100% of writes in the
  test suite, including `0 → 1 → 0` sequences.
- **SC-004**: Inline steps show no branch interleaving from input materialization through transition commit for both
  activity profiles, with claim/fence/durability checkpoints intact and no database transaction held around user code.
- **SC-005**: The added per-variable version/digest, read sets and write history keep variable commit within an agreed
  overhead budget under a large-payload, hot-loop benchmark (threshold set with the measurement in *Open question B*).

## Open questions

These are the questions ADR 0092 delegates to this spec. Each is written to the ADR's proposed answer and recorded
against the #3411 thread; evidence found here flows back as a comment there.

- **Open question A — no-inline-gap feasibility for `External` leaves** (ADR 0092 decision 10 / reviewer Q7). The spec's
  hypothesis: run an `External` inline leaf's Schedule/Start/Invoke in one work item with the `ActivityAttemptClaimed`
  claim/flush as intermediate commits, so no *other branch's* work item interleaves while the claim, fencing and
  durability checkpoints stay in place and no database transaction wraps user code. A proof-of-concept in the plan
  confirms this against ADR 0032 R2 (spec 095 FR-019). If it cannot be done without weakening those rules, that is a
  finding on #3411 that reopens decision 10.
- **Open question B — write-history physical storage** (ADR 0092 decision 13 / reviewer Q12). Derived from checkpoint
  commits vs. extra rows, chosen by measurement against large payloads and hot loops under ADR 0032, under the fixed
  invariants that durable metadata is mandatory and every intermediate write is preserved. Mandatory metadata is never
  optional regardless of the choice.

## Assumptions

- The baseline is #3778's text for ADR 0092; the amendments it makes to ADR 0045 §3/§4, ADR 0048 §4 and ADR 0090
  §2/§6/§7 apply as stated. If #3778 is revised before agreement, this spec is rechecked.
- Reviewer questions 3 and 8 are resolved as recorded under *Approval* (Fault+LastWins; suspended keeps base, no resume
  refresh). The remaining reviewer questions resolve as ADR 0092 proposes; where they do not, this spec is rechecked.
- Where the engine cannot tell which read a write derived from, the write binds to the **oldest in-use dependency** —
  the conservative direction, since a false conflict is visible and overridable while a missed one is silent. (Spec
  engineering determination; no ADR position.)
- Read-set storage ships straightforwardly (per invocation / per attempt) and is compacted only if the *Open question B*
  measurement requires it; it is not pre-optimized for loop iteration count. (Spec engineering determination.)
- `Merge` and `Reduce` still overwrite like `Set`; the conservative JSON-object merge (decision 14) and general
  script/C# writes (A-3) are out of scope and tracked separately.
