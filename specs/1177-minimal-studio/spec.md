# Feature Specification: Minimal Studio presets

**Feature Branch**: `agent/codex/3515-minimal-studio`
**Created**: 2026-10-31
**Status**: Implemented — delivery gates tracked on PR #3517
**Input**: Owner-approved Porcelain, Atelier and Graphite mockups; “Deliver this end to end.”
**Issue**: [#3515](https://github.com/elsa-workflows/elsa-foundation/issues/3515)
**Program**: [Elsa 4 Simplification](../../docs/program-goals/elsa-4-simplification.md), #2559

## User Scenarios & Testing

### User Story 1 - Choose a complete minimal preset (Priority: P1)

An author chooses Porcelain, Atelier or Graphite through Appearance and gets the corresponding light-navigation/light-workspace, dark-navigation/light-workspace or soft-dark presentation. Previews make the choices understandable. Existing themes, custom themes and saved choices remain available.

**Why this priority**: The owner wants all three approved directions, without forcing one on every user.
**Independent Test**: Choose each preset, reload, and verify its surfaces and selected settings; vary workspace, palette, mode and navigation independently.
**Acceptance Scenarios**:
1. Given Appearance, when choosing an approved preset, then the matching neutral surfaces, typography, geometry and restrained accent treatment appear and persist.
2. Given an existing saved built-in or custom theme, when upgrading, then its identity and choices remain valid.
3. Given an explicit user preference and a different host default, when opening Studio, then the user's choice wins; host fallback alone does not become a saved user preference.

### User Story 2 - Work in a visually consistent Studio (Priority: P1)

An author uses lists, filters, settings, cards, dialogs, the designer and inspectors that share the approved visual language across core and extension modules.

**Why this priority**: A theme must cover the whole application rather than one demonstration screen.
**Independent Test**: Exercise workflow definitions, run history, Appearance, run inspection, templates and structured logs, plus an independently rendered module using the public UI package.
**Acceptance Scenarios**:
1. Given each preset, when browsing resources, then names, copyable identifiers, versions, row actions, selection and pagination remain readable and usable with quiet dotted row separators.
2. Given a run with an issue, when inspecting it, then workflow status and incident health remain distinct and actionable without large decorative color fills.
3. Given a settings, catalog or inspector screen, when navigating controls, then the same type scale, spacing, neutral surfaces and control geometry apply.
4. Given an independently contributed module using shared UI roles, when the host changes preset, then its presentation follows the shared contract without private host CSS imports.

### User Story 3 - Use every state and screen size (Priority: P1)

Keyboard users and users on narrow screens or high contrast retain clear focus, navigation, meaningful feedback and access to dense content.

**Why this priority**: Static visual polish must remain usable in real application states.
**Independent Test**: Verify desktop and 320px views, keyboard focus, selection, loading, empty, error, unsupported and read-only states.
**Acceptance Scenarios**:
1. Given narrow width or long identifiers, when viewing a collection or editor, then page controls remain reachable and overflow stays inside its owning table/canvas.
2. Given keyboard focus or high contrast, when navigating, then controls and boundaries remain distinguishable; selection and errors do not rely on color alone.
3. Given source-owned or read-only content, when changing appearance, then permissions, state, graph handles, persistence and undo/redo behavior remain unchanged.

### User Story 4 - Integrate and verify the design (Priority: P2)

Integrators configure sensible defaults and branding and build modules with documented shared roles. Reviewers can compare real screenshots with durable visual references and precise measurements.

**Why this priority**: Modular and white-label hosts need a supported design contract.
**Independent Test**: Build the public package consumer and follow the guide; compare the representative implemented screens against approved references.
**Acceptance Scenarios**:
1. Given the guide, when configuring a host or module, then no product source fork or copied sibling CSS is needed.
2. Given the representative screens at matching viewports, when reviewing screenshots, then navigation contrast, type, geometry, spacing, separators and accent restraint agree with the approved references and recorded measurements.

### Edge Cases

- The older built-in Atelier has a different stable identity and presentation; a new preset must not silently replace saved selections of that theme.
- Custom theme data may arrive asynchronously, and temporary previews must not overwrite persisted choices.
- A populated list and its empty-result state are mutually exclusive; the mockup inset is a component study, not application behavior.
- Missing optional modules or denied access must not be represented as empty resources.
- Long labels, row selection, refresh failures, runtime states, reduced motion and forced colors remain coherent.
- Existing branded/image-led login presentation remains supported; authentication/session lifetimes are outside scope.

## Requirements

### Functional Requirements

- **FR-001**: Appearance MUST offer the three approved directions with useful previews and persistent choices.
- **FR-002**: Workspace/layout, palette, mode, navigation and branding MUST remain independent, and existing built-in/custom theme identities and explicit preferences MUST remain valid.
- **FR-003**: The presentation MUST use a shared sans-serif type scale, softly rounded geometry, neutral surface hierarchy, restrained accents and fine dotted secondary separators.
- **FR-004**: The presentation MUST cover shared shell, pages, controls, lists, filters, pagination, cards, settings, dialogs, editors, inspectors and technical details across core and optional modules.
- **FR-005**: Warnings, failures, focus, selection and runtime evidence MUST remain clear through words/icons and sufficient contrast.
- **FR-006**: The change MUST preserve permissions, read-only/source-owned behavior, workflow state, logical handles, persistence, undo/redo and keyboard navigation.
- **FR-007**: Dense content MUST remain accessible at narrow widths, and high contrast/reduced motion MUST remain supported.
- **FR-008**: Integrator documentation MUST explain preset/default selection, shared module styling, independent appearance choices and continued branding/login support.
- **FR-009**: Real representative screens MUST be compared against the approved mockups using a recorded visual contract and review evidence before delivery.

### Key Entities

- Appearance preset: a coordinated starting presentation, independently adjustable through existing appearance controls.
- Shared visual roles: reusable typography, geometry, surfaces, boundaries, controls and semantic states.
- Visual reference: an approved concept image together with normalized measurements and state-specific acceptance notes.

## Success Criteria

- **SC-001**: All three presets are selectable, survive reload, support mode/layout changes and pass legacy/custom preference preservation scenarios.
- **SC-002**: All six reference screen types have real rendered visual-review evidence; each approved direction is represented in desktop and narrow qualification.
- **SC-003**: Shared collections and controls work in the host and a standalone module consumer, with keyboard interaction and truthful feedback.
- **SC-004**: The visual contract has explicit measurements and a completed comparison for navigation, type, spacing, geometry, dotted boundaries and accent restraint.
- **SC-005**: Whole affected suites, browser checks, consumer builds, architecture/maps, independent review, exact-head CI and resulting-main evidence support delivery; skips and quarantines are reported separately.

## Assumptions

- The three generated directions and eight cross-screen mockups in this task are approved design references. Minor generated inconsistencies are normalized into a single coherent component language.
- Existing full presets remain supported; no database, auth or domain behavior changes are needed.
- Existing shared page/collection composition from #3512 is the foundation; modules keep their own data, permissions and interactions.
- Login visuals remain independently replaceable through the already documented branding options.
