# Tasks: Minimal Studio presets

## Setup and foundations

- [x] T001 Review the approved specification/research/plan; retain approved images and normalize their measurements in `contracts/visual-contract.md`.
- [x] T002 Add the three palettes, `minimal` workspace, inherited treatment and shared roles in Hosting Client themes/UI tokens.
- [x] T003 Preserve optional workspace metadata through frontend clone/normalize/validation and typed backend theme save/load/import/export; reserve new builtin IDs without replacing old Atelier.

## US1: Choose a complete preset

Independent proof: choose each preset, reload, vary independent axes, preview/clone custom themes and reset to host defaults.

- [x] T004 Implement an explicit complete-preset action and correct default/override/preview precedence in `ThemeProvider.tsx`.
- [x] T005 Add useful preset previews in Appearance, the Minimal workspace choice and an editable/clearable theme presentation default in Theme Builder.
- [x] T006 Cover preset/metadata/persistence/legacy/custom behavior in whole affected frontend and `ElsaThemeStoreApiTests` suites; record a red/green bite proof.

## US2: Work across coherent screens

Independent proof: six reference screen types and the standalone public UI consumer share neutral type/geometry/boundaries while retaining domain interactions.

- [x] T007 Implement public control/collection/card/disclosure roles in shared UI CSS and owner shell/settings styles.
- [x] T008 Apply owner-local workflow list/status/inspector and deferred designer decoration without changing ports, runtime semantics or editor state.
- [x] T009 Group builtin/custom templates and refine cards/actions using actual provenance and truthful previews in `WorkflowTemplatesPage` and owner CSS.
- [x] T010 Apply shared roles to Structured Logs and Connection dialogs in their owner stylesheets.

## US3: States, keyboard and responsive access

Independent proof: narrow/desktop, high contrast, real state transitions, copy/selection/dialog behavior and retained editor/runtime occurrences.

- [x] T011 Include current-main folders, tags, favorites and saved views; add a real ThemeProvider minimal-preset browser witness and real Structured Logs module browser composition; extend existing appearance/collection/template checks where appropriate.
- [x] T012 Verify keyboard/focus, 320px overflow containment, modes, semantic confirmation and source-owned/read-only state through actual rendering.

## US4: Integration, fidelity and delivery

- [x] T013 Update canonical `Studio/docs/design-tokens.md`, `module-ui.md`, `branding.md`, appearance guidance and affected extension/API catalogs where applicable.
- [x] T014 Run whole affected suites, standard lint/typecheck/build, consumer proof, Studio .NET, architecture and maps. Compare executed/discovered .NET coverage.
- [x] T015 Capture and review all six real screen types against retained references; refine until visual-contract details agree and record remaining differences honestly.
- [x] T016 Complete independent correctness/standards/acceptance author review and prepare draft PR #3517 with gate evidence. Hosted exact-head qualification, merge and resulting-main verification remain external delivery gates tracked on that PR; this implementation checklist does not claim them complete.

## Dependencies and execution

T001 precedes T002–T005; T003 precedes persisted metadata proof; T007 precedes owner module decoration. T011–T012 prove the integrated presentation. T013–T015 complete before final review and hosted qualification. Implementation remains one coherent PR; independent planning research and review lenses may run concurrently, while edits to shared files remain sequential. Use existing scoped build/test queues and do not bypass them.

Review: all four user stories have implementation and independent verification tasks; no unresolved clarification or constitutional exception remains. Optional commit hooks are handled by the normal local commits; no additional approval is required by the owner's delivery instruction.

## Integration and QA

Current main through `a455c1299` is integrated, including folders, tags, favorites and saved views from #3503. Real rendered organization flows cover all three presets at 320px and 1440px. Owner-local refinements align filters, constrain long filter controls, retain reachable row actions and keep sequence/artifact surfaces neutral. Branding and login seams remain unchanged. Validation commands, screenshot comparisons, test counts, independent review rounds and the final delivery receipt are recorded on [PR #3517](https://github.com/elsa-workflows/elsa-foundation/pull/3517).
