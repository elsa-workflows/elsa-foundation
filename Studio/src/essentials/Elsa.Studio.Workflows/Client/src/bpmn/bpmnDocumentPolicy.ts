import { bpmnNamespaces, resolveBpmnDocumentReference, type BpmnDocumentEdge, type ParsedBpmnDocument } from "./bpmnDocument";
import { bpmnEventDefinitionTypes as bpmnEventDefinitions, isActivityBearingElementType, isEventElementType, isGatewayElementType, isSubProcessElementType } from "./bpmnNotationTypes";

export { bpmnEventDefinitions };

export type BpmnDocumentConnectionKind = "sequenceFlow" | "messageFlow" | "dataInputAssociation" | "dataOutputAssociation" | "association" | "compensationAssociation";
export type BpmnDocumentPolicyResult = { allowed: true } | { allowed: false; reason: string };
export interface BpmnDocumentEndpoint {
  /** All actual semantic references, including data I/O items not drawn as separate shapes. */
  referenceIds: readonly string[];
  displayElementId?: string;
  ownerElementId?: string;
}
export interface BpmnDocumentResizeIntent { width: number; height: number; isExpanded?: boolean; direction?: string }

export function isBpmnModelElement(element: Element | null | undefined): element is Element {
  return element?.namespaceURI === bpmnNamespaces.model;
}
export function bpmnBoolean(element: Element, name: string): boolean {
  return ["true", "1"].includes(element.getAttribute(name) ?? "");
}
export function bpmnFlowScope(element: Element): Element | undefined {
  for (let parent = element.parentElement; parent; parent = parent.parentElement)
    if (isBpmnModelElement(parent) && (parent.localName === "process" || isSubProcessElementType(parent.localName))) return parent;
  return undefined;
}
export function bpmnOwningProcess(element: Element): Element | undefined {
  for (let current: Element | null = element; current; current = current.parentElement)
    if (isBpmnModelElement(current) && current.localName === "process") return current;
  return undefined;
}
function activity(element: Element) { return isBpmnModelElement(element) && isActivityBearingElementType(element.localName); }
function flowNode(element: Element) { return activity(element) || isBpmnModelElement(element) && (isEventElementType(element.localName) || isGatewayElementType(element.localName)); }
function eventSubprocess(element: Element) { return isSubProcessElementType(element.localName) && bpmnBoolean(element, "triggeredByEvent"); }
function definition(element: Element, type: string) { return bpmnEventDefinitions(element).includes(`${type}EventDefinition`); }
function throwing(element: Element) { return ["endEvent", "intermediateThrowEvent"].includes(element.localName); }
function catching(element: Element) { return ["startEvent", "intermediateCatchEvent", "boundaryEvent"].includes(element.localName); }
function dataReference(element: Element) { return ["dataObjectReference", "dataStoreReference"].includes(element.localName); }
function organization(parsed: ParsedBpmnDocument, element: Element): Element | undefined {
  return element.localName === "participant" ? resolveBpmnDocumentReference(parsed, element, element.getAttribute("processRef")) ?? element : bpmnOwningProcess(element);
}
function inputAssociation(source: Element, target: Element) { return dataReference(source) && (activity(target) || throwing(target)); }
function outputAssociation(source: Element, target: Element) { return (activity(source) || catching(source)) && dataReference(target); }
const allow: BpmnDocumentPolicyResult = { allowed: true };
const refuse = (reason: string): BpmnDocumentPolicyResult => ({ allowed: false, reason });

/** Independent standard-source policy; it does not import the optional Modeler or grant execution admission. */
export function assessBpmnDocumentConnection(parsed: ParsedBpmnDocument, kind: BpmnDocumentConnectionKind, sourceId: string, targetId: string): BpmnDocumentPolicyResult {
  const source = parsed.byId.get(sourceId), target = parsed.byId.get(targetId);
  if (!isBpmnModelElement(source) || !isBpmnModelElement(target)) return refuse("Connection endpoints must be retained standard BPMN elements.");
  if (kind === "sequenceFlow") {
    if (!flowNode(source) || !flowNode(target)) return refuse("Sequence flow endpoints must be flow nodes.");
    const scope = bpmnFlowScope(source);
    if (!scope || scope !== bpmnFlowScope(target)) return refuse("Sequence flows must stay in one standard flow scope.");
    if (source.localName === "endEvent" || target.localName === "startEvent" || target.localName === "boundaryEvent") return refuse("This event does not accept the requested sequence-flow direction.");
    if ([source, target].some(element => eventSubprocess(element) || bpmnBoolean(element, "isForCompensation"))
      || source.localName === "boundaryEvent" && definition(source, "compensate")) return refuse("Event subprocesses and compensation work use their own relationships.");
    if (source.localName === "intermediateThrowEvent" && definition(source, "link") || target.localName === "intermediateCatchEvent" && definition(target, "link"))
      return refuse("Link events do not accept a sequence flow on this side.");
    if (source.localName === "eventBasedGateway" && target.localName !== "receiveTask"
      && !(target.localName === "intermediateCatchEvent" && ["message", "timer", "conditional", "signal"].some(type => definition(target, type))))
      return refuse("An event-based gateway needs a receive task or a supported catching event.");
    return allow; // Legal flow-node self-loops are not rejected by a generic unequal-ID rule.
  }
  if (kind === "messageFlow") {
    const sourceOrganization = organization(parsed, source), targetOrganization = organization(parsed, target);
    if (!sourceOrganization || !targetOrganization || sourceOrganization === targetOrganization) return refuse("Message flows must connect different organizational processes or pools.");
    const messageOrNone = (element: Element) => bpmnEventDefinitions(element).every(type => type === "messageEventDefinition");
    const sourceAllowed = activity(source) || source.localName === "participant" || throwing(source) && messageOrNone(source);
    const targetAllowed = !bpmnBoolean(target, "isForCompensation") && (activity(target) || target.localName === "participant"
      || catching(target) && messageOrNone(target) && (target.localName !== "boundaryEvent" || definition(target, "message")));
    return sourceAllowed && targetAllowed ? allow : refuse("The endpoint types or event definitions do not permit this message-flow direction.");
  }
  if (kind === "dataInputAssociation") return inputAssociation(source, target) ? allow : refuse("A data input association connects a data reference to an activity or throwing event.");
  if (kind === "dataOutputAssociation") return outputAssociation(source, target) ? allow : refuse("A data output association connects an activity or catching event to a data reference.");
  if (kind === "compensationAssociation") {
    const scope = bpmnFlowScope(source);
    const host = resolveBpmnDocumentReference(parsed, source, source.getAttribute("attachedToRef"));
    return source.localName === "boundaryEvent" && definition(source, "compensate") && activity(target) && !eventSubprocess(target)
      && scope !== undefined && scope === bpmnFlowScope(target) && host !== target ? allow : refuse("Compensation needs a boundary event and a different activity in the same scope.");
  }
  if (kind !== "association") return refuse("This BPMN connection kind is unavailable.");
  if (source.contains(target) || target.contains(source)) return refuse("An association cannot connect an ancestor and its descendant.");
  const oneAnnotation = (source.localName === "textAnnotation") !== (target.localName === "textAnnotation");
  return oneAnnotation || inputAssociation(source, target) || outputAssociation(source, target) ? allow : refuse("Use a typed BPMN relationship for these endpoints.");
}

export function assessBpmnBoundaryAttachment(parsed: ParsedBpmnDocument, boundaryId: string, hostId: string): BpmnDocumentPolicyResult {
  const boundary = parsed.byId.get(boundaryId), host = parsed.byId.get(hostId);
  if (!isBpmnModelElement(boundary) || boundary.localName !== "boundaryEvent" || !isBpmnModelElement(host) || !activity(host))
    return refuse("A boundary event needs an activity host.");
  if (eventSubprocess(host) || bpmnBoolean(host, "isForCompensation")) return refuse("This activity cannot host a boundary event.");
  if (host.localName === "receiveTask") {
    for (const element of parsed.byId.values()) {
      if (element.localName !== "sequenceFlow" || resolveBpmnDocumentReference(parsed, element, element.getAttribute("targetRef")) !== host) continue;
      if (resolveBpmnDocumentReference(parsed, element, element.getAttribute("sourceRef"))?.localName === "eventBasedGateway")
        return refuse("A receive task after an event-based gateway cannot host a boundary event.");
    }
  }
  return allow;
}

/** Native resize availability follows the pinned toolbox; explicit source DI edits are a separate operation. */
export function assessBpmnDocumentResize(element: Element, intent: BpmnDocumentResizeIntent): BpmnDocumentPolicyResult {
  if (!isBpmnModelElement(element) || !Number.isFinite(intent.width) || !Number.isFinite(intent.height) || intent.width <= 0 || intent.height <= 0)
    return refuse("Resize requires a standard shape and finite positive dimensions.");
  if (isSubProcessElementType(element.localName)) return intent.isExpanded === true && intent.width >= 100 && intent.height >= 80
    ? allow : refuse("Native subprocess resize requires an expanded shape of at least 100 by 80.");
  if (["lane", "participant", "group"].includes(element.localName)) return allow;
  if (element.localName === "textAnnotation") return !intent.direction || ["e", "w"].includes(intent.direction) ? allow : refuse("Text annotation resize uses its east or west handle.");
  return refuse("This notation has no native resize action; use an explicit authored DI edit if required.");
}

/** Semantic item references remain intact while the view draws data associations at the activity/event owner. */
export function bpmnDocumentDisplayEndpoints(parsed: ParsedBpmnDocument, edge: BpmnDocumentEdge): { source: BpmnDocumentEndpoint; target: BpmnDocumentEndpoint } {
  const element = parsed.byId.get(edge.elementId) ?? edge.element;
  const owner = element.parentElement;
  const ownerElementId = isBpmnModelElement(owner) && (activity(owner) || isEventElementType(owner.localName)) ? owner.getAttribute("id") ?? undefined : undefined;
  const source: BpmnDocumentEndpoint = { referenceIds: edge.sourceRefs, displayElementId: edge.sourceRefs[0] };
  const target: BpmnDocumentEndpoint = { referenceIds: edge.targetRefs, displayElementId: edge.targetRefs[0] };
  if (edge.elementType === "dataInputAssociation") { target.displayElementId = ownerElementId; target.ownerElementId = ownerElementId; }
  if (edge.elementType === "dataOutputAssociation") { source.displayElementId = ownerElementId; source.ownerElementId = ownerElementId; }
  return { source, target };
}
