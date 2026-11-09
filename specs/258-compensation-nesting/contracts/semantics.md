# Contract: M3 scope transitions

Extends [spec 251 contracts/semantics.md](../../251-compensation-uncertainty/contracts/semantics.md). Transitions not mentioned here are unchanged. Every transition commits the scope's private state in the same checkpoint as the evaluation that caused it.

## Scheduling (changed)

Every child the scope schedules carries its routing keys (`compensation.role`, `compensation.scopeActivityExecutionId`, `compensation.step`, `compensation.entryId`, `compensation.attempt`, and for handed-over handlers `compensation.originScopeActivityExecutionId` / `compensation.originEntryId`) in both the request metadata and the scheduling provenance metadata (FR-304). The scope recognises a child's outcome from command metadata, falling back to the child's provenance metadata.

A handed-over entry's handler is `entry.handlerExecutableNodeId`, scheduled as the scope's own child with:

- scheduling identity `entry.forwardActivityExecutionId`;
- `runtime.operationKey = {entry.operationKey}/compensate`, `runtime.targetOperationKey = entry.operationKey`;
- scheduling cause `compensation`.

## Transitions

```
OnChildCompleted(child) — last step completed, phase Running:
  if confirmOnComplete:
    confirm every eligible entry (record confirmation) → Completed, Done
  else:
    eligible = entries where status in {Registered, NoHandler, Uncertain} (own and received)
    send compensation.register to the nearest enclosing CompensationScope,
      stop at a reusable boundary, "maybe" delivery (R-1, R-2), ≤16 entries per message
    → Completed, Done   (private state is cleared by the runtime)

OnChildNotified(code = compensation.register, via child c, payload):
  phase Completed / Compensated: ignore
  if payload.originScope in receivedHandOvers: ignore (repeat)
  append entries with fresh ordinals in payload order, keeping origin (first origin wins);
    an entry whose origin is already journaled is skipped (a repeated batch)
  record payload.batch for originScope in handOversInProgress; once every batch 0..batchCount-1
    is journaled, move originScope to receivedHandOvers (batches may arrive in any order)
  phase Running: stay Running (continue waiting for c's completion)
  phase Compensating / AwaitingIntervention: schedule nothing now; the new entries are eligible,
    so the next handler scheduled (when the running handler completes, or after an operator
    retry ends the wait) is the newest of them
  → Defer

OnChildNotified(code = compensation.confirm, via child c):
  phase Running and c is one of the scope's steps, not a repeat of the same Confirm:
    mark every Registered / NoHandler / Uncertain entry Confirmed; record confirmation → Defer
  otherwise (recovery running, or c is a handler): ignore, no fault (research R11)

OnChildNotified(code = compensation.compensate, via child c):
  phase is not Running, or c is not the running step (recovery running, or c is a handler):
    ignore, no fault (research R11)
  phase Running and c is the running step:
    cause = { kind: explicit, step, requestedBy, target }   target = the request's target when it names
      a scope other than this one (an outer scope), else none
    c is the Compensate itself: nothing to journal or stop; recover as after a fault
    otherwise: journal c Uncertain (handler or not), then
      nothing eligible and the finish below faults: fault at once without stopping c (research R13)
      else: cancel c's subtree (seam A) and recover as after a fault (NextEligible, newest first)

OnChildFaulted(child, code = compensation.scope.compensate-requested), phase Running:
  cause = { kind: requested, step, activityExecutionId }; absorb; recover as after a fault

OnDescendantFaulted(via child c, incident) — R-3, phase Running and c is the running step:
  exactly as OnChildFaulted for c, except that absorption cancels c's subtree

Recovery finished:
  if any entry is Uncertain without a handler created by Compensate:
    Faulted(compensation.scope.uncertain-unhandled)
  else if cause.target is set: Faulted(compensation.scope.compensate-requested,
    category compensation.target:{target}), so the next scope out recovers in turn
  else if cause.kind is explicit or requested: Completed, Compensated (research R12)
  else as spec 251 (Compensated, or faultAfterCompensation)

A target that matches no enclosing scope is not refused at run time: every enclosing scope recovers
in turn and the outermost one faults with compensation.scope.compensate-requested. Publishing refuses
a literal target that does not enclose the Compensate (FR-312).
```

## Rules

- **Confirmed** entries are never eligible and never handed over (FR-311).
- **Repeats** (a second hand-over from the same origin, a second Confirm or Compensate delivery of the same notification) change nothing (FR-319).
- **Cancellation** has no transition: the scope is not called (R7). Its state stays as last committed.
- **Ordering**: received entries take fresh ordinals, so "newest first" stays the single recovery order across own and received entries (spec 250 ordinal contract).
