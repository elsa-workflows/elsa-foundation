# Tasks: Elsa Foundation Auth System

**Input**: Design documents from `/specs/002-auth-system/`

**Prerequisites**: spec.md, plan.md, research.md, data-model.md, adr/0001-auth-architecture.md, contracts/, quickstart.md

**Tests**: Include contract/integration tests per provider profile and Studio SDK behavior.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing. Backend tasks target `elsa-foundation`; Studio tasks target `elsa-foundation-studio`.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish the abstractions package and capability/ownership scaffolding everything else depends on.

- [ ] T001 Create `Elsa.Foundation.Identity.Abstractions` project with Authentication/Iam/Authorization/Ownership folders
- [ ] T002 [P] Define `OwnershipMode`, `ProviderCapabilities`, and effective-capability computation
- [ ] T003 [P] Define the shared `Permission` catalog contract (`IPermissionCatalog`) and namespaced default keys
- [ ] T004 [P] Add Studio auth SDK workspace under `src/Elsa.Studio.Web/Client/src/auth/`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Provider-agnostic contracts and the normalization pipeline that all providers and stories build on.

**CRITICAL**: No provider or management implementation may bypass these seams.

- [ ] T005 Define `IAuthenticationProviderModule`, `IAuthenticationProviderManager`, `IPrincipalFactory`, `ITokenService`
- [ ] T006 Define IAM stores (`IUserStore`, `IRoleStore`, `IApplicationStore`, `ICredentialStore`, `IExternalIdentityStore`, `IClaimMappingStore`, `IProviderConfigurationStore`, `ITenantMembershipStore`)
- [ ] T007 Define IAM managers (`IUserManager`, `IRoleManager`, `IApplicationManager`, `ICredentialManager`, `IProviderManager`, `IClaimMappingManager`) with invariants
- [ ] T008 Define `IPermissionEvaluator`, `RequirePermission` policy provider, and resource-handler hook
- [ ] T009 Implement claims normalization pipeline (`ClaimMappingRule` evaluation → normalized role/permission claims)
- [ ] T010 Define `IOwnershipModeProvider` and effective-capabilities resolution
- [ ] T011 [P] Add `AuthEvent` audit abstraction and security-default guards (key/HTTPS/secret-hash checks)
- [ ] T012 Add contract tests for module composition, policy registration, and capability computation

**Checkpoint**: Contracts compile and conformance tests pass without any concrete provider.

---

## Phase 3: User Story 1 - Provider-Agnostic Sign-In (Priority: P1) MVP

**Goal**: A user signs in through the configured provider and the app receives a normalized principal regardless of provider.

**Independent Test**: Switch between the external OIDC provider and the OpenIddict/local provider with no change to feature modules or Studio screens; both yield a normalized `AuthSession`.

- [ ] T013 [P] [US1] Implement external OIDC client provider (`Identity.Oidc`) using ASP.NET Core OIDC/JWT handlers
- [ ] T014 [P] [US1] Implement OpenIddict reference provider (`Identity.OpenIddict`) for first-party token issuance
- [ ] T015 [US1] Implement ASP.NET Core Identity substrate (`Identity.AspNetCoreIdentity`) for local users/roles
- [ ] T016 [US1] Wire `IPrincipalFactory` to resolve/link `User` + `ExternalIdentity` on first sign-in
- [ ] T017 [P] [US1] Implement Studio `AuthProvider`, `useAuthSession`, OIDC adapter, and redirect-callback handling
- [ ] T018 [P] [US1] Implement HTTP + SignalR token injection with transparent refresh
- [ ] T019 [US1] Add integration tests: OIDC and OpenIddict sign-in both produce normalized principal/session

**Checkpoint**: Sign-in works end-to-end under at least two providers behind one contract.

---

## Phase 4: User Story 2 - Managing Users, Roles, and Permissions (Priority: P2)

**Goal**: Administrators manage users, roles, and permission assignments; effective permissions resolve consistently.

**Independent Test**: Create a role with permissions, assign to a user, and confirm the user's `EffectivePermissionSet` reflects it.

- [ ] T020 [P] [US2] Implement users management endpoints (`/_elsa/identity/users*`) with lockout safeguards
- [ ] T021 [P] [US2] Implement roles/permissions endpoints incl. role permission preview
- [ ] T022 [US2] Implement `GET /_elsa/identity/capabilities` and `capabilityDisabled` semantics
- [ ] T023 [P] [US2] Build Studio users/roles management screens consuming `useAuthCapabilities`
- [ ] T024 [US2] Add tests: effective-permission resolution and capability-gated write behavior

**Checkpoint**: Roles/permissions manageable in foundation-owned mode; gated correctly in other modes.

---

## Phase 5: User Story 3 - Managing Applications, Clients, and API Keys (Priority: P3)

**Goal**: Register applications/clients and issue/rotate/revoke credentials with audit.

**Independent Test**: Create an application, issue an API key (plaintext once), rotate with a grace window, confirm old key invalidates per policy.

- [ ] T025 [P] [US3] Implement applications/scopes endpoints
- [ ] T026 [US3] Implement credentials issue/rotate/revoke with hashing-at-rest and grace windows
- [ ] T027 [P] [US3] Build Studio applications + credentials screens (show-once secret handling)
- [ ] T028 [US3] Add tests: rotation grace behavior, secret never returned after creation, audit emitted

---

## Phase 6: User Story 4 - Configuring and Federating OIDC Providers (Priority: P4)

**Goal**: Add/configure external providers and map their claims/groups to roles/permissions.

**Independent Test**: Configure a provider, define a group→role mapping, sign in a federated user, confirm mapped roles/permissions apply.

- [ ] T029 [P] [US4] Implement providers endpoints incl. settings validation and `test` connectivity check
- [ ] T030 [US4] Implement claim/role mapping endpoints and ordered rule evaluation
- [ ] T031 [P] [US4] Build Studio providers + mappings screens with write-only secret fields
- [ ] T032 [US4] Add tests: provider settings validation, federated sign-in applies mapped grants

---

## Phase 7: User Story 5 - Consistent Authorization Across Backend and Studio (Priority: P5)

**Goal**: The same permission model governs backend enforcement and Studio presentation; backend is the source of truth.

**Independent Test**: A user lacking a permission cannot perform the backend action even if the Studio control is forced; Studio hides/disables it by default.

- [ ] T033 [US5] Apply `RequirePermission` policies across foundation management + feature endpoints
- [ ] T034 [P] [US5] Implement Studio `usePermissions`, `AuthGuard`, `RequireAuth`
- [ ] T035 [US5] Document permission-propagation timing per provider (immediate vs refresh)
- [ ] T036 [US5] Add tests: backend denies despite forced UI; permission claims match evaluator

---

## Phase 8: Compatibility & Migration

**Purpose**: Provide a supported path off legacy Elsa Identity / Studio Login without a hard cutover.

- [ ] T037 [P] Implement `Identity.Legacy` compatibility adapter over existing Elsa Identity stores
- [ ] T038 [P] Implement Studio legacy adapter (local-credential login via adapter only; no `Login` template)
- [ ] T039 Implement optional import tooling for legacy users/roles/api-keys + external identity links
- [ ] T040 Write profile migration playbooks (A↔B↔C↔D) with feature-flag staging and rollback

---

## Dependencies

- Phase 1 → Phase 2 → all user stories.
- US1 (sign-in) precedes US2–US5 (they assume a normalized principal).
- US5 depends on the permission catalog (T003/T008) and at least US2 roles.
- Migration (Phase 8) depends on contracts (Phase 2) and at least one provider (US1).

## Parallelization Notes

- `[P]` tasks within a phase touch independent files/projects and may run concurrently.
- Backend provider modules (T013/T014) and Studio SDK (T017/T018) can proceed in parallel once contracts exist.
