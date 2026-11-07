import { describe, expect, it } from "vitest";
import type { ActivityNode } from "../workflowTypes";
import { applyBpmnDocumentCommands, bpmnNamespaces as ns, readBpmnDocument, type BpmnDocumentCommand } from "../bpmn/bpmnDocument";

const xml = `<b:definitions xmlns:b="${ns.model}" xmlns:d="${ns.diagram}" xmlns:dc="${ns.bounds}" xmlns:di="${ns.coordinates}" xmlns:v="urn:vendor" xmlns:q="urn:retained" targetNamespace="urn:process">
<b:process id="P"><b:task id="A" name="Original"/><b:task id="B"/><b:sequenceFlow id="F" sourceRef="A" targetRef="B"/></b:process>
<d:BPMNDiagram id="D"><d:BPMNPlane id="Plane" bpmnElement="P"><d:BPMNEdge id="E" bpmnElement="F" v:route="retained">
<?before retained?><di:waypoint x="100" y="100" v:point="start"/><!-- between retained --><di:waypoint x="200" y="100" v:point="target"/>
<d:BPMNLabel v:label="q:Value"><dc:Bounds x="110" y="120" width="80" height="20"/></d:BPMNLabel><?after retained?>
</d:BPMNEdge></d:BPMNPlane></d:BPMNDiagram></b:definitions>`;
function owner(source = xml): ActivityNode {
  return { nodeId: "owner", activityVersionId: "bpmn@1", inputs: [], outputs: [], structure: { kind: "elsa.bpmn.structure", schemaVersion: "2.0.0",
    payload: { documentXml: source, processId: "P", activities: [], bindings: [], variables: [], retained: { metadata: true } } } };
}
function edge(source: ActivityNode) { return readBpmnDocument(source).byId.get("E")!; }
function points(source: ActivityNode) { return [...edge(source).children].filter(element => element.namespaceURI === ns.coordinates && element.localName === "waypoint"); }
const route = [{ x: 110, y: 100 }, { x: 150, y: 100 }, { x: 150, y: 200 }, { x: 210, y: 200 }];

describe("canonical BPMN route source correspondence", () => {
  it("keeps endpoint nodes and their metadata attached to endpoints when inserting bends", () => {
    const original = owner(), previous = edge(original);
    const next = applyBpmnDocumentCommands(original, [{ kind: "waypoints", edgeId: "E", points: route, sourceIndices: [0, null, null, 1] }]);
    expect(points(next).map(point => point.getAttributeNS("urn:vendor", "point"))).toEqual(["start", null, null, "target"]);
    expect(points(next).map(point => [Number(point.getAttribute("x")), Number(point.getAttribute("y"))])).toEqual(route.map(point => [point.x, point.y]));
    expect(edge(next).getAttributeNS("urn:vendor", "route")).toBe("retained");
    expect(edge(next).getElementsByTagNameNS(ns.diagram, "BPMNLabel")[0].outerHTML).toBe(previous.getElementsByTagNameNS(ns.diagram, "BPMNLabel")[0].outerHTML);
    expect(edge(next).lookupNamespaceURI("q")).toBe("urn:retained");
    expect([...edge(next).childNodes].filter(node => node.nodeType === 7 || node.nodeType === 8).map(node => [node.nodeName, node.textContent]))
      .toEqual([...previous.childNodes].filter(node => node.nodeType === 7 || node.nodeType === 8).map(node => [node.nodeName, node.textContent]));
    expect(original.structure!.payload.documentXml).toBe(xml);
  });

  it("deletes only an explicitly omitted vertex while retaining edge and surviving endpoint metadata", () => {
    const original = owner(xml.replace('<!-- between retained -->', '<di:waypoint x="150" y="150" v:point="deleted-bend"/><!-- between retained -->'));
    const next = applyBpmnDocumentCommands(original, [{ kind: "waypoints", edgeId: "E", points: [route[0], route[3]], sourceIndices: [0, 2] }]);
    expect(points(next).map(point => point.getAttributeNS("urn:vendor", "point"))).toEqual(["start", "target"]);
    expect(edge(next).getAttributeNS("urn:vendor", "route")).toBe("retained");
    expect(edge(next).getElementsByTagNameNS(ns.diagram, "BPMNLabel")).toHaveLength(1);
  });

  it("keeps same-count legacy callers and no-op source identity intact", () => {
    const original = owner();
    expect(applyBpmnDocumentCommands(original, [{ kind: "waypoints", edgeId: "E", points: [{ x: 100, y: 100 }, { x: 200, y: 100 }] }])).toBe(original);
    const next = applyBpmnDocumentCommands(original, [{ kind: "waypoints", edgeId: "E", points: [route[0], route[3]] }]);
    expect(points(next).map(point => point.getAttributeNS("urn:vendor", "point"))).toEqual(["start", "target"]);
  });

  it("refuses implicit retargeting of annotated vertices when route membership changes", () => {
    const original = owner();
    expect(() => applyBpmnDocumentCommands(original, [
      { kind: "attribute", target: { elementId: "A" }, name: "name", value: "Must remain local" },
      { kind: "waypoints", edgeId: "E", points: route }
    ])).toThrow(/explicit source mapping/);
    expect(original.structure!.payload.documentXml).toBe(xml);
    expect(readBpmnDocument(original).byId.get("A")!.getAttribute("name")).toBe("Original");
  });

  it("retains existing count-changing callers when only the stable source endpoint has metadata", () => {
    const original = owner(xml.replace(' v:point="target"', ""));
    const next = applyBpmnDocumentCommands(original, [{ kind: "waypoints", edgeId: "E", points: route }]);
    expect(points(next)).toHaveLength(4);
    expect(points(next)[0].getAttributeNS("urn:vendor", "point")).toBe("start");
  });

  it.each([{ mapping: [0] }, { mapping: [0, 0] }, { mapping: [0, 2] }, { mapping: [0, -1] }, { mapping: [0, .5] }, { mapping: [0, undefined] }])("refuses invalid vertex mapping $mapping atomically", ({ mapping }) => {
    const original = owner();
    const command = { kind: "waypoints", edgeId: "E", points: [route[0], route[3]], sourceIndices: mapping } as BpmnDocumentCommand;
    expect(() => applyBpmnDocumentCommands(original, [command])).toThrow(/source mapping/);
    expect(original.structure!.payload.documentXml).toBe(xml);
  });
});
