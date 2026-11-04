# Tasks: external authentication and OIDC connections

Delivery record #3579; program #2771; necessary identity-isolation prerequisite #2848. Full scope stays open through resulting-main qualification. Paths below are repository-relative.

## Phase 1 — Setup and design

- [x] T001 Claim/revalidate #3579/#2848, isolate the worktree and record baseline/owner authorization in `research.md` and issue comments.
- [x] T002 Refresh Elsa 3/Elsa 4 inventory and define complete journeys in `spec.md`, `research.md`, `data-model.md`, `contracts/identity-and-management.md`, `quickstart.md` and `plan.md`.
- [ ] T003 Resolve the repeated mapped-role card and update `research.md` R6 and `spec.md`; do not use the withdrawn selection.
- [x] T004 Amend explicit override/phase bookkeeping in `docs/adr/0085-elsa-3-parity-identity-administration-and-scope.md`, `docs/adr/0080-elsa-4-simplification-decisions.md`, `docs/program-goals/elsa3-parity.md` and the registry.

## Phase 2 — Foundational identity isolation (independent of T003)

- [x] T005 Define typed token subjects and scoped issue/revocation contracts in `Essentials/src/Foundation/Identity/Core/Authentication/`; update `Core/PublicAPI.Unshipped.txt`.
- [x] T006 Implement trusted-principal subject resolution and canonical identifier replacement in `Identity/Elsa.Foundation.Identity/Authentication/`, `Identity/Api/FoundationIdentityApi.cs` and `Identity/AspNetCoreIdentity/.../Services/IdentityClaimsProjector.cs`.
- [x] T007 Preserve validated external origin in `Identity/Oidc/OidcBearerNormalizationEvents.cs` and strip incoming internal provenance in `Identity/Elsa.Foundation.Identity/Authorization/AuthorizationServices.cs`.
- [x] T008 Store versioned scoped keys and typed refresh payloads in `Identity/OpenIddict/Behavior/OpenIddictTokenService.cs`; refuse unscoped old payloads.
- [x] T009 Prove tenant/provider/issuer/case/user-kind separation, real HTTP logout/refresh isolation and projection correctness in `Essentials/tests/Foundation/Identity/Tests/`; update touched public consumers.

## Phase 3 — US1: exact identity and admission

- [ ] T010 [US1] Extend exact issuer/subject link and atomic admission contracts in `Identity/Core/Iam/` and implementation/store counterparts under `Identity/AspNetCoreIdentity/` and `Identity/Persistence/EntityFrameworkCore/`. Exact-link contracts/stores, additive migration and content-preserving schema upgrade are implemented; atomic admission remains.
- [ ] T011 [US1] Implement reject-default, explicit JIT/default Elsa roles and configured matcher policies with preview-safe assessment under `Identity/`; include disabled/ambiguous/conflicting-user behavior tests.
- [ ] T012 [US1] After T003, implement explicit existing-Elsa-role mapping, deployment/editor delegation and permission projection under `Identity/`; cover claim injection and deleted roles.
- [ ] T013 [US1] Compose external admission into ordinary cookie/OpenIddict sessions in `Identity/AspNetCoreIdentity/`; honor FoundationOwned/ExternalOwned/Hybrid capabilities.

## Phase 4 — US3: source ownership and lifecycle

- [ ] T014 [US3] Define typed connection/catalog and live discovery contracts in `Identity/ExternalAuthentication/Core/`; reuse provider-configuration CAS in its optional implementation with explicit authorized global boundary. Catalog/contracts are implemented; bootstrap/provider discovery integration remains.
- [x] T015 [US3] Implement deployment baseline, no-auto-shadow collision, explicit full override, disabled shadow and archive/restore/remove semantics under `Identity/`; include material revisions/provenance.
- [ ] T016 [US3] Implement atomic catalog mutation/final-login guard including unusable baseline and concurrent last-path races under `Identity/`; test provider-backed persistence. Coordinator/CAS/native writer race checks are implemented; actual protocol/IAM recovery adapters remain for runtime qualification.

## Phase 5 — US2: configure, test, preview, enable

- [ ] T017 [US2] Implement OIDC adapter/discovery/exchange/token validation and point-of-use Secrets resolution under `Identity/ExternalAuthentication/Oidc/` through managed Core; preserve direct bearer mode.
- [ ] T018 [US2] Implement durable purpose/browser/revision/secret-bound atomic flow and preview stores under `Identity/Persistence/EntityFrameworkCore/`, including provider migrations and expiry/replay tests.
- [ ] T019 [US2] Implement bounded non-interactive tests, pure prospective admission/permissions and administrator-bound non-provisioning preview under `Identity/`; prove no normal session/identity mutation.
- [ ] T020 [US1] Integrate real backend chooser/challenge/callback with local login under `Identity/Api/` and `Identity/AspNetCoreIdentity/`; preserve return-path/CSRF and theme behavior.
- [ ] T021 [US2] Expose permission/capability/revision-protected management, bounded user/role lookup and test/preview APIs under `Identity/Api/`; add endpoint/contract/security tests.
- [ ] T022 [US2] Implement optional Studio SDK/module connection list/editor/test/preview/enable flow in `Extensions/Identity/studio/`, reusing public metadata-only secret picking and `--studio-*` tokens.

## Phase 6 — US4: links, sessions and recovery

- [ ] T023 [US4] Implement durable session identity, status validation and scoped revocation across cookies/access/refresh under `Identity/`; use the same scope for disable/delete and selected-session revocation.
- [ ] T024 [US4] Implement link/session inspection and mutation APIs plus redacted `IAuthEventSink` notifications under `Identity/`; cover conflict/revocation failure and unrelated-user survival.
- [ ] T025 [US4] Add Studio links/sessions/recovery, source-owned/read-only, ownership-mode, missing-module and denied-action surfaces in `Extensions/Identity/studio/`; cover loading/empty/error/conflict states.

## Phase 7 — US5: actual provider and deployment qualification

- [ ] T026 [US5] Build a controlled actual OIDC-provider/composed-host harness and reproducible runtime evidence in `specs/1205-external-authentication/`; use owned stores/keys/ports only.
- [ ] T027 [US5] Prove two-provider/local choice, admission/JIT/matching/roles, explicit override lifecycle, preview isolation, secrets, revocation and last-login protection with actual hosts/Studio; record results in `evidence.md`.
- [ ] T028 [US5] Prove durable restart and shared two-node flow completion/replay/catalog races for each claimed mode; record exact store/key/provider configuration without credentials in `evidence.md`.
- [ ] T029 [US5] Qualify representative permission/ownership/source/missing-module/theme/responsive/keyboard/retained-tab Studio journeys in `evidence.md`.

## Phase 8 — Documentation, gates, review and closeout

- [ ] T030 Update `Identity/**/README.md`, `EXTENSION_POINTS.md`, root catalog, `docs/reference/authentication-architecture.md` and `identity-configuration.md`; remove obsolete behavior/docs and account for public API changes.
- [ ] T031 Run affected backend/EF/Studio gates, generated-map checks, packed module/assets and separate public consumer; preserve detailed result statuses in `evidence.md`.
- [x] T032 Revalidate claims/PRs, commit/push only alongside one draft PR for #3579 (also resolving #2848), map all acceptance criteria and post required gate evidence.
- [ ] T033 Run independent correctness/standards/acceptance reviews on the exact draft head; fix findings, satisfy current required checks and merge under live org rules. Record rounds on the PR/issues.
- [ ] T034 Qualify resulting main with affected actual journeys, CI/artifact/consumer boundaries in `resulting-main-qualification.md`; reconcile issues/program/spec/docs only after proof.

## Dependencies and execution

T005–T009 may proceed while T003 is pending. Then T010/T011/T014 establish identity/catalog foundations; T012 waits for T003. Lifecycle validation precedes management mutation; protocol/state precede real login/preview; API precedes Studio integration. Session/flow/provider persistence is required before runtime qualification. No file-overlapping tasks run concurrently. Protocol tests and UI tests may run independently after their interfaces stabilize, respecting shared build slots. Current skill permits parallel tasks, but no implementation delegation is required.

All journeys remain P1 delivery requirements; there is no reduced acceptance/MVP closeout. Tests follow risk-based repository cadence; no blanket TDD declaration is present. New regressions should demonstrate the prior failure where practical, and all valid prior behavior remains tested.
