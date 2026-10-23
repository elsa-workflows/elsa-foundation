# Data model: Elsa 3 Flowchart connections (spec 239)

All types are internal to `Elsa3.Mapping` unless marked public. Additive changes to spec 236 types are marked (+).

## 1. Map

- `Elsa3GraphConstructKind` (new enum): `Fork`, `Join`, `Start`.
- `Elsa3ActivityTarget.GraphConstruct(Elsa3GraphConstructKind Kind)` (new target variant, public like its siblings).
  Replaces `GraphLevel` on the `FlowFork`, `FlowJoin` and `Start` entries. `GraphLevel` stays in the model; no
  built-in entry uses it after this spec.
- Entry changes:

| Elsa 3 type | Target | Members |
|---|---|---|
| `Flowchart` | catalog `Elsa.Activities.Flowchart.Activities.Flowchart` | `Activities` → slot `Flowchart.Activities`; `Variables` → structure; `Connections`, `Start` → graph (consumed by the pass) |
| `FlowFork` | `GraphConstruct(Fork)` | `Branches` → structure (read by the pass) |
| `FlowJoin` | `GraphConstruct(Join)` | `Mode` → structure (read by the pass) |
| `Start` | `GraphConstruct(Start)` | none |

## 2. Resolution

- `Elsa3ResolvedActivity` (+) `Outcomes: Elsa3ActivityOutcomes`.
- `Elsa3ActivityOutcomes` (new record): `Ports: IReadOnlyList<string>` (static outcome names, ordinal), `Dynamic:
  Elsa3DynamicOutcomes?`.
  - `Elsa3DynamicOutcomes(string SourceInputKey, string? UnmatchedOutcome)`: a port is valid when it is a literal item
    of the node's `SourceInputKey` input, or `UnmatchedOutcome`.
  - `Elsa3ActivityOutcomes.DoneOnly` for an activity with no `elsa.outcomes` facet and for intrinsics.
  - `Elsa3ActivityOutcomes.None` for a graph construct.
- `Elsa3ResolvedSource` (+) `GraphConstruct`.
- `Elsa3ActivityExactReplacement` (+, public) `OutcomeNames: IReadOnlySet<string>?` (null keeps today's callers
  source-compatible; the materializer always sets it). Ordinal.

## 3. Plan

- `Elsa3NodePlan` (+)
  - `GraphConstruct: Elsa3GraphConstructKind?`: set for a resolved construct.
  - `FollowedBy: Elsa3Activity?`: the synthesized node that must follow this one (HttpEndpoint projection, R-10).
  - `StructureFields["connections"]`, `StructureFields["startNodeId"]` for a converted flowchart (existing dictionary).
- `Elsa3GraphEdge` (new record, pass-local): `SourceNodeId`, `Port` (Elsa 3 port before mapping, Elsa 4 after),
  `TargetNodeId`, `ConnectionIndex` (the Elsa 3 connection it came from, for diagnostics and order).
- Emitted connection payload, matching `FlowchartConnection`/`FlowchartEndpoint` in web JSON:

```json
{ "source": { "nodeId": "decide", "port": "True" }, "target": { "nodeId": "notify", "port": null } }
```

No `id` (Elsa 4 derives `{source}:{port}->{target}`); `target.port` null; no `nodeMetadata` or `connectionMetadata`
(default policies).

## 4. Graph pass state machine (per flowchart)

```text
collect edges ──► validate endpoints ──► drop dead edges (terminals, fork non-branches) ──► map ports
      ──► classify back edges (entries: start candidate + no-inbound children)
      ──► loop check ──► join checks ──► collapse constructs (fixed point, document order)
      ──► resolve startNodeId ──► dedupe ──► write structure fields; remove constructs from slots/Activities
```

Any error stops the pass for that flowchart after reporting every cause found in the stage that failed (so one
definition reports all its connection errors at once). Errors in different flowcharts are independent.

## 5. Diagnostics

Each graph diagnostic carries the spec 236 fields (definition, node id, JSON path, guidance, metadata). For a
connection: node id = the Elsa 3 source node, JSON path = `{flowchart path}.connections[{index}]`, metadata
`TargetNodeId` and `Port`. For a join or fork: node id and path of the construct. Codes:
[contracts/diagnostic-codes.md](contracts/diagnostic-codes.md).

## 6. Validation rules (summary of FR-003 to FR-013)

| Rule | Outcome |
|---|---|
| Endpoint not a child of the flowchart | `GRAPH-CONNECTION-INVALID` |
| `connections` on a non-flowchart | `GRAPH-CONNECTION-INVALID` |
| Port unsupported by the map's outcome rule | `NO-COUNTERPART` |
| Port not emitted by the Elsa 4 activity | `GRAPH-OUTCOME-UNMAPPED` |
| Edge leaves `Complete`, `End`, `Break`, or a fork port outside its branches | removed, `DEAD-CONNECTION-REMOVED` |
| FlowFork branches by JavaScript | `UNSUPPORTED-MODE` |
| Construct outside a flowchart | `UNSUPPORTED-MODE` |
| `WaitAny` with concurrent inbound edges; `WaitAll` not provably all-live; mode by expression | `GRAPH-JOIN-UNSUPPORTED` |
| Back edge not to a single-inbound start node, or into a construct | `GRAPH-LOOP-UNSUPPORTED` |
| Start/fork at the start with several targets | `GRAPH-START-AMBIGUOUS` |
| `End` outside the root flowchart | `UNSUPPORTED-MODE` |
