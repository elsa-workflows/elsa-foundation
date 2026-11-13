import type { ActivityNode } from "../workflowTypes";
import { bpmnNamespaces as ns, type BpmnScopeAddress } from "../bpmn/bpmnDocument";

export const operationScope: BpmnScopeAddress = { documentOwnerNodeId: "owner", diagramId: "Diagram", scopeElementId: "P" };
export function operationOwner(): ActivityNode {
  const shape = (id: string, x: number, y: number, width = 100, height = 80, extra = "") => `<bd:BPMNShape id="${id}DI" bpmnElement="${id}" ${extra}><dc:Bounds x="${x}" y="${y}" width="${width}" height="${height}"/></bd:BPMNShape>`;
  return { nodeId: "owner", activityVersionId: "bpmn@exact", inputs: [], outputs: [], structure: { kind: "elsa.bpmn.structure", schemaVersion: "2.0.0", payload: {
    processId: "P", variables: [],
    activities: [{ nodeId: "native", activityVersionId: "sequence@exact", inputs: [], outputs: [], structure: { kind: "elsa.sequence", schemaVersion: "1.0.0", payload: { activities: [
      { nodeId: "inline", activityVersionId: "inline@exact", inputs: [{ name: "Expression", expression: { type: "ActivityResult", value: { producerNodeId: "producer", producerScopeId: "native", projectionKey: "$result", vendor: { exact: true } } } }], outputs: [] }
    ] } } }], bindings: [{ elementId: "A", childNodeId: "native", vendor: { keep: "exact" } }],
    documentXml: `<b:definitions xmlns:b="${ns.model}" xmlns:q="urn:test" xmlns:bd="${ns.diagram}" xmlns:dc="${ns.bounds}" xmlns:di="${ns.coordinates}" xmlns:v="urn:vendor" targetNamespace="urn:test">
<b:process id="P" isExecutable="false"><b:extensionElements><v:keep id="task">before<v:child/>after</v:keep></b:extensionElements>
<b:task id="A" name="Bound"><b:extensionElements><v:exact qname="q:A"/></b:extensionElements><b:outgoing>q:F</b:outgoing></b:task><b:task id="B"><b:incoming>q:F</b:incoming></b:task>
<b:exclusiveGateway id="G"/><b:subProcess id="S"><b:task id="Child"/></b:subProcess><b:transaction id="TX"/><b:subProcess id="ES" triggeredByEvent="true"/>
<b:dataObject id="Object"/><b:dataObjectReference id="Data" dataObjectRef="Object"/><b:sequenceFlow id="F" sourceRef="A" targetRef="B"/><b:textAnnotation id="Note"><b:text>Original annotation</b:text></b:textAnnotation></b:process>
<bd:BPMNDiagram id="Diagram"><bd:BPMNPlane id="Plane" bpmnElement="P">${shape("A", 100.25, 100.5)}${shape("B", 350.25, 100.5)}${shape("G", 550, 120, 50, 50)}${shape("S", 100, 300, 100, 80, 'isExpanded="false"')}${shape("TX", 300, 300, 300, 180, 'isExpanded="true"')}${shape("ES", 650, 300, 300, 180, 'isExpanded="true"')}${shape("Data", 250, 20, 50, 64)}${shape("Note", 20, 550, 140, 70)}<bd:BPMNEdge id="FDI" bpmnElement="F"><di:waypoint x="200.25" y="140.5"/><di:waypoint x="350.25" y="140.5"/></bd:BPMNEdge></bd:BPMNPlane></bd:BPMNDiagram>
<bd:BPMNDiagram id="SubDiagram"><bd:BPMNPlane id="SubPlane" bpmnElement="S">${shape("Child", 20, 20)}</bd:BPMNPlane></bd:BPMNDiagram>
<bd:BPMNDiagram id="TxDiagram"><bd:BPMNPlane id="TxPlane" bpmnElement="TX"/></bd:BPMNDiagram><bd:BPMNDiagram id="EventDiagram"><bd:BPMNPlane id="EventPlane" bpmnElement="ES"/></bd:BPMNDiagram>
</b:definitions>`
  } } };
}
