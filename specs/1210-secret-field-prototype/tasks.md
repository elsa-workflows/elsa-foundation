# Tasks: Compact Secret field prototype

## Setup and foundation

- [x] T001 Define and review the bounded acceptance record in specs/1210-secret-field-prototype/spec.md and issue #3851.
- [x] T002 Review theme, state and architecture decisions in specs/1210-secret-field-prototype/plan.md and research.md.

## US1 — Compact Create secret

Independent test: create/find/reopen and cancellation on an owned host with correct label/error/focus behavior.

- [x] T003 [US1] Build module-local field shell in Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/SecretFormField.tsx and styles.css.
- [x] T004 [US1] Integrate Create secret and opt-in Scope picker in Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/SecretDialogs.tsx and SecretScopePicker.tsx.
- [x] T005 [US1] Verify associated help, validation, disabled submission, cancellation and regression in Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/__tests__/.

## US2 — Appearance and state review

Independent test: actual-component preview and complete registry-derived theme/appearance matrix with retained captures.

- [x] T006 [US2] Add interactive preview in Studio/tests/browser/secretFieldPrototypeFixture.tsx and secret-field-prototype.html.
- [x] T007 [US2] Add executable theme/state/accessibility matrix in Studio/tests/browser/secret-field-prototype.spec.ts; measure before/after geometry and inspect captures.
- [x] T008 [US2] Add real create/find/reopen/cancel proof in Studio/tests/acceptance/journeys.spec.ts and required inventory in tools/ux-acceptance/run.py.

## Validation and findings

- [x] T009 Run affected suites, typecheck/build/lint and focused mutation proof; retain results in specs/1210-secret-field-prototype/findings.md and PR evidence.
- [x] T010 Commit and run composed proof, inspect exact-revision captures and independent correctness/standards/acceptance review; preserve incomplete states explicitly.
- [x] T011 Record proposed shared field contract and rollout recommendations in specs/1210-secret-field-prototype/findings.md; open a reviewable draft prototype PR.

## Dependencies and implementation strategy

T001 → T002 → T003 → T004 → T005. T006 follows T004, T007 follows T006, T008 follows T004, T009 follows T005/T007/T008, T010 follows T009, T011 follows findings. Test authoring in T005 and preview setup in T006 can proceed independently once T004 is stable; execute sequentially in this session. MVP is US1; US2 is required before recommending shared extraction. This existing-form change preserves valid regressions and uses risk-based tests; no greenfield TDD cadence is inferred.

## Prototype closeout

Local source proof at `6732623e2d9294a19f8ad5493f142ea7dacf6d7b`: Secrets 73/73 (inventory 73), browser 76/76 (inventory 76; 48 theme/appearance combinations), composed 12/12 with clean owned hosts/stores, architecture 616/616 and generated-map check passed. Independent correctness/standards and isolated visual reviews passed; composed visual review is recorded on PR #3857. This Filled-baseline receipt records the local proof; shared CI/downloadable evidence is required before merge. Shared-component rollout remains separate from this bounded prototype.

## Comparison extension

- [x] T012 [US2] Add preview-only persistent Filled/Outlined controls, including live-dialog switching, in Studio/tests/browser/secretFieldPrototypeFixture.tsx and secretFieldPrototypeFixture.css.
- [x] T013 [US2] Run both presentations through the theme/state matrix and draft-preservation checks in Studio/tests/browser/secret-field-prototype.spec.ts; inspect current captures independently.
- [x] T014 Record comparison findings and review evidence in specs/1210-secret-field-prototype/findings.md and draft PR #3857. This preview-only extension leaves composed product code unchanged.

Comparison local proof at UI source `1becff2c915df35468996a3930b61000e378c13c`: 134/134 browser checks passed with no skips/retries, including 96 style/theme/appearance combinations and live-switch, persistence, native keyboard/focus and border-label geometry. Independent review receipts and exact-head shared CI artifacts are retained on PR #3857. Final comparison images use the unchanged UI source across the CI/documentation closeout.
