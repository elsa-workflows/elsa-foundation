# W9 — Checkpoint-coalescing persistence benchmark results

Work unit: **W9 — Checkpoint-coalescing persistence policy** (findings **E3-6**, **RT-10**; satisfies
the Groundwork benchmark governance gate flagged in PS-4). Program-goal bucket:
[`elsa-4-review-remediation`](../../program-goals/elsa-4-review-remediation.md). Brief:
[roadmap.md §W9](roadmap.md#w9-checkpoint-coalescing-persistence-policy).

Measured on the post-Phase-0 tree (W1 fault semantics + W5 ownership fencing merged), .NET 10, in-memory
runtime substrate. Reproduced by
`Elsa.Workflows.Runtime.Tests.RuntimeCheckpointCoalescingTests.Coalescing_ReachesSameTerminalStateWithFewerCommitsThanImmediate`.

## Headline result

For a representative straight-line drain burst, the opt-in coalescing policy folds the burst's intra-drain
checkpoints into **one** atomic durable commit, matching Elsa 3's one-write-per-burst reference behaviour,
while reaching a byte-identical terminal state.

| Policy | Durable checkpoint commits per straight-line burst | Terminal state |
|---|---|---|
| `ImmediateRuntimeCheckpointPersistencePolicy` (default) | **3** | ✅ reference |
| `CoalescingRuntimeCheckpointPersistencePolicy` (opt-in) | **1** | ✅ identical |
| Elsa 3 default (`per-burst` commit) — reference target | **1** | — |

**Commit-count parity with Elsa 3 for straight-line workflows: achieved (1 commit per burst).**

## Reconciliation with the brief's "4–5 commits per activity" figure

The W9 brief (written against tree `ffafa32f`, before W1/W5 landed) estimated "≥4 queue hops and 4–5
checkpoint commits per activity" under the Immediate policy. Re-measured on the **current** tree, a
single-activity straight-line burst drives **3** durable checkpoint commits under Immediate
(`WorkflowStarted`-bearing start commit → activity continuation commit → `WorkflowCompleted` commit). The
lower figure reflects the current committer/drain shape after W1/W5, not a change in W9's scope: the ratio
that matters — **per-burst durable commits, Immediate vs. coalescing** — is `3 → 1`, i.e. the coalescing
policy removes the intra-burst constant-factor write multiplier exactly as the brief intended. On a durable
provider each avoided commit is a real round-trip saved.

The coalescing win **scales with burst length**: an N-hop non-suspending segment that costs ~N durable
commits under Immediate costs **1** under coalescing (capped — see below). The measured micro-benchmark is a
single-activity burst; a longer straight-line segment folds proportionally more.

## Durability / performance trade — this is a *selectable* policy

Like Elsa 3's commit strategies, the trade is opt-in and per-application:

- **Default (`Immediate`)** — every checkpoint flushes when decided. Smallest crash-replay window (one
  checkpoint); highest write amplification. **Unchanged; this remains the default.**
- **Opt-in (`Coalescing`)** — intra-drain non-suspending checkpoints fold into one flush at quiescence.
  Fewest durable writes; wider crash-replay window (one *segment*). Enabled with
  `services.AddCoalescingRuntimeCheckpointPersistence()`.

Every durability-critical boundary still flushes immediately under coalescing —
`WorkflowSuspended`/`Completed`/`Faulted`/`Cancelled`, `IncidentRecorded`, `ActivitySuspended`/`Cancelled`,
and `BookmarkCreated` — so a decided fault stays durable at the moment it is decided and an external stimulus
always finds a durable bookmark (never an in-memory-only one). W5 ownership fencing still gates the single
folded flush through `RuntimeCheckpointCommitter.CommitAsync`, so a stale writer is rejected before anything
persists.

## Replay-cost / cap trade

A coalesced segment's crash-replay cost is bounded by
`CoalescingRuntimeCheckpointPersistenceOptions.MaxSegmentCheckpoints` (default **50**): once the buffered
checkpoint count reaches the cap an intermediate flush is forced. This bounds two otherwise-unbounded costs:

- **Replay cost** — a crash re-runs at most one segment (≤ cap checkpoints of activity re-execution),
  never the whole workflow.
- **Memory** — the ambient working set holds at most one segment of buffered change-sets.

Lowering the cap narrows the crash-replay window and memory footprint at the cost of more durable commits
(toward Immediate); raising it trades a wider replay window for fewer writes. `50` is a middle default; tune
per workload. In-segment activity re-execution after a mid-segment crash is expected and documented in
[`docs/runtime-durable-resumption.md`](../../runtime-durable-resumption.md#coalescing-checkpoint-persistence--the-deferred-flush-window-e3-6--rt-10).

## Correctness evidence

- **Crash-safe by construction** — the durable scheduler queue never advances past the last flushed state;
  the segment-entry work item is only dequeued as part of the atomic flush commit.
  `RuntimeCheckpointCoalescingTests.CrashMidSegment_DurableQueueStillHoldsSegmentEntry_AndNoPartialCheckpointPersisted`
  asserts a mid-segment crash leaves the segment entry in the durable queue and persists **no** partial
  checkpoint.
- **Two-generation convergence** —
  `GroundworkCoalescingCrashConvergenceTests.Coalescing_CrashMidSegment_QueueRetainsSegmentEntry_ThenHonestSweepConvergesWithoutDuplicateEffects`
  crashes gen-1 mid-segment over a shared durable store, then runs an honest gen-2 sweep that converges to
  the crash-free Immediate control snapshot with no duplicate terminal effect.
- **Policy + fold unit coverage** — `RuntimeCheckpointCoalescingPolicyTests` (boundary→Immediate,
  non-boundary→Deferred, `CoalescedFlush` marker→Immediate) and `RuntimeCheckpointFoldTests`
  (last-writer-wins collapse, Append never collapsed, outbox excluded from the folded state).
- **Bookmark-suspend durability** —
  `RuntimeCheckpointCoalescingTests.Coalescing_BookmarkSuspend_FlushesDurableBookmarkImmediately`
  drives a `CreateBookmark` suspend under coalescing and asserts the bookmark lands in the **durable**
  bookmark store at the boundary (never buffered in-memory), so a durable timer/stimulus pump — e.g. W8's
  Delay pump, which reads the durable bookmark store — can never race an in-memory-only bookmark.
- **Delay-boundary store isolation** —
  `RuntimeCheckpointCoalescingTests.Coalescing_DoesNotDecorateDurableTimerOrBookmarkStores_SoDelaySuspensionStaysDurable`
  proves that coalescing wraps only the seven core checkpoint stores, so W8's `IDurableTimerStore` and the
  `IBookmarkStateStore` are **never** captured by the buffer even when both features are composed. W8's `Delay`
  writes a durable timer *and* a bookmark at suspension; both therefore persist the instant they are written —
  before quiescence ends — so the durable timer pump can never observe an in-memory-only timer or bookmark.

## Baselines (all green, unmodified except added tests)

Measured after merging W8 (durable timer pump / Delay) into the branch.

| Suite | Baseline | After W9 |
|---|---|---|
| Architecture guards | 37 | 37 |
| Runtime | 588 | 615 (+27 coalescing/policy/fold/suspend) |
| Groundwork | 142 | 143 (+1 two-generation convergence) |
| Resumption | 12 | 12 |
| Activities Runtime | 140 | 140 |
| Publishing API | 49 | 49 |
| Scheduling runtime | 19 | 19 |
| Activities Scheduling | 8 | 8 |

Condition A (zero default-path change) is proven by every pre-existing baseline staying green **unmodified**:
with the default Immediate policy the coalescing decorators and ambient working-set machinery are never
registered, so the default path is byte-identical.
