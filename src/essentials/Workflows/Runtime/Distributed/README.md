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

## Version-aware placement, draining and failover (spec 184)

Composed through `WorkflowsRuntimeDistributedFeature`, placement is a query on cluster membership (ADR 0078,
invariant 3), and membership's host id is the one routing identity: the holder of every placement lease and transport
item lease, and the owner of every execution lease. The `NodeId` setting is no longer a source of identity; a value that
differs from the host id refuses the shell.

- **The claimant checks itself.** Before it claims or renews, a member resolves the execution's pinned executable (from
  durable state, or from a start command for an execution that has none yet) and evaluates its requirements with
  `IRuntimeRequirementChecker` against the shell's registries as they are then: each runtime consumer at its schema
  version, each durable-value storage driver, and each activity type alias. A member that cannot run the execution does
  not claim it; the command goes to the durable transport with no other side effect, and the dispatch result says why
  it did not run here. A requirement that cannot be resolved is never treated as met.
- **Work no active member can run waits and is reported.** It stays in the transport, is never failed or recorded as an
  incident, and is reported in Attention, naming what it needs and never a host, until a capable member claims it. The
  pump rotates through the backlog, so such work does not starve the work behind it.
- **Renewal never grants.** A renewal is a compare-and-set on the lease as held, so a lease released by a reclaim, or
  claimed by another member, is not taken back. A member that no longer satisfies an execution's requirement hands it
  off instead of renewing.
- **Draining.** A member that is draining claims nothing and hands off every execution it holds: it passivates the
  local actor, which waits for an in-flight drain to finish its turn, releases the placement lease, and makes its
  unacknowledged transport items visible. When the pump stops it hands off whatever it still holds.
- **Failover.** The first time a shell's runtime activates in a process, its join sweep reclaims every lease held under
  the host id, which can only be a predecessor's, before it claims anything. An active member that sees a host id
  departed, and confirms it with a fresh read, reclaims that host id's leases within the sweep. Reclaim releases
  placement and transport leases as compare-and-sets and makes the host id's executions recovery candidates at once;
  the recovery sweep re-drives them through `IRuntimeRecoveryCandidateSource`. Reclaim writes no execution lease and no
  fencing token, so a member reclaimed from while alive is refused at its commit.
- **Runnability.** Each shell publishes one runnability entry, derived from exactly the registries its checker reads, so
  other members can tell whether any active member could run waiting work. A host composes the report once with
  `AddWorkflowRuntimeRunnabilityReport()`, beside its membership provider.

## Delivery contract

Cross-node command delivery is **at-least-once**. When a command arrives on a node that does not own an execution's
placement, the forwarding actor sends it to the durable transport inbox and returns a `Deferred` dispatch result: the
command is accepted for routing but not run locally. The owning node's placement pump leases pending inbox items and
dispatches them to its local in-process actor. Dequeue is ack-based (lease/visibility), not destructive-before-dispatch:
if the owning node dies after leasing an item but before acking it, the lease expires and the item becomes visible
again, so the survivor that claims placement re-leases and re-drives it on failover. This mirrors W2's queue semantics
and the ack-based hold-until-commit dequeue recorded in `docs/runtime-durable-resumption.md`. Re-driven commands are
made safe — not merely deduplicated — by the fencing token described above.

## In-memory defaults and durable persistence stores

The placement store and command transport in this unit are in-memory implementations, shared by every node container in
a single process (that is the two-node test harness shape). They are the default when the host does not select a durable
provider. The independent `WorkflowsRuntimeDistributedEntityFrameworkCorePersistence` and
`WorkflowsRuntimeDistributedCommandTransportEntityFrameworkCorePersistence` features replace placement and transport,
respectively, with scoped EF Core stores. Each durable implementation preserves the same frozen wire semantics and
survives process restarts.
