# Implementation Tasks: Worker external bearer normalization

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data model](data-model.md), [bearer contract](contracts/bearer-normalization.md) and [proof matrix](contracts/acceptance-proof-matrix.md).

All tasks are planned and unchecked. #2304 owns authoring/review only; a subsequent coherent implementation leaf owns T001-T028 after specification publication. No new test project/provider matrix/cadence. Tests are required by the specification and trust boundary, not a requirement to mirror every helper.

## Phase 1 — Setup and source baseline

- [ ] T001 Recheck issue/PR/Project51/claim state and installed JWT/options behavior; record source/platform/command baseline in `specs/190-worker-oidc-normalization/implementation-evidence.md` before editing.
- [ ] T002 Establish reusable isolated issuer/token/canary controls in `tests/essentials/Foundation/Identity/Tests/OidcBearerNormalizationTests.cs` with deterministic teardown; reuse existing host/registration helpers where possible.

## Phase 2 — Foundation: guarded configuration

- [ ] T003 Add default-false NormalizeBearerClaims and optional Audience to `src/essentials/Foundation/Identity/Oidc/OidcAuthenticationOptions.cs`; expose opt-in/Audience/existing ProviderId/TenantId in `OidcAuthenticationFeature.cs` with accurate setting metadata and preserved blank/absent semantics.
- [ ] T004 Make bearer Audience use explicit Audience or absent-only legacy ClientId fallback in `src/essentials/Foundation/Identity/Oidc/ConfigureOidcOptions.cs`; leave interactive registration driven solely by ClientId.
- [ ] T005 Implement final configuration/handler/event/collaborator validation in `src/essentials/Foundation/Identity/Oidc/OidcBearerNormalizationOptionsValidation.cs`: fixed namespace, effective issuer/audience, distinct raw/normalized types, frozen opt-in/scheme, actual hosted and IShellInitializer activation and exact installed guarded path, one normalizer descriptor and unsupported Challenge/Forbidden callbacks; late incompatible settings/reloads refuse.
- [ ] T006 Wire one scoped guarded events adapter with explicit duplicate-opt-in registration refusal and exact distinct normalized-type enrollment through `src/essentials/Foundation/Identity/Oidc/Extensions/OidcAuthenticationServiceCollectionExtensions.cs`, preserving current replacement guards and legacy paths.

## Phase 3 — US1: validated token to authorized runtime

**Goal**: Real external bearer authorization without user provisioning.
**Independent proof**: Actual valid/invalid JWT -> mapping/normalization -> real permission policy, followed by the real HTTP/database workflow journey.

- [ ] T007 [US1] Add input-filter/output-admission tests in `tests/essentials/Foundation/Identity/Tests/OidcBearerNormalizationTests.cs` for forged internal match claims, cross-namespace rules, exact type/marker/provider/tenant and malformed/multiple identities.
- [ ] T008 [US1] Implement the guarded MessageReceived/TokenValidated/AuthenticationFailed paths in `src/essentials/Foundation/Identity/Oidc/OidcBearerNormalizationEvents.cs`; real validation must precede namespace-owned rule loading and final normalization/admission.
- [ ] T009 [US1] Run the actual JWT handler with all three prior callback Success bypass controls in `tests/essentials/Foundation/Identity/Tests/OidcBearerNormalizationTests.cs`; preserve Fail/NoResult and reject incompatible event replacement.
- [ ] T010 [US1] Add minimal existing-project references and shared real Worker/issuer fixture in `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/WorkerOidcBearerHostFixture.cs`, composing explicit IAM schema/store and named Runtime SQLite with isolated lock/signing prerequisites.
- [ ] T011 [US1] Add actual HTTP anonymous/tampered/wrong-issuer/wrong-audience/lifetime/no-grant/forged-claim controls in `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/WorkerOidcBearerHostEvidenceTests.cs`; record zero mapping calls for invalid tokens and no unauthorized runtime effects.
- [ ] T012 [US1] Prove mapped HTTP execute/suspend/stimulus/resume/completion and actual database state in `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/WorkerOidcBearerHostEvidenceTests.cs`; derive required endpoint permissions rather than assuming execution grants all routes.

## Phase 4 — US2: declarative opt-in and compatibility

**Goal**: Equivalent developer/declarative configuration without silent authentication changes.
**Independent proof**: Rebuilt feature binding and actual named-handler/scheme activation controls for legacy, bearer-only and mixed first-party layouts.

- [ ] T013 [US2] Extend `tests/essentials/Foundation/Identity/Tests/OidcAuthenticationFeatureTests.cs` for actual CShells binding of opt-in/namespace/Audience, comparing service-registration results and no unintended interactive handler.
- [ ] T014 [US2] Extend `tests/essentials/Foundation/Identity/Tests/OidcAuthenticationRegistrationTests.cs` for default-false compatibility, absent ClientId fallback, explicit blank refusal and host-owned defaults.
- [ ] T015 [US2] Add both registration orders, repeated opt-in/duplicate-adapter refusal, late opt-in/scheme rename, raw normalized-type collision, foreign EventsType/subclass and replaced actual-handler startup refusals in `tests/essentials/Foundation/Identity/Tests/OidcBearerNormalizationTests.cs`; retain existing OpenIddict composition controls.
- [ ] T016 [US2] Inspect exact activated feature closure, mounted routes, frozen namespace/scheme and distinct normalized type in `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/WorkerOidcBearerHostEvidenceTests.cs`; do not publish a Worker profile.

## Phase 5 — US3: durable local mappings and failure ownership

**Goal**: Truthful per-request local-rule changes, restart persistence and failures.
**Independent proof**: Same-token stored-rule removal, fresh host restart, real IAM observations and injected request-stage faults/cancellation.

- [ ] T017 [US3] Add prior callback value-bearing failures/exceptions, mapping/normalizer exceptions and malformed-output canaries with401/no-ticket assertions in `tests/essentials/Foundation/Identity/Tests/OidcBearerNormalizationTests.cs`; add evaluator/resource operational failure controls without changing Spec151 behavior.
- [ ] T018 [US3] Implement fixed stage failures and RequestAborted forwarding/checks at lookup/normalization/publication in `src/essentials/Foundation/Identity/Oidc/OidcBearerNormalizationEvents.cs`, preserving cancellation through the actual handler's AuthenticationFailed boundary.
- [ ] T019 [US3] Add actual handler request-abort controls before/after lookup/normalization/publication, including noncooperative collaborators, in `tests/essentials/Foundation/Identity/Tests/OidcBearerNormalizationTests.cs`; no successful ticket after observed cancellation.
- [ ] T020 [US3] Prove same-token persisted grant removal and provider/tenant isolation in `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/WorkerOidcBearerHostEvidenceTests.cs`, using existing SaveAsync with empty GrantPermissions rather than inventing a delete API.
- [ ] T021 [US3] Prove fresh host restart reloads rules/workflow state and zero bearer-request user/external-link writes in `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/WorkerOidcBearerHostEvidenceTests.cs` by actual store/database observations.
- [ ] T022 [US3] Verify unchanged ExternalOidcDefault TokenRefreshBoundary metadata and value-free adapter diagnostics/challenges in `tests/essentials/Foundation/Identity/Tests/OidcBearerNormalizationTests.cs`; separate local-rule observations from upstream claims/revocation.

## Phase 6 — Integrated proof and publication

- [ ] T023 Run bounded production signature/normalization/success-fence/output-guard mutation controls, restore exact source and rerun affected proof; record baseline/fail/restored evidence in `specs/190-worker-oidc-normalization/implementation-evidence.md`.
- [ ] T024 Update owning OIDC README/EXTENSION_POINTS and supported authentication configuration explanation in `src/essentials/Foundation/Identity/Oidc/README.md`, `src/essentials/Foundation/Identity/Core/EXTENSION_POINTS.md` and `docs/reference/authentication-architecture.md`; link canonical contracts instead of duplicating meanings.
- [ ] T025 Run restored existing Identity, IAM and Runtime EF suites plus retained affected OpenIddict/Workbench checks from `specs/190-worker-oidc-normalization/quickstart.md`; record actual counts/skips/source/platform, without new provider cadence.
- [ ] T026 Run architecture and generated-map freshness; deliberately refresh/review findings and stage changed maps/manifest by explicit path if required; record commands in `specs/190-worker-oidc-normalization/implementation-evidence.md`.
- [ ] T027 Root-review complete diff/contracts and actor evidence, resolve actual review findings, commit unsigned, open one gated PR and record exact-head checks/reviews on the implementation issue and in `specs/190-worker-oidc-normalization/implementation-evidence.md`.
- [ ] T028 Verify all required resulting-main workflows/source-package identity, close/synchronize/release the implementation leaf, and only then refine Worker profile publication under1961; record actual delivery boundary in `specs/190-worker-oidc-normalization/implementation-evidence.md` and `docs/program-goals/feature-composition-readiness.md`.

## Dependencies and bounded parallel work

T001-T006 establish shared configuration/trust. US1 T007-T009 can begin before the actor host T010-T012; registration controls T013-T015 provide independently verifiable supporting work but must integrate before actor certification. US3 builds on guarded request behavior and the real host. T023-T028 require the integrated restored source.

Parallel example: one bounded reader reviews source/contract while root authors; after implementation claim, a worker may own isolated Identity test additions while another prepares the real actor fixture on disjoint files. Root owns production wiring, conflict decisions, sequential shared-machine builds, integrated review and all acceptance gates. These examples do not authorize a second active GitHub delivery leaf.

## Delivery strategy

Implement all three P1 outcomes as one coherent adapter leaf. US1 alone provides the first valid request; it is not releasable without configuration compatibility and cancellation/durable-rule controls. Keep all28 tasks unchecked until their own evidence exists. Specification review may refine allocations without falsely marking implementation done or expanding this into a new IAM/platform project.
