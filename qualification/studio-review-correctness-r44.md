# Studio correctness R44 — final camera-source delta

Exact clean reviewed head: `3982b1c60a4029985272cf412a959eb20c28bc71`.
Prior reviewed base: `45888e0988e6e6863b80d31447137ef36e0e0252`.
Worktree: `/Users/sipke/.codex/worktrees/playback-studio-integration/elsa-foundation`.
Manifest: `studio-review-correctness-r44-source.json`.

**No actionable correctness findings in this finite delta.**

Independently verified the complete delta is `Studio/tests/acceptance/timelineDiagnosticsJourney.ts` only, +26/-11. All other 19,770 tree entries are identical, including production, C# qualification controls and process runner. This review made no source edits and ran no tests, builds or .NET; no peer report was used.

The shared `frame` helper (lines 19–34) yields through two animation frames, normally centers the real DOM target, then requires full viewport intersection, a bottom edge above the actual Console tab, and a center hit-test within the target's ancestor/descendant relationship. The ancestor alternative permits text inside a pointer-inert child to be framed via its native containing element. It does not permit an unrelated overlay to satisfy the hit check. This is camera framing, not forced interaction: no style mutation, fabricated API response, focus override, lower viewport ratio, arbitrary sleep or forced click was introduced. The checks remain assertions against the actual composed layout; this source review does not substitute for inspecting the new images.

All new targets are unambiguously scoped to the required surface:

- The pre-restart target at lines 149–154 is the real Active badge in the exact Bookmark lifecycle region. The lifecycle helper still waits for actual resource availability and the genuine creation-event action. Eligible text remains asserted, and the creation action remains enabled. Choosing the Active state as this camera's subject is coherent with the suspended-before-restart evidence; it no longer claims that the lower creation link must simultaneously be visible in that particular image. The process restart and history/task/bookmark/occurrence invariants are unchanged.
- The expiry Journal target at lines 195–199 is the exact Bookmark created text inside the Activity journal region, so a matching main Timeline row cannot satisfy it. The product's compact journal renders that name under its actual activity-scoped HistoryEventList (`RunHistoryTimeline.tsx:98–115`, `historyPresentation.tsx:160–181`). No unrelated event was selected to make the image pass.
- The duplicate target at lines 211–216 is the exact Duplicate delivery observed heading inside History event details, not a matching row in the main Timeline. Its native duplicate and single-consumption assertions remain unchanged.
- The stale target uses the same real Retry history button. Its stricter Console-boundary assertion is additive; the real request failure/recovery, exact ordered renderer rows, native ID membership, selection and inspector-heading assertions remain intact, and recovery still uses a normal native click.

The earlier loader/creation-readiness fix, collapse of Filter events after selecting All events, strict history prefix/unique IDs/allowed acknowledgement tail, exact occurrence snapshots, real process cuts and passive-expiry/duplicate controls are retained. No remaining earlier actionable finding is reopened by this camera-only delta.

The root reports the original e05c six-case functional run and completed review of its 66 images; those receipts keep their exact original attribution and are not re-executed here. The corrected whole-six retake, its actual image/ARIA review, privacy/export checks and resulting-main delivery remain separate required gates. Source-review cleanliness at this exact head is not an acceptance claim for those pending gates.
