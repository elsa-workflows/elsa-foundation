# Feature Specification: Version-Aware Placement, Draining and Failover

**Feature Branch**: `claude/2093-specs-b7-b9`
**Created**: 2026-09-27
**Status**: Approved
**Input**: Workstream B7, [issue #2103](https://github.com/elsa-workflows/elsa-foundation/issues/2103), of the
cluster-safe schema rollout program [#2093](https://github.com/elsa-workflows/elsa-foundation/issues/2093). Work that
needs a module version is placed only on hosts that can run it, and placement becomes a query on cluster membership.
The same workstream owns draining and failover: a draining member takes no new placements and hands its executions
off at their next checkpoint, and when a member drops out its per-execution leases are reclaimed at once instead of
being left to expire. Commit fencing still decides.

Decisions of record: [ADR 0078](../../docs/adr/0078-workflow-executions-are-virtual-actors-and-cluster-membership-is-a-foundation-contract.md)
("Invariants every membership or actor provider must preserve", invariants 2, 3 and 4; "The first consumers", its
"Version-aware placement" and "Draining and failover" paragraphs with the 2026-09-27 amendment; Consequences, on
`NodeId`) and [ADR 0031](../../docs/adr/0031-runtime-burst-execution-sticky-single-writer-drain-with-in-process-fast-path.md)
(durable state is the truth; in-process memory is only a cache). ADR 0078 is accepted with these decisions through B0
([#2096](https://github.com/elsa-workflows/elsa-foundation/issues/2096), PR #2118). The owner's decisions on #2093
(2026-09-27) bind this spec: that failover and draining belong to B7, and Q20 (membership per host), Q22 (a membership
outage is not a write outage) and Q24 (the host id source).

Companion specs: [spec 183](../183-cluster-membership/spec.md) (B1 to B3) supplies the fleet view, the member query and
its placement purpose, host ids, incarnations, lapse and displacement. This spec is that contract's second consumer
(spec 183, Dependencies) and adds the requirement kinds and the report section spec 183's FR-015 leaves to B7.
[Spec 181](../181-schema-finalization-gate/spec.md) (B5) decides which schema versions a host may write and refuses a
host that cannot read them. [Spec 180](../180-schema-upcaster-chain/spec.md) (B4) defines schema families and readable
sets.

## Terms

Spec 180's, spec 181's and spec 183's Terms apply, notably member, host id, incarnation, status, lapse, displaced
incarnation, fleet view, fresh read, member report, member query, counted member and finalized version. Execution
placement lease, execution command transport, distributed actor provider and workflow drain mean what the
[Elsa glossary](../../docs/glossary/elsa.md) says. In addition:

- **Execution lease**: the single-writer ownership record `IRuntimeExecutionOwnershipService` writes (a lease, a
  heartbeat and a fencing token). **The fence** is the check `RuntimeExecutionFenceValidator` applies to it at
  checkpoint commit.
- **Per-execution leases**: the three leases a member can hold for one execution: its placement lease, its transport
  item leases (the visibility leases `IExecutionCommandTransport.LeaseAsync` grants) and its execution lease. The
  scheduler's work claims and durable-timer claims are not among them (Out of Scope).
- **Routing identity**: the identity the distributed runtime writes as the holder of a placement lease and of a
  transport item lease (`ExecutionPlacementOptions.NodeId` today) and as the owner of an execution lease and its
  heartbeat (`RuntimeExecutionOwnershipOptions.OwnerId` today).
- **Placement requirement**: the list of member-query requirements an execution needs from the member that runs it
  (FR-005).
- **Runnability section**: the member report section this spec adds: what a host's runtime can activate (FR-008). It
  says "runnability" where the code says "capability", because the [root glossary](../../docs/glossary/root.md)
  retires "Capability".
- **Unplaceable work**: pending work for an execution whose placement requirement no active member satisfies.
- **Departed host id**: a host id for which a fresh read of the fleet view shows no live incarnation, and shows its most
  recent incarnation as left or expired. Lapse is a member's own conclusion and others cannot see it; departure is
  what others can see. A restart that displaced an earlier incarnation is not a departure, because the host id has a
  live incarnation again.
- **Reclaim**: making the per-execution leases held under one host id available to other members at once (FR-024).
- **Join sweep**: the reclaim a process performs on its own host id when a shell's distributed runtime first
  activates in it, before that runtime takes any work (FR-022).
- **Hand-off**: a draining member releasing an execution's placement at its next checkpoint commit, so an active
  member can claim it (FR-019).

## Current state

- **Placement is routing, and nothing in it asks whether a node can run the work.** `DistributedWorkflowExecutionActorProvider`
  claims placement on every activation through `IExecutionPlacementService.TryClaimAsync`. An owned execution drains
  on the local in-process actor; any other is handed to `ForwardingWorkflowExecutionActor`, which appends the command
  to the durable transport. `ExecutionPlacementPumpTask` sweeps every 10 seconds: it renews every lease the node
  holds, through the same `TryClaimAsync` that also grants an unplaced or expired lease, and claims executions with
  visible transport backlog. The placement lease lasts 30 seconds.
- **The fence is independent of placement.** `IRuntimeExecutionOwnershipService.AcquireAsync` always issues a strictly
  greater fencing token and takes the execution over, whatever lease is live. `RuntimeExecutionFenceValidator`
  refuses a commit whose lease id, owner or token is not the stored one, or whose lease has expired. The execution
  lease lasts one minute. Recovery treats an execution as interrupted once its lease expires or its heartbeat is five
  minutes old.
- **Two unreconciled per-process identities.** `NodeId` defaults to `node:{machine}:{pid}` and the execution lease
  `OwnerId` to `inproc:{machine}:{pid}`. Nothing sets one from the other, and both change at every restart.
- **The runtime already knows what an executable needs.** An executable declares runtime consumer requirements (a
  consumer key and a schema version), durable-value storage-driver requirements and, per node, CLR activity type
  aliases. `IRuntimeRequirementChecker` evaluates all three against the installed registries. The artifact reconciler
  and Publishing's deployment preflight use it; placement does not.
- **Nothing drains.** A stopping node's leases expire on their own timers.
- **A single host runs this code.** `Elsa.Workbench` composes `WorkflowsRuntimeDistributed` with the durable placement
  and transport features, so a cluster of one exercises every path below.

The full inventory, with paths, timings and the gaps the plan has to fill, is in [research.md](./research.md).

## The invariants and how they hold

**Invariant 1 (placement).** No member claims or renews placement of an execution that its own runtime cannot run at
that moment, and no member claims while it is joining, draining or left, or before its join sweep has completed.

**Invariant 2 (fencing).** Reclaim never decides whether a commit persists. Every path this spec adds either releases a
routing lease, or makes an execution eligible for a re-drive that must acquire a strictly greater fencing token before
it can commit. A member that is reclaimed from while it is still alive reaches its commit with a stale token and is
refused (ADR 0078, invariant 2).

**Invariant 3 (nothing owed is dropped).** Work that no member can run stays in the durable queue, is reported, and is
claimed by the first capable member to become active (ADR 0078, invariant 4).

They hold through five mechanisms, each tested separately (Success Criteria):

1. **The claimant checks itself** (FR-011). At claim and at renewal a member evaluates the placement requirement
   against its own registries, as they are at that moment, with the evaluation the reconciler already uses. It never
   consults its own published report, so a report that lags can mislead a diagnostic but can never place work.
2. **Reclaim needs positive evidence from a fresh read** (FR-023). A host id that is absent from the view, one with a
   live incarnation, or one seen only in a cached read is never reclaimed from. A host outside the view — another fleet,
   a cluster of one under the in-process provider, a process on a release before this one — keeps its leases until
   they expire, as today.
3. **Every release is a compare-and-set against the lease as observed, and renewal never re-grants** (FR-014, FR-024).
   A reclaim and a renewal that race cannot both win, and a lease that was released is never taken back by a renewal.
4. **The join sweep runs only when a shell's runtime first activates in a process** (FR-022), before it takes any
   work, when no lease held under the host id can be the process's own.
5. **The fence is unchanged** (FR-025). Reclaim writes no execution lease and no fencing token.

Placement is held to the routing standard spec 183 sets ("The invariant and how it holds"): a wrong routing decision
costs work, not correctness, because the fence refuses the stale writer. That is why mechanism 2 prefers leaving a
doubtful lease to expire over reclaiming it: a false reclaim is safe, but it makes a live member lose the work it is
doing.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - During a rolling upgrade, work that needs the new module runs only on upgraded hosts (Priority: P1)

Three hosts share a database with the durable membership provider. A package adds the activity type `Acme.Approve`.
Host A is upgraded first, and a workflow using `Acme.Approve` is published through it. A stimulus for an execution of
that workflow arrives at host B, which has not been upgraded. B does not claim the execution; it forwards the command
to the durable transport, and A's pump claims and runs it. B and C never claim it, however many sweeps pass.

**Why this priority**: This is #2103's acceptance: during a rolling upgrade, an execution needing the new module is
never placed on a host without it.

**Independent Test**: Two hosts on one database, one with an extra activity registered. Drive executions of a workflow
that uses it, and of one that does not, through both hosts, and assert where each was claimed.

**Acceptance Scenarios**:

1. **Given** host B lacks an activity type an execution's pinned executable declares, **When** a command for it is
   dispatched on B, **Then** B writes no placement lease, forwards the command to the durable transport, and the
   dispatch result says the command was accepted for routing and why it did not run on B.
2. **Given** host A satisfies the requirement, **When** A's pump next sweeps, **Then** A claims the execution, drains it
   and acknowledges the command.
3. **Given** an execution whose executable needs nothing B lacks, **When** it is dispatched on B, **Then** B claims it
   and drains it locally, exactly as today.
4. **Given** A holds the placement and then loses the requirement, for instance because the package was removed on the
   running host, **When** A's pump next renews, **Then** A does not renew, hands the execution off at its next
   checkpoint, and no incapable member claims it.

---

### User Story 2 - Work no member can run waits and says why (Priority: P1)

The only host that had the module is stopped. A command arrives for an execution that needs it. The command is
accepted into the durable transport and no remaining host claims it. Attention shows a warning naming the unmet
requirement and the number of executions waiting. When a capable host becomes active, it claims the work and runs it,
and the warning clears.

**Why this priority**: This is the case that looks like success. The sender was told its command was accepted for
routing. Without a report, the work would wait forever with nothing saying so. Failing it instead would drop work the
durable queue owes (ADR 0078, invariant 4).

**Independent Test**: Queue work for an executable that needs a requirement no running host satisfies. Read Attention
and the transport. Start a capable host and assert the work runs and the warning clears.

**Acceptance Scenarios**:

1. **Given** no active member satisfies an execution's placement requirement, **When** work for it is pending,
   **Then** no member claims it, the transport still holds it, and Attention reports one warning naming the unmet
   requirement and the number of executions waiting, without naming any host.
2. **Given** the same, **When** sweeps continue, **Then** no member records an activation failure or an incident for
   that execution: refusing it is placement's decision, not a runtime fault.
3. **Given** a capable member becomes active, **When** its pump next sweeps, **Then** it claims and runs the work, and
   the warning is gone on the next Attention read.

---

### User Story 3 - A crashed host that restarts reclaims its own leases at once (Priority: P1)

Host A, configured with host id `a` and the durable provider, crashes mid-drain and restarts under the same host id.
Because `a`'s earlier incarnation was still live when it crashed, the new incarnation's join is refused and retried
until that entry is no longer live — at most the expiry period plus the skew allowance (spec 183, FR-004b) — so the
restart gains nothing over a survivor's own expiry-triggered reclaim (User Story 4). Once the join succeeds, before
its runtime takes any work, the new process releases every placement lease and transport item lease still held under
`a`, and makes every execution whose execution lease is still held under `a` eligible for recovery, covering whatever
a survivor has not already reclaimed.

**Why this priority**: ADR 0078 requires that a host rejoining as a new incarnation has its leases reclaimed by the
time it takes any work, whether that reclaim runs through its own join sweep or a survivor's departure-triggered
reclaim got there first. On a host whose id survives restarts (a VM, a `StatefulSet` pod) this is the common failover,
delayed for a live-collision restart by up to spec 183's expiry period plus the skew allowance (FR-004b).

**Independent Test**: With a controllable clock, kill host A mid-drain and restart it under the same host id. Advance
the clock to the earlier incarnation's expiry so the new incarnation's join succeeds, and assert its join sweep
reclaims any leases a survivor has not already reclaimed, and that exactly one commit lands per checkpoint.

**Acceptance Scenarios**:

1. **Given** leases held under `a` by the earlier process, **When** the new process's join succeeds, after the earlier
   incarnation's entry is no longer live, **Then** its join sweep releases any of those leases a survivor has not
   already reclaimed, before its runtime claims anything.
2. **Given** the interrupted execution, **When** the next recovery sweep runs on any member, **Then** the execution is
   a recovery candidate although its execution lease has not expired, whether because the join sweep or a survivor's
   departure reclaim reached it first, and the recovery says it was reclaimed.
3. **Given** the re-drive, **When** it commits, **Then** it holds a strictly greater fencing token than the crashed
   drain did.

---

### User Story 4 - A host that crashes and stays down is reclaimed from once it expires (Priority: P1)

Host B crashes and does not come back. Once B's membership entry expires, a surviving active member's fresh read shows
`b` departed, and within one sweep that member releases `b`'s placement leases, makes the transport items leased to
`b` visible and makes `b`'s executions recovery candidates.

**Why this priority**: Membership expiry, not each lease's own timeout, decides failover. That also lets a deployment
lengthen its leases without slowing failover down.

**Independent Test**: With a controllable clock, kill host B without a graceful stop. Read from a survivor just before
and just after B's expiry, and inspect B's leases after each.

**Acceptance Scenarios**:

1. **Given** `b` is still live, **When** survivors sweep, **Then** nothing held under `b` is touched.
2. **Given** `b`'s most recent incarnation has expired and no incarnation of `b` is live, **When** a survivor next
   sweeps, **Then** every lease held under `b` is reclaimed within that sweep.
3. **Given** two survivors reclaim concurrently, **When** both finish, **Then** each lease was released once, and
   nothing held under any other host id changed.

---

### User Story 5 - Reclaim never decides a commit (Priority: P1)

Host B loses the membership store but still reaches the runtime database, mid-drain. The survivors judge `b`
departed, reclaim, and re-drive the execution on host A. B's drain then reaches its commit. B's commit is refused by
the fence, A's lands, and exactly one commit exists.

**Why this priority**: This is the other case that looks like success. A reclaim that let both writers commit would
read as faster failover.

**Independent Test**: Drive the interleaving (B mid-drain, reclaim, re-drive on A, B commits, A commits) with fakes for
the membership and runtime stores, and assert the commits.

**Acceptance Scenarios**:

1. **Given** that interleaving, **When** both drains reach their commits, **Then** exactly one succeeds and the other
   raises `RuntimeStaleFencingTokenException`.
2. **Given** B's placement lease was released by the reclaim, **When** B next renews it, **Then** the renewal fails and
   the lease is not granted to B again by that renewal.
3. **Given** B is behind in a survivor's cached view only, and a fresh read shows it live, **When** the survivor
   sweeps, **Then** nothing held under `b` is reclaimed.

---

### User Story 6 - A stopping host drains (Priority: P2)

Host A begins to stop, and its membership status becomes draining. It takes no new placements. Each execution it holds
is handed off: at once if no drain is in progress, otherwise at the drain's next checkpoint commit. Commands that
arrive for those executions go to the durable transport and are claimed by active members. When A stops it writes
left, and whatever it could not hand off in time is reclaimed by the survivors at once.

**Why this priority**: ADR 0078 names graceful draining as something a host cannot do today.

**Independent Test**: Stop a host gracefully while it holds idle and mid-drain executions, and assert where each is
next claimed and that nothing is lost.

**Acceptance Scenarios**:

1. **Given** A is draining, **When** a command arrives on A for an unplaced execution, **Then** A does not claim it and
   forwards the command.
2. **Given** A is mid-drain on an execution, **When** the drain's next checkpoint commits, **Then** A passivates the
   execution and releases its placement, and an active member claims it at its next sweep.
3. **Given** A has written left while still holding leases, **When** a survivor's fresh read shows it, **Then** those
   leases are reclaimed within one sweep.

---

### User Story 7 - A single host pays nothing and restarts faster (Priority: P2)

`Elsa.Workbench` runs alone, with the in-process membership provider and durable placement. Placement behaves as today,
with two differences. Work its runtime cannot run waits and is reported (User Story 2) instead of failing when it
activates. After a restart, its own earlier leases are released by the join sweep instead of waiting to expire. It
writes no membership table.

**Why this priority**: Non-clustered hosting must pay nothing for this program (#2093, Acceptance), and a single host
runs this code path today.

**Independent Test**: Start a single host with the in-process provider, dispatch work, restart it, and inspect the
fleet view, the leases and the database.

**Acceptance Scenarios**:

1. **Given** the in-process provider, **When** the host sweeps, **Then** no host id other than its own is ever
   reclaimed from.
2. **Given** a restart, **When** the process starts, **Then** its join sweep releases the leases its previous process
   held.
3. **Given** the in-process provider, **When** the host has run for several sweeps, **Then** no membership table
   exists (spec 183, FR-018).

---

### Edge Cases

- **A host with several shells.** Each shell with the distributed runtime publishes its own runnability entry and
  checks its own registries when it claims, because registries are per shell while membership is per host (FR-008).
- **The first release that carries this spec.** Leases written by the previous release carry `node:{machine}:{pid}` and
  `inproc:{machine}:{pid}`, which name no member. No host id is departed for them, so they are never reclaimed; they
  expire on their own timers once, and the fence decides any commit in that window.
- **A lapsed member.** It keeps placing work as the runtime did before membership, subject to its own requirement
  check, but judges no departure and reclaims nothing (FR-026). Active members that see its host id departed may
  reclaim from it; its renewals then fail (FR-014).
- **A membership outage for everyone.** No fresh read succeeds, so no host id is ever judged departed and nothing is
  reclaimed. Placement falls back to lease expiry, as today, and execution does not stop (Decisions).
- **A host id that restarted after a survivor's cached read.** The survivor confirms departure with a fresh read, which
  shows the new live incarnation, so it reclaims nothing (FR-023).
- **A requirement that cannot be resolved.** The pinned executable cannot be loaded or read. The member treats the
  execution as not runnable here, does not claim it, and reports it with that reason (FR-016). It never treats an
  unresolved requirement as satisfied.
- **A command that arrives while the member is joining.** It is forwarded to the durable transport and claimed once a
  capable member is active: on a single host, by its own pump within one sweep after it becomes active.
- **Several hosts share a runtime database, each with the in-process provider.** Each declares itself alone (spec 183,
  FR-018a). Processes on different machines have different host ids and route correctly by lease expiry. Processes on
  one machine share the machine-name host id and therefore one routing identity, so each treats the other's leases as
  its own. The fence refuses the stale writer, so no data is lost, but work is repeated and refused commits are
  logged. Nothing detects this (Decisions, Q8, Q18 and Q25).
- **A drain that outlasts the host's shutdown timeout.** It is interrupted as a crash would be. If the host writes left,
  survivors reclaim at once; if it does not get that far, its entry expires and survivors reclaim then.
- **A host with the distributed runtime but without the resumption feature.** Recovery candidates are re-driven by the
  resumption pump. Without it, a reclaimed execution lease makes the execution a candidate that nothing re-drives,
  exactly as an expired lease is today. Placement and transport reclaim still apply.
- **The same host id in two databases.** Reclaim acts on the stores of each shell separately. A departed host id is
  departed in every database its shells use.

## Requirements *(mandatory)*

### Functional Requirements

**Routing identity**

- **FR-001**: When the distributed runtime is composed, its routing identity MUST be the local member's host id as the
  membership contract exposes it (spec 183, FR-003): the holder of every placement lease and transport item lease the
  runtime writes, and the owner of every execution lease and heartbeat. `NodeId` and the execution lease `OwnerId`
  stop being two per-process values and become one value, which survives a restart wherever the host id does. That is
  what lets one departure verdict reclaim all three kinds of per-execution lease.
- **FR-002**: The routing identity MUST satisfy `DistributedRuntimeIdentityConstraints`. Spec 183's FR-003 already
  holds the host id to the same limits, so no placement, transport or runtime column changes.
- **FR-003**: `WorkflowsRuntimeDistributedFeature.NodeId` stops being a source of identity. A bound, non-blank value that
  differs from the host id MUST refuse the shell's configuration with a diagnostic that names the membership host-id
  setting. A value equal to the host id is accepted. The setting MUST NOT be deleted, and MUST NOT be made to throw in
  its setter: CShells ignores a key with no matching property and swallows a setter's exception, so either would
  silently accept a configuration that still names a different id ([research.md](./research.md), "Identity").
- **FR-004**: This spec adds no identity source of its own. Under the in-process provider the host id is the machine
  name unless configured; under a durable provider an explicit host id is required (spec 183, FR-003 and FR-003a).

**The placement requirement and the runnability section**

- **FR-005**: An execution's placement requirement MUST be derived from its pinned executable
  (`WorkflowExecutableIdentity`), as the requirements `IRuntimeRequirementChecker` evaluates for it: each runtime
  consumer key with its schema version, each durable-value storage-driver key, and each CLR activity type alias its
  nodes declare. An executable is immutable and content-addressed
  ([ADR 0038](../../docs/adr/0038-artifact-hash-is-purely-behavioral-and-executables-are-content-addressed.md)), so its
  requirement MAY be cached per artifact id for the life of the process.
- **FR-006**: Membership's closed requirement vocabulary (spec 183, FR-015) MUST gain three kinds, as a contract
  change: "activates runtime consumer C at schema version S", "has durable-value storage driver D" and "resolves
  activity type alias A". Each is data only, so every provider can translate it into its own terms (ADR 0078,
  invariant 3).
- **FR-007**: A placement requirement MUST NOT include spec 183's "reads schema family F at version V". A member whose
  runtime EF module is active can read that module's finalized versions, or spec 181's FR-015 would have refused the
  module; and a member whose writes to a runtime family are refused under spec 181's FR-012 is excluded by FR-012 of
  this spec instead.
- **FR-008**: The member report MUST gain a runnability section (spec 183, FR-014), produced by one source. It holds one
  entry per shell in which the distributed runtime is active: the per-database identity of the finalization record
  the shell's Runtime EF module last read (spec 181, FR-001), or none yet; the runtime consumer keys with their
  supported schema versions; the storage-driver keys; and the activity type aliases the shell's type registry
  resolves. It MUST be derived from exactly the registries `IRuntimeRequirementChecker` reads, so the entry and the
  checker agree on every requirement. Like every section, it carries no secret and no configuration text. It is
  recomputed when a shell's registries change, for instance after a Nuplane reload, and published on demand as well
  as on each heartbeat (spec 183, FR-011).
- **FR-009**: A placement-purpose member query with runnability requirements MUST match a member when one of its
  runnability entries that names the queried database identity, or names none, satisfies every requirement. Spec 183's
  placement-purpose rules apply unchanged: only the current incarnation of each host id is considered, only with
  status active, and never with an unknown report (spec 183, FR-016).
- **FR-010**: The conformance suite's invariant tier (spec 183, FR-058) MUST cover the three new kinds on every
  provider: a placement query with runnability requirements returns only members whose section satisfies them, and
  handles missing, unknown and database-scoped entries as FR-009 says.

**Claiming and renewing**

- **FR-011**: A member MUST NOT claim or renew placement of an execution unless the runtime of the shell making the
  claim satisfies the execution's placement requirement at that moment, as `IRuntimeRequirementChecker` evaluates it
  against that shell's current registries. This is the placement query of FR-009, evaluated for the claiming member
  alone, on the facts its runnability entry is derived from. The member never consults its own published entry for
  its own claim.
- **FR-012**: A member MUST NOT claim while its status is joining, draining or left (spec 183, FR-005), before its
  shell's join sweep has completed (FR-022), or while spec 181 refuses its writes to any family of the Runtime EF module
  (spec 181, FR-012). It MUST NOT renew while spec 181 refuses those writes, and hands the execution off instead
  (FR-019). A lapsed member is governed by FR-026.
- **FR-013**: A claim that FR-011 or FR-012 refuses MUST have no side effect beyond diagnostics: no placement lease is
  written, no transport item is leased, no execution state changes, and no activation failure or incident is recorded.
  The command is forwarded to the durable transport exactly as a command for an execution owned elsewhere is, and the
  dispatch result says it was accepted for routing and why it was not run on this member.
- **FR-014**: Renewal MUST extend only a lease the member still holds under the placement token it holds, as a
  compare-and-set. A lease that has been released, or claimed by another member, since the member last held it MUST
  NOT be granted to it again by a renewal; the member passivates that execution locally instead. Today's pump renews
  through `TryClaimAsync`, which also grants a released lease, so that path changes ([research.md](./research.md),
  "Placement and routing today").
- **FR-015**: A renewal that finds the member no longer satisfies the placement requirement (FR-011) MUST NOT renew. The
  member hands the execution off at its next checkpoint, as FR-019 describes.
- **FR-016**: The placement requirement MUST be resolved before a claim, on both claim paths: the actor provider's
  activation, and the pump's sweep of transport backlog. If it cannot be resolved, for instance because the pinned
  executable cannot be loaded, the member MUST treat the execution as not runnable here, not claim it, and report it
  with that reason (FR-017). An unresolved requirement is never treated as satisfied.

**Unplaceable work**

- **FR-017**: When work is pending for an execution whose placement requirement no active member satisfies, as a
  placement-purpose query shows (a cached read is enough), the runtime MUST report it as a warning through the
  runtime's Attention contributor (`WorkflowRuntimeAttentionContributor`), naming the unmet requirements and the
  number of executions waiting, and MUST log it. An unresolved requirement (FR-016) is reported the same way, with its
  reason. No domain API's answer names hosts or otherwise reveals the fleet's topology, as spec 182's FR-011 requires
  of dormancy refusals.
- **FR-018**: Unplaceable work MUST stay in the durable transport. No member fails, drops or dead-letters it for being
  unplaceable, and the first capable member to become active claims it at its next sweep.

**Draining**

- **FR-019**: A member whose status is draining (spec 183, FR-005: from when the host begins to stop) MUST hand off
  every execution it holds. An execution with no drain in progress is handed off at once; one mid-drain is handed off
  after the drain's next checkpoint commit. Hand-off passivates the local actor at that boundary
  (`WorkflowExecutionActorPassivationBoundary.HostDrain`), releases the placement lease, matched on host id and
  placement token, and then makes visible every transport item the member leased for that execution and has not
  acknowledged, so an active member can claim and lease them at its next sweep.
- **FR-020**: A command that reaches a draining member for an execution it has handed off, or does not hold, MUST be
  forwarded to the durable transport.
- **FR-021**: Draining is bounded by the host's shutdown timeout. Before its status becomes left (spec 183, FR-030),
  the member MUST release every placement lease and make visible every transport item lease it still holds. A drain
  that has not reached a checkpoint by then is interrupted as a crash would be, and its execution lease is reclaimed
  once the host id is departed (FR-023).

**Failover**

- **FR-022**: The first time a shell's distributed runtime activates in a process, it MUST, before it claims anything,
  reclaim every per-execution lease held under the process's host id in that shell's stores (FR-024). None of them can
  be the process's own. Under a durable provider, spec 183's FR-004b guarantees no other live process holds the host
  id; under the in-process provider, composing it is the declaration that the host is alone (spec 183, FR-018a). A
  shell reload within the process and a rejoin after a lapse are not a first activation, and MUST NOT sweep: the
  leases under the host id are that process's own.
- **FR-023**: An active member that observes a departed host id, through membership's change signal or by comparing
  successive reads (spec 183, FR-013), MUST confirm the departure with a fresh read, and then reclaim that host id's
  per-execution leases in each of its shells' stores within one sweep. Only positive evidence counts. A host id absent from the view,
  a host id with a live incarnation, and a host id seen departed only in a cached read are never reclaimed from.
- **FR-024**: Reclaim MUST release each live placement lease held under the host id, matched on host id and placement
  token as observed; MUST make each transport item leased to the host id visible, matched on holder and lease token as
  observed; and MUST make each execution whose execution lease or heartbeat is held under the host id a recovery
  candidate at once, with a recovery source that names the reclaim rather than a lease timeout. Reclaim runs in bounded
  batches, is idempotent, and is safe when several members run it concurrently.
- **FR-025**: Reclaim MUST NOT write an execution lease or a fencing token. A re-drive acquires its own lease through
  `IRuntimeExecutionOwnershipService.AcquireAsync`, and `RuntimeExecutionFenceValidator` and the checkpoint stores'
  fence decision are unchanged. No membership provider is consulted at commit (spec 183, FR-035).
- **FR-026**: A member that has lapsed (spec 183, FR-007) MUST NOT judge any host id departed and MUST NOT reclaim,
  because its view of the fleet is in doubt. It MAY keep claiming, renewing under FR-014 and draining, subject to
  FR-011, as the runtime did before membership existed. Its leases stay reclaimable by active members that see its
  host id departed. This keeps a membership outage from stopping execution, as spec 181's narrowed FR-018 keeps it
  from stopping writes (spec 183, Decisions, Q22).
- **FR-027**: Where the resumption feature is composed, its recovery sweep MUST honour FR-024's recovery candidates. The
  contract that carries them lives in the runtime's core and is implemented by the distributed leaf, so the core gains
  no reference to the leaf: the provider isolation the leaf's README states, under framework constitution §2.7.

**Diagnostics**

- **FR-028**: Every reclaim MUST be logged with the host id, whether it was a join sweep or a departure, and the number
  of leases released of each kind.
- **FR-029**: A claim refused under FR-011 or FR-016 MUST be logged with the execution id and the unmet or unresolved
  requirement, at a level that does not repeat on every sweep for the same execution.

**Performance**

- **FR-030**: Performance is stated qualitatively, because performance measurement is retired (#1668,
  [ADR 0073](../../docs/adr/0073-ef-core-is-the-only-first-party-persistence-family.md)). A claim adds one requirement
  check, served from a per-artifact cache after the first. A sweep adds one cached read of the fleet view. Reclaim
  runs only when a host id departs or a process starts. The runnability section grows with the number of activity
  types a host resolves, and is republished only when it changes.

**Tests**

- **FR-031**: Each mechanism MUST be tested in both directions. A member without a requirement never claims, and one
  with it claims. A departed host id is reclaimed from within one sweep, and a live, absent, restarted or
  cache-only-departed host id is never reclaimed from. A released lease is not re-granted by a renewal, and a held one
  is renewed. The in-process provider never reclaims another host id.
- **FR-032**: The conformance suite's invariant-2 test (spec 183, FR-057) MUST include a reclaim interleaving: a member
  reclaimed from while alive reaches its commit after the re-drive, and exactly one commit succeeds.

### Requirements on membership (B1 to B3)

These are what this spec needs from spec 183. The first is a contract change spec 183's FR-015 anticipates; the rest
are already met.

| Requirement | Met by | Change |
|---|---|---|
| The three runnability requirement kinds and the runnability section (FR-006, FR-008) | spec 183, FR-014 and FR-015 | New kinds and a new section. |
| The local member's host id, incarnation, status and lapse | spec 183, FR-003 to FR-007 | None. |
| Fresh reads, and each member's status, liveness and displacement per host id | spec 183, FR-009 and FR-010 | None. Departure (Terms) is derived from these. |
| Change notifications | spec 183, FR-013 | None. The change signal accelerates FR-023; reads are the backstop. |

### Key Entities

- **Placement requirement**: the runnability requirements of an execution's pinned executable.
- **Runnability entry**: per shell with the distributed runtime: a database identity or none, consumer keys with
  schema versions, storage-driver keys and activity type aliases.
- **Routing identity**: the member's host id, written on placement leases, transport item leases and execution leases.
- **Departed host id**: no live incarnation, the most recent one left or expired, confirmed by a fresh read.
- **Reclaim**: releasing a host id's placement and transport item leases, and making its executions recovery
  candidates.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In a rolling upgrade of two or more hosts, zero executions needing the new module are claimed by a host
  without it, and every such execution is claimed by a host with it.
- **SC-002**: Work that no active member can run is never failed, dropped or recorded as an incident, and is reported
  in Attention for as long as it waits.
- **SC-003**: After a crash and a restart under the same host id, every interrupted execution is re-driven within one
  sweep of whichever runs first: the new process's own join sweep, once its join succeeds, or a survivor's
  departure-triggered reclaim.
- **SC-004**: After a crash without restart, every lease held under the host id is reclaimed within one sweep of its
  membership entry expiring.
- **SC-005**: Across every interleaving of reclaim, re-drive and a late commit by the reclaimed member, exactly one
  commit per checkpoint succeeds.
- **SC-006**: A live host id, an absent one, and one departed only in a cached read are reclaimed from zero times.
- **SC-007**: A draining host claims zero new placements, and every execution it held is claimed by an active member
  after its next checkpoint.
- **SC-008**: A single host with the in-process provider creates no membership table, reclaims no other host id, and
  releases its previous process's leases at start.

## Assumptions

- Spec 183 is built first, with at least the in-process provider, and the durable provider for any clustered test
  (spec 183, Dependencies).
- Every command that activates an execution carries, or can resolve, the execution's pinned executable. The command
  payloads carry `PinnedExecutable` today ([research.md](./research.md), "Requirements").
- `IRuntimeRequirementChecker` resolves activity type aliases through the registry alone, with no fallback to a CLR
  type name, so the runnability section can list exactly what the checker accepts ([research.md](./research.md),
  "Requirements").
- Hosts' clocks agree within spec 183's skew allowance, as every lease already assumes.

## Dependencies

- **Spec 183** (B1 [#2097](https://github.com/elsa-workflows/elsa-foundation/issues/2097), B2
  [#2098](https://github.com/elsa-workflows/elsa-foundation/issues/2098), B3
  [#2099](https://github.com/elsa-workflows/elsa-foundation/issues/2099)), and the contract change above.
- **Spec 181** (B5, [#2101](https://github.com/elsa-workflows/elsa-foundation/issues/2101)), for the per-database
  identity in the runnability entry and for FR-012's exclusion of a member whose runtime writes are refused. Placement
  without spec 181 still works; that exclusion applies once spec 181 is built.
- The runtime's Attention contributor (`WorkflowsRuntimeAttention`), for FR-017.

## Out of Scope

- Membership itself, its providers and its conformance suite (spec 183).
- Finalization, refusal of hosts that cannot read, and dormancy (specs 181 and 182).
- The scheduler's work claims and durable-timer claims. They expire on their own visibility timeouts, which membership
  does not replace (spec 183, FR-035).
- Draining a host without stopping it, for maintenance. Spec 183 ties draining to stopping.
- Placement by package version. Executables do not record package versions (ADR 0038), so there is nothing to derive
  such a requirement from (Decisions).
- An actor-framework provider. FR-006's kinds are what such a provider would translate (spec 183, FR-061).

## Decisions

Recorded 2026-09-27, when this spec was drafted. The first is the owner's, on #2093; the rest are this draft's own,
and merging the spec approves them.

- **Q25 — The routing identity under the in-process provider.** Decided by the owner on #2093 (2026-09-28), consistent
  with Q8: accepted. FR-001 makes the routing identity the host id, which under the in-process provider is the machine
  name. Two processes on one machine that share a runtime database while each composes the in-process provider — two
  Workbench processes against one PostgreSQL, say — then share one routing identity, where today their process ids
  keep them apart. Fencing keeps that safe; the known limit is its cost: work is repeated and refused commits are
  logged (Edge Cases). It is the misconfigured cluster Q8 and Q18 decided not to detect, a durable provider fixes it,
  and keeping a process-derived identity only under the in-process provider would give single hosts no restart benefit
  (User Story 7) and split the rule in two.
- **Failover and draining belong to placement.** Decided by the owner on #2093, and recorded in ADR 0078's
  2026-09-27 amendment: membership publishes status and incarnation, and placement, which already consumes the member
  query, acts on them.
- **One routing identity: the host id, for all three leases.** ADR 0078 says `NodeId` becomes the host id. The
  execution lease owner is a second per-process identity ADR 0078 does not mention, and reclaiming execution leases
  needs it to name the same host. Recording an incarnation beside each lease was considered and not chosen: it would
  change three persisted shapes, two of them with frozen golden fixtures, and FR-022's join sweep gets the same result
  without it, because at process start every lease under the host id is a predecessor's.
- **Placement matches what an execution declares, not package versions.** ADR 0078 describes a member's report as
  "which modules are activated at which versions". An executable records its runtime consumer and schema version, its
  storage drivers and its activity types, and ADR 0038 keeps package versions out of its identity, so those are the
  facts a requirement can be derived from. They are also version-aware where it matters: a new executable format is a
  new consumer schema version (spec 180, FR-028).
- **The claimant checks itself, and the membership answer is for everyone else.** A member's own claim uses its
  current registries (FR-011); the published section serves queries about other members (FR-017) and providers that
  route for the fleet. A report that lags therefore cannot place work, and a claim never waits for a publish.
- **Departure, not lapse, triggers reclaim, and only on a fresh read.** Other members cannot see a lapse. Absence from
  the view is not evidence, which keeps the in-process provider, split fleets and processes on older releases from
  having their leases taken. A false reclaim is safe but makes a live member lose its work, so the fresh read is worth
  its cost on an event that happens only when a host departs.
- **A lapsed member keeps placing work and stops reclaiming.** Stopping a lapsed member from claiming would turn a
  membership-store outage into a fleet-wide execution outage, the trap spec 183's Q22 avoided for writes. Placement
  then degrades to today's lease expiry, and the fence decides.
- **Renewal never re-grants.** Without this, a reclaimed member's next renewal would take back a released lease,
  and reclaim would flap for as long as a partition lasts.
- **Unplaceable work waits, and says so.** Failing it would drop work the durable queue owes, and leaving it silent
  would look like success. Refusing to claim it is not a runtime fault, so it records no incident.
- **Schema readability is not a placement requirement.** Spec 181 already refuses a host that cannot read a family's
  finalized version before its runtime can activate. Adding the kind to every placement query would only repeat that
  check.

Recorded 2026-09-28, when the owner tightened spec 183's rejoin rule on #2093.

- **User Story 3 no longer claims a same-host-id restart reclaims sooner than a survivor's own departure reclaim.**
  Spec 183's FR-004b now refuses and retries a join under a still-live host id until that incarnation expires, so a
  crash-restart under a stable host id never rejoins, and never runs its own join sweep, before its earlier
  incarnation would already read as departed to a survivor. FR-022's join sweep still matters as the reclaim path for
  a single host with no survivor (User Story 7), and as a backstop wherever a survivor has not already reclaimed by
  the time the join succeeds.
