// @vitest-environment node
import { describe, expect, it } from "vitest";
import { readLinearBpmnBlockRegion, type BpmnBlockRefuse } from "../graph-authoring/bpmnBlockRegion";
import { createActivityNode } from "../workflowAdapter";
import { bpmnActivity, writeLine } from "./fixtures";

const refuse: BpmnBlockRefuse = (code, path) => { throw new Error(`${code}: ${path}`); };
function source() {
  const root = createActivityNode(bpmnActivity, "root");
  root.structure!.payload = {
    activities: [createActivityNode(writeLine, "a"), createActivityNode(writeLine, "b")],
    elements: [{ elementId: "start", elementType: "startEvent" }, { elementId: "ea", elementType: "serviceTask", childNodeId: "a", laneId: "lane" },
      { elementId: "eb", elementType: "serviceTask", childNodeId: "b", laneId: "lane" }, { elementId: "end", elementType: "endEvent" }],
    sequenceFlows: [{ flowId: "entry", sourceRef: "start", targetRef: "ea", name: "Retain me" }, { flowId: "inside", sourceRef: "ea", targetRef: "eb" }, { flowId: "exit", sourceRef: "eb", targetRef: "end" }]
  };
  return root;
}
const catalog = [bpmnActivity, writeLine];
describe("bounded BPMN block selection", () => {
  it("orders canvas element IDs by routing, retains exact bound children/flow metadata and does not mutate", () => {
    const root = source();
    const original = structuredClone(root);
    const region = readLinearBpmnBlockRegion(root, ["eb", "ea"], catalog, refuse);
    expect(region.elements.map(element => element.elementId)).toEqual(["ea", "eb"]);
    expect(region.activities.map(child => child.nodeId)).toEqual(["a", "b"]);
    expect(region.incoming).toMatchObject({ flowId: "entry", name: "Retain me" });
    expect(region.internal.map(flow => flow.flowId)).toEqual(["inside"]);
    expect(region.outgoing.flowId).toBe("exit");
    expect(region.laneId).toBe("lane");
    expect(root).toEqual(original);
  });
  it.each(["sourceElementId", "targetElementId"])("refuses moving a documented message-flow %s", field => {
    const root = source(); root.structure!.payload.messageFlows = [{ flowId: "message", [field]: "ea" }];
    expect(() => readLinearBpmnBlockRegion(root, ["ea", "eb"], catalog, refuse)).toThrow("bpmn-message-boundary");
  });
  it("refuses activity IDs instead of silently treating them as element selection", () => {
    expect(() => readLinearBpmnBlockRegion(source(), ["a", "b"], catalog, refuse)).toThrow("stale-bpmn-selection");
  });
  it("refuses cross-lane work", () => {
    const root = source(); (root.structure!.payload.elements as Record<string, unknown>[])[2].laneId = "other";
    expect(() => readLinearBpmnBlockRegion(root, ["ea", "eb"], catalog, refuse)).toThrow("cross-lane-bpmn-region");
  });
  it.each([true, false])("accepts the backend's cancelActivity=%s field on ordinary tasks", value => {
    const root = source();
    (root.structure!.payload.elements as Record<string, unknown>[])[1].cancelActivity = value;
    expect(readLinearBpmnBlockRegion(root, ["ea", "eb"], catalog, refuse).activities.map(child => child.nodeId)).toEqual(["a", "b"]);
  });
  it.each(["exclusiveGateway", "parallelGateway", "startEvent", "boundaryEvent"])("refuses selected %s", type => {
    const root = source(); (root.structure!.payload.elements as Record<string, unknown>[])[1].elementType = type;
    expect(() => readLinearBpmnBlockRegion(root, ["ea", "eb"], catalog, refuse)).toThrow("unsupported-bpmn-element");
  });
  it.each(["listenerNodeId", "loopCharacteristics", "isForCompensation", "isTransaction", "triggeredByEvent"])("refuses special %s semantics", key => {
    const root = source(); (root.structure!.payload.elements as Record<string, unknown>[])[1][key] = key === "listenerNodeId" ? "listener" : key === "loopCharacteristics" ? { cardinality: "3" } : true;
    expect(() => readLinearBpmnBlockRegion(root, ["ea", "eb"], catalog, refuse)).toThrow("special-bpmn-boundary");
  });
  it("refuses a boundary event attached outside the selection", () => {
    const root = source(); (root.structure!.payload.elements as unknown[]).push({ elementId: "boundary", elementType: "boundaryEvent", attachedToRef: "ea" });
    expect(() => readLinearBpmnBlockRegion(root, ["ea", "eb"], catalog, refuse)).toThrow("attached-bpmn-event");
  });
  it("refuses an unbound task and unavailable exact catalog version", () => {
    const root = source(); delete (root.structure!.payload.elements as Record<string, unknown>[])[1].childNodeId;
    expect(() => readLinearBpmnBlockRegion(root, ["ea"], catalog, refuse)).toThrow("unbound-bpmn-task");
    expect(() => readLinearBpmnBlockRegion(source(), ["ea"], [bpmnActivity], refuse)).toThrow("unavailable-bpmn-binding");
  });
  it("refuses direct-model bindings", () => {
    const root = source(); (root.structure!.payload.elements as Record<string, unknown>[])[1].properties = { "elsa.direct.binding": "{}" };
    expect(() => readLinearBpmnBlockRegion(root, ["ea"], catalog, refuse)).toThrow("unsupported-bpmn-binding");
  });
  it("refuses conditional/default flow settings and branching", () => {
    const root = source(); (root.structure!.payload.sequenceFlows as Record<string, unknown>[])[1].conditionOutcome = "Approved";
    expect(() => readLinearBpmnBlockRegion(root, ["ea", "eb"], catalog, refuse)).toThrow("conditional-bpmn-region");
    const branch = source(); (branch.structure!.payload.sequenceFlows as unknown[]).push({ flowId: "shortcut", sourceRef: "ea", targetRef: "end" });
    expect(() => readLinearBpmnBlockRegion(branch, ["ea", "eb"], catalog, refuse)).toThrow("nonlinear-bpmn-region");
  });
  it("refuses malformed or duplicate authored data instead of filtering it away", () => {
    const root = source(); (root.structure!.payload.elements as unknown[]).push(null);
    expect(() => readLinearBpmnBlockRegion(root, ["ea"], catalog, refuse)).toThrow("malformed-bpmn-element");
    const duplicate = source(); (duplicate.structure!.payload.sequenceFlows as Record<string, unknown>[])[0].flowId = "ea";
    expect(() => readLinearBpmnBlockRegion(duplicate, ["ea"], catalog, refuse)).toThrow("duplicate-bpmn-identity");
  });
});
