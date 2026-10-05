# Runtime Database Access

- **Status:** Planned; breakdown published, runtime execution not started.
- **Area:** Workflow runtime / EF persistence access / HTTP workload correctness.
- **Stewards:** Sipke and the runtime database access program lead; root lead owns integration and QA.
- **Program:** [#2382](https://github.com/elsa-workflows/elsa-foundation/issues/2382).
- **Scheduling:** [Project 55](https://github.com/orgs/elsa-workflows/projects/55).

## Purpose

Explain and reduce avoidable database access for short durable HTTP workflows. Preserve response/output, committed state and inspection, atomic checkpoint proof, fencing, partition isolation and crash/replay behavior. Keep Immediate as host default; prove improvements on explicit Coalesced mode and report Immediate controls separately.

## Active objectives

1. [#2385](https://github.com/elsa-workflows/elsa-foundation/issues/2385) Capture the workload, effective composition and reproduction prerequisites — next control-room objective: workload identity, original-artifact prerequisites and bounded reproduction plan.
2. [#2392](https://github.com/elsa-workflows/elsa-foundation/issues/2392) Specify and reproduce bounded coalesced page merging — independent Ready buffer: current-source pagination specification, deterministic regression and reference-host before-fix evidence.

No runtime worker is running. Keep one active lead objective, isolated writer worktrees and one integration lane. The Project is the scheduling surface; native issue parents and blocked-by links carry execution relationships. Later implementations remain blocked behind evidence and reviewed specifications.

## Scope and roadmap

- Explain successful request commands, unexpected 202 and untraced follow-up work.
- Correct coalesced pagination, then assess repeated materialization.
- Verify effective cadence and review complete ReplaySafe contracts.
- Select narrowly justified atomic checkpoint and queue/outbox reductions.
- Prove the integrated normal-host result and respond to every supplied finding.

[The canonical plan](../plans/runtime-db-access-program.md) carries requirements, all five epics/eight features/twenty leaves, milestone exits, ambiguity ownership and coverage. Issues contain task acceptance and delivery criteria. [Supplied findings](../reports/runtime-db-access/findings.md) and [historical source response](../reports/runtime-db-access/response.md) retain evidence provenance.

## Boundaries and coordination

The owner's request scopes this narrow workload program. ADR 0073's broad performance-infrastructure retirement remains: no revived benchmarks, global timing gates or budgets. Thirty milliseconds stays aspirational. No durability weakening, global default flip, inspection/Studio redesign or broad provider rewrite. #1237/#1242 retain inspection/Studio ownership; #2286 owns the lease-identity defect; #1305/#1308 are historical proposals to revalidate. Existing #1306, #1312 and #1239 are adopted with current-scope addenda.

Program ownership is a new named bucket requested on 5 October 2026. Retired EF/cold-start programs supply context without becoming active measurement programs again. Revisit priorities after M2, when actual post-pagination costs can replace historical estimates.

## Completion

Require causal accounting or explicitly owned residual uncertainty, proven bounded pagination and fewer avoidable Coalesced operations, preserved touched contracts, reviewed dispositions for rejected candidates, and a comparable final evidence/response packet. Closure counts alone are insufficient. Merge, package publication and deployment retain their normal authority.
