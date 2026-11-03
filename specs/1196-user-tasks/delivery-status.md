# Delivery qualification record

Program [#3688](https://github.com/elsa-workflows/elsa-foundation/issues/3688), acceptance [#3577](https://github.com/elsa-workflows/elsa-foundation/issues/3577), PR [#3691](https://github.com/elsa-workflows/elsa-foundation/pull/3691).

Design and implementation are present. M0–M4 are complete. Owned-host and package qualification have passed; required CI and resulting-main acceptance remain active. Five independent review rounds have completed, with round 5 clean on ec404a7f0d5a8bc50d3d0337c44784e9f219edb2. CI subsequently identified the Docker copy omission and schema-readability expectation repaired below; the next reviewed head must include those fixes. Neither the Program nor the feature is accepted, merged or closed. Per the spec lifecycle transition rule, this implementation PR sets the spec's Implemented status before merge; it becomes true when the PR merges. Program closure still requires resulting-main qualification.

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

Provider-suite local limitation: the whole native provider project had247 passes and one PostgreSQL covering-index plan assertion failure. That assertion also failed on the unmodified original base329ec72b. No unrelated planner assertion was changed. Exact-head Linux required-provider CI is still required.

Hosting-suite local result:57/58 passed, with the existing legacy-controls upgrade journey failing. The focused journey passed on unmodified current mainfa8b4a292, then passed on the same branch binaries at lower load. Preserve the initial failure; use whole exact-head CI as the final gate.

## Independent review and causal controls

Round1 reviewed5d753dcd8107d31b05541b06ea06b9751dc3689e independently for correctness, standards and acceptance. Seven concrete findings were accepted and repaired: mixed-context retention, nonliteral outcomes, pending-receipt outages, stale catalog link, string-valued action defaults, superseded ambiguous retries and skipped assignment pages. The round comment records evidence and no dropped findings.

Real authoring subsequently exposed missing dynamic-outcome facets on the catalog wire and specialized scalar editors losing to the generic editor. Both have failing regressions and fixes. Complex new activity defaults must use fixed Object syntax, so JSON text defaults become arrays/objects rather than string values. Existing activity contract surfaces are retained; the new task's intended surface is added to the baseline.

Causal controls observed: disabling the accepted-response resume guard makes the forged-resume test fail; restoring it passes. Disabling collection-derived port consumption makes its three tests fail; restoring it passes. New pending-receipt, physical-boundary and UI regressions fail before their fixes and pass after them. The default-wire regression likewise fails with Literal JSON text and passes with the restored Object defaults. Round2 repaired optional omitted/null outcome collections and exact capability readiness. Round3 adds shared replica-safe cursor keys and replaces the library-host handler loop with canonical Events composition; both have failing-before/passing-after witnesses.

## Remaining exits

- Complete latest-source whole suites/count comparison, final architecture/maps/tier/reference/locked restore and exact-head required CI, including full provider/hosting qualification.
- Finish a clean independent exact-head review round; mark ready, request repository review and merge only under the repository's permitted process.
- Rebuild resulting main and repeat decisive native/client/browser/restart journeys. Reconcile specification, Program, feature and linked parity/BPMN records before closure.

All normal hosts and integration databases are owned and disposable. Earlier development stores are retained; user hosts/stores were not used. Detailed local logs live under `/tmp/elsa-user-tasks-3577`; durable evidence is the source and commands/results on this PR and Program.
