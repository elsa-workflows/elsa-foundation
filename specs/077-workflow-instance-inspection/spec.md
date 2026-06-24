# Feature Specification: Workflow Instance Inspection

**Feature Branch**: `codex/workflow-instance-inspection`

**Created**: 2026-06-24

**Status**: Draft

**Input**: User description: "Spec this out and then implement it end to end. When done, do a self review until no more actionable issues remain." Context: the current workflow instance list shows quick instance details, but users need a wider inspection view and need each workflow instance displayed on the designer canvas using the workflow definition designer metadata such as node positions.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Inspect an instance on its workflow canvas (Priority: P1)

A workflow author or operator opens a workflow instance and sees the executed workflow on a read-only designer canvas that uses the same layout metadata as the workflow definition version that produced the instance.

**Why this priority**: This is the core value. The existing list and side panel expose facts, but they do not show where execution happened in the authored workflow graph.

**Independent Test**: Can be tested by opening a workflow instance created from a Flowchart definition with saved node positions and confirming that the instance view displays the nodes in those positions with runtime status markers.

**Acceptance Scenarios**:

1. **Given** a workflow instance created from a Flowchart definition with saved designer layout, **When** the user opens the instance inspection view, **Then** the canvas displays the workflow graph using the saved layout.
2. **Given** a workflow instance with activity execution history, **When** the canvas renders, **Then** each activity node with runtime history shows its latest runtime status.
3. **Given** a workflow instance with an incident linked to an activity, **When** the canvas renders, **Then** the affected activity is visually marked and the incident is visible in the inspection details.

---

### User Story 2 - Navigate from instance list to a wider inspection view (Priority: P2)

A user reviewing workflow executions can scan the instance list, select an instance, and move into a wider detail workspace without losing the ability to return to the list.

**Why this priority**: The current inline inspector is too constrained for graph, timeline, and incident diagnosis. A dedicated workspace is needed, while the list still remains useful for triage.

**Independent Test**: Can be tested by selecting an instance from the list and confirming that a deep-linkable instance detail view opens with the selected instance loaded.

**Acceptance Scenarios**:

1. **Given** the workflow instance list contains at least one instance, **When** the user selects an instance, **Then** the application opens a wider detail view for that exact instance.
2. **Given** the user opens a direct link to an instance detail view, **When** the instance exists, **Then** the application loads the same inspection view without requiring prior list navigation.
3. **Given** the selected instance no longer exists, **When** the detail view loads, **Then** the user sees a clear not-found state and can return to the instance list.

---

### User Story 3 - Correlate graph nodes, timeline, and incidents (Priority: P3)

A user investigating a workflow execution can select a graph node, timeline item, or incident and see the related runtime records together.

**Why this priority**: Graph visualization alone is not enough. Users need to move from visual context to concrete runtime evidence and back.

**Independent Test**: Can be tested by opening an instance with multiple activity executions and incidents, selecting a timeline item, and confirming the matching graph node and details are highlighted.

**Acceptance Scenarios**:

1. **Given** an instance view with activity history, **When** the user selects a timeline item, **Then** the corresponding graph node is highlighted.
2. **Given** an incident linked to an activity, **When** the user selects the incident, **Then** the matching graph node and activity history are highlighted.
3. **Given** a graph node has multiple runtime records, **When** the user selects the node, **Then** the inspection details show the related activity executions and incidents.

### Edge Cases

- The instance references a workflow definition version whose designer layout is missing or incomplete.
- The instance references a workflow definition version that cannot be found.
- The workflow definition version contains an unsupported root activity for canvas rendering.
- Runtime activity records cannot be matched to authored activity nodes.
- Multiple executions exist for the same authored activity, such as loop iterations or retries.
- The instance has no activity history yet.
- The instance has incidents not linked to a specific activity.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Users MUST be able to open a dedicated inspection view for a workflow instance from the instance list.
- **FR-002**: Users MUST be able to open the inspection view directly by URL or equivalent deep link.
- **FR-003**: The inspection view MUST show summary information for the selected instance, including status, definition identity, artifact identity, timestamps, and correlation identity when available.
- **FR-004**: The inspection view MUST show the workflow graph for the definition version that produced the selected instance.
- **FR-005**: The workflow graph MUST use the designer layout metadata saved for the relevant definition version when that metadata exists.
- **FR-006**: The workflow graph MUST be read-only in the instance inspection context.
- **FR-007**: The workflow graph MUST mark activity nodes that have runtime activity history.
- **FR-008**: The workflow graph MUST mark activity nodes that have linked incidents.
- **FR-009**: The inspection view MUST show ordered activity execution history with enough identity and timing information to correlate records with graph nodes.
- **FR-010**: The inspection view MUST show incidents with severity, status, message, and linked activity context when available.
- **FR-011**: Users MUST be able to correlate graph nodes, activity history, and incidents by selecting any one of those evidence surfaces.
- **FR-012**: The system MUST handle missing definition version data, missing layout metadata, and unmatched runtime activity records with clear non-blocking fallback states.
- **FR-013**: The existing instance list MUST remain available for scanning and filtering workflow instances.

### Key Entities *(include if feature involves data)*

- **Workflow Instance**: A runtime execution of a workflow, including status, timestamps, correlation, artifact identity, and definition version identity.
- **Workflow Definition Version Snapshot**: The authored workflow state and designer layout for the exact definition version that produced an instance.
- **Activity Execution Record**: Runtime history for an activity execution, including authored activity identity, executable node identity, status, timing, hierarchy, and incidents.
- **Incident**: A runtime failure or blocking condition associated with a workflow instance and optionally with an activity execution or executable node.
- **Instance Inspection View**: The user-facing composition of summary, graph, activity history, and incident evidence for one workflow instance.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can open an existing workflow instance from the instance list and reach a wider inspection view in no more than one selection.
- **SC-002**: A Flowchart instance created from a definition with saved layout renders with all authored child activities positioned from that saved layout.
- **SC-003**: For an instance with one or more incidents linked to activity execution records, the inspection view identifies the affected activity on both the graph and the incident list.
- **SC-004**: A direct link to an existing workflow instance inspection view loads the selected instance without requiring previous navigation state.
- **SC-005**: Missing layout or unmatched runtime records do not prevent the rest of the instance inspection view from loading.

## Assumptions

- The first implementation targets the existing Studio workflow module and Elsa Server development host.
- Read-only inspection is in scope; editing workflow definitions from an instance inspection view is out of scope.
- The workflow graph should reflect the definition version that produced the runtime artifact, not the current mutable draft.
- Flowchart and Sequence roots should be inspectable using the existing designer representation where supported.
- Advanced path animation, replay controls, and edge-level execution semantics are deferred unless already available from current runtime records.
