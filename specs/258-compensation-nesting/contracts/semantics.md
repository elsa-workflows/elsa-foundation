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
  if payload.originScope in receivedHandOvers: ignore (repeat)
  append entries with fresh ordinals in payload order, keeping origin (first origin wins)
  when the last batch of the hand-over is journaled: add originScope to receivedHandOvers
  phase Running: stay Running (continue waiting for c's completion)
  phase Compensating / AwaitingIntervention: new entries are eligible in the current recovery;
    if no handler is running, schedule NextEligible
  → Defer

OnChildNotified(code = compensation.confirm):
  phase Running: mark every Registered / NoHandler / Uncertain entry Confirmed; record confirmation → Defer
  otherwise: fault the notifying path with compensation.confirm-during-recovery (the Confirm activity faults)

OnChildNotified(code = compensation.compensate, via child c):
  notifying path is a handler: compensation.compensate-in-handler
  target names an outer scope: compensate self, then fault with compensation.scope.compensate-requested
  phase Running and c is the running step:
    cause = { kind: explicit, step, requestedBy }
    cancel c's subtree (seam A)
    if c had started: journal Uncertain (handler or not)
    start recovery as after a fault (NextEligible, newest first)
  any other phase: ignore (recovery already running)

OnChildFaulted(child, code = compensation.scope.compensate-requested), phase Running:
  cause = { kind: requested, step, activityExecutionId }; absorb; recover as after a fault

OnDescendantFaulted(via child c, incident) — R-3, phase Running and c is the running step:
  exactly as OnChildFaulted for c, except that absorption cancels c's subtree

Recovery finished:
  if any entry is Uncertain without a handler created by Compensate:
    Faulted(compensation.scope.uncertain-unhandled)
  else as spec 251 (Compensated, or faultAfterCompensation)
```

## Rules

- **Confirmed** entries are never eligible and never handed over (FR-311).
- **Repeats** (a second hand-over from the same origin, a second Confirm or Compensate delivery of the same notification) change nothing (FR-319).
- **Cancellation** has no transition: the scope is not called (R7). Its state stays as last committed.
- **Ordering**: received entries take fresh ordinals, so "newest first" stays the single recovery order across own and received entries (spec 250 ordinal contract).
