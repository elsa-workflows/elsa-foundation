# Implementation Plan: Feature Selection Reflow

**Branch**: `agent/codex/3175-feature-selection-reflow` | **Date**: 2026-10-21 | **Spec**: [spec.md](spec.md)

**Input**: Issue #3175 and its measured 320px/360px Feature Management selection-row defect.

## Summary

At narrow Feature Management containers, lay out the existing visible-count label and selected-count span on the first grid row, then place Enable, Disable, and Clear in three equal action cells on the second row. Scope the change to the module's existing `feature-management` container query below 480px. Preserve the wide flex row, markup, selection state, and every action handler.

Add a browser fixture that mounts the real page with in-memory Studio and Server catalogs. A Playwright test will measure the rendered row at 320px and 360px viewports, a 286px module container inside a wide viewport, and a 1280px reference; it covers empty and 114-selected states without invoking Enable, Disable, or Apply.

## Technical Context

**Language/Version**: CSS; React and TypeScript in the existing Studio browser harness.

**Primary Dependencies**: Existing Studio module CSS, React 19.1, `@playwright/test` 1.61.1, and the existing Studio Vite browser fixture. No new dependency.

**Storage**: None. Browser fixture catalogs and selection state are in memory.

**Testing**: Focused Playwright browser regression, existing Feature Management Vitest suite, scoped Stylelint, and focused module client build.

**Target Platform**: Existing Chromium browser project, including narrow viewport and container sizes.

**Project Type**: Existing Studio extension module and Studio browser tests.

**Performance Goals**: No performance claim; layout remains CSS-only.

**Constraints**: No markup or behavior changes; use the existing named container query. Counts and buttons must remain visible and untruncated; no overflow clipping or horizontal scrolling. Do not activate/apply feature changes in the test.

**Scale/Scope**: One CSS selector group and one small browser fixture/spec; no backend, permission, package, module-reference, configuration, navigation, or CI changes.

## Constitution Check

- Framework §2.21.1: this is a presentation correction; all existing behavior and assertions remain valid. The affected browser regression is additive.
- Framework §2.21.2: this is not greenfield application logic. The issue requires a rendered regression, and the implementation will demonstrate that it fails before the CSS change and passes afterward.
- Framework §2.23: no logic-bearing implementation, feature class, registration, or public surface is added. The browser test is warranted by the acceptance criteria.
- Elsa constitution: no API, persistence, activation, permission, dependency, or feature-composition rule changes. No exception or architecture decision is needed.

**Post-design check**: Same scope. The fixture exercises the existing page with mocked in-memory catalogs; no external interface or data model is introduced.

## Project Structure

### Documentation

```text
specs/998-feature-selection-reflow/
├── spec.md
├── plan.md
├── research.md
├── quickstart.md
└── tasks.md
```

### Source and tests

```text
Extensions/FeatureManagement/studio/Elsa.Studio.FeatureManagement/Client/src/styles.css
Studio/tests/browser/feature-management.html
Studio/tests/browser/featureManagementFixture.tsx
Studio/tests/browser/feature-management-layout.spec.ts
```

**Structure Decision**: Keep responsive ownership in Feature Management's existing stylesheet and use Studio's existing Playwright/Vite harness to render that exact module page. The fixture's host catalogs are test-only and stay in memory.

## Design

The page already declares `container-type: inline-size` and `container-name: feature-management`; the query below 480px already reflows the filter/category toolbar. Add the bulk-action grid to that same query. Keep the JSX and interactive order unchanged. At narrow widths, make the wrapper's children participate in the outer three-column grid so the visible label spans columns one and two, the selected count occupies column three, and the three buttons occupy one cell each on the next row. Restore the selected-count overflow behavior at narrow sizes so it cannot ellipsize.

The wide default CSS remains untouched. A fixed viewport-width media query is not suitable because the module can be narrow inside a desktop viewport.

## Verification

See [quickstart.md](quickstart.md). Establish the focused browser test's red result before changing `styles.css`, then require it to pass after the CSS fix. Run the existing client module suite, the scoped CSS lint, and the module client build once on the final diff. Root owns normal-host evidence and exact-head integration gates.

## Complexity Tracking

No constitutional violations, new dependencies, API surfaces, or architectural exceptions.
