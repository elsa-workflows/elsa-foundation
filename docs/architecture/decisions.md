# Kernel decision register

> **Proposed.** The kernel design awaits approval. This register lists every owner decision that binds the clean-slate
> kernel's design, grouped by topic, with its date, a one-line rule and its source. The pages hold the detail; the
> proposal ADR links here. A decision changes only by a new owner decision, recorded here with its date.

Decisions are by Sipke Schoorstra, the owner, for [program #4229](https://github.com/elsa-workflows/elsa-foundation/issues/4229).
"Sketch" is the [design sketch, draft 5](../reports/clean-slate-kernel/design-sketch-draft-5.md). A source marked
"owner card" was given in the coordinator's session and has no earlier public record; this register is its first.

## Goal and principles

| Date | Rule | Source |
|---|---|---|
| 12 Nov | Rethink the core from a clean slate. Unreleased code: any API and any ADR about the current architecture may change. | [Sketch, requirements](../reports/clean-slate-kernel/design-sketch-draft-5.md#requirements-the-owners-12-november) |
| 12 Nov | Persistence is an abstraction. EF Core (SQLite, PostgreSQL, SQL Server, MySQL) is one provider; a document store such as MongoDB must be possible. | Sketch, requirements |
| 12 Nov | A workflow can handle an HTTP request and produce the response itself (synchronous HTTP), including a resume on another node. | [Sketch, synchronous HTTP](../reports/clean-slate-kernel/design-sketch-draft-5.md#synchronous-http-requests) |
| 12 Nov | Optimise for correctness, then database calls per workflow, then latency, then flexibility. | [Sketch, decisions](../reports/clean-slate-kernel/design-sketch-draft-5.md#decisions-taken-so-far-12-november) |
| 15 Nov | Replace whole layers and edges, never pieces of the commit. Timers, messages, events and subscriptions stay inside the fenced atomic commit; brokers and schedulers are ingress adapters. | [Sketch, extensibility boundary](../reports/clean-slate-kernel/design-sketch-draft-5.md#extensibility-boundary-owner-principle-15-november) |
| 15 Nov | A send is recorded atomically with the sender's state and may be delivered later, deduplicated by derived message id. | [Sketch, the commit](../reports/clean-slate-kernel/design-sketch-draft-5.md#the-commit); [Log paper](../reports/clean-slate-kernel/log-profile-contract-sketch.md) |
| 16 Nov | Layer 3 stays host-neutral: execution models, activities, Studio and operator APIs depend only on the turn contract, the four logical seams (commit, ownership, delivery, read models) and read models; never on partitions, positions or SQL. | [#4229](https://github.com/elsa-workflows/elsa-foundation/issues/4229) program state ("Adopted 16 November") |
| 16 Nov | After release 1, layers 1 and 2 become a separable built-in default that users may replace with their own actor stack; release 1 protects this with a contracts package that layer 3 builds against. | Owner card |

## Layers, runtime and profiles

| Date | Rule | Source |
|---|---|---|
| 12 Nov | Three layers: actor runtime, durable actors, actor types. A workflow instance is one actor type running an execution model. | [Sketch, three layers](../reports/clean-slate-kernel/design-sketch-draft-5.md#three-layers) |
| 12 Nov | Runtimes host partitions only; Orleans hosts each partition as a grain. One grain per instance, its fence and the sweep are dropped. | [Sketch, the seam](../reports/clean-slate-kernel/design-sketch-draft-5.md#the-seam-between-layers-1-and-2) |
| 12 Nov | One public runtime seam with a runtime conformance suite; an Orleans reference adapter kept in tests; adapters shipped on demand. | Sketch, the seam |
| 12 Nov | No "runtimes" plug-in. Persistence mode, actor runtime and provider vary independently; deployment profiles (Embedded, Cluster, Orleans, Log later) are presets. No capability flags. | [Sketch, profiles](../reports/clean-slate-kernel/design-sketch-draft-5.md#deployment-profiles-not-runtimes) |
| 12 Nov | Inline execution is a per-workflow or per-route choice; inline plus Ephemeral costs no database call. | Sketch, profiles |
| 12 Nov | A resume does not move its instance to the receiving node; the one hop to the owner stays unless measurements show it matters. | Owner card (deployment profiles, 12 November) |
| 12 Nov | Wake-ups between nodes are a pluggable hint over the runtime's transport; polling is the guarantee. | Sketch, decisions (walkthrough 3) |
| 12 Nov | Names: an instance's turn, a background job, a runtime pass. | [Sketch, header](../reports/clean-slate-kernel/design-sketch-draft-5.md) |
| 15 Nov | The instance table is an actor table, with an explicit `type` column beside `model` and only actor-generic columns; each actor type has its own read models. | Owner card |

## Kernel defaults

| Date | Rule | Source |
|---|---|---|
| 12 Nov | Owner default 1, at-least-once side effects, in the sketch's recorded wording (decision 1): side effects are at least once in every mode, and idempotency keys are documented. Durable narrows the window: before an External activity runs, a turn of its own commits its attempt (journal entry `a:frame:1`, the attempt segment of the effect key), so a later owner knows an attempt began. No mode prevents a repeat after a crash between an effect and its commit: a receiver can apply a call and the owner be lost before the outcome commits, and the next owner calls again under the same effect key. Effects that must not repeat need the receiver to deduplicate by the effect key, or reconciliation. Corrected on 17 November to the sketch's recorded wording, after the gate 2 review (m3); no new rule. | Owner, 12 November (kernel default 1), as recorded in [sketch decision 1](../reports/clean-slate-kernel/design-sketch-draft-5.md#decisions-taken-so-far-12-november); mechanism in [LEASE.md](../../prototypes/kernel-model/LEASE.md) and [NATIVE.md](../../prototypes/kernel-model/NATIVE.md) |
| 12 Nov | History is Full by default; State and Minimal per workflow. | Sketch, decisions |
| 12 Nov | The lease is 10 seconds. | Sketch, decisions |
| 12 Nov | An HTTP response is released after the commit of the turn that wrote it; opt-out per route. | Sketch, decisions |
| 12 Nov | No reads across instances; questions go as messages or through projections. | Sketch, decisions |
| 12 Nov | Events are not kept by default; keeping is per subscription. | Sketch, decisions |
| 16 Nov | 1,024 virtual partitions, with a fence per virtual partition (replaces the 12 November default of 64). | [PARTITIONS.md](../../prototypes/kernel-model/PARTITIONS.md#the-owners-decisions-sipke-16-november-2026) |
| 17 Nov | Rebalance moves the fewest partitions; a node's partitions may be scattered, which costs nothing with a fence per partition (PARTITIONS.md decision 4). | Owner card |
| 17 Nov | The Worker refactor to range ownership of the 1,024 virtual partitions, with a journal per virtual partition, is release 1's first milestone, right after design approval and before product wiring; kernel behaviour, two reviews (PARTITIONS.md decision 5). | Owner card |

## Commit, fence and storage

| Date | Rule | Source |
|---|---|---|
| 12 Nov | The fence goes in the batch by default; the fence as its own statement stays an option. | [Sketch, the commit](../reports/clean-slate-kernel/design-sketch-draft-5.md#the-commit) |
| 13 Nov | SQL Server's default fence is the checked fence, as its own statement (E9); other engines keep the fence in the batch. Narrowed on 16 November to the EF reference path (next rows). | [Sketch, decided 13 November](../reports/clean-slate-kernel/design-sketch-draft-5.md#decided-13-november) |
| 16 Nov | The kernel writes its own commit SQL on PostgreSQL, SQL Server, SQLite and MySQL; EF keeps the schema, migrations and cold paths. The checked fence is one multi-row statement. | [COMMIT-SQL.md](../../prototypes/kernel-model/COMMIT-SQL.md#defaults) |
| 16 Nov | SQL Server's owned commit uses the fence in the batch; the EF reference path keeps the checked fence. | [COMMIT-SQL.md](../../prototypes/kernel-model/COMMIT-SQL.md#defaults) (#4698) |
| 12 Nov | Snapshot plus journal stays the default; a lazy snapshot is the fallback spike if measurements demand it. | [Write volume](write-volume.md) |
| 12 Nov | Large values are stored by reference (decision 10), with a size benchmark in the cost runs. | Sketch, decisions (walkthrough 10) |
| 15 Nov | The message table stays the inbox queue; the fit of a partitioned inbox is reported once virtual partitions change the keys, and it is built only if cheap (round 5 D). The write-volume page proposes keeping it out of release 1. | [#4229 comment](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6887994219); [write volume](write-volume.md) |

## Messages, jobs, events and recovery

| Date | Rule | Source |
|---|---|---|
| 12 Nov | Short waits of an unwritten Ephemeral instance stay in memory up to a threshold. | Sketch, decisions (walkthrough 4) |
| 12 Nov | The subscription and event tables are the kernel's one event mechanism. | Sketch, decisions (walkthrough 5) |
| 12 Nov | A reply address expires at the route's timeout, carried as an absolute deadline. | Sketch, decisions (walkthrough 6) |
| 12 Nov | Recreate after delete: a generated id never comes back; a caller key refuses a repeat within `keep_until`. | Sketch, decisions (walkthrough 7) |
| 12 Nov | Jobs are counted for poison like messages; past the threshold the job parks and its instance faults. | Sketch, decisions (walkthrough 9) |
| 13 Nov | Park faults the instance, consumes its deliveries and removes its subscriptions, keeping state, fault and history. | [Observability](observability.md#terms) |
| 16 Nov | The effect key is `{instance origin}/{node}/{iteration path}/{attempt}`. | [Ownership and fencing](ownership-and-fencing.md#side-effects) (#4676) |

## History and the viewer

| Date | Rule | Source |
|---|---|---|
| 11 Nov | Viewer rows are written after the commit through a durable hand-off, the commit journal. | [Sketch, history](../reports/clean-slate-kernel/design-sketch-draft-5.md#history-and-the-viewer) |
| 12 Nov | The projector runs off the hot path in its own transaction, each round set-based. | Sketch, decisions (walkthrough 8) |
| 12 Nov | The viewer shows projected rows plus the journal tail past the cursor; Studio may request a catch-up; live watching is a push stream of committed entries. | Sketch, decisions (walkthrough 11) |

## Execution models and jobs

| Date | Rule | Source |
|---|---|---|
| 12 Nov | Native keeps one activity tree; structured containers are built in, Flowchart runs on a graph engine extracted from main's Flowchart, StateMachine on a statechart engine as a native composite. | [Sketch, containers](../reports/clean-slate-kernel/design-sketch-draft-5.md#containers-and-their-semantics-owner-decision-12-november) |
| 12 Nov | BPMN is a separate model hosting the valence-works `Bpmn.Semantics` interpreter. | Sketch, containers |
| 12 Nov | One job concept: Elsa.Jobs is rebuilt on the kernel as a job actor type, keeping its public API and dropping its own store and workflow bridge. | [Sketch, standalone jobs](../reports/clean-slate-kernel/design-sketch-draft-5.md#standalone-jobs-elsajobs-on-the-kernel-owner-decision-12-november) |

## Release 1 scope

| Date | Rule | Source |
|---|---|---|
| 12 Nov, amended 15 Nov | In: the kernel and built-in runtime; Embedded and Cluster; Native with structured containers, Flowchart and StateMachine; synchronous HTTP; PostgreSQL, SQL Server and SQLite tuned; MySQL supported; MongoDB in CI only; Studio on the new core; agents as native activities; basic trace context and metrics, retention and delete, admission control, simple migration; parked-work inspect, retry and discard. | [Sketch, release 1](../reports/clean-slate-kernel/design-sketch-draft-5.md#release-1-scope-owner-decision-amended-15-november); [#4229 comment](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6888104085) |
| 15 Nov | Orleans is behind proven demand; Log is planned, not designed; BPMN, ETL and a dedicated agent model come later. | Same |
| 17 Nov | A turn that wrote a held HTTP response and then parks answers the caller 500 at once. | Owner card ([plan, HTTP contract](../program-goals/clean-slate-kernel.md#the-http-error-and-timeout-contract-k8-proposed)) |
| 17 Nov | Kernel schema changes that a running version cannot read use a coordinated stop, and each release's notes state its rollback floor. | Owner card ([plan, schema compatibility](../program-goals/clean-slate-kernel.md#kernel-schema-and-protocol-compatibility-proposed)) |
| 16 Nov | The BPMN prototype stream is parked until after release 1. | [#4229](https://github.com/elsa-workflows/elsa-foundation/issues/4229) program state |
| 16 Nov | #2531's M5 qualification and the budgets of 32 SQLite commands and 16 PostgreSQL round trips (ten WriteLines, Balanced) move to gate 3. | [#4229 comment](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6917342686) |

## The seven design-now answers (16–17 November)

The 31 numbered decisions were taken on 17 November by card; 19 of them were accepted together on their
recommendations, and that card also accepted the migration addressing fallback. The pages carry the rejected options.

| Topic | Rule | Page |
|---|---|---|
| Quotas | No running-instance quotas in release 1: rates and the ingress threshold only. | [Fairness](fairness-and-backpressure.md#decisions-owner-1617-november-2026) 1 |
| Backlog | The backlog estimate goes on the virtual partition's fence row. | Fairness 2 |
| Send cap | A turn above the send cap fails and parks. | Fairness 3 |
| `InboxFull` | Only load-shedding commands (`Suspend`, `Terminate`, `Delete`, discard) are exempt, under an operator rate; `Resume` and retry obey it. | Fairness 4 |
| ForEach cap | A parallel ForEach takes 1,000 items, degree 16 by default and 64 at most; the state-size benchmark confirms or lowers it. | [Large fan-out](large-fan-out.md#decisions-owner-1617-november-2026) 1 |
| Distributed form | An explicit option on ForEach. | Large fan-out 2 |
| Combiner faults | FailFast by default. | Large fan-out 3 |
| Combiner retry | Leaves only. | Large fan-out 4 |
| Parked child | The parent keeps waiting: the notice says "parked", and the registry and `Outstanding` keep the child until a later notice or a discard (a layer 3 change for every waiting parent). | Large fan-out 5 |
| Retention | A retention worker over the instance-list read model, with lifecycle entries at every history level, landed as one rule with Park's journalling. | [Data lifecycle](data-lifecycle.md#decisions-owner-1617-november-2026) 1 |
| Shortened policy | Applies to ended instances, with a count, confirmation above a threshold and a bounded delete rate. | Data lifecycle 2 |
| Erasure | Delete and values by reference now, with the format items fixed; per-instance data keys in 1.x, opt-in per definition. | Data lifecycle 3 |
| Keys | Caller and correlation keys carry no personal data (documented, with a Studio warning). | Data lifecycle 4 |
| Keyed hashes | The `h:` form is reserved; keyed hashes come in 1.x. | Data lifecycle 5 |
| Journal envelope | A clear envelope with generic opaque fields, adopted beside walkthrough decision 12. | Data lifecycle 6 |
| Done markers | Payload-free, with Park's kept deliveries excepted. | Data lifecycle 7 |
| Compaction | Non-positive journal rows below the cursor are compacted after release 1. | Data lifecycle 8 |
| Values by reference | In the same database: a values row per instance and digest in the same fenced batch; an external store later as an adapter. | [Write volume](write-volume.md#decisions-owner-1617-november-2026) 1 |
| Acceptance of decision 10 | The `pad=` PostgreSQL run. | Write volume 2 |
| Tuning | PostgreSQL tuning is an operations note. | Write volume 3 |
| Rollback | As built: newer versions are withheld on older nodes, with a metric. | [Migration](migrating-running-instances.md#decisions-owner-1617-november-2026) 1 |
| Refused instance | Stays running on the source version. | Migration 2 |
| Simple cases | The defined simple-case boundary is release 1's scope. | Migration 3 |
| ADR 0049 | Widened: release 1 migrates running and suspended instances at a turn boundary; ADR 0049 is superseded. | Migration 4 |
| Source definitions | Kept while any instance, ticket or history refers to them. | Migration 5 |
| Addressing fallback | Id-only until creation headers are persisted; the reused-caller-key risk is documented. | Migration, release 1's fallback |
| Standalone Jobs | 1.x, with the remote worker protocol; workflow background jobs ship in release 1; the first-use lookup card is settled in the 1.x spike. | [Job workers](job-workers.md#decisions-owner-1617-november-2026) 1 |
| Heartbeats | In memory over the runtime's transport, with a durable fallback. | Job workers 2 |
| Gateway keys | `{worker}/{request id}` with a `keep_until`. | Job workers 3 |
| Standalone effect key | `{job origin}/job/-/0`. | Job workers 4 |
| Recovery | In place (same id, next revision), plus intervention: faults the model can hold stay Running with a blocking incident. | [Observability](observability.md#decisions-owner-1617-november-2026) 1 |
| Park | Extended: consumed deliveries and arrivals kept as done rows under the `Parked` window, marked by `KeptBy` (non-null only while its Park is unresolved), with `ParkRecord.Consumed` telling Park-consumed rows from later arrivals, journalled at every level; the restore clears `Done`. Discard and delete of a parked child send its parent a final Faulted notice. | Observability 2 |

## Process

| Date | Rule | Source |
|---|---|---|
| 13 Nov | Architecture references live in `docs/architecture/` with a Proposed banner until approval; a shorter published section goes into `website/docs/` at the switch. | [Sketch, architecture documentation](../reports/clean-slate-kernel/design-sketch-draft-5.md#architecture-documentation-owner-decision-12-november) |
| 12 Nov | The owner sees the list before any ADR or old-core page is retired. | Sketch, architecture documentation |
| 15 Nov | The simulation runs permanently in CI: path filters, a fast simulation and mutation PR gate, and the heavy suites nightly (round 5 F, delivered in #4670). | [#4229 comment](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6887994219) |
| 17 Nov | Unified Execution (#2892), closed on 14 November, is recorded as superseded by the kernel; no decision was needed. | Owner card; [#2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892) closed 14 November |
| 17 Nov | The switch has no live runs to handle: the old core is unreleased and holds no user data. | Owner card |
| 17 Nov | No old-core tables are kept: everything is on developers' local databases, which are deleted and regenerated. | Owner card |
| 17 Nov | The ADR 0080 D0 amendment and the program-registry row for the kernel go in now, with the proposal. | Owner card |
| 16 Nov | Kernel behaviour (commit, fence, ownership, delivery, timers, recovery) needs two independent reviews; tests, docs, CI and tooling need a self-review plus one. | [#4229](https://github.com/elsa-workflows/elsa-foundation/issues/4229) program state |
