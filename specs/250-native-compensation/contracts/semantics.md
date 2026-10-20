# Contract: compensation scope semantics

This contract fixes the behaviour of `Elsa.CompensationScope` across milestones. M1 implements the
states and transitions marked **M1**. Later milestones add states without changing M1's meaning.

## Scope phases

| Phase | Meaning | Milestone |
|---|---|---|
| `Running` | Forward steps are being scheduled in order. | M1 |
| `Compensating` | Recovery started; handlers are being scheduled newest first. | M1 |
| `AwaitingIntervention` | A handler failed after its retries; an operator must act (OD2). The handler's incident stays blocking with outcome `WaitForIntervention`; an operator resumes it with `RetryFaultedActivity/1` ([spec 251](../../251-compensation-uncertainty/contracts/semantics.md)). | M2 |
| `Completed` | All steps completed. Outcome `Done`. The runtime clears the journal on completion, so late compensation (OD3) needs a durable journal that M3 must add. | M1 (outcome), M3 (late compensation) |
| `Compensated` | Recovery finished; every registered entry is `Compensated` or explicitly resolved. Outcome `Compensated`. | M1 |
| `Confirmed` | Entries are no longer eligible for compensation. | M3 |
| `Abandoned` | An operator abandoned recovery. The scope faults with the cause; unresolved entries stay visible as unresolved. | M4 |

## Entry statuses

| Status | Meaning | Eligible for recovery | Milestone |
|---|---|---|---|
| `Registered` | Forward occurrence completed and has a handler. | yes | M1 |
| `NoHandler` | Forward occurrence completed; the step has no handler. | no (shown as "Not needed") | M1 |
| `Compensating` | Handler's schedule request committed with this status; its execution id is recorded on completion. | in progress | M1 |
| `Compensated` | Handler completed. | no | M1 |
| `Failed` | Handler faulted; awaiting retry or intervention. | after intervention | M2 |
| `Uncertain` | Forward outcome unknown (lost response). Recovery runs its reconcile-or-compensate handler. | yes | M2 |
| `ResolvedManually` | An operator recorded an out-of-band resolution, with reason. | no | M4 |
| `Confirmed` | Eligibility ended by confirmation. | no | M3 |

No status is inferred from another. A scheduled handler is `Compensating`, never `Compensated` (PRD FR7).

## Transitions (M1)

All transitions happen inside the scope's own runtime evaluations. Each one commits the scope's private
state in the same checkpoint as the evaluation that caused it.

```
ExecuteStructure (activation)
  steps empty            -> complete Done
  else                   -> phase Running, schedule step[0], Defer

OnChildCompleted(child)
  phase Running:
    child is step[i], i = nextStepIndex - 1 (the running step), and no entry holds child AEI
                         -> append entry(i, child AEI, ordinal++ , Registered|NoHandler)
                            if i is last -> phase Completed, complete Done
                            else schedule step[i+1], Defer
  phase Compensating:
    child is the handler of the entry in Compensating (matched by compensation.entryId metadata and node id)
                         -> entry Compensated
                            next = highest-ordinal Registered entry
                            next exists -> entry(next) Compensating, schedule handler, Defer
                            none        -> phase Compensated, complete Compensated, or fault with the step's fault
                                           when faultAfterCompensation is true
  any other child        -> ignore (repeat delivery or stale), Defer   [FR-009]

OnChildFaulted(child, incident)
  phase Running and child is the current step
                         -> absorb(incident), cause = {step, AEI, incident, fault}
                            phase Compensating, schedule highest-ordinal Registered entry (as above)
                            none registered -> phase Compensated, complete Compensated; when
                                               faultAfterCompensation is true, do not absorb and fault
  phase Compensating and child is the running handler
                         -> M1: do not absorb; Defer. The blocking incident stays and the run's incident
                            strategy decides. Entry stays Compensating.            [M2: spec 251 retries, then waits]
  otherwise              -> do not absorb; Defer
```

"Current step" means the direct child the scope scheduled last. M1 supports leaf steps and steps whose faults
reach the scope as a fault of that child (a Flowchart step re-faults). A fault that never reaches the scope as a
direct-child fault — a Sequence step, which absorbs its children's faults — leaves the incident blocking and the
scope running; the M3 composition matrix closes that gap. Any fault the scope does not own (a stale child, a child
of a phase the scope left) is never absorbed and keeps blocking the run.

"Schedule handler" passes:

- scheduling activity execution id = the entry's forward activity execution id (FR-007, FR-010);
- metadata `compensation.role = handler`, `compensation.scopeActivityExecutionId`, `compensation.entryId`,
  `compensation.forwardActivityExecutionId`, `compensation.step`;
- scheduling cause `compensation`.

Forward steps are scheduled with metadata `compensation.role = step` and `compensation.step`, so inspection can
label both kinds without reading private state.

## Ordering

Recovery order is descending completion ordinal, assigned by the scope when it appends the entry. With
sequential steps this is reverse step order. The ordinal, not the step index, is the contract, so loops and
nested scopes (M3) keep "newest first" without a new rule.

## Crash and repeat-delivery behaviour

| Point of failure | State after restart | Result |
|---|---|---|
| Forward step claimed, effect done, completion not committed | Step re-attempted with the same invocation id (`External` claim was flushed before the body) | Participant de-duplicates by invocation id; one entry |
| Step completion committed, parent evaluation not yet applied | Owed parent evaluation replays | One entry: the append and cursor move commit together |
| Handler scheduled (entry `Compensating`), effect done, completion not committed | Handler re-attempted with the same invocation id | Participant de-duplicates; entry becomes `Compensated` once |
| Handler completion delivered twice | Second delivery finds the entry already `Compensated` | Ignored (FR-009) |
| Between two handlers | Next entry is either still `Registered` (nothing scheduled) or `Compensating` with its handler owed | Exactly one handler per entry |

The scope never schedules a handler without committing `Compensating` and the schedule request in the same
checkpoint. A restart therefore never needs to guess whether a handler was started.

## Persistence modes

The pre-effect boundary (the `ActivityAttemptClaimed` flush for `External` activities) applies in all three
modes, so the table above holds in each. What differs is how much replays: in Balanced and Ephemeral,
`ReplaySafe` work since the last commit runs again. That never schedules a second handler for an entry,
because the entry status commits together with the schedule request. Each mode is claimed only with its restart tests
passing (FR-013).

**Decision (19 October 2026):** M1 supports all three modes. Issue #3043 proposed Durable plus Balanced with a
mandatory pre-effect boundary, and refusing Ephemeral for compensated external work. The pre-effect flush already
applies to `External` activities in every mode, and the P1–P4 restart matrix (`CompensationScopeRestartTests`,
#3083) passes in Durable, Balanced and Ephemeral, so refusing Ephemeral would remove a working mode without a
failing case. FR-013's gate still holds: a mode whose restart tests stop passing loses its support claim. A step or handler marked
`ReplaySafe` loses that boundary in every mode; FR-014's warning (deferred to M5) covers it.

## Recovery cause

The recovery cause is recorded in the scope's private state and returned by the inspection projection. Structural activities complete with an outcome only (`RuntimeStructuralContinuation`), so exposing the cause as an activity output needs structural output support (research R12). Its shape:

```json
{ "kind": "fault", "step": "Confirm order", "activityExecutionId": "…", "incidentId": "…",
  "faultCode": "order.rejected", "message": "…" }
```

The message follows the existing fault/incident disclosure policy; a withheld message is returned withheld,
never as blank.

## Later milestones (semantics fixed now)

- **M2 uncertainty.** An activity reports an uncertain outcome through a reserved fault category
  `outcome-unknown`. The scope records an `Uncertain` entry for that occurrence, which is eligible for recovery,
  instead of treating it as "never completed". Its handler must be safe whether or not the effect exists
  (compensate-if-present). A late forward request for an uncertain occurrence is refused by the participant, which
  remembers the compensated operation key ([spec 251](../../251-compensation-uncertainty/spec.md) FR-220); it does
  not reopen the forward path.
- **M2 handler failure.** Bounded immediate retries per scope (`handlerMaxAttempts`; backoff is M5). When they are
  exhausted, the entry becomes `Failed` and the scope enters `AwaitingIntervention`: the handler's incident stays
  blocking with outcome `WaitForIntervention`, and an operator resumes it with the `RetryFaultedActivity/1`
  alteration (#2747). A structural activity cannot hold a bookmark, so the wait is the incident, not a bookmark.
  The original cause and every handler attempt stay separate ([spec 251](../../251-compensation-uncertainty/spec.md)).
- **M3 late compensation (OD3).** A completed scope's journal stays compensable. Prerequisite: the runtime clears
  structural private state on completion today, so M3 first needs a durable journal that outlives the scope (a
  runtime opt-in or a compensation projection); execution metadata and incidents cannot reconstruct entry statuses,
  ordinals or handler occurrences. An enclosing scope that starts
  recovery treats each completed inner scope as one entry and recovers it by re-entering the inner scope node in
  recovery mode with the inner journal. Confirmation (a `Confirm` activity or scope setting) moves entries to
  `Confirmed`.
- **M3 explicit trigger.** A `Compensate` activity raises an ancestor notification to the nearest or a named
  scope. This needs ancestor targeting by activity type, because CLR activities share one consumer key; this
  program adds that runtime extension (research R4).
- **M3 cancellation.** Whether workflow or subtree cancellation starts recovery is a product decision that needs
  a runtime cancellation hook (research R7). Until then, cancellation runs no handler, and the docs say so.
