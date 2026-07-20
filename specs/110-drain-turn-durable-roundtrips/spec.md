# Feature Specification: Collapse per-drain-turn durable round-trips — characterize, and eliminate the redundant executable-artifact reads

**Feature Branch**: `worktree-agent-a3459d4105c4984c3`

**Created**: 2026-07-20

**Status**: Draft (characterization complete; premise re-aimed by evidence)

**Input**: Engine-performance work unit under the [Runtime Execution Seam](../../docs/program-goals/runtime-execution-seam.md) bucket: *"collapse per-drain-turn durable commit round-trips."* Two prior profiles suggested ~33% of a 2-node drain window is spent in `elsa.runtime.checkpoint.commit`. This unit was to fold those per-turn durable commits under the non-mandatory (Coalesced) path without weakening W5/RT-2 fencing, ADR 0020 atomicity, spec-105 consume-idempotency, or crash-redrive safety.

## Context and characterization outcome

STEP 1 (mandatory characterization) was performed empirically over on-disk WAL SQLite — see
[research.md](./research.md) for the method, the instrument (`benchmarks/.../DurableRoundTripDiagnostics.cs`),
and the full table. The result **refutes the unit's premise** and re-aims it.

Under the shipped **Coalesced** cadence (ADR 0032, delivered across specs 105 + 107 + 108):

- Per-drain-turn checkpoints that defer are **overlay-only — zero durable I/O**. The
  `elsa.runtime.checkpoint.commit` span wraps the committer call, so a *buffered* span exists but does
  no fsync; only *flushed* spans round-trip. "12 spans" ≠ "12 durable round-trips."
- The per-turn durable-commit collapse the unit set out to build **already ships**. Measured:
  hot-loop×10 goes from **66 checkpoint commits + 194 durable scheduler-queue ops (Immediate)** to
  **1 commit + 2 queue ops (Coalesced)**; the 2-node from **12 + 41** to **2 + 4**.
- The execution-liveness fence touch, the spec-105 consumed-item delete, the overlay bookkeeping, and
  the commit markers are all either folded into the single per-flush unit-of-work or served from the
  in-memory overlay. None is a residual per-turn durable round-trip.
- The root-write lease is **workflow-level (~2/run), not per-turn** — per-activity checkpoints carry
  no `WorkflowExecution` state and skip the lease entirely.

The **only** per-drain-turn durable round-trip coalescing does not remove is the redundant
**executable-artifact read**: `IWorkflowExecutableStore.FindAsync` is called ~5×/activity (46 reads
for the 10-activity hot loop), every call resolving the *same* immutable, content-addressed pinned
artifact (ADR 0038). This unit is therefore re-aimed to eliminate that redundancy with a drain-scoped
reconstructible read cache, and to keep the durable-round-trip diagnostic as the acceptance instrument.

## Scope boundary

- **In scope**: (1) the permanent durable-round-trip diagnostic (durable transactions per drain turn,
  broken down by kind), which is the acceptance instrument; (2) a **drain-scoped, reconstructible,
  immutable-by-`ArtifactId` read cache** over `IWorkflowExecutableStore.FindAsync` on the hot dispatch
  path so repeated same-artifact reads within one drain collapse to one durable read.
- **Explicitly NOT in scope (already delivered / would regress or collide)**:
  - Any change to the checkpoint-commit / coalescing / fence / queue-advance path. The measurement
    shows it is already at one atomic durable commit per flush with the per-hop queue storm folded
    into the overlay. Touching it would be a no-op or a regression risk against W5/RT-2/ADR 0020/
    spec-105/crash-redrive, all of which the current shape already satisfies.
  - Immediate mode. Its per-hop commit + claim/complete cost is the documented, opt-out-only default
    (ADR 0032); this unit introduces no global behavior change for Immediate mode.
  - The executable read cache reaching the **root-write-lease / GC** path
    (`WorkflowExecutableDependencyGraph.LoadClosureAsync`, deletion guard reads), which must observe
    fresh durable state for retention correctness. The cache is confined to the execution dispatch
    read path.
  - ADR 0031's burst-scoped cache for heavy **user** objects and the JSON-hop fast path. This unit's
    cache is for the runtime *executable artifact* only, reuses the drain DI scope (no new `AsyncLocal`
    accessor, RT-7), and is sequenced to compose with — not duplicate — the burst unit.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — The durable round-trip cost of a drain is visible and attributable (Priority: P1)

An engineer can measure, for a given workflow and cadence, how many durable transactions per run the
drain performs, broken down into checkpoint commits, scheduler-queue ops, lease writes, and executable
reads — so a regression in any class is caught and a claim like "coalescing removes the commit storm"
is evidence, not assertion.

**Why this priority**: The whole unit turned on a premise that only an instrument could confirm or
refute. The instrument is the durable deliverable regardless of the code change.

**Independent Test**: Run `DurableRoundTripDiagnostics` for 2-node and hot-loop×10 under Immediate and
Coalesced; assert the coalesced commit count and queue-op count are strictly below Immediate, and that
the counts are deterministic across iterations.

**Acceptance Scenarios**:

1. **Given** the hot-loop×10 graph, **When** run under Coalesced, **Then** durable checkpoint commits
   == 1 and durable scheduler-queue ops == 2, versus 66 and 194 under Immediate.
2. **Given** either graph, **When** the executable read cache is enabled, **Then** `FindAsync` durable
   reads per run drop from ~5×activity-count to ~1 while every other durable-op count is unchanged.

### User Story 2 — Repeated same-artifact executable reads within a drain collapse to one durable read (Priority: P1)

While one drain resolves the pinned executable for each hop, the artifact — immutable and
content-addressed — is loaded from the durable store at most once; subsequent resolutions in the same
drain are served from a reconstructible in-scope cache and produce byte-identical executables.

**Why this priority**: It is the only per-drain-turn durable round-trip class the shipped coalescing
does not already remove.

**Independent Test**: Drive the hot loop with a counting `IWorkflowExecutableStore`; assert `FindAsync`
hits the durable store once for the pinned artifact and the run completes byte-identically to the
uncached run (same committed checkpoint state, same outputs).

**Acceptance Scenarios**:

1. **Given** a hot loop of N activities sharing one pinned executable, **When** drained with the cache
   enabled, **Then** the durable store sees exactly one `FindAsync` for that artifact id.
2. **Given** the cache is cold or disabled, **When** the same workflow is drained, **Then** the
   committed checkpoint state is byte-identical (cache is never a correctness dependency).
3. **Given** two concurrent drains of different workflow executions, **When** each resolves its own
   pinned artifact, **Then** neither drain observes the other's cached artifact (per-drain scope).

## Requirements *(mandatory)*

- **FR-001**: A permanent, deterministic diagnostic MUST report durable transactions per run for a
  workflow/cadence pair, broken down into: checkpoint commits, durable scheduler-queue transitions
  (enqueue/dequeue/claim/complete/consume), root-write-lease writes, and executable reads. It MUST run
  under `benchmarks/` (excluded from CI test gates) and assert Coalesced < Immediate on commit and
  queue-op counts.
- **FR-002**: On the execution dispatch read path, resolving the pinned executable for a hop MUST
  consult a drain-scoped cache keyed by `ArtifactId` before issuing a durable `FindAsync`, and MUST
  populate it on a miss. A cache hit MUST return an executable byte-identical to the durable one
  (immutability is guaranteed by ADR 0038 content-addressing).
- **FR-003**: The cache lifetime MUST be exactly one drain (reuse the drain DI scope). It MUST NOT
  outlive the drain, MUST NOT be shared across workflow executions, and MUST NOT introduce a new
  `AsyncLocal` service-locator (RT-7). A cold or absent cache MUST fall back to the durable read with
  byte-identical results (ADR 0031 invariant: durable state is truth; cache is a reconstructible
  accelerator, never a correctness dependency).
- **FR-004**: The cache MUST NOT serve the root-write-lease / GC read path
  (`LoadClosureAsync`, deletion-guard reads); those continue to read fresh durable state.
- **FR-005**: No change to the checkpoint-commit, coalescing, fence, or queue-advance path. W5
  terminal-status fencing, RT-2 single-writer/ownership fencing, ADR 0020 atomicity, spec-105
  consume-idempotency, and crash-redrive behavior remain exactly as shipped (the measurement confirms
  they are already at one atomic durable commit per flush).

## Success criteria

- Executable-read durable round-trips per run drop from ~5×activity-count to ~1 for a single-artifact
  workflow, with every other durable-op count unchanged and committed state byte-identical.
- The durable-round-trip diagnostic exists in-repo and reproduces the [research.md](./research.md)
  table.
- Full runtime, groundwork-persistence, and activities-runtime test projects stay green; a byte-identical
  guardrail (cache on vs off) is added.

## Replay-window statement

This unit does **not** change the crash-replay window. It touches only an idempotent read cache over
immutable artifacts; it adds no deferred durable state, folds nothing new, and moves no flush boundary.
A crash mid-drain replays exactly as today (from the last flushed checkpoint), because the cache holds
no state that is not already reconstructible from the pinned durable artifact. (Contrast: ADR 0032's
coalescing *does* widen the replay window to a configured segment — that is already shipped and is not
altered here.)

## Dependencies / sequencing

- Builds on shipped ADR 0032 coalescing (specs 105/107/108) — treated as the durable-commit collapse,
  already delivered.
- Sequenced **before or independently of** ADR 0031's burst fast-path unit; both reuse the drain DI
  scope. If the burst unit lands first, FR-002/FR-003 attach the artifact cache to the burst scope
  instead of opening its own — same lifetime, no duplication.
