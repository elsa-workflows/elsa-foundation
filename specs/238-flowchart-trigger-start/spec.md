# Feature Specification: Flowchart Starts at the Trigger That Fired

**Feature Branch**: `238-flowchart-trigger-start`

**Created**: 2026-10-16

**Status**: Implemented — PR for #2850

**Input**: GitHub issue [#2850](https://github.com/elsa-workflows/elsa-foundation/issues/2850), "Flowchart ignores the trigger node: a multi-trigger flowchart always starts at its start node", plus the scope Joey set on 2026-10-16 (recorded under [Decisions](#decisions)).

## Context

A Flowchart can hold several start triggers, for example two HTTP endpoints that can start the workflow, or an HTTP
endpoint next to a Cron. Publishing indexes every one of them, and the trigger that fires passes its node id into
the run. The Flowchart ignores that id and always begins at its start node. Workflows that several triggers start
from one flowchart are common in Elsa 3, and the Elsa 3 → Elsa 4 conversion
([#2705](https://github.com/elsa-workflows/elsa-foundation/issues/2705), spec 239) must convert them 1:1. This spec
closes the gap in Elsa 4 itself, so the converter needs no workaround. Spec 239 depends on it.

This spec contains no Elsa 3 code. Elsa 3 is referenced only as the behaviour to match.

### Verified current behaviour (origin/main `f4ddd62f2`, 2026-10-16)

- Publishing indexes every trigger node in the workflow, nested ones included, with no per-workflow limit
  (`WorkflowTriggerBindingExtractor` flattens the whole tree; `RecurringTriggerPumpOwnerScopingTests` publishes two
  Timers in one artifact).
- The fired trigger's node id is persisted with the run's seed state (`runtime.triggerNodeId`) and handed to every
  activity invocation as `IRuntimeActivityExecutionContext.TriggerNodeId`. A redriven or resumed invocation reads
  the same persisted value.
- `FlowchartExecutionEngine.StartAsync` always schedules `FlowchartGraph.SelectStartNode()`: the authored
  `startNodeId`, else the first child with no inbound connection, else the first child. It never reads
  `TriggerNodeId`. Only the BPMN engine seeds from it.
- Effect today: when the start node is an HTTP endpoint or Event that did not fire, it suspends on its own bookmark
  and the run parks there, so the branch of the trigger that fired never runs. When the start node is a Timer or
  Cron, it completes at once and the run walks the start node's branch, not the branch that fired.
- `ValueFlowValidator` computes Flowchart dominance only from `startNodeId`. A result producer on another
  trigger's branch is not reachable from that root, so its consumers fail with `ValueFlow/UnavailableProducer`. Two
  variable writes on different trigger branches can never reach each other, so they are reported as
  `ValueFlow/ConcurrentWrite`, although one run only ever takes one of those branches.
- Loop-edge classification (`FlowchartGraph.ComputeBackwardEdges`) roots its depth-first walk at the start node, then
  at every other node in ordinal id order. A loop that only a trigger branch reaches can therefore be classified from
  an arbitrary node inside it, not from where the run enters it.
- Elsa 3 honoured the trigger only in the Flowchart, and only for its direct children. Sequence always started at its
  first child.

## Decisions

| # | Question | Decision |
|---|---|---|
| D1 | Where the trigger is honoured | Only in the Flowchart, and only when the trigger node is a **direct child** of that Flowchart. Sequence, BPMN and every other composite are unchanged. Set by Joey. |
| D2 | Nested flowcharts | The trigger id is workflow-wide. A Flowchart acts on it only when the trigger is its own direct child. An outer Flowchart whose descendant (not child) is the trigger starts at its start node as today. Set by Joey; matches Elsa 3. |
| D3 | Flowchart inside a loop | Each pass starts the Flowchart again, and each pass starts at the trigger. Set by Joey; matches Elsa 3. |
| D4 | Redrive and resume | A redriven start begins at the same node, because the trigger id is persisted with the run. No new state is stored. |
| D5 | Design-time entry points | The value-flow check treats as entry points the Flowchart's start node plus every direct child with **no inbound connection** (FR-008). Such a node can only ever run as the trigger, so treating it as an entry point is sound, and the check needs no catalog data. Revised by Joey 2026-10-16 after planning found that the catalog marks every CLR activity, triggers included, as `Action`. |
| D6 | Concurrent writes across trigger branches | Two writes that only different entry points reach are mutually exclusive, not concurrent (FR-010). Accepted by Joey 2026-10-16. |
| D7 | Loop-edge classification | Direct children marked as trigger nodes in the executable become walk roots right after the start node, so a loop reached only from a trigger is classified from where the run enters it (FR-005). Accepted by Joey 2026-10-16. |

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A multi-trigger flowchart runs the branch whose trigger fired (Priority: P1)

As a workflow author, I put two start triggers in one Flowchart (two HTTP endpoints that can start the workflow, or
an HTTP endpoint and a Cron), each at the head of its own branch, and publish. When one of them fires, the run starts
at that trigger, and only that trigger's branch runs. The start node does not run unless it is the trigger that
fired.

**Why this priority**: This is the defect. Today such a workflow either parks forever on the trigger that did not fire
or runs the wrong branch. Spec 239 needs it to convert Elsa 3 workflows 1:1.

**Independent Test**: Publish a Flowchart with two HTTP endpoint start triggers, each followed by a distinct
activity. Fire the second endpoint. The run completes; the second branch's activity ran, the first endpoint and its
branch did not, and no bookmark was left for the first endpoint.

**Acceptance Scenarios**:

1. **Given** a published Flowchart whose start node is HTTP endpoint A and which holds a second start-capable HTTP
   endpoint B on its own branch, **When** a request hits B, **Then** the run starts at B, B completes with the
   request, B's branch runs, and A neither runs nor leaves a bookmark.
2. **Given** a published Flowchart with a Cron as start node and a start-capable HTTP endpoint on another branch,
   **When** a request hits the endpoint, **Then** the run starts at the endpoint and the Cron's branch does not run.
3. **Given** the same Flowchart, **When** the Cron fires, **Then** the run starts at the Cron and the endpoint's branch
   does not run.
4. **Given** two trigger branches that join a shared downstream activity, **When** either trigger fires, **Then** the
   shared activity runs once, after that trigger's branch.

---

### User Story 2 - Value flow is validated per entry point (Priority: P1)

As a workflow author, I read a trigger's result (for example, the HTTP request body) in an activity further down that
trigger's branch, and I set a variable on each trigger's branch. Validation and publishing accept this. Validation
still rejects a read that some entry point can reach without passing the producer.

**Why this priority**: Without it, the workflows from User Story 1 cannot be published whenever a branch uses its
trigger's result, which is the normal case.

**Independent Test**: Validate a draft Flowchart whose start node is trigger A and whose second trigger B has no inbound
connection and feeds an activity that reads B's result. No value-flow error is reported. Add an inbound connection from A's
branch into that consumer: `ValueFlow/UnavailableProducer` is reported.

**Acceptance Scenarios**:

1. **Given** a draft Flowchart where trigger B, with no inbound connection, leads to consumer C, and C reads B's result, **When**
   the draft is validated, **Then** no `ValueFlow/UnavailableProducer` error is reported for C.
2. **Given** the same draft plus a connection from the start node's branch into C, **When** the draft is validated,
   **Then** `ValueFlow/UnavailableProducer` is reported for C, because the start node reaches C without passing B.
3. **Given** a draft where each of two trigger branches sets the same variable and neither branch reaches the other,
   **When** the draft is validated, **Then** no `ValueFlow/ConcurrentWrite` error is reported.
4. **Given** a draft where one entry point forks into two branches that both set the same variable, **When** the
   draft is validated, **Then** `ValueFlow/ConcurrentWrite` is still reported.
5. **Given** a draft where a mid-flow trigger (an HTTP endpoint with an inbound connection) sits after producer P,
   and a later activity reads P's result, **When** the draft is validated, **Then** no error is reported, because a
   node with an inbound connection is not an entry point.

---

### User Story 3 - Single-start flowcharts behave exactly as before (Priority: P1)

As an operator of existing Elsa 4 workflows, I see no change in how a Flowchart with one start trigger, or a Flowchart
run directly without a trigger, starts, routes, validates or publishes.

**Why this priority**: The fix must not regress the common case.

**Independent Test**: The existing Flowchart runtime and value-flow validator test suites pass unchanged.

**Acceptance Scenarios**:

1. **Given** a Flowchart run directly (no trigger), **When** it starts, **Then** it starts at its start node.
2. **Given** a Flowchart whose only start trigger is its start node, **When** that trigger fires, **Then** it starts
   at the start node, as today.
3. **Given** a Flowchart whose trigger is nested inside a child composite (D2), **When** that trigger fires, **Then**
   the outer Flowchart starts at its start node, as today.

---

### Edge Cases

- **Flowchart inside a loop (D3)**: each pass starts the Flowchart at the trigger again. The trigger is a trigger
  delivery on every pass, so it completes with the same stimulus without suspending.
- **Nested Flowchart whose direct child is the trigger (D2)**: the outer Flowchart starts at its start node. When its
  flow reaches the nested Flowchart, the nested one starts at the trigger.
- **Redrive (D4)**: a redriven or crash-recovered start reads the persisted trigger id and starts at the same node.
- **Trigger with inbound connections**: the run starts at it as the root path. Later arrivals over its inbound
  connections route as for any node. The value-flow check does not treat it as an entry point (D5), so a consumer
  of its result that the start node can also reach is still rejected. Such workflows are rare.
- **Disconnected non-trigger node**: the value-flow check treats it as an entry point. It never runs, so this can
  only add a rejection when it feeds a consumer that reads a result from elsewhere.
- **Trigger that is the start node**: no change.
- **Trigger id that names no direct child** (for example a trigger in a sibling composite, or a stale id): the
  Flowchart starts at its start node.
- **A loop reached only from a trigger branch**: its loop-closing edge is the one that returns to where the trigger
  branch enters the loop (D7).
- **Flowchart without an authored `startNodeId`**: the runtime still honours the trigger. The value-flow check
  keeps today's behaviour and skips the Flowchart, as it does now.
- **BPMN process inside a Flowchart**: unchanged. The BPMN engine already seeds from the trigger when the process
  itself is the trigger node.

## Requirements *(mandatory)*

### Functional Requirements

#### Runtime

- **FR-001**: When a Flowchart starts and the run's trigger node id equals the id of one of the Flowchart's direct
  children, the Flowchart MUST schedule that child as its first node, in place of the start node.
- **FR-002**: In every other case (no trigger id, or a trigger id that is not a direct child) the Flowchart MUST
  start exactly as today.
- **FR-003**: The root execution path and root scope MUST be owned by the node the Flowchart started at. Every place
  that rebuilds the initial state when none is persisted MUST use the same choice.
- **FR-004**: A Flowchart started again inside a loop MUST apply FR-001 on every pass.
- **FR-005**: Loop-edge classification MUST root its walk at the start node first, then at every direct child marked
  as a trigger node in ordinal id order, then at the remaining nodes in ordinal id order. For a graph whose trigger
  children are all reachable from the start node, the classification MUST equal today's.
- **FR-006**: Sequence, BPMN and all other composites MUST NOT change.
- **FR-007**: The change MUST NOT add new persisted state, metadata keys or contracts. It uses the existing
  `TriggerNodeId` on the execution context.

#### Design-time value flow

- **FR-008**: For each Flowchart that has a `startNodeId`, the value-flow check MUST treat these as entry points: the
  start node, and every direct child that is the target of no connection in that Flowchart.
- **FR-009**: A producer MUST count as available to a consumer in the same Flowchart only when every path from every
  entry point to the consumer passes the producer.
- **FR-010**: Two writes to the same variable in one Flowchart MUST count as potentially concurrent only when neither
  reaches the other and they are not exclusive. They are exclusive when the set of entry points reaching each is
  non-empty and the two sets do not overlap.
- **FR-011**: The cyclic back-edge check MUST use the same multi-entry dominance as FR-009.
- **FR-012**: For a Flowchart with exactly one entry point, every value-flow result MUST equal today's.
- **FR-013**: Design-time code MUST NOT take a dependency on runtime projects, and the entry-point rule MUST be
  computed from the draft alone, with no catalog lookup.

#### Tests

- **FR-014**: Runtime tests MUST cover: starting at a non-start trigger child for HTTP + HTTP and HTTP + Cron;
  a direct run and a non-child trigger id falling back to the start node; a nested Flowchart (D2); a Flowchart in a
  loop restarting at the trigger (D3); and a trigger branch joining a shared node.
- **FR-015**: Validator tests MUST cover User Story 2's scenarios 1–5 and one single-entry regression case for each
  of dominance, concurrent writes and cyclic back-edges.
- **FR-016**: At least one end-to-end test MUST publish a multi-trigger Flowchart, fire the non-start trigger, and
  assert the branch that ran and that no bookmark is left for the trigger that did not fire.

### Key Entities

- **Trigger node id**: the id of the node whose trigger binding started the run. It is workflow-wide, persisted with
  the run's seed state, and already present on every activity invocation.
- **Entry point (design time)**: a node inside a Flowchart where a run can begin: the start node, or a direct child
  with no inbound connection.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A published Flowchart with two start triggers runs the fired trigger's branch in 100% of runs, for both
  trigger orders and for HTTP + HTTP and HTTP + Cron.
- **SC-002**: Zero runs of such a Flowchart leave a bookmark for a trigger that did not fire.
- **SC-003**: Drafts where each trigger branch reads its own trigger's result, or sets the same variable as another
  trigger branch, validate and publish with zero value-flow errors.
- **SC-004**: All existing Flowchart runtime, value-flow validator and Flowchart end-to-end tests pass unchanged.
- **SC-005**: Spec 239 can convert an Elsa 3 multi-trigger Flowchart without adding nodes, connections or
  workarounds to model the trigger choice.

## Assumptions

- One run is started by exactly one trigger, so trigger branches in one Flowchart are mutually exclusive within a run.
- A start trigger in a multi-trigger Flowchart heads its own branch and has no inbound connection. That is how
  Elsa 3 multi-trigger flowcharts are drawn.
- The runtime graph already marks trigger nodes through the node's execution-type metadata, which FR-005 can read.
- Studio needs no change. It already lets authors place several triggers in one Flowchart.

## Out of Scope

- Honouring a trigger nested deeper than a direct child by seeding a path through its ancestors.
- Sequence or any other composite honouring the trigger.
- A publish-time diagnostic for trigger nodes that a run can never reach.
- Changing how triggers are indexed, or which nodes count as triggers at runtime.
- Studio presentation of multiple entry points.
