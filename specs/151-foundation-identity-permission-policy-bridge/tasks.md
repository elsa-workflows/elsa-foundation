---

description: "Dependency-ordered implementation tasks for the Foundation Identity permission policy bridge"
---

# Tasks: Foundation Identity Permission Policy Bridge

**Input**: Design documents in `specs/151-foundation-identity-permission-policy-bridge/`

**Prerequisites**: `spec.md`, `plan.md`, `research.md`, `data-model.md`, `contracts/`, and `quickstart.md`

**Test discipline**: Every behavior task starts with a failing test and records the expected failure before production code is changed. Tasks marked `[P]` touch separate files and may run in parallel only after their phase dependencies are satisfied.

**Program scope**: Delivery issue #1344 under program #1342. Transport-adjacent activity authorization remains deferred to #1356; full endpoint/catalog inventory remains #1346.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Safe to execute concurrently because the task has no unresolved dependency or file overlap.
- **[US1]**: One authorization outcome across endpoint styles.
- **[US2]**: Explicit permission composition and resource precedence.
- **[US3]**: Auditable module permission ownership and normalized provider claims.

---

## Phase 1: Setup and dependency boundaries

**Purpose**: Make the planned standard ASP.NET Core and test dependencies explicit without adding a framework reference or a new Elsa project.

- [ ] T001 Add `Microsoft.AspNetCore.Authorization.Policy` and full `Microsoft.AspNetCore.Http` package references to `src/Elsa/Foundation/Identity/Abstractions/Elsa.Foundation.Identity.Abstractions.csproj`, add the one-way `Elsa.Foundation.Identity.Abstractions` project reference to `src/Elsa/Api/FastEndpoints/Elsa.Api.FastEndpoints.csproj`, and add the existing cataloged `Microsoft.CodeAnalysis.CSharp` package to `tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj`.
- [ ] T002 Run `dotnet list src/Elsa/Api/FastEndpoints/Elsa.Api.FastEndpoints.csproj reference`, `dotnet list src/Elsa/Foundation/Identity/Abstractions/Elsa.Foundation.Identity.Abstractions.csproj reference`, and `dotnet list src/Elsa/Foundation/Identity/Abstractions/Elsa.Foundation.Identity.Abstractions.csproj package --include-transitive`; record that Identity Abstractions has no FastEndpoints or other Elsa project reference.

---

## Phase 2: Foundational policy identity and registration contracts

**Purpose**: Establish deterministic policy identity and fail-closed DI ownership before endpoint or evaluator work begins.

**Critical**: All tests in this phase must fail for the intended missing behavior before their implementations are added. This phase blocks every user story.

### Failing tests

- [ ] T003 Add failing canonical-key and v1 single/any/all codec fixtures, Unicode-equivalence cases, duplicate sorting, wildcard validation, strict malformed-v1 rejection (including mixed-case namespace/version variants that must not fall back to legacy), legacy parse-only aliases, and unrelated-policy parse results in `tests/Elsa/Foundation/Identity/Tests/AuthorizationContractsTests.cs`.
- [ ] T004 Add failing provider fixtures for canonical and legacy policy resolution, `RequireAuthenticatedUser`, normalized-principal requirements, malformed reserved policies (including mixed-case namespace/version variants with zero legacy fallback calls), and preserved host named/default/fallback policies in `tests/Elsa/Foundation/Identity/Tests/AuthorizationContractsTests.cs`.
- [ ] T005 Add failing metadata tests for `RequirePermission`, `RequireAnyPermission`, and `RequireAllPermissions`, including invalid/empty/whitespace input and same-builder return behavior, in `tests/Elsa/Foundation/Identity/Tests/AuthorizationContractsTests.cs`.
- [ ] T006 Add failing symmetric registration tests for default, explicit replacement before/after Foundation registration, direct untagged registration before/after, remove-then-direct, zero, multiple, marker mismatch, and repeated registration for evaluator/formatter/catalog in `tests/Elsa/Foundation/Identity/Tests/ReplacementContractRegistrationTests.cs`.
- [ ] T007 Add failing result-handler registration tests for zero/one/multiple pre-existing implementation-type, factory, and instance descriptors; repeated Foundation registration; host-after conflict; named diagnostics; and captured/default result behavior for challenge, forbid, success, and unrelated-policy delegation in `tests/Elsa/Foundation/Identity/Tests/ReplacementContractRegistrationTests.cs`.

### Implementation

- [ ] T008 Implement canonical permission keys, `PermissionRequirementMode`, immutable policy descriptors, parse results, v1 codec, strict reserved-namespace handling, and compatible legacy single parsing in `src/Elsa/Foundation/Identity/Abstractions/Authorization/PermissionPolicyCodec.cs` and `src/Elsa/Foundation/Identity/Abstractions/Authorization/AuthorizationContracts.cs` to satisfy T003.
- [ ] T009 Implement compatible single and new composite authorization requirements plus canonical policy construction/delegation in `src/Elsa/Foundation/Identity/Abstractions/Authorization/AuthorizationContracts.cs` to satisfy T004 without changing the public single requirement constructor/property.
- [ ] T010 Implement the three standard `IEndpointConventionBuilder` extensions using one `AuthorizeAttribute` policy metadata item in `src/Elsa/Foundation/Identity/Abstractions/Authorization/PermissionEndpointConventionBuilderExtensions.cs` to satisfy T005.
- [ ] T011 Implement tagged defaults, `ReplacePermissionEvaluator<T>`, `ReplacePermissionPolicyNameFormatter<T>`, `ReplacePermissionCatalog<T>`, immediate pre-default checks, startup descriptor/marker validation, and named diagnostics in `src/Elsa/Foundation/Identity/Abstractions/Extensions/FoundationIdentityServiceCollectionExtensions.cs` to satisfy T006.
- [ ] T012 Implement descriptor-based, non-recursive, idempotent host/default result-handler fallback capture and tagged Foundation wrapper registration in `src/Elsa/Foundation/Identity/Abstractions/Authorization/PermissionAuthorizationMiddlewareResultHandler.cs` and `src/Elsa/Foundation/Identity/Abstractions/Extensions/FoundationIdentityServiceCollectionExtensions.cs` to satisfy T007.
- [ ] T013 Run `dotnet test tests/Elsa/Foundation/Identity/Tests/Elsa.Foundation.Identity.Tests.csproj --no-restore` and confirm all Phase 2 tests pass before story work starts.

**Checkpoint**: Canonical policy identity, provider ownership, standard endpoint metadata, and replacement/result-handler registration contracts are stable.

---

## Phase 3: User Story 2 — Explicit permission composition and resource precedence (P1)

**Goal**: Make single/any/all, implication, wildcard, resource denial, cancellation, and operational-failure semantics unambiguous in one shared handler.

**Independent Test**: Direct policy authorization covers every member/composition outcome and proves no later handler/member/evaluator can turn denial or an operational failure into a grant.

### Failing tests

- [ ] T014 [US2] Add failing exact, canonical-equivalence, transitive/directional/cycle-safe implication, wildcard-grant, wildcard-request, and malformed catalog implication tests in `tests/Elsa/Foundation/Identity/Tests/AuthorizationContractsTests.cs`.
- [ ] T015 [US2] Add failing single/any/all tests for duplicate members, deterministic member order, evaluator call counts, any short-circuit, and all completeness in `tests/Elsa/Foundation/Identity/Tests/AuthorizationContractsTests.cs`.
- [ ] T016 [US2] Add failing resource grant/deny/abstain tests for member-local hard denial, denial after grant, evaluator-only-after-unanimous-abstention, any-member isolation, and all-member failure in `tests/Elsa/Foundation/Identity/Tests/AuthorizationContractsTests.cs`.
- [ ] T017 [US2] Add failing resource exception, resource `TimeoutException`, evaluator exception, evaluator `TimeoutException`, and throw-first/grant-second tests that assert propagation and zero later calls in `tests/Elsa/Foundation/Identity/Tests/AuthorizationContractsTests.cs`.
- [ ] T018 [US2] Add failing cancellation tests for `HttpContext` resource lookup, `IHttpContextAccessor` fallback with a domain resource, original-resource preservation, context-property/method-token equality, no-active-context `CancellationToken.None`, and `OperationCanceledException` propagation in `tests/Elsa/Foundation/Identity/Tests/AuthorizationContractsTests.cs`.

### Implementation

- [ ] T019 [US2] Canonicalize catalog lookup/implication traversal, enforce grant-side wildcard rules, reject wildcard catalog definitions/targets, and retain presentation spelling in `src/Elsa/Foundation/Identity/Abstractions/Authorization/AuthorizationContracts.cs` to satisfy T014.
- [ ] T020 [US2] Implement one shared per-member evaluation path and deterministic single/any/all composition in `src/Elsa/Foundation/Identity/Abstractions/Authorization/AuthorizationContracts.cs` to satisfy T015.
- [ ] T021 [US2] Implement deterministic resource-source aggregation with member-local deny veto, grant-after-no-deny, evaluator-after-unanimous-abstention, and operational-failure short-circuiting in `src/Elsa/Foundation/Identity/Abstractions/Authorization/AuthorizationContracts.cs` to satisfy T016–T017.
- [ ] T022 [US2] Add the source-compatible `PermissionEvaluationContext.CancellationToken` init property, register `IHttpContextAccessor` with `AddHttpContextAccessor`, resolve `RequestAborted` from resource/accessor without replacing the protected resource, and pass the identical token to all calls in `src/Elsa/Foundation/Identity/Abstractions/Authorization/AuthorizationContracts.cs` and `src/Elsa/Foundation/Identity/Abstractions/Extensions/FoundationIdentityServiceCollectionExtensions.cs` to satisfy T018.
- [ ] T023 [US2] Run `dotnet test tests/Elsa/Foundation/Identity/Tests/Elsa.Foundation.Identity.Tests.csproj --no-restore`; confirm User Story 2 passes independently, including deliberate negative mutations for denial precedence and operational-failure fall-through in `tests/Elsa/Foundation/Identity/Tests/AuthorizationContractsTests.cs`.

**Checkpoint**: Shared policy evaluation is deterministic, cancellable, and fail-closed without endpoint-framework involvement.

---

## Phase 4: User Story 1 — One authorization outcome across endpoint styles (P1)

**Goal**: Route Minimal API and all six transitional Elsa FastEndpoints bases through the same Foundation Identity policy/evaluator path with identical HTTP outcomes.

**Independent Test**: One serialized host exposes Minimal API and FastEndpoints single/any/all routes and produces identical 401/403/200 and failure behavior for the fixed contract matrix.

### Failing tests

- [ ] T024 [P] [US1] Add failing normalized-principal validator tests for exact marker/type matching, unregistered runtime type, forged marker/raw permission, marker cardinality, zero/multiple trusted identities, tenant/provider grant-union prevention, and trusted-plus-untrusted principal filtering in `tests/Elsa/Foundation/Identity/Tests/ClaimsNormalizationTests.cs`.
- [ ] T025 [P] [US1] Add failing 401/403 result tests for anonymous, authenticated untrusted/unmarked/ambiguous principals, both unauthenticated/authenticated identity orderings, trusted denial, success, authentication-before-routing, and all four challenge/forbid/success/unrelated-policy paths against both the captured custom handler and ASP.NET Core default in `tests/Elsa/Foundation/Identity/Tests/ReplacementContractRegistrationTests.cs`.
- [ ] T026 [P] [US1] Add failing helper tests for no-action wildcard and wildcard-plus-actions OR policy composition in `tests/Elsa/Api/FastEndpoints/Tests/ElsaEndpointPermissionsTests.cs`.
- [ ] T027 [P] [US1] Add a failing serialized six-route Minimal API/FastEndpoints parity host and the full outcome matrix from `contracts/endpoint-adapter-contract.md` in `tests/Elsa/Foundation/Identity/Tests/Api/PermissionEndpointAdapterIntegrationTests.cs`, including an authentication handler whose normalizer exception returns authentication failure/HTTP 401 with no marked ticket and zero authorization calls; use `FastEndpointsHostCollection` and exact discovery assemblies.
- [ ] T028 [P] [US1] Add a failing Roslyn symbol/data-flow architecture guard and separate mutations for `.Permissions`, `.PermissionsAll`, `FindFirst`, `FindFirstValue`, `FindAll`, `HasClaim`, `Claims.Any`, aliases, and provider-specific permission constants in `tests/Elsa/Architecture/PermissionAuthorizationBoundaryTests.cs`, with only the documented symbol/path allowlist.

### Implementation

- [ ] T029 [US1] Add `IdentityClaimTypes.Normalized`, `FoundationIdentityOptions.NormalizedAuthenticationTypes`, `AddNormalizedAuthenticationType`, and an internal exactly-one-identity validator that returns a principal containing only the selected trusted identity in `src/Elsa/Foundation/Identity/Abstractions/FoundationIdentityOptions.cs`, `src/Elsa/Foundation/Identity/Abstractions/Authorization/NormalizedPrincipalValidator.cs`, and `src/Elsa/Foundation/Identity/Abstractions/Extensions/FoundationIdentityServiceCollectionExtensions.cs` to satisfy T024.
- [ ] T030 [US1] Add the normalized-principal requirement to every generated permission policy, refuse untrusted/unmarked/ambiguous principals before grant evaluation, and rewrite only those authenticated failures to a challenge by scanning every identity in `src/Elsa/Foundation/Identity/Abstractions/Authorization/AuthorizationContracts.cs` and `src/Elsa/Foundation/Identity/Abstractions/Authorization/PermissionAuthorizationMiddlewareResultHandler.cs` to satisfy T025.
- [ ] T031 [US1] Implement canonical FastEndpoints policy composition while preserving `ElsaEndpointPermissions.Compose` and `ConfigurePermissions(params string[])` in `src/Elsa/Api/FastEndpoints/Abstractions/ElsaEndpointPermissions.cs` to satisfy T026.
- [ ] T032 [US1] Replace FastEndpoints claim matching with exactly one `Policies(...)` call in `src/Elsa/Api/FastEndpoints/Abstractions/ElsaEndpoint.TRequest.TResponse.TMapper.cs`, `src/Elsa/Api/FastEndpoints/Abstractions/ElsaEndpoint.TRequest.TResponse.cs`, `src/Elsa/Api/FastEndpoints/Abstractions/ElsaEndpoint.TRequest.cs`, `src/Elsa/Api/FastEndpoints/Abstractions/ElsaEndpointWithoutRequest.TResponse.cs`, `src/Elsa/Api/FastEndpoints/Abstractions/ElsaEndpointWithoutRequest.cs`, and `src/Elsa/Api/FastEndpoints/Abstractions/ElsaEndpointWithMapper.cs`, leaving every route/action declaration unchanged.
- [ ] T033 [US1] Complete the test authentication schemes, adapter-boundary `IClaimsNormalizer` exception-to-`AuthenticateResult.Fail` mapping, trusted/untrusted principals, no-marked-ticket assertion, domain resources, call counters, cancellation endpoints, and exact FastEndpoints discovery filter needed by T027 in `tests/Elsa/Foundation/Identity/Tests/Api/PermissionEndpointAdapterIntegrationTests.cs`, without adding a second production policy provider or changing `src/Elsa/Foundation/Identity/Api/Configurators/IdentityClaimTypeFastEndpointsConfigurator.cs` for third-party routes.
- [ ] T034 [US1] Implement the Roslyn guard's symbol resolution, authorization-decision data-flow rules, scoped allowlist, and mutation harness in `tests/Elsa/Architecture/PermissionAuthorizationBoundaryTests.cs` to satisfy T028; record the negative mutation bite-proof.
- [ ] T035 [US1] Run `dotnet test tests/Elsa/Foundation/Identity/Tests/Elsa.Foundation.Identity.Tests.csproj --no-restore`, `dotnet test tests/Elsa/Api/FastEndpoints/Tests/Elsa.Api.FastEndpoints.Tests.csproj --no-restore`, and `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore`; confirm the complete adapter matrix, unchanged route/action behavior, and architecture mutations pass independently.

**Checkpoint**: Minimal API and transitional FastEndpoints routes have one policy owner and indistinguishable permission outcomes.

---

## Phase 5: User Story 3 — Auditable module permission ownership and normalized providers (P2)

**Goal**: Prove trusted first-party claim projection and immutable, module-owned permission catalog lifecycle with Studio Preferences and Module Management canaries.

**Independent Test**: Cookie/bearer/external-factory principals authorize only after trusted projection, and successive service providers expose only active uniquely owned canary permissions.

### Failing tests

- [ ] T036 [P] [US3] Add failing strip-map-mark tests for forged incoming internal claims, exact `v1` marker cardinality, provider/tenant rule filtering, rule ordering/stop behavior, and preserved presentation spelling in `tests/Elsa/Foundation/Identity/Tests/ClaimsNormalizationTests.cs`.
- [ ] T037 [P] [US3] Add failing external-principal-factory and reconstructed-cookie runtime `AuthenticationType`/marker authorization tests in `tests/Elsa/Foundation/Identity/Tests/IdentityProviderModuleTests.cs` and `tests/Elsa/Foundation/Identity/Tests/AspNetCoreIdentity/AspNetCoreIdentitySignInTests.cs`; inject a throwing real `IClaimsNormalizer` into `AspNetCoreIdentityPrincipalFactory` and assert the production factory propagates before returning any principal/marker.
- [ ] T038 [P] [US3] Add failing issued-token and validated-bearer runtime `AuthenticationType`/marker authorization tests, including proof that the `"openiddict"` construction type is not trusted, in `tests/Elsa/Foundation/Identity/Tests/OpenIddict/OpenIddictTokenServiceTests.cs` and `tests/Elsa/Foundation/Identity/Tests/OpenIddict/OpenIddictBearerAuthenticationTests.cs`.
- [ ] T039 [US3] Add failing catalog provenance tests for stable owner/contributor values, canonical duplicate diagnostics naming both owners, reserved wildcard rejection, and Studio Preferences/Module Management canaries in `tests/Elsa/Modularity/Tests/PermissionCatalogOwnershipLifecycleTests.cs`.
- [ ] T040 [US3] Add failing successive-provider lifecycle tests for enable, disable, unload, re-enable, and replacement without stale canary entries in `tests/Elsa/Modularity/Tests/PermissionCatalogOwnershipLifecycleTests.cs`.

### Implementation

- [ ] T041 [US3] Strip incoming normalized markers and emit exactly one trusted marker only after successful normalization/projection in `src/Elsa/Foundation/Identity/Abstractions/Authorization/AuthorizationContracts.cs`, `src/Elsa/Foundation/Identity/AspNetCoreIdentity/Services/AspNetCoreIdentityPrincipalFactory.cs`, `src/Elsa/Foundation/Identity/AspNetCoreIdentity/Services/AspNetCoreIdentityUserClaimsPrincipalFactory.cs`, and `src/Elsa/Foundation/Identity/AspNetCoreIdentity/Services/IdentityClaimsProjector.cs`; preserve the real factory's exception propagation so no principal/marker can be returned on normalization failure, satisfying T036–T037 without adding an unused request-authentication handler.
- [ ] T042 [US3] Register exact external-factory/cookie runtime authentication types in `src/Elsa/Foundation/Identity/AspNetCoreIdentity/Extensions/AspNetCoreIdentityServiceCollectionExtensions.cs` to satisfy T037.
- [ ] T043 [US3] Emit the marker into signed tokens and register only the validated bearer runtime authentication type in `src/Elsa/Foundation/Identity/OpenIddict/Behavior/OpenIddictTokenService.cs` and `src/Elsa/Foundation/Identity/OpenIddict/Behavior/Extensions/OpenIddictBehaviorServiceCollectionExtensions.cs` to satisfy T038.
- [ ] T044 [US3] Add source-compatible non-positional `OwnerId` and `ContributorType` properties, stable contributor owner defaults, canonical indexing, reserved-wildcard validation, and two-owner diagnostics in `src/Elsa/Foundation/Identity/Abstractions/Authorization/AuthorizationContracts.cs` to satisfy T039.
- [ ] T045 [P] [US3] Declare stable explicit owners for Module Management and Studio Preferences in `src/Elsa/Modularity/Api/Authorization/ModuleManagementPermissions.cs` and `src/Elsa/Studio/Preferences/Api/StudioPreferencesPermissions.cs`.
- [ ] T046 [US3] Keep `CompositePermissionCatalog` an immutable service-provider snapshot and complete successive-provider lifecycle behavior in `src/Elsa/Foundation/Identity/Abstractions/Authorization/AuthorizationContracts.cs` to satisfy T040.
- [ ] T047 [US3] Run `dotnet test tests/Elsa/Foundation/Identity/Tests/Elsa.Foundation.Identity.Tests.csproj --no-restore` and `dotnet test tests/Elsa/Modularity/Tests/Elsa.Modularity.Tests.csproj --no-restore`; confirm User Story 3 passes independently and do not add the program-wide endpoint/catalog inventory owned by #1346.

**Checkpoint**: Provider trust is explicit and permission ownership/lifecycle is auditable for the two bridge canaries.

---

## Phase 6: Documentation, compatibility, and repository gates

**Purpose**: Document the public seams, prove no contract drift, and produce merge evidence.

- [ ] T048 [P] Document endpoint metadata, legacy-policy window, trusted runtime authentication types, replacement/fan-in/result-handler registration, failure/cancellation semantics, and catalog provenance in `src/Elsa/Foundation/Identity/Abstractions/EXTENSION_POINTS.md` and `docs/reference/authentication-architecture.md`.
- [ ] T049 [P] Document the FastEndpoints adapter as transitional and policy-only, including wildcard/action OR compatibility and the prohibition on direct claim matching, in `src/Elsa/Api/FastEndpoints/EXTENSION_POINTS.md`.
- [ ] T050 Update #1344 progress/evidence and the delivery chain in `docs/program-goals/first-party-rest-api-consolidation.md` without absorbing #1346 or #1356 scope.
- [ ] T051 Run all focused builds/tests in `quickstart.md`, including the ASP.NET Core Identity cookie and OpenIddict bearer suites touched by runtime-type evidence; fix only failures attributable to this slice.
- [ ] T052 Generate the container-free `Elsa.Server.test.slnf`, run the full Release build and test filter, run `dotnet run --project tools/maps/Elsa.Maps.Generator -- check`, and run `git diff --check`.
- [ ] T053 If the map check is stale because of the new dependency, run the authorized full map refresh, review the generated findings, and stage every changed map plus `docs/maps/manifest.json` when changed.
- [ ] T054 Re-run the Roslyn mutation bite-proof and a denial/operational-failure mutation; inspect the full diff for route, method, payload, OpenAPI, discovery, public-constructor, and unrelated-policy drift.
- [ ] T055 Run `gh issue view 1344 --repo elsa-workflows/elsa-foundation --comments` and `gh pr list --repo elsa-workflows/elsa-foundation --state open --search "1344"` to re-check competing work, inspect `git status --short`, stage every reviewed changed path explicitly, and commit with `git commit -m "Unify Foundation permission authorization policies"`.
- [ ] T056 Push `1305-permission-policy-bridge`, open or update the #1344 pull request, post focused/full/maps/diff/mutation evidence as a PR comment, move #1344/project status to Review, resolve review and CI findings, merge only on a green gate, move #1344/project status to Done, release the worktree claim, and verify the post-merge `Maps`, `CI`, and `HTTP workflow performance` runs on `main` are green.

---

## Dependencies and execution order

### Phase dependencies

- **Phase 1** has no dependency.
- **Phase 2** depends on Phase 1 and blocks all user stories.
- **User Story 2 / Phase 3** depends on Phase 2 and establishes shared evaluation semantics.
- **User Story 1 / Phase 4** depends on Phase 3 because adapter parity is meaningful only after shared semantics are fixed.
- **User Story 3 / Phase 5** depends on the trusted-principal foundation from Phase 4; all Phase 5 tasks begin after T035.
- **Phase 6** depends on all selected story checkpoints and concludes with T056's PR/merge/post-merge gate.

### Task dependencies

- T003→T005 and T006→T007 are serialized within their shared test files; the two file groups may run in parallel. T008→T010 satisfy T003–T005, while T011→T012 satisfy T006–T007; T012 also depends on T009's requirement types.
- T014→T018 are serialized in `AuthorizationContractsTests.cs` after T013; T019→T022 implement them without parallel edits to `AuthorizationContracts.cs`.
- T024–T028 may run in parallel after T023; T029→T034 integrate in order, with T032 depending on T031.
- T036–T038 may run in parallel after T035; T039→T040 are serialized in their shared catalog test file. T041→T044 and T046 share authorization files and execute serially, while T045 is independent after T039.
- T048 and T049 may run in parallel after T047; T050 follows their final terminology, and T051→T055 are serial gates.

### Parallel work examples

```text
After T002:  T003→T005 authorization tests | T006→T007 registration tests
After T013:  T014→T018 authorization-semantics tests (one shared file, serialized)
After T023:  T024 trust tests | T026 adapter helper tests | T027 same-host tests | T028 architecture tests
After T035:  T036 normalization | T037 cookie | T038 bearer | T039→T040 catalog/lifecycle
```

## Delivery strategy

1. Land no production behavior without its recorded failing test.
2. Complete the foundational policy/DI contracts before composing permission decisions.
3. Prove direct evaluator semantics before adapter parity.
4. Prove trusted provider projection and catalog lifecycle before documentation and full gates.
5. Open one PR for #1344 only after T054; split any production concern outside this task graph into #1346, #1356, or a new follow-up instead of expanding the PR.

## Completion gate

The work unit is complete only when all 56 tasks are checked, both endpoint styles pass the fixed matrix, all three replacement contracts and the result-handler wrapper fail conflicts deterministically, the Roslyn mutations prove bypass detection, focused/full/maps gates are green, PR evidence is posted, #1344 is merged/closed with board state Done, and the merged `main` gates are green.
