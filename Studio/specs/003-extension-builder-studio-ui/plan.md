# Implementation Plan: Extension Builder — Studio UI (trusted-team v1)

**Branch**: `sfmskywalker-extension-builder-studio-ui` | **Date**: 2026-06-23 | **Spec**: `specs/003-extension-builder-studio-ui/spec.md`

**Input**: Feature specification from `/specs/003-extension-builder-studio-ui/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Implement a first-party Studio Extension Builder module that lets trusted users create owner-scoped workspaces/projects from templates, edit project files, build server-side packages, promote validated artifacts into the Nuplane-loadable feed, inspect runtime status, and recover through retry/rollback. The implementation will live inside the existing React/Vite/TypeScript Studio shell, reuse shared workbench primitives (`StudioDataGrid`, `StudioTabs`, `StudioToolbar`, `StatusChip`, feedback states, module-management layout classes), and consume the canonical `/_elsa/extension-builder` backend contract through a typed client boundary.

## Technical Context

**Language/Version**: TypeScript 5.6, React 19, Vite 7

**Primary Dependencies**: Existing Studio SDK, React, lucide-react, shared Studio UI primitives; no new runtime dependency for v1 editor unless existing repo tooling already provides one.

**Storage**: Server-side only through `/_elsa/extension-builder`; UI holds transient selected workspace/project/file, dirty editor buffer, build polling/log state, and operation feedback.

**Testing**: Vitest + jsdom in `src/Elsa.Studio.Web/Client`.

**Target Platform**: Elsa Studio web client served by the existing Studio host and configured backend endpoint.

**Project Type**: Frontend web application module inside `src/Elsa.Studio.Web/Client`.

**Performance Goals**: Keep navigation responsive for typical trusted-team workspaces; poll build/runtime state without manual refresh; avoid blocking large log/file renders by using dense scrollable panels.

**Constraints**: UI gates on advisory capability flags but never treats them as a security boundary; owner-scoped last-write-wins; no AI authoring, SaaS sandboxing, or concurrent co-editing in v1; backend contract may not be runnable locally, so tests use typed boundary mocks.

**Scale/Scope**: One integrated route (`/extension-builder`) with workspace/project browser, project editor, build panel, and promote/runtime inspector.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Modular UI contract**: The plan identifies shared Studio primitives used or
  introduced, and explains how module authors consume them without copying host
  CSS internals.
- **Workbench pattern fit**: The selected page archetype is one of resource
  index, master/detail, configuration, overview, diagnostics/logs, or a
  justified extension.
- **Typography and token discipline**: The plan names the token surfaces touched
  and confirms that module-local CSS does not introduce unmanaged fonts, colors,
  radii, shadows, or status treatments.
- **Accessible interaction**: The plan covers keyboard, focus, loading,
  disabled, empty, error, validation, and screen-reader behavior for any changed
  interactive primitives.
- **Real-screen proof**: The plan includes at least one real Studio screen as
  proof of the system change, with tests and browser/visual verification.

**Gate result**: PASS. The feature is implemented as a native workbench screen, not a parallel shell. It reuses shared tokens/components, presents browser/editor/build/runtime as dense operational panels, and validates the route plus key workflows through focused Vitest coverage.

## Project Structure

### Documentation (this feature)

```text
specs/[###-feature]/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)
<!--
  ACTION REQUIRED: Replace the placeholder tree below with the concrete layout
  for this feature. Delete unused options and expand the chosen structure with
  real paths (e.g., apps/admin, packages/something). The delivered plan must
  not include Option labels.
-->

```text
src/Elsa.Studio.Web/Client/
├── src/app/App.tsx
├── src/app/modules/
│   ├── ExtensionBuilderPage.tsx
│   ├── extensionBuilderApi.ts
│   ├── ModuleManagementPage.tsx
│   └── PackageFeedsPage.tsx
├── src/app/styles.css
└── src/__tests__/
    ├── extension-builder.test.tsx
    └── overview.test.tsx
```

**Structure Decision**: Use the existing Studio Web client as the single implementation surface. Add a first-party built-in route/page beside module-management and package-feeds, with a typed API client colocated under `src/app/modules`.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None | N/A | N/A |

## Phase 0 Research

See `specs/003-extension-builder-studio-ui/research.md`.

## Phase 1 Design

See:

- `specs/003-extension-builder-studio-ui/data-model.md`
- `specs/003-extension-builder-studio-ui/contracts/extension-builder-ui-api.md`
- `specs/003-extension-builder-studio-ui/quickstart.md`

## Post-Design Constitution Check

**Gate result**: PASS. The design preserves the modular product contract by adding a route into the existing shell, uses workbench browser/editor/inspector panels over dashboard cards, relies on shared Studio tokens and primitives, keeps controls keyboard-accessible with disabled/feedback states, and proves the implementation with real route tests and narrowed Studio Web validation.
