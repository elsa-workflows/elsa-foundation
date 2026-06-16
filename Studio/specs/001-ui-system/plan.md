# Implementation Plan: Elsa Studio UI System

**Branch**: `001-ui-system` | **Date**: 2026-06-16 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-ui-system/spec.md`

## Summary

Establish an extensible Elsa Studio UI system for modular admin pages. The system
will provide a shared component vocabulary, token contract, page archetypes, and
real-screen migration path. The initial proof sequence is shared UI foundation,
feature management, then module management including backend module visibility as
required.

## Technical Context

**Language/Version**: TypeScript 5.6, React 19, C#/.NET 10 for backend-backed module data

**Primary Dependencies**: Existing React/Vite shell, lucide-react icons, CSS variables/component CSS; planned additions: TanStack Table for grid behavior when the first data grid lands, and headless accessible primitives only where native controls are insufficient

**Storage**: Existing in-memory/client state for shell UI; backend module metadata from Studio API endpoints

**Testing**: Vitest for React/SDK behavior, dotnet test for backend API/provider behavior, browser verification for real screens

**Target Platform**: ASP.NET-hosted modular React Studio shell with same-origin ESM module assets

**Project Type**: Modular web application with host shell, SDK, backend API, and independently built module clients

**Performance Goals**: Keep initial Studio route interactive on typical development hardware; grids must remain responsive with representative module/feature catalogs; defer virtualization until row counts justify it

**Constraints**: Module pages must not depend on private host CSS selectors; UI must support light/dark themes; shell must remain compatible with same-origin module asset loading

**Visual Direction**: Implement the approved Workbench mockup language holistically:
true-white/cool-neutral surfaces, hairline borders, small radii, restrained
blue/cyan selection states, compact row-first resource lists, split inspectors,
and minimal shadow. Avoid oversized cards, heavy display weights, decorative
gradients, and one-off module palettes.

**Scale/Scope**: Foundation plus two real screens: feature management and module management. Additional pages adopt the system incrementally.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Modular UI contract**: PASS. The plan introduces shared primitives and token contracts under the Studio shell, with module-facing consumption documented in contracts.
- **Workbench pattern fit**: PASS. The foundation uses resource index, master/detail, configuration, overview, and diagnostics/log page archetypes.
- **Typography and token discipline**: PASS. Typography, color, radius, spacing, border, shadow, focus, and semantic states are governed through tokens and shared component CSS.
- **Visual-language fidelity**: PASS. The plan requires Feature Management and Module Management to match the latest mockup direction through compact typography, row/grid-first layouts, and flatter surfaces.
- **Accessible interaction**: PASS. Shared controls must cover keyboard, focus, labels, disabled/loading, empty/error, validation, and screen-reader behavior.
- **Real-screen proof**: PASS. Feature management and module management are the first two proof screens after the foundation.

## Project Structure

### Documentation (this feature)

```text
specs/001-ui-system/
├── plan.md
├── spec.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── studio-ui-components.md
│   └── module-management-api.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/Elsa.Studio.Web/Client/src/app/
├── components/
│   ├── ThemeProvider.tsx
│   └── ThemeSwitcher.tsx
├── ui/                         # new shared Studio UI primitives
│   ├── tokens.css
│   ├── shell/
│   ├── layout/
│   ├── data-grid/
│   ├── inspector/
│   ├── forms/
│   ├── feedback/
│   └── diagnostics/
├── App.tsx                     # shell composition consumes shared primitives
└── styles.css                  # reduced to global/reset and legacy bridge

src/Elsa.Studio.Web/Client/src/sdk/
└── index.ts                    # exposes module-facing UI contracts only when stable

src/Elsa.Studio.FeatureManagement/Client/src/
├── module.tsx                  # migrates to shared UI primitives
└── styles.css                  # reduced or removed after migration

src/Elsa.Studio.Api/
├── Models/                     # module management response contracts
└── Services/                   # backend module metadata/manifest aggregation

tests/Elsa.Studio.Tests/
└── *Tests.cs                   # backend module registry/API coverage
```

**Structure Decision**: Keep the shared UI implementation in the host client first
so it can stabilize against real screens. Promote module-facing exports through
the Studio SDK only after component props and token contracts are proven by
feature management and module management.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None | N/A | N/A |

## Phase 0: Research

Research is captured in [research.md](./research.md).

## Phase 1: Design & Contracts

Design artifacts:

- [data-model.md](./data-model.md)
- [contracts/studio-ui-components.md](./contracts/studio-ui-components.md)
- [contracts/module-management-api.md](./contracts/module-management-api.md)
- [quickstart.md](./quickstart.md)

## Post-Design Constitution Check

- **Modular UI contract**: PASS. Component and API contracts explicitly separate host internals from module-facing UI.
- **Workbench pattern fit**: PASS. Contracts define resource index, split inspector, configuration, and diagnostics surfaces.
- **Typography and token discipline**: PASS. Tokens are first-class entities and component contracts reject unmanaged visual styling.
- **Visual-language fidelity**: PASS. The implemented screens are verified against the Workbench direction, with any drift tracked through browser screenshots and CSS audit.
- **Accessible interaction**: PASS. Accessibility states are included in primitive contracts and validation steps.
- **Real-screen proof**: PASS. Tasks require feature management and module management migrations, tests, and browser verification.
