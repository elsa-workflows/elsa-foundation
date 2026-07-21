# Feature Specification: Publish-time routing tables, recomputed on materialization (ADR 0047 D3)

**Feature Branch**: `worktree-agent-afab78e779b02243f`
**Created**: 2026-07-21
**Program**: Runtime Execution Seam
**ADR**: [ADR 0047](../../docs/adr/0047-replaysafe-activities-execute-as-fused-hops-with-precomputed-routing.md) — **Decision D3 only**
**Work unit**: D3 is deliberately first — pure compile/materialization-time work with zero runtime-semantics
risk, it benefits External activities too (the discrete completion cascade does the same lookup), and it is the
piece D1+D2 (hop fusion) will consume. The spec-095 FR wording and ADR 0031 "5–7 hops" amendments belong to the
**D1 unit**, not this one (this unit removes no hops; it removes a per-hop graph rebuild).

**Input**: ADR 0047 D3 + its ratification resolution #2 (recomputed-on-materialization); spec 111 burst cache;
ADR 0038 content-hash semantics. Routing-contract map in [research.md](./research.md).

## Why

After spec 111 collapsed the redundant executable *reads* per burst, each straight-line completion hop still
rebuilt the composite's routing graph from scratch: the composite engines called `FlowchartGraph.From` /
`BpmnGraph.From` / `SequenceNavigator.From` on `context.ExecutableNode` at **every** Start / child-completion,
each deserializing `ExecutableActivityStructure.Payload`, revalidating, and (Flowchart) linearly scanning every
connection to route one outcome. The outcome→successor relation is fully determined at publish time. D3
precomputes it once per materialized executable so routing is a dictionary lookup, and Flowchart's linear scan
becomes an index lookup.

## What

1. **A per-node, in-memory routing-structure memo** on `ExecutableNode.GetOrAddRoutingStructure<T>(factory)`:
   computes the composite's routing structure once per materialized instance and caches it. Private backing
   field ⇒ invisible to serialization ⇒ **no persisted-schema change and no ADR 0038 content-hash change**
   (resolution #2). Composes with the spec-111 burst cache: the same `ExecutableNode` instance is served for
   every hop in a drain, so the structure builds **once per composite per burst**, no second cache layer.

2. **Flowchart publish-time routing indexes**: `FlowchartGraph` builds `source → outbound connections` and
   `target → inbound connections` indexes at materialization, preserving connection declaration order.
   `SelectOutboundConnections` / `SelectTargets` / `GetInboundConnections` / `CanReach` use them. The pre-D3
   linear scans are retained callable as `SelectOutboundConnectionsByScan` / `GetInboundConnectionsByScan` for
   the differential guardrail. (BPMN and Sequence already index; their D3 gain is memoization only.)

3. **The composite engines route through the memo**: `context.ExecutableNode.GetOrAddRoutingStructure(...From)`
   in the Flowchart engine (Start / OnChildCompleted / OnChildFaulted), Sequence (ExecuteStructure /
   OnChildCompleted), and BPMN engine (Start / OnChildCompleted). The routing structure — hence the decisions —
   is the composite's own `From`, so the table cannot diverge from the walk.

## Guardrails

- **Routing decisions byte-for-byte identical** — `FlowchartRoutingTableDifferentialTests`: for a corpus
  (straight-line, multi-outcome branch, fan-in join, parallel fork, ordered multi-target, mixed-ports, empty),
  every node × outcome-set is routed through both the index path and the retained linear-scan reference and
  asserted order-sensitively equal; inbound compared likewise. The memo is proven to build once and never
  diverge from a fresh build.
- **End-to-end byte-identical durable state** — the full Flowchart / Sequence / BPMN / ControlFlow /
  Activities.Runtime / Workflows.Runtime / Groundwork suites (which assert durable outcomes) pass unchanged;
  `DurableRoundTripDiagnostics` commit/read counts are unchanged. D3 is transparent (no behavior toggle):
  memoization + index produce identical results, so there is no on/off durable-state divergence to gate; the
  spec-111 burst-cache On/Off A/B stands in as the measurement lever.
- **Hash/schema untouched** — `WorkflowExecutableCompilerGoldenTests` (65) pass unchanged.

## Scope boundary

D3 only. No hop removal, no fusion, no FR/ADR-0031 hop-count amendment (those travel with D1). Join/fan-in
gating stays in the engines on runtime state (ADR 0047 resolution #1). ControlFlow composites route
intrinsically (input vs named slots) and have no outcome→successor relation to tabulate — untouched.

## Success criteria

- Differential + memo guardrail tests green; all seven QA suites + golden tests green; full solution build green.
- Materialization benchmark shows the collapse (hot-loop×10: 11 → 1 per run; 2-node: 2 → 1) with the burst cache.
