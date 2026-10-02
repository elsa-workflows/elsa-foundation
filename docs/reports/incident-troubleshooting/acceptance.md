# Incident troubleshooting delivery evidence

Program [#2334](https://github.com/elsa-workflows/elsa-foundation/issues/2334), feature [#2336](https://github.com/elsa-workflows/elsa-foundation/issues/2336), canonical [spec191](../../../specs/191-incident-troubleshooting/spec.md).

Status: implementation and verification in progress. No delivery acceptance claimed.

## Before state

[Original live QA](analysis-and-plan.md) documents the missing-variable scenario, screenshots and limits. The running user hosts were not attested current-source builds. Their source refs are recorded as inspected source only.

## Proven preparation

- Program/Epic/Feature/Tasks and Project54 created with native hierarchy and leaf ownership; three implementation tasks precede lead delivery.
- Specification requirements checklist: 8/8 complete; 23 checklist tasks across four user stories.
- Root plan/specification commit: `5ebe2e6ce` on `1308-incident-troubleshooting`; health API checkpoint `9fb29bae2`; causal evidence integrated from worker `be45c6cbe` as `06ab8a925`. Source changes remain subject to combined verification.
- Root `git diff --check`: passed.
- Foundation baseline current-main CI at exact `9ff8d80191e6906767e5ad52581db050b313d908`: [run37058093553](https://github.com/elsa-workflows/elsa-foundation/actions/runs/37058093553) completed success; exact-head [Maps37058093011](https://github.com/elsa-workflows/elsa-foundation/actions/runs/37058093011) also passed. Studio baseline `bbec838982bde4e7bd14a3f3b065b62c6caec81e` [CI37054699003](https://github.com/elsa-workflows/elsa-foundation-studio/actions/runs/37054699003) passed. The general `gh run list` returned older runs, so exact-head API/run queries are used for gate evidence. This does not resolve the separate #2293 root-cause investigation.
- Authoritative `dotnet run --project tools/maps/Elsa.Maps.Generator -c Release -p:RestoreLockedMode=true -- check`: initially reported new-spec tracking maps stale; explicit regeneration changed only manifest/spec-status/findings. Findings reviewed (spec count 221→222; no new architecture finding); repeated check passed.
- `Test-IncidentTroubleshooting.ps1` PowerShell parser check: passed. A clean reference Workbench was rebuilt from baseline plus the plan commit (`5ebe2e6ce`) in Release (101 existing warnings, zero errors), with isolated SQLite databases and loopback ports 7343/5195. Two root WriteLine JavaScript runs returned AcceptedButFaulted; the healthy control returned Accepted. The regression failed at its exact-activity association assertion, proving the baseline defect through the real HTTP/runtime/persistence path. Earlier fixture attempts included an unnecessary intrinsic dependency and were rejected by placement; that fixture was corrected before claiming the reproduction. No changed-code acceptance is claimed from this baseline run.

## Required acceptance matrix

| Scenario | Regression evidence | Real browser/HTTP evidence | Status |
|---|---|---|---|
| Undefined JS input and durable exact association | Pending | Pending rebuilt host | Pending |
| Failed input vs absent/unavailable evidence | Pending | Pending | Pending |
| Active/blocking/healthy filtering across pages | Pending | Pending REST script | Pending |
| Healthy and resolved/suppressed history | Pending | Pending | Pending |
| Nonblocking and retry policy/lifecycle | Pending | Pending composed/fixture scope attribution | Pending |
| Exact repeated occurrence and nested scopes | Pending | Pending | Pending |
| Runtime Flowchart/Sequence/BPMN cue mapping | Pending | Pending | Pending |
| Incident/activity reciprocal navigation | Pending | Pending | Pending |
| Unassociated engine incident | Pending | Pending | Pending |
| Permission/loading/older-server evidence | Pending | Pending | Pending |
| Pane layouts, keyboard and current themes | Pending | Pending screenshots | Pending |
| Dispatch accepted with incident feedback | Pending | Pending | Pending |

## Delivery gates

Affected suites/typecheck/lint/build, architecture/maps, independent exact-head review, mutation/revert proof, PR CI and post-merge main CI/Maps are pending. Existing unrelated Foundation main-red CI #2293 is retained for independent reconciliation; this program does not claim its cause fixed.

No user/peer host was restarted and no production deployment is in scope. Git push/PR route is awaiting the repository-required preference answer while local implementation/verification continues.

## Source review checkpoint

Independent read-only review confirmed a page-bound exact-occurrence navigation gap in Studio and a health-query provider paging gap. Studio now has bounded exact association batches and regressions beyond the initial page; Foundation integrated worker EF health paging as `ff9dfc2f3`. API review then caught an unsafe inference that the `all-tenants` label implies structure permission. Root now limits direct paging to the explicit allow-all development adapter and authorizes other candidates individually, using native health pages where available. The production authorization path still retains all authorized matches to count/page them; its scale limitation is documented in the API README. These corrections remain subject to compiled checks.

A proposed stale root-cause metadata defect was withdrawn after tracing writers: poison metadata never receives the observer's incident-only fault-inner enrichment in the normal path. Root corrected two xUnit exact-type assertions after the new typed exception was integrated. The first compiled health run passed 35/38 tests; three new theory cases failed because their terminal-incident fixture omitted required resolution provenance. That fixture is corrected, and combined runtime checks are queued through the normal build-slot wrapper. No passing rerun is claimed yet. Source review does not substitute for compiled/real-host proof.
