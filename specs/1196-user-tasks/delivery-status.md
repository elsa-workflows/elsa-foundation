# Delivery qualification record

Program [#3688](https://github.com/elsa-workflows/elsa-foundation/issues/3688), acceptance [#3577](https://github.com/elsa-workflows/elsa-foundation/issues/3577), draft PR [#3691](https://github.com/elsa-workflows/elsa-foundation/pull/3691).

Design and implementation are present. M2 is complete; integrated qualification and independent review remain active. Neither the Program nor the feature is accepted, merged or closed. Keep the specification Draft until the remaining delivery gates pass.

## Resumption and ownership

The initial checkpoint stopped after three successive failures of the new HTTP fixture, under the organization protocol. The owner instructed “Continue 20 more times maximum” on 3 November. Fresh app claims were established and blocking labels removed. Additional attempt 1 of the formerly blocked check passed all four tests: management authority permits inspection after group revocation, while completion remains forbidden. No further retry of that HTTP check was needed. See the PR history for the initial checkpoint and claim receipts.

The packet was renumbered from1190, then1194, to1196 after current main and open PRs occupied1190–1195. Historical links pinned to the initial commit remain valid.

## Executed evidence

The following are observed intermediate runs. Final gate comments identify the reviewed head; these results do not certify later changes automatically.

| Boundary | Evidence |
|---|---|
| Lifecycle/concurrency/recovery | Whole Runtime3,221; whole EF1,874 before the latest integration witnesses; latest task-specific EF20 passed. Real overlapping independent SQL contexts, competing claim/complete/cancel, three-generation Durable/Balanced recovery, forged and replaced waits, transient reads before/after bookmark claim, missing-state uncertainty and deletion retention are covered. |
| Supported databases | PostgreSQL, SQL Server and MySQL task contract tests3/3, required providers with no skips; SQLite in the full EF suite. |
| Trusted identity/API | Whole Identity559, Runtime API222, real two-user Workbench claim race/release/revocation/reassignment/trusted completion actor journey passed. Canonical user ID survives first-party bearer issuance/refresh; non-user subjects do not gain task authority. |
| Activity integration | Activities Runtime732 executed/730 listed, DispatchWorkflow380/380, Publishing API914 before the final default-wire regression; native BPMN User Task worker passed. Listed counts can expand at execution for dynamic theories. |
| Studio | Workflows3,183, Hosting1,054, UserTasks17 mounted tests; module/Workflows/live type checks, builds, CSS/layout guards passed. Normal signed-in1440/390 inbox journeys passed keyboard/axe, required/readonly validation, explicit false, exact data, duplicate receipt and one branch. Actual designer form/activity authoring, all three visual action connections, save/reload, publication and selected-branch execution passed. |
| Custom forms/client | Real PowerShell authoring/publication/completion client passed all3 configured action paths with exact response outputs. Async external provider now passes the real durable native-wait journey; matching nested custom renderer passes the mounted UI. A separately compiled consumer outside the workspace uses public contracts and the actual packed Studio feature without project references. |
| Packaging | Actual UserTasks archive verifies dependency metadata and byte-exact compiled JS/CSS/style-entry assets; public-contract consumer loads the packed feature. This is a scoped module/contract proof, not a whole distribution release. |
| Architecture/composition | Architecture614; locked server solution restore; generated filter check and129-project tier manifest check passed before the final catalog/default additions. |

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
