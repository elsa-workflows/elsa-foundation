# Research: Execute DispatchWorkflow Across Distributed Nodes

## Decision: Keep `IWorkflowStartDispatcher` As The Activity-Facing Seam

**Rationale**: Parent #674 and child #683 require the activity to remain transport-neutral. The child-start handler already calls the workflow start dispatcher with retained pin, authority, tenant, partition, run kind, and test scope. Distributed execution belongs behind the configured actor provider.

**Alternatives considered**:

- Add activity inputs for node, queue, priority, or routing channel. Rejected by #683 and parent #674 scope.
- Add a DispatchWorkflow-specific transport abstraction. Rejected because durable correctness already belongs to runtime checkpoint/outbox and actor-provider seams.

## Decision: Exercise Existing Groundwork Distributed Provider

**Rationale**: #683 names the existing distributed Groundwork execution actor provider, durable command transport, placement leases, and fencing as the required implementation surface. The slice should close integration/test gaps rather than introduce a parallel distributed runtime.

**Alternatives considered**:

- Simulate cross-node behavior only with fake actor providers. Rejected because acceptance requires provider durability, placement, and fencing.
- Introduce broker-backed transport. Rejected as explicitly out of scope.

## Decision: Treat Checkpoint Fencing As The Safety Boundary

**Rationale**: Duplicate delivery and stale placement are distributed runtime realities. The durable provider must converge with existing deterministic dispatch/child identities and provider fences, not with activity-level locks or mutable transport state.

**Alternatives considered**:

- Add activity-level duplicate suppression. Rejected because it leaks transport concern into the activity contract and cannot protect stale provider writers.

## Decision: Readiness Classification Is Composition Evidence

**Rationale**: #683 requires distinguishing in-memory development, durable single-node Groundwork, and distributed Groundwork. This should be exposed as composition/readiness diagnostics derived from registered persistence/outbox/distributed actor-provider capabilities, not from workflow-authored inputs.

**Alternatives considered**:

- Treat any Groundwork persistence as distributed-ready. Rejected because single-node durable recovery and two-node placement are different operational guarantees.

## Decision: Two-Node Tests May Be Process-Local Hosts

**Rationale**: Deterministic tests can use two service providers with different node IDs sharing the same durable Groundwork storage. That proves the provider and fencing semantics without requiring external orchestration.

**Alternatives considered**:

- Spawn separate OS processes. Rejected unless needed, because the acceptance criteria require separate logical nodes and durable provider state, not process isolation.
