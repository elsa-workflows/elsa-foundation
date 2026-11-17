# Clean-slate execution kernel

- **Status:** Active program, recorded in [ADR 0080 D0](../adr/0080-elsa-4-simplification-decisions.md#d0--focus) on
  17 November 2026; this plan is proposed with the design. Owner goal of 12 November 2026 (build the kernel, prove it,
  get the design approved, ship release 1, switch, delete the old core). Gate 2, design approval, is open; this plan
  authorizes no product code before it.
- **Area:** The execution core: actor runtime, durable actors, the workflow instance actor and its execution models,
  storage providers, the API read path and Studio's run views.
- **Steward:** Sipke.
- **Program:** [#4229](https://github.com/elsa-workflows/elsa-foundation/issues/4229), with its five gates: prove the
  kernel; approve the design; ship release 1; switch Elsa's default; remove the old core.
- **Decisions:** [ADR 0099](../adr/0099-clean-slate-execution-kernel.md) and the
  [kernel decision register](../architecture/decisions.md). Detail: [architecture references](../architecture/README.md).

## Purpose and scope

Replace Elsa 4's runtime core with the kernel that the prototype (`prototypes/kernel-model/`) proved, at the release 1
scope of [ADR 0099 section 10](../adr/0099-clean-slate-execution-kernel.md#10-release-1-scope). Out of scope for this
plan: the ADR's non-goals for release 1, and anything not named there.

## How the work lands

- One concern per PR, about 500 changed lines or fewer, stacked where they depend on each other. Mechanical moves and
  renames go in their own PRs.
- Product slices land behind a host switch. The old core stays the default until gate 4.
- **Review tiers** (owner, 16 November): a change to kernel behaviour (commit, fence, ownership, delivery, timers,
  recovery) needs two independent reviews; tests, docs, CI and tooling need a self-review plus one.
- Each PR names what it changes, what it does not, the gates it ran with their output, and which claims were traced and
  which were run. No PR loosens an invariant or raises a budget without saying so; a raised budget is an owner
  decision.
- Proof: the store and runtime conformance suites on every engine, the simulation and its mutations, process-kill and
  two-writer tests on the commit, and deterministic command and round-trip budgets. No timing gate.

## Milestones

Each milestone closes when its acceptance holds on main. K1 to K12 are gate 3 (release 1); K13 is gate 4; K14 is
gate 5.

| # | Milestone | Acceptance |
|---|---|---|
| K0 | **Design approval** (gate 2) | ADR 0099, the register, the seven answers and the supersede list reviewed by the independent architecture review; every material finding resolved; the owner's delegated approval recorded on #4229 |
| K1 | **Worker refactor to range ownership** (two reviews) | The prototype's Worker owns ranges of the 1,024 virtual partitions, with a fence row and a journal per virtual partition and `VirtualPartitions` at 1,024; rebalance moves the fewest partitions, so a node's partitions may be scattered; the simulation at both sizes, the mutations and store conformance pass, with take, beat, takeover and rebalance costs measured in the integrated Worker for both scattered and contiguous ownership, against [PARTITIONS.md](../../prototypes/kernel-model/PARTITIONS.md)'s experiment figures; a witness moves a subset of virtual partitions to another owner while a commit spanning several of them is outstanding, and shows that one stale fence rolls the whole flush back, with no journal hole and no stranded seen or trial mark in the virtual partitions the old owner keeps. It comes right after design approval and before product wiring (owner, 17 November) |
| K2 | **Contracts package and storage contract** | Layer 3's contracts package (turn contract, the four seams, read models) and the store contract with the in-memory store; an architecture guard fails any layer 3 reference to layers 1 and 2; the store conformance suite passes on the in-memory store. A project-reference guard cannot catch a partition property moved into the contracts package, so representative activities, the native model, a second model and the runtime API compile against the public contracts alone, and a test feeds them opaque addresses that carry no parseable partition. Today the prototype exposes `InstanceRef.Partition` through `ITurn` and the native HTTP adapter builds an address from a partition (`Contracts.cs` lines 8 and 75–77, `Addresses.cs` lines 9–25, `Live.cs` line 100); K2 removes both from layer 3's surface |
| K3 | **Store changes the answer pages flag** (each its own PR, two reviews) | See "Store and kernel changes" below. Each lands as an executable contract on the prototype or the in-memory store: its conformance cases, and a simulation invariant or mutation that fails without it. Physical-provider acceptance of the same cases closes with K4 |
| K4 | **Providers** | EF Core schema and migrations with the owned commit SQL on all four engines; store conformance in both fence modes and killed-connection stress on each relational engine; MongoDB conformance in CI on a single-node replica set; production MongoDB (1.1) first needs a three-member replica set's primary-loss and uncertain-commit qualification. The K3 cases pass on every engine. Every process that reads or writes the kernel's schema and protocol families checks their versions and refuses before processing a version it cannot read (see "Kernel schema and protocol compatibility"). **Tuned** (PostgreSQL, SQL Server, SQLite) means: the owned commit is the default, and deterministic round-trip budgets per reference workflow hold at or below the prototype's owned figures in [COMMIT-SQL.md](../../prototypes/kernel-model/COMMIT-SQL.md#round-trips-the-cost-runs-ef--owned) (ten no-ops, Balanced, on the owner: PostgreSQL 2, SQL Server 3, SQLite 3 engine calls), with PostgreSQL's operations note published. **Supported** (MySQL) means conformance and stress in CI, with its budgets recorded and not tuned |
| K5 | **Durable layer, built-in runtime, profiles** | Embedded and Cluster; 1,024 virtual partitions; the wake signal; the runtime conformance suite; graceful stop ends owned work before persistence is disposed (#3939's row) |
| K6 | **Workflow instance actor and native model** | Structured containers on an adapted activity context; the parked-child registry and notice change (large fan-out decision 5); the effect key wired into the built-in HTTP and email activities; M1–M3 controls of #2531's hand-off kept or ported. The in-process job contract picks one durable outcome for the right ticket generation in each race: a `StartJob` timeout against the job's completion, in both orders and across a takeover; and Park then recover against a late result from the old ticket generation |
| K7 | **Graph engine and Flowchart; StateMachine** | The Flowchart conformance corpus and invariants G1–G5; StateMachine invariants S1–S4; both in Embedded and Cluster |
| K8 | **Synchronous HTTP** | Every case of the sketch's [synchronous HTTP](../reports/clean-slate-kernel/design-sketch-draft-5.md#synchronous-http-requests) section, including a resume owned by another node and an owner lost while a request waits, under the HTTP error and timeout contract below. Its tests are separate from the durable send obligations, because an HTTP response is never recorded |
| K9 | **Projector, read models and the API read path** | The instance list, parked-work and plan-outcome read models; the viewer's tail read and catch-up; the runtime API on read models only |
| K10 | **Studio on the projector** | Run views on the projections, the tail read, catch-up on request and the live stream of committed entries; composed Studio journeys pass on both profiles |
| K11 | **Operations** | Trace context and metrics as the observability page lists, including the `elsa.kernel.drain.held` gauge that case M3-13 reads; admission and the ingress threshold; retention and delete; simple migration with plans, dispatched by creation wherever creation headers exist, with id-only fallback outcomes marked in the plan-outcomes read model and shown to the operator; parked-work inspect, retry and discard; values by reference |
| K12 | **Qualification** | See "Release 1 qualification" below |
| K13 | **Default switch** (gate 4) | **Release acceptance:** K1 to K12 accepted on main, each gate 3 row below with its evidence posted on #4229, and no open review finding at Minor or above on a merged release 1 PR. Then the switch PR makes the kernel the default (see "The switch"), the docs at the switch land, and the owner gives the go |
| K14 | **Old-core removal** (gate 5) | The retirement inventory below, taken on the removal PR's base, shown to the owner and approved |

### Store and kernel changes the answer pages flag

Each changes kernel behaviour, so each needs two independent reviews when built.

| Change | Page |
|---|---|
| A `backlog` column on the fence row, written by commit and beat | [Fairness](../architecture/fairness-and-backpressure.md) 2 |
| The send cap checked when a turn's outcome is sealed | Fairness 3 |
| Park keeps by reason and by full identity: `KeptBy` holds the row's kept reason (`Consumed` or `Arrived`); the delivery handle `(Id, Scope, Match)` names a kept row, encoded and decoded in layer 2 only; Park journals at every history level | [Observability](../architecture/observability.md#what-park-keeps) 2, B2 |
| The restore transition that clears `Done` on a restored row and resets its poison counters; the sweep's predicate excludes kept rows, with an index on `(Target, Seq)` partial on `KeptBy IS NOT NULL` where the engine supports it (MySQL takes the full index) | [Observability](../architecture/observability.md#recover), M1 |
| `ParkRecord.Resolution`: its cursor on `(Seq, Id, Scope, Match)`, the recorded model end, and the handle of the previous page's last restored row that the progress gate reads; rows above a recover drain's cursor are withheld | [Observability](../architecture/observability.md#resolving-a-park-a-bounded-drain), M3 |
| Every resolution (recover, discard, the window's expiry as an automatic discard, terminate, and delete of a parked instance) runs as a bounded drain under that cursor, with one final notice to a waiting parent; the control kinds `kernel.drain` and `kernel.park-expiry` pass withholding, are never suspects and are exempt from the poison check; the consume rule keeps no arrival once a resolution opens, and the interruption rules resume keeping; the recovery ticket generation for jobs | Observability 1 and 2, M1, M3 |
| A paged read of an instance's kept rows after a cursor, and point lookups by full key, read through `IInboxInspection.KeptAsync` (case M3-12 at K9 and K11) | Observability |
| The `trace` column on messages and its propagation at every send | Observability |
| Lifecycle entries at every history level (State and Minimal write more), as one rule with Park's journalling | [Data lifecycle](../architecture/data-lifecycle.md) 1 |
| Payload-free done markers, with Park's kept deliveries excepted, after their readers are checked | Data lifecycle 7 |
| The journal envelope and codec headers on every payload | Data lifecycle 6 and format item 2 |
| Value rows in the same fenced batch, removed by the instance's delete | [Write volume](../architecture/write-volume.md) 1 |
| The command-kind seam (`IActorType.IsCommand`) for `Migrate` | [Migration](../architecture/migrating-running-instances.md) |
| Persisted creation headers on every provider: a creation is unique per tenant and id across deletes, with the retirement marker keeping the creation high-water mark for every id kind (caller keys included) and never compacted in release 1. Creation gates deletion and replaces migration's id-only fallback | [Data lifecycle](../architecture/data-lifecycle.md), B1; Migration |
| The terminal mark, a column pair (revision and time of the run's last terminal transition), persisted on every provider | Data lifecycle, B1 |
| The qualified delete: every delete names tenant, creation and terminal mark with a reason, and the owner checks tenant, creation, mark, the policy in force for Retention, and `ProtectsDeletion` before deleting; an ambiguous selection is refused; there is no `Force` (data lifecycle decision 9). `kernel.terminate` with `ThenDelete` | Data lifecycle, B1 |
| The tenant contract: a `tenant` column on every tenant-keyed record, checked by the owner on load and on consumption; `Q(t, n)` at every ingress and in the keyed-request hash; tenant-qualified correlation keys; read-model indexes led by tenant; the tenant-id grammar checked where the `BoundScope` is created | [Tenancy](../architecture/tenancy.md#release-1), M2 |
| The collation pin on SQL Server and MySQL: every column that holds a tenant id, an actor id or a tenant-qualified name gets the pinned collation in the schema and the migrations, and every expression that compares with, joins to, partitions by or hashes a pinned column, and every kernel routine's string parameters and variables, uses it. The three known prototype sites are not carried over: `EfStore.LoadActorsAsync`'s alias lookup and the SQL Server `EventInsert` CHECKSUM stripe (both `DATABASE_DEFAULT`), and MySQL's `kernel_insert_event` (`wire` without `COLLATE`) | Tenancy decision 6, M2 |
| A turn budget per pass (`PassTurnBudget`) | [Fairness](../architecture/fairness-and-backpressure.md#fair-scheduling-within-a-pass), release 1 |
| Deficit round robin by tenant within a pass, with the tenant column on the instance row | Fairness, release 1 |
| The age hint that `RunAsync` reports to layer 1 (a change to the seam between layers 1 and 2) | Fairness, release 1 |
| The `(partition, done, due)` index, partial on `done = false`, for the capped due-depth count | Fairness, release 1 |
| `StartJob`'s optional timeout, as a timer message in the same commit, recalled when the result arrives | [Job workers](../architecture/job-workers.md#release-1) |
| The job ticket carrying the effect key and the attempt id as opaque strings | Job workers, release 1 |

After release 1, also kernel behaviour: compaction of non-positive journal rows below the projector's cursor (data
lifecycle decision 8), and a forced handover from an unresponsive owner (observability).

## Release 1 qualification

Gate 3 takes on #2531's M5 rows ([hand-off](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6917342686),
[two more rows](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6917418143)):

1. **Integrated qualification:** representative expressions and payloads, sustained arrivals, owner crash and
   takeover, duplicate commands and database interruption, each with a reproducible outcome. Measurements name the
   model, provider and machine topology; several processes on one host are not independent-machine evidence.
2. **Budgets:** ten WriteLines under Balanced in **32 SQLite commands or fewer** and **16 PostgreSQL round trips or
   fewer**, as deterministic product budgets. The prototype measures 3 commands and 2 or 3 round trips
   ([NATIVE.md](../../prototypes/kernel-model/NATIVE.md#5-what-it-costs)).
3. **Layer 3 semantics that carry over:** weak hydration metadata, prepared JavaScript, conversion preparation and
   demand-driven inputs, with their tests kept or ported.
4. **M1 controls:** a start completes under a clock that moves backwards; generated and caller-supplied ids stay
   distinct under concurrent starts.
5. **Inputs:** #3915's payload and topology profile design.
6. **Graceful stop** (#3939): a stop ends all owned work, off-turn work included, before persistence is disposed.
7. **Uncertain commits** (#3698): a landed commit is never reported as refused, including after a late lease loss or an
   ambiguous COMMIT.

8. **Schema compatibility rehearsal** (gate 2 review, m4): see "Kernel schema and protocol compatibility" below.

Also before release, from the answer pages: the `pad=` PostgreSQL run that accepts values by reference; the
state-size benchmark that confirms or lowers the ForEach cap; a write-volume run on the final schema; the two-tenant
stress test with admission on and off; an ownership-traffic run (takes, beats, takeovers and rebalance under load);
and a retention and recovery load run. No capacity conclusion is drawn before these runs. No Azure or multi-machine
spend without the owner's approval.

## The HTTP error and timeout contract (K8, proposed)

A response is live output on the `Emit` channel. It is held in memory until the commit of the turn that wrote it
lands, then released; a route may opt out and release before the commit. It is never a durable obligation: no row
records it, and nothing retries it. Built and run in the prototype except where marked
([NATIVE.md](../../prototypes/kernel-model/NATIVE.md), section 4):

| Case | The caller receives |
|---|---|
| The turn's commit lands, the response is released | The response, once |
| The turn that wrote the response writes nothing else, so it has no commit (Ephemeral, or nothing to write) | The response, at once (run: NATIVE.md section 4, case 5) |
| The owner resets on the receiving node while the request waits | 503 at once (run) |
| The owning process dies after `COMMIT` and before the release, and it is also the receiving node | A dropped connection, with the process (reasoned) |
| The owner is on another node and is lost before its turn commits | The delivery keeps `ReplyTo` on its durable row (`ReplyTo` in `EfStore.cs`, written by the owned insert in `EfStore.Owned.cs`). The new owner runs the turn again; if that commit lands within the reply deadline, the response goes to the reply address, otherwise the caller gets 504 at the deadline (reasoned: the field is built, the takeover path is not run end to end) |
| The owner is on another node and is lost after its turn commits, before the release | 504 at the reply deadline, the route's timeout (reasoned: no signal from the owner reaches the receiving node) |
| The turn that wrote the response parks | 500 at once; the held response is dropped (owner, 17 November) |
| The client disconnects or times out | Nothing; the workflow is not cancelled. A route option that maps a disconnect to a cancel is proposed and not built (NATIVE.md section 4, "Not done") |

In every case the workflow's committed state is unaffected and continues under its owner. A turn that runs again
before the reply deadline answers the waiting caller through `ReplyTo`; output after the caller has had its answer, or
after the deadline, reaches no caller. A caller never receives two responses. For a turn that writes, the caller never
receives a response whose commit did not land, unless the route opted out.

## Kernel schema and protocol compatibility (proposed)

[ADR 0077](../adr/0077-a-module-upgrades-in-place-only-when-its-persisted-schema-is-unchanged.md) requires
coordination for a schema-family change. F14 withholding protects model state versions only; it gives a sweeper or a
projector no way to read a new durable marker. The kernel's families:

| Family | What it covers | Writers | Readers |
|---|---|---|---|
| Store schema | Fence and partition rows, node rows, actor rows, message rows (with `KeptBy`, `trace` and codec columns), subscriptions, events, journal, projection cursor, value rows | EF migrations; owned commit SQL; take and beat; sweeper (deletes); projector (views and cursor) | Take, poll, load, owned commit SQL, sweeper, projector, viewer tail read, read-model APIs |
| Message protocol | Kernel message kinds (control kinds, tickets, recover, discard, migrate), derived message-id grammar, the `Done`, `keep_until` and `KeptBy` meanings, ticket generations | Turns through owned SQL; API and ingress adds | Worker delivery and poison handling; sweeper; recover |
| Journal and payload codec | The journal envelope and the codec header on every payload | Owned commit SQL | Projector, viewer, retention worker through read models |

**The rule.** The store records one version per family. Before a take, a sweep or a projector round, a process
checks that each stored version is in the set of versions it can read, and refuses, processing nothing and reporting
the versions, when one is not: a version it does not read is refused whether it is higher or lower. Readers that hold no ownership, the viewer tail read and the read-model APIs, compare the versions when they
start and refuse to serve while a family version is above theirs. In release 1, a change that a running version cannot
read is a coordinated stop: stop every node, migrate, start the new version (owner, 17 November). The coordinated stop
is the only guard for an owner that already holds partitions: it does not re-check before each poll or commit. To make
a missed stop visible, every node's heartbeat row carries the family versions it reads, and the migration that raises
a family version refuses while any live node row reports it cannot read the new one.

**The rollback floor** (accepted by the owner, 17 November) is the oldest kernel version that reads all of the store's
current family versions. Each release's notes state it. A version below the floor refuses; going below it needs a database restored from
before the upgrade. Before release no user data exists, so developers regenerate their databases instead (K13).

**The rehearsal (K12).** Upgrade one node to a build that raises a family version (a new codec header or a new
`KeptBy` meaning) and let it write. Then start a pre-upgrade owner, sweeper and projector against the same store: each
must refuse before it processes any row, and the upgraded node keeps running. Then start an old-version owner that
already holds partitions, and run the migration that raises a family version: the migration must refuse while that
owner's node row is live. Record the floor this produces.

## Gate 2 review: counterexamples and answers

The [independent architecture review](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6949612272)
raised four Minors and plan recommendations that this plan and ADR 0099 answer. B1 is answered on
[data lifecycle](../architecture/data-lifecycle.md), B2, M1 and M3 on
[observability](../architecture/observability.md#counterexamples-and-answers), and M2 on
[tenancy](../architecture/tenancy.md#counterexample-and-answer); their cases are listed under "Acceptance cases from
the pages" below.

| Finding | Counterexample | Answer |
|---|---|---|
| m1 | The default owned SQL Server provider is budgeted with a checked fence from the stale reference | [Ownership and fencing](../architecture/ownership-and-fencing.md#a-take-racing-a-stale-commit) gives both paths: owned in the batch, EF checked, each with its lock order, and E9 as the evidence |
| m2 | The process dies after `COMMIT` and before the HTTP release; the workflow advances while the caller times out, yet ADR 0099 called the response a recorded obligation | ADR 0099's 0020 row now says the response is held in memory and never recorded; the contract above gives the caller's answer in each case, tested at K8 apart from durable sends |
| m3 | A receiver applies a call, the owner is lost before the outcome commits, and the next owner calls again, even in Durable | The [register](../architecture/decisions.md#kernel-defaults) now requires receiver deduplication by the effect key or reconciliation, and says what Durable's attempt checkpoint records |
| m4 | A host writes a new codec or `KeptBy` meaning, then a pre-upgrade owner or projector takes over | The families, the refusal rule and the rollback floor above; the refusal checks at K4 and the rehearsal at K12 |
| Plan | Experiment costs, a contracts package that still exposes partitions, a K3 that depends on K4's providers, unproven job races, single-node Mongo evidence, silent migration fallback | K1's scattered and contiguous costs and the stale multi-VP flush witness; K2's opaque-address compile test; K3 staged on the prototype and closed at K4; K6's job races; K4's three-member Mongo condition for 1.1; K11's marked fallback outcomes |

### Acceptance cases

| Case | Milestone |
|---|---|
| Take, beat, takeover and rebalance costs, measured for scattered and for contiguous ownership | K1 |
| Move a subset of virtual partitions during an outstanding multi-VP commit: the stale fence rolls back the whole flush; no journal hole, no stranded seen or trial mark in the retained partitions | K1 |
| Representative activities, the native model, a second model and the runtime API compile against the contracts package alone and run on opaque addresses with no parseable partition | K2 |
| Each K3 change passes its conformance cases and its simulation invariant or mutation on the prototype or in-memory store | K3 |
| The same K3 cases pass on PostgreSQL, SQL Server, SQLite, MySQL and MongoDB | K4 |
| On SQL Server, the owned in-batch fence and the EF checked fence each hold under the 40-second stress with no take deadlock | K4 |
| A process refuses before processing a store whose family version it cannot read: owner take, sweeper and projector, each on its own | K4 |
| A `StartJob` timeout against the job's completion, in both orders and across a takeover, selects one durable outcome for the current ticket generation | K6 |
| Park, then recover, against a late result from the old ticket generation: the late result is dropped and the new generation's outcome applies once | K6 |
| Each row of the HTTP error and timeout contract, including a process killed between `COMMIT` and the release, with the caller's answer and the workflow's committed state checked | K8 |
| A remote owner lost before its turn commits: the new owner's re-run lands within the reply deadline and answers through `ReplyTo`; and the same race where it lands after the deadline, where the caller gets 504 and no second response | K8 |
| A plan run on a mix of creation-qualified and creation-0 rows: the fallback outcomes are marked id-only and listed apart in the operator API | K11 |
| The mixed-version rehearsal: an upgraded node raises a family version; a pre-upgrade owner, sweeper and projector each refuse before processing; the floor is recorded | K12 |
| A stored family version below the reader's readable set is refused, as a higher one is | K4 |
| With an old-version owner holding partitions, the migration that raises a family version refuses while its node row is live | K12 |
| The k-row fence locks in ascending id on every engine; two overlapping range commits on SQL Server, PostgreSQL and MySQL complete with no deadlock | K1 (prototype), K4 (providers) |
| MongoDB on a three-member replica set: primary loss and an uncertain commit, before production support | After release 1 (1.1) |

### Acceptance cases from the pages

Each page defines its cases; this table places every case id under each milestone the page gives it, without
repeating the case. A case tagged K3 and K4 runs first on the in-memory store and the simulation, then on every
provider.

| Milestone | Cases |
|---|---|
| K2 | [Tenancy](../architecture/tenancy.md#acceptance-cases): T1 |
| K3 | [Data lifecycle](../architecture/data-lifecycle.md#acceptance-cases): RD1, RD2, RD3, RD4, RD4b, RD5, RD6, RD7, RD7b, RD8c; [Migration](../architecture/migrating-running-instances.md#acceptance-cases): MP1, MP2; [Observability](../architecture/observability.md#acceptance-cases): B2-1, B2-2, B2-3, B2-4, B2-5, B2-6, M1-1, M1-2, M1-3, M1-4, M1-5, M3-1, M3-2, M3-4, M3-5, M3-6, M3-9, M3-10, M3-11, M3-13; [Tenancy](../architecture/tenancy.md#acceptance-cases): T2, T3, T4, T7, T11, T13, T16, T17 |
| K4 | [Data lifecycle](../architecture/data-lifecycle.md#acceptance-cases): RD1, RD2, RD5, RD6, RD7, RD7b; [Migration](../architecture/migrating-running-instances.md#acceptance-cases): MP2, MP4; [Observability](../architecture/observability.md#acceptance-cases): B2-1, B2-2, B2-3, B2-4, M1-1, M1-2, M3-2, M3-7, M3-8, M3-9; [Tenancy](../architecture/tenancy.md#acceptance-cases): T2, T3, T4, T15, T16, T17 |
| K5 | [Tenancy](../architecture/tenancy.md#acceptance-cases): T12 |
| K6 | [Observability](../architecture/observability.md#acceptance-cases): M3-6, M3-10, R-1; [Tenancy](../architecture/tenancy.md#acceptance-cases): T5, T7 |
| K7 | [Observability](../architecture/observability.md#acceptance-cases): M3-13, R-1 |
| K8 | [Tenancy](../architecture/tenancy.md#acceptance-cases): T8 |
| K9 | [Data lifecycle](../architecture/data-lifecycle.md#acceptance-cases): RD10; [Observability](../architecture/observability.md#acceptance-cases): M3-12, R-2; [Tenancy](../architecture/tenancy.md#acceptance-cases): T6 |
| K11 | [Data lifecycle](../architecture/data-lifecycle.md#acceptance-cases): RD3, RD4, RD4b, RD8, RD8b, RD8c, RD9; [Migration](../architecture/migrating-running-instances.md#acceptance-cases): MP1, MP3; [Observability](../architecture/observability.md#acceptance-cases): B2-5, M1-3, M3-12, R-2; [Tenancy](../architecture/tenancy.md#acceptance-cases): T9, T10, T11, T12 |
| K12 | [Migration](../architecture/migrating-running-instances.md#acceptance-cases): MP4; [Observability](../architecture/observability.md#acceptance-cases): M3-1, M3-3, M3-5, M3-11, M3-13; [Tenancy](../architecture/tenancy.md#acceptance-cases): T14 |

## The switch

**The basis** (owner, 17 November): the old core is unreleased software with no user data. Its databases are local
ones on developers' machines. So there are no live runs to carry over, no data migration and no old tables to keep:
"we'll simply delete the database and have it regenerated."

- Until K13 the kernel is built in its own packages behind a host switch, with the old core the default. The two never
  read each other's tables.
- The switch PR makes the kernel the default. Developers delete their local databases and let the kernel's migrations
  create them again.
- **Rollback** is a revert of the switch PR, followed by the same database reset. There is no data rollback, because
  no user data exists.

## Owner answers of 17 November

- **Rebalance strategy** ([PARTITIONS.md](../../prototypes/kernel-model/PARTITIONS.md#the-owners-decisions-sipke-16-november-2026), decision 4): fewest
  moves. A node's partitions may be scattered, which costs nothing with a fence per partition.
- **The Worker refactor** (PARTITIONS.md, decision 5): range ownership of the 1,024 virtual partitions, with a journal
  per virtual partition, is release 1's first milestone (K1), right after design approval and before product wiring.
  It is kernel behaviour, so two reviews.
- **Unified Execution** [#2892](unified-execution.md) was closed on 14 November, superseded by the kernel; no
  decision was needed.

## Retirement (proposed)

**The inventory method.** The deletion list comes from a fresh inventory taken on the removal PR's base, never from historical
counts. The 18,920 files, 513 projects and 2,583 references recorded earlier in the program make nothing eligible for
deletion. The inventory takes the project-reference graph and the namespaces of the old core's engine (scheduler,
drainer, checkpoint commit stores, runtime EF contexts and their migrations, and tests bound to those internals), and
classifies each item as delete, port (layer 3 code and tests that carry over) or keep. It also lists every reader of
old-core code (projects, tests, docs, samples, CI workflows) so that none is left pointing at deleted code. **The owner
sees the list before anything is deleted.**

**Docs to rewrite or retire at the switch** (the sketch's
[architecture documentation](../reports/clean-slate-kernel/design-sketch-draft-5.md#architecture-documentation-owner-decision-12-november)
list; the owner sees it first):

| Page | Proposed disposition |
|---|---|
| [architecture-tour.md](../architecture-tour.md) | Rewrite on the three layers |
| [how-a-workflow-executes.md](../how-a-workflow-executes.md) | Rewrite as a turn, a commit and the projector |
| [runtime-durable-resumption.md](../runtime-durable-resumption.md) | Retire; resumption is a message to the instance |
| [runtime-durable-timers.md](../runtime-durable-timers.md) | Retire; timers are messages with a due time |
| [bookmark-expiration.md](../bookmark-expiration.md) | Rewrite or retire after K6 shows how waits expire |
| [runtime-fault-behavior.md](../runtime-fault-behavior.md) | Rewrite on Park, recovery and intervention |

The fresh inventory adds any other page that describes the old core (candidates: [seams.md](../seams.md),
[serialization.md](../serialization.md), [user-tasks.md](../user-tasks.md), which name its runtime packages). At the
switch the architecture references change from Proposed to Current, and a shorter user-facing section is published
under `website/docs/`.

**ADRs.** ADR 0099's list of ADRs it supersedes, amends or keeps; each status changes at the switch, after the owner
has seen the list.

**Issues and PRs.** #4176 (ADR 0098) is closed as superseded after the owner sees it; #2564 and #4043 were closed
on 16 November.
