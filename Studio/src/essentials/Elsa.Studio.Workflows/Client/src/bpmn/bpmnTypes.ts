// Client-side mirror of the `elsa.bpmn.structure` v1.0.0 payload (see the Elsa.Activities.Bpmn module
// in elsa-foundation, spec 108). Gateways and events are engine-interpreted structure elements; only
// tasks, subprocesses and decision gateways bind an Elsa child through `childNodeId`.

export { bpmnStructureKind } from "./bpmnStructure";

/** Normalize standard modeler names without treating a foreign namespace as a known BPMN shape. */
export function bpmnNotationType(type: string) {
  if (type.startsWith("unknown:")) return type;
  if (type.includes(":") && !type.startsWith("bpmn:")) return `unknown:${type}`;
  const local = type.includes(":") ? type.slice(type.lastIndexOf(":") + 1) : type;
  return local.charAt(0).toLowerCase() + local.slice(1);
}

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
  intermediateCatchEvent: "intermediateCatchEvent",
  intermediateThrowEvent: "intermediateThrowEvent",
  boundaryEvent: "boundaryEvent",
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
  adHocSubProcess: "adHocSubProcess",
  transaction: "transaction",
  callActivity: "callActivity",
  exclusiveGateway: "exclusiveGateway",
  parallelGateway: "parallelGateway",
  inclusiveGateway: "inclusiveGateway",
  complexGateway: "complexGateway",
  eventBasedGateway: "eventBasedGateway",
  dataObject: "dataObject",
  dataObjectReference: "dataObjectReference",
  dataInput: "dataInput",
  dataOutput: "dataOutput",
  dataStoreReference: "dataStoreReference",
  textAnnotation: "textAnnotation",
  group: "group",
  participant: "participant",
  lane: "lane"
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
  return isTaskElementType(elementType) || isSubProcessElementType(elementType) || elementType === bpmnElementTypes.callActivity;
}

export function isSubProcessElementType(elementType: string) {
  return elementType === bpmnElementTypes.subProcess || elementType === bpmnElementTypes.adHocSubProcess || elementType === bpmnElementTypes.transaction;
}

export function isEventElementType(elementType: string) {
  return elementType === bpmnElementTypes.startEvent || elementType === bpmnElementTypes.endEvent
    || elementType === bpmnElementTypes.intermediateCatchEvent || elementType === bpmnElementTypes.intermediateThrowEvent
    || elementType === bpmnElementTypes.boundaryEvent;
}

export function isGatewayElementType(elementType: string) {
  return elementType === bpmnElementTypes.exclusiveGateway
    || elementType === bpmnElementTypes.parallelGateway
    || elementType === bpmnElementTypes.inclusiveGateway
    || elementType === bpmnElementTypes.complexGateway
    || elementType === bpmnElementTypes.eventBasedGateway;
}

export function isDecisionGatewayType(elementType: string) {
  return elementType === bpmnElementTypes.exclusiveGateway || elementType === bpmnElementTypes.inclusiveGateway;
}

export function isBpmnDecisionType(activityTypeKey: string | undefined) {
  return activityTypeKey === "Elsa.Activities.Bpmn.Activities.BpmnDecision";
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
  name?: string;
  properties?: Record<string, string>;
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
