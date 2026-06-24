# Feature Specification: Workflow Definition Test Runs

**Feature Branch**: `076-workflow-test-runs`

**Created**: 2026-06-24

**Status**: Draft

**Input**: User description: "Create a specification and implementation for running a workflow definition from the designer for testing without promoting it into a durable workflow executable artifact. The designer should be able to run the workflow under development without polluting the system with workflow executable artifacts; the runtime boundary should remain artifact-only by compiling an ephemeral/transient executable for the test run."

**Program goal**: [Runtime Execution Seam](../../docs/program-goals/runtime-execution-seam.md) — this feature closes a designer iteration gap while preserving the Design/Runtime split and artifact-only runtime contract.

---

## User Scenarios & Testing *(mandatory)*

The primary actor is a workflow designer editing a draft or version of a workflow definition. The system must let the designer test the workflow under development without making that test artifact appear as a published, reusable, or scheduled workflow executable.

### User Story 1 - Run the current workflow under development (Priority: P1)

A workflow designer edits a workflow definition and starts a test run directly from the designer. The system validates the editable workflow state, prepares a runnable snapshot for that test only, starts execution, and returns the execution identity and test-run status without requiring the designer to publish or promote the workflow first.

**Why this priority**: This is the core value. If only this story ships, designers no longer have to pollute the durable executable catalog merely to check whether a workflow under development behaves correctly.

**Independent Test**: Fully testable by creating or editing a workflow definition with a valid root activity, requesting a test run, and confirming that an execution is started while no new published executable appears in the durable executable list.

**Acceptance Scenarios**:

1. **Given** a workflow definition with valid editable state, **When** the designer starts a test run, **Then** the system starts a workflow execution for that exact editable state and returns a test-run identity, execution identity, and accepted status.
2. **Given** a workflow definition that has not been published, **When** the designer starts a test run, **Then** the system does not require a prior published executable.
3. **Given** a successful test-run start, **When** a user lists published workflow executables, **Then** the test-run artifact is absent from that durable published list.
4. **Given** the same workflow definition is tested multiple times after edits, **When** each test run starts, **Then** each run is tied to the workflow state at the moment the run was requested.

---

### User Story 2 - Receive clear feedback when the editable workflow cannot run (Priority: P2)

A workflow designer attempts to test a workflow that is incomplete, invalid, or uses unsupported authoring content. The system rejects the test run with actionable validation feedback and does not create a workflow execution.

**Why this priority**: A test-run button must be safe and explainable. Designers need fast feedback when their current workflow cannot be converted into a runnable test snapshot.

**Independent Test**: Fully testable by attempting to test definitions with no root activity, duplicate activity identifiers, unknown activity definitions, or unsupported input bindings, and confirming each attempt returns a rejected test run with a clear reason and no execution identity.

**Acceptance Scenarios**:

1. **Given** a workflow definition without a root activity, **When** the designer starts a test run, **Then** the system rejects the request with a message explaining that the workflow has no runnable root.
2. **Given** a workflow definition containing duplicate authored activity identifiers, **When** the designer starts a test run, **Then** the system rejects the request with a message identifying the duplicate.
3. **Given** a workflow definition whose activity definitions cannot be resolved, **When** the designer starts a test run, **Then** the system rejects the request with a message identifying the unresolved activity.
4. **Given** the system rejects a test run before execution, **When** durable execution records are inspected, **Then** no workflow execution was started for that rejected test run.

---

### User Story 3 - Keep test runs isolated from production workflow artifacts (Priority: P3)

An operator or workflow designer reviews published executables, scheduling targets, or reusable workflow artifacts after designer testing. Test-run artifacts remain isolated and expire automatically so they do not become production candidates or clutter operational views.

**Why this priority**: This preserves operational hygiene and prevents accidental production use of a workflow that was only tested from the designer.

**Independent Test**: Fully testable by starting a test run, inspecting artifact listings and production execution entry points, and confirming the transient test artifact is hidden from durable published catalogs and is removed or becomes unavailable after its retention window.

**Acceptance Scenarios**:

1. **Given** a designer has started one or more test runs, **When** an operator views published or promotable workflow executables, **Then** test-run artifacts do not appear there.
2. **Given** a test-run artifact exists, **When** a production caller attempts to start it as a normal published workflow, **Then** the system prevents that artifact from being used as a production start target.
3. **Given** a test-run artifact reaches its retention window, **When** cleanup occurs, **Then** the artifact is no longer available for further starts while the historical execution result remains identifiable by test-run and execution ids.

---

### Edge Cases

- The workflow definition is missing, deleted, or inaccessible to the caller.
- The editable workflow has no root activity.
- The editable workflow references activity definitions that are missing, disabled, or incompatible with the current runtime configuration.
- The editable workflow contains unsupported bindings or values for the current runnable-artifact compiler.
- Multiple test runs are requested for the same workflow in quick succession after different edits.
- The transient artifact expires before execution can be dispatched.
- Cleanup fails for an expired transient artifact.
- A caller attempts to schedule, publish, promote, or start a transient test artifact through a production execution path.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST allow an authorized workflow designer to start a test run from an editable workflow definition or definition version without requiring prior publication.
- **FR-002**: A test run MUST execute the workflow state as it existed when the test run was requested, not a later edited state.
- **FR-003**: The system MUST prepare a runnable artifact for each accepted test run before runtime execution starts.
- **FR-004**: Runtime execution MUST consume only the prepared runnable artifact and configured runtime features; runtime execution MUST NOT load design-side workflow definition state to decide what to run.
- **FR-005**: Test-run runnable artifacts MUST be marked as transient/test-scoped and MUST NOT appear as durable published workflow executables.
- **FR-006**: Test-run runnable artifacts MUST NOT be eligible for scheduling, promotion, reuse as production workflow start targets, or normal published-executable listing.
- **FR-007**: The system MUST return a test-run identity, workflow execution identity when dispatch succeeds, source workflow identity, source version identity, artifact identity, status, and reason when applicable.
- **FR-008**: The system MUST reject test-run requests for workflows that cannot be prepared into a runnable artifact and MUST provide an actionable, user-facing reason.
- **FR-009**: Rejected test-run requests MUST NOT start workflow execution.
- **FR-010**: The system MUST retain enough test-run metadata to let the designer correlate a test run with execution status, logs, traces, or failure feedback.
- **FR-011**: The system MUST expire or clean up transient test-run artifacts after a bounded retention window while preserving the ability to identify the historical test-run result.
- **FR-012**: The system MUST protect the test-run action with the same workflow-design authorization expectations as editing or managing the source workflow definition.
- **FR-013**: The system MUST report when test-run dispatch is accepted, rejected, or fails before dispatch in a machine-classifiable way.
- **FR-014**: The system MUST prevent transient artifact leakage into durable published artifact storage views and production execution entry points.
- **FR-015**: The system MUST support repeated test runs for the same workflow definition without requiring the designer to delete prior test artifacts manually.

### Key Entities

- **Workflow Test Run**: A designer-initiated attempt to run an editable workflow snapshot for development/testing; includes identity, source workflow/version references, dispatch status, execution identity when available, timestamps, and a failure or rejection reason when applicable.
- **Transient Runnable Artifact**: A runtime-owned runnable representation prepared for a single test-run context; it is executable by runtime but is not a durable published workflow executable.
- **Source Workflow Snapshot**: The workflow definition state captured at test-run request time so later edits do not change the test already being dispatched.
- **Workflow Execution**: The runtime execution started for an accepted test run and correlated back to the workflow test run.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A designer can start a test run from an unpublished workflow definition in fewer than 3 user actions from the designer surface.
- **SC-002**: 100% of accepted test runs start from a captured workflow state and remain correlated to the source workflow and execution identity.
- **SC-003**: 0 transient test-run artifacts appear in published executable listings, promotable artifact lists, or scheduling target lists.
- **SC-004**: 95% of invalid workflow test-run requests return a user-actionable rejection reason within 2 seconds under normal development-load conditions.
- **SC-005**: Repeated test runs of the same workflow require no manual cleanup by the designer.
- **SC-006**: A runtime-only deployment can execute a prepared test-run artifact without loading workflow design state during execution.

## Assumptions

- The first implementation targets the existing combined development host where designer, publishing/compile bridge, and runtime services are available together.
- The designer already has permission to edit or manage the source workflow definition; this same authority is sufficient to request a test run.
- A bounded transient-artifact retention period is acceptable; the default is short-lived development retention rather than long-term artifact retention.
- Test-run history may remain visible for diagnostics even after the underlying transient runnable artifact expires.
- The feature does not make workflow test runs a replacement for publishing, promotion, scheduling, or production workflow execution.
- The initial supported workflow content matches the current runnable-artifact compiler's supported subset; unsupported content is rejected with clear feedback rather than silently ignored.
