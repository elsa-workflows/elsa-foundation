# Published R4 CI failures and correction boundary

This records actual failures, not feature acceptance or a new formal review round.
Independent review remains at **3/6**. No failure, skip or timeout is waived.

## Immutable source and retained artifact

- PR head: `bab905ec25df7e32fd1909dacfac2922f0a3f5e0`.
- Actual hosted composed source: merge `6845c15f8fc2eb8479d996824ddab94211aba714`, reported clean.
- Verified merge parents are main `fbd86b5ead89d4a00f12d647605a7e7502f7d2a6` and the PR head above. Tested merge and PR head have the same tree, `dab5c83725ac7602105b00a58c6e6db5bc9087b4`.
- [CI run 42023615032](https://github.com/elsa-workflows/elsa-foundation/actions/runs/42023615032) completed with failures while delivery was paused.
- Downloaded native artifact: `studio-ux-6845c15f8fc2eb8479d996824ddab94211aba714-1`, artifact ID `13417457448`, 379,422,180 bytes. Its original report, Playwright results, screenshots, ARIA and traces are retained. The author opened the failed 320px Timeline image; this is diagnosis, not independent visual acceptance.

## Actual results

| Gate | Observed result | Qualification limit |
|---|---|---|
| Backend source checks | Ubuntu and Windows passed | Does not compile or execute the new test projects |
| Studio .NET | Passed | Does not establish browser or composed UX acceptance |
| Architecture guards and generated maps | Passed | Supporting gates only |
| PR shards and Core-only | Compilation failed | New Runtime fixture lacks `Services.Bookmarks`; new Dispatch fixture lacks `Elsa.Activities.Testing` |
| Workflows unit tests | 3,426 passed; 9 failed, across 246 files | Nine iteration-navigation cases still assume the activity Data view is initially visible |
| Browser shard 2 | 487 passed; 15 failed; 4 skipped | Includes narrow inspector initialization, explicit Data selection, fixture URL and Timeline readiness failures |
| Browser shard 1 | 488 passed; 17 failed; 2 skipped | Includes the same changed view assumptions in motion and Golden Runs cases |
| Composed primary journeys | 13 passed; 7 failed; 0 skipped or retried, 20 observed | Four Timeline cases timed out; three User Tasks cases failed their initial empty-state assertion |
| Composed diagnostics | Not reached | The failed primary phase prevented the two diagnostics from running |
| Full tier | Not run on this draft | The draft decision is not full-tier test evidence |

The expected complete composed inventory remains **20 primary plus 2 diagnostic journeys**. The observed 20-case failed primary run is not a 22-case acceptance result.

## Concrete corrections and remaining work

Commit `8e4f50ca1` adds only the two missing test imports. Normal merge `1b372d5adb8f12dc768e087d7548685ecb2e4d7f` integrates delivered GitHub main `697e9a342998282a08a598ad7137b2e90c9435c2`; the only conflict was comment wording around the identical post-mailbox cancellation guard. Its executable behavior and incoming regression are retained. Unpublished local main `b7fc0ec33` was not merged or changed.

All four actual Timeline traces stop at the same Minimal capture locator. The outer locator selects a form inside the Run timeline; its `has` locator incorrectly starts at the containing Run timeline again. Playwright resolves `has` relative to each form, so the form cannot match. The correction starts the inner locator at the searchbox. The native ThemeSwitcher action, visible/unobscured anchor checks, full capture inventory and existing 300-second limit remain unchanged. This correction requires a normal rerun.

The browser failures also identify a product initialization defect: opening Diagram with no activity selected can request the activity inspector before the measured narrow layout is available, hiding the canvas behind an empty inspector. The correction must preserve explicit activity/incident targets and the Timeline workspace. Existing Data-value assertions must navigate to Data while preserving the Journal default and their keyboard/focus checks. Off-route appearance fixtures must use a canonical owned run URL rather than weakening the product URL-write guard. Golden Runs tests must recognize the intended Timeline default without discarding their list-return and identity assertions.

The three User Tasks failures are `desktop-dark`, `narrow-light` and `narrow-dark`; `desktop-light` passed. They fail the `No tasks here` heading assertion. The actual desktop-dark ARIA inbox contains the Open task `Purchase approval desktop-light-56bb233b`, created by the preceding failed Timeline case. Its timeout occurs before UI task completion, and its old teardown archives only the definition. These are downstream fixture contamination, not a demonstrated User Tasks product defect. The Tasks owner has been informed. Exact owned task/run/definition cleanup through existing guarded APIs is being prepared separately from UI completion/restart acceptance; the original failure verdict and pre-cleanup evidence must remain visible.

The shared Inputs renderer pair and adjacent CSS remain reserved. At local main-integrated `1b372d5ad`, the blobs are `7fba03d55df3ba489d5acfb55892de7fe86c4bc2` (WorkflowActivityExecutionDetails), `88be5d2426b4b348797462bcb1e27f3959d9b6fd` (RuntimeValueEvidence) and `b0b5f0874ccb224098a5d8ec3f8a691792ed6d23` (activityInspection.css). This is an integration handoff, not published or accepted source.

The owner explicitly resumed delivery after the demo. Local heavy phases still require the supervisor's resource reservation. No retry, new full hosted dispatch, native host or automatic merge was launched while diagnosing these failures. The first requested phase is a clean, bounded Runtime-only compilation and 24-case positive cohort; other new tests, causal controls, current frontend/browser/provider qualification, full native 22-case acceptance and exact-head review/merge remain open.
