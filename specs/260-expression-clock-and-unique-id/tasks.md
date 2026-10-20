# Tasks: Expressions read the current time and unique values through recorded parameters

**Input**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md),
[contract](contracts/clock-uniqueid-and-binding-pure-v2.md)

**Tests**: xUnit only. Required by framework §2.23 for every logic-bearing change below, and named by the spec's
acceptance scenarios. This is not greenfield work, and the Elsa constitution declares no TDD cadence, so tests sit
beside the implementation in each story.

**Analysis**: remediated after `/speckit-analyze` on 2026-10-20. That pass found C1 (wrap prelude errors), H1 (existing
tests to update), H2 (quickstart projects), H3 (retry is defined per FR-008/FR-008a), M1–M9 and L1–L6.

**Format**: `- [ ] [ID] [P?] [Story] Description with file path`

## Phase 1: Setup

- [x] T001 Build the touched projects once to confirm a clean baseline

## Phase 2: Foundational (blocks every story)

- [x] T002 Add `ClockExpressionParameterBinding` and `UniqueIdExpressionParameterBinding` (no members) with discriminators `"Clock"` and `"UniqueId"` in `Essentials/src/Expressions/Core/Models/ExpressionDefinition.cs` (P-1)
- [x] T003 Add `ExpressionCapabilityProfiles.BindingPureV2` and `IsBindingPure(string)` in `Essentials/src/Expressions/Core/Models/ExpressionDefinition.cs`; give v2 the pure grant in `Essentials/src/Expressions/Core/Contracts/ExpressionEvaluationCapabilities.cs`; update `Essentials/src/Expressions/Core/PublicAPI.Unshipped.txt` (P-5)
- [x] T004 Add optional `EvaluatedAt` and `WithEvaluatedAt` to `Essentials/src/Workflows/Runtime/Core/Models/RuntimeInputBindingResolutionContext.cs` (P-2)
- [x] T005 [P] Wire tests: `Clock` and `UniqueId` round-trip; the four-kind fingerprint fixture and the `WorkflowInstance` fixture are unchanged; a v2 definition with both kinds keeps its bytes, and its fingerprint differs from the same definition with literals, in `Essentials/tests/Expressions/Tests/ExpressionParameterBindingWireTests.cs` (FR-001, FR-002, FR-006)
- [x] T006 [P] Capability tests: v1 and v2 resolve to the pure grant; other spellings do not, in `Essentials/tests/Expressions/Tests/ExpressionDefinitionContractTests.cs` (FR-013, FR-024)

## Phase 3: User Story 1 + 2 — Clock and UniqueId values (P1)

**Goal**: `args.now` and `args.<id>` resolve from the evaluation instant, are stable on retry and differ per `UniqueId` parameter.

- [x] T007 [US1] Set `EvaluatedAt = materializedAt` in `RuntimeActivityInputMaterializer.MaterializeSnapshotAsync`, throwing if the caller's instant differs (done). Also build the context with `evaluatedAt: materializedAt` in `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Values/RuntimeActivityInputSnapshotMaterializer.cs`, so a replaced `IRuntimeActivityInputMaterializer` gets the instant (M9) (P-2, FR-010)
- [x] T008 [US1] One `timeProvider.GetUtcNow()` per intrinsic `MaterializeValueAsync`, passed as `evaluatedAt`, in `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Values/WorkflowIntrinsicExecutor.cs` (FR-012)
- [x] T009 [US1] `Clock` and `UniqueId` arms in `DetermineDependencyPolicy` and `MaterializeParametersAsync` (P-3, P-4); accept both pure profiles, keeping the substring `'binding-pure-v1'` in the message, which `WriteHttpResponseExecutionTests.cs:207` checks, in `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Values/RuntimePortableExpressionEvaluator.cs`
- [x] T010 [US1] Accept v1 and v2 in `Essentials/src/Expressions/Elsa.Expressions/Services/PortableExpressionEvaluator.cs`, keeping `'binding-pure-v1'` in the message (`PortableExpressionEvaluatorTests.cs:56`) (P-6)
- [x] T011 [P] [US1] Resolver tests in `Essentials/tests/Workflows/Runtime/Tests/CanonicalRuntimeInputBindingResolverTests.cs`:
  - `Clock` is the snapshot's `MaterializedAt` in ISO millisecond UTC form, and two `Clock` parameters are equal;
  - 1,000 `UniqueId` values are distinct, lowercase UUID v7;
  - a context evaluated at another instant is refused.
  The missing-instant path (`VF-EXP-CLOCK-001`) has no public route, since both producers always set the instant. It stays a defensive guard with no test (FR-003–FR-005, SC-002).
- [x] T012 [US1] Run-level tests mirroring `WorkflowInstanceParameterRunTests`, in `Essentials/tests/Activities/Runtime/Tests/ClockParameterRunTests.cs` (new) (FR-007, FR-008, FR-008a, FR-012, SC-001):
  - an activity input returning `args.now` and `args.id` records `MaterializedAt` and a v7 ID in its committed snapshot;
  - a faulted activity retried through `RetryFaultedActivity` sees the same values;
  - an intrinsic `Set` of `args.now` commits the instant read before evaluation;
  - a `Control` intrinsic outcome may branch on a `Clock` comparison.

## Phase 4: User Story 3 — Loops see time move (P1)

- [x] T013 [US3] Comment at the re-materialization call site (`:186-193`) naming ADR 0088 D2, and noting that the inspection timestamp at `:201-203` is not the `Clock` source, in `Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/WorkflowParentActivityCompletionSchedulerWorkHandler.cs` (FR-010)
- [x] T014 [US3] Loop tests in `Essentials/tests/Activities/ControlFlow/Tests/While/WhileClockParameterTests.cs` (new) (FR-010, FR-011, SC-001):
  - `While` with a fake clock advanced between passes exits once `args.now` passes `args.deadline`, and each pass sees its own `Clock`;
  - `UniqueId` differs per pass;
  - a redelivered child completion after a committed pass does not evaluate again;
  - one `Do` pass test, which shares the re-materialization path (M2).

## Phase 5: User Story 6 — Existing executables keep their behavior (P1)

- [x] T015 [US6] Stamp `binding-pure-v2` for plain-source `JavaScript` in `CompileExpressionInput`, in `Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Services/RuntimeInputBindingCompiler.cs` (P-7, FR-021)
- [x] T015a [US6] Update the tests that pin the old stamp, giving the reason (FR-021, §2.21.1) (H1). Only the compiler assertion changed; the golden fixtures carry no profile and stayed byte-identical after regeneration:
  - `Essentials/tests/Workflows/Publishing/Api/Tests/WorkflowExecutableCompilerTests.cs:1289-1294` (plain JS now v2);
  - the golden `Essentials/tests/Workflows/Publishing/Api/Tests/Fixtures/CompilerGoldens/javascript-expression.json`, regenerated with `UPDATE_COMPILER_GOLDENS=1` (artifact hash includes the profile).
- [x] T016 [P] [US6] Stamping tests in `Essentials/tests/Workflows/Publishing/Api/Tests/ExpressionCapabilityProfileStampingTests.cs` (new) (FR-021, FR-023, SC-005):
  - plain JavaScript compiles to v2, and plain Liquid to v1;
  - a portable definition declaring v1 keeps v1;
  - a stored binding stamped v1 reads back as v1.
- [x] T017 [P] [US6] Liquid tests in `Essentials/tests/Expressions/Tests/LiquidExplicitParametersTests.cs` (FR-005, FR-022, M3):
  - a Liquid expression stamped v2 is rejected;
  - a Liquid v1 expression with `Clock` and `UniqueId` parameters receives their string values.

## Phase 6: User Story 4 — A Date without a clock (P2)

- [x] T018 [US4] `ClocklessDateRules` (internal static) in `Essentials/src/Expressions/JavaScript/Elsa.Expressions.JavaScript/Services/ClocklessDateRules.cs`, holding the six forms, their messages and severities, the invalid-date message and the frozen prelude (P-9)
- [x] T019 [US4] Link `ClocklessDateRules.cs` into `Essentials/src/Expressions/JavaScript/Jint/Elsa.Expressions.JavaScript.Jint.csproj`
- [x] T020 [US4] `ClocklessTimeSystem : ITimeSystem` in `Essentials/src/Expressions/JavaScript/Jint/Services/ClocklessTimeSystem.cs` (FR-015, FR-016)
- [x] T021 [US4] `IsolatedJintEngine.Create(..., clocklessDate)` in `Essentials/src/Expressions/JavaScript/Jint/Services/IsolatedJintEngine.cs` (P-8, FR-018, FR-023)
- [x] T022 [US4] Changes to `Essentials/src/Expressions/JavaScript/Jint/Services/JintPortableJavaScriptEvaluator.cs` (P-6, P-12, C1, L5):
  - accept v1 and v2; v2 gets `clocklessDate: true`;
  - under v2, exclude `Date` from the stripped list and wrap prelude `TypeError`s and the invalid-date `RangeError` in `JavaScriptBindingEvaluationException` with the rule's text as the message;
  - fix the "binding-pure-v1 evaluator" options message.
- [x] T023 [P] [US4] Sandbox tests in `Essentials/tests/Expressions/JavaScript/Jint/Tests/ClocklessDateTests.cs` (new) (FR-014–FR-016, FR-018, SC-003, SC-004):
  - every allowed contract member;
  - ISO forms parse, and non-ISO strings, `2026-02-30`, `T25:00` and four fraction digits give `NaN`;
  - local getters equal UTC getters;
  - identical results under three `CurrentCulture` values;
  - `ClocklessTimeSystem` has a UTC default zone and a zero offset;
  - `instanceof`, `constructor`, subclassing and `JSON.stringify` work;
  - `Temporal`, `Intl` and `Math.random` stay undefined.
- [x] T024 [P] [US4] v1 regression in `Essentials/tests/Expressions/JavaScript/Jint/Tests/StrippedAmbientCapabilityDiagnosticTests.cs`: the same `Date` source fails under v1 as today and succeeds under v2 (FR-023)
- [x] T024a [P] [US4] Corpus-pattern tests (SC-006) in `ClocklessDateTests.cs`, one per pattern:
  - stamp now;
  - ISO round-trip;
  - parse a request date;
  - elapsed milliseconds;
  - one ID and two IDs.

## Phase 7: User Story 5 — Clear diagnostics (P2)

- [x] T025 [P] [US5] Message tests in `Essentials/tests/Expressions/JavaScript/Jint/Tests/ClocklessDateDiagnosticTests.cs` (new) (FR-017, SC-003):
  - one test per removed form (six), each asserting a `JavaScriptBindingEvaluationException` with the exact contract message;
  - the invalid-date `toISOString()` message.
- [x] T026 [US5] Validator in `Essentials/src/Expressions/JavaScript/Elsa.Expressions.JavaScript/Services/JavaScriptExpressionToolingProvider.cs` (P-10, FR-019):
  - after a successful parse, re-parse into an Acornima AST and report each removed form from `ClocklessDateRules` with its location;
  - `toLocale*` forms as warnings.
- [x] T027 [P] [US5] Validator tests in `Essentials/tests/Expressions/Tests/ExpressionToolingProviderContractTests.cs` (FR-019):
  - each removed form reports its message, severity and location;
  - `new Date(args.now)`, `Date.parse(x)` and `obj.now()` report nothing.

## Phase 8: Polish and documentation

- [x] T030 [P] Update the parameter-kind list and add a `binding-pure-v2` section to `specs/095-value-flow-redesign/contracts/expression-and-import-contract.md` (FR-025)
- [x] T031 [P] Update four `EXTENSION_POINTS.md` files: `Essentials/src/Expressions/Elsa.Expressions/`, `Essentials/src/Expressions/JavaScript/Elsa.Expressions.JavaScript/`, `Essentials/src/Expressions/Liquid/` and `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/`
- [x] T032 [P] Documentation and stale "v1" text (L5):
  - add the author-facing table and rewrites to `Essentials/src/Workflows/Design/JavaScript/README.md`;
  - point the ADR 0083 profile sentence to ADR 0088;
  - fix the stale text in `Essentials/src/Activities/Primitives/Activities/Inline.cs:22`.
- [x] T033 Update `PublicAPI.Unshipped.txt` for the Expressions projects as the build reports
- [x] T034 Run the quickstart set and the architecture guard tests; check `docs/maps/dependency-map.json` (`git diff --stat` on Windows)
- [ ] T035 After the owner accepts ADR 0088, set it to Accepted in the ADR and in `docs/adr/README.md`, and set spec status to Implemented, in the same PR (M8)

Removed after analysis (M5): the editor typings change (former T028/T029). The editor's standard library already
declares `Date`, so FR-020 now keeps the typings as they are.

## Dependencies

- Phase 2 blocks everything, and Phase 3 blocks Phase 4.
- Phases 5 and 6 depend only on T003. Phase 7 depends on T018 and T021.

## Implementation strategy

Phases 2 and 3 form the MVP. Then come the loop (Phase 4) and the profile (Phases 5 to 7), with documentation last.
