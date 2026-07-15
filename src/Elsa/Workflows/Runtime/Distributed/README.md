# Elsa.Workflows.Runtime.Distributed

A provider leaf that turns the in-process workflow-execution actor subsystem into a clustered one. It adds
per-execution placement (routing), a durable cross-node command transport, and passivation/reactivation on top of the
existing runtime, and it replaces the single active `IWorkflowExecutionActorProvider` with
`DistributedWorkflowExecutionActorProvider`. Core runtime contracts gain zero references to this leaf (provider
isolation, constitution S=2.7).

## Placement is routing; fencing is safety — the heart of the unit

Placement is the routing layer. A per-execution placement lease decides which node runs the drain for a workflow
execution, so that under normal operation exactly one node holds the mailbox and commands are serialized through it.
Placement is deliberately best-effort: leases expire on a `TimeProvider`-driven clock, and a node that loses the
network or dies simply stops renewing, letting a survivor claim the execution. Placement never, by itself, guarantees
that two nodes cannot both believe they own the same execution at the same instant — during a lease-handover window
they transiently can.

Fencing is the safety layer, and it is authoritative. Every drain acquires W5's monotonic execution fencing token from
the shared liveness store; the checkpoint committer re-checks that token at commit time and rejects any write whose
token is not the highest observed. So even if placement routing is wrong for a window — even if a dead node resurrects
mid-drain and reaches its commit — its stale, lower fencing token is rejected and its writes never land. Placement
decides where work runs; fencing decides whether a commit is allowed to persist. Double durable execution is prevented
by fencing, not by placement, which is why the distributed provider consumes the fencing seam unchanged and adds only
routing on top.

## Delivery contract

Cross-node command delivery is **at-least-once**. When a command arrives on a node that does not own an execution's
placement, the forwarding actor sends it to the durable transport inbox and returns a `Deferred` dispatch result: the
command is accepted for routing but not run locally. The owning node's placement pump leases pending inbox items and
dispatches them to its local in-process actor. Dequeue is ack-based (lease/visibility), not destructive-before-dispatch:
if the owning node dies after leasing an item but before acking it, the lease expires and the item becomes visible
again, so the survivor that claims placement re-leases and re-drives it on failover. This mirrors W2's queue semantics
and the ack-based hold-until-commit dequeue recorded in `docs/runtime-durable-resumption.md`. Re-driven commands are
made safe — not merely deduplicated — by the fencing token described above.

## In-memory defaults and durable Groundwork stores

The placement store and command transport in this unit are in-memory implementations, shared by every node container in
a single process (that is the two-node test harness shape). They are the default when the host does not select a durable
provider. The opt-in `WorkflowsRuntimeDistributedGroundworkPersistence` feature replaces them with scoped,
Groundwork-backed placement and transport stores that share the same frozen v1 wire format and survive process restarts.
