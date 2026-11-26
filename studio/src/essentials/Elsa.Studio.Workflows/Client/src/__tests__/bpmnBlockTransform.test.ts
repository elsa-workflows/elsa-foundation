// @vitest-environment node
import { describe, expect, it } from "vitest";
import { extractBpmnBlockRegion } from "../graph-authoring/bpmnBlockTransform";
import type { BpmnBlockRefuse } from "../graph-authoring/bpmnBlockRegion";
import { createActivityNode } from "../workflowAdapter";
import { bpmnActivity, writeLine } from "./fixtures";
import type { DesignMetadataRecord } from "../workflowTypes";

const refuse: BpmnBlockRefuse = (code, path) => { throw new Error(`${code}: ${path}`); };
function fixture() {
  const owner = createActivityNode(bpmnActivity, "root");
  const a = createActivityNode(writeLine, "a"), b = createActivityNode(writeLine, "b");
  a.inputs = [{ referenceKey: "Text", value: { expressionType: "Literal", value: "Keep configuration" } }];
  owner.structure!.payload = {
    activities: [a, b], lanes: [{ laneId: "lane", poolId: "pool", name: "Shipping" }],
    elements: [{ elementId: "start", elementType: "startEvent" }, { elementId: "ea", elementType: "task", childNodeId: "a", laneId: "lane" },
      { elementId: "eb", elementType: "task", childNodeId: "b", laneId: "lane" }, { elementId: "end", elementType: "endEvent" }],
    sequenceFlows: [{ flowId: "entry", sourceRef: "start", targetRef: "ea", name: "Retain entry" },
      { flowId: "inside", sourceRef: "ea", targetRef: "eb", name: "Retain internal" }, { flowId: "exit", sourceRef: "eb", targetRef: "end" }],
    diagram: { shapes: { start: { x: 0, y: 80, width: 36, height: 36 }, ea: { x: 200.5, y: 80.25, width: 120, height: 90 },
      eb: { x: 420.5, y: 80.25, width: 240, height: 100 }, end: { x: 800, y: 80, width: 36, height: 36 } },
    edges: { inside: { waypoints: [{ x: 320.5, y: 125.25 }, { x: 420.5, y: 130.25 }], label: "Keep layout metadata" } } }
  };
  const layout: DesignMetadataRecord[] = [{ nodeId: "start", x: 0, y: 80 }, { nodeId: "ea", x: 200.5, y: 80.25 },
    { nodeId: "eb", x: 420.5, y: 80.25 }, { nodeId: "end", x: 800, y: 80 }];
  return { owner, layout, template: createActivityNode(bpmnActivity, "block") };
}
function extract(value = fixture(), reserved = new Set<string>()) {
  return extractBpmnBlockRegion(value.owner, value.template, ["eb", "ea"], [bpmnActivity, writeLine], value.layout, reserved, refuse);
}

describe("native BPMN subprocess extraction", () => {
  it("creates a real process binding, retains work/flow identities, and redirects only boundary endpoints", () => {
    const value = fixture(), original = structuredClone(value);
    const result = extract(value);
    const parent = result.owner.structure!.payload, body = result.block.structure!.payload;
    expect(parent.elements).toEqual([{ elementId: "start", elementType: "startEvent" },
      { elementId: result.elementId, elementType: "subProcess", childNodeId: "block", laneId: "lane" }, { elementId: "end", elementType: "endEvent" }]);
    expect(parent.sequenceFlows).toEqual([{ flowId: "entry", sourceRef: "start", targetRef: result.elementId, name: "Retain entry" },
      { flowId: "exit", sourceRef: result.elementId, targetRef: "end" }]);
    expect(body.activities).toEqual(value.owner.structure!.payload.activities);
    expect(body.elements).toMatchObject([{ elementType: "startEvent" }, { elementId: "ea", childNodeId: "a", laneId: null },
      { elementId: "eb", childNodeId: "b", laneId: null }, { elementType: "endEvent" }]);
    expect((body.sequenceFlows as Record<string, unknown>[])[1]).toEqual({ flowId: "inside", sourceRef: "ea", targetRef: "eb", name: "Retain internal" });
    expect(result.block.inputs).toEqual([{ referenceKey: "CanStartWorkflow", value: { expressionType: "Literal", value: false } }]);
    expect(parent.lanes).toEqual(value.owner.structure!.payload.lanes);
    expect(value).toEqual(original);
    (body.activities as typeof value.owner[])[0].inputs = [];
    expect(value).toEqual(original);
  });
  it("retains fractional positions, dimensions and internal bends in the nested coordinate frame", () => {
    const result = extract();
    expect(result.layout.find(item => item.nodeId === "ea")).toEqual({ nodeId: "ea", x: 0, y: 0 });
    expect(result.layout.find(item => item.nodeId === "eb")).toEqual({ nodeId: "eb", x: 220, y: 0 });
    expect(result.layout.find(item => item.nodeId === result.elementId)).toMatchObject({ x: 200.5, y: 80.25 });
    expect(result.layout.filter(item => ["start", "end"].includes(item.nodeId))).toEqual(fixture().layout.filter(item => ["start", "end"].includes(item.nodeId)));
    const body = result.block.structure!.payload.diagram as { shapes: Record<string, Record<string, number>>; edges: Record<string, unknown> };
    expect(body.shapes.eb).toEqual({ x: 220, y: 0, width: 240, height: 100 });
    expect(body.edges.inside).toEqual({ waypoints: [{ x: 120, y: 45 }, { x: 220, y: 50 }], label: "Keep layout metadata" });
    const end = (result.block.structure!.payload.elements as { elementId: string; elementType: string }[]).find(element => element.elementType === "endEvent")!;
    expect(body.shapes[end.elementId].x).toBeGreaterThan(body.shapes.eb.x + body.shapes.eb.width);
  });
  it("allocates plain event/flow identities against the whole resource's reserved identities", () => {
    const result = extract(fixture(), new Set(["block-start", "block-entry", "block-subprocess"]));
    expect(result.elementId).not.toBe("block-subprocess");
    expect((result.block.structure!.payload.elements as { elementId: string }[])[0].elementId).not.toBe("block-start");
    expect((result.block.structure!.payload.sequenceFlows as { flowId: string }[])[0].flowId).not.toBe("block-entry");
  });
  it("refuses invalid diagram values before producing a lossy proposal", () => {
    const value = fixture();
    const diagram = value.owner.structure!.payload.diagram as { shapes: Record<string, Record<string, unknown>> };
    diagram.shapes.ea.width = "bad";
    expect(() => extract(value)).toThrow("malformed-bpmn-shape");
  });
});
