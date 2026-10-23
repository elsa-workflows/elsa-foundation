# Feature Specification: Refuse concurrent captures into one variable

**Feature Branch**: `agent/claude/3198-capture-concurrency`

**Created**: 2026-10-22

**Status**: Draft

**Issue**: [#3198](https://github.com/elsa-workflows/elsa-foundation/issues/3198), part of epic
[#3052](https://github.com/elsa-workflows/elsa-foundation/issues/3052)

**Input**: ADR 0048 §5 is accepted. It requires publication to refuse output captures that can run concurrently into
one shared variable, unless an explicit deterministic merge or reduction governs them, and its required evidence
lists "shared-ancestor parallel capture rejection". That rule was never built for captures. Today the last committed
capture wins, silently, for workflow variables and, since spec 265, for graph activity variables.

**Depends on**:
- ADR 0048 (accepted): container output captures; §5 is the rule this spec builds.
- Spec 265 (merged, #3199): graph activity bodies capture into the activity's own variables. Its tests pin that
  sequential captures into one graph variable are accepted and the last value wins
  (`Compilation_accepts_two_body_nodes_capturing_into_one_graph_variable`,
  `Sequential_captures_into_one_graph_variable_leave_the_last_value`). They must keep passing.

**What exists today** (research, 2026-10-22):
- Workflow draft validation already refuses concurrent *intrinsic* writes (Set, Merge, Reduce) into one variable with
  `ValueFlow/ConcurrentWrite`: "Collect branch contributions and apply one explicit deterministic merge or reduction
  after the join". It does not see output captures. It decides concurrency from a fixed list of structure kinds it
  knows (Parallel, Flowchart, Sequence and a few others) and does not recognise BPMN.
- That check runs when a workflow draft is validated, and draft promotion re-runs the full draft validation. Graph
  activity drafts and graph activity compilation do not run it. Workflow executable compilation has no backstop for
  it, and source-module reconciliation materializes and publishes versions after only the credential-literal rule.
- Today's Flowchart branch of the rule ignores outcome ports. Paths that leave different ports of a decision node
  count as concurrent, so captures or Sets on the two outcomes of an If inside a Flowchart are refused today.
- Container-scoped capture targets are refused at publication today. Only workflow-root and graph-variable captures
  exist.
- Concurrency comes from Parallel branches, Flowchart forks (several outbound connections, `parallelFork` and
  `inclusiveFork` policies), and BPMN parallel and inclusive gateways, event-based races, implicit splits and
  non-sequential multi-instance. Loops run sequentially; Elsa 4 has no parallel ForEach. If and Switch run exactly one
  branch. Background placement only applies to leaves and adds no structural concurrency.

## Decisions

Confirmed by the owner on 2026-10-23, after review of the code.

| # | Decision | Why |
|---|----------|-----|
| D-1 | **One rule for every variable write, all writers reported at once.** A capture is a write. Captures and intrinsic writes (Set, Merge, Reduce) are checked together by the existing concurrent-write rule, so a capture and a Set that can run concurrently into one variable collide too. There is one finding per variable, keeping today's path and type, and it names every node that writes that variable concurrently, not only the first pair, so the author can fix them all in one pass. | ADR 0048 §5 and the existing `ValueFlow/ConcurrentWrite` rule say the same thing. Two rules would drift, and a capture racing a Set is the same defect. Reporting every writer avoids a fix-one, republish, find-the-next loop. |
| D-2 | **Each structure declares how its children relate in time.** For any two of its children, a structure states whether one always runs before the other, whether they can run at the same time, whether they are mutually exclusive, or that it does not say. This is declared by the structure, not by a list kept in the validator. Every built-in structure declares it fully, BPMN included. A structure that declares nothing is treated as running its children one after another. In this spec only the concurrent-write rule uses the declaration; the ordering rules move to it later (#3282). | Structures are pluggable, and BPMN is missed today because the validator does not know it. The structure's author is the only one who knows its semantics. Describing the whole time relation now costs little, because each structure must describe its children's timing anyway to answer concurrency. It also avoids a second declaration for ordering later. |
| D-2a | **Flowchart concurrency follows its ports.** Paths that leave one node through the same outcome port run concurrently (an implicit fork). All outbound paths of a node with the `parallelFork` or `inclusiveFork` policy count as concurrent. Paths that leave different ports of any other node are mutually exclusive, because an activity completes with exactly one outcome. Nodes on one path are ordered, and branches that start at different trigger entries stay exclusive as today. | It matches what the engine does: it schedules the outbound connections whose port matches the single completed outcome, or all of them under `parallelFork`. No built-in activity completes with more than one outcome; #3285 makes the runtime enforce that. |
| D-3 | **Refused where the author is, and again at publication.** The rule runs in workflow draft validation (which draft promotion already re-runs), in graph activity draft validation, and in graph activity compilation. Workflow executable compilation also runs it as a backstop, so a version that never went through draft validation is still refused. That covers source-module reconciliation, which today runs only the credential-literal rule (#3283). The analysis lives in one place and serves all of these. | ADR 0048 §5 says "fail publication", and executable compilation is the one step every executable passes through. Today the intrinsic rule is workflow-draft-only, and graph activities have no check at all. |
| D-4 | **Already published executables keep running.** Nothing is re-validated retroactively. A definition that breaks the rule is refused at its next publication, which includes recompiling an existing version. | Published executables are immutable and content-addressed (ADR 0038). |

### Follow-ups filed while settling the decisions

All four are sub-issues of epic #3052 and are out of scope here.

- [#3282](https://github.com/elsa-workflows/elsa-foundation/issues/3282): the value-flow ordering rules and the expression picker take ordering from the structures' declarations (D-2), instead of hard-coded structure kinds.
- [#3283](https://github.com/elsa-workflows/elsa-foundation/issues/3283): reconciled workflow versions pass the full draft validation gate.
- [#3284](https://github.com/elsa-workflows/elsa-foundation/issues/3284): workflow executable compilation reports every error in one refusal.
- [#3285](https://github.com/elsa-workflows/elsa-foundation/issues/3285): the runtime enforces one outcome per activity completion.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Concurrent captures in a workflow are refused (Priority: P1)

A workflow author puts two activities on parallel branches, for example the two branches of a Parallel or two
targets of a Flowchart fork, and captures an output of each into the same workflow variable. Today both captures
commit and whichever finishes last wins, so the variable's value depends on timing. After this feature, validating
the draft reports which two captures conflict and on which variable, and publication refuses the workflow until
the author captures into separate variables and combines them after the join.

**Why this priority**: This is the defect ADR 0048 §5 exists to prevent, and workflows are where most captures are
authored.

**Independent Test**: Validate and publish a workflow with a Parallel whose two branches capture into one variable;
both are refused, naming both nodes and the variable. Replace one capture with a separate variable and a single
Set after the join; both succeed.

**Acceptance Scenarios**:

1. **Given** a Parallel whose two branches each capture an output into variable `total`, **When** the draft is
   validated, **Then** a concurrent-write finding names both capturing nodes and `total`, and suggests capturing
   into separate variables and combining them after the join.
2. **Given** the same workflow, **When** it is published, **Then** publication is refused with the same finding.
3. **Given** a Flowchart where one node forks to two nodes that each capture into `total`, **When** the draft is
   validated, **Then** the capture conflict is reported.
4. **Given** a branch that captures into `total` while a concurrent branch runs a Set intrinsic on `total`,
   **When** the draft is validated, **Then** the conflict is reported.
5. **Given** a Sequence where two nodes capture into `total` one after another, **When** the draft is validated
   and published, **Then** both succeed, and at run time the later capture's value remains.

---

### User Story 2 - Exclusive and sequential paths stay allowed (Priority: P1)

An author captures into one variable from both arms of an If, from several cases of a Switch, from consecutive
nodes of a Sequence or a Flowchart path, or from each iteration of a loop. Only one of these writes can run at a
time, so they are not concurrent and must not be refused.

**Why this priority**: A rule that refuses ordinary sequential and branching workflows would be worse than the
gap. This is the guard against false refusals, and it shares P1 with Story 1.

**Independent Test**: Validate and publish workflows with captures into one variable from If arms, Switch cases,
Sequence steps, a sequential Flowchart path and a loop body; all succeed.

**Acceptance Scenarios**:

1. **Given** an If whose Then and Else each capture into `result`, **When** the workflow is validated and
   published, **Then** no conflict is reported.
2. **Given** a Switch whose cases each capture into `result`, **When** it is validated, **Then** no conflict is
   reported.
3. **Given** a loop whose body captures into `result`, **When** it is validated, **Then** no conflict is reported.
4. **Given** a Flowchart path `A → B` where A and B both capture into `result`, **When** it is validated,
   **Then** no conflict is reported.
5. **Given** a Flowchart decision node whose outcomes lead to two exclusive nodes that each capture into
   `result`, **When** it is validated, **Then** no conflict is reported.

---

### User Story 3 - Concurrent captures in a graph activity body are refused (Priority: P2)

An activity author builds a graph activity whose body has a Parallel or a Flowchart fork, and two concurrent body
nodes capture into one of the activity's own variables. Validating the activity draft reports the conflict, and
publishing the activity refuses it, as for a workflow.

**Why this priority**: Spec 265 made graph-variable captures possible. Graph activities have no write-concurrency
check of any kind today.

**Independent Test**: Validate and publish a graph activity whose body Parallel has two branches capturing into
one graph variable; both are refused. The spec 265 sequential-capture tests still pass.

**Acceptance Scenarios**:

1. **Given** a graph activity body with a Parallel whose branches capture into graph variable `total`, **When**
   the activity draft is validated, **Then** a concurrent-write finding names both body nodes and `total`.
2. **Given** the same activity, **When** it is published, **Then** publication is refused.
3. **Given** a graph activity body where two nodes in a Sequence capture into `total`, **When** it is validated,
   compiled and run, **Then** it is accepted and the later value remains.

---

### User Story 4 - Every structure declares its concurrency, BPMN included (Priority: P2)

A module author ships a structure activity. The structure states which of its children can run at the same time
and which are mutually exclusive, so the concurrent-write rule works for it without the validator knowing it.
The built-in BPMN structure declares its parallel and inclusive gateways, event-based races, implicit splits and
non-sequential multi-instance as concurrent, and its exclusive gateways as exclusive.

**Why this priority**: Without it, BPMN processes are never checked, and every new structure would need a change
to the validator.

**Independent Test**: A BPMN process whose parallel gateway leads to two tasks capturing into one variable is
refused. A test structure registered only in the test declares two concurrent slots, and captures into one
variable from those slots are refused without any change to the validator.

**Acceptance Scenarios**:

1. **Given** a BPMN process whose parallel gateway splits to two tasks that each capture or Set `total`, **When**
   it is validated, **Then** the conflict is reported.
2. **Given** a BPMN exclusive gateway whose two outgoing tasks capture into `total`, **When** it is validated,
   **Then** no conflict is reported.
3. **Given** a structure that declares nothing about its children, **When** two of its children capture into one
   variable, **Then** they are treated as running one after another and no conflict is reported.
4. **Given** the existing intrinsic concurrent-write tests, **When** the structure knowledge moves out of the
   validator, **Then** they pass unchanged.

---

### Edge Cases

- **Two outputs of one node capture into the same variable.** Both captures commit in one completion, in a fixed
  order, so this is not a race. It is still ambiguous authoring; it is refused as a duplicate capture target on that
  node, a separate finding from the concurrency one.
- **Concurrency several levels up.** Two captures in different nested containers are concurrent when their nearest
  common ancestor runs those containers concurrently, for example two Sequences on two Parallel branches.
- **A capture and a write in an outer and an inner graph activity.** A graph activity is a closed scope (spec 265),
  so an inner graph's captures can only target the inner graph's variables. They never collide with the caller's
  variables.
- **A Flowchart that joins and forks again.** Nodes before a join and nodes after it are not concurrent with each
  other. Nodes between a fork and its join are.
- **A threshold or first-wins join.** Branches still run concurrently until the join fires, so their captures still
  conflict.
- **Merge or Reduce intrinsics inside the parallel branches.** These are writes like Set; they do not make
  concurrent writes deterministic (existing behaviour, kept).
- **Unknown structure kind.** A structure whose handler is not installed cannot be analysed. The existing handling
  of unknown structures applies; this feature adds no new refusal for it.
- **Published executables that break the rule.** They keep running. The next publication of their definition is
  refused (D-4).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Validation MUST refuse two variable writes that can run concurrently and target the same variable
  (same declaring scope and reference key), where a write is an output capture or a Set, Merge or Reduce intrinsic.
- **FR-002**: There MUST be one finding per variable. It MUST name every node that writes that variable
  concurrently, the variable and its scope, and say how to fix it: capture into separate variables and combine them
  with one write after the join. Every violating variable MUST be reported in the same validation or publication
  attempt.
- **FR-003**: Writes that cannot run concurrently MUST NOT be refused: consecutive steps, a Flowchart path,
  If arms, Switch cases, loop iterations, Flowchart paths that leave different outcome ports of a node without a
  fork policy, and BPMN exclusive gateway branches.
- **FR-004**: The rule MUST apply to workflow draft validation, to graph activity draft validation, to graph
  activity compilation, and to workflow executable compilation, so a version that never went through draft
  validation is still refused.
- **FR-005**: For a graph activity, the rule MUST cover captures into the activity's own variables from its body
  nodes.
- **FR-006**: Each structure MUST be able to declare, for any two of its children, whether one always runs before
  the other, whether they can run concurrently, whether they are mutually exclusive, or that it does not say. A
  structure that declares nothing MUST be treated as running its children one after another.
- **FR-007**: Every built-in structure MUST declare that relation fully: Sequence, Flowchart (outcome ports and
  fork policies, as in D-2a), If, Switch, Parallel, the loops (For, ForEach, While, Do), BPMN (parallel,
  inclusive, exclusive and event-based gateways, implicit splits and multi-instance) and CompensationScope.
- **FR-008**: The concurrent-write rule MUST take its concurrency knowledge only from the structures' declarations,
  not from a list of structure kinds of its own.
- **FR-009**: Two output captures of one node into the same variable MUST be refused as a duplicate capture target.
- **FR-010**: Already published executables MUST keep running unchanged; nothing is re-validated retroactively.
- **FR-011**: Every existing test of the intrinsic concurrent-write rule, and spec 265's sequential-capture tests,
  MUST keep passing.
- **FR-012**: The new structure declaration MUST be recorded in the extension-point catalogs of the structure
  contract and of every structure that implements it.

### Key Entities

- **Variable write**: An output capture or a Set, Merge or Reduce intrinsic on a node, with its target variable
  (declaring scope and reference key).
- **Structure concurrency declaration**: What a structure states about its children: which slots run
  concurrently, which children within a slot can run at the same time, and which are mutually exclusive.
- **Concurrent-write finding**: The refusal, naming the two writing nodes, the variable and its scope, and the fix.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Every concurrent-capture shape listed in Stories 1, 3 and 4 is refused at draft validation and at
  publication: Parallel, Flowchart fork, capture versus Set, graph body Parallel and BPMN parallel gateway.
- **SC-002**: None of the sequential or exclusive shapes in Story 2 is refused, and spec 265's two sequential-capture
  tests still pass.
- **SC-003**: All existing intrinsic concurrent-write tests pass without change.
- **SC-004**: A structure added only in a test participates in the rule without any change to the validator.
- **SC-005**: ADR 0048's required evidence item "shared-ancestor parallel capture rejection" has a test that fails
  when the rule is removed.

## Assumptions

- Container-scoped capture targets remain refused at publication; ADR 0048's container captures are not built. When
  they are, they fall under the same rule through FR-001's declaring scope.
- "An explicit deterministic merge or reduction" keeps today's meaning: one write placed after the join. Captures
  gain no merge or reduction mode of their own.
- Intrinsics are not allowed in graph activity bodies yet (#3023). When they are, FR-001 covers them there as well.
- Studio shows the finding through its existing draft validation display; no new Studio control is needed.
- Concurrency here is logical: branches that the engine interleaves on one writer count as concurrent, because their
  completion order is not fixed by the definition.
