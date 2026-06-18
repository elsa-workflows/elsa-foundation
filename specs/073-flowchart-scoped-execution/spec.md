# Feature Specification: Flowchart Scoped Execution

**Feature Branch**: `073-flowchart-scoped-execution`

**Created**: 2026-06-17

**Status**: Draft

**Input**: User description: "Implement a clean-slate Elsa Flowchart activity execution model using a policy-driven scoped execution tree with generic execution paths/scopes, implicit activation-aware joins, public gateway policy extension points, loop-safe semantics, runtime-owned Flowchart state, and diagnostics based on the brainstorm canvas decisions."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Run reconverging flowcharts without explicit joins (Priority: P1)

Workflow authors need ordinary multi-inbound activities to behave correctly when branches reconverge, without requiring an explicit Join activity in every common diagram.

**Why this priority**: Implicit activation-aware joins are the core usability and correctness improvement over the current simple continuation model.

**Independent Test**: Can be tested by running a diamond-shaped Flowchart where only active branches are required before the reconverged activity runs.

**Acceptance Scenarios**:

1. **Given** a Flowchart where one branch splits into two active branches that reconverge on an ordinary activity, **When** both branches complete, **Then** the reconverged activity runs exactly once.
2. **Given** a Flowchart where a decision selects only one of two branches that reconverge on an ordinary activity, **When** the selected branch completes, **Then** the reconverged activity runs without waiting forever for the untaken branch.
3. **Given** a Flowchart where a reconverged activity is waiting for another active branch, **When** the waiting state is inspected, **Then** diagnostics explain which branch is still expected.

---

### User Story 2 - Execute loopbacks without cross-iteration interference (Priority: P2)

Workflow authors need loopbacks and repeated visits to execute predictably so an arrival from one loop iteration cannot accidentally satisfy a join in another iteration.

**Why this priority**: Advanced loops and loopbacks are a primary reason for replacing the initial Flowchart dispatcher.

**Independent Test**: Can be tested by running a Flowchart with a loopback that revisits a join across multiple iterations and verifying each iteration is isolated.

**Acceptance Scenarios**:

1. **Given** a Flowchart with a loopback to a stable loop entry, **When** the loop executes multiple times, **Then** each iteration has isolated control-flow state.
2. **Given** a join inside a loop, **When** an earlier iteration has completed, **Then** its arrivals do not satisfy the join for a later iteration.
3. **Given** an ambiguous loopback into an active synchronization area, **When** the Flowchart is validated or executed, **Then** the system rejects it with a clear explanation.

---

### User Story 3 - Extend gateway behavior through public policies (Priority: P3)

Elsa module authors need to provide custom routing or synchronization behavior without replacing the Flowchart engine.

**Why this priority**: Public gateway policies are a v1 extensibility decision and prevent built-in gateway behavior from becoming a closed set.

**Independent Test**: Can be tested by registering a custom policy and verifying it receives a read-only decision context and returns commands that the Flowchart engine applies.

**Acceptance Scenarios**:

1. **Given** a registered custom gateway policy, **When** a matching Flowchart node is reached, **Then** the policy decides the next control-flow commands.
2. **Given** a custom policy, **When** it runs, **Then** it can read graph, scope, execution path, arrival, and active-child summaries without directly mutating runtime state.
3. **Given** a policy decision, **When** commands are returned, **Then** the Flowchart engine applies state changes and child scheduling consistently.

---

### User Story 4 - Explain advanced Flowchart decisions (Priority: P4)

Operators and workflow authors need diagnostics that explain why a node ran, why a join is waiting, which path became unreachable, and which race branch was canceled.

**Why this priority**: Advanced graph execution is hard to trust without user-facing explanations.

**Independent Test**: Can be tested by executing scenarios with joins, dead paths, loop iterations, and races, then inspecting the emitted diagnostic records.

**Acceptance Scenarios**:

1. **Given** a join waiting for active branches, **When** diagnostics are inspected, **Then** they identify the join, expected branches, and current scope or iteration.
2. **Given** an untaken decision branch, **When** a join no longer waits for that branch, **Then** diagnostics explain why that path cannot still arrive.
3. **Given** a First Wins race, **When** one branch wins, **Then** diagnostics identify the winning branch and canceled competing branch scope(s).

### Edge Cases

- A Flowchart has no child activities: it completes successfully.
- A Flowchart has an explicit start node that does not exist: it fails with a clear validation/runtime error.
- A connection references a missing source or target node: it fails with a clear validation/runtime error.
- A multi-inbound ordinary activity receives simultaneous branch completions in any order: it runs exactly once when its policy is satisfied.
- A branch is canceled while waiting on a durable external stimulus: Flowchart records cancellation only after the runtime cancellation path acknowledges it.
- A custom policy returns conflicting commands: the Flowchart rejects the decision and records a policy failure diagnostic.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Flowchart execution MUST use a clean-slate scoped execution model as the primary behavior for this feature.
- **FR-002**: The system MUST represent each active path through a Flowchart as an execution path with one current execution scope.
- **FR-003**: The system MUST support the minimum execution scope kinds: root, branch, join, loop iteration, and race.
- **FR-004**: Flowchart execution state MUST be owned by the Flowchart activity instance, while scheduled child activity metadata MUST include generic `executionPathId` and `executionScopeId` correlation values.
- **FR-005**: Every child activity scheduled by a Flowchart MUST belong to exactly one owning execution path and one current execution scope.
- **FR-006**: The system MUST schedule the start node from explicit Flowchart start metadata when present, and otherwise choose a deterministic safe start node for simple graphs.
- **FR-007**: Ordinary activities with one inbound connection MUST continue directly according to matching completed outcomes.
- **FR-008**: Ordinary activities with multiple inbound connections MUST use implicit activation-aware join behavior by default.
- **FR-009**: Implicit joins MUST wait only for active execution paths in the current synchronization scope and loop iteration that can still reach the target.
- **FR-010**: Implicit joins MUST create one merged execution path for the target activity when the join is satisfied.
- **FR-011**: The system MUST distinguish unreachable paths from still-active paths when evaluating inclusive or implicit joins.
- **FR-012**: The system MUST reject ambiguous loopbacks that cross active join/race boundaries or target ordinary multi-inbound nodes without explicit loop/join policy metadata.
- **FR-013**: Traversing a backward edge into an active ancestor region MUST create a new loop iteration scope.
- **FR-014**: Execution paths from one loop iteration MUST NOT satisfy joins in a different loop iteration.
- **FR-015**: The system MUST provide built-in user-facing gateway behaviors named Decision, Parallel Fork, Parallel Join, Inclusive Fork, Inclusive Join, First Wins, and Merge.
- **FR-016**: First Wins behavior MUST cancel losing sibling execution paths within the race scope while preserving unrelated ancestor or cousin work.
- **FR-017**: Flowchart cancellation of child work MUST use the normal runtime cancellation path and MUST mark Flowchart-owned execution paths canceled only after cancellation is acknowledged.
- **FR-018**: Gateway and join behavior MUST be selected through policy metadata stored in the Flowchart's versioned structure payload, keyed by node and connection identifiers.
- **FR-019**: Gateway policies MUST be public extension points from v1.
- **FR-020**: Public gateway policies MUST receive a read-only decision context and MUST NOT receive direct mutable runtime state, raw stores, or direct scheduler control.
- **FR-021**: Gateway policies MUST return commands for the Flowchart engine to apply rather than mutating Flowchart or runtime state directly.
- **FR-022**: The Flowchart engine MUST record diagnostics for scheduling, join waiting/firing, dead-path decisions, loop iteration boundaries, policy failures, and race cancellation.
- **FR-023**: Diagnostics MUST use graph/user terms, including relevant node, connection, branch, scope, loop iteration, and decision reason.
- **FR-024**: Flowchart branch scheduling and join handling MUST be idempotent and order-independent so branch completions can be processed safely in any order.
- **FR-025**: The feature MUST document public policy extension points and Flowchart structure semantics.

### Key Entities *(include if feature involves data)*

- **Execution Scope**: A logical control-flow area owned by a composite activity execution. Flowchart v1 uses root, branch, join, loop iteration, and race scopes.
- **Execution Path**: A single active, waiting, completed, canceled, or faulted path through a composite activity graph.
- **Arrival**: An immutable record that an execution path reached a target through a specific connection; used for diagnostics and join evaluation evidence.
- **Gateway Policy**: A public extension point that decides how a node or connection should route, wait, merge, cancel, or continue control flow.
- **Policy Command**: A requested state transition or scheduling/cancellation action returned by a gateway policy and applied by the Flowchart engine.
- **Flowchart Structure Metadata**: Versioned graph metadata that records start node, connections, node policies, connection semantics, and labels/defaults.
- **Diagnostic Event**: A user-facing explanation of a meaningful Flowchart execution decision or failure.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of covered diamond-flow tests execute the reconverged activity exactly once when all active branches arrive.
- **SC-002**: 100% of covered decision-reconvergence tests complete without waiting for untaken branches.
- **SC-003**: 100% of covered loopback tests isolate join evaluation by loop iteration.
- **SC-004**: 100% of covered race tests cancel only losing sibling branches within the race scope.
- **SC-005**: At least one custom policy scenario demonstrates public policy registration, read-only context access, and command-returning behavior.
- **SC-006**: Every non-trivial built-in policy scenario produces at least one diagnostic explaining why the Flowchart scheduled, waited, fired, canceled, or declared a path unreachable.
- **SC-007**: Existing Flowchart tests are either updated to the new clean-slate semantics or intentionally replaced by equivalent tests that validate the same user-visible behavior.

## Assumptions

- The current Flowchart implementation is unused and does not require a compatibility mode.
- Flowchart owns its scoped execution state initially; generic `executionPathId` and `executionScopeId` names leave room for other composite activities to adopt the same correlation model later.
- True concurrent branch execution is a broader runtime scheduler concern. This feature makes Flowchart semantics safe for future concurrency but does not require new worker or locking infrastructure by itself.
- Public gateway policy contracts are part of this feature because custom routing and synchronization are expected extension needs.
- BPMN is inspiration only. Elsa will use native gateway names and will not reproduce Camunda quirks or full BPMN specification behavior.
