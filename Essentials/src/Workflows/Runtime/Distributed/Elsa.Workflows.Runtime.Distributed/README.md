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
  local actor, releases the placement lease, and makes its unacknowledged transport items visible. Passivation makes
  an in-flight drain yield at its next step, as a waiting Cancel does, so even a drain that keeps making progress ends
  its turn and the member releases the placement while it still holds the lease (#3678). The yielded segment's next
  steps are durable, and the next owner continues from that checkpoint once the recovery sweep re-drives the
  execution. When the pump stops because the host stops or its shell is removed, it hands off whatever it still
  holds. A shell reload is not such a stop: CShells activates the new generation before it drains
  the old one, the new generation's pump renews the leases under the host id, and the old one hands off nothing when it
  stops.
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

**A dispatch never holds the sweep** (#2831). A command can start a drain that runs as long as its workflow keeps making
progress. The pump waits for each dispatch only up to `ExecutionPlacementPumpOptions.InlineDispatchWait` (default
1 second, measured on the injected clock). A command still running after that finishes on its own and is acknowledged
when it does, under the item's latest lease.

All of one sweep's waits for local work, across every persistence scope, share a budget of a third of the placement
lease. That includes passivating or handing off an execution whose drain is still running. Such a hand-off or
passivation finishes on its own, and until it does, later sweeps neither hand the execution off again nor renew, claim
or lease it. However many executions are busy, the sweep keeps renewing every placement the node holds, except one it
is handing off or passivating. Without that, another node would claim a busy execution once its placement lease
lapsed, take over its runtime ownership and run the same workflow. That takeover is deliberately immediate: it does not
wait for the old owner's lease to expire, which keeps recovery fast, so keeping placement renewed is what stops two
nodes from running one execution (#3667).

A hand-off or passivation makes the drain yield at its next step (#3678), so it waits only for the step that is running.
A drain that is waiting for a continuation another deliverer holds yields at its next poll, within half a second, and
leaves that continuation with its holder (see [durable resumption](../../../../../../docs/runtime-durable-resumption.md#a-live-drains-continuation-in-the-sweeps-hands-2225)).
Only a step that outlasts the lease, such as an activity blocked on I/O, lets the placement lapse first; another member
then claims the execution, and fencing refuses the old drain's next commit. A Cancel sent meanwhile waits in the
transport, because a sweep leases nothing for an execution it is handing off, and takes effect on the next owner. A
sweep that overlaps a hand-off the stop path started can still lease the Cancel and dispatch it. Resolving the mailbox
then waits for the passivation, so the Cancel runs on a fresh mailbox on this member once the hand-off has passivated
the old one. Only a caller that resolved the mailbox before the passivation began reaches the passivating mailbox. Its
Cancel asks the drain to yield too. Once the hand-off has passivated the mailbox, the Cancel is answered `Deferred` and
runs nothing there. If the stop path's one-lease bound cancels the hand-off first, the drain still yields to the
Cancel, which then runs on that mailbox.

An execution with a drain this pump left running gets no inline wait:
- A Cancel for it is dispatched without waiting, reaches the drain's mailbox, and the drain yields to it. A Cancel
  forwarded from another node waits in the transport until this node's next sweep that lists the execution delivers
  it, usually within `SweepInterval` (default 10 seconds). A sweep lists a scope's pending work once, after renewing and
  before dispatching there; a Cancel sent while that scope's dispatches are waited for (at most a third of the lease in
  all) goes to the sweep after. Failure backoff (up to `MaxBackoffInterval`) can delay it further. One sent while
  this node hands the execution off takes effect on the next owner, or here as described above.
- Its other commands stay leased while the drain runs, so the mailbox does not fill with waiters. When the drain ends,
  the pump releases the leases of exactly those commands and the next sweep dispatches them. A command dispatched
  earlier that came back deferred or rejected keeps its lease until it lapses.

A drain the pump did not leave running, such as one for a command routed straight to the local mailbox, is not
tracked. The pump's next dispatch for that execution is waited for like any other, then left running.

An item whose transport lease lapses while its dispatch still runs is leased again but not dispatched twice.

## In-memory defaults and durable persistence stores

The placement store and command transport in this unit are in-memory implementations, shared by every node container in
a single process (that is the two-node test harness shape). They are the default when the host does not select a durable
provider. The independent `WorkflowsRuntimeDistributedEntityFrameworkCorePersistence` and
`WorkflowsRuntimeDistributedCommandTransportEntityFrameworkCorePersistence` features replace placement and transport,
respectively, with scoped EF Core stores. Each durable implementation preserves the same frozen wire semantics and
survives process restarts.
