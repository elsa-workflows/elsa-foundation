import type { ActivityCatalogItem, ActivityNode } from "../workflowTypes";
import { bpmnElementTypes, bpmnStructureKind, isTaskElementType, type BpmnElement, type BpmnSequenceFlow } from "../bpmn/bpmnTypes";

type Fields = Record<string, unknown>;
export type BpmnBlockRefuse = (code: string, path: string, message: string) => never;
export interface BpmnBlockRegion {
  elements: BpmnElement[];
  activities: ActivityNode[];
  internal: BpmnSequenceFlow[];
  incoming: BpmnSequenceFlow;
  outgoing: BpmnSequenceFlow;
  laneId: string | null;
}

const record = (value: unknown): value is Fields => value !== null && typeof value === "object" && !Array.isArray(value);
const empty = (value: unknown) => value == null || (Array.isArray(value) ? value.length === 0 : record(value) && Object.keys(value).length === 0);
const identity = (value: unknown): value is string => typeof value === "string" && value.length > 0 && value === value.trim();

/** Strict authored reads: the rendering adapter intentionally tolerates incomplete collections. Edits must not lose them. */
export function readBpmnBlockScope(owner: ActivityNode, refuse: BpmnBlockRefuse) {
  if (owner.structure?.kind !== bpmnStructureKind || owner.structure.schemaVersion !== "1.0.0")
    refuse("unsupported-bpmn-scope", "scope", "Open a supported native BPMN process before refactoring.");
  const payload = owner.structure.payload;
  if (payload.isTransaction != null && payload.isTransaction !== false)
    refuse("transaction-boundary", "scope.isTransaction", "Keep transaction work in its current process.");
  if (!Array.isArray(payload.elements) || !Array.isArray(payload.sequenceFlows) || !Array.isArray(payload.activities))
    refuse("malformed-bpmn", "scope", "Repair the BPMN element, flow and bound-activity collections before refactoring.");
  const seen = new Set<string>();
  const elements = payload.elements.map((raw, index) => {
    const path = `scope.elements[${index}]`;
    if (!record(raw) || !identity(raw.elementId) || !identity(raw.elementType))
      refuse("malformed-bpmn-element", path, "Every BPMN element needs a current identity and type.");
    if (seen.has(raw.elementId)) refuse("duplicate-bpmn-identity", path, "BPMN identities must be unique before refactoring.");
    seen.add(raw.elementId);
    return raw as unknown as BpmnElement;
  });
  const elementIds = new Set(elements.map(element => element.elementId));
  const flows = payload.sequenceFlows.map((raw, index) => {
    const path = `scope.sequenceFlows[${index}]`;
    if (!record(raw) || !identity(raw.flowId) || !identity(raw.sourceRef) || !identity(raw.targetRef))
      refuse("malformed-bpmn-flow", path, "Every BPMN flow needs its identity and both endpoints.");
    if (seen.has(raw.flowId)) refuse("duplicate-bpmn-identity", path, "BPMN element and flow identities must be distinct.");
    seen.add(raw.flowId);
    if (!elementIds.has(raw.sourceRef) || !elementIds.has(raw.targetRef))
      refuse("dangling-bpmn-flow", path, "Repair this BPMN flow's missing endpoint before refactoring.");
    return raw as unknown as BpmnSequenceFlow;
  });
  const children = new Map<string, ActivityNode>();
  for (const [index, raw] of payload.activities.entries()) {
    if (!record(raw) || !identity(raw.nodeId) || !identity(raw.activityVersionId) || !Array.isArray(raw.inputs) || !Array.isArray(raw.outputs))
      refuse("malformed-bpmn-binding", `scope.activities[${index}]`, "Repair this native BPMN activity binding before refactoring.");
    if (children.has(raw.nodeId)) refuse("duplicate-bpmn-binding", `scope.activities[${index}]`, "Native activity identities must be unique.");
    children.set(raw.nodeId, raw as unknown as ActivityNode);
  }
  return { payload, elements, flows, children };
}

function plainElement(element: BpmnElement, refuse: BpmnBlockRefuse) {
  const path = `elements.${element.elementId}`;
  if (element.eventDefinitions != null && !Array.isArray(element.eventDefinitions) || element.cancelActivity != null && typeof element.cancelActivity !== "boolean")
    refuse("malformed-bpmn-element", path, "Repair this BPMN element's event and boundary settings before refactoring.");
  // The backend emits cancelActivity=true even for ordinary tasks; it has meaning only on boundary events.
  if (!empty(element.eventDefinitions) || !empty(element.attachedToRef) || !empty(element.listenerNodeId)
    || element.loopCharacteristics != null || element.isForCompensation != null && element.isForCompensation !== false || !empty(element.compensationHandlerElementId)
    || element.isTransaction != null && element.isTransaction !== false || element.triggeredByEvent != null && element.triggeredByEvent !== false)
    refuse("special-bpmn-boundary", path, "Keep events, listeners, loops, transactions and compensation in their current process.");
  if (element.defaultFlowId != null)
    refuse("conditional-bpmn-region", `${path}.defaultFlowId`, "Choose work with unconditional routing and no default-flow policy.");
  if (!empty(element.properties) && (!record(element.properties) || "elsa.direct.binding" in element.properties
    || Object.keys(element.properties).some(key => key.startsWith("elsa.bpmn.retained-condition:"))))
    refuse("unsupported-bpmn-binding", `${path}.properties`, "Direct-model and retained conditional bindings cannot be extracted as native work.");
}

/** One same-lane native task/subprocess chain. Selection is by canvas element identity, never child-node identity. */
export function readLinearBpmnBlockRegion(owner: ActivityNode, selectedIds: string[], catalog: ActivityCatalogItem[], refuse: BpmnBlockRefuse, depth = 0): BpmnBlockRegion {
  if (depth > 64) refuse("deep-bpmn-region", "scope", "Open a shallower native process before refactoring.");
  const scope = readBpmnBlockScope(owner, refuse);
  const selected = new Set(selectedIds);
  if (!selected.size || selected.size !== selectedIds.length || selectedIds.some(id => !scope.elements.some(element => element.elementId === id)))
    refuse("stale-bpmn-selection", "selectedElementIds", "Select current BPMN elements, without duplicates, before extracting.");
  const elements = scope.elements.filter(element => selected.has(element.elementId));
  const messages = scope.payload.messageFlows;
  if (messages != null && (!Array.isArray(messages) || messages.some(flow => !record(flow))))
    refuse("malformed-bpmn-message-flows", "messageFlows", "Repair the process's message-flow documentation before refactoring.");
  if (Array.isArray(messages) && messages.some(flow => selected.has(flow.sourceElementId) || selected.has(flow.targetElementId)))
    refuse("bpmn-message-boundary", "messageFlows", "Keep documented message-flow endpoints in their current process.");
  const lanes = new Set(elements.map(element => element.laneId ?? null));
  if (lanes.size !== 1) refuse("cross-lane-bpmn-region", "selectedElementIds", "Select work in one lane.");
  const activities = new Map<string, ActivityNode>();
  for (const element of elements) {
    plainElement(element, refuse);
    if (!isTaskElementType(element.elementType) && element.elementType !== bpmnElementTypes.subProcess)
      refuse("unsupported-bpmn-element", `elements.${element.elementId}`, "Select native bound tasks or ordinary subprocesses, without gateways or events.");
    const child = element.childNodeId ? scope.children.get(element.childNodeId) : undefined;
    if (!child || scope.elements.filter(other => other.childNodeId === child.nodeId).length !== 1)
      refuse("unbound-bpmn-task", `elements.${element.elementId}.childNodeId`, "Each selected element must own one native activity binding.");
    const versions = catalog.filter(item => item.activityVersionId === child.activityVersionId);
    if (versions.length !== 1 || versions[0].available === false || versions[0].executionType !== "Action")
      refuse("unavailable-bpmn-binding", `elements.${element.elementId}.childNodeId`, "Resolve this exact native action in the current authorized catalog before refactoring.");
    if (element.elementType === bpmnElementTypes.subProcess ? child.structure?.kind !== bpmnStructureKind : child.structure != null)
      refuse("unsupported-bpmn-binding", `elements.${element.elementId}.childNodeId`, "An ordinary subprocess needs a native BPMN body; structured task bindings need a separate preservation proof.");
    if (element.elementType === bpmnElementTypes.subProcess) readOrdinaryBpmnBody(child, catalog, refuse, depth + 1);
    activities.set(element.elementId, child);
  }
  // A boundary event remains attached to its task even when it is not part of the visual selection.
  if (scope.elements.some(element => typeof element.attachedToRef === "string" && selected.has(element.attachedToRef)))
    refuse("attached-bpmn-event", "selectedElementIds", "A selected task has an attached boundary event. Keep them in their current process.");
  const touching = scope.flows.filter(flow => selected.has(flow.sourceRef) || selected.has(flow.targetRef));
  if (touching.some(flow => !empty(flow.conditionOutcome) || flow.isDefault != null && flow.isDefault !== false
    || !empty(flow.conditionExpression) || scope.elements.some(element => element.defaultFlowId === flow.flowId
      || element.properties?.[`elsa.bpmn.retained-condition:${flow.flowId}`] != null)))
    refuse("conditional-bpmn-region", "sequenceFlows", "Choose a region whose incoming, internal and outgoing flows are unconditional.");
  const incoming = touching.filter(flow => !selected.has(flow.sourceRef));
  const outgoing = touching.filter(flow => !selected.has(flow.targetRef));
  const internal = touching.filter(flow => selected.has(flow.sourceRef) && selected.has(flow.targetRef));
  if (incoming.length !== 1 || outgoing.length !== 1 || internal.length !== selected.size - 1)
    refuse("nonlinear-bpmn-region", "selectedElementIds", "Choose one connected chain with one entry and one normal exit.");
  const ordered: BpmnElement[] = [];
  const visited = new Set<string>();
  let next: string | undefined = incoming[0].targetRef;
  while (next && selected.has(next) && !visited.has(next)) {
    const id: string = next;
    visited.add(id);
    ordered.push(elements.find(element => element.elementId === id)!);
    const before = touching.filter(flow => flow.targetRef === id);
    const after: BpmnSequenceFlow[] = touching.filter(flow => flow.sourceRef === id);
    if (before.length !== 1 || after.length !== 1)
      refuse("nonlinear-bpmn-region", `elements.${id}`, "The selected work branches or joins; choose a linear chain.");
    next = after[0].targetRef;
  }
  if (visited.size !== selected.size || next !== outgoing[0].targetRef)
    refuse("nonlinear-bpmn-region", "selectedElementIds", "The selection contains a cycle or disconnected work.");
  return { elements: ordered, activities: ordered.map(element => activities.get(element.elementId)!), internal, incoming: incoming[0], outgoing: outgoing[0], laneId: elements[0].laneId ?? null };
}

/** Complete ordinary process proof; authored activity-array order alone is never a routing proof. */
export function readOrdinaryBpmnBody(owner: ActivityNode, catalog: ActivityCatalogItem[], refuse: BpmnBlockRefuse, depth = 0) {
  if (depth > 64) refuse("deep-bpmn-region", "scope", "Open a shallower native process before refactoring.");
  const scope = readBpmnBlockScope(owner, refuse);
  const starts = scope.elements.filter(element => element.elementType === "startEvent");
  const ends = scope.elements.filter(element => element.elementType === "endEvent");
  if (starts.length !== 1 || ends.length !== 1 || scope.elements.length !== scope.children.size + 2 || scope.flows.length !== scope.elements.length - 1)
    refuse("nonlinear-bpmn-body", `nodes.${owner.nodeId}`, "Use one complete ordinary process with a single plain start and end.");
  const start = starts[0], end = ends[0];
  for (const event of [start, end]) {
    plainElement(event, refuse);
    if (event.childNodeId != null) refuse("bound-bpmn-event", `elements.${event.elementId}`, "A process boundary event cannot own a task binding.");
  }
  const ids = scope.elements.filter(element => element !== start && element !== end).map(element => element.elementId);
  if (!ids.length) {
    const flow = scope.flows[0];
    if (!flow || flow.sourceRef !== start.elementId || flow.targetRef !== end.elementId || !empty(flow.conditionOutcome) || flow.isDefault)
      refuse("nonlinear-bpmn-body", `nodes.${owner.nodeId}`, "The empty process must connect its plain start directly to its plain end.");
    return { ...scope, start, end, region: null };
  }
  const region = readLinearBpmnBlockRegion(owner, ids, catalog, refuse, depth);
  if (region.incoming.sourceRef !== start.elementId || region.outgoing.targetRef !== end.elementId)
    refuse("nonlinear-bpmn-body", `nodes.${owner.nodeId}`, "The process must execute all its work once between its plain start and end.");
  return { ...scope, start, end, region };
}
