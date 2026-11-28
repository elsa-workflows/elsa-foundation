# Quickstart: Verify Activity Instance Metadata

1. Start Foundation and Studio using the repository development instructions.
2. Open an editable workflow and select an ordinary activity.
3. In Details, enter a Display Name and multiline Description.
4. Confirm the Inspector title and one-line canvas label update immediately while the existing
   input summary remains the node subtitle.
5. Undo and redo, reload the draft, duplicate the node, change its activity version, and delete the
   duplicate; confirm the documented metadata lifecycle.
6. Copy Node ID, Activity Type, and Activity Version ID by keyboard and pointer. Confirm exact values,
   local check state, and screen-reader announcement.
7. Publish and create a Test Run. Edit the draft wording afterward and confirm both historical
   surfaces retain the frozen wording.
8. Confirm no canvas node shows a version. Confirm palette versions appear only for genuinely
   ambiguous multiple selectable versions.
9. Run targeted Studio Vitest/Playwright tests and Foundation design, publishing, runtime, and
   persistence tests listed in `tasks.md`.

## Verification results

- Studio typecheck and lint pass. Lint reports no errors; its 50 warnings are pre-existing.
- Focused Studio presentation, Inspector, accessibility, executable graph, persistence, and shared UI
  tests pass (54 tests), as does the historical Executable Inspector regression.
- Playwright `activity-inspector-tabs.spec.ts` passes (3 tests).
- Foundation Design Core (4), Design Groundwork (87), Design API (64), Publishing API (437),
  Runtime API (88), and Groundwork fixture (60) suites pass.
- The complete Studio Workflows package reaches 988 passing tests. Four unrelated baseline failures
  remain: an existing unsupported-root subtitle expectation, two load-sensitive five-second
  timeouts, and an existing folder-focus assertion. Each feature-focused equivalent passes.
