# Tasks: Expression Code Intelligence Foundation

Baseline implementation tasks are complete. File names below reflect the landed design rather than the provisional names in the initial plan.

Tasks T001-T025 describe the implemented baseline. Program #2310 adds T026-T031. T026-T027 have exact-head producer-consumer and normal-host evidence. T028 passes the M2 automated gate at Foundation7457b7a4/Studio00677684 under the documented unavailable-review fallback; human acceptance/delivery remain pending. Task #2379 implements T029-T030 under D12's frozen common catalog/policy contract. T029-T031 remain open.

## Contracts and composition

- [x] T001 Define versioned tooling outcomes, documents, symbols, value shapes, capabilities, diagnostics, completion, and hover models in `src/Elsa/Expressions/Core/Models/ExpressionToolingModels.cs`.
- [x] T002 Define provider and resolver contracts in `src/Elsa/Expressions/Core/Contracts/`.
- [x] T003 Implement deterministic exact-type provider resolution in `src/Elsa/Expressions/Services/ExpressionToolingProviderResolver.cs`.
- [x] T004 Register the resolver and independently composable JavaScript/Liquid providers in their owning features.
- [x] T005 Document provider and Design API extension points in `src/Elsa/Expressions/EXTENSION_POINTS.md` and `src/Elsa/Workflows/Design/Api/EXTENSION_POINTS.md`.

## Authoritative Design context

- [x] T006 Define location-only context requests, authorization/filter extension points, and the context service contract in `src/Elsa/Workflows/Design/Core/Contracts/`.
- [x] T007 Implement revisioned, bounded, post-policy-paged context coordination in `src/Elsa/Workflows/Design/Core/Services/ExpressionAuthoringContextService.cs`.
- [x] T008 Project persisted draft inputs, lexically visible variables, definitely available activity outputs, expected result types, and bounded inline member shapes in `src/Elsa/Workflows/Design/Api/Services/PersistedExpressionAuthoringContextSource.cs`.
- [x] T009 Include permission and host-policy fingerprints in opaque context/catalog revisions.
- [x] T010 Add context, descriptor, symbol, completion, hover, and validation endpoints with `no-store`, permission checks, cancellation, explicit outcomes, and capability links under `src/Elsa/Workflows/Design/Api/Endpoints/Authoring/`.
- [x] T011 Advertise `expressions.tooling.v1` only for a composed service/provider set in `src/Elsa/Workflows/Design/Api/Capabilities/WorkflowDesignApiCapabilities.cs`.

## Per-expression-type tooling

- [x] T012 Implement metadata-only JavaScript tooling and runtime-accurate `args`, `variables`, `getVariable`, and named getter projection in `src/Elsa/Expressions/JavaScript/Services/JavaScriptExpressionToolingProvider.cs`.
- [x] T013 Implement parser-backed Liquid diagnostics plus Liquid completion/hover in `src/Elsa/Expressions/Liquid/Services/LiquidExpressionToolingProvider.cs`.
- [x] T014 Implement bounded nested and dotted activity-result resolution without evaluating source in `src/Elsa/Expressions/Core/Services/ExpressionToolingSymbolResolver.cs`.
- [x] T015 Expose provider descriptors and declared capability sets before document activation.

## Consequential-operation gates

- [x] T016 Implement full-draft provider validation and explicit `Valid`, `Errors`, `Unavailable`, `Unauthorized`, `Incompatible`, `Stale`, and `Canceled` states in `src/Elsa/Workflows/Design/Validations/`.
- [x] T017 Register a fail-closed `IDraftValidator` adapter while retaining a safe unavailable resolver for independently composed validation hosts.
- [x] T018 Gate publication and promotion on a valid full-draft result.
- [x] T019 Gate Test Run on known/non-unavailable failures, require explicit acknowledgement only for unavailable validation, and record the validation state/acknowledgement in run metadata.
- [x] T020 Return Test Run validation metadata through `WorkflowTestRunView`.

## Verification

- [x] T021 Cover exact provider routing, nested/dotted completion and hover, language validation, expected-result ranking, and no-evaluator operation in `tests/Elsa/Expressions/Tests/ExpressionToolingProviderContractTests.cs`.
- [x] T022 Cover authoritative context identity, scope, policy filtering, policy-only invalidation, post-policy paging, stale revisions, failures, and cancellation in Workflows Design tests.
- [x] T023 Cover capability composition and canonical relations in Architecture tests.
- [x] T024 Cover full-draft validation state mapping, publication fail-closed behavior, and Test Run acknowledgement/metadata in Workflows Design and Publishing tests.
- [x] T025 Run the focused and regression suites recorded in `verification.md`.

## Program #2310 continuation: User Story 4 — Real composed APIs

- [x] T026 [US4] Add a real Workbench-process persisted-draft authoring integration test under `tests/essentials/Workbench/Tests/`, reusing `WorkbenchProcess`, the stock authenticated shell, and its isolated SQLite content root; discover actual activity version/output metadata through the authoring API, then cover capability discovery, context, completion, hover and validation for JavaScript and Liquid.
- [x] T027 [US4] Add architecture-only independent-provider composition and missing-provider capability-advertisement cases under `tests/Elsa/Architecture/` after T026 establishes the real-host path; unavailable, unauthorized, incompatible, stale and cancellation behavior stays in the owning provider/API suites.

## Program #2310 continuation: User Story 2 — Runtime-owned language assistance

- [x] T028 [US2] Align Foundation JavaScript tooling globals, signatures and diagnostics with the runtime expression profile in `src/essentials/Expressions/JavaScript/` and provider contract tests after T026 passes; Studio spec 094 T030 owns local parser grammar.
- [ ] T029 [P] [US2] Extract the effective binding-pure Liquid filter/tag profile and consume it from both runtime evaluation and `LiquidExpressionToolingProvider` in `src/essentials/Expressions/Liquid/` with parity tests after T026 passes.
- [ ] T030 [US2] Add Liquid cursor-context completion plus known-shape signature/diagnostic conformance in `src/essentials/Expressions/Liquid/Services/LiquidExpressionToolingProvider.cs` and provider contract tests after T029.
- [ ] T031 [US2] Record exact-head focused, affected-suite, architecture, maps and real producer-consumer evidence in `specs/143-expression-code-intelligence/verification.md`.

## Dependencies

- T026 is the first executable implementation leaf and owns the end-to-end real-host cases.
- T027 follows T026 and remains architecture-only so it does not duplicate the real-host harness.
- T026-T027 pass at Foundation `f81be4be` with coordinated Studio `c9479fba` proof; see `verification.md`. T028-T029 are unblocked; T030 depends on T029.
- T031 closes the Foundation contribution only after T027-T030 and the coordinated Studio consumer checks pass.
