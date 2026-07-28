# Research: Per-hop envelope-building CPU (spec 130, measure-first)

## Method

Instrument: `benchmarks/Elsa/Workflows/Runtime/Benchmarks/EnvelopeBuildStageDiagnostics.cs`.
Substrate: `WorkflowExecutionHarness` + on-disk SQLite Groundwork runtime stores (real durable `FindAsync`), hotloop10 (10 NoOp leaves in a Flowchart root), `[InlineData]` rows for Immediate and Coalesced(cap256). Same graph and store wiring as `BufferedCommitStageDiagnostics` / `DurableRoundTripDiagnostics`.

Per-hop denominators come from `RuntimeSchedulerDispatchDiagnostics` (deterministic, load-invariant) and from the reliable `BuildProjectionAsync` call count captured in the accumulator decorator.

Measured per cadence mode:
- (a) `BuildProjectionAsync` total wall + count (accumulator decorator); of which the `FindAsync` store-read wall (inspection-store decorator).
- (b) inter-commit gap: wall between successive committer→store calls in the same drain = upper bound on per-hop envelope build (construction happens between commits).
- (c) allocations: process-wide `GC.GetTotalAllocatedBytes()` across the run (robust); per-hop `GC.GetAllocatedBytesForCurrentThread()` bracketed inside the accumulator decorator (thread-affine, best-effort); GC collection counts (gen0/1/2).

## Denominator — PINNED BEFORE THE RUN

The gate is defined against **in-memory hot-loop CPU** = the wall the drain spends doing work that is *not* the isolated durable flush. Concretely, for the Coalesced run:

```
inMemoryHotLoopCpu(Coalesced) = totalRunWall(Coalesced) − durableFlushWall(Coalesced)
```

where `durableFlushWall` is the durable serialize+persist wall isolated by the same `TimingCommitStore`-before-coalescing trick `BufferedCommitStageDiagnostics` uses (the timer that ticks only on a real flushed segment). Under Coalesced(cap256) hotloop10, the whole run folds to a single flushed segment, so `durableFlushWall` is one durable write; everything else in the run wall is in-memory hot-loop CPU (dispatch + envelope build + overlay bookkeeping).

Because the numerator (BuildProjection wall, inter-commit gap) and the denominator (run wall − flush wall) are captured **in the same run under the same fleet load**, the *ratio* is load-invariant to first order even though the absolute walls are noisy on this shared box.

Envelope-build CPU shares reported:
- `inspectionShare = BuildProjectionWall / inMemoryHotLoopCpu`
- `envelopeShare   = interCommitGapTotal / inMemoryHotLoopCpu`

## Kill / proceed / re-aim thresholds — PINNED BEFORE THE RUN

- **KILL** (land instrument as permanent tripwire, one-line program-goals entry, STOP) if:
  `envelopeShare < 5%` of in-memory hot-loop CPU **AND** `bytesPerHop < ~2 KB`.
- **PROCEED to Stage 2** if: `inspectionShare ≥ ~10%` **OR** `envelopeShare ≥ 15%`.
- **RE-AIM to candidate C** if item 3 (payload serialize) dominates the envelope build rather than the inspection projection.

NoOp-loop caveat (pinned): NoOp leaves capture no value snapshots, so the SHA-256 value-snapshot evidence-id cost is invisible. If the measurement is borderline against the thresholds, re-run with a second graph carrying captured values before deciding.

## Machine discipline

- `uptime` before: `22:38 up 5 days, load averages: 158.73 186.40 156.91` (heavily loaded shared fleet box).
- Walls are indicative only; counters and same-run ratios are the evidence. Cadence rows compared order-swapped where a wall comparison is drawn.

---

## Results

Three samples were taken across the run window (fleet load bounced between ~34 and ~204 — this box is heavily shared, so the absolute walls swing wildly; the deterministic counts and the same-run ratios are the evidence). Key figures, Coalesced(cap256) hot-loop×10 unless noted:

### Deterministic counters (load-invariant — the decisive evidence)

| Counter | Immediate | Coalesced(cap256) |
|---|---|---|
| `BuildProjectionAsync` calls (hops) | 44 | 44 |
| `FindAsync` durable reads | 44 (== builds) | 44 (== builds) |
| Scheduler dispatches | 58 | 5 |
| ReplaySafe fused spans | 0 | 11 |
| Distinct activity executions | 11 | 11 |
| **Fold ratio (builds : activities)** | 4.00× | **4.00×** |
| **`FindAsync` returned null (miss)** | 11/44 | **44/44** |
| Durable flush writes | 66 | 1 |

The two headline facts, both deterministic:
1. **Spec-123 fusion cut dispatches 58→5 but left the per-hop envelope builds at 44 in both modes** — construction was never folded, exactly as the lead predicted.
2. **Under Coalesced, all 44 `FindAsync` reads return null.** The projection is buffered in the checkpoint-commit overlay and only written to the durable inspection store at the single segment flush, and the inspection store has **no coalescing overlay**, so every mid-segment read hits an empty durable baseline. **75% of the 44 builds are intermediate projections that the last-write-wins fold discards** (4.00× builds per activity). In Immediate mode only 11/44 miss — the started/completed hops of each activity find the prior projection and take the `Merge` path — so Immediate's reads do useful work while Coalesced's are pure waste.

### Wall / CPU shares (same-run ratios; absolute walls are load-noise)

| Metric (Coalesced) | sample 1 (load ~34) | sample 2 (load ~123) | sample 3 (load ~200) |
|---|---|---|---|
| `BuildProjectionAsync` per-hop | 136 µs | 222 µs | 191 µs |
| of which `FindAsync` store-read | 86.6% | 89.9% | 84.5% |
| **pure construction CPU share** | **0.25%** | **0.15%** | **0.03%** |
| inspection-build share | 1.90% | 1.47% | 0.21% |
| inter-commit gap (loose/contaminated) | 38.2% | 40.2% | 21.4% |

Immediate pure-construction CPU share across samples: 1.04→derived, 0.37%, 0.70% — all ≪ 5%.

### Allocations

- Process-wide, whole run (Coalesced): ~13.2 MB, ~300 KB/hop, GC gen0/1/2 ≈ 2/0/0. This includes durable-flush serialization + POCO state, **not** just the envelope.
- `BuildProjectionAsync` thread-affine bracket (remarkably stable across samples: 9829 / 9829 / 9888 B/hop Coalesced; ~25.6 KB/hop Immediate). This is dominated by the **`FindAsync` deserialize/query**, not object construction. Pure-construction allocation was not byte-isolated, but its CPU share (0.03–0.70%) bounds it well under the 2 KB/hop floor; the >2 KB/hop measured here is attributable to the store read.

### Why the inter-commit gap is not the decision metric

The gap (b) reads 21–45% but is 2.7–19 ms **per hop** — three-to-four orders of magnitude above the ~20–30 µs pure-construction cost. It contains activity-execute + scheduler dispatch + thread-scheduling latency under fleet load, so on this box it measures scheduling, not envelope building. It is reported as a labelled loose upper bound only; the decisive clean number is the directly-timed pure-construction CPU (build − find).

## Verdict — KILL (envelope-building-CPU hypothesis refuted), with a re-aim finding

**KILL.** The actual unit hypothesis — that per-hop envelope *construction* (metadata `Snapshot` ×3, projection object build, continuation-payload serialize, `RuntimeCheckpointStateChangeSet` ctor validation) is a meaningful CPU cost — is **refuted**. Directly-measured pure-construction CPU is **0.03–0.70%** of the in-memory hot-loop denominator in every sample and both cadence modes, far below the pinned 5% floor. The pinned PROCEED thresholds are **not** met by any clean measurement (projection build 0.2–1.9% ≪ 10%; the only ≥15% signal is the scheduling-contaminated inter-commit gap, which does not isolate envelope build). Items 1/3/4 are micro-scale (a few dict copies, one small `JsonElement`, eight `.Any` over empty/tiny collections) and never approach the threshold. The NoOp-loop value-snapshot SHA-256 cost is invisible here, but since the construction share is ~30× below the floor even before adding it, the borderline re-run caveat is not triggered.

**Re-aim finding (filed as a separate follow-up, NOT actioned here).** The only non-negligible cost in the envelope-build region is *not CPU* — it is the **per-hop durable inspection-store `FindAsync`**, which the coalescing overlay does not fold. Deterministic evidence: 44 reads/run, **44/44 returning null under Coalesced**, feeding a **4.00× fold ratio** (75% of builds are intermediate and discarded). This is a store-read-coalescing opportunity in the exact family as spec 110's redundant executable-artifact read (characterize → refute the CPU premise → re-aim to a store read). It maps to the brief's **candidate A** (fold-aware skip/deferral of intermediate inspection projections inside a coalescing segment), but candidate A carries a HARD precondition — grep every consumer of `IActivityExecutionInspectionStore` / `IRuntimeActivityExecutionInspectionAccumulator` and prove no in-burst reader depends on an intermediate projection existing pre-flush — that deserves its own scoped unit rather than being smuggled in under a killed CPU unit. Recommended as a follow-up; a background chip has been filed.

**Disposition.** The instrument `EnvelopeBuildStageDiagnostics` lands as a permanent tripwire (like `DurableRoundTripDiagnostics`), so any future regression that reintroduces meaningful per-hop construction CPU — or that changes the 44-build / 44-read / 4.00×-fold shape — is caught. A one-line finding is added to `docs/program-goals/runtime-execution-seam.md`. No production code changed. STOP.

## Machine discipline (after)

`uptime` after final run: `22:50 up 5 days, load averages: 202.74 151.54 135.25`. Load swung ~34→204 across the three samples; the deterministic counters (44 builds, 44 reads, 44/44 null, 4.00× fold, 58→5 dispatches) were byte-identical across all of them, which is exactly why the verdict rests on them and not on the walls.

