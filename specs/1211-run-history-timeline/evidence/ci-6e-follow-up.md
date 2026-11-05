# Current CI failures and bounded correction

This records diagnosis and supporting subsets, not acceptance or a new formal review round. Review remains **3/6**.

## Published source and observed results

PR head `6e73c806ca6979660c20ea26cdab8e63414ba737` completed [CI42045484081](https://github.com/elsa-workflows/elsa-foundation/actions/runs/42045484081) with failure. The composed runner reports clean merge `73549adbc535ad561eb646834cb4ee61041fec2f`, whose verified parents are delivered main055 and that PR head. Its tree `4c6f0a283bb15e55c73ce52582e96b398cb077ff` equals the PR tree.

| Gate | Actual result | Limit |
|---|---|---|
| Generated maps, architecture guards, Core-only, PR shard 3, both backend source hosts | Passed | Supporting checks |
| Studio .NET and frontend checks | Passed; Workflows 3,435/3,435 across 246 files | No composed acceptance implied |
| PR shard 1 Dispatch project | 406 passed /10 failed /0 skipped, total416 | Ten new owner-observation metadata assertions failed; normal CI retry also failed |
| PR shard 2 Activities Runtime project | 774 passed /10 failed /0 skipped, total784 | Same native metadata expectation error; normal CI retry also failed |
| Browser shard 1 | Passed | Full counts remain in the original job log |
| Browser shard 2 | 500 passed /2 failed /4 skipped, total506 | Both narrow instance accessibility cases stop at the second Data value assertion |
| Composed primary | 20 passed /4 failed /0 skipped or retried, total24 | All four Timeline cases fail restoring workspace style after Minimal capture |
| Composed diagnostics | Not reached | Primary phase failed |
| Full tier | Not run on this draft | Draft decision success is not test execution |

The current canonical inventory is **24 primary +2 diagnostics =26**: five nonhistory journeys in four projects, plus four Timeline projects. All **20 Timeline frames per history project** remain required. The older 20+2 count describes an earlier integration snapshot, before the additional main journeys.

Downloaded artifact `studio-ux-73549adbc535ad561eb646834cb4ee61041fec2f-1`, ID `13428526456`, is 417,915,733 bytes. Original report, Playwright results, PNG/ARIA and traces remain retained at `/tmp/1211-6e73-ux.zip`; concise case inventory is `/tmp/1211-6e73-ux-case-inventory.json`. The author opened actual desktop-light failed-menu and 320px dark Minimal PNGs and the menu ARIA. This is diagnosis, not independent visual approval. Raw traces remain local and must not be reproduced with request credentials.

## Fixture corrections

The normal ThemeSwitcher offers two distinct `Linen` menu items: workspace style and color scheme. The native fixture selects Minimal successfully and captures its controls, then fails restoring the previous style because its global exact-name selector matches both. Scope the selected item, Minimal action and restoration to the existing accessible `Workspace style` group. Retain the real preference action, palette, URL/row preservation, style restoration, every capture and all existing timeouts/assertions. Product controls and rendering remain unchanged.

Both 390px instance accessibility cases first choose Data, return to the canvas and then reopen the selected node with Space. Reopening the narrow inspector defaults to Journal. The second value assertion must explicitly choose Data through the same helper as the first. The correction retains actual canvas visibility, selection, focus restoration, completion receipt, overflow and axe assertions; no manual focus restoration or timeout increase is added. Hosted browser jobs do not upload traces in this workflow; the original failing locator and callsite remain in `/tmp/1211-6e73-ci-browser2.raw.log`.

All four User Tasks journeys passed in this run. Exact owned Timeline teardown retained each original failure and recorded a separate 202 rejection receipt before archiving its definition; none is UI completion/restart evidence. The original contaminated-inbox failures remain in [the predecessor record](ci-bab-follow-up.md).

## Actual backend subsets and pending correction

The first clean Runtime24 preparation at `1b372d5adb8f12dc768e087d7548685ecb2e4d7f` stopped at a canonical BPMN archive HTTP504, before restore or any test. A separately granted replacement used the three original hash-verified pinned archives with the unchanged canonical verifier. It passed **24/24**, comprising 14 bulk and10 compound cases, with zero skipped/retried rows. Original failed and replacement receipts are retained separately.

Current CI's20 owner-observation failures compare preclaim metadata with a native claimed intent. Native claiming adds `runtime.delivery.firstAttemptedAt` before observing history. Local `79ef29012617143122532e01491bd55f1896b5e3` adds that exact fixture-clock timestamp to a copy of every original metadata entry, separately asserts the typed timestamp, then compares the entire ordered dictionary. No key is ignored. Its granted phase passed Activities **13/13** and Dispatch **13/13**.

That same phase then recorded EF **1 passed /3 failed /0 skipped**, and stopped without retry. The three failures compare separately reconstructed `RuntimeHistoryHead` records containing a policy with an `IReadOnlyList`: generated record equality compares the list references. The diagnostic displays look identical, but the comparison is invalid. Later assertions in those three cases were not reached. Local `51e960ecc8d92e27f0c99b829a383851282e1d9c` compares every scalar head field, every policy scalar and the complete ordered supported-kind array; strict transaction/marker counts, 4,097-entry fixtures, fresh-context replay and refusal assertions remain. Its EF4 rerun is queued, **not run**.

Original subset receipts are `/tmp/1211-r4-1b-runtime-positive-pinned-cache/terminal-outcome.json` and `/tmp/1211-r4-79-remaining-positive/terminal-outcome.json`. Actual TRX rows and binary manifests bind results to those sources. The latter's original path-only cleanup check missed generic idle build daemons; the deputy removed only owned MSBuild group19659, and the author removed verified compiler PID19663. `/tmp/1211-r4-79-remaining-positive/cleanup-final.json` retains the correction and verifies all eight recorded owned groups empty. Rider group1105 and unrelated hosts/stores were untouched. Future audits must inspect recorded process groups, not only worktree paths.

These separate-source subsets are not an exact-current-head 54-case pass. Current causal controls, surrounding/provider qualification, all26 composed journeys, independent UX inspection, required same-head CI and protected merge/resulting-main verification remain open. No reserved Inputs renderer/CSS, unpublished local main or retired demo environment was changed.

## Follow-up e18: native context capture

Normal publication initially returned GitHub's internal `commit_refs_rules` error. One ordinary later push updated the branch to `e18bfbb2bb43e3c5cb0091a91c8486a26cdf71af`, while the raw PR head remained6e. A single close/reopen of the same draft refreshed it; both raw refs now match e18. [PR recovery receipt](https://github.com/elsa-workflows/elsa-foundation/pull/3850#issuecomment-6663156878) records genuine new-head CI42063547589 and the obsolete6e run cancelled by normal concurrency. No source-only trigger commit, workflow dispatch or forced ref update was used.

Its composed job failed with **20 primary passed /4 failed**, diagnostics not reached. Other jobs were still running at diagnosis; frontend, PR shards2/3, Core-only, source checks, architecture and maps had passed. The clean composed merge `bdd09d37679af5fefa0a8ea7bbea5fd4d15346de` has parents main055 and e18, and tree `6155440076ab63b819fb71558d9fc074e67e8569` equals e18. Artifact `13433566921`, 499,029,826 bytes, is retained at `/tmp/1211-e18-ux.zip`; report and concise inventory are `/tmp/1211-e18-ux-report.json` and `/tmp/1211-e18-ux-case-inventory.json`.

All four Timeline cases now reach seven frame pairs through `bookmark-consumed`, including the real owned restart and normal task-resumption checks. They next fail `activity-journal-context`: the activity heading has viewport ratio `0.9800000190734863` instead of1. Later journal, causal/value navigation, fault/stale frames and diagnostics remain unqualified. Passing those earlier scenes is not complete UX acceptance.

The author and existing Studio source worker opened the actual desktop-light and320px-dark failure PNGs/ARIA. Safe trace DOM snapshots identify desktop `.wf-instance-tab-content` scrollTop31 and narrow outer Timeline workbench scrollTop3534. The fixture explicitly start-aligns the15px heading with1.25 line height (18.75px); stable0.98 visibility is consistent with approximately0.375px vertical clipping at an integer scroll boundary. The selected Journal tab remains in frame. This supports a fixture-alignment correction, not a demonstrated product geometry defect; it is author preflight, not formal independent acceptance.

The prepared correction preserves native start alignment and then makes an ordinary−8px scroll on the nearest already active `overflow-y:auto/scroll` ancestor. This keeps the heading and nearby Journal tab together. It changes no DOM content/styles or product state. Ratio1, all five hit-tests, both context anchors, all20 frames and normal300-second timeout remain. Actual geometry in all four projects requires qualification. Current e18 jobs are retained; no replacement push was made merely to cancel unfinished evidence.

The separately reserved51e EF4 phase has since passed **4/4**, zero skips/retries, after normal canonical verification/locked restore. The complete head/policy, fresh-context replay,4,097-entry transaction/marker and refusal assertions executed. [Scoped receipt](https://github.com/elsa-workflows/elsa-foundation/pull/3850#issuecomment-6663395395) preserves exact TRX/binary/source manifests and original79 failures. `/tmp/1211-r4-51e-ef-positive/cleanup-final.json` verifies all five launched/descendant groups empty using78 scoped observed process identities; no signals or unrelated mutations. Its backend/build source equals e18. No current-head54-case aggregate or whole-feature acceptance is asserted; the next single saved-resume causal phase is requested separately, not launched.
