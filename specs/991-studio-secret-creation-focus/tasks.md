# Tasks: Keep the required source visible in Studio secret creation

**Input**: Design documents from `specs/991-studio-secret-creation-focus/`

**Prerequisites**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md), and [quickstart.md](quickstart.md). No `contracts/` directory is needed because this change adds no external or public contract.

**Tests**: Add regression coverage for the changed UI and behavior-preservation boundaries. This feature introduces no Foundation feature class or logic-bearing service, so no registration test applies. The application-wide test cadence is not inferred here; root requires this task's story regression tests and baseline runs before any production UI edit. Keep those as an explicit pre-implementation gate. No backend end-to-end suite is added because this change introduces no backend/API semantic change; the actual-styles Studio browser fixture proves the UI boundary and metadata-only response behavior.

**Execution gate**: Root must review this task list before work starts. Complete every baseline task and obtain root approval before editing production UI. Keep all checklist items unchecked until their work is actually complete; this plan claims no test or runtime pass.

## Format: `[ID] [P?] [Story] Description`

- `[P]` marks tasks that touch different files and can be authored in parallel without depending on unfinished work. Shared-machine test runs remain serial.
- `[US#]` identifies user-story implementation and verification tasks. Setup and foundational tasks intentionally have no story label.
- Every task names its concrete file path.

## Phase 1: Setup

**Purpose**: Confirm reviewed inputs and the source baseline before adding regression fixtures.

- [x] T001 Root reviews this task list and confirms `agent/codex/3066-secret-creation` at the approved `d628fc0da1e44ce5ea61768fbf6377b43919bacb` baseline against `specs/991-studio-secret-creation-focus/spec.md` and `plan.md`; do not edit production files before the later baseline gate.

---

## Phase 2: Foundational — baseline evidence and dependency gate

**Purpose**: Capture all story regression baselines before any production file change. Complete the story-grouped baseline subsections, run shared-machine test processes serially, and obtain root approval. Only then declare the direct workspace dependency and prove package resolution before UI implementation.

### Baselines for User Story 1 (P1): required source first

- [x] T002 [P] Add a synthetic preselected Configuration Store scenario to `Studio/tests/browser/secretScopesFixture.tsx` and an actual-host-styles test to `Studio/tests/browser/secret-scopes.spec.ts` that measures the complete Value or Configuration key control inside the visible dialog body at 1280×800 before scrolling.
- [x] T003 [P] Add focused source-selection and current requiredness/submission regression cases to `Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/__tests__/secretDialogs.test.tsx`; preserve current Type defaults, Value versus Configuration key selection, and Configuration key trim behavior.

### Baselines for User Story 2 (P2): optional choices and context

- [x] T004 Add focused regression cases to `Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/__tests__/secretDialogs.test.tsx` and `Studio/tests/browser/secret-scopes.spec.ts` for initial More options collapse, grouping, generic count exclusions, directly visible Store context and canonical Scope hint, and empty-Store recovery with Type still direct.

### Baselines for User Story 3 (P3): preservation and recovery

- [x] T005 Add regression coverage in `Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/__tests__/secretDialogs.test.tsx`, `Studio/tests/browser/secretScopesFixture.tsx`, and `Studio/tests/browser/secret-scopes.spec.ts` for exact create payload/defaults/omissions/expiry, Type and Store switching, authored-value retention, Value remasking, advisory Scope failure with focused custom Scope, no stored-value fetch, and no raw source in fixture metadata.
- [x] T006 Update the tall-error case in `Studio/tests/browser/secret-scopes.spec.ts` to open More options and scroll to a real below-fold optional field or long help instead of Expires; preserve its dialog-body scrolling, fixed-action visibility, save-error, focus, and entered-value assertions.

### Run baselines and pass the pre-implementation gate

- [x] T007 Root runs the browser visibility test from `Studio/` using `pnpm exec playwright test tests/browser/secret-scopes.spec.ts --grep "required source is visible at 1280x800" --workers=1`; record actual body/control geometry for both source paths and the expected pre-change result alongside the validation criteria in `specs/991-studio-secret-creation-focus/quickstart.md`.
- [x] T008 Run the full Secrets Client Vitest suite and the `secret-scopes.spec.ts` browser suite serially from the documented working directories; distinguish expected new-layout/disclosure failures from existing behavior baselines and record results for the commands in `specs/991-studio-secret-creation-focus/quickstart.md` without claiming expected RED results as passing.
- [x] T009 Root reviews the baseline evidence from `Studio/tests/browser/secret-scopes.spec.ts` and `Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/__tests__/secretDialogs.test.tsx` against `spec.md` and `quickstart.md`; production UI editing remains blocked until root approves the captured baselines.

### Direct dependency gate before UI implementation

- [x] T010 After T009 approval, remove the obsolete ambient SDK copy `Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/studio-sdk.d.ts` and accurately type the four generic API mocks in `src/__tests__/secretsApi.test.ts` and the SDK-backed module fixture in `src/__tests__/module.test.tsx` against the real workspace SDK (no SDK edits or diagnostic suppression); add `@elsa-workflows/studio-ui: workspace:*` as a direct dependency and a `typecheck` script in `Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/package.json` and update only that importer in `Studio/pnpm-lock.yaml`; from `Studio/`, run `pnpm --filter @elsa-workflows/studio-ui build`, `pnpm --filter @elsa-workflows/studio-secrets exec tsc --noEmit -p tsconfig.json`, and `pnpm --filter @elsa-workflows/studio-secrets build`, stopping before UI edits if direct resolution or the importer check fails.

**Checkpoint**: All User Story 1–3 regression additions and baseline runs are recorded; root has approved the evidence; the direct dependency, Studio UI build, direct Secrets TypeScript check, and Secrets build resolve before any production UI edit.

---

## Phase 3: User Story 1 — Enter the required secret source first (Priority: P1)

**Goal**: Technical name, Type, and the required Value or Configuration key lead the form, with the complete source control visible at 1280×800 before scrolling.

**Independent Test**: With actual host styles, verify the whole Value control for the ordinary value-backed path and the whole Configuration key for the preselected Configuration path fit inside the visible dialog body at 1280×800; verify the required source follows Technical name and Type, and Expires remains directly available.

### Implementation and verification

- [ ] T011 [US1] Reorder the existing controls in `Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/SecretDialogs.tsx` so Technical name and Type precede the selected required source and Expires; place verbose descriptor help after the source and retain existing state and submit-handler wiring.
- [ ] T012 [US1] Run the focused source-selection cases in `Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/__tests__/secretDialogs.test.tsx` and the root-owned 1280×800 actual-styles test in `Studio/tests/browser/secret-scopes.spec.ts`; verify both source paths without scrolling before measurement.

**Checkpoint**: User Story 1 meets SC-001 and keeps current source-selection and submission semantics.

---

## Phase 4: User Story 2 — Find optional choices without losing context (Priority: P2)

**Goal**: Optional metadata, Store, and Scope are grouped under More options while the selected Store, Scope meaning, Expires, and empty-Store recovery remain directly available.

**Independent Test**: Verify More options starts collapsed for unconfigured optional values with the existing default Store; inspect grouping, generic count, selected-Store context and canonical Scope hint; verify configured values survive manual collapse/reopen and an empty Store can be recovered with Type still direct.

### Implementation and verification

- [ ] T013 [US2] Use the existing `AdvancedSection` in `Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/SecretDialogs.tsx` to group Display name, Description, Tags, Store, and Scope; keep empty Store directly reachable, keep selected-Store context and one accessible canonical Scope hint outside the fold, and use a bounded caller-hint association in `Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/SecretScopePicker.tsx` only if needed; do not edit `Studio/src/apps/Elsa.Studio.Web/Client/src/app/ui/disclosure/AdvancedSection.tsx`.
- [ ] T014 [US2] Implement a generic optional count in `Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/SecretDialogs.tsx` that counts authored optional metadata and a Store changed from its default while excluding the default Store, Technical name, Value, Configuration key, and Expires; then run US2 unit and browser cases in `Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/__tests__/secretDialogs.test.tsx` and `Studio/tests/browser/secret-scopes.spec.ts` for count, context, collapse/reopen, and empty-Store recovery.

**Checkpoint**: User Story 2 meets SC-002 and preserves all authored optional values without exposing their contents in disclosure copy.

---

## Phase 5: User Story 3 — Preserve authored values and recover safely (Priority: P3)

**Goal**: The disclosure change preserves exact values, current create behavior, masking, advisory Scope recovery, errors, cancellation, focus, and the no-stored-value-read boundary.

**Independent Test**: Exercise collapse/reopen and Store/Type switching; trigger Scope catalog failure with custom Scope focused; recover and dismiss the section; verify eligibility, exact request behavior, Value remasking, save error/cancel focus, and metadata-only fixture responses.

### Implementation and verification

- [ ] T015 [US3] Wire the existing Scope suggestion error state in `Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/SecretDialogs.tsx` to force More options open only while the error is active, then rely on the shared disclosure latch to keep it open after recovery until user dismissal; preserve the focused custom Scope, eligibility, request construction, Show/Hide remasking, and do not add a stored-value read or existing-Secret selector.
- [ ] T016 [US3] Run the focused preservation and recovery cases in `Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/__tests__/secretDialogs.test.tsx` and `Studio/tests/browser/secret-scopes.spec.ts`, including metadata-only fixture assertions in `Studio/tests/browser/secretScopesFixture.tsx` and the revised below-fold tall-error case.

**Checkpoint**: User Story 3 meets SC-003 and SC-004 with unchanged creation outcome and no stored-value exposure.

---

## Phase 6: Polish and cross-cutting verification

**Purpose**: Run affected local checks, finish the spec's terminal transition in the implementation PR, then complete root QA and exact-head hosted checks.

- [x] T017 Run `pnpm --filter @elsa-workflows/studio-secrets test`, `pnpm --filter @elsa-workflows/studio-secrets build`, `pnpm --filter @elsa-workflows/studio-secrets exec tsc --noEmit -p tsconfig.json`, `pnpm --filter @elsa-workflows/studio-ui build`, `pnpm exec playwright test tests/browser/secret-scopes.spec.ts --workers=1`, `pnpm typecheck`, and `pnpm lint` from `Studio/`; from repository root run `bash tools/architecture/restore-ci-project-graph.sh --locked-mode`, `dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj -c Release --no-restore`, and `dotnet run --project tools/maps/Elsa.Maps.Generator -- check`, following `specs/991-studio-secret-creation-focus/quickstart.md` and reporting each result separately.
- [x] T018 In the implementation PR before merge, set `**Status**: Implemented` in `specs/991-studio-secret-creation-focus/spec.md`, delete `specs/991-studio-secret-creation-focus/checklists/requirements.md`, and remove the checklist entry from `specs/991-studio-secret-creation-focus/plan.md`.
- [ ] T019 After lifecycle edits, root records final actual-host-style visual and keyboard QA from `Studio/tests/browser/secret-scopes.spec.ts`, obtains three independent non-author reviews, and verifies configured hosted required checks against the exact final implementation diff before merge.
- [ ] T020 After merge, verify the resulting `main` CI and Maps checks and affected Studio behavior against `.github/workflows/ci.yml` and `docs/contributing/generated-maps.md`; allow CI to publish documentation maps on `generated-maps` and do not stage or force-add ignored map files.

---

## Dependencies and Execution Order

### Phase dependencies

- Setup T001 precedes all work.
- Foundational baseline tasks T002–T008 are mandatory before any production-file edit. T007 is root-owned and must measure the initial viewport before a scroll. Baseline test processes run serially on the shared machine.
- T009 is the root approval gate. T010 is the direct-dependency/build/typecheck gate and must complete before production UI implementation.
- User Story 1 precedes User Story 2 because both change the same Create dialog; User Story 2 precedes User Story 3 because the recovery/error behavior is wired through its disclosure.
- Polish tasks follow all three stories. Complete terminal status/checklist edits before exact-head hosted checks; verify resulting-main gates after merge.

### User story dependencies

- **User Story 1 (P1)**: Starts after foundational baselines, root approval, and the direct dependency gate. It is the suggested MVP increment.
- **User Story 2 (P2)**: Depends on the P1 field ordering; it adds the shared disclosure and visible Store/Scope context.
- **User Story 3 (P3)**: Depends on P1 and P2 because recovery, focus, and retained values must work through the final disclosure layout.

### Parallel opportunities

- T002 (browser fixture/spec authoring) and T003 (Secrets unit-test authoring) touch different test files and may be authored in parallel. Run all browser and unit processes serially on the shared machine.
- The three production story phases share `SecretDialogs.tsx`; keep them sequential. No user-story implementation tasks are marked parallel.
- Root's actual-styles browser test may run while a separate author edits only the unit-test file, but production UI changes remain blocked until all baseline evidence is captured and root approves it.

## Implementation Strategy

### MVP first

Complete setup, story-grouped baselines, root approval, and the direct dependency gate. Implement and independently verify User Story 1 only: the required source becomes visible before scrolling while existing submit behavior stays unchanged. Stop for root review before proceeding to the disclosure work.

### Incremental delivery

1. Finish baseline and dependency gates.
2. Implement User Story 1 and validate its source-visibility criteria.
3. Implement User Story 2 and validate collapse, count, context, and recovery paths.
4. Implement User Story 3 and validate preservation, failure recovery, focus, and confidentiality boundaries.
5. Run final local/hosted checks, root QA and independent reviews, apply terminal spec cleanup in the implementation PR, then verify main after merge.

### Parallel execution example

```text
Task T002: Add the actual-styles browser assertion and synthetic Configuration scenario in Studio/tests/browser/secret-scopes.spec.ts and Studio/tests/browser/secretScopesFixture.tsx
Task T003: Add the focused source-selection baseline in Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/__tests__/secretDialogs.test.tsx
```

These authoring tasks may run in parallel because their files are separate. Their test runs are serialized; no production UI work can start until T009 approval and T010 pass.
