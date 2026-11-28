# Runtime faults and recovery

An activity can finish with an execution fault. The workflow model applies its
incident strategy and records the committed result for inspection. Open the
run's History to see committed activity evidence. See
[how a workflow executes](how-a-workflow-executes.md) for the read path.

Park records a delivery that the owner cannot safely complete. It preserves the run and the relevant deliveries in the store. Wait
for the host to report the parked run, then select that exact Park for recovery.
Recovery can restore waiting state or retry eligible selected deliveries.
Use the run and Park identity from the current read, since a stale selection
can refer to an earlier failure or creation.

See the [Park design](architecture/observability.md#what-park-keeps) for the
capture protocol and [program scope](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-7142657677)
for the released recovery operations. The [classic fault guide](https://github.com/elsa-workflows/elsa-foundation/blob/18a16dee878106ac525aa9a861d851a200e43c55/docs/runtime-fault-behavior.md)
explains the earlier scheduler's fault and poison records.
