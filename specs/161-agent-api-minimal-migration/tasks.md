# Tasks: Wave 4 Agent REST and SSE API Migration

## Phase 1: Setup

- [x] T001 Read issue #1370, the first-party REST migration program, ADR 0068, Agent endpoints, and Foundation Identity authorization contracts in `docs/`, `src/Elsa/Agent/`, and `src/Elsa/Foundation/Identity/`.
- [x] T002 Freeze the eleven real FastEndpoints-before HTTP, OpenAPI, and SSE observations in `tests/Elsa/Architecture/Baselines/`.

## Phase 2: Foundational

- [x] T003 [P] Add Agent-owned permission contributions and reviewed implications in `src/Elsa/Agent/Api/Authorization/AgentPermissionContributor.cs`.
- [x] T004 [P] Add shared Agent Minimal API host, authentication principal matrix, and FastEndpoints coexistence canary in `tests/Elsa/Architecture/Wave4AgentFastEndpointsBaselineTests.cs`.

## Phase 3: User Story 1 - Contract-preserving Agent mappings

- [x] T005 [US1] Add explicit eleven-route mapper and stable owner/security/OpenAPI metadata in `src/Elsa/Agent/Api/AgentApi.cs`.
- [x] T006 [US1] Add owner-local generated response and SSE serializer contexts in `src/Elsa/Agent/Api/AgentJsonContext.cs`.
- [x] T007 [US1] Replace the Agent `IWebShellFeature` adapter and remove exactly eleven endpoint adapters in `src/Elsa/Agent/Api/FoundationAgentApiFeature.cs` and `src/Elsa/Agent/Api/Endpoints/`.
- [x] T008 [US1] Compare all eleven Minimal API HTTP/OpenAPI projections against the immutable fixtures in `tests/Elsa/Architecture/Wave4AgentMinimalApiCompatibilityTests.cs`.

## Phase 4: User Story 2 - Shared authorization and coexistence

- [x] T009 [US2] Prove anonymous 401, authenticated 403, exact, implied, wildcard, resource, tenant, and mixed-framework authorization in `tests/Elsa/Architecture/Wave4AgentAuthorizationTests.cs`.
- [x] T010 [US2] Assert explicit Agent endpoint ownership, authoring model, and permission metadata in `tests/Elsa/Agent/Tests/AgentApiMappingTests.cs`.

## Phase 5: User Story 3 - Streaming and lifecycle safety

- [x] T011 [US3] Prove SSE headers, framing, awaited enumeration, cancellation, and disposal in `tests/Elsa/Architecture/Wave4AgentSseLifecycleTests.cs`.
- [x] T012 [US3] Prove three real route publication, DI, generated serializer, authentication/metadata, and collectible-context cycles in `tests/Elsa/Architecture/Wave4AgentCollectibilityTests.cs`.
- [x] T013 [US3] Remove exactly eleven Agent transition exceptions and update the executable ratchet in `tests/Elsa/Architecture/Baselines/fastendpoints-transition-exceptions.json` and `tests/Elsa/Architecture/FastEndpointsTransitionTests.cs`.

## Phase 6: Polish and cross-cutting gates

- [x] T014 Update the migration report, evidence index, specification, contracts, data model, and quickstart in `docs/reports/` and `specs/161-agent-api-minimal-migration/`.
- [x] T015 Run the full Agent tests, architecture tests, relevant backend/E2E checks, full solution build, maps check, formatter, and final diff review from the control room; no Agent-specific `e2e-tests/` harness exists, so the real TestServer HTTP/SSE suite is the available host-level evidence.

## Dependencies

T001-T004 precede T005-T008. T005-T008 precede T009-T013. T014 records completed evidence; T015
remains the parent control-room integration gate.

## Parallel opportunities

T003/T004 can proceed in parallel after the baseline scope is agreed. T008, T009, and T010 can be
run independently once the mapper exists. T011 and T012 can run independently after mapping and
serializer ownership are complete.

## Implementation strategy

The MVP is the frozen baseline plus the explicit mapper and exact HTTP/OpenAPI comparison. The
authorization and lifecycle stories are required before the wave can be considered complete. Full
repository gates and any E2E failures remain the integrating control room's responsibility.
