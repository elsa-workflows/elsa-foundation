# Tasks: Foundation Identity Minimal API Migration

**Input**: [plan.md](plan.md) and [spec.md](spec.md)

## Phase 1: Before Evidence (blocking)

- [x] T001 Review issue #1369, program gates, current claims/open PRs, constitution rules, and the Wave 2 registry dependency.
- [x] T002 Capture all nine FastEndpoints route, HTTP, security, ownership, and consumed OpenAPI observations in `tests/Elsa/Foundation/Identity/Tests/Baselines/identity-http-fastendpoints.json` and `identity-openapi-fastendpoints.json`.
- [x] T003 Freeze exact approved framework-authoring differences in `tests/Elsa/Foundation/Identity/Tests/Baselines/identity-approved-differences.json` and require exhaustive consumption.

## Phase 2: Foundation Identity Routes (P1)

- [x] T004 [US1] Add failing differential and behavior tests for bootstrap, capabilities, challenge, token, refresh, session, and logout in `tests/Elsa/Foundation/Identity/Tests/Api/IdentityCompatibilityComparerTests.cs` and `TokenEndpointTests.cs`.
- [x] T005 [US1] Add explicit owner-local mappings, stable metadata, and response behavior in `src/Elsa/Foundation/Identity/Api/FoundationIdentityApi.cs`.
- [x] T006 [US1] Restrict token exchange to configured interactive schemes and add the first-party-bearer 401 regression in `tests/Elsa/Foundation/Identity/Tests/Api/TokenEndpointTests.cs`.
- [x] T007 [US2] Require only `identity.providers.read` on capabilities and prove anonymous, denied, exact, implied, wildcard, and normalized grants in `tests/Elsa/Foundation/Identity/Tests/Api/PermissionEndpointAdapterIntegrationTests.cs`.
- [x] T008 [US2] Prove a retained FastEndpoints route shares the same evaluator and preserve the coexistence canary in `tests/Elsa/Foundation/Identity/Tests/Api/EnabledShellCompositionTests.cs`.

## Phase 3: ASP.NET Core Identity Routes (P1)

- [x] T009 [US1] Add failing compatibility cases for login page, JSON/form login, credentials, redirect, and cookies in `tests/Elsa/Foundation/Identity/Tests/Api/IdentityCompatibilityComparerTests.cs`.
- [x] T010 [US1] Add explicit login mappings and stable endpoint metadata in `src/Elsa/Foundation/Identity/AspNetCoreIdentity/AspNetCoreIdentityApi.cs`.
- [x] T011 [US1] Preserve null/empty request compatibility and use owner-local generated request/response metadata in `src/Elsa/Foundation/Identity/AspNetCoreIdentity/AspNetCoreIdentityJsonContext.cs`.

## Phase 4: Composition, Unloadability, and Retirement (P2)

- [x] T012 [P] [US3] Add owner-local source-generated JSON contexts in `src/Elsa/Foundation/Identity/Api/FoundationIdentityApiJsonContext.cs` and `src/Elsa/Foundation/Identity/AspNetCoreIdentity/AspNetCoreIdentityJsonContext.cs`.
- [x] T013 [US3] Assert exact route names/tags, owner, Minimal authoring, security disposition, policies, and schemas in `tests/Elsa/Foundation/Identity/Tests/Api/MinimalIdentityEndpointMetadataTests.cs`.
- [x] T014 [US3] Add repeated real-surface collectibility cycles for both owners in `tests/Elsa/Architecture/Wave3IdentityMinimalApiCollectibilityTests.cs`.
- [x] T015 Remove only the nine owner FastEndpoints classes/project references and ratchet `tests/Elsa/Architecture/Baselines/fastendpoints-transition-exceptions.json` from 143/8 to 134/6.
- [x] T016 Update unified identity-policy documentation in `docs/reference/identity-configuration.md`, `docs/reference/authentication-architecture.md`, and publish `docs/reports/foundation-identity-wave3-minimal-api.md`.

## Phase 5: Repository Gates

- [x] T017 Run the complete identity and architecture suites, relevant live backend identity E2E tests, solution build, maps freshness, changed-file formatting, diff review, and exact nine-entry transition check.
- [x] T018 Complete independent five-axis review, address all Critical/Required findings, update the report with final evidence, and commit the final Spec Kit record.

## Dependencies

Before evidence blocks production deletion. Foundation Identity and ASP.NET Core Identity route work can proceed independently after the baseline, but shared compatibility/security/collectibility and registry retirement require both owners.

## Parallel Opportunities

T012 owner contexts can be implemented independently; route behavior, authorization, and collectibility test preparation can proceed in separate files after T003.

## Implementation Strategy

Preserve and test the external contract first, then migrate each owner, then prove shared authorization and unloadability, and finally retire exactly nine registrations. The independently testable MVP is User Story 1 with all before/after cases green; User Stories 2 and 3 are mandatory release gates.
