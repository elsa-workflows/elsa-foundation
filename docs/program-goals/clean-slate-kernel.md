# Clean-slate execution kernel

- **Status:** Proposed program plan, 17 November 2026. Owner goal of 12 November 2026 (build the kernel, prove it,
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

Each milestone closes when its acceptance holds on main. K1 to K11 are gate 3 (release 1); K12 is gate 4; K13 is
gate 5.

| # | Milestone | Acceptance |
|---|---|---|
| K0 | **Design approval** (gate 2) | ADR 0099, the register, the seven answers and the supersede list reviewed by the independent architecture review; every material finding resolved; the owner's delegated approval recorded on #4229 |
| K1 | **Contracts package and storage contract** | Layer 3's contracts package (turn contract, the four seams, read models) and the store contract with the in-memory store; an architecture guard fails any layer 3 reference to layers 1 and 2; the store conformance suite passes on the in-memory store |
| K2 | **Store changes the answer pages flag** (each its own PR, two reviews) | See "Store and kernel changes" below; each with conformance cases on every engine and a simulation invariant or mutation that fails without it |
| K3 | **Providers** | EF Core schema and migrations with the owned commit SQL: PostgreSQL, SQL Server and SQLite tuned, MySQL supported; MongoDB conformance in CI on a replica set; killed-connection stress on each relational engine |
| K4 | **Durable layer, built-in runtime, profiles** | Embedded and Cluster; 1,024 virtual partitions; the wake signal; the runtime conformance suite; graceful stop ends owned work before persistence is disposed (#3939's row) |
| K5 | **Workflow instance actor and native model** | Structured containers on an adapted activity context; the parked-child registry and notice change (large fan-out decision 5); the effect key wired into the built-in HTTP and email activities; M1–M3 controls of #2531's hand-off kept or ported |
| K6 | **Graph engine and Flowchart; StateMachine** | The Flowchart conformance corpus and invariants G1–G5; StateMachine invariants S1–S4; both in Embedded and Cluster |
| K7 | **Synchronous HTTP** | Every case of the sketch's [synchronous HTTP](../reports/clean-slate-kernel/design-sketch-draft-5.md#synchronous-http-requests) section, including a resume owned by another node and an owner lost while a request waits |
| K8 | **Projector, read models and the API read path** | The instance list, parked-work and plan-outcome read models; the viewer's tail read and catch-up; the runtime API on read models only |
| K9 | **Studio on the projector** | Run views on the projections, the tail read, catch-up on request and the live stream of committed entries; composed Studio journeys pass on both profiles |
| K10 | **Operations** | Trace context and metrics as the observability page lists; admission and the ingress threshold; retention and delete; simple migration with plans; parked-work inspect, retry and discard; values by reference |
| K11 | **Qualification** | See "Release 1 qualification" below |
| K12 | **Default switch** (gate 4) | Release acceptance; the coexistence, rollback and live-run disposition below carried out; the docs at the switch; the owner's go |
| K13 | **Old-core removal** (gate 5) | The retirement inventory below, shown to the owner and approved; retained readers and artifacts recorded |

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
| Compaction of non-positive journal rows below the cursor (after release 1) | Data lifecycle 8 |

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
6. **Graceful stop** (#3939) and 7. **uncertain commits** (#3698): a landed commit is never reported as refused.

Also before release, from the answer pages: the `pad=` PostgreSQL run that accepts values by reference; the
state-size benchmark that confirms or lowers the ForEach cap; a write-volume run on the final schema; the two-tenant
stress test with admission on and off. No Azure or multi-machine spend without the owner's approval.

## Coexistence, rollback and live runs (proposed)

This section is a proposal for the review; the owner decides the live-run disposition and the switch at K12.

**Until the switch.** The kernel ships in its own packages (the contracts package, then layers 1 and 2, then the
workflow instance actor and models) that no old-core package references. A host sends new work to one core, chosen
by the host switch; the old core is the default. The kernel keeps its own tables and migrations and never reads or writes the old
core's tables, so the two can share a database without touching each other's data. The activity library and the
definitions are shared; the adapted activity context is the one point where the two cores meet the same code, and K5
keeps the old core's tests green while it changes.

**No data migration.** Running or finished old-core instances are not converted to kernel instances: the owner
accepted the clean break on 11 and 12 November (sketch trade-off 17). The old core has shipped only as the contributor
preview `contributor-preview-2026-10-07.1` (#2531).

**Live runs at the switch.** A host switches cores only when its old-core
instances are drained:

1. The host stops starting new old-core instances: new starts go to the kernel.
2. Running and suspended old-core instances finish on the old core, which stays composed beside the kernel for that
   period and takes no new starts, or an operator terminates them.
3. The switch tool reports how many old-core instances are still running, and refuses to remove the old core while
   any are.

**Readers of old-core data.** Old-core run history stays readable through the old core's API and Studio views while
the old core is installed. The kernel's migrations never drop old-core tables. Removing the old core (K13) removes
its code; dropping its tables is a separate, documented operator step.

**Rollback.**

- **Before the switch:** turn the host switch back. Kernel instances stop running and their rows stay untouched.
- **After the switch:** a host can return to the old core only for new work; kernel instances are not readable by the
  old core, so they stay in their tables until the host switches forward again.
- **Within the kernel:** a release that bumps a state version is rolled back as built: instances already written at
  the new version are withheld on older nodes until the roll-forward, with a metric for withheld instances
  (migration decision 1).

## Retirement (proposed)

**The inventory method.** The deletion list comes from a fresh inventory on the switch commit, never from historical
counts. The 18,920 files, 513 projects and 2,583 references recorded earlier in the program make nothing eligible for
deletion. The inventory takes the project-reference graph and the namespaces of the old core's engine (scheduler,
drainer, checkpoint commit stores, runtime EF contexts and their migrations, and tests bound to those internals), and
classifies each item as delete, port (layer 3 code and tests that carry over) or keep. It records the readers and
artifacts that stay (old-core tables, exported history). **The owner sees the list before anything is deleted.**

**Docs to rewrite or retire at the switch** (the sketch's
[architecture documentation](../reports/clean-slate-kernel/design-sketch-draft-5.md#architecture-documentation-owner-decision-12-november)
list; the owner sees it first):

| Page | Proposed disposition |
|---|---|
| [architecture-tour.md](../architecture-tour.md) | Rewrite on the three layers |
| [how-a-workflow-executes.md](../how-a-workflow-executes.md) | Rewrite as a turn, a commit and the projector |
| [runtime-durable-resumption.md](../runtime-durable-resumption.md) | Retire; resumption is a message to the instance |
| [runtime-durable-timers.md](../runtime-durable-timers.md) | Retire; timers are messages with a due time |
| [bookmark-expiration.md](../bookmark-expiration.md) | Rewrite or retire after K5 shows how waits expire |
| [runtime-fault-behavior.md](../runtime-fault-behavior.md) | Rewrite on Park, recovery and intervention |

The fresh inventory adds any other page that describes the old core (candidates: [seams.md](../seams.md),
[serialization.md](../serialization.md), [user-tasks.md](../user-tasks.md), which name its runtime packages). At the
switch the architecture references change from Proposed to Current, and a shorter user-facing section is published
under `website/docs/`.

**ADRs.** ADR 0099's list of ADRs it supersedes, amends or keeps; each status changes at the switch, after the owner
has seen the list.

**Issues and PRs.** #4176 (ADR 0098) is closed as superseded after the owner sees it; #2564 and #4043 were closed
on 16 November.
