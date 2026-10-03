# Incident troubleshooting delivery evidence

Program [#2334](https://github.com/elsa-workflows/elsa-foundation/issues/2334), feature [#2336](https://github.com/elsa-workflows/elsa-foundation/issues/2336), canonical [spec192](../../../specs/192-incident-troubleshooting/spec.md).

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

No user/peer host was restarted and no production deployment is in scope. The saved human preferences in both primary checkouts select organization branches and draft PRs; those ignored local preferences have been copied into the program worktrees. No new Git workflow choice is required.

## Source review checkpoint

Independent read-only review confirmed a page-bound exact-occurrence navigation gap in Studio and a health-query provider paging gap. Studio now has bounded exact association batches and regressions beyond the initial page; Foundation integrated worker EF health paging as `ff9dfc2f3`. API review then caught an unsafe inference that the `all-tenants` label implies structure permission. Root now limits direct paging to the explicit allow-all development adapter and authorizes other candidates individually, using native health pages where available. The production authorization path still retains all authorized matches to count/page them; its scale limitation is documented in the API README. These corrections remain subject to compiled checks.

A proposed stale root-cause metadata defect was withdrawn after tracing writers: poison metadata never receives the observer's incident-only fault-inner enrichment in the normal path. Root corrected two xUnit exact-type assertions after the new typed exception was integrated. The first compiled health run passed 35/38 tests; three new theory cases failed because their terminal-incident fixture omitted required resolution provenance. That fixture is corrected. Source review does not substitute for compiled/real-host proof.

## First integrated compiled gate

The five-project focused solution-filter run at `9b6572891` used the normal build-slot wrapper and completed under extreme shared-machine load. Runtime API passed 129/129; focused runtime passed 138/139, including the corrected health theories. The remaining retry test expected `Poisoned` despite selecting `RetryNow`; its disposition assertion is corrected to `RetryScheduled`, preserving the stale-metadata assertions. Activities passed the healthy Jint case, but the undefined-variable case hit the shared success-only harness guard at its expected `AcceptedButFaulted` dispatch. The harness now has an explicit default-false opt-in for that scenario, retaining all other acceptance and queue checks. EF tests did not run because a new fixture used an untyped nullable timestamp conditional; its local variable is corrected to `DateTimeOffset?`.

Architecture passed 595/617. All 22 failures identify missing evaluated Release/Debug restore assets. The documented `tools/architecture/restore-ci-project-graph.sh` prerequisite must be reconciled before a green architecture claim. No failed, uncompiled, or cancelled check is counted as passed.

Studio's final affected eight-file run passed 141/141. The earlier 5-second sensitive-evidence timeout did not reproduce in this run (755 ms). Current typecheck passed; final lint/bundle and rebuilt browser proof remain pending. A source mutation removing exact on-demand association made the page-bound navigation test fail; restored source passed that test. Exact command/head and final delivery evidence remain to be recorded.

Independent final review accepted descendant cues and their exact associations. Root retained real persisted `Faulted` lifecycle badges: active incident borders/counts and historical cues are separate from actual execution status. A resolved incident removes its current-health danger cue; it does not rewrite a still-faulted activity's status. The contained cue's accessible name is being expanded to include both incident and affected-child counts.

## First PR CI reconciliation

Foundation PR #2371 at `7b00548bf` caught four failures beyond the initial narrow filter: two sensitive-expression recorder tests still asserted the old exact exception type; the undefined-JavaScript fixture expected `Attempts` to be null rather than empty; and the existing failed-capture projection contract exposed a real regression. Root restored structural `captureFailed` independently of Values permission while keeping failure details/payload withheld. The existing projection test now also verifies both unauthorized withholding and authorized failure disclosure. Typed exception and zero-attempt assertions are corrected without removing the no-secret-leak or lifecycle checks. The pending local runtime rerun was terminated during restore after CI exposed these defects; it executed no tests and is not a green result. Both documented Release/Debug architecture restores completed successfully.

Studio's workspace and PR build at `3017a2ce` reported the same main-entry budget overage (129.56/127.50 kB). The team is extracting runtime-only overlays from the generic authored adapter into the lazy run inspector boundary; budgets are unchanged. Affected tests after this extraction passed 141/141; final typecheck/bundle and host/browser gates remain pending.

## First external review round

Draft PRs: Foundation [#2371](https://github.com/elsa-workflows/elsa-foundation/pull/2371), Studio [#556](https://github.com/elsa-workflows/elsa-foundation-studio/pull/556). Both are attached to the delivery task. Copilot returned actual reviews for both repositories; review is not treated as unavailable.

| Finding | Disposition | Verification |
|---|---|---|
| Foundation native health paging assumes EF is the selected incident store | Valid; compatibility witness and selected-store fallback integrated as `8fa607206` | Mixed-store filter/cursor and context/accessor regressions added; compiled checks pending |
| Foundation expression request construction escapes typed failure handling | Valid; materialization failure boundary restored as `cd06755ff` | Constructor-clone regression added; compiled checks pending |
| Foundation optional projection catches cancellation/fatal failures | Nonfatal fallback retained, cancellation/fatal failures propagate in `cd06755ff` | Operational fallback, cancellation and fatal regressions added; compiled checks pending |
| Foundation asynchronous authorization loop suggested as LINQ Where | Declined with a direct reply; predicates require awaited authorization | Thread resolved; behavior unchanged |
| Foundation first-match JSON scan suggested as LINQ Where | Declined with a direct reply; explicit scan preserves first valid match without an extra iterator | Thread resolved; behavior unchanged |
| Studio primary entry exceeds existing bundle budget | Valid; runtime overlays extracted into the lazy inspector boundary | Guard passes 126.21/127.50 kB; complete 20-project workspace build passed |
| Studio exact historical lookup conflates permission/loading failure with no association | Validate and preserve truthful failure states | Fix/regression pending |
| Studio BPMN incident navigation focuses the bound activity ID instead of canvas element ID | Validate both navigation and inspector-close focus paths | Fix/regression pending |
| Studio input association matches display name instead of stable referenceKey | Validate exact descriptor version and reference key, retaining older-descriptor fallback | Fix/regression pending |
| Studio unsupported health-filter message implies visible unfiltered results | Clarify that no results were loaded and how to clear the filter | Fix/regression pending |

At Foundation `098d78d00`, exact-head generated maps CI passed ([37082085734](https://github.com/elsa-workflows/elsa-foundation/actions/runs/37082085734)); Build & test also passed ([37082086102, job111085027185](https://github.com/elsa-workflows/elsa-foundation/actions/runs/37082086102/job/111085027185)). That job includes full Runtime 1984/1984, Runtime API 129/129, Runtime EF 862/862, Activities Runtime 352/352, Architecture 635/635 and Workbench 39/39. The four failures from the preceding CI round are reconciled; the separate container matrix is still running. These are checks of `098d78d00`, before the new external-review corrections.

Studio's first lint/typecheck job passed its lint/typecheck and shuffled tests but failed at the same bundle budget gate; it is not counted as a green CI job. The overlay extraction's complete local workspace build passed with the unchanged budget, but additional navigation/permission corrections and their verification remain pending. No PR is ready or merged.

The complete Foundation PR check set at `098d78d00` subsequently passed, including all selected EF container suites; the main-only alert job was correctly skipped on the PR. Local focused verification at the same source passed API 129/129, Runtime 145/145, Activities 14/14 and EF 44/44 (`/tmp/incident-integrated-runtime-v2.log`). That `dotnet test` invocation did not rebuild the non-test Workbench application; a separate application build remains required before changed-code HTTP/browser acceptance. The typed request/projection review correction is integrated as `cd06755ff` and is not covered by those earlier passes.

Root reviewed both bounded follow-up diffs before integration. Native-health composition regressions exercise contradictory EF vs selected in-memory incidents, each health predicate, both authorization paths and page-size-one cursor traversal; no new project references or lock files were needed. The normal-wrapper combined focused rerun at source `8fa607206` was cancelled while queued; it executed no tests. Exact-head CI subsequently compiled and checked these corrections. Authoritative map freshness passed again with the new compatibility contract (`/tmp/incident-review-maps-check.log`); no generated map changed.


## Current review and integration checkpoint

At Foundation `56f898f05e78897491cdebebe6b04aff258d1d94`, every PR check passed, including [CI37085245239](https://github.com/elsa-workflows/elsa-foundation/actions/runs/37085245239) and [Maps37085245040](https://github.com/elsa-workflows/elsa-foundation/actions/runs/37085245040). The main Build & test job passed Runtime 1994/1994, API 129/129, EF 869/869, Activities 352/352 and Architecture 635/635; the selected container suites and separate architecture/core gates also passed. This verifies the selected-store witness, typed request materialization boundary, cancellation/fatal propagation and failed-capture disclosure fixes.

Studio `368127abd18a3494eda07d749ef7b8359719b743` passed all PR checks ([CI37086697661](https://github.com/elsa-workflows/elsa-foundation-studio/actions/runs/37086697661), browser tests and Docker build). Final local four-file tests passed 70/70, final typecheck passed and final Workflows bundle passed 126.21/127.50 kB. These checks cover the exact lookup failure distinctions, BPMN focus, stable input reference and unsupported-filter wording.

Subsequent review found two Foundation public positional-record ABI regressions and five Studio gaps involving mixed-health counts, duplicate authored identities, frozen incident labels and direct Issues navigation. Corrections and their new regressions remain in progress. Moving the new health filter to an init property requires explicit HTTP binding and metadata because the pinned NativeEndpoints binder describes/binds constructor parameters only. Real mapped GET tests must prove the corrected filter.

Root reconciled newer main changes including scheduler partition scope and intrinsic runtime placement. Concurrent publication allocated spec191 to the Worker HTTP profile before this program merged; the incident spec is corrected to spec192 and inbound links updated to prevent a new number collision. The program scope is unchanged. The queued Workbench build of the earlier source was cancelled before compilation so the acceptance application can be built from the reconciled final source. No changed-code HTTP/browser acceptance or merged delivery is claimed yet.
