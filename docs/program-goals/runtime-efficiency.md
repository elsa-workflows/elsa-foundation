# Runtime Efficiency

- **Status:** Active since 2026-10-10; end-to-end continuation authorized 2026-11-01.
- **Area:** Execution overhead, immutable preparation, persistence, admission and concurrent recovery.
- **Steward:** Sipke. **Program:** [#2531](https://github.com/elsa-workflows/elsa-foundation/issues/2531).
- **Authorization:** [owner instruction and delivery scope](https://github.com/elsa-workflows/elsa-foundation/issues/2531#issuecomment-6554699791),
  [ADR 0080 D0](../adr/0080-elsa-4-simplification-decisions.md#d0--focus) and
  [D2](../adr/0080-elsa-4-simplification-decisions.md#d2--persistence-modes-balanced-by-default).

This program absorbs [Runtime Database Access](runtime-db-access.md) and [Runtime Throughput](runtime-throughput.md).
The program issue is the queue and progress record. Keep one active implementation leaf; reuse matching issues.
The ready queue already exceeds its cap, so additional work stays on the parent until existing leaves are reconciled.

## Outcome and evidence

Reduce work per execution while preserving the selected persistence mode, current input values, authorization,
ownership fences, checkpoint/outbox atomicity and recovery. Use deterministic command/read budgets for regression
coverage. Owner-requested timing comparisons are bounded qualification evidence, with their actual hardware,
payloads, cache state, failures and exclusions retained.

The [native assessment](../reports/native-runtime-performance-2026-10-31.md) and
[first implementation](../reports/native-runtime-performance-implementation-2026-10-31.md) establish the starting
point. [#3542](https://github.com/elsa-workflows/elsa-foundation/issues/3542) / PR #3545 delivered the zero-input
fast path, immutable input preparation, indexed coalescing views and weak EF provider factories. Its timing cohort
predates final main integration; later correctness qualification does not change that provenance.

## Delivery milestones

| Milestone | Completion evidence | Existing records |
|---|---|---|
| M0: reconcile the queue | Each older objective is mapped to current code, delivered work or remaining acceptance; original failed/skipped/superseded states remain explicit. | [Persistence epic #2564](https://github.com/elsa-workflows/elsa-foundation/issues/2564), #2582–#2586, #2828, #2831 |
| M1: complete accepted native starts | Explain the intermittent incomplete in-memory start with a causal control; prove completion/recovery and identifier behavior under concurrency. Passing later runs alone do not establish a fix. | #3542 assessment and parent #2531 |
| M2: demand-driven inputs | Literal-only and narrowly scoped inputs avoid unnecessary history/value/frame reads; custom materializers, dynamic bindings, lazy structure inputs and closed scopes retain their semantics. | Parent #2531; first slice #3542 delivered |
| M3: immutable preparation | Reuse hydration/nullability, conversion and prepared-JavaScript metadata with bounded or weak ownership; prove fresh values, concurrent isolation, validation and collectible-module release. | [Hydration #2657](https://github.com/elsa-workflows/elsa-foundation/issues/2657); parent #2531 |
| M4: storage and scheduling cost | Attribute the fixed command floor, lower justified budgets, qualify pooling and retained history, and resolve applicable suspension/cancellation, pump and diagnostics objectives. Preserve replay and stale-writer rejection. | #2564; [pumps #1307](https://github.com/elsa-workflows/elsa-foundation/issues/1307), [diagnostics #2613](https://github.com/elsa-workflows/elsa-foundation/issues/2613) |
| M5: integrated qualification | Representative expressions/payloads, sustained arrivals, owner crash/takeover, duplicate commands and database interruption have reproducible outcomes; native/BPMN/ETL measurements name the actual model, provider and machine topology. | Parent #2531; model capabilities remain on [Unified Execution #2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892) |

The first continuation slice, #2657, reuses weak type-owned hydration metadata. JavaScript and conversion
preparation remain separate M3 work. M1–M5 acceptance remains open until its evidence is recorded; a merged
optimization does not complete this program. Available ETL already uses bounded partitions/batches: qualify and
improve that implementation instead of assuming it must be introduced.

## Boundaries and sequencing

- Run beside the active programs and coordinate shared runtime/model changes through their existing issues and PRs.
  This program owns efficiency and qualification; Unified Execution retains ownership of model/hosting features.
- Balanced remains the default. Preserve each mode's documented checkpoint and external-effect boundaries.
- Keep artifact caching bounded and lifecycle-aware. Mutable execution values and scoped services remain invocation-
  or ownership-scoped; arbitrary process-wide durable-state caching is outside this plan.
- Keep the [proposed commit journal](../adr/0084-runtime-commit-journal.md) low priority under its existing triggers.
  No journal rewrite, timing CI gate, general benchmark service, production SLO, release or production deployment
  is introduced by this continuation.
- Choose the independent-host qualification fixture when implementation is ready to run. Record actual host and
  database placement; multiple processes or containers on one host are not independent-machine capacity evidence.

## Completion

Close #2531 only after M0–M5 and every still-applicable older child acceptance have demonstrated results on main,
or a separately recorded owner disposition. Each delivered change carries reviewed-head checks, behavioral
controls and affected-suite accounting; final integration verifies a containing main commit. Reports preserve
remaining failures and evidence limits. The program and registry are marked completed only at that point.
