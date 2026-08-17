# Tasks: Retained Host Ownership and Security Metadata

**Input**: Design documents from `/specs/156-retained-host-route-metadata/`

**Tests**: Included because the implementation changes security and endpoint metadata behavior; follow TDD.

## Phase 1: Setup

- [X] T001 Confirm issue #1365 claim, branch, source ADRs, and current host route inventory in `docs/adr/0037-studio-management-bridge-keeps-host-management-key-server-side.md` and `docs/adr/0068-first-party-rest-apis-use-aspnet-core-minimal-apis.md`.

## Phase 2: Foundational

- [X] T002 Add failing manifest coverage for custom host-credential filter enforcement in `tests/Elsa/Api/Compatibility/Testing/Tests/EndpointManifestBuilderTests.cs`.
- [X] T003 Add failing retained Workbench route manifest coverage in `tests/Elsa/Modularity/Tests/RetainedHostEndpointMetadataTests.cs`.

## Phase 3: User Story 1 - Inspect retained host routes (Priority: P1) 🎯 MVP

**Goal**: Every retained route publishes valid ownership, authoring, and one security disposition.

**Independent Test**: The retained Workbench mapping test builds and validates a deterministic manifest of 64
published entries (60 non-console entries plus the SignalR hub and negotiate entries and two HTTP routes);
shared manifest tests reject missing/ambiguous metadata.

- [X] T004 [US1] Add custom host-credential enforcement metadata and manifest validation in `src/Elsa/Api/AspNetCore/EndpointHostCredentialEnforcementMetadata.cs` and `tests/Elsa/Api/Compatibility/Testing/Manifests/EndpointManifestBuilder.cs`.
- [X] T005 [US1] Add host route convention calls to Extension Builder in `src/Elsa/Modularity/ExtensionBuilder/ExtensionBuilderApi.cs`.
- [X] T006 [US1] Add host ownership/security/authoring metadata to Workbench module and readiness mappings in `src/Apps/Elsa.Workbench/ElsaModuleManagementApi.cs` and `src/Apps/Elsa.Workbench/Readiness/ShellReadinessEndpointExtensions.cs`.
- [X] T007 [US1] Add root, console-log, and CShells host metadata in `src/Apps/Elsa.Workbench/Program.cs`.
- [X] T008 [US1] Add host ownership/security/authoring metadata to Foundation Host health and module-management mappings in `src/Apps/Elsa.Foundation.Host/Health/HealthEndpoints.cs` and `src/Apps/Elsa.Foundation.Host/ModuleManagement/ModuleManagementEndpoints.cs`.
- [X] T009 [US1] Add architecture/source guards for exact retained host route metadata in `tests/Elsa/Architecture/HostEndpointMetadataTests.cs`.

## Phase 4: User Story 2 - Preserve host security boundaries (Priority: P1)

**Goal**: Existing custom credentials, trusted caller, default policy, health, CORS, and SignalR behavior remains intact.

**Independent Test**: Security tests cover missing/invalid/valid management keys and unguarded CShells regression;
existing console/health fixtures remain green.

- [X] T010 [US2] Reuse the management-key filter for Workbench CShells management without adding a Foundation permission in `src/Elsa/Modularity/ExtensionBuilder/ManagementApiKeyAuthentication.cs` and `src/Apps/Elsa.Workbench/Program.cs`.
- [X] T011 [US2] Add positive/negative host-control and metadata contract tests in `tests/Elsa/Modularity/Tests/RetainedHostEndpointMetadataTests.cs` and `tests/Elsa/Modularity/Tests/FoundationHostEndpointTests.cs`.

## Phase 5: Polish & Cross-Cutting Concerns

- [X] T012 [P] Run affected API/modularity/architecture tests and review the generated manifest/diff.
- [X] T013 [P] Run generated maps freshness check and `git diff --check`.
- [X] T014 Commit the completed implementation locally with issue-scoped message.

## Dependencies & Execution Order

- T001 → T002/T003 (tests first) → T004 → T005–T011 → T012/T013 → T014.
- T005, T006, T007, and T008 touch independent mapping owners after T004 and can be parallelized, but are integrated and tested together.

## Implementation Strategy

1. Establish the failing manifest and route tests.
2. Implement the shared marker/validator, then annotate mappings and secure CShells management.
3. Run focused suites, architecture/maps checks, review the diff, and commit locally.
