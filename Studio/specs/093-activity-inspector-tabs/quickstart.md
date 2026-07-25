# Quickstart: Validate Activity Inspector Tabs

## Prerequisites

- Install workspace dependencies with the repository's pinned package manager.
- Use a Chromium browser available to Playwright.

## Focused component validation

Run the activity-Inspector and affected editor tests:

```bash
pnpm --filter @elsa-workflows/studio-workflows exec vitest run \
  src/__tests__/activityInspectorTabs.test.tsx \
  src/__tests__/inspectorSlotActions.test.tsx \
  src/__tests__/outputCaptureEditor.test.tsx \
  src/__tests__/inputReferenceUi.test.tsx
```

Expected outcomes:

- Inputs and Outputs are always first and second.
- Variables and Slots appear only when supported.
- Details and Version are always available.
- Exact content appears in one agreed location.
- Tab semantics, keyboard behavior, persistence, fallback, and activity-reset rules pass.

## Static validation

```bash
pnpm --filter @elsa-workflows/studio-workflows typecheck
pnpm --filter @elsa-workflows/studio-workflows build
pnpm lint
```

Expected outcomes:

- TypeScript compilation succeeds.
- The Workflows bundle stays within its existing budget.
- JavaScript/TypeScript and token-based CSS rules pass.

## Browser validation

```bash
pnpm exec playwright test tests/browser/activity-inspector-tabs.spec.ts --project=chromium
```

Verify:

1. The outer Inspector, Runtime, and Artifacts tabs remain unchanged.
2. The activity name and applicable notices remain visible while long Inputs content scrolls.
3. The inner tab order is Inputs, Outputs, conditional Variables, conditional Slots, Details, Version.
4. Every label remains reachable at the minimum expanded Inspector width without page-level horizontal overflow.
5. Keyboard arrows, Home, and End move focus and selection through the tablist.
6. Inputs and Outputs display explicit empty states when applicable.
7. Switching tabs preserves tab-local scroll and transient state.
8. Switching activities preserves a valid active tab, falls back from missing conditional tabs, and resets per-activity state.
9. Leaving Inspector and returning during the same editor session restores the active inner tab.

## Visual review

Capture the browser fixture in the repository's light and black-glass themes at:

- A normal desktop Inspector width.
- The minimum expanded Inspector width.
- A long Inputs panel scrolled away from its top.

Confirm that the fixed context remains compact, the inner row reads as secondary navigation beneath the outer panel tabs, focus treatment is visible, and no raw-color or theme-specific styling has been introduced.
