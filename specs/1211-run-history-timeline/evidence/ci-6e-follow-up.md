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
