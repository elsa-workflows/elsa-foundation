import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useWorkflowCanvas } from "../workflow-editor/useWorkflowCanvas";
import { forgetRemovedNodes, type WorkflowDraftRecipe } from "../workflow-editor/workflowDocument";
import { resolveScope } from "../workflowAdapter";
import { bpmnStructureKind } from "../bpmn/bpmnTypes";
import { flowchartStructureKind } from "../flowchartStartNode";
import type { ActivityCatalogItem, ActivityNode, WorkflowDraft } from "../workflowTypes";

const mounted: Array<{ root: Root; container: HTMLElement }> = [];

afterEach(() => {
  for (const item of mounted.splice(0)) {
    flushSync(() => item.root.unmount());
    item.container.remove();
  }
});

describe("useWorkflowCanvas BPMN placement", () => {
  // A BPMN canvas renders from the process ELEMENTS, not from the slot activities. An activity that
  // lands in the slot without a bound element is invisible and gets dropped by the next canvas sync,
  // so every catalog placement in a BPMN scope must stamp an element too.
  it("binds a palette placement to a new BPMN element", () => {
    const harness = renderBpmnCanvas();

    const added = harness.addActivity(writeLineCatalogItem());

    expect(added).not.toBeNull();
    const elements = readElements(harness.committedDraft());
    expect(elements).toHaveLength(2);
    const placed = elements.find(element => element.elementId !== "start");
    expect(placed?.elementType).toBe("task");
    expect(placed?.childNodeId).toBe(added!.nodeId);
  });

  it("keeps the placed activity in the slot alongside its element", () => {
    const harness = renderBpmnCanvas();

    const added = harness.addActivity(writeLineCatalogItem());

    expect(readActivities(harness.committedDraft()).map(activity => activity.nodeId)).toContain(added!.nodeId);
  });

  // React Flow settles a drag by calling updateNodePositions and onNodeDragStop back to back in one
  // synchronous block, so the hook's `nodes` closure still holds the pre-drag position. The dragged
  // node React Flow passes in is the authority.
  it("persists the dragged node's final position rather than the mirror's", () => {
    const harness = renderBpmnCanvas();
    const added = harness.addActivity(writeLineCatalogItem(), { x: 100, y: 100 });

    harness.dragStop({ id: elementIdFor(harness.committedDraft(), added!.nodeId), position: { x: 640, y: 480 } });

    const layout = harness.committedDraft().layout.find(record => record.nodeId !== "start");
    expect(layout).toMatchObject({ x: 640, y: 480 });
  });

  it("persists every node of a multi-selection drag", () => {
    const harness = renderBpmnCanvas();
    const added = harness.addActivity(writeLineCatalogItem(), { x: 100, y: 100 });
    const elementId = elementIdFor(harness.committedDraft(), added!.nodeId);

    harness.dragStop(
      { id: elementId, position: { x: 10, y: 20 } },
      [
        { id: elementId, position: { x: 10, y: 20 } },
        { id: "start", position: { x: 30, y: 40 } }
      ]
    );

    const layout = harness.committedDraft().layout;
    expect(layout.find(record => record.nodeId === elementId)).toMatchObject({ x: 10, y: 20 });
    expect(layout.find(record => record.nodeId === "start")).toMatchObject({ x: 30, y: 40 });
  });

  it("honours the drop position for a BPMN placement", () => {
    const harness = renderBpmnCanvas();

    const added = harness.addActivity(writeLineCatalogItem(), { x: 512, y: 384 });

    const layout = harness.committedDraft().layout.find(record => record.nodeId !== "start");
    expect(layout).toMatchObject({ x: 512, y: 384 });
    expect(added).not.toBeNull();
  });
});

// updateLayout only ever upserts, so a delete that does not prune leaves the departed node's position
// in the saved draft forever — it grows on every removal and is re-applied if the id is ever reused.
describe("useWorkflowCanvas side tables of deleted nodes", () => {
  it("drops the layout and presentation records of a deleted flowchart activity", () => {
    const harness = renderFlowchartCanvas();

    harness.deleteNode("node-a");

    const draft = harness.committedDraft();
    expect(draft.layout.map(record => record.nodeId)).not.toContain("node-a");
    expect((draft.activityPresentation ?? []).map(record => record.nodeId)).not.toContain("node-a");
  });

  it("drops the layout record of a deleted BPMN element, which layout keys by element id", () => {
    const harness = renderBpmnCanvas();
    const added = harness.addActivity(writeLineCatalogItem(), { x: 300, y: 200 });
    const elementId = elementIdFor(harness.committedDraft(), added!.nodeId);
    expect(harness.committedDraft().layout.map(record => record.nodeId)).toContain(elementId);

    harness.deleteNode(elementId);

    const draft = harness.committedDraft();
    expect(draft.layout.map(record => record.nodeId)).toEqual(["start"]);
    // The bound activity leaves with its element, so its record must go too.
    expect(draft.layout.map(record => record.nodeId)).not.toContain(added!.nodeId);
  });

  it("keeps the surviving nodes' records", () => {
    const harness = renderBpmnCanvas();
    const added = harness.addActivity(writeLineCatalogItem(), { x: 300, y: 200 });

    harness.deleteNode(elementIdFor(harness.committedDraft(), added!.nodeId));

    expect(harness.committedDraft().layout.find(record => record.nodeId === "start")).toMatchObject({ x: 0, y: 0 });
  });

  // Overwriting a single-cardinality slot evicts its occupant without any canvas delete, so it is the
  // other way a node leaves the document. The evicted activity here owns a BPMN scope, whose elements
  // are keyed by element id — an eviction that expands only the activity walk strands one record per
  // nested element.
  it("drops the side tables of an activity displaced from a single-cardinality slot", () => {
    const harness = renderSingleSlotCanvas();

    harness.addActivity(writeLineCatalogItem(), { x: 400, y: 300 });

    const draft = harness.committedDraft();
    expect(draft.layout.map(record => record.nodeId)).not.toContain("displaced-process");
    expect(draft.layout.map(record => record.nodeId)).not.toContain("nested-activity");
    expect(draft.layout.map(record => record.nodeId)).not.toContain("nested-element");
    expect(draft.layout.map(record => record.nodeId)).toContain("unrelated");
    expect((draft.activityPresentation ?? []).map(record => record.nodeId)).not.toContain("displaced-process");
  });
});

describe("forgetRemovedNodes", () => {
  const draft = {
    layout: [{ nodeId: "keep", x: 1, y: 2 }, { nodeId: "drop", x: 3, y: 4 }],
    activityPresentation: [{ nodeId: "keep", displayName: "Keep" }, { nodeId: "drop", displayName: "Drop" }]
  } as unknown as WorkflowDraft;

  it("prunes both side tables in one step", () => {
    expect(forgetRemovedNodes(draft, ["drop"])).toEqual({
      layout: [{ nodeId: "keep", x: 1, y: 2 }],
      activityPresentation: [{ nodeId: "keep", displayName: "Keep" }]
    });
  });

  it("returns the existing records untouched when nothing was removed", () => {
    const forgotten = forgetRemovedNodes(draft, []);

    expect(forgotten.layout).toBe(draft.layout);
    expect(forgotten.activityPresentation).toBe(draft.activityPresentation);
  });

  it("substitutes an empty presentation table for a draft that has none", () => {
    expect(forgetRemovedNodes({ layout: [] } as unknown as WorkflowDraft, ["gone"]).activityPresentation).toEqual([]);
  });
});

function renderBpmnCanvas() {
  return renderCanvas({
    draft: bpmnDraft(),
    catalog: [bpmnCatalogItem(), writeLineCatalogItem()],
    isBpmnDesigner: true
  });
}

function renderFlowchartCanvas() {
  return renderCanvas({
    draft: flowchartDraft(),
    catalog: [flowchartCatalogItem(), writeLineCatalogItem()],
    isBpmnDesigner: false
  });
}

function renderSingleSlotCanvas() {
  return renderCanvas({
    draft: singleSlotDraft(),
    catalog: [containerCatalogItem(), bpmnCatalogItem(), writeLineCatalogItem()],
    isBpmnDesigner: false
  });
}

function renderCanvas({ draft: initialDraft, catalog, isBpmnDesigner }: {
  draft: WorkflowDraft;
  catalog: ActivityCatalogItem[];
  isBpmnDesigner: boolean;
}) {
  const catalogByVersion = new Map(catalog.map(item => [item.activityVersionId, item]));
  let draft = initialDraft;
  const commits: WorkflowDraft[] = [];

  const applyRecipe = (recipe: WorkflowDraftRecipe) => {
    const next = recipe({ draft, frames: [], selectedNodeId: null, testRun: null, publishedArtifactId: null });
    if (!next) return;
    draft = next;
    commits.push(next);
  };

  const api: {
    addActivity?: ReturnType<typeof useWorkflowCanvas>["addActivity"];
    commitLayout?: ReturnType<typeof useWorkflowCanvas>["commitLayout"];
    onNodesDelete?: ReturnType<typeof useWorkflowCanvas>["onNodesDelete"];
    nodes?: ReturnType<typeof useWorkflowCanvas>["nodes"];
  } = {};

  function Harness() {
    const scope = resolveScope(draft.state.rootActivity, [], catalogByVersion);
    const canvas = useWorkflowCanvas({
      draft,
      scope,
      scopeOwner: draft.state.rootActivity ?? null,
      frames: [],
      catalog,
      catalogByVersion,
      isUnsupportedDesigner: false,
      isBpmnDesigner,
      canAddActivitiesToCanvas: true,
      selectedNodeId: null,
      editDraft: applyRecipe,
      editDraftAndSelect: recipe => applyRecipe(recipe),
      select: vi.fn(),
      resetToRoot: vi.fn(),
      setStatus: vi.fn(),
      setError: vi.fn()
    });
    api.addActivity = canvas.addActivity;
    api.commitLayout = canvas.commitLayout;
    api.onNodesDelete = canvas.onNodesDelete;
    api.nodes = canvas.nodes;
    return null;
  }

  const container = document.createElement("div");
  document.body.append(container);
  const root = createRoot(container);
  flushSync(() => root.render(<Harness />));
  // The canvas mirror is populated by an effect; let it settle so `nodes` holds the existing canvas
  // nodes before the test places anything on top of them.
  flushSync(() => root.render(<Harness />));
  mounted.push({ root, container });

  return {
    addActivity: (activity: ActivityCatalogItem, position?: { x: number; y: number }) => {
      let added: ActivityNode | null = null;
      flushSync(() => {
        added = api.addActivity!(activity, position);
      });
      return added as ActivityNode | null;
    },
    // Mirrors React Flow's onNodesDelete: the canvas nodes that just left the graph.
    deleteNode: (nodeId: string) => {
      const node = api.nodes!.find(candidate => candidate.id === nodeId);
      if (!node) throw new Error(`No canvas node ${nodeId}; have ${api.nodes!.map(n => n.id).join(", ")}`);
      flushSync(() => api.onNodesDelete!([node]));
      flushSync(() => root.render(<Harness />));
    },
    // Mirrors React Flow's onNodeDragStop payload: the settled node, plus every node of a
    // multi-selection drag.
    dragStop: (
      node: { id: string; position: { x: number; y: number } },
      draggedNodes?: Array<{ id: string; position: { x: number; y: number } }>
    ) => {
      flushSync(() => {
        api.commitLayout!(
          new MouseEvent("mouseup") as unknown as Parameters<NonNullable<typeof api.commitLayout>>[0],
          node as unknown as Parameters<NonNullable<typeof api.commitLayout>>[1],
          (draggedNodes ?? [node]) as unknown as Parameters<NonNullable<typeof api.commitLayout>>[2]
        );
      });
    },
    committedDraft: () => {
      if (commits.length === 0) throw new Error("No draft was committed");
      return commits[commits.length - 1];
    }
  };
}

function elementIdFor(draft: WorkflowDraft, childNodeId: string) {
  const element = readElements(draft).find(candidate => candidate.childNodeId === childNodeId);
  if (!element) throw new Error(`No BPMN element bound to ${childNodeId}`);
  return element.elementId;
}

function readElements(draft: WorkflowDraft) {
  const payload = draft.state.rootActivity?.structure?.payload as { elements?: unknown } | undefined;
  return (Array.isArray(payload?.elements) ? payload.elements : []) as Array<{
    elementId: string;
    elementType: string;
    childNodeId?: string;
  }>;
}

function readActivities(draft: WorkflowDraft) {
  const payload = draft.state.rootActivity?.structure?.payload as { activities?: unknown } | undefined;
  return (Array.isArray(payload?.activities) ? payload.activities : []) as ActivityNode[];
}

function bpmnDraft(): WorkflowDraft {
  return {
    state: {
      rootActivity: {
        nodeId: "node-bpmn",
        activityVersionId: "bpmn@1",
        inputs: [],
        outputs: [],
        structure: {
          kind: bpmnStructureKind,
          schemaVersion: "1.0.0",
          payload: {
            elements: [{ elementId: "start", elementType: "startEvent" }],
            sequenceFlows: [],
            activities: []
          }
        }
      },
      inputs: [],
      outputs: [],
      variables: []
    },
    layout: [{ nodeId: "start", x: 0, y: 0 }]
  } as unknown as WorkflowDraft;
}

function flowchartDraft(): WorkflowDraft {
  return {
    state: {
      rootActivity: {
        nodeId: "node-flowchart",
        activityVersionId: "flowchart@1",
        inputs: [],
        outputs: [],
        structure: {
          kind: flowchartStructureKind,
          schemaVersion: "1.0.0",
          payload: {
            activities: [{
              nodeId: "node-a",
              activityVersionId: "writeline@1",
              inputs: [],
              outputs: [],
              structure: null
            }],
            connections: [],
            startNodeId: null,
            nodeMetadata: {},
            connectionMetadata: {}
          }
        }
      },
      inputs: [],
      outputs: [],
      variables: []
    },
    layout: [{ nodeId: "node-a", x: 240, y: 160 }],
    activityPresentation: [{ nodeId: "node-a", displayName: "Renamed" }]
  } as unknown as WorkflowDraft;
}

// A generic container whose `body` property holds one ActivityNode rather than an array — that is what
// makes the slot single-cardinality, so a drop replaces the occupant instead of appending.
function singleSlotDraft(): WorkflowDraft {
  return {
    state: {
      rootActivity: {
        nodeId: "node-container",
        activityVersionId: "container@1",
        inputs: [],
        outputs: [],
        structure: {
          kind: "Container",
          schemaVersion: "1.0.0",
          payload: {
            body: {
              nodeId: "displaced-process",
              activityVersionId: "bpmn@1",
              inputs: [],
              outputs: [],
              structure: {
                kind: bpmnStructureKind,
                schemaVersion: "1.0.0",
                payload: {
                  elements: [{ elementId: "nested-element", elementType: "task", childNodeId: "nested-activity" }],
                  sequenceFlows: [],
                  activities: [{
                    nodeId: "nested-activity",
                    activityVersionId: "writeline@1",
                    inputs: [],
                    outputs: [],
                    structure: null
                  }]
                }
              }
            }
          }
        }
      },
      inputs: [],
      outputs: [],
      variables: []
    },
    layout: [
      { nodeId: "displaced-process", x: 10, y: 20 },
      { nodeId: "nested-element", x: 30, y: 40 },
      { nodeId: "nested-activity", x: 50, y: 60 },
      { nodeId: "unrelated", x: 70, y: 80 }
    ],
    activityPresentation: [{ nodeId: "displaced-process", displayName: "Old process" }]
  } as unknown as WorkflowDraft;
}

function containerCatalogItem(): ActivityCatalogItem {
  return {
    activityVersionId: "container@1",
    activityTypeKey: "Elsa.Container",
    version: "1.0.0",
    category: "Composition",
    displayName: "Container",
    executionType: "Action",
    inputs: [],
    outputs: []
  } as unknown as ActivityCatalogItem;
}

function flowchartCatalogItem(): ActivityCatalogItem {
  return {
    activityVersionId: "flowchart@1",
    activityTypeKey: "Elsa.Flowchart",
    version: "1.0.0",
    category: "Composition",
    displayName: "Flowchart",
    executionType: "Action",
    inputs: [],
    outputs: []
  } as unknown as ActivityCatalogItem;
}

function bpmnCatalogItem(): ActivityCatalogItem {
  return {
    activityVersionId: "bpmn@1",
    activityTypeKey: "Elsa.BpmnProcess",
    version: "1.0.0",
    category: "BPMN",
    displayName: "BPMN Process",
    executionType: "Action",
    inputs: [],
    outputs: []
  } as unknown as ActivityCatalogItem;
}

function writeLineCatalogItem(): ActivityCatalogItem {
  return {
    activityVersionId: "writeline@1",
    activityTypeKey: "Elsa.WriteLine",
    version: "1.0.0",
    category: "Primitives",
    displayName: "Write Line",
    executionType: "Action",
    inputs: [],
    outputs: []
  } as unknown as ActivityCatalogItem;
}
