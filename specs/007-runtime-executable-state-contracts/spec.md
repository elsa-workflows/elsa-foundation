# Feature Specification: Runtime Executable Artifact And Execution State Contracts

**Feature Branch**: `codex/runtime-executable-state-contracts`
**Created**: 2026-06-10
**Status**: Draft
**Input**: Start implementing the Elsa 4 Runtime Execution Seam from locked reports. First unit: runtime executable artifact and execution state contract.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Runtime Executes A Pinned Artifact (Priority: P1)

A runtime maintainer can represent the exact executable artifact snapshot a workflow execution is allowed to run without loading the authored workflow document.

**Why this priority**: Artifact-only execution is the load-bearing boundary for the Runtime Execution Seam.

**Independent Test**: Create a minimal workflow executable and workflow execution state in Runtime.Core using only runtime-owned contracts.

**Acceptance Scenarios**:

1. **Given** a workflow executable identity, **When** a workflow execution state is created, **Then** it pins the exact artifact id, version, and hash.
2. **Given** a runtime executable node, **When** it is represented in the artifact, **Then** the executable node id is distinct from the authored activity id.
3. **Given** Runtime.Core contracts, **When** their dependencies are inspected, **Then** no Design-owned authored workflow model is required.

### User Story 2 - Execution State Is Split By Runtime Purpose (Priority: P1)

A runtime maintainer can model workflow execution state, activity execution state, scheduler state, and durable value state without recreating an Elsa 3-style monolithic workflow state.

**Why this priority**: Later scheduler, bookmark, persistence, and diagnostics work need clean state ownership before behavior is implemented.

**Independent Test**: Instantiate each state contract and assert the fields needed by the locked reports are present and separated.

**Acceptance Scenarios**:

1. **Given** one executable node, **When** it is executed twice in a loop or parallel branch, **Then** two activity executions can share node identity while keeping separate activity execution ids.
2. **Given** scheduled work, **When** it is stored in scheduler state, **Then** it references executable nodes and activity executions rather than Design activity nodes.
3. **Given** a value that must survive suspension or completion, **When** it is captured, **Then** it is represented as declared durable value state with lifecycle and storage policy.

### User Story 3 - Runtime Boundaries Are Named And Extensible (Priority: P2)

A runtime maintainer can name checkpoint boundaries and route workflow execution through one active execution agent per workflow execution id without selecting a storage or actor implementation yet.

**Why this priority**: Checkpoint names and actor-style ownership must not be retrofitted after scheduler behavior exists.

**Independent Test**: Verify the initial checkpoint names and the workflow execution agent/provider abstractions compile against Runtime.Core only.

**Acceptance Scenarios**:

1. **Given** a runtime state change, **When** a checkpoint is described, **Then** it uses a locked checkpoint name separate from persistence policy.
2. **Given** a workflow execution id, **When** work is dispatched, **Then** the dispatch target can be modeled as an execution agent provider without committing to a concrete actor framework.

### Edge Cases

- A workflow execution references an artifact id whose runtime feature is not installed; the failure must be an executable-artifact/runtime dependency error in later behavior, not a Design deserialization error.
- Multiple executions of the same authored activity must not collapse into one activity execution identity.
- Audit/history payloads may reference runtime ids later, but they are not part of continuation state in this unit.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Runtime.Core MUST define a runtime-owned workflow executable artifact contract with artifact identity, source definition reference, artifact version/hash, timestamps, compatibility metadata, executable nodes, and a resume target table placeholder.
- **FR-002**: Runtime.Core MUST define executable node identity separately from authored activity identity.
- **FR-003**: Workflow execution state MUST pin to an exact workflow executable artifact identity by default.
- **FR-004**: Workflow execution state MUST contain execution identity, status, timestamps, correlation, optional parent workflow, tenant/system metadata, and no authored workflow document payload.
- **FR-005**: Runtime.Core MUST define `ActivityExecution` as the durable identity for one concrete execution of one executable node.
- **FR-006**: Activity execution state MUST include lifecycle, scheduling/parent/branch/iteration relationships, bookmark/incident references, fault counts, and metadata without durable evaluated inputs or raw outputs.
- **FR-007**: Scheduler state MUST minimally represent pending work and volatile waits using workflow execution, executable node, and activity execution ids.
- **FR-008**: Durable value state MUST model declared durable values with lifecycle and storage vocabulary from the locked serialization/value decisions.
- **FR-009**: Runtime.Core MUST define the initial locked checkpoint names and a persistence-policy hook that is separate from checkpoint semantics.
- **FR-010**: Runtime.Core MUST define initial workflow execution agent/provider abstractions for one active mailbox/agent per workflow execution id.
- **FR-011**: Tests MUST prove Runtime.Core contracts do not depend on Design-owned authored workflow models.

### Key Entities

- **WorkflowExecutable**: Runtime-owned artifact produced by compile/publish and consumed by runtime execution.
- **ExecutableNode**: Runtime-owned node inside a workflow executable.
- **WorkflowExecutionState**: Continuation state for one workflow execution pinned to one artifact.
- **ActivityExecution**: Durable identity for one concrete execution of one executable node.
- **ActivityExecutionState**: Lifecycle and relationship state for an activity execution.
- **SchedulerState**: Runtime-owned pending work and volatile wait state.
- **DurableValueState**: Persisted declared runtime value state, not raw activity outputs.
- **RuntimeCheckpoint**: Named state-change boundary, separate from flush policy.
- **WorkflowExecutionAgent**: Provider-owned execution mailbox abstraction keyed by workflow execution id.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Runtime.Core can compile with the new contracts and without any `Elsa.Workflows.Design.*` project reference.
- **SC-002**: Focused tests can create a pinned workflow execution state and two activity executions for the same node with distinct execution ids.
- **SC-003**: Focused tests can represent scheduler work and durable value state without authored workflow document models.
- **SC-004**: Architecture tests fail if Runtime.Core source or project references introduce Design-owned authored workflow models.

## Assumptions

- This unit defines contracts and structural tests only; full scheduler, bookmark, persistence, distributed actor, outbox, and Elsa 3 live instance resume behavior remain out of scope.
- `WorkflowExecutable` remains the concrete name for this first Runtime-owned artifact contract unless later specs rename it.
- Runtime JavaScript's existing Design reference remains known deferred debt and is not fixed in this unit.
