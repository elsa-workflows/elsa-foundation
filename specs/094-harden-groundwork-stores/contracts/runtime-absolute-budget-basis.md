# Absolute-budget basis for the runtime coverage-ledger rows

Status: **RATIFIED 2026-08-04 for the durable-write ceiling; bounded-read adoption is recorded in #1176
on 2026-09-01, pending explicit acceptance.**
Proposed by the #646 analysis; the durable-write ceiling was reviewed and accepted by **Sipke Schoorstra**
(maintainer), who set it at 150 ms. Issue #1176 records the 40 ms bounded-read value as the executable
backstop; this document does not claim independent reviewer evidence for that adoption. The proposer and
durable-write reviewer are distinct, satisfying `GatePolicy.Replacement`'s independence requirement; later
replacements retain that same requirement.

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

### Adopted interim ceilings

One number here is derived from measurement; the rest are reasoned bounds, and the difference is stated
rather than blurred.

**The one measured anchor.** From `runtime-http-performance-2026-07.md`: 13 commits/request at 466.924 ms
p95 versus 1 commit/request at 38.529 ms p95. The marginal cost of one durable SQLite commit is therefore
**≈ 35.7 ms p95** (≈ 18.8 ms p50). *Caveat: a p95 of a sum is not a sum of p95s, so treat this as an
order-of-magnitude anchor, not an exact per-commit figure.*

**Policy shape: two classes, not eight invented numbers.**

| class | workloads | ceiling | basis |
|---|---|---|---|
| **Durable write path** | `checkpoint-commit`, `queue-drain`, `outbox-drain` | **150 ms p95** | ratified value; ~4× the 35.7 ms measured SQLite commit cost, widened from the proposed 100 ms to absorb slower CI hardware and remote providers |
| **Bounded read path** | `bookmark-lookup`, `recovery-scan`, `due-timer-selection`, `recurring-schedule-selection`, `trigger-binding-stimulus-lookup` | **40 ms p95** | adopted in #1176 as the executable backstop, pending explicit acceptance; reads perform no fsync and all non-commit work in the reference trace is single-digit ms. Raised from the proposed 25 ms because a **remote** provider adds a network round trip to a read that local SQLite never pays |

Two classes rather than eight per-workload numbers because there is exactly one measurement. Inventing
eight would dress up a single data point as eight.

**Two caveats that matter more than the numbers.**

1. **The measurement is from an Apple Silicon dev host; CI runners are slower.** A 3× headroom derived
   from fast hardware may be tight on CI. Either set the ceilings from a **CI-measured** baseline, or
   widen the write class to 150 ms if they are enforced on CI. This is the same hardware-sensitivity that
   makes `SQLite defaults` flake, documented below.
2. **These are SQLite-with-fsync figures.** Other providers differ, potentially a lot. As a
   catastrophic-regression backstop one generous ceiling per class is adequate; if Tier B ever becomes a
   precision instrument it needs per-provider numbers, which is an argument for keeping it a backstop.

**Sunset condition, revised on ratification.** The reviewer's point is load-bearing: **nobody runs SQLite
in production** — real deployments use PostgreSQL, SQL Server or MongoDB. So the sunset measurement must
**not** be another SQLite run. Replace each class ceiling with `measured p95 × 3` per workload taken on
the *production-representative* providers, and expect per-provider numbers to diverge: a remote server
pays a network round trip that local SQLite never does, while offering concurrency SQLite cannot.

Until that run exists, these two class ceilings are a deliberately blunt backstop derived from the only
measurement available, and their blast radius is bounded by being generous. That is the correct trade for
a catastrophic-regression gate and the wrong one for a precision instrument, which is why Tier C rather
than Tier B is the drift detector.

Groundwork's four provider leaves are `sqlite`, `postgresql`, `sqlserver` and `mongodb`. There is no
Oracle provider; anything running on Oracle would fall outside both this budget and the conformance
matrix.

**Historical note:** Earlier drafts deliberately withheld literal values pending measurement. The class
ceilings above are now adopted executable backstops with a stated sunset; the durable-write value is
ratified, while the bounded-read value remains pending explicit acceptance under #1176. Production-
representative measurements may replace them only through an independent ratification.

**Why these are class values rather than measured per-workload values.** The store-performance harness has
not run a full runtime matrix at the current Groundwork version — `specs/094-harden-groundwork-stores/versions/`
stops at `preview.88` while the repo consumes `preview.103`. The 150 ms and 40 ms values are therefore
class backstops (150 ms ratified; 40 ms adopted under #1176 pending acceptance), not invented per-workload
measurements. The first Tier B action remains to
**run the matrix once at the current version and populate replacement ceilings from it**, which also
resolves the missing-evidence-generation defect recorded in [spec 144's quickstart](../../144-zero-ef-final-removal/quickstart.md).

### Update (2026-08-08): the first measurement exists, for one workload

The matrix has now run. #1175 registered the `checkpoint-commit` adapter leaf — the first `IBenchmarkAdapter`
implementation in the repository — and took one signed four-artifact measurement set on SQLite and one on
PostgreSQL at `preview.103`. Per-operation medians and the derived `p95 × 3` ceilings are in
[docs/reports/checkpoint-commit-store-performance-2026-08.md](../../../docs/reports/checkpoint-commit-store-performance-2026-08.md).

Scope, precisely: **one of the eight runtime workloads**, and no `performanceVerdict` — `compare` and `gate`
were run and correctly refused, because a verdict needs the two-form comparison the correction below shows
is unavailable. The class ceilings in this document are therefore **unchanged**; the measured numbers are
recorded, not replacement ceilings, and superseding the standing 150 ms / 40 ms values still needs an
independent ratifier. Nothing measured
so far breaches either: the durable write path came in at 75 ms (SQLite) and 94 ms (PostgreSQL) p95.

Two observations bear on the reasoning above. Per-provider numbers do diverge, and not uniformly in one
direction, which supports keeping Tier B a blunt backstop — but the *size* of the cross-provider gap is not
yet trustworthy: the two provider drivers are not configured symmetrically (SQLite disables connection
pooling, PostgreSQL does not), so the measured read inversion has an unexcluded fixture explanation. The
per-provider ceilings are unaffected, because Tier B is per-provider by construction. And the 35.7 ms anchor
is not a like-for-like comparand for these figures either: it was measured on the *portable* store in WAL
mode, whereas the matrix measures the *physical* target on SQLite's default rollback journal.

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

1. ~~**`PerformanceVerdict` has no `NotHotPath` member**~~ **— DONE.** The member exists
   (`Harness/Gates.cs:9`). *Caveat:* `GateEvaluator` still only emits `Pass`, `Redesign` and `Blocked`, so
   nothing produces it yet; the three `pass-or-reviewed-not-hot-path` rows still need a hand-authored
   verdict carrying a review reference.
2. ~~**`GatePolicy` needs an absolute ceiling alongside the ratios**~~ **— DONE.** `MaxP95Milliseconds` is
   a nullable field on `GatePolicy`, inherited by `Replacement` when omitted, and evaluated alongside the
   ratios in `GateEvaluator.Evaluate`, which adds it to `GateRow`.
3. **The artifact manifest needs a comparand-identity field** for Tier C — **still open, and larger than
   this line implies.** See the correction below.

~~Tiers A and C need no gate-logic changes at all;~~ Tier B needs one field and one comparison — that
field now exists.

### Correction (2026-08-06): Tier C is not "no gate-logic changes"

The claim in **The core insight** that the existing machinery "works exactly as-is if the denominator is
the last accepted generation of Groundwork itself" **does not hold against the code.**
`Comparison.Compare` (`Harness/ArtifactsAndMatrix.cs:498-511`) requires all eight artifacts to share one
comparison cohort, one machine environment, and — decisively —

```csharp
if (source.CommitSha != candidate.CommitSha)
    return Blocked(..., "Oracle and target do not share exact commit provenance.");
```

A generation-over-generation comparison has two commits by definition, so **every Tier C run returns
`Blocked` today**. Tier C therefore needs a real change to the comparison contract (a comparand-identity
concept that admits two commits while keeping the machine and workload bindings), not just a manifest
field. Until that lands, the only legal comparison at one commit is between two **physical forms** — and
the form labels in the workload JSON (`shared-documents-with-linked-index-tables`,
`document-type-specific-tables`, …) have no binding in `src/`; Groundwork ships one shape per store.

Consequence for the sunset condition: the first production-provider run can populate **Tier B ceilings**
(which need only one measurement set — median-of-three p95 × 3) but **cannot** produce a
`performanceVerdict`, because a verdict requires a comparison. Those are separable, and only the first is
reachable now.

### Correction (2026-08-06), resolved 2026-09-01: enforce the bounded-read ceiling

Before this resolution, the adopted bounded-read value (40 ms) was referenced nowhere but its constant
declaration. `GatePolicy.DefaultFor(GateClass.RuntimeHotPath)` applied the 150 ms **durable-write**
ceiling to *every* runtime workload, including the five bounded-read workloads this document assigns 40
ms. A bounded read regressing from 5 ms to 140 ms passed the default gate silently.

The original recommendation was to supersede rather than wire the provisional value. That left a live
window in which a bounded read could regress from 5 ms to 140 ms and still pass. Issue #1176 therefore
made `DefaultFor` workload-aware and applies the #1176-adopted 40 ms backstop to the five bounded-read
workloads. Durable writes retain 150 ms. Explicit acceptance of #1176 is still the governance record for
treating 40 ms as ratified. Reviewed per-workload policies still supersede these class
ceilings under the existing sunset condition; enforcement now protects the interval before those
measurements exist.

**Resolution, 2026-09-01 (#1176).** The CLI derives the default gate class from the exact comparison
workload and rejects an unreviewed `--class` override; all five declared bounded reads are pinned to the
#1176-adopted 40 ms backstop, durable writes remain pinned to 150 ms, and reviewed replacements inherit
the correct workload class when they omit an explicit ceiling. The former
"enforced by nothing" regression test has been replaced by executable coverage for every bounded-read
workload.

## Field evidence for the shape: Tier A's own gate flakes

Observed while this proposal was open, and it argues for the design rather than against it.

`SQLite defaults` (`.github/workflows/http-workflow-performance.yml`, the `--enforce-p95-ms 250` gate
that Tier A inherits) **failed twice on branch `claude/ef-core-oracle-groundwork-16f4f3` without any
change to the measured path**:

- commit `3c34f66bc` failed it while changing **exactly one markdown file**;
- commit `3a3950dd5` failed it while touching nothing under `src/Elsa/Workflows/Runtime/**` or
  `src/Elsa/Persistence/Groundwork/**`;
- the commits between them, including ones that *did* change persistence code, passed.

A single absolute millisecond ceiling on a shared CI runner is therefore demonstrably noisy at this
threshold. That is not a reason to abandon Tier A — it is real user-visible evidence and it already runs
— but it is a concrete reason **not to make an absolute ceiling the sole gate**, which is the design this
proposal argues for and the reason Tier C exists.

Two consequences worth ratifying alongside the tiers:

1. **Tier B's ceilings should be generous** (the proposed 3× headroom, not a tight bound), because they
   are a catastrophic-regression backstop, not a precision instrument.
2. **A single failing run of an absolute gate is not evidence of regression.** Require a repeat, or
   correlate against whether the change touched the measured path at all, before treating it as one.
   Both failures above would have been dismissed correctly by that second test alone.

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

The durable-write decision is ratified above. Issue #1176 records implementation adoption of the 40 ms
bounded-read backstop, but it is the ratification record only if its acceptance is explicitly recorded;
this document does not invent independent reviewer evidence. The remaining governance record needs:

- an independent reviewer recorded as `GateReview(ProposedBy, ReviewedBy, ReviewReference, ReviewedAtUtc)`;
- one runtime matrix run at the current Groundwork version to populate Tier B's ceilings;
- acceptance recorded against #1176/#646, then imported into the ledger by spec 144's T011.

Until the current-version matrix and the required acceptance are recorded, every affected runtime row
correctly remains without a `performanceVerdict`.
