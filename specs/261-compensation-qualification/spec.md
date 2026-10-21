# Feature Specification: Qualify native compensation and assess the Elsa 3 follow-on

**Feature Branch**: `agent/claude/3048-compensation-qualification`

**Created**: 2026-10-20

**Status**: Draft — M5 design packet under [#3048](https://github.com/elsa-workflows/elsa-foundation/issues/3048). The owner decided OD17–OD20 on 20 October 2026, each as the recommended option A.

**Program**: [Native compensation #3035](https://github.com/elsa-workflows/elsa-foundation/issues/3035) → [Epic #3040](https://github.com/elsa-workflows/elsa-foundation/issues/3040) → [Feature #3048](https://github.com/elsa-workflows/elsa-foundation/issues/3048) (M5). Builds on [spec 250](../250-native-compensation/spec.md) (M1), [spec 251](../251-compensation-uncertainty/spec.md) (M2), [spec 258](../258-compensation-nesting/spec.md) (M3) and [spec 260](../260-compensation-operator-actions/spec.md) (M4). Their requirements and owner decisions still hold, except where this spec says otherwise. The M4 Studio slice is open as [#3140](https://github.com/elsa-workflows/elsa-foundation/pull/3140); this packet reads it from `origin/agent/claude/3047-compensation-operator-studio` and assumes it merges before PR 2 starts.

**Input**: Qualify compensation delivery and document the Elsa 3 follow-on (#3048): prove the integrated candidate end to end in Studio and in every supported persistence configuration, record restart, migration, serialization, pinning, retention and recovery evidence with exact revisions, bring guides and samples in line with shipped behaviour, resolve review findings, make supported and unsupported combinations explicit, and assess what the Elsa 3 compensation request (elsa-core#4015) can reuse. It also closes the work earlier milestones deferred to M5: handler retry backoff (spec 251 OD7), the replay-safe publication warning (spec 250 FR-014) and the broken M1 live journey (#3142).

Releases and deployment are outside this program (owner, [program charter](../../docs/program-goals/native-compensation.md)). Nothing in this spec claims a package release, a published image or a deployment.

## Owner decisions (20 October 2026)

The owner chose option A for each of OD17–OD20 below.

Each question lists its options and the recommended one. The packet is written for the recommendation; [plan.md](plan.md) names the tasks each other option would change.

### OD17 — Handler retry backoff: shape and default

Today a failed compensation handler runs again at once, up to `handlerMaxAttempts` (spec 251 OD7). M5 adds a durable wait between attempts ([research R1–R4](research.md)). Which shape, and what default?

| Option | Shape | Default |
|---|---|---|
| **A (recommended)** | Three scope inputs: `handlerRetryDelay` (first wait), `handlerRetryBackoffMultiplier` (growth per further attempt), `handlerRetryMaxDelay` (cap). No jitter. | `handlerRetryDelay` `00:00:00`, so scopes retry at once, as today, until the author sets a delay. Multiplier `2`, cap `00:05:00`. |
| B | As A | A non-zero default for every scope: `00:00:05`, ×2, cap `00:01:00`. |
| C | One fixed `handlerRetryDelay` input, no growth | `00:00:00` |
| D | No backoff in this program; record it as a known limit | — |

**Recommendation: A.** No workflow changes behaviour unless its author opts in, which keeps every M1–M4 journey and the user guide's "runs it again straight away" true. A small default delay would also be imprecise: a due retry runs at the next outbox sweep, every 10 seconds by default (R3), so a 5-second default would really be 5 to 15 seconds.

### OD18 — Which persistence providers are qualified for compensation

Every compensation journey today runs on SQLite only (`Activities.Compensation.Tests` `CompensationHost`, the Workbench owned-process tests). The runtime stores themselves are proven on PostgreSQL, SQL Server and MySQL by the nightly `runtime` EF container suite, but no compensation journey runs there ([research R8](research.md)). Which providers does M5 call supported?

| Option | Qualified | Evidence |
|---|---|---|
| **A (recommended)** | SQLite, PostgreSQL, SQL Server, MySQL, in the three persistence modes. In-memory stores: development and tests only, no restart claim. Oracle and multi-node placement: not qualified. | SQLite: the full journey matrix on every PR (as today). The three server providers: a new nightly EF container suite `compensation` that runs a representative subset of journeys, with restarts, in each mode. |
| B | SQLite only; server providers "expected to work, not qualified" | Today's evidence only. |
| C | All four providers with the full journey matrix | Every journey on every provider nightly (roughly 4× today's compensation run time per provider). |

**Recommendation: A.** The provider is where checkpoint, outbox, claim and JSON column behaviour differ, so a support claim needs at least one restart journey per provider and mode. A subset keeps the nightly budget in line with the [EF suite cadence report](../../docs/reports/ci/ef-suite-value-cadence.md); the full matrix adds no case the subset and the SQLite matrix do not already cover.

### OD19 — Where the samples live and what they are

#3048 asks for samples that match demonstrated behaviour. The repository has `samples/` (Nuplane packages loaded into the Workbench) and `e2e-tests/` (REST scripts); compensation has neither.

| Option | Sample | Proof |
|---|---|---|
| **A (recommended)** | A Nuplane sample package `samples/Elsa.Samples.Compensation`: a small file-backed reservation participant that follows the participant contract (spec 251 FR-220), plus three workflow definitions as JSON (reservation saga, nested scope with Confirm, operator recovery) and a README walkthrough. | A Workbench owned-process test loads the package and runs the three definitions on every PR (`Build & test`), as the test-only Reservations witness does today. |
| B | REST scripts under `e2e-tests/compensation/` with built-in activities only (Write Line, Fault) | Run by hand. |
| C | Snippets in the user guide only | None beyond the docs build. |

**Recommendation: A.** Only a participant that dedupes on the operation key shows what makes compensation safe: repeats after a crash, uncertain outcomes and a late request refused after its undo. Built-in activities cannot show any of that. The Workbench test witness already proves the package shape; the sample is a clean copy without the test-only hooks, proven in CI so it cannot drift from shipped behaviour.

### OD20 — The replay-safe warning (spec 250 FR-014): in M5, and on what

FR-014 says publication warns, without blocking, when a step or handler activity is declared `ReplaySafe`. It was deferred because design-time validation cannot see an activity's side-effect profile. It is not implemented ([research R5](research.md)). Two facts shape it: Write Line and Fault are `ReplaySafe`, and so are every composite (Sequence, Flowchart, For Each, While, For, Parallel, Switch, If) and the compensation scope itself.

| Option | Warns on |
|---|---|
| **A (recommended)** | In M5, as a non-blocking warning on (1) a step that has a compensation and whose activity is a `ReplaySafe` leaf, and (2) a compensation handler that is a `ReplaySafe` leaf. Composites, scopes, Confirm and Compensate never warn. |
| B | In M5, literally as FR-014 says: every `ReplaySafe` step or handler activity, composites included. |
| C | Not in this program; record it as a known limit. |

**Recommendation: A.** A step without a compensation has nothing to undo, and a composite's profile describes the container, not the work inside it, so warning on them would fire on almost every scope and teach authors to ignore it. Option A warns exactly where a deferred claim can lose an effect that recovery is meant to undo.

## Defaults applied without asking

Recorded so they can be challenged:

- **Live browser journeys stay opt-in.** They run by hand against a named Studio and Workbench revision (`ELSA_STUDIO_URL`), as in M1–M4, and their results are recorded with both revisions. No CI job starts a Studio and Workbench pair for them.
- **Operator retries never wait.** `RetryFaultedActivity/1` runs the handler at once; the backoff applies to the scope's automatic retries only.
- **Backoff waits are not interruptible.** While a scope waits between attempts there is no blocking incident, so Studio offers no action and an operator cannot skip the wait. Cancelling the run still stops it (no handler starts).
- **#3130 is fixed in M5.** The diagnostics page's always-true `canManage` was found in the M4 review (spec 260 research R12); #3048 asks for review findings to be resolved.
- **Reports live under `docs/reports/`.** The qualification record is `docs/reports/native-compensation-qualification.md`; the Elsa 3 assessment is `docs/reports/native-compensation-elsa3-follow-on.md`.
- **Post-merge evidence goes on the issue.** After the last PR merges, the `main` CI run and a dispatched nightly EF run on the merge commit are recorded as a comment on #3048 (repo policy: [merging](../../docs/contributing/merging.md#post-merge-gates)); the qualification record links that comment instead of being edited by a status-only PR.

## User Scenarios & Testing

### User Story 1 - A failing compensation waits before it runs again (Priority: P1)

*Cancel delivery* fails because the delivery service is restarting. The author set the scope's **Handler retry delay** to 30 seconds. The scope records the failed attempt, waits 30 seconds, then 60 seconds, and runs the compensation again each time with the same operation key. The service is back on the third attempt; recovery continues. The run view shows "Retry scheduled (1st) · Attempt 2 of 3 · after 10:42:30" while it waits. The backend restarts during the wait; the retry still runs once, after the due time.

**Why this priority**: Spec 251 OD7 deferred it to M5. Immediate retries rarely outlast a transient outage, so the scope escalates to an operator for failures that would have cleared on their own.

**Independent Test**: Backend journeys BK1–BK6 in [quickstart](quickstart.md) in Durable, Balanced and Ephemeral with a fake clock and a host kill while waiting; a Vitest for the new state.

**Acceptance Scenarios**:

1. **Given** a scope with `handlerRetryDelay` 0 (the default), **then** a failed handler runs again at once, exactly as in M2–M4.
2. **Given** `handlerRetryDelay` d > 0, multiplier m and cap c, **when** attempt n (n ≥ 1) of a handler fails and attempts are left, **then** in one commit the scope absorbs the fault, records the attempt, and schedules the next attempt not before *fault time* + min(c, d × m^(n−1)).
3. **Given** a scheduled retry, **then** no other work of the run is held back: later notifications, hand-overs and cancellations are processed while it waits.
4. **Given** a host restart while the retry waits, **then** after restart the retry runs once, at or after its due time, with the same keys; a due time that passed while the host was down runs at the first sweep.
5. **Given** the run or the scope is cancelled while the retry waits, **then** the handler never starts (spec 258 OD10).
6. **Given** a workflow republished while the retry waits, **then** the retry runs the handler of the executable the run started on (spec 250 FR-012).
7. **Given** the last attempt fails, **then** the scope waits for an operator as in spec 251; an operator retry runs at once.

### User Story 2 - Publishing warns when an undo may not have its effect committed (Priority: P2)

An author pairs *Charge card* (a custom activity marked `ReplaySafe`) with *Refund card*. On **Review & publish**, Studio lists a warning on that row: "*Charge card* is marked safe to repeat, so its call may not be committed before it runs; if the process stops, recovery may not know it happened. Mark it as an external effect unless it has none." Publish stays enabled. The author publishes anyway, or removes the marker.

**Why this priority**: Spec 250 FR-014, deferred to M5 (OD20). It is a warning, not a correctness gate, so P2.

**Independent Test**: Validator unit tests W1–W6; an API test that the draft validations carry the warning with severity `Warning` and promotion still succeeds; Vitest and the live journey for the review dialog.

**Acceptance Scenarios**:

1. **Given** a step with a compensation whose forward activity is a `ReplaySafe` leaf, **then** the draft validations list a `Warning` at `{scopeNodeId}/steps/{index}` with type `CompensationScope/ReplaySafeStep`.
2. **Given** a compensation handler that is a `ReplaySafe` leaf, **then** they list a `Warning` of type `CompensationScope/ReplaySafeHandler` at the same path.
3. **Given** a step without a compensation, a composite step or handler, or an activity whose profile cannot be resolved, **then** there is no warning.
4. **Given** only warnings, **then** the backend publishes and Studio's publication review shows them under "Warnings" and keeps **Publish** enabled.

### User Story 3 - The Studio journeys pass against the integrated candidate (Priority: P1)

A maintainer starts a Workbench and a Studio from the candidate revision and runs the live journeys: author a reservation scope, publish, run and inspect it (M1, repaired); author a nested scope with a Confirm and see the hand-over and the kept step (new); act on a waiting recovery (M4); see a scheduled retry and the replay-safe warning (new). Each journey passes, with axe clean, and the results are recorded with both revisions.

**Why this priority**: #3048 acceptance 1. The M1 journey fails today against the current Studio (#3142: it uses the old "Review and Publish" and "Run" labels; spec 260 research R19 on the M4 Studio branch).

**Independent Test**: `Studio/tests/live/*.spec.ts` against the candidate pair, recorded in the qualification record.

**Acceptance Scenarios**:

1. **Given** the candidate Studio and Workbench, **then** `compensation-journey.spec.ts`, `nesting-journey.spec.ts`, `operator-actions-journey.spec.ts` and the backoff and warning steps pass, and the result names both revisions.
2. **Given** a Studio label changes later, **then** only the shared helper module changes, not every journey.

### User Story 4 - Supported configurations are proven and the rest are named (Priority: P1)

An operator choosing a database reads the qualification record: compensation is qualified on SQLite, PostgreSQL, SQL Server and MySQL in Durable, Balanced and Ephemeral; in-memory stores are for development; Oracle and multi-node placement are not qualified. Each row links the test and CI job that proves it and the revision it ran on.

**Why this priority**: #3048 acceptance 2 and 5.

**Independent Test**: The [qualification matrix](contracts/qualification-matrix.md): every "supported" row has a passing CI or recorded run on the candidate revision.

**Acceptance Scenarios**:

1. **Given** each qualified provider and mode, **then** the provider subset (restart, wait and retry, nested hand-over, resolve with a kill before delivery, abandon, backoff with a kill while waiting) passes in the nightly `compensation` suite with its required-provider switch set.
2. **Given** a scope that started under an older state version (1 to 4) and waits, **when** the current code resumes it, **then** recovery finishes and the state is written back as the current version.
3. **Given** a compensated run, **when** it is deleted, **then** its activity executions and incidents (and so its operator history) are gone; a running or waiting run cannot be deleted.
4. **Given** an unqualified combination, **then** the qualification record and the user guide say so; no doc implies support.

### User Story 5 - Guides and samples match what ships (Priority: P1)

A developer new to Elsa reads the compensation guide, loads the sample package into the Workbench and runs the three sample workflows. What they see matches the guide. A table tells them when to use compensation, cancellation, reconciliation and confirmation, and what each does not do.

**Why this priority**: #3048 acceptance 3.

**Independent Test**: The docs audit in [contracts/docs-and-samples.md](contracts/docs-and-samples.md) has every row resolved; the sample test passes in CI; the docs build and link check pass.

**Acceptance Scenarios**:

1. **Given** each statement in `website/docs/essentials/compensation.md`, **then** it is backed by a test or journey named in the audit, or removed.
2. **Given** the sample package, **when** the Workbench sample test runs the three definitions, **then** the participant ledger and run outcomes match the README.
3. **Given** the four-way table, **then** each concept names what starts it, whether handlers run, how it ends and what Elsa does not provide.

### User Story 6 - Release readiness and the Elsa 3 follow-on are on record (Priority: P2)

The program owner opens the qualification record and the Elsa 3 assessment. The first lists supported and unsupported combinations, known limits from M1–M5, open follow-ups and the exact revisions of the evidence, and says plainly that no package was released or deployed. The second links elsa-core#4015, lists the semantic scenarios Elsa 3 could reuse and leaves implementation to a separate effort.

**Why this priority**: #3048 acceptance 5 and 6.

**Independent Test**: Both documents exist, follow [contracts/release-readiness.md](contracts/release-readiness.md) and [contracts/elsa3-follow-on.md](contracts/elsa3-follow-on.md), and pass the docs link check.

### Edge Cases

- **Backoff and a cancelled scope**: the delayed schedule fires after the scope was cancelled. The runtime acknowledges it and creates no child (FR-503).
- **Backoff and run deletion**: a run cannot be deleted while it runs, so a pending retry never outlives its run's history; a delayed item that reaches a run that is gone (deleted after it was cancelled) is dropped.
- **Backoff and a terminal run**: the run faulted for another reason while the retry waited; the delayed item is acknowledged without scheduling.
- **Backoff with a hand-over arriving**: the outer scope journals the handed-over entries while it waits, as during any recovery; the newest entry is undone after the waiting attempt finishes (handlers run one at a time).
- **A Compensate or Confirm while waiting**: the scope is `Compensating`; both are ignored as today (spec 258).
- **Backoff in a test run near its retention window**: a Studio test run's executable reference expires after 30 minutes (`StartWorkflowTestRunRequestHandler.DefaultRetention`); research R10 settles what that does to a waiting scope, and the guide states it.
- **Delay above the cap or the runtime limit**: the validator refuses a literal over 24 hours; an expression is clamped at run time and the clamp is recorded on the attempt.
- **Clock skew between nodes**: due times are computed on the node that commits the failed attempt and compared on the sweeping node; a fast or slow clock shifts a retry by the skew, never runs it twice.
- **FR-014 on an activity whose profile cannot be resolved** (a reusable activity, an unknown type): no warning; reusable activities are `External` by default.
- **A Draft with warnings only**: the backend publishes; Studio must not block (today it would: R6).
- **Upcast of a scope that waits between attempts**: only version 5 has scheduled retries, so no older payload carries one.
- **Live journeys and the 30-minute test-run window**: a live journey that waits longer than the window uses a published run, not a test run.

## Requirements

### Functional Requirements

**Runtime seam R-7: delayed child schedule (generic)**

- **FR-501**: A structural activity may schedule a child **not before** a given time: `ScheduleChildActivity` gains an optional `notBefore`. It is valid wherever scheduling a child is valid. A `notBefore` at or before the commit time schedules normally. One more than `RuntimeDelayedScheduleOptions.MaxDelay` (default 24 hours) ahead of the runtime clock faults the evaluation with `runtime.delayed-schedule.too-far` ([contracts/runtime-seam.md](contracts/runtime-seam.md)).
- **FR-502**: The delayed schedule commits in the evaluation's checkpoint as an ordinary `EnqueueSchedulerWork` post-commit intent carrying its materialized schedule work item (with its minted activity execution id), whose outbox item becomes available at `notBefore`. The checkpoint is mandatory in every persistence mode. The item is never delivered before then: not by the in-process hop, not by the immediate post-commit dispatch, not from a coalescing overlay. It never enters the run's scheduler queue early, so it holds back no other work of the run.
- **FR-503**: When the delayed schedule is handled, a scheduling parent that is no longer `Running`, a terminal run or a missing run acknowledges it and creates no child. (Today only a cancelled execution-scope owner is checked.)
- **FR-504**: A run whose only outstanding work is delayed schedules stays `Running`; recovery and run health do not treat it as stalled, and no sweep re-drives it before the due time.
- **FR-505**: A host without the post-commit outbox sweep faults an evaluation that stages a delayed schedule with `runtime.delayed-schedule.unavailable`, so a retry never silently waits for ever.
- **FR-506**: Workflows.Runtime and Activities.Runtime know no compensation term for R-7; it is proven with a test structural activity.

**Compensation scope: backoff**

- **FR-507**: The scope gains inputs `handlerRetryDelay` (duration, default `00:00:00`), `handlerRetryBackoffMultiplier` (number, default `2`, at least `1`) and `handlerRetryMaxDelay` (duration, default `00:05:00`, at most `24:00:00`) (OD17 A). Pinned at publication like every scope input.
- **FR-508**: When a handler attempt fails and attempts are left, the wait before the next attempt is `min(handlerRetryMaxDelay, handlerRetryDelay × multiplier^(failedAttempts − 1))`. Zero schedules at once, exactly as in M2–M4. Otherwise the scope schedules the same handler with the same scheduling id, keys and metadata through R-7, records `notBefore` on the new attempt, and commits both with the absorption.
- **FR-509**: The draft validator refuses a negative delay, a multiplier below 1, a cap below the delay, and a delay or cap over 24 hours, at `{scopeNodeId}/inputs/{key}`. A value from an expression is clamped at run time into that range and the attempt records `delayClamped`.
- **FR-510**: Operator retries are not delayed. Cancellation, Confirm, Compensate and hand-overs during a wait behave as during any recovery (spec 258). A delayed attempt that is acknowledged without running (FR-503) leaves the entry as it was.
- **FR-511**: The scope's private state moves to version 5 (attempt `notBefore`, `delayClamped`), upgraded from versions 1 to 4 when read; never downgraded.
- **FR-512**: The journal projection `compensation.journal/1` gains `notBefore` on attempts (additive). Studio shows a waiting attempt as **Retry scheduled** with its attempt count and due time, as text plus icon.

**Replay-safe warning (FR-014, OD20 A)**

- **FR-513**: A Core contract reads an activity version's side-effect profile at design time, with one implementation that applies the same attribute rule as the executable compiler, so publication and the warning cannot disagree. A version it cannot resolve has no profile.
- **FR-514**: A compensation Draft validator adds a `Warning` for a step that has a compensation and whose forward activity is a `ReplaySafe` leaf (`CompensationScope/ReplaySafeStep`), and for a compensation handler that is a `ReplaySafe` leaf (`CompensationScope/ReplaySafeHandler`), at `{scopeNodeId}/steps/{index}`. Structural activities never warn.
- **FR-515**: Studio's publication review reads each validation's severity, lists warnings separately and does not block publishing on them. Errors still block.

**Qualification**

- **FR-516**: Every row of the [qualification matrix](contracts/qualification-matrix.md) marked supported has a passing proof on the candidate revision, recorded with the revision, the job or command and the count.
- **FR-517**: A nightly EF container suite `compensation` runs the provider subset on PostgreSQL, SQL Server and MySQL in each persistence mode, with a required-provider switch, listed in `tools/ci/ef-suites.json` (OD18 A).
- **FR-518**: An integration test resumes a waiting scope from each older state version (1 to 4) under the current code and finishes its recovery.
- **FR-519**: Retention is proven: a completed scope's journal is not served; deleting a compensated run removes its incidents; a running run cannot be deleted; an executable referenced by a pending delayed schedule is not collected; the test-run window's effect on a waiting scope is settled and documented (R10).
- **FR-520**: The live journeys share one helper module; the M1 journey is repaired (#3142); a nesting journey with Confirm is added; backoff and the warning are shown live.
- **FR-521**: Review findings on the M1–M4 runtime and Studio code are resolved or filed with a reason, including #3130.

**Docs, samples and records**

- **FR-522**: The user guide matches shipped behaviour per the [docs audit](contracts/docs-and-samples.md), documents the backoff inputs and the warning, and has one table that distinguishes compensation, cancellation, reconciliation and confirmation. The glossary disambiguates effect reconciliation from catalog reconciliation.
- **FR-523**: The sample package and its Workbench test exist as in OD19 A.
- **FR-524**: `docs/reports/native-compensation-qualification.md` records supported and unsupported combinations, known limits, open follow-ups and the evidence revisions, and states that no package release or deployment is claimed.
- **FR-525**: `docs/reports/native-compensation-elsa3-follow-on.md` links elsa-core#4015, lists reusable semantic scenarios and leaves implementation to a separate effort; the program charter's Elsa 3 row points to it.

### Key Entities

- **Delayed child schedule**: a child schedule committed now that becomes deliverable at a due time (R-7).
- **Scheduled retry**: a handler attempt waiting for its due time; entry `Compensating`, attempt `Running` with `notBefore`.
- **Backoff policy**: the three scope inputs and the formula in FR-508.
- **Replay-safe warning**: the non-blocking finding of FR-514.
- **Qualification matrix**: the row-by-row support claim with its proof ([contracts/qualification-matrix.md](contracts/qualification-matrix.md)).

## Success Criteria

- **SC-501**: With the default inputs, every M1–M4 compensation test passes unchanged.
- **SC-502**: In BK1–BK6 on SQLite in every mode, and in BK2 (S7 of the provider subset) on PostgreSQL, SQL Server and MySQL in every mode, each handler retry runs once, not before its due time, with the same operation key; a kill while waiting changes nothing but the time.
- **SC-503**: Each negative control fails when its safeguard is removed: the delayed intent's availability, the parent-running check on delivery, the mandatory checkpoint, the delay formula, the warning, and Studio's warning filter.
- **SC-504**: The qualification matrix has no supported row without a recorded proof on the candidate revision, and every unsupported combination appears in the record and the guide.
- **SC-505**: All live journeys pass against one recorded Studio and Workbench pair, with axe clean at 390 and 1200 px.
- **SC-506**: The docs audit has no open row; the sample test passes in `Build & test`.
- **SC-507**: Architecture guards, the generated-maps check and CI on the exact head commit pass for each PR; the post-merge `main` CI and a nightly EF run on the merge commit are recorded on #3048.

## Assumptions

- PR #3140 (M4 Studio) merges before PR 2; its live journey and labels are the baseline.
- The post-commit outbox already honours a pending item's `AvailableAt` in the durable stores and the coalescing overlay (research R2); M5 adds the way to set it at commit.
- Due-time precision is the outbox sweep interval (10 seconds by default); the guide says the delay is a minimum.
- Exactly-once effects, a reconciliation framework, parallel steps and BPMN compensation stay out of scope (program non-goals, ADR 0086).

## Out of scope

Package releases, published images and deployment (owner exclusion). Implementing compensation in Elsa 3. Jittered or per-handler backoff, a "retry now" action during a wait, and a structural timer callback (research R1 keeps it as the fallback). Bulk operator actions, cancel-run and variable edits from Studio (still parked). A Studio-to-Workbench live journey in CI. Qualifying Oracle or multi-node placement.
