---
status: proposed
date: 2026-11-17
---

# Clean-slate execution kernel: actor runtime, durable actors, execution models

Status: proposed (17 November 2026). Program [#4229](https://github.com/elsa-workflows/elsa-foundation/issues/4229),
gate 2. The decisions below are the owner's, listed with dates and sources in the
[kernel decision register](../architecture/decisions.md). Design approval follows the independent architecture review;
until then this ADR authorizes no product code. Detail lives in the [architecture references](../architecture/README.md)
and the [design sketch](../reports/clean-slate-kernel/design-sketch-draft-5.md); this record does not repeat it.

## Context

**Cost.** Elsa 4's runtime core pays many database calls per workflow. For the same reference workflows:

| Workflow | Elsa 3.10.1 default, SQLite commands | Elsa 4 Balanced (11 November), SQLite commands / PostgreSQL round trips | Kernel prototype Balanced, SQLite engine calls (commands) / PostgreSQL round trips, fence in the batch |
|---|---:|---:|---:|
| Ten WriteLines | 29 | 64 / 48 | 5 (3) / 2 |
| HTTP request with its response | 15 | 83 / 80 | 5 (3) / 2; 0 / 0 inline in Ephemeral |
| Fork, join, one resume | 25 | 131 / 142 | 26 (18) / 11 |

Sources: Elsa 3 measured 15 November ([receipt](../reports/clean-slate-kernel/evidence/elsa3-cost/RECEIPT.md), default
configuration, counted until the call returned; its "not apples to apples" section lists the differences in
guarantees). Elsa 4 and the kernel from [NATIVE.md section 5](../../prototypes/kernel-model/NATIVE.md#5-what-it-costs)
and the [#2531 closeout](https://github.com/elsa-workflows/elsa-foundation/issues/2531), which gives 64 and 48 on main
at its close. Ten Durable WriteLines cost 230 PostgreSQL round trips on Elsa 4 and 22 on the prototype. The kernel
figures are prototype measurements; no kernel product exists.

**Why a new core.** Epic #2564 cut Elsa 4's calls slice by slice and was closed as superseded on 16 November. Four
design review rounds of a journal-first redesign (ADR 0098, open PR #4176) each found the fixes keeping the current
core's per-entity stores and handler reads. On 12 November the owner chose a clean slate: unreleased code, any API and
any ADR about the current architecture may change. His requirements: fast and well architected; Elsa native workflows
and other execution models (BPMN, ETL, agents, models not yet known); persistence as an abstraction with EF Core as one
provider and MongoDB possible; the actor model as its own replaceable layer; synchronous HTTP handled by a workflow.

**Evidence.** A standalone prototype (`prototypes/kernel-model/`, on main since #4250–#4260) carries the design:
deterministic simulation with mutation checks in CI, store conformance on PostgreSQL, SQL Server, SQLite, MySQL and
MongoDB, a runtime conformance suite with an Orleans reference adapter, killed-connection stress, and native, Flowchart
(graph engine), StateMachine, BPMN, ETL, agent and values-by-reference spikes. Its [README](../../prototypes/kernel-model/README.md),
[DESIGN.md](../../prototypes/kernel-model/DESIGN.md) and [FINDINGS.md](../../prototypes/kernel-model/FINDINGS.md) record
what is built, run and unverified. Prototype checks are design evidence; they qualify no product.

## Decision

### 1. Three layers

| Layer | Owns |
|---|---|
| 1. Actor runtime | Placement of ownership scopes on nodes, activation, one pass at a time per scope, wake-ups |
| 2. Durable actors | The durable inbox, the fenced atomic commit of a turn's state and sends, timers, background jobs, events, poison handling, the journal, over the persistence abstraction |
| 3. Actor types | What an actor does with its messages. A workflow instance is one actor type that runs an execution model; job and plan actors are others |

Runtimes host partitions only. The built-in runtime ships; Orleans hosts each partition as a grain, in a reference
adapter kept in tests ([sketch, three layers](../reports/clean-slate-kernel/design-sketch-draft-5.md#three-layers)).

### 2. The turn contract

An execution model implements `TurnAsync(ITurn)` and `ExecuteJobAsync(IJob)` over opaque state. A turn reads its
inbox and state in memory and produces new state, sends, timers, subscriptions, jobs and journal entries; the kernel
commits them atomically at the turn's end and only there. Everything that crosses an instance boundary is a message; no
instance reads another's state. The inbox alone says what is owed
([sketch, the execution model contract](../reports/clean-slate-kernel/design-sketch-draft-5.md#the-execution-model-contract)).

### 3. Four logical seams

Layer 2 offers four logical seams: **commit** (append a fenced batch), **ownership** (take, renew, lose authority),
**delivery** (route a message identity, deduplicate, keep one sender's order) and **read models** (every non-owner
query). A storage provider implements all four as one unit. A send is recorded atomically with its sender's state and
may be delivered later. The prototype's `IKernelStore` and `IActorRuntime` still combine them; the split is part of
the first product slice's design
([round 5 H paper](../reports/clean-slate-kernel/log-profile-contract-sketch.md)).

### 4. Deployment profiles

Persistence mode, actor runtime and provider vary independently; a profile is a named preset. **Embedded** (built-in
runtime, one node) and **Cluster** (built-in runtime, partitions shared over nodes) ship in release 1. **Orleans** waits
for proven demand. **Log** (replicated-log groups) is planned and not designed. A workflow behaves the same under every
profile, and no capability flag makes it fail on one. Inline execution is chosen per workflow or route
([sketch, profiles](../reports/clean-slate-kernel/design-sketch-draft-5.md#deployment-profiles-not-runtimes)).

### 5. Virtual partitions and the fence

1,024 fixed virtual partitions, one fence row each. One owner node writes a partition; its commit lands only while the
row still has the owner's epoch and expected position, checked in the same transaction, so a stale owner never lands a
commit. Leases decide who should write; the fence decides who can. The kernel writes its own commit SQL on PostgreSQL,
SQL Server, SQLite and MySQL, with the fence in the batch; EF Core keeps the schema, migrations and cold paths
([ownership and fencing](../architecture/ownership-and-fencing.md),
[PARTITIONS.md](../../prototypes/kernel-model/PARTITIONS.md), [COMMIT-SQL.md](../../prototypes/kernel-model/COMMIT-SQL.md)).

### 6. Persistence modes

The author chooses per workflow. **Ephemeral:** an instance that ends in its first turn without sending writes nothing,
and its short waits stay in memory. **Balanced** (default): one commit per wait; activities since the last commit may run
again after a crash. **Durable:** a commit after every activity. History is Full by default, with State and Minimal per
workflow, and viewer rows are projected from the journal after the commit
([NATIVE.md](../../prototypes/kernel-model/NATIVE.md), [storage model](../architecture/storage-model.md)).

### 7. The effect key

Every activity attempt carries `{instance origin}/{node}/{iteration path}/{attempt}`, stable across replay and
takeover. In release 1 the built-in HTTP and email activities pass it as `Idempotency-Key` and `Message-ID`, as the
prototype's do. Side effects stay at least
once; deduplication is the receiver's
([ownership and fencing, side effects](../architecture/ownership-and-fencing.md#side-effects)).

### 8. Layer 3 is host-neutral

Execution models, activities, Studio and the operator APIs depend only on the turn contract, the four seams and read
models. They never see partitions, positions, epochs or SQL. Release 1 ships a contracts package that layer 3 builds
against, and layers 1 and 2 as their own packages behind it.

### 9. Replace whole layers and edges, never pieces of the commit

Timers, messages, events and subscriptions stay inside the fenced commit. Replaceable: storage as a whole unit, the
actor runtime, execution models and actor types, ingress adapters (brokers, webhooks, schedulers as idempotent message
inserts), egress through jobs or an outbox with the effect key, the wake notifier and a test clock. No external broker
is the inbox and no scheduler library is the timer engine
([sketch, extensibility boundary](../reports/clean-slate-kernel/design-sketch-draft-5.md#extensibility-boundary-owner-principle-15-november)).

### 10. Release 1 scope

The kernel with the built-in runtime; Embedded and Cluster; the native model with structured containers, Flowchart on
the extracted graph engine and StateMachine; synchronous HTTP; PostgreSQL, SQL Server and SQLite tuned, MySQL
supported; MongoDB proven in CI; Studio on the new core with run views on the projections; agents as native activities;
basic trace context and metrics, retention and delete, admission control, simple instance migration; parked-work
inspect, retry and discard; values by reference
([sketch, release 1](../reports/clean-slate-kernel/design-sketch-draft-5.md#release-1-scope-owner-decision-amended-15-november)).

### 11. The seven design-now answers

Each answer is a Proposed page with the owner's decisions of 17 November at its end:

| Question | Page | Decisions in short |
|---|---|---|
| 1. Migrating running instances | [migrating-running-instances](../architecture/migrating-running-instances.md) | State versions per model and activity; release 1 migrates running and suspended instances at a turn boundary for the simple cases; a refused instance stays running |
| 2. Data lifecycle | [data-lifecycle](../architecture/data-lifecycle.md) | Retention worker over the instance-list read model; guarded shortened policy; delete plus values by reference now, per-instance keys in 1.x; clear journal envelope |
| 3. Fairness and backpressure | [fairness-and-backpressure](../architecture/fairness-and-backpressure.md) | Admission at the edge; no running-instance quotas; backlog on the fence row; only load-shedding commands bypass `InboxFull` |
| 4. Large fan-out | [large-fan-out](../architecture/large-fan-out.md) | Release 1 caps a parallel ForEach at 1,000 items, degree 16 by default and 64 at most; a combiner tree after release 1; a parked child keeps its parent waiting |
| 5. Job workers | [job-workers](../architecture/job-workers.md) | One job contract; standalone Elsa.Jobs and the remote worker protocol in 1.x |
| 6. Observability | [observability](../architecture/observability.md) | W3C trace context on every message; stable metric names; recovery in place plus intervention; Park keeps what a retry needs |
| 7. Write volume | [write-volume](../architecture/write-volume.md) | Snapshot plus journal stays; values by reference in the same database, accepted by a PostgreSQL run |

## Non-goals for release 1

- **Alternative durable hosts and a bring-your-own actor framework.** After release 1, layers 1 and 2 are to become a
  separable built-in default that users may replace. A replacement host (Orleans grains, Dapr actors, Proto.Actor,
  Akka.NET) would need activation with single-writer turns, an atomic per-actor outcome write, durable timers delivered
  as messages, and at-least-once delivery deduplicated by id, with an actor outbox for per-actor guarantees. That is a
  research priority after release 1; release 1 only protects the boundary (section 8).
- The Log profile.
- BPMN (its prototype stream is parked until after release 1), ETL and its combiner tree, and a dedicated agent model.
- MongoDB in production (1.1).
- Standalone Elsa.Jobs and remote job workers (1.x).

## Consequences

- **Clean break.** No running instance moves from the current core; the activity library and definitions carry over
  with an adapted activity context; the runtime API and Studio's run views are re-pointed. Two cores coexist until the
  switch ([program plan](../program-goals/clean-slate-kernel.md)).
- **Costs accepted.** Failover takes about one lease (10 s). Activity code runs at least once in Balanced. No instance
  reads another's state; lists and counts lag the projector. The whole state is rewritten at each commit, which values
  by reference bound. One shared database is the cluster's write ceiling. The sketch's
  [trade-offs](../reports/clean-slate-kernel/design-sketch-draft-5.md#trade-offs) list the rest.
- **Product work** comes in small PRs behind a host switch, with the old core the default until the switch. Changes to
  kernel behaviour need two independent reviews.
- **Budgets.** Ten WriteLines under Balanced: 32 SQLite commands or fewer and 16 PostgreSQL round trips or fewer, as
  deterministic product budgets (carried over from #2564 to gate 3).

## Alternatives considered

- **The current core with fixes (#2564).** Its delivered slices (#4089, #4166) left ten Balanced WriteLines at 64
  SQLite commands and 48 PostgreSQL round trips, against targets of 32 and 16; most of the rest was fixed per-run
  overhead around each drain. Closed as superseded on 16 November.
- **Journal-first core (ADR 0098, PR #4176).** A journal per run with a head row as fence, behind the existing ports.
  Four review rounds found blockers that came from keeping the old handlers' reads and writes. Superseded on
  12 November; the PR stays open until the owner sees its closure.
- **An actor framework as the durable layer** (Orleans, Proto.Actor). Rejected for layer 2: Orleans calls are at most
  once and state and sends are never one atomic act; Proto.Actor persists by event sourcing per actor. They remain
  layer 1 options.
- **Append-only storage, event sourcing or a log-only store as the default.** Measured write volume does not ask for it
  ([write volume](../architecture/write-volume.md)); a lazy snapshot stays the fallback spike and Log a later profile.

## ADRs this supersedes, amends or keeps

The owner sees this list before any ADR is changed. "Supersede" takes effect at the switch, when the old core goes;
until then the listed ADRs keep governing the current core.

| ADR | Disposition | Why |
|---|---|---|
| [0001](0001-checkpoint-gated-activity-execution-inspection.md) checkpoint-gated inspection | Supersede | Viewer rows are projected from the journal after the commit |
| [0020](0020-runtime-checkpoint-commit-post-commit-work.md) checkpoint commit and post-commit work | Supersede | The recipient's inbox is the outbox; nothing is dispatched after the commit |
| [0029](0029-runtime-execution-flows-through-the-pipelines.md) execution pipelines | Supersede | The drain dispatch point it wraps goes; any activity pipeline is a native-model question |
| [0031](0031-runtime-burst-execution-sticky-single-writer-drain-with-in-process-fast-path.md) sticky single-writer burst | Supersede | One owner per partition runs turns in memory; work items and the drainer go |
| [0032](0032-runtime-checkpoint-cadence-is-policy-driven-per-workflow.md) checkpoint cadence | Supersede | Persistence modes are turn boundaries (section 6) |
| [0033](0033-runtime-core-splits-contracts-from-engine.md) Runtime.Core split | Supersede | Release 1's package layout replaces those packages (section 8) |
| [0047](0047-replaysafe-activities-execute-as-fused-hops-with-precomputed-routing.md) ReplaySafe fused hops | Supersede | A turn runs many activities in memory; there are no hops to fuse |
| [0049](0049-runtime-alterations-use-snapshotted-atomic-jobs.md) alterations as snapshotted atomic jobs | Supersede | Owner, 17 November: migration widens to running instances at a turn boundary, through a plan actor |
| [0084](0084-runtime-commit-journal.md) commit journal | Supersede | The kernel's per-partition journal is the commit journal it proposed |
| 0098 journal-first core (PR #4176, not on main) | Supersede | Replaced by this ADR on 12 November |
| 0096 Elsa.Jobs (Codex branch, not on main) | Supersede | Standalone Jobs are rebuilt as a job actor type in 1.x |
| [0073](0073-ef-core-is-the-only-first-party-persistence-family.md) EF Core only | Amend | The kernel's commit SQL is hand-written per engine; a MongoDB provider is first-party from 1.1 |
| [0074](0074-first-party-ef-stores-retry-in-bounded-application-loops.md) bounded EF retry | Amend | The kernel's commit classifies failures itself (reset, retry as is, park); 0074 keeps governing other stores |
| [0078](0078-workflow-executions-are-virtual-actors-and-cluster-membership-is-a-foundation-contract.md) virtual actors, membership | Amend | Virtual actors stay; ownership is per partition under a fence. How the membership contract maps to the built-in runtime's node rows is settled in the runtime slice |
| [0080](0080-elsa-4-simplification-decisions.md) simplification | Amend | D2's modes keep their names on turn boundaries; D0 gains the kernel program's registry row |
| [0094](0094-agent-definitions-own-authored-content-and-providers-publish-activities.md) agent content | Amend, terminology | "Agent turn" becomes "agent request" when agents run on the kernel, since a turn is the kernel's term |
| [0052](0052-execution-evidence-is-checkpoint-atomic-and-at-least-once-delivered.md)–[0062](0062-execution-evidence-starts-in-memory-and-adds-groundwork-durability.md) execution evidence | Keep, revisit | Proposed and parked; checkpoint-atomic capture maps to journal entries in the turn's commit when the program resumes |
| 0087 ETL, 0088 Rules, 0095 Decisions | Keep, revisit | Domain ownership holds; each runs as an execution model or job on the kernel when built |
| 0063 BPMN library, 0064 Flowchart dead paths, 0086 compensation | Keep | The kernel hosts Bpmn.Semantics; the graph engine keeps dead-path joins; compensation semantics are the models' |
| 0027, 0028, 0045, 0046, 0048, 0089, 0090 variables and value flow | Keep | Layer 3 native-model semantics; a step's unit of work commits with its turn |
| 0043 publication slots, 0038–0040 artifacts, 0077 schema upgrade, 0079 host package sets | Keep | Definition, artifact and hosting rules; the route table is built from publications, and F14 withholding fits 0077 |
| 0097 multi-tenancy (PR #4156, open) | Check when it lands | Its scope must fit the kernel's tenant column and tenant checks on sends |

Every other ADR concerns authoring, Studio, APIs, identity, packaging or the Extension Builder, and the kernel does not
touch it. ADRs 0030, 0041, 0042, 0065, 0069 and 0072 are already superseded.

## References

- [Kernel decision register](../architecture/decisions.md) and [architecture references](../architecture/README.md).
- [Design sketch, draft 5](../reports/clean-slate-kernel/design-sketch-draft-5.md) and the
  [walkthrough](https://vowful-mustard-pd8t.here.now/).
- [Program plan, coexistence, retirement and rollback](../program-goals/clean-slate-kernel.md).
- Prototype: [README](../../prototypes/kernel-model/README.md), [DESIGN](../../prototypes/kernel-model/DESIGN.md),
  [FINDINGS](../../prototypes/kernel-model/FINDINGS.md), [NATIVE](../../prototypes/kernel-model/NATIVE.md),
  [ENGINES](../../prototypes/kernel-model/ENGINES.md), [MONGO](../../prototypes/kernel-model/MONGO.md),
  [WRITES](../../prototypes/kernel-model/WRITES.md), [COMMIT-SQL](../../prototypes/kernel-model/COMMIT-SQL.md),
  [PARTITIONS](../../prototypes/kernel-model/PARTITIONS.md).
