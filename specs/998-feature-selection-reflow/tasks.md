# Tasks: Feature Selection Reflow

**Input**: Design documents in `specs/998-feature-selection-reflow/`.

**Prerequisites**: `spec.md`, `plan.md`, `research.md`, and `quickstart.md`.

**Tests**: Add the rendered regression because the acceptance criteria concern actual browser layout. This is not a greenfield feature class or logic-bearing implementation under framework §2.23. Execute the requested red/green proof, then run the scoped existing module tests and CSS/build checks.

## Phase 1: Setup

**Purpose**: Use the existing Studio browser harness; no project or dependency setup is needed.

## Phase 2: Foundational

**Purpose**: No shared foundations are required for this CSS-only correction.

## Phase 3: User Story 1 - Read and use narrow selection controls (Priority: P2)

**Goal**: Keep both counts and all three action buttons fully legible in narrow Feature Management containers while preserving the existing wide row and interactions.

**Independent Test**: The Playwright spec measures empty and populated selection at 320px, 360px, and 286px module width, verifies containment/non-overlap/no horizontal scroll, keyboard order, and the 1280px row.

- [x] T001 [US1] Add the real-page mock-catalog fixture in `Studio/tests/browser/feature-management.html` and `Studio/tests/browser/featureManagementFixture.tsx`.
- [x] T002 [US1] Add rendered geometry and keyboard assertions in `Studio/tests/browser/feature-management-layout.spec.ts`.
- [x] T003 [US1] Run `pnpm exec playwright test tests/browser/feature-management-layout.spec.ts` from `Studio/` against the unchanged module CSS and record the expected failing geometry in `/Users/sipke/Documents/Codex/2026-10-19/studio-ux-audit/execution/3175-browser-red.log`.
- [x] T004 [US1] Reflow only `.feature-management-bulk-actions` in `Extensions/FeatureManagement/studio/Elsa.Studio.FeatureManagement/Client/src/styles.css` under the existing narrow named-container query.
- [x] T005 [US1] Run the focused browser spec, Feature Management client tests, scoped Stylelint, and client build; record command outcomes in `/Users/sipke/Documents/Codex/2026-10-19/studio-ux-audit/execution/3175-implementation-evidence.md`.

## Dependencies & Execution Order

- T001 and T002 create the test surface and can be completed before production CSS.
- T003 must run on the unchanged CSS before T004; the red result must identify the count truncation or overlap assertion.
- T004 depends on that baseline result.
- T005 runs once after T004 on the final diff.

## Implementation Strategy

Deliver the single P2 story as one increment: add the fixture and test, prove the current failure, make the scoped CSS correction, then run the focused checks. No feature action is invoked by the regression.
