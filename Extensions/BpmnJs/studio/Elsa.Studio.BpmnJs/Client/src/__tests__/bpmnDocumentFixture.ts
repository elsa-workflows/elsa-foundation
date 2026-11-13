import type { ActivityNode, BpmnDocumentAuthoring, BpmnScopeAddress } from "@elsa-workflows/studio-workflows";
// Tests exercise the real owning source API without importing the entire composed UI module.
import * as owning from "../../../../../../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/bpmn/bpmnDocument";
import { planBpmnDocumentSyntaxChange } from "../../../../../../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/bpmn/bpmnDocumentSyntaxChange";
import { serializeBpmnXml } from "../../../../../../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/bpmn/bpmnDocumentXml";
import { projectBpmnNativeDocument, bindBpmnNativeBaseline } from "../bpmnDocumentProjection";
import SubprocessCompatibility from "bpmn-js/lib/features/drilldown/SubprocessCompatibility";
// @ts-expect-error bpmn-moddle10.3.1 does not publish declarations for its runtime entry.
import { BpmnModdle } from "bpmn-moddle";

export const authoring: BpmnDocumentAuthoring = { ...owning, planBpmnDocumentSyntaxChange, serializeBpmnXml };
export const scope: BpmnScopeAddress = { documentOwnerNodeId: "owner", diagramId: "main", scopeElementId: "process" };
export const rawXml = `<?xml version="1.0"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI" xmlns:dc="http://www.omg.org/spec/DD/20100524/DC" xmlns:di="http://www.omg.org/spec/DD/20100524/DI" xmlns:q="urn:process" xmlns:v="urn:vendor" targetNamespace="urn:process" id="definitions">
<!-- retained source comment --><?vendor retained?>
<bpmn:message id="message" name="Message"/>
<bpmn:process id="process">
 <bpmn:serviceTask id="work" name="Original" v:flag="qualified">
  <bpmn:documentation textFormat="text/html"><html:p xmlns:html="http://www.w3.org/1999/xhtml">rich <html:b>content</html:b></html:p></bpmn:documentation>
  <bpmn:extensionElements><v:configuration v:flag="qualified" flag="unqualified" xmlns:q="urn:inner" ref="q:Inner">before <v:bold/> after</v:configuration></bpmn:extensionElements>
  <bpmn:outgoing>q:flow</bpmn:outgoing>
 </bpmn:serviceTask>
 <bpmn:exclusiveGateway id="gateway" name="Route" default="done"><bpmn:incoming>q:flow</bpmn:incoming></bpmn:exclusiveGateway>
 <bpmn:endEvent id="end"/>
 <bpmn:sequenceFlow id="flow" sourceRef="work" targetRef="gateway"/>
 <bpmn:sequenceFlow id="done" sourceRef="gateway" targetRef="end" name="Done"/>
 <bpmn:sequenceFlow id="high" sourceRef="gateway" targetRef="end" name="High"/>
 <bpmn:subProcess id="sub"><bpmn:task id="inner"/></bpmn:subProcess>
</bpmn:process>
<bpmn:process id="other"/>
<bpmndi:BPMNDiagram id="main"><bpmndi:BPMNPlane id="plane" bpmnElement="q:process">
 <bpmndi:BPMNShape id="work-di" bpmnElement="q:work"><dc:Bounds x="100" y="100" width="120" height="80" v:geometry="retain"/></bpmndi:BPMNShape>
 <bpmndi:BPMNShape id="gateway-di" bpmnElement="gateway" isMarkerVisible="true"><dc:Bounds x="300" y="100" width="50" height="50"/><bpmndi:BPMNLabel><dc:Bounds x="290" y="160" width="80" height="20"/></bpmndi:BPMNLabel></bpmndi:BPMNShape>
 <bpmndi:BPMNShape id="end-di" bpmnElement="end"><dc:Bounds x="450" y="100" width="36" height="36"/></bpmndi:BPMNShape>
 <bpmndi:BPMNEdge id="flow-di" bpmnElement="flow"><di:waypoint x="220" y="140" v:pointKey="source"/><di:waypoint x="300" y="125"/></bpmndi:BPMNEdge>
 <bpmndi:BPMNEdge id="done-di" bpmnElement="done"><di:waypoint x="350" y="125"/><di:waypoint x="450" y="118"/></bpmndi:BPMNEdge>
 <bpmndi:BPMNEdge id="high-di" bpmnElement="high"><di:waypoint x="350" y="125"/><di:waypoint x="450" y="118"/></bpmndi:BPMNEdge>
 <bpmndi:BPMNShape id="sub-di" bpmnElement="sub"><dc:Bounds x="600" y="100" width="120" height="80"/></bpmndi:BPMNShape>
 <bpmndi:BPMNShape id="inner-di" bpmnElement="inner"><dc:Bounds x="650" y="300" width="100" height="80"/></bpmndi:BPMNShape>
</bpmndi:BPMNPlane></bpmndi:BPMNDiagram>
<bpmndi:BPMNDiagram id="secondary"><bpmndi:BPMNPlane id="other-plane" bpmnElement="other"/></bpmndi:BPMNDiagram>
</bpmn:definitions>`;
export function owner(xml = rawXml): ActivityNode {
  return { nodeId: "owner", activityVersionId: "bpmn@1", inputs: [], outputs: [], structure: { kind: "elsa.bpmn.structure", schemaVersion: "2.0.0", payload: {
    documentXml: xml, processId: "process", activities: [
      { nodeId: "work-native", activityVersionId: "work@1", inputs: [], outputs: [], future: { retain: true } },
      { nodeId: "listener-native", activityVersionId: "listener@1", inputs: [], outputs: [] }
    ], bindings: [{ elementId: "work", childNodeId: "work-native", listenerNodeId: "listener-native" }], variables: [], future: { retain: true }
  } } };
}
export function edit(xml: string, action: (document: XMLDocument, byId: Map<string, Element>) => void): string {
  const document = new DOMParser().parseFromString(xml, "application/xml");
  const byId = new Map([...document.getElementsByTagName("*")].filter(element => element.hasAttribute("id")).map(element => [element.getAttribute("id")!, element]));
  action(document, byId);
  return authoring.serializeBpmnXml(document);
}
export async function modeled(source = owner()) {
  const projection = projectBpmnNativeDocument(authoring, source, scope);
  const moddle = new BpmnModdle();
  const result = await moddle.fromXML(projection.xml);
  if (result.warnings.length) throw new Error(JSON.stringify(result.warnings));
  // Isolated actual pinned import module; this does not render a Modeler or qualify its complete import.
  let render: ((event: unknown, context: { definitions: unknown }) => void) | undefined;
  new SubprocessCompatibility({ on(_name: string, _priority: number, handler: typeof render) { render = handler; } } as unknown as ConstructorParameters<typeof SubprocessCompatibility>[0], moddle);
  render!({}, { definitions: result.rootElement });
  const saved = await moddle.toXML(result.rootElement);
  return { projection, baseline: bindBpmnNativeBaseline(authoring, projection, saved.xml), definitions: result.rootElement, moddle };
}
export function deferred<T>() {
  let resolve!: (value: T) => void, reject!: (error: unknown) => void;
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no; });
  return { promise, resolve, reject };
}
