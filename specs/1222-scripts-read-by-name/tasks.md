---
description: "Task list for Scripts Read Variables By Name (spec 1222, provisional)"
---

# Tasks: Scripts Read Variables By Name

**Input**: Design documents from `specs/1222-scripts-read-by-name/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Status**: **Provisional** (initiative #3052). These tasks are the implementation plan for when the initiative branch
lands; no production code is written until ADR 0089 (PR #3420, reduced per finding F-1) is agreed. External
prerequisites: #3167 (S-0, `JsonElement` alias inference) for the full #3287 fix; #3171 (side-track on `main`) for the
parser parity.

**Tests**: xUnit only (no FluentAssertions). Include framework §2.23 tests for logic-bearing implementations and for
feature wiring that carries logic. Cadence: the Elsa derived constitution does not declare TDD; tests are listed per
story and may be written alongside implementation (no failing-run gate assumed). Architecture guard tests
(`Essentials/tests/Architecture/Tests/`) must stay green.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no incomplete dependencies)
- **[Story]**: US1–US5 maps to the spec's user stories

---

## Phase 1: Setup

- [ ] T001 Confirm external prerequisites and record them in the PR: #3167 (S-0) status for the #3287 type fix, and #3171 (side-track) status for publish/runtime parser parity.
- [ ] T002 [P] Capture a baseline of current read-path tests in `Essentials/tests/` and `Extensions/Elsa3/tests/Mapping/Tests/` that this work changes (ambient-variable tests, declared-parameter tests, `Elsa3JavaScriptConversionTests`), so regressions are visible.

---

## Phase 2: Foundational (blocking prerequisites)

**⚠️ Blocks all user stories.**

- [ ] T003 Add a per-parameter `surface` flag (`variables` | `args`, default `args`) to the compiled spec 095 `Variable` parameter in `Essentials/src/Expressions/Core/Models/ExpressionDefinition.cs` and the binding model, per contracts/variables-read-surface.md (OQ-1 / R-2).
- [ ] T004 Update `specs/095-value-flow-redesign/contracts/expression-and-import-contract.md` §1 (the `surface` flag and `variables` root over pinned params) and §6 (importer keeps `variables.X`), recorded as a contract change flowing to #3420 / spec 095.
- [ ] T005 Build the publish-time read-set resolver (Design side): parse JS/Liquid source with the language parser, resolve each direct name nearest-scope-first by reusing `ScopedVariableResolver` plus the loop iteration scope (ADR 0028), in `Essentials/src/Workflows/Design/...`. No regex (R-5).

**Checkpoint**: the surface flag, contract and resolver exist; user stories can proceed.

---

## Phase 3: User Story 1 — Direct reads (Priority: P1) 🎯 MVP

**Goal**: scripts read workflow variables by direct name in JS and Liquid, resolved nearest-scope and pinned.

**Independent Test**: publish and run a workflow reading `variables.order` (and nested/loop/alias/Liquid variants); the
script sees the nearest-scope value; an unset variable reads `null`; only named variables are loaded.

### Tests for User Story 1

- [ ] T006 [P] [US1] Cover nearest-scope resolution + shadowing (workflow/container/iteration) of the publish resolver in `Essentials/tests/.../VariableReadResolutionTests.cs`.
- [ ] T007 [P] [US1] Cover run-time exposure of the `variables` root from pinned params, unset → `null`, and selective load (a large/external unnamed variable is not loaded) in `Essentials/tests/.../VariablesRootEvaluationTests.cs`.
- [ ] T008 [P] [US1] Cover Liquid `{{ variables.X }}` parity in `Essentials/tests/.../LiquidVariableReadTests.cs`.

### Implementation for User Story 1

- [ ] T009 [US1] Pin each resolved direct read as a `Variable` parameter with `surface = variables` (name not rewritten) in the publish/compile path (`RuntimeInputBindingCompiler` / the resolver output). Depends on T003, T005.
- [ ] T010 [US1] Build the run-time `variables` root from `surface = variables` pinned params, with unset → JSON `null`, in `Essentials/src/Expressions/JavaScript/Jint/Services/IsolatedJintEngine.cs` (replace `SetReadOnlyVariables`' ambient build) and `JintPortableJavaScriptEvaluator.cs`.
- [ ] T011 [US1] Remove the ambient read surface for scripts: regex read-detection, frame-chain projection and `AmbientVariables` handling in `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Values/RuntimePortableExpressionEvaluator.cs`; feed `variables` from pinned params only.
- [ ] T012 [US1] Materialize only the named variables into pinned params in `RuntimeActivityInputMaterializer.cs` (no load-all).
- [ ] T013 [US1] Remove the runtime `getVariable` / `get<Name>()` functions and getter completions (decision 6) from `IsolatedJintEngine.cs` and `JavaScriptExpressionToolingProvider`; keep `getVariable('literal')` / `variables['literal']` as publish-time aliases that pin like `variables.X`.
- [ ] T014 [US1] Apply the same `variables` root + nearest-scope reads to Liquid (`variables.X`), reusing the resolver.

**Checkpoint**: direct reads work end-to-end; MVP.

---

## Phase 4: User Story 2 — Computed names refused at publish (Priority: P2)

**Goal**: computed variable reads are refused at publish with a stable diagnostic; none reaches run time.

**Independent Test**: publishing `getVariable(name)` / `variables[key]` / passing the accessor as a value fails with the
diagnostic and guidance.

### Tests for User Story 2

- [ ] T015 [P] [US2] Cover publish refusal for each computed form (non-literal index, non-literal `getVariable` arg, accessor-as-value) and the diagnostic text/guidance in `Essentials/tests/.../ComputedNameRefusalTests.cs`.

### Implementation for User Story 2

- [ ] T016 [US2] In the publish read-set resolver (T005), detect computed access via the parser and refuse with a stable diagnostic that names the construct and points to the direct-name form, stating computed names are a deferred capability (R-4).

**Checkpoint**: computed reads are refused, not silently load-all.

---

## Phase 5: User Story 3 — Converter reads faithfully (Priority: P2)

**Goal**: Elsa 3 variable reads convert to `variables.X`, publish, and are faithful-or-refused; #3296 and #3287
(arguments) fixed.

**Independent Test**: convert `getVariable("order")` → `variables.order` that publishes; a script declaring its own
`args` is refused with guidance; a converted read/input `RunJavaScript` publishes (no type mismatch).

### Tests for User Story 3

- [ ] T017 [P] [US3] Cover emission of `variables.X` for `Variables.X` / `getVariable("X")` / `get<Name>()` in `Extensions/Elsa3/tests/Mapping/Tests/Elsa3JavaScriptConversionTests.cs`.
- [ ] T018 [P] [US3] Cover the #3296 `args`-collision refusal (declared `args` + lowered reads) in `Extensions/Elsa3/tests/Mapping/Tests/Elsa3JavaScriptConversionTests.cs`.
- [ ] T019 [P] [US3] Cover the #3287 fix (converted read/input `RunJavaScript` publishes; `arguments` carries the real type) in `Extensions/Elsa3/tests/Mapping/Tests/` and the publish/compile tests.
- [ ] T020 [P] [US3] Cover the faithfulness refusal (Elsa 4 resolution ≠ Elsa 3 resolution) and the transient-in-Elsa-4 import flag.

### Implementation for User Story 3

- [ ] T021 [US3] Emit `variables.X` for Elsa 3 variable reads (stop lowering them to `args`); keep `args.X` for inputs/activity-results/metadata, in `Extensions/Elsa3/src/Mapping/Services/Elsa3ExpressionRewriter.cs` and `Elsa3ValueFlowLowerer.cs`.
- [ ] T022 [US3] Refuse (faithful-or-refuse) with guidance when a script's own declared `args` collides with lowered `args` reads in scope (#3296), extending the guard in `Elsa3ExpressionRewriter.cs`.
- [ ] T023 [US3] Give the `arguments` expression its real result type from the input contract instead of hard-coded `Object` in `Elsa3ValueFlowLowerer.LowerScript` (#3287; pairs with #3167 from S-0).
- [ ] T024 [US3] Confirm statement bodies emit without the IIFE wrap on the `RunJavaScript` body path (#3010) and add a regression test.
- [ ] T025 [US3] Add the faithfulness check (refuse when Elsa 4 nearest-scope resolution differs from Elsa 3) and the transient-in-Elsa-4 import-time flag.

**Checkpoint**: converter faithful; #3204/#3296/#3287 (arguments) covered.

---

## Phase 6: User Story 4 — Rename safety (Priority: P3)

**Goal**: rename never silently breaks a read; publish refuses unresolvable names; shadow change warns; published
versions immutable.

**Independent Test**: rename updates a resolvable reference in a draft; removing a declaration a script names refuses at
publish; a published version is unchanged by a draft rename.

### Tests for User Story 4

- [ ] T026 [P] [US4] Cover publish refusal for an unresolvable name and the shadow-change warning (resolves to a different declaration than the previously published version) in `Essentials/tests/.../RenameSafetyPublishTests.cs`.
- [ ] T027 [P] [US4] Cover Studio rename updating a resolvable reference and flagging an unresolvable one for manual repair in the Design/Studio tests.

### Implementation for User Story 4

- [ ] T028 [US4] In the publish resolver, refuse a name that no longer resolves, and warn when a name resolves to a different declaration than the previously published version (R-8).
- [ ] T029 [US4] Studio rename: update references the resolver can resolve; surface the rest for manual repair; keep readable names in source; never alter a published version.

**Checkpoint**: rename-safe.

---

## Phase 7: User Story 5 — Secrets never leak (Priority: P3)

**Goal**: direct reads of protected variables are checked at publish; secret values are redacted from logs; withheld
(no-permission) values are not provided.

**Independent Test**: reading a secret and logging shows redaction; a no-permission value is not provided.

### Tests for User Story 5

- [ ] T030 [P] [US5] Cover publish-time protection check for a direct read of a protected variable, redaction in logs/diagnostics, and non-provision of a withheld value in `Essentials/tests/.../SecretVariableReadTests.cs`.

### Implementation for User Story 5

- [ ] T031 [US5] Check protection at publish for a direct read of a protected variable (name is known); ensure secret values are redacted wherever reads could be logged; keep the existing refusal for a withheld (no-permission) value (R-7).

**Checkpoint**: secrets handled.

---

## Phase 8: Polish & Cross-Cutting

- [ ] T032 [P] Update docs/glossary or CONTEXT for the `variables` read surface if warranted (domain terms only).
- [ ] T033 [P] Confirm architecture guard tests pass: `Runtime_projects_do_not_add_design_references`, `Elsa3MigrationBoundaryTests`, `FeatureCompositionTests`; place new tests in the correct CI tier (§E7).
- [ ] T034 Run `specs/1222-scripts-read-by-name/quickstart.md` validation end-to-end.
- [ ] T035 Post the findings (F-1, F-2, F-3) and the spec 095 contract change on PR #3420; confirm ADR 0089 is reduced to the implemented model before this spec's formal approval.
- [ ] T036 Set this spec's `**Status**:` line to `Implemented` in the delivery PR, before it merges (spec-lifecycle rule; never a separate status PR).

---

## Dependencies & Execution Order

- **Setup (P1)** → no deps.
- **Foundational (P2: T003–T005)** → blocks all stories.
- **US1 (P1)** after Foundational — MVP.
- **US2 (P2)** after T005 (reuses the resolver's computed detection).
- **US3 (P2)** after US1's pinning/`variables` root (T009–T010); needs #3167 (S-0) for the full #3287 fix.
- **US4 (P3)** after T005/T009 (resolver + pinning).
- **US5 (P3)** after T009 (pinned reads).
- **Polish** after the desired stories.

### Parallel opportunities

- T006–T008 (US1 tests) parallel; T017–T020 (US3 tests) parallel; test tasks across stories parallel once their phase deps are met.
- US4 and US5 are independent of each other once US1 lands.

## Implementation Strategy

- **MVP** = Phase 1–3 (direct reads). Validate independently, then add US2 (refuse computed), US3 (converter), US4 (rename), US5 (secrets).
- Each story is an independent, testable increment.
