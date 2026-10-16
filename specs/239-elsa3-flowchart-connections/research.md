# Research: Elsa 3 Flowchart connections (spec 239)

Verified 2026-10-16 against `origin/236-elsa3-activity-map` 544482427 (Elsa 4) and elsa-core 5b0c2d73 (Elsa 3). Line
numbers drift. Elsa 3 and Elsa 4 flowchart semantics are summarized in the spec's Context section; this file records
the decisions the plan rests on.

## R-1 Where graph conversion runs

- **Decision**: Graph conversion is a pass inside normalization. Once every node of a definition is normalized,
  `Elsa3FlowchartGraphBuilder` runs once per Elsa 3 flowchart (innermost first) and writes `connections` and
  `startNodeId` into that flowchart's `Elsa3NodePlan.StructureFields`. The Flowchart becomes an ordinary native
  container (slot `Flowchart.Activities`, scoped variables), built by `Elsa3NativeStructureBuilder` like Sequence.
- **Rationale**:
  - Both conversion paths (single definition, collection) and `IElsa3NodeConverter.PrepareAsync` already run the
    normalizer, so all three get graph conversion with no second pipeline (FR-016).
  - The pass needs every child's resolved Elsa 4 activity (its outcomes), which exists only after the children are
    normalized; a node-level rewrite on the Flowchart runs before its children.
  - Nested flowcharts inside any container (If branch, loop body, Switch case: 13 in the corpus) are covered by the
    same tree walk. A caller assembling `ConvertNodeAsync` results would have to re-implement nesting.
  - Value flow is lowered once, definition-wide, after the pass, so a consumer that reads a sibling's result resolves
    exactly as today (FR-002).
- **Alternative considered**: a separate graph converter that calls `PrepareAsync`, then `ConvertNodeAsync` per child,
  then assembles the flowchart. Rejected: it duplicates tree assembly, does not reach flowcharts nested in other
  containers, and needs `ConvertAsync` to call back into it. `IElsa3NodeConverter` stays as the public per-node
  surface (spec 235); its `Deferred` list simply no longer contains flowcharts (R-9).

## R-2 Representing FlowFork, FlowJoin and Start during normalization

- **Decision**: A new map target kind, `Elsa3ActivityTarget.GraphConstruct(kind)` with kind `Fork`, `Join` or
  `Start`, replaces `GraphLevel` for those three types. The resolver resolves it without the catalog to an
  `Elsa3ResolvedActivity` with `Source = GraphConstruct` and no version id. Member rules still run (FlowFork
  `Branches` and FlowJoin `Mode` are read as Structure members). Inside a flowchart the graph pass consumes the node
  and removes it from the parent's slots and `Activities`, before lowering. Outside a flowchart (a FlowJoin in a
  Sequence) it is `ELS3-ACTIVITY-UNSUPPORTED-MODE`: Elsa 3 gives it no behaviour there either.
- **Rationale**: keeps the node in the plan long enough for member rules and diagnostics to name it, then removes it
  so mapping and lowering never see it (G-2: no placeholder nodes). `GraphLevel` and its pending code are no longer
  used by any entry.
- **Alternative**: keep `GraphLevel` failures and suppress them when the parent is a flowchart. Rejected: hides a
  failure-as-control-flow path and loses member-rule checking for `Branches`/`Mode`.

## R-3 Outcomes of a resolved Elsa 4 activity

- **Decision**: `Elsa3ResolvedActivity` gains `Outcomes`: the static ports and the dynamic-outcome rule.
  - Catalog activity: read from the version's `elsa.outcomes` design facet
    (`{ports:[{name,type}], dynamicOutcomes?:{kind:"valuePerItem", source, unmatched}}`, written by
    `ClrAssemblyScanner` and by `ActivityDefinitionPublisher`). No facet → the activity's only outcome is `Done`.
  - Intrinsic: `Done` (`WorkflowIntrinsicExecutor` completes every intrinsic with `Done`).
  - Reusable replacement: `Elsa3ActivityExactReplacement` gains `OutcomeNames` (additive). The materializer fills it
    from the target's Elsa 3 definition by the rule of spec 236 D-5/D-6: `Done` plus the declared outcomes plus every
    literal outcome a `Complete` names. Contract names are the Elsa 3 strings verbatim (D-4), and the graph activity
    completes with that name (`GraphActivity.cs` `_selectedOutcomeName`), so the caller's port matches verbatim.
  - Dynamic outcomes (`SendHttpRequest` status codes, `RunJavaScript` possible outcomes): a port is valid when it is
    one of the node's literal items for the facet's `source` input, or the `unmatched` name. An item list bound by
    expression cannot be checked: the connection is `GRAPH-OUTCOME-UNMAPPED` (G-1).
- **Rationale**: FR-003 needs to know whether an Elsa 4 activity emits a port; the facet is the catalog's own answer
  and the CLR scanner already writes it.

## R-4 Connection mapping order

For each Elsa 3 connection `S:P → T` (after R-2 constructs are resolved):

1. Validate: `S` and `T` are children of this flowchart, else `GRAPH-CONNECTION-INVALID`.
2. `S` is a terminal (`Complete`, `End`, `Break`): remove, `DEAD-CONNECTION-REMOVED` (G-7).
3. `S` is a FlowFork: kept as a fork edge if `P` is a branch (or `Done` when the fork has none); else removed with
   `DEAD-CONNECTION-REMOVED`.
4. `S` is a FlowJoin or Start: kept as a construct edge on `Done`; another port → `GRAPH-OUTCOME-UNMAPPED`.
5. Otherwise `P` maps to Elsa 4 ports: the map entry's `Elsa3OutcomeRule` for `P` if any (empty → `NO-COUNTERPART`
   with the rule's reason); else `P` verbatim if `S`'s Elsa 4 outcomes contain it (R-3); else
   `GRAPH-OUTCOME-UNMAPPED`. One Elsa 4 connection per mapped port. `{match}` in a Switch rule expands to every case
   match.
6. Collapse constructs (R-5), then remove duplicate `(source, port, target)` triples, keeping first-seen order.

Target ports and `vertices` are never read. Connection ids are not emitted; Elsa 4 derives
`{source}:{port}->{target}` deterministically, and duplicates are already removed.

## R-5 Collapsing forks, joins and Start

- **Fork** `F`: every inbound edge `X:P → F` is replaced by `X:P → B` for every kept fork edge `F → B`. An Elsa 4
  completion follows every connection on its port, so this is the fan-out.
- **Join** `J`: after the mode check (R-6), every inbound edge `X:P → J` is replaced by `X:P → T` for every outbound
  edge `J → T`. `T`'s implicit dead-path join then waits for the same inputs. Equivalence: under dead-path joining,
  "join, then continue" and "each target joins the same inputs" fire on the same arrival sets, and the join itself
  executes nothing.
- **Start** `S`: removed; its `Done` target becomes the start candidate (R-7).
- Chains (fork into join, join into join) collapse to a fixed point, processing constructs in document order.
- A back edge into a fork or join → `GRAPH-LOOP-UNSUPPORTED` (FR-009), checked before collapsing.

## R-6 Join checks (G-4, G-5)

Computed on the Elsa 3 graph before collapsing, over forward edges only (R-8).

- **Fan-out point**: a node one completion can leave by two or more edges at once: a FlowFork with two or more kept
  branch edges, or any node with two or more edges on the same Elsa 3 port. (Distinct ports of one node are
  alternatives, not fan-out, because each converted activity completes with one outcome. The spec 236 map already
  refuses or warns where an Elsa 3 activity fires several outcomes at once.)
- **Concurrent inbound edges**: two inbound edges `e1`, `e2` of a join are concurrent when some fan-out point has two
  distinct outgoing edges `f1`, `f2` with the source of `e1` reachable from `f1` and the source of `e2` from `f2`
  (reachability over forward edges, not through the join). Different entry points (triggers) are never concurrent:
  one run starts at one of them.
- **`WaitAny`** converts when no two inbound edges are concurrent; else `GRAPH-JOIN-UNSUPPORTED`.
- **`WaitAll`** converts when all inbound edges are pairwise concurrent from one common fan-out point `F`, and every
  node on every forward path from `F` to the join is *unconditional*: exactly one outgoing edge, on `Done`, and its
  Elsa 4 outcomes are only `Done` (and `Break`, which ends the flowchart in both engines). Else
  `GRAPH-JOIN-UNSUPPORTED`.
- **`WaitAllActive`** and the implicit join always convert.
- **Mode by expression**: `GRAPH-JOIN-UNSUPPORTED`. Mode literal is read case-insensitively as the enum name or its
  integer (`WaitAll = 0`, `WaitAllActive = 1`, `WaitAny = 2`, elsa-core `FlowJoinMode.cs`).
- **Rationale**: conservative and decidable; the corpus' `WaitAll` (fork into three single-activity branches) and the
  mutually exclusive `WaitAny` joins pass, the two fork-into-`WaitAny` joins are refused, as agreed.

## R-7 Start node

- `startNodeId` = the target of a Start construct (exactly one `Done` target, else `GRAPH-START-AMBIGUOUS`); else the
  first child marked `canStartWorkflow`; else the first child with no inbound connection; else the first child. Only
  converted nodes count (constructs are gone), so a fork chosen by this order is replaced by its branch targets: one
  target → that node; several → `GRAPH-START-AMBIGUOUS`.
- Always written when the flowchart has children (ValueFlowValidator skips its checks without it).
- Spec 238 adds the run-time trigger step and multi-entry validation; the converter writes the same `startNodeId`
  either way.

## R-8 Back edges

- Forward edges: a depth-first walk from the entry points (the start node, then every other child with no inbound
  connection, in document order); an edge to a node on the current walk stack is a back edge. This mirrors Elsa 4's
  `FlowchartReachabilityAnalyzer` and, with spec 238, its loop classification from each trigger (238 D7).
- A back edge converts only when its target is the start node and the start node has no other inbound connection
  (Elsa 4 rejects a loop target with several inbound connections at run time). Else `GRAPH-LOOP-UNSUPPORTED`.

## R-9 IElsa3NodeConverter after this spec

- `PrepareAsync` no longer defers flowcharts; `Deferred` stays in the contract (empty for every built-in shape) so a
  future graph-level construct can use it. `ConvertNodeAsync` on a flowchart child converts it as before; on a fork,
  join or Start node it fails with `UNSUPPORTED-MODE` (a construct has no node of its own).
- `ELS3-ACTIVITY-GRAPH-CONVERSION-PENDING` remains a constant, documented as no longer raised (never reused).

## R-10 HttpEndpoint projection in a flowchart (spec 236 D-12)

- The `HttpTriggerResponseMode` rewrite inserts the synthesized `set` node into the parent's `Activities` and records
  it as following the endpoint (`Elsa3NodePlan.FollowedBy`). The graph pass moves the endpoint's outbound edges onto
  the synthesized node and adds `endpoint:Done → node`. In a Sequence nothing changes.

## R-11 End placement (FR-011, amends spec 236 D-3)

- `Elsa3TerminalPlacementRewrite`: `End` converts only when its parent is the definition's root flowchart, for both
  workflow kinds. Elsewhere `UNSUPPORTED-MODE` with guidance. `Complete` is unchanged.

## R-12 Test data

- No JTest definition is checked in. Fixtures are test-owned JSON reproducing each corpus shape (SC-002), converted
  against `RealActivityCatalog`, which already scans the Flowchart assembly (`FlowchartActivity` anchor).
- Converted fixtures go through the Flowchart structure handler and `ValueFlowValidator` in tests (SC-004). The
  multi-trigger fixtures' validation test is the H-1 halt point: it is written but marked skipped with the reason
  "needs spec 238 (#2850)" until 238 is on `main`, then enabled.
- SC-006 is run manually against the JTest checkout and recorded on the PR.
