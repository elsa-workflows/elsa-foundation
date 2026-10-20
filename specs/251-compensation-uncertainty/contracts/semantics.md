# Contract: M2 scope transitions

This contract extends [spec 250 contracts/semantics.md](../../250-native-compensation/contracts/semantics.md). M1 transitions not mentioned here are unchanged. Every transition commits the scope's private state in the same checkpoint as the evaluation that caused it.

## Scheduling a step or handler (changed)

- **Step `i`:** scheduling metadata adds `runtime.operationKey = {scope AEI}/step/{i}`.
- **Handler of entry `e`:** scheduling metadata adds
  - `runtime.operationKey = {e.operationKey}/compensate`;
  - `runtime.targetOperationKey = e.operationKey`;
  - `compensation.attempt = n`.

  It also appends `{ activityExecutionId: null-until-known, result: Running }` to `e.handlerAttempts` in the same commit. The attempt's execution id is recorded when the handler completes or faults, matched by `compensation.entryId` and node id as in M1.
- **Eligible entries:** `Registered`, or `Uncertain` with a handler. They are taken in descending `ordinal`.

## Transitions

```
OnChildFaulted(child, incident, faultCategory)
  phase Running and child is the current step:
    faultCategory = outcome-unknown:
      step has handler    -> append entry(i, child AEI, ordinal++, Uncertain, forwardOutcome Uncertain,
                                          operationKey {scope}/step/{i})
                             cause = {kind uncertain, ...}
                             absorb(incident, compensation.forwardOutcome=unknown)
                             phase Compensating, schedule next eligible entry (or finish as in M1)
      step has no handler -> append entry(i, ..., Uncertain, forwardOutcome Uncertain)
                             cause = {kind uncertain, ...}
                             requestIntervention(incident, compensation.forwardOutcome=unknown, scope, entry, step)
                             e.interventionIncidentId = incident; phase AwaitingIntervention; Defer
    otherwise             -> M1 (absorb, cause kind fault, recover)

  phase Compensating and child is the running handler of entry e (latest attempt):
    attempts(e) < handlerMaxAttempts
                          -> mark latest attempt Faulted {AEI, incident}
                             absorb(incident, compensation.attempt, compensation.maxAttempts)
                             schedule e's handler again (new attempt), Defer
    otherwise             -> mark latest attempt Faulted; e Failed; e.interventionIncidentId = incident
                             requestIntervention(incident, scope, entry, step, attempt, maxAttempts)
                             phase AwaitingIntervention; Defer

  phase AwaitingIntervention and child is the retried handler of the Failed entry e
  (operator retry successor; latest attempt):
                          -> as the Compensating branch above, counting the operator retry as an attempt

  phase AwaitingIntervention and child is the retried uncertain step without a handler:
                          -> treat as a new fault of the running step (phase Running rules), keeping the entry.
                             The retry used the step's operation key, so under the participant contract (FR-220) a
                             definite fault says no effect exists for that key; nothing is left to undo for it

  otherwise               -> do not absorb; Defer   (M1)

OnChildCompleted(child)
  phase Compensating or AwaitingIntervention and child is the latest-attempt handler of entry e,
  where e is Compensating or Failed:
                          -> latest attempt Completed {AEI}; e Compensated; e.interventionIncidentId = null
                             phase Compensating; schedule next eligible entry, or finish as in M1
  phase AwaitingIntervention and child is the retried uncertain step i without a handler:
                          -> e NoHandler, forwardOutcome Uncertain kept;
                             phase Running; schedule step[i+1] or complete Done as in M1
  any other child         -> ignore, Defer   [FR-215]

OnChildRetried (operator successor scheduled)
  no scope evaluation: the alteration supersedes the faulted child and schedules its successor under the same
  parent. The scope only sees the successor's completion or fault.
```

"Latest attempt" means the execution id equals the latest attempt's recorded id, or that attempt has no id yet and the child carries the entry's `compensation.entryId` and the handler's node id. A completion from any earlier attempt is ignored.

## Finishing (unchanged meaning)

With no eligible entry left, the scope behaves as in M1:

- it completes `Compensated`;
- or it faults with the original cause when `faultAfterCompensation` is true.

`Uncertain` entries whose handler succeeded count as compensated. A scope never finishes while an entry is `Failed`.

## Runtime seams

| Seam | Contract |
|---|---|
| `ActivityExecutionContext.OperationKey` / `TargetOperationKey` | Resolution order: assigned metadata, then first-attempt execution id, then invocation id. `TargetOperationKey` is `null` unless assigned. Both are stable across crash re-attempts, boundary retries and operator retries. |
| `ActivityChildFaultedContext.FaultCode` / `FaultCategory` | Taken from the faulted child's committed normalized fault. `null` when the fault carries none. |
| `RequestChildFaultIntervention(incidentId, reason, metadata)` | Valid only in a child-fault evaluation with a `Defer` continuation, for that evaluation's incident, at most once, and never together with absorption. In the same checkpoint the incident stays `Blocking` and gets outcome `WaitForIntervention` from source `StructuralIntervention`, carrying the given metadata. The child's subtree is not cancelled. A violation faults the evaluation, as absorption does. |
| Absorption metadata | `RequestChildFaultAbsorption`'s metadata is copied into the resolved incident's outcome metadata. |

## Persistence modes

FR-216's injection points apply in Durable, Balanced and Ephemeral. In every mode the pre-effect flush (spec 250 FR-013) guarantees that a step or handler which reached its body has its operation key durable before the effect.
