# Feature Specification: Uncertain effects and resumable compensation

**Feature Branch**: `agent/claude/3045-compensation-uncertainty`

**Created**: 2026-10-20

**Status**: Implemented. The runtime seams and the core of #2747 (`RetryFaultedActivity/1`) landed through [#3113](https://github.com/elsa-workflows/elsa-foundation/pull/3113). Scope state v2, uncertain steps, bounded handler retries, the intervention wait, the Studio states and the docs landed under [#3045](https://github.com/elsa-workflows/elsa-foundation/issues/3045). Evidence:

- compensation tests 58/58, including J1–J6 and every FR-216 injection point in Durable, Balanced and Ephemeral;
- the Workbench owned-process journeys (M1, and J1 + J5 with a kill after the retry) 2/2;
- the SC-204 negative controls, run by hand and reverted: dropping the handler operation key fails 6 tests, dropping the uncertain branch fails 5, dropping the intervention request fails 5;
- Studio Vitest, typecheck and lint.

The Studio retry button for #2747 is M4.

**Program**: [Native compensation #3035](https://github.com/elsa-workflows/elsa-foundation/issues/3035) → [Epic #3038](https://github.com/elsa-workflows/elsa-foundation/issues/3038) → [Feature #3045](https://github.com/elsa-workflows/elsa-foundation/issues/3045) (M2). Builds on [spec 250](../250-native-compensation/spec.md), whose FR-001 to FR-020 and owner decisions OD1–OD4 still hold. Prerequisite delivered here: the core of [#2747](https://github.com/elsa-workflows/elsa-foundation/issues/2747) (operator retry of a faulted activity).

**Input**: Recover safely from lost external responses, duplicate deliveries and compensation failures, with useful Studio status (#3045).

## Owner decisions (20 October 2026)

| ID | Question | Decision |
|---|---|---|
| OD2 (refined) | How does "waits for intervention" work? | A handler that exhausts its attempts leaves its incident **blocking with outcome Wait for intervention**. The run stays running; no automatic retry is scheduled. This replaces spec 250's "durable bookmark" wording, which a structural activity cannot hold. |
| OD5 | How does an operator resume? | Through **#2747's retry**, built now as an M2 prerequisite (by owner exception to the Runtime Alterations park): a runtime alteration that resolves the blocking incident and reschedules the same activity occurrence's work. The Studio Retry button is M4. |
| OD6 | Where do the new runtime contracts live? | As **generic** runtime seams any activity can use: a logical operation key on the activity context, the fault code and category on the child-fault callback, and a structural "keep this incident blocking for intervention" request. |
| OD7 | Do handler retries back off? | **No, not in M2.** A failed handler is retried immediately, up to a scope setting (default 3 attempts in total). Backoff needs a structural delay seam and is M5 work. |

## User Scenarios & Testing

### User Story 1 - A lost response does not leak a reservation (Priority: P1)

*Reserve delivery* sends its request; the delivery service applies the reservation but the response is lost. The activity cannot tell whether the reservation exists, so it reports an **uncertain outcome** instead of a plain failure. The scope records the step as uncertain and starts recovery. *Cancel delivery* runs with the same operation key the forward call used: it cancels the reservation if it exists and, if it does not, makes sure a late-arriving reservation for that key is refused. *Release inventory* then runs as in M1. The scope completes `Compensated` and no reservation remains.

**Why this priority**: A lost response is the most common way a saga leaks an effect. Treating it as "never happened" is the bug this milestone exists to prevent (#3045 acceptance 2).

**Independent Test**: A backend journey with the reservation participant set to apply an effect and then lose the response. Run it once with the effect present and once with the effect applied only after the cancel (late arrival). Assert the participant's final state in both runs.

**Acceptance Scenarios**:

1. **Given** a step that reports an uncertain outcome, **when** the scope handles it, **then** it records the step as an uncertain entry, records an uncertain recovery cause and starts recovery. The entry is never shown as "not run", "no effect", "completed" or "compensated" until its handler succeeds.
2. **Given** an uncertain entry with a handler, **when** recovery reaches it, **then** the handler runs in completion order like any registered entry, and receives the forward step's operation key as its target.
3. **Given** the effect exists, **when** the handler runs, **then** the effect is undone once and the entry becomes compensated, still marked as having had an uncertain forward outcome.
4. **Given** the effect does not exist yet, **when** the handler runs and the forward request arrives later, **then** the participant refuses the late request and no effect remains.
5. **Given** an uncertain step with no handler, **then** the scope does not start recovery as if nothing happened. It keeps the step's incident blocking, waits for intervention and shows the step as uncertain.

### User Story 2 - A failing handler is retried, then waits for an operator (Priority: P1)

*Cancel delivery* fails because the delivery service is briefly unavailable. The scope runs it again straight away with the same operation key. If it succeeds within the scope's attempt limit, recovery continues. If every attempt fails, the scope stops and waits: the handler's incident stays blocking with "wait for intervention", the run stays running, the original cause is kept, and every attempt is recorded. Nothing is marked compensated.

**Why this priority**: OD2. A handler failure must never silently pass as compensation, and transient failures should not need an operator.

**Independent Test**: The participant fails the handler a configurable number of times. Run with fewer failures than the limit, and with failures at or above it. Kill the host mid-retry and while waiting.

**Acceptance Scenarios**:

1. **Given** a handler that fails fewer times than the attempt limit, **then** it is retried immediately with the same operation key, the undo happens once, and recovery continues.
2. **Given** a handler that fails on every attempt, **then** the entry becomes failed, the scope waits for intervention, the handler's incident stays blocking with outcome "wait for intervention", the run stays running, and the recovery cause still names the original failure.
3. **Given** a host restart while the scope is retrying or waiting, **then** the scope resumes in the same state, with no extra handler run and no lost attempt.
4. **Given** each persistence mode (Durable, Balanced, Ephemeral), **then** scenarios 1–3 hold, with each mode's restart tests passing.

### User Story 3 - An operator resumes recovery (Priority: P1)

The delivery service is back. An operator with runtime-manage permission submits a retry for the blocked handler through the existing alteration API. The handler runs again with the same operation key, succeeds, and the scope continues recovery and completes. A duplicate submission does nothing.

**Why this priority**: Waiting for intervention is useless without a way to intervene (#2747).

**Independent Test**: An API-level journey: drive story 2 to the wait, submit the retry alteration (twice, with the same idempotency key, and once more with a new key), and assert one handler run, one undo and a completed scope.

**Acceptance Scenarios**:

1. **Given** a blocking incident on a faulted activity in a running run, **when** an authorized operator submits a retry, **then** in one checkpoint the incident is resolved with an operator-retry outcome and the activity is scheduled again with its original inputs, parent, scheduling identity and metadata.
2. **Given** a compensation handler retried this way, **when** it completes, **then** the scope treats it as the entry's handler, marks the entry compensated and continues recovery.
3. **Given** the retried handler fails again, **then** the scope records the attempt and waits for intervention again with a fresh blocking incident.
4. **Given** the same retry submitted twice, or a retry for an activity whose incident was already resolved, **then** at most one new run of the activity is scheduled.
5. **Given** a caller without runtime-manage permission, **then** the retry is refused.

### User Story 4 - Studio shows uncertainty and the need for action (Priority: P2)

An operator opens the run. The uncertain step reads "⚠ Uncertain" instead of "Faulted". A handler that is waiting reads "✋ Needs action", with "Attempt 3 of 3" and a link to its incident. The scope summary says recovery is waiting for intervention and still names the original cause.

**Why this priority**: Operators must see that an effect may exist and that recovery is blocked on them (spec 250 FR-018).

**Independent Test**: Vitest component tests for every new state plus the mocked browser suite with axe, at both widths and in both themes.

**Acceptance Scenarios**:

1. **Given** an uncertain forward step, **then** its row shows "⚠ Uncertain" as text and icon, and its recovery column shows the handler's progress.
2. **Given** a handler waiting for intervention, **then** its row shows "✋ Needs action", the attempt count and a link that opens the blocking incident; the scope summary shows "Needs action" and the original cause.
3. **Given** a handler retried within its limit, **then** the row shows the attempt count while it runs and after it succeeds.
4. **Given** only the keyboard and a screen reader, **then** every new state and link is reachable and announced, and the axe check passes.

### Edge Cases

- A forward step reports an uncertain outcome and its handler also reports one: the entry stays uncertain, the handler attempt counts as failed, and the normal retry and intervention rules apply.
- An operator retries an uncertain forward step without a handler (story 1.5) through #2747: the step runs again with the same operation key. If it completes, the scope marks the entry as not needing a handler and continues forward. If it fails or is uncertain again, the scope applies the M1 or M2 rule again.
- A late handler completion arrives for an entry that is already compensated, or for an older attempt after a newer one was scheduled: it is ignored and never advances recovery twice.
- A stale owner commits after another owner took over the run: the commit is rejected by the existing fence, and the participant deduplicates by operation key when the step or handler runs under the new owner.
- A scope that started on M1 is resumed by M2 code: its version-1 state is upgraded in place. Its existing entries keep their M1 identity, and their operation key is the one M1 participants saw (the forward invocation id).
- A handler is a composite: the scope's operation key is given to the composite occurrence. Activities inside it get their own default keys. Only leaf handlers are key-stable across retries in M2.

## Requirements

### Functional Requirements

**Operation keys**

- **FR-201**: Every activity occurrence has a **logical operation key** that external participants use to deduplicate effects. It defaults to the occurrence's first-attempt identity, so it is stable across crash re-attempts and automatic retries. A structural parent may assign the key and a **target operation key** when it schedules a child. Assigned keys survive restarts, crash re-attempts, automatic boundary retries and operator retries (FR-211).
- **FR-202**: A compensation scope assigns each forward step the key `{scope occurrence}/step/{index}` and each handler the key `{forward key}/compensate`, with the forward key as its target. A retried handler reuses the same keys.
- **FR-203**: Spec 250 FR-011's advice changes: participants deduplicate on the operation key, not the invocation id. For entries created before M2, the operation key is the forward invocation id, which is what those participants received.

**Uncertain outcomes**

- **FR-204**: An activity reports an uncertain outcome by faulting with the reserved fault category `outcome-unknown`. The runtime carries a child's fault code and category to its structural parent.
- **FR-205**: When the running step faults with `outcome-unknown` and has a handler, the scope journals an `Uncertain` entry with the next completion ordinal, records an uncertain recovery cause, absorbs the fault and starts recovery. The absorbed incident records that the forward outcome is unknown.
- **FR-206**: When the running step faults with `outcome-unknown` and has no handler, the scope journals an `Uncertain` entry and does not absorb the fault: it requests intervention on the step's incident (FR-209) and waits. It never treats the step as having had no effect.
- **FR-207**: Recovery compensates entries that are `Registered` or `Uncertain` with a handler, in descending completion ordinal. An uncertain entry keeps its uncertain forward outcome after its status changes.

**Handler failure and intervention**

- **FR-208**: A compensation scope has an input `handlerMaxAttempts` (default 3, minimum 1) that bounds how many times a handler runs for one entry before the scope waits for intervention. Every attempt is recorded on the entry with its execution and outcome. Operator retries count as attempts, and the limit is not reset by them.
- **FR-209**: The runtime lets a structural parent **request intervention** for a child's fault: in the same checkpoint as the parent's evaluation, the child's incident stays blocking and is given the outcome "wait for intervention", from a structural-intervention source, with metadata naming the scope, entry and step. The run stays running and no strategy acts on that incident again.
- **FR-210**: When a handler faults and the entry has attempts left, the scope absorbs the fault and schedules the same handler again, with the same scheduling identity and keys, in one commit. When no attempts are left, the entry becomes `Failed`, the scope requests intervention on the handler's incident and enters `AwaitingIntervention`. The recovery cause is never overwritten.

**Operator retry (#2747 core)**

- **FR-211**: A runtime alteration **retry faulted activity** targets a faulted activity occurrence that has a blocking incident in a running run. In one checkpoint it resolves that incident with an operator-retry outcome (actor and reason recorded) and schedules the activity again with its original inputs, parent, scheduling identity, scheduling cause and metadata (including assigned operation keys). It is idempotent through the alteration's existing idempotency key, refuses a target whose incident is no longer blocking, requires runtime-manage permission and is fenced like any runtime alteration. Retrying a terminal (faulted) run stays out of scope.
- **FR-212**: When an entry is `Failed` and its retried handler completes, the entry becomes `Compensated`, the scope returns to `Compensating` and recovery continues. When the retried handler faults, FR-210 applies again.
- **FR-213**: When an uncertain step without a handler is retried (FR-206) and completes, the scope turns the entry `NoHandler` (keeping its uncertain forward outcome) and continues forward. When it faults, the scope handles it as a new fault of the running step.

**Durability and repeats**

- **FR-214**: The scope's private state moves to version 2. Version-1 state is upgraded when it is read: statuses, ordinals and identities are kept, handler executions become completed attempts, and the operation key is set as in FR-203. Version 2 is never downgraded.
- **FR-215**: A repeat delivery of a forward or handler completion or fault, a completion for an entry that is no longer `Compensating` or `Failed`, or a completion of an attempt other than the latest, is ignored and never schedules work or changes state.
- **FR-216**: Stories 1–3 hold in Durable, Balanced and Ephemeral modes, each with restart tests at: after a forward effect with a lost response; between handler attempts; while waiting for intervention; and after an operator retry is submitted but before the handler completes.

**Studio**

- **FR-217**: Run inspection adds forward state `Uncertain` ("⚠ Uncertain"), recovery state `NeedsAction` ("✋ Needs action"), scope phase "Needs action" and a per-row attempt count ("Attempt n of m"). Each is text plus icon. A waiting row links to its blocking incident. States are still derived from existing run data (spec 250 FR-019): the incident's resolution outcome and metadata tell uncertain and waiting apart.
- **FR-218**: Studio's incident model reads the API's resolution outcome (action, source and metadata). Today it expects a field the API does not send, so the incident detail's resolution row is always empty.

**Documentation**

- **FR-219**: Spec 250's semantics contract and the program charter are amended to match OD2 (refined) and OD5: waiting for intervention is a blocking incident, resumed by #2747's retry, not a bookmark, and the program does reuse incident retry. The user guide documents the participant contract (FR-220).
- **FR-220**: The **participant contract** for compensable effects is: apply the forward effect at most once per operation key and return the original result on a repeat; undo the effect for a target key if it is present; if it is absent, remember the target key so that a later forward request for it is refused; make the undo idempotent per handler key; and report `outcome-unknown` only when the outcome cannot be determined. No reconciliation framework is provided.

### Key Entities

- **Operation key**: the stable identity of one logical effect, given to the participant on every attempt.
- **Target operation key**: on a handler, the operation key of the forward effect it undoes.
- **Uncertain entry**: a journal entry for a step whose outcome is unknown; compensable, and never treated as "no effect".
- **Handler attempt**: one run of a handler for an entry, with its execution, incident and result.
- **Intervention wait**: the scope phase `AwaitingIntervention`, backed by a blocking incident with outcome "wait for intervention".
- **Retry alteration**: the operator request that resolves a blocking incident and reschedules its activity (#2747).

## Success Criteria

- **SC-201**: In the lost-response journey, with the effect present and with a late arrival, the participant ends with no reservation in every persistence mode, across host kills at each FR-216 point.
- **SC-202**: A handler that fails fewer times than its limit leads to exactly one undo; one that fails on every attempt leads to a wait with every attempt recorded, the original cause intact and the run still running.
- **SC-203**: After an operator retry, recovery completes with one undo per entry, also when the retry is submitted twice.
- **SC-204**: Each M2 negative control fails when its safeguard is removed and passes when it is restored: the scope-assigned operation key on retries, treating `outcome-unknown` as an uncertain entry, the intervention request, and the retry alteration's idempotency.
- **SC-205**: Studio shows every new state as text plus icon, and the axe check reports no violations on the run view.

## Assumptions

- Participants that need exactly-once effects implement FR-220. Elsa does not provide exactly-once delivery (program non-goal).
- Handler retries without backoff are acceptable for M2, because the limit is small and an operator can always resume (OD7).
- The #2747 retry alteration is built for runs that are still running. Moving a faulted run back to running stays out of scope.
- The Studio Retry button, abandon and mark-resolved actions are M4. M2 operators retry through the API.
- Spec 250's M1 behaviour stays unchanged for scopes whose steps never report `outcome-unknown` and whose handlers succeed on their first attempt.

## Out of scope

Retry backoff and the structural delay seam (M5). Confirmation, nested, repeated and late compensation, the explicit Compensate activity and cancellation policy (M3). Studio operator actions (M4). A general reconciliation or connector framework, retrying terminal runs, and exactly-once effects.
