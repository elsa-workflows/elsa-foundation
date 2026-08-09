# checkpoint-commit store performance — 2026-08

First real measurement from the #646 store-performance matrix. Until now the matrix had never run:
`BenchmarkAdapterFactory` carried no leaves, so every workload was a blocked run rather than a measured
one. Issue #1175 registered the `checkpoint-commit` leaf; this records what it measured.

## What this is, and what it is not

One measurement set per provider — SQLite and PostgreSQL — for the `checkpoint-commit` workload at its
frozen v1.1 input. Each set is one untimed warm-up process plus three measured processes, which is the
ratified acceptance protocol (`>=100` invocations and `>=30` seconds per operation).

**No `performanceVerdict` is produced, and none can be.** `Comparison.Compare` requires eight artifacts
across two measurement sets that differ only by *physical form*, and those form labels
(`shared-documents-with-linked-index-tables`, `document-type-specific-tables`, …) have no binding in
`src/` — Groundwork ships one shape per store. `compare` and `gate` were run against the completed SQLite
set and both refused:

```
compare  Complete: false   BlockReason: "A comparison cohort requires exactly two distinct
                                        complete four-process measurement sets."
gate     Verdict: Blocked  Reason:      (same)
```

That is the correct outcome, not a defect — and note both commands exit 0, because they *record* the block
rather than failing. **This advances no coverage-ledger row.**

What the run does supply is the Tier B input that
[runtime-absolute-budget-basis.md](../../specs/094-harden-groundwork-stores/contracts/runtime-absolute-budget-basis.md)
asks for. Tier B ceilings need only one measurement set — median-of-three p95 × 3 — and that document's
sunset condition asks specifically for production-representative providers, which PostgreSQL satisfies and
SQLite does not.

## How to read the numbers

`ProcessMeasurement.MeasureAsync` times everything inside `InvokeAsync`. The named phase is *not* the timed
unit: one execution of `commit-checkpoint-bundle` as the workload defines it is 1024 immediate durable
commits, which at 100 invocations would run for hours.

So each of the five named phases of the frozen `operationSequence` is timed as the **elementary public
store call it is composed of**, against rows seeded outside the timing window. Every workload input carries
`"timedSetup": "excluded"`, which is the contract's own statement that setup is not measured.

| operation | timed call |
|---|---|
| `seed-fenced-executions` | `IRuntimeExecutionOwnershipService.AcquireAsync` |
| `commit-checkpoint-bundle` | `IRuntimeCheckpointCommitStore.CommitAsync`, one representative bundle |
| `replay-equivalent-commit` | `CommitAsync` re-submitting an already-committed bundle |
| `attempt-stale-fence-commit` | `CommitAsync` with a superseded fence, rejected |
| `reopen-and-read-committed-bundle` | `IWorkflowExecutionStateStore.FindAsync` |

The bundle shape matches the frozen scenario's: four activity changes, three durable values, two outbox
intents, 512-byte payload.

Correctness is not sampled — it is a gate. All eight processes ran the complete frozen scenario once, in
its own storage scope, and reproduced the ratified digest
`ebb92b59a7a331e863c813f7110272093be6a78794a9cc7a0d914103ab4c9c62` before being allowed to time anything.

All figures below are milliseconds, and are the median across the three measured processes of that
process's own percentile — the same statistic `GateEvaluator.Evaluate` uses.

## SQLite — `file-backed-distinct-connections`, engine 3.50.4

| operation | p50 | p95 | p99 | ops/s | samples/process |
|---|---:|---:|---:|---:|---|
| `seed-fenced-executions` | 24.628 | 26.984 | 31.933 | 41.98 | 1230 / 1260 / 1274 |
| `commit-checkpoint-bundle` | 69.842 | 75.066 | 80.962 | 14.27 | 429 / 413 / 430 |
| `replay-equivalent-commit` | 0.851 | 1.081 | 1.430 | 1112.62 | 33379 / 32293 / 33708 |
| `attempt-stale-fence-commit` | 65.584 | 71.110 | 74.590 | 15.18 | 455 / 456 / 467 |
| `reopen-and-read-committed-bundle` | 0.734 | 0.799 | 0.921 | 1332.95 | 39159 / 39989 / 40022 |

## PostgreSQL — `real-postgresql-container`, `postgres:17.6-alpine3.22`, engine 17.6

| operation | p50 | p95 | p99 | ops/s | samples/process |
|---|---:|---:|---:|---:|---|
| `seed-fenced-executions` | 27.113 | 33.223 | 35.179 | 35.88 | 1072 / 1088 / 1077 |
| `commit-checkpoint-bundle` | 84.690 | 93.931 | 100.643 | 11.77 | 354 / 353 / 354 |
| `replay-equivalent-commit` | 0.254 | 0.338 | 0.546 | 3741.03 | 110659 / 113502 / 112232 |
| `attempt-stale-fence-commit` | 84.434 | 91.642 | 94.110 | 11.91 | 356 / 360 / 358 |
| `reopen-and-read-committed-bundle` | 0.161 | 0.246 | 0.267 | 5882.60 | 176230 / 177131 / 176478 |

## Derived Tier B ceilings

Median-of-three p95 × 3, which is the derivation method the budget document ratifies.

| operation | SQLite | PostgreSQL |
|---|---:|---:|
| `seed-fenced-executions` | 81.0 | 99.7 |
| `commit-checkpoint-bundle` | 225.2 | 281.8 |
| `replay-equivalent-commit` | 3.2 | 1.0 |
| `attempt-stale-fence-commit` | 213.3 | 274.9 |
| `reopen-and-read-committed-bundle` | 2.4 | 0.7 |

These are **recorded, not ratified**. Replacing the standing 150 ms / 40 ms class ceilings needs an
independent ratifier, so that document is unchanged and only points here.

Against the standing ceilings, nothing here fails: the durable write path measures 75 ms (SQLite) and 94 ms
(PostgreSQL) p95 against a 150 ms ceiling, and the bounded read measures 0.8 ms and 0.25 ms against 40 ms.

## What the numbers show

**PostgreSQL writes slower and reads faster — but the two are not configured symmetrically, so read the
cross-provider gap with care.** The durable commit costs 94 ms against SQLite's 75 ms. The bounded read is
0.25 ms against 0.80 ms, and idempotent replay 0.34 ms against 1.08 ms.

The confound: the SQLite provider driver sets `Pooling = false`
([SqliteGroundworkProviderDriver.cs](../../tests/Elsa/Persistence/Groundwork/Testing/SqliteGroundworkProviderDriver.cs)),
while the PostgreSQL driver sets no pooling override and so gets Npgsql's default pool. Every SQLite
operation therefore pays connection establishment that PostgreSQL amortizes. That asymmetry is not
corrected here on purpose — the benchmark deliberately reuses the same driver that produces the Spec 094
conformance evidence, and re-tuning it for the benchmark would decouple the two.

**A networked PostgreSQL beating a local SQLite on a primary-key read inverts the expected ordering, and
this measurement does not explain why.** Unpooled connection setup is the obvious candidate and is roughly
the right magnitude, but that is a hypothesis, not a result: no experiment here isolates it. Treat the
per-provider figures as sound and the cross-provider *ratio* as unresolved until someone measures the two
under matched connection settings.

What survives the caveat is the shape the budget document predicted: per-provider numbers diverge, they do
not diverge uniformly in one direction, and that is the argument for keeping Tier B a blunt backstop rather
than a precision instrument. Tier B ceilings are per-provider, so each column stands on its own regardless
of how the comparison resolves.

**A rejected commit costs almost as much as an accepted one.** `attempt-stale-fence-commit` measures 71 ms
against 75 ms on SQLite (95%) and 92 ms against 94 ms on PostgreSQL (98%). A stale fence is detected and
rejected, and the workload confirms no state is written — yet the rejection path pays essentially the full
cost of the commit it refuses. Whatever work precedes the fence decision is not being skipped. Worth a look
on its own; it is not a correctness problem, and this measurement does not diagnose it.

**Idempotent replay is genuinely cheap.** 1.08 ms and 0.34 ms, two orders of magnitude below the accepted
commit. The commit marker short-circuits before the write path, which is what the design intends.

## Findings that are not numbers

**The frozen scenario cannot run against a real durable provider at the default lease duration.** It
acquires all 128 execution leases up front, then heartbeats a given execution only every 128th commit.
Under the default one-minute `RuntimeExecutionOwnershipOptions.LeaseDuration` the earliest leases have
already expired when their turn comes, and `HeartbeatAsync` returns `Expired`. The first cohort aborted
exactly there. The in-memory double the benchmark tests use never shows this, because its commits take
microseconds. The leaf sizes the lease to the run; that weakens no asserted invariant, because the scenario
proves stale-fence rejection through token supersession, never through expiry.

**The SQLite provider driver runs with `journal_mode = delete`, not WAL.** Verified by reading the live
database mid-run. This matters for comparison: the 35.7 ms per-commit anchor in the budget document came
from `runtime-http-performance-2026-07.md`, which measured the *portable* store factory in WAL mode. The
75 ms here is the *physical* target — shared documents plus linked index tables — on a rollback journal.
The two are not like-for-like, and the gap is explained by physical shape and journal mode, not only by the
code under test.

**These numbers describe the physical target, which is the point.** The pre-existing runtime benchmark at
`benchmarks/Elsa/Workflows/Runtime/Benchmarks` composes a *logical* store wrapped in a test bounded-store
adapter, with a hardcoded provider version. That path has no compiled physical target and no route
admission, so it cannot describe the system the Spec 094 conformance evidence describes. The leaf uses
`ResetPhysicalAsync` + `OpenPhysicalClientAsync`, the same driver path the conformance suites use.

**A quiet machine is not advice, it is a precondition — and SQLite is far more sensitive to this than
PostgreSQL.** An early attempt was measured while this session was concurrently reading the live SQLite
file to check progress: the correctness phase took 9m20s under that load and 1m53s without it, a 5×
distortion from read-only observation alone. Later attempts on a host running at a load average of 60–150
(8 cores) produced a `commit-checkpoint-bundle` p95 of 328–404 ms against the 75 ms recorded here. Over the
same period PostgreSQL was unaffected and reproduced its numbers to three decimals — its work happens
inside its container, while SQLite contends for host fsync.

**The contamination signature is per-process sample counts diverging inside a single cohort.** The figures
above were taken with `[429, 413, 430]` samples across the three measured processes; the discarded runs
showed `[199, 112, 415]` and `[254, 100, 142]`, with one process barely clearing the 100-sample floor.
Check that spread before trusting a set — the harness will not check it for you. `GateEvaluator.Complete`
requires at least 100 samples per process and never compares processes to each other, so a load-contaminated
cohort is admitted silently.

## Provenance

| | |
|---|---|
| commit | `93e441b72e4dcefb91527ad63828d712216794fe` |
| harness assembly | `5f7fb83d5c07fdb3715a3702c7a6e2c5dfa1855dd941d09a5ff493ae05258871` |
| composition fingerprint | `19a802ff10fa756be64e7b5bc787ea3af2ab5d4d47fc7de22993598a5871db54` |
| host fingerprint | `9d8f1d014c7b81873534bf82b81756b0c441645541cd1c475a9345c2a308b153` |
| machine | macOS 26.5.2, Arm64, 8 CPUs, .NET 10.0.8 |
| cohort / scale | `tierb-001` / `frozen-v1.1` |
| adapter / form | `groundwork` / `shared-documents-with-linked-index-tables` |
| Groundwork packages | `Core`, `Documents`, `Sqlite`, `PostgreSql` all `0.0.1-preview.103` |
| SQLite manifest | `e8faff0a1d42d0614268b4222dfc4c103087a384b617b8b11394d71d2f7f42dd` |
| PostgreSQL manifest | `32c5e4e526fea153b66c0f1f8ab800bbb77d20cb8747c9fe010a419de627d8c4` |

**The measurement commit is an ancestor of the branch tip, and that is intended.** Everything committed
after `93e441b72` is documentation; `CheckpointCommitAdapter.cs` is byte-identical between that commit and
the tip, verifiable with `git diff 93e441b72 HEAD -- <that file>`. Evidence records the commit it was taken
at, so re-measuring for a documentation change would only churn the numbers. A change to the *leaf* is a
different matter and must re-measure both providers in the same PR — see issue #1198.

The physical form is a **label**, not a selected shape: Groundwork ships one shape per store, and the label
exists only so a future two-form comparison has something to name.

Raw artifacts are not committed — five operations × up to 177k samples × three processes × two providers is
several megabytes of latency arrays. The two `artifact-manifest.v2.json` files are committed instead, under
[checkpoint-commit-store-performance-2026-08/](checkpoint-commit-store-performance-2026-08/); each binds
every artifact in its set by SHA-256, so the sets are attested without the bulk.

## Reproducing

Artifact and staging directories must live outside the worktree — every child re-verifies a clean HEAD with
`--untracked-files=all`. Build the host and the harness in the same configuration, because each child
re-verifies the harness assembly digest. The adapter host's README carries the three-step recipe:
`probe-provider` → `capture-plan` → `matrix`.

A partially completed cohort is **not resumable** by design: `RunCoreAsync` requires any preexisting
artifact directory to hold one *complete* four-artifact set carrying a different measurement-set id. Wipe
the directory and re-run.
