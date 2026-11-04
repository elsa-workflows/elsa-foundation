# Delivery qualification record

Program [#3688](https://github.com/elsa-workflows/elsa-foundation/issues/3688), acceptance [#3577](https://github.com/elsa-workflows/elsa-foundation/issues/3577), PR [#3691](https://github.com/elsa-workflows/elsa-foundation/pull/3691).

Implementation [PR #3691](https://github.com/elsa-workflows/elsa-foundation/pull/3691) merged as `bfe9083349949b0cc8ebf1d3651df60ea2ca7767` after clean independent review round 8 and green exact-head required CI. M0–M5 are complete. The [resulting-main qualification record](resulting-main-qualification.md) owns final rebuilt-host, client, browser, identity/BPMN restart, recovery and archive evidence. The [closeout PR #3706](https://github.com/elsa-workflows/elsa-foundation/pull/3706) applies M6/Program completion only after executed resulting-main qualification, healthy-main inspection and its own required checks pass; the Program issue records live state until merge. The spec's Implemented transition was included in the implementation PR itself.

The intermediate runs and failures below are historical evidence, not claims that every later head was qualified by those earlier runs.

## Resumption and ownership

The initial checkpoint stopped after three successive failures of the new HTTP fixture, under the organization protocol. The owner instructed “Continue 20 more times maximum” on 3 November. Fresh app claims were established and blocking labels removed. Additional attempt 1 of the formerly blocked check passed all four tests: management authority permits inspection after group revocation, while completion remains forbidden. No further retry of that HTTP check was needed. See the PR history for the initial checkpoint and claim receipts.

The packet was renumbered from 1190, then 1194, to 1196 after current main and open PRs occupied 1190–1195. Historical links pinned to the initial commit remain valid.

## Executed evidence

The following are observed intermediate runs. Final gate comments identify the reviewed head; these results do not certify later changes automatically.

| Boundary | Evidence |
|---|---|
| Lifecycle/concurrency/recovery | Whole Runtime 3,225 and EF 1,878 passed. Real overlapping independent SQL contexts, competing claim/complete/cancel, three-generation Durable/Balanced recovery, forged and replaced waits, transient reads before/after bookmark claim, missing-state uncertainty and deletion retention are covered. |
| Supported databases | PostgreSQL, SQL Server and MySQL task contract tests 3/3, required providers with no skips; SQLite in the full EF suite. |
| Trusted identity/API | Whole Identity 566 and Runtime API 226 passed, as did the real two-user Workbench claim race/release/revocation/reassignment/trusted completion actor journey. Canonical user ID survives first-party bearer issuance/refresh; non-user subjects do not gain task authority. |
| Activity integration | Activities Runtime 733 executed/731 listed, DispatchWorkflow 380/380, Publishing API 918 executed/898 listed, Activity Designs 697 and Catalog API 40 passed; native BPMN User Task worker passed. Listed counts expand at execution for dynamic theories; listed/executed set comparison found no missing cases. Canonical Events suite 13 passed. |
| Studio | Workflows 3,184, Hosting 1,054 and UserTasks 17 mounted tests passed; module/Workflows/live type checks, builds and CSS/layout guards passed. Normal signed-in 1440/390 inbox journeys passed keyboard/axe, required/read-only validation, explicit false, exact data, duplicate receipt and one branch. Actual designer form/activity authoring, all three visual action connections, save/reload, publication and selected-branch execution passed. All three live journeys passed on reviewed head ec404a7f. |
| Custom forms/client | Real PowerShell authoring/publication/completion client passed all 3 configured action paths with exact response outputs on ec404a7f. Async external provider passes the real durable native-wait journey; matching nested custom renderer passes the mounted UI. A separately compiled consumer outside the workspace uses public contracts and the actual packed Studio feature without project references. |
| Packaging | Actual UserTasks archive verifies dependency metadata and byte-exact compiled JS/CSS/style-entry assets; public-contract consumer loads the packed feature. This is a scoped module/contract proof, not a whole distribution release. |
| Architecture/composition | Architecture 614, locked server solution restore, generated filter check and 129-project tier manifest check passed after canonical Events composition. Existing external package pins remain unchanged. |

CI on ec404a7f passed the architecture, core, frontend and Windows/Linux backend gates. The PR shard found a stale independent schema-family expectation; adding the new UserTasks 1.0.0 family passes the whole Cluster Readability suite (122). The Studio image omitted the new extension directory; adding its COPY passes the production Docker build. The independent Weaver witness stopped during consumed-intent-remount after nine earlier native cases; its exported evidence omits that private failure log. An API retry was refused by the integration's Actions permission. A fresh head will rerun the witness through the normal PR trigger; no failure is waived.

Current main 8a92746d4 was integrated cleanly before the final review, including scheduler RetryAfter handling and the consolidated CI workflow. The whole Runtime suite then passed 3,255 tests. The corrected production image also served the actual UserTasks module JavaScript (HTTP 200, 20,227 bytes).

The complete local independent Weaver package witness passed on 3f22e14063cafbc1745dc7f2399143b1aa05a8ea: 89 required cases, including the previously failed remount case, 26 autonomy cases and 20 restart cases, with no evidence gaps. The in-memory restart negative control was observed. No product change was needed for the earlier remount failure. Final UI inspection then found a long task title clipping at 390px (357px content in a 332px box); the detail now wraps unbroken text, and the real live journey checks the heading's own overflow and retains its screenshots. Final reviewed-head CI remains required.

The retained live screenshot also exposed the accepted-response notice remaining after a Completed refresh. A mounted regression failed on the prior component; successful refresh now replaces that notice from the current task status. The live journey additionally refuses pending-delivery text after committed completion.

The separately owned remount probe repair #3693 was integrated from main 19b063e767925bcd178ae7e724292d9e76093a76 in b92687e32c119a0e5c08f4a5c72bae21651d5cd9. The feature patch was byte-identical against its updated base. All 279 probe controls and the complete local 89-case witness passed on that integrated head. The prior passing run 41565508614 was the repair PR's run, not a main run; the corrected attribution is recorded on the Program. The owner subsequently explicitly approved up to 20 total formal review rounds for this delivery; the round-7 hold was cleared and round 8 reviewed the final repairs cleanly.

Broader CI then found missing shared coverage wiring: the activity had no behavioral drive, the schema-family chain handle was absent, and tooling inventories omitted the new family/two content columns. The new drive measures Done/Approve/Reject through real durable acceptance/delivery and records actual outputs and required-input refusal; the whole activity suite passes 229/229. The canonical UserTasks schema chain now owns both task and form reads; two regressions failed on the previous corruption classification and pass with structured schema-skew refusal. Whole Runtime EF passes 1,880/1,880 and migration/tooling passes 508/508. Independent pinned inventories remain in place. No activity outcome metadata or persisted format changed.

Runtime count reconciliation: local Debug executes 3,255; hosted Release executes 3,254. Comparing their TRX method inventories finds exactly one Debug-only test (`BurstScopeCacheTests.GetOrAddAsync_RejectsMustSurviveValue_InDebug`), with no other missing methods. This is a build-configuration difference, not a lost runtime case.

The repaired suites' listed/executed counts reconcile: Behavioral 229/229, Runtime EF 1,880/1,880 and migrations 494/508 (14 deferred theory rows, no missing listed case). Architecture remains 614/614 after the canonical schema-reader change.

Frontend CI later caught a premature route-selection test assertion before its detail read completed. A delayed mock response reproduced the failure. The fixture now controls both responses explicitly and waits for the rendered title after each response; all 18 module tests, type checking and targeted lint pass. No product code, assertion or timeout was relaxed.

Provider-suite local limitation: the whole native provider project had247 passes and one PostgreSQL covering-index plan assertion failure. That assertion also failed on the unmodified original base329ec72b. No unrelated planner assertion was changed. Exact-head Linux required-provider CI is still required.

Hosting-suite local result:57/58 passed, with the existing legacy-controls upgrade journey failing. The focused journey passed on unmodified current mainfa8b4a292, then passed on the same branch binaries at lower load. Preserve the initial failure; use whole exact-head CI as the final gate.

## Independent review and causal controls

Round1 reviewed5d753dcd8107d31b05541b06ea06b9751dc3689e independently for correctness, standards and acceptance. Seven concrete findings were accepted and repaired: mixed-context retention, nonliteral outcomes, pending-receipt outages, stale catalog link, string-valued action defaults, superseded ambiguous retries and skipped assignment pages. The round comment records evidence and no dropped findings.

Real authoring subsequently exposed missing dynamic-outcome facets on the catalog wire and specialized scalar editors losing to the generic editor. Both have failing regressions and fixes. Complex new activity defaults must use fixed Object syntax, so JSON text defaults become arrays/objects rather than string values. Existing activity contract surfaces are retained; the new task's intended surface is added to the baseline.

Causal controls observed: disabling the accepted-response resume guard makes the forged-resume test fail; restoring it passes. Disabling collection-derived port consumption makes its three tests fail; restoring it passes. New pending-receipt, physical-boundary and UI regressions fail before their fixes and pass after them. The default-wire regression likewise fails with Literal JSON text and passes with the restored Object defaults. Round2 repaired optional omitted/null outcome collections and exact capability readiness. Round3 adds shared replica-safe cursor keys and replaces the library-host handler loop with canonical Events composition; both have failing-before/passing-after witnesses.

## Final delivery gates

Reviewed-head CI passed all required and substantive jobs in [run 41581773609](https://github.com/elsa-workflows/elsa-foundation/actions/runs/41581773609), including full native provider/hosting, both Studio browser shards and all 89 independent Weaver cases. The three independent review axes were clean on fabc78cf in round 8; the permitted merge and rebuilt-main journeys are recorded in [resulting-main qualification](resulting-main-qualification.md).

The closeout reconciles the Program registry, checklist and linked parity/BPMN dependency. Its gate-evidence comment must verify executed resulting-main qualification, current-main health and its own reviewed-head required checks before merge. The original main push was superseded/canceled when #3697 landed; repeated acceptance passes on descendant main 3e343812 (Runtime3278/EF1884/client3/browser3/hosted2). Cancellation is preserved as cancellation; the original exact-push completion condition was an extra guard and is transparently replaced by the canonical protocol in the qualification record. No failed check is waived. No failed CI result was waived. The local provider/hosting limitations above are retained as history; final exact-head native CI passed.

All normal hosts and integration databases were owned and disposable. Hosts were stopped after qualification and owned stores retained; user hosts/stores were not used. Detailed local logs live under `/tmp/elsa-user-tasks-3577`; durable evidence is the source, this qualification record and commands/results on the PR and Program.
