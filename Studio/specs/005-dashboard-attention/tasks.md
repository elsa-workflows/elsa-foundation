# Tasks: Dashboard Widgets and Attention

**Studio root**: `/Users/sipke/.codex/worktrees/d246/elsa-foundation-studio`

**Foundation root**: `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation`

## Phase 1: Setup and module scaffolding

- [X] T001 Add Studio Dashboard, Attention, and Workflows Dashboard projects/packages to `Elsa.Studio.slnx`, `pnpm-workspace.yaml`, `src/Elsa.Studio.Web/Elsa.Studio.Web.csproj`, and `src/Elsa.Studio.Web/Program.cs`
- [X] T002 [P] Scaffold `src/Elsa.Studio.Dashboard/`, `src/Elsa.Studio.Attention/`, and `src/Elsa.Studio.Workflows.Dashboard/` with feature, manifest handler, client package, Vite, TypeScript, and test configuration
- [X] T003 Add Foundation Attention, Studio Preferences, Attention satellite, and Workflows Dashboard projects to `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/Elsa.Server.slnx`, `Elsa.Server.test.slnf`, and `src/Apps/Elsa.Server/Elsa.Server.csproj` (`Elsa.Server.test.slnf` does not exist in this repository; all test projects are in `Elsa.Server.slnx`.)
- [X] T004 [P] Scaffold Foundation projects under `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Elsa/Attention/`, `src/Elsa/Studio/Preferences/`, `src/Elsa/Workflows/Runtime/Attention/`, `src/Elsa/Secrets/Attention/`, `src/Elsa/Modularity/Attention/`, and `src/Elsa/Workflows/Dashboard/`
- [X] T005 Update Studio and Foundation first-party feature catalogs and shell configuration in `src/Elsa.Studio.Web/shells.json`, `src/Elsa.Studio.Web/appsettings.json`, and `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Apps/Elsa.Server/shells.json`

## Phase 2: Foundational Dashboard and Preferences

**Goal**: Establish the clean-break widget host and governed preferences required by every user story.

- [X] T006 [P] Add preference namespace/store/API contract tests in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/tests/Elsa/Studio/Preferences/Tests/`
- [X] T007 [P] Add Groundwork preference persistence contract tests for SQLite and PostgreSQL in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/tests/Elsa/Studio/Preferences/Persistence/Groundwork/Tests/`
- [X] T008 [P] Add clean-break Dashboard Widget SDK contract tests in `src/Elsa.Studio.Web/Client/src/__tests__/registry.test.ts` and `src/Elsa.Studio.Dashboard/Client/src/__tests__/widgetContract.test.tsx`
- [X] T009 [P] Add Studio runtime host-ID and auth-disabled session tests in `tests/Elsa.Studio.Tests/StudioRuntimeScriptTests.cs` and `src/Elsa.Studio.Web/Client/src/__tests__/app-auth.test.tsx`
- [X] T010 Implement registered namespace, document, quota, revision, validation, Dashboard/Attention namespace definitions, and store contracts in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Elsa/Studio/Preferences/Core/`
- [X] T011 Implement authenticated GET/conditional PUT preference endpoints in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Elsa/Studio/Preferences/Api/`
- [X] T012 Implement Groundwork preference manifests/repository and SQLite/PostgreSQL composition in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Elsa/Studio/Preferences/Persistence/Groundwork/` and `src/Elsa/Persistence/Groundwork/Unified/GroundworkUnifiedManifest.cs`
- [X] T013 Add stable `Studio:HostId` and Dashboard runtime defaults to `src/Elsa.Studio.Web/StudioRuntimeScript.cs`, `src/Elsa.Studio.Web/Client/src/app/runtime.ts`, and Studio appsettings
- [X] T014 Ensure auth-disabled rendering supplies an explicit anonymous session in `src/Elsa.Studio.Web/Client/src/auth/` and scope helpers include host/backend/subject/tenant identity
- [X] T015 Move generic setting editor selection and built-in editors from `src/Elsa.Studio.FeatureManagement/Client/src/module.tsx` into mandatory shared infrastructure under `src/Elsa.Studio.Web/StudioUi/src/`
- [X] T016 Replace `StudioDashboardWidgetContribution` in `src/Elsa.Studio.Web/Client/src/sdk/index.ts` with the host-framed optional-loader/settings contract from `contracts/dashboard-widget-sdk.md`
- [X] T017 Implement the scoped Preferences API client and device-local fallback in `src/Elsa.Studio.Dashboard/Client/src/dashboardPreferences.ts`
- [X] T018 Implement managed widget loading, cancellation, timeout, cache/refresh timing, and isolated runtime states in `src/Elsa.Studio.Dashboard/Client/src/useDashboardWidgets.ts`
- [X] T019 Implement host-owned frame, semantic grid, settings dialog, and token CSS in `src/Elsa.Studio.Dashboard/Client/src/WidgetFrame.tsx`, `DashboardPage.tsx`, and `styles.css`
- [X] T020 Register `/`, `/dashboard`, and `/overview` navigation/routes from `src/Elsa.Studio.Dashboard/Client/src/module.tsx` and remove Dashboard-specific rendering from `src/Elsa.Studio.Web/Client/src/app/App.tsx`
- [X] T021 Delete `src/Elsa.Studio.Samples.Dashboard/` and remove all sample feature, workspace, manifest, test, and configuration references
- [X] T022 Run focused Slice 1 tests and builds for Foundation Preferences, `@elsa-workflows/studio-dashboard`, Studio Web, and `tests/Elsa.Studio.Tests/Elsa.Studio.Tests.csproj`

## Phase 3: User Story 1 — See What Requires Attention (P1)

**Independent test**: Seed authorized workflow, secret, and module conditions; verify a bounded correlated queue, partial failure, navigation, snooze, escalation, permissions, and all-clear behavior.

- [X] T023 [P] [US1] Add Attention Core aggregation, bounds, timeout, cancellation, authorization, discovery, and partial-result tests in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/tests/Elsa/Attention/Core/Tests/`
- [X] T024 [P] [US1] Add Attention endpoint request/error/security tests in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/tests/Elsa/Attention/Api/Tests/`
- [X] T025 [P] [US1] Add tenant-isolated Secrets repository and migration tests in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/tests/Elsa/Secrets/`
- [X] T026 [P] [US1] Add contributor contract tests in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/tests/Elsa/Workflows/Runtime/Attention/Tests/`, `tests/Elsa/Secrets/Attention/Tests/`, and `tests/Elsa/Modularity/Attention/Tests/`
- [X] T027 [P] [US1] Add Attention widget tests for correlation, filters, snooze, escalation, partial failure, bounds, all-clear, permissions, and sizes in `src/Elsa.Studio.Attention/Client/src/__tests__/`
- [X] T028 [US1] Implement normalized Attention models, contributor registry, aggregation service, options, validation, and permission evaluation in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Elsa/Attention/Core/`
- [X] T029 [US1] Implement the filtered generic Attention endpoint and per-contributor envelopes in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Elsa/Attention/Api/`
- [X] T030 [US1] Add authoritative `TenantId` to Secrets models/contracts and tenant-filter all repository/API operations in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Elsa/Secrets/`
- [X] T031 [US1] Implement explicit legacy-secret tenant migration/backfill policy in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Elsa/Secrets/Persistence/Groundwork/`
- [X] T032 [P] [US1] Implement complete bounded Workflow Runtime incident/failure contributor in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Elsa/Workflows/Runtime/Attention/`
- [X] T033 [P] [US1] Implement expired/revoked/soon-expiring Secrets contributor in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Elsa/Secrets/Attention/`
- [X] T034 [P] [US1] Implement failed/incompatible/diagnostic-bearing Modularity contributor in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Elsa/Modularity/Attention/`
- [X] T035 [US1] Register Attention Core/API and contributors independently in Foundation Server and expose the optional Studio-host Attention capability through `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Apps/Elsa.Server/Program.cs`, `Elsa.Server.csproj`, `shells.json`, and Studio runtime composition. The Studio host defaults the optional local API off; remote Foundation APIs remain independently addressable.
- [X] T036 [US1] Implement Attention API client, normalized merge/correlation/sort logic, and scoped query keys in `src/Elsa.Studio.Attention/Client/src/attentionApi.ts`
- [X] T037 [US1] Implement versioned Attention snoozes through Studio Preferences in `src/Elsa.Studio.Attention/Client/src/attentionPreferences.ts`
- [X] T038 [US1] Implement wide/full Attention widget UI with All/Critical/Snoozed filters and navigation-only actions in `src/Elsa.Studio.Attention/Client/src/AttentionWidget.tsx`
- [X] T039 [US1] Register the ordinary Attention Dashboard Widget and module manifest in `src/Elsa.Studio.Attention/Client/src/module.tsx` and `src/Elsa.Studio.Attention/Handlers/`
- [X] T040 [US1] Run focused Slice 2 commands documented in `specs/005-dashboard-attention/quickstart.md` and record results in `specs/005-dashboard-attention/tasks.md`

## Phase 4: User Story 2 — Monitor Workflow Run Health (P2)

**Independent test**: Seed more runs than one page across outcomes/incidents/test scopes and DST boundaries; verify exact range totals, rates, buckets, current running count, and top failing definitions.

- [X] T041 [P] [US2] Add provider-neutral Run Health contract fixtures and endpoint tests in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/tests/Elsa/Workflows/Dashboard/Tests/`
- [X] T042 [P] [US2] Add in-memory and provider-dialect Groundwork run aggregate adapter coverage under `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/tests/Elsa/Workflows/Dashboard/Tests/`; the SQLite acceptance fixture persists 125 executions and the shared bounded SQL adapter supplies SQLite/PostgreSQL dialects.
- [X] T043 [P] [US2] Add Studio Run Health loader/settings/rendering tests in `src/Elsa.Studio.Workflows.Dashboard/Client/src/__tests__/runHealth.test.tsx`
- [X] T044 [US2] Implement run snapshot models, validation, outcome mapping, range/time-zone buckets, rates, and top-failure query contract in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Elsa/Workflows/Dashboard/`
- [X] T045 [US2] Implement exact in-memory and Groundwork run adapters in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Elsa/Workflows/Dashboard/` and `src/Elsa/Workflows/Dashboard/Persistence/Groundwork/`, wired through unified persistence composition
- [X] T046 [US2] Add provider-neutral execution-origin/test-run data in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Elsa/Workflows/Runtime/Core/Models/` and matching Groundwork persistence mappings
- [X] T047 [US2] Implement the independently authorized Run Health endpoint in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Elsa/Workflows/Dashboard/`
- [X] T048 [US2] Implement Run Health API client, 24h/7d/30d settings, loader, KPI/trend/top-failure body, and medium/wide/full layouts in `src/Elsa.Studio.Workflows.Dashboard/Client/src/`
- [X] T049 [US2] Register the Workflow Run Health Dashboard Widget in `src/Elsa.Studio.Workflows.Dashboard/Client/src/module.tsx`
- [X] T050 [US2] Run focused Run Health commands documented in `specs/005-dashboard-attention/quickstart.md` and record results in `specs/005-dashboard-attention/tasks.md`

## Phase 5: User Story 3 — Understand the Workflow Portfolio (P3)

**Independent test**: Seed active/deleted/published/draft/invalid definitions beyond one page and verify exact overlapping counters on every official provider.

- [X] T051 [P] [US3] Add provider-neutral Portfolio contract fixtures and endpoint tests in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/tests/Elsa/Workflows/Dashboard/Tests/`
- [X] T052 [P] [US3] Add EF Core SQLite and provider-dialect Groundwork Portfolio adapter coverage: EF coverage lives with the existing Design EF test surface, while the Groundwork SQLite 105-definition acceptance fixture exercises the shared SQLite/PostgreSQL adapter.
- [X] T053 [P] [US3] Add Studio Portfolio loader/rendering tests in `src/Elsa.Studio.Workflows.Dashboard/Client/src/__tests__/portfolio.test.tsx`
- [X] T054 [US3] Implement Portfolio snapshot/query contracts and bounded derived-validation orchestration in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Elsa/Workflows/Dashboard/`
- [X] T055 [US3] Implement exact in-memory, EF Core SQLite, and Groundwork SQLite/PostgreSQL Portfolio adapters in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Elsa/Workflows/Dashboard/`, `src/Elsa/Workflows/Design/Persistence/EFCore/`, and unified Groundwork composition without expanding the EF project surface
- [X] T056 [US3] Implement the independently authorized Workflow Portfolio endpoint in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Elsa/Workflows/Dashboard/`
- [X] T057 [US3] Implement Portfolio API client, loader, overlapping KPI body, and small/medium layouts in `src/Elsa.Studio.Workflows.Dashboard/Client/src/`
- [X] T058 [US3] Register the Workflow Portfolio Dashboard Widget in `src/Elsa.Studio.Workflows.Dashboard/Client/src/module.tsx`
- [X] T059 [US3] Run focused Portfolio commands documented in `specs/005-dashboard-attention/quickstart.md` and record results in `specs/005-dashboard-attention/tasks.md`

## Phase 6: User Story 4 — Personalize a Reliable Dashboard (P4)

**Independent test**: Reorder, resize, hide, restore, configure, and refresh widgets across sessions/scopes; verify Host Policy, migration fallback, dynamic module lifecycle, and accessible responsive states.

- [X] T060 [P] [US4] Add Dashboard preference concurrency, migration-reset, unknown-widget, auto-add, host-pin, and cross-scope tests in `src/Elsa.Studio.Dashboard/Client/src/__tests__/preferences.test.tsx` (covered in `widgetContract.test.tsx` to reuse the SDK fixture without duplicate setup)
- [X] T061 [P] [US4] Add keyboard layout, responsive frame, inactive-refresh, per-widget retry, and dynamic registry lifecycle tests in `src/Elsa.Studio.Dashboard/Client/src/__tests__/DashboardPage.test.tsx`
- [X] T062 [US4] Implement keyboard move/resize/hide/restore controls, hidden-widget manager, and new-widget auto-add preference in `src/Elsa.Studio.Dashboard/Client/src/DashboardPage.tsx`
- [X] T063 [US4] Implement host-pinned visibility and settings migration/reset notices in `src/Elsa.Studio.Dashboard/Client/src/dashboardPreferences.ts` and `WidgetFrame.tsx`
- [X] T064 [US4] Implement global/per-widget refresh controls, last/next refresh labels, inactive-surface suspension, and scoped cleanup in `src/Elsa.Studio.Dashboard/Client/src/useDashboardWidgets.ts`
- [X] T065 [US4] Verify module reload removes widgets/aborts loaders and returns retained preferences through `src/Elsa.Studio.Web/Client/src/app/App.tsx` registry rebuild integration tests
- [X] T066 [US4] Run focused Dashboard personalization commands documented in `specs/005-dashboard-attention/quickstart.md` and record results in `specs/005-dashboard-attention/tasks.md` (14 tests, typecheck, production build, and CSS policy pass on 2026-07-13)

## Phase 7: Polish, tracking, and full verification

- [X] T067 Update public Dashboard Widget SDK/module documentation and remove obsolete sample references in `README.md`, `CONTEXT.md`, SDK declarations, and relevant docs
- [X] T068 Create and link deferred GitHub issue seeds listed in `specs/005-dashboard-attention/spec.md` (#316–#332), plus the requested outdated architecture-tour documentation follow-up (#333); all link back to #315.
- [X] T069 Run Foundation Release build and full test solution from `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/` (Release build passed; all changed feature and architecture suites passed. The full solution has one unrelated HTTP timeout test failure, reproduced identically from unchanged `f152659d` in an isolated worktree.)
- [X] T070 Run Studio typecheck, lint, tests, build, and `tests/Elsa.Studio.Tests` from `/Users/sipke/.codex/worktrees/d246/elsa-foundation-studio/` (all passed on 2026-07-13; lint reported 49 existing warnings and zero errors; .NET 381/381 passed)
- [X] T071 Run real-browser `/dashboard` proof for three widgets, all sizes, auth/scope, preferences, refresh, partial failure, snooze, and exact seeded aggregates
- [X] T072 Run up to five self-review/fix iterations per slice and record final acceptance evidence in `specs/005-dashboard-attention/tasks.md` and linked GitHub issues
- [X] T073 Complete requirement-by-requirement audit against `specs/005-dashboard-attention/spec.md` and mark only proven tasks complete

## Final acceptance evidence (2026-07-13)

- Foundation focused suites: Attention Core 10/10, Attention API 4/4, Studio Preferences 16/16, Workflows Dashboard 14/14, moved EF Portfolio acceptance 1/1, Architecture 69/69; Release Server build completed with zero warnings and zero errors.
- Foundation full solution: every changed feature suite and the remainder of the solution passed except `SyncEndpoint_RequestTimeoutExceeded_Replies408_AndTheInstanceRemainsValid`; the same test failed from an isolated clean worktree at unchanged commit `f152659d`, proving it is baseline debt rather than a dashboard regression.
- Studio complete gate: typecheck, JavaScript/CSS lint (zero errors), all workspace Vitest suites, all production builds, and Elsa.Studio.Tests 381/381 passed.
- Browser proof used a locally seeded backend with 105 workflow definitions and 125 workflow runs. It rendered exactly Attention, Workflow Portfolio, and Workflow Run Health; verified exact counters, independent refresh requests, range/test-run settings, partial contributor failure, correlation, snooze persistence, hide/restore, reorder/resize persistence, responsive full-width layout, semantic headings, and accessible control names with no console or request errors.
- Review fixes included sharing the SDK auth context across host/runtime modules, explicit `/_elsa/*` 404 handling, optional host-Attention capability negotiation, widget-size propagation, wide/full Attention bounds, valid design-token CSS, pure Core dependency boundaries, and preserving the shrink-only EF surface ratchet.
- FR-001 through FR-071 and SC-001 through SC-010 were audited against the implementation, focused tests, architecture suite, and browser evidence. Deferred product extensions remain captured as #316–#332; outdated documentation correction is explicitly captured as #333.

## Dependencies

```text
Setup -> Foundational Dashboard/Preferences
Foundational -> US1 Attention
Foundational -> US2 Run Health
Foundational -> US3 Portfolio
US1 + US2 + US3 -> US4 complete personalized composition proof
All stories -> Polish/full verification/audit
```

US2 and US3 may proceed in parallel after the foundational slice because their
snapshot resources, adapters, loaders, and tests are independent. US1 depends on
Preferences for snoozes. US4 finishes behavior introduced in the foundational
slice and validates all three widgets together.

## Parallel execution examples

- T006–T009: Foundation preference tests, SDK contract tests, and host runtime tests.
- T023–T027: Attention Core/API, Secrets tenancy, satellite, and Studio widget tests.
- T032–T034: Three Attention satellite implementations after Core is ready.
- T041–T043 and T051–T053: backend provider and Studio widget tests for the two Workflow snapshots.
- US2 and US3 implementation streams after T022.
- T060–T061: preference and Dashboard interaction test suites.

## Implementation strategy

1. Complete Slice 1 and prove a real empty/customizable Dashboard with canonical preferences.
2. Complete Slice 2 and prove Attention end-to-end with three contributors.
3. Complete Slice 3 by implementing Run Health and Portfolio independently, then compose all widgets.
4. Finish personalization/accessibility hardening, full suites, browser proof, and completion audit.

No technical-only slice is treated as completion. Each slice must end in an
independently demonstrable user outcome and a bounded self-review loop.
