import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useWorkflowCanvas } from "../workflow-editor/useWorkflowCanvas";
import type { WorkflowDraftRecipe } from "../workflow-editor/workflowDocument";
import { resolveScope } from "../workflowAdapter";
import { bpmnStructureKind } from "../bpmn/bpmnTypes";
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

function renderBpmnCanvas() {
  const catalog = [bpmnCatalogItem(), writeLineCatalogItem()];
  const catalogByVersion = new Map(catalog.map(item => [item.activityVersionId, item]));
  let draft = bpmnDraft();
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
      isBpmnDesigner: true,
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
    return null;
  }

  const container = document.createElement("div");
  document.body.append(container);
  const root = createRoot(container);
  flushSync(() => root.render(<Harness />));
  // The canvas mirror is populated by an effect; let it settle so `nodes` holds the existing BPMN
  // elements before the test places anything on top of them.
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
