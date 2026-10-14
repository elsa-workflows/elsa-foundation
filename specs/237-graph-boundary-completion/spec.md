# Feature Specification: Graph Boundary Completion From Any Depth

**Feature Branch**: `237-graph-boundary-completion`

**Created**: 2026-10-14

**Status**: In progress — implementation on branch `237-graph-boundary-completion`

**Input**: GitHub issue [#2750](https://github.com/elsa-workflows/elsa-foundation/issues/2750), "Graph activities: let a descendant node complete the nearest graph boundary with a chosen outcome", plus the maintainer interview of 2026-10-14 recorded under [Decisions](#decisions).

## Context

A graph activity (a reusable activity published by the activity-graph provider) completes today only when its
direct entry node completes. Its boundary outcome is that entry's single outcome, translated through schema-2
outcome mappings. Nothing deeper in the graph body can end the graph activity with a chosen outcome. A Flowchart,
the usual entry, completes only with `Done` or `Break`, and an unconnected outcome ends its path without bubbling
up. The `control` intrinsic completes only its own node, and `finish` ends the whole workflow execution.

Specs [138](../138-reusable-boundary-outcomes/spec.md) and [221](../221-reusable-activity-definitions/spec.md)
deferred "explicit return and arbitrary descendant completion" on purpose. Spec 221 FR-038 requires that, when it
arrives, it is scoped to the **nearest** graph boundary. Spec 221 FR-036 denies graph descendants workflow-root
mutation, so `finish` is not the answer inside a graph.

Spec 236 (#2706) converts reusable workflows from an earlier Elsa generation and needs a construct it can emit for
"end this reusable activity now with outcome X from anywhere inside it". It is on hold until this spec ships. The
construct defined here is a generic graph feature; spec 236 adapts to it.

### Verified current behaviour (origin/main `c6ae516ca`, 2026-10-14)

- The graph activity schedules only its entry node and rejects completion from any other node.
- Design-time validation checks outcome-mapping sources only against the entry's emitted outcomes.
- When a composite completes early, nothing cancels its other live descendants in storage. Their bookmarks,
  durable timers and queued work stay; their later completions are just ignored. This holds for Flowchart `Break`,
  Parallel threshold completion and `finish`. No test asserts store state after early completion.
- The runtime already offers a durable, recursive subtree cancellation that deletes bookmarks, durable timers and
  queued work in the same commit as the parent's completion. Today only BPMN uses it.
- No runtime mechanism lets a node signal an ancestor other than its direct parent. `Break` is forwarded one level
  at a time by each composite, and loops consume it.

## Decisions

Settled in the maintainer interview on 2026-10-14:

| # | Question | Decision |
|---|---|---|
| D1 | Activity or intrinsic | A catalog activity named **Complete** in the Graph module, with a stable type key and one input, `outcome`. |
| D2 | Crossing composites | The runtime delivers the exit straight to the nearest enclosing graph boundary. Intermediate composites (Sequence, If, Switch, Flowchart, loops, Parallel) need no change and cannot intercept it. `Break` is unchanged and unrelated. |
| D3 | What it carries | Exactly one literal outcome naming a boundary outcome by reference key. No expressions, no multiple outcomes. |
| D4 | Declaration and validation | Schema 2 validation is relaxed, not replaced: each Complete node must name an emitted boundary outcome, and each emitted boundary outcome must be reachable through a mapping or a Complete node. Existing schema-2 manifests stay valid. |
| D5 | No enclosing boundary | Rejected when a workflow is validated or published; if it still runs, the node faults with a clear diagnostic. |
| D6 | Stopping remaining work | The boundary durably cancels the rest of its body in the same commit as its completion. The same leak in Flowchart `Break`, Parallel threshold and `finish` is tracked in #2752, not fixed here. |
| D7 | Values | No value travels with the exit. Public outputs still come only from output mappings. |
| D8 | Studio | Backend only. The activity appears in the palette with a plain `outcome` input; a richer editor is a Studio follow-up. |

## User Scenarios & Testing *(mandatory)*

### User Story 1 - End a reusable activity from deep inside its body (Priority: P1)

As a workflow author, I place a **Complete** node anywhere inside a reusable activity's graph (inside a Flowchart
branch, a Sequence, an If, a loop body or a Parallel branch) and choose one of the reusable activity's declared
outcomes. When that node runs, the reusable activity ends at once with that outcome, and the parent workflow
continues along the connection for that outcome.

**Why this priority**: This is the missing capability. Without it, a reusable activity whose body is a Flowchart
cannot end with an authored outcome such as `succeed` or `fail`, and spec 236 cannot convert such activities.

**Independent Test**: Publish a reusable activity with boundary outcomes `succeed` and `fail` whose entry is a
Flowchart; one branch ends in Complete(`fail`). Run it from a parent Flowchart with separate connections for
`succeed` and `fail`, and verify only the `fail` connection runs.

**Acceptance Scenarios**:

1. **Given** a reusable activity whose Flowchart body reaches Complete(`fail`), **When** it runs from a parent workflow, **Then** the reusable activity completes with outcome `fail` and only the parent's `fail` connection runs.
2. **Given** Complete inside a loop body inside the graph, **When** it runs on the second iteration, **Then** the loop does not run further iterations and the reusable activity completes with the chosen outcome.
3. **Given** Complete inside one branch of a Parallel inside the graph, **When** it runs, **Then** the reusable activity completes with the chosen outcome without waiting for the other branches.
4. **Given** a reusable activity A whose body contains reusable activity B, and B's body contains Complete(`x`), **When** it runs, **Then** only B completes with `x`; A continues as B's outcome routes it.
5. **Given** the entry node of the graph is itself a Complete node, **When** the reusable activity runs, **Then** it completes immediately with the chosen outcome.
6. **Given** a graph body that never reaches a Complete node, **When** its entry completes naturally, **Then** the boundary outcome comes from the outcome mappings exactly as today.

---

### User Story 2 - Remaining work in the body stops for good (Priority: P1)

As an operator, when a reusable activity ends through a Complete node, I need the other work still in flight in its
body to stop permanently: waiting bookmarks, scheduled durable timers and queued work in that body must not
resume, fire or run later, and must not remain in storage.

**Why this priority**: An early exit that leaves suspended siblings behind would resume work the author meant to
abandon, or leave orphaned state in storage. The current early-completion paths have exactly this gap.

**Independent Test**: Publish a reusable activity whose body forks into a branch that waits on a bookmark, a branch
that waits on a durable timer, and a branch that reaches Complete. After the reusable activity completes, verify
that the bookmark and the timer are gone from storage, that resuming the old bookmark does nothing, and that the
timer never fires.

**Acceptance Scenarios**:

1. **Given** a sibling branch suspended on a bookmark, **When** another branch reaches Complete, **Then** the bookmark is removed from storage in the same commit that completes the reusable activity.
2. **Given** a sibling branch waiting on a durable timer, **When** another branch reaches Complete, **Then** the timer is removed from storage in the same commit and never fires.
3. **Given** queued but not yet started work in the body, **When** Complete runs, **Then** that work is removed and never runs.
4. **Given** the cancelled body, **When** the workflow continues after the reusable activity, **Then** no execution in the cancelled body is left in a running, waiting or suspended state.

---

### User Story 3 - Invalid placements are caught before they run (Priority: P2)

As a workflow author, I want publishing to reject Complete nodes that name an outcome the reusable activity does
not declare, and Complete nodes placed where no reusable activity encloses them, so mistakes surface while
authoring rather than at run time.

**Why this priority**: The converter and authors rely on validation to prove the outcome set. Runtime faults
remain as a safety net, but they should be rare.

**Independent Test**: Attempt to publish a reusable activity with Complete(`unknown`), and a plain workflow with a
Complete node at its root; both are rejected with diagnostics that name the node and the problem.

**Acceptance Scenarios**:

1. **Given** a reusable activity whose body contains Complete(`unknown`) and `unknown` is not an emitted boundary outcome, **When** it is published, **Then** publication is rejected with a diagnostic naming the node and the outcome.
2. **Given** a Complete node whose outcome is not a single literal (empty, an expression, or a list), **When** it is published, **Then** publication is rejected with a diagnostic naming the node.
3. **Given** a reusable activity that declares outcome `fail`, reached only by a Complete node and not by any mapping, **When** it is published, **Then** publication succeeds.
4. **Given** a reusable activity that declares an outcome reached by neither a mapping nor a Complete node, **When** it is published, **Then** publication is rejected as today.
5. **Given** a plain workflow (not a reusable activity) that contains a Complete node, **When** it is validated or published, **Then** it is rejected with a diagnostic naming the node.
6. **Given** a Complete node that somehow runs without an enclosing reusable activity, **When** it runs, **Then** the node faults with a diagnostic that says no enclosing graph boundary was found, and the workflow is not silently finished.

---

### User Story 4 - Existing reusable activities keep working (Priority: P2)

As an operator of published reusable activities, I need every manifest that is valid today to stay valid and
behave identically after this change.

**Why this priority**: Schema 2 is persisted. The change must relax its rules, never tighten them.

**Independent Test**: Run the existing reusable-activity unit tests and e2e scripts unchanged; they pass.

**Acceptance Scenarios**:

1. **Given** a schema-1 or schema-2 manifest published before this change, **When** it is validated and run after, **Then** it validates and behaves exactly as before.

### Edge Cases

- **Two Complete nodes race**: two branches of a Parallel inside the same graph reach Complete in the same drain. The first exit committed decides the outcome; the later one is ignored and does not fault the workflow.
- **Complete and natural completion race**: the entry completes naturally while a Complete is pending. Whichever commits first wins; the other is ignored.
- **Body ends naturally but only Complete nodes reach the outcomes**: a reusable activity with no outcome mappings, whose outcomes are all reached by Complete nodes, has a path that ends without reaching one. The boundary faults with a diagnostic saying the body ended without selecting an outcome; it never invents `Done`.
- **Missing required output**: the graph exits early before a required public output's source variable is set. The boundary completion faults, as it does for natural completion with a missing required output (spec 221 FR-037).
- **Complete inside a nested reusable activity's loop**: the loop does not consume it; the nearest reusable activity, the inner one, completes.
- **Break and Complete in the same body**: `Break` keeps its current meaning (ends the nearest loop or bubbles up as `Break`); Complete is independent of it.
- **Complete inside a loop or Parallel that is outside any graph, in a plain workflow**: rejected by workflow validation (User Story 3, scenario 5).
- **Retry of the reusable activity after an early exit faulted it**: the retry runs the body from its entry again with a fresh scope, as for any graph boundary retry.
- **The bookmark of a cancelled sibling is resumed by an external stimulus after the exit**: nothing resumes, because the bookmark no longer exists.

## Requirements *(mandatory)*

### Functional Requirements

**The Complete activity**

- **FR-001**: The Graph module MUST provide a catalog activity named **Complete** with a stable type key and exactly one authored input, `outcome`.
- **FR-002**: The `outcome` input MUST hold exactly one literal string naming a boundary outcome of the enclosing reusable activity by its reference key. Expressions, empty values and multiple outcomes MUST NOT be accepted.
- **FR-003**: When a Complete node runs, it MUST end the **nearest** enclosing graph boundary with the boundary outcome it names. "Nearest" means the closest ancestor execution that is a graph boundary; an outer graph boundary is never reached from inside an inner one.
- **FR-004**: The exit MUST reach that boundary regardless of the composites between the Complete node and the boundary, including Sequence, If, Switch, Flowchart, all loops and Parallel. Those composites MUST NOT need any change to support it, and none of them may consume or redirect it.
- **FR-005**: `Break` semantics MUST NOT change. Complete MUST NOT be expressed as, or recognised as, a `Break`.

**Boundary completion**

- **FR-006**: The boundary MUST complete with the boundary outcome named by the first committed exit request from its body. Exit requests that arrive after the boundary has stopped running MUST be ignored without fault.
- **FR-007**: A natural completion of the entry node MUST continue to select the boundary outcome through outcome mappings exactly as today. A graph with no Complete node MUST behave exactly as today.
- **FR-008**: Public outputs on early exit MUST be produced only through the existing output mappings, evaluated over the graph's state at the moment of exit. The Complete node MUST NOT carry a value. A missing required output MUST fault the boundary completion, as for natural completion.
- **FR-009**: Boundary output capture, outcome recording, completion of the boundary and cancellation of its remaining body MUST commit atomically.

**Stopping the remaining body**

- **FR-010**: When the boundary completes through a Complete node, every other live execution in its body, at any depth, MUST be cancelled durably: their bookmarks, durable timers and queued work items MUST be deleted from storage in the same commit as the boundary completion (FR-009).
- **FR-011**: After that commit, no cancelled execution may resume, fire or run, and none may remain in a running, waiting or suspended state.
- **FR-012**: Cancellation MUST be limited to the boundary's own body. Work outside the boundary, including the parent workflow's other branches, MUST be unaffected.

**Design-time validation**

- **FR-013**: Publishing a reusable activity MUST reject any Complete node in its body whose outcome is not a single literal naming an emitted boundary outcome, with a diagnostic that names the node and the offending value.
- **FR-014**: For schema-2 manifests, an emitted boundary outcome MUST count as reachable when at least one outcome mapping or at least one Complete node in the body names it. The rule that every emitted outcome must be reachable otherwise stays.
- **FR-015**: Every schema-1 and schema-2 manifest that validates today MUST still validate and MUST compile to a runtime descriptor with the same behaviour.
- **FR-016**: Validating or publishing a workflow that is not a reusable activity body MUST reject any Complete node it contains, with a diagnostic that names the node.
- **FR-017**: Schema-1 manifests MUST reject any Complete node in their body. Schema 1 stays frozen, as spec 138 kept it; an author who needs an early exit migrates the draft to schema 2.

**Runtime safety net**

- **FR-018**: A Complete node that runs with no enclosing graph boundary MUST fault with a diagnostic stating that no enclosing graph boundary was found. It MUST NOT finish the workflow or complete any other ancestor.
- **FR-019**: A Complete node whose named outcome is not a boundary outcome of the nearest boundary at run time MUST fault with a diagnostic naming the outcome and the boundary.

**Follow-up and coordination**

- **FR-020**: The durable-cancellation gap in Flowchart `Break`, Parallel threshold completion and `finish` is tracked separately in [#2752](https://github.com/elsa-workflows/elsa-foundation/issues/2752). This spec MUST NOT change those paths and MUST NOT depend on that issue.
- **FR-021**: The Complete activity's type key, input key and validation rules MUST be documented where spec 236 and other emitters can find them.

### Key Entities

- **Complete activity**: A catalog leaf activity placed inside a graph body. Its only input is one literal boundary outcome reference key.
- **Graph boundary**: The graph activity execution that encloses a body. It is the target of every Complete node in its body and not of Complete nodes in nested graph boundaries.
- **Boundary outcome**: A public outcome declared by the reusable activity's contract, identified by reference key at design time and by name at run time.
- **Exit request**: The runtime signal from a Complete node to its nearest boundary, carrying the chosen boundary outcome. The first committed one wins.
- **Body subtree**: All executions under the boundary's entry node; the unit that is durably cancelled on exit.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A reusable activity can end with any of its declared outcomes from a Complete node placed at any depth, proven for at least Flowchart, Sequence, If, Switch, a loop and Parallel between the node and the boundary, plus one nested reusable activity.
- **SC-002**: After an early exit, 0 bookmarks, 0 durable timers and 0 queued work items from the cancelled body remain in storage, proven against both the in-memory and the relational store with a sibling suspended on a bookmark and a sibling waiting on a durable timer.
- **SC-003**: 100% of existing reusable-activity unit tests and e2e scripts pass without modification.
- **SC-004**: Every invalid Complete placement in User Story 3 is rejected at publication with a diagnostic that names the node; none reaches run time in those tests.
- **SC-005**: Spec 236 can express "end this reusable activity with outcome X from anywhere in it" as one authored node, using only the type key, input key and rules documented by this spec.

## Assumptions

- "Graph boundary" means a graph activity execution from the activity-graph provider. No other composite becomes an exit target in this spec.
- The existing durable subtree cancellation is reused for FR-010; this spec does not introduce a new cancellation store mechanism.
- The Complete activity's own execution ends as completed; it is not reported as cancelled.
- Natural completion of the entry is unchanged and still needs its outcome mappings; a Complete node does not replace them.
- Retries of a faulted boundary keep their current semantics.
- Studio work is out of scope. The activity appears in the palette with a plain text `outcome` input; an outcome picker bound to the enclosing reusable activity's contract is a Studio follow-up.
- Nothing here is specific to reusable workflows from earlier Elsa generations; spec 236 maps its constructs onto this one.

## Out of Scope

- Durable sibling cancellation for Flowchart `Break`, Parallel threshold completion and `finish` ([#2752](https://github.com/elsa-workflows/elsa-foundation/issues/2752), FR-020).
- Carrying a value or multiple outcomes with Complete.
- Expressions in the `outcome` input.
- A new manifest schema version.
- Studio authoring support beyond default palette visibility.
- Elsa 3 conversion, which belongs to spec 236.
