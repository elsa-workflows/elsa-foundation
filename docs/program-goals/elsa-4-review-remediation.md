# Elsa 4 Architecture Review Remediation

Status: active.

Area: cross-domain remediation of the 2026-07 architecture review findings (W1–W21).

Steward(s): Sipke plus active architects/agents.

## Purpose

Coordinate execution of the improvement roadmap produced by the
[Elsa 4 architecture review 2026-07](../reports/elsa-4-architecture-review-2026-07.md).
The per-work-unit implementation briefs (goal, verified current state, scope, acceptance
criteria, dependencies, skill routing) live in
[docs/reports/elsa-4-architecture-review-2026-07/roadmap.md](../reports/elsa-4-architecture-review-2026-07/roadmap.md)
and are the canonical scope definitions; this bucket tracks which units are planned, in
flight, or done, and which sessions/branches own them.

## In Scope

- Work units W1–W21 as briefed in the roadmap.
- Sequencing and conflict coordination between units and with in-flight work
  (notably specs/083 Move 2 on the runtime spine).
- Promoting remediation outcomes into their proper source-of-truth layers
  (constitution gates, glossary terms, maps, specs).

## Out Of Scope

- New review findings (file those in reports first).
- Redefining unit scope outside the roadmap briefs (amend the roadmap document instead).
- Runtime pipeline decomposition itself (owned by [Runtime Execution Seam](runtime-execution-seam.md); W12.2 defers to it).

## Active Objectives

Phase 0 (safety & correctness) — first wave launched 2026-07-02:

1. W2 Durable resumption chain (PS-2, RT-3) — in flight.
2. W3 State versioning contract (PS-1, PS-3) — in flight.
3. W4 Endpoint security model (MS-12/13/16) — in flight.
4. W6 Repo hygiene quick wins (MD-1/2/3/4/7, IN-12) — in flight.
5. W1 Fault semantics end-to-end — **held** until specs/083 Move 2 lands (same runtime-spine surface).
6. W5 Ownership enforcement — **held** until specs/083 Move 2 lands (same reason).

Phases 1–3 (W7–W21): queued; see the roadmap's dependency graph.

## Linked Surfaces

- [Consolidated review report](../reports/elsa-4-architecture-review-2026-07.md)
- [Roadmap briefs W1–W21 + skill routing](../reports/elsa-4-architecture-review-2026-07/roadmap.md)
- [Detail sub-reports](../reports/elsa-4-architecture-review-2026-07/README.md)
- Related buckets: [Runtime Execution Seam](runtime-execution-seam.md) (W1/W5/W12),
  [Code Reality And Test Maturity](code-reality-and-test-maturity.md) (W15),
  [Diagnostics Observability Readiness](diagnostics-observability-readiness.md) (W19),
  [Constitution Readiness](constitution-readiness.md) (W6.3, W14, W21).

## Current Roadmap Notes

- One unit per branch/PR; reference finding IDs; follow the roadmap's execution protocol
  (Speckit flow, constitution gates, skill routing, map refresh).
- W2 touches `WorkflowsRuntimeApiFeature.cs` registration, which specs/083 also edits —
  keep the registration diff minimal to ease merging.
- Update this bucket as units complete or move; link PRs next to each objective.
