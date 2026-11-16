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
  (several outcomes → warning) is what applies. **Finding F-1** (amendment to spec 266): scenario 5 and FR-003's
  "paths that leave different outcome ports of a node without a fork policy" should read as decided by #4193.
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
- `Flowchart` after a `Break`, and after a first-wins race, marks the other paths cancelled in its own state but does
  not cancel the running activities. Their captures commit late.

Decision: a structure also declares whether it **completes only after every child run it started has ended**
(`CompletesAfterItsChildren`). A pair ordered by an ancestor (one child before the other) is still unordered when a
structure between the earlier write and that ancestor can complete early. This is what Story 1 scenario 8 and FR-003's
join sentence require across levels, for example a threshold Parallel inside a Sequence.

- Parallel declares `false` when a threshold is set below its branch count, otherwise `true`.
- Flowchart declares `false` when a node uses the `firstWins` policy, otherwise `true`. A `Break` cannot be seen at
  design time without catalog facts (any child may propagate it). **Finding F-2** (defect, not this spec): the Break
  documentation says the Flowchart "cancels any other in-flight paths", but the running activities are not cancelled
  and their captures commit after the Flowchart completed. The same holds for a threshold Parallel; whether that is
  intended is for the join initiative (#4193).
- BPMN and CompensationScope cancel their live children; they declare `true`. Sequence, If, Switch and the loops run
  one child at a time and declare `true`.

## R-4 Joins inside one structure

- Flowchart joins: under both join policy kinds (`deadPathJoin`, the default, and `reachabilityJoin`) a node with
  several inbound connections waits for every live inbound path. The `merge` policy does not wait
  (`FlowchartJoinCoordinator.ShouldWaitForTarget`): it runs once per arrival. So for two children X and Y where Y is
  reachable from X, X is ordered before Y unless a non-waiting join (a `merge` node with two or more inbound
  connections) on a path from X to Y is not dominated by X: another arrival can run it, and Y, while X is in flight.
  A merge node reached by unordered arrivals can itself run twice at once (R-5).
- BPMN joins: parallel and inclusive gateways wait. An exclusive gateway, an event-based gateway and any other element
  with several incoming flows pass each token on (BPMN uncontrolled flow); they are treated like `merge`.

## R-5 A child that runs more than once at the same time

Spec FR-008 asks BPMN to declare non-sequential multi-instance as unordered. Its instances are runs of **one** child,
so the declaration also answers for a child paired with itself: `Unordered` means two runs of it can be in flight
together. Then every write in its subtree is an unordered write, even a single node's. Sources:

- BPMN non-sequential multi-instance (`LoopCharacteristics.IsSequential == false`).
- A Flowchart or BPMN node reached from a non-waiting join whose inbound paths are not mutually exclusive.

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
  successor of a pseudo element in front of its host, so it is ordered after the host's predecessors and unordered
  with the host.
- Unordered: everything else, including parallel, inclusive and event-based gateway branches, implicit splits,
  event subprocesses and compensation handlers (they have no sequence flows).
- Children that are not bound to an element, or a graph that cannot be read, give `Unspecified`.

## R-7 Where the warning is produced

- Workflow drafts: `ValueFlowValidator` (unchanged path and type id; severity becomes `Warning`).
- Graph drafts and graph compilation: `GraphActivityProvider.ValidateAsync` and `CompileAsync` add an
  `ActivityDiagnostic` with severity `Warning`, code `activity.graph.unordered-variable-write`, and metadata
  `validationType = ValueFlow/ConcurrentWrite`. Compilation continues because only errors stop it.
- Workflow executable compilation adds nothing (D-3).
- One analysis serves all three: `UnorderedVariableWriteAnalysis` in `Elsa.Workflows.Design.Core`, which both callers
  already reference. Each caller supplies how a node writes variables (captures, and intrinsics for workflows).

## R-8 Alternatives considered (design choices of this spec)

- **Reuse `AssessChildResult`'s facts.** Rejected: they describe result availability, are left `null` by several
  handlers, and cannot express early release or self-overlap. Using them would keep the validator guessing.
- **Put the rule in the shared data selection analyzer.** Rejected: activity compilation drops its warnings (R-1),
  and the analyzer is about reads.
- **Detect Break in a Flowchart through catalog version ids.** Rejected: Break propagates through Sequence, If and
  Switch, so a static check would be partial and catalog-dependent. Recorded as F-2 instead.
