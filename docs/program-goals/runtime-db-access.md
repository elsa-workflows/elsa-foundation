# Runtime Database Access

- **Status:** In Progress; owner-appointed control room since 5 October 2026. Workload plan delivered; current-run query accounting and reference capture active.
- **Area:** Workflow runtime / EF persistence access / HTTP workload correctness.
- **Stewards:** Sipke and the runtime database access program lead; root lead owns integration and QA.
- **Program:** [#2382](https://github.com/elsa-workflows/elsa-foundation/issues/2382).
- **Scheduling:** [Project 55](https://github.com/orgs/elsa-workflows/projects/55).

## Purpose

Explain and reduce avoidable database access for short durable HTTP workflows. Preserve response/output, committed state and inspection, atomic checkpoint proof, fencing, partition isolation and crash/replay behavior. Keep Immediate as host default; prove improvements on explicit Coalesced mode and report Immediate controls separately.

The owner-approved primary scenario uses HttpEndpoint startup and a deterministic computation; original transform artifacts are not a prerequisite.

## Active objectives

1. [#2386](https://github.com/elsa-workflows/elsa-foundation/issues/2386) Query accounting — active lead objective: causal request/caller ledger, current-run evidence and a valid REST-start control.
2. [#2450](https://github.com/elsa-workflows/elsa-foundation/issues/2450) Coalesced command-scope correction — Spec 196 lifecycle update, minimal default-factory scoped registration, accepted before-fix/mutation proof and local runtime/architecture/primary HTTP/REST gates. PR #2451 last published head `63ae25bd` passed CI, Maps and solution filters; root and independent integration review accepted it. CodeRabbit's valid lifecycle finding is handled in the current local pre-merge update, with final external disposition and exact-head gates pending. Copilot was requested but no persisted request or review is confirmed. Refresh current main before merge; resulting-main gates and final concurrent acceptance remain open, with T17/T18 owning that work.
3. [#2388](https://github.com/elsa-workflows/elsa-foundation/issues/2388) HTTP 202 diagnosis — in review; individual prior response attribution remains unresolved.

[#2385](https://github.com/elsa-workflows/elsa-foundation/issues/2385), [#2392](https://github.com/elsa-workflows/elsa-foundation/issues/2392) and [#2393](https://github.com/elsa-workflows/elsa-foundation/issues/2393) are delivered through [PR #2414](https://github.com/elsa-workflows/elsa-foundation/pull/2414), [PR #2436](https://github.com/elsa-workflows/elsa-foundation/pull/2436) and [PR #2442](https://github.com/elsa-workflows/elsa-foundation/pull/2442). The paging correction is merged. Failed main932 evidence is retained with unresolved cause; subsequent exact main `1e94f719` and `592d6c0e` CI/Maps passed, and T06 Project Verification is Passed. Historical main `d652f734` failed ordinary CI with separate Core/Architecture jobs skipped; that retained failure remains unresolved. The T19 integration refresh uses main `bfdde6ccdc4666479146177c1c845b7647111739` and requires new candidate gates. A later green revision does not causally repair an earlier failure. The single SQLite supporting diagnostic did not reproduce the exception and its interval was rejected, so it supplies no causal repair or green-main claim.

Keep one active lead objective, isolated writer worktrees and root-owned integration/QA. [Draft PR #2447](https://github.com/elsa-workflows/elsa-foundation/pull/2447) remains the existing accounting lane; its fixture pins are preserved. The [seven before timing cases](https://github.com/elsa-workflows/elsa-foundation/blob/df8ce5bc1d9210687eae52e976020d39ca5b9686/docs/reports/runtime-db-access/before-timing.md), including the failed Coalesced C4 case, and the [identity ledger](https://github.com/elsa-workflows/elsa-foundation/blob/df8ce5bc1d9210687eae52e976020d39ca5b9686/docs/reports/runtime-db-access/ef-identity-accounting.md) are historical evidence from checkpoint `df8ce5bc`. The latest accepted method attribution is recorded in the [method-accounting report at head 3616df777bc1573912c0d9a3439177645711be24](https://github.com/elsa-workflows/elsa-foundation/blob/3616df777bc1573912c0d9a3439177645711be24/docs/reports/runtime-db-access/method-accounting.md): 92 of 1,663 commands have exact method joins; the remaining 1,571 lack a matching method ancestor. Other callers, checkpoint participants, queue/outbox histories and T04 settled-work attribution remain open. The first failed observer capture stays rejected. Project/issues carry live scheduling/native dependencies; this document is a committed checkpoint. Other implementation chains remain blocked behind accepted accounting and reviewed specifications. The owner authorizes automatic Feedz preview-package publication caused by otherwise approved program merges; manual publication/releases and deployments remain outside scope. Required gates and the final T17/T18 work remain.

Current accounting checkpoint is accepted head `3616df777bc1573912c0d9a3439177645711be24`, with [CI](https://github.com/elsa-workflows/elsa-foundation/actions/runs/37582175487), [Maps](https://github.com/elsa-workflows/elsa-foundation/actions/runs/37582175099), [solution filters](https://github.com/elsa-workflows/elsa-foundation/actions/runs/37582175149) and [CodeQL](https://github.com/elsa-workflows/elsa-foundation/actions/runs/37582169554) passed at that exact revision. The [method-accounting report](https://github.com/elsa-workflows/elsa-foundation/blob/3616df777bc1573912c0d9a3439177645711be24/docs/reports/runtime-db-access/method-accounting.md) records 92 exact method joins among 1,663 commands; the other 1,571 lack matching method ancestry. T02 remains In Progress / Verification Running and incomplete while other repository callers, checkpoint participants and scheduler/outbox histories remain unresolved. The earlier runner teardown after evidence export is historical. A new Azure runner is reserved for bounded extended attribution with automatic shutdown at 10:01 UTC; its attempted host compile failed and root is inspecting it, so no extended-candidate live pass is claimed. The retained `90ed` native SQLite failure and other historical failures remain unresolved. Automatic Feedz preview-package publication caused by otherwise approved program merges is authorized; manual publication/releases and deployments remain outside scope. Final T17/T18 gates remain.

## Scope and roadmap

- Explain successful request commands, unexpected 202 and untraced follow-up work.
- Correct coalesced pagination, then assess repeated materialization.
- Verify effective cadence and review complete ReplaySafe contracts.
- Select narrowly justified atomic checkpoint and queue/outbox reductions.
- Prove the integrated normal-host result and respond to every supplied finding.

[The canonical plan](../plans/runtime-db-access-program.md) carries requirements, all five epics/eight features/twenty-one leaves, milestone exits, ambiguity ownership and coverage. Issues contain task acceptance and delivery criteria. [Supplied findings](../reports/runtime-db-access/findings.md) and [historical source response](../reports/runtime-db-access/response.md) retain evidence provenance.

## Boundaries and coordination

The owner's request scopes this narrow workload program. ADR 0073's broad performance-infrastructure retirement remains: no revived benchmarks, global timing gates or budgets. Thirty milliseconds stays aspirational. No durability weakening, global default flip, inspection/Studio redesign or broad provider rewrite. Automatic Feedz preview-package publication from otherwise approved merges is authorized; manual publication/releases and deployments remain outside scope. #1237/#1242 retain inspection/Studio ownership; #2286 owns the lease-identity defect; #1305/#1308 are historical proposals to revalidate. Existing #1306, #1312 and #1239 are adopted with current-scope addenda.

Program ownership is a new named bucket requested on 5 October 2026. Retired EF/cold-start programs supply context without becoming active measurement programs again. Revisit priorities after M2, when actual post-pagination costs can replace historical estimates.

## Completion

Require causal accounting or explicitly owned residual uncertainty, proven bounded pagination and fewer avoidable Coalesced operations, preserved touched contracts, reviewed dispositions for rejected candidates, and a comparable final evidence/response packet. Closure counts alone are insufficient. The owner authorized implementation, commit, push, PRs and green-gate program merges on 6 October 2026, and on 7 October authorized automatic Feedz preview-package publication caused by otherwise approved merges. Manual publication/releases and deployments remain outside scope.
