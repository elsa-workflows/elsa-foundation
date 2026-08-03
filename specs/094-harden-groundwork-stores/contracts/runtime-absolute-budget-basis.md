# Proposal: an absolute-budget basis for the runtime coverage-ledger rows

Status: **proposal, not ratified.** Authored 2026-08-03 under [#646](https://github.com/elsa-workflows/elsa-foundation/issues/646).
Requires an independent ratifier — see [Governance](#governance).

## The problem

21 of the 35 rows in [`coverage-ledger.json`](../coverage-ledger.json) are `family: runtime`. **None of them
can ever receive an EF-ratio verdict**, because no runtime persistence seam has ever had an EF-Core-backed
implementation. Runtime seams resolve to `InMemory*` or `Groundwork*`; there is no third registration and
there never was one (`git log --all -- "src/Elsa/Workflows/Runtime/**EFCore**"` is empty). See
[the zero-EF decision map](../../../docs/decision-maps/zero-ef-groundwork.md), `oracle-inventory`.

Meanwhile the gate machinery in
[`Harness/Gates.cs`](../../../benchmarks/Elsa.Groundwork.StorePerformance.Benchmarks/Harness/Gates.cs)
is **ratio-only**: `GatePolicy(GateClass, MaxP95Ratio, MinThroughputRatio, MaxP99Ratio, Review)`. There is
no absolute-budget concept, and `PerformanceVerdict` is `{ Pass, Redesign, Blocked }` — it has no
`NotHotPath` member even though the ledger schema's `performanceVerdict.outcome` enum does.

So spec 144's T011 ("#646 supplies an accepted verdict for every required coverage-ledger row") cannot
close for 21 of 35 rows on the current machinery, regardless of how much comparison evidence accumulates.

## The core insight

**A ratio gate needs a baseline, not an oracle.** The existing machinery — three measured child processes,
median-of-three, paired bootstrap ratio confidence intervals, fail-closed artifact manifest — works exactly
as-is if the denominator is *the last accepted generation of Groundwork itself* rather than EF.

That observation collapses most of this problem. Two of the three tiers below need **no new gate code**.

## The proposed basis: three tiers

### Tier A — Envelope (already ratified, already enforced, zero new work)

The user-visible property is workflow start/resume latency, not the latency of an individual store call.
That envelope is already measured and already gated:

| anchor | value | source |
|---|---|---|
| CI-enforced ceiling on HTTP workflow start p95 | **250 ms** | `.github/workflows/http-workflow-performance.yml` (`--enforce-p95-ms 250`), runs on every push |
| Ratified warm target | **50 ms** | [`docs/reports/runtime-http-performance-2026-07.md`](../../../docs/reports/runtime-http-performance-2026-07.md) |
| Measured warm p95, coalesced (1 commit/request) | **38.529 ms** | same report |
| Measured warm p95, immediate (13 commits/request) | **466.924 ms** | same report |

**Proposal: the 21 runtime rows inherit Tier A collectively.** If a real workflow start stays inside the
enforced envelope on the current build, no runtime store is individually pathological. This is the primary
runtime gate, it is real user-visible evidence rather than a synthetic store microbenchmark, and it costs
nothing new because it already runs.

Tier A alone is a defensible answer to "is Groundwork fast enough". It is not sufficient on its own only
because an envelope can absorb a localized regression — hence Tier B.

### Tier B — Per-workload absolute ceilings (new; derived, not invented)

Purpose: catch a regression localized to one store that the envelope averages away.

**Derivation method.** The Immediate-vs-Coalesced spread in the retained measurement is 466.9 ms vs
38.5 ms — a **12× span that the system tolerated while still functioning**, with the 250 ms CI ceiling
sitting inside it. A per-component ceiling of **3× the measured p95 at the ratified scale** is therefore
comfortably inside the envelope's demonstrated tolerance while still failing on any regression large enough
to matter. Budgets are set once from a retained baseline generation, then frozen until re-ratified.

Applied to the eight runtime workloads:

| workload | rows | ceiling |
|---|---|---|
| `checkpoint-commit` | 4 | 3 × measured p95 at ratified scale |
| `recovery-scan` | 5 | 3 × measured p95 |
| `queue-drain` | 2 | 3 × measured p95 |
| `trigger-binding-stimulus-lookup` | 2 | 3 × measured p95 |
| `recurring-schedule-selection` | 2 | 3 × measured p95 |
| `bookmark-lookup` | 1 | 3 × measured p95 |
| `outbox-drain` | 1 | 3 × measured p95 |
| `due-timer-selection` | 1 | 3 × measured p95 |

**Deliberately not stated as literal millisecond numbers here.** The store-performance harness has never
run a full runtime matrix at the current Groundwork version — `specs/094-harden-groundwork-stores/versions/`
stops at `preview.88` while the repo consumes `preview.103`. Writing invented millisecond constants into a
ratified contract would be exactly the failure mode this whole programme exists to avoid. The first
Tier B action is to **run the matrix once at the current version and populate the ceilings from it**, which
also resolves the missing-evidence-generation defect recorded in
[spec 144's quickstart](../../144-zero-ef-final-removal/quickstart.md).

### Tier C — Self-referential ratio ratchet (reuses existing machinery verbatim)

Purpose: catch slow drift that stays under the Tier B ceilings.

Run the existing `GatePolicy` ratio gates with the comparand set to the **last accepted retained
generation** instead of EF. Same `MaxP95Ratio` / `MinThroughputRatio` / `MaxP99Ratio`, same bootstrap CIs,
same fail-closed manifest binding. Only the comparand identity in the artifact manifest changes.

Proposed values: reuse `GateClass.RuntimeHotPath`'s existing defaults (p95 ≤ 1.10×, throughput ≥ 0.90×,
p99 ≤ 2.0×) unchanged. They were ratified for runtime seams; nothing about generation-over-generation
comparison argues for different numbers, and reusing them avoids a second ratification argument.

### The `not-hot-path` rows

Three rows already carry `requiredPerformanceVerdict: "pass-or-reviewed-not-hot-path"`:
`runtime-activity-execution-inspection`, `runtime-workflow-alteration`, and (in the IAM family)
`iam-provider-configuration-global`. These take a reviewed `not-hot-path` verdict with no measurement —
the ledger schema already supports the value.

`runtime-diagnostics-settings` is `externally-blocked` on #660 and is out of scope until that clears.

## What must change in code

Small, and mostly additive:

1. **`PerformanceVerdict` has no `NotHotPath` member** (`Harness/Gates.cs`) while the ledger schema's
   `performanceVerdict.outcome` enum does. Either add it, or record those rows by hand with a review
   reference. Adding it is cleaner and keeps the harness the single writer.
2. **`GatePolicy` needs an absolute ceiling alongside the ratios** for Tier B — one nullable
   `MaxP95Milliseconds`, evaluated the same way the ratio rows already are, so `GateRow` gains one column.
3. **The artifact manifest needs a comparand-identity field** for Tier C so a generation-over-generation
   run is distinguishable from an EF-oracle run in retained evidence. Without it the two are
   indistinguishable after the fact, which would be a provenance defect.

Tiers A and C need no gate-logic changes at all; Tier B needs one field and one comparison.

## Why this shape rather than pure absolute budgets

Pure absolute budgets were the obvious answer and I think they are the wrong one alone:

- They are **hardware- and environment-dependent**, so a single ratified millisecond constant either has to
  be so loose it catches nothing, or it fails on a slower CI runner for reasons unrelated to the code.
  Tier A sidesteps this by gating a *user-visible* property that the project already accepted a number for;
  Tier C sidesteps it by comparing like-for-like on the same machine in the same run.
- They **do not detect drift**. A store that degrades 8% per release passes an absolute ceiling for years.
  Tier C catches exactly that, and it is the tier that costs nothing to build.
- Absolute ceilings *are* the right tool for catching a catastrophic localized regression, which is why
  Tier B exists — but as a backstop, not the primary gate.

## Governance

`GatePolicy.Replacement` hard-rejects self-authored amendments: it throws when
`ProposedBy == ReviewedBy`. That rule is correct and this proposal does not bypass it.

This document is the **proposal half**. It needs:

- an independent reviewer recorded as `GateReview(ProposedBy, ReviewedBy, ReviewReference, ReviewedAtUtc)`;
- one runtime matrix run at the current Groundwork version to populate Tier B's ceilings;
- ratification recorded against #646, then imported into the ledger by spec 144's T011.

Until then every runtime row correctly remains without a `performanceVerdict`.
