# Tasks: Compact Secret field prototype

## Setup and foundation

- [x] T001 Define and review the bounded acceptance record in specs/1210-secret-field-prototype/spec.md and issue #3851.
- [x] T002 Review theme, state and architecture decisions in specs/1210-secret-field-prototype/plan.md and research.md.

## US1 — Compact Create secret

Independent test: create/find/reopen and cancellation on an owned host with correct label/error/focus behavior.

- [ ] T003 [US1] Build module-local field shell in Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/SecretFormField.tsx and styles.css.
- [ ] T004 [US1] Integrate Create secret and opt-in Scope picker in Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/SecretDialogs.tsx and SecretScopePicker.tsx.
- [ ] T005 [US1] Verify associated help, validation, disabled submission, cancellation and regression in Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/__tests__/.

## US2 — Appearance and state review

Independent test: actual-component preview and complete registry-derived theme/appearance matrix with retained captures.

- [ ] T006 [US2] Add interactive preview in Studio/tests/browser/secretFieldPrototypeFixture.tsx and secret-field-prototype.html.
- [ ] T007 [US2] Add executable theme/state/accessibility matrix in Studio/tests/browser/secret-field-prototype.spec.ts; measure before/after geometry and inspect captures.
- [ ] T008 [US2] Add real create/find/reopen/cancel proof in Studio/tests/acceptance/journeys.spec.ts and required inventory in tools/ux-acceptance/run.py.

## Validation and findings

- [ ] T009 Run affected suites, typecheck/build/lint and focused mutation proof; retain results in specs/1210-secret-field-prototype/findings.md and PR evidence.
- [ ] T010 Commit and run composed proof, inspect exact-revision captures and independent correctness/standards/acceptance review; preserve incomplete states explicitly.
- [ ] T011 Record proposed shared field contract and rollout recommendations in specs/1210-secret-field-prototype/findings.md; open a reviewable draft prototype PR.

## Dependencies and implementation strategy

T001 → T002 → T003 → T004 → T005. T006 follows T004, T007 follows T006, T008 follows T004, T009 follows T005/T007/T008, T010 follows T009, T011 follows findings. Test authoring in T005 and preview setup in T006 can proceed independently once T004 is stable; execute sequentially in this session. MVP is US1; US2 is required before recommending shared extraction. This existing-form change preserves valid regressions and uses risk-based tests; no greenfield TDD cadence is inferred.
