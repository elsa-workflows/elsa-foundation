# 122 — BPMN cyclic sequence flows (token iteration keys) (BPMN Phase 2, events tier — CLOSING unit)

**Status**: Implemented
**Merged**: PR #956

## Goal

Lift the Phase-1 **acyclic-graph restriction** so a published BPMN process may model **loop-back sequence
flows** (a flow whose target is upstream of its source) and have them execute. A do-while count loop, a
retry loop around a task, a parallel fork/join **inside** a loop body — all become executable. The mechanism
is the Flowchart #382 loop model ported to BPMN **tokens**: a token carries an **iteration key**; traversing
a **backward** (loop-back) edge mints a **fresh** key; forward propagation **inherits** the emitting token's
key; and **join accounting groups arrivals by `(element, iteration key)`** so a revisit of a join never
conflates iteration *N* with iteration *N+1*.

This is the CLOSING work unit of the BPMN Phase 2 events tier. It removes `BpmnGraph.ValidateAcyclic` and the
importer's cycle degradation; every other structural rule stays byte-identical and is what continues to
constrain where a loop-back may land (no loop-back into a start event, a boundary event, or an
event-gateway-armed catch — the existing rules already forbid those). No new authoring surface, no
loop-iteration variable, no runaway-loop guardrail, and no interchange schema change: cycles are just flows.

### What stays out of scope (stated cuts, D5)

- **No loop-iteration variable exposure.** A token's iteration key is engine-internal join-accounting
  identity; there is no authored `loopCounter`/`loopIndex` frame for a plain sequence-flow loop (that is an
  authoring-surface follow-up, and multi-instance already owns the `loopIndex` frame it needs).
- **No unbounded-loop guardrails.** A modeled loop with no exit is the author's responsibility this slice
  (the same stance every BPMN engine takes for an un-terminating loop); the engine adds no max-iteration cap.
- **No interchange schema growth.** A cyclic document imports and round-trips as ordinary elements + flows;
  the importer emits and parses nothing new. Only the *degradation finding* is removed.
- **No expression-conditioned flows** (unchanged engine-wide cut); a bounded loop is authored with an
  exclusive-gateway decision child (outcome-routed) exactly as Phase-1 branch tests are.

## Context (what exists today, origin/main = e5a5de8ca)

- **Graph** (`BpmnGraph`, `src/Elsa/Activities/Bpmn/Internal/BpmnGraph.cs`). `From` reads the structure and
  runs `Validate`: unique ids, resolvable refs, ≥1 start event, per-family child binding, single default
  flow, `ValidateEventBasedGateways`, `ValidateBoundaryEvents`, `ValidateMultiInstance`, and — last —
  `ValidateAcyclic` (a three-color DFS that throws "BPMN structure contains a cycle through element '…'").
  `CanReach(source, target)` is a BFS with a visited set — **already cycle-safe**, no change needed.
  `InboundFlows`/`OutboundFlows`/`GetDefaultFlow`/`AttachedCatchBoundaries`/`AttachedErrorBoundary` are the
  navigation surface.
- **Token** (`BpmnToken`, schema v1): `TokenId, AtElementId, FlowId, ParentTokenId, Status ∈
  Active/AwaitingChild/WaitingAtJoin/Consumed/Canceled, ProducingActivityExecutionId`. No iteration
  dimension. Ids come from `BpmnExecutionState.Sequence`; the sole mutation home is `BpmnStateMutator`;
  `Canceled` tokens are never pruned; `Consumed` tokens not retained by an active child are pruned on save.
- **Join accounting** (`BpmnTokenCoordinator`). Tokens emitted onto a multi-inbound parallel/inclusive
  gateway park as `WaitingAtJoin` (`ShouldWaitAtJoin`). `ReleaseReadyJoins` groups waiting tokens by
  `AtElementId` and fires the first ready group; **parallel** `IsJoinReady` = every inbound flow id present
  among the element's waiting tokens; **inclusive** `IsJoinReady` = no un-arrived inbound flow is still
  reachable by a live token position (`AnyLivePositionCanReach` over `CanReach`); `FireJoin` consumes exactly
  one arrival **per arrived inbound flow** (surplus same-flow arrivals stay parked) and mints one merged
  `Active` token at the gateway. **Arrivals are keyed by `FlowId` with no iteration dimension** — under a
  cycle, iteration *N* and *N+1* arrivals on the same flow conflate. This is what iteration keys fix.
- **Engine** (`BpmnExecutionEngine`). `StartAsync` seeds start tokens; `Propagate` releases ready joins then
  dispatches the first `Active` token; `ApplyDecision`'s `EmitTokens` mints one token per outbound flow taken
  (event-based gateways make their emitted tokens race members); `ScheduleChild` parks the token
  `AwaitingChild` and arms catch boundaries; a multi-instance host becomes a loop coordinator with private
  per-instance sub-tokens; error-boundary absorption mints an error token; `FireJoin` mints a merged token.
  `FinishEvaluation` picks the continuation and, when all live tokens are `WaitingAtJoin` with no active
  children, faults `bpmn.join.deadlock`.
- **Flowchart precedent (#382).** `FlowchartJoinCoordinator` groups/matches arrivals by `(CurrentNodeId,
  ExecutionScopeId, IterationKey)`. `FlowchartScopeResolver.ResolveTargetScope` mints an iteration key
  `"{ownerNodeId}:{iterationNumber}"` on a backward edge, using a monotonic per-owner counter
  (`FlowchartExecutionState.LoopIterationCounters`) that survives scope pruning. `ExecutionPath`/
  `FlowchartArrival` carry a nullable `IterationKey`. **Backward-edge classification**
  (`FlowchartReachabilityAnalyzer.IsBackwardEdge`) is, in the *actual* source, the naive
  `CanReach(target, source)` guarded by `ValidateNoAmbiguousLoopbacks` (a loop target must be single-inbound)
  — see the Deviations note; this slice ports the *correct DFS back-edge* semantics the work brief describes,
  not that naive form.
- **Interchange.** `BpmnDocumentImporter` runs `HasCycle` over the connected flow graph and, on a cycle, adds
  a **Degraded** finding ("not executable in this slice; loops arrive in the events tier"). The exporter emits
  nothing cycle-specific. This finding is removed here; the exporter is unchanged.

## Design decisions

### D1 — Token iteration key (additive; schema stays version 1)

- `BpmnToken` gains one additive optional constructor channel, `IterationKey` (`string?`, `null` on every
  token of the implicit first pass — "iteration 0"). Schema stays version 1 (additive state growth, the
  unreleased-no-backcompat stance).
- **Inheritance rule (the whole mechanism).** Every engine token-minting site copies the source/parent
  token's `IterationKey` **except** `EmitTokens` when the traversed flow is a **backward edge**, which mints a
  **fresh** key. Concretely:
  - `StartAsync` seed tokens — `null` (implicit iteration 0).
  - `EmitTokens` (forward edge) — inherit the emitting token's key.
  - `EmitTokens` (backward edge) — mint a fresh key (below).
  - `FireJoin` merged token — inherit the **fired group's** key.
  - multi-instance instance sub-tokens — inherit the coordinator token's key.
  - boundary catch listener tokens — inherit the host token's key.
  - error-boundary error token — inherit the interrupt target (host/coordinator) token's key.
  - event-based-gateway race member tokens — minted by `EmitTokens`, so they inherit (a gateway's own
    outbound is forward under the classification; a loop-back into a gateway-armed catch is rejected by the
    existing exactly-one-inbound rule).
- **Key format — deterministic, unique per traversal, derived from `Sequence`.** The fresh key minted at a
  backward-edge traversal is `"{loopEntryElementId}#{Sequence+1}"` where `loopEntryElementId` is the flow's
  target and `Sequence+1` is the id counter value at the mint point (the same value the minted token's id
  uses, so the key number and its loop-entry token's id number coincide). This is a **pure function of
  mutation order**, unique across the process (Sequence is monotonic, never reused), and needs **no
  per-owner counter record**: the required property — *sibling tokens of one iteration share a key by
  inheritance; a fresh key appears only at the single backward-edge mint* — holds because a fresh key is
  minted at exactly one place (the backward-edge branch of `EmitTokens`) and every other site inherits.
  *(This is where the port simplifies over Flowchart: Flowchart needs the monotonic per-owner counter because
  its keys must survive scope pruning while remaining collision-free across pruned generations; BPMN keys are
  Sequence-derived and globally unique by construction, so a pruned Consumed token can never resurrect a
  key.)*

### D2 — Backward-edge classification (computed once at graph construction; deterministic)

- `BpmnGraph` precomputes, at construction, the set of **backward flow ids** and exposes
  `bool IsBackwardFlow(string flowId)` (+ a `BackwardFlowIds` collection for tests). A backward edge is the
  **standard compiler back edge**: during a depth-first traversal from the graph's entry points, an edge
  `u → v` is backward iff `v` is **GRAY** (on the current DFS stack — an ancestor of `u`) when the edge is
  examined. This marks **exactly the loop-closing edge** of each loop, never the forward edges of the cycle
  (unlike the naive "target can reach source", which marks every edge of a cycle — see Deviations).
- **Entry points = start events.** The DFS is seeded from the start events, then from any remaining
  unvisited elements (a subgraph unreachable from a start event still gets classified). Both seed lists and
  each node's outbound-flow iteration are **ordinal-sorted** (start-event ids, then remaining element ids;
  outbound flows by flow id), so the back-edge set is a **pure function of the element/flow/start-event sets**
  — stable regardless of authoring order and identical across runs. Multi-start graphs are covered by the
  ordinal-sorted-roots rule; for the reducible flow graphs that structured BPMN loops produce, the back-edge
  set is in fact order-invariant (a back edge is an edge whose head dominates its tail), but this slice does
  not rely on reducibility — determinism comes from the fixed traversal order.

### D3 — Join accounting groups by `(element, iteration key)`

- `BpmnTokenCoordinator` groups `WaitingAtJoin` arrivals by `(AtElementId, IterationKey)`.
  `ReleaseReadyJoins` fires the first ready `(element, key)` group; `IsJoinReady` and `FireJoin` operate
  **within one group** (the arrived-flow-id set, the surplus-parking, the merged-token mint all scoped to the
  group). The merged token carries the group's key (inheritance, D1).
- **Inclusive-join reachability stays iteration-aware.** `AnyLivePositionCanReach` considers only live token
  positions **of the same iteration key** as the waiting group — because forward propagation preserves the
  key, only a same-key live token can produce a same-key arrival at the join; a different-key token that
  reaches the join's inbound source arrives under *its* key, forming a different group. In an acyclic graph
  every key is `null`, so the same-key filter is the identity and inclusive-join behavior is **byte-identical
  to today**. `CanReach` itself is unchanged (already cycle-safe).
- **The deadlock detector is unchanged and still fires only on true deadlock.** `FinishEvaluation` faults
  `bpmn.join.deadlock` only when there are **no active children and every live token is `WaitingAtJoin`**.
  That condition means nothing can move regardless of grouping: a new arrival requires an `Active`/
  `AwaitingChild` token or a running child to propagate one, and there are none. Iteration grouping cannot
  turn a live run into a false positive (a group that is not yet ready keeps its would-be feeders live and
  non-`WaitingAtJoin`, so the guard does not trip) nor mask a real deadlock. No change.

### D4 — `ValidateAcyclic` removed; every other structural rule constrains cycles unchanged

- `BpmnGraph.Validate` **no longer calls `ValidateAcyclic`**, and the method is deleted. All other rules stay
  byte-identical. Cycles are therefore constrained only by the pre-existing structural rules, which already
  forbid the ill-formed loop-backs:
  - a loop-back **into a start event** → the start-event-has-no-inbound rule rejects it;
  - a loop-back **into a boundary event** → the boundary-has-no-inbound rule rejects it;
  - a loop-back **into an event-gateway-armed catch** → the event-gateway-target-has-exactly-one-inbound rule
    rejects it (the catch already has the gateway as its sole inbound).
  No new rule is needed; the tests assert each still rejects.
- **Re-visit tolerance of the events-tier machinery.** Every per-token construct re-arms/re-instantiates
  naturally on a revisit, because the state records are keyed by the *arriving token id*, not by element:
  - a **multi-instance host** revisited by a second (later-iteration) token starts a **second, independent**
    loop — `BpmnLoopState` is keyed by coordinator `TokenId`, so N concurrent loops on one element coexist;
  - a **catch/boundary/event-gateway** element revisited re-arms — the spec 116/119/120 machinery is
    per-token (listener tokens parented to the arriving host token, race members minted per arrival);
  - a **parallel/inclusive join** revisited groups the new arrivals under the new iteration key (D3).

### D5 — Stated cuts

No loop-iteration variable exposure; no unbounded-loop guardrail; no interchange schema growth (finding
removed only); no expression-conditioned flows (unchanged). See "What stays out of scope" above.

## In scope (this slice)

- **Model (D1):** `BpmnToken.IterationKey` (additive); `BpmnStateMutator.NewToken` iteration-key channel.
- **Graph (D2, D4):** delete `ValidateAcyclic` (call + method); add the backward-flow precompute +
  `IsBackwardFlow`/`BackwardFlowIds`.
- **Coordinator (D3):** `(element, IterationKey)` grouping in `ReleaseReadyJoins`/`IsJoinReady`/`FireJoin`;
  same-key filter in `AnyLivePositionCanReach`.
- **Engine (D1):** thread the iteration key through all six token-minting sites (start seed inherit-null;
  `EmitTokens` forward-inherit / backward-mint; `FireJoin` group-inherit; multi-instance instance inherit;
  boundary listener inherit; error token inherit).
- **Interchange (D5):** remove the `HasCycle` degradation finding + the now-unused `HasCycle` method;
  exporter unchanged.
- **Tests + docs:** backward-edge classification unit tests; do-while; parallel fork/join in a loop; catch
  event in a loop; multi-instance host in a loop; boundary host in a loop; structural-rule rejections;
  determinism; interchange import-clean + round-trip. BPMN README + EXTENSION_POINTS phasing notes;
  Interchange README (cycle degradation lifted).

## Out of scope (deferred / stated cuts)

Loop-iteration variable exposure; unbounded-loop guardrails; interchange schema growth; expression-conditioned
flows; any change to `CanReach`, the deadlock detector, the exporter, or the spec 116–121 machinery beyond the
mechanical iteration-key threading.

## Functional requirements

**FR-1 — Cyclic graphs build.** `BpmnGraph.From` accepts a graph with loop-back sequence flows;
`ValidateAcyclic` no longer exists. Every other structural rule is unchanged and still throws its
deterministic `BpmnExecutionException`.

**FR-2 — Backward-edge classification.** `BpmnGraph.IsBackwardFlow` returns true for exactly the loop-closing
edges (DFS back edges from the ordinal-sorted start-event roots), false for forward/cross edges — including
the forward edges of a cycle. The set is deterministic and independent of authoring order and of multi-start
iteration order.

**FR-3 — Iteration-key minting + inheritance.** A backward-edge traversal mints a fresh
`"{targetElementId}#{Sequence+1}"` key; every other minting site inherits its source/parent/group token's
key; a start seed is `null`. Ids and keys stay a pure function of `Sequence`.

**FR-4 — Join grouping.** Parallel and inclusive joins group waiting arrivals by `(element, iteration key)`;
a join fires within one group; the merged token inherits the group's key. Iteration *N* and *N+1* arrivals at
the same join never conflate.

**FR-5 — Inclusive reachability iteration-aware.** `AnyLivePositionCanReach` counts only same-iteration-key
live positions; acyclic behavior is byte-identical (all keys `null`).

**FR-6 — Structural rules still constrain loop-backs.** A loop-back into a start event, a boundary event, or
an event-gateway-armed catch is rejected by the pre-existing rules (no new rule).

**FR-7 — Re-visit tolerance.** A task, an exclusive-gateway count loop (do-while), a parallel fork/join
inside a loop body, a catch event in a loop body, a multi-instance host in a loop body, and a boundary host
in a loop body all execute: each per-token construct re-arms/re-instantiates per pass, and the process
completes deterministically.

**FR-8 — Deadlock detector unchanged.** `bpmn.join.deadlock` fires only on a true all-`WaitingAtJoin`,
no-active-children snapshot; iteration grouping introduces no false positive and masks no real deadlock.

**FR-9 — Determinism.** Identical runs produce identical token ids, iteration keys, and state ids.

**FR-10 — Interchange.** A cyclic document imports **clean** (no cycle finding) and round-trips through
import → export → import; the importer emits nothing new; the spec 118/120/121 interchange suites are
otherwise unmodified.

## Invariants that MUST survive

- `Elsa.Bpmn.ExecutionState` stays schema version 1 (`IterationKey` is additive); the only mutation home
  remains `BpmnStateMutator`; all record ids derive from `Sequence`; `Canceled` tokens are never pruned; a
  terminal continuation never co-exists with staged child schedules; a `Fault`/`Cancel` continuation never
  co-exists with a staged seam-A/seam-B request.
- Behaviors stay decision-only and iteration-key-unaware; the entire iteration-key lifecycle lives in the
  engine + coordinator + state mutator + graph. No new behavior family, no new token status.
- `CanReach`, the deadlock detector, the exporter, and the spec 116–121 machinery are unchanged beyond the
  mechanical iteration-key threading and the `(element, IterationKey)` join-grouping rename.
- Deterministic ids only; no wall-clock-derived identity. No new HTTP endpoints; the domain project-tree
  naming guard and VF-ACT gates hold.

## Success criteria

- Backward-edge unit tests: simple self-loop, do-while shape, nested loops, multi-entry (two start events)
  stability, parallel-in-loop — `BackwardFlowIds` equals the expected loop-closing-edge set.
- A cyclic graph builds (the former `CyclicGraph_IsRejected` becomes `CyclicGraph_IsAccepted…`).
- Do-while (task → exclusive-gateway decision → loop back N times → exit): the body runs N+1 times, distinct
  iteration keys per pass are observable on live tokens, the process completes deterministically.
- Parallel fork/join inside a loop, two iterations: the join fires once per iteration with correct pairing;
  the node downstream of the join runs exactly once per iteration; the process completes (**the load-bearing
  conflation regression test**).
- Catch event inside a loop: the catch arms/resumes per iteration (distinct bookmark per pass), carrying the
  pass's iteration key.
- Multi-instance host inside a loop: two sequential passes each run their own loop instances.
- Boundary host inside a loop: the listener arms and tears down per pass.
- Structural rules preserved: a loop-back into a start / boundary / gateway-armed catch still rejected.
- Determinism: identical runs → identical token/iteration-key/state ids.
- Interchange: a cyclic document imports clean (finding gone) and round-trips; spec 118/120/121 interchange
  suites unmodified.
- Full test projects green: BPMN, BPMN Interchange, Activities Runtime, Workflows Runtime, Flowchart (read
  but unmodified — run to prove it), Architecture. Full solution build clean.

## Deviations from the ratified plan

- **Backward-edge classification: DFS back edge, not the Flowchart analyzer's literal `CanReach(target,
  source)`.** The work brief instructed to "port `FlowchartReachabilityAnalyzer`'s semantics" and described
  those semantics as "DFS-order/dominator-based, NOT naive 'target reaches source', which would mark every
  edge of a cycle." The *actual* `FlowchartReachabilityAnalyzer.IsBackwardEdge` is precisely that naive
  `CanReach(target, source)` — it marks **every** edge of a cycle as backward, and Flowchart tolerates the
  false positives only because `ValidateNoAmbiguousLoopbacks` forces loop targets to be single-inbound and
  its tests do not assert the absence of the spurious classifications. Porting that naive form to BPMN would
  be **incorrect**: for a parallel fork/join inside a loop it would mint a fresh key on the fork's forward
  edges, so the two branches would never share an iteration key and their join could never pair — the exact
  conflation this unit exists to prevent. Resolution: this slice implements the **standard DFS back-edge
  classification** the brief *describes* (an edge to a GRAY/on-stack ancestor), seeded from ordinal-sorted
  start events. This is the correct, deterministic port of the *intent*; it diverges only from the literal
  Flowchart code, which the brief itself flagged as the wrong model. `FlowchartReachabilityAnalyzer` is read
  but not modified.
- **No per-owner iteration counter record.** The brief permitted adding an additive per-owner counter record
  "if the Flowchart port genuinely needs [it] for correctness." It does not: BPMN keys are Sequence-derived
  and globally unique, so a fresh key appears only at the single backward-edge mint and can never collide with
  a pruned generation. The `Sequence`-derived `"{targetElementId}#{Sequence+1}"` key (D1) satisfies the
  required property without a counter, so none is added.
- **Inclusive-join reachability gains a same-iteration-key filter (D3).** Beyond the mechanical grouping, the
  inclusive-join `AnyLivePositionCanReach` is narrowed to same-key live positions to keep cross-iteration
  tokens from holding a join open. In acyclic graphs (all keys `null`) this is the identity, so existing
  inclusive-join behavior is unchanged; it is called out here because it is a reasoned change to the
  reachability predicate, not a pure rename.
