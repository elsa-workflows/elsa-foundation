# Tasks: Studio appearance

## Setup and foundations
- [x] T001 Claim #3447, inspect existing contracts and record approved scope in spec.md.
- [x] T002 Review plan.md, research.md, data-model.md and contracts/appearance.md against both constitutions.

## US1 - Independent appearance
- [ ] T003 [US1] Add persistence and mounted switching regressions in the host Client/src/__tests__/appearance.test.ts and workspaceAppearance.test.tsx; record the failing baseline.
- [ ] T004 [US1] Implement independent style/scheme resolution, transient preview, store fallback and reset in host app/themes/appearance.ts, workspaceStyles.ts and components/ThemeProvider.tsx.
- [ ] T005 [US1] Expose independent accessible choices in host components/ThemeSwitcher.tsx and settings/AppearancePage.tsx, preserving preset selection.

## US2 - Three complete styles
- [ ] T006 [US2] Implement semantic surface/edge roles and coherent shared shell/control/settings treatments in host app/ui/tokens.css, themes/workspaceStyles.css and App.tsx.
- [ ] T007 [US2] Implement list, Sequence, graph and inspector treatments with accessible floating resize and adaptive clearance in Workflows surface-owned styles.
- [ ] T008 [US2] Add rendered browser journeys and inspect actual screens in Studio/tests/browser for all styles/modes and desktop/narrow layouts.

## US3 - Compatibility and qualification
- [ ] T009 [US3] Extend delayed/failed custom-store, legacy preset, preview, reset and live mounted-state tests in host frontend suites.
- [ ] T010 [US3] Run complete affected frontend suites, types/lint/build/budgets and required Foundation gates; reconcile failures and preserve evidence.

## Delivery
- [ ] T011 Update Studio/docs/design-tokens.md and UX/ADR guidance for the final additive contract; record validation in this spec and #3447.
- [ ] T012 Review the exact diff independently for correctness, standards and acceptance, commit, open one draft PR, attach it and follow review/CI gates.

## Dependencies and execution
T001–T002 → T003–T005 → T006–T008 → T009–T012. Persistence research and surface inspection can proceed independently; code touching the same files is sequential. The first independently testable increment is US1; implementation continues through all approved stories.
