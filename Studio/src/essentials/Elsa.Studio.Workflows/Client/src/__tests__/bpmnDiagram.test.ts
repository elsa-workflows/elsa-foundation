import { describe, expect, it } from "vitest";
import { reconcileBpmnDiagram } from "../bpmn/bpmnDiagram";
import { buildBpmnCanvas, syncBpmnCanvasToScope } from "../bpmn/bpmnAdapter";
import { getChildSlots } from "../workflowAdapter";
import type { BpmnElement, BpmnSequenceFlow } from "../bpmn/bpmnTypes";
import type { ActivityNode } from "../workflowTypes";

const elements: BpmnElement[] = [{ elementId: "a", elementType: "task" }, { elementId: "b", elementType: "task" }, { elementId: "c", elementType: "task" }];
const flows: BpmnSequenceFlow[] = [{ flowId: "flow", sourceRef: "a", targetRef: "b" }];
function diagram() {
  return { vendor: { retained: true }, shapes: {
    a: { x: 100, y: 100, width: 100, height: 80, label: { x: 90, y: 190, width: 120, height: 20, vendor: "label" }, isMarkerVisible: true },
    b: { x: 400, y: 100, width: 100, height: 80 }, c: { x: 700, y: 300, width: 100, height: 80 },
    unrelated: { custom: "untouched" }
  }, edges: {
    flow: { waypoints: [{ x: 200, y: 140, vendor: "point" }, { x: 300, y: 140 }, { x: 300, y: 160 }, { x: 400, y: 160 }],
      label: { x: 270, y: 120, width: 60, height: 20, vendor: "edge-label" }, vendor: "edge" },
    unrelated: { custom: "untouched" }
  } };
}
const positions = (a = { x: 100, y: 100 }, b = { x: 400, y: 100 }) => new Map([["a", a], ["b", b], ["c", { x: 700, y: 300 }]]);
function reconcile(nextPositions = positions(), nextFlows = flows, nextElements = elements) {
  return reconcileBpmnDiagram(diagram(), elements, flows, nextElements, nextFlows, nextPositions) as ReturnType<typeof diagram>;
}

describe("cross-editor BPMN diagram reconciliation", () => {
  it("preserves untouched shapes, bends, labels, markers and unknown metadata exactly", () => {
    expect(reconcile()).toEqual(diagram());
  });

  it("translates the complete route and labels when both ends move together", () => {
    const next = reconcile(positions({ x: 150, y: 120 }, { x: 450, y: 120 }));
    expect(next.edges.flow.waypoints).toEqual(diagram().edges.flow.waypoints.map(point => ({ ...point, x: point.x + 50, y: point.y + 20 })));
    expect(next.edges.flow.label).toEqual({ x: 320, y: 140, width: 60, height: 20, vendor: "edge-label" });
    expect(next.shapes.a.label).toEqual({ x: 140, y: 210, width: 120, height: 20, vendor: "label" });
    expect(next.shapes.a.isMarkerVisible).toBe(true);
    expect(next.edges.flow.vendor).toBe("edge");
  });

  it("redocks an edge after one shape moves and relocates its label without clearing metadata", () => {
    const next = reconcile(positions({ x: 100, y: 200 }));
    expect(next.edges.flow.waypoints[0]).toEqual({ x: 200, y: 240, vendor: "point" });
    expect(next.edges.flow.waypoints.at(-1)).toEqual({ x: 400, y: 140 });
    expect(next.edges.flow.waypoints.slice(1, -1)).toEqual([{ x: 300, y: 240 }, { x: 300, y: 140 }]);
    expect(next.edges.flow.label.y).not.toBe(diagram().edges.flow.label.y);
    expect(next.edges.flow.label.vendor).toBe("edge-label");
    expect(next.shapes.b).toEqual(diagram().shapes.b);
    expect(next.edges.unrelated).toEqual(diagram().edges.unrelated);
  });

  it("redocks reconnects against the new target instead of retaining the previous endpoint", () => {
    const next = reconcile(positions(), [{ ...flows[0], targetRef: "c" }]);
    expect(next.edges.flow.waypoints.at(-1)).toEqual({ x: 700, y: 340 });
    expect(next.edges.flow.waypoints.at(-1)).not.toEqual(diagram().edges.flow.waypoints.at(-1));
    expect(next.edges.flow.waypoints[0].vendor).toBe("point");
  });

  it("redocks saved routes when an incident shape previously had no DI", () => {
    const partial = diagram() as unknown as { shapes: Record<string, unknown>; edges: Record<string, { waypoints: { x: number; y: number }[] }> };
    delete partial.shapes.a;
    const next = reconcileBpmnDiagram(partial, elements, flows, elements, flows, positions({ x: 100, y: 200 })) as typeof partial;
    expect(next.edges.flow.waypoints[0]).toMatchObject({ x: 200, y: 240 });
    expect(next.edges.flow.waypoints.at(-1)).toEqual({ x: 400, y: 140 });
    expect(next.shapes.a).toMatchObject({ x: 100, y: 200 });
  });

  it("removes only departed graph DI, preserving unrelated opaque records", () => {
    const next = reconcile(positions(), [], elements.filter(element => element.elementId !== "a"));
    expect(next.shapes).not.toHaveProperty("a");
    expect(next.edges).not.toHaveProperty("flow");
    expect(next.shapes.unrelated).toEqual({ custom: "untouched" });
    expect(next.edges.unrelated).toEqual({ custom: "untouched" });
    expect(next.vendor).toEqual({ retained: true });
  });

  it("creates coherent geometry for newly stamped shapes and connections", () => {
    const nextPositions = positions(); nextPositions.set("new", { x: 900, y: 300 });
    const next = reconcile(nextPositions, [...flows, { flowId: "newFlow", sourceRef: "c", targetRef: "new" }],
      [...elements, { elementId: "new", elementType: "endEvent" }]) as unknown as { shapes: Record<string, unknown>; edges: Record<string, { waypoints: unknown[] }> };
    expect(next.shapes.new).toEqual({ x: 900, y: 300, width: 36, height: 36 });
    expect(next.edges.newFlow.waypoints[0]).toEqual({ x: 800, y: 340 });
    expect(next.edges.newFlow.waypoints.at(-1)).toEqual({ x: 900, y: 318 });
  });

  it("routes a new self-loop around its host rather than through it", () => {
    const next = reconcile(positions(), [{ flowId: "self", sourceRef: "a", targetRef: "a" }]) as unknown as { edges: Record<string, { waypoints: { x: number; y: number }[] }> };
    expect(next.edges.self.waypoints[0]).toEqual({ x: 200, y: 140 });
    expect(next.edges.self.waypoints.at(-1)).toEqual({ x: 150, y: 180 });
    expect(next.edges.self.waypoints.some(point => point.x > 200 && point.y > 180)).toBe(true);
  });

  it("retains malformed geometry for explicit validation rather than silently replacing it", () => {
    const invalid = diagram() as unknown as { shapes: Record<string, unknown>; edges: Record<string, unknown> };
    invalid.edges.flow = { waypoints: "malformed", vendor: "keep" };
    const next = reconcileBpmnDiagram(invalid, elements, flows, elements, flows, positions({ x: 50, y: 200 })) as typeof invalid;
    expect(next.edges.flow).toEqual(invalid.edges.flow);
    expect(reconcileBpmnDiagram(undefined, elements, flows, elements, flows, positions())).toBeUndefined();
  });

  it("uses the reconciliation in the actual integrated-canvas commit seam", () => {
    const owner: ActivityNode = { nodeId: "process", activityVersionId: "exact-bpmn", inputs: [], outputs: [], structure: {
      kind: "elsa.bpmn.structure", schemaVersion: "1.0.0", payload: { elements, sequenceFlows: flows, activities: [], diagram: diagram() }
    } };
    const scope = { owner, slot: getChildSlots(owner)[0] };
    const canvas = buildBpmnCanvas(scope, [], elements.map(element => ({ nodeId: element.elementId, ...positions().get(element.elementId)! })));
    const nextNodes = canvas.nodes.map(node => node.id === "a" ? { ...node, position: { x: 100, y: 200 } } : node);
    const saved = syncBpmnCanvasToScope(scope, nextNodes, canvas.edges);
    const savedDiagram = saved.structure!.payload.diagram as ReturnType<typeof diagram>;
    expect(savedDiagram.shapes.a.y).toBe(200);
    expect(savedDiagram.edges.flow.waypoints[0]).toEqual({ x: 200, y: 240, vendor: "point" });
    expect(owner.structure!.payload.diagram).toEqual(diagram());
  });
});
