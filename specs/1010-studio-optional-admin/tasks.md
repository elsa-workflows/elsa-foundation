# Tasks: Optional Studio administration

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [contract](contracts/administration.md).

Risk-based behavior tests and causal controls preserve the existing valid contracts; no mandatory test-first
cadence is declared. One implementation leaf stays active; read-only research/review may run independently.

## Phase1 — Scope and composition foundation

- [x] T001 Record spec/design/contracts/research/checklist in specs/1010-studio-optional-admin before code.
- [x] T002 Create Studio/src/essentials/Elsa.Studio.Hosting.Nuplane project/options/composition/README/public API tracking; add solution/project/lock inventory inputs without optional dependencies in base Hosting.
- [x] T003 Add installed managed-file source detection to Hosting's existing IOptions<StudioHostingOptions> snapshot when core registry/provider descriptors are replaced; cover public applied/read-only behavior.
- [x] T004 Register effective content-root Nuplane configuration/callback/feeds, fallback selection, shared discovery/reporting and root-owned admin/maintenance in the addon; test missing/ambiguous/callback feeds and optional absence.

## Phase2 — US1: public administration and distribution adoption

Independent test: actual public host endpoints with non-default shell/file/feed choices, auth and real temporary files.

- [x] T005 Move/simplify private module-management HTTP/DTO/config adapters into the addon; preserve registry/upload/delete/feed/reconcile routes and read/manage policy enforcement.
- [x] T006 Resolve installed drop-folder identity from final effective registered feeds; keep pending feed edits from redirecting current uploads; use shared configuration mutation gate and private atomic writer.
- [x] T007 Adopt public addon calls in Web Program.cs and remove copied Nuplane discovery/reporting from StudioFeatureAssemblies.cs; retain distribution module selection and remaining theme writer.
- [x] T008 Migrate moved internal-type test fixtures to public composition/HTTP in Studio/tests/Elsa.Studio.Tests, preserving upload/archive/path/auth cases without a production friend.

## Phase3 — US2: shared feature management and supported stores

Independent test: public catalog/apply, real selected file and secret-bearing feature settings; concurrent full-state saves.

- [x] T009 Replace local ElsaFeatureManagementApi catalog/manifest/settings/secret algorithms and DTOs with Modularity.Core/Nuplane application/contracts; validate request shape and register private mandatory StudioApi guard.
- [x] T010 Implement private IShellFeatureConfigurationStore/IShellReloader adapters for installed selected source, keyed revisions, whole atomic mutation/config-reload cycle and explicit reload failures; remove local snapshot/conflict types.
- [x] T011 Test custom file/non-default shell/decoy isolation, full-state/duplicate/required/secret behavior, conflict/concurrent saves, source overlays/custom blueprint/null-file refusal and deterministic config reload through public endpoints.

## Phase4 — US3: confirmed maintenance and protected assets

Independent test: real enrolled native installs, retained shell scope, actual prune deletion and protected HTTP overlap.

- [x] T012 Replace local prune/legacy retention code with upstream maintenance Preview/Execute and exact explicit confirmation; persist supported nullable inactive-version policy and legacy incompatibility diagnostics.
- [x] T013 Implement private bounded admitted package-content adapter and response permit ownership, reusing guarded PackageContent bytes; protect manifest/nuspec/catalog reads with the same rules/shared mapper.
- [x] T014 Replace NuplaneStaticWebAssetsEndpoint physical-path responses with the supported immutable extracted profile; preserve MIME/no-cache/conditional/range and explicit retryable/unsupported outcomes.
- [x] T015 Add real public-addon/native-store tests for ordinary refusal, explicit enrollment, active/recovery/retained-reader protection, physical obsolete deletion, binding mismatches, protected read/maintenance overlap, links/traversal/bounds/cancellation/slow-response saturation.
- [x] T016 Update Hosting Client moduleManagementApi.ts/PackageFeedsPage.tsx models and controls to supported retention plus Preview/details/explicit Execute; preserve read-only older Server data and truthful outcome reporting.
- [x] T017 Add frontend/browser tests for deliberate legacy migration, preview-before-execute, cancellation/stale plans and refusal/partial outcomes; run typecheck/lint/unit/build/runtime verification.

## Phase5 — Qualification and reviewed delivery

- [ ] T018 Run whole affected .NET projects and discovery accounting; use mutation controls for revisions/mandatory auth/preview/asset admission and bounds, restoring sources exactly.
- [ ] T019 Run affected locked restores, architecture/maps/inventory checks, actual candidate pack/dependency inspection and isolated reference Web smoke; update canonical hosting contract/README with actual profiles and retired code.
- [ ] T020 Set spec Implemented with completed draft independent review; post exact-head evidence and request external review after required build/test gate; preserve no-release/publisher boundary.
- [ ] T021 Verify reviewed merge and actual resulting main, close #3359/parent feature as appropriate and reconcile Program #3340 board/hierarchy/evidence.

## Dependencies and execution

T001 → T002–004 → public administration/shared-store adoption → maintenance/content → frontend controls → gates/delivery.
US1 establishes common composition; US2 and US3 reuse it and all are required. Research/read-only source checks
can run independently; implementation stays in this worker. Native fixtures and frontend unit tests are separate
validation surfaces and can run independently after their source inputs settle. No additional issues are generated
for this existing leaf checklist; downstream isolated consumer combinations remain #3363–#3365.
