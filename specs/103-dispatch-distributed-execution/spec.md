# Feature Specification: Execute DispatchWorkflow Across Distributed Nodes

**Feature Branch**: `codex/dispatch-workflow-program`

**Created**: 2026-07-16

**Status**: Draft

**Input**: GitHub issue #683, "Execute dispatched workflows across distributed nodes", including its complete current body and zero comments, under parent #674.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Child Starts On Eligible Node (Priority: P1)

As a cluster operator, I want a DispatchWorkflow child-start intent committed on one node to be claimed and executed by an eligible distributed node so that dispatch remains transport-neutral while scaling across hosts.

**Why this priority**: This is the core #683 acceptance boundary. It proves the existing activity contract composes with distributed Groundwork placement rather than requiring a broker or activity-authored transport fields.

**Independent Test**: Run a two-node Groundwork acceptance scenario where one node commits the parent dispatch checkpoint and durable child-start outbox item, then an eligible node drains the distributed transport, claims placement, and materializes exactly one child execution.

**Acceptance Scenarios**:

1. **Given** one node commits a DispatchWorkflow checkpoint and durable child-start outbox item, **when** another node handles that persisted intent and an eligible node drains the durable transport, **then** that eligible node starts the child through the existing workflow start dispatcher and configured execution actor provider.
2. **Given** the child executes on an eligible node that may differ from the node that handled the dispatch, **when** runtime lifecycle inspection is queried, **then** parent, dispatch, and child lifecycle state are consistent regardless of which node ran the child.
3. **Given** the same scenario, **when** activity contract metadata is inspected, **then** no routing channel, priority, affinity, broker, or transport-selection input is present.

---

### User Story 2 - Duplicate Delivery And Placement Changes Converge (Priority: P2)

As a host developer, I want duplicate child-start delivery, stale placement, and node changes to converge on one logical child so that at-least-once distributed processing does not duplicate workflow work.

**Why this priority**: Distributed routing is useful only if duplicate delivery and lease races remain fenced by the provider checkpoint boundary.

**Independent Test**: Inject duplicate child-start deliveries and placement ownership changes around the same dispatch identity and prove that only one child execution identity is materialized while stale writers are rejected or become no-ops.

**Acceptance Scenarios**:

1. **Given** two nodes attempt to handle the same child-start intent, **when** one wins the distributed placement lease, **then** exactly one logical child execution is admitted.
2. **Given** placement changes after forwarding, **when** a stale owner tries to write, **then** checkpoint fencing prevents duplicate or regressing state.
3. **Given** duplicate delivery after the child is already started, **when** the handler replays, **then** dispatch and child state remain idempotent.

---

### User Story 3 - Restart Preserves Distributed Progress (Priority: P3)

As an operator, I want either node to restart after durable intent creation without losing dispatch progress so that a cluster can recover from ordinary host failures.

**Why this priority**: #683 must prove distributed execution is restart-safe, not merely in-memory cross-node routing.

**Independent Test**: Restart either node after the parent dispatch checkpoint and before or after child materialization, then resume pumps and verify dispatch and child-start progress converge.

**Acceptance Scenarios**:

1. **Given** the dispatch-handling node restarts after durable intent creation but before an eligible node drains it, **when** processing resumes, **then** an eligible node can claim and execute the child.
2. **Given** a draining or forwarding node restarts after claiming or forwarding but before acknowledgement, **when** processing resumes, **then** the original dispatch identity converges to Started or terminal state without duplicate child materialization.
3. **Given** restart recovery completes, **when** deployment readiness is inspected, **then** in-memory, durable single-node Groundwork, and distributed Groundwork compositions are distinguished accurately.

### Edge Cases

- A node commits the dispatch and exits before any local post-commit processing runs.
- A draining node claims the durable transport item but loses ownership before checkpoint write.
- Duplicate delivery occurs before child admission, after admission, and after child terminal checkpoint.
- Placement lease expires while another node is draining the same transport item.
- The child-start dispatcher returns a distributed forwarded result with incomplete forwarding metadata.
- A single-node Groundwork host is durable but not distributed; readiness must not overstate cluster capability.
- In-memory development dispatch remains asynchronous but non-crash-durable and non-distributed.
- Existing local in-process dispatch behavior must remain unchanged.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A DispatchWorkflow child-start intent committed on one node MUST be executable by an eligible distributed Groundwork node without adding activity inputs or broker-specific concepts.
- **FR-002**: The existing `IWorkflowStartDispatcher` and configured workflow execution actor provider MUST remain the only activity-facing execution seam.
- **FR-003**: Distributed placement and forwarding MUST choose the draining node while checkpoint and command fencing remain the safety boundary against stale or duplicate writers.
- **FR-004**: Duplicate child-start delivery MUST converge on one dispatch record, one child workflow execution identity, and no duplicate logical child materialization.
- **FR-005**: Placement changes or stale ownership MUST NOT regress dispatch lifecycle state or create a second child execution.
- **FR-006**: Restarting either node after durable intent creation MUST preserve dispatch and child-start progress.
- **FR-007**: Dispatch lifecycle state and authenticated inspection MUST remain consistent regardless of which node executes the child.
- **FR-008**: Deployment readiness MUST distinguish in-memory development, durable single-node Groundwork, and distributed Groundwork compositions.
- **FR-009**: Architecture guardrails MUST prove no MassTransit, service-bus, routing-channel, priority, affinity, or transport-selection dependency enters the `DispatchWorkflow` activity contract.
- **FR-010**: Existing local in-process DispatchWorkflow behavior MUST remain unchanged.
- **FR-011**: Distributed dispatch MUST preserve artifact pinning, authority/tenant/partition inheritance, run-kind/test-scope semantics, delivery recovery, fault/cancellation behavior, redrive behavior, and safe diagnostics from #675-#682.
- **FR-012**: This work unit MUST NOT add broker integration, Studio UI, WorkflowDefinitionActivity changes, or new activity-authored transport controls.

### Key Entities

- **Distributed Dispatch Host**: One logical Elsa host instance with a node identity, Groundwork-backed runtime persistence, and the distributed workflow execution actor provider.
- **Durable Child-Start Transport Item**: Provider-owned command transport record that carries or references the existing child-start command without changing the DispatchWorkflow activity contract.
- **Placement Lease**: Durable ownership evidence that lets one eligible node claim distributed execution work while stale nodes fail fenced writes.
- **Distributed Readiness State**: Composition evidence used to classify in-memory, durable single-node Groundwork, and distributed Groundwork deployments.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A two-node acceptance test proves a parent dispatch checkpoint and durable child-start outbox item committed on one node result in child execution on an eligible distributed node in 100% of deterministic test runs.
- **SC-002**: Duplicate delivery and placement-change tests produce exactly one child execution identity and zero duplicate logical child starts.
- **SC-003**: Restart tests for both nodes after durable intent creation converge without lost dispatch progress or state regression.
- **SC-004**: Runtime inspection reports consistent parent, dispatch, and child lifecycle state for distributed execution.
- **SC-005**: Readiness tests classify in-memory, durable single-node Groundwork, and distributed Groundwork compositions distinctly.
- **SC-006**: Architecture tests prove the activity contract has no broker, routing-channel, priority, affinity, or transport-selection dependency.
- **SC-007**: Full DispatchWorkflow, Runtime Distributed, Groundwork, Runtime API, and Architecture suites remain green.

## Assumptions

- #676 and #678 are locally complete and provide the activity and durable Groundwork restart substrate required by this slice.
- #675-#682 local commits are authoritative for the existing dispatch contract and must not be weakened.
- Existing Groundwork distributed actor provider, durable command transport, placement leases, and fencing are the preferred seams; no new transport abstraction is introduced.
- Distributed tests may use provider test fixtures or process-local two-node hosts as long as they exercise durable provider state, node identity, placement, and fencing.
- Parent #674 remains open; this work unit creates only a local commit and does not close issues, push, or open a PR.
