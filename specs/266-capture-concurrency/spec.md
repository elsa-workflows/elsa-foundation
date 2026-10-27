# Feature Specification: Warn about writes to one variable whose order is not defined

**Feature Branch**: `agent/claude/3198-capture-concurrency`

**Created**: 2026-10-22

**Status**: Draft

**Issue**: [#3198](https://github.com/elsa-workflows/elsa-foundation/issues/3198), part of epic
[#3052](https://github.com/elsa-workflows/elsa-foundation/issues/3052)

**Input**: ADR 0048 §5 asked publication to refuse output captures that can run concurrently into one shared variable,
unless an explicit deterministic merge or reduction governs them. That rule was never built for captures: today the
last committed capture wins, silently, for workflow variables and, since spec 265, for graph activity variables.
Review by Frans van Ek on 2026-10-23 changed the direction, and the owner agreed. Elsa provides guardrails and
diagnostics; it does not restrict what the engineer builds. This spec therefore builds a **warning**, documents
"last commit wins" as defined behaviour, and amends ADR 0048 §4 and §5 to match.

**Depends on**:
- ADR 0048 (accepted, amended by this spec): container output captures.
- Spec 265 (merged, #3199): graph activity bodies capture into the activity's own variables. Its tests pin that
  sequential captures into one graph variable are accepted and the last value wins
  (`Compilation_accepts_two_body_nodes_capturing_into_one_graph_variable`,
  `Sequential_captures_into_one_graph_variable_leave_the_last_value`). They must keep passing.

**What exists today** (research, 2026-10-22 and 2026-10-23):
- Workflow draft validation already raises `ValueFlow/ConcurrentWrite` as an **error** for concurrent intrinsic writes
  (Set, Merge, Reduce) into one variable, so promotion refuses them. It does not see output captures. It decides
  concurrency from a fixed list of structure kinds it knows (Parallel, Flowchart) and does not recognise BPMN.
- Graph activity drafts and graph activity compilation do not run that check.
- Today's Flowchart branch of the rule ignores outcome ports. Paths that leave different ports of a decision node
  count as concurrent, so writes on the two outcomes of an If inside a Flowchart are refused today.
- Commits are totally ordered per workflow instance: the execution actor admits one command at a time, one drain
  applies scheduler work items one by one, and checkpoint commits are fenced by an execution token. Two branches
  never write at the same moment; "concurrent" here means the definition does not fix their order. The last commit
  wins, and that is well defined.
- Merge and Reduce intrinsics overwrite exactly like Set today. There is no combine step and no merge strategy, so two
  concurrent Merges lose one contribution.
- Validation warnings are an established mechanism (`ValidationSeverities.Warning`, already emitted by the
  Compensation and Connection validators). Promotion and promotion preflight do not block on warnings.
- Container-scoped capture targets are refused at publication today. Only workflow-root and graph-variable captures
  exist.
- Unordered branches come from Parallel branches, Flowchart forks (several connections from one port,
  `parallelFork` and `inclusiveFork` policies), and BPMN parallel and inclusive gateways, event-based races, implicit
  splits and non-sequential multi-instance. Loops run sequentially; Elsa 4 has no parallel ForEach. If and Switch run
  exactly one branch. Background placement only applies to leaves and adds no structural branching.

## Decisions

Confirmed by the owner on 2026-10-23, after Frans van Ek's review.

| # | Decision | Why |
|---|----------|-----|
| D-1 | **A warning, not a refusal, for every declared variable write, with all writers reported at once.** Captures and intrinsic writes (Set, Merge, Reduce) are checked together by the existing `ValueFlow/ConcurrentWrite` rule, which becomes a warning, including for the intrinsic writes it refuses today. There is one finding per variable, keeping today's path and type id. It names every node that writes that variable without a defined order, says that the last commit wins, and says how to make the result deterministic: order the branches, capture into separate variables, or combine them with one write after the join. | Elsa gives guardrails and diagnostics and does not restrict what the engineer builds. Static analysis sees only declared writes (not script `setVariable`, C# activities, or writes decided at run time), so a refusal would promise more than the engine can keep. Reporting every writer avoids a fix-one, republish, find-the-next loop. The type id stays because Studio and tests match on it. |
| D-2 | **Each structure declares how its children relate in time.** For any two of its children, a structure states whether one always runs before the other, whether their order is not defined (they can be in flight together), whether they are mutually exclusive, or that it does not say. Every built-in structure declares this fully, BPMN included. In this spec only the warning uses the declaration; the ordering rules move to it later (#3282). | Structures are pluggable, and BPMN is missed today because the validator does not know it. The structure's author is the only one who knows its semantics. Describing the whole time relation now costs little and avoids a second declaration for ordering later. |
| D-2a | **Flowchart order follows its ports.** Paths that leave one node through the same outcome port are unordered (an implicit fork). All outbound paths of a node with the `parallelFork` or `inclusiveFork` policy are unordered. Paths that leave different ports of any other node are mutually exclusive, unless the node's activity contract allows several outcomes per completion (decision D-4 on epic #3052, PR #3389); then they are unordered, as under `inclusiveFork`. Nodes on one path are ordered, and branches that start at different trigger entries stay exclusive as today. | It matches what the engine does: it schedules the outbound connections whose port matches the completed outcome, or all of them under `parallelFork`. No built-in activity declares multi-outcome completion yet, so this changes no warning today. D-4 makes multi-outcome completion an explicit part of the activity contract, which is what lets the analysis tell the two cases apart. |
| D-2b | **A structure that declares nothing is treated as unordered.** The warning then says that the structure does not declare how its children are ordered, so a module author knows the fix is to declare it. | With a warning, erring towards reporting costs little. A silent "sequential" default would hide real races in third-party structures. |
| D-3 | **Reported where the author is; publication never blocks on it.** The warning appears in workflow draft validation, in graph activity draft validation and in graph activity compilation diagnostics. Workflow executable compilation adds no check, because it has no warning channel and nothing to block. | A warning belongs where the author can act on it. The publication gates stay unchanged. |
| D-4 | **Merge and Reduce are warned about like Set.** They are not exempt. | They overwrite today. Exempting them by name would hide a lost contribution. When real merge strategies exist, a strategy that declares itself order-independent can be exempted by that work. |
| D-5 | **"Last commit wins" is documented behaviour, and ADR 0048 is amended.** §5 turns the concurrent-capture refusal into this warning and states last-commit-wins semantics. §4 is corrected: commits are serialized per instance by the execution actor, the single drain and the fencing token; the frame revision is a version counter, not a concurrency defense. The required-evidence item "shared-ancestor parallel capture rejection" becomes "shared-ancestor parallel capture warning". | The rule this spec builds changes what §5 says, so the amendment belongs to this unit. §5's semantics lean on the ordering guarantee in §4. |

### Follow-ups

All are sub-issues of epic #3052 and out of scope here.

- [#3282](https://github.com/elsa-workflows/elsa-foundation/issues/3282): the value-flow ordering rules and the expression picker take ordering from the structures' declarations (D-2) instead of hard-coded structure kinds.
- [#3283](https://github.com/elsa-workflows/elsa-foundation/issues/3283): reconciled workflow versions pass the full draft validation gate.
- [#3284](https://github.com/elsa-workflows/elsa-foundation/issues/3284): workflow executable compilation reports every error in one refusal.
- [#3285](https://github.com/elsa-workflows/elsa-foundation/issues/3285): activities select their outcomes explicitly, several per completion (D-4). This spec no longer depends on it; D-2a already treats a multi-outcome node as unordered.
- [#3290](https://github.com/elsa-workflows/elsa-foundation/issues/3290): inspection shows each variable's write history and the winning write.
- [#3291](https://github.com/elsa-workflows/elsa-foundation/issues/3291): inspection shows a commit-ordered journal and compares two instances.
- [#3292](https://github.com/elsa-workflows/elsa-foundation/issues/3292): the runtime traces a write that overwrites another open branch's write.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Unordered writes in a workflow are reported (Priority: P1)

A workflow author puts two activities on branches whose order the workflow does not define, for example the two
branches of a Parallel or two targets of a Flowchart fork, and captures an output of each into the same workflow
variable. Today nothing tells them that the variable's final value depends on which branch commits last. After this
feature, validating the draft warns about it, names every writer, and explains how to make the result deterministic.
Publication still succeeds.

**Why this priority**: This is the risk ADR 0048 §5 exists to surface, and workflows are where most captures are
authored.

**Independent Test**: Validate a workflow with a Parallel whose two branches capture into one variable; a warning
names both nodes and the variable, and the workflow still publishes. Replace one capture with a separate variable
and a single Set after the join; the warning disappears.

**Acceptance Scenarios**:

1. **Given** a Parallel whose two branches each capture an output into variable `total`, **When** the draft is
   validated, **Then** a `ValueFlow/ConcurrentWrite` warning names both capturing nodes and `total`, says that the
   last commit wins, and suggests ordering the branches, separate variables, or one write after the join.
2. **Given** the same workflow, **When** it is promoted and published, **Then** publication succeeds.
3. **Given** a Flowchart where one node forks through one port to two nodes that each capture into `total`, **When**
   the draft is validated, **Then** the warning is reported.
4. **Given** a branch that captures into `total` while an unordered branch runs a Set, Merge or Reduce intrinsic on
   `total`, **When** the draft is validated, **Then** the warning is reported.
5. **Given** three Parallel branches that each write `total`, **When** the draft is validated, **Then** one warning
   names all three writers.
6. **Given** two Parallel branches that Set `counter` (today a promotion-blocking error), **When** the draft is
   validated and promoted, **Then** the finding is a warning and promotion succeeds.
7. **Given** a Sequence where two nodes capture into `total` one after another, **When** the draft is validated,
   **Then** no warning is reported, and at run time the later capture's value remains.

---

### User Story 2 - Exclusive and sequential paths stay quiet (Priority: P1)

An author writes one variable from both arms of an If, from several cases of a Switch, from consecutive nodes of a
Sequence or a Flowchart path, or from each iteration of a loop. Their order is defined or only one of them runs, so
no warning appears.

**Why this priority**: Warnings on ordinary sequential and branching workflows would train authors to ignore the
warning. This shares P1 with Story 1.

**Independent Test**: Validate workflows with captures into one variable from If arms, Switch cases, Sequence steps,
a sequential Flowchart path, Flowchart decision outcomes and a loop body; none reports the warning.

**Acceptance Scenarios**:

1. **Given** an If whose Then and Else each capture into `result`, **When** the workflow is validated, **Then** no
   warning is reported.
2. **Given** a Switch whose cases each capture into `result`, **When** it is validated, **Then** no warning is
   reported.
3. **Given** a loop whose body captures into `result`, **When** it is validated, **Then** no warning is reported.
4. **Given** a Flowchart path `A → B` where A and B both capture into `result`, **When** it is validated, **Then**
   no warning is reported.
5. **Given** a Flowchart node whose activity selects one outcome per completion, and whose two outcome ports lead to
   two nodes that each capture into `result`, **When** it is validated, **Then** no warning is reported. (If the
   activity's contract allows several outcomes per completion, the two paths are unordered and the warning is
   reported, per D-2a.)

---

### User Story 3 - Unordered writes in a graph activity body are reported (Priority: P2)

An activity author builds a graph activity whose body has a Parallel or a Flowchart fork, and two unordered body
nodes capture into one of the activity's own variables. Validating the activity draft and compiling it report the
warning, and the activity still publishes.

**Why this priority**: Spec 265 made graph-variable captures possible. Graph activities have no write-order check of
any kind today.

**Independent Test**: Validate and publish a graph activity whose body Parallel has two branches capturing into one
graph variable; both report the warning and publication succeeds. The spec 265 sequential-capture tests still pass.

**Acceptance Scenarios**:

1. **Given** a graph activity body with a Parallel whose branches capture into graph variable `total`, **When** the
   activity draft is validated, **Then** a warning names both body nodes and `total`.
2. **Given** the same activity, **When** it is compiled for publication, **Then** the compilation diagnostics carry
   the same warning and compilation succeeds.
3. **Given** a graph activity body where two nodes in a Sequence capture into `total`, **When** it is validated,
   compiled and run, **Then** no warning is reported and the later value remains.

---

### User Story 4 - Every structure declares how its children relate in time, BPMN included (Priority: P2)

A module author ships a structure activity. The structure states, for any two of its children, whether one runs
before the other, whether their order is undefined, or whether they are mutually exclusive, so the warning works for
it without the validator knowing it. The built-in BPMN structure declares its parallel and inclusive gateways,
event-based races, implicit splits and non-sequential multi-instance as unordered, and its exclusive gateways as
exclusive.

**Why this priority**: Without it, BPMN processes are never checked, and every new structure would need a change to
the validator.

**Independent Test**: A BPMN process whose parallel gateway leads to two tasks capturing into one variable reports
the warning. A test structure registered only in the test declares two unordered children, and captures into one
variable from them report the warning without any change to the validator.

**Acceptance Scenarios**:

1. **Given** a BPMN process whose parallel gateway splits to two tasks that each capture or Set `total`, **When** it
   is validated, **Then** the warning is reported.
2. **Given** a BPMN exclusive gateway whose two outgoing tasks capture into `total`, **When** it is validated,
   **Then** no warning is reported.
3. **Given** a structure that declares nothing about its children, **When** two of its children capture into one
   variable, **Then** the warning is reported and says the structure does not declare how its children are ordered.
4. **Given** the existing intrinsic concurrent-write tests, **When** the structure knowledge moves out of the
   validator, **Then** they pass, with only the expected severity changed from error to warning.

---

### Edge Cases

- **Two outputs of one node capture into the same variable.** Both captures commit in one completion, in a fixed
  order, so the order is defined. It is still ambiguous authoring; it is reported as a duplicate capture target
  warning on that node, a separate finding.
- **Branching several levels up.** Two captures in different nested containers are unordered when their nearest
  common ancestor runs those containers unordered, for example two Sequences on two Parallel branches.
- **A capture and a write in an outer and an inner graph activity.** A graph activity is a closed scope (spec 265),
  so an inner graph's captures can only target the inner graph's variables. They never collide with the caller's
  variables.
- **A Flowchart that joins and forks again.** Nodes before a join and nodes after it are ordered. Nodes between a
  fork and its join are not.
- **A threshold or first-wins join.** Branches stay in flight until the join fires, so their writes are still
  unordered.
- **Writes the analysis cannot see.** Script `setVariable`, C# activities writing through the execution context,
  and writes inside sub-workflows or graph activities are not declared writes. They produce no warning; #3292 covers
  a runtime trace for them.
- **Unknown structure kind.** A structure whose handler is not installed cannot be analysed. The existing handling of
  unknown structures applies; this feature adds no new finding for it.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Validation MUST warn about two or more variable writes that target the same variable (same declaring
  scope and reference key) and whose order the definition does not fix, where a write is an output capture or a Set,
  Merge or Reduce intrinsic. The finding MUST have warning severity and MUST NOT block promotion, publication or
  compilation.
- **FR-002**: There MUST be one finding per variable, keeping the `ValueFlow/ConcurrentWrite` type and today's path.
  It MUST name every node that writes that variable without a defined order, the variable and its scope, state that
  the last commit wins, and say how to make the result deterministic. Every affected variable MUST be reported in the
  same validation or compilation pass.
- **FR-003**: Writes whose order is defined, or of which only one can run, MUST NOT be reported: consecutive steps, a
  Flowchart path, If arms, Switch cases, loop iterations, Flowchart paths that leave different outcome ports of a
  node without a fork policy, and BPMN exclusive gateway branches. A node or structure that may select several
  outcomes in one completion (D-4) is not exclusive: its selected paths are unordered.
- **FR-004**: The warning MUST appear in workflow draft validation, in graph activity draft validation and in graph
  activity compilation diagnostics.
- **FR-005**: For a graph activity, the rule MUST cover captures into the activity's own variables from its body
  nodes.
- **FR-006**: Each structure MUST be able to declare, for any two of its children, whether one always runs before
  the other, whether their order is not defined, whether they are mutually exclusive, or that it does not say.
- **FR-007**: A structure that declares nothing MUST be treated as unordered for this rule, and the finding MUST say
  that the structure does not declare how its children are ordered.
- **FR-008**: Every built-in structure MUST declare that relation fully: Sequence, Flowchart (outcome ports and fork
  policies, as in D-2a), If, Switch, Parallel, the loops (For, ForEach, While, Do), BPMN (parallel, inclusive,
  exclusive and event-based gateways, implicit splits and multi-instance) and CompensationScope.
- **FR-009**: The rule MUST take its ordering knowledge only from the structures' declarations, not from a list of
  structure kinds of its own.
- **FR-010**: Two output captures of one node into the same variable MUST be reported as a duplicate capture target
  warning.
- **FR-011**: Every existing test of the intrinsic concurrent-write rule MUST keep passing, with only the expected
  severity changed to warning, and spec 265's sequential-capture tests MUST keep passing unchanged.
- **FR-012**: The new structure declaration MUST be recorded in the extension-point catalogs of the structure
  contract and of every structure that implements it.
- **FR-013**: ADR 0048 MUST be amended: §5 replaces the concurrent-capture refusal with this warning and states that
  the last commit wins; §4 states that commits are serialized per instance and that the frame revision is a version
  counter; the required-evidence item becomes "shared-ancestor parallel capture warning".
- **FR-014**: The runtime semantics MUST be documented where authors look for value-flow behaviour: writes whose
  order the definition does not fix resolve by commit order, and the last commit wins.

### Key Entities

- **Variable write**: An output capture or a Set, Merge or Reduce intrinsic on a node, with its target variable
  (declaring scope and reference key).
- **Child time relation**: What a structure states about two of its children: one runs before the other, their
  order is not defined, they are mutually exclusive, or it does not say.
- **Unordered-write finding**: The warning, naming every writing node, the variable and its scope, the
  last-commit-wins consequence and the fix.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Every unordered-write shape in Stories 1, 3 and 4 reports the warning at draft validation (and at graph
  compilation for Story 3): Parallel, Flowchart fork, capture versus Set, three writers, graph body Parallel, BPMN
  parallel gateway and an undeclared structure. None of them blocks publication.
- **SC-002**: None of the sequential or exclusive shapes in Story 2 reports the warning, and spec 265's two
  sequential-capture tests still pass.
- **SC-003**: All existing intrinsic concurrent-write tests pass, with severity warning, and a workflow they describe
  now promotes.
- **SC-004**: A structure added only in a test participates in the rule without any change to the validator.
- **SC-005**: ADR 0048's amended required-evidence item, "shared-ancestor parallel capture warning", has a test that
  fails when the rule is removed.

## Assumptions

- Container-scoped capture targets remain refused at publication; ADR 0048's container captures are not built. When
  they are, they fall under the same rule through FR-001's declaring scope.
- Captures gain no merge or reduction mode of their own. "Combine them with one write after the join" keeps today's
  meaning.
- Intrinsics are not allowed in graph activity bodies yet (#3023). When they are, FR-001 covers them there as well.
- Studio shows the warning through its existing draft validation display; no new Studio control is needed.
- Making the actual run order visible in inspection is out of scope (#3290, #3291).
