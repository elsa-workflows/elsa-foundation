# Feature Specification: Inspect and operate compensation recovery in Studio

**Feature Branch**: `agent/claude/3047-compensation-operator-actions`

**Created**: 2026-10-20

**Status**: Implemented — the four phases, in [#3136](https://github.com/elsa-workflows/elsa-foundation/pull/3136) (PR 1: Phases A and B and the backend docs) and the Studio PR (PR 2: Phase C and the Studio walkthrough), under [#3047](https://github.com/elsa-workflows/elsa-foundation/issues/3047): runtime seams R-5 and R-6, the incident rule, FR-405 and FR-406 (Phase A), the scope's decisions, state v4, the journal projector and the permission signal (Phase B), the Studio operator actions, journal-backed run view, history and URL selection (Phase C), and the user guide, glossary, extension points and runtime README (Phase D). Evidence:

- Phase A: `Elsa.Workflows.Runtime.Tests` 2486, `Elsa.Workflows.Runtime.EntityFrameworkCore.Tests` 1001, `Elsa.Activities.Runtime.Tests` 624 (with the forged-offer test from the review), `Elsa.Workflows.Runtime.Api.Tests` 199 and the behavioral suite 199, all passing, including the `DecideIntervention/1` refusals in contract order, the delivery handler, `SourceParentNotRunning` and explicit targets across execution authorities;
- Phase B: `Elsa.Activities.Compensation.Tests` 293/293, with journeys O1–O8 and J1 in Durable, Balanced and Ephemeral and the three O8 restart injection points, and the Workbench owned-process journey (O1 + O2h + O4 over REST on a Studio test run, with a kill at injection point 2); the architecture suite 578/578;
- Phase C: Studio Vitest 3174/3174 across the workspace (the alteration client, the journal mapping and its derived fallback, the dialog, the actions' visibility, the submission flow with fake timers, the URL selection), typecheck, lint with 0 errors, the mocked browser suite `compensation-scope.spec.ts` 74/74 with axe at 390 and 1200 px in light and dark for every new view and each dialog (SC-406), and the live journey `operator-actions-journey.spec.ts` against a real Studio and Workbench: retry, resolve with a reload mid-poll (one submission), a stale second tab refused, abandon, and axe on each view and dialog (SC-407; its reader step needs a configured user without manage permission, research R19);
- SC-404: the eight negative controls fail when their safeguard is removed, seven in PR 1 and Studio's `canManage` gate in PR 2 ([quickstart](quickstart.md));
- Phase D: `python3 tools/maps/check-catalog-links.py` reports no broken link.

**Program**: [Native compensation #3035](https://github.com/elsa-workflows/elsa-foundation/issues/3035) → [Epic #3040](https://github.com/elsa-workflows/elsa-foundation/issues/3040) → [Feature #3047](https://github.com/elsa-workflows/elsa-foundation/issues/3047) (M4). Builds on [spec 250](../250-native-compensation/spec.md) (M1), [spec 251](../251-compensation-uncertainty/spec.md) (M2) and [spec 258](../258-compensation-nesting/spec.md) (M3). Their requirements and owner decisions still hold, except where this spec says otherwise.

**Input**: Inspect and operate compensation recovery in Elsa Studio: correlate the failure, forward outcome, handler attempts, captured context and remaining work without database access, and retry, resolve or abandon a waiting recovery with authorization and audit evidence (#3047).

## Owner decisions (20 October 2026)

| ID | Question | Decision |
|---|---|---|
| OD12 | Which operator actions ship in M4? | **Retry, Resolve and Abandon** (spec 250 OD2). Retry is a Studio button over the existing `RetryFaultedActivity/1` (#2747, built in spec 251). Resolve declares the outcome of a waiting entry without running it again, with a mandatory reason; the scope then continues as if the entry settled. Abandon stops a scope's recovery with a mandatory reason; the scope ends `Abandoned` and faults with a coded fault, so the run does not silently succeed. The owner grants a **narrow one-off exception to the ADR 0080 D0 park on Runtime Alterations** for this: at most two compensation-only alteration kinds, or one generic intervention-decision seam whose only consumer is compensation. Bulk retry, cancel-run and variable edits stay parked. |
| OD13 | Who sees the actions? | Studio hides them unless a **server-provided, per-user signal** says the caller has `workflows/runtime:manage` for the run (the `permissions.canManage` shape). Studio still handles 401 and 403. |
| OD14 | Where does action history come from? | From **incident resolution outcome metadata**: actor, reason, time and outcome, as `RetryFaultedActivity/1` already records them. No plans-by-run endpoint. |
| OD15 | How does Studio read the journal? | A **read-only journal projection endpoint** per scope occurrence: entries, statuses, attempts with incident ids, operation keys, origin and hand-over, confirmations, cause and remaining handlers. Captured values honour `CanInspectSensitiveValuesAsync`. Permission `workflows/runtime:read`, tenant-scoped. Studio uses it instead of inferring where it can. |
| OD16 | Which runs can an operator alter by id? | **Every run of the caller's tenant**, whatever execution authority started it, for alterations that name runs by id (FR-406). This matches run deletion, including its per-run authorization with the run as the permission resource. It applies to all alteration kinds. The run's authority is still captured for the concurrency check, and query targets keep the authority filter. |

Choices made in this packet under OD12, recorded so they can be challenged ([research](research.md)):

- **One generic seam, not compensation kinds.** A built-in `DecideIntervention/1` alteration resolves a blocking incident that waits for intervention with a decision the requesting structural activity offered when it asked for intervention, and delivers that decision to the requester in the same checkpoint (R2–R4). Workflows.Runtime knows no compensation term.
- **Outcome vocabulary.** Resolve takes one of two outcomes: `EffectAbsent` ("the effect does not exist: it never happened or was undone outside Elsa") or `EffectAccepted` ("the effect exists and stays"). Both map to the entry status `ResolvedManually` (R6).
- **Abandon fault.** `compensation.scope.abandoned`, category `outcome-unknown`: an enclosing scope treats the abandoned scope as an uncertain step, never as undone (R8).
- **Explicit targets.** An operator with `workflows/runtime:manage` can alter any run of their tenant by id, whatever started it (R10). Today only runs started through the runtime REST API can be altered.

Defaults that still apply: generic runtime seams; nothing crosses a reusable-activity boundary; late compensation reaches only the enclosing scope; cancellation never starts a handler.

## User Scenarios & Testing

### User Story 1 - Diagnose a waiting recovery without database access (Priority: P1)

An operator opens a run whose *Order* scope shows "✋ Needs action". The scope's journal lists each step with its forward outcome, its handler attempts with their incidents, the operation keys, where a handed-over entry came from, what was confirmed, and which handlers still have to run. The banner names the failure that started recovery. Selecting an entry opens its forward or handler occurrence with its captured inputs and outputs, when the operator may see values.

**Why this priority**: #3047 acceptance 1. An operator cannot decide on an action without seeing the whole recovery.

**Independent Test**: An API test of the journal endpoint against a scope in each phase, and a Vitest of the journal-backed run view; a negative control without the sensitive-values grant.

**Acceptance Scenarios**:

1. **Given** a live scope (running, compensating, waiting, cancelled or faulted), **when** Studio opens it, **then** the run view reads the journal endpoint and shows every entry with status, forward outcome, attempts (each with its incident), operation key, origin, confirmation and whether its handler still has to run.
2. **Given** a scope that completed (its private state is cleared), **then** the run view falls back to the M1–M3 derivation from run data and says the journal is no longer kept.
3. **Given** a reader without `workflows/activity-executions:inspect-values`, **then** the journal still shows structure, keys and statuses, the cause message reads "withheld", and captured values stay withheld on the occurrences.
4. **Given** the recovery cause, **then** the banner shows the step, the incident and its failure type, and links to the incident.

### User Story 2 - Retry a failed handler from Studio (Priority: P1)

The delivery service is back. The operator selects **Retry** on the *Cancel delivery* row. A dialog names the target ("Cancel delivery, attempt 4 of 3, incident inc-5") and the consequence ("runs the compensation again with the same operation key; recovery continues if it succeeds"). The operator adds an optional reason and confirms. Studio shows "Retry submitted", then the new attempt, then "⟲ Compensated".

**Why this priority**: #3047 acceptance 3 and the #2747 Studio button deferred by spec 251.

**Independent Test**: Vitest of the action and polling; the live browser journey against the Workbench.

**Acceptance Scenarios**:

1. **Given** a handler row in "Needs action" and a caller who may manage the run, **then** the row offers Retry.
2. **Given** a confirmed retry, **then** Studio submits `RetryFaultedActivity/1` for the failed attempt with an `Idempotency-Key`, polls the plan to its end, and refreshes the run.
3. **Given** the same submission sent twice (a double click or a network retry), **then** the second returns `Replayed` and nothing runs twice.
4. **Given** the retry is refused (for example `NoBlockingIncident` because someone else acted first), **then** Studio shows why, refreshes the run and nothing changed.

### User Story 3 - Resolve a waiting entry with a reason (Priority: P1)

The refund system cannot undo a charge, and finance refunded it by hand. The operator selects **Resolve** on the waiting *Refund* row, chooses "Effect absent or undone", types the reason "Refunded manually, ticket FIN-221", and confirms. The entry reads "Resolved: effect absent" and recovery continues with the next handler. On another run, a delivery step is uncertain and has no compensation; the carrier confirms the slot exists and the business keeps it. The operator chooses "Effect present, keep it". The step reads "Resolved: effect kept" and the scope carries on with the next step.

**Why this priority**: OD12. Without Resolve, an operator can only retry something that will never succeed, or cancel the run.

**Independent Test**: Integration journeys O2 and O3 in every persistence mode; Vitest for the dialog and the new states.

**Acceptance Scenarios**:

1. **Given** a handler waiting for intervention, **when** it is resolved with either outcome, **then** in one alteration checkpoint its incident is resolved with an operator-decision outcome, and the scope marks the entry `ResolvedManually` with that outcome and continues recovery as if the handler had completed.
2. **Given** an uncertain forward step without a handler, **when** it is resolved `EffectAbsent`, **then** the scope treats the step as failed with no effect and recovers the earlier steps; **when** it is resolved `EffectAccepted`, **then** the scope treats the step as done and runs the next step.
3. **Given** a resolution, **then** the reason, actor, time and outcome are readable on the incident, and the entry is never handed over, confirmed or compensated afterwards.
4. **Given** a missing or blank reason, **then** the action is refused and nothing changes.

### User Story 4 - Abandon a recovery (Priority: P2)

A handler keeps failing and the business decides to stop. The operator selects **Abandon recovery** on the scope banner. The dialog says which entries will stay not undone and that the scope will fault. The operator gives a reason and confirms. The scope reads "Abandoned", its unresolved entries read "Not undone", and the run faults with `compensation.scope.abandoned` (or, inside another scope, the outer scope treats it as an uncertain step).

**Why this priority**: OD12 and spec 250 OD2. It is the last resort, so P2.

**Independent Test**: Journey O4 in every mode, nested variant O4n.

**Acceptance Scenarios**:

1. **Given** a scope waiting for intervention, **when** an authorized operator abandons it with a reason, **then** the waiting incident is resolved with an operator-decision outcome, the scope phase becomes `Abandoned`, no further handler runs, and the scope faults with `compensation.scope.abandoned`.
2. **Given** an abandoned top-level scope under the default incident strategy, **then** the run faults; it never completes as if recovery had finished.
3. **Given** an abandoned inner scope, **then** the enclosing scope receives an `outcome-unknown` fault of that step: it runs the step's handler if it has one, otherwise it waits for intervention itself.
4. **Given** an abandoned scope, **then** its journal stays readable and lists what was and was not undone.

### User Story 5 - Permissions and tenants are enforced (Priority: P1)

An analyst with read access opens the same run. They see the journal and history, but no action buttons. A script they run against the alteration endpoint gets 403. An operator of another tenant cannot see the run at all.

**Why this priority**: #3047 acceptance 4. Operator actions change production state.

**Independent Test**: API tests per permission set and tenant; Vitest with `canManage` false; the live browser journey with a read-only user.

**Acceptance Scenarios**:

1. **Given** a caller without `workflows/runtime:manage` for the run, **then** the run details say `permissions.canManage: false`, Studio shows no action, and a submission returns 403 and creates no plan.
2. **Given** a run of another tenant, **then** the run, its journal and its incidents return 404, and an alteration naming it ends `TargetNotFound` with nothing applied.
3. **Given** Studio receives 401 or 403 while submitting, **then** it shows the matching message and refreshes the permission signal.
4. **Given** a run started by an HTTP endpoint, a timer or a Studio test run, **then** an authorized operator of its tenant can act on it (R10).

### User Story 6 - Restarts keep state and navigation consistent (Priority: P2)

The operator confirms a Resolve and the backend restarts before the decision is applied. After the restart the decision is applied once. The operator reloads Studio: the same run and scope open, the action shows as applied, and the history lists it once.

**Why this priority**: #3047 acceptance 5.

**Independent Test**: Journey O8 with a process kill at each injection point; a Vitest of the reload path.

**Acceptance Scenarios**:

1. **Given** a kill after the alteration checkpoint and before the decision reaches the scope, **then** after restart the scope applies it exactly once.
2. **Given** a kill while the plan is still queued, **then** the plan runs after restart, or is refused if the incident no longer waits.
3. **Given** a Studio reload during polling, **then** Studio reopens the same run and occurrence from the URL, resumes polling the pending plan, and never submits it again.

### User Story 7 - Past operator actions are visible (Priority: P2)

A reviewer opens a compensated run. The scope shows an **Operator actions** list: "Retry by alice, 10:42, Delivery service restored", "Resolved (effect absent) by bob, 11:05, Refunded manually", each linked to its incident.

**Why this priority**: OD14 and #3047 acceptance 3 (audit evidence).

**Independent Test**: Vitest over incident fixtures; the live journey reads the list after its actions.

**Acceptance Scenarios**:

1. **Given** incidents resolved by `OperatorRetry` or `OperatorDecision`, **then** the scope lists them newest first with actor, reason, time, kind and outcome.
2. **Given** a completed scope whose journal is no longer kept, **then** the list is still shown, because it comes from incidents.

### Edge Cases

- **Stale action**: the entry no longer waits (an operator or the scope acted first). The incident is no longer blocking, so the plan is refused (`InterventionNotPending`, or `NoBlockingIncident` for a retry) and nothing changes.
- **Double submit** with the same `Idempotency-Key`: the existing plan is returned with disposition `Replayed`; nothing is applied twice.
- **Two operators, two keys**: the actor runs jobs for one run one at a time; the first resolves the incident, the second is refused as stale.
- **Retry while handler work is claimed**: refused `SourceWorkClaimed` (existing). A decision while the requester has claimed work: refused `RequesterWorkClaimed`.
- **Resolve on a forward step vs a handler**: both offer the same three decisions. On a handler, recovery continues. On a forward step, `EffectAbsent` starts recovery of the earlier steps and `EffectAccepted` continues forward. A later step that reads the resolved step's result faults as for any missing value (spec 250).
- **Abandon while a handler runs**: there is no waiting incident, so Studio offers no Abandon and the API refuses it. Cancelling the run is the way to stop a hanging handler; it starts no handler.
- **Abandon a scope with a partial hand-over**: batches still in flight from an inner scope are not journaled once the scope faulted; their entries end as kept, and the journal lists the hand-over as partial. Entries handed over before the abandon stay not undone, with their origin.
- **Abandon an outer scope whose entries came from inner scopes**: those entries stay not undone and keep their origin.
- **A handler that is itself a scope** is abandoned: the outer scope sees a failed handler attempt and applies its retry and intervention rules (spec 251).
- **Run cancelled**: the run is terminal, so every action is refused (`WorkflowTerminal`).
- **Scope cancelled, run still running** (a Compensate stopped it): its waiting incident stays blocking but the scope is not running. Decisions are refused (`RequesterNotRunning`); a retry is refused (`SourceParentNotRunning`, R9), so cancellation never starts a handler.
- **Cancellation lands after a decision commits and before it is delivered**: the decision is acked unapplied; the incident shows the decision, the journal shows the entry unchanged, and Studio says "Decision not applied: the scope was cancelled first".
- **Decision on an incident that a descendant raised** (a fault inside a Sequence step, spec 258 R-3): the scope finds the entry by incident. Resolve cancels the step's suspended subtree. Abandon faults at once and leaves the subtree to the next scope out or the run's fate (spec 258 R13).
- **Operator without manage**: no action is shown; a direct submission gets 403.
- **Other tenant**: 404 on reads; `TargetNotFound` on an alteration.
- **Retry after resolve or abandon**: the incident is resolved, so the retry is refused `NoBlockingIncident`.
- **A reason with control characters, or over 500 characters**: refused as an invalid payload.

## Requirements

### Functional Requirements

**Runtime seam R-5: intervention decisions (generic)**

- **FR-401**: A structural parent that requests intervention for a child fault may name the **decisions** an operator can take on it: 1 to 8 codes, each dotted, lower case, at most 64 characters, not starting `runtime.`. The runtime records them in the `WaitForIntervention` outcome metadata (`runtime.interventionDecisions`) with the requester's id (`runtime.interventionRequestedBy`). Naming decisions requires the parent to implement the decision callback (FR-403); otherwise the evaluation faults.
- **FR-402**: A built-in alteration **`DecideIntervention/1`**, payload `{ incidentId, decision, reason }`, applies a decision. It requires `workflows/runtime:manage`, an `Idempotency-Key`, a running run, a `Blocking` incident with a `WaitForIntervention` outcome from `StructuralIntervention`, a decision the requester offered, a requester that is running and has no claimed work, and a reason of 1 to 500 characters. In one alteration checkpoint it resolves the incident with action kind `OperatorDecision`, source `OperatorDecisionAlteration` and the audit metadata of FR-422, and enqueues the decision for the requester. Otherwise it records one failed outcome with a refusal code and applies nothing ([contracts/operator-actions.md](contracts/operator-actions.md)).
- **FR-403**: The runtime delivers the decision to the requester through a new callback (`IRuntimeActivityInterventionDecisionHandler`), as a structural evaluation that may defer (scheduling children or cancelling a child subtree), complete or fault, committed like any other structural evaluation. A requester that is no longer running acks the delivery without a callback. A repeated delivery is acked by its processed marker.
- **FR-404**: The incident transition rule allows exactly one more replacement of a committed outcome: `WaitForIntervention` → `Resolved` with `OperatorDecision` from `OperatorDecisionAlteration`, on the same activity execution.
- **FR-405**: `RetryFaultedActivity/1` refuses a source whose parent is not running (`SourceParentNotRunning`), so a retry never starts a handler under a cancelled scope (R9).
- **FR-406**: An alteration that names runs by id captures every run of the caller's tenant, whatever execution authority started it, that the caller may manage. Each named run is authorized at submission as run deletion authorizes it: `workflows/runtime:manage` with the run's state as the permission resource, so a host's resource handler can refuse one run. A refused run is never captured or altered and fails `TargetNotFound`, exactly like a missing run; the other runs in the plan proceed. Query targets keep today's authority filter; bulk alteration stays parked (R10).

**Compensation scope**

- **FR-407**: When a scope requests intervention (a handler out of attempts, or an uncertain step without a handler), it offers `compensation.resolve.effect-absent`, `compensation.resolve.effect-accepted` and `compensation.abandon`.
- **FR-408**: A resolve decision on a `Failed` entry marks it `ResolvedManually` with outcome `EffectAbsent` or `EffectAccepted` and the incident id, and the scope returns to `Compensating` and schedules the next eligible handler, or finishes as in spec 251.
- **FR-409**: A resolve decision on a waiting uncertain step without a handler marks the entry `ResolvedManually`. `EffectAbsent`: the scope enters `Compensating` and recovers the earlier entries, keeping the uncertain cause; `faultAfterCompensation` applies as for any recovery. `EffectAccepted`: the scope returns to `Running` and schedules the next step, or completes `Done` and hands over as in spec 258. In both cases a suspended subtree under the step (a descendant fault) is cancelled.
- **FR-410**: An abandon decision sets the phase `Abandoned`, records which incident abandoned it, runs no further handler, leaves every entry's status as it was, and faults the scope with `compensation.scope.abandoned`, category `outcome-unknown`.
- **FR-411**: `ResolvedManually` entries are never eligible, never confirmed and never handed over. A scope in `Abandoned` ignores hand-overs, Confirm and Compensate.
- **FR-412**: The scope's private state moves to version 4, upgraded from versions 1 to 3 when read. Version 4 is never downgraded.
- **FR-413**: Every decision transition commits atomically with the evaluation that applies it, holds in Durable, Balanced and Ephemeral modes, and ignores a decision for an incident no entry waits on.

**Journal and permission signal**

- **FR-414**: A generic, read-only **activity state view** endpoint returns a module-projected view of a live structural activity's private state ([contracts/journal-and-permissions.md](contracts/journal-and-permissions.md)). The compensation module projects its journal: phase, cause, entries (status, forward outcome, operation key, forward and handler occurrences, attempts with incidents, waiting incident and offered decisions, resolution, origin, confirmation), the remaining handlers in recovery order, hand-over progress and abandonment.
- **FR-415**: The endpoint requires `workflows/runtime:read` and the run inspection grant, is tenant-scoped, and returns 404 for an unknown, unauthorized or other-tenant run. It returns no captured value. The cause message is withheld unless `CanInspectSensitiveValuesAsync` allows it. A scope whose private state was cleared returns `available: false`.
- **FR-416**: The run details response carries `permissions: { canManage, canInspectValues }` for the caller and that run. `canManage` is evaluated with the Identity permission evaluator for `workflows/runtime:manage`, the caller's tenant and the run as resource, and fails closed.

**Studio**

- **FR-417**: The run view reads the journal for a live scope and derives from run data (M1–M3) only when the journal is unavailable or the host lacks the capability.
- **FR-418**: Every state is text plus icon: Waiting to undo, Undoing, Retrying (attempt n of m), Uncertain, Compensated, Confirmed, Needs action, Resolved: effect absent, Resolved: effect kept, Not undone, and the scope phases Needs action and Abandoned.
- **FR-419**: Actions appear only when the run details say `canManage`, the host advertises the alteration links, and the incident waits: Retry and Resolve on the waiting row, Abandon recovery on the scope banner, and Retry plus any offered decisions in the incident dialog.
- **FR-420**: Each action opens a confirmation dialog that names the target (scope, step, occurrence, attempt, incident) and the consequence, asks for a reason (required for Resolve and Abandon), and for Resolve asks for the outcome.
- **FR-421**: Studio submits with an `Idempotency-Key` minted when the dialog opens, polls the plan and its job to the end, maps refusal and HTTP errors to messages, and refreshes the run. A refused action shows the refusal and changes nothing.
- **FR-422**: Operator history comes from incidents resolved with `OperatorRetry` or `OperatorDecision`: `runtime.alteration.submittedBy`, `runtime.alteration.reason`, the outcome's applied time, the kind, and for decisions `runtime.interventionDecision` and `runtime.interventionRequestedBy`.
- **FR-423**: The selected occurrence is kept in the URL, and a pending plan id is kept per run in session storage, so a reload reopens the same view and resumes polling without resubmitting.
- **FR-424**: Every new control is keyboard operable with an accessible name; the axe check passes at 390 and 1200 px in light and dark.

**Documentation**

- **FR-425**: The user guide gains an operator section (diagnose, retry, resolve, abandon, permissions, history); the glossary gains operator decision, resolved manually and abandoned recovery; the runtime and compensation `EXTENSION_POINTS.md` list R-5 and the state view seam; spec 258 FR-315 says cancellation never *starts* a handler.

### Key Entities

- **Operator decision**: an operator's answer to an intervention wait, taken from the decisions the requester offered, with a reason.
- **Resolved manually**: an entry an operator settled without running it, with outcome `EffectAbsent` or `EffectAccepted`.
- **Abandoned recovery**: a scope an operator stopped; it faults and keeps its journal.
- **Activity state view**: a module's read-only projection of a live structural activity's private state.
- **Permission signal**: the per-user, per-run `permissions.canManage` flag.

## Success Criteria

- **SC-401**: For every journey in [quickstart](quickstart.md), every field #3047 asks for (failure, forward outcome, attempts, captured context, remaining work) is reachable in Studio without database access.
- **SC-402**: Each action, in Durable, Balanced and Ephemeral, ends with the participant state the journey expects, with no effect applied twice, across a kill at each injection point.
- **SC-403**: Each refused action (stale, not offered, not running, cancelled, no permission, other tenant, invalid reason) leaves the run's activity, incident and scope state byte-for-byte unchanged.
- **SC-404**: Each negative control fails when its safeguard is removed: the incident-status lock, the offered-decision check, the requester-running check, the retry parent-running check, the validator allowance, the abandon fault, Studio's `canManage` gate, and the cause-message gate.
- **SC-405**: A user without manage sees no action and gets 403 from a submission; another tenant's user gets 404.
- **SC-406**: Studio shows every state as text plus icon, and axe reports no violation.
- **SC-407**: The live browser journey against a real Workbench retries, resolves and abandons, shows a refusal, and checks the read-only user.

## Assumptions

- The incident is the lock: while an entry waits, only an operator action or a cancellation changes it (research R1).
- Operator reasons are visible to readers of the run structure, like every outcome metadata today. The dialog says not to put secrets in a reason.
- A completed scope keeps no private state (spec 258 R1), so its journal is not served; Studio derives it as in M3.
- The retry alteration and the alteration substrate (plans, jobs, idempotency, fencing) are unchanged apart from FR-405 and FR-406.

## Out of scope

Bulk retry, cancel-run and variable edits from Studio (still parked); a plans-by-run endpoint; abandoning a scope that is not waiting; a reconciliation framework; retrying a terminal run; retry backoff (M5); BPMN compensation (ADR 0086).
