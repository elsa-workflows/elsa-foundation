# Elsa.Activities.Compensation

The native `CompensationScope` activity. It runs named steps in order. When a step faults, it absorbs the fault and
undoes the steps that completed by running their compensation handlers one at a time, newest first, then completes
with the `Compensated` outcome. A scope whose steps all complete finishes with `Done` and runs no handler. The
`faultAfterCompensation` input makes the scope fault with the step's fault after recovery instead.

Each step is a forward activity in slot `CompensationScope.Step[{name}]` with an optional handler in
`CompensationScope.Compensation[{name}]`. A handler is scheduled with its forward occurrence as the scheduling
execution, so its inputs bind to that exact occurrence's committed result. Forward and handler activities should
use `ActivityExecutionContext.OperationKey` as the idempotency key sent to external systems. It remains stable
across crash recovery and automatic/operator retries. A handler receives the forward effect's key as `TargetOperationKey`.

While the scope is active, its journal of completed steps lives in its private state and is committed with every
child evaluation; an entry is marked compensating in the same commit as its handler's schedule request. A restart
may repeat a physical call, so handlers must honor the operation key. The runtime clears structural private state when the scope completes; the
forward and handler executions keep the scope's `compensation.*` scheduling metadata, which is the completed
record. Completion and fault callbacks read that metadata from the child's committed scheduling provenance,
so a bookmark resume uses the same scope/step identity as a synchronous child.

A forward fault categorized `outcome-unknown` is journaled as uncertain and recovered like a completed step when
it has a handler; without one, the scope waits for intervention. Failed handlers retry up to `handlerMaxAttempts`
(default 3), then retain a blocking intervention incident. An operator can continue with `RetryFaultedActivity/1`.
Workflow cancellation runs no handler; a fault inside a `Sequence` step does not reach the scope because `Sequence`
does not propagate child faults. Native compensation is separate from BPMN compensation
([ADR 0086](../../../../docs/adr/0086-native-and-bpmn-compensation-stay-separate.md)). Semantics and milestones:
[spec 250](../../../../specs/250-native-compensation/spec.md) and
[spec 251](../../../../specs/251-compensation-uncertainty/spec.md).

The runtime activity class references only the runtime contract surface; the design-side structure handler and
Draft validator (`Internal/`) reference `Elsa.Workflows.Design.*`.
