# Tasks: Reusable Studio Theme Store

Input: [spec](spec.md), [plan](plan.md), [research](research.md), [data model](data-model.md), [contract](contracts/theme-store.md).
Risk-based behavior tests and causal controls; no mandated test-first cadence. One implementation leaf, no implementation delegation.

## Phase1 — Setup and composition

- [x] T001 Record and review specification/design/contracts/research/checklist before implementation in specs/1011-studio-theme-store.
- [x] T002 Create Studio/src/essentials/Elsa.Studio.Hosting.Themes project, README, public API tracking and solution/reference/lock inputs without Web/Nuplane dependencies.
- [x] T003 Implement public StudioThemeStoreOptions/AddElsaStudioThemes, typed startup validation, copied ContentRoot-resolved applied choices and duplicate/after-Build guards.
- [x] T004 Move the three independently selected shell features to the package; use existing discovery/sharing, root mutation gate and Api authorization policy.

## Phase2 — US1 public optional host

Independent test: real public-API host, selected nondefault shell, feature combinations and authenticated requests.

- [x] T005 Register private settings/directory/API/models in the addon and migrate Web Program/assembly roster to public composition; remove moved app-only classes.
- [x] T006 Migrate ElsaThemeStoreApiTests to public APIs/HTTP and test-owned wire shapes, using fresh hosts for changed installed choices; qualify base absence, core/picker/management combinations, auth and lifecycle.

## Phase3 — US2 managed/source separation

Independent test: directory overlay, unrelated write, directory removal, original managed state retained.

- [x] T007 Separate internal managed state from directory-enriched read snapshots and projected responses in the package; retain existing JSON and historical managed records.
- [x] T008 Update each save/import/duplicate/delete/default/visibility mutation to operate on intended managed state under the whole host gate; refuse unpersistable source-owned changes explicitly.
- [x] T009 Add no-copy/directory-removal, overlapping-ID/legacy preservation, default/visibility and concurrent atomic mutation regressions; remove the former directory-copy expectation.

## Phase4 — US3 paths/assets/browser

Independent test: alternate ContentRoot/store/source/assets, round-trip import/export/appearance, real empty-default editor.

- [x] T010 Preserve safe theme/material validation, paths, 2MiB/type bounds, asset IDs and cancelled staging cleanup; add focused public-host tests and remove obsolete ConfigFileWriter if all consumers are moved.
- [x] T011 Fix ThemeBuilder deletion selection and qualify Appearance/empty DirectoryOnly behavior with frontend unit/render/runtime cases.
- [x] T012 Run affected complete frontend suites plus designer typecheck/lint/build and runtime journeys, preserving server/browser validator parity.

## Phase5 — qualification and delivery

- [x] T013 Run whole affected .NET projects/discovery accounting and risk-based restored mutation controls for source persistence, feature/auth gates, installed options and asset validation.
- [x] T014 Validate affected locked restore, architecture/maps/inventory, actual package closure and reference Web smoke; update canonical contract/README with final APIs/profiles.
- [ ] T015 Set spec Implemented after independent correctness/standards/acceptance draft review; open and attach one PR for #3360 with exact-head evidence and required CI/external review.
- [ ] T016 Verify actual reviewed merge/resulting main and candidate artifacts, then close #3360/parent feature as appropriate and reconcile Program #3340/Project63 evidence.

## Dependencies and execution

T001 → T002–004 → T005–006 → T007–009 → T010–012 → T013–016. Public feature composition is the common foundation; managed persistence and configured assets/browser outcomes must all complete before delivery. Read-only research and independent PR review can be parallel; implementation remains one worker. No new issues are generated for this existing leaf. #3359 remains stopped on its saved branch; #3364 owns final multi-package external consumers.
