# Feature Specification: Elsa Studio UI System

**Feature Branch**: `001-ui-system`

**Created**: 2026-06-16

**Status**: Draft

**Input**: User description: "Define and implement an extensible Elsa Studio UI system for modular admin pages. Establish a Spec Kit constitution and initial spec, then prioritize feature management and module management screens, including backend modules as required."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Shared Workbench Foundation (Priority: P1)

As a Studio module author, I want a stable set of shared UI patterns and components so that any contributed route, panel, widget, settings editor, or diagnostics view looks and behaves like part of Elsa Studio without copying one-off host CSS.

**Why this priority**: The product is modular. Without a shared foundation, every module will drift visually and interaction-wise as new screens are added.

**Independent Test**: A sample module page can be built using only documented shared Studio primitives and tokens, with no module-specific typography, color, shell, toolbar, status, form, or inspector styling beyond approved component props.

**Acceptance Scenarios**:

1. **Given** a module needs to present a resource list, **When** it uses the shared UI system, **Then** it can render a native-feeling page with shell alignment, page header, toolbar, data grid/list, status chips, empty/loading/error states, and optional inspector.
2. **Given** a module needs to present configuration settings, **When** it uses the shared UI system, **Then** it can render grouped fields, validation messages, switches, selects, text/secret/json inputs, dirty state, and sticky actions without custom visual language.
3. **Given** a module contributes diagnostics or logs, **When** it uses the shared UI system, **Then** it can render dense rows, severity/status states, timelines, and bottom-panel content that match the host.

---

### User Story 2 - Feature Management Workbench Screen (Priority: P2)

As an operator configuring Studio features, I want feature management to use the shared split-inspector language so I can scan feature packages, enable or disable them, edit contributed settings, understand validation errors, and apply changes confidently.

**Why this priority**: Feature management already exists and is the first real proving ground for module-contributed settings and editor extensibility.

**Independent Test**: The feature management page can be opened with a representative catalog containing enabled, disabled, advanced, experimental, invalid, and read-error features; users can inspect and modify one feature without losing scanability of the list.

**Acceptance Scenarios**:

1. **Given** features are loaded, **When** the user selects a feature row, **Then** the inspector shows source, package metadata, enabled state, settings, validation, dependencies, and apply/reset actions using shared primitives.
2. **Given** a feature has dirty configuration changes, **When** the user navigates within the feature management screen, **Then** the UI continues to show unsaved state and exposes Apply and Reset actions predictably.
3. **Given** a feature has backend read or validation errors, **When** the user opens it, **Then** errors are displayed through shared alert and field validation components without breaking layout.

---

### User Story 3 - Module Management And Backend Modules (Priority: P3)

As a developer or operator inspecting Studio modularity, I want a module management screen that shows frontend and backend modules, manifests, compatibility, contributions, routes, endpoints, diagnostics, and load state so I can understand what the shell discovered and why something did or did not load.

**Why this priority**: Module management is the second concrete screen requested after the UI foundation and feature management. It proves the design system across backend-backed resource grids, contribution inspection, and diagnostics.

**Independent Test**: A representative module registry containing built-in, frontend-only, backend-scoped, disabled, failed, and incompatible modules can be inspected through one resource grid and one module inspector without custom visual styling.

**Acceptance Scenarios**:

1. **Given** the host discovers Studio modules, **When** the user opens Modules, **Then** the screen lists module name, source, version, SDK compatibility, backend/frontend scope, load status, contributed routes/panels/widgets/settings, and last load information.
2. **Given** a selected backend module exposes endpoints or diagnostics, **When** the user opens the module inspector, **Then** the inspector shows endpoint/contribution metadata and diagnostic events using shared tabs, lists, status chips, and alert patterns.
3. **Given** a module is failed, incompatible, or disabled, **When** the user filters the module grid, **Then** the state remains readable and actionable through shared severity and empty-state patterns.

---

### User Story 4 - Shell Command Center Behavior (Priority: P4)

As a frequent Studio user, I want the shell to provide consistent global search, command placement, environment/backend status, theme controls, and source links so that module screens do not invent their own top-level navigation or actions.

**Why this priority**: Command Center behavior makes the system feel cohesive, but it can be delivered after the core workbench primitives and first two real screens.

**Independent Test**: Overview, Features, Modules, and Diagnostics screens use the same shell command areas for global actions while keeping page-specific actions in the shared toolbar.

**Acceptance Scenarios**:

1. **Given** the user is on any Studio route, **When** they inspect the top command bar, **Then** global search, backend/environment status, theme, and source actions appear consistently.
2. **Given** a page has local actions, **When** the page renders, **Then** local actions appear in the page toolbar rather than creating a competing shell-level command group.

### Follow-up Story - Workflow Designer Upgrade (Backlog)

As a workflow author, I want the React Flow designer to support mature flowchart editing interactions so that I can create, connect, insert, move, delete, reconnect, and persist activities without losing source ports, layout, vertices, or existing workflow JSON metadata.

**Why this is follow-up**: The current UI-system scope remains focused on shared Workbench primitives, Feature Management, Module Management, and shell command behavior. The workflow designer is already implemented in the Workflows module and should evolve as an interaction-quality follow-up rather than displacing the existing priority order.

**Independent Test**: A draft flowchart can round-trip through designer operations including connect existing nodes, connect-to-create, edge insert/splice, palette drop on edge, delete/reconnect edge, save/autosave, and keyboard picker selection while preserving workflow JSON as the canonical model.

### Edge Cases

- Module-provided UI loads before every optional shared primitive is available.
- A module has no data, no settings, or no diagnostics.
- A module returns invalid setting metadata, missing package metadata, or incompatible SDK information.
- A page has many rows, many columns, long identifiers, long validation messages, or deeply nested JSON settings.
- The bottom panel is open, resized, or unavailable on smaller viewports.
- Dark mode and light mode both need readable semantic statuses and focus states.
- Module-specific CSS accidentally overrides shared Studio tokens.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST define a shared Elsa Studio UI design language based on the Modular Admin Workbench direction: Minimal Data Grid for resource information architecture, Split Inspector for detail and configuration workflows, and Command Center behavior in the shell.
- **FR-002**: System MUST expose stable shared primitives for shell, navigation, page header, toolbar, search/filter controls, summary strip, data grid/list, inspector, drawer, tabs, forms/settings, alerts, status chips, empty/loading/error states, and bottom console/log panels.
- **FR-003**: System MUST document the intended page archetypes: resource index, master/detail, configuration, overview, diagnostics/logs, and specialized extensions.
- **FR-004**: System MUST define token usage for typography, color, spacing, border, radius, shadow, focus ring, and semantic status states.
- **FR-004a**: Resource and configuration screens MUST use compact admin typography: shell/page titles around 18-22px, section headings around 14-17px, row/body text around 13px, labels around 10-12px, and medium weights instead of heavy display weights.
- **FR-004b**: Resource lists MUST render as dense rows or data grids with selected states, not as stacked large cards. Cards are reserved for repeated dashboard widgets, compact metadata blocks, and empty/error states.
- **FR-004c**: The default visual surface MUST be true white or cool neutral with hairline borders, minimal shadow, 5-8px radii, restrained blue/cyan selection color, and semantic green/amber/red statuses.
- **FR-005**: Module authors MUST be able to build native-feeling contributed pages without copying host shell CSS or inventing local typography and status styles.
- **FR-006**: Feature management MUST be refactored or implemented as a split-inspector configuration screen using shared primitives.
- **FR-007**: Module management MUST include frontend and backend module visibility, manifests, compatibility, contributions, diagnostics, and load state.
- **FR-008**: Data-heavy screens MUST support readable long identifiers, row selection, filtering/search, loading, empty, error, and pagination or virtualized scrolling when row counts require it.
- **FR-009**: Configuration screens MUST support dirty state, validation, advanced settings, disabled settings, secret values, JSON/text areas, reset/apply actions, and backend error reporting.
- **FR-010**: Diagnostic and operations screens MUST support severity/status chips, timestamps, dense event rows, timeline/list views, and bottom-panel or inspector rendering.
- **FR-011**: Shared interactive primitives MUST provide keyboard operation, visible focus, accessible labeling, disabled/loading states, and screen-reader semantics.
- **FR-012**: The implementation MUST include focused tests for shared behavior and representative real-screen usage, not only static component snapshots.
- **FR-013**: Browser verification MUST cover desktop and responsive layouts for each real screen migrated to the UI system.
- **FR-014**: The spec backlog MUST record feature management as the first follow-up implementation screen and module management as the second, including backend modules as required.

### Key Entities *(include if feature involves data)*

- **Studio UI Token**: A named design value for typography, color, spacing, radius, border, shadow, focus, or semantic status.
- **Studio UI Primitive**: A reusable component or pattern with stable props and documented states.
- **Page Archetype**: A reusable page composition such as resource index, master/detail, configuration, overview, or diagnostics/logs.
- **Module Contribution**: A module-provided route, navigation item, panel, widget, setting editor, endpoint, or diagnostic entry.
- **Feature Catalog Item**: A configurable feature with metadata, enabled state, settings, package information, validation/read errors, and advanced/experimental flags.
- **Studio Module Registry Item**: A frontend or backend module record with source, version, SDK compatibility, load status, manifest details, contributions, endpoints, and diagnostics.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A new module page can be assembled from documented shared primitives without custom page-level CSS for shell, page header, toolbar, status, form, or inspector styling.
- **SC-002**: Feature management and module management share the same visual language for search/filter toolbars, selected rows, inspectors, tabs, alerts, and status chips.
- **SC-003**: Page titles, table rows, controls, labels, and inspector chrome stay within the compact typography ranges defined by the UI system tokens.
- **SC-003a**: In the first desktop viewport, Feature Management and Module Management show their primary list/grid plus inspector without oversized cards, hero-scale headings, or decorative gradients.
- **SC-004**: Keyboard users can reach and operate shared navigation, toolbar actions, row selection, tabs, form controls, drawer/inspector actions, and bottom-panel tabs.
- **SC-005**: Light and dark themes preserve readable foreground/background contrast, status contrast, and focus visibility for all shared primitives.
- **SC-006**: At least one representative module-authored screen demonstrates native UI composition without importing private host CSS selectors.

## Assumptions

- The existing Studio shell remains the delivery surface.
- The UI system will be adopted incrementally rather than replacing every screen in one change.
- The implementation can add focused frontend dependencies when justified by the plan, but the design language remains Elsa Studio-owned.
- Backend module management may require new API surface beyond the current manifest endpoint.
- The user strongly approved the exploratory mockups; stakeholder note: the mockups are considered "fire" and should guide visual ambition without becoming static screenshots to copy.
- The first two implementation priorities after the foundation are feature management and module management, with backend modules included as required.
- Latest visual direction: match the approved Workbench mockups with flatter surfaces, compact row-first lists, slim command/header chrome, cool neutral color, and subdued shadows.
