# Feature Specification: Elsa 3 Flowchart connections — convert Elsa 3 flowcharts to Elsa 4 graphs

**Feature Branch**: `2705-elsa3-flowchart-connections` (on `main`; spec 236 merged as #2805, 2026-10-15)

**Created**: 2026-10-16

**Status**: Draft

**Issue**: [#2705](https://github.com/elsa-workflows/elsa-foundation/issues/2705)

**Depends on**:
- Spec 236 (#2706, PR #2805): the activity map, the converter and `IElsa3NodeConverter`.
- Spec 238 (#2850, Flowchart starts at the trigger node): an Elsa 4 change that makes a multi-trigger flowchart start
  at the trigger that fired, and makes the design-time value-flow check treat every direct child with no inbound
  connection as an entry point.

**Dependency boundary (halt points). Both cleared 2026-10-16:** spec 238 merged as #2856 (`6b3ad8c3d`); this branch is rebased onto it and the multi-trigger validation test passes.
**Dependency boundary (halt points).** Spec, plan, tasks and the converter itself do not depend on spec 238: the
converted output is the same with or without it. Exactly two things do, and work halts there until 238 is on `main`:
- **H-1**: the value-flow validation proof of SC-004 for the multi-trigger fixtures (User Story 3). Without 238's
  validator change, a producer on the second trigger's branch reads as unreachable. Every other SC-004 fixture is
  proven without 238.
- **H-2**: marking the PR ready for review and merging. A converted multi-trigger flowchart runs correctly only with
  238's runtime change.
Spec 236 (#2805) is a base, not a halt point: it is merged, and this branch sits on `main`. Spec 236's own deferral to #2796 (publishing
imported reusables) is unchanged and is not a halt point for this spec.

**Tracking**: ADO 47607. Prerequisite of spec 235 (Elsa3JsonReconciliation, draft PR #2708, issue #2707), together
with #2706 and #2717.

**Input**: "Elsa 3 converter: import Flowchart connections" (#2705). The issue is input; the decisions below come from
a review session with the maintainer on 2026-10-15 and 2026-10-16.

## Context (verified 2026-10-16 against `origin/236-elsa3-activity-map` 544482427 and elsa-core 5b0c2d73; line numbers drift)

- **Today.** Every Elsa 3 `Flowchart`, `FlowFork`, `FlowJoin` and `Start` reports `ELS3-ACTIVITY-GRAPH-CONVERSION-PENDING`
  (map entries `GraphLevel(...)`, `Elsa3BuiltInActivityMap.Entries.cs`). The children of a flowchart are still
  normalized and lowered. `IElsa3NodeConverter.PrepareAsync` prepares a whole definition, lists each graph-level node
  in `Deferred`, and `ConvertNodeAsync` converts one prepared child. The map already carries outcome data
  (`Elsa3OutcomeRule`: If `Done` → `True`, `False`; Switch `Done` → each match and `Default`; DispatchWorkflow `Done`
  → `Dispatched`, `Completed`; HttpEndpoint file and size outcomes unsupported).
- **Elsa 3 flowchart semantics** (default counter-based mode; token mode is opt-in through run options and appears in
  no JSON we convert):
  - A connection is `{source: {activity, port}, target: {activity, port}, vertices}`. Only the source port routes;
    the target port (`In`) and `vertices` have no behaviour.
  - Start: the trigger activity when the run was triggered and it is a direct child; else the `Start` property
    (never serialized); else the first `Start` activity; else the first child that can start the workflow; else the
    first child with no inbound connection; else the first child.
  - A node with several inbound connections waits until every forward inbound connection was visited, then runs if
    at least one was followed and is skipped otherwise; skipping propagates (dead-path elimination). This is
    `WaitAllActive`.
  - `FlowFork` completes with all its branch names as outcomes (`Done` when none). `FlowJoin.Mode` is `WaitAny`
    (default: the first followed arrival runs the join and cancels still-running inbound ancestors), `WaitAll` (runs
    only when every inbound connection was followed, otherwise skipped) or `WaitAllActive`.
  - `If` and `Switch` with embedded branches complete with `Done` only; `FlowDecision` completes with `True`/`False`;
    `FlowSwitch` with its case labels or `Default`.
  - A back connection is allowed when its target lies on every forward path from the root to its source; it
    reschedules the target unconditionally.
  - `End` and `Break` are terminal nodes: the enclosing flowchart completes at once. `Complete` bubbles past
    flowcharts to the workflow or reusable workflow.
- **Elsa 4 flowchart** (`Essentials/src/Activities/Flowchart`):
  - Structure `elsa.flowchart.structure` 1.0.0 with `activities`, `variables`, `connections`
    (`{id?, source: {nodeId, port}, target: {nodeId, port}}`), `startNodeId`, `nodeMetadata`, `connectionMetadata`.
  - Start: `startNodeId`, else the first child with no inbound connection, else the first child. Spec 238 adds: the
    trigger node, when it is a direct child.
  - Routing follows every connection whose source port is among the completed node's outcomes. Several connections on
    one port fan out in parallel.
  - A node with several inbound connections is an implicit join. With the default host option (`deadPathJoin`) it
    waits until every inbound connection has a live or dead arrival and runs when at least one is live: the same as
    Elsa 3 `WaitAllActive`.
  - No FlowFork, FlowJoin or Start activity exists. `If` completes with `True`/`False` after its branch; `Switch` with
    the matched case or `Default`. `Done` is not universal.
  - A loop whose target has more than one inbound connection is rejected at run time (`FlowchartReachabilityAnalyzer`);
    loops back to the start node are supported. One completion whose connections hit the same target twice is
    rejected at run time.
  - The design-time value-flow check uses `startNodeId` for reachability and skips the checks when it is absent.
- **Corpus** (JTest, 36 Elsa 3 definitions, used as evidence; not checked in): 49 flowcharts (36 root, 13 nested with
  one child and no connections), 245 connections, all target ports `In`, no vertices, no unknown ids, no cycles, no
  `Start`, 4 FlowForks, 7 FlowJoins (5 `WaitAny`, 1 `WaitAll`, 1 `WaitAllActive`), 2 implicit joins, 43 start-capable
  nodes all directly in root flowcharts, 2 flowcharts with several triggers, 1 `Complete` (root flowchart of a
  reusable workflow).

## Decisions (maintainer, 2026-10-15/16)

| # | Decision |
|---|---|
| G-1 | **Faithful or refuse.** What converts behaves as in Elsa 3. What cannot be expressed exactly is refused with a stable code and guidance on how to change the Elsa 3 workflow. No complex or risky workaround in the converter. |
| G-2 | **Idiomatic Elsa 4, no placeholders.** Elsa 3 concepts Elsa 4 does not need disappear: `FlowFork` becomes fan-out connections, `FlowJoin` becomes an implicit join, `Start` becomes `startNodeId`. No empty or stand-in nodes. |
| G-3 | **Several start triggers convert 1:1.** Elsa 4 runs a multi-trigger flowchart correctly once spec 238 is in. No per-trigger split. |
| G-4 | **`WaitAny`** converts when its inbound branches are mutually exclusive. When they can run concurrently it is refused (`ELS3-ACTIVITY-GRAPH-JOIN-UNSUPPORTED`): Elsa 4 has no join that cancels the losing branches. |
| G-5 | **`WaitAll`** converts when every inbound path provably runs. Otherwise it is refused (`ELS3-ACTIVITY-GRAPH-JOIN-UNSUPPORTED`). |
| G-6 | **Implicit joins and `WaitAllActive`** convert to Elsa 4 implicit joins. |
| G-7 | **Outbound connections of terminal nodes** (`Complete`, `End`, `Break`) and of FlowFork ports outside its branches never fire in Elsa 3. They are removed with a warning, never silently. |
| G-8 | **Start triggers follow spec 238's start-path rule** (Joey, 2026-10-16, after #2856 merged). A start-capable flowchart child that another activity leads into and that is not the start node is refused at conversion (`ELS3-ACTIVITY-GRAPH-START-TRIGGER-MISPLACED`), not left to fail at publish. Placement through nested containers stays with 238's publish preflight. |

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A flowchart converts with its connections (Priority: P1)

A team migrating to Elsa 4 converts an Elsa 3 workflow whose root is a flowchart of ordinary activities joined by
connections, the shape of almost every real Elsa 3 workflow. The result is an Elsa 4 flowchart whose nodes are the
converted children and whose connections route the same outcomes to the same nodes.

**Why this priority**: 34 of 36 corpus definitions have connections; without this almost nothing converts.

**Independent Test**: Convert a definition whose root flowchart chains an HttpEndpoint, a FlowDecision with `True`
and `False` branches, WriteLines and a WriteHttpResponse, against the real activity catalog; the converted flowchart
holds every node and one connection per Elsa 3 connection, with Elsa 4 port names.

**Acceptance Scenarios**:

1. **Given** a flowchart with connections, **When** it is converted, **Then** no
   `ELS3-ACTIVITY-GRAPH-CONVERSION-PENDING` is reported, the Elsa 4 flowchart holds every converted child with its
   Elsa 3 node id, and each connection keeps its source node, target node and source port (mapped to the Elsa 4
   outcome name).
2. **Given** a connection from an Elsa 3 `If` or `Switch` on `Done`, **When** it is converted, **Then** it becomes
   one connection per Elsa 4 outcome the map names (`True` and `False`; each case match and `Default`) to the same
   target.
3. **Given** two Elsa 3 connections that map to the same source, port and target, **When** converted, **Then** one
   Elsa 4 connection remains.
4. **Given** target ports (`In`) and `vertices`, **When** converted, **Then** they are dropped without a diagnostic
   (no behaviour).
5. **Given** a flowchart whose start resolves by Elsa 3's rules to node N, **When** converted, **Then** the Elsa 4
   flowchart's `startNodeId` is N, always set explicitly.
6. **Given** a nested flowchart (inside an If branch, a loop body, a Switch case), **When** converted, **Then** it is
   converted the same way, inside its parent's native structure.
7. **Given** a flowchart variable, **When** converted, **Then** it is a scoped variable of the Elsa 4 flowchart.
8. **Given** an HttpEndpoint inside a flowchart whose captured `Headers` or `QueryStringData` needs the synthesized
   `set` node (spec 236 D-11 to D-13), **When** converted, **Then** the node is placed after the endpoint: the
   endpoint's `Done` connects to it and the endpoint's former outbound connections leave from it.

---

### User Story 2 - Forks and joins become Elsa 4 fan-out and implicit joins (Priority: P1)

A migrator converts flowcharts that branch in parallel with FlowFork and come back together with FlowJoin or an
implicit join. Elsa 4 expresses both natively, so the result has no fork or join node, and runs the same.

**Why this priority**: 4 forks and 9 joins in the corpus; equal to P1 because a converted flowchart with a wrong join
fails silently at run time.

**Independent Test**: Convert a fork into three branches that meet at a `WaitAll` join; the converted flowchart has no
fork or join node, the fork's predecessor connects to all three branch heads, and the three branch tails connect to
the join's successor.

**Acceptance Scenarios**:

1. **Given** `A → FlowFork(b1, b2) → {X on b1, Y on b2}`, **When** converted, **Then** the fork is gone and A's
   connection on its port leads to both X and Y.
2. **Given** `{X, Y} → FlowJoin → Z`, **When** converted with an accepted mode, **Then** the join is gone and X and Y
   connect to Z on the ports that led to the join.
3. **Given** an implicit join (a node with several inbound connections, no FlowJoin) or a `WaitAllActive` join,
   **When** converted, **Then** it converts (G-6).
4. **Given** a `WaitAny` join whose inbound paths are mutually exclusive (different outcomes of one decision, or
   branches started by different triggers), **When** converted, **Then** it converts (G-4).
5. **Given** a `WaitAny` join whose inbound paths can run at the same time (after a FlowFork or any other fan-out),
   **When** converted, **Then** the result fails with `ELS3-ACTIVITY-GRAPH-JOIN-UNSUPPORTED` naming the join, with
   guidance to use `WaitAll`/`WaitAllActive` or remove the parallel branches.
6. **Given** a `WaitAll` join whose every inbound path provably runs (each path from a common fan-out to the join
   passes only nodes with one outgoing connection on an outcome they always emit), **When** converted, **Then** it
   converts. **Given** a `WaitAll` join where some inbound path may not run, **Then** the result fails with
   `ELS3-ACTIVITY-GRAPH-JOIN-UNSUPPORTED` (G-5).
7. **Given** a FlowFork whose branches are set by an expression Elsa 3 evaluates at run time, **When** converted,
   **Then** the result fails with `ELS3-ACTIVITY-UNSUPPORTED-MODE`; a literal list (or an Object expression holding a
   JSON array of strings) converts. A connection from a fork port outside its branches never fires, so it is removed
   with `ELS3-BEHAVIOUR-DEAD-CONNECTION-REMOVED`.
8. **Given** a FlowJoin whose mode is an expression, **When** converted, **Then** the result fails with
   `ELS3-ACTIVITY-GRAPH-JOIN-UNSUPPORTED`.
9. **Given** a `Start` activity, **When** converted, **Then** it is gone and the node it connects to on `Done` is the
   `startNodeId`.
10. **Given** a FlowFork or `Start` that would be the flowchart's start node and leads to more than one node, **When**
    converted, **Then** the result fails with `ELS3-ACTIVITY-GRAPH-START-AMBIGUOUS`, with guidance to put one activity
    first (an Elsa 4 flowchart starts at exactly one node).

---

### User Story 3 - Flowcharts with several triggers convert as one workflow (Priority: P1)

A workflow can be started by several triggers in one flowchart (several HTTP endpoints, an HTTP endpoint and a cron
schedule). It converts to one Elsa 4 workflow that starts at whichever trigger fired.

**Why this priority**: The maintainer expects this shape often in real workflows (G-3).

**Independent Test**: Convert a flowchart with two start-capable HttpEndpoints, each with its own downstream path;
the converted flowchart holds both endpoints and both paths, and its `startNodeId` is the first start-capable child.
With spec 238 in place, each endpoint starts its own path.

**Acceptance Scenarios**:

1. **Given** a root flowchart with several start-capable children, **When** converted, **Then** all of them and their
   paths are kept in one flowchart, and `startNodeId` is the first of them in Elsa 3 order.
2. **Given** two triggers whose paths meet at a `WaitAny` join, **When** converted, **Then** it converts (the paths
   are mutually exclusive: one run is started by one trigger).

---

### User Story 4 - Terminals inside flowcharts behave as in Elsa 3 (Priority: P2)

Complete, End and Break inside flowcharts end what they ended in Elsa 3: Complete ends the workflow or the reusable
workflow, End ends its flowchart, Break ends its flowchart and the nearest loop.

**Why this priority**: The corpus has one Complete (a reusable workflow's root flowchart); the rules exist in spec 236
and only need the graph around them.

**Independent Test**: Convert a reusable workflow whose root flowchart routes to two Complete nodes with outcomes
`succeed` and `fail`; the converted graph holds two graph `Complete` nodes, declares both outcomes and keeps the
connections that lead to them.

**Acceptance Scenarios**:

1. **Given** a `Complete` or `End` in a plain workflow's root flowchart, **When** converted, **Then** it becomes
   `finish` (spec 236 rule) inside the converted flowchart.
2. **Given** a `Complete` at any depth of a reusable workflow, including nested flowcharts, **When** converted,
   **Then** it becomes the graph `Complete` activity (spec 236 D-1 to D-7).
3. **Given** an `End` in a reusable workflow's root flowchart, **When** converted, **Then** it becomes `Complete` with
   outcome `done` (D-3).
4. **Given** an `End` anywhere but the definition's root flowchart, in either kind of workflow, **When** converted,
   **Then** the result fails with `ELS3-ACTIVITY-UNSUPPORTED-MODE`, with guidance to route the flowchart's last
   activity to nothing instead. *(This narrows spec 236 D-3, which converted `End` at any depth of a reusable workflow;
   in Elsa 3 `End` ends only its enclosing flowchart.)*
5. **Given** a `Break` inside a flowchart, **When** converted, **Then** it converts as in spec 236 (Elsa 4's flowchart
   completes with `Break`, which the nearest loop handles).
6. **Given** connections leaving a `Complete`, `End` or `Break`, **When** converted, **Then** they are removed with
   `ELS3-BEHAVIOUR-DEAD-CONNECTION-REMOVED` (G-7).

---

### User Story 5 - Unsupported graph shapes are refused with guidance (Priority: P1)

A migrator converts a flowchart Elsa 4 cannot express exactly: an outcome that does not exist in Elsa 4, a loop shape
Elsa 4 rejects, a connection to a node that is not there. The converter returns a failed result whose diagnostics name
the node or connection and say what to change. Nothing throws; nothing is dropped silently.

**Why this priority**: Spec 235 skips and reports by code, and the maintainer values this feedback loop (G-1).

**Independent Test**: One definition per cause; each fails with exactly the expected code and location.

**Acceptance Scenarios**:

1. **Given** a connection whose source or target id is not a child of the flowchart, **When** converted, **Then**
   `ELS3-ACTIVITY-GRAPH-CONNECTION-INVALID` names the connection.
2. **Given** a connection on a port the map marks unsupported (HttpEndpoint `File too large`, …), **When** converted,
   **Then** `ELS3-ACTIVITY-NO-COUNTERPART` names the connection and the reason (member-map R6).
3. **Given** a connection on a port that is neither mapped nor an outcome the Elsa 4 activity declares (including a
   reusable reference's contract outcomes and dynamic outcomes such as status codes), **When** converted, **Then**
   `ELS3-ACTIVITY-GRAPH-OUTCOME-UNMAPPED` names the connection and the port.
4. **Given** a back connection (a loop), **When** converted, **Then** it converts only when it returns to the start
   node and the start node has no other inbound connection; otherwise `ELS3-ACTIVITY-GRAPH-LOOP-UNSUPPORTED`, with
   guidance to use a While, For or ForEach loop.
5. **Given** a back connection into a FlowFork or FlowJoin, **When** converted, **Then**
   `ELS3-ACTIVITY-GRAPH-LOOP-UNSUPPORTED`.
6. **Given** `connections` on an Elsa 3 container that is not a flowchart, **When** converted, **Then**
   `ELS3-ACTIVITY-GRAPH-CONNECTION-INVALID`.
7. **Given** several causes in one definition, **When** converted, **Then** every cause is reported in deterministic
   order.
8. **Given** a start-capable child (`canStartWorkflow`) that another activity leads into and that is not the start
   node, **When** converted, **Then** `ELS3-ACTIVITY-GRAPH-START-TRIGGER-MISPLACED` names it, with guidance to move it to
   an entry or make it a mid-flow wait (G-8).

### Edge Cases

- A flowchart with no children converts to an empty Elsa 4 flowchart with no `startNodeId`.
- A flowchart with children and no connections converts; `startNodeId` follows the Elsa 3 order. Nodes no path
  reaches are kept (they did not run in Elsa 3 either).
- A child that is itself refused fails the definition; its connections are not reported again.
- A fork that leads straight into a join, or a join straight into another join, collapses through both rules.
- A fan-out node with two connections on one port to the same target (after collapsing) leaves one connection.
- A `WaitAny` join reached by the same branch twice through a collapsed fork: the branches are concurrent, so G-4
  applies.
- A FlowDecision used as the start node with `canStartWorkflow` converts like any start node.
- The host's join option: conversion assumes Elsa 4's default `deadPathJoin`. A host configured with
  `reachabilityJoin` changes implicit-join timing for every flowchart, converted or not; this is documented, not
  diagnosed.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: An Elsa 3 `Flowchart` MUST convert to the Elsa 4 Flowchart activity (catalog version resolved like any
  CLR activity) with its children in `activities`, its variables as scoped variables, its connections in
  `connections`, and an explicit `startNodeId` whenever it has children. `ELS3-ACTIVITY-GRAPH-CONVERSION-PENDING` MUST
  no longer be raised; the constant stays as a never-reused code.
- **FR-002**: Each child MUST be converted by the existing node pipeline (spec 236), sharing the definition-wide
  lowering of `IElsa3NodeConverter.PrepareAsync`, so value flow across flowchart siblings resolves as on the
  whole-definition path.
- **FR-003**: Each Elsa 3 connection MUST map to Elsa 4 connections by its source node's outcome rule: a map
  `Elsa3OutcomeRule` first (one connection per Elsa 4 outcome; an unsupported rule → `NO-COUNTERPART`); otherwise the
  port verbatim when the Elsa 4 activity declares that outcome (static, dynamic or a reusable reference's contract
  outcome); otherwise `Done` when the port is `Done` and the Elsa 4 activity declares no outcomes; otherwise
  `GRAPH-OUTCOME-UNMAPPED`. Target ports and `vertices` MUST be dropped (R9). Identical resulting connections MUST be
  merged.
- **FR-004**: `startNodeId` MUST follow Elsa 3's order without the run-time trigger step: the target of a `Start`
  activity, the first start-capable child, the first child with no inbound connection, the first child.
- **FR-005**: A `FlowFork` MUST be removed: each connection into the fork is replaced by connections from the same
  source and port to the target of every fork connection whose port is one of the fork's branches (or `Done` when the
  fork has no branches). Fork connections on other ports MUST be removed with `ELS3-BEHAVIOUR-DEAD-CONNECTION-REMOVED`.
  Branches MUST be readable without evaluating JavaScript, else `UNSUPPORTED-MODE`.
- **FR-006**: A `FlowJoin` MUST be removed after its mode check: each connection into the join is replaced by
  connections from the same source and port to every target of the join's outbound connections. Mode `WaitAllActive`
  always converts; `WaitAny` (the default) converts only when no two inbound paths can run at the same time (G-4);
  `WaitAll` converts only when every inbound path provably runs (G-5); a mode set by expression, or a failed check,
  → `GRAPH-JOIN-UNSUPPORTED`.
- **FR-007**: A `Start` activity MUST be removed and its single `Done` target used as `startNodeId`.
- **FR-008**: A FlowFork or `Start` that would be the start node and leads to more than one node MUST fail with
  `GRAPH-START-AMBIGUOUS`.
- **FR-009**: A back connection MUST convert only when it targets the start node and the start node has no other
  inbound connection; otherwise, and for any back connection into a FlowFork or FlowJoin, `GRAPH-LOOP-UNSUPPORTED`.
- **FR-010**: Connections leaving a `Complete`, `End` or `Break` node MUST be removed with
  `DEAD-CONNECTION-REMOVED`.
- **FR-011**: `End` MUST convert only in the definition's root flowchart (plain: `finish`; reusable: `Complete` with
  `done`); elsewhere `UNSUPPORTED-MODE`. This amends spec 236 D-3 for reusable workflows. `Complete` keeps spec 236's
  rules; in a plain workflow it still converts only in the root flowchart.
- **FR-012**: A connection whose source or target is not a child of the same flowchart, or `connections` on a
  non-flowchart container, MUST fail with `GRAPH-CONNECTION-INVALID`. Two activities of one flowchart that share an id
  MUST fail with `GRAPH-DUPLICATE-NODE-ID`, whether or not a connection names the id.
- **FR-013**: The HttpEndpoint projection node of spec 236 D-12 MUST be placed in a flowchart by connection: endpoint
  `Done` → projection node, and the endpoint's outbound connections leave from the projection node.
- **FR-013a**: A start-capable direct child of a flowchart that has an inbound connection and is not the start node
  MUST fail with `GRAPH-START-TRIGGER-MISPLACED` (G-8, spec 238 D8).
- **FR-014**: New codes MUST be public constants in `Elsa3ActivityDiagnosticCodes`, never renamed or reused:
  errors `ELS3-ACTIVITY-GRAPH-CONNECTION-INVALID`, `ELS3-ACTIVITY-GRAPH-OUTCOME-UNMAPPED`,
  `ELS3-ACTIVITY-GRAPH-JOIN-UNSUPPORTED`, `ELS3-ACTIVITY-GRAPH-LOOP-UNSUPPORTED`,
  `ELS3-ACTIVITY-GRAPH-START-AMBIGUOUS`, `ELS3-ACTIVITY-GRAPH-START-TRIGGER-MISPLACED`, `ELS3-ACTIVITY-GRAPH-DUPLICATE-NODE-ID`; warning `ELS3-BEHAVIOUR-DEAD-CONNECTION-REMOVED`. Each diagnostic MUST carry
  the definition, node id (the connection's source for a connection), JSON path and guidance, as spec 236's do.
- **FR-015**: Conversion MUST stay deterministic (same input and catalog → byte-identical output), never throw for
  input or catalog content, and keep the nesting budget (a Flowchart adds 4 levels; connections add none).
- **FR-016**: Both conversion paths (single definition and collection) MUST use this graph conversion; the
  materializer's helpers keep their shape (spec 236 FR-019).
- **FR-017**: The map, member-map.md, the Mapping README, `EXTENSION_POINTS.md` and the diagnostic-code contract MUST
  describe the Flowchart, FlowFork, FlowJoin and Start rules; the "graph conversion pending" wording MUST be removed.

### Key Entities

- **Converted flowchart**: the Elsa 4 Flowchart structure built from an Elsa 3 flowchart: converted children,
  scoped variables, mapped connections, `startNodeId`.
- **Connection mapping**: one Elsa 3 connection → zero or more Elsa 4 connections, by the source's outcome rule and
  the fork/join collapse.
- **Join check**: the static analysis that decides whether a `WaitAny` join's inbound paths are mutually exclusive
  and whether a `WaitAll` join's inbound paths all run.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: No supported flowchart shape reports `ELS3-ACTIVITY-GRAPH-CONVERSION-PENDING`; the code is raised by no
  conversion path.
- **SC-002**: A test-owned fixture per corpus shape (decision tree, implicit join, fork into `WaitAll`, fork into
  `WaitAllActive`, mutually exclusive `WaitAny`, two-trigger flowchart, two triggers into `WaitAny`, nested
  single-child flowcharts, reusable workflow with two Completes, HttpEndpoint projection) converts with 0 Errors
  against the real activity catalog (`RealActivityCatalog`). Conversion of the multi-trigger fixtures needs nothing
  from spec 238.
- **SC-003**: For each new code, and each refusal in User Story 5, a test input produces exactly that code at the
  expected location; 0 exceptions escape either conversion path.
- **SC-004**: Converted fixtures pass Elsa 4's flowchart structure handling and design-time value-flow validation
  (start node set, no unknown endpoints, no duplicate connection hit, no rejected loop shape). The multi-trigger
  fixtures' value-flow validation needed spec 238 (H-1, cleared: it passes on main with #2856).
- **SC-005**: Converting the same input twice yields byte-identical output.
- **SC-006**: Run against the 36 JTest definitions (outside the repo, recorded in the PR), no definition reports
  `GRAPH-CONVERSION-PENDING`; every refusal is one of the documented codes. Expected graph refusals:
  `branching-fork-parallel` and `branching-wait-any` (concurrent `WaitAny`), and `branching-wait-all` (a `WaitAll`
  join reached by mutually exclusive paths, which Elsa 3 always skips; found by the 2026-10-16 corpus run).

## Assumptions

- Spec 238 is needed at run time and for design-time validation of multi-trigger flowcharts (halt points H-1 and H-2),
  not at conversion time, so the converter does not detect it.
- The Elsa 4 host uses the default `deadPathJoin` join option.
- Elsa 3 token-based flowchart mode (`mergeMode`, `UseTokenFlow`) is out of scope: it is set through run options or
  code, never in the definition JSON, and appears nowhere in the corpus. A `mergeMode` custom property, if present, is
  handled by spec 236's member rules.
- Elsa 3's flat and legacy connection JSON forms are a deserialization concern of the Elsa 3 DTO, not of this spec.
- Studio layout (positions) is out of scope, as in spec 236.
- `Complete` in a plain workflow outside the root flowchart stays refused (spec 236); Elsa 3 would end the whole
  workflow there, so widening it to `finish` is a possible later change, not part of this spec.
