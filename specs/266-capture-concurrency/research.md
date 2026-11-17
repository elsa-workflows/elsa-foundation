# Research: Warn about writes to one variable whose order is not defined

Spec: [spec.md](spec.md). Branch base: `initiative/3052-elsa3-to-elsa4-conversion` at `2c84e802f`.

## R-1 What exists on the branch

- `ValueFlowValidator` (`Essentials/src/Workflows/Design/Validations/Elsa.Workflows.Design.Validations/Validators/ValueFlowValidator.cs`)
  raises `ValueFlow/ConcurrentWrite` as an **error** for Set, Merge and Reduce intrinsics (and the legacy
  `elsa.intrinsic.set@1` node). It ignores captures. It decides concurrency from two hard-coded structure kinds
  (`elsa.parallel.structure`, `elsa.flowchart.structure`) and, for Flowchart, from `AssessChildResult`'s
  `MayRunConcurrently`/`AreMutuallyExclusive`. Any other common container counts as ordered, BPMN included.
- `IActivityStructureHandler.AssessChildResult` already returns `ActivityChildResultFacts` with nullable
  `MayRunConcurrently` and `AreMutuallyExclusive`. Those facts serve result reads (availability of a producer's result
  at a consumer). Their meaning is "may a result be read", not "is the order of writes fixed", and the handlers fill
  them unevenly (Parallel, If, Switch and the loops leave both `null`).
- Graph activities: `GraphActivityProvider.ValidateAsync` (draft) and `CompileAsync` (compilation) produce
  `ActivityDiagnostic`s; neither runs a write-order check. `ActivityTemplateCompiler` drops warning findings of the
  shared data selection analyzer, so that analyzer is not a warning channel for compilation.
- ADR 0048 §4 and §5, its required-evidence item and the unordered-capture amendment were already amended by the spec
  PR (#3286). FR-013 needs no further ADR text in this delivery.

## R-2 Fit with the agreed ADRs

- **ADR 0092** (merged): decision 5 keeps D-8 (last commit wins) for every write and adds the run-time lost-update
  warning for a stale base. Decision 6 asks that spec 266's publish-time warning points to the `Fault` opt-in. The
  warning text names it (P-6). This plan touches no run-time path; read sets, the commit rule and
  `LogicalInvocationId` belong to S-10 (#4316).
- **ADR 0045 §6, amendment of 2026-11-16** (S-3, #4658): every activity may select several declared outcomes and no
  multi-outcome flag exists. D-2a of the spec makes paths that leave different ports exclusive "unless the node's
  activity contract allows several outcomes per completion". With no such contract, that condition holds for every
  node, so **paths that leave different ports of any Flowchart node are unordered** (they are what an inclusive fork
  is). This keeps today's port-blind behaviour. The final treatment of such nodes belongs to the join initiative
  (#4193). Consequence for the spec: Story 2 scenario 5's "no warning" case cannot occur today; its parenthetical
  (several outcomes → warning) is what applies. **Finding F-1**: amended in spec 266 by
  [#4720](https://github.com/elsa-workflows/elsa-foundation/pull/4720) (D-2a, FR-003, Story 2 scenario 5); narrowing is
  for #4193.
- Exclusivity inside a Flowchart remains for branches that start at different trigger entries, as today.

## R-3 Early release: a structure that completes while its children still run

A research pass over the runtime (`WorkflowParentActivityCompletionSchedulerWorkHandler.cs`,
`StructuralParentEvaluationSupport.cs`, `WorkflowInvokeActivitySchedulerWorkHandler.cs`) found:

- The runtime never cancels the live children of a completing parent by itself. Only a parent that asks
  (`RequestChildSubtreeCancellation`) cancels them: GraphActivity, BpmnProcess and CompensationScope do.
- A child's own completion, captures included, commits regardless of its parent's status. Only the parent's reaction
  is skipped.
- `Parallel` with a threshold below its branch count completes when the threshold is met and leaves the other
  branches running. Their captures commit after the Parallel's successor started.
- `Flowchart` after a `Break`, and after a first-wins race, marked the other paths cancelled in its own state but did
  not cancel the running activities. Their captures committed late. For `Break` this contradicted its documentation
  ("cancels any other in-flight paths") and is fixed in this delivery (F-2 below).

Decision: a structure also declares whether it **completes only after every child run it started has ended**
(`CompletesAfterItsChildren`). A pair ordered by an ancestor (one child before the other) is still unordered when a
structure between the earlier write and that ancestor can complete early. This is what Story 1 scenario 8 and FR-003's
join sentence require across levels, for example a threshold Parallel inside a Sequence.

- Parallel declares `false` when a threshold is set below its branch count, otherwise `true`.
- Flowchart declares `false` when a node uses the `firstWins` policy, otherwise `true`.
- **Finding F-2.** A `Break` cannot be seen at design time (any child may propagate it), so it is fixed at run time
  instead (decided by the coordinator, 2026-11-17): a Flowchart that ends on `Break` stages
  `RequestLiveChildrenSubtreeCancellation`, a new runtime request that cancels every still-live child in the same
  commit, resolved by the cancellation planner over the states it already loads, so it costs nothing unless a
  `Break` fires. A child whose schedule was still queued has no state yet, so the schedule handler also acknowledges a
  schedule whose parent has completed or been cancelled without starting the child (found in the local review). The
  first-wins race and the threshold Parallel are join behaviour and stay with the join initiative
  (#4193, comment 6943318941); when that initiative changes them, the two declarations above change with them.
- BPMN and CompensationScope cancel their live children; they declare `true`. Sequence, If, Switch and the loops run
  one child at a time and declare `true`.

## R-4 Joins inside one structure

- Flowchart joins: under both join policy kinds (`deadPathJoin`, the default, and `reachabilityJoin`) a node with
  several inbound connections waits for every live inbound path. The `merge` policy does not wait
  (`FlowchartJoinCoordinator.ShouldWaitForTarget`): it runs once per arrival. So for two children X and Y where Y is
  reachable from X, X is ordered before Y unless a non-waiting join (a `merge` node with two or more inbound
  connections) on a path from X to Y is not dominated by X and has another inbound source that neither follows X nor
  excludes it: that arrival can run the join, and Y, while X is in flight. An exclusive merge after an exclusive split
  therefore keeps its continuation ordered. A join downstream of a `firstWins` race does not order the race's
  branches either: the losing path is cancelled only in the Flowchart's state, its activity keeps running, and the
  join is released without it.
  A merge node reached by unordered arrivals from distinct sources can itself run twice at once (R-5); two connections
  from one source are one arrival, because a completion schedules each target once (spec 1208 FR-022).
- BPMN joins: parallel and inclusive gateways wait. An exclusive gateway, an event-based gateway and any other element
  with several incoming flows pass each token on (BPMN uncontrolled flow); they are treated like `merge`.

## R-5 A child that runs more than once at the same time

Spec FR-008 asks BPMN to declare non-sequential multi-instance as unordered. Its instances are runs of **one** child,
so the declaration also answers for a child paired with itself: `Unordered` means two runs of it can be in flight
together. Then every write in its subtree is an unordered write, even a single node's. Sources:

- BPMN non-sequential multi-instance (`LoopCharacteristics.IsSequential == false`).
- A Flowchart or BPMN node reached from a non-waiting join whose arrivals can overlap. Each arrival runs the join, so
  two arrivals are in flight together unless they exclude each other or exactly one of them is reached from the join
  within one pass (the way back of a loop, which comes only after the join's run). In a Flowchart one source counts
  once (spec 1208 FR-022); in BPMN every incoming flow is its own token, judged with the choice it leaves an exclusive
  split on (a split's "skip" flow excludes the step it skips).
- A node on a branch that a fork on a cycle sends off the cycle (Flowchart: any node with several distinct
  successors, whatever their ports, D-2a as amended; BPMN: any split that is not exclusive). Each pass starts another
  run of the branch, which also overlaps the next pass of the cycle. This includes a Flowchart loop that repeats
  through one port and leaves through another; whether that may be narrowed is for the join initiative (#4193).

For the self-pair, `Unspecified` is treated as "never overlaps": treating every child of an undeclared structure as
self-overlapping would warn about every single write in it. D-2b's unordered default applies to pairs of different
children, which is what the spec states.

## R-6 BPMN relation

The BPMN handler analyses the element graph (sequence flows) with a virtual root over its start events:

- Ordered: Y reachable from X, with the R-4 join rule.
- Mutually exclusive: neither reaches the other, and some exclusive split dominates both while every path from it to
  X starts on a different choice than every path to Y. Exclusive splits are exclusive gateways (each outgoing flow a
  choice), different start events, and a host with interrupting boundary events (the normal flow and each
  interrupting boundary are choices; a non-interrupting boundary is not a choice). A boundary event is modelled as a
  successor of a pseudo element in front of its host, so it is ordered after the host's predecessors. With an
  interrupting boundary the host and the boundary path are exclusive: the process cancels the host before it can
  commit (R-3). With a non-interrupting boundary they are unordered.
- Unordered: everything else, including parallel, inclusive and event-based gateway branches, implicit splits,
  event subprocesses and compensation handlers (they have no sequence flows).
- Children that are not bound to an element, or a graph that cannot be read, give `Unspecified`.

## R-7 Where the warning is produced

- Workflow drafts: `ValueFlowValidator` (unchanged path and type id; severity becomes `Warning`).
- Graph drafts and graph compilation: `GraphActivityProvider.ValidateAsync` and `CompileAsync` add an
  `ActivityDiagnostic` with severity `Warning`, code `activity.graph.unordered-variable-write`, and metadata
  `validationType = ValueFlow/ConcurrentWrite`. Compilation continues because only errors stop it.
- Workflow executable compilation adds nothing (D-3).
- One analysis serves all three: the `IUnorderedVariableWriteAnalyzer` contract in `Elsa.Workflows.Design.Validations.Core`
  (which Graph Design already references), implemented internally in `Elsa.Workflows.Design.Validations`. Each caller
  says whether intrinsics count as writes (workflows) or only captures (graph bodies).

## R-8 Alternatives considered (design choices of this spec)

- **Reuse `AssessChildResult`'s facts.** Rejected: they describe result availability, are left `null` by several
  handlers, and cannot express early release or self-overlap. Using them would keep the validator guessing.
- **Put the rule in the shared data selection analyzer.** Rejected: activity compilation drops its warnings (R-1),
  and the analyzer is about reads.
- **Detect Break in a Flowchart through catalog version ids.** Rejected: Break propagates through Sequence, If and
  Switch, so a static check would be partial and catalog-dependent. Fixed at run time instead (F-2).
- **Cancel Break's live children through the `IRuntimeLiveChildActivityConsumer` marker.** Rejected: the marker
  lists every child of the Flowchart on each child completion, which grows with every pass of a long-running
  Flowchart, to serve a rare `Break`.
