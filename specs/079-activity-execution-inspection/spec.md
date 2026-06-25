# Feature Specification: Activity Execution Inspection

**Feature Branch**: `sfmskywalker-activity-executions-design`

**Created**: 2026-06-25

**Status**: Draft

**Input**: User description: "Capture individual activity executions for workflow instance inspection, including activities that execute multiple times through loops, retries, or parent activity slots. Compare Elsa 3 activity execution behavior with current Elsa 4 runtime and design a checkpoint-gated activity execution inspection capability."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Inspect each concrete activity execution (Priority: P1)

A workflow operator or author inspecting a workflow instance can select an authored activity and see each concrete activity execution that occurred for that activity, including repeated executions caused by loops, retries, or composite activity scheduling.

**Why this priority**: Workflow instance inspection is misleading when a node can run more than once but the UI can show only one status. The operator must be able to distinguish each concrete execution.

**Independent Test**: Can be tested by running a workflow where the same authored activity executes multiple times and confirming that the instance evidence lists distinct activity executions with stable identities, statuses, timestamps, and ordering.

**Acceptance Scenarios**:

1. **Given** a workflow instance where one authored activity executes three times, **When** the instance evidence is inspected, **Then** the system shows three distinct activity executions linked to the same authored activity.
2. **Given** repeated activity executions committed in the same time window, **When** the activity executions are listed, **Then** they appear in a deterministic execution sequence.
3. **Given** an activity execution that was skipped, completed, suspended, faulted, cancelled, or recovered, **When** that execution is inspected, **Then** its committed lifecycle status and relevant evidence are visible.

---

### User Story 2 - Trust committed execution evidence after recovery (Priority: P2)

A runtime operator can trust that activity execution inspection evidence reflects committed workflow state and does not show uncommitted lifecycle transitions that would disappear after a crash or recovery.

**Why this priority**: Inspection evidence must not get ahead of durable runtime state. Showing uncommitted activity details would make recovery and debugging inconsistent.

**Independent Test**: Can be tested by exercising scheduler-boundary checkpoints and confirming that downstream scheduler work is visible only after the activity execution state and inspection evidence are committed.

**Acceptance Scenarios**:

1. **Given** an activity execution is scheduled, **When** scheduler work advances to activity start, **Then** the scheduled activity execution state and inspection summary have already been committed.
2. **Given** an activity execution starts, **When** scheduler work advances to invocation, **Then** the running activity execution state and inspection summary have already been committed.
3. **Given** runtime checkpoint policy skips an optional checkpoint, **When** instance evidence is inspected, **Then** skipped uncommitted evidence is not shown as durable inspection evidence.

---

### User Story 3 - Correlate composite scheduling and waits (Priority: P3)

A user investigating composite activity behavior can understand why a child activity execution was scheduled, which parent and scheduler caused it, and which path, scope, branch, or iteration it belongs to when such provenance exists.

**Why this priority**: Loops, joins, races, and parent activity slots are exactly where repeated executions become hard to explain.

**Independent Test**: Can be tested by running a Flowchart with a loopback and confirming that child activity executions include scheduling provenance sufficient to distinguish each iteration.

**Acceptance Scenarios**:

1. **Given** a composite activity schedules a child activity, **When** the child activity execution is inspected, **Then** structural parent and temporal scheduler identities are distinguishable.
2. **Given** a Flowchart loop schedules the same child node more than once, **When** those child executions are inspected, **Then** path, scope, scheduling cause, and iteration correlation are visible when committed.
3. **Given** an activity execution creates a wait or bookmark, **When** that execution is inspected, **Then** bookmark summaries are visible without requiring full payload disclosure.

---

### User Story 4 - Inspect values and faults safely (Priority: P4)

A user diagnosing activity behavior can see policy-governed input and output evidence and incident summaries for an activity execution without exposing sensitive payloads by default.

**Why this priority**: Inputs, outputs, and faults are essential for diagnosis, but inspection must not become an accidental sensitive-data store.

**Independent Test**: Can be tested by running activities with inputs, outputs, and faults under different payload capture decisions and confirming that metadata and payload visibility match policy.

**Acceptance Scenarios**:

1. **Given** payload capture policy allows metadata only for activity inputs, **When** an activity execution is inspected, **Then** input names and metadata are visible but payloads are not.
2. **Given** payload capture policy allows payload capture for an activity output, **When** the committed execution is inspected, **Then** that output payload is visible in the execution detail.
3. **Given** input materialization fails before the activity body runs, **When** the fault checkpoint is inspected, **Then** the activity execution is visible with fault evidence and linked incident summary.

### Edge Cases

- A workflow instance contains many executions for the same authored activity.
- Several activity executions are committed with identical timestamps.
- Runtime checkpoint policy skips an optional checkpoint.
- A scheduler-boundary checkpoint fails before post-commit scheduler work is enqueued.
- An activity creates a bookmark and suspends without completing.
- An activity faults during input materialization before activity code runs.
- An activity is skipped by its execution guard.
- An activity is cancelled or recovered without an incident.
- Composite activity provenance is missing, partial, or supplied by a non-Flowchart composite.
- Value payload capture is denied, metadata-only, or explicitly allowed.
- An incident is linked to an activity execution but full diagnostic payload is not available.
- Cross-workflow scheduling identifiers exist but the selected inspection view resolves only the current workflow instance.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST represent each concrete activity execution with a stable activity execution identity.
- **FR-002**: The system MUST allow multiple activity executions to reference the same authored activity.
- **FR-003**: The system MUST provide deterministic ordering for activity executions within a workflow execution.
- **FR-004**: The system MUST distinguish structural parent provenance from temporal scheduling provenance for activity executions.
- **FR-005**: The system MUST capture optional scheduling provenance for branch, iteration, execution path, execution scope, and scheduling cause when such data is available.
- **FR-006**: The system MUST commit scheduler-boundary activity lifecycle transitions before advancing dependent scheduler work.
- **FR-007**: The system MUST include activity execution inspection changes in the same durable checkpoint as the related lifecycle state changes.
- **FR-008**: The system MUST NOT expose inspection evidence as committed durable evidence when the checkpoint that would persist it is skipped.
- **FR-009**: Users MUST be able to inspect a selected activity execution's committed status, substatus, timestamps, execution sequence, checkpoint identity, and scheduling provenance.
- **FR-010**: Users MUST be able to inspect committed outcome names for an activity execution.
- **FR-011**: Users MUST be able to inspect bookmark summaries for an activity execution, including resume target and stimulus information when committed.
- **FR-012**: Users MUST be able to inspect incident summaries for an activity execution, including severity, status, failure type, message, and blocking state when committed.
- **FR-013**: The system MUST capture activity input and output value snapshots according to runtime payload capture policy.
- **FR-014**: The system MUST default value inspection to metadata-only or no payload unless payload capture is explicitly allowed.
- **FR-015**: The system MUST represent skipped, suspended, faulted, cancelled, and recovered activity executions as inspectable activity executions when committed.
- **FR-016**: The system MUST allow an instance inspection consumer to load summary evidence for all activity executions and detailed evidence for one selected activity execution.
- **FR-017**: The system MUST provide enough identity and provenance for future cross-workflow chain traversal without requiring cross-workflow traversal in the first inspection view.
- **FR-018**: The feature MUST preserve the separation between workflow design documents and workflow runtime evidence.

### Key Entities *(include if feature involves data)*

- **Activity Execution**: One concrete runtime invocation of an executable activity node within a workflow execution.
- **Activity Execution Inspection Projection**: Runtime-owned read model for inspecting committed evidence for one activity execution.
- **Activity Scheduling Provenance**: Runtime-owned correlation data explaining why and from where an activity execution was scheduled.
- **Activity Execution Value Snapshot**: Policy-governed inspection evidence for an activity input or output.
- **Runtime Checkpoint**: The commit boundary where runtime changes become durable together.
- **Scheduler-Boundary Checkpoint**: Mandatory runtime checkpoint that must persist before dependent scheduler work can safely continue.
- **Bookmark Summary**: Non-payload inspection evidence for a wait or bookmark owned by an activity execution.
- **Incident Summary**: Non-diagnostic-payload inspection evidence for a fault or blocking condition linked to an activity execution.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A workflow instance with three executions of the same authored activity shows all three as distinct inspectable activity executions.
- **SC-002**: 100% of scheduler-boundary transitions covered by this feature persist their activity lifecycle state before dependent scheduler work is processed.
- **SC-003**: For repeated activity executions committed with identical timestamps, ordering remains deterministic in every inspection result.
- **SC-004**: For a Flowchart loopback scenario, each repeated child activity execution exposes enough committed provenance to distinguish the iteration or path that scheduled it.
- **SC-005**: Value payload visibility matches runtime payload capture decisions for activity inputs and outputs in all covered policy modes.
- **SC-006**: A fault during input materialization produces inspectable activity execution evidence linked to an incident summary.
- **SC-007**: Existing workflow instance inspection can summarize node execution counts without loading every detailed value snapshot.

## Assumptions

- The first implementation focuses on current-workflow-instance inspection; cross-workflow chain traversal is deferred.
- Full workflow time-travel is out of scope; activity execution details link to checkpoint identity but do not reconstruct complete workflow snapshots.
- Runtime checkpoint policy controls optional checkpoint persistence, while scheduler-boundary checkpoints are mandatory durability barriers.
- Activity-authored custom inspection evidence is out of scope for the first slice; the runtime captures evidence it already observes.
- Studio visualization is a consumer of this feature, not the owner of runtime inspection evidence.
