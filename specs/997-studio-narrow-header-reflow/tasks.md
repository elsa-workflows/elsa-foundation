# Tasks: Studio Narrow Header Reflow

**Input**: Design documents in `specs/997-studio-narrow-header-reflow/`.

**Prerequisites**: `spec.md`, `plan.md`.

**Tests**: The user-visible geometry regression and compatibility coverage are required by #3165 acceptance. The fixture geometry assertion was run RED on the unchanged `a710adf` CSS before the correction; root's locator-assisted baseline movement remains separate from the final candidate’s native pointer/keyboard proof.

## Phase 1: Baseline and Design Selection

**Purpose**: Establish current-main failure evidence and select a candidate before implementation.

- [x] T001 [US1] Review root's #3165 normal-host observations and record the 320px/360px locator-assisted movement as automation evidence, not native-pointer proof; retain the Light-click 360px no-shift result and the fixture's clipped-target/overflow RED in `specs/997-studio-narrow-header-reflow/plan.md`.
- [x] T002 [US1] Review root's selected matched option evidence and record the combined wrap, quick-mode compaction, and avatar-account CSS decision in `specs/997-studio-narrow-header-reflow/plan.md` before implementation.

## Phase 2: User Story 1 — Reachable narrow Studio header (P2)

**Goal**: Every retained header action remains visible and operable without horizontal page movement at narrow widths.

**Independent Test**: `Studio/tests/browser/user-menu.spec.ts` checks actual rendered geometry and pointer/focus behavior for the selected candidate; normal composed-host after proof confirms exact host artifacts at 320px and 360px.

- [x] T004 [US1] Extend `Studio/tests/browser/userMenuFixture.tsx` to vary navigation preference, account display name/session state, and the real `AgentLauncher` session indicator without duplicating `ShellFrame`.
- [x] T005 [US1] Add a baseline-failing geometry and interaction case to `Studio/tests/browser/user-menu.spec.ts` that records action bounds, document/frame/action `clientWidth` and `scrollWidth`, horizontal offsets, and module-page left position at 320px, 360px, and the selected breakpoint edge.
- [x] T006 [US1] Apply the root-selected max1024px action/status wrapping and automatic Weaver height, plus max640px quick-mode compaction and avatar-only account presentation in `Studio/src/apps/Elsa.Studio.Web/Client/src/app/styles.css`; make no production TSX or component stylesheet change.
- [x] T007 [US1] Extend the same browser spec to verify pointer and keyboard mode/theme/navigation selection, native source anchors, account menu and keyboard sign-out, representative dense Weaver badges, keyboard order, Escape, focus restoration, 640px/641px behavior, and bounded account presentation at1280px and1440px.
- [x] T008 [US1] Run `pnpm exec playwright test tests/browser/user-menu.spec.ts` from `Studio` and the selected affected Client Vitest tests from `Studio/src/apps/Elsa.Studio.Web/Client`; record results against `Studio/tests/browser/user-menu.spec.ts`.
- [x] T009 [US1] Run `pnpm lint:css` from `Studio`; if TSX changes require it, also run `pnpm lint`, `pnpm typecheck`, and the Client `pnpm build`; record affected results against `Studio/src/apps/Elsa.Studio.Web/Client/src/app/styles.css`.
- [x] T010 [US1] Produce exact-source normal-host after proof and a meaningful revert/bite-proof showing the regression assertion fails when the selected correction is removed; record final host evidence with the #3165 issue checkpoint and link the evidence from `specs/997-studio-narrow-header-reflow/plan.md` and keep #3075's separate prerequisites open.

## Dependencies and Review Order

- T001 and T002 capture/reconcile the root-owned baseline and design decision before implementation.
- T004 and T005 precede T006 so the responsive assertion is already in place.
- T006 precedes T007 through T010.
- T001 and T002 are root-owned review/evidence tasks; implementation tasks start only after root selects a design.
- No tasks are parallelized because the fixture, breakpoint behavior, and CSS decision are one responsive slice and root owns serial host evidence.

## Implementation Strategy

Complete the baseline and visual comparison first. Then extend the existing real-shell fixture and geometry test, implement the approved CSS correction, and run the focused browser/client checks. Root completed isolated normal-host after proof and mutation evidence. Hosted exact-head CI/review, architecture/maps and resulting-main verification remain integration gates before closing the child. This implementation PR carries the terminal spec status under `docs/reference/spec-lifecycle.md`; it becomes true on merge.
