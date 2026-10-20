# Elsa.Activities.Compensation

The native `CompensationScope` activity. It runs named steps in order. When a step faults, it absorbs the fault and
undoes the steps that completed by running their compensation handlers one at a time, newest first, then completes
with the `Compensated` outcome. A scope whose steps all complete finishes with `Done` and runs no handler. The
`faultAfterCompensation` input makes the scope fault with the step's fault after recovery instead.

Each step is a forward activity in slot `CompensationScope.Step[{name}]` with an optional handler in
`CompensationScope.Compensation[{name}]`. A handler is scheduled with its forward occurrence as the scheduling
execution, so its inputs bind to that exact occurrence's committed result. Forward and handler activities should
use their invocation id (`ActivityExecutionContext.InvocationId`) as the idempotency key they send to external
systems: a crash after an effect re-runs the activity with the same id.

While the scope is active, its journal of completed steps lives in its private state and is committed with every
child evaluation; an entry is marked compensating in the same commit as its handler's schedule request, so a restart
never runs a handler twice for one entry. The runtime clears structural private state when the scope completes; the
forward and handler executions keep the scope's `compensation.*` scheduling metadata, which is the completed
record.

Limits in this version: a handler fault stays a blocking incident and the run's incident strategy decides; workflow
cancellation runs no handler; a fault inside a `Sequence` step does not reach the scope because `Sequence` does not
propagate child faults. Native compensation is separate from BPMN compensation
([ADR 0086](../../../../docs/adr/0086-native-and-bpmn-compensation-stay-separate.md)). Semantics and milestones:
[spec 250](../../../../specs/250-native-compensation/spec.md).

The runtime activity class references only the runtime contract surface; the design-side structure handler and
Draft validator (`Internal/`) reference `Elsa.Workflows.Design.*`.
