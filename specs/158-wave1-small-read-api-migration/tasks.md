# Tasks: Wave 1 Small and Read-Oriented REST API Migration

## Phase 1: Setup

- [x] T001 Read #1367, Wave 0 registry, ADR 0068, Foundation Identity contracts, and owner endpoint tests in `docs/` and `tests/`.
- [x] T002 Freeze the eight route/method and security observations in `specs/158-wave1-small-read-api-migration/contracts/wave1-http-contract.md`.

## Phase 2: Foundational

- [x] T003 Add module-owned permission vocabulary and contributors for wildcard-only owners under `src/Elsa/Attention/Api/`, `src/Elsa/Expressions/JavaScript/Rendering/`, and `src/Elsa/Workflows/Runtime/JavaScript/`.
- [x] T004 Add explicit owner/authoring/security metadata and shared evaluator authorization tests under `tests/Elsa/Architecture/`.

## Phase 3: User Story 1 - Contract-preserving mappings

- [x] T005 [US1] Replace API Capabilities FastEndpoints feature and registration with `MapApiCapabilitiesApi` in `src/Elsa/Api/Capabilities/`.
- [x] T006 [US1] Replace Attention FastEndpoints feature and registration with `MapAttentionApi` in `src/Elsa/Attention/Api/`.
- [x] T007 [US1] Replace Expressions FastEndpoints feature and two registrations with `MapExpressionsApi` in `src/Elsa/Expressions/Api/`.
- [x] T008 [US1] Replace JavaScript Rendering and Runtime JavaScript registrations with explicit mappers in their owner directories.
- [x] T009 [US1] Replace the two Dashboard registrations with `MapWorkflowsDashboardApi` in `src/Elsa/Workflows/Dashboard/`.
- [x] T010 [US1] Add focused HTTP/JSON/error/OpenAPI contract tests and immutable before fixtures under the six owner test directories.

## Phase 4: User Story 2 - Shared authorization and coexistence

- [x] T011 [US2] Prove anonymous, denied, exact, implied, wildcard, and mixed-host evaluator behavior in `tests/Elsa/Architecture/` and owner API tests.
- [x] T012 [US2] Update permission catalog ownership and metadata architecture assertions for the six owners.

## Phase 5: User Story 3 - Lifecycle and retirement reconciliation

- [x] T013 [US3] Add repeated collectible-context lifecycle evidence for all six owner mappers, including each owner’s real DI setup and source-generated serializer context/options path.
- [x] T014 [US3] Remove exactly eight entries from `tests/Elsa/Architecture/Baselines/fastendpoints-transition-exceptions.json` and update the executable count/ratch tests.

## Phase 6: Polish and gates

- [x] T015 Update owner READMEs/reports and reconcile acceptance criteria in `docs/reports/wave-1-minimal-api-migration-2026-08.md`.
- [x] T016 Run focused tests, architecture gates, full build, maps freshness, and final diff/self-review.

## Dependencies

T001-T004 precede T005-T010. T005-T010 precede T011-T014. T015-T016 are final cross-cutting gates.

## Implementation strategy

The independently testable MVP is the six explicit mappers and eight contract fixtures. Authorization and retirement reconciliation are required before the wave is complete; no later wave is included.
