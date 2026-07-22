# Research: publish-time routing tables (ADR 0047 D3)

This is the correctness contract for the routing table — every input the current per-completion routing
walk consults. The table must reproduce these decisions **exactly** (same successors, same order, same
no-match/terminal behavior), or (better, and what this unit does) be derived by the same code path the
walk uses so it *cannot* diverge.

## Where the walk actually lives (verified against source)

ADR 0047 D3 attributes the graph walk to `WorkflowParentActivityCompletionSchedulerWorkHandler`. That is an
ADR-level simplification. The handler
(`src/Elsa/Activities/Runtime/Services/WorkflowParentActivityCompletionSchedulerWorkHandler.cs`) does **not**
route: it loads parent state + the pinned executable, constructs the parent composite activity, and invokes
`IRuntimeActivityChildCompletionHandler.OnChildCompletedAsync(context, ActivityChildCompletedContext)` passing
the completed child's `ExecutableNodeId` and `OutcomeNames` (lines 255-262). The routing — outcome → successor
executable node(s) — happens **inside the composite engine**, which reconstructs its graph from
`ExecutableNode.Structure.Payload` on every call and scans it.

The edges are not first-class fields on `ExecutableNode`. They are an opaque `JsonElement`
(`ExecutableActivityStructure.Payload`, kind + schemaVersion + payload) owned by the activity module,
deserialized per invocation into a typed structure and wrapped in a per-call graph object. That
per-call deserialize + scan is the cost D3 removes.

## The routing relation, per composite

| Composite | Routing site | Match rule | Order | Join/fan-in | No-match / default |
|---|---|---|---|---|---|
| **Flowchart** | `FlowchartGraph.SelectOutboundConnections` (`FlowchartGraph.cs`), driven from `FlowchartExecutionEngine.OnChildCompletedAsync:143` (and the gateway policy path `:107-121`) | `connection.Source.NodeId == completedChild` ∧ `connection.Source.Port ∈ NormalizeOutcomes(outcomes)` (Ordinal set) | connection **declaration order** in `FlowchartStructure.Connections` | resolved **after** routing, per matched target, from runtime state (`FlowchartJoinCoordinator.ShouldWaitForTarget`, inbound via `GetInboundConnections`) — **not precomputable, stays runtime** | empty outcome ⇒ `Done` port (`FlowchartEndpoint.NormalizePort`); no matching connection ⇒ empty ⇒ path terminates. No implicit "else" edge. Duplicate targets ⇒ hard error. `Break` outcome short-circuits before routing. |
| **Sequence** | `SequenceNavigator.SelectAfter` (`SequenceNavigator.cs`), driven from `Sequence.OnChildCompletedAsync` | index of completed child **+1** over `SequenceExecutableStructure.Activities` (ordered ids); **outcomes ignored** | positional | none | past the last child ⇒ `null` ⇒ Sequence completes; `Break` short-circuits |
| **BPMN** | `BpmnFlowSelector` (`BpmnFlowSelector.cs`), driven from `BpmnExecutionEngine.OnChildCompletedAsync`, applied through token emission | `flow.ConditionOutcome ∈ outcomes` (exclusive: first match; inclusive/task: all matches or unconditional) | flow declaration order within source | token `WaitingAtJoin` vs `Active` from runtime state (`BpmnTokenCoordinator.ShouldWaitAtJoin`) — **runtime** | `Element.DefaultFlowId` / `IsDefault` flow, else empty (token ends) |
| **ControlFlow** (If/Switch/loops) | intrinsic navigators (`If.cs`, `Switch.cs`, `*Navigator.cs`) | evaluated input vs **named child slots** at `ExecuteStructureAsync` time; completion only validates the branch | n/a | n/a | **no outcome→successor connection walk — nothing to tabulate** |

## What is static (precomputable) vs runtime

The static, publish-time-determined part is **the edge relation and its ordering**: `(sourceNodeId, outcome) →
outbound connections/flows`, and `targetNodeId → inbound connections/flows`. This is a pure function of the
immutable `Structure`. Everything the join/fan-in accounting consults — which sibling branches have arrived,
token positions, path/scope status — is **runtime state** and is deliberately left in the engine (ADR 0047
D2 ratification resolution #1: joins keep the discrete cascade). D3 precomputes only the static relation.

Only **Flowchart** actually did a linear scan (`_connections.Where(source ==).Where(port ∈)`). **BPMN**
already pre-indexes (`ILookup _outboundBySource/_inboundByTarget`) and **Sequence** already indexes
(`_indexesByNodeId`, O(1) `+1`). So for Flowchart the D3 change is *both* memoizing the graph *and* turning
the scan into an index lookup; for BPMN/Sequence it is *only* memoizing the per-hop `From` rebuild. In all
three, the pre-D3 cost was rebuilding the graph (deserialize `Structure.Payload` + revalidate + reindex) on
**every** completion hop — `From(context.ExecutableNode)` was called fresh at every Start / OnChildCompleted /
OnChildFaulted.

## Placement decision (binds this unit)

ADR 0047 ratification resolution #2: **recomputed on materialization**, not stored in the hashed artifact.
The routing structure is carried on the in-memory `ExecutableNode` instance only (a private, non-serialized
memo — `GetOrAddRoutingStructure<T>`), derived deterministically from graph content the ADR 0038 content hash
already covers. It therefore adds no field to the persisted executable schema and does not touch the hash
(confirmed: `WorkflowExecutableCompilerGoldenTests` — 65 cases — pass unchanged).

## Composition with spec 111

The spec-111 burst cache (`BurstCachedWorkflowExecutableReader` + `WorkflowBurstScope`) already serves **one**
`WorkflowExecutable` instance per artifact per drain, so the same `ExecutableNode` instances flow to every hop
in a burst. Hanging the memo on `ExecutableNode` means the routing structure builds **once per composite per
burst** and every later hop reuses it — one memoize riding on the existing per-artifact burst entry, no second
cache layer. Burst-absent (cache off) ⇒ a fresh instance per hop ⇒ the memo recomputes per hop, byte-identical,
just without reuse (this is the natural A/B, measured below).

## Measured evidence (benchmark)

`EngineExecutionBenchmarks.RoutingStructureMaterializations_CollapseWithBurstCache`, counting actual
materializations via `RoutingStructureMaterializationDiagnostics`:

| Shape | burst-cache ON (post-D3) | burst-cache OFF (≙ pre-D3 per-hop) |
|---|---|---|
| hot-loop×10 | **1** materialization/run | 11 |
| 2-node | **1** | 2 |

The routing-structure build is gone from the hot path: it collapses from once-per-completion-hop to
once-per-composite-per-burst. Per ADR 0047, D3 is an **enabler** — the deterministic materialization/commit
counters are the evidence; a within-noise wall delta on these small payloads is the honest, expected result
(the per-hop structure deserialize is a small share of a durable hop, same finding as spec 109's fast path).
D1/D2 (fusion) is what converts the removed walk into a large hop-count win.
