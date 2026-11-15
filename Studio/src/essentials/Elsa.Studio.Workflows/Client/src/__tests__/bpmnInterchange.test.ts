// @vitest-environment node
import { expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { exportBpmnDocument, layoutFromBpmnDiagram, presentationFromBpmn, withDiagramFromLayout } from "../api/bpmnInterchange";
import { createActivityNode } from "../workflowAdapter";
import { bpmnActivity } from "./fixtures";

function source() {
  const child = createActivityNode(bpmnActivity, "child");
  child.structure!.payload = { elements: [{ elementId: "inner", elementType: "task" }], activities: [], sequenceFlows: [],
    diagram: { shapes: { inner: { x: 10.25, y: 20.75, width: 120.5, height: 80.25 } }, edges: { retained: { waypoints: [{ x: 1.5, y: 2.25 }] } } } };
  const root = createActivityNode(bpmnActivity, "root");
  root.structure!.payload = { elements: [{ elementId: "outer", elementType: "subProcess", childNodeId: "child" }], activities: [child], sequenceFlows: [],
    diagram: { shapes: { outer: { x: 100.5, y: 200.25, width: 200, height: 120 } }, edges: {} } };
  return root;
}
function context(response: unknown) {
  return { http: { postJson: vi.fn().mockResolvedValue(response) } } as unknown as StudioEndpointContext;
}
it("returns an export whose native bindings were all retained", async () => {
  await expect(exportBpmnDocument(context({ xml: "<definitions/>", omissions: [] }), source())).resolves.toBe("<definitions/>");
});
it("refuses a download that would silently omit an authored native binding", async () => {
  await expect(exportBpmnDocument(context({ xml: "<unsafe/>", omissions: [{ elementId: "inner", code: "sensitive-input", message: "This credential input cannot travel.", referenceKey: "credential" }] }), source()))
    .rejects.toThrow("inner: This credential input cannot travel.");
});
it("imports fractional positions from every native subprocess's own DI coordinate frame", () => {
  expect(layoutFromBpmnDiagram(source())).toEqual([{ nodeId: "outer", x: 100.5, y: 200.25 }, { nodeId: "inner", x: 10.25, y: 20.75 }]);
});
it("exports current inner and outer canvas positions without mutating dimensions, bends or the source", () => {
  const node = source(), original = structuredClone(node);
  const result = withDiagramFromLayout(node, [{ nodeId: "outer", x: 300.25, y: 400.5 }, { nodeId: "inner", x: 20.5, y: 30.25 }]);
  expect(result.structure!.payload.diagram).toEqual({ shapes: { outer: { x: 300.25, y: 400.5, width: 200, height: 120 } }, edges: {} });
  const inner = (result.structure!.payload.activities as typeof node[])[0];
  expect(inner.structure!.payload.diagram).toEqual({ shapes: { inner: { x: 20.5, y: 30.25, width: 120.5, height: 80.25 } }, edges: { retained: { waypoints: [{ x: 1.5, y: 2.25 }] } } });
  expect(node).toEqual(original);
});
it("carries native subprocess names through standard element names and restores their confirmed child presentation", () => {
  const original = source();
  const exported = withDiagramFromLayout(original, [], [{ nodeId: "child", displayName: "Prepare shipment" }]);
  expect(exported.structure!.payload.elements).toContainEqual(expect.objectContaining({ elementId: "outer", name: "Prepare shipment" }));
  expect(presentationFromBpmn(exported)).toEqual([{ nodeId: "child", displayName: "Prepare shipment" }]);
  expect(original.structure!.payload.elements).not.toContainEqual(expect.objectContaining({ name: "Prepare shipment" }));
});
it.each(["summary", "two names"])("refuses unsupported %s instead of silently losing authored presentation", profile => {
  const original = source();
  if (profile === "two names") (original.structure!.payload.elements as { name?: string }[])[0].name = "BPMN name";
  expect(() => withDiagramFromLayout(original, [], [{ nodeId: "child", displayName: "Native name", ...(profile === "summary" ? { description: "Retain this summary" } : {}) }])).toThrow("BPMN export was refused");
});
