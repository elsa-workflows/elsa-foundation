import type { ActivityNode } from "../workflowTypes";
import { bpmnNamespaces } from "../bpmn/bpmnDocument";

const shape = (id: string, ref: string, x: number, y: number, width: number, height: number, attributes = "", label = "") =>
  `<bpmndi:BPMNShape id="${id}" bpmnElement="${ref}" ${attributes}><dc:Bounds x="${x}" y="${y}" width="${width}" height="${height}"/>${label}</bpmndi:BPMNShape>`;
const label = (x: number, y: number) => `<bpmndi:BPMNLabel vendor:keep="label"><dc:Bounds x="${x}" y="${y}" width="80" height="20"/></bpmndi:BPMNLabel>`;
const edge = (id: string, ref: string, points: number[][], caption = "") => `<bpmndi:BPMNEdge id="${id}" bpmnElement="${ref}" vendor:keep="edge">${points.map(([x, y]) => `<di:waypoint x="${x}" y="${y}" vendor:keep="point"/>`).join("")}${caption}</bpmndi:BPMNEdge>`;

export const documentCanvasXml = `<bpmn:definitions xmlns:bpmn="${bpmnNamespaces.model}" xmlns:bpmndi="${bpmnNamespaces.diagram}" xmlns:dc="${bpmnNamespaces.bounds}" xmlns:di="${bpmnNamespaces.coordinates}" xmlns:tns="urn:canvas" xmlns:vendor="urn:vendor" targetNamespace="urn:canvas" id="Definitions">
<bpmn:process id="P">
 <bpmn:laneSet id="Lanes"><bpmn:lane id="LaneA"><bpmn:flowNodeRef>T</bpmn:flowNodeRef><bpmn:flowNodeRef>Sub</bpmn:flowNodeRef></bpmn:lane>
 <bpmn:lane id="LaneB"><bpmn:childLaneSet id="ChildLanes"><bpmn:lane id="LaneB1"><bpmn:flowNodeRef>O</bpmn:flowNodeRef><bpmn:flowNodeRef>C</bpmn:flowNodeRef></bpmn:lane></bpmn:childLaneSet></bpmn:lane></bpmn:laneSet>
 <bpmn:serviceTask id="T" name="Native task"><bpmn:extensionElements><vendor:config>before <vendor:child ref="T"/> after</vendor:config></bpmn:extensionElements>
 <bpmn:ioSpecification><bpmn:dataInput id="Input"/><bpmn:dataOutput id="Output"/><bpmn:inputSet><bpmn:dataInputRefs>Input</bpmn:dataInputRefs></bpmn:inputSet><bpmn:outputSet><bpmn:dataOutputRefs>Output</bpmn:dataOutputRefs></bpmn:outputSet></bpmn:ioSpecification>
 <bpmn:dataInputAssociation id="IA"><bpmn:sourceRef>DR</bpmn:sourceRef><bpmn:sourceRef>DR2</bpmn:sourceRef><bpmn:targetRef>Input</bpmn:targetRef></bpmn:dataInputAssociation>
 <bpmn:dataOutputAssociation id="OA"><bpmn:sourceRef>Output</bpmn:sourceRef><bpmn:targetRef>DR</bpmn:targetRef></bpmn:dataOutputAssociation></bpmn:serviceTask>
 <bpmn:boundaryEvent id="B" attachedToRef="T" cancelActivity="false"><bpmn:timerEventDefinition/></bpmn:boundaryEvent>
 <bpmn:userTask id="O"/>
 <bpmn:subProcess id="Sub"><bpmn:task id="I"/><bpmn:endEvent id="IE"/><bpmn:sequenceFlow id="IF" sourceRef="I" targetRef="IE"/></bpmn:subProcess>
 <bpmn:boundaryEvent id="SB" attachedToRef="Sub"><bpmn:messageEventDefinition/></bpmn:boundaryEvent>
 <bpmn:subProcess id="C"><bpmn:task id="H"/><bpmn:endEvent id="HE"/><bpmn:sequenceFlow id="HF" sourceRef="H" targetRef="HE"/></bpmn:subProcess>
 <bpmn:dataObject id="Data"/><bpmn:dataObjectReference id="DR" dataObjectRef="Data"/><bpmn:dataObjectReference id="DR2" dataObjectRef="Data"/>
 <bpmn:textAnnotation id="Note"><bpmn:text>Retained note</bpmn:text></bpmn:textAnnotation><bpmn:association id="AN" sourceRef="T" targetRef="Note"/>
 <bpmn:sequenceFlow id="F" sourceRef="T" targetRef="O"/>
</bpmn:process><bpmn:process id="P2"><bpmn:receiveTask id="T2"/></bpmn:process>
<bpmn:collaboration id="Collab"><bpmn:participant id="Pool" processRef="P"/><bpmn:participant id="Pool2" processRef="P2"/><bpmn:messageFlow id="MF" sourceRef="T" targetRef="T2"/></bpmn:collaboration>
<bpmndi:BPMNDiagram id="Diagram"><bpmndi:BPMNPlane id="Plane" bpmnElement="Collab">
${shape("PoolDI", "Pool", 10.25, 20.5, 900, 600, 'isExpanded="true" isHorizontal="true"')}
${shape("LaneADI", "LaneA", 40.25, 20.5, 870, 300)}
${shape("LaneBDI", "LaneB", 40.25, 320.5, 870, 300)}
${shape("LaneB1DI", "LaneB1", 70.25, 320.5, 840, 300)}
${shape("TDI", "T", 100.5, 100.75, 100, 80, "", label(90.5, 185.75))}
${shape("BDI", "B", 182.5, 162.75, 36, 36, "", label(165.5, 205.75))}
${shape("ODI", "O", 600.5, 400.75, 100, 80)}
${shape("SubDI", "Sub", 300.5, 100.75, 250, 160, 'isExpanded="true"')}
${shape("IDI", "I", 320.5, 130.75, 100, 80)}${shape("IEDI", "IE", 500.5, 200.75, 36, 36)}
${shape("SBDI", "SB", 532.5, 242.75, 36, 36)}
${shape("CDI", "C", 400.5, 450.75, 120, 80, 'isExpanded="false"')}
${shape("HDI", "H", 420.5, 460.75, 100, 80)}${shape("HEDI", "HE", 550.5, 460.75, 36, 36)}
${shape("DRDI", "DR", 50.5, 250.75, 48, 64)}${shape("DR2DI", "DR2", 140.5, 250.75, 48, 64)}
${shape("NoteDI", "Note", 750.5, 100.75, 140, 70)}
${shape("Pool2DI", "Pool2", 1100.5, 20.5, 400, 300, 'isExpanded="false"')}${shape("T2DI", "T2", 1150.5, 100.75, 100, 80)}
${edge("FDI", "F", [[200.5, 140.75], [250.5, 140.75], [250.5, 300.75], [550.5, 300.75], [550.5, 440.75], [600.5, 440.75]], label(350.5, 280.75))}
${edge("IFDI", "IF", [[420.5, 170.75], [500.5, 218.75]])}
${edge("HFDI", "HF", [[520.5, 500.75], [550.5, 478.75]])}
${edge("IADI", "IA", [[98.5, 282.75], [100.5, 180.75]])}${edge("OADI", "OA", [[100.5, 180.75], [74.5, 250.75]])}
${edge("ANDI", "AN", [[200.5, 140.75], [750.5, 135.75]])}${edge("MFDI", "MF", [[200.5, 140.75], [1150.5, 140.75]])}
</bpmndi:BPMNPlane></bpmndi:BPMNDiagram>
<bpmndi:BPMNDiagram id="ChildDiagram"><bpmndi:BPMNPlane id="ChildPlane" bpmnElement="C">
${shape("ChildHDI", "H", 20.25, 30.5, 100, 80)}${shape("ChildHEDI", "HE", 200.25, 30.5, 36, 36)}${edge("ChildHFDI", "HF", [[120.25, 70.5], [200.25, 48.5]])}
</bpmndi:BPMNPlane></bpmndi:BPMNDiagram></bpmn:definitions>`;

export function documentCanvasOwner(transform: (xml: string) => string = value => value): ActivityNode {
  return { nodeId: "real-owner", activityVersionId: "exact-bpmn", inputs: [], outputs: [], structure: { kind: "elsa.bpmn.structure", schemaVersion: "2.0.0", payload: {
    documentXml: transform(documentCanvasXml), processId: "P", variables: [], bindings: [{ elementId: "T", childNodeId: "native", listenerNodeId: "listener", vendor: "retained" }],
    activities: [{ nodeId: "native", activityVersionId: "exact-sequence", inputs: [], outputs: [], structure: { kind: "elsa.sequence", schemaVersion: "1", payload: {
      activities: [{ nodeId: "native-leaf", activityVersionId: "exact-inline", inputs: [{ referenceKey: "request", value: { expressionType: "WorkflowRequest", value: { memberKey: "route", vendor: "keep" } } }], outputs: [] }] } } },
    { nodeId: "listener", activityVersionId: "exact-listener", inputs: [], outputs: [], module: { retained: true } }], opaque: { retained: true }
  } } };
}
export const documentCanvasScope = { documentOwnerNodeId: "real-owner", diagramId: "Diagram", scopeElementId: "Collab" };
