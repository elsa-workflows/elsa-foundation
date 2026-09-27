# Feature Specification: Cluster Membership

**Feature Branch**: `claude/2093-membership-spec`
**Created**: 2026-09-27
**Status**: Draft
**Input**: Workstreams B1 ([#2097](https://github.com/elsa-workflows/elsa-foundation/issues/2097)), B2
([#2098](https://github.com/elsa-workflows/elsa-foundation/issues/2098)) and B3
([#2099](https://github.com/elsa-workflows/elsa-foundation/issues/2099)) of the cluster-safe schema rollout program
[#2093](https://github.com/elsa-workflows/elsa-foundation/issues/2093). Cluster membership is a foundation contract: a
record of which hosts are alive and what each of them can read. It ships with an in-process default, a cluster of one
that costs nothing, and an opt-in EF Core provider for clusters. Each host reports, through membership, the
persisted-schema versions it can read.

Decision of record: [ADR 0078](../../docs/adr/0078-workflow-executions-are-virtual-actors-and-cluster-membership-is-a-foundation-contract.md)
(Decision; "Invariants every membership or actor provider must preserve"; "Actor frameworks"; "Considered options",
in particular the rejection of "Durable membership on every host, even alone"; Consequences). It is Proposed, and B0
([#2096](https://github.com/elsa-workflows/elsa-foundation/issues/2096)) accepts it. The boundary it is bounded by is
[ADR 0031](../../docs/adr/0031-runtime-burst-execution-sticky-single-writer-drain-with-in-process-fast-path.md): durable
state is the truth, and in-process memory is only a cache. The rules this spec applies are the
[framework constitution](../../.specify/memory/constitution-framework.md)'s §2.1 (three-layer separation), §2.6.2
(replacement contracts) and §2.20 (provider module decomposition), and ADRs
[0073](../../docs/adr/0073-ef-core-is-the-only-first-party-persistence-family.md) (EF Core only),
[0074](../../docs/adr/0074-first-party-ef-stores-retry-in-bounded-application-loops.md) (bounded retries) and
[0076](../../docs/adr/0076-persistence-tooling-runs-inside-the-host-closure.md) (D2, `[EfModule]`; D9, the activation
guard on the host container).

Companion specs, all drafts on [PR #2109](https://github.com/elsa-workflows/elsa-foundation/pull/2109):
[spec 180](../180-schema-upcaster-chain/spec.md) (B4) defines schema families and readable sets, which B3 reports.
[Spec 181](../181-schema-finalization-gate/spec.md) (B5) is this spec's first consumer. Its "Requirements on
membership", MR-001 to MR-007, are requirements on this spec, and "Requirements from spec 181" below maps each one.
[Spec 182](../182-dormant-features-until-finalization/spec.md) (B6) consumes membership only through spec 181.

## Terms

Spec 180's and spec 181's Terms apply, notably schema family, readable set and counted member. Host, feature, module,
provider and shell mean what the [root glossary](../../docs/glossary/root.md) says. In addition:

- **Member**: one incarnation of a host in the fleet. A restarted host is a new member (ADR 0078, Decision).
- **Host id**: the stable identity of a host. It survives restarts.
- **Incarnation**: the identity of one run of a host. A host gets a new one each time its process starts, and each
  time it rejoins after a lapse.
- **Status**: joining, active, draining or left. A member writes its own status.
- **Heartbeat** and **expiry**: a member renews its entry every heartbeat interval. The entry expires when it has not
  been renewed for its expiry period plus the skew allowance, as another member's clock reads it.
- **Lapse**: a member's own conclusion that others may have counted it as expired. A lapsed member knows it (MR-005).
- **Displaced incarnation**: an earlier incarnation of a host id, after a later incarnation of the same host id has
  joined.
- **Fleet view**: the set of members as one provider sees it. A **fresh read** reads the provider's authoritative
  store. A **cached read** may lag by at most one heartbeat interval.
- **Member report**: what a member says about itself, in named sections. ADR 0078 and #2097 call it the
  "capabilities" or "capability payload"; this spec says "member report" because the root glossary retires
  "Capability".
- **Readability report**: the member report section B3 adds: for each schema family the host has loaded, its readable
  set. Spec 181 uses the same term.
- **Member query**: a request for the members that satisfy a list of requirements, for a stated purpose (counting or
  placement).
- **Provider kind**: in-process or durable (MR-006).

## Current state

Nothing records which hosts exist. The runtime has four per-execution or per-item liveness mechanisms, and each is
keyed by an execution or a work item, never by a host:

- the **execution lease and heartbeat** that carry the fencing token checked at checkpoint commit
  (`IRuntimeExecutionOwnershipService`, `ExecutionLivenessState`), which the recovery scanner reads;
- the **placement lease** of `WorkflowsRuntimeDistributed` (`IExecutionPlacementStore`: `OwnerId`, `PlacementToken`,
  expiry), renewed by the placement pump;
- the command transport's **visibility lease**, held by the node id;
- the scheduler work queue's **claim visibility timeout**.

A host appears in them only as an owner id, and there are two unreconciled ones: the placement `NodeId`
(`node:{machine}:{pid}`) and the execution owner id (`inproc:{machine}:{pid}`). Both change on every restart, so no
record can say that a host restarted, is draining, or which modules it can read. Every lease is stamped and judged
with the acting host's `TimeProvider`; no database clock is used anywhere. The `[SingleNodeTask]` lock and the host
health probes are not host registries either. The inventory, with files, is in [research.md](./research.md).

**Membership builds on these and replaces none of them.** The execution lease is the fence (ADR 0078, invariant 2) and
stays exactly as it is. The placement lease stays the routing record; B7
([#2103](https://github.com/elsa-workflows/elsa-foundation/issues/2103)) makes placement ask membership which hosts
may take work, and takes `NodeId` from the member's host id (ADR 0078, Consequences). What membership adds is the one
thing none of them has: a per-host record with an incarnation, a status and a report.

## The invariant and how it holds

**Invariant.** A fresh read of the fleet view never omits a member that may still read or write through a module
whose schema family it reports, and never credits a member with a version it cannot read.

The gate of spec 181 finalizes on what a fresh read shows, so omitting a live member, or over-reporting what one can
read, is exactly the failure that looks like success. It holds through five mechanisms, each tested separately
(Success Criteria):

1. **A publish is durable before it returns, and a fresh read reads the authoritative store** (FR-010, FR-011). A
   member that has published is in every later fresh read.
2. **Expiry is conservative relative to lapse** (FR-007, FR-008). Other members count a member as expired only after
   it has itself concluded that it lapsed, as long as clocks agree within the skew allowance.
3. **Nothing is dropped silently** (FR-012, FR-016). A read that cannot return every member fails instead of
   returning part. An entry a reader cannot interpret is returned with an unknown report, which counts as reading
   nothing. A displaced incarnation still counts until its own entry expires.
4. **A report never claims more than every loaded reader can read** (FR-021, FR-022). The readable set is the
   intersection over every loaded declaration of the family, and a family leaves the report only when no declaration
   of it remains loaded.
5. **A lapsed member knows it** (FR-007), so spec 181's FR-018 can stop its gated writes.

Routing is held to a different standard on purpose. For placement, a displaced or doubtful member is dropped at once,
because a wrong routing decision is harmless: the fence at the commit rejects a stale writer (ADR 0078, invariant 2).
For counting, a doubtful member is kept, because a premature finalization is not harmless. Membership is never
consulted at the commit.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A single host is a cluster of one and pays nothing (Priority: P1)

An operator runs one host and configures nothing about clustering. The host reports itself as the only live member,
with its readability report, and every consumer of membership works. No membership table exists, no heartbeat is
written and no background task runs.

**Why this priority**: Non-clustered hosting must pay nothing for this program (#2093, Acceptance; #2097). It is also
what lets spec 181 finalize immediately on a single host.

**Independent Test**: Start a host that composes no membership feature against a fresh database. Read the fleet view,
and inspect the database, the registered background work and the opened connections.

**Acceptance Scenarios**:

1. **Given** a host that composes no membership feature, **When** a consumer reads the fleet view, **Then** it holds
   exactly one member: this host, live, with provider kind in-process.
2. **Given** the same host, **When** it has run for longer than any heartbeat interval, **Then** the database holds no
   membership table, and membership has opened no connection and registered no hosted service or recurring task.
3. **Given** the same host, **When** it asks "can every live member read family F at version V?" for a version in its
   readable set, **Then** the answer is yes, with no blockers.

---

### User Story 2 - Hosts that share a store see each other, and a stopped host drops out (Priority: P1)

Three hosts compose the EF membership provider against one database. Each sees all three. One host is killed. Until
its expiry has passed, every host still counts it. Afterwards, none does.

**Why this priority**: This is B2's acceptance (#2098), and the expiry direction is where a mistake looks like success:
a member dropped too early lets the gate finalize while that host can still write.

**Independent Test**: Two or three hosts with a controllable clock share one database on each supported engine. Kill
one without a graceful stop, advance the clock, and read the fleet view from a survivor just before and just after
the expiry boundary.

**Acceptance Scenarios**:

1. **Given** two hosts that joined the same store, **When** either reads the fleet view, **Then** it sees both, each
   with its host id, incarnation, status and report.
2. **Given** a host that stops heartbeating, **When** a survivor reads at any instant before that host's expiry
   period plus the skew allowance has passed, **Then** the host is still live.
3. **Given** the same host, **When** a survivor reads after that point, **Then** the host is expired and not counted.
4. **Given** a host that stops gracefully, **When** it shuts down, **Then** it is draining while it stops and left once
   it has stopped, and a left member is never counted.

---

### User Story 3 - The fleet answers "can every live member read F at V?" (Priority: P1)

During a rolling upgrade, one host reads family F at {1} and two read {1, 2}. The fleet answers no, and names the host
that cannot read version 2. When that host is upgraded and its new incarnation has published, the answer becomes yes.

**Why this priority**: This is B3's acceptance (#2099) and spec 181's MR-007. It is the only question the gate asks.

**Independent Test**: Drive joins, upgrades and leaves in a fleet of in-memory or EF members, and after each step
compare the answer and its list of blockers with the expected set.

**Acceptance Scenarios**:

1. **Given** counted members reading {1} and {1, 2}, **When** the query asks about version 2, **Then** the answer is
   no and it lists the member that reads {1}, with its readable set.
2. **Given** a member that has just published a report reading {1}, **When** any member makes a fresh read after the
   publish returned, **Then** the answer includes that member.
3. **Given** a member whose entry the reader cannot interpret, **When** the query runs, **Then** that member is listed
   as a blocker with an unknown report, never skipped.
4. **Given** a member that has loaded two declarations of F, reading {1} and {1, 2}, **When** it publishes, **Then**
   its report says {1}.

---

### User Story 4 - A restarted host is a new member (Priority: P2)

A host crashes and restarts within its expiry period. Its new incarnation joins under the same host id, and the
earlier incarnation is displaced at that moment. Placement stops treating the earlier incarnation as a candidate at
once. The gate keeps counting it until its own entry expires.

**Why this priority**: ADR 0078 makes "a restarted host is a new member" part of the contract. Without it, a crashed
host's leases wait out their own timeouts.

**Independent Test**: With the EF provider and a controllable clock, start a host, kill it, start it again with the
same host id, and read the fleet view for both purposes.

**Acceptance Scenarios**:

1. **Given** an earlier incarnation of host H, **When** a new incarnation of H joins, **Then** the fleet view shows
   the earlier one as displaced and the new one as H's current incarnation.
2. **Given** that fleet view, **When** a placement query runs, **Then** only the current incarnation can match.
3. **Given** that fleet view, **When** the readability query runs before the displaced entry expires, **Then** the
   displaced incarnation is still counted.
4. **Given** two live processes configured with the same host id, **When** the second joins, **Then** the first learns
   at its next heartbeat that it was displaced while healthy, lapses, reports a duplicate host id, and does not rejoin.

---

### User Story 5 - Any provider proves the four invariants before it is supported (Priority: P2)

A contributor writes a membership provider, for instance one backed by an actor framework's cluster. They run the
provider-neutral conformance suite against it. It passes only if the provider keeps ADR 0078's four invariants and the
semantics above.

**Why this priority**: ADR 0078 says the invariants are "checked rather than trusted", and that a provider not run
against the suite is not supported.

**Independent Test**: Run the suite against the in-process and EF providers, then against deliberately broken
providers: one that expires members early, one that truncates the fleet view, one whose publish returns before it is
visible, and one registered beside another provider. Each broken provider must fail a named test.

**Acceptance Scenarios**:

1. **Given** the in-process provider, **When** the suite runs, **Then** every single-member test passes and every
   multi-member test is reported as not applicable to a cluster of one.
2. **Given** the EF provider, **When** the suite runs on SQLite, SQL Server, PostgreSQL and MySQL, **Then** every test
   passes.
3. **Given** two providers composed in one host, **When** the host starts, **Then** it fails with a diagnostic that
   names both.
4. **Given** a provider that returns before a publish is visible, **When** the read-after-write test runs, **Then** it
   fails.

---

### User Story 6 - Placement asks membership, not a table (Priority: P3)

B7 needs "a member able to run work that requires X". It asks the membership contract with a member query and receives
the matching members, and for every other member the requirement it failed. It never reads the membership tables and
never calls a framework's placement API.

**Why this priority**: ADR 0078's invariant 3. This spec only fixes the interface; B7 designs placement.

**Independent Test**: Run the same member query against the in-process and EF providers, and assert identical answers
for identical fleets.

**Acceptance Scenarios**:

1. **Given** members that are joining, active, draining, displaced and expired, **When** a placement query runs,
   **Then** only live, current, active members can match.
2. **Given** the same fleet, **When** a counting query runs, **Then** joining, active and draining members, including
   displaced ones that have not expired, are all counted.

---

### Edge Cases

- **A host with several shells.** Membership is per host process. Every shell of one process sees the same member and
  the same fleet view. A process whose shells select different providers, or different stores, is refused when the
  second one activates (FR-017).
- **A package installed through Nuplane on a running host.** Its family declarations are loaded before its shell
  generation prepares, so a publish then reports them (FR-020). A declaration still loaded in an older generation
  narrows the readable set until that generation's load context is gone (FR-021).
- **A package removed on a running host.** Its families stay in the report until no declaration of them remains
  loaded. Removing them earlier would stop the host being counted while it can still write.
- **The membership store is missing its tables.** Under `Validate` the EF provider's module is refused like any other
  EF module with a pending migration, so the shell does not activate.
- **An entry written by a newer version of the provider.** A reader that cannot interpret it returns it with an
  unknown report (FR-012), which blocks every readability answer until the reader is upgraded.
- **Members configured with different intervals.** Each entry carries its own expiry period, and readers judge each
  entry by it (FR-006, FR-028).
- **SQLite as the membership store.** Several processes can share one SQLite file only on one machine. It suits
  development and tests, not a multi-machine cluster (FR-033).
- **A host id that is too long, blank or malformed.** Configuration is refused at startup with a diagnostic.

### Failure modes

- **A member cut off from the store.** It cannot heartbeat. It concludes that it lapsed once its expiry period has
  passed since the start of its last successful heartbeat, and spec 181's FR-018 stops its gated writes. The others
  count it as expired only later, after the skew allowance too. It rejoins as a new incarnation when the store is
  reachable again.
- **The store unreachable for everyone.** Every member lapses after its expiry period, and no fresh read succeeds, so
  nothing finalizes. Under spec 181's FR-018, gated writes stop on every host until the store returns. This is the
  conservative direction and it is loud, but it couples the availability of gated writes to the membership store
  (Open Question 5).
- **A stalled host** (a long GC pause, CPU starvation, a suspended VM). It stops heartbeating, so it lapses and then
  expires. When it resumes it measures elapsed time by the larger of the wall clock and a monotonic clock (FR-007),
  because a monotonic clock may not advance while a VM is suspended. A stall between that check and a write cannot be
  closed by any lease: spec 180's FR-018 is what refuses a stale host overwriting a row it cannot read, and the fence
  is what refuses a stale checkpoint.
- **A host that is alive but whose work is hung.** Membership measures process liveness, not work health. A hung
  dispatch keeps its host live in the fleet view. The scheduler's absolute dispatch deadline covers that case, not
  membership.
- **Clock skew within the allowance.** Expiry and lapse stay ordered (mechanism 2).
- **Clock skew beyond the allowance.** A reader whose clock runs ahead can count a live member as expired. A member
  that sees another entry's heartbeat time ahead of its own clock by more than the allowance reports skew (FR-029).
  If the gate finalizes too early as a result, the host that was missed refuses the family's reads and writes loudly
  (spec 180, FR-007 and FR-018; spec 181, FR-012). It does not corrupt rows, but the rollback boundary has been
  crossed early.
- **A clock that jumps.** A forward jump can make a member conclude early that it lapsed, which is safe. A backward
  jump is caught by the monotonic measure of FR-007.
- **A misconfigured cluster.** Several hosts share a database while each uses the in-process default, so each sees a
  cluster of one. See Open Question 1.
- **Split fleets.** Hosts that share a module database but point membership at different stores form two fleets that
  cannot see each other. It is the same failure as a misconfigured cluster, and Open Question 1 covers both.

## Requirements *(mandatory)*

### Functional Requirements

**The contract (B1)**

- **FR-001**: Membership MUST be a replacement contract under framework constitution §2.6.2, declared as one on the
  contract itself. One provider is active per host process. The in-process default yielding to one opt-in provider is
  not a conflict. Two opt-in providers in one process MUST fail at startup with a diagnostic that names both, and MUST
  NOT be resolved by last-write-wins, whatever order they are composed in (ADR 0078, invariant 1).
- **FR-002**: The contract MUST live in a `.Core` package that holds only abstractions and models, per §2.1 Layer 1.
  It references nothing beyond the `Microsoft.Extensions.*` abstractions and other `.Core` packages. An architecture
  test MUST fail the build if it references EF Core, a database engine, an actor framework or any provider package
  (#2097, Acceptance).
- **FR-003**: A member's host id MUST be stable across restarts of the same host. It defaults to the machine name and
  MAY be set by configuration. It MUST be at most 128 UTF-16 code units, non-blank and well-formed Unicode, compared
  ordinally, the same limits `DistributedRuntimeIdentityConstraints` sets today, so that B7 can take `NodeId` from it
  unchanged. The contract MUST expose the local member's host id and incarnation to consumers.
- **FR-004**: A member's incarnation MUST be new each time the host process starts and each time it rejoins after a
  lapse. It is an opaque, unique value that consumers compare only for equality. When an incarnation joins, every
  earlier incarnation of the same host id MUST be marked displaced.
- **FR-005**: A member's status MUST move only forward within an incarnation: joining, then active, then draining, then
  left. A member is joining from when it joins until the host has started, draining from when the host begins to stop,
  and left once it has stopped. A crashed member never writes left; it expires.
- **FR-006**: Liveness MUST be by heartbeat and expiry. Each member renews its entry every heartbeat interval. Each entry
  carries its own expiry period, which MUST be at least three heartbeat intervals, so one lost heartbeat does not make
  a member lapse. A reader MUST judge a member live until its last heartbeat time plus its expiry period plus the skew
  allowance has passed on the reader's clock. Intervals, expiry and allowance are validated at startup, and an invalid
  combination is refused.
- **FR-007**: A member MUST conclude that it lapsed, and MUST tell its consumers so, when any of these holds: its
  expiry period has passed since the start of its last successful heartbeat, measured as the larger of wall-clock and
  monotonic elapsed time; its entry is missing from the store; or its incarnation has been displaced (MR-005). A lapsed
  member rejoins as a new incarnation. A displaced one never rejoins, because another process now holds its host id: it
  stays lapsed and reports the displacement (FR-037), as a duplicate host id if its own heartbeats were still
  succeeding when it learned of it.
- **FR-008**: The skew allowance MUST make mechanism 2 hold: for any skew between two clocks within the allowance, a
  member concludes that it lapsed no later than any other member judges it expired. It holds by construction, because
  the heartbeat time a member writes is taken from its own clock at the start of the heartbeat from which it measures
  its lapse.

**The fleet view**

- **FR-009**: A read of the fleet view MUST return one snapshot: the provider kind, the instant the snapshot was judged
  at, and every member with its host id, incarnation, status, last heartbeat time, expiry period, whether it is live,
  whether it is displaced, and its report with that report's revision (MR-001, MR-006).
- **FR-010**: A consumer MUST be able to ask for a fresh read, which reads the provider's authoritative store, or a
  cached read, which may lag by at most one heartbeat interval. Any decision that must not be premature, such as spec
  181's evaluation and confirmation, MUST use fresh reads. Routing MAY use cached reads.
- **FR-011**: Publishing a member's report MUST be available on demand as well as on every heartbeat. An on-demand
  publish recomputes the report from its sources at that moment, and returns only once the report is visible to every
  later fresh read by any member (MR-003, MR-004). Until the member leaves or expires, a fresh read MUST include its
  latest published report, or a later one.
- **FR-012**: A fresh read MUST NOT be partial. If the provider cannot return every member, the read fails with an
  error. It never returns a truncated or empty view in place of a failure. An entry the reader cannot interpret MUST
  be returned with an unknown report, never skipped.
- **FR-013**: A consumer MUST be able to learn of changes to the fleet: a join, a status change, a new report, a lapse,
  a displacement, a departure or an expiry. The provider raises in-process events (§2.6.1) when it observes a change,
  and a consumer can also compare successive reads. Events are an accelerator; a consumer MUST NOT depend on them as
  its only way to learn of a change (spec 181, FR-005).

**The member report and member queries**

- **FR-014**: A member report MUST consist of named sections, each produced by exactly one registered report source.
  Two sources for one section fail at startup. A section is data only. It MUST NOT carry a connection string, a secret
  or configuration text.
- **FR-015**: A member query MUST name a purpose and a list of requirements. Requirements are data from a closed
  vocabulary the contract defines, so that every provider can translate them into its own terms (ADR 0078,
  invariant 3). This spec defines one kind: "reads schema family F at version V". B7 adds the kinds it needs, with the
  report section that carries them, as a contract change.
- **FR-016**: The answer to a member query MUST list the matching members and, for every other member it considered,
  the requirement that member failed. For the **counting** purpose, the members considered are every live member whose
  status is joining, active or draining, displaced or not; a member with an unknown report fails every requirement.
  For the **placement** purpose, only the current incarnation of each host id with status active is considered, and a
  member with an unknown report is not considered. Consumers MUST ask through the contract and MUST NOT read a
  provider's store or call a framework's API for this.

**The in-process default**

- **FR-017**: Without any membership configuration, a host MUST get the in-process provider. Its fleet view is the host
  alone. Fresh and cached reads are the same, a publish is visible at once, and the member never lapses. Membership is
  per host process: every shell of one process MUST see the same member and fleet view, and a shell that selects a
  different provider or store than an already activated shell of the same process MUST be refused at activation.
- **FR-018**: The in-process provider MUST cost nothing. It creates no table and no file, opens no connection, writes
  nothing durable, and registers no hosted service, recurring task or timer. It needs no configuration, and no feature
  has to be composed to get it: it is a Layer 2 default implementation under §2.1, which any consumer registers without
  replacing an existing registration. Non-clustered hosting therefore takes no hard dependency (#2097).

**The readability report (B3)**

- **FR-019**: The readability report MUST hold one entry per schema family the host has loaded: the family, its owning
  EF module, and its readable set as an ordered list of opaque version labels (spec 180, FR-004). Whether an entry also
  carries a database identity is Open Question 2. If the owner chooses the EF module as the unit of versioning (spec
  180, Open Question 1), entries are keyed by EF module instead and nothing else here changes.
- **FR-020**: The report MUST be derived only from the family declarations of spec 180's FR-001, read from every
  assembly loaded in the process, across every load context. No configuration can change it (MR-002). A publish
  recomputes it, so a package loaded through Nuplane is reported by the first publish after its assembly loads.
- **FR-021**: When several declarations of one family are loaded, for instance an old and a new shell generation
  during a reload, the readable set reported MUST be their intersection.
- **FR-022**: A family MUST stay in the report while any declaration of it remains loaded, even after its modules are
  disabled.
- **FR-023**: The contract MUST answer "can every counted member read family F at version V?" as a counting query with
  one requirement. The answer is yes only when every counted member's readable set for F contains V. Otherwise it
  lists each counted member that cannot, with its readable set or "unknown" (MR-007). A counted member is one whose
  report declares F, or whose report is unknown.

**The EF Core provider (B2)**

- **FR-024**: The EF provider MUST be enabled only by configuration, as an opt-in feature that replaces the in-process
  default (ADR 0078, Decision; #2098). Whether that feature is composed per shell or once per host is Open Question 3;
  FR-017 holds either way. Its settings follow the other EF modules': provider, connection string or connection name,
  schema and pooling, plus the heartbeat interval, expiry period, skew allowance and cleanup period.
- **FR-025**: It MUST be an `[EfModule]` with its own migrations-history table under `EfMigrationsHistory`, provider
  contexts and migrations for SQLite, SQL Server, PostgreSQL and MySQL, and the `Validate` and `AutoMigrate` policies
  every EF module has. The `dotnet elsa persistence` tool discovers it like any other module. Its rows carry a schema
  version stamp from their first migration.
- **FR-026**: The store MUST hold one entry per member, keyed by host id and incarnation, with its status, heartbeat
  time, expiry period and report. Every write is a compare-and-set on the entry's revision, retried in a bounded loop
  (ADR 0074). A member writes only its own entries, except that a joining member marks earlier incarnations of its
  host id displaced, and cleanup deletes entries.
- **FR-027**: Joining MUST insert a new incarnation as joining with its report, then mark earlier incarnations of the
  host id displaced. Each heartbeat MUST update the member's own entry, taking the heartbeat time from the member's
  clock at the start of the heartbeat, republishing the report if it changed, and then refreshing the cached view. A
  failed heartbeat is retried at the heartbeat interval. The retry MUST NOT widen past the expiry period the way
  `BackoffSweepPumpTask`'s failure backoff does. A heartbeat that finds its entry displaced, left or missing makes the
  member lapse (FR-007).
- **FR-028**: Readers MUST judge expiry by each entry's own expiry period (FR-006).
- **FR-029**: Clocks MUST be read through the injected `TimeProvider`, as every lease in the tree is today. A member
  that reads another entry's heartbeat time more than the skew allowance ahead of its own clock MUST report clock skew
  naming both hosts.
- **FR-030**: A member MUST write draining when its host begins to stop and left when it has stopped, so that a
  graceful stop is distinguishable from a crash.
- **FR-031**: Any member MUST be able to delete entries that have been left or expired for longer than the cleanup
  period, which MUST exceed the expiry period plus the skew allowance. Deletion is a compare-and-set, so it never
  removes an entry that was renewed in the meantime. It runs in bounded batches and is idempotent.
- **FR-032**: Membership entries MUST NOT be partitioned by persistence scope. Membership is per host, not per tenant.
  The provider MUST read and write the primary. A read replica cannot give read-after-write and is not supported for
  the membership store.
- **FR-033**: The provider MUST pass provider tests on all four engines, through the same container legs the other EF
  modules use. SQLite is supported for several processes on one machine only.
- **FR-034**: Provider failures MUST be wrapped at the store boundary (§2.23.5). No error message or diagnostic
  carries a connection string.

**Relation to the runtime's existing leases**

- **FR-035**: Membership MUST NOT replace or weaken the execution lease and its fencing token, the placement lease, the
  transport visibility lease or the scheduler's claim timeout. No provider may be consulted at checkpoint commit (ADR
  0078, invariant 2).
- **FR-036**: Adopting the member's host id as `WorkflowsRuntimeDistributed`'s `NodeId`, and reclaiming a lapsed or
  displaced member's leases at once, are B7's. This spec only exposes the host id, the incarnation, displacement and
  lapse (FR-003, FR-004, FR-007, FR-013) that they need.

**Diagnostics**

- **FR-037**: A member MUST report each of the following as a warning or error naming the hosts involved, and MUST make
  it visible in its own entry in the fleet view: a lapse, a displacement, a duplicate host id (displaced while its own
  heartbeats were succeeding, FR-007), clock skew (FR-029), an entry it cannot interpret, a failed fresh read, and
  shells of one process that disagree about membership (FR-017).

**Conformance suite**

- **FR-038**: A provider-neutral conformance suite MUST ship with the contract, as a test kit any provider runs by
  supplying a fixture that starts, stops, kills, isolates and restarts members, and controls their clocks. It has two
  tiers: the membership tier (FR-039) and the invariant tier (FR-040). A provider that is not run against it is not
  supported (ADR 0078).
- **FR-039**: The membership tier MUST prove at least: a single member sees itself; members sharing a store see each
  other; a publish is visible to the next fresh read on another member; no member is judged expired before its expiry
  period plus the allowance, and every member is judged expired after it; a member concludes that it lapsed no later
  than others judge it expired, for skews on both sides within the allowance; a displaced incarnation is counted until
  it expires and excluded from placement at once; status only moves forward; a fleet view is never partial; an
  uninterpretable entry is returned as unknown; the readability query's answers and blockers; and cleanup never
  deletes a live entry.
- **FR-040**: The suite MUST prove each of ADR 0078's invariants for the provider: (1) two opt-in providers in one host
  fail at startup with a diagnostic; (2) two hosts that both believe they own one execution, because their fleet views
  disagree, produce exactly one successful commit; (3) a member query returns only members whose report satisfies it,
  and handles members whose report is missing or unknown, and displaced incarnations, as FR-016 says; (4) work routed
  to a member that then dies is still delivered from the durable queue. Invariants 2 and 4 run over the distributed
  runtime composed on the provider.
- **FR-041**: Each test in FR-039 and FR-040 MUST be shown to bite: a deliberately broken provider (early expiry,
  truncation, a publish that returns before it is visible, a second registration) fails a named test.

**Admitting an actor framework as a provider**

- **FR-042**: An Orleans, Proto.Actor or Akka.NET integration MUST be a provider of this contract under
  `src/extensions`, and nothing else. It supplies membership backed by the framework's own cluster membership, and
  never runs beside the EF provider (invariant 1). It MUST give fresh reads with read-after-write (MR-003), even where
  the framework's own membership view is only eventually consistent, and MUST let a member learn that the framework has
  declared it dead (MR-005). It translates member queries into its own placement terms (invariant 3). If it also
  routes executions, it does so over the durable queue and never removes the fence (invariants 2 and 4). It MUST pass
  both tiers of the suite on every engine it supports.

### Requirements from spec 181

| Requirement | Met by | Limits |
|---|---|---|
| MR-001 fleet view: host id, incarnation, status, liveness | FR-003 to FR-006, FR-009 | "Left" is written only by a graceful stop; a crashed member expires instead. |
| MR-002 readability report derived from declarations only | FR-019 to FR-022 | Needs spec 180's FR-001 declarations, which do not exist yet. FR-021 narrows MR-002: the set reported is the intersection over loaded declarations. |
| MR-003 read-after-write | FR-010, FR-011, FR-032 | Holds for fresh reads only, and not with a read replica. Spec 181 must use fresh reads for evaluation and confirmation. |
| MR-004 publish before activate, including through Nuplane | FR-011, FR-020 | Membership provides the primitive and recomputes on demand. The ordering, publish and then read the record before activating, can only be enforced by the caller: spec 181's FR-013 and FR-015. |
| MR-005 lapse awareness | FR-007, FR-027, FR-042 | The in-process provider never lapses. |
| MR-006 provider kind visible | FR-009 | None. |
| MR-007 "can every live member read F at V?", with blockers | FR-023 | Counts displaced-but-live members and unknown reports, which extends spec 181's definition of a counted member (see Open Questions). |

Spec 181's Open Questions 2 (a misconfigured cluster) and 5 (fleets that span databases) are this spec's Open
Questions 1 and 2.

### Key Entities

- **Member**: host id, incarnation, status, last heartbeat time, expiry period, displaced or not, report and report
  revision.
- **Fleet view**: provider kind, the instant it was judged at, and its members. It is read fresh or cached.
- **Member report**: named sections, one source each. The readability report is the first.
- **Readability entry**: family, owning EF module, readable set, and possibly a database identity (Open Question 2).
- **Member query**: a purpose (counting or placement) and requirements from a closed vocabulary. Its answer lists
  matching members, and the failed requirement of every other member it considered.
- **Membership provider**: the single active implementation per process. In-process by default; EF Core opt-in; an
  actor framework only under FR-042.
- **Conformance kit**: the provider-neutral suite and the fixture a provider implements.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A host with no membership configuration reports exactly one live member, itself. After it has run for
  several heartbeat intervals, the database holds no membership table, and membership has opened no connection and
  registered no background work.
- **SC-002**: Two hosts sharing an EF store each see both within one heartbeat interval of the second joining, on
  SQLite, SQL Server, PostgreSQL and MySQL.
- **SC-003**: A host that stops heartbeating is live at every read before its expiry period plus the allowance and
  expired at every read after it. It concludes that it lapsed no later than any reader judges it expired, with reader
  clocks offset by up to the allowance either way.
- **SC-004**: Across repeated trials on every engine, the first fresh read after a publish has returned, on another
  member, includes that report every time.
- **SC-005**: The readability query gives the expected answer and blockers after every join, upgrade, lapse,
  displacement and departure in a scripted rolling upgrade, and it never answers yes while an unknown report or a
  displaced-but-live incarnation lacks the version.
- **SC-006**: Composing two opt-in providers fails at startup with a diagnostic naming both. Composing none, or one,
  starts.
- **SC-007**: The conformance suite passes for the in-process provider and for the EF provider on all four engines,
  and each deliberately broken provider of FR-041 fails at least one named test.
- **SC-008**: The contract package references no provider package, and one consumer test suite runs unchanged against
  the in-process and EF providers (#2097, Acceptance).

## Assumptions

- Hosts' clocks are kept within the skew allowance of each other, typically by NTP. Every lease in the tree already
  assumes this. Beyond the allowance, the failure is loud rather than silent (Failure modes).
- A fleet is tens to low hundreds of hosts, so one full read per heartbeat per member is cheap. Performance is stated
  qualitatively, because performance measurement is retired (#1668, ADR 0073).
- On all four engines, a committed write is visible to every later read on another connection at the default isolation
  level, when both go to the primary.
- Migrations are regenerated until 4.0 ships as a stable release (ADR 0078, Consequences;
  [#1976](https://github.com/elsa-workflows/elsa-foundation/issues/1976)), so the EF provider's tables land in its 4.0
  baseline.
- B3 has nothing to report until spec 180's family declarations exist.

## Dependencies

- **B0** (#2096) for accepted ADR text.
- **Spec 180** (B4, #2100), FR-001 and FR-004, for the family declarations and readable sets that B3 reports.
- Consumers: **spec 181** (B5, #2101) and **B7** (#2103).

## Out of Scope

- Finalization, holds and the refusal of hosts that cannot read (spec 181); upcasters (spec 180); dormancy (spec 182).
- Placement itself (B7): its requirement kinds, its report section, adopting the host id as `NodeId`, and reclaiming a
  lapsed or displaced member's leases.
- Any actor-framework provider. This spec sets only the rules for admitting one (FR-042).
- The expand-only migration guard (B8, [#2104](https://github.com/elsa-workflows/elsa-foundation/issues/2104)).
- Operator surfaces for the fleet view, such as a CLI command, an HTTP endpoint or Attention items. FR-037 makes the
  conditions visible; carrying them to operators is left to spec 181's status and spec 182's Attention contributor.
- Fleets whose members cannot all reach one membership store.

## Open Questions

1. **Must a cluster declare itself in configuration?** (Spec 181, Open Question 2; asked on #2093.) Several hosts that
   share a database while each uses the in-process default each see a cluster of one, and would finalize at once.
   - *Option A, declaration.* A clustered deployment must compose a durable provider, and the in-process default is
     the declaration that a host is alone. A startup diagnostic warns when durable facts that already exist show other
     hosts: spec 181's record naming a member outside this fleet view in a recent intent or finalization, or execution
     leases held by an owner other than this host. Nothing new is written.
   - *Option B, detection.* The in-process default writes a durable marker per host so that others can see it. That
     is the "Durable membership on every host" option ADR 0078 rejects, and it breaks FR-018.

   **Recommendation: A.** The failure it leaves is loud rather than silent. A host that was missed refuses to read or
   rewrite rows it cannot read (spec 180, FR-007 and FR-018), and refuses the family's writes at its next refresh
   (spec 181, FR-012). No row is misread, but the rollback boundary is crossed early. B costs every single host a
   durable write to prevent a configuration mistake.
2. **Do readability entries carry a database identity?** (Spec 181, Open Question 5; asked on #2093.) Today every
   counted member counts for a family, whichever database it serves. That can never finalize early, but a host that
   serves a different database can hold finalization back indefinitely.
   - *Option A.* Each readability entry carries the identity of the database its family's EF module is bound to. The
     identity is an opaque value created once in spec 181's finalization record, not derived from a connection string,
     which can alias one database under several names and would leak into the report. The gate counts only entries
     with its own database's identity. An entry without one counts for every database, which is the case for a host
     that has not yet read the record, so the conservative direction is kept. Because the identity never changes once
     created, a host can read it before publishing without weakening spec 181's mechanism 2.
   - *Option B.* No identity. Keep counting every member.

   **Recommendation: A.** It removes the trap of B and keeps its safety, at the cost of one column in spec 181's
   record.
3. **Is membership selected per shell or per host?** The decision of record says "an opt-in feature replaces the
   default", which in CShells is a per-shell choice. Membership is per process, so a per-shell selection needs FR-017's
   agreement check between shells. Selecting it once in host configuration, on the host container as ADR 0076's D9
   does for the activation guard, makes disagreement impossible by construction, lets the heartbeat run before any
   shell activates, and keeps the provider from being switched through the Modularity API at run time. The cost is
   that the EF provider has to be in the host closure rather than installed through Nuplane, and that its migrations
   run through the plain-host migrator. **Recommendation: per host**, if CShells shells can resolve a host-container
   service; otherwise per shell with FR-017.
4. **Default timings.** Proposed: heartbeat every 10 seconds, expiry after 30 seconds, a skew allowance of 5 seconds,
   and cleanup after 10 minutes. They match the placement lease (30 seconds) and pump (10 seconds) the runtime uses
   today, and fit inside spec 181's proposed 15-second refresh and 30-second evaluation.
5. **Should spec 181 narrow FR-018?** It stops every gated write on a lapsed member until it rejoins. Combined with
   FR-007, an outage of the membership store longer than the expiry period stops gated writes on every host. A lapsed
   member that keeps writing its observed finalized version writes rows every counted member can read, and spec 180's
   FR-018 already refuses its overwriting a row it cannot read. The narrower rule would keep writes available through
   a membership outage without losing safety.
6. **Glossary entries.** "Member", "incarnation", "fleet view", "member report" and "displaced incarnation" should join
   the entries proposed for specs 180 to 182 in `docs/glossary/elsa.md` when these specs are approved.
