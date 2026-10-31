# Tasks: Minimal Studio presets

## Setup and foundations

- [x] T001 Review the approved specification/research/plan; retain approved images and normalize their measurements in `contracts/visual-contract.md`.
- [ ] T002 Add the three palettes, `minimal` workspace, inherited treatment and shared roles in Hosting Client themes/UI tokens.
- [ ] T003 Preserve optional workspace metadata through frontend clone/normalize/validation and typed backend theme save/load/import/export; reserve new builtin IDs without replacing old Atelier.

## US1: Choose a complete preset

Independent proof: choose each preset, reload, vary independent axes, preview/clone custom themes and reset to host defaults.

- [ ] T004 Implement an explicit complete-preset action and correct default/override/preview precedence in `ThemeProvider.tsx`.
- [ ] T005 Add useful preset previews in Appearance, the Minimal workspace choice and an editable/clearable theme presentation default in Theme Builder.
- [ ] T006 Cover preset/metadata/persistence/legacy/custom behavior in whole affected frontend and `ElsaThemeStoreApiTests` suites; record a red/green bite proof.

## US2: Work across coherent screens

Independent proof: six reference screen types and the standalone public UI consumer share neutral type/geometry/boundaries while retaining domain interactions.

- [ ] T007 Implement public control/collection/card/disclosure roles in shared UI CSS and owner shell/settings styles.
- [ ] T008 Apply owner-local workflow list/status/inspector and deferred designer decoration without changing ports, runtime semantics or editor state.
- [ ] T009 Group builtin/custom templates and refine cards/actions using actual provenance and truthful previews in `WorkflowTemplatesPage` and owner CSS.
- [ ] T010 Apply shared roles to Structured Logs and Connection dialogs in their owner stylesheets.

## US3: States, keyboard and responsive access

Independent proof: narrow/desktop, high contrast, real state transitions, copy/selection/dialog behavior and retained editor/runtime occurrences.

- [ ] T011 Add a real ThemeProvider minimal-preset browser witness and real Structured Logs module browser composition; extend existing appearance/collection/template checks where appropriate.
- [ ] T012 Verify keyboard/focus, 320px overflow containment, modes, semantic confirmation and source-owned/read-only state through actual rendering.

## US4: Integration, fidelity and delivery

- [ ] T013 Update canonical `Studio/docs/design-tokens.md`, `module-ui.md`, `branding.md`, appearance guidance and affected extension/API catalogs where applicable.
- [ ] T014 Run whole affected suites, standard lint/typecheck/build, consumer proof, Studio .NET, architecture and maps. Compare executed/discovered .NET coverage.
- [ ] T015 Capture and review all six real screen types against retained references; refine until visual-contract details agree and record remaining differences honestly.
- [ ] T016 Complete independent correctness/standards/acceptance review, draft PR evidence, hosted exact-head qualification, merge and resulting-main verification.

## Dependencies and execution

T001 precedes T002–T005; T003 precedes persisted metadata proof; T007 precedes owner module decoration. T011–T012 prove the integrated presentation. T013–T015 complete before final review and hosted qualification. Implementation remains one coherent PR; independent planning research and review lenses may run concurrently, while edits to shared files remain sequential. Use existing scoped build/test queues and do not bypass them.

Review: all four user stories have implementation and independent verification tasks; no unresolved clarification or constitutional exception remains. Optional commit hooks are handled by the normal local commits; no additional approval is required by the owner's delivery instruction.
