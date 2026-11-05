# Tasks: external authentication and OIDC connections

Delivery record #3579; program #2771; necessary identity-isolation prerequisite #2848. Full scope stays open through resulting-main qualification. Paths below are repository-relative.

## Phase 1 — Setup and design

- [x] T001 Claim/revalidate #3579/#2848, isolate the worktree and record baseline/owner authorization in `research.md` and issue comments.
- [x] T002 Refresh Elsa 3/Elsa 4 inventory and define complete journeys in `spec.md`, `research.md`, `data-model.md`, `contracts/identity-and-management.md`, `quickstart.md` and `plan.md`.
- [x] T003 Record the considered owner decision in `research.md` R6 and `spec.md`: provisioning policy only, assigning saved existing Elsa roles; omit external group-to-role mappings for now.
- [x] T004 Amend explicit override/phase bookkeeping in `docs/adr/0085-elsa-3-parity-identity-administration-and-scope.md`, `docs/adr/0080-elsa-4-simplification-decisions.md`, `docs/program-goals/elsa3-parity.md` and the registry.

## Phase 2 — Foundational identity isolation (independent of T003)

- [x] T005 Define typed token subjects and scoped issue/revocation contracts in `Essentials/src/Foundation/Identity/Core/Authentication/`; update `Core/PublicAPI.Unshipped.txt`.
- [x] T006 Implement trusted-principal subject resolution and canonical identifier replacement in `Identity/Elsa.Foundation.Identity/Authentication/`, `Identity/Api/FoundationIdentityApi.cs` and `Identity/AspNetCoreIdentity/.../Services/IdentityClaimsProjector.cs`.
- [x] T007 Preserve validated external origin in `Identity/Oidc/OidcBearerNormalizationEvents.cs` and strip incoming internal provenance in `Identity/Elsa.Foundation.Identity/Authorization/AuthorizationServices.cs`.
- [x] T008 Store versioned scoped keys and typed refresh payloads in `Identity/OpenIddict/Behavior/OpenIddictTokenService.cs`; refuse unscoped old payloads.
- [x] T009 Prove tenant/provider/issuer/case/user-kind separation, real HTTP logout/refresh isolation and projection correctness in `Essentials/tests/Foundation/Identity/Tests/`; update touched public consumers.

## Phase 3 — US1: exact identity and admission

- [x] T010 [US1] Extend exact issuer/subject link and atomic admission contracts in `Identity/Core/Iam/` and implementation/store counterparts under `Identity/AspNetCoreIdentity/` and `Identity/Persistence/EntityFrameworkCore/`. Exact-link contracts/stores, additive migration, content-preserving schema upgrade and atomic admission are implemented. Policy/protocol composition and actual provider qualification remain in their following tasks.
- [x] T011 [US1] Implement reject-default, explicit JIT/default Elsa roles and configured matcher policies with preview-safe assessment under `Identity/`; include disabled/ambiguous/conflicting-user behavior tests.
- [x] T012 [US1] Apply T003: remove unused external group-to-role mapping contracts; retain provisioning-policy role selection, deployment/editor delegation and saved-permission projection under `Identity/`; verify claim injection, preserved manual memberships and missing roles.
- [x] T013 [US1] Compose external admission into ordinary cookie/OpenIddict sessions in `Identity/AspNetCoreIdentity/`; honor FoundationOwned/ExternalOwned/Hybrid capabilities. Actual owned HTTP tests prove normal cookie/access/refresh/logout and ownership behavior; durable selected-session/status enforcement remains T023.

## Phase 4 — US3: source ownership and lifecycle

- [x] T014 [US3] Define typed connection/catalog and live discovery contracts in `Identity/ExternalAuthentication/Core/`; reuse provider-configuration CAS in its optional implementation with explicit authorized global boundary. Optional API contributes bounded live bootstrap/provider discovery with Elsa IAM capabilities and explicit source lifecycle.
- [x] T015 [US3] Implement deployment baseline, no-auto-shadow collision, explicit full override, disabled shadow and archive/restore/remove semantics under `Identity/`; include material revisions/provenance.
- [x] T016 [US3] Implement catalog mutation/final-login guard with installed-protocol/IAM/session readiness and evidence-backed administrative recovery. Actual HTTP sign-ins/native SQLite prove invalid-baseline refusal, safe baseline reveal, stale credential/callback/identity/permission rejection and forced two-node catalog CAS retirement. Coordinated IAM/link/session mutations are covered by T024; separate-process/server-database/deployment proof remains T026–T028.

## Phase 5 — US2: configure, test, preview, enable

- [x] T017 [US2] Implement OIDC adapter/discovery/exchange/token validation and point-of-use Secrets resolution under `Identity/ExternalAuthentication/Oidc/` through managed Core; preserve direct bearer mode. Protocol component implementation is complete; broker/durable-state/session composition and actual deployment qualification remain in following tasks.
- [x] T018 [US2] Implement durable purpose/browser/revision/secret-bound atomic flow and preview stores under optional `Identity/ExternalAuthentication/Persistence/EntityFrameworkCore/`, including provider migrations and expiry/replay tests. Component restart/shared-service-provider proof uses SQLite and owned Data Protection keys; actual browser, separate-process and server-database qualification remain T026–T028.
- [x] T019 [US2] Implement bounded non-interactive tests, pure prospective admission/permissions and administrator-bound non-provisioning preview under `Identity/`; actual HTTP tests preserve the existing administrator cookie/token and IAM rows for a disabled shadowed draft and an enabled linked user. Studio/browser/product qualification remains T026–T029.
- [x] T020 [US1] Integrate real backend chooser/challenge/callback with local login under optional `Identity/ExternalAuthentication/Api/` and `Identity/AspNetCoreIdentity/`; actual owned HTTP tests cover return paths, CSRF/browser/purpose/replay, native cookie/token composition and existing theme markup. Browser presentation and deployment qualification remain T026–T029.
- [x] T021 [US2] Expose permission/capability/source/revision-protected management, bounded user/role lookup and test/preview APIs in optional `Identity/ExternalAuthentication/Api/`; actual HTTP endpoint/security/lifecycle/projection tests pass. Studio/deployment qualification remain separate.
- [x] T022 [US2] Implement optional Studio SDK/module connection list/editor/test/preview/enable flow in `Extensions/Identity/studio/`, reusing public metadata-only secret picking and `--studio-*` tokens. Component/SDK tests, actual composed-backend Chromium journey, Classic-light accessibility/narrow layout, affected Studio host build and packed standalone SDK consumer pass. Full Studio-host/external-product/deployment/representative UI qualification remain T026–T031.

## Phase 6 — US4: links, sessions and recovery

- [x] T023 [US4] Implement durable session identity, status validation and scoped revocation across cookies/access/refresh under `Identity/`; use the same scope for disable/delete and selected-session revocation. Native SQLite store/restart/CAS tests and actual HTTP cookie/access/refresh tests prove selected-session and account invalidation, unchanged unrelated sessions, absolute expiry and no resurrection after disable→enable. Administrative APIs/events and cross-IAM last-path coordination are covered by T024; separate-process/server-database qualification remains T026–T028.
- [x] T024 [US4] Implement link/session inspection, guarded attachment/removal, selected-session revocation and redacted `IAuthEventSink` notifications. Current grants/ownership/delegation and prospective per-user/global recovery commit with IAM guards and the catalog CAS in database-managed mode; baseline mode needs no provider database. Actual controlled-provider HTTP/native SQLite cases prove conflict, fault/unknown-commit handling, retry, sibling/unrelated credential survival, and two independent runtime nodes preserving the final link. Full host/process/server-engine/Studio qualification remains T025–T031.
- [x] T025 [US4] Add Studio links/sessions/recovery, source-owned/read-only, ownership-mode, missing-module and denied-action surfaces in `Extensions/Identity/studio/`; cover loading/empty/error/conflict states. SDK/component tests and mounted-component Chromium/native SQLite journey pass, including actual attachment, final-path refusal, subsequent safe removal, saved-role preservation, selected revocation and denied reader panels. Full Studio-host/theme/deployment acceptance remains T026–T031.

## Phase 7 — US5: actual provider and deployment qualification

- [x] T026 [US5] Build a controlled actual OIDC-provider/composed-host harness and reproducible runtime evidence in `specs/1212-external-authentication/`; use owned stores/keys/ports only. The owned controlled-provider journey, pinned Keycloak interoperability, reference Studio and Workbench runners are executable under `tools/identity/ExternalAuthentication.Probe/`; their bounded results and remaining process/server-database matrix are recorded separately.
- [x] T027 [US5] Prove two-provider/local choice, admission/JIT/matching/roles, explicit override lifecycle, preview isolation, secrets, revocation and last-login protection with actual hosts/Studio; record results in `evidence.md`.
- [x] T028 [US5] Prove durable restart and shared two-node flow completion/replay/catalog races for each claimed mode; record exact store/key/provider configuration without credentials in `evidence.md`. Separate Kestrel processes pass on native SQLite (same machine) and PostgreSQL 16.15, each in managed and deployment-only modes; replay, revocation, exact-link/final-path races and source changes are checked, with managed catalog CAS/lifecycle and private frozen PostgreSQL dumps. Broader UI/topology and final consumer/CI/delivery qualification remain separate.
- [ ] T029 [US5] Qualify representative permission/ownership/source/missing-module/theme/responsive/keyboard/retained-tab Studio journeys in `evidence.md`.

## Phase 8 — Documentation, gates, review and closeout

- [x] T030 Update `Identity/**/README.md`, `EXTENSION_POINTS.md`, root catalog, `docs/reference/authentication-architecture.md` and `identity-configuration.md`; remove obsolete behavior/docs and account for public API changes.
- [ ] T031 Run affected backend/EF/Studio gates, generated-map checks, packed module/assets and separate public consumer; preserve detailed result statuses in `evidence.md`.
- [x] T032 Revalidate claims/PRs, commit/push only alongside one draft PR for #3579 (also resolving #2848), map all acceptance criteria and post required gate evidence.
- [ ] T033 Run independent correctness/standards/acceptance reviews on the exact draft head; fix findings, satisfy current required checks and merge under live org rules. Record rounds on the PR/issues.
- [ ] T034 Qualify resulting main with affected actual journeys, CI/artifact/consumer boundaries in `resulting-main-qualification.md`; reconcile issues/program/spec/docs only after proof.

## Dependencies and execution

T003 selects provisioning policy only. T005–T009 establish identity isolation; T010/T011/T014 establish identity/catalog foundations; T012 applies the role-scope decision. Lifecycle validation precedes management mutation; protocol/state precede real login/preview; API precedes Studio integration. Session/flow/provider persistence is required before runtime qualification. No file-overlapping tasks run concurrently. Protocol tests and UI tests may run independently after their interfaces stabilize, respecting shared build slots. Current skill permits parallel tasks, but no implementation delegation is required.

All journeys remain P1 delivery requirements; there is no reduced acceptance/MVP closeout. Tests follow risk-based repository cadence; no blanket TDD declaration is present. New regressions should demonstrate the prior failure where practical, and all valid prior behavior remains tested.
