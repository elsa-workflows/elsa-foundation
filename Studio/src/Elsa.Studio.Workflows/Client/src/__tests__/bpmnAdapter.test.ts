import { describe, expect, it } from "vitest";
import type { Edge, Node } from "@xyflow/react";
import type { ActivityCatalogItem, ActivityNode } from "../workflowTypes";
import { getActivityDesignerSupport, getChildSlots, readStructureDesignFacet } from "../workflowAdapter";
import {
  buildBpmnCanvas,
  createBpmnBoundNode,
  createBpmnShapeNode,
  syncBpmnCanvasToScope,
  updateBpmnElement,
  type BpmnNodeData
} from "../bpmn/bpmnAdapter";
import { bpmnShapePalette, bpmnStructureKind } from "../bpmn/bpmnTypes";

function catalogItem(overrides: Partial<ActivityCatalogItem> = {}): ActivityCatalogItem {
  return {
    activityVersionId: "writeline@1",
    activityTypeKey: "Elsa.WriteLine",
    version: "1.0.0",
    category: "Console",
    displayName: "Write Line",
    executionType: "activity",
    inputs: [],
    outputs: [],
    ...overrides
  };
}

function activityNode(nodeId: string): ActivityNode {
  return { nodeId, activityVersionId: "writeline@1", inputs: [], outputs: [] };
}

function bpmnOwner(elements: unknown[], sequenceFlows: unknown[], activities: ActivityNode[] = []): ActivityNode {
  return {
    nodeId: "node-bpmn",
    activityVersionId: "bpmn@1",
    inputs: [],
    outputs: [],
    structure: {
      kind: bpmnStructureKind,
      schemaVersion: "1.0.0",
      payload: { elements, sequenceFlows, activities }
    }
  };
}

const diamondElements = [
  { elementId: "start", elementType: "startEvent" },
  { elementId: "task-a", elementType: "task", childNodeId: "node-a" },
  { elementId: "end", elementType: "endEvent" }
];
const diamondFlows = [
  { flowId: "flow-1", sourceRef: "start", targetRef: "task-a" },
  { flowId: "flow-2", sourceRef: "task-a", targetRef: "end", conditionOutcome: "Done", extra: "kept" }
];

describe("bpmn designer mode plumbing", () => {
  it("resolves the bpmn designer support from the structure kind", () => {
    expect(getActivityDesignerSupport(bpmnOwner([], []))).toBe("bpmn");
  });

  it("projects a bpmn child slot with bpmn mode from the generic activities fallback", () => {
    const owner = bpmnOwner(diamondElements, diamondFlows, [activityNode("node-a")]);
    const slots = getChildSlots(owner);
    expect(slots).toHaveLength(1);
    expect(slots[0].mode).toBe("bpmn");
    expect(slots[0].activities.map(activity => activity.nodeId)).toEqual(["node-a"]);
  });

  it("accepts a bpmn structure design facet", () => {
    const facetItem = catalogItem({
      activityTypeKey: "Elsa.BpmnProcess",
      displayName: "BPMN Process",
      designFacets: [
        {
          kind: bpmnStructureKind,
          schemaVersion: "1.0.0",
          payload: {
            mode: "bpmn",
            supportsScopedVariables: true,
            slots: [{ name: "Bpmn.Activities", property: "activities", displayName: "Activities", cardinality: "many" }],
            initialPayload: { activities: [], elements: [], sequenceFlows: [] }
          }
        }
      ]
    });

    const facet = readStructureDesignFacet(facetItem);
    expect(facet?.kind).toBe(bpmnStructureKind);
    expect(facet?.payload.mode).toBe("bpmn");
    expect(getActivityDesignerSupport(null, facetItem)).toBe("bpmn");
  });
});

describe("buildBpmnCanvas", () => {
  it("maps elements to bpmnElement nodes and flows to edges with condition labels", () => {
    const owner = bpmnOwner(diamondElements, diamondFlows, [activityNode("node-a")]);
    const scope = { owner, slot: getChildSlots(owner)[0] };
    const canvas = buildBpmnCanvas(scope, [catalogItem()], [{ nodeId: "task-a", x: 400, y: 80 }]);

    expect(canvas.nodes.map(node => node.id)).toEqual(["start", "task-a", "end"]);
    expect(canvas.nodes.every(node => node.type === "bpmnElement")).toBe(true);
    expect(canvas.nodes[1].position).toEqual({ x: 400, y: 80 });
    expect(canvas.nodes[1].data.boundActivity?.label).toBe("Write Line");
    expect(canvas.edges.map(edge => edge.id)).toEqual(["flow-1", "flow-2"]);
    expect(canvas.edges[1].label).toBe("Done");
  });
});

describe("syncBpmnCanvasToScope", () => {
  function canvasFor(owner: ActivityNode) {
    const scope = { owner, slot: getChildSlots(owner)[0] };
    return { scope, ...buildBpmnCanvas(scope, [catalogItem()], []) };
  }

  it("round-trips an unchanged canvas", () => {
    const owner = bpmnOwner(diamondElements, diamondFlows, [activityNode("node-a")]);
    const { scope, nodes, edges } = canvasFor(owner);

    const next = syncBpmnCanvasToScope(scope, nodes, edges);
    expect(next.structure?.payload.elements).toEqual(diamondElements);
    // Unknown authored props on flows survive the round-trip.
    expect((next.structure?.payload.sequenceFlows as Record<string, unknown>[])[1].extra).toBe("kept");
    expect((next.structure?.payload.activities as ActivityNode[]).map(activity => activity.nodeId)).toEqual(["node-a"]);
  });

  it("adds a stamped shape node as a new element", () => {
    const owner = bpmnOwner(diamondElements, diamondFlows, [activityNode("node-a")]);
    const { scope, nodes, edges } = canvasFor(owner);
    const gateway = bpmnShapePalette.find(shape => shape.key === "parallelGateway")!;
    const shapeNode = createBpmnShapeNode(gateway, { x: 10, y: 10 });

    const next = syncBpmnCanvasToScope(scope, [...nodes, shapeNode], edges);
    const elements = next.structure?.payload.elements as Record<string, unknown>[];
    expect(elements).toHaveLength(4);
    expect(elements[3].elementType).toBe("parallelGateway");
  });

  it("adds a bound task node as element plus slot activity", () => {
    const owner = bpmnOwner(diamondElements, diamondFlows, [activityNode("node-a")]);
    const { scope, nodes, edges } = canvasFor(owner);
    const boundActivity = activityNode("node-b");
    const boundNode = createBpmnBoundNode(catalogItem(), boundActivity, { x: 20, y: 20 });

    const next = syncBpmnCanvasToScope(scope, [...nodes, boundNode], edges, [boundActivity]);
    const elements = next.structure?.payload.elements as Record<string, unknown>[];
    expect(elements[3].elementType).toBe("task");
    expect(elements[3].childNodeId).toBe("node-b");
    expect((next.structure?.payload.activities as ActivityNode[]).map(activity => activity.nodeId)).toEqual(["node-a", "node-b"]);
  });

  it("deleting an element drops its flows and its orphaned bound activity", () => {
    const owner = bpmnOwner(diamondElements, diamondFlows, [activityNode("node-a")]);
    const { scope, nodes, edges } = canvasFor(owner);
    const remainingNodes = nodes.filter(node => node.id !== "task-a");
    const remainingEdges = edges.filter(edge => edge.source !== "task-a" && edge.target !== "task-a");

    const next = syncBpmnCanvasToScope(scope, remainingNodes, remainingEdges);
    const elements = next.structure?.payload.elements as Record<string, unknown>[];
    expect(elements.map(element => element.elementId)).toEqual(["start", "end"]);
    expect(next.structure?.payload.sequenceFlows).toEqual([]);
    expect(next.structure?.payload.activities).toEqual([]);
  });

  it("a new edge becomes a sequence flow", () => {
    const owner = bpmnOwner(diamondElements, diamondFlows, [activityNode("node-a")]);
    const { scope, nodes, edges } = canvasFor(owner);
    const newEdge: Edge = { id: "flow-new", source: "start", target: "end", type: "workflow" };

    const next = syncBpmnCanvasToScope(scope, nodes, [...edges, newEdge]);
    const flows = next.structure?.payload.sequenceFlows as Record<string, unknown>[];
    expect(flows.map(flow => flow.flowId)).toEqual(["flow-1", "flow-2", "flow-new"]);
    expect(flows[2]).toMatchObject({ sourceRef: "start", targetRef: "end", isDefault: false });
  });
});

describe("updateBpmnElement", () => {
  it("patches one element immutably", () => {
    const owner = bpmnOwner(diamondElements, diamondFlows);
    const next = updateBpmnElement(owner, "task-a", { name: "Approve order" });
    const elements = next.structure?.payload.elements as Record<string, unknown>[];
    expect(elements[1].name).toBe("Approve order");
    expect(elements[1].elementId).toBe("task-a");
    expect((owner.structure?.payload.elements as Record<string, unknown>[])[1].name).toBeUndefined();
  });
});

describe("bpmn canvas node factories", () => {
  it("stamps a terminate end event with its event definition", () => {
    const terminate = bpmnShapePalette.find(shape => shape.key === "terminateEndEvent")!;
    const node: Node<BpmnNodeData> = createBpmnShapeNode(terminate, { x: 0, y: 0 });
    expect(node.data.element.elementType).toBe("endEvent");
    expect(node.data.element.eventDefinitions).toEqual([{ type: "terminate" }]);
  });

  it("binds container catalog items as subProcess elements", () => {
    const container = catalogItem({
      activityTypeKey: "Elsa.BpmnProcess",
      displayName: "BPMN Process",
      containerStructure: { mode: "bpmn" }
    });
    const node = createBpmnBoundNode(container, activityNode("node-inner"), { x: 0, y: 0 });
    expect(node.data.element.elementType).toBe("subProcess");
  });
});

describe("bpmn flow editing helpers", async () => {
  const { updateBpmnFlow, updateBpmnDefaultFlow } = await import("../bpmn/bpmnAdapter");

  it("updateBpmnFlow patches condition outcome without touching endpoints", () => {
    const owner = {
      nodeId: "node-bpmn",
      activityVersionId: "bpmn@1",
      inputs: [],
      outputs: [],
      structure: {
        kind: "elsa.bpmn.structure",
        schemaVersion: "1.0.0",
        payload: {
          elements: [{ elementId: "gw", elementType: "exclusiveGateway" }, { elementId: "end", elementType: "endEvent" }],
          sequenceFlows: [{ flowId: "flow-1", sourceRef: "gw", targetRef: "end" }],
          activities: []
        }
      }
    };
    const next = updateBpmnFlow(owner, "flow-1", { conditionOutcome: "Approved", sourceRef: "hacked" });
    const flows = next.structure?.payload.sequenceFlows as Record<string, unknown>[];
    expect(flows[0].conditionOutcome).toBe("Approved");
    expect(flows[0].sourceRef).toBe("gw");
  });

  it("updateBpmnDefaultFlow marks one default, clears siblings and its condition, and stamps defaultFlowId", () => {
    const owner = {
      nodeId: "node-bpmn",
      activityVersionId: "bpmn@1",
      inputs: [],
      outputs: [],
      structure: {
        kind: "elsa.bpmn.structure",
        schemaVersion: "1.0.0",
        payload: {
          elements: [{ elementId: "gw", elementType: "exclusiveGateway" }, { elementId: "a", elementType: "endEvent" }, { elementId: "b", elementType: "endEvent" }],
          sequenceFlows: [
            { flowId: "flow-a", sourceRef: "gw", targetRef: "a", isDefault: true },
            { flowId: "flow-b", sourceRef: "gw", targetRef: "b", conditionOutcome: "B" }
          ],
          activities: []
        }
      }
    };
    const next = updateBpmnDefaultFlow(owner, "gw", "flow-b");
    const flows = next.structure?.payload.sequenceFlows as Record<string, unknown>[];
    expect(flows[0].isDefault).toBe(false);
    expect(flows[1].isDefault).toBe(true);
    expect(flows[1].conditionOutcome).toBeNull();
    const elements = next.structure?.payload.elements as Record<string, unknown>[];
    expect(elements[0].defaultFlowId).toBe("flow-b");

    const cleared = updateBpmnDefaultFlow(next, "gw", null);
    const clearedFlows = cleared.structure?.payload.sequenceFlows as Record<string, unknown>[];
    expect(clearedFlows.every(flow => flow.isDefault !== true)).toBe(true);
  });
});

describe("bpmn interchange layout bridges", async () => {
  const { layoutFromBpmnDiagram, withDiagramFromLayout } = await import("../api/bpmnInterchange");

  const node = {
    nodeId: "node-bpmn",
    activityVersionId: "bpmn@1",
    inputs: [],
    outputs: [],
    structure: {
      kind: "elsa.bpmn.structure",
      schemaVersion: "1.0.0",
      payload: {
        elements: [{ elementId: "start", elementType: "startEvent" }, { elementId: "end", elementType: "endEvent" }],
        sequenceFlows: [],
        activities: [],
        diagram: { shapes: { start: { x: 120, y: 200, width: 36, height: 36 } }, edges: {} }
      }
    }
  };

  it("layoutFromBpmnDiagram maps DI shapes to layout records keyed by elementId", () => {
    expect(layoutFromBpmnDiagram(node)).toEqual([{ nodeId: "start", x: 120, y: 200 }]);
  });

  it("withDiagramFromLayout stamps canvas positions into DI shapes, preserving sizes", () => {
    const next = withDiagramFromLayout(node, [{ nodeId: "start", x: 300, y: 50 }, { nodeId: "end", x: 500, y: 50 }]);
    const shapes = (next.structure?.payload.diagram as { shapes: Record<string, Record<string, unknown>> }).shapes;
    expect(shapes.start).toMatchObject({ x: 300, y: 50, width: 36, height: 36 });
    expect(shapes.end).toMatchObject({ x: 500, y: 50 });
  });
});
