# Feature Specification: In-process-hop payload short-circuit (ADR 0031 fast path, item a)

**Feature Branch**: `claude/elsa-engine-performance-bd4eee`
**Created**: 2026-07-20
**Program**: Runtime Execution Seam ([ADR 0031](../../docs/adr/0031-runtime-burst-execution-sticky-single-writer-drain-with-in-process-fast-path.md), accepted 2026-07-19)
**Work unit**: WU-3 — implements ADR 0031 follow-up items **(a)** the in-process-hop payload short-circuit and **(c)** the
memory-is-never-correctness guardrail; documents **(d)** burst-affinity/single-writer interaction. Follows WU-1
([spec 105](../105-drain-step-single-transaction/spec.md)) and WU-2 ([spec 106](../106-runtime-live-drain-delivery/spec.md)).
Follow-up item **(b)** — the burst-scoped reconstructible cache for heavy objects — is explicitly **left for the next unit**.
**Input**: ADR 0031 (a)/(c)/(d); the WU-2 live-drain delivery seam (`IRuntimeLiveDrainDeliveryAccessor`,
`RuntimeLiveDrainDeliveryScope`); the ratified queue-idempotency and single-writer decisions; the Step-0 cost breakdown in
[research.md](research.md).

## Why (measured — see [research.md](research.md))

ADR 0031 names "several JSON serialize/deserialize round-trips … per hop" as part of the per-activity cost. Step-0
profiling of the real drain path found the per-hop work-item JSON round-trip is **~2% of an in-memory hop and ~0.36% of a
durable hop** — NOT the dominant cost. The dominant costs are the hop count itself, durable checkpoint-commit fsync (ADR
0032's dial, shipped as coalescing), and the durable claim/outbox round-trips (WU-2's dial, shipped). This unit therefore
implements the ratified item-(a) short-circuit as a **small, safe, byte-identical** cut at the WU-2 seam — a redundant
per-hop parse+allocation removed, whose benefit scales with payload size — and is explicit that it is not, by itself, a
large throughput win on small payloads. The correctness guarantee (item c) is the load-bearing deliverable.

## The hop this cuts

During an Immediate live drain (WU-2), each straight-line continuation flows:

1. A handler builds the next `RuntimeSchedulerWorkItem` and `SchedulerWorkHandlerHelpers.NewEnqueueSchedulerWorkIntent`
   **serializes** it into an `EnqueueSchedulerWork` post-commit intent payload (`JsonElement`).
2. `RuntimeCheckpointCommitter.CommitAsync` folds that intent into the durable checkpoint commit (crash backstop).
3. `RuntimePostCommitOutboxProcessor` (WU-2 in-memory branch) reads the deliverable outbox item and calls
   `RuntimeSchedulerPostCommitIntentDispatcher.DispatchAsync`, which **deserializes** `intent.Payload` back into a
   `RuntimeSchedulerWorkItem` and enqueues it through the queue's idempotent `EnqueueAsync`.

Steps 1 and 3 are a serialize→deserialize round-trip of an object that never left the process during a live drain. The
short-circuit carries the already-materialized work item across the hop in memory so step 3 skips the deserialize; step 1's
serialized payload is **still produced and remains the authoritative durable form**.

## User Story 1 — Cheaper straight-line hops without a durability change (Priority: P1)

As a runtime operator, I need each Immediate-mode continuation hop to skip the redundant in-process work-item deserialize
while the durable outbox payload stays authoritative, so hot loops pay less per-hop overhead with no change to crash
recovery.

**Independent test**: With a live-drain scope owning the execution and the continuation published on its carrier, the
scheduler intent dispatcher enqueues the cached work item without reading the durable payload (proven by making the durable
payload serialize a *different* work-item id and asserting the cached id is enqueued).

## User Story 2 — Byte-identical committed state (Priority: P1)

As a runtime maintainer, I need a run with the fast path enabled to commit byte-identical durable state to a run with it
disabled, so the cache can never silently become a correctness dependency.

**Independent test**: Drive the same deterministic multi-hop workflow to completion with the fast path ENABLED and DISABLED
over the same in-memory durable substrate; the ordered checkpoint commits and the terminal activity/workflow state are
byte-for-byte identical (ownership-plane random ids masked), and both runs complete.

## User Story 3 — Crash / absence always redrive from the durable form (Priority: P1)

As a runtime operator, I need any absence of the cached object — process crash, sweep delivery, coalescing overlay,
cross-drain, disabled flag, or a durable store that stripped the in-memory conduit — to fall back to the durable
deserialize path unchanged, so the fast path is strictly optional.

**Independent test**: A continuation cached in a drain that then "crashes" (scope discarded) redrives from the still-durable
outbox payload in a fresh scope and enqueues exactly once; a sweep-delivered item with no cached payload dispatches
normally.

## Functional Requirements

- **FR-001**: `SchedulerWorkHandlerHelpers.NewEnqueueSchedulerWorkIntent` MUST attach the materialized
  `RuntimeSchedulerWorkItem` to the intent as an **in-process-only** conduit (`RuntimePostCommitIntent.MaterializedSchedulerWorkItem`,
  `[JsonIgnore]`), in addition to serializing it into `Payload` exactly as today. The serialized `Payload` remains the sole
  authoritative durable form; the conduit is never persisted.
- **FR-002**: The drain-scoped carrier lives on `RuntimeLiveDrainDeliveryScope` (keyed by post-commit intent id). It is
  pushed by `WorkflowDrainOrchestrator.DrainImmediateAsync` for the duration of one Immediate live drain and popped at drain
  end, so entries are **evicted at drain end** and never read after a crash, redrive, or cross-drain hand-off.
- **FR-003**: `RuntimeCheckpointCommitter.CommitAsync` MUST publish each committed `EnqueueSchedulerWork` continuation's
  materialized work item onto the owning live drain's carrier **after** a successful commit, and **only when**: the fast
  path is enabled; a live-drain scope owns THIS execution; and no coalescing session owns the execution (the coalescing
  overlay is authoritative on continuation delivery — mirrors spec 106 FR-003). A rolled-back or skipped commit publishes
  nothing.
- **FR-004**: `RuntimeSchedulerPostCommitIntentDispatcher.DispatchAsync` MUST take the cached work item (remove-on-read)
  instead of deserializing `intent.Payload` **only when**: the fast path is enabled; a live-drain scope owns the intent's
  execution; and the carrier holds that intent id. In every other case it MUST deserialize the durable payload unchanged.
  The intent↔work-item execution-id validation runs identically for both paths.
- **FR-005**: The take MUST be single-use (remove-on-read): a redelivered intent (deduped by the queue's idempotent
  enqueue) falls through to the durable deserialize path rather than re-reading a stale cached object.
- **FR-006**: A live-drain scope for a *different* execution MUST NOT divert the current intent's delivery to the cache
  (mirrors spec 106 FR-005). Intent kinds other than `EnqueueSchedulerWork` are unaffected.
- **FR-007**: `RuntimeInProcessHopFastPathOptions { Enabled = true }` is registered by default. Registering
  `{ Enabled = false }` before the runtime feature forces the durable deserialize path everywhere (the item-(c) A/B toggle
  and a host kill switch). It MUST commit byte-identical state either way.

## Copy-vs-handoff decision (investigated; ADR 0031 rule 5)

`RuntimeSchedulerWorkItem` is **immutable** — every property is get-only and its `Payload` `JsonElement` is cloned in the
constructor. The consuming handlers (`WorkflowInvokeActivitySchedulerWorkHandler` and siblings) only **read** the work item
and deserialize its `Payload`; none mutates it. The fast path therefore **hands off the reference** (no defensive copy):
sharing an immutable, single-writer-owned object across an in-process hop cannot be observed as mutation, and single-writer-
per-execution (ADR 0031 decision (b)) means no concurrent reader exists. Documented here so a future mutable work-item field
would require revisiting this to copy-on-publish.

## Why the drain-scoped carrier (not a transient that rides the store)

The materialized object must survive the *durable persistence boundary* for the durable Immediate live-drain case (WU-2
delivers in-memory for durable stores too), where the stored intent is rehydrated from SQLite and the `[JsonIgnore]` conduit
is gone. The committer therefore copies the conduit onto the **drain-scoped carrier** the moment the commit lands — the
carrier holds the live object across persistence, keyed on the live-drain scope exactly as ADR 0031 specifies ("keyed by
WorkItemId on the live-drain scope"), with deterministic eviction when the scope is popped at drain end. The conduit on the
intent is only the in-process factory→committer bridge; it is not relied on to survive any store.

## Burst affinity / single-writer interaction (ADR 0031 follow-up item d)

- **Single writer per execution.** The carrier is populated and consumed only by the one live drain that owns the
  execution's delivery scope, which is bounded by the drain's RT-2 single-writer ownership lease. There is no concurrent
  drain of one workflow execution (ADR 0031 decision (b)), so the plain dictionary on the scope needs no lock and no
  cross-thread hand-off occurs. Fork/join branches interleave *within* this single writer's drain loop.
- **Scale-out.** A workflow bursts on only one node at a time (the live agent enforces one active mailbox per execution
  id). A continuation delivered on node A is cached only in A's drain scope; if execution moves to node B (or A crashes),
  B has no cache and B's dispatcher deserializes the durable outbox payload. Cross-node correctness rides entirely on the
  durable form — memory is a per-burst accelerator, never shared or shipped between nodes.
- **Composition with the other dials.** Independent of checkpoint cadence (ADR 0032): under coalescing the fast path stands
  down (FR-003) and the overlay session folds continuations itself. Independent of the WU-2 in-memory delivery: that dial
  removes the durable *claim* round-trip; this dial removes the redundant *deserialize* on top of it.

## Guardrail & crash coverage (ADR 0031 follow-up item c)

- **Byte-identical A/B** — `RuntimeInProcessHopFastPathGuardrailTests` (Elsa.Activities.Runtime.Tests): the same
  deterministic 4-node straight-line flowchart driven to completion with the fast path ENABLED vs DISABLED commits
  byte-identical checkpoint commits + terminal state (fixed clock; ownership-plane random ids masked). A determinism
  self-check (two disabled runs) proves the fingerprint is a real convergence claim; the fast path is confirmed to engage
  in the enabled run.
- **Crash window / absence fallback / single-use take / exec-id validation / toggle** —
  `RuntimeInProcessHopFastPathTests` (Elsa.Workflows.Runtime.Tests) at the dispatcher+carrier+committer seam.

## Success Criteria

- **SC-001**: Fast path enabled + cached continuation ⇒ the cached work item is enqueued without reading the durable payload.
- **SC-002**: Fast path disabled ⇒ the durable payload is deserialized even when a cached entry is present.
- **SC-003**: No live-drain scope / different-execution scope / no cached entry ⇒ durable deserialize path.
- **SC-004**: Committer publishes to the carrier only under an owning live-drain scope, fast path enabled, and no coalescing
  session; not otherwise.
- **SC-005**: ENABLED vs DISABLED end-to-end runs commit byte-identical durable state and both complete.
- **SC-006**: Full `Elsa.Workflows.Runtime.Tests`, `Elsa.Activities.Runtime.Tests`, and `Elsa.Persistence.Groundwork.Tests`
  pass.

## Out of scope

- **The burst-scoped reconstructible heavy-object cache (ADR 0031 follow-up item b)** — the reconstructible-only cache
  contract for shared heavy in-memory objects (warmed clients, parsed documents, compiled models). Left for the next unit;
  the drain-scoped carrier introduced here is a private, single-purpose payload carrier, not that general cache.
- **The command-payload (`RuntimeInvokeActivityCommandPayload`) deserialize** — the smallest measured stage (<0.1%); not
  worth carrying through the queue.
- **Hop-count reduction, checkpoint cadence, claim/outbox round-trips** — owned by other dials (future work / ADR 0032 /
  spec 106).

## Changed surfaces

- New: `RuntimeInProcessHopFastPathOptions`; carrier methods on `RuntimeLiveDrainDeliveryScope`;
  `RuntimePostCommitIntent.MaterializedSchedulerWorkItem` (`[JsonIgnore]` conduit).
- Modified: `SchedulerWorkHandlerHelpers.NewEnqueueSchedulerWorkIntent` (attach conduit); `RuntimeCheckpointCommitter`
  (publish after commit + deps); `RuntimeSchedulerPostCommitIntentDispatcher` (consume + deps); `RuntimeCoreServiceCollectionExtensions`
  (register options; explicit committer factory).
- Tests: `RuntimeInProcessHopFastPathTests`, `RuntimeInProcessHopFastPathGuardrailTests`; benchmark A/B methods.
