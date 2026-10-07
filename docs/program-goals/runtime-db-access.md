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
2. [#2450](https://github.com/elsa-workflows/elsa-foundation/issues/2450) Coalesced command-scope correction — refreshed approved Spec 196 and PR #2451 at `63ae25bd`; exact-head CI/Maps and root/independent integration review passed. CodeRabbit's final spec-lifecycle update remains open; Copilot review remains unconfirmed. Publication-boundary merge hold, resulting-main verification and final concurrent T17/T18 acceptance remain pending.
3. [#2388](https://github.com/elsa-workflows/elsa-foundation/issues/2388) HTTP 202 diagnosis — in review; individual prior response attribution remains unresolved.

[#2385](https://github.com/elsa-workflows/elsa-foundation/issues/2385), [#2392](https://github.com/elsa-workflows/elsa-foundation/issues/2392) and [#2393](https://github.com/elsa-workflows/elsa-foundation/issues/2393) are delivered through [PR #2414](https://github.com/elsa-workflows/elsa-foundation/pull/2414), [PR #2436](https://github.com/elsa-workflows/elsa-foundation/pull/2436) and [PR #2442](https://github.com/elsa-workflows/elsa-foundation/pull/2442). The paging correction is merged. Failed main932 evidence is retained with unresolved cause; subsequent exact main `1e94f719` and `592d6c0e` CI/Maps passed, and T06 Project Verification is Passed. Historical main `d652f734` failed ordinary CI with separate Core/Architecture jobs skipped; its causal attribution remains unresolved. Main `2ad0d854` separately passed CI, Maps and Windows/Linux backend-source replay on 7 October; those exact-revision passes do not repair the earlier evidence. The single SQLite supporting diagnostic did not reproduce the exception and its interval was rejected, so it supplies no causal repair or green-main claim.

Keep one active lead objective, isolated writer worktrees and root-owned integration/QA. [Draft PR #2447](https://github.com/elsa-workflows/elsa-foundation/pull/2447) remains the existing accounting lane; its fixture pins are preserved while reviewed captures proceed. The [seven before timing cases](../reports/runtime-db-access/before-timing.md) retain every attempt, including the failed Coalesced C4 case. The corrected EF observer passed a fixed 58-case gate and two new actual diagnostic captures are accepted. [The identity ledger](../reports/runtime-db-access/ef-identity-accounting.md) separates providers, contexts, commands, saves and transaction lifecycles; exact caller/participant and T04 settled-work attribution remain open. The first failed observer capture stays rejected. Project/issues carry live scheduling/native dependencies; this document is a committed checkpoint. Other implementation chains remain blocked behind accepted accounting and reviewed specifications. Merges remain held for the publication-boundary decision; package publication and deployment are excluded.

Preceding accounting checkpoint: draft PR #2447 at `df8ce5bc` passed exact-head CI/Architecture/Core/EF suites, Maps, filters and CodeQL. This update requires fresh head gates. The [method-accounting packet](../reports/runtime-db-access/method-accounting.md) adds reviewed real-host Coalesced and Immediate captures on the preserved before source: 92 exact durable-value page-method joins across 1,663 commands. HTTP/REST controls and primary settlement passed; background activity differs between packets and retains its own accounting. Other methods, checkpoint participants and queue/outbox identities remain open. Earlier failed captures remain excluded. The fresh Azure runner remains reserved for bounded attribution work; owned capture hosts and containers are removed. Publication-boundary merge hold and final integrated program gates remain.

## Scope and roadmap

- Explain successful request commands, unexpected 202 and untraced follow-up work.
- Correct coalesced pagination, then assess repeated materialization.
- Verify effective cadence and review complete ReplaySafe contracts.
- Select narrowly justified atomic checkpoint and queue/outbox reductions.
- Prove the integrated normal-host result and respond to every supplied finding.

[The canonical plan](../plans/runtime-db-access-program.md) carries requirements, all five epics/eight features/twenty-one leaves, milestone exits, ambiguity ownership and coverage. Issues contain task acceptance and delivery criteria. [Supplied findings](../reports/runtime-db-access/findings.md) and [historical source response](../reports/runtime-db-access/response.md) retain evidence provenance.

## Boundaries and coordination

The owner's request scopes this narrow workload program. ADR 0073's broad performance-infrastructure retirement remains: no revived benchmarks, global timing gates or budgets. Thirty milliseconds stays aspirational. No durability weakening, global default flip, inspection/Studio redesign or broad provider rewrite. #1237/#1242 retain inspection/Studio ownership; #2286 owns the lease-identity defect; #1305/#1308 are historical proposals to revalidate. Existing #1306, #1312 and #1239 are adopted with current-scope addenda.

Program ownership is a new named bucket requested on 5 October 2026. Retired EF/cold-start programs supply context without becoming active measurement programs again. Revisit priorities after M2, when actual post-pagination costs can replace historical estimates.

## Completion

Require causal accounting or explicitly owned residual uncertainty, proven bounded pagination and fewer avoidable Coalesced operations, preserved touched contracts, reviewed dispositions for rejected candidates, and a comparable final evidence/response packet. Closure counts alone are insufficient. The owner authorized implementation, commit, push, PRs and green-gate program merges on 6 October 2026; package publication and deployment remain outside scope.
