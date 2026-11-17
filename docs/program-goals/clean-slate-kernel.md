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
| K1 | **Worker refactor to range ownership** (two reviews) | The prototype's Worker owns ranges of the 1,024 virtual partitions, with a fence row and a journal per virtual partition and `VirtualPartitions` at 1,024; rebalance moves the fewest partitions, so a node's partitions may be scattered; the simulation at both sizes, the mutations and store conformance pass, with take, beat, takeover and rebalance costs measured against [PARTITIONS.md](../../prototypes/kernel-model/PARTITIONS.md). It comes right after design approval and before product wiring (owner, 17 November) |
| K2 | **Contracts package and storage contract** | Layer 3's contracts package (turn contract, the four seams, read models) and the store contract with the in-memory store; an architecture guard fails any layer 3 reference to layers 1 and 2; the store conformance suite passes on the in-memory store |
| K3 | **Store changes the answer pages flag** (each its own PR, two reviews) | See "Store and kernel changes" below; each with conformance cases on every engine and a simulation invariant or mutation that fails without it |
| K4 | **Providers** | EF Core schema and migrations with the owned commit SQL on all four engines; store conformance in both fence modes and killed-connection stress on each relational engine; MongoDB conformance in CI on a replica set. **Tuned** (PostgreSQL, SQL Server, SQLite) means: the owned commit is the default, and deterministic round-trip budgets per reference workflow hold at or below the prototype's owned figures in [COMMIT-SQL.md](../../prototypes/kernel-model/COMMIT-SQL.md#round-trips-the-cost-runs-ef--owned) (ten no-ops, Balanced, on the owner: PostgreSQL 2, SQL Server 3, SQLite 3 engine calls), with PostgreSQL's operations note published. **Supported** (MySQL) means conformance and stress in CI, with its budgets recorded and not tuned |
| K5 | **Durable layer, built-in runtime, profiles** | Embedded and Cluster; 1,024 virtual partitions; the wake signal; the runtime conformance suite; graceful stop ends owned work before persistence is disposed (#3939's row) |
| K6 | **Workflow instance actor and native model** | Structured containers on an adapted activity context; the parked-child registry and notice change (large fan-out decision 5); the effect key wired into the built-in HTTP and email activities; M1–M3 controls of #2531's hand-off kept or ported |
| K7 | **Graph engine and Flowchart; StateMachine** | The Flowchart conformance corpus and invariants G1–G5; StateMachine invariants S1–S4; both in Embedded and Cluster |
| K8 | **Synchronous HTTP** | Every case of the sketch's [synchronous HTTP](../reports/clean-slate-kernel/design-sketch-draft-5.md#synchronous-http-requests) section, including a resume owned by another node and an owner lost while a request waits |
| K9 | **Projector, read models and the API read path** | The instance list, parked-work and plan-outcome read models; the viewer's tail read and catch-up; the runtime API on read models only |
| K10 | **Studio on the projector** | Run views on the projections, the tail read, catch-up on request and the live stream of committed entries; composed Studio journeys pass on both profiles |
| K11 | **Operations** | Trace context and metrics as the observability page lists; admission and the ingress threshold; retention and delete; simple migration with plans; parked-work inspect, retry and discard; values by reference |
| K12 | **Qualification** | See "Release 1 qualification" below |
| K13 | **Default switch** (gate 4) | **Release acceptance:** K1 to K12 accepted on main, each gate 3 row below with its evidence posted on #4229, and no open review finding at Minor or above on a merged release 1 PR. Then the switch PR makes the kernel the default (see "The switch"), the docs at the switch land, and the owner gives the go |
| K14 | **Old-core removal** (gate 5) | The retirement inventory below, taken on the removal PR's base, shown to the owner and approved |

### Store and kernel changes the answer pages flag

Each changes kernel behaviour, so each needs two independent reviews when built.

| Change | Page |
|---|---|
| A `backlog` column on the fence row, written by commit and beat | [Fairness](../architecture/fairness-and-backpressure.md) 2 |
| The send cap checked when a turn's outcome is sealed | Fairness 3 |
| Park's `KeptBy` column, the restore that clears `Done`, and Park journalled at every history level | [Observability](../architecture/observability.md) 2 |
| Recover and discard as control kinds, with the recovery ticket generation for jobs | Observability 1 |
| The `trace` column on messages and its propagation at every send | Observability |
| Lifecycle entries at every history level (State and Minimal write more), as one rule with Park's journalling | [Data lifecycle](../architecture/data-lifecycle.md) 1 |
| Payload-free done markers, with Park's kept deliveries excepted, after their readers are checked | Data lifecycle 7 |
| The journal envelope and codec headers on every payload | Data lifecycle 6 and format item 2 |
| Value rows in the same fenced batch, removed by the instance's delete | [Write volume](../architecture/write-volume.md) 1 |
| The command-kind seam (`IActorType.IsCommand`) for `Migrate` | [Migration](../architecture/migrating-running-instances.md) |
| Persisted creation headers on every provider, replacing the id-only fallback | Migration |
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

Also before release, from the answer pages: the `pad=` PostgreSQL run that accepts values by reference; the
state-size benchmark that confirms or lowers the ForEach cap; a write-volume run on the final schema; the two-tenant
stress test with admission on and off. No Azure or multi-machine spend without the owner's approval.

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
