# Proposal: an absolute-budget basis and executable gate for the three oracle-less diagnostics providers

Status: **PROPOSAL — needs an independent reviewer and one first measurement.**
Proposed by the EF-Core-oracle scoping analysis
([`docs/reports/ef-core-oracle-scoping-2026-08.md`](../../../docs/reports/ef-core-oracle-scoping-2026-08.md), PR #1279).
Reviewer: **unassigned.** Numeric values: **deliberately absent** — see
[The numbers are not in this document](#the-numbers-are-not-in-this-document).

This document is the **proposal half**, in the same sense as
[`runtime-absolute-budget-basis.md`](runtime-absolute-budget-basis.md). It authorizes no edit, sets no
number, and produces no verdict.

**Companion:** [`diagnostics-sqlite-split-basis.md`](diagnostics-sqlite-split-basis.md) covers the SQLite
half (Route 1) and is a **hard prerequisite** for this one — see
[Why this cannot start first](#why-this-cannot-start-first).

## The problem, in the contract's own words

`diagnostics.json`'s `correctness.timingGate` states the requirement verbatim:

> SQLite compares the retained same-provider EF oracle. SQL Server, PostgreSQL, and MongoDB require
> independently reviewed numeric absolute budgets plus an executable absolute-budget gate; neither
> exists yet.

Two distinct things are missing, and they are usually conflated:

1. **Numeric budgets** — reviewed p95/p99/throughput values per operation class, per provider.
2. **An executable absolute-budget gate** — a code path that can turn those numbers into a verdict.

The second is the harder one, and its absence is not a matter of degree.

## There is no absolute-only code path today

This was verified against the harness rather than inferred. Four independent facts:

**1. A no-comparand run cannot form a comparison.** `Comparison.Compare`
(`Harness/ArtifactsAndMatrix.cs:498`) hard-requires two measurement sets:

```csharp
if (artifactSet.Artifacts.Count != 8 || cohorts.Length != 1 || measurementSets.Length != 2 ||
    measurementSets.Any(set => set.Count() != 4))
    return Blocked(..., "A comparison cohort requires exactly two distinct complete four-process measurement sets.");
```

A provider with no oracle produces **four** artifacts, not eight. It is blocked before any gate runs.

**2. Every gate criterion is a ratio.** `GateEvaluator.Evaluate` (`Harness/Gates.cs:106-114`) computes
p50/p95/p99/throughput as quotients against `oracleOperations`, then:

```csharp
var pass = p95Ratio <= policy.MaxP95Ratio && throughputRatio >= policy.MinThroughputRatio
        && p99Ratio <= policy.MaxP99Ratio && withinCeiling;
```

The absolute ceiling is one conjunct among four, and the source comment is explicit that this is
intentional — *"The absolute ceiling is evaluated alongside the ratios, never instead of them."* Remove
the comparand and three of the four conjuncts are undefined. There is no expression that evaluates
`withinCeiling` alone.

**3. The absolute concept is p95-only.** `GatePolicy` (`Harness/Gates.cs:17`) carries
`MaxP95Milliseconds` and nothing else absolute. There is **no** `MaxP99Milliseconds` and **no**
`MinThroughputPerSecond`. `GateRow` (`:62`) likewise carries `P95Milliseconds` /
`MaxP95Milliseconds` only. So even if the ratio conjuncts were removed, the surviving gate would bound
p95 and be silent on tail latency and throughput — for a workload whose whole risk profile is drain
throughput and tail behaviour under shed.

**4. Nothing emits a non-ratio verdict.** `PerformanceVerdict.NotHotPath` exists (`:9`) but no code path
produces it, and `Blocked(...)` (`:127`) returns `rows: []`, so a blocked run carries no measured
evidence forward at all.

**Conclusion.** `gate.diagnostics.absolute-budget-required` names a code path that does not exist. This
is the substance of Route 2, and it is a real feature, not a configuration change.

## The output shape already exists — as retained evidence, not as code

Spec 093 solved this exact problem once. Its result artifact is committed at
[`docs/reports/evidence/093-design-benchmarks/gates.json`](../../../docs/reports/evidence/093-design-benchmarks/gates.json)
and is **pure absolute — no ratios, no oracle**:

```json
{
  "BudgetGatePassed": true,
  "FormSelectionGatePassed": true,
  "BudgetRows": [
    {
      "Operation": "act.identity.get",
      "Class": "point-read",
      "P95Ms": 0.3122,          "P95BudgetMs": 0.8,   "P95Pass": true,
      "P99Ms": 1.026945999999999, "P99BudgetMs": 2.5, "P99Pass": true,
      "ThroughputPerSecond": 3335.3116538075833,
      "ThroughputFloor": 2000,  "ThroughputPass": true,
      "Pass": true
    }
  ]
}
```

That is precisely the missing shape: per-operation, per-class, three bounds each, measured value recorded
beside its budget so headroom is visible. **Route 2 does not need to invent a schema. It needs to
reinstate one.**

### But the code was deliberately not carried forward

`Harness/Protocol.cs:9-11` is unambiguous:

> *"Recovered and generalized from the Spec 093 harness protocol (commit 30ec15491): one untimed warm-up
> child process followed by three independent measured child processes. The 093 design-only targets and
> **its superseded absolute-budget amendment are intentionally not carried forward**."*

So the omission is a recorded design decision, not an oversight. Route 2 must **reverse that decision
with justification** — the justification being that #646 has three providers with no comparand, which the
093-era harness did not have to serve. A reviewer should treat "why is this being carried forward now
when it was excluded deliberately" as the first question, and the answer must be on the record.

### And it is not recoverable from this repository

Checked, because "port the 093 evaluator" is the obvious plan and it does not work:

```bash
git log --all -- "benchmarks/Elsa.DesignPersistence.Benchmarks"   # empty — never committed here
git cat-file -t 30ec15491                                        # fatal: Not a valid object name
```

The 093 harness project was **never committed to this repository**, and the commit its protocol cites is
**not in this repository's history**. The report's `dotnet run --project
benchmarks/Elsa.DesignPersistence.Benchmarks -- gate` invocations are therefore not reproducible here.

**Evidence-integrity consequence, which a reviewer must weigh:** the 093 budget table and its 19/19
verdict are a *ratified decision record and a schema reference*. They are **not** a reproducible baseline
from this tree, and must not be cited as one. The numbers in them describe design-persistence operations
on a harness that is absent, so they cannot be transplanted onto diagnostics operations even as a
starting point.

## The derivation method

Copy 093's method, not its numbers. Its structure is the transferable part: **group operations into
classes, then set p95 / p99 / throughput per class**, so a budget is defensible per access shape rather
than invented per operation.

Proposed class mapping for the workload's fifteen operations. This mapping is a **proposal for the
reviewer to confirm**, and three groups are deliberately marked as not latency-bearing:

| Class | Operations | Rationale |
|---|---|---|
| **durable append** | `append-structured-log-batches`, `append-open-telemetry-batches` | The hot write path. Batch-sized, so throughput floor matters more than p95. |
| **bounded read** | `read-structured-log-recent`, `resume-structured-log-history`, `query-open-telemetry-resources`, `query-open-telemetry-metrics`, `query-open-telemetry-logs` | Indexed bounded windows. Should be the tightest latency class. |
| **grouped reduction** | `query-open-telemetry-traces`, `read-open-telemetry-trace-detail` | The only class bounded by `MaxGroupedQueryInputRecords`; reduces up to the trace capacity. Structurally the most expensive read and needs its own, looser budget. |
| **exact count** | `inspect-exact-stream-counts` | Exact, not approximate, by contract — so it cannot be optimized into a metadata estimate. |
| **retention write** | `trim-diagnostic-streams` | Set-based delete; interferes with ingest, so measure under concurrent append. |
| *restart observation* | `reopen-and-read-structured-log-high-water`, `reopen-and-verify-durable-history` | **Not a latency class.** These prove durability across a new store instance; their wall-clock includes store construction and is not a per-operation budget. Propose recording without a budget. |
| *correctness only* | `seed-cross-scope-diagnostic-history`, `verify-cross-scope-isolation` | **Not latency-bearing.** Setup and a scope-isolation assertion. Propose excluding from the budget table entirely rather than assigning a token budget. |

### Budgets must be per provider, not per workload

One number across SQL Server, PostgreSQL and MongoDB would be meaningless: they have different round-trip
costs, different index structures, and in MongoDB's case a transaction-capable replica set whose write
path is not comparable to a single-node relational commit. `requiredProviderEvidence` already declares
three *different* evidence kinds for them, which is the contract conceding the same point.

This is also why the budgets cannot be expressed as class constants. `GatePolicy.DefaultFor` keys on
`GateClass` and takes no workload or provider argument, and
`RatifiedBoundedReadPathP95Milliseconds` is the standing proof of what happens when a per-workload number
is declared as a constant anyway: ratified at 40 ms, **enforced by nothing**, with five bounded-read
workloads silently carrying the 150 ms durable-write ceiling instead. Per-provider diagnostics budgets
must arrive as reviewed policy **files**, keyed by workload *and* provider.

## What must change in code

Additive, but larger than Route 1 by a wide margin.

| # | Change | Location |
|---:|---|---|
| 1 | Add `MaxP99Milliseconds` and `MinThroughputPerSecond` to `GatePolicy`, `ReviewedGateReplacement`, and `GateRow`, inherited-on-omission exactly as `MaxP95Milliseconds` already is. | `Harness/Gates.cs:17`, `:57`, `:62` |
| 2 | Add a **measurement** mode to the artifact/comparison layer: one complete four-process measurement set with no comparand, distinct from a `ComparisonResult`. It must keep every existing binding (cohort, machine environment, workload/version/provider/scale/seed/input fingerprint, correctness digest) and drop only the oracle. | `Harness/ArtifactsAndMatrix.cs:498-512` |
| 3 | Add an absolute-only evaluation path that emits `Pass`/`Redesign` from the three bounds alone, with no ratio terms, and populates rows with measured-vs-budget per operation. | `Harness/Gates.cs:81-119` |
| 4 | Extend the policy-file loader to carry a **per-provider** budget table keyed by operation class, and reject a file that omits a class the workload's operation sequence contains. Silence on an operation must be an error, not a pass. | `Harness/Gates.cs:65-79` (`GatePolicyFile`) |
| 5 | Emit `NotHotPath` where it applies (the restart-observation and correctness-only groups), so those operations are recorded as deliberately un-budgeted rather than absent. | `Harness/Gates.cs:117` |
| 6 | A new CLI verb — `measure`/`budget-gate` alongside `compare`/`gate` — since `gate` currently requires an oracle and target pair. | `Program.cs:91` help text and dispatch |
| 7 | Regression tests proving: a four-artifact set cannot be gated as a comparison; an absolute gate cannot pass an operation with no declared budget; a ratio policy cannot be silently applied to a no-comparand run; and the reverse. | `tests/Elsa/Groundwork/StorePerformance/Benchmarks/Tests/ProtocolAndGateTests.cs` |

Item 2 is the structurally significant one. Everything else is field-plumbing around it.

## The numbers are not in this document

Deliberately. `runtime-absolute-budget-basis.md` set its ceiling by **derivation from a retained
measurement**, and its own governance section required a maintainer to set the final value — Sipke set the
durable-write ceiling at 150 ms. That is the correct shape and this proposal does not shortcut it.

Inventing plausible millisecond values here would be the single worst outcome available: a budget that
looks reviewed, passes on first run because it was fitted to nothing, and thereafter certifies whatever
the code happens to do. **A budget that no measurement informed is not a gate; it is decoration.**

So the numbers come from a first measurement, in this order:

1. Land Route 1's EF diagnostics adapter leaf and `capture-plan` route capture.
2. Run one measurement set per provider — SQL Server container, PostgreSQL container, MongoDB
   transaction-capable replica set — at the workload's declared scale.
3. Derive per-class p95/p99/throughput from those medians, with an explicit and recorded headroom factor.
4. Independent review sets or amends the values; ratify against #646 and #642.

## Why this cannot start first

Route 2 is **downstream of Route 1**, not parallel to it.

Steps 2–4 above need a diagnostics adapter leaf, and the AdapterHost has none — `checkpoint-commit` is
the only implemented leaf, and `capture-plan` for workloads with native routes is *"not started — refuses
rather than fakes"*. That adapter is Route 1's item 10. Until it exists, no diagnostics measurement is
possible on **any** provider, so there is nothing to derive a budget from.

This ordering is worth stating plainly because it inverts the intuitive priority: the three oracle-less
providers look like the bigger, more urgent gap, but they cannot even be measured until the SQLite path
that Route 1 unblocks is built. Route 1 is therefore both cheaper *and* a prerequisite.

It is also worth being clear about what Route 2 protects. The three providers it serves have **no EF
oracle to lose**. Nothing about deferring Route 2 destroys evidence. It gates provider *coverage*, not
oracle *harvesting* — so it does not sit on the irreversible path that governs this program's ordering.

## Not to be done

- **Do not substitute a generation-over-generation ratio** as a cheaper alternative to absolute budgets.
  `Comparison.Compare` blocks on `source.CommitSha != candidate.CommitSha`
  (`ArtifactsAndMatrix.cs:512`), so every such run returns `Blocked` today. This is
  `runtime-absolute-budget-basis.md`'s 2026-08-06 Tier C correction and it applies unchanged here.
- **Do not substitute a physical-form comparison.** The two forms named in
  `physicalFormsFor646` have no binding in `src/`; Groundwork ships one shape per store. A form-vs-form
  comparison at one commit would compare a thing to itself.
- **Do not transplant 093's numeric budgets.** Different operations, different access shapes, and a
  harness absent from this repository. Method transfers; values do not.
- **Do not wire `RatifiedBoundedReadPathP95Milliseconds`** as part of this work.
  `ProtocolAndGateTests.Ratified_bounded_read_ceiling_is_declared_but_enforced_by_no_construction_path`
  fails if any construction path starts applying it, and #1176 owns the wire-or-supersede choice. Once
  per-provider policy files exist, superseding is the cleaner resolution — but that is #1176's call to
  record, not a side effect of this change.
- **Do not let a missing budget read as a pass.** Item 4 above exists because the most likely silent
  failure is a policy file that omits an operation class and a gate that therefore never bounds it.

## Governance

Before any code lands:

- a reviewer distinct from the proposer, recorded here (`GatePolicy.Replacement` enforces
  `ProposedBy != ReviewedBy` mechanically for the resulting policy files);
- a ruling on the operation-class mapping above, including the two deliberately un-budgeted groups;
- a recorded justification for reversing `Protocol.cs`'s deliberate exclusion of the 093 absolute-budget
  amendment;
- one measurement set per provider, after Route 1's adapter leaf;
- numeric values set by the maintainer, then ratified against #646 and #642 and imported by spec 144's
  T011.

Until all of that, `diagnostics-durable-history` correctly remains blocked for SQL Server, PostgreSQL and
MongoDB, and both diagnostics coverage-ledger rows correctly carry no `performanceVerdict` for those three
providers.
