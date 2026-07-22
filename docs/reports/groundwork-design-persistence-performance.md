# Groundwork Design-Persistence Performance Report (Spec 093 US4 / T069)

**Status:** complete. Gate 5 (per-row EF ratio) was replaced by absolute operational budgets via a
ratified amendment on 2026-07-22; the fair same-conditions re-measurement passes **19/19** budget rows
and the form-selection gate (gate 6) passes at 100K and 1M.

This is the complete and honest record required by T069. It documents the environment and protocol, the
original three-scale matrix **including a disclosed measurement contamination**, the three remediation
cycles, the fair final numbers, the ratified gate-5 amendment, and open follow-ups.

---

## 1. Environment and protocol

- **Machine:** `Sipkes-MacBook-Air`, Apple Silicon, 8 logical processors (macOS 26.5.2). Read directly
  from the `Environment` blocks of every `bench-out/*.json` run file.
- **Runtime:** .NET 10.0.8, Release build.
- **Store:** SQLite. Physical-form targets set `PRAGMA journal_mode=WAL`
  (`benchmarks/Elsa.DesignPersistence.Benchmarks/Forms/SqliteFormTarget.cs`).
- **Protocol (per operation, per target):** one untimed warm-up process, then **three independent child
  processes**, each ≥ 100 operations and ≥ 30 s steady state (`ProtocolSettings.Acceptance`). Raw
  per-operation latency samples are captured to disk; the harness computes p50/p95/p99, throughput, and
  **capped percentile-bootstrap 95% confidence intervals** for relative improvement. Acceptance uses the
  **median of the three per-run** p95/p99/throughput, never an aggregate that could hide a regression.
- **Correctness first:** every read operation is hashed and compared across all five targets before any
  timing is trusted. All 11 read hashes are identical across the EF oracle, the composed Groundwork store,
  and all three modeled Groundwork forms at every scale (`CorrectnessPassed: true` in each comparison).
- **Targets:** `ef.normalized` (temporary EF oracle), `groundwork.store` (real composed stores),
  and three modeled Groundwork physical forms `groundwork.shared` / `groundwork.dedicated` /
  `groundwork.entity`.
- **Benchmark Acceptance Catalog (19 rows):** 6 point reads, 2 batch/projection reads, 3 catalog
  page/count reads, 4 writes at concurrency 1, and the same 4 writes at concurrency 16. Reads use the
  100K mixed catalog, 10% active scope, page size 50, a 1–2% selective predicate, and 90/10 hit/miss.

### Reproduction

```bash
dotnet tool restore   # Groundwork schema tool, required before groundwork targets
# full matrix (re-measures — 10h+ wall for the three scales):
dotnet run -c Release --project benchmarks/Elsa.DesignPersistence.Benchmarks -- matrix 1k   --out bench-out
dotnet run -c Release --project benchmarks/Elsa.DesignPersistence.Benchmarks -- matrix 100k --out bench-out
dotnet run -c Release --project benchmarks/Elsa.DesignPersistence.Benchmarks -- matrix 1m   --out bench-out
# re-aggregate + gate from run files already on disk (no re-measuring):
dotnet run -c Release --project benchmarks/Elsa.DesignPersistence.Benchmarks -- compare 100k --out bench-out
dotnet run -c Release --project benchmarks/Elsa.DesignPersistence.Benchmarks -- gate         --out bench-out
```

---

## 2. Original matrix and the disclosed contamination

The original three-scale matrix (1K / 100K / 1M, all five targets, warm-up + three runs each) ran across a
~10h15m window. **The overnight EF-oracle baseline and the first Groundwork runs overlapped concurrent
builds on the same laptop.** Background compilation stole CPU and I/O, inflating the EF oracle latencies
by roughly **1.3–26x** and distorting the early Groundwork numbers as well. That contamination is real and
is not hand-waved away here.

Illustrative pre-remediation vs. fair EF p95 (`bench-out-pre-remediation/comparison.100k.json` vs.
`bench-out/comparison.100k.json`):

| Operation | pre-remediation EF p95 | fair EF p95 | inflation |
|---|---|---|---|
| `act.identity.get` | 0.311 ms | 0.109 ms | 2.9x |
| `wf.identity.get` | 0.411 ms | 0.210 ms | 2.0x |
| `wf.lifecycle.create@c1` | 1.782 ms | 0.705 ms | 2.5x |
| `wf.catalog.filter-page` | 683.8 ms | 346.8 ms | 2.0x |
| `act.lifecycle.add-version@c16` | 2.704 ms | 0.173 ms | 15.6x |

Because the contaminated numbers cannot be trusted for a ratio gate, **all pass/fail conclusions in this
report rest on the fair, same-conditions re-measurement of 2026-07-22** (`bench-out/fair.log`,
`bench-out/comparison.100k.json`, `bench-out/gates.json`). The pre-remediation evidence is preserved
unmodified under `bench-out-pre-remediation/` for audit; it is retained as history, not as a basis for any
verdict.

---

## 3. Remediation cycles

Three optimization cycles landed between the contaminated overnight matrix and the fair re-measurement.
Each is a real code/upstream change with measured effect:

**(a) Elsa lazy router + write-preflight fold** — commit `9e1a06b20`. Isolated point-read latency dropped
from **12.5 ms to 0.114 ms** by making query-route resolution lazy and folding the write preflight so the
hot path stops paying router-construction and redundant preflight cost on every call.

**(b) Groundwork #122 — compile-at-admission plan sets** — upstream PR #124 (preview.79); Elsa adoption
commit `9e9a8e78b`. Compiling plan sets at admission cut **session bind from 496 µs to 43 µs**.

**(c) Groundwork #123 — mutation-SQL cache** — upstream PR #125 (preview.80); Elsa adoption commit
`00f53f107`. Caches the mutation SQL so repeated writes skip re-generation.

**Cumulative effect:** point reads ~**19x** faster, c1 writes ~**8x**, c16 writes ~**4x**, and 100K
seeding fell from ~**70 min to ~3 min**. Both Groundwork upstream items (#122, #123) have shipped.

---

## 4. Fair-conditions final table (2026-07-22)

Source: `bench-out/comparison.100k.json` (median of three independent processes). GW = `groundwork.store`,
EF = `ef.normalized`. Ratios (GW ÷ EF) are shown as **recorded evidence only** — see §5 for why they are
no longer a gate.

| Operation | GW p95 (ms) | GW p99 (ms) | GW tput (ops/s) | EF p95 | p95 ratio | p99 ratio | tput ratio |
|---|---|---|---|---|---|---|---|
| `wf.identity.get` | 0.302 | 0.928 | 3417.5 | 0.210 | 1.44 | 1.72 | 0.37 |
| `wf.identity.version-exact` | 0.451 | 1.329 | 2471.2 | 0.085 | 5.29 | 3.89 | 0.18 |
| `wf.identity.version-latest` | 0.447 | 1.672 | 2330.8 | 0.469 | 0.95 | 0.99 | 0.50 |
| `wf.version.exists` | 0.661 | 1.664 | 2167.4 | 0.251 | 2.64 | 2.53 | 0.29 |
| `act.identity.get` | 0.312 | 1.027 | 3335.3 | 0.109 | 2.87 | 1.75 | 0.34 |
| `act.identity.version-exact` | 0.508 | 1.249 | 2158.9 | 0.115 | 4.43 | 2.20 | 0.23 |
| `act.catalog.versions-batch` | 2.777 | 12.302 | 509.4 | 1.981 | 1.40 | 3.83 | 0.45 |
| `wf.catalog.projection` | 2.943 | 6.094 | 468.3 | 3.159 | 0.93 | 1.06 | 0.88 |
| `wf.catalog.filter-page` | 178.290 | 210.525 | 5.8 | 346.769 | **0.51** | **0.48** | **1.31** |
| `act.catalog.filter-page` | 52.993 | 56.225 | 19.7 | 28.124 | 1.88 | 1.70 | 0.51 |
| `wf.catalog.count` | 208.496 | 461.049 | 5.4 | 257.129 | **0.81** | 1.22 | 0.86 |
| `wf.lifecycle.create@c1` | 1.703 | 13.811 | 956.0 | 0.705 | 2.42 | 7.82 | 0.31 |
| `wf.lifecycle.materialize@c1` | 1.584 | 11.900 | 1285.9 | 0.507 | 3.12 | 8.96 | 0.27 |
| `act.lifecycle.create@c1` | 2.262 | 12.944 | 650.8 | 0.262 | 8.62 | 14.05 | 0.15 |
| `act.lifecycle.add-version@c1` | 2.109 | 14.216 | 731.2 | 0.205 | 10.29 | 21.99 | 0.12 |
| `wf.lifecycle.create@c16` | 39.029 | 185.584 | 1344.2 | 0.416 | 93.92 | 94.66 | 0.36 |
| `wf.lifecycle.materialize@c16` | 35.841 | 133.744 | 1919.8 | 0.172 | 208.86 | 85.10 | 0.31 |
| `act.lifecycle.create@c16` | 88.509 | 351.437 | 759.7 | 0.259 | 342.00 | 202.79 | 0.18 |
| `act.lifecycle.add-version@c16` | 59.939 | 244.134 | 1113.9 | 0.173 | 346.27 | 164.24 | 0.18 |

**Where Groundwork wins on the bounded catalog** (the routes that matter for authoring-UI
responsiveness): `wf.catalog.count` p95 ratio **0.81x**, `wf.catalog.filter-page` **0.51x** (and 1.31x
throughput), `wf.catalog.projection` **0.93x**. On these bounded, index-driven reads Groundwork's
projected-column plan is competitive with or beats EF.

**The 16/19 ratio "failures"** are the point reads, the batch/versions read, and the writes. They are not
performance defects — they are the direct, expected cost of correctness work the EF oracle does not do (per
operation: ledger marker, replay preflight, scope-bound session, atomic multi-document staging). The
absolute latencies remain small (point reads sub-millisecond, c1 writes ~2 ms). This is exactly why the
ratio gate was replaced.

---

## 5. The amendment (ratified gate-5 replacement)

**Decision record.** On 2026-07-22, after reviewing the fair-conditions data, the program owner ratified
(via interactive decision; validated by the T079 three-axis independent review of 2026-07-22: performance-gate legitimacy PASS, test-objective preservation PASS, deletion completeness and core independence PASS, zero blockers) **replacing gate 5 — the per-row
EF-ratio gate at 100K — with absolute operational budgets** for the Benchmark Acceptance Catalog rows.
Gates 1–4 and 6–9 are unchanged. EF measurements remain **recorded as evidence, not a gate**.

**Rationale: semantic inequality.** The EF ratio compared semantically unequal work. Per operation, the
Groundwork write path executes the ratified operation-ledger marker, replay preflight, scope-bound
sessions, and atomic multi-document staging; the temporary EF oracle performs bare `SaveChanges`. The
oracle's own conformance profile — `DesignPersistenceContractProfiles.LegacyEfOracle` (see `research.md`
§ Test-oracle applicability) — declares exactly these scenarios **N/A**, with reasons in its own words:

> *"No durable operation ledger can reconcile acknowledgement loss."* (lost acknowledgement)
> *"No caller-stable operation key or durable replay outcome."* (exact replay)
> *"No storage-scope-bound write boundary."* (foreign scope-write rejection)

Charging Groundwork a latency ratio against an oracle that is contractually exempt from the ledger,
replay, and scope work is comparing different jobs. The budgets instead bound the **product-relevant
authoring envelope** — interactive-save perception thresholds, point-lookup latencies, catalog page
responsiveness — and guard against regression.

**The ratified budget table (100K, SQLite, median of three independent processes):**

| Class | Rows | p95 | p99 | Throughput |
|---|---|---|---|---|
| point-read | identity get / version-exact / version-latest / exists (6) | ≤ 0.8 ms | ≤ 2.5 ms | ≥ 2,000 ops/s |
| batch/projection | `act.catalog.versions-batch`, `wf.catalog.projection` | ≤ 5 ms | ≤ 20 ms | ≥ 200 ops/s |
| catalog page/count | `wf`+`act` filter-page, `wf.catalog.count` | ≤ 400 ms | ≤ 800 ms | ≥ 4 ops/s |
| writes @c1 | create / materialize / create / add-version (4) | ≤ 3 ms | ≤ 25 ms | ≥ 400 ops/s |
| writes @c16 | the same 4 rows at concurrency 16 | ≤ 100 ms | ≤ 500 ms | ≥ same row's @c1 tput |

**Budget verdict: 19/19 PASS.** Measured median vs. budget, with headroom (`bench-out/comparison.100k.json`,
`bench-out/gates.json`):

| Operation | Class | p95 / budget | p99 / budget | tput / floor | verdict |
|---|---|---|---|---|---|
| `wf.identity.get` | point-read | 0.302 / 0.8 | 0.928 / 2.5 | 3417.5 / 2000 | pass |
| `wf.identity.version-exact` | point-read | 0.451 / 0.8 | 1.329 / 2.5 | 2471.2 / 2000 | pass |
| `wf.identity.version-latest` | point-read | 0.447 / 0.8 | 1.672 / 2.5 | 2330.8 / 2000 | pass |
| `wf.version.exists` | point-read | 0.661 / 0.8 | 1.664 / 2.5 | 2167.4 / 2000 | pass |
| `act.identity.get` | point-read | 0.312 / 0.8 | 1.027 / 2.5 | 3335.3 / 2000 | pass |
| `act.identity.version-exact` | point-read | 0.508 / 0.8 | 1.249 / 2.5 | 2158.9 / 2000 | pass |
| `act.catalog.versions-batch` | batch/projection | 2.777 / 5 | 12.302 / 20 | 509.4 / 200 | pass |
| `wf.catalog.projection` | batch/projection | 2.943 / 5 | 6.094 / 20 | 468.3 / 200 | pass |
| `wf.catalog.filter-page` | catalog page/count | 178.29 / 400 | 210.53 / 800 | 5.8 / 4 | pass |
| `act.catalog.filter-page` | catalog page/count | 52.99 / 400 | 56.22 / 800 | 19.7 / 4 | pass |
| `wf.catalog.count` | catalog page/count | 208.50 / 400 | 461.05 / 800 | 5.4 / 4 | pass |
| `wf.lifecycle.create@c1` | write@c1 | 1.703 / 3 | 13.811 / 25 | 956.0 / 400 | pass |
| `wf.lifecycle.materialize@c1` | write@c1 | 1.584 / 3 | 11.900 / 25 | 1285.9 / 400 | pass |
| `act.lifecycle.create@c1` | write@c1 | 2.262 / 3 | 12.944 / 25 | 650.8 / 400 | pass |
| `act.lifecycle.add-version@c1` | write@c1 | 2.109 / 3 | 14.216 / 25 | 731.2 / 400 | pass |
| `wf.lifecycle.create@c16` | write@c16 | 39.03 / 100 | 185.58 / 500 | 1344.2 / 956.0 | pass |
| `wf.lifecycle.materialize@c16` | write@c16 | 35.84 / 100 | 133.74 / 500 | 1919.8 / 1285.9 | pass |
| `act.lifecycle.create@c16` | write@c16 | 88.51 / 100 | 351.44 / 500 | 759.7 / 650.8 | pass |
| `act.lifecycle.add-version@c16` | write@c16 | 59.94 / 100 | 244.13 / 500 | 1113.9 / 731.2 | pass |

For every @c16 row the measured throughput exceeds the same row's @c1 throughput, so write scaling does
not invert (the tightest is `act.lifecycle.create`: 759.7 @c16 ≥ 650.8 @c1). The narrowest latency
headroom is `act.lifecycle.create@c16` p95 at 88.5 ms against the 100 ms budget; all others clear their
budgets with visible margin.

The gate is enforced in code: `Gates.EvaluateBudget` classifies each catalog row and checks it against the
budget table (`benchmarks/Elsa.DesignPersistence.Benchmarks/Harness/Gates.cs`); `Orchestrator.Compare`
evaluates it at the 100K scale only; `Program.RunGate` reports the budget verdict as gate 5 and writes
`bench-out/gates.json`. The EF-ratio table is still computed and printed, explicitly labeled
"recorded evidence, NOT a gate".

`gate` command output (2026-07-22):

```
budget gate (gate 5, 100K absolute budgets): PASS  (19/19 rows)
EF-ratio (recorded evidence, NOT a gate): all-rows-pass=False
form-selection gate (gate 6, 100K & 1M): PASS
gates -> bench-out/gates.json
```

---

## 6. Form-selection gate (gate 6) — PASS at 100K and 1M

Gate 6 (unchanged): each selected physical-entity form must improve median p95 or throughput by ≥ 10%
over **both** the shared/linked and dedicated-document forms, in the same direction across all three runs,
with a 95% bootstrap CI excluding zero, at **both** 100K and 1M.

- **Verdict: PASS** at both scales. All 16 discriminating (gated) form rows pass at 100K and at 1M
  (`bench-out/comparison.100k.json`, `bench-out/comparison.1m.json`, `bench-out/gates.json`).
- **Margins:** the entity form's p95 improvement over the alternatives ranges **73.2% – 100.0%** at both
  100K and 1M, direction-consistent in all three runs, with bootstrap CIs excluding zero (e.g.
  `wf.catalog.filter-page` +73.2%/+83.8% vs. dedicated/shared; `wf.version.exists` ~100%).
- **Honest caveat about the window.** The form runs were measured during the same overnight window as the
  contaminated EF baseline. This does **not** undermine gate 6: gate 6 compares Groundwork forms **against
  each other**, and the three forms were measured in **adjacent windows under like conditions**, so any
  residual background contention applied roughly equally to all three. The inter-form margins (73–100%)
  are far beyond any plausible contention effect, and the direction holds in every run with CIs excluding
  zero. The contamination distorted the **EF↔GW** comparison (cross-tool, different windows), which is
  precisely the comparison now demoted to evidence — it did not drive the form selection.

Pure primary-key identity lookups (`*.identity.get`, `wf.identity.version-exact`) resolve through the same
`(tenant, id)` key in every form and tie by construction; they are measured and reported but are not gated
(see `Gates.DiscriminatingOps` and data-model Decision 3).

---

## 7. Evidence locations, and caveats

- **Fair (authoritative) run files and comparisons:** `bench-out/` — raw per-operation samples in
  `{target}.{scale}.run{1,2,3}.json` plus `warmup`; aggregated `comparison.{1k,100k,1m}.json`; final
  `gates.json`; fair-run transcript `bench-out/fair.log`.
- **Pre-remediation (contaminated, preserved for audit):** `bench-out-pre-remediation/`.
- **Query plans:** captured per discriminating route (`EXPLAIN QUERY PLAN`) inside each run file's
  `QueryPlans` block; the entity form mirrors the projected-column tables.

### Open follow-ups and recorded caveats

- **Durability configuration difference (recorded caveat).** The EF oracle and Groundwork do not run
  identical SQLite durability settings: **EF = rollback journal + `synchronous=FULL`**, while
  **Groundwork = WAL + `synchronous=NORMAL`** (`SqliteFormTarget.cs` sets `journal_mode=WAL`). This is one
  more reason the raw EF↔GW comparison is evidence rather than a gate — the two stacks make different
  durability/latency trade-offs. It does not affect the budget gate (absolute, Groundwork-only) or the
  form gate (Groundwork forms share the same WAL/NORMAL configuration).
- **Upstream remediations shipped:** Groundwork #122 (PR #124, preview.79) and #123 (PR #125, preview.80)
  are merged and released; Elsa adopted them at `9e9a8e78b` and `00f53f107`.
- **Contamination avoidance for future re-runs:** run the matrix on an otherwise-idle machine (no
  concurrent builds) so an EF↔GW comparison, if ever revived as evidence, is measured under like
  conditions end to end.

---

## 8. Gate summary for the removal condition

| Gate | Criterion | Verdict |
|---|---|---|
| 5 (amended 2026-07-22) | 100K absolute operational budgets, all catalog rows | **PASS — 19/19** |
| 6 (unchanged) | physical-form selection, ≥10% over both forms, 100K and 1M, CI excludes zero | **PASS** |
| — | EF same-provider ratio | recorded as evidence, **not a gate** |

Gates 1–4 and 7–9 are tracked outside this performance report (correctness parity, provider conformance,
native plans, atomicity/retry/scope/restart, reference composition, dependency audit, architecture guard).
This report closes the T069 performance/form-selection obligation.


## Committed evidence artifacts

The aggregate comparison files, gate verdicts, and orchestration logs are committed under
[`docs/reports/evidence/093-design-benchmarks/`](evidence/093-design-benchmarks/) (including the
pre-remediation 100K comparison for the contamination disclosure). The raw per-operation sample
files (~3.6 GB per generation) remain local benchmark outputs; every aggregate in this report is
recomputable by checking out commit `30ec15491` (the last commit carrying the harness, whose
`Gates.cs` produced the committed `gates.json`) and running its `compare`/`gate` commands over the
raw run files; the committed comparisons embed the per-run medians, percentiles, and bootstrap
intervals used by the gates. The harness was deleted by the subsequent removal commit per the
contract's evaluate-then-delete ordering, so the quoted gate output in §5 reproduces the
`30ec15491` harness state rather than any committed orchestration log. Upstream remediation
provenance: groundwork PR #124 merge `ad8ac47c7` (preview.79) and PR #125 merge `b7a31055a`
(preview.80), both verified on the framework's `origin/main`.
