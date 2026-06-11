# Feature Specification: Workflow Execution Vertical Slice

> Supersession note (2026-06-11): graph-shaped workflow executable requirements in this slice are
> superseded by
> [070-workflow-root-activity-contract](../070-workflow-root-activity-contract/spec.md). A workflow
> executable now carries one compiled root activity, not workflow-level start nodes and edges.

**Feature Branch**: `015-workflow-execution-slice`

**Created**: 2026-06-10

**Status**: Draft

**Input**: User description: "Create a complete plan and implement an end-to-end vertical slice so a demo can invoke a few REST API endpoints to define and execute a workflow. The slice should allow creating a design-time workflow definition from JSON, compiling it into a WorkflowExecutable, and executing it through REST without taking too many shortcuts."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Publish A JSON-Authored Workflow (Priority: P1)

A framework maintainer can create a workflow definition and version through the existing design REST API, then publish that version into a runtime-owned executable artifact through a publishing REST endpoint.

**Why this priority**: This proves the key architecture crossing. Design owns authored state; Publishing reads it and produces the runtime artifact; Runtime does not load the authored workflow document.

**Independent Test**: Create a workflow definition version containing a two-step literal `WriteLine` graph, publish it, and verify the response identifies a `WorkflowExecutable` with executable nodes, artifact identity, start nodes, and sequential edges.

**Acceptance Scenarios**:

1. **Given** a workflow definition version whose state contains one start `WriteLine` activity and one terminal `WriteLine` activity connected in sequence, **When** the version is published, **Then** a `WorkflowExecutable` artifact is produced with one executable node per authored activity and one executable edge matching the authored connection.
2. **Given** each authored activity node carries literal input state, **When** the version is published, **Then** the executable stores runtime literal input bindings with enough metadata to recreate typed runtime input arguments during execution.
3. **Given** the published artifact is returned, **When** the response is inspected, **Then** it includes artifact id, definition id, definition version id, artifact version, artifact hash, node count, edge count, and start node ids.

---

### User Story 2 - Execute A Published Workflow Artifact (Priority: P1)

A framework maintainer can execute a published workflow executable through a runtime REST endpoint and observe that the configured activities actually ran.

**Why this priority**: This is the Monday demo proof point. It demonstrates real execution, not only catalog construction or a synthetic success response.

**Independent Test**: Publish the two-step `WriteLine` workflow from User Story 1, execute the returned artifact id, and verify the execution result reports both activity executions in order with completed status.

**Acceptance Scenarios**:

1. **Given** a published executable artifact with two sequential `WriteLine` nodes, **When** the runtime execute endpoint is called with the artifact id, **Then** the runtime executes both activities in graph order and returns a completed workflow execution result.
2. **Given** the runtime executes an activity node, **When** it constructs the activity, **Then** it uses the descriptor-type activity construction seam and the compiled runtime input bindings, not the design-time activity node.
3. **Given** execution completes, **When** the result is inspected, **Then** it includes workflow execution id, artifact id, status, started/completed timestamps, and per-activity execution summaries.

---

### User Story 3 - Demonstrate The End-To-End REST Journey (Priority: P2)

A maintainer can run a short checked-in HTTP script or quickstart to create a definition, create a version with JSON state, publish the version, and execute the artifact.

**Why this priority**: The goal is a live demo. A scriptable journey lowers demo risk and becomes a regression aid for the slice.

**Independent Test**: Start `Elsa.Server`, run the documented REST sequence, and verify the final response reports completed execution.

**Acceptance Scenarios**:

1. **Given** `Elsa.Server` is running with existing design, publishing, activities, and runtime features enabled, **When** the quickstart REST calls are executed in order, **Then** the final execute call completes without manual database edits or code-first setup.
2. **Given** the quickstart uses a JSON workflow state, **When** it is copied into a REST client, **Then** no local source-code changes are required to define the workflow being executed.
3. **Given** a demo operator reviews the quickstart, **When** they need the activity ids for the JSON state, **Then** the quickstart shows how to list or discover constructable activities before creating the version.

---

### User Story 4 - Reject Unsupported Workflow Shapes Clearly (Priority: P2)

A maintainer receives clear diagnostics when trying to publish or execute workflow shapes that this vertical slice does not support yet.

**Why this priority**: Bounded scope is acceptable only if failures are explicit. Silent partial execution would create misleading confidence in the runtime.

**Independent Test**: Attempt to publish workflows with missing start node, multiple start nodes, unknown activity version id, branch/parallel fan-out, or unsupported non-literal input bindings and verify domain diagnostics identify the unsupported shape.

**Acceptance Scenarios**:

1. **Given** a workflow version has no start activity, **When** it is published, **Then** publishing fails with a diagnostic naming the missing start condition.
2. **Given** a workflow version has more than one outgoing connection from an executable node, **When** it is published or executed, **Then** the slice rejects the branch/parallel shape instead of choosing an arbitrary path.
3. **Given** an activity input is not a literal value, **When** the version is published, **Then** publishing fails with a diagnostic naming the unsupported input source.

### Edge Cases

- A workflow version references an activity catalog row that is missing or has no registered runtime constructor.
- A literal input value cannot be converted to the activity input type declared by the catalog row.
- A workflow graph has a cycle, disconnected non-start node, missing terminal node, or ambiguous next node.
- An execute request references an unknown artifact id.
- A runtime activity throws while executing.
- The same workflow version is published more than once after no authored change.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The existing design REST API MUST remain the entry point for creating workflow definitions and workflow definition versions from JSON `WorkflowDefinitionState` payloads.
- **FR-002**: A publishing endpoint MUST publish a workflow definition version into a runtime-owned `WorkflowExecutable` artifact without adding a Runtime-to-Design dependency.
- **FR-003**: Publishing MUST construct executable nodes from authored activity nodes by reading activity catalog rows and copying descriptor type, descriptor payload, activity type, activity version, compiled input bindings, output capture declarations, and metadata into the executable artifact.
- **FR-004**: The executable artifact MUST represent sequential control flow explicitly enough for Runtime to select start nodes and next nodes without reloading design-time `ActivityConnection` records.
- **FR-005**: Runtime execution MUST consume only `WorkflowExecutable` artifacts and runtime-owned execution services; it MUST NOT load or depend on `WorkflowDefinitionState`, `WorkflowDefinitionVersion`, or any `Elsa.Workflows.Design.*` assembly.
- **FR-006**: Runtime execution MUST construct each `IActivity` through the existing `IActivityFactory` descriptor-type seam.
- **FR-007**: The vertical slice MUST support literal input bindings for CLR activity `InputArgument<T>` properties, including string literals for the primitive `WriteLine` activity.
- **FR-008**: The vertical slice MUST execute a single connected sequential workflow graph from exactly one start node to completion.
- **FR-009**: The execute endpoint MUST return an execution result with workflow execution id, artifact id, status, started/completed timestamps, and ordered activity execution summaries.
- **FR-010**: Publishing and execution MUST produce clear diagnostics for unsupported workflow shapes and missing artifacts rather than silently skipping nodes or returning synthetic success.
- **FR-011**: The server composition MUST register the publishing and runtime slice services/endpoints needed for the REST demo.
- **FR-012**: A quickstart or HTTP request file MUST document the end-to-end demo REST flow.
- **FR-013**: Focused automated tests MUST cover publishing shape, runtime execution behavior, REST endpoint request/response contracts where practical, and Runtime dependency boundaries.
- **FR-014**: The implementation MUST keep the existing activity construction seam and Design/Runtime dependency direction intact.

### Key Entities *(include if feature involves data)*

- **WorkflowDefinitionState**: Design-owned authored workflow JSON accepted by the existing version API.
- **WorkflowExecutable**: Runtime-owned artifact produced by Publishing and consumed by Runtime execution.
- **ExecutableNode**: Runtime-owned representation of one compiled activity node.
- **ExecutableEdge**: Runtime-owned sequential control-flow link between executable nodes.
- **RuntimeInputBinding**: Runtime-owned compiled declaration for one activity input value.
- **WorkflowExecutableStore**: Artifact lookup surface used by publish and execute endpoints for the demo slice.
- **WorkflowExecutionResult**: Runtime response summary for one execution attempt.
- **ActivityExecutionResult**: Runtime response summary for one activity execution inside the workflow.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A demo operator can complete the documented REST sequence in five calls or fewer after discovering the `WriteLine` activity version id.
- **SC-002**: The final execute response for the documented demo reports `Completed` and exactly two completed activity executions in the expected order.
- **SC-003**: Automated tests prove Runtime does not reference `Elsa.Workflows.Design.*` or `Elsa.Workflows.Publishing.Api`.
- **SC-004**: Unsupported graph/input cases return deterministic diagnostics covered by focused tests.
- **SC-005**: The implemented server starts with the slice enabled and exposes the documented publishing and execute endpoints.

## Assumptions

- The demo slice is intentionally sequential-only; branching, joins, loops, bookmarks, triggers, variables, expressions, durable persistence, and recovery are out of scope.
- The first artifact store may be in-memory, because the goal is to demonstrate the seam and execution path before durable publication is designed.
- The first runtime executor may execute synchronously inside the HTTP request, because background scheduling and execution agents remain separate runtime seam work.
- Literal input values are enough for the Monday demo; expression language evaluation and variable binding remain future runtime-value-binding work.
- Existing anonymous endpoint behavior in this repo is acceptable for the demo slice.
