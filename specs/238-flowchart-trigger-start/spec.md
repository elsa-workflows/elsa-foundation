# Feature Specification: Flowchart Starts at the Trigger That Fired

**Feature Branch**: `238-flowchart-trigger-start`

**Created**: 2026-10-16

**Status**: In revision: placement rule added for review (draft PR #2856, issue #2850)

**Input**: GitHub issue [#2850](https://github.com/elsa-workflows/elsa-foundation/issues/2850), "Flowchart ignores the trigger node: a multi-trigger flowchart always starts at its start node", plus the scope Joey set on 2026-10-16 (recorded under [Decisions](#decisions)).

## Context

A Flowchart can hold several start triggers, for example two HTTP endpoints that can start the workflow, or an HTTP
endpoint next to a Cron. Publishing indexes every one of them, and the trigger that fires passes its node id into
the run. The Flowchart ignores that id and always begins at its start node. Workflows that several triggers start
from one flowchart are common in Elsa 3, and the Elsa 3 → Elsa 4 conversion
([#2705](https://github.com/elsa-workflows/elsa-foundation/issues/2705), spec 239) must convert them 1:1. This spec
closes the gap in Elsa 4 itself, so the converter needs no workaround. Spec 239 depends on it.

The defect is one case of a wider mismatch:
- Publishing indexes a start trigger at **any depth**.
- Only BPMN and, after this spec, the Flowchart start a run where the trigger is. Every other composite begins at its
  own default entry.

A start trigger placed where its ancestors do not begin at it can therefore never start a correct run. Firing it
parks the run, or runs work that does not belong to that trigger. This spec makes the two sides agree: the
Flowchart honours its trigger children (US1), and publishing refuses a start trigger that no run could start from
(US4).

This spec contains no Elsa 3 code. Elsa 3 is referenced only as the behaviour to match.

### Verified current behaviour (origin/main `f4ddd62f2`, 2026-10-16)

- **Indexing.** Publishing indexes every trigger node in the workflow, nested ones included, with no per-workflow
  limit. `WorkflowTriggerBindingExtractor` flattens the whole tree, and `RecurringTriggerPumpOwnerScopingTests`
  publishes two Timers in one artifact. Nothing checks where a start trigger sits.
- **Trigger id.** The fired trigger's node id is persisted with the run's seed state (`runtime.triggerNodeId`) and
  handed to every activity invocation as `IRuntimeActivityExecutionContext.TriggerNodeId`. A redriven or resumed
  invocation reads the same persisted value.
- **Flowchart start.** `FlowchartExecutionEngine.StartAsync` always schedules `FlowchartGraph.SelectStartNode()`:
  the authored `startNodeId`, else the first child with no inbound connection, else the first child. It never reads
  `TriggerNodeId`. Only the BPMN engine seeds from it.
- **Effect today.**
  - When the start node is an HTTP endpoint or Event that did not fire, it suspends on its own bookmark and the run
    parks there, so the branch of the trigger that fired never runs.
  - When the start node is a Timer or Cron, it completes at once and the run walks the start node's branch, not the
    branch that fired.
- **Sequence start.** A Sequence always begins at its first child (`SequenceNavigator.SelectFirst`). A trigger at
  child 0 of a root Sequence is therefore correct. Every e2e trigger script, about 17 C# tests and the Elsa 3
  converter's outputs use that shape.
- **Defaults that start runs.** `Event` and `BpmnProcess` default `CanStartWorkflow` to true, and `Timer` and `Cron`
  always start. A mid-flow Event, Timer or Cron left at its default is indexed as a start trigger.
- **Value flow.** `ValueFlowValidator` computes Flowchart dominance only from `startNodeId`.
  - A result producer on another trigger's branch is not reachable from that root, so its consumers fail with
    `ValueFlow/UnavailableProducer`.
  - Two variable writes on different trigger branches can never reach each other, so they are reported as
    `ValueFlow/ConcurrentWrite`, although one run only ever takes one of those branches.
- **Loop-edge classification.** `FlowchartGraph.ComputeBackwardEdges` roots its depth-first walk at the start node,
  then at every other node in ordinal id order. A loop that only a trigger branch reaches can therefore be classified
  from an arbitrary node inside it, not from where the run enters it.
- **Preflight failures.** `WorkflowTriggerPreflightException` derives from `Exception`, not `ArgumentException`. The
  publishing endpoint translator therefore does not map it to a 400, and an author sees a server error rather than
  guidance.
- **Restore.** Restoring a retired publication re-runs the trigger extractor during activation
  (`RestorePublicationSlotRequestHandler` → `WorkflowActivationCoordinator` → `IWorkflowTriggerIndexer`).
- **Studio's warning.** Studio flags a start-capable Flowchart child that is not the start node and has no inbound
  connection as a *misplaced start trigger*, with a one-click "set as start" (`findMisplacedStartTriggers` in
  `flowchartStartNode.ts`). After this spec that shape is a valid entry, so the warning is wrong.
- **Elsa 3.** Elsa 3 honoured the trigger only in the Flowchart, and only for its direct children. Sequence always
  started at its first child.

## Decisions

| # | Question | Decision |
|---|---|---|
| D1 | Where the trigger is honoured at runtime | Only in the Flowchart, and only when the trigger node is a **direct child** of that Flowchart. Sequence, BPMN and every other composite are unchanged. Set by Joey. |
| D2 | Nested flowcharts | The trigger id is workflow-wide. A Flowchart acts on it only when the trigger is its own direct child. An outer Flowchart whose descendant (not child) is the trigger starts at its start node. At publish, such a nesting is valid only when it lies on the start path (D8). Set by Joey; revised 2026-10-16. |
| D3 | Flowchart inside a loop | A start trigger inside a loop body is refused at publish (D8). The runtime keeps restarting such a Flowchart at the trigger on each pass, but only for artifacts published before this rule. Revised by Joey 2026-10-16. |
| D4 | Redrive and resume | A redriven start begins at the same node, because the trigger id is persisted with the run. No new state is stored. |
| D5 | Design-time entry points | The value-flow check treats as entry points the Flowchart's start node plus every direct child with **no inbound connection** (FR-008). Such a node can only ever run as the trigger, so treating it as an entry point is sound, and the check needs no catalog data. Revised by Joey 2026-10-16 after planning found that the catalog marks every CLR activity, triggers included, as `Action`. |
| D6 | Concurrent writes across trigger branches | Two writes that only different entry points reach are mutually exclusive, not concurrent (FR-010). Accepted by Joey 2026-10-16. |
| D7 | Loop-edge classification | Direct children marked as trigger nodes in the executable become walk roots right after the start node, so a loop reached only from a trigger is classified from where the run enters it (FR-005). Accepted by Joey 2026-10-16. |
| D8 | Where a start trigger may sit (**start-path rule**) | A start trigger is valid only when every composite between the root and the trigger begins at the child that leads to it. **Root**: the trigger is the root activity. **Flowchart**: the trigger is a direct child with no inbound connection or is the start node; or the path continues through the start node. **Sequence**: the path continues through child 0. **Any other composite** (If, Switch, loops, Parallel, BPMN internals and the rest): refused. Set by Joey 2026-10-16. |
| D9 | Which nodes the rule checks | Only nodes that produce start trigger bindings (preflight outcome `Registered`). A trigger that is intentionally non-starting (for example `CanStartWorkflow = false`) is a mid-flow wait and may sit anywhere. |
| D10 | How the runtime learns composite semantics | Through a new strategy contract in `Elsa.Workflows.Runtime.Core`, keyed by structure kind. Given a composite and the trigger id, it names the child that a run started by that trigger legitimately begins at. Flowchart and Sequence implement it. A composite with no implementation refuses. This keeps activity semantics out of the runtime (spec 070). Set by Joey 2026-10-16. |
| D11 | How a refusal reaches the author | A 400 that names the trigger node, its activity type and the composite that does not begin at it, plus guidance: move the trigger to an entry position, or turn it into a mid-flow wait (`CanStartWorkflow = false`, or `Delay` instead of `Timer`/`Cron`). The check runs before anything is saved. Set by Joey 2026-10-16. |
| D12 | Publications made before this rule | The rule applies wherever start bindings are computed: publish preflight, activation, restore and artifact import. A retired publication with a misplaced start trigger therefore fails to restore, with the same guidance. Its saved executable and in-flight runs are untouched. Accepted by Joey 2026-10-16. |
| D13 | Studio's misplaced-trigger warning | Studio's warning follows D8. A start-capable Flowchart child with no inbound connection is a valid entry, not misplaced. A start-capable child **with** an inbound connection that is not the start node is misplaced. Accepted by Joey 2026-10-16. |

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

**Independent Test**: Validate a draft Flowchart whose start node is trigger A and whose second trigger B has no
inbound connection and feeds an activity that reads B's result. No value-flow error is reported. Add an inbound
connection from A's branch into that consumer: `ValueFlow/UnavailableProducer` is reported.

**Acceptance Scenarios**:

1. **Given** a draft Flowchart where trigger B, with no inbound connection, leads to consumer C, and C reads B's
   result, **When** the draft is validated, **Then** no `ValueFlow/UnavailableProducer` error is reported for C.
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

### User Story 3 - Single-start workflows behave exactly as before (Priority: P1)

As an operator of existing Elsa 4 workflows, I see no change in how a workflow with one correctly placed start
trigger, or a workflow run directly without a trigger, starts, routes, validates or publishes. That includes a
trigger as the root, as child 0 of a root Sequence, or as the start node of a root Flowchart.

**Why this priority**: The fix must not regress the common case.

**Independent Test**: The existing Flowchart, Sequence, HTTP, scheduling and BPMN test suites, and the e2e trigger
scripts, pass. The only changed tests are the ones listed under FR-028, each with its reason.

**Acceptance Scenarios**:

1. **Given** a Flowchart run directly (no trigger), **When** it starts, **Then** it starts at its start node.
2. **Given** a Flowchart whose only start trigger is its start node, **When** that trigger fires, **Then** it starts
   at the start node, as today.
3. **Given** a root Sequence whose child 0 is a start trigger, **When** it is published and the trigger fires,
   **Then** it publishes and runs as today.

---

### User Story 4 - A start trigger that no run could start from is refused at publish (Priority: P1)

As a workflow author, I publish a workflow in which a start trigger sits where no run could start at it. Examples:
child 2 of a Sequence, inside a loop body or an If branch, or a Flowchart child with an inbound connection that is
not the start node. The publish fails before anything is saved. The error names the trigger and the composite that
does not begin at it, and tells me how to fix it: move the trigger to an entry position, or make it a mid-flow
wait.

**Why this priority**: Without it, US1 leaves the runtime and the design-time check disagreeing. A mid-flow Event,
Timer or Cron left at its default would start runs in the middle of a Flowchart and skip the work upstream. Other
misplaced triggers would still park runs silently.

**Independent Test**: Publish a root Sequence whose child 1 is an HTTP endpoint with `CanStartWorkflow = true`. The
publish answers 400 with the trigger's node id and guidance, and no executable, binding or publication is saved. Set
`CanStartWorkflow = false` and publish again: it succeeds.

**Acceptance Scenarios**:

1. **Given** a root Sequence whose child 1 is a start trigger, **When** I publish, **Then** the publish is refused
   with a 400 naming the trigger and the Sequence.
2. **Given** a root Flowchart where a start-capable Event has an inbound connection and is not the start node,
   **When** I publish, **Then** the publish is refused.
3. **Given** a start trigger inside a `For` loop body, an `If` branch or a `Parallel` branch, **When** I publish,
   **Then** the publish is refused, naming that composite.
4. **Given** an outer Flowchart whose start node is an inner Flowchart, where the inner Flowchart has a start
   trigger child with no inbound connection, **When** I publish, **Then** the publish succeeds. The same inner
   Flowchart reached through a non-start child of the outer Flowchart is refused.
5. **Given** a mid-flow Event with `CanStartWorkflow = false` anywhere in the workflow, **When** I publish, **Then**
   the publish succeeds, because the Event produces no start binding.
6. **Given** a retired publication with a misplaced start trigger, **When** I restore it, **Then** the restore fails
   with the same guidance, and its saved executable is unchanged (D12).
7. **Given** an artifact import whose workflow has a misplaced start trigger, **When** it is imported, **Then** it is
   refused with the same guidance.

---

### User Story 5 - Studio's misplaced-trigger warning matches the rule (Priority: P2)

As a workflow author in Studio, I see a misplaced-trigger warning only for start triggers that publishing will
refuse. Two start triggers heading their own branches in a root Flowchart raise no warning. A start trigger with an
inbound connection that is not the start node does.

**Why this priority**: Studio currently suggests "set as start" for exactly the shape US1 makes valid, which would
undo a correct multi-trigger flowchart.

**Independent Test**: In the Studio unit tests:
- a root Flowchart with two start-capable branch heads reports no misplaced trigger;
- a start-capable child with an inbound connection that is not the start node is reported.

**Acceptance Scenarios**:

1. **Given** a Flowchart with start node A and a second start-capable child B with no inbound connection, **When** the
   designer evaluates it, **Then** B is not reported as misplaced.
2. **Given** a start-capable child C with an inbound connection that is not the start node, **When** the designer
   evaluates it, **Then** C is reported as misplaced, with guidance that matches the publish error.

---

### Edge Cases

- **Redrive (D4)**: a redriven or crash-recovered start reads the persisted trigger id and starts at the same node.
- **Trigger that is the start node**: valid whatever its inbound connections. No change.
- **Trigger with inbound connections that is not the start node**: refused at publish (US4). For an artifact
  published before this rule, the runtime still starts at it, as a root path.
- **Nested Flowchart (D2)**: valid when the inner Flowchart lies on the start path (it is the outer start node, or
  child 0 of a Sequence on the path); refused otherwise.
- **Flowchart inside a loop (D3)**: a start trigger in a loop body is refused at publish. For an artifact published
  before this rule, each pass restarts the Flowchart at the trigger.
- **Flowchart without an authored `startNodeId`**:
  - The start node is the one `SelectStartNode` picks (the first child with no inbound connection, else the first
    child), and the rule uses the same choice.
  - The value-flow check keeps today's behaviour and skips the Flowchart.
- **Trigger id that names no direct child** (for example a trigger in a sibling composite, or a stale id): the
  Flowchart starts at its start node.
- **Disconnected non-trigger node**: the value-flow check treats it as an entry point. It never runs, so this can
  only add a rejection when it feeds a consumer that reads a result from elsewhere.
- **A loop reached only from a trigger branch**: its loop-closing edge is the one that returns to where the trigger
  branch enters the loop (D7).
- **BPMN**:
  - A `BpmnProcess` as the root is a start trigger and is valid.
  - A `BpmnProcess` nested anywhere off the start path must not produce start bindings, so it must be intentionally
    non-starting (`CanStartWorkflow = false`).
  - The BPMN importer's nested-process node must author that (FR-027).
  - BPMN catch-event children already author `CanStartWorkflow = false`.
- **Graph (reusable) activity bodies**: trigger nodes inside a graph body are compiled without trigger metadata and
  are never indexed, so the rule never sees them. No change.
- **Composite kinds not known to the runtime** (a third-party structural activity): a start trigger beneath one is
  refused until that module implements the contract (D10).

## Requirements *(mandatory)*

### Functional Requirements

#### Runtime

- **FR-001**: When a Flowchart starts and the run's trigger node id equals the id of one of the Flowchart's direct
  children, the Flowchart MUST schedule that child as its first node, in place of the start node.
- **FR-002**: In every other case (no trigger id, or a trigger id that is not a direct child) the Flowchart MUST
  start exactly as today.
- **FR-003**: The root execution path and root scope MUST be owned by the node the Flowchart started at. Every place
  that rebuilds the initial state when none is persisted MUST use the same choice.
- **FR-004**: A Flowchart started again inside a loop MUST apply FR-001 on every pass. This applies only to
  artifacts published before the placement rule, since D3 refuses new ones.
- **FR-005**: Loop-edge classification MUST root its walk at the start node first, then at every direct child marked
  as a trigger node in ordinal id order, then at the remaining nodes in ordinal id order. For a graph whose trigger
  children are all reachable from the start node, the classification MUST equal today's.
- **FR-006**: Sequence, BPMN and all other composites MUST NOT change how they start.
- **FR-007**: The runtime change MUST NOT add new persisted state or metadata keys. It uses the existing
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

#### Start-trigger placement (publish)

- **FR-017**: For every node whose trigger preflight outcome is `Registered` (it produces at least one start
  binding), the trigger preflight MUST check the start-path rule (D8) along the path from the root to that node.
  Nodes that are intentionally non-starting MUST NOT be checked (D9).
- **FR-018**: At each composite on that path, the preflight MUST ask the start-entry strategy registered for the
  composite's structure kind which child a run started by this trigger begins at. When that child is not the next
  node on the path, the placement is invalid. When no strategy is registered for the kind, or the composite has no
  structure, it is also invalid.
- **FR-019**: The Flowchart strategy MUST name these children:
  - the trigger itself, when it is a direct child and either has no inbound connection or is the start node;
  - otherwise the start node, as chosen by `SelectStartNode`;
  - nothing for a direct-child trigger that has an inbound connection and is not the start node.
- **FR-020**: The Sequence strategy MUST name child 0 (`SequenceNavigator.SelectFirst`).
- **FR-021**: The strategy contract MUST live in `Elsa.Workflows.Runtime.Core`. It MUST be registered by the
  composite's own feature and MUST NOT require the runtime to know any activity's structure (spec 070).
- **FR-022**: An invalid placement MUST fail the trigger preflight with a `WorkflowTriggerPreflightException` whose
  facet is `Placement`. It carries the trigger's node id and activity type, and the id and structure kind of the
  composite that does not begin at it. It MUST fail before any executable, binding, schedule or publication is
  saved.
- **FR-023**: A `Placement` failure from the publish endpoint MUST answer 400. The message names the trigger and the
  composite and gives the guidance from D11. A preflight failure caused by host configuration (a blank provider id,
  for example) MUST NOT become a 400.
- **FR-024**: The same check MUST apply when start bindings are computed for activation, for restoring a retired
  publication, and for artifact import. Each refuses with the same placement guidance through its existing failure
  channel (D12).
- **FR-025**: Runs, bookmarks, bindings and schedules that already exist MUST NOT be changed by this rule.
- **FR-026**: The new contract and its two implementations MUST be listed in the owning `EXTENSION_POINTS.md` files
  in the same change.
- **FR-027**: The BPMN importer MUST author `CanStartWorkflow = false` on every nested `BpmnProcess` node it emits,
  so a nested process never produces start bindings. If verification shows the importer emits no such node, this
  requirement is void, and the plan records the evidence.
- **FR-028**: Tests that build executables with misplaced start triggers MUST be updated, each with its reason named
  in the PR (§2.21.1). Already known: the runtime extractor and indexer tests whose `Elsa.Sequence` roots carry no
  structure.

#### Studio

- **FR-029**: Studio's misplaced-start-trigger check MUST follow D8 and D13 for a Flowchart:
  - a start-capable child with no inbound connection is not misplaced;
  - a start-capable child with an inbound connection that is not the start node is misplaced;
  - the "set as start" action is offered only for the misplaced case.

#### Tests

- **FR-014**: Runtime tests MUST cover:
  - starting at a non-start trigger child for HTTP + HTTP and HTTP + Cron;
  - a direct run and a non-child trigger id falling back to the start node;
  - a nested Flowchart (D2);
  - a Flowchart in a loop restarting at the trigger (D3, for artifacts published before the rule);
  - a trigger branch joining a shared node.
- **FR-015**: Validator tests MUST cover User Story 2's scenarios 1–5 and one single-entry regression case for each
  of dominance, concurrent writes and cyclic back-edges.
- **FR-016**: At least one end-to-end test MUST publish a multi-trigger Flowchart, fire the non-start trigger, and
  assert the branch that ran and that no bookmark is left for the trigger that did not fire.
- **FR-030**: Placement tests MUST cover each scenario in User Story 4, the root, root-Sequence child-0 and
  root-Flowchart start-node placements, and a structure kind with no registered strategy. At least one test MUST go
  through the publish endpoint and assert the 400 and that nothing was saved.

### Key Entities

- **Trigger node id**: the id of the node whose trigger binding started the run. It is workflow-wide, persisted with
  the run's seed state, and already present on every activity invocation.
- **Entry point (design time)**: a node inside a Flowchart where a run can begin: the start node, or a direct child
  with no inbound connection.
- **Start path**: the chain of composites from the root to a start trigger. A placement is valid when each composite
  on it begins at the next node of the chain for a run that this trigger starts.
- **Start-entry strategy**: a per-structure-kind answer to "which child does a run started by trigger T begin at?",
  supplied by the composite's own module.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A published Flowchart with two start triggers runs the fired trigger's branch in 100% of runs, for both
  trigger orders and for HTTP + HTTP and HTTP + Cron.
- **SC-002**: Zero runs of such a Flowchart leave a bookmark for a trigger that did not fire.
- **SC-003**: Drafts where each trigger branch reads its own trigger's result, or sets the same variable as another
  trigger branch, validate and publish with zero value-flow errors.
- **SC-004**: All existing Flowchart, Sequence, HTTP, scheduling and BPMN tests and all e2e trigger scripts pass. The
  only changed tests are those named under FR-028.
- **SC-005**: Spec 239 can convert an Elsa 3 multi-trigger Flowchart without adding nodes, connections or
  workarounds to model the trigger choice.
- **SC-006**: 100% of the misplaced start-trigger placements in User Story 4 are refused with a 400 and guidance, and
  none of them leaves anything saved.

## Assumptions

- One run is started by exactly one trigger, so trigger branches in one Flowchart are mutually exclusive within a run.
- A start trigger in a multi-trigger Flowchart heads its own branch and has no inbound connection. That is how
  Elsa 3 multi-trigger flowcharts are drawn.
- The runtime graph already marks trigger nodes through the node's execution-type metadata, which FR-005 can read.
- Publishing, activation, restore and artifact import all compute start bindings through
  `IWorkflowTriggerBindingExtractor.Evaluate`, so one check there covers FR-024.

## Out of Scope

- Honouring a trigger nested deeper than a direct child by seeding a path through its ancestors. The placement rule
  refuses those placements instead.
- Sequence or any other composite honouring the trigger.
- Changing how triggers are indexed, or which nodes count as triggers at runtime.
- Re-evaluating or rewriting publications that are already active.
- Trigger nodes inside graph (reusable) activity bodies.
- Studio presentation of multiple entry points beyond the warning in User Story 5.
