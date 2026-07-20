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

### Paired OFF/ON (order-alternated repeats) — not usable, machine saturated

The order-alternated paired pass ran while other sessions drove the machine to **1-min load 400+**. Walls
were 25–50× inflated (N=1 solo alone took 17–37 s vs ~0.67 s in round 2) and the run ultimately failed
with a `RuntimeExecutionOwnershipLostException` — the ownership-lease *heartbeat* background task was
CPU-starved so the lease expired mid-drain. This is an **environmental artifact of the load, not a
group-commit defect** (the heartbeat renewal is independent of the commit path, and the pre-existing
`ConcurrencyScalingCurve` benchmark shares the same high-load vulnerability). No wall signal is salvageable
from this pass; **round 2 is the authoritative measurement** and a clean-machine re-run is required to size
the throughput win.

## Verdict

**The group-commit mechanism works and is correct.** Deterministic, load-proof evidence: under concurrency
it folds a large and N-growing fraction of commits into shared transactions (123/128 at N=128, ~10× fewer
durable transactions) while keeping exactly 1 durable marker per run, byte-identical state, failure
isolation (poison → per-member re-drive), and no N=1 regression. The correctness unit tests
(`GroundworkRuntimeCheckpointWriterTests.GroupCommit`) pass.

**The throughput win is directionally positive but not cleanly quantified** on this heavily contended
shared machine: a single-order pass showed N=32 ON 1.5× faster with N=8/128 confounded by run-order under
swinging load; the paired pass ran under load 400+ and is not trustworthy for walls. Because the win could
not be firmly sized and the engine-perf bar for a *default-on* change is high, group commit lands **opt-in,
default off** (`AddGroundworkRuntimeGroupCommit`), with the reproducible A/B instrument
(`ConcurrencyScalingCurve_GroupCommit`) retained so a clean-machine run can decide on enabling it by
default. This matches the campaign's discipline of gating perf defaults on trustworthy measurement.

### Note for the next unit

Spec 114's 3× shared-vs-isolated gap is the *aggregate* single-writer serialization, of which the
checkpoint commit is only one of several per-run store round-trips (marker pre-read + root-write-lease
acquire/release also serialize on the same SQLite connection gate). Group commit folds the commits; a
complementary lever is to **reduce the number of serialized store round-trips per run** (e.g. fold the
lease touch and marker read into the checkpoint transaction), which attacks the same bottleneck from the
other side.
