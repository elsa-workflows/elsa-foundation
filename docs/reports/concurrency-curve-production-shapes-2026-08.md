# Concurrency curve re-measured with production leaf shapes (2026-08)

Closes backlog item **P2** ([#1225](https://github.com/elsa-workflows/elsa-foundation/issues/1225)) from the
[Elsa 4 improvement recommendations](elsa-4-improvement-recommendations-2026-08.md). Supersedes the leaf-shape
assumption in [spec 114's research](../../specs/114-concurrency-throughput-instrument/research.md), which is the
current evidence base for the shared-writer bottleneck.

Instrument: `benchmarks/Elsa/Workflows/Runtime/Benchmarks/EngineConcurrencyBenchmarks.ConcurrencyScalingCurve`.

## Why this re-measurement exists

Spec 114 measured its N ∈ {1, 8, 32, 128} curve with `NoOpStep`, a `ReplaySafe` CLR leaf that exists only in the
benchmark assembly. The marking pass behind [ADR 0047](../adr/0047-replaysafe-activities-execute-as-fused-hops-with-precomputed-routing.md)
found that **no shipped leaf activity carries `[ActivitySideEffectProfile(ReplaySafe)]`**, so that curve described
the fusable floor rather than any shape a user can compose. The curve is now swept over three leaf shapes, with the
leaf class as the only variable:

| Leaf shape | What it stands for |
|---|---|
| **External CLR leaf** (`WriteLine`, unmarked ⇒ fail-safe `External`) | what almost every shipped leaf is, and what a workflow of HTTP calls / message sends / database writes costs |
| **`Set` intrinsic** | where Elsa 4's pure value work actually happens; fusable only since the blanket intrinsic exclusion became per-kind |
| **ReplaySafe CLR leaf** (`NoOpStep`) | the benchmark-only best case, kept as the reference bound |

## Method

N concurrent hot-loop×10 executions per level, N ∈ {1, 8, 32, 128}, over three backends (in-memory,
isolated-SQLite = one database per run, shared-SQLite = one database for all N). Durable backends run the shipping
configuration: Coalesced checkpoint persistence, segment cap 256, burst cache on. Per-provider setup is paid before
the timed window.

Two changes to the instrument's method were needed and both matter for reading the numbers:

1. **Sweep order is backend → N → leaf shape**, so the three shapes at a given (backend, N) are measured
   back-to-back. The comparison this report exists to make is *across leaf shapes at fixed N*, and the host's load
   drifts over a sweep this long. Pairing the shapes tightly is what keeps that comparison readable when the
   absolute walls are not. This is the same pairing discipline the spec 115 group-commit A/B uses.
2. **Console stdout is parked for the sweep.** The External leaf is a real `WriteLine`; without this, N×10 console
   writes would serialize on `Console`'s own global lock and be charged to the store writer.

`ITestOutputHelper` output is unaffected by (2), so the curve is still emitted.

### Measurement environment: read this before reading any wall time

- Machine: 8 logical processors, Apple Silicon (Mac14,2), Darwin 25.5.0. Debug build, as in spec 114.
- **The host was heavily loaded throughout, by unrelated concurrent work: 1-minute load average between ~230 and
  ~460 on 8 cores, for both runs.** This is worse than spec 114's own already-caveated capture (load ~13 → 30).
- **Absolute wall times and throughputs in this report are not usable as magnitudes.** Two runs of the identical
  binary differ by up to 3× on the same cell (shared-SQLite / N=32 / External: 359.7 s in run 1, 1039.2 s in run 2).
- **What survives: the deterministic counts, and the paired ratios.** Commit and dispatch counts are identical
  across both runs at every cell. The ratios drawn on below reproduced across the two runs; ratios that did not
  reproduce are marked as such and are not used as evidence.
- A clean capture on a quiet machine is still worth doing to firm up magnitudes. Every finding below is stated so
  that it rests on counts or on a ratio that reproduced, not on an absolute.

## Result 1: the deterministic counts (load-proof)

Identical at every N, on both durable backends, in both runs. These reproduce P1's single-run table exactly.

| Leaf shape | commits/run | dispatches/run |
|---|--:|--:|
| External CLR leaf (`WriteLine`) | **11** | **56** |
| `Set` intrinsic | 1 | 5 |
| ReplaySafe CLR leaf (`NoOpStep`) | 1 | 5 |

The External shape pays **11× the durable commits and 11.2× the scheduler dispatches** of either fusable shape, and
that ratio is flat in N. It is a per-run property of the leaf class, not something concurrency changes. This is the
part of the curve that can falsify its own claim: it separates "the engine is doing more work" from "the shared
writer is congested", and it says the extra work is real and constant.

The in-memory backend is a useful control: it reports **58 dispatches/run for both CLR shapes and 48 for `Set`**.
That is not a leaf-shape effect. `ReplaySafeFusionDriver.ShouldFuse` requires a live coalescing session, so fusion is
a burst-only optimization and the in-memory backend (runtime defaults, Immediate) never fuses at all. 58 is exactly
ADR 0047's pre-fusion baseline. The instrument's original header labelled all three backends "Coalesced+ReplaySafe";
that label was wrong for in-memory and is corrected here, because it invites reading a 58-vs-5 gap as a leaf effect
when it is a policy effect.

## Result 2: the External curve has no rising region

Shared-SQLite throughput (runs/s), the deployment shape. Spec 114's published `NoOpStep` curve is shown for
reference; the two 2026-08 runs are this capture.

| N | spec 114 `NoOpStep` (load ~13–30) | run 1 External | run 2 External | run 1 `Set` | run 2 `Set` | run 1 `NoOpStep` | run 2 `NoOpStep` |
|--:|--:|--:|--:|--:|--:|--:|--:|
| 1 | 2.2 | 0.5 | 0.1 | 1.7 | 0.5 | 0.7 | 0.6 |
| 8 | 3.5 | 0.3 | 0.1 | 4.8 | 6.0 | 4.4 | 3.2 |
| 32 | **5.6** | 0.1 | 0.03 | 7.4 | 1.0 | 2.1 | 1.1 |
| 128 | 1.5 | **FAULTED** | **FAULTED** | not reached | **FAULTED** | not reached | 0.7 |

The shape difference is the answer to the question this item asked:

- The `NoOpStep` curve **rises to a knee and then falls**: 2.2, 3.5, 5.6, 1.5, peaking at N=32. That knee is
  what an admission-control limiter sized off spec 114 would be set to.
- The **External curve never rises**. Peak throughput is at **N=1** in both runs, and every increase in N makes it
  worse. There is no concurrency at which the shared writer serves an External-dominated workflow better than
  serially. It is past its knee before the curve starts.

The fusable shapes still show a rising region under this load (peak at N=8 in both runs, earlier than spec 114's
N=32 because the host is far busier). So the difference is not "everything got slower"; the External shape lost the
rising region that the fusable shapes kept.

**The sharing penalty is also ~3× steeper, and this ratio reproduced.** Shared-SQLite wall ÷ isolated-SQLite wall at
the same N and leaf shape, which cancels most of the host-load effect because the two are measured minutes apart on
the same shape:

| N | External, run 1 | External, run 2 | `NoOpStep`, run 1 | `NoOpStep`, run 2 |
|--:|--:|--:|--:|--:|
| 8 | 2.2× | 2.3× | 0.25× | 0.21× |
| 32 | **9.9×** | **10.4×** | 4.1× | 1.5× |

At N=32 the External shape pays a ~10× penalty for sharing one writer, reproduced to within 5% across two runs three
hours apart at different host loads. The `NoOpStep` shape's penalty at the same N did not reproduce (4.1× vs 1.5×)
and is smaller in both runs. Below N=8 the fusable shapes are *faster* shared than isolated, because isolated pays
for N separate databases' WAL traffic; the External shape is already paying to share at N=8.

## Result 3: past a threshold the collapse stops being a throughput problem and becomes a fault

This is the finding that was not anticipated, and it is the one RB1 should be sized against.

At N=128 the External shape **did not complete**. It threw, and it threw on **all three backends**, with a different
exception on each:

| Backend | N=128 External outcome |
|---|---|
| in-memory | `RuntimeSchedulerWorkClaimLostException`, work claim lost during `renew`, status `Stale` |
| isolated-SQLite | `RuntimeStaleFencingTokenException`, checkpoint commit fenced out as `ExpiredLease` |
| shared-SQLite | `RuntimeExecutionOwnershipLostException`, ownership lease `Expired` while heartbeating |

Three different deadlines, one mechanism: each is a **wall-clock lease or claim that a drain must renew while its
work is queued**. `WorkflowDrainOrchestrator` renews ownership on a `LeaseDuration / 3` cadence against a default
`RuntimeExecutionOwnershipOptions.LeaseDuration` of one minute. Under enough offered concurrency the renewal itself
is starved (of the connection gate on the durable backends, of the thread pool on in-memory) and a **healthy
execution that was merely slow is converted into a failed one**.

The ordering across the three shapes, measured back-to-back in the same load window, is the load-robust part:

- **External faults at N=128 on every backend**, including in-memory where there is no store and no shared writer.
- **`Set` faulted at N=128 only on the contended shared writer**, and completed on the other two backends.
- **`NoOpStep` never faulted**, even at N=128 on the shared writer with a p50 of 163.9 s, i.e. a per-run latency
  nearly 3× the lease duration. Exceeding the lease duration does not itself cause the fault; being *starved of the
  renewal* does.

That ordering tracks dispatches/run exactly (56 / 5 / 5, and `Set` differs from `NoOpStep` only in also contending
on the writer), which is what makes it a work-volume effect rather than a coincidence.

**These faults are not deterministic.** In run 1, N=128 External completed on in-memory (31.7 s) and on
isolated-SQLite (69.1 s) and faulted only on the shared writer; in run 2, at higher load, it faulted on all three.
The threshold moves with host load, which is exactly what one expects of a wall-clock deadline with no admission
control in front of it. The claim supported by this evidence is *"there exists an offered concurrency at which the
External shape converts into lease-expiry faults, and it is lower than for either fusable shape"*, not a specific N.

## What this means for RB1 (#1235, admission control)

1. **Size the limiter off the External column, and expect a single-digit limit.** The External curve peaks at N=1 on
   a shared SQLite writer and falls monotonically. A limiter sized off spec 114's `NoOpStep` knee would be set
   around 32, roughly an order of magnitude too permissive for the shape production traffic actually has.
2. **The limiter's job is not only throughput; it is preventing lease-expiry faults.** RB1's brief says "Done when
   the P2 curve shows throughput plateauing rather than falling". That is necessary but not sufficient. The stronger
   acceptance criterion this capture supports: **beyond the limit, requests are shed with a visible refusal instead
   of being admitted and later failing with `RuntimeExecutionOwnershipLostException` / `RuntimeStaleFencingTokenException`
   / `RuntimeSchedulerWorkClaimLostException`.** Those three exceptions are the current, un-shed behaviour, and they
   are indistinguishable to a caller from a genuine ownership loss.
3. **Meter dispatches, not runs.** Commits and dispatches per run vary 11× with leaf shape and are deterministic
   per shape. A limit expressed in concurrent *runs* admits 11× more work for an External workflow than for an
   intrinsic one while reporting the same number. A limit expressed in in-flight scheduler dispatches is
   shape-invariant, and `RuntimeSchedulerDispatchDiagnostics` already counts exactly that.
4. **Do not size it against in-memory numbers.** The in-memory backend in this instrument runs unfused (Immediate),
   so its dispatch counts are ADR 0047's pre-fusion baseline and its walls are not the shipping path.

## Follow-ups this capture did not take

- **A quiet-machine capture.** Every magnitude here is inflated. The counts and the two reproduced ratios do not
  need it; the throughput magnitudes do.
- **Whether the N=128 lease-expiry faults survive on a quiet host**, and at what N the threshold sits when the host
  is not oversubscribed. This is the number RB1 would most like to have.
- **Shared-Postgres for the production shapes.** `ConcurrencyScalingCurve_SharedPostgres` still runs the `NoOpStep`
  leaf only, and is still capped by the stock container's `max_connections` at N=128 (spec 114 §Postgres).
- **The group-commit A/B (`ConcurrencyScalingCurve_GroupCommit`) also still runs `NoOpStep` only.** Spec 115 set the
  group-commit default to off because the win did not survive a quiet machine, measured at 1 commit/run. The
  External shape commits 11 times per run, so the folding opportunity is 11× larger and that default deserves
  re-testing against this shape before it is treated as settled.
