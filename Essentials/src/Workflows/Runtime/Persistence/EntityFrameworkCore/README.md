# Runtime EF history failure boundary

Run-history reads, independent delivery observations and explicit history retention normalize relational provider failures to a Runtime-owned `IOException` sibling. It preserves the original exception as `InnerException` and localizes the operation, persistence scope and workflow execution. The named EF exception is internal; callers use the portable I/O family documented by the Runtime history ports.

The I/O base is deliberate: existing history delivery callers distinguish dependency I/O from hard argument, policy and integrity errors. Cancellation, caller-transaction/dirty-context refusal and corrupt history retain their existing classifications. Only the existing relational provider-failure classifier triggers normalization. The observation writer keeps its narrow three-attempt head-conflict bound, transaction rollback and tracker cleanup. A commit or disposal failure can follow a durable write; normalization does not grant permission to repeat an effect or bypass the existing immutable replay and acknowledgement protocol.

For history composition, ownership and retention behavior, see the [Runtime history documentation](../../Elsa.Workflows.Runtime/README.md#run-history-and-timeline-inspection).
