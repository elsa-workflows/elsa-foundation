# Research: group-commit / cross-drain fsync sharing (spec 115)

## Store-API evidence (why the Elsa-side seam is viable)

- **Fan-in is first-class.** `IDocumentStore.BeginAsync(DocumentCommitScope)` → one `IDocumentUnitOfWork`;
  arbitrarily many `SaveAsync`/`DeleteAsync` stage into it and one `CommitAsync` = one DB transaction.
  Elsa already fans ~15 document kinds of one checkpoint into one unit-of-work
  (`GroundworkRuntimeCheckpointWriter.ApplyAtomicallyAsync`). A gateway can fan N runs' checkpoints into
  one unit-of-work the same way. `GroundworkDocumentUnitOfWorkStore` is the adapter that routes writes
  into an already-open unit-of-work.
- **The SQLite single writer is a `SemaphoreSlim(1,1)` + one connection** with no group-commit layer of
  its own; consumed package `Groundwork.Sqlite 0.0.1-preview.77` runs `journal_mode=WAL`,
  `synchronous=NORMAL`, `busy_timeout=5000`. Collapsing N unit-of-work commits into one collapses N
  writer-gate acquisitions, N transactions and N WAL-append/commit cycles into one.
- **The local Groundwork clone is a different revision** than preview.77 (missing the WAL/synchronous
  pragma code), so a Groundwork-side change could neither reflect production nor be E2E-measured from this
  repo — a second reason the Elsa-side seam is the right one.
- **Constraints honored:** the unit-of-work scope resolver forbids a mixed-tenant transaction (batching
  keys on tenant); the unit-of-work is all-or-nothing (one member's conflict poisons the batch → the
  gateway rolls back and re-drives each member individually).

## Measurement — the first design was wrong, and the instrument caught it

**Instrument:** `EngineConcurrencyBenchmarks.ConcurrencyScalingCurve_GroupCommit` — shared-SQLite A/B
(group commit OFF vs ON), N concurrent 10-activity hot loops, one shared coordinator across all harnesses
(models the single host-wide coordinator). The coordinator's batch counters are deterministic, load-proof
evidence of whether folding actually happened.

### Round 1 — release-gate-before-commit design: ZERO batching (deterministic)

The first coordinator released the flush gate *before* doing the durable commit (the commit ran in the
caller's fallback, outside the gate). Result at **every** level N ∈ {1,8,32,128}:
`BatchFlushCount = 0`, `BatchedMemberCount = 0`, `SoloFlushCount = N`. **The coordinator never folded a
single pair.** Cause: with the commit outside the gate, a leader holds the gate for only microseconds, so
concurrent runs almost never coincide at the gate — each run flushed solo. This is the classic group-commit
pitfall: the flush must happen *under* the serialization point, not after it.

### Round 2 — flush-pipeline design (commit under the gate): batching engages strongly

Holding the gate across the durable flush (like a database log mutex) makes arriving commits accumulate
while a leader commits, so the next leader folds them. Deterministic counters (load-proof), one paired run:

| N | batchFlushes | batchedMembers | soloFlushes | agg markers | commits saved (members−flushes) |
|---:|---:|---:|---:|---:|---:|
| 1 | 0 | 0 | 1 | 1 | 0 (solo, no regression) |
| 8 | 3 | 6 | 2 | 8 | 3 |
| 32 | 5 | 22 | 10 | 32 | 17 |
| 128 | 12 | 123 | 5 | 128 | **111** |

At N=128, **123 of 128 concurrent commits folded into 12 transactions** — a ~10× reduction in durable
transactions for the same work, with per-run marker count still exactly 1/run (correctness preserved). The
fraction folded rises with N, the signature of a working group commit: the more contended the single
writer, the more sharing.

### Wall time — heavily load-contaminated; paired ratios below

The machine was under severe, swinging ambient load throughout (1-min load 28–115 on 8 cores), so absolute
walls and single-order A/B pairs are unreliable — exactly the caveat spec 114 flagged. A single-order pass
showed N=32 ON 1.5× faster but N=8/128 confounded by ON drawing the higher-load slot. The final instrument
therefore measures **paired OFF/ON back-to-back with order alternated across repeats**; the load-robust
signal is the per-pair ON/OFF ratio, read together with the deterministic fold counts.

### Round 3 — paired pass under load 400+: discarded

An order-alternated paired pass ran while other sessions drove the machine to **1-min load 400+**. Walls
were 25–50× inflated and the run failed with a `RuntimeExecutionOwnershipLostException` — the
ownership-lease *heartbeat* background task was CPU-starved so the lease expired mid-drain. Environmental
artifact of the load, not a group-commit defect (the heartbeat renewal is independent of the commit path,
and the pre-existing `ConcurrencyScalingCurve` benchmark shares the same vulnerability). Discarded.

### Round 4 — paired OFF/ON, order-alternated ×3 (authoritative walls)

Re-run after the ambient load subsided: started at 1-min load ~164 and **fell to ~15 by the end**, so the
levels run later (N=32, then N=128 last) are progressively cleaner; N=128 rep 2 is the cleanest pair
captured. Each row is one back-to-back OFF/ON pair; `ON/OFF` < 1 means group commit is faster.

| N | rep | order | OFF wall (ms) | ON wall (ms) | ON/OFF | batchFlushes | batchedMembers | soloFlushes | degraded | markers/run |
|---:|---:|---|--:|--:|--:|--:|--:|--:|--:|--:|
| 1 | – | probe | 3 188 | 3 337 | 1.05 | 0 | 0 | 1 | 0 | 1.0 |
| 8 | 0 | OFF,ON | 10 548 | 6 580 | 0.62 | 0 | 0 | 8 | 0 | 1.0 |
| 8 | 1 | ON,OFF | 4 617 | 8 858 | 1.92 | 0 | 0 | 8 | 0 | 1.0 |
| 8 | 2 | OFF,ON | 4 123 | 5 602 | 1.36 | 0 | 0 | 8 | 0 | 1.0 |
| 32 | 0 | OFF,ON | 24 325 | 24 209 | 1.00 | 6 | 14 | 18 | 0 | 1.0 |
| 32 | 1 | ON,OFF | 10 791 | 13 614 | 1.26 | 5 | 13 | 19 | 0 | 1.0 |
| 32 | 2 | OFF,ON | 5 637 | 12 107 | 2.15 | 5 | 13 | 19 | 0 | 1.0 |
| 128 | 0 | OFF,ON | 133 127 | 89 082 | **0.67** | 13 | 120 | 8 | 0 | 1.0 |
| 128 | 1 | ON,OFF | 82 967 | 80 399 | 0.97 | 9 | 120 | 8 | 0 | 1.0 |
| 128 | 2 | OFF,ON | 63 525 | 44 436 | **0.70** | 18 | 119 | 9 | 0 | 1.0 |

Reading (walls carry the load caveat; counters are deterministic):

- **N=128 — the regime the unit targets — shows a consistent win**: 119–120 of 128 commits fold into 9–18
  shared transactions every rep, and walls improve 3–33% (ratios 0.67 / 0.97 / 0.70; geometric mean ≈ 0.77,
  ≈ 1.3× throughput). The cleanest pair (rep 2, lowest ambient load) is 63.5 s → 44.4 s.
- **N=32 is inconclusive-to-negative in this pass** (1.00 / 1.26 / 2.15) with partial folding (13–14/32).
  The 2.15 outlier rode an OFF draw of 5.6 s — far below every other N=32 OFF sample — under still-elevated
  load; but the direction at N=32 cannot honestly be claimed as a win from this data.
- **N=8 never batched in this pass** (soloFlushes = 8 in all reps): at low concurrency under load, commits
  rarely coincide at the gate, so ON ≈ OFF plus noise. (Round 2, at lower load, did fold 6/8 once — batching
  at N=8 is possible but marginal.)
- **N=1**: solo path never batches (soloFlushes=1, batchFlushes=0); the +5% delta is single-sample noise
  under load ~160 (round 2's cleaner probe measured +1.6%).
- **Correctness invariants held everywhere**: markers exactly 1/run at every level and rep (aggCommits = N),
  zero degraded batches, all workflows completed.

## QA evidence

- `dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj` —
  **642/642 passed** (includes the restart/crash-convergence and redelivery contracts and the new
  GroupCommit tests), re-run green after the degrade/cancellation-isolation fix.
- `dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj -c Release` —
  **1362/1362 passed** (writer refactor is behavior-preserving with no coordinator registered).
- `dotnet build Elsa.Server.slnx` — **succeeded** (constructor change is trailing-optional; no call-site
  breaks anywhere in the solution).

## Verdict

**The group-commit mechanism works and is correct.** Deterministic, load-proof evidence across every
measured pass: under saturation it folds ~94% of concurrent commits into shared transactions (119–123 of
128 into 9–18 transactions, a ~7–14× reduction in durable transactions) while keeping exactly 1 durable
marker per run, byte-identical state, failure isolation (poison → per-member re-drive under each member's
own token), and a solo path that never batches or waits. The correctness unit tests
(`GroundworkRuntimeCheckpointWriterTests.GroupCommit`) pass, and both full suites are green.

**The throughput win is real in the saturated regime but not uniform.** At N=128 — precisely the regime
spec 114 identified (3× collapse on the shared single writer) — paired walls improve consistently
(ON/OFF ≈ 0.67–0.97, ≈ 1.3× throughput on the geometric mean, ~1.4× on the cleanest pair). At N=32 the
pass is inconclusive-to-negative; at N=8 batching rarely forms. Because the win is not uniform across the
curve and the engine-perf bar for a *default-on* change is high, group commit lands **opt-in, default off**
(`AddGroundworkRuntimeGroupCommit`), with the reproducible A/B instrument
(`ConcurrencyScalingCurve_GroupCommit`) retained so a quiet-machine run — ideally also probing mid-N more
finely — can decide the default. This matches the campaign's discipline of gating perf defaults on
trustworthy measurement.

## Quiet-machine follow-up (2026-07-21) — the default stays OFF

The verdict above deferred the default to "a quiet-machine run — ideally also probing mid-N more
finely". That run has now happened, post-merge (PR #916), and it settles the question.

**Conditions.** 8 cores; ambient 1-min load 2.7–4.8 at the start of each pass (waited out a
load-38–56 iCloud sync storm before starting), versus 28–400+ in every prior round. The A/B levels
were extended to N ∈ {8, **16**, 32, **64**, 128} and the paired, order-alternated instrument was run
three full times (3 reps/level/pass → 9 pairs per level, 6 at N=128). Pass 1 aborted before its
N=128 level on a benchmark-infra race unrelated to group commit: the paged
`IWorkflowExecutableStore.ListAllAsync` traversal returned the same artifact row twice while 128
concurrent harnesses were inserting executables, tripping the dependency-graph
`ConflictingIdentity` guard (`more than one artifact with ID 'artifact-85'`). Filed as its own
follow-up; passes 2 and 3 completed all levels.

**All quiet pairs** (`ON/OFF` < 1 = group commit faster; folding columns are the ON run's counters):

| N | pass.rep | order | OFF wall (ms) | ON wall (ms) | ON/OFF | batchFlushes | batchedMembers | soloFlushes | degraded | markers/run |
|---:|---:|---|--:|--:|--:|--:|--:|--:|--:|--:|
| 8 | 1.0 | OFF,ON | 399 | 438 | 1.10 | 1 | 2 | 6 | 0 | 1.0 |
| 8 | 1.1 | ON,OFF | 571 | 537 | 0.94 | 0 | 0 | 8 | 0 | 1.0 |
| 8 | 1.2 | OFF,ON | 471 | 485 | 1.03 | 0 | 0 | 8 | 0 | 1.0 |
| 8 | 2.0 | OFF,ON | 416 | 407 | 0.98 | 1 | 3 | 5 | 0 | 1.0 |
| 8 | 2.1 | ON,OFF | 630 | 425 | 0.67 | 1 | 2 | 6 | 0 | 1.0 |
| 8 | 2.2 | OFF,ON | 395 | 427 | 1.08 | 0 | 0 | 8 | 0 | 1.0 |
| 8 | 3.0 | OFF,ON | 481 | 456 | 0.95 | 2 | 6 | 2 | 0 | 1.0 |
| 8 | 3.1 | ON,OFF | 485 | 539 | 1.11 | 0 | 0 | 8 | 0 | 1.0 |
| 8 | 3.2 | OFF,ON | 557 | 485 | 0.87 | 0 | 0 | 8 | 0 | 1.0 |
| 16 | 1.0 | OFF,ON | 1 075 | 1 222 | 1.14 | 0 | 0 | 16 | 0 | 1.0 |
| 16 | 1.1 | ON,OFF | 769 | 993 | 1.29 | 1 | 2 | 14 | 0 | 1.0 |
| 16 | 1.2 | OFF,ON | 930 | 869 | 0.93 | 2 | 4 | 12 | 0 | 1.0 |
| 16 | 2.0 | OFF,ON | 994 | 914 | 0.92 | 3 | 6 | 10 | 0 | 1.0 |
| 16 | 2.1 | ON,OFF | 902 | 684 | 0.76 | 1 | 3 | 13 | 0 | 1.0 |
| 16 | 2.2 | OFF,ON | 605 | 1 639 | 2.71 | 1 | 2 | 14 | 0 | 1.0 |
| 16 | 3.0 | OFF,ON | 1 375 | 902 | 0.66 | 3 | 7 | 9 | 0 | 1.0 |
| 16 | 3.1 | ON,OFF | 923 | 890 | 0.96 | 2 | 6 | 10 | 0 | 1.0 |
| 16 | 3.2 | OFF,ON | 926 | 1 002 | 1.08 | 1 | 4 | 12 | 0 | 1.0 |
| 32 | 1.0 | OFF,ON | 3 096 | 3 336 | 1.08 | 5 | 14 | 18 | 0 | 1.0 |
| 32 | 1.1 | ON,OFF | 1 513 | 2 840 | 1.88 | 3 | 6 | 26 | 0 | 1.0 |
| 32 | 1.2 | OFF,ON | 3 273 | 2 659 | 0.81 | 3 | 7 | 25 | 0 | 1.0 |
| 32 | 2.0 | OFF,ON | 2 010 | 1 820 | 0.91 | 5 | 22 | 10 | 0 | 1.0 |
| 32 | 2.1 | ON,OFF | 1 776 | 3 278 | 1.85 | 4 | 15 | 17 | 0 | 1.0 |
| 32 | 2.2 | OFF,ON | 1 546 | 1 977 | 1.28 | 4 | 8 | 24 | 0 | 1.0 |
| 32 | 3.0 | OFF,ON | 3 446 | 1 622 | 0.47 | 6 | 25 | 7 | 0 | 1.0 |
| 32 | 3.1 | ON,OFF | 2 482 | 1 699 | 0.68 | 6 | 20 | 12 | 0 | 1.0 |
| 32 | 3.2 | OFF,ON | 2 986 | 3 380 | 1.13 | 2 | 4 | 28 | 0 | 1.0 |
| 64 | 1.0 | OFF,ON | 12 942 | 7 822 | 0.60 | 9 | 54 | 10 | 0 | 1.0 |
| 64 | 1.1 | ON,OFF | 7 162 | 4 341 | 0.61 | 8 | 56 | 8 | 0 | 1.0 |
| 64 | 1.2 | OFF,ON | 5 081 | 7 600 | 1.50 | 10 | 42 | 22 | 0 | 1.0 |
| 64 | 2.0 | OFF,ON | 3 919 | 4 308 | 1.10 | 11 | 48 | 16 | 0 | 1.0 |
| 64 | 2.1 | ON,OFF | 6 790 | 5 578 | 0.82 | 12 | 38 | 26 | 0 | 1.0 |
| 64 | 2.2 | OFF,ON | 5 661 | 5 133 | 0.91 | 10 | 41 | 23 | 0 | 1.0 |
| 64 | 3.0 | OFF,ON | 13 094 | 9 564 | 0.73 | 15 | 42 | 22 | 0 | 1.0 |
| 64 | 3.1 | ON,OFF | 6 402 | 6 848 | 1.07 | 14 | 43 | 21 | 0 | 1.0 |
| 64 | 3.2 | OFF,ON | 10 161 | 10 983 | 1.08 | 9 | 27 | 37 | 0 | 1.0 |
| 128 | 2.0 | OFF,ON | 14 662 | 23 882 | 1.63 | 22 | 114 | 14 | 0 | 1.0 |
| 128 | 2.1 | ON,OFF | 21 294 | 15 989 | 0.75 | 14 | 117 | 11 | 0 | 1.0 |
| 128 | 2.2 | OFF,ON | 19 720 | 25 530 | 1.29 | 13 | 110 | 18 | 0 | 1.0 |
| 128 | 3.0 | OFF,ON | 37 363 | 28 840 | 0.77 | 23 | 107 | 21 | 0 | 1.0 |
| 128 | 3.1 | ON,OFF | 36 335 | 32 193 | 0.89 | 23 | 99 | 29 | 0 | 1.0 |
| 128 | 3.2 | OFF,ON | 25 101 | 21 434 | 0.85 | 10 | 119 | 9 | 0 | 1.0 |

N=1 solo probes (one per pass): OFF 78/70/112 ms vs ON 79/71/95 ms — no solo regression,
`soloFlushes=1, batchFlushes=0` every time.

**Per-level summary of the ON/OFF wall ratio:**

| N | pairs | geomean | median | min–max | folded members (of N) | t vs 1.0 (ln ratios) |
|---:|---:|--:|--:|--:|--:|--:|
| 8 | 9 | 0.96 | 0.98 | 0.67–1.11 | 0–6 | −0.78 (n.s.) |
| 16 | 9 | 1.07 | 0.96 | 0.66–2.71 | 0–7 | +0.44 (n.s.) |
| 32 | 9 | 1.03 | 1.08 | 0.47–1.88 | 4–25 | +0.19 (n.s.) |
| 64 | 9 | 0.90 | 0.91 | 0.60–1.50 | 27–56 | −1.07 (n.s.) |
| 128 | 6 | 0.99 | 0.87 | 0.75–1.63 | 99–119 | −0.11 (n.s.) |

**Reading:**

- **The folding mechanism behaves exactly as designed, and its engagement scales with N**: rare at
  N ≤ 16, partial at 32, strong at 64 (~40–90%), near-total at 128 (77–93% of members fold, into
  10–23 flushes, mean batch ≈ 5–12 members — nowhere near the `MaxBatchSize = 64` cap; zero degraded
  batches; markers exactly 1/run at every level, pair, and pass).
- **But the wall-clock win does not survive the quiet machine.** No level's ratio distribution is
  statistically distinguishable from 1.0. N=64 is the most favorable (geomean 0.90) and N=128's
  median leans positive (0.87), but both spreads span well past 1.0 in 15 pairs. Round 4's
  consistent N=128 win (0.67–0.97) was measured under heavy ambient load and does not reproduce:
  quiet N=128 pairs include 1.63 and 1.29.
- **Why near-total folding buys ~nothing here**: the consumed SQLite provider runs
  `journal_mode=WAL, synchronous=NORMAL`, so an individual commit is a WAL append without a
  guaranteed per-commit fsync — the per-transaction cost group commit amortizes is already small.
  Meanwhile 8 cores driving N ≥ 64 concurrent drains are CPU-saturated (per-pair walls swing up to
  ~2.5× between passes from scheduling alone), and batched members must wait for their leader's
  flush before completing. The saved writer-gate acquisitions and the added gate wait roughly cancel.
- Within-pair ratios remain the only trustworthy wall signal even when quiet — absolute walls at
  N=128 swung 14.7–37.4 s (OFF) across passes as ambient load crept from ~2.7 to ~4.8.

**Recommendation: keep `AddGroundworkRuntimeGroupCommit` opt-in, default OFF.** The quiet-machine
curve gives no statistically defensible throughput win at any N on this hardware, so the default-on
bar set by the verdict above is not met. `MaxBatchSize = 64` remains a fine default for opt-in users
(observed batches never approached it). The opt-in stays valuable where it was designed to pay:
deployments whose durable store has expensive per-commit fsyncs (`synchronous=FULL`, network
filesystems, or a future provider without WAL) — re-run this instrument in that configuration before
enabling. If a later unit wants to move shared-writer throughput on this configuration, the lever is
the round-trip count, not the commit count (see the note below).

### Note for the next unit

Spec 114's 3× shared-vs-isolated gap is the *aggregate* single-writer serialization, of which the
checkpoint commit is only one of several per-run store round-trips (marker pre-read + root-write-lease
acquire/release also serialize on the same SQLite connection gate). Group commit folds the commits; a
complementary lever is to **reduce the number of serialized store round-trips per run** (e.g. fold the
lease touch and marker read into the checkpoint transaction), which attacks the same bottleneck from the
other side.
