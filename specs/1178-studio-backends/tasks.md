# Tasks: Studio backend connections

## Setup and shared seams
- [x] T001 Capture approved requirements and independently review spec/design in specs/1178-studio-backends/.
- [x] T002 Add shared capability/session/requirement contracts in Studio/src/essentials/Elsa.Studio.Hosting/Client/src/sdk/.
- [x] T003 Add safe profile runtime projection and governed host persistence in Studio/src/essentials/Elsa.Studio.Hosting/.

## US1 - Profiles
Independent proof: configured ownership and scoped personal CRUD/reload.
- [x] T004 [US1] Implement profile model/persistence and separate pages in Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/backends/.
- [x] T005 [US1] Cover namespace scope, validation, revision and metadata secrecy in Studio/tests/Elsa.Studio.Tests/.

## US2 - Selection
Independent proof: successful/failed/cancelled switches and blocked drafts/writes.
- [x] T006 [US2] Implement independent immutable backend transports/auth preparation in Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/backends/.
- [x] T007 [US2] Integrate browser-qualified session selection, recovery and both switcher placements in Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/App.tsx.
- [x] T008 [US2] Prove pending-write, credential, shell-path, query and resource isolation in Hosting frontend tests and Studio/tests/live/.

## US3 - Capability adaptation
Independent proof: authoring/runtime/dormant/incompatible/unavailable matrix.
- [x] T009 [US3] Compose module-owned requirements in Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/registry.ts.
- [x] T010 [US3] Migrate Workflows discovery wrapper and declare actual requirements in affected Studio/Extensions module registration files.
- [x] T011 [US3] Prove strict parsing, identity cache isolation, direct-route and optional-surface behavior in affected frontend suites.

## US4 - Feature administration
Independent proof: exact-target settings/review/Apply and capability refresh.
- [x] T012 [US4] Bind host contexts/reviews/completion and present runtime/operational availability in Extensions/FeatureManagement/studio/Elsa.Studio.FeatureManagement/Client/src/module.tsx.
- [x] T013 [US4] Guard dirty drafts/pending Apply and refresh capabilities after successful Server Apply in the existing Features module.
- [x] T014 [US4] Extend whole Features tests and real multi-backend feature-setting proof in Studio/tests/live/.

## Qualification and delivery
- [x] T015 Document configuration, persistence ownership, module requirements and package consumption in Hosting README/extension-point guidance.
- [x] T016 Run affected whole suites, typecheck/lint/build, locked restore, architecture/maps and regression bite proofs; record exact evidence on #3531/PR.
- [ ] T017 Run composed browser, responsive and package/durable-provider qualification on the exact candidate; independently review/fix the diff.
- [ ] T018 Open/review draft PR, qualify required hosted checks, merge and verify resulting main; update this spec's terminal status in the finishing PR.

Dependencies: T002/T003 precede US1/US2; US2 lifetime precedes US3/US4 target integration; all stories precede final qualification. Independent reviews may run concurrently; implementation changes sharing SDK/App/module files remain sequential. Risk-based test cadence follows the plan; no universal TDD cadence is asserted.

Implementation is complete. Final qualification and external delivery gates T017–T018 are recorded on issue #3531 and PR #3536; unchecked delivery gates are not a claim of completed review or merge.
