---
description: "Task list for Scripts Read Variables By Name (spec 1222, provisional)"
---

# Tasks: Scripts Read Variables By Name

**Input**: Design documents from `specs/1222-scripts-read-by-name/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Status**: **Provisional** (initiative #3052). These tasks are the implementation plan for when the initiative branch
lands; no production code is written until ADR 0089 with the minimal D-7 amendment (PR #3420, being rewritten to that
scope) is agreed. External prerequisites: #3167 (S-0, `JsonElement` alias inference) for the full #3287 fix; #3171
(side track on `main`) for the parser parity; spec 1216 / S-2 (PR #3726) FR-025–FR-027 for the missing-name behaviour.

**Tests**: xUnit only (no FluentAssertions). Include framework §2.23 tests for logic-bearing implementations and for
feature wiring that carries logic. Cadence: the Elsa derived constitution does not declare TDD; tests are listed per
story and may be written alongside implementation (no failing-run gate assumed). Architecture guard tests
(`Essentials/tests/Architecture/Tests/`) must stay green.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no incomplete dependencies)
- **[Story]**: US1–US5 maps to the spec's user stories

---

## Phase 1: Setup

- [ ] T001 Confirm external prerequisites and record them in the PR: #3167 (S-0) status for the #3287 type fix, #3171 (side track) status for publish/runtime parser parity, and spec 1216 FR-025–FR-027 wording.
- [ ] T002 [P] Capture a baseline of current read-path tests in `Essentials/tests/` and `Extensions/Elsa3/tests/Mapping/Tests/` that this work changes (ambient-variable tests, declared-parameter tests, `Elsa3JavaScriptConversionTests`), so regressions are visible.

---

## Phase 2: Foundational (blocking prerequisites)

**⚠️ Blocks all user stories.**

- [ ] T003 Add a per-parameter `surface` flag (`variables` | `args`, default `args`) to the compiled spec 095 `Variable` parameter, and an optional expression-level fallback marker carrying the conservative read report, in `Essentials/src/Expressions/Core/Models/ExpressionDefinition.cs` and the binding model, per contracts/variables-read-surface.md (OQ-1 / R-2).
- [ ] T004 Update `specs/095-value-flow-redesign/contracts/expression-and-import-contract.md` §1 (surface flag, fallback marker, `variables` root, recorded reads in the evaluation result) and §6 (importer keeps `variables.X` and computed `getVariable(expr)`), recorded as a contract change flowing to #3420 / spec 095.
- [ ] T005 Build the publish-time read-set resolver (Design side): parse JS/Liquid source with the language parser, resolve each literal name nearest-scope-first by reusing `ScopedVariableResolver` plus the loop iteration scope (ADR 0028), and detect computed/opaque access to set the fallback marker, in `Essentials/src/Workflows/Design/...`. No regex (R-6).

**Checkpoint**: the surface flag, marker, contract and resolver exist; user stories can proceed.

---

## Phase 3: User Story 1 — Literal reads (Priority: P1) 🎯 MVP

**Goal**: scripts read workflow variables by literal name in JS and Liquid, resolved nearest-scope and pinned.

**Independent Test**: publish and run a workflow reading `variables.order` (and nested/loop/alias/Liquid variants); the
script sees the nearest-scope value; an unset variable reads `null`; only named variables are loaded.

### Tests for User Story 1

- [ ] T006 [P] [US1] Cover nearest-scope resolution + shadowing (workflow/container/iteration) of the publish resolver in `Essentials/tests/.../VariableReadResolutionTests.cs`.
- [ ] T007 [P] [US1] Cover run-time exposure of the `variables` root from pinned params, unset → `null`, and selective load (a large/external unnamed variable is not loaded on a literal-only expression) in `Essentials/tests/.../VariablesRootEvaluationTests.cs`.
- [ ] T008 [P] [US1] Cover Liquid `{{ variables.X }}` parity in `Essentials/tests/.../LiquidVariableReadTests.cs`.

### Implementation for User Story 1

- [ ] T009 [US1] Pin each resolved literal read as a `Variable` parameter with `surface = variables` (name not rewritten) in the publish/compile path (`RuntimeInputBindingCompiler` / the resolver output). Depends on T003, T005.
- [ ] T010 [US1] Build the run-time `variables` root from `surface = variables` pinned params, with unset → JSON `null`, in `Essentials/src/Expressions/JavaScript/Jint/Services/IsolatedJintEngine.cs` (replace `SetReadOnlyVariables`' ambient build) and `JintPortableJavaScriptEvaluator.cs`.
- [ ] T011 [US1] Remove the run-time regex read-detection (`IsAmbientVariableReferenced`, `HasComputedAmbientAccess`) from `RuntimePortableExpressionEvaluator.cs`; literal-only expressions get no frame-chain projection.
- [ ] T012 [US1] Materialize only the named variables into pinned params in `RuntimeActivityInputMaterializer.cs` (no load-all for literal-only expressions).
- [ ] T013 [US1] Remove the generated `get<Name>()` functions and getter completions (decision 6) from `IsolatedJintEngine.cs` and `JavaScriptExpressionToolingProvider`; keep `getVariable` as a runtime function (literal argument → pinned value).
- [ ] T014 [US1] Apply the same `variables` root + nearest-scope reads to Liquid (`variables.X`), reusing the resolver.

**Checkpoint**: literal reads work end-to-end; MVP.

---

## Phase 4: User Story 2 — Computed and opaque reads via the run-time fallback (Priority: P1)

**Goal**: computed names and host-function reads publish, are reported conservatively, and are looked up, recorded,
protection-checked and snapshotted at run time; a missing name reads `undefined` (nil) with a warning.

**Independent Test**: publishing `getVariable(name)` / `variables[key]` succeeds with a conservative report; running it
reads the right value, records the read in the input snapshot, and reads `undefined` + warning for a missing name.

### Tests for User Story 2

- [ ] T015 [P] [US2] Cover publish of each computed form (non-literal index, non-literal `getVariable` arg) and a host-function read: publish succeeds, the fallback marker is set, and the conservative report lists the visible variables, in `Essentials/tests/.../ComputedReadPublishTests.cs`.
- [ ] T016 [P] [US2] Cover run-time fallback: nearest-scope lookup (including iteration scope), recorded reads in the input snapshot, unread candidates not recorded, mixed literal + computed reads of the same variable, in `Essentials/tests/.../ComputedReadFallbackTests.cs`.
- [ ] T017 [P] [US2] Cover a missing computed name: JavaScript reads `undefined` (not `null`), Liquid reads nil, a warning is recorded, the read is marked absent (spec 1216 FR-025/FR-026); a missing literal name stays a publish diagnostic (FR-027).
- [ ] T018 [P] [US2] Cover a computed read resolving to a transient variable faulting the step (OQ-3), and the transient-candidate publish warning.

### Implementation for User Story 2

- [ ] T019 [US2] In the publish resolver (T005), set the fallback marker with the conservative read report for expressions with computed or opaque reads; emit the "computed read reported", "possible protected read" and "transient candidate" diagnostics (R-4, R-5).
- [ ] T020 [US2] In `RuntimePortableExpressionEvaluator.cs`, make the candidate set readable before evaluation only for marked expressions, reusing the existing visible-variable projection (R-4 step 2).
- [ ] T021 [US2] In `IsolatedJintEngine.cs`, back the `variables` root and `getVariable` with the fallback lookup for unpinned names: resolve nearest-scope-first, check protection for the variable actually read, record each read, return `undefined` + warning + absent for a missing name, and fault on a transient match.
- [ ] T022 [US2] Return recorded reads from `JintPortableJavaScriptEvaluator.cs` and the Liquid evaluator, and add them to the step's input snapshot in `RuntimeActivityInputMaterializer.cs`.
- [ ] T023 [US2] Route host-function variable reads through the same fallback lookup so they are recorded, checked and snapshotted the same way.

**Checkpoint**: computed and opaque reads work through the fallback; literal-only expressions pay no fallback cost.

---

## Phase 5: User Story 3 — Converter reads faithfully (Priority: P2)

**Goal**: Elsa 3 literal variable reads convert to `variables.X`, computed reads convert to their Elsa 4 computed form,
both publish, and are faithful-or-refused; #3296 and #3287 (arguments) fixed.

**Independent Test**: convert `getVariable("order")` → `variables.order` and `getVariable(name)` → `getVariable(name)`,
both publish; a script declaring its own `args` is refused with guidance; a converted read/input `RunJavaScript`
publishes (no type mismatch).

### Tests for User Story 3

- [ ] T024 [P] [US3] Cover emission of `variables.X` for `Variables.X` / `getVariable("X")` / `get<Name>()`, and of `getVariable(expr)` / `variables[expr]` for computed reads (not refused), in `Extensions/Elsa3/tests/Mapping/Tests/Elsa3JavaScriptConversionTests.cs`.
- [ ] T025 [P] [US3] Cover the #3296 `args`-collision refusal (declared `args` + lowered reads) in `Extensions/Elsa3/tests/Mapping/Tests/Elsa3JavaScriptConversionTests.cs`.
- [ ] T026 [P] [US3] Cover the #3287 fix (converted read/input `RunJavaScript` publishes; `arguments` carries the real type) in `Extensions/Elsa3/tests/Mapping/Tests/` and the publish/compile tests.
- [ ] T027 [P] [US3] Cover the literal faithfulness refusal (Elsa 4 resolution ≠ Elsa 3 resolution), the shadowed-candidate import warning for computed reads, and the transient-in-Elsa-4 import flag.

### Implementation for User Story 3

- [ ] T028 [US3] Emit `variables.X` for Elsa 3 literal variable reads (stop lowering them to `args`) and keep computed reads as `getVariable(expr)` / `variables[expr]` (remove the computed-read refusal); keep `args.X` for inputs/activity-results/metadata, in `Extensions/Elsa3/src/Mapping/Services/Elsa3ExpressionRewriter.cs` and `Elsa3ValueFlowLowerer.cs`.
- [ ] T029 [US3] Refuse (faithful-or-refuse) with guidance when a script's own declared `args` collides with lowered `args` reads in scope (#3296), extending the guard in `Elsa3ExpressionRewriter.cs`.
- [ ] T030 [US3] Give the `arguments` expression its real result type from the input contract instead of hard-coded `Object` in `Elsa3ValueFlowLowerer.LowerScript` (#3287; pairs with #3167 from S-0).
- [ ] T031 [US3] Confirm statement bodies emit without the IIFE wrap on the `RunJavaScript` body path (#3010) and add a regression test.
- [ ] T032 [US3] Add the literal faithfulness check (refuse when Elsa 4 nearest-scope resolution differs from Elsa 3), the shadowed-candidate import warning for computed reads, and the transient-in-Elsa-4 import-time flag.

**Checkpoint**: converter faithful; #3204/#3296/#3287 (arguments) covered; computed Elsa 3 reads convert.

---

## Phase 6: User Story 4 — Rename safety (Priority: P3)

**Goal**: rename never silently breaks a read; publish refuses unresolvable literal names; shadow change warns;
computed reads that could reach the renamed variable are flagged; published versions immutable.

**Independent Test**: rename updates a resolvable reference in a draft and flags a computed one; removing a declaration a
script names literally refuses at publish; a published version is unchanged by a draft rename.

### Tests for User Story 4

- [ ] T033 [P] [US4] Cover publish refusal for an unresolvable literal name and the shadow-change warning (resolves to a different declaration than the previously published version) in `Essentials/tests/.../RenameSafetyPublishTests.cs`.
- [ ] T034 [P] [US4] Cover Studio rename updating a resolvable reference and flagging both an unresolvable one and a computed read whose conservative report contains the variable, in the Design/Studio tests.

### Implementation for User Story 4

- [ ] T035 [US4] In the publish resolver, refuse a literal name that no longer resolves, and warn when a literal name resolves to a different declaration than the previously published version (R-9).
- [ ] T036 [US4] Studio rename: update references the resolver can resolve; surface the rest (including computed reads with the variable among their candidates) for manual repair; keep readable names in source; never alter a published version.

**Checkpoint**: rename-safe.

---

## Phase 7: User Story 5 — Secrets never leak (Priority: P3)

**Goal**: literal reads of protected variables are checked at publish; computed reads are checked at run time for the
variable actually read; secret values are redacted from logs and recorded reads; withheld values are not provided.

**Independent Test**: reading a secret by literal and computed name and logging shows redaction; a no-permission value is
not provided.

### Tests for User Story 5

- [ ] T037 [P] [US5] Cover publish-time protection check for a literal read of a protected variable, the "possible protected read" publish diagnostic for a computed read, the run-time check of the actual read, redaction in logs/diagnostics/recorded reads, and non-provision of a withheld value in `Essentials/tests/.../SecretVariableReadTests.cs`.

### Implementation for User Story 5

- [ ] T038 [US5] Check protection at publish for a literal read of a protected variable; check the actual computed read at run time (a withheld variable the script never reads does not block it); ensure secret values are redacted wherever reads could be logged, including recorded reads (R-8).

**Checkpoint**: secrets handled.

---

## Phase 8: Input snapshot vs body session (cross-cutting)

- [ ] T039 [P] Cover that input-binding expressions read the input snapshot and never see pending writes, and that `RunJavaScript` body reads (literal and computed) go through the ADR 0092 session view while their recorded reads still enter the input snapshot; record the write assumptions for verification against S-7 (R-10).

---

## Phase 9: Polish & Cross-Cutting

- [ ] T040 [P] Update docs/glossary or CONTEXT for the `variables` read surface and the run-time fallback if warranted (domain terms only).
- [ ] T041 [P] Confirm architecture guard tests pass: `Runtime_projects_do_not_add_design_references`, `Elsa3MigrationBoundaryTests`, `FeatureCompositionTests`; place new tests in the correct CI tier (§E7).
- [ ] T042 Run `specs/1222-scripts-read-by-name/quickstart.md` validation end-to-end.
- [ ] T043 Confirm ADR 0089 on PR #3420 carries the minimal D-7 amendment this spec implements, and the spec 095 contract change, before this spec's formal approval.
- [ ] T044 Set this spec's `**Status**:` line to `Implemented` in the delivery PR, before it merges (spec-lifecycle rule; never a separate status PR).

---

## Dependencies & Execution Order

- **Setup (P1)** → no deps.
- **Foundational (P2: T003–T005)** → blocks all stories.
- **US1 (P1)** after Foundational — MVP.
- **US2 (P1)** after T005 (marker) and T010 (`variables` root).
- **US3 (P2)** after US1's pinning/`variables` root (T009–T010) and US2's fallback (T021) for computed conversions; needs #3167 (S-0) for the full #3287 fix.
- **US4 (P3)** after T005/T009/T019 (resolver, pinning, conservative report).
- **US5 (P3)** after T009 and T021 (pinned reads, fallback check).
- **Phase 8** after US1 and US2.
- **Polish** after the desired stories.

### Parallel opportunities

- T006–T008 (US1 tests), T015–T018 (US2 tests), T024–T027 (US3 tests) parallel; test tasks across stories parallel once their phase deps are met.
- US4 and US5 are independent of each other once US1 and US2 land.

## Implementation Strategy

- **MVP** = Phase 1–3 (literal reads). Validate independently, then add US2 (run-time fallback), US3 (converter), US4
  (rename), US5 (secrets).
- Each story is an independent, testable increment.
