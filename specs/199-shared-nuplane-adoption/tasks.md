# Tasks: Shared Nuplane Adapter Adoption

**Scope**: Implement and prepare Foundation #2314 against an isolated preview qualification worktree. Keep this canonical preparation branch documentation-only and leave its `.159`/`.94` package pins and lockfiles unchanged. Final stable-family adoption is a separate unchecked gate below.

**Evidence location**: `artifacts/modular-hosting-2500/foundation-2314-preparation/` means a directory under the session-owned artifact workspace outside the repository checkout. Keep raw package archives, caches, logs and generated qualification records out of source control.

**Test cadence**: No derived application constitution declares a test-first cadence. Refactored tests must preserve their subject/objective under framework §2.21.1. New test tasks follow implementation dependencies; no fail-first run is claimed as mandatory. No new test project is introduced.

## Phase 1: Setup

- [ ] T001 Create a separately owned qualification worktree from the reviewed Foundation source baseline and capture its clean starting commit/status in `artifacts/modular-hosting-2500/foundation-2314-preparation/`; do not alter the canonical branch pins in `Directory.Packages.props`.
- [ ] T002 In the qualification worktree only, apply the coherent centrally managed CShells `.171` family and Nuplane `.99` preview overlay, add the preview `CShells.Nuplane` package version to `Directory.Packages.props`, and record the exact overlay diff without changing canonical pins or locks.

## Phase 2: Foundational Qualification Baseline

**Purpose**: Establish a locked restore target for the candidate before replacing either host composition.

- [ ] T003 In the qualification worktree, add direct `CShells.Nuplane` `PackageReference` entries to `src/apps/Elsa.Foundation.Host/Elsa.Foundation.Host.csproj` and `src/apps/Elsa.Workbench/Elsa.Workbench.csproj`, restore every reached `packages.lock.json` in the scoped project dependency graph, including `src/apps/Elsa.Foundation.Host/packages.lock.json` and `src/apps/Elsa.Workbench/packages.lock.json`, and verify exact `.171`/`.99` identities plus required `net8.0`, `net9.0` and `net10.0` assets. Record archive/cache/source/lock identities under `artifacts/modular-hosting-2500/foundation-2314-preparation/`; `.172` has archive verification only and is not runtime proof.

## Phase 3: User Story 1 - Deliver package changes consistently (Priority: P1)

**Goal**: Replace duplicated host coordination with one coherent upstream adapter composition, keeping each host's existing defaults and composition boundaries working throughout the candidate change.

**Independent Test**: Both hosts resolve root and shell observer/participant aliases to one coordinator; the adapter follows package autoload; eligible cold package completion is visible to a later shell build; Foundation.Host remains feature-free and Workbench retains its catalog projection.

- [ ] T004 [US1] As one coherent source migration in the qualification worktree, configure `CShells.Nuplane` through `WithNuplaneFeatureDiscovery` in `src/apps/Elsa.Foundation.Host/Program.cs` and `src/apps/Elsa.Workbench/Program.cs`; add app-local monitored profile and result-callback setup at `src/apps/Elsa.Foundation.Host/Shells/NuplaneIntegrationOptionsSetup.cs` and `src/apps/Elsa.Workbench/Modularity/NuplaneIntegrationOptionsSetup.cs`; preserve autoload and earlier observer order, both legacy policy defaults, existing refusal callbacks, host assembly providers and Workbench feature-catalog projection; then remove `src/apps/Elsa.Foundation.Host/Feed/NuplaneAssemblyProvider.cs`, `src/apps/Elsa.Foundation.Host/Shells/ShellReloadOnPackagesChanged.cs`, `src/apps/Elsa.Workbench/NuplaneAssemblyProvider.cs` and `src/apps/Elsa.Workbench/Modularity/ShellCatalogRefreshOnPackagesChanged.cs`, updating stale references in `src/apps/Elsa.Foundation.Host/ModuleManagement/ModuleManagementEndpoints.cs`, `src/apps/Elsa.Foundation.Host/Shells/EagerShellActivationHostedService.cs` and `docs/foundation-host-feeds.md` without changing startup policy.
- [ ] T005 [US1] Adapt `tests/essentials/Modularity/Tests/HostOwnedServicesAreSharedWithShellsTests.cs` to prove the registered adapter follows autoload and root/shell observer-participant aliases use the same root coordinator; retain its existing ordering objective without assertions tied to the deleted observer class. Run `tests/essentials/Architecture/HostProvidedPackagesGuardTests.cs` and `tests/essentials/Architecture/SharedAssemblyClosureGuardTests.cs` to verify package-host declarations and dependency closure remain valid; preserve Foundation.Host's feature-free project boundary and Workbench catalog registration.
- [ ] T006 [US1] Add public-composition cold-build regression in `tests/essentials/Modularity/Tests/SharedNuplaneHostIntegrationTests.cs`: for both host profiles, dispatch an eligible package completion while no shell is active, then trigger the later build through registered public adapter/build-participant services and prove it observes pending package freshness before feature selection; use no internal reflection or production test hooks.

## Phase 4: User Story 2 - Change reload policy without rebuilding the host (Priority: P1)

**Goal**: Preserve both live host profiles through standard options DI; configuration changes affect the next eligible delivery but do not schedule work themselves.

**Independent Test**: On one built provider, assert absent/malformed/true/false profile behavior for both hosts; reload configuration and prove only a later eligible completion captures the changed policy. Workbench reload-option toggles alone create no callback or reload.

- [ ] T007 [US2] Add actual-DI registration and behavior tests in `tests/essentials/Modularity/Tests/SharedNuplaneHostProfilesTests.cs` for Foundation's every-eligible/reload-on defaults and Workbench's changed-or-pending/reload-off defaults, malformed-value fallbacks, real configuration-root Reload invalidation without provider reconstruction, per-delivery policy snapshots, disabled/pending-work behavior, and no work from a setting toggle before the next eligible package completion.

## Phase 5: User Story 3 - Understand failed package activation (Priority: P1)

**Goal**: Preserve Elsa refusal interpretation, sanitized per-shell reporting and direct-cancellation behavior while ordinary thrown failures use the actual Nuplane dispatch boundary.

**Independent Test**: Mixed reload results preserve per-shell counts and refusal redaction; ordinary thrown refresh/reload errors log Error with original exception/correlation, then Nuplane isolates them with its Warning and continues to a later observer; direct caller cancellation propagates without an error log.

- [ ] T008 [US3] Add focused per-implementation callback tests in `tests/essentials/Modularity/Tests/NuplaneReloadResultCallbackTests.cs`, resolving each host's configured `OnReloadResults` through options DI. Cover Foundation mixed and all-failed shells, ordinary exception type-only details, recognized nested refusal and host-directory substitution/private-interface behavior; cover Workbench nested and aggregate refusal branches. Reuse unchanged `ShellReloadFailure` behavior where covered; do not use the real-host test as a substitute for these stubbed callback tests.
- [ ] T009 [US3] Rewire `tests/essentials/Modularity/Tests/WorkbenchShellCatalogRefreshTests.cs` to the registered adapter and actual Nuplane dispatcher while preserving every existing test subject and assertion; prove Error/original-exception/correlation, dispatcher Warning, following-observer execution, later eligible retry and direct-observer cancellation without logging.
- [ ] T010 [US3] Preserve the existing real-host scenarios in `tests/essentials/Cluster/EntityFrameworkCore/Tests/FoundationHostReloadRefusalTests.cs` against the registered adapter; repair host/feed fixture wiring only if needed and keep all current refusal, prior-generation retention, remediation and recovery assertions without adding a redundant real-host scenario.

## Phase 6: Preview Qualification and Candidate Review

**Purpose**: Prove the candidate against isolated previews without promoting preview defaults or declaring stable adoption.

- [ ] T011 Build and run the touched host, Modularity and Cluster/EF test projects in the qualification worktree through the shared build-slot wrapper; run affected architecture guards including `tests/essentials/Architecture/HostShellFeatureVisibilityTests.cs`, `tests/essentials/Architecture/HostProvidedPackagesGuardTests.cs`, `tests/essentials/Architecture/SharedAssemblyClosureGuardTests.cs` and `tests/essentials/Architecture/NuGetLockFileTests.cs`, then regenerate and check maps in that worktree and record every command result.
- [ ] T012 Rebuild owned Foundation.Host (`src/apps/Elsa.Foundation.Host/Elsa.Foundation.Host.csproj`) and Workbench (`src/apps/Elsa.Workbench/Elsa.Workbench.csproj`) instances from the qualification worktree with fresh databases; verify package add/update/remove, Foundation reload-on, Workbench reload-off/opt-in, cold-build freshness, refusal retention and later recovery. Label all results as preview qualification; reserve backend `e2e-tests/` and frozen Acts1/2 rehearsal for the final stable-family gate.
- [ ] T013 Preserve the reviewed application-source diff, preview-only overlay/locks, package/source/cache provenance and validation results under `artifacts/modular-hosting-2500/foundation-2314-preparation/`; obtain root/independent source review and appropriate reversible mutations for monitored-option invalidation and coordinator alias/order. Leave canonical pins unchanged and do not publish preview pins as defaults.

## Phase 7: Final Stable Adoption (Blocked Until Stable Families Exist)

**Purpose**: Complete release-gated adoption only after Nuplane and the full CShells family are stable.

- [ ] T014 After stable Nuplane and complete CShells releases are published, replay the reviewed application-source diff into the final adoption branch; update `Directory.Packages.props` with the normal stable `CShells.Nuplane` version and full stable families, then update reached `packages.lock.json` files, applicable Docker restore graph and generated maps together.
- [ ] T015 Rebuild and test real Foundation.Host and Workbench against the exact stable family; rerun affected Modularity, Cluster/EF and architecture tests, relevant `e2e-tests/` suites and `bash tools/demo/rehearse.sh` frozen Acts1/2 rehearsal, then require current-head and resulting-main CI/Maps gates before closing #2314.

## Dependencies and Execution Order

- T001 → T002 → T003 establish the isolated preview qualification baseline.
- User Story 1 source migration T004 is intentionally coherent across both hosts; it does not remove old behavior before new profiles and callbacks are connected. T005-T006 verify composition identity/order and cold-build freshness.
- User Story 2 T007 and User Story 3 T008-T010 depend on T004; callback stub tests T008, Workbench dispatch tests T009 and retained Foundation host scenarios T010 cover separate boundaries.
- T011-T013 validate and preserve the preview candidate after the three stories are complete.
- T014-T015 are blocked until stable upstream families are published. Preview tasks do not satisfy final stable acceptance.

## Parallel Opportunities

- After T004, composition proof T005 and cold-build proof T006 use separate test files and can be developed independently.
- After T004, callback stub coverage T008 and the Workbench dispatcher regression T009 use separate test files and can be prepared independently; Foundation host regression T010 retains its own integration boundary.
- Build/test commands remain serialized through the shared build-slot wrapper; do not run overlapping solution or host builds on the shared machine.

## Implementation Strategy

Complete setup and isolated restore first. Deliver the coherent two-host source migration as the first working increment, then verify composition/cold-build behavior (US1), live policy changes (US2), and Elsa-specific refusal diagnostics (US3). Qualify only in the preview copy and preserve a reviewable source handoff. Keep stable adoption unchecked until stable packages exist; then update canonical pins/locks/maps and repeat actual-host, E2E, demo and resulting-main gates.
