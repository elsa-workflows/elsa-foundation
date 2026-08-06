# BPMN subprocess navigation

Give an activity-bearing BPMN element with a container-bound activity the same slot badges an ordinary
activity node has, so authors can descend into a subprocess and edit its nested process.

Implements the subprocess clause of [ADR-0019](../adr/0019-author-bpmn-element-structure-through-the-shared-graph-canvas.md).
Applies to **both** graph hosts: the workflow designer's BPMN canvas and the Activity Definition graph
editor. `BpmnElementNode` is shared, and since #487/#489 both hosts emit `bpmnElement` nodes.

## Background

`BpmnElementNode` renders no child-slot control, so a subprocess is a dead end on the canvas. Slot
navigation already works for ordinary activity nodes and needs no new mechanism: the ordinary node
reads `WorkflowSlotNavigationContext` and renders a `wf-node-slot-badge` per child slot
(`workflow-editor/graph.tsx`), `GraphAuthoringCanvas` provides the context, and both hosts pass a
`slotNavigation` callback.

The gap is only that `BpmnNodeData` carries no slots and `BpmnElementNode` renders no badges.

### Verified starting state

- `BpmnElementNode.tsx` contains zero `childSlots` / `SlotNavigation` / `onEnterSlot` references; its
  only commit is `ac873533`.
- Both hosts emit `bpmnElement` nodes, so both gain the badges. `useWorkflowCanvas` has always branched
  to `buildBpmnCanvas`; the AD editor's `buildModel` began doing so in #487, and #489 made it a full BPMN
  host (shape palette, element inspector, element-id-addressed delete).

  *This item read the opposite way when the plan was written* — at that point the AD editor called
  `buildCanvas` unconditionally and could not render BPMN elements at all. #487 and #489 landed
  underneath this branch. The design did not need to change, because badges address the bound activity
  node id and the AD host resolves the owner the same way, but the second host is live rather than
  dormant and now carries its own test.
- Cascade delete already works in both hosts: `resolveRemovedActivityNodeIds` (`useWorkflowCanvas.ts`)
  and `collectRemovedGraphNodeIds` (`bpmn/bpmnAdapter.ts`, added by #487) both map an element node to
  `boundActivity.nodeId` and collect the subtree. No delete work is needed.
- The AD host descends via a `scopePath` of node ids and always views `getChildSlots(owner)[0]`, so a
  non-primary slot badge would land on the primary slot. Not reachable: `supportsActivityGraphAuthoring`
  filters multi-slot activities out of that host's palette, so every container it can hold has exactly
  one slot and renders exactly one badge.

### The routing constraint

BPMN canvas node ids are element ids. Both hosts' `slotNavigation` callbacks look up the owner among the
slot activities, so navigation must pass `node.data.boundActivity.nodeId`.

Confirmed by probe against the real adapters:

| Owner id passed | `planSlotNavigation` | `resolveScope` | Result |
| --- | --- | --- | --- |
| `boundActivity.nodeId` | frame on the bound node | nested scope, `mode: "bpmn"` | nested elements + flows render |
| element id | frame on a non-existent child | `null` | **blank canvas under a bogus breadcrumb crumb** |

The wrong id does not no-op. `resolveScopeOwner` returns `null`, `useWorkflowCanvas` clears the node and
edge arrays, and the breadcrumb still shows the frame that was pushed.

## Approach

Populate the slots in the adapter, render them in the node, and let the existing host callbacks do the
rest. No host changes.

### 1. Carry child slots in the BPMN node payload

`bpmn/bpmnAdapter.ts` — add `childSlots: ChildSlot[]` to `BpmnNodeData` and populate it:

- `createBpmnNode` (used by `buildBpmnCanvas`): `getChildSlots(boundActivityNode, catalogByVersion)`
  when an activity is bound, `[]` otherwise. `buildBpmnCanvas` already builds `catalogByVersion` as a
  `Map`, which `getChildSlots` accepts as an `ActivityCatalogLookup`.
- `createBpmnBoundNode` (catalog placement): `getChildSlots(activityNode, catalogItem)`.
- `createBpmnShapeNode` (palette shape): `[]`.

Events and gateways bind no activity, so they get `[]` without an `isActivityBearingElementType` guard.
An unbound task and a bound leaf activity also yield `[]` naturally.

### 2. Extract the slot-badge control

The badge markup in `workflow-editor/graph.tsx` becomes a shared `workflow-editor/NodeSlotBadges.tsx`
used by both node components. It owns:

- the per-node `onEnterSlot` override taking precedence over the context handler, so both nodes keep the
  drag-stability property `workflow-editor/contexts.ts` documents;
- the `nodrag` class and `stopPropagation`, so a badge click navigates without selecting or dragging the
  node;
- the static `"N slots"` fallback when no navigation handler is available.

It must not live in `graph.tsx`: that module already imports `BpmnElementNode`, so putting the shared
control there would create a `bpmn → graph → bpmn` cycle.

### 3. Render badges on activity-bearing elements

`bpmn/BpmnElementNode.tsx` — in the task/subprocess branch of `renderBpmnShape`, render `NodeSlotBadges`
for a bound activity with slots, passing:

- owner id: `bound.nodeId` (**not** `element.elementId`);
- owner label: `element.name?.trim() || bound.label`, so the crumb reads `Verify payment / Activities`
  rather than `BPMN Process / Activities`. The host's `slotNavigation` wrapper applies `slotCrumbLabel`,
  so pass the raw label.

`renderBpmnShape` currently takes only `nodeData`; it needs the slots from the same payload, which it
already receives.

## Test strategy

Unit tests live in `src/Elsa.Studio.Workflows/Client/src/__tests__/`.

**`bpmnAdapter.test.ts`** (extend)

- `buildBpmnCanvas` populates `childSlots` for a subprocess bound to a container activity.
- `childSlots` is empty for a start event, a gateway, an unbound task, and a task bound to a leaf.
- `createBpmnBoundNode` populates `childSlots` for a container catalog item; `createBpmnShapeNode`
  leaves it empty.

**`bpmnElementNode.test.tsx`** (new)

- A subprocess renders one `wf-node-slot-badge` per slot; a start event and a gateway render none.
- Clicking a badge calls the `WorkflowSlotNavigationContext` handler with the **bound activity node id**,
  and the assertion states explicitly that it is not the element id.
- A per-node `data.onEnterSlot` takes precedence over the context handler.
- Without a handler, the static `"N slots"` badge renders instead of buttons.

**Routing regression** (in `bpmnAdapter.test.ts`, adapter-level)

Pin the constraint that makes this feature silently break, as a table over both ids:

- from `boundActivity.nodeId`: `planSlotNavigation` → `resolveScope` yields a scope whose slot mode is
  `bpmn`, and `buildBpmnCanvas` on it renders the nested elements and flows;
- from the element id: `resolveScope` returns `null`.

Also cover a subprocess bound to a *flowchart* activity, which resolves to a `flowchart` scope — the
descend-through policy does not apply because the slot's cardinality is `many`.

**Browser** (`tests/browser/`) — considered and deliberately not built

The original plan called for a designer spec covering enter-subprocess → breadcrumb shows the element
name → nested canvas renders → breadcrumb back. It was dropped once the cost was known, and this section
records why rather than leaving it as unfinished scope.

`tests/browser/` has no WorkflowEditor fixture at all — every existing spec targets another surface, and
the one graph fixture (`activity-definition-graph-authoring.spec.ts`) mounts the AD host, which cannot
render BPMN element nodes at all. That fixture is doubly unsuitable: its BPMN catalog entry is a
placeholder `kind: "Bpmn"` with `{activities, connections}` rather than a real `elsa.bpmn.structure`
payload. Covering this change therefore means standing up a new fixture mode around `WorkflowEditor`
(1219 lines) with mocked draft, catalog, descriptor, availability and publication endpoints — an
infrastructure change in its own right, not a step in this one.

What such a spec would uniquely add over the unit tests is proof that a badge click survives React Flow's
pane and drag handling. That path is already exercised in production: `wf-node-slot-badge` with `nodrag`
ships today on the ordinary activity node, through the same canvas and the same `NodeSlotBadges` control
this change reuses verbatim rather than reimplementing. The genuinely new surface is the CSS placement
inside `.wf-bpmn-task-copy` and the ⊞ marker clearance, which was verified by rendering the real markup
against the real stylesheet: one badge and two badges both clear the marker, and the unchanged cases
(plain task, badge-less subprocess, ordinary activity node) are visually identical.

Revisit when a WorkflowEditor browser fixture exists for other reasons — at that point this spec is cheap
and worth adding.

## Verification

```
pnpm typecheck
npx eslint src/Elsa.Studio.Workflows/Client/src
pnpm --filter @elsa-workflows/studio-workflows test
pnpm check:bundle:workflows
```

The three `module.test.tsx` failures (folder-tree focus, popstate, unsupported-root-node) are pre-existing
on `main`. A fourth folder-tree test flakes under parallel load. Confirm against a clean tree before
attributing any failure to this change.

Rebased onto `6f42a763`: `3 failed | 1125 passed (1128)` across 106 files — the same three failures, plus
17 new tests (5 adapter, 11 `bpmnElementNode.test.tsx`, 1 AD-host descend case). `stylelint` and the
bundle budgets pass; the primary JavaScript entry sits at 125.37 kB against a 125.70 kB budget, so it is
worth re-checking whenever this area grows.

(An earlier figure in this doc read `1090 passed (1093)` against a `1074 (1077)` baseline. That was
measured before rebasing onto #487/#489, which added tests of their own.)

The CSS is not covered by the automated suite (see the browser-test decision above); it was verified by
rendering the node markup against the real stylesheet. Re-do that check by hand if the BPMN task shape or
the slot-badge styling changes.

## Risks

- **Wrong id routing** is the one real trap, and it fails as a blank canvas rather than an error. The
  routing regression test above is the guard. Verified by mutation: flipping `bound.nodeId` to
  `element.elementId` fails exactly one test, with the two ids named in the failure message.
- **Shared component, two live hosts.** `BpmnElementNode` is registered in the `nodeTypes` both hosts use
  and both now emit `bpmnElement` nodes, so the badges ship to the workflow designer and the Activity
  Definition graph editor together. Each host has its own routing test — `bpmnElementNode.test.tsx` and
  the descend case in `activityGraphImplementationEditor.test.tsx` — and both were mutation-verified.
- **Badge crowding** on small BPMN task shapes — the existing `wf-node-slot-badge` styling targets the
  larger `wf-node` frame. Resolved: badges are scoped to `.wf-bpmn-task-copy` so they drop the ordinary
  node's icon-column indent, and `.wf-bpmn-task:has(…)` adds a bottom band so they clear the centred ⊞
  marker. Confirmed visually against the real stylesheet for one and two badges, with the plain task,
  badge-less subprocess and ordinary activity node unchanged.

## Out of scope

**Resolved while this branch was open.** When the plan was written the AD graph editor was not a BPMN
host: it rendered BPMN scopes through `buildCanvas` (no elements, no flows, no edges) and wrote them back
through `syncCanvasToScope`, which updated `payload.activities` while leaving `elements` stale, so
activities added there were silently destroyed by the next BPMN-aware save — even though BPMN is offered
at AD creation per ADR-0012. That was filed as #488 and deliberately excluded here.

#487 bound the AD editor to the BPMN adapters and #489 completed it into a full BPMN host, satisfying
ADR-0019. #488 is closed. This plan's only remaining interaction with that host is that it is now a live
second consumer of the slot badges, covered above.

Still open and unrelated: #491 — `useWorkflowCanvas.commitCanvas` applies `removedNodeIds` to
`activityPresentation` but never to `layout`, so a delete strands a layout record in all three workflow
designer modes.
