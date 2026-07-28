# Feature Specification: Activity Instance Metadata

**Feature Branch**: `codex/activity-instance-metadata`

**Created**: 2026-07-28

**Status**: Draft

**Input**: User description: "Let workflow authors give each activity occurrence a useful Display Name and Description, surface copyable technical identifiers in the Inspector, and remove repetitive version labels from activity nodes and the normal palette."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Name and document an activity occurrence (Priority: P1)

As a workflow author, I can give an individual activity occurrence a meaningful Display Name and Description so that the workflow explains its intent instead of showing only generic activity types.

**Why this priority**: Per-occurrence naming is the primary user value. It makes repeated uses of the same activity type distinguishable and turns the designer into readable workflow documentation.

**Independent Test**: Select an activity in an editable workflow, author its Display Name and Description in Details, and verify that the Inspector and canvas update immediately, the change participates in normal draft persistence and undo, and clearing the name restores the catalog fallback.

**Acceptance Scenarios**:

1. **Given** an activity occurrence has no authored Display Name, **When** it is rendered, **Then** its catalog display name is used, with the technical activity type as the final fallback.
2. **Given** an activity occurrence is selected in an editable draft, **When** the author enters a Display Name, **Then** the Inspector title and canvas label update immediately without a separate Apply action.
3. **Given** an author enters a Description, **When** the activity is shown on the canvas, **Then** the existing authored-input summary remains the visible subtitle and the full Description remains available through the Inspector and the node's tooltip or accessible description.
4. **Given** the author clears either field or enters only surrounding whitespace, **When** the draft is persisted, **Then** that field is treated as absent and the Display Name fallback applies.
5. **Given** a Display Name is longer than the node can display, **When** the canvas renders it, **Then** the label remains one line and truncates visually while the full value remains available in the Inspector and tooltip.
6. **Given** multiple activities have the same authored Display Name, **When** the draft is saved, **Then** the names remain valid because Node ID, not Display Name, is the stable identity.

---

### User Story 2 - Copy technical activity identifiers consistently (Priority: P1)

As a workflow author or troubleshooter, I can quickly copy an activity's technical identifiers from a consistent compact control so that I can use them in diagnostics, support conversations, and automation.

**Why this priority**: The Node ID is frequently needed during troubleshooting, but it is currently hidden in Details and lacks a direct copy affordance.

**Independent Test**: Select an activity, copy its Node ID from the fixed Inspector context, then copy Activity Type and Activity Version ID from their respective tabs using pointer and keyboard operation; verify exact values and accessible success feedback.

**Acceptance Scenarios**:

1. **Given** any activity occurrence is selected, **When** the Inspector is visible, **Then** a compact explicitly labelled Node ID row appears directly beneath the activity title.
2. **Given** a technical identifier is visible, **When** the user activates its copy control, **Then** the exact untruncated value is copied, the icon briefly becomes a checkmark, and "Node ID copied" or the corresponding identifier label is announced without a global toast.
3. **Given** an identifier fits its available width, **When** it is rendered, **Then** its full value is visible.
4. **Given** an identifier does not fit its available width, **When** it is rendered, **Then** it uses middle truncation and exposes the complete value in a tooltip while copying the complete value.
5. **Given** Details is active, **When** the user reviews identity information, **Then** Activity Type uses the same copyable identifier interaction and Node ID is not duplicated there.
6. **Given** Version is active, **When** the user reviews the exact Activity Version ID, **Then** it uses the same copyable identifier interaction.

---

### User Story 3 - Read a less cluttered designer and palette (Priority: P2)

As a workflow author, I can scan activity names and meaningful summaries without repetitive version badges competing for attention.

**Why this priority**: Versions remain important for lifecycle work, but repeating them on every node and normal palette row adds visual noise to the primary authoring flow.

**Independent Test**: Open editable, Test Run, and executable canvases plus the activity palette; verify that routine version badges are absent, exact versions remain reachable in Version, and ambiguous palette choices retain enough version information to distinguish them.

**Acceptance Scenarios**:

1. **Given** any designer, Test Run, or executable-inspection canvas, **When** activity nodes render, **Then** no activity version badge appears on the node.
2. **Given** an activity is selected, **When** the user opens Version, **Then** the exact version identity and existing version actions remain available.
3. **Given** the palette contains one selectable version of an activity, **When** the row renders, **Then** no visible version indicator appears.
4. **Given** the palette contains multiple selectable versions of the same activity, **When** those rows render, **Then** compact version information appears only where needed to distinguish the choices.
5. **Given** a palette version is hidden, **When** the row is inspected through tooltip or assistive technology, **Then** the exact version remains discoverable without adding visual clutter.

---

### User Story 4 - Preserve presentation across workflow history and tooling (Priority: P2)

As a workflow author or operator, I see the name and description that belonged to the published or tested workflow at that time, while purely presentational edits do not change workflow behavior.

**Why this priority**: Historical views must not silently adopt later wording, and cosmetic documentation changes must not create a new behavioral identity.

**Independent Test**: Publish and Test Run a workflow with authored activity metadata, edit the draft metadata later, and verify that historical views retain the frozen values while behavioral equivalence remains unchanged.

**Acceptance Scenarios**:

1. **Given** an activity has authored presentation metadata, **When** a workflow version is published or Test Run, **Then** that source retains a frozen copy of the metadata for later inspection.
2. **Given** a frozen published or Test Run source exists, **When** the draft metadata changes later, **Then** the historical source continues to display its original name and description.
3. **Given** only activity Display Name, Description, or visual layout changes, **When** behavioral equivalence is evaluated, **Then** the workflow remains behaviorally identical.
4. **Given** a historical source predates activity presentation metadata, **When** it is inspected, **Then** the same catalog and technical-type fallbacks used by existing workflows apply.
5. **Given** Weaver or workflow JSON reads the draft, **When** activity metadata exists, **Then** that metadata is included and preserved by automated edits that do not explicitly replace or delete the activity.

### Edge Cases

- Newly placed activities start without authored instance metadata; catalog fallbacks remain immediately usable.
- Duplicating an activity copies its Display Name and Description to the new occurrence while assigning a distinct Node ID.
- Changing only an activity implementation version preserves the occurrence's Display Name and Description.
- Deleting or replacing an activity removes presentation metadata associated with the removed Node ID.
- Root, container, and engine-intrinsic activity occurrences support metadata even when they are inspected as the current canvas owner.
- BPMN elements retain their existing independent naming model and do not receive activity-occurrence metadata fields.
- Display Names are plain single-line text up to 200 characters; Descriptions are plain text up to 2,000 characters and preserve internal line breaks.
- Metadata is editable only for one selected activity in an editable draft; published, executable, Test Run, and Run views are read-only.
- Inputs remains the initial Inspector tab; selecting a node does not force Details open.
- Clipboard access may be restricted; failure feedback remains local and accessible without reporting success.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Each authored activity occurrence MUST support an optional Display Name and optional Description independent of the reusable activity definition.
- **FR-002**: Activity label resolution MUST use authored instance Display Name first, catalog display name second, and technical activity type last.
- **FR-003**: The resolved activity label MUST be used consistently in the canvas, Inspector title, breadcrumbs, search or navigation results, Weaver context, and read-only historical surfaces.
- **FR-004**: Details MUST provide editable Display Name and Description fields for a single selected activity in an editable draft.
- **FR-005**: Display Name MUST accept at most 200 characters and Description MUST accept at most 2,000 characters.
- **FR-006**: Metadata persistence MUST trim surrounding whitespace, treat blank values as absent, preserve Description's internal line breaks, and render Description as plain text.
- **FR-007**: Metadata edits MUST update visible labels immediately and participate in the existing draft autosave, manual save, undo, and redo behavior without a separate Apply action.
- **FR-008**: Display Names MUST NOT be required or unique.
- **FR-009**: The canvas MUST keep the existing authored-input summary as the visible node subtitle and MUST NOT add Description as another visible node line.
- **FR-010**: Long canvas labels MUST remain single-line and visually truncated while their full values remain discoverable.
- **FR-011**: A newly placed activity MUST begin with absent instance metadata, while duplication MUST copy the source occurrence's Display Name and Description.
- **FR-012**: Activity version changes MUST preserve instance metadata; activity deletion or replacement MUST remove metadata owned by the removed Node ID.
- **FR-013**: Root, container, and engine-intrinsic activities MUST support instance metadata; BPMN elements MUST remain outside this feature.
- **FR-014**: Inputs MUST remain the initial Inspector tab and metadata editing MUST remain in Details without adding inline canvas editing or bulk editing.
- **FR-015**: The fixed Inspector context MUST show an explicitly labelled Node ID immediately beneath the selected activity title.
- **FR-016**: Node ID MUST be removed from Details so the same identifier is not duplicated in the Inspector.
- **FR-017**: Node ID, Activity Type, and Activity Version ID MUST use one consistent copyable-identifier interaction.
- **FR-018**: The copyable-identifier interaction MUST support pointer and keyboard operation, exact-value copying, full-value discovery, constrained-width middle truncation, visible copied state, and accessible success or failure feedback without a global toast.
- **FR-019**: Activity version badges MUST be removed from every canvas node variant while exact versions and version actions remain available in Version.
- **FR-020**: Palette version indicators MUST be hidden when one selectable version is present and shown only when multiple choices would otherwise be indistinguishable.
- **FR-021**: Hidden palette versions MUST remain discoverable through tooltip or accessible description.
- **FR-022**: Authored Display Name and Description MUST be treated as presentation metadata and MUST NOT change workflow runtime behavior or behavioral identity.
- **FR-023**: Published versions and Test Runs MUST retain frozen per-node presentation metadata for historical inspection.
- **FR-024**: Historical views MUST use frozen presentation metadata when present and the normal fallback chain when absent.
- **FR-025**: Existing workflows and historical sources MUST require no generated migration values.
- **FR-026**: Workflow JSON and Weaver's read-only designer context MUST include instance metadata and preserve it unless an operation explicitly deletes or replaces the owning activity.
- **FR-027**: Published, executable, Test Run, and Run surfaces MUST render presentation metadata read-only.

### Key Entities

- **Activity Occurrence Presentation**: Optional user-authored Display Name and Description associated with one stable Node ID in a workflow design.
- **Resolved Activity Label**: The user-facing label chosen from instance Display Name, catalog display name, or technical activity type.
- **Frozen Source Presentation**: An immutable per-node snapshot of activity presentation metadata retained with a published or Test Run source for historical inspection.
- **Copyable Identifier**: A consistent labelled presentation of a technical value with exact-value copy behavior and accessible local feedback.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In acceptance testing, 100% of authored activity kinds—ordinary, root, container, and intrinsic—can be named and described without changing their runtime configuration.
- **SC-002**: An author can copy Node ID, Activity Type, or Activity Version ID with one activation from the surface where it is presented, using either pointer or keyboard.
- **SC-003**: Across editable, Test Run, and executable canvases, 100% of tested activity nodes omit visible version badges while exact version information remains reachable.
- **SC-004**: In palette tests, 100% of unambiguous activity rows omit visible versions and 100% of ambiguous multi-version rows remain distinguishable.
- **SC-005**: Changing only activity presentation metadata leaves behavioral-equivalence results unchanged in every contract test.
- **SC-006**: Every tested published or Test Run source retains its original activity presentation after the source draft is edited later.
- **SC-007**: Existing workflows and historical sources without instance metadata remain inspectable with no migration action and display the documented fallback label.
- **SC-008**: Accessibility verification confirms that identifier values, copy controls, copy results, full truncated values, and description text are understandable and operable without relying on pointer or hover alone.

## Assumptions

- Existing workflow permissions govern metadata editing; no new permission is introduced.
- Activity instance metadata is design-owned information rather than activity input or runtime configuration.
- Historical retention follows the existing lifecycle of published Source References and Test Run References.
- The existing activity catalog remains the fallback source for display names, icons, ports, and descriptions that are not authored per occurrence.
- Existing draft concurrency, autosave, save, undo, redo, export, and Weaver mutation mechanisms are reused.
- A follow-up feature supersedes the Node ID placement defined by `specs/093-activity-inspector-tabs`; the original feature remains an accurate historical record of its delivered scope.
