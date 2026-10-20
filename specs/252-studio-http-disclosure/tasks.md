# Tasks: HTTP progressive disclosure

Input: [plan.md](plan.md), [spec.md](spec.md), research, data model and interaction contract. One PR fixes #3064 within #3057. All work remains in the existing program #2559.

## Phase 1: Setup

- [x] T001 Verify the winning App claim, clean selected base, branch and next spec number; record provenance in `specs/252-studio-http-disclosure/plan.md` and #3064.
- [x] T002 Create and root-review the specification, research/design artifacts and requirement checklist in `specs/252-studio-http-disclosure/` before implementation.

## Phase 2: Shared metadata

- [x] T003 Add optional tier/requiredness SDK declarations and all current extension copies; extend `Studio/src/apps/Elsa.Studio.Web/Client/src/__tests__/sdk-typing-copies.test.ts` to include Connections. Include the root-approved optional property-editor validation callback/capability refinement from plan step 6. Do not introduce another wire API.

## Phase 3: US1 — Straightforward request (P1)

Goal: direct URL/method/connection/body and one opening action for secondary HTTP controls. Independent test: new real-panel HTTP activity shows the direct choices and exposes the four secondary inputs through More options.

- [x] T004 [US1] Add failing hierarchy coverage in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/activityPropertyGroups.test.tsx` and update the scanner expectation in `Essentials/tests/Activities/Design/Tests/Unit/ClrAssemblyScannerTests.cs`; retain baseline failure evidence before production changes.
- [x] T005 [US1] Partition descriptor tiers with existing category/order rules and public AdvancedSection in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/ActivityPropertiesPanel.tsx`; keep rows mounted and primary choices directly visible.
- [x] T006 [US1] Promote only Connection tier to Essential in `Essentials/src/Activities/Http/Activities/SendHttpRequest.cs`, retain not-required and all execution behavior, and confirm the focused tests pass.

## Phase 4: US2 — Preserve authored settings (P1)

Goal: stored/default/bound/error state stays discoverable and method/disclosure changes preserve exact authored input data. Independent test: existing body, content type, binding, conversion and unknown extras survive the complete interaction matrix.

- [x] T007 [US2] Add regression cases for authored versus synthesized defaults, opaque keys, seeded wire state and exact lossless interactions in the existing Workflows `src/__tests__/activityPropertyGroups.test.tsx` harness; extend `activityProperties.test.ts` or `activityInputWire.test.ts` only where a changed helper warrants it.
- [x] T008 [US2] Implement read-only authored-state counting and current row error/options-failure visibility in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/ActivityPropertiesPanel.tsx` (a small reused helper may live in `activityProperties.ts`); cover memo equality, cleanup, retry and stale activity/revision boundaries. Under the approved plan step 6 refinement, bridge built-in TimeSpan/Numeric local errors through optional SDK reporting, aggregate collection/dictionary editor instances safely, and keep unknown/unobservable contributions directly visible. Prove invalid/recovered, sibling removal and draft/activity/editor switch cases before claiming error coverage. Do not invent validation or mutate inputs on collapse.
- [x] T009 [US2] Add a real-panel HTTP browser fixture/spec under `Studio/tests/browser/` using existing setup where practical; prove keyboard, full-page axe, configured/error recovery and GET/HEAD/POST value preservation with Light/Dark evidence. Report fixture proof separately from composed Studio proof.

## Phase 5: US3 — Safe fallback and module guidance (P2)

Goal: absent/unknown metadata and required inputs remain directly visible, with coherent categories/order. Independent test: legacy, unfamiliar, Setting-tier and required-Optional descriptors need no disclosure to reach their inputs.

- [x] T010 [US3] Cover fallback/requiredness and custom grouping/order in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/activityPropertyGroups.test.tsx`; preserve valid existing tests and correct any classification gap in the panel.
- [x] T011 [US3] Document the implemented metadata, authored/error visibility, requiredness and fallback contract in `Studio/docs/ux/design-spec.md`; preserve unrelated target rules and keep the HTTP example faithful to actual behavior.

## Phase 6: Root integration, qualification and delivery

- [x] T012 Run whole affected Workflows/Web/backend Design/Design API suites, typechecks, module build/budgets and lint; record exact commands/results in `specs/252-studio-http-disclosure/quickstart.md` or linked PR evidence. Review DRY code/test setup and the complete diff.
- [x] T013 Rebuild owned Studio/Workbench from this source with a fresh disposable DB; perform the actual inspector golden path, laptop/narrow keyboard and Light/Dark screenshot review, then run `e2e-tests/http/Test-SendHttpRequestStatusOutcomes.ps1` as scoped runtime-path proof. Record source/process provenance in `specs/252-studio-http-disclosure/evidence/` or PR evidence.
- [x] T014 Run the full architecture guard with discovered/executed counts reconciled and `tools/maps/Elsa.Maps.Generator` freshness check; refresh genuinely changed outputs and review generated findings. Record evidence beside the feature or on its PR.
- [ ] T015 Recheck ownership before committing; finish `specs/252-studio-http-disclosure/spec.md` status and remove terminal checklist residue in the implementation PR. Open the one App-authored PR, attach it, publish gate evidence, complete three independent non-author review axes and green exact-head CI, then use the current org merge protocol.
- [ ] T016 Verify resulting-main gates, issue closure and acceptance, synchronize #3064/#3057 and Project61, and record the final receipt in `specs/252-studio-http-disclosure/evidence/` or the issue/PR. Do not mark the epic complete; later leaves and #3075 remain.

## Dependencies and execution

T001→T002→T003→US1→US2→US3→T012–T016. US2/US3 share panel/test files and run sequentially with one writer. US3's fallback test data can be designed alongside US1, but edits remain serialized. Backend scanner expectation and SDK declaration-copy analysis are independent of panel authoring; no second writer is needed. Root can review artifacts and observe post-merge CI while the bounded implementation worker owns source edits.

US1 is the first demonstrable increment, not a reduced delivery scope: all three stories and final gates are required. Red→green evidence is required here because existing behavior changes under the repository merge gate; this is not an inferred greenfield TDD mandate. No new feature registration, runtime behavior or persistence model is introduced.
