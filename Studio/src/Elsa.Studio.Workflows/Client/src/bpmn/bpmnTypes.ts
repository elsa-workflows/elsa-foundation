// Client-side mirror of the `elsa.bpmn.structure` v1.0.0 payload (see the Elsa.Activities.Bpmn module
// in elsa-foundation, spec 108). Gateways and events are engine-interpreted structure elements; only
// task-family and subProcess elements bind an Elsa child activity through `childNodeId`.

export const bpmnStructureKind = "elsa.bpmn.structure";

export interface BpmnEventDefinition {
  type: string;
  properties?: Record<string, string>;
}

export interface BpmnElement {
  elementId: string;
  elementType: string;
  name?: string | null;
  childNodeId?: string | null;
  laneId?: string | null;
  defaultFlowId?: string | null;
  eventDefinitions?: BpmnEventDefinition[];
  properties?: Record<string, string>;
  [key: string]: unknown;
}

export interface BpmnSequenceFlow {
  flowId: string;
  sourceRef: string;
  targetRef: string;
  name?: string | null;
  conditionOutcome?: string | null;
  isDefault?: boolean;
  [key: string]: unknown;
}

export const bpmnElementTypes = {
  startEvent: "startEvent",
  endEvent: "endEvent",
  task: "task",
  userTask: "userTask",
  serviceTask: "serviceTask",
  scriptTask: "scriptTask",
  manualTask: "manualTask",
  businessRuleTask: "businessRuleTask",
  sendTask: "sendTask",
  receiveTask: "receiveTask",
  subProcess: "subProcess",
  exclusiveGateway: "exclusiveGateway",
  parallelGateway: "parallelGateway",
  inclusiveGateway: "inclusiveGateway"
} as const;

const taskElementTypes: ReadonlySet<string> = new Set([
  bpmnElementTypes.task,
  bpmnElementTypes.userTask,
  bpmnElementTypes.serviceTask,
  bpmnElementTypes.scriptTask,
  bpmnElementTypes.manualTask,
  bpmnElementTypes.businessRuleTask,
  bpmnElementTypes.sendTask,
  bpmnElementTypes.receiveTask
]);

export function isTaskElementType(elementType: string) {
  return taskElementTypes.has(elementType);
}

export function isActivityBearingElementType(elementType: string) {
  return isTaskElementType(elementType) || elementType === bpmnElementTypes.subProcess;
}

export function isEventElementType(elementType: string) {
  return elementType === bpmnElementTypes.startEvent || elementType === bpmnElementTypes.endEvent;
}

export function isGatewayElementType(elementType: string) {
  return elementType === bpmnElementTypes.exclusiveGateway
    || elementType === bpmnElementTypes.parallelGateway
    || elementType === bpmnElementTypes.inclusiveGateway;
}

export function isTerminateEndEvent(element: BpmnElement) {
  return element.elementType === bpmnElementTypes.endEvent
    && (element.eventDefinitions ?? []).some(definition => definition.type === "terminate");
}

// A pure BPMN shape the palette can stamp onto the canvas (no bound Elsa activity).
export interface BpmnShapeDescriptor {
  key: string;
  label: string;
  elementType: string;
  eventDefinitions?: BpmnEventDefinition[];
}

export const bpmnShapePalette: BpmnShapeDescriptor[] = [
  { key: "startEvent", label: "Start event", elementType: bpmnElementTypes.startEvent },
  { key: "endEvent", label: "End event", elementType: bpmnElementTypes.endEvent },
  { key: "terminateEndEvent", label: "Terminate end event", elementType: bpmnElementTypes.endEvent, eventDefinitions: [{ type: "terminate" }] },
  { key: "exclusiveGateway", label: "Exclusive gateway", elementType: bpmnElementTypes.exclusiveGateway },
  { key: "parallelGateway", label: "Parallel gateway", elementType: bpmnElementTypes.parallelGateway },
  { key: "inclusiveGateway", label: "Inclusive gateway", elementType: bpmnElementTypes.inclusiveGateway },
  { key: "task", label: "Task (unbound)", elementType: bpmnElementTypes.task }
];

export function bpmnElementTypeLabel(element: BpmnElement) {
  if (isTerminateEndEvent(element)) return "Terminate end event";
  const raw = element.elementType;
  const spaced = raw.replace(/([a-z0-9])([A-Z])/g, "$1 $2").toLowerCase();
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}
