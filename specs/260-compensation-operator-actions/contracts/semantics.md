# Contract: M4 scope transitions

Extends [spec 258 contracts/semantics.md](../../258-compensation-nesting/contracts/semantics.md). Transitions not mentioned here are unchanged. Every transition commits the scope's private state in the same checkpoint as the evaluation that caused it.

## Requesting intervention (changed)

Both places where the scope waits now offer decisions:

```
offer = [compensation.resolve.effect-absent, compensation.resolve.effect-accepted, compensation.abandon]

OnHandlerFaulted, attempts exhausted (spec 251):
  requestIntervention(incident, reason, metadata as M2, decisions = offer)

WaitForStep, uncertain step without a handler (spec 251, also after a retried step is uncertain again):
  requestIntervention(incident, reason, metadata as M2, decisions = offer)
```

## Applying a decision

```
OnInterventionDecided(incidentId, decision, via child c):
  phase is not AwaitingIntervention: ignore, Defer                     [repeat or stale; nothing changes]
  e = the entry whose interventionIncidentId = incidentId (own or received)
  no such e: ignore, Defer

  decision = compensation.abandon:
    abandonment = { incidentId, entryId: e.id, fromPhase: Running if e is a waiting step else Compensating }
    e.interventionIncidentId = null      (status unchanged: Failed or Uncertain)
    phase Abandoned
    → Faulted(compensation.scope.abandoned, category outcome-unknown)
      no subtree cancellation (a fault cannot ride one, spec 258 R13)

  decision = compensation.resolve.effect-absent | compensation.resolve.effect-accepted:
    e' = e with status ResolvedManually, resolution = { outcome, incidentId }, interventionIncidentId = null
    c is not the faulted activity (a descendant fault, spec 258 R-3): cancel c's subtree in this evaluation

    e was Failed (a handler waited):
      phase Compensating; schedule the next eligible entry, or finish as in spec 251
      (finish: nothing Failed is left, so Completed Compensated, or faultAfterCompensation)

    e was Uncertain without a handler (a step waited, the scope had not started recovery):
      EffectAbsent:   phase Compensating; recover the earlier entries newest first (cause stays uncertain);
                      finish as for any recovery
      EffectAccepted: phase Running; schedule step[i+1] with scheduling identity c, or complete Done
                      (hand over or confirm as in spec 258; e' is neither)

    When the finish must fault and a subtree cancellation is staged: fault at once without the cancellation,
    as spec 258 R13; the enclosing scope's absorption, or the run's end, cancels the subtree.
```

"Finish" uses spec 258's `FinishFault`: an explicit or requested cause completes `Compensated`; a cause target passes the request on; otherwise `faultAfterCompensation` decides.

## Other transitions in phase `Abandoned`

```
OnChildCompleted / OnChildFaulted / OnDescendantFaulted: ignore, Defer   (the scope has faulted; nothing reaches it
                                                                          in practice, the runtime acks late outcomes)
OnChildNotified (register, confirm, compensate): ignore
```

## Enclosing scope receiving `compensation.scope.abandoned`

Category `outcome-unknown`, so spec 251's rule applies to the step (or handler) that was the abandoned scope:

```
as a step:    has a handler → journal Uncertain, absorb, recover (it is the newest entry, so its handler runs first)
              no handler    → journal Uncertain, request intervention with the same offer, AwaitingIntervention
as a handler: a failed attempt; retry while attempts are left, then wait with the same offer
```

## Rules

- **Lock**: a decision applies only to the entry whose `interventionIncidentId` it names; the alteration already checked that the incident was waiting (R1).
- **Settled is final**: `ResolvedManually` is never eligible, confirmed or handed over; the resolution never changes.
- **No recovery of recovery**: a resolve does not start a new cause; the original cause is never overwritten.
- **Repeats** (a second delivery of the same work item, or a decision for an entry already resolved) change nothing.
- **Cancellation**: a scope cancelled after the alteration commits never sees the decision (the delivery is acked); its journal shows the entry as it was.
- **Persistence modes**: the transitions above commit in the requester's evaluation checkpoint, so Durable, Balanced and Ephemeral behave alike; the delivery item is durable from the alteration checkpoint in every mode.
