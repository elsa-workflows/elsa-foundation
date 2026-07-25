# Feature Specification: Activity Inspector Tabs

**Feature Branch**: `[not-created]`

**Created**: 2026-07-25

**Status**: Draft

**Input**: User description: "Reduce noise in the workflow designer's activity Inspector by organizing activity inputs, outputs, variables, slots, identity details, and version information into focused inner tabs."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Edit activity inputs without unrelated content (Priority: P1)

As a workflow author, I can open a selected activity directly on its Inputs tab so that I can configure the activity without scrolling past identity, version, output, variable, or slot information.

**Why this priority**: Configuring activity inputs is the Inspector's primary authoring task and the main source of excessive scrolling in the current layout.

**Independent Test**: Select activities with many, few, and no configurable inputs and verify that Inputs is the initial tab, contains only input-authoring controls, and provides a clear empty state when applicable.

**Acceptance Scenarios**:

1. **Given** an activity with configurable inputs is selected, **When** the Inspector opens for the first time in the editor session, **Then** Inputs is active and its controls appear without unrelated sections above or below them.
2. **Given** an activity with no configurable inputs is selected, **When** Inputs is active, **Then** the tab remains available and explains that the activity has no configurable inputs.
3. **Given** Set Variable or Set Output is selected, **When** Inputs is active, **Then** its destination selector appears with its value fields, while container-scoped variable declarations remain outside this tab.

---

### User Story 2 - Configure output capture in a focused view (Priority: P1)

As a workflow author, I can switch directly from Inputs to Outputs so that I can configure output capture without traversing the activity's other information.

**Why this priority**: Output capture is the second primary binding task and should be as easy to reach as input configuration.

**Independent Test**: Select activities with and without outputs, switch between Inputs and Outputs using pointer and keyboard controls, and verify that only the relevant output-capture content or empty state is shown.

**Acceptance Scenarios**:

1. **Given** an activity exposes outputs, **When** the author selects Outputs, **Then** all output-capture controls are available without an additional redundant Outputs heading.
2. **Given** an activity exposes no outputs, **When** the author selects Outputs, **Then** the tab remains second in the tab order and explains that the activity has no outputs.
3. **Given** keyboard focus is on an inner tab, **When** the author uses the supported tab-list keys, **Then** focus and selection move predictably among the available tabs.

---

### User Story 3 - Reach specialized activity information only when needed (Priority: P2)

As a workflow author, I can use dedicated tabs for variables, embedded slots, activity details, and version information so that specialized concerns no longer form one long Inspector page.

**Why this priority**: Separating secondary concerns removes noise while preserving every existing Inspector capability.

**Independent Test**: Inspect ordinary, reusable, variable-supporting, and slot-owning activities and verify the exact tab set, order, content, and fallback behavior for each.

**Acceptance Scenarios**:

1. **Given** an activity supports container-scoped variables, **When** it is selected, **Then** Variables appears after Outputs and contains the variable declarations.
2. **Given** an activity exposes embedded child slots, **When** it is selected, **Then** Slots appears after Variables when Variables is present, or after Outputs otherwise, and contains slot navigation and replacement controls.
3. **Given** any activity is selected, **When** Details is active, **Then** its Node ID and Activity Type are shown.
4. **Given** any activity is selected, **When** Version is active, **Then** its activity version identity is shown; reusable activities additionally expose their existing exact-version, source, lifecycle, provider, and version actions.
5. **Given** an activity does not support variables or expose slots, **When** it is selected, **Then** the corresponding conditional tabs are absent without leaving empty placeholders.

---

### User Story 4 - Maintain orientation while navigating (Priority: P2)

As a workflow author, I remain oriented to the selected activity and keep my working place while switching tabs, activities, and outer designer panels.

**Why this priority**: Dividing content into tabs only improves usability if users can still identify the selected activity and if navigation does not unexpectedly discard their place.

**Independent Test**: Scroll and interact within multiple inner tabs, switch activities and outer panels, and verify the agreed persistence, reset, fallback, and fixed-context behavior.

**Acceptance Scenarios**:

1. **Given** an activity is selected, **When** the author scrolls long tab content, **Then** the activity name, applicable context notices, and inner tab row remain visible while only the active tab body scrolls.
2. **Given** the author switches inner tabs and returns, **When** the selected activity has not changed, **Then** each tab retains its prior scroll position and transient editor state.
3. **Given** the author selects another activity, **When** the current tab is available for that activity, **Then** the same tab remains active, its scroll position starts at the top, and transient state from the previous activity is cleared.
4. **Given** the author selects another activity while Variables or Slots is active, **When** the new activity does not expose that tab, **Then** Inputs becomes active.
5. **Given** the author leaves Inspector for Runtime or Artifacts, **When** they return during the same editor session, **Then** the previously active inner Inspector tab is restored.

### Edge Cases

- The selected activity has neither inputs nor outputs; both core tabs remain present with distinct empty states.
- An activity exposes Slots but not Variables, or Variables but not Slots; the remaining conditional tab occupies its prescribed relative position without a gap.
- A built-in activity has only an activity version identifier; Version remains available with the information that exists.
- A reusable activity's exact version details are loading, unavailable, or have an upgrade recommendation; the existing status and actions remain contained within Version.
- The active activity becomes unavailable for new use; the warning remains visible regardless of the active inner tab.
- The Inspector is showing the current canvas container rather than a selected child node; the scope-owner explanation remains visible regardless of the active inner tab.
- The Inspector is narrow enough that all available inner tabs do not fit at once; every tab remains reachable without causing page-level horizontal overflow.
- The author changes activity while a menu or expanded editor is open; transient controls from the previous activity close and do not appear for the new activity.
- No activity is selected; the existing prompt to select an activity remains in place and no empty tab shell is shown.
- The selected item is a non-activity designer element with its own Inspector; this feature does not force activity tabs onto that Inspector.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The existing outer Inspector, Runtime, and Artifacts navigation MUST remain unchanged; the new activity tabs MUST be nested within Inspector.
- **FR-002**: The selected activity's display name MUST remain visible as fixed Inspector context above the inner tabs rather than being repeated inside Details.
- **FR-003**: The existing scope-owner explanation and activity-availability warning MUST remain visible in the fixed activity context whenever applicable.
- **FR-004**: The inner activity tab order MUST be Inputs, Outputs, Variables when supported, Slots when exposed, Details, and Version.
- **FR-005**: Inputs and Outputs MUST always be present for a selected activity, even when their respective content is empty.
- **FR-006**: Variables MUST appear only for activities that support container-scoped variable declarations.
- **FR-007**: Slots MUST appear only for activities that expose embedded child slots.
- **FR-008**: Inputs MUST contain the activity's existing property/input authoring controls, including the destination selectors used by intrinsic Set Variable and Set Output activities.
- **FR-009**: Variables MUST contain container-scoped variable declarations and MUST NOT contain intrinsic variable or output destination selectors.
- **FR-010**: Outputs MUST contain the activity's existing output-capture controls.
- **FR-011**: Slots MUST contain the activity's existing embedded-slot navigation and single-slot replacement controls.
- **FR-012**: Details MUST contain Node ID and Activity Type.
- **FR-013**: Version MUST remain present for every activity and MUST contain the activity version identity plus all existing reusable-boundary version, source, provider, lifecycle, recommendation, and action content when applicable.
- **FR-014**: Inputs and Outputs MUST NOT repeat redundant section headings that duplicate their active tab labels.
- **FR-015**: Empty Inputs and Outputs tabs MUST present clear, distinct messages describing the absence of configurable inputs or exposed outputs.
- **FR-016**: Inner tabs MUST use text labels without icons or item counts.
- **FR-017**: When the available labels exceed the Inspector width, the tab row MUST remain a single horizontally navigable row and MUST NOT create page-level horizontal overflow.
- **FR-018**: The inner tabs MUST expose tab-list, tab, selected-state, focus, and keyboard behavior that is understandable to assistive technology and fully operable without a pointer.
- **FR-019**: Inputs MUST be the initial inner tab when Inspector is first opened in an editor session.
- **FR-020**: Selecting another activity MUST preserve the active inner tab when that tab exists for the new activity and MUST otherwise fall back to Inputs.
- **FR-021**: Leaving and returning to Inspector during the same editor session MUST preserve the active inner tab.
- **FR-022**: Switching inner tabs for the same selected activity MUST preserve each tab's scroll position and transient editor state.
- **FR-023**: Selecting another activity MUST reset the visible tab body to the top and clear transient editor state associated with the previous activity.
- **FR-024**: The activity name, applicable context notices, and inner tab row MUST remain visible while the active tab body scrolls independently.
- **FR-025**: Existing activity editing, output capture, variable declaration, slot navigation/replacement, version selection, source navigation, undo/redo, and save behavior MUST remain functionally unchanged apart from their new organization.
- **FR-026**: The feature MUST retain the existing no-selection experience and MUST NOT apply activity tabs to non-activity designer-element inspectors.
- **FR-027**: This feature MUST NOT introduce module-contributed inner Inspector tabs or a new nested extensibility contract.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For every supported activity, an author can reach Inputs or Outputs with no more than one inner-tab selection and without scrolling through unrelated Inspector sections.
- **SC-002**: In acceptance testing, 100% of existing Inspector capabilities remain reachable in exactly one of the agreed inner tabs or the fixed activity context.
- **SC-003**: At the Inspector's minimum supported expanded width, every available inner tab is reachable by both pointer and keyboard without page-level horizontal overflow.
- **SC-004**: Across activity changes, inner-tab switches, and outer-panel round trips, all acceptance tests observe the specified active-tab, scroll, and transient-state behavior with no state leaking between activities.
- **SC-005**: Inputs and Outputs show a specific empty state for 100% of tested activities that lack the corresponding content.
- **SC-006**: Accessibility verification confirms that every inner tab can be identified, focused, selected, and associated with its content without relying on icons, counts, hover, or pointer input.

## Assumptions

- This feature reorganizes the workflow-authoring activity Inspector only; runtime and executable inspection experiences are outside scope.
- Existing permissions, persistence, autosave, undo/redo, activity contracts, and source/version operations remain unchanged.
- Inputs, Outputs, Variables, Slots, Details, and Version are built-in Inspector concerns rather than module Contributions.
- Active inner-tab state is session-local; it is not stored in the workflow, encoded in the URL, or restored across a full editor reload.
- Existing shared Studio tab behavior and design-token contracts remain the visual and accessibility baseline.
