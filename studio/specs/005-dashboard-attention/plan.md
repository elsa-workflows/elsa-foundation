# Implementation Plan: Dashboard Widgets and Attention

**Branch**: `codex/dashboard-attention` | **Date**: 2026-07-13 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/005-dashboard-attention/spec.md`

## Summary

Replace the self-framed sample dashboard with a dedicated, customizable
`Elsa.Studio.Dashboard` module; add governed server-backed Studio Preferences;
add an independent Attention Core/API with domain satellite contributors and an
ordinary Attention Dashboard Widget; and add complete Workflow Portfolio and
Run Health snapshots through backend/provider adapters and a Studio Workflows
Dashboard module. The work spans Elsa Foundation Studio and Elsa Foundation and
is delivered as three independently testable slices.

## Technical Context

**Language/Version**: TypeScript 5.6 / ES2022, React 19.1, C# on .NET 10

**Primary Dependencies**: Vite 7, Vitest 2, TanStack Query 5, Studio SDK/UI,
FastEndpoints through Elsa endpoint bases, CShells features, Groundwork
persistence, existing Elsa authorization/session abstractions

**Storage**: Canonical Studio Preferences in backend persistence with a
Groundwork implementation and optimistic revisions; browser local storage only
as anonymous/unavailable device fallback. Workflow statistics use exact
provider-specific aggregate adapters for in-memory, EF Core SQLite design, and
Groundwork SQLite/PostgreSQL design/runtime providers.

**Testing**: Vitest/React Testing Library patterns in Studio; xUnit/.NET test in
Foundation and Studio host tests; persistence adapter contract tests; browser
runtime verification of the real Dashboard route

**Target Platform**: Browser SPA served by ASP.NET Core Studio host, backed by
Elsa Server on Linux/Windows/macOS development environments

**Project Type**: Modular web application plus reusable backend libraries, APIs,
and persistence adapters across two repositories

**Performance Goals**: Attention contributors default to 5-second timeout;
widget loaders default to 10 seconds; no per-entity fan-out; exact aggregates
operate in bounded memory; Dashboard renders successful widgets independently;
five items per contributor and twenty Attention members globally

**Constraints**: Clean-break Dashboard Widget SDK; no hidden polling or live
streams while Dashboard is inactive; backend authorization remains authoritative;
no secret/payload content in Attention or Preferences; module CSS uses only
`--studio-*` tokens; keyboard controls are the baseline for reorder/resize;
domain cores remain independent of Dashboard and Attention

**Scale/Scope**: Three first-party widgets, three initial Attention contributors,
two workflow snapshot resources, two registered preference namespaces, exact
aggregates beyond normal list-page size, all officially supported persistence
lanes, and legacy sample removal

## Constitution Check

*GATE: Pass before research and after design.*

- **Modular UI contract**: Pass. Dashboard owns the frame and shared lifecycle;
  modules contribute typed metadata, optional loaders/settings, and body UI.
  Generic setting editors move out of optional Feature Management into mandatory
  shared infrastructure.
- **Workbench pattern fit**: Pass. Dashboard is the constitution-approved
  overview/widget surface; Attention is a dense ranked queue inside one widget,
  not a card-based resource-management page.
- **Typography and token discipline**: Pass. New Dashboard, Attention, and
  Workflows Dashboard CSS use `--studio-*` tokens and shared status, setting,
  loading, empty, error, and focus primitives.
- **Accessible interaction**: Pass. Keyboard move/size/hide/restore actions are
  required; pointer drag is not required. Frames own focus, status, retry,
  timestamps, settings, and screen-reader semantics.
- **Real-screen proof**: Pass. `/dashboard` with the three real widgets is the
  proof screen at small, medium, wide, and full sizes, including auth, scope,
  partial-failure, preference, and refresh scenarios.

## Project Structure

### Documentation (this feature)

```text
specs/005-dashboard-attention/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── attention-api.md
│   ├── dashboard-widget-sdk.md
│   ├── studio-preferences-api.md
│   └── workflow-dashboard-api.md
└── tasks.md
```

### Source Code

```text
# Elsa Foundation Studio repository
src/
├── Elsa.Studio.Dashboard/
├── Elsa.Studio.Attention/
├── Elsa.Studio.Workflows.Dashboard/
├── Elsa.Studio.Web/
└── Elsa.Studio.Samples.Dashboard/   # removed by Slice 1

tests/Elsa.Studio.Tests/

# Elsa Foundation repository
src/Elsa/
├── Attention/{Core,Api}/
├── Studio/Preferences/{Core,Api,Persistence/Groundwork}/
├── Workflows/Runtime/Attention/
├── Secrets/Attention/
├── Modularity/Attention/
└── Workflows/Dashboard/

tests/Elsa/
├── Attention/{Core,Api}/Tests/
├── Studio/Preferences/Tests/
└── Workflows/Dashboard/Tests/
```

**Structure Decision**: Dashboard and Studio adapter modules live in the Studio
repository; reusable contracts, APIs, durable preferences, domain Attention
satellites, and workflow aggregate adapters live in Foundation. Backend domain
cores remain independent except for required provider-neutral fields/query
ports; presentation translation stays in satellite modules.

## Delivery Slices

1. **Slice 1 — Dashboard Widgets and Studio Preferences**: governed preference
   storage/API, stable Studio host identity, anonymous fallback, dedicated
   Dashboard module, clean-break widget contract, host frames/lifecycle/layout,
   shared setting editors, legacy aliases, and sample deletion.
2. **Slice 2 — Attention**: Core/API, two-host aggregation, Workflow Runtime,
   Secrets, and Modularity satellites, Secrets tenant boundary, snoozes, and
   `Elsa.Studio.Attention` widget.
3. **Slice 3 — Workflow Dashboard**: exact provider adapters, definition/run
   snapshot resources, and Portfolio/Run Health widgets.

## Phase 0 Research Decisions

See [research.md](research.md). All technical unknowns are resolved; there are
no `NEEDS CLARIFICATION` markers.

## Phase 1 Design Artifacts

- [data-model.md](data-model.md)
- [contracts/dashboard-widget-sdk.md](contracts/dashboard-widget-sdk.md)
- [contracts/attention-api.md](contracts/attention-api.md)
- [contracts/studio-preferences-api.md](contracts/studio-preferences-api.md)
- [contracts/workflow-dashboard-api.md](contracts/workflow-dashboard-api.md)
- [quickstart.md](quickstart.md)

## Constitution Check Re-evaluation

- **Modular UI contract**: Still pass. Public contracts and host-owned frames
  make consistent behavior mandatory rather than sample convention.
- **Workbench pattern fit**: Still pass. No resource management is moved into
  Dashboard; destinations retain their full workbenches.
- **Typography and token discipline**: Still pass. Contracts and tasks require
  shared UI and token-governed module CSS.
- **Accessible interaction**: Still pass. Contract and quickstart cover keyboard
  layout controls, settings, refresh, failure, empty, and all-clear behavior.
- **Real-screen proof**: Still pass. Quickstart requires the real route and all
  three widgets against real backend endpoints.

## Complexity Tracking

No constitution violations require justification.
