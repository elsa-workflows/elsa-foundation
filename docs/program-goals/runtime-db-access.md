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

Keep one active lead objective, isolated writer worktrees and root-owned integration/QA. [Draft PR #2447](https://github.com/elsa-workflows/elsa-foundation/pull/2447) remains the existing accounting lane; its fixture pins are preserved while reviewed captures proceed. The [seven before timing cases](https://github.com/elsa-workflows/elsa-foundation/blob/df8ce5bc1d9210687eae52e976020d39ca5b9686/docs/reports/runtime-db-access/before-timing.md) retain every attempt, including the failed Coalesced C4 case. The corrected EF observer passed a fixed 58-case gate and two new actual diagnostic captures are accepted. [The identity ledger](https://github.com/elsa-workflows/elsa-foundation/blob/df8ce5bc1d9210687eae52e976020d39ca5b9686/docs/reports/runtime-db-access/ef-identity-accounting.md) separates providers, contexts, commands, saves and transaction lifecycles; exact caller/participant and T04 settled-work attribution remain open. The first failed observer capture stays rejected. Project/issues carry live scheduling/native dependencies; this document is a committed checkpoint. Other implementation chains remain blocked behind accepted accounting and reviewed specifications. The owner authorizes automatic Feedz preview-package publication caused by otherwise approved program merges; manual publication/releases and deployments remain outside scope. Required gates and the final T17/T18 work remain.

Current accounting checkpoint: draft PR #2447 at `df8ce5bc1d9210687eae52e976020d39ca5b9686` passed [CI including separate Core/Architecture and selected EF suites](https://github.com/elsa-workflows/elsa-foundation/actions/runs/37558914183), [Maps](https://github.com/elsa-workflows/elsa-foundation/actions/runs/37558913647) and [solution filters](https://github.com/elsa-workflows/elsa-foundation/actions/runs/37558913677). Its retained `90ed` native SQLite failure remains unresolved. The accepted finite Coalesced and Immediate Azure captures and bounded M2 timing observations are recorded in the [canonical plan](../plans/runtime-db-access-program.md); all owned Azure resources were removed after evidence export. The phase crosswalk covers 1,569 command pairs, while exact methods, participants and row histories remain open. Evidence links pin the unmerged accounting commit, not files present on this branch. The new private method-attribution candidate is under review and has no accepted execution or capture evidence. Automatic Feedz preview-package publication caused by otherwise approved program merges is authorized; manual publication/releases and deployments remain outside scope. Final T17/T18 gates remain.

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
