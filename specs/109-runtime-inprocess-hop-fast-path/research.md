# Research — Step 0 per-hop cost breakdown (spec 109)

**Mandate.** ADR 0031 quantifies "~5–7 work-item hops per ordinary activity, several JSON serialize/deserialize
round-trips, fresh scope/context construction per hop" and says a 10-activity in-memory hot loop still costs ~38 ms/activity
with near-zero durable I/O. Before scoping the fast path, this unit profiled where the per-hop time actually goes, so the
implementation cuts the real cost, not the assumed one.

## Method

Temporary `Stopwatch` instrumentation (a static `HopProfiler` with named accumulators, since removed) wrapped the named
hop stages in the live drain path, driven by the existing engine benchmark harness
(`benchmarks/Elsa/Workflows/Runtime/Benchmarks/EngineExecutionBenchmarks.cs`) over the 10-activity straight-line hot loop
(`NoOpStep` leaves), in-memory and durable-SQLite, after warmups. Stages measured: work-item serialize on enqueue
(`NewEnqueueSchedulerWorkIntent`), work-item deserialize on dispatch (`RuntimeSchedulerPostCommitIntentDispatcher`),
command-payload deserialize (`WorkflowInvokeActivitySchedulerWorkHandler.DeserializeInvokePayload`), durable-value
`ListAsync`, DI scope creation, checkpoint commit, plus the drain-loop machinery (acquire-next / claim, terminal-status
read, whole-handler dispatch, outbox `ProcessAsync`). The profiler's own Stopwatch overhead inflates absolute per-run wall
on the hottest stages, so read the RELATIVE shares, not the absolute totals.

## In-memory hot loop×10 (per run ≈ 68 ms clean; ~44 hops/run)

| Stage | per run | share | note |
|---|---:|---:|---|
| `drain:dispatch(all-handlers)` | ~53 ms | ~78% | all handler dispatch + claim/ack/pipeline/tracer machinery |
| &nbsp;&nbsp;of which `TOTAL-invoke-handler` | ~14 ms | ~20% | 11 invokes/run |
| &nbsp;&nbsp;&nbsp;&nbsp;of which `checkpoint-commit` | ~5 ms | ~7% | in-memory store writes |
| `outbox:ProcessAsync` | ~3 ms | ~4% | in-memory delivery loop |
| **work-item deserialize (dispatch)** | **~0.8 ms** | **~1.2%** | 44 calls/run @ ~0.018 ms — the stage this unit cuts |
| **work-item serialize (enqueue)** | **~0.7 ms** | **~1.0%** | 43 calls/run @ ~0.016 ms (still produced — durable form) |
| **command-payload deserialize** | ~0.03 ms | <0.1% | smallest stage |
| durable-value `ListAsync` | ~0.07 ms | ~0.1% | |
| DI scope create | ~0.005 ms | ~0% | ambient burst scope reused (already optimized) |

**All JSON serialize/deserialize across a run ≈ 1.5 ms of ~68 ms = ~2.2%.**

## Durable-SQLite hot loop×10 (per run ≈ 686 ms)

| Stage | per run | share | note |
|---|---:|---:|---|
| `drain:dispatch(all-handlers)` | ~377 ms | ~55% | dominated by durable writes inside handlers + claim completion |
| `outbox:ProcessAsync` | ~150 ms | ~22% | durable outbox recording |
| `checkpoint-commit` | ~45 ms | ~7% | fsync per commit (66 commits/run) |
| `drain:acquire-next` | ~38 ms | ~6% | SQLite claim acquisition |
| `drain:terminal-status-read` | ~11 ms | ~1.6% | state read per dispatched item |
| **work-item serialize (enqueue)** | **~1.3 ms** | **~0.19%** | |
| **work-item deserialize (dispatch)** | **~1.1 ms** | **~0.16%** | |
| durable-value `ListAsync` | ~1.0 ms | ~0.15% | |
| command-payload deserialize | ~0.1 ms | ~0.01% | |
| DI scope create | ~0.01 ms | ~0% | |

**All JSON serialize/deserialize across a run ≈ 2.5 ms of ~686 ms = ~0.36%.**

## Finding — serialization is NOT the dominant cost

The per-hop JSON round-trip that ADR 0031 follow-up item (a) short-circuits is **~2% of an in-memory hop and ~0.4% of a
durable hop**. The dominant costs are, in order:

1. **The number of hops per activity** (~4–6) and the per-hop dispatch machinery (claim/ack, pipeline, tracer, handler
   work), which the durable providers turn into store round-trips. This is a hop-count problem, not a serialization problem.
2. **Durable checkpoint-commit fsync** (~45 ms/run durable) — owned by the checkpoint-cadence dial ([ADR 0032](../../docs/adr/0032-runtime-checkpoint-cadence-is-policy-driven-per-workflow.md), shipped as coalescing), not by this unit.
3. **Durable claim/acquire and outbox recording round-trips** — the outbox claim round-trip is exactly what
   [spec 106](../106-runtime-live-drain-delivery/spec.md) (WU-2) already removes for Immediate live drains.
4. **DI scope / activity-context construction per hop is already cheap** — the drain reuses the ambient burst scope
   (`di-scope-create` ≈ 0), so the ADR's "fresh scope/context construction per hop" concern is already addressed by the
   ambient-services seam (specs 082/106). No further scope-reuse work is warranted here.

## Scope decision driven by the evidence

- **Implement item (a)** — the work-item payload short-circuit at the intent-delivery hop — because it is the ratified
  follow-up and is a clean, safe cut at the WU-2 live-drain seam. Report it honestly as a **small** win (sub-percent to ~2%
  on these small-payload shapes) whose benefit **scales with payload size** (large command payloads / large work items make
  the avoided parse+allocate proportionally larger; the `NoOp`/probe benchmarks are a lower bound).
- **Scope OUT the command-payload deserialize** (`RuntimeInvokeActivityCommandPayload`) — the SMALLEST measured stage
  (<0.1%). Carrying that object through the durable queue would add plumbing for ~0.01 ms/hop. Not worth it.
- **Do not chase scope/context reuse** — already optimized (ambient burst scope).
- **The real levers (hop count, commit cadence, claim round-trips) are owned by other dials** — ADR 0032 (shipped),
  spec 106 (shipped), and a future hop-count reduction; recorded so the program goal targets them next rather than
  re-cutting serialization.

## Benchmark A/B (fast path OFF vs ON), same harness

| Shape | OFF p50 | ON p50 | Read |
|---|---:|---:|---|
| in-memory 2-node | ~14.3 ms | ~15.1 ms | within noise |
| in-memory hot-loop×10 | ~55.4 ms | ~56.8 ms | within noise |
| durable hot-loop×10 | ~350 ms | ~235 ms* | *artifact — see below |

\* The durable "improvement" reversed when the run order was swapped (ON-first: ON ~444 ms, OFF-second ~307 ms), i.e. the
delta follows measurement **position**, not the flag: durable-SQLite wall time is dominated by fsync + OS-page-cache
warmup. Commits/run is **66 in both** and the guardrail proves **byte-identical committed state**, so correctness holds; the
fast-path wall effect is within warmup noise on these payloads, consistent with the sub-percent serialization share above.

**Conclusion:** the fast path is correct and safe (guardrail-proven byte-identical), removes a redundant per-hop parse +
allocation, and is proportionate to implement — but is not, by itself, a large throughput win on small payloads. The
outsized durable hot-loop cost lives in hop count, commit fsync, and claim/outbox round-trips, which other dials own.
