---
status: proposed
date: 2026-09-24
decision_context: Discussion on 2026-09-23 and 2026-09-24 of the cluster gap ADR 0077 leaves open; decided by Sipke Schoorstra — membership first, a foundation contract with swappable providers, actor frameworks admitted only as providers bound by the four invariants below, and on 2026-09-24 that features needing new data wait for finalization and that migrations keep being regenerated until 4.0 ships as a stable release.
---

# Workflow executions are virtual actors, and cluster membership is a foundation contract

## Context

[ADR 0077](0077-a-module-upgrades-in-place-only-when-its-persisted-schema-is-unchanged.md) leaves one gap
explicitly open: **ragged activation**. Once a schema-changing module's migration is applied, pods activate
the new module version at their own pace, and the first to activate begins writing rows the others refuse.
The direction chosen for closing it is a cluster version gate — a new version reads its predecessor's rows
but keeps writing the old format until every live host can read the new one. That gate needs something the
system does not have: a record of **which hosts are alive and what each of them can do**.

Looking for where that record should live turned up something larger. **The workflow runtime is already a
virtual-actor system**, though no decision record says so:

- The code names it: `IWorkflowExecutionActorProvider` is "the workflow-execution actor subsystem". An
  in-process provider is the default; the opt-in `WorkflowsRuntimeDistributed` feature replaces it with
  `DistributedWorkflowExecutionActorProvider`.
- Each execution has one owner at a time, held by a per-execution placement lease (`OwnerId`,
  `PlacementToken`, an expiry), and every commit is **fenced**: a stale owner's checkpoint is rejected with
  `RuntimeStaleFencingTokenException`. The feature's own description says it plainly — "double execution is
  prevented by the single-writer fencing token at checkpoint commit."
- Work arrives through a durable work-item queue that acts as each execution's mailbox, and
  [ADR 0031](0031-runtime-burst-execution-sticky-single-writer-drain-with-in-process-fast-path.md) drains it
  single-writer, turn by turn, with in-process memory as a cache that is never a correctness dependency.

That is the virtual-actor model — stable identity, activation on demand anywhere, one fenced writer,
a durable mailbox, state that survives any host — built on the relational database.

**What it lacks is membership.** A lease knows *an* owner id (the `NodeId` setting) and an expiry. Nothing
records which hosts exist, whether a host restarted, whether it is shutting down, or which modules and schema
versions it can serve. The consequences are concrete: a crashed host's leases expire one timeout at a time;
placement cannot avoid a host that lacks the module an execution needs; a host cannot drain gracefully; and
the schema gate has nothing to finalize against.

## Decision

**Workflow executions are virtual actors over durable state.** This is the governing model for distributed
execution, and ADR 0031's invariant is its boundary: durable state is the truth, in-process memory is only a
cache, and delivery is at-least-once with de-duplication.

**Cluster membership is a foundation contract.** A member carries:

- a **host id** and an **incarnation** — a restarted host is a new member, so its predecessor's leases are
  dead the moment the new incarnation joins, not after each lease times out;
- a **status** — joining, active, draining, left — and a heartbeat;
- its **capabilities** — which modules are activated at which versions, and which schema versions each EF
  module on that host can read.

The contract is a **replacement contract** under
[framework constitution §2.6.2](../../.specify/memory/constitution-framework.md): one provider is active per
application, the contract declares that it is one, and registering a second provider is detected at startup
with a clear diagnostic rather than resolved by last-write-wins.

**It ships with two providers.**

- **In-process, the default** — a cluster of one, the host itself. No tables, no heartbeats written.
  Non-clustered hosting pays nothing for this decision.
- **Durable, opt-in** — EF-backed members, heartbeats and incarnations, for clustered hosting. This is the
  same shape the runtime already uses: an in-process default that an opt-in feature replaces.

**It lives at the foundation level, not inside the workflow runtime.** Its first consumer is the persistence
schema gate, which serves every EF module; placing membership in the workflow runtime would make persistence
depend on the workflow engine.

**The finalized schema version per module is always durable**, whichever membership provider is active. It is
a fact about the database, not about the fleet — in the same way the per-module migrations-history tables are.
This gives even a single host a guarantee it lacks today: rolled back to an older binary, it refuses the module
cleanly rather than reading rows it cannot understand.

## Invariants every membership or actor provider must preserve

These four rules bind the in-process provider, the EF provider, and **any future integration with an actor
framework** such as Orleans, Proto.Actor or Akka.NET. A provider that cannot preserve all four is not a valid
provider, however well it performs.

1. **There is exactly one view of the fleet.** Membership is a provider contract, and an actor-framework
   integration supplies membership *backed by its own cluster membership* — it never runs beside Elsa's.
   Two membership systems disagree about who is alive exactly when it matters, during a failure. The
   constitution's replacement-contract rule makes a second registration a startup error, not a silent
   override.

2. **Fencing lives at the commit, independent of routing.** No provider's single-activation guarantee
   substitutes for the fencing token checked at checkpoint commit. Actor frameworks' guarantees are weaker
   than they read: Orleans documents single activation as eventually consistent, so during membership
   changes a grain can briefly have two activations. The fence is what makes a duplicate harmless, so it
   stays in the persistence commit where no routing layer can bypass it.

3. **Placement capabilities are a query on membership.** A consumer that needs "a member able to run work
   requiring module M at version V" asks the membership contract. It never reads the EF membership tables,
   and never calls a framework's placement API, directly. Each provider translates the query into its own
   terms — an Orleans placement director, Akka.NET cluster roles, Proto.Actor member kinds — so
   version-aware placement survives any swap of provider.

4. **The durable work queue is the record of what is owed.** A provider may route work, activate executions
   and accelerate delivery; it never *owns* delivery. Actor frameworks deliver in memory, and ADR 0031
   requires at-least-once durable delivery with de-duplication as a queue-provider contract. A framework is
   therefore a routing accelerator over the durable queue, never the mailbox of record.

**Each invariant is backed by a provider conformance suite** that every provider must pass, so the rules are
checked rather than trusted. At minimum: two concurrent owners of one execution produce exactly one successful
commit (2); work routed and then lost in transit is still delivered from the durable queue (4); a capability
query returns only members advertising that capability (3); and registering two providers fails at startup
with a diagnostic (1). A provider that is not run against the suite is not supported.

## The first consumers

**The schema version gate.** A module version declares the schema version it writes and the versions it can
read. Writers write the module's *finalized* version; a new version is finalized **automatically once every
live member reports it can read it**, and an operator can place a hold — during a canary, say — and release
it. Once a version is finalized, a host whose readable range excludes it refuses to activate the module. Rows
outlive versions — a workflow started under one version may be resumed years and several versions later — so
readers apply a **chain of upcasters** from each version to its successor, rows upgrade lazily when next
written, and a version may leave the readable range only once a scan proves no rows at that version remain.
While a version awaits finalization, migrations are **expand-only**: nothing is dropped, renamed or retyped
until no finalized version still reads it.

**Features that need the new data wait for finalization.** Until then writers use the old format, so new
fields have nowhere to go. Finalization is also the **rollback boundary**: before it, no rows in the new format
exist and rolling back to the previous binary is safe; after it, it is not. Gating new-data features on
finalization is what keeps that rollback real. The cost is small: with automatic finalization the window lasts
only as long as the rollout, and on a single host it closes immediately. It stretches only under an operator's
hold — during a canary, which is exactly when rollback must stay clean. Two rules go with it:

- **A dormant feature says why.** It reports that it becomes available once every host can read the new
  version, rather than simply being absent.
- **A write that needs dormant data is refused, never dropped.** A request setting a field the old format
  cannot hold fails with a diagnostic. Quietly discarding the field would be exactly the silent data loss
  this design exists to prevent.

Modules check dormancy through one shared helper over the finalized version, so the rule is applied the same
way everywhere rather than reinvented per module.

**Version-aware placement.** Mid-rollout, or when a module has been installed on only part of the fleet, an
execution needing that module is placed only on members that advertise it. Finalization stops a new version
writing too early; placement stops an execution landing on a host that cannot run it. Neither is sufficient
without the other.

**Draining and failover.** A draining member takes no new placements and hands executions off at their next
checkpoint. A member whose heartbeat lapses — or that rejoins as a new incarnation — has its leases reclaimed
at once, instead of each lease waiting out its own timeout.

## Actor frameworks

**None is adopted as the core model.** Orleans, Akka.NET and Proto.Actor each bring their own membership,
placement, persistence and transport; the runtime already has all four, and two of them — persistence and
transport — would contradict [ADR 0073](0073-ef-core-is-the-only-first-party-persistence-family.md) and
ADR 0031. Adopting one would also mean operating a second cluster, with its own ports and membership store,
beside the database that already coordinates everything.

**Each is admitted as a provider**, under `src/extensions`, bound by the four invariants and the conformance
suite. The design keeps that door open deliberately without walking through it: an integration earns its
place when the database-coordinated transport and lease renewal become the throughput ceiling, or when an
adopter already runs one of these frameworks and wants Elsa to join that cluster rather than stand beside it.
Neither is evidenced today. If one is built, Orleans fits best, because its virtual actors are the model the
runtime already uses — an integration needs a single generic workflow-execution grain type, so modules
installed at runtime add no grain types. Proto.Actor's cluster mode fits next; Akka.NET's cluster sharding
approximates virtual actors with the most ceremony.

## Considered options

- **Adopt an actor framework as the runtime's core.** Rejected for the reasons above: it duplicates four
  mechanisms the runtime already has and contradicts two accepted decisions.
- **A registry built only for schema finalization.** Rejected. Placement and draining would each grow their
  own partial registry, and the three would disagree.
- **A separate set of clustered modules that consumers opt into.** Rejected. Every consumer would branch or
  duplicate, and single-host deployments would never execute the cluster code paths — the paths most likely
  to fail in production would run only in the distributed test suite.
- **Durable membership on every host, even alone.** Rejected. One implementation is simpler, but every
  deployment would carry tables and heartbeat writes it does not need, and the in-process-default shape
  already exists in the runtime.
- **Early use of new-data features where the data lives only in new columns.** An EF update from an older
  model never touches a column it does not know, so such data survives old writers. Rejected: rolling back
  before finalization would hide data users had already entered, each change would need its own proof, and it
  fails outright for stores that rewrite a JSON document or delete and reinsert a row.
- **Leave dormancy to each module.** Rejected: nothing would stop a module from silently dropping new data,
  the one failure this design most needs to rule out.

## Consequences

- A new foundation module carries the membership contract and the in-process provider; an opt-in module
  carries the EF provider. Neither exists yet.
- `WorkflowsRuntimeDistributed` should consume membership: its `NodeId` becomes the member's host id, and
  failover reclaims a lapsed member's leases rather than re-driving each lease on its own timeout.
- The conformance suite is part of the contract, built alongside the first provider rather than after it.
- Every schema-changing release ships an upcaster from its predecessor. For an additive change the upcaster
  is trivial; the cost grows only with the size of the change.
- **Migrations keep being regenerated until 4.0 ships as a stable release.** Until then every model change
  rewrites the affected module's `Initial` migration under a new identifier, so a database that applied the
  previous one sees the new one as pending and tries to create tables that already exist. None of the gate —
  nor any upgrade path — applies to a real database before then, and a persistent development database,
  Workbench's own SQLite files included, must be recreated after a model change. At the 4.0 stable release
  each module's `Initial` is frozen as its baseline, later model changes add incremental migrations as
  Secrets already does, and a guard fails the build if a shipped migration is edited, renamed or deleted.
  The freeze is part of shipping 4.0, not a follow-up to it; it is tracked as #1976.

## Linked decisions

- [ADR 0031](0031-runtime-burst-execution-sticky-single-writer-drain-with-in-process-fast-path.md) — the
  single-writer drain and the durable-first invariant this model is bounded by
- [ADR 0073](0073-ef-core-is-the-only-first-party-persistence-family.md) — why persistence stays EF under any
  provider
- [ADR 0076](0076-persistence-tooling-runs-inside-the-host-closure.md) — the activation guards the gate builds
  on
- [ADR 0077](0077-a-module-upgrades-in-place-only-when-its-persisted-schema-is-unchanged.md) — the gap this
  closes; amended on 2026-09-24 to name this gate as its destination
- [Framework constitution §2.6.2](../../.specify/memory/constitution-framework.md) — replacement contracts
- Issue #1144 (FR-1, independent release)
