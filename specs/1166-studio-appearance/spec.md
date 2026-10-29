# Feature Specification: Studio appearance and workspace redesign

**Feature Branch**: `agent/codex/3447-studio-appearance`
**Created**: 2026-10-29
**Status**: Approved
**Input**: Owner-approved Quiet Studio, Focus Canvas and Open Studio concepts, with independently selectable color schemes and modes. Implementation authorized in this chat.
**Issue**: #3447
**Program-goal state**: none/free-flow

## User Scenarios & Testing

### User Story 1 - Choose appearance independently (Priority: P1)

An author keeps a preferred workspace arrangement while choosing another color scheme, without losing their draft or inspection context.

**Why this priority**: The current combined themes prevent choosing Drift's arrangement with another palette.
**Independent Test**: Change style, colors and mode separately in Appearance, reload, and verify each choice and the open resource survive.

**Acceptance Scenarios**:
1. Given a selected workspace style, color scheme and dark mode, changing colors preserves the style and mode.
2. Given a selected color scheme and navigation override, changing style preserves both choices.
3. Given an edited workflow and selected activity, live appearance changes retain the draft, selection and inspection context.

### User Story 2 - Work in a calm, complete workspace (Priority: P2)

An author chooses Quiet Studio, Focus Canvas or Open Studio and gets coherent navigation, lists, forms, canvas tools and diagnostics.

**Why this priority**: The concepts improve task hierarchy throughout Studio, not only one screenshot.
**Independent Test**: Navigate from the workflow list to a Sequence and Flowchart, edit properties and inspect a run in each style.

**Acceptance Scenarios**:
1. Each style presents the same capabilities with clear primary actions, quiet secondary chrome and visible selection/focus.
2. Focus Canvas uses the available canvas area with floating tools at desktop sizes; tools remain reachable at narrow sizes.
3. Lists use aligned columns and restrained separators; properties and settings retain readable labels, field affordances and error states.

### User Story 3 - Keep existing preferences and custom themes (Priority: P3)

An existing user retains their theme selection and can independently customize it. Administrators retain configured and custom theme stores.

**Why this priority**: Redesign must not silently discard published/custom appearance choices.
**Independent Test**: Load legacy built-in and delayed custom selections, preview a draft theme, reset appearance and reload.

**Acceptance Scenarios**:
1. Legacy selections resolve without overwriting pending custom identities with a fallback theme.
2. Missing, disabled or invalid preferences fall back to an available choice while failed store loading remains non-destructive.
3. Reset clears all appearance and existing chrome overrides, then lets configured defaults and system mode apply.

### Edge Cases

- Unavailable browser storage; unknown style/color IDs; disabled or unpublished theme; failed or delayed theme store.
- A custom palette lacking Dim or High contrast; OS mode changes; preview followed by reset.
- Collapsed/maximized tools; changing style while an input is focused; contributed inspector/palette tabs.
- Narrow displays, zoomed text, long labels, keyboard navigation, reduced motion and forced colors.

## Requirements

### Functional Requirements

- **FR-001**: Offer the three approved workspace styles with independent color scheme and mode selection.
- **FR-002**: Persist explicit choices independently; style and color changes must not overwrite other preferences.
- **FR-003**: Preserve existing theme presets, configured defaults, custom stores, preview and navigation overrides.
- **FR-004**: Preserve unsaved authoring and runtime inspection state through live appearance changes.
- **FR-005**: Apply consistent typography, surfaces, controls and purposeful borders to shell, lists, forms, settings, authoring and diagnostics.
- **FR-006**: Keep contributed panels, commands, activity pickers, exact identities, errors and permissions available in every style.
- **FR-007**: Support light/dark/dim/high-contrast according to the selected palette's supported modes, retaining the user's requested mode across fallback.
- **FR-008**: Retain keyboard access, readable content and usable tools across desktop and narrow widths.
- **FR-009**: Reset must remove all appearance overrides and preserve implicit chrome defaults after effects settle.

### Key Entities

- Workspace style: the supported arrangement and treatment of Studio's presentation.
- Color scheme: a selectable published theme palette with coordinated mode variants.
- Theme preset: an existing named combination of palette, typography, shape and layout, retained as a compatible starting point.
- Appearance preferences: independent browser-local choices, separate from governed backend preferences.

## Success Criteria

### Measurable Outcomes

- **SC-001**: All three styles can each use two different built-in palettes in all four supported modes without changing the other selected axes.
- **SC-002**: Existing built-in and delayed custom selections, preview, reload and reset scenarios preserve or recover the intended appearance.
- **SC-003**: Authoring and inspecting the same workflow in each style retains draft content, selected activity and accessible commands.
- **SC-004**: Rendered desktop and 390px-width journeys show no unreachable required tools or horizontal page overflow caused by the redesign.

## Assumptions

- Existing published themes supply the selectable color schemes; there is no second independently managed palette catalog.
- The generated concepts guide hierarchy and visual treatment; real controls and domain semantics take precedence over generated mockup details.
- Existing theme definitions remain importable; explicit new choices are additive browser-local preferences.
- Runtime, persistence, authorization and module feature identities do not change.
