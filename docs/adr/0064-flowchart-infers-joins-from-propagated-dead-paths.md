# Flowchart Infers Joins From Propagated Dead Paths

Status: proposed (2026-08-10)

Flowchart will decide implicit joins from **arrivals it already holds** rather than from a graph search over live
work. An untaken outbound connection emits a **dead arrival** instead of nothing; dead arrivals propagate forward
through nodes whose every inbound is dead; a join fires when every inbound connection has an arrival of either
kind, and schedules its target only when at least one of them is live. Reachability stays in the model as an
authoring-time validation over the **forward (acyclic) projection** of the graph, and leaves the execution path.
Flowchart and `BpmnProcess` remain separate activities with separate engines; what they share is a conformance
corpus, not code.

## The problem this fixes

Flowchart's defining feature is that the author does not declare gateways. A node with several inbound
connections joins, and the engine works out whether that join must wait. That inference is the whole of
Flowchart's difficulty, and today it is answered by a graph search:
`FlowchartJoinCoordinator.ShouldWaitForImplicitJoin` waits when any live `ActiveChild` in the same execution scope
can reach an un-arrived inbound source, using `FlowchartGraph.CanReach`.

Three problems follow.

**The reachability oracle is cyclic, and scope partitioning is silently compensating for it.** `CanReach` walks
every outbound connection, including backward edges. In a graph with a loop, every node in the cycle reaches every
other node in the cycle, so "can a live token still reach this inbound source" is nearly always true inside a loop.
What prevents a deadlock today is only that `ActiveChildren` is filtered to the same `ExecutionScopeId`. Two
mechanisms that ought to be orthogonal — join accounting and scope nesting — are therefore entangled, and a change
to the scope model moves join behaviour in ways that are hard to predict. Elsa 3 used forward-only inbound
connections for exactly this reason ([elsa-core ADR 0007](https://github.com/elsa-workflows/elsa-core/blob/release/3.8.0/doc/adr/0007-adoption-of-explicit-merge-modes-for-flowchart-joins.md)).

**Deadness is derived rather than represented.** When a decision takes outcome `A` and not `B`, nothing records
that `B` is dead. A downstream join learns it indirectly, by proving that no live token can reach it. That is the
non-local inclusive-join problem the BPMN literature needed a formal semantics to pin down
([Christiansen, Carbone and Hildebrandt, WSFM 2010](https://davidchristiansen.dk/pubs/wsfm2010.pdf)), imported
into a model that never had to have it.

**"Why is this join waiting?" has no local answer.** The honest explanation today is "a breadth-first search found
a live token that might still reach inbound 3." For a feature whose reason to exist is being easier to reason
about than BPMN, that is the wrong shape of answer.

## Decision

**Deadness becomes a token state.** `FlowchartArrivalStatus` gains `Dead` alongside `Arrived` and `Consumed`.

1. **Decisions emit both.** A node completing with outcomes emits live arrivals on matching outbound connections
   and **dead** arrivals on its non-matching ones, instead of emitting nothing on the untaken edges.
2. **Joins are local.** A target node fires when every inbound connection has an arrival for its
   `(node, scope, iteration)` key, live or dead. It schedules the target when at least one arrival is live.
3. **Dead paths propagate.** When every arrival at a node is dead, the node is not scheduled: its arrivals are
   consumed and dead arrivals are emitted on its outbound connections, carrying deadness forward to the next join.
4. **Scopes absorb.** Dead arrivals are absorbed at loop-iteration and scope boundaries, so a dead path from
   iteration *n* can never satisfy a join in iteration *n+1*.
5. **Reachability moves off the hot path.** The forward projection of the graph (edges minus those
   `FlowchartReachabilityAnalyzer.IsBackwardEdge` classifies as backward) remains available for authoring-time
   validation and diagnostics. The join decision no longer consults it.
6. **Iteration identity has one home.** The iteration key is derived from the nearest enclosing `LoopIteration`
   ancestor in the scope tree rather than copied onto every `ExecutionPath`, so the scope tree and the key cannot
   disagree.

This is dead-path elimination, which WS-BPEL specified and several engines implement. What is new here is applying
it to an **implicit-join** graph model, where it pays off far more than in a declared-gateway one: with declared
gateways the engine is told what to wait for, so locality buys tidiness; with inferred joins, locality is what
makes the inference decidable at all.

## What this deliberately does not add

Flowchart stays a simplified model, and the list of things it will not grow is part of the decision: boundary
events, event subprocesses, compensation, transactions, escalation, and message or signal correlation. Those are
`BpmnProcess`'s reason to exist. Collection iteration stays `ForEach`; Flowchart loops are graph cycles, not
multi-instance constructs. A Flowchart that acquires these is BPMN with worse documentation.

## Relationship to BpmnProcess

The two remain separate activities with separate engines and separate authoring surfaces. That separation is
reaffirmed, not merely tolerated: a Flowchart node is simultaneously a task and a gateway, its joins are implicit,
and it carries an execution-scope model tied to Elsa's per-iteration variable scoping
([ADR 0027](0027-scoped-variable-references-include-declaring-scope.md),
[ADR 0028](0028-loop-body-runs-in-a-per-iteration-variable-scope.md)). Expressing that over a BPMN element model
would require synthesising hidden gateway elements and would break the one-to-one correspondence between authored
node ids and runtime element ids that inspection, alterations ([ADR 0049](0049-runtime-alterations-use-snapshotted-atomic-jobs.md))
and provenance depend on.

**Shared conformance corpus, not shared code.** An earlier recommendation was to extract the activation-aware join
predicate so both engines used one implementation. That is withdrawn.
[ADR 0063](0063-bpmn-moves-to-a-host-agnostic-library.md) moves BPMN to its own repository, which would make any
shared abstraction a third cross-repository package; and this ADR removes the duplicated algorithm anyway by
replacing Flowchart's reachability search with local arrival accounting. What remains worth sharing is a corpus of
routing scenarios — diamond with a dead branch, loop with a join, nested loop, race, break inside a fork — run
against both engines, asserting identical answers where the two semantics agree and recording deliberate
divergence where they do not. That survives the repository split, which shared code would not.

## Consequences

Join decisions become local, so they are decidable inside loops without special-casing, cost no graph walk per
arrival, and stop depending on the scope model for their correctness. "Why is this join waiting" becomes a lookup
against one node's arrival set, which the existing `FlowchartDiagnosticEvent` accumulator can surface directly.
The whole class of user error where an author declares an AND-join and needed an OR-join stays impossible, because
authors still declare nothing.

The costs. Dead arrivals are state, so `FlowchartExecutionState` grows and the `#382` pruning rules must learn
which dead arrivals are safe to drop. Absorption at loop and scope boundaries is the subtle part, and getting it
wrong produces a join that fires an iteration early — the failure mode this ADR must be tested hardest against. A
node with one live and one dead inbound now schedules where today it might wait, which is the intended semantics
but is a behaviour change for existing graphs, so the corpus must pin the difference before the default flips.

## Alternatives considered

*Keep the reachability search and make it acyclic.* Computing `CanReach` over the forward projection alone would
fix the entanglement described above and is a much smaller change. It was rejected as the primary decision because
it leaves the join non-local: the answer still depends on a search over live work elsewhere in the graph, so the
diagnostic story does not improve and the cost per arrival stays. It is retained as the **fallback** if
dead-path propagation proves too invasive, and as a strictly-better interim state.

*Declare join modes on the node, as Elsa 3 did.* Elsa 3's `MergeMode` enum is the tested-and-found-wanting
version of this. Its own ADR records that the modes were reverse-engineered from bugs, and it made the author
responsible for a decision the engine can compute. Adopting explicit modes would also surrender Flowchart's main
advantage over BPMN authoring.

*Adopt real BPMN gateways in Flowchart.* This is the option Elsa 3's ADR 0007 called "overkill", and the judgement
holds for a different reason than it gave: not that gateways are too complex to implement, but that declaring them
is the thing Flowchart exists to avoid.

*Share one token core between Flowchart and BpmnProcess.* Rejected above and superseded by the conformance
corpus.

## Follow-up

Sequenced so the risky part lands behind a proven equivalence rather than in front of it.

- **WU-1 — forward-projection reachability.** Move `CanReach` (and the join predicate that uses it) onto the
  forward projection, and keep the cyclic walk only where a cyclic answer is intended. Independent, small, and
  strictly an improvement even if the rest is never built.
- **WU-2 — conformance corpus.** The routing scenarios above as an executable suite over Flowchart, with the
  current engine as the baseline. This is the instrument the rest is measured with, so it comes before the change
  it measures.
- **WU-3 — dead-path propagation behind a policy kind.** `FlowchartArrivalStatus.Dead`, dead emission on untaken
  outbounds, local join firing, forward propagation, and scope/loop absorption — registered as new
  `IFlowchartPolicy` kinds so the existing registry carries the A/B rather than a fork of the engine. Default
  stays on the current predicate.
- **WU-4 — iteration key derived from the scope tree**, removing the copy on `ExecutionPath`.
- **WU-5 — flip the default** once WU-2 shows the two agree everywhere they should and the deliberate divergences
  are recorded, then retire the reachability path from join decisions.

**Cross-references:** [ADR 0063](0063-bpmn-moves-to-a-host-agnostic-library.md) (why sharing code with BPMN is
withdrawn); [ADR 0027](0027-scoped-variable-references-include-declaring-scope.md) and
[ADR 0028](0028-loop-body-runs-in-a-per-iteration-variable-scope.md) (the scope model iteration keys hang off);
[execution model comparison](../reports/execution-model-comparison-2026-08.md) §5.4 (the Flowchart-versus-BPMN
analysis this decision comes out of, including the withdrawn extraction recommendation).
