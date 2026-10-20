# Tasks: Expressions read the current time and unique values through recorded parameters

**Input**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md),
[contract](contracts/clock-uniqueid-and-binding-pure-v2.md)

**Tests**: xUnit only. Required by framework §2.23 for every logic-bearing change below, and named by the spec's
acceptance scenarios. This is not greenfield work, and the Elsa constitution declares no TDD cadence, so tests sit
beside the implementation in each story.

**Format**: `- [ ] [ID] [P?] [Story] Description with file path`

## Phase 1: Setup

- [ ] T001 Build the touched projects once to confirm a clean baseline: `Essentials/src/Expressions/Core`, `Essentials/src/Expressions/JavaScript/Jint`, `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime`, `Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing`

## Phase 2: Foundational (blocks every story)

- [ ] T002 Add `ClockExpressionParameterBinding` and `UniqueIdExpressionParameterBinding` (no members) with `[JsonDerivedType]` discriminators `"Clock"` and `"UniqueId"` beside `WorkflowInstanceExpressionParameterBinding` in `Essentials/src/Expressions/Core/Models/ExpressionDefinition.cs` (P-1)
- [ ] T003 Add `ExpressionCapabilityProfiles.BindingPureV2 = "binding-pure-v2"` and `IsBindingPure(string)` in `Essentials/src/Expressions/Core/Models/ExpressionDefinition.cs`; make `ExpressionEvaluationCapabilities.Resolve` grant v2 the pure set in `Essentials/src/Expressions/Core/Contracts/ExpressionEvaluationCapabilities.cs` (P-5)
- [ ] T004 Add optional `EvaluatedAt` (`DateTimeOffset?`) to `RuntimeInputBindingResolutionContext` (constructor parameter, defaulting to null, plus a `WithEvaluatedAt` copy helper) in `Essentials/src/Workflows/Runtime/Core/Models/RuntimeInputBindingResolutionContext.cs` (P-2)
- [ ] T005 [P] Wire tests: round-trip and fingerprint for `Clock` and `UniqueId`, rejection of unknown kinds unchanged, existing four-kind and `WorkflowInstance` fixtures byte-identical, in `Essentials/tests/Expressions/Tests/ExpressionParameterBindingWireTests.cs` (FR-001, FR-002, FR-006)
- [ ] T006 [P] Capability tests: v1 and v2 resolve to the pure grant, unknown profiles do not, in `Essentials/tests/Expressions/Tests/ExpressionEvaluationCapabilitiesTests.cs` (new, or the existing capabilities test file if one exists) (FR-013, FR-024)

## Phase 3: User Story 1 + 2 — Clock and UniqueId values (P1)

**Goal**: `args.now` and `args.<id>` resolve from the evaluation instant, are stable on retry and differ per `UniqueId` parameter.
**Independent test**: run an activity whose JavaScript input returns both kinds, fault it once, retry, and compare.

- [ ] T007 [US1] Set `EvaluatedAt = materializedAt` in `RuntimeActivityInputMaterializer.MaterializeSnapshotAsync`; throw if the caller's context carries a different instant, in `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Values/RuntimeActivityInputMaterializer.cs` (P-2, FR-010)
- [ ] T008 [US1] Read `timeProvider.GetUtcNow()` once per `MaterializeValueAsync` and pass it as `EvaluatedAt` in `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Values/WorkflowIntrinsicExecutor.cs` (P-2, FR-012)
- [ ] T009 [US1] Add `Clock` and `UniqueId` arms to `DetermineDependencyPolicy` (no policy) and `MaterializeParametersAsync` (formats per P-3; `VF-EXP-CLOCK-001` without an instant per P-4); accept both pure profiles at the profile check, in `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Values/RuntimePortableExpressionEvaluator.cs` (FR-003, FR-004, FR-005, FR-012)
- [ ] T010 [US1] Accept v1 and v2 in the dispatcher check in `Essentials/src/Expressions/Elsa.Expressions/Services/PortableExpressionEvaluator.cs` (P-6)
- [ ] T011 [P] [US1] Resolver tests: `Clock` equals the context instant in ISO millisecond form; two `Clock` parameters are equal; two `UniqueId` parameters differ, are lowercase UUID v7, and 1,000 generated values are distinct; a missing instant throws `VF-EXP-CLOCK-001` naming parameter, node and input; neither kind adds a policy. File: `Essentials/tests/Workflows/Runtime/Tests/ClockAndUniqueIdParameterResolutionTests.cs` (new) (FR-003–FR-005, FR-012, SC-002)
- [ ] T012 [US1] Run-level tests mirroring `WorkflowInstanceParameterRunTests`: an activity input returning `args.now`/`args.id` records `MaterializedAt` and a v7 ID in its committed snapshot; a faulted-then-retried activity sees the same values; an intrinsic `Set` of `args.now` commits the instant read once before evaluation. File: `Essentials/tests/Activities/Runtime/Tests/ClockParameterRunTests.cs` (new) (FR-007, FR-008, FR-009, SC-001)

## Phase 4: User Story 3 — Loops see time move (P1)

**Goal**: each `While` pass sees its own instant and IDs; a committed pass never evaluates again.
**Independent test**: a `While` deadline loop under a controllable `TimeProvider`.

- [ ] T013 [US3] Confirm the re-materializer passes its `materializedAt` (from `WorkflowParentActivityCompletionSchedulerWorkHandler.cs:192`) into `MaterializeSnapshotAsync` unchanged, and that the inspection timestamp (:201-203) is not used for `Clock`; add a comment at the call site naming ADR 0088 D2, in `Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/WorkflowParentActivityCompletionSchedulerWorkHandler.cs` (FR-010)
- [ ] T014 [US3] `While` tests: a condition `args.now < args.deadline` with a fake clock advanced between passes exits when the deadline passes, and each pass's evaluated `Clock` differs; a `UniqueId` in the condition differs per pass; a redelivered child-completion work item after a committed pass does not evaluate the condition again (the evaluation count stays the same). File: `Essentials/tests/Activities/ControlFlow/Tests/While/WhileClockParameterTests.cs` (new) (FR-010, FR-011, SC-001)

## Phase 5: User Story 6 — Existing executables keep their behavior (P1)

**Goal**: v1 unchanged; plain-source JavaScript published as v2; Liquid stays v1.

- [ ] T015 [US6] Stamp `binding-pure-v2` for plain-source `JavaScript` (ordinal, ignoring case) in `CompileExpressionInput`, leaving other languages and full portable definitions unchanged, in `Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Services/RuntimeInputBindingCompiler.cs` (P-7, FR-021)
- [ ] T016 [P] [US6] Publishing tests: plain JavaScript compiles to v2; plain Liquid to v1; a portable definition declaring v1 keeps v1; deserializing a stored binding stamped v1 keeps v1. File: `Essentials/tests/Workflows/Publishing/Api/Tests/ExpressionCapabilityProfileStampingTests.cs` (new) (FR-021, FR-023, SC-005)
- [ ] T017 [P] [US6] Liquid test: a Liquid expression stamped v2 is rejected with the existing v1-required message, in `Essentials/tests/Expressions/Tests/LiquidExplicitParametersTests.cs` (FR-022)

## Phase 6: User Story 4 — A Date without a clock (P2)

**Goal**: under v2, `Date` parses ISO only, is UTC-only, and behaves per the contract; v1 unchanged.

- [ ] T018 [US4] Create `ClocklessDateRules` (internal static): the six removed forms with their exact messages and validation severities, the invalid-date message, and the frozen JavaScript prelude from R-1, in `Essentials/src/Expressions/JavaScript/Elsa.Expressions.JavaScript/Services/ClocklessDateRules.cs` (P-9)
- [ ] T019 [US4] Link `ClocklessDateRules.cs` into `Essentials/src/Expressions/JavaScript/Jint/Elsa.Expressions.JavaScript.Jint.csproj`, the same way `JavaScriptBindingSyntax.cs` is linked
- [ ] T020 [US4] Create `ClocklessTimeSystem : ITimeSystem` (`GetUtcNow` throws; a fixed custom UTC zone with English names; ISO-only `TryParse` via the R-1 regex delegating to `DefaultTimeSystem`; zero offset) in `Essentials/src/Expressions/JavaScript/Jint/Services/ClocklessTimeSystem.cs` (FR-015, FR-016)
- [ ] T021 [US4] Add `clocklessDate` to `IsolatedJintEngine.Create`: when true, set `TimeSystem` and the invariant `Culture`, run the prelude, and strip `Temporal`, `Intl` and `Math.random` but not `Date`; when false, behave exactly as today. File: `Essentials/src/Expressions/JavaScript/Jint/Services/IsolatedJintEngine.cs` (P-8, FR-018, FR-023)
- [ ] T022 [US4] Accept v1 and v2 in `JintPortableJavaScriptEvaluator`; create the engine with `clocklessDate` for v2; exclude `Date` from the stripped-capability diagnostic under v2 and name the actual profile in its message, in `Essentials/src/Expressions/JavaScript/Jint/Services/JintPortableJavaScriptEvaluator.cs` (P-6, P-12)
- [ ] T023 [P] [US4] Sandbox tests under v2: every allowed member in the contract; ISO forms parse (date-only, year-month, year, with offset, expanded year); non-ISO strings, `2026-02-30`, `T25:00` and four fraction digits give `NaN`; `getHours() === getUTCHours()`; `toString()` is identical with the process time zone and culture set to three different values; `instanceof`, `constructor`, subclassing and `JSON.stringify` work; `Temporal`, `Intl` and `Math.random` stay undefined. File: `Essentials/tests/Expressions/JavaScript/Jint/Tests/ClocklessDateTests.cs` (new) (FR-014–FR-016, FR-018, SC-003, SC-004)
- [ ] T024 [P] [US4] v1 regression: the existing `typeof` assertions in `ExplicitExpressionParametersTests.cs` and the `new Date()` diagnostic in `StrippedAmbientCapabilityDiagnosticTests.cs` still pass for v1; add one test showing the same `Date` source fails under v1 and succeeds under v2, in `Essentials/tests/Expressions/JavaScript/Jint/Tests/StrippedAmbientCapabilityDiagnosticTests.cs` (FR-023, US6 scenario 1)

## Phase 7: User Story 5 — Clear diagnostics (P2)

**Goal**: every removed form fails with its contract message at runtime and in authoring validation.

- [ ] T025 [P] [US5] Runtime message tests: one test per removed form (six) asserting the exact contract message surfaces in `JavaScriptBindingEvaluationException`; the invalid-date `toISOString()` message, in `Essentials/tests/Expressions/JavaScript/Jint/Tests/ClocklessDateDiagnosticTests.cs` (new) (FR-017, SC-003)
- [ ] T026 [US5] In `JavaScriptExpressionToolingProvider`, after a successful parse walk the Acornima AST and report each removed form from `ClocklessDateRules` at its line and column (errors, warnings for `toLocale*`), without evaluating, in `Essentials/src/Expressions/JavaScript/Elsa.Expressions.JavaScript/Services/JavaScriptExpressionToolingProvider.cs` (P-10, FR-019)
- [ ] T027 [P] [US5] Validator tests: each removed form reports its message, severity and location; `new Date(args.now)`, `Date.parse(x)`, `obj.now()` and a local `function Date() {}` shadow produce no diagnostic, in `Essentials/tests/Expressions/Tests/ExpressionToolingProviderContractTests.cs` (FR-019)
- [ ] T028 [US5] Declare a `Date` interface and constructor with only the allowed members in `Essentials/src/Workflows/Design/JavaScript/Contributors/BindingPureArgsDeclarationContributor.cs` (P-11, FR-020)
- [ ] T029 [P] [US5] Typings test: the declaration contains `Date` with `toISOString` and `parse` but not `now` or `toLocaleString`, in `Essentials/tests/Workflows/Design/Tests/Unit/` (the file that tests `BindingPureArgsDeclarationContributor`) (FR-020)

## Phase 8: Polish and documentation

- [ ] T030 [P] Update the parameter-kind list and add a `binding-pure-v2` section (allowed, removed and constrained table with the rewrites, from the contract) in `specs/095-value-flow-redesign/contracts/expression-and-import-contract.md` (FR-025)
- [ ] T031 [P] Update `Essentials/src/Expressions/Elsa.Expressions/EXTENSION_POINTS.md`, `Essentials/src/Expressions/JavaScript/Elsa.Expressions.JavaScript/EXTENSION_POINTS.md`, `Essentials/src/Expressions/Liquid/EXTENSION_POINTS.md` (Liquid stays v1) and `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/EXTENSION_POINTS.md` (resolver sources)
- [ ] T032 [P] Update `Essentials/src/Workflows/Design/JavaScript/README.md` with the author-facing table and rewrites, and the ADR 0083 text that says bindings retain `binding-pure-v1` in `docs/adr/0083-javascript-binding-grammar-is-pinned-at-publish.md` (pointer to ADR 0088)
- [ ] T033 Update `PublicAPI.Unshipped.txt` where the touched projects track public API
- [ ] T034 Run the quickstart test set; regenerate `docs/maps/dependency-map.json` only if `-- check` reports a change (use `git diff --stat` on Windows); run the architecture guard tests
- [ ] T035 Set spec status to Implemented in `specs/260-expression-clock-and-unique-id/spec.md` before merge, in the same PR

## Dependencies

- Phase 2 blocks everything. US1+2 (Phase 3) blocks US3 (Phase 4), because the `While` tests need the resolver arms.
- US6 (Phase 5) depends only on T003. US4 (Phase 6) depends on T003. US5 (Phase 7) depends on T018 and T021.
- Phases 3, 5 and 6 can proceed in parallel after Phase 2.

## Parallel examples

- After T004: T011 can be written while T007–T009 are implemented; T016 and T017 alongside T015.
- In Phase 6: T023 and T024 in parallel once T021–T022 land.

## Implementation strategy

The MVP is Phases 2 and 3: `Clock` and `UniqueId` stable on retry. This is useful even before v2, because a parameter
can be compared as a string. Next come `While` (Phase 4) and the profile (Phases 5 to 7), then documentation. Each
phase ends with its own tests green.
