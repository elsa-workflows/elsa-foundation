# Tasks: Secrets Module

**Input**: Design documents from `/specs/079-secrets-module/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Required by FR-039, FR-040, SC-002 through SC-010, and framework unit-test gates.

**Organization**: Tasks are grouped by user story so each story is independently testable after the foundational contracts exist.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story the task serves
- Tasks include exact file paths

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create project shells and solution references.

- [x] T001 Add backend Secrets projects to `src/Elsa/Secrets/Core/Elsa.Secrets.Core.csproj`, `src/Elsa/Secrets/Elsa.Secrets.csproj`, `src/Elsa/Secrets/Api/Elsa.Secrets.Api.csproj`, and `src/Elsa/Secrets/Persistence/Groundwork/Elsa.Secrets.Persistence.Groundwork.csproj`
- [x] T002 Add backend test project in `tests/Elsa/Secrets/Tests/Elsa.Secrets.Tests.csproj`
- [x] T003 Add backend project and test project entries to `Elsa.Server.slnx`
- [x] T004 Add Studio Secrets project shell in `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Secrets/Elsa.Studio.Secrets.csproj`
- [x] T005 Add Studio Secrets client package shell in `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Secrets/Client/package.json`, `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Secrets/Client/tsconfig.json`, and `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Secrets/Client/vite.config.ts`
- [x] T006 Add Studio project references to `/Users/sipke/Projects/Elsa/elsa-foundation-studio/Elsa.Studio.slnx` and `/Users/sipke/Projects/Elsa/elsa-foundation-studio/tests/Elsa.Studio.Tests/Elsa.Studio.Tests.csproj`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core models, contracts, registration surfaces, and persistence shape required by all user stories.

**CRITICAL**: No user story work can begin until this phase is complete.

- [x] T007 [P] Add core secret status, capability, descriptor, reference, request, response, and query models in `src/Elsa/Secrets/Core/Models/SecretModels.cs`
- [x] T008 [P] Add secret aggregate models in `src/Elsa/Secrets/Core/Models/Secret.cs`, `src/Elsa/Secrets/Core/Models/SecretVersion.cs`, and `src/Elsa/Secrets/Core/Models/SecretPayload.cs`
- [x] T009 [P] Add core contracts in `src/Elsa/Secrets/Core/Contracts/ISecretManager.cs`, `ISecretResolver.cs`, `ISecretRepository.cs`, `ISecretStore.cs`, `ISecretStoreRegistry.cs`, `ISecretTypeProvider.cs`, `ISecretTypeRegistry.cs`, `ISecretNameValidator.cs`, `ISecretValueProtector.cs`, and `ISecretAuditSink.cs`
- [x] T010 [P] Add safe result and audit event models in `src/Elsa/Secrets/Core/Models/SecretResolution.cs` and `src/Elsa/Secrets/Core/Events/SecretOperationAuditRecord.cs`
- [x] T011 Add service registration tests for core and default feature services in `tests/Elsa/Secrets/Tests/SecretsFeatureRegistrationTests.cs`
- [x] T012 Add implementation tests for name normalization and duplicate handling in `tests/Elsa/Secrets/Tests/SecretNameValidatorTests.cs`
- [x] T013 Add default services and extensions in `src/Elsa/Secrets/Services/DefaultSecretNameValidator.cs`, `DefaultSecretValueProtector.cs`, `SecretStoreRegistry.cs`, `SecretTypeRegistry.cs`, `NullSecretAuditSink.cs`, and `src/Elsa/Secrets/Extensions/SecretsServiceCollectionExtensions.cs`
- [x] T014 Add `SecretsFeature` shell feature in `src/Elsa/Secrets/Features/SecretsFeature.cs`
- [x] T015 Add built-in type provider tests in `tests/Elsa/Secrets/Tests/SecretTypeProviderTests.cs`
- [x] T016 Add built-in type providers in `src/Elsa/Secrets/Types/TextSecretTypeProvider.cs`, `RsaKeySecretTypeProvider.cs`, and `X509CertificateSecretTypeProvider.cs`
- [x] T017 Add in-memory repository test fixture in `tests/Elsa/Secrets/Tests/SecretTestFixture.cs`
- [x] T018 Add in-memory repository in `src/Elsa/Secrets/Services/InMemorySecretRepository.cs`

**Checkpoint**: Core package and default service registration compile and foundational tests fail/pass as implementation is added.

---

## Phase 3: User Story 1 - Manage Named Secrets (Priority: P1) MVP

**Goal**: Operators can create, list, inspect, update metadata, rotate, test, revoke, and delete secrets without value reveal.

**Independent Test**: Backend lifecycle tests and API tests prove the full management lifecycle returns only metadata and enforces safe state transitions.

### Tests for User Story 1

- [x] T019 [P] [US1] Add secret manager lifecycle tests in `tests/Elsa/Secrets/Tests/SecretManagerTests.cs`
- [ ] T020 [P] [US1] Add no-reveal mapper tests in `tests/Elsa/Secrets/Tests/SecretModelMapperTests.cs`
- [x] T021 [P] [US1] Add encrypted and configuration store tests in `tests/Elsa/Secrets/Tests/SecretStoreTests.cs`
- [ ] T022 [P] [US1] Add API endpoint tests for lifecycle routes in `tests/Elsa/Secrets/Tests/SecretsApiEndpointTests.cs`

### Implementation for User Story 1

- [x] T023 [US1] Implement `DefaultSecretManager` lifecycle behavior in `src/Elsa/Secrets/Services/DefaultSecretManager.cs`
- [x] T024 [US1] Implement metadata mapper in `src/Elsa/Secrets/Services/SecretModelMapper.cs`
- [x] T025 [US1] Implement stores in `src/Elsa/Secrets/Stores/EncryptedSecretStore.cs` and `src/Elsa/Secrets/Stores/ConfigurationSecretStore.cs`
- [x] T026 [US1] Add API request models in `src/Elsa/Secrets/Api/Requests/*.cs`
- [ ] T027 [US1] Add API handlers in `src/Elsa/Secrets/Api/Handlers/SecretRequestHandlers.cs`
- [x] T028 [US1] Add API endpoints in `src/Elsa/Secrets/Api/Endpoints/Secrets/List.cs`, `Get.cs`, `Create.cs`, `Update.cs`, `Rotate.cs`, `Revoke.cs`, `Delete.cs`, `Test.cs`, `Descriptors.cs`, and `Picker.cs`
- [x] T029 [US1] Add route constants and API feature registration in `src/Elsa/Secrets/Api/Constants/RouteConstants.cs` and `src/Elsa/Secrets/Api/Features/SecretsApiFeature.cs`

**Checkpoint**: User Story 1 is fully functional through backend services and API tests.

---

## Phase 4: User Story 2 - Use Secrets From Workflows And Modules (Priority: P1)

**Goal**: Runtime consumers resolve secret references at point of use, and workflow inputs can store references instead of literal values.

**Independent Test**: Secret expression tests prove a saved `SecretReference` resolves to the latest active value and fails safely when the secret is inactive or incompatible.

### Tests for User Story 2

- [x] T030 [P] [US2] Add resolver behavior tests in `tests/Elsa/Secrets/Tests/SecretManagerTests.cs`
- [ ] T031 [P] [US2] Add secret expression descriptor and handler tests in `tests/Elsa/Secrets/Tests/SecretExpressionTests.cs`
- [x] T032 [P] [US2] Add workflow input serialization safety tests in `tests/Elsa/Secrets/Tests/SecretReferenceSerializationTests.cs`

### Implementation for User Story 2

- [x] T033 [US2] Implement `DefaultSecretResolver` in `src/Elsa/Secrets/Services/DefaultSecretResolver.cs`
- [x] T034 [US2] Implement `SecretExpressionDescriptor` and `SecretExpressionHandler` in `src/Elsa/Secrets/Expressions/SecretExpressionDescriptor.cs` and `src/Elsa/Secrets/Expressions/SecretExpressionHandler.cs`
- [x] T035 [US2] Register the Secret expression descriptor through `src/Elsa/Secrets/Extensions/SecretsServiceCollectionExtensions.cs`
- [x] T036 [US2] Add expression and reference JSON coverage to `tests/Elsa/Secrets/Tests/SecretReferenceSerializationTests.cs`

**Checkpoint**: User Story 2 is independently testable through expression evaluation and serialized reference checks.

---

## Phase 5: User Story 3 - Choose Secret Types And Stores (Priority: P2)

**Goal**: Operators can choose compatible secret types and stores, with descriptors and validation preventing unsupported combinations.

**Independent Test**: Descriptor and compatibility tests prove built-in types/stores are discoverable and invalid type/store combinations fail before persistence.

### Tests for User Story 3

- [ ] T037 [P] [US3] Add descriptor API tests in `tests/Elsa/Secrets/Tests/SecretDescriptorEndpointTests.cs`
- [x] T038 [P] [US3] Add store/type compatibility tests in `tests/Elsa/Secrets/Tests/SecretTypeProviderTests.cs`
- [x] T039 [P] [US3] Add Groundwork manifest and repository tests in `tests/Elsa/Secrets/Tests/GroundworkSecretRepositoryTests.cs`

### Implementation for User Story 3

- [x] T040 [US3] Extend manager validation in `src/Elsa/Secrets/Services/DefaultSecretManager.cs` to enforce store/type capabilities before writes
- [x] T041 [US3] Add Groundwork storage manifest in `src/Elsa/Secrets/Persistence/Groundwork/SecretsStorageManifest.cs`
- [x] T042 [US3] Add Groundwork JSON settings in `src/Elsa/Secrets/Persistence/Groundwork/SecretsGroundworkJson.cs`
- [x] T043 [US3] Implement Groundwork repository in `src/Elsa/Secrets/Persistence/Groundwork/Stores/GroundworkSecretRepository.cs`
- [x] T044 [US3] Add Groundwork registration and feature in `src/Elsa/Secrets/Persistence/Groundwork/DependencyInjection/GroundworkSecretsStoreRegistration.cs` and `src/Elsa/Secrets/Persistence/Groundwork/SecretsGroundworkPersistenceFeature.cs`

**Checkpoint**: User Story 3 is independently testable through descriptors, compatibility checks, and durable repository tests.

---

## Phase 6: User Story 4 - Govern And Audit Secret Operations (Priority: P3)

**Goal**: Permission names and audit records exist for secret operations, and privileged operations emit audit-ready records without values.

**Independent Test**: Tests prove lifecycle, resolution, and test operations emit safe audit records and permission constants are stable.

### Tests for User Story 4

- [x] T045 [P] [US4] Add permission constant tests in `tests/Elsa/Secrets/Tests/SecretsPermissionsTests.cs`
- [x] T046 [P] [US4] Add audit sink tests in `tests/Elsa/Secrets/Tests/SecretAuditTests.cs`

### Implementation for User Story 4

- [x] T047 [US4] Add permission constants in `src/Elsa/Secrets/Core/Permissions/SecretsPermissions.cs`
- [x] T048 [US4] Emit audit records from `src/Elsa/Secrets/Services/DefaultSecretManager.cs` and `src/Elsa/Secrets/Services/DefaultSecretResolver.cs`
- [x] T049 [US4] Annotate or structure API endpoints in `src/Elsa/Secrets/Api/Endpoints/Secrets/*.cs` for later permission enforcement while preserving local anonymous development behavior

**Checkpoint**: User Story 4 is independently testable through safe audit records and permission constants.

---

## Phase 7: Studio Secrets Module

**Goal**: Foundation Studio can manage secrets and contribute a reusable secret picker.

**Independent Test**: Studio .NET registration tests and Vitest client tests prove the module contributes its manifest, renders management/picker states, calls API contracts, and serializes secret references without raw values.

### Tests

- [x] T050 [P] Add Studio module manifest registration test in `/Users/sipke/Projects/Elsa/elsa-foundation-studio/tests/Elsa.Studio.Tests/SecretsStudioModuleTests.cs`
- [x] T051 [P] Add Studio API client tests in `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Secrets/Client/src/__tests__/secretsApi.test.ts`
- [x] T052 [P] Add Studio module registration tests in `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Secrets/Client/src/__tests__/module.test.tsx`
- [x] T053 [P] Add Studio picker serialization tests in `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Secrets/Client/src/__tests__/secretPicker.test.tsx`
- [x] T054 [P] Add Studio no-reveal rendering tests in `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Secrets/Client/src/__tests__/secretsPage.test.tsx`

### Implementation

- [x] T055 Add Studio service registration in `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Secrets/SecretsStudioFeature.cs`, `SecretsStudioServiceCollectionExtensions.cs`, and `Handlers/ContributeSecretsStudioModule.cs`
- [x] T056 Add Studio SDK type augmentation in `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Secrets/Client/src/studio-sdk.d.ts`
- [x] T057 Add Studio API adapter and types in `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Secrets/Client/src/secretsApi.ts` and `secretTypes.ts`
- [x] T058 Add Studio module registration, feature area, routes, and property editor contribution in `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Secrets/Client/src/module.tsx`
- [x] T059 Add Studio secrets management UI in `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Secrets/Client/src/SecretsPage.tsx` and `SecretDetail.tsx`
- [x] T060 Add Studio create/rotate/picker UI in `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Secrets/Client/src/SecretDialogs.tsx` and `SecretPickerEditor.tsx`
- [x] T061 Add Studio module styles in `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Secrets/Client/src/styles.css`

**Checkpoint**: Studio route and picker are independently testable through client tests and module manifest tests.

---

## Phase 8: Host Composition, Documentation, And Polish

**Purpose**: Wire the feature into local host composition, update extension-point docs/maps, and run full validation.

- [x] T062 Add backend host feature references and local shell composition entries in `src/Apps/Elsa.Server/Elsa.Server.csproj` and `src/Apps/Elsa.Server/appsettings*.json` or shell configuration files as appropriate
- [x] T063 Add Studio host references/shell composition entries in `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Web/Elsa.Studio.Web.csproj` and `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Web/shells.json` without overwriting unrelated local changes
- [x] T064 Add extension-point documentation in `src/Elsa/Secrets/Core/EXTENSION_POINTS.md` and `src/Elsa/Secrets/EXTENSION_POINTS.md`
- [x] T065 Refresh generated maps with `bash tools/maps/generate-extension-point-map.sh` and `bash tools/maps/generate-feature-dependency-map.sh`
- [x] T066 Run backend validation with `dotnet build Elsa.Server.slnx` and `dotnet test tests/Elsa/Secrets/Tests/Elsa.Secrets.Tests.csproj`
- [x] T067 Run Studio validation with `dotnet test /Users/sipke/Projects/Elsa/elsa-foundation-studio/tests/Elsa.Studio.Tests/Elsa.Studio.Tests.csproj`, `pnpm --dir /Users/sipke/Projects/Elsa/elsa-foundation-studio --filter @elsa-workflows/studio-secrets test`, and `pnpm --dir /Users/sipke/Projects/Elsa/elsa-foundation-studio --filter @elsa-workflows/studio-secrets build`
- [x] T068 Run no-reveal safety scan for submitted fixture values across backend and Studio test outputs
- [x] T069 Run self-review, address all actionable findings, and repeat validation until clean
- [ ] T070 Commit completed backend and Studio work with coherent messages, push according to the selected Git operating model, open a PR, wait for checks, and merge after approval/check success

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on setup and blocks all user stories.
- **User Stories 1 and 2 (P1)**: Depend on foundational contracts. They can proceed in parallel after T007-T018, but implementation should keep manager/store and resolver/expression tests isolated.
- **User Story 3 (P2)**: Depends on foundational contracts and benefits from User Story 1 manager behavior.
- **User Story 4 (P3)**: Depends on manager/resolver operations from User Stories 1 and 2.
- **Studio (Phase 7)**: Depends on API and contract shapes from User Stories 1-3.
- **Polish (Phase 8)**: Depends on desired backend and Studio stories.

### User Story Dependencies

- **US1**: Can start after Phase 2; delivers backend MVP.
- **US2**: Can start after Phase 2; uses manager/resolver contracts and is independently testable with in-memory repository.
- **US3**: Starts after US1 validation surfaces are stable.
- **US4**: Starts after manager/resolver lifecycle points exist.

### Parallel Opportunities

- T007-T010 can run in parallel.
- T011-T012 and T015-T017 can run before implementations, in parallel by file.
- T019-T022 can run in parallel.
- T030-T032 can run in parallel.
- T037-T039 can run in parallel.
- T050-T054 can run in parallel.

## Implementation Strategy

### MVP First

1. Complete setup and foundational tasks.
2. Complete US1 backend lifecycle/API.
3. Complete US2 resolver/expression.
4. Validate backend independently before Studio.

### Incremental Delivery

1. Backend lifecycle and no-reveal metadata.
2. Runtime references and expression resolution.
3. Type/store descriptors and Groundwork persistence.
4. Audit/permission contracts.
5. Studio management and picker.
6. Host composition and full validation.

## Notes

- Mark each task `[x]` when completed.
- Keep personal `.agent-prefs/*.md` uncommitted.
- Do not overwrite unrelated local changes in `/Users/sipke/Projects/Elsa/elsa-foundation-studio/src/Elsa.Studio.Web/shells.json`.
