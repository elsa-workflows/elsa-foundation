# Feature Specification: Cluster Membership

**Feature Branch**: `claude/2093-rollout-specs`
**Created**: 2026-09-27
**Status**: Approved
**Input**: Workstreams B1 ([#2097](https://github.com/elsa-workflows/elsa-foundation/issues/2097)), B2
([#2098](https://github.com/elsa-workflows/elsa-foundation/issues/2098)) and B3
([#2099](https://github.com/elsa-workflows/elsa-foundation/issues/2099)) of the cluster-safe schema rollout program
[#2093](https://github.com/elsa-workflows/elsa-foundation/issues/2093). Cluster membership is a foundation contract: a
record of which hosts are alive and what each of them can read. It ships with an in-process default, a cluster of one
that costs nothing, and an opt-in EF Core provider for clusters. Each host reports, through membership, the
persisted-schema versions it can read.

Decision of record: [ADR 0078](../../docs/adr/0078-workflow-executions-are-virtual-actors-and-cluster-membership-is-a-foundation-contract.md)
(Decision; "Invariants every membership or actor provider must preserve"; "Actor frameworks"; "Considered options",
in particular the rejection of "Durable membership on every host, even alone"; Consequences). It is accepted with
these decisions through B0 ([#2096](https://github.com/elsa-workflows/elsa-foundation/issues/2096), PR #2118). The
boundary it is bounded by is
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
- **Host id**: the identity a member joins under. It is meant to survive restarts of the same host, but whether the
  default achieves that depends on the environment (FR-003).
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

Nothing records which hosts exist. The runtime has several per-execution or per-item liveness mechanisms, but every
one is keyed by an execution or a work item, never by a host, and the two that mint a per-process id derive it from
the machine name and process id, so neither survives a restart (see [research.md](./research.md), "Existing liveness
and identity mechanisms").

**Membership builds on these and replaces none of them.** The execution lease stays the commit-time fence (ADR 0078,
invariant 2). The placement lease stays the routing record until B7
([#2103](https://github.com/elsa-workflows/elsa-foundation/issues/2103)) makes placement ask membership which hosts
may take work, and takes `NodeId` from the member's host id (ADR 0078, Consequences; see
[research.md](./research.md), "Existing liveness and identity mechanisms", "How the spec builds on each"). What
membership adds is the one thing none of them has: a per-host record with an incarnation, a status and a report.

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
   of it remains loaded. Amended 2026-09-29 (Decisions): a declaration an in-place upgrade has retired no longer
   counts, and one is retired only once nothing in the host can still run it.
5. **A lapsed member knows it** (FR-007), so spec 181's FR-018 can hold its gated writes to the version it last
   observed as finalized (spec 181, Decisions, Q22).

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

### User Story 4 - A restarted host is a new member, and a live host id is never taken from under it (Priority: P2)

A host crashes and restarts under a host id whose source is stable across restarts (a VM, a bare-metal machine, a
Kubernetes `StatefulSet` pod). If it restarts before its earlier incarnation's entry has expired, the join is refused,
and the joiner watches that incarnation for one full liveness window — its expiry period plus the skew allowance (35
seconds by default), measured from when the joiner first observed it as live — issuing a warning naming the host id
and the live incarnation it is waiting on for each refused attempt in the meantime. If the earlier incarnation never
renews during that window, it was a crash: its entry expires, the join then succeeds, and the earlier incarnation is
marked displaced; placement stops treating it as a candidate at once, and the gate keeps counting it until its own
entry expires. If instead the earlier incarnation renews during that window, it is a live duplicate, not a crash: the
joiner reports the duplicate host id (FR-039), naming the host id and both processes as far as known, and refuses to
start — it never keeps retrying silently. On a host whose id source is not stable (a Kubernetes `Deployment` pod,
whose hostname changes every restart), a restart instead joins under a new host id; the old entry is never displaced,
and drops out of counting only when it expires on its own (FR-003, FR-007). Separately, if two live processes are ever
configured with the same host id, for instance two Elsa processes on one development machine, whichever joins second
observes the first renew within one liveness window and refuses to start with the duplicate host id diagnostic,
rather than retrying forever.

**Why this priority**: ADR 0078 makes "a restarted host is a new member" part of the contract, but a live incarnation
must never be displaced by another. FR-003a now requires an explicit host id whenever a durable provider is composed,
which removes the case where the machine-name default alone caused an accidental collision; FR-004b's watch-then-decide
is the backstop both for a restart that outruns its earlier incarnation's expiry and for an operator explicitly
configuring two live processes with the same id. A crash-restart under a stable host id is therefore delayed by up to
the expiry period plus the skew allowance before it rejoins; a live duplicate refuses to start within that same window
rather than displacing the survivor or retrying forever.

**Independent Test**: With the EF provider and a controllable clock, start a host, kill it, restart it with the same
host id before its entry has expired, and confirm the new incarnation's join is refused while it watches the earlier
entry; advance the clock past the liveness window without renewing that entry and confirm the join then succeeds.
Repeat after the entry has already expired and confirm the join succeeds at once. Separately, start two hosts
configured with the same host id while both are live, let the earlier one keep heartbeating, and assert the second
observes a renewal within one liveness window, reports the duplicate host id diagnostic (FR-039) naming both, and
refuses to start rather than retrying forever.

**Acceptance Scenarios**:

1. **Given** an earlier incarnation of host H that is no longer live (FR-006), **When** a new incarnation of H joins,
   **Then** the fleet view shows the earlier one as displaced and the new one as H's current incarnation.
2. **Given** an earlier incarnation of host H that is still live and never renews again, **When** a new incarnation of
   H attempts to join, **Then** the join is refused while the joiner watches the earlier incarnation, and it succeeds
   only once the earlier incarnation's entry has gone unrenewed for one full liveness window — its expiry period plus
   the skew allowance, measured from when the joiner first observed it as live.
3. **Given** the fleet view of scenario 1, **When** a placement query runs, **Then** only the current incarnation can
   match.
4. **Given** the fleet view of scenario 1, **When** the readability query runs before the displaced entry expires,
   **Then** the displaced incarnation is still counted.
5. **Given** two live processes, **When** the second is configured with the host id the first is still live under and
   the first keeps renewing, **Then** the second's join is refused, and once the second observes the first renew
   within one liveness window of first observing it, the second reports the duplicate host id diagnostic (FR-039),
   naming the host id and both processes as far as known, and refuses to start; the first keeps its incarnation
   undisturbed, and nothing is displaced.
6. **Given** a host whose id source changes on every restart, **When** the process restarts, **Then** it joins under a
   new host id, and the old entry is neither displaced nor treated as the same host; it only stops being counted once
   it expires.

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

- **A host with several shells.** Membership is per host process, selected once in host configuration, so every shell
  of one process sees the same member and the same fleet view by construction: no shell has its own provider or store
  to disagree with another's (FR-017; Decisions, Q20).
- **A package installed through Nuplane on a running host.** Its family declarations are loaded before its shell
  generation prepares, so a publish then reports them (FR-020). A declaration still loaded in an older generation
  narrows the readable set until that generation's load context is gone (FR-021). Amended 2026-09-29 (Decisions):
  Nuplane never unloads a host-integrated package's load context, so a host-integrated EF module's previous release
  stays loaded after a reload, and "gone" would mean a restart; the older generation instead stops narrowing once it is
  retired - Nuplane's active package set lists a newer generation of the same assembly, no shell generation that can
  still run code composes from its load context, and the feature catalog the next shell generation is built from does
  not name it (FR-021). Such a module is therefore upgraded in place, with no restart; restarting the host on the new
  release still works, and is no longer required.
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
- **A durable provider composed with no explicit host id.** Refused at startup with a diagnostic (FR-003a;
  Decisions, Q24), before any join is attempted.
- **Two live durable-provider processes explicitly configured with the same host id.** The second's join is refused,
  and it watches the first for one full liveness window (FR-004b); when the first renews within that window, the
  second reports the duplicate host id diagnostic (FR-039), naming the host id and both processes, and refuses to
  start rather than retrying forever, and an operator must configure a distinct host id to resolve it. The first is
  undisturbed.
- **An ephemeral pod restarts under a new host id.** The old entry is never displaced, because no incarnation joins
  under its host id again. It is counted until its own entry expires, which only delays counting, and never lets two
  live incarnations be mistaken for one host (FR-003, FR-007).

### Failure modes

- **A member cut off from the store.** It cannot heartbeat. It concludes that it lapsed once its expiry period has
  passed since the start of its last successful heartbeat. Spec 181's FR-018 (narrowed; Decisions, Q22) lets it keep
  writing at the version it last observed as finalized, rather than stopping its gated writes outright. The others
  count it as expired only later, after the skew allowance too. It rejoins as a new incarnation when the store is
  reachable again.
- **The store unreachable for everyone.** Every member lapses after its expiry period, and no fresh read succeeds, so
  nothing finalizes. Under spec 181's narrowed FR-018, gated writes stay available on every host at each host's last
  observed finalized version; only writes that would need a newer, unobserved finalization are unavailable. No
  version that any counted member cannot read is ever written, because spec 180's FR-015 still refuses a write that
  cannot confirm the version it targets is finalized.
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
  cluster of one. A cluster declares itself only by composing a durable provider (FR-018a); there is no startup
  warning and no detection of this case (Decisions, Q18).
- **Split fleets.** Hosts that share a module database but point membership at different stores form two fleets that
  cannot see each other. It is the same failure as a misconfigured cluster, and Decisions, Q18 covers both.

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
- **FR-003**: A member's host id MUST default to the machine name, and MAY be set by configuration. It MUST be at most
  128 UTF-16 code units, non-blank and well-formed Unicode, compared ordinally, the same limits
  `DistributedRuntimeIdentityConstraints` sets today, so that B7 can take `NodeId` from it unchanged. The contract MUST
  expose the local member's host id and incarnation to consumers. The default is safe, but it is not stable across
  restarts in every environment, and this spec MUST NOT imply that it is:
  - On a host whose machine name is stable across restarts (a VM, a bare-metal machine, a Kubernetes `StatefulSet`
    pod), the default host id survives a restart, so a restart is a new incarnation of the same host id (FR-004), and
    the invariant's mechanisms apply to it.
  - On a host whose machine name changes on every restart (a Kubernetes `Deployment` pod), the default host id does
    not survive a restart. A restarted pod joins under a new host id; its old entry is a member that never rejoins,
    and it drops out of counting only when its own entry expires (FR-007). Finalization is delayed by, at most, that
    expiry; it is never made unsafe by it, because the old entry is not displaced or reused.
  - On a machine that runs more than one Elsa process (a developer machine, or multi-process hosting), the
    machine-name default gives every process on it the same host id. This is the one case the default does not
    resolve safely by itself: two live processes must never share a host id. FR-004b refuses the second process's join
    rather than letting it displace the first.

  The machine-name default applies only to the in-process provider.
- **FR-003a**: A host MUST configure an explicit host id whenever a durable membership provider (B2, or any other
  provider under FR-061) is composed; starting a durable provider without one MUST be refused at startup with a
  diagnostic. A clustered deployment therefore states its own identity instead of relying solely on FR-004b's
  collision refusal to catch a mistake (Decisions, Q24).
- **FR-004**: A member's incarnation MUST be new each time the host process starts and each time it rejoins after a
  lapse. It is an opaque, unique value that consumers compare only for equality.
- **FR-004a**: When an incarnation joins under a host id whose most recently joined incarnation is not live by FR-006's
  liveness rule (it has expired or left), that earlier incarnation MUST be marked displaced. Only a restart of the
  same host — one where the earlier incarnation has stopped heartbeating, left, or expired — may displace it, and
  never one that is still live (FR-004b).
- **FR-004b**: When an incarnation attempts to join under a host id whose most recently joined incarnation is still
  live (heartbeating and not expired, FR-006), the join MUST be refused, and the joiner MUST watch that incarnation for
  one full liveness window — its expiry period plus the skew allowance (35 seconds by default), measured from when the
  joiner first observed it as live. Each refused attempt while the joiner waits MUST be visible as a warning naming the
  host id and the live incarnation it is waiting on. If, before the window elapses, the joiner observes that
  incarnation's entry renew, the case MUST be concluded to be a live duplicate, not a crash: the joiner MUST report the
  duplicate host id (FR-039), naming the host id and both processes as far as known, and MUST refuse to start — it MUST
  NOT keep retrying silently. If the window elapses with no renewal observed, the incarnation has crashed: the join
  MUST then proceed and displace it (FR-004a). The existing live incarnation MUST NOT be displaced or otherwise
  disturbed at any point before its own entry expires. A crash-restart under a host id whose source is stable across
  restarts is therefore delayed by up to that liveness window before it rejoins; a live duplicate refuses to start,
  within that same window, rather than displacing the survivor or retrying forever. If two processes are ever
  explicitly configured with the same host id while both stay live, for instance two Elsa processes on one development
  machine, whichever joins second detects the first's renewal within its liveness window, reports the duplicate host
  id, and refuses to start; an operator must configure a distinct host id to resolve it. A race where two joins are
  concurrently in flight for one host id MUST resolve so that a live incarnation is never silently displaced by
  another live one: each side that observes the other renewing during its own window refuses to start rather than
  displacing it.
- **FR-005**: A member's status MUST move only forward within an incarnation: joining, then active, then draining, then
  left. A member is joining from when it joins until the host has started, draining from when the host begins to stop,
  and left once it has stopped. A crashed member never writes left; it expires.
- **FR-006**: Liveness MUST be by heartbeat and expiry. Each member renews its entry every heartbeat interval,
  defaulting to 10 seconds. Each entry carries its own expiry period, defaulting to 30 seconds, which MUST be at
  least three heartbeat intervals, so one lost heartbeat does not make a member lapse. A reader MUST judge a member
  live until its last heartbeat time plus its expiry period plus the skew allowance — defaulting to 5 seconds — has
  passed on the reader's clock. Intervals, expiry and allowance are validated at startup, and an invalid combination
  is refused.
- **FR-007**: A member MUST conclude that it lapsed, and MUST tell its consumers so, when any of these holds: its
  expiry period has passed since the start of its last successful heartbeat, measured as the larger of wall-clock and
  monotonic elapsed time; its entry is missing from the store; or its incarnation has been displaced (MR-005). A lapsed
  member rejoins as a new incarnation. A displaced one never rejoins, because another process now holds its host id: it
  stays lapsed and reports the displacement (FR-038). Because FR-004b never displaces an incarnation while it is still
  live, a member is only ever displaced after its own entry has already gone unrenewed for a full liveness window, so
  this path never coincides with heartbeats that were still succeeding; the duplicate-host-id diagnostic (FR-039) is
  raised instead by the joiner that finds a live duplicate, before it starts (FR-004b).
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
  a displacement, a departure or an expiry. `IClusterMembership` MUST expose a host-level change signal — a change
  token or subscription — that a consumer can wait on or subscribe to, with no dependency on the Events feature:
  membership is selected per host (Decisions, Q20), while `IEventPublisher` is shell-scoped, so a shell-scoped event
  cannot stand for a host-level fleet change. A shell that wants shell-scoped events adapts the signal itself. A
  consumer can also compare successive reads. The signal is an accelerator; a consumer MUST tolerate a missed signal
  and MUST NOT depend on it as its only way to learn of a change — it MUST still re-read to find the current state
  (spec 181, FR-005).

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
  selected once, in host configuration, on the host container (ADR 0076, D9), not per shell: every shell of one
  process MUST see the same member and fleet view, and no shell can select a different provider or store, so
  disagreement between shells is impossible by construction and FR-017 needs no run-time agreement check
  (Decisions, Q20).
- **FR-018**: The in-process provider MUST cost nothing. It creates no table and no file, opens no connection, writes
  nothing durable, and registers no hosted service, recurring task or timer. It needs no configuration, and no feature
  has to be composed to get it: it is a Layer 2 default implementation under §2.1, which any consumer registers without
  replacing an existing registration. Non-clustered hosting therefore takes no hard dependency (#2097).
- **FR-018a**: A cluster declares itself solely by composing a durable membership provider (B2). Composing the
  in-process default, or configuring nothing, is itself the declaration that a host is alone. No mechanism in this
  spec detects, or warns at startup about, several hosts that share a database while each uses the in-process
  default: each sees a cluster of one, and nothing here says otherwise. A missed declaration is not silent forever:
  the invariant's other mechanisms make it loud once it bites, through spec 180's read and write refusals and spec
  181's finalization and write refusals (Decisions, Q18).

**The readability report (B3)**

- **FR-019**: The readability report MUST hold one entry per schema family the host has loaded: the family, its owning
  EF module, its readable set as an ordered list of opaque version labels (spec 180, FR-004), and the opaque
  per-database identity of the finalization record it read most recently, when it has read one. An entry with no
  identity yet counts for every database, which keeps the conservative direction until the host has read the record
  (spec 181, FR-001; Decisions, Q19). Amended 2026-09-28 (Decisions): the entry also carries the member's observed
  finalized version for the family (spec 181, FR-010), when it has read one, so spec 186's settle condition can see
  what every counted member has observed without a database round trip (spec 186, MR-001).
- **FR-020**: The report MUST be derived only from the family declarations of spec 180's FR-001, read from every
  assembly loaded in the process, across every load context. No configuration can change it (MR-002). A publish
  recomputes it, so a package loaded through Nuplane is reported by the first publish after its assembly loads.
  Amended 2026-09-29 (Decisions): except the declarations FR-021 retires.
- **FR-021**: When several declarations of one family are loaded, for instance an old and a new shell generation
  during a reload, the readable set reported MUST be their intersection. Amended 2026-09-29 (Decisions): a declaration
  MUST stop counting once it is retired, and MUST NOT stop counting before. It is retired when all three hold:
  1. The host's package runtime has positive evidence that a newer generation replaced it: for Nuplane, its active
     package set lists a loaded assembly of the same name and not this one, and this one is in a load context Nuplane
     created.
  2. No shell generation that can still run code composes a feature from its load context. A shell generation counts
     from before its first initializer runs until its container has finished disposing: when it was drained, until
     that drain completes, which the shell runtime completes only after the generation's service provider has been
     disposed; otherwise, or when that drain fails, until its container has disposed everything it created after the
     generation began. It counts every load context, other than the default one, of a feature its container names -
     for CShells, every feature of the catalog snapshot it was built from, enabled or not - so a replaced declaration
     beside an unchanged feature in the same load context keeps counting. A generation whose features cannot be read counts for every replaced
     declaration.
  3. The feature catalog the next shell generation will be built from names neither it nor a feature in its load
     context. A catalog that has not been initialized yet, or that cannot be read, counts for every replaced
     declaration: the first build may be reading any of them.

  So while an old and a new generation are both live they are still intersected, and the old one stops counting as
  soon as nothing that could run it is left, without a restart. A declaration in the default load context, in a load
  context the package runtime did not create, or whose replacement is still loading or failed to load, is never
  retired. The host publishes its report again when a shell generation that stops counting, or a refresh of the feature
  catalog that stops naming it, retires one, so a gate waiting on it evaluates without waiting for another publish.
  That publish runs after the shell generation's disposal, never inside it.
- **FR-022**: A family MUST stay in the report while any declaration of it remains loaded, even after its modules are
  disabled. Amended 2026-09-29 (Decisions): any declaration FR-021 has not retired. A package removed, not replaced,
  retires nothing.
- **FR-023**: The contract MUST answer "can every counted member read family F at version V?" as a counting query with
  one requirement, taking an optional database identity (spec 181, FR-001). The answer is yes only when every counted
  member's readable set for F contains V. Otherwise it lists each counted member that cannot, with its readable set
  or "unknown" (MR-007). A counted member is one whose report declares F, and, when a database identity is given,
  whose entry for F names that identity or names none, or whose report is unknown (Decisions, Q19).

**The EF Core provider (B2)**

- **FR-024**: The EF provider MUST be enabled only by configuration, as an opt-in feature that replaces the in-process
  default (ADR 0078, Decision; #2098), composed once on the host container rather than per shell (FR-017; Decisions,
  Q20), so its migrations run through the plain-host migrator rather than a shell's. Its settings follow the other EF
  modules': provider, connection string or connection name, schema and pooling, plus the heartbeat interval, expiry
  period, skew allowance and cleanup period.
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
  period, defaulting to 10 minutes, which MUST exceed the expiry period plus the skew allowance. Deletion is a
  compare-and-set, so it never removes an entry that was renewed in the meantime. It runs in bounded batches and is
  idempotent.
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
  lapse (FR-003, FR-004, FR-004a, FR-007, FR-013) that they need.

**Diagnostics**

Each of the following is its own failure mode a member MUST report as a warning or error naming the hosts involved,
so that a failure in any one of them is traceable to a single requirement. Every condition appears in the subject's
own entry in the fleet view, except FR-039: the refused joiner reports it at startup, because it never joins and so
never has an entry (Decisions, 2026-09-28).

- **FR-037**: A member MUST report a lapse (FR-007).
- **FR-038**: A member MUST report a displacement (FR-004a).
- **FR-039**: A joiner MUST report a duplicate host id: the live incarnation it is watching under FR-004b's liveness
  window renews before that window elapses, so its own join is refused as a live duplicate rather than a crash. The
  report MUST name the host id and both processes as far as known, and the joiner MUST refuse to start rather than
  keep retrying.
- **FR-040**: A member MUST report clock skew, naming both hosts (FR-029).
- **FR-041**: A member MUST report an entry it cannot interpret (FR-012).
- **FR-042**: A member MUST report a failed fresh read.

**Conformance suite**

- **FR-044**: A provider-neutral conformance suite MUST ship with the contract, as a test kit any provider runs by
  supplying a fixture that starts, stops, kills, isolates and restarts members, and controls their clocks. It has two
  tiers: the membership tier (FR-045 to FR-055) and the invariant tier (FR-056 to FR-059). A provider that is not run
  against it is not supported (ADR 0078).

The membership tier is one FR per clause it must prove, so a failing test names exactly which one it is:

- **FR-045**: The membership tier MUST prove that a single member sees itself.
- **FR-046**: The membership tier MUST prove that members sharing a store see each other.
- **FR-047**: The membership tier MUST prove that a publish is visible to the next fresh read on another member.
- **FR-048**: The membership tier MUST prove that no member is judged expired before its expiry period plus the skew
  allowance has passed, and that every member is judged expired after it has passed.
- **FR-049**: The membership tier MUST prove that a member concludes that it lapsed no later than another member
  judges it expired, for skews on both sides within the allowance.
- **FR-050**: The membership tier MUST prove that a displaced incarnation is counted until it expires, and excluded
  from placement at once.
- **FR-051**: The membership tier MUST prove that a member's status only moves forward.
- **FR-052**: The membership tier MUST prove that a fleet view is never partial.
- **FR-053**: The membership tier MUST prove that an entry the reader cannot interpret is returned as unknown.
- **FR-054**: The membership tier MUST prove the readability query's answers and blockers, across a scripted rolling
  upgrade.
- **FR-055**: The membership tier MUST prove that cleanup never deletes a live entry.

The invariant tier is one FR per invariant of ADR 0078:

- **FR-056**: The invariant tier MUST prove invariant 1: two opt-in providers in one host fail at startup with a
  diagnostic.
- **FR-057**: The invariant tier MUST prove invariant 2: two hosts that both believe they own one execution, because
  their fleet views disagree, produce exactly one successful commit. This runs over the distributed runtime composed
  on the provider.
- **FR-058**: The invariant tier MUST prove invariant 3: a member query returns only members whose report satisfies
  it, and handles members whose report is missing or unknown, and displaced incarnations, as FR-016 says.
- **FR-059**: The invariant tier MUST prove invariant 4: work routed to a member that then dies is still delivered
  from the durable queue. This runs over the distributed runtime composed on the provider.
- **FR-060**: Each test in FR-045 to FR-055 and FR-056 to FR-059 MUST be shown to bite: a deliberately broken provider
  (early expiry, truncation, a publish that returns before it is visible, a second registration) fails a named test.

**Admitting an actor framework as a provider**

- **FR-061**: An Orleans, Proto.Actor or Akka.NET integration MUST be a provider of this contract under
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
| MR-002 readability report derived from declarations only | FR-019 to FR-022 | Needs spec 180's FR-001 declarations, which do not exist yet. FR-021 narrows MR-002: the set reported is the intersection over loaded declarations that are not retired (amended 2026-09-29). FR-019 also carries the database identity (Decisions, Q19). |
| MR-003 read-after-write | FR-010, FR-011, FR-032 | Holds for fresh reads only, and not with a read replica. Spec 181 must use fresh reads for evaluation and confirmation. |
| MR-004 publish before activate, including through Nuplane | FR-011, FR-020 | Membership provides the primitive and recomputes on demand. The ordering, publish and then read the record before activating, can only be enforced by the caller: spec 181's FR-013 and FR-015. |
| MR-005 lapse awareness | FR-007, FR-027, FR-061 | The in-process provider never lapses. |
| MR-006 provider kind visible | FR-009 | None. |
| MR-007 "can every live member read F at V?", with blockers | FR-023 | Counts displaced-but-live members and unknown reports, matching spec 181's Terms definition of a counted member; takes an optional database identity (Decisions, Q19). |

Spec 181's Q8 (a misconfigured cluster) and Q11 (fleets that span databases) are this spec's Q18 and Q19
(Decisions).

### Key Entities

- **Member**: host id, incarnation, status, last heartbeat time, expiry period, displaced or not, report and report
  revision.
- **Fleet view**: provider kind, the instant it was judged at, and its members. It is read fresh or cached.
- **Member report**: named sections, one source each. The readability report is the first.
- **Readability entry**: family, owning EF module, readable set, and the database identity of the finalization
  record most recently read, when one has been read (Decisions, Q19).
- **Member query**: a purpose (counting or placement) and requirements from a closed vocabulary. Its answer lists
  matching members, and the failed requirement of every other member it considered.
- **Membership provider**: the single active implementation per process. In-process by default; EF Core opt-in; an
  actor framework only under FR-061.
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
  and each deliberately broken provider of FR-060 fails at least one named test.
- **SC-008**: The contract package references no provider package, and one consumer test suite runs unchanged against
  the in-process and EF providers (#2097, Acceptance).
- **SC-009**: A durable provider composed with no explicit host id refuses at startup with a diagnostic (FR-003a),
  before any join is attempted. Two durable-provider processes explicitly configured with the same host id still
  resolve to at most one live incarnation (FR-004b), as SC-006 exercises for two opt-in providers.
- **SC-010**: A readability query given a database identity counts only members whose entry for the family names it
  or names none; a member serving a different database never contributes to that answer.
- **SC-011**: Two durable-provider processes explicitly configured with the same host id, both live: the second
  observes the first renew within one liveness window of first observing it — at most the expiry period plus the skew
  allowance from when the attempt began — reports the duplicate host id diagnostic (FR-039) naming both, and refuses
  to start; the first's incarnation is never displaced or disturbed.

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
- Placement itself (B7, [#2103](https://github.com/elsa-workflows/elsa-foundation/issues/2103)): its requirement
  kinds, its report section, and adopting the host id as `NodeId`.
- Failover: draining, and reclaiming a lapsed or displaced member's leases immediately rather than waiting for a
  lease's own timeout (ADR 0078, "Draining and failover"). This belongs to B7 (#2103) (Decisions).
- Any actor-framework provider. This spec sets only the rules for admitting one (FR-061).
- The expand-only migration guard (B8, [#2104](https://github.com/elsa-workflows/elsa-foundation/issues/2104)).
- Operator surfaces for the fleet view, such as a CLI command, an HTTP endpoint or Attention items. FR-037 to FR-042
  make the conditions visible; carrying them to operators is left to spec 181's status and spec 182's Attention
  contributor.
- Fleets whose members cannot all reach one membership store.

## Decisions

Recorded 2026-09-27, when the owner answered this spec's open questions on #2093.

- **Q18 — Must a cluster declare itself in configuration?** Yes, and there is no startup warning (differs from the
  draft's recommended Option A, which paired declaration with a startup diagnostic). A clustered deployment must
  compose a durable provider; the in-process default is the declaration that a host is alone (FR-018a). No mechanism
  reads durable facts to detect a mismatch. A missed declaration fails loudly, not silently, once it bites: through
  spec 180's read and write refusals and spec 181's finalization and write refusals.
- **Q19 — Do readability entries carry a database identity?** Yes: an opaque identity, created once in spec 181's
  finalization record, not derived from a connection string (FR-019). The readability query counts only entries
  that name it or name none (FR-023).
- **Q20 — Is membership selected per shell or per host?** Per host, chosen once in host configuration on the host
  container, as ADR 0076's D9 does for the activation guard (amends ADR 0078). FR-017's shell-agreement check
  becomes unnecessary and is simplified: disagreement between shells is impossible by construction. The EF provider
  is composed once on the host container rather than per shell (FR-024).
- **Q21 — Default timings.** Heartbeat every 10 seconds, expiry after 30 seconds, a skew allowance of 5 seconds, and
  cleanup after 10 minutes (FR-006, FR-031).
- **Q22 — Narrowing spec 181's FR-018.** Narrowed: a member that has dropped out of membership keeps writing the
  version it last observed as finalized, rather than stopping every gated write. Spec 180's FR-015 already refuses
  it overwriting a row it cannot read, so the narrower rule keeps writes available through a membership outage
  without losing safety.
- **Q23 — Glossary entries.** Added when the specs were approved (PR #2109), alongside the entries for specs 180 to 182.
- **Q24 — The default host id source.** The machine name for the in-process provider. An explicit host id is
  REQUIRED whenever a durable provider is composed (FR-003a); starting one without an explicit id is refused at
  startup. Two live processes that claim the same id are refused rather than displacing each other (FR-004b).

Recorded 2026-09-28, as a minimal amendment for spec 186 (B9).

- **The readability entry gains the member's observed finalized version.** Spec 186's settle condition needs to see
  what every counted member has observed without a database round trip, and the member already reads that version
  (spec 181, FR-010), so it is one more field on the entry it already publishes (FR-019; spec 186, MR-001), not a new
  report source.

Recorded 2026-09-28, when the owner amended FR-013 and the rejoin rule on #2093.

- **FR-013 drops its dependency on the Events feature.** Membership is selected per host (Q20), while
  `IEventPublisher` is shell-scoped, so a shell-scoped event cannot stand for a host-level fleet change.
  `IClusterMembership` instead exposes its own host-level change signal — a change token or subscription — and a
  shell that wants shell-scoped events adapts the signal itself. Consumers still tolerate a missed signal by
  re-reading, as FR-013 already required.
- **Rejoin waits for expiry, and a live incarnation is never displaced.** FR-004a already conditioned displacement on
  the earlier incarnation being not live by FR-006's rule; this amendment tightens FR-004b so a join under a
  still-live host id is refused and retried until that incarnation expires (at most the expiry period plus the skew
  allowance, 35 seconds by default), rather than treated as a one-shot refusal that presumes a misconfiguration. A
  crash-restart under a stable host id is delayed by up to that long before it rejoins; that delay is the accepted
  cost of never displacing a live duplicate. User Story 4's scenarios, and spec 184's User Story 3 and its research,
  are corrected to match: a same-host-id restart while the earlier incarnation is still live gains nothing over a
  survivor's own expiry-triggered reclaim.

Recorded 2026-09-28, implementing the rejoin decision above; not a new decision.

- **FR-039 moves from the displaced member to the joiner, because the rejoin amendment made its old trigger
  unreachable.** FR-004b already stopped a live incarnation from ever being displaced, so a member can no longer be
  both displaced and still heartbeating: FR-007's cross-reference to FR-039 for "a displacement while its own
  heartbeats were still succeeding" described a case that can no longer occur. FR-004b is restated so a joiner that
  finds a live latest incarnation retries while watching it for one full liveness window, measured from when it first
  observed it live: if that incarnation never renews within the window, the join proceeds as a crash-restart (FR-004a);
  if it renews within the window, the joiner concludes it is a live duplicate, raises the duplicate-host-id diagnostic
  (FR-039) naming the host id and both processes, and refuses to start rather than retrying silently forever. Each
  refused attempt before that point is also visible, as a warning naming the live incarnation the joiner is waiting
  on. FR-007's cross-reference to FR-039 is corrected to match. User Story 4's scenarios, and SC-009/SC-011, are
  amended so a live duplicate's outcome is "startup refused with FR-039 within one liveness window," not an unbounded
  retry.

Recorded 2026-09-28, reconciling the diagnostics preamble with the FR-039 move above; not a new decision.

- **The diagnostics preamble no longer promises every condition its own fleet entry.** FR-039 is now raised by a
  joiner that is refused before it ever joins (FR-004b), so it has no fleet entry to carry the diagnostic; it reports
  the diagnostic at startup instead. FR-037, FR-038, FR-040, FR-041 and FR-042 still appear in the subject's own
  entry.

**2026-09-28 note.** Found while building B5 (#2101); lands with the B5 PR, whose merge is the owner's approval.
FR-019's entry names the database identity of the finalization record the host read only while that is the one
database the host has read the family in and no activation of the family is mid-read; otherwise it names none, and
its observed finalized version is given only while every database the host has read agrees on it. Naming the
database read "most recently" would stop a second database's evaluators counting this host (FR-023). See spec 181's
note of the same date.

**2026-09-29 note (B5c, [#2151](https://github.com/elsa-workflows/elsa-foundation/issues/2151)).** Decided by the owner
on #2093 the same day; lands with the #2151 PR, whose merge is the owner's approval. `Elsa.Foundation.Host` now composes
`AddConfiguredClusterMembership` on its host container, beside `AddEfSchemaReadability`, exactly as `Elsa.Workbench`
does, so FR-024 holds on both shipped hosts: the EF provider is enabled by configuration alone, requires an explicit host
id (FR-003a), and an unconfigured host keeps the in-process default of FR-017 and FR-018. This supersedes spec 181's
2026-09-29 note that `Elsa.Foundation.Host` is a cluster of one. The host carries EF Core, the four engines and the EF
provider for this alone ([ADR 0076](../../docs/adr/0076-persistence-tooling-runs-inside-the-host-closure.md), amended
2026-09-29), and shares what it carries, so a feed-loaded EF module binds the host's `Elsa.Persistence.EntityFramework`
and EF Core. Nothing in this spec changes. `FoundationHostClusterBootTests` (`tests/essentials/Cluster/EntityFrameworkCore/Tests`)
boot two built hosts over one SQLite database with the provider enabled: they see each other (User Story 2); a
feed-loaded module's new schema version is not finalized while one of them runs the release that cannot read it, and
the feature that needs it answers 409 saying it waits for every host (FR-023; spec 182, FR-008); the older host is
killed and restarted on the new release under the same host id, waits out its predecessor's expiry (User Story 4,
FR-004b), and the version is then finalized and served on both. The same test with the provider left off shows each
host finalizing on its own (FR-018a), and a host with the provider half configured refuses to start.

Recorded 2026-09-29, amending FR-020 to FR-022 for an in-place upgrade; lands with the PR that implements it, whose merge
is the owner's approval.

- **A replaced package generation stops counting once it is retired, and not before.** Nuplane loads each
  host-integrated package graph into a load context it never unloads, so an EF module upgraded in place on a running
  host keeps its previous release's assembly loaded for the life of the process. FR-021's intersection over every
  loaded declaration then pinned the upgraded host to what the previous release reads - [1] and [1, 2] intersect to
  [1] - so the new version never finalized and every feature that needs it stayed dormant until a restart. The same
  stale assembly made the EF activation guard's module catalog find the module declared twice. The fix keeps the
  conservative direction in every transition: a declaration is retired only on positive evidence that a newer
  generation replaced it in the host's active package set, and only once nothing that could run it is left. That is
  every shell generation - an active one, including one whose reload failed, one still draining, or one still
  initializing - from before its first initializer runs until its provider has been disposed, pinning every load
  context of a feature its container names; and the next shell generation, which CShells builds from its runtime
  feature catalog's current snapshot however stale: `Elsa.Foundation.Host` skips refreshing that catalog after a
  reconcile while no shell is active, so with eager activation off, or after it failed, the first request would build
  from a catalog that still names the old generation. Until then both generations are intersected, as before. The
  host's evidence of replacement is Nuplane's catalog of the active package set, read on every publish; the evidence
  that nothing still runs the old generation is CShells' shell containers, its drains and its feature catalog. The
  finalization gates were considered and not chosen as that evidence: they live per shell container
  and are registered only after admission, so they cannot speak for a generation that is about to publish, nor for a
  family whose module no shell enables (FR-022). The activation guard, which judges the shell generation an apply is
  about to build, stops counting a replaced generation at once. Research: "B3: what 'loaded' means".
- **A module's migrations are bound by assembly, never by name.** The same in-place upgrade reached the new release's
  migrations while each EF module package carried EF Core in its own package graph. A module named its migrations
  assembly, EF Core resolves such a name with `Assembly.Load` from its own load context, and after an upgrade that was
  the previous release's graph context, which the new release binds EF Core from, so the name reached the previous
  release, whose migrations are keyed to the previous release's context type. The new release's context therefore saw
  no migration of its own: under `Validate` a reload onto a release with an unapplied migration activated anyway, and
  `AutoMigrate` would have applied nothing. Since #2151 `Elsa.Foundation.Host` carries and shares EF Core, so EF Core
  resolves the name from the host's own load context, which reaches a package's assembly only through Nuplane's
  host-integrated resolution of the active package set, and that answers with the new release; the previous release
  stays loaded, in the non-collectible host-integrated package context Nuplane gave it, but the name no longer reaches
  it there. Every module binding now hands EF Core the module's assembly itself
  (`EfRelationalProviderBinding.UseMigrationsFrom`), as do the design-time factory and the Workbench's OpenIddict
  context, so which release a module's migrations come from no longer depends on the load context EF Core resolves
  names from, and a reload onto a release with an unapplied migration is refused by `EfDatabaseMigrator`'s `Validate`
  check with the migration pending (ADR 0076), as a restart would be. This changes no requirement; it is recorded here
  because it is the same in-place upgrade.
