# Implementation Plan: Publish-time routing tables (ADR 0047 D3)

**Spec**: [spec.md](./spec.md) · **Research**: [research.md](./research.md) · **Branch**:
`worktree-agent-afab78e779b02243f` · **Status**: implemented

## Approach

Recompute the routing relation on materialization and carry it on the in-memory `ExecutableNode` only.
The relation is derived by the composite's own graph builder (`FlowchartGraph.From` / `BpmnGraph.From` /
`SequenceNavigator.From`), so it cannot diverge from the runtime walk. Nothing in the persisted artifact or
its content hash changes.

## Changes

### Product

1. `src/Elsa/Workflows/Runtime/Core/Models/ExecutableNode.cs`
   - `GetOrAddRoutingStructure<T>(Func<ExecutableNode,T>)`: lazy, thread-safe (`LazyInitializer` +
     `ConcurrentDictionary<Type,object>`), keyed by structure type (a node reconstructs exactly one kind).
     Private backing field ⇒ not serialized ⇒ hash/schema-stable.

2. `src/Elsa/Workflows/Runtime/Core/Models/RoutingStructureMaterializationDiagnostics.cs` (new)
   - Static `Count` / `Reset` + `internal OnMaterialized()`, incremented only when the memo factory actually
     runs (never on a cache hit). Benchmark-attribution only; no behavior. The routing analog of the existing
     commits/run and executable-reads/run counters.

3. `src/Elsa/Activities/Flowchart/Internal/FlowchartGraph.cs`
   - Build `_outboundBySource` / `_inboundByTarget` connection indexes at construction, preserving connection
     declaration order.
   - `SelectOutboundConnections` / `SelectTargets` / `GetInboundConnections` / `CanReach` use the indexes.
   - Retain the pre-D3 linear scans as `SelectOutboundConnectionsByScan` / `GetInboundConnectionsByScan` for
     the differential guardrail.

4. Engine call-site swaps (`FlowchartGraph.From(node)` → `node.GetOrAddRoutingStructure(FlowchartGraph.From)`
   etc.):
   - `FlowchartExecutionEngine.cs` (Start / OnChildCompleted / OnChildFaulted — 3 sites)
   - `Sequence/Activities/Sequence.cs` (ExecuteStructure / OnChildCompleted — 2 sites)
   - `Bpmn/Internal/BpmnExecutionEngine.cs` (Start / OnChildCompleted — 2 sites)
   - `BpmnProcessTriggerStimulusProvider` (stimulus/trigger path, not a completion hot path) left as direct
     `From` — memoizing there needs no burst and yields nothing.

### Tests / benchmark

5. `tests/Elsa/Activities/Flowchart/Tests/FlowchartRoutingTableDifferentialTests.cs` (new): index-vs-scan
   differential over the corpus + memo build-once / no-divergence.
6. `benchmarks/.../EngineExecutionBenchmarks.cs`: `RoutingStructureMaterializations_CollapseWithBurstCache`
   reports materializations/run ON vs OFF for hot-loop and 2-node, soft-asserts the collapse.

## Correctness argument

- The index preserves declaration order (groups built by iterating connections in order), so
  `SelectOutboundConnections` returns the same connections in the same order as the scan — asserted for every
  node × outcome-set in the corpus, including no-match and default (empty ⇒ `Done`) and multi-target ordering.
- The memo returns the same instance every hop (`Assert.Same`) and its routing equals a fresh build's routing;
  the structure is immutable and a pure function of the node, so recomputing on a fresh materialization is
  byte-identical (the burst-absent path).
- Join/fan-in gating is untouched — it still reads runtime state in the engine (ADR 0047 resolution #1).

## Risks

- **Hash/schema drift** — mitigated: private field, golden tests (65) pass unchanged.
- **Order sensitivity** (scheduling determinism depends on successor order) — mitigated: index preserves
  declaration order; differential test asserts order-sensitively.
- **Concurrency on the memo** — within a burst the drain is single-writer (ADR 0031); across bursts each
  materialization is a distinct immutable instance; `ConcurrentDictionary` + `LazyInitializer` are safe even if
  the executable store ever shares an instance, and a benign double-build is harmless (deterministic result).

## What D1+D2 consume from this unit

The inline completion pass (D2) must not pay a graph walk either; it reads the same
`ExecutableNode.GetOrAddRoutingStructure(...)` memo to route the fused edge. D3 leaves the join/fan-in
gating in the engine so D2's "single-predecessor edges only" first iteration composes cleanly.
