# Tasks: Dashboard Widgets and Attention

**Studio root**: `/Users/sipke/.codex/worktrees/d246/elsa-foundation-studio`

**Foundation root**: `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation`

## Phase 1: Setup and module scaffolding

- [ ] T001 Add Studio Dashboard, Attention, and Workflows Dashboard projects/packages to `Elsa.Studio.slnx`, `pnpm-workspace.yaml`, `src/Elsa.Studio.Web/Elsa.Studio.Web.csproj`, and `src/Elsa.Studio.Web/Program.cs`
- [ ] T002 [P] Scaffold `src/Elsa.Studio.Dashboard/`, `src/Elsa.Studio.Attention/`, and `src/Elsa.Studio.Workflows.Dashboard/` with feature, manifest handler, client package, Vite, TypeScript, and test configuration
- [ ] T003 Add Foundation Attention, Studio Preferences, Attention satellite, and Workflows Dashboard projects to `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/Elsa.Server.slnx`, `Elsa.Server.test.slnf`, and `src/Apps/Elsa.Server/Elsa.Server.csproj`
- [ ] T004 [P] Scaffold Foundation projects under `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Elsa/Attention/`, `src/Elsa/Studio/Preferences/`, `src/Elsa/Workflows/Runtime/Attention/`, `src/Elsa/Secrets/Attention/`, `src/Elsa/Modularity/Attention/`, and `src/Elsa/Workflows/Dashboard/`
- [ ] T005 Update Studio and Foundation first-party feature catalogs and shell configuration in `src/Elsa.Studio.Web/shells.json`, `src/Elsa.Studio.Web/appsettings.json`, and `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Apps/Elsa.Server/shells.json`

## Phase 2: Foundational Dashboard and Preferences

**Goal**: Establish the clean-break widget host and governed preferences required by every user story.

- [ ] T006 [P] Add preference namespace/store/API contract tests in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/tests/Elsa/Studio/Preferences/Tests/`
- [ ] T007 [P] Add Groundwork preference persistence contract tests for SQLite and PostgreSQL in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/tests/Elsa/Studio/Preferences/Persistence/Groundwork/Tests/`
- [ ] T008 [P] Add clean-break Dashboard Widget SDK contract tests in `src/Elsa.Studio.Web/Client/src/__tests__/registry.test.ts` and `src/Elsa.Studio.Dashboard/Client/src/__tests__/widgetContract.test.tsx`
- [ ] T009 [P] Add Studio runtime host-ID and auth-disabled session tests in `tests/Elsa.Studio.Tests/StudioRuntimeScriptTests.cs` and `src/Elsa.Studio.Web/Client/src/__tests__/app-auth.test.tsx`
- [ ] T010 Implement registered namespace, document, quota, revision, validation, Dashboard/Attention namespace definitions, and store contracts in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Elsa/Studio/Preferences/Core/`
- [ ] T011 Implement authenticated GET/conditional PUT preference endpoints in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Elsa/Studio/Preferences/Api/`
- [ ] T012 Implement Groundwork preference manifests/repository and SQLite/PostgreSQL composition in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Elsa/Studio/Preferences/Persistence/Groundwork/` and `src/Elsa/Persistence/Groundwork/Unified/GroundworkUnifiedManifest.cs`
- [ ] T013 Add stable `Studio:HostId` and Dashboard runtime defaults to `src/Elsa.Studio.Web/StudioRuntimeScript.cs`, `src/Elsa.Studio.Web/Client/src/app/runtime.ts`, and Studio appsettings
- [ ] T014 Ensure auth-disabled rendering supplies an explicit anonymous session in `src/Elsa.Studio.Web/Client/src/auth/` and scope helpers include host/backend/subject/tenant identity
- [ ] T015 Move generic setting editor selection and built-in editors from `src/Elsa.Studio.FeatureManagement/Client/src/module.tsx` into mandatory shared infrastructure under `src/Elsa.Studio.Web/StudioUi/src/`
- [ ] T016 Replace `StudioDashboardWidgetContribution` in `src/Elsa.Studio.Web/Client/src/sdk/index.ts` with the host-framed optional-loader/settings contract from `contracts/dashboard-widget-sdk.md`
- [ ] T017 Implement the scoped Preferences API client and device-local fallback in `src/Elsa.Studio.Dashboard/Client/src/dashboardPreferences.ts`
- [ ] T018 Implement managed widget loading, cancellation, timeout, cache/refresh timing, and isolated runtime states in `src/Elsa.Studio.Dashboard/Client/src/useDashboardWidgets.ts`
- [ ] T019 Implement host-owned frame, semantic grid, settings dialog, and token CSS in `src/Elsa.Studio.Dashboard/Client/src/WidgetFrame.tsx`, `DashboardPage.tsx`, and `styles.css`
- [ ] T020 Register `/`, `/dashboard`, and `/overview` navigation/routes from `src/Elsa.Studio.Dashboard/Client/src/module.tsx` and remove Dashboard-specific rendering from `src/Elsa.Studio.Web/Client/src/app/App.tsx`
- [ ] T021 Delete `src/Elsa.Studio.Samples.Dashboard/` and remove all sample feature, workspace, manifest, test, and configuration references
- [ ] T022 Run focused Slice 1 tests and builds for Foundation Preferences, `@elsa-workflows/studio-dashboard`, Studio Web, and `tests/Elsa.Studio.Tests/Elsa.Studio.Tests.csproj`

## Phase 3: User Story 1 — See What Requires Attention (P1)

**Independent test**: Seed authorized workflow, secret, and module conditions; verify a bounded correlated queue, partial failure, navigation, snooze, escalation, permissions, and all-clear behavior.

- [ ] T023 [P] [US1] Add Attention Core aggregation, bounds, timeout, cancellation, authorization, discovery, and partial-result tests in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/tests/Elsa/Attention/Core/Tests/`
- [ ] T024 [P] [US1] Add Attention endpoint request/error/security tests in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/tests/Elsa/Attention/Api/Tests/`
- [ ] T025 [P] [US1] Add tenant-isolated Secrets repository and migration tests in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/tests/Elsa/Secrets/`
- [ ] T026 [P] [US1] Add contributor contract tests in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/tests/Elsa/Workflows/Runtime/Attention/Tests/`, `tests/Elsa/Secrets/Attention/Tests/`, and `tests/Elsa/Modularity/Attention/Tests/`
- [ ] T027 [P] [US1] Add Attention widget tests for correlation, filters, snooze, escalation, partial failure, bounds, all-clear, permissions, and sizes in `src/Elsa.Studio.Attention/Client/src/__tests__/`
- [ ] T028 [US1] Implement normalized Attention models, contributor registry, aggregation service, options, validation, and permission evaluation in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Elsa/Attention/Core/`
- [ ] T029 [US1] Implement the filtered generic Attention endpoint and per-contributor envelopes in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Elsa/Attention/Api/`
- [ ] T030 [US1] Add authoritative `TenantId` to Secrets models/contracts and tenant-filter all repository/API operations in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Elsa/Secrets/`
- [ ] T031 [US1] Implement explicit legacy-secret tenant migration/backfill policy in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Elsa/Secrets/Persistence/Groundwork/`
- [ ] T032 [P] [US1] Implement complete bounded Workflow Runtime incident/failure contributor in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Elsa/Workflows/Runtime/Attention/`
- [ ] T033 [P] [US1] Implement expired/revoked/soon-expiring Secrets contributor in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Elsa/Secrets/Attention/`
- [ ] T034 [P] [US1] Implement failed/incompatible/diagnostic-bearing Modularity contributor in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Elsa/Modularity/Attention/`
- [ ] T035 [US1] Register Attention Core/API and contributors independently in Foundation Server and Studio host composition through `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Apps/Elsa.Server/Program.cs`, `Elsa.Server.csproj`, `shells.json`, and Studio `src/Elsa.Studio.Web/Program.cs`/`Elsa.Studio.Web.csproj`
- [ ] T036 [US1] Implement Attention API client, normalized merge/correlation/sort logic, and scoped query keys in `src/Elsa.Studio.Attention/Client/src/attentionApi.ts`
- [ ] T037 [US1] Implement versioned Attention snoozes through Studio Preferences in `src/Elsa.Studio.Attention/Client/src/attentionPreferences.ts`
- [ ] T038 [US1] Implement wide/full Attention widget UI with All/Critical/Snoozed filters and navigation-only actions in `src/Elsa.Studio.Attention/Client/src/AttentionWidget.tsx`
- [ ] T039 [US1] Register the ordinary Attention Dashboard Widget and module manifest in `src/Elsa.Studio.Attention/Client/src/module.tsx` and `src/Elsa.Studio.Attention/Handlers/`
- [ ] T040 [US1] Run focused Slice 2 commands documented in `specs/005-dashboard-attention/quickstart.md` and record results in `specs/005-dashboard-attention/tasks.md`

## Phase 4: User Story 2 — Monitor Workflow Run Health (P2)

**Independent test**: Seed more runs than one page across outcomes/incidents/test scopes and DST boundaries; verify exact range totals, rates, buckets, current running count, and top failing definitions.

- [ ] T041 [P] [US2] Add provider-neutral Run Health contract fixtures and endpoint tests in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/tests/Elsa/Workflows/Dashboard/Tests/`
- [ ] T042 [P] [US2] Add in-memory, Groundwork SQLite, and Groundwork PostgreSQL run aggregate adapter tests under `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/tests/Elsa/Workflows/Dashboard/Tests/Providers/`
- [ ] T043 [P] [US2] Add Studio Run Health loader/settings/rendering tests in `src/Elsa.Studio.Workflows.Dashboard/Client/src/__tests__/runHealth.test.tsx`
- [ ] T044 [US2] Implement run snapshot models, validation, outcome mapping, range/time-zone buckets, rates, and top-failure query contract in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Elsa/Workflows/Dashboard/`
- [ ] T045 [US2] Implement exact run adapters in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Elsa/Workflows/Dashboard/Providers/`, `src/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/`, and `src/Elsa/Persistence/Groundwork/Unified/`
- [ ] T046 [US2] Add provider-neutral execution-origin/test-run data in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Elsa/Workflows/Runtime/Core/Models/` and matching Groundwork persistence mappings
- [ ] T047 [US2] Implement the independently authorized Run Health endpoint in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Elsa/Workflows/Dashboard/`
- [ ] T048 [US2] Implement Run Health API client, 24h/7d/30d settings, loader, KPI/trend/top-failure body, and medium/wide/full layouts in `src/Elsa.Studio.Workflows.Dashboard/Client/src/`
- [ ] T049 [US2] Register the Workflow Run Health Dashboard Widget in `src/Elsa.Studio.Workflows.Dashboard/Client/src/module.tsx`
- [ ] T050 [US2] Run focused Run Health commands documented in `specs/005-dashboard-attention/quickstart.md` and record results in `specs/005-dashboard-attention/tasks.md`

## Phase 5: User Story 3 — Understand the Workflow Portfolio (P3)

**Independent test**: Seed active/deleted/published/draft/invalid definitions beyond one page and verify exact overlapping counters on every official provider.

- [ ] T051 [P] [US3] Add provider-neutral Portfolio contract fixtures and endpoint tests in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/tests/Elsa/Workflows/Dashboard/Tests/`
- [ ] T052 [P] [US3] Add EF Core SQLite and Groundwork SQLite/PostgreSQL Portfolio adapter tests under `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/tests/Elsa/Workflows/Dashboard/Tests/Providers/`
- [ ] T053 [P] [US3] Add Studio Portfolio loader/rendering tests in `src/Elsa.Studio.Workflows.Dashboard/Client/src/__tests__/portfolio.test.tsx`
- [ ] T054 [US3] Implement Portfolio snapshot/query contracts and bounded derived-validation orchestration in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Elsa/Workflows/Dashboard/`
- [ ] T055 [US3] Implement exact in-memory, EF Core SQLite, and Groundwork SQLite/PostgreSQL Portfolio adapters and indexes in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Elsa/Workflows/Dashboard/Providers/`, `src/Elsa/Workflows/Design/Persistence/EFCore/`, and `src/Elsa/Persistence/Groundwork/Unified/`
- [ ] T056 [US3] Implement the independently authorized Workflow Portfolio endpoint in `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/src/Elsa/Workflows/Dashboard/`
- [ ] T057 [US3] Implement Portfolio API client, loader, overlapping KPI body, and small/medium layouts in `src/Elsa.Studio.Workflows.Dashboard/Client/src/`
- [ ] T058 [US3] Register the Workflow Portfolio Dashboard Widget in `src/Elsa.Studio.Workflows.Dashboard/Client/src/module.tsx`
- [ ] T059 [US3] Run focused Portfolio commands documented in `specs/005-dashboard-attention/quickstart.md` and record results in `specs/005-dashboard-attention/tasks.md`

## Phase 6: User Story 4 — Personalize a Reliable Dashboard (P4)

**Independent test**: Reorder, resize, hide, restore, configure, and refresh widgets across sessions/scopes; verify Host Policy, migration fallback, dynamic module lifecycle, and accessible responsive states.

- [ ] T060 [P] [US4] Add Dashboard preference concurrency, migration-reset, unknown-widget, auto-add, host-pin, and cross-scope tests in `src/Elsa.Studio.Dashboard/Client/src/__tests__/preferences.test.tsx`
- [ ] T061 [P] [US4] Add keyboard layout, responsive frame, inactive-refresh, per-widget retry, and dynamic registry lifecycle tests in `src/Elsa.Studio.Dashboard/Client/src/__tests__/DashboardPage.test.tsx`
- [ ] T062 [US4] Implement keyboard move/resize/hide/restore controls, hidden-widget manager, and new-widget auto-add preference in `src/Elsa.Studio.Dashboard/Client/src/DashboardPage.tsx`
- [ ] T063 [US4] Implement host-pinned visibility and settings migration/reset notices in `src/Elsa.Studio.Dashboard/Client/src/dashboardPreferences.ts` and `WidgetFrame.tsx`
- [ ] T064 [US4] Implement global/per-widget refresh controls, last/next refresh labels, inactive-surface suspension, and scoped cleanup in `src/Elsa.Studio.Dashboard/Client/src/useDashboardWidgets.ts`
- [ ] T065 [US4] Verify module reload removes widgets/aborts loaders and returns retained preferences through `src/Elsa.Studio.Web/Client/src/app/App.tsx` registry rebuild integration tests
- [ ] T066 [US4] Run focused Dashboard personalization commands documented in `specs/005-dashboard-attention/quickstart.md` and record results in `specs/005-dashboard-attention/tasks.md`

## Phase 7: Polish, tracking, and full verification

- [ ] T067 Update public Dashboard Widget SDK/module documentation and remove obsolete sample references in `README.md`, `CONTEXT.md`, SDK declarations, and relevant docs
- [ ] T068 Create and link deferred GitHub issue seeds listed in `specs/005-dashboard-attention/spec.md`
- [ ] T069 Run Foundation Release build and full test solution from `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation/`
- [ ] T070 Run Studio typecheck, lint, tests, build, and `tests/Elsa.Studio.Tests` from `/Users/sipke/.codex/worktrees/d246/elsa-foundation-studio/`
- [ ] T071 Run real-browser `/dashboard` proof for three widgets, all sizes, auth/scope, preferences, refresh, partial failure, snooze, and exact seeded aggregates
- [ ] T072 Run up to five self-review/fix iterations per slice and record final acceptance evidence in `specs/005-dashboard-attention/tasks.md` and linked GitHub issues
- [ ] T073 Complete requirement-by-requirement audit against `specs/005-dashboard-attention/spec.md` and mark only proven tasks complete

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
