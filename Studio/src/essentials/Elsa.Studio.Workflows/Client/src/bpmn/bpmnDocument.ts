import type { ActivityNode, VariableDefinition } from "../workflowTypes";
import { getChildSlots } from "../workflowAdapter";
import { bpmnStructureKind } from "./bpmnTypes";

/** Complete-source authoring contract. No persisted flat graph or second diagram store. */
export const bpmnDocumentSchemaVersion = "2.0.0";
export const bpmnNamespaces = {
  model: "http://www.omg.org/spec/BPMN/20100524/MODEL",
  diagram: "http://www.omg.org/spec/BPMN/20100524/DI",
  bounds: "http://www.omg.org/spec/DD/20100524/DC",
  coordinates: "http://www.omg.org/spec/DD/20100524/DI"
} as const;

export interface BpmnDocumentBinding {
  elementId: string;
  childNodeId?: string | null;
  listenerNodeId?: string | null;
  [key: string]: unknown;
}

export interface BpmnScopeHeader extends Record<string, unknown> {
  scopeElementId: string;
  nodeId: string;
  activityVersionId: string;
  inputs: ActivityNode["inputs"];
  outputs: ActivityNode["outputs"];
  variables: VariableDefinition[];
  executionPlacement?: "inline" | "background";
}

export interface BpmnDocumentPayload extends Record<string, unknown> {
  documentXml: string;
  processId: string;
  activities: ActivityNode[];
  bindings: BpmnDocumentBinding[];
  variables: VariableDefinition[];
  scopeHeaders?: BpmnScopeHeader[];
}

/** Only disjoint Elsa configuration can accompany a standard XML edit. */
export interface BpmnDocumentConfigurationChange {
  activities?: ActivityNode[];
  bindings?: BpmnDocumentBinding[];
  variables?: VariableDefinition[];
  scopeHeaders?: BpmnScopeHeader[];
}

export interface BpmnDocumentTransaction {
  commands?: readonly BpmnDocumentCommand[];
  configuration?: BpmnDocumentConfigurationChange;
}

/** Explicit root addressing never invents a persisted ID for idless definitions. */
export type BpmnXmlAddress = ({ elementId: string; documentRoot?: never } | { documentRoot: true; elementId?: never }) & {
  /** Addresses idless standard properties without inventing persisted element identities. */
  path?: { namespaceURI: string; localName: string; index?: number }[];
};

export interface BpmnScopeAddress {
  documentOwnerNodeId: string;
  diagramId: string;
  scopeElementId: string;
}

export interface BpmnDocumentSelection {
  elementId: string;
  diId?: string;
  property?: BpmnXmlAddress;
}

export interface BpmnBounds { x: number; y: number; width: number; height: number }
export interface BpmnPoint { x: number; y: number }

export type BpmnDocumentCommand =
  | { kind: "namespace"; target: BpmnXmlAddress; prefix: string; namespaceURI: string }
  | { kind: "attribute"; target: BpmnXmlAddress; name: string; namespaceURI?: string; value: string | null }
  | { kind: "text"; target: BpmnXmlAddress; value: string }
  | { kind: "append"; parent: BpmnXmlAddress; xml: string }
  | { kind: "insert"; parent: BpmnXmlAddress; xml: string; before?: BpmnXmlAddress }
  | { kind: "remove"; target: BpmnXmlAddress }
  | { kind: "reparent"; target: BpmnXmlAddress; parent: BpmnXmlAddress; before?: BpmnXmlAddress | null }
  | { kind: "replaceType"; target: BpmnXmlAddress; localName: string }
  | { kind: "reconnect"; elementId: string; sourceRef: string; targetRef: string }
  | { kind: "bounds"; shapeId: string; bounds: BpmnBounds }
  | { kind: "labelBounds"; diId: string; bounds: BpmnBounds | null }
  | { kind: "dataReferences"; elementId: string; sourceRefs: string[]; targetRef: string }
  | { kind: "waypoints"; edgeId: string; points: BpmnPoint[]; sourceIndices?: (number | null)[] };

export interface ParsedBpmnDocument {
  payload: BpmnDocumentPayload;
  document: XMLDocument;
  byId: ReadonlyMap<string, Element>;
  process: Element;
}

const identityNamespaces: ReadonlySet<string> = new Set(Object.values(bpmnNamespaces));
const competingFields = ["elements", "sequenceFlows", "diagram", "pools", "lanes", "messageFlows", "isTransaction"];
const bindableFlowNodeTypes = new Set(["task", "userTask", "serviceTask", "manualTask", "scriptTask", "businessRuleTask", "sendTask", "receiveTask",
  "callActivity", "subProcess", "adHocSubProcess", "transaction", "startEvent", "endEvent", "boundaryEvent", "intermediateCatchEvent", "intermediateThrowEvent",
  "exclusiveGateway", "inclusiveGateway", "parallelGateway", "eventBasedGateway", "complexGateway"]);
const scopeTypes = new Set(["process", "subProcess", "adHocSubProcess", "transaction"]);

function fail(message: string): never { throw new Error(`Cannot edit BPMN document: ${message}.`); }
function object(value: unknown, name: string): Record<string, unknown> {
  if (!value || typeof value !== "object" || Array.isArray(value)) fail(`${name} must be an object`);
  return value as Record<string, unknown>;
}
function array(value: unknown, name: string): unknown[] {
  if (!Array.isArray(value)) fail(`${name} must be an array`);
  return value;
}
function requiredText(value: unknown, name: string): string {
  if (typeof value !== "string" || !value.trim()) fail(`${name} is missing`);
  return value;
}
function is(element: Element, namespaceURI: string, localName: string) {
  return element.namespaceURI === namespaceURI && element.localName === localName;
}
function children(element: Element, namespaceURI: string, localName: string): Element[] {
  return [...element.children].filter(child => is(child, namespaceURI, localName));
}
function parseXml(xml: string): XMLDocument {
  // BPMN source has no need for DTD/entity expansion. Never fetch an external document.
  const markup = xml.replace(/<!--[\s\S]*?-->|<!\[CDATA\[[\s\S]*?\]\]>|<\?[\s\S]*?\?>/g, "");
  if (/<!\s*(?:DOCTYPE|ENTITY)\b/i.test(markup)) fail("DTD and entity declarations are not supported");
  const document = new DOMParser().parseFromString(xml, "application/xml");
  if (document.getElementsByTagNameNS("http://www.mozilla.org/newlayout/xml/parsererror.xml", "parsererror").length
    || document.documentElement.localName === "parsererror") fail("XML is malformed");
  return document;
}
function validNcName(document: XMLDocument, name: string): boolean {
  if (name.includes(":")) return false;
  // XML Name validation plus colon exclusion is NCName validation. Do not apply namespace
  // usage constraints to ID values: "xmlns" is a valid ID, and Unicode extenders are legal.
  try { document.createElement(name); return true; } catch { return false; }
}
function indexDocument(document: XMLDocument): Map<string, Element> {
  const byId = new Map<string, Element>();
  for (const element of [...document.getElementsByTagName("*")]) {
    if (!identityNamespaces.has(element.namespaceURI ?? "")) continue;
    const id = element.getAttribute("id");
    if (!id) continue;
    if (!validNcName(document, id)) fail(`identity '${id}' is not an XML ID`);
    if (byId.has(id)) fail(`identity '${id}' is duplicated`);
    byId.set(id, element);
  }
  return byId;
}
function validateVariables(variables: VariableDefinition[]) {
  const keys = new Set<string>();
  for (const variable of variables) {
    object(variable, "variable");
    const key = requiredText(variable.referenceKey, "variable referenceKey");
    object(variable.type, "variable type");
    if (keys.has(key)) fail(`variable '${key}' is duplicated`);
    keys.add(key);
  }
}

function rejectPhysicalScopeViews(activities: ActivityNode[]) {
  const pending = [...activities], visited = new Set<ActivityNode>();
  while (pending.length) {
    const activity = pending.pop()!;
    if (visited.has(activity)) continue;
    visited.add(activity);
    if (activity.structure?.kind === "elsa.bpmn.scope-view") fail("projected scope structures cannot be physically authored");
    // Use native ownership slots, never arbitrary input values or contributed metadata objects.
    pending.push(...getChildSlots(activity).flatMap(slot => slot.activities));
  }
}

function hasStandardScopeChain(element: Element, definitions: Element): boolean {
  for (let current: Element | null = element; current?.namespaceURI === bpmnNamespaces.model && scopeTypes.has(current.localName); current = current.parentElement)
    if (current.localName === "process") return current.parentElement === definitions;
  return false;
}

/** Parse without rewriting bytes. Opening, switching or a no-op Save never migrates the source. */
export function readBpmnDocument(owner: ActivityNode): ParsedBpmnDocument {
  if (owner.structure?.kind !== bpmnStructureKind || owner.structure.schemaVersion !== bpmnDocumentSchemaVersion)
    fail("complete-document authoring version is unavailable");
  const raw = object(owner.structure.payload, "payload");
  if (competingFields.some(field => Object.hasOwn(raw, field))) fail("payload has competing graph or diagram authorities");
  const documentXml = requiredText(raw.documentXml, "documentXml");
  const processId = requiredText(raw.processId, "processId");
  const activities = array(raw.activities, "activities").map(value => object(value, "activity") as ActivityNode);
  const bindings = array(raw.bindings, "bindings").map(value => object(value, "binding") as unknown as BpmnDocumentBinding);
  const variables = array(raw.variables, "variables") as VariableDefinition[];
  const document = parseXml(documentXml);
  if (!is(document.documentElement, bpmnNamespaces.model, "definitions")) fail("source must be BPMN definitions");
  const byId = indexDocument(document);
  const process = byId.get(processId);
  if (!process || !is(process, bpmnNamespaces.model, "process") || process.parentElement !== document.documentElement)
    fail(`selected process '${processId}' is unavailable`);
  const activityIds = new Set<string>();
  for (const activity of activities) {
    const nodeId = requiredText(activity.nodeId, "activity nodeId");
    requiredText(activity.activityVersionId, "activity version");
    array(activity.inputs, "activity inputs");
    array(activity.outputs, "activity outputs");
    if (activityIds.has(nodeId) || nodeId === owner.nodeId) fail(`activity '${nodeId}' is duplicated`);
    activityIds.add(nodeId);
  }
  rejectPhysicalScopeViews(activities);
  const headers = raw.scopeHeaders === undefined ? [] : array(raw.scopeHeaders, "scopeHeaders")
    .map(value => object(value, "scope header") as BpmnScopeHeader);
  const headersByScope = new Map<string, BpmnScopeHeader>();
  const configuredIds = new Set(activityIds);
  for (const header of headers) {
    const scopeId = requiredText(header.scopeElementId, "scopeElementId");
    const scope = byId.get(scopeId);
    if (!scope || scope.namespaceURI !== bpmnNamespaces.model || !scopeTypes.has(scope.localName) || scope === process)
      fail(`scope header '${scopeId}' must address another standard process or body`);
    if (!hasStandardScopeChain(scope, document.documentElement)) fail("configured descendants require a continuous standard scope chain");
    if (headersByScope.has(scopeId)) fail(`scope '${scopeId}' has duplicate headers`);
    const nodeId = requiredText(header.nodeId, "scope nodeId");
    if (configuredIds.has(nodeId) || nodeId === owner.nodeId) fail(`native scope identity '${nodeId}' is duplicated`);
    configuredIds.add(nodeId);
    requiredText(header.activityVersionId, "scope activity version");
    array(header.inputs, "scope inputs"); array(header.outputs, "scope outputs");
    validateVariables(array(header.variables, "scope variables") as VariableDefinition[]);
    if (header.executionPlacement !== undefined && !["inline", "background"].includes(header.executionPlacement)) fail("scope execution placement is invalid");
    if ([...competingFields, "structure", "documentXml", "processId", "activities", "bindings", "scopeHeaders"].some(field => Object.hasOwn(header, field)))
      fail("scope header must not persist a second structure or semantic document");
    headersByScope.set(scopeId, header);
  }
  const requireScopeChain = (scope: Element | null) => {
    for (let current = scope; current && current !== document.documentElement; current = current.parentElement) {
      if (current.namespaceURI !== bpmnNamespaces.model || !scopeTypes.has(current.localName) || current === process) continue;
      if (!headersByScope.has(current.getAttribute("id") ?? "")) fail("configured descendants require their complete standard scope header chain");
    }
  };
  for (const header of headers) requireScopeChain(byId.get(header.scopeElementId)!.parentElement);
  const boundElements = new Set<string>();
  const boundActivities = new Set<string>();
  for (const binding of bindings) {
    const elementId = requiredText(binding.elementId, "binding elementId");
    const element = byId.get(elementId);
    if (!element || element.namespaceURI !== bpmnNamespaces.model) fail(`bound element '${elementId}' is unavailable`);
    if (!bindableFlowNodeTypes.has(element.localName) || element.parentElement?.namespaceURI !== bpmnNamespaces.model
      || !scopeTypes.has(element.parentElement.localName)) fail("native bindings require a direct standard flow node");
    requireScopeChain(element.parentElement);
    if (competingFields.some(field => Object.hasOwn(binding, field))) fail("binding has competing graph or diagram authorities");
    if (boundElements.has(elementId)) fail(`element '${elementId}' has duplicate binding entries`);
    boundElements.add(elementId);
    const references = [binding.childNodeId, binding.listenerNodeId].filter(value => value !== undefined && value !== null);
    if (!references.length) fail(`element '${elementId}' has an empty binding`);
    for (const reference of references) {
      if (typeof reference !== "string" || !activityIds.has(reference)) fail(`element '${elementId}' has an unavailable native binding`);
      if (boundActivities.has(reference)) fail(`activity '${reference}' is bound more than once`);
      if (["subProcess", "adHocSubProcess", "transaction"].includes(element.localName)
        && activities.find(activity => activity.nodeId === reference)?.structure?.kind === bpmnStructureKind)
        fail("standard subprocess cannot also persist an independent BPMN activity body; scope projection is not qualified");
      boundActivities.add(reference);
    }
  }
  if (activities.some(activity => !boundActivities.has(activity.nodeId))) fail("native activity has no binding");
  validateVariables(variables);
  return { payload: { ...raw, documentXml, processId, activities, bindings, variables,
    ...(raw.scopeHeaders !== undefined ? { scopeHeaders: headers } : {}) }, document, byId, process };
}

/** QName references may qualify a local ID with the document's target namespace. */
export function resolveBpmnDocumentReference(parsed: ParsedBpmnDocument, context: Element, reference: string | null): Element | undefined {
  if (!reference) return undefined;
  const value = reference.trim();
  if (!value.includes(":")) return parsed.byId.get(value);
  const parts = value.split(":");
  if (parts.length !== 2 || context.lookupNamespaceURI(parts[0]) !== parsed.document.documentElement.getAttribute("targetNamespace")) return undefined;
  return parsed.byId.get(parts[1]);
}

function resolve(parsed: ParsedBpmnDocument, address: BpmnXmlAddress): Element {
  if (address.documentRoot !== undefined && (address.documentRoot !== true || address.elementId !== undefined))
    fail("XML address must select either the document root or an element identity");
  let element = address.documentRoot ? parsed.document.documentElement : parsed.byId.get(requiredText(address.elementId, "XML address elementId"));
  if (!element) fail(`element '${address.elementId}' is unavailable`);
  for (const step of address.path ?? []) {
    element = children(element, step.namespaceURI, step.localName)[step.index ?? 0];
    if (!element) fail(`property '${step.localName}' is unavailable on '${address.elementId}'`);
  }
  return element;
}
function coordinate(value: number) {
  if (!Number.isFinite(value)) fail("geometry must use finite coordinates");
  return String(value);
}
function writeBounds(element: Element, bounds: BpmnBounds) {
  if (bounds.width <= 0 || bounds.height <= 0) fail("shape dimensions must be positive");
  for (const [name, value] of Object.entries(bounds)) {
    coordinate(value);
    if (!element.hasAttribute(name) || Number(element.getAttribute(name)) !== value) element.setAttribute(name, String(value));
  }
}
function uniqueChild(parent: Element, namespaceURI: string, localName: string): Element {
  const matches = children(parent, namespaceURI, localName);
  if (matches.length !== 1) fail(`'${parent.getAttribute("id")}' needs exactly one ${localName}`);
  return matches[0];
}
function xmlBaseChain(element: Element | null): string[] {
  const values: string[] = [];
  for (let current = element; current; current = current.parentElement) {
    const value = current.getAttributeNS("http://www.w3.org/XML/1998/namespace", "base");
    if (value !== null) values.unshift(value);
  }
  return values;
}
function preserveXmlContext(element: Element, destination: Element) {
  const xmlNamespace = "http://www.w3.org/XML/1998/namespace";
  if (JSON.stringify(xmlBaseChain(element.parentElement)) !== JSON.stringify(xmlBaseChain(destination))) {
    let resolved: string | null = null;
    for (const value of xmlBaseChain(element)) {
      if (/^[A-Za-z][A-Za-z0-9+.-]*:/.test(value)) {
        try { resolved = new URL(value).href; } catch { resolved = null; }
      } else if (resolved) {
        try { resolved = new URL(value, resolved).href; } catch { resolved = null; }
      }
    }
    if (!resolved) fail("move changes an unresolved XML base; an explicit source-base transformation is required");
    element.setAttributeNS(xmlNamespace, "xml:base", resolved);
  }
  for (const [name, fallback] of [["lang", ""], ["space", "default"]]) {
    let value: string | null = null;
    for (let current: Element | null = element; current && value === null; current = current.parentElement)
      value = current.getAttributeNS(xmlNamespace, name);
    let destinationValue: string | null = null;
    for (let current: Element | null = destination; current && destinationValue === null; current = current.parentElement)
      destinationValue = current.getAttributeNS(xmlNamespace, name);
    if ((value ?? fallback) !== (destinationValue ?? fallback)) element.setAttributeNS(xmlNamespace, `xml:${name}`, value ?? fallback);
  }
}
function rejectOpaqueQNameResolution(target: Element, prefix: string) {
  for (const element of [target, ...target.getElementsByTagName("*")]) {
    if (element.lookupNamespaceURI(prefix)) continue;
    const values = [...element.attributes].map(attribute => attribute.value);
    values.push(...[...element.childNodes].filter(node => node.nodeType === Node.TEXT_NODE || node.nodeType === Node.CDATA_SECTION_NODE).map(node => node.textContent ?? ""));
    if (values.some(value => value.includes(`${prefix}:`))) fail("namespace declaration would resolve retained opaque QName content");
  }
}
function preserveNamespaceContext(element: Element, destination: Element) {
  // Destination-only prefixes cannot be undeclared in XML 1.0. Refuse a move that would give
  // retained opaque content a new meaning, just as explicit namespace introduction does.
  const destinationPrefixes = new Set<string>();
  for (let current: Element | null = destination; current; current = current.parentElement) {
    for (const attribute of [...current.attributes]) {
      if (attribute.namespaceURI === "http://www.w3.org/2000/xmlns/" && attribute.prefix === "xmlns")
        destinationPrefixes.add(attribute.localName);
    }
  }
  for (const prefix of destinationPrefixes)
    if (destination.lookupNamespaceURI(prefix) && !element.lookupNamespaceURI(prefix)) rejectOpaqueQNameResolution(element, prefix);
  preserveXmlContext(element, destination);
  const declarations = new Map<string, string>();
  for (let current: Element | null = element; current; current = current.parentElement) {
    for (const attribute of [...current.attributes]) {
      if (attribute.namespaceURI !== "http://www.w3.org/2000/xmlns/" || declarations.has(attribute.name)) continue;
      declarations.set(attribute.name, attribute.value);
    }
  }
  if (!declarations.has("xmlns")) declarations.set("xmlns", element.lookupNamespaceURI(null) ?? "");
  // QName-valued attributes/text (including vendor configuration) depend on ancestor declarations.
  // Preserve that context before moving a subtree, even when XMLSerializer could fix element names.
  for (const [name, value] of declarations) element.setAttributeNS("http://www.w3.org/2000/xmlns/", name, value);
}
function updateSequenceReferences(parsed: ParsedBpmnDocument, flow: Element, sourceRef: string, targetRef: string) {
  const id = requiredText(flow.getAttribute("id"), "sequence flow identity");
  const references = new Map<string, Element>();
  for (const element of parsed.byId.values()) {
    for (const direction of ["incoming", "outgoing"]) {
      for (const reference of children(element, bpmnNamespaces.model, direction)) {
        if (resolveBpmnDocumentReference(parsed, reference, reference.textContent)?.getAttribute("id") !== id) continue;
        if (references.has(direction)) fail(`sequence flow '${id}' has ambiguous ${direction} references`);
        references.set(direction, reference);
      }
    }
  }
  for (const [reference, direction] of [[sourceRef, "outgoing"], [targetRef, "incoming"]]) {
    const endpoint = parsed.byId.get(reference)!;
    const child = references.get(direction) ?? parsed.document.createElementNS(bpmnNamespaces.model, endpoint.prefix ? `${endpoint.prefix}:${direction}` : direction);
    if (!references.has(direction)) child.textContent = id;
    if (child.parentElement !== endpoint) {
      if (references.has(direction)) preserveNamespaceContext(child, endpoint);
      // FlowNode adds incoming* followed by outgoing* before the derived Activity/Event fields.
      // A self-loop updates both on one owner, so append order would produce invalid BPMN.
      const preceding = new Set(["extensionElements", "auditing", "monitoring", "categoryValueRef", "incoming",
        ...(direction === "outgoing" ? ["outgoing"] : [])]);
      const anchor = [...endpoint.children].find(element => element.namespaceURI === bpmnNamespaces.model && !preceding.has(element.localName));
      endpoint.insertBefore(child, anchor ?? null);
    }
  }
}

// Reference paths from the BPMN 2.0 Semantic/BPMNDI schemas shipped with pinned bpmn-moddle.
// Names, expressions, arbitrary attributes and extension payloads are not inferred ID references.
const modelReferenceAttributes: Readonly<Record<string, readonly string[]>> = {
  association: ["sourceRef", "targetRef"], boundaryEvent: ["attachedToRef"], callActivity: ["calledElement"],
  callChoreography: ["calledChoreographyRef", "initiatingParticipantRef"], callConversation: ["calledCollaborationRef"],
  choreographyTask: ["initiatingParticipantRef"], subChoreography: ["initiatingParticipantRef"], globalChoreographyTask: ["initiatingParticipantRef"],
  compensateEventDefinition: ["activityRef"], conversationAssociation: ["innerConversationNodeRef", "outerConversationNodeRef"],
  conversationLink: ["sourceRef", "targetRef"], correlationProperty: ["type"], correlationPropertyBinding: ["correlationPropertyRef"],
  correlationPropertyRetrievalExpression: ["messageRef"], correlationSubscription: ["correlationKeyRef"],
  dataInput: ["itemSubjectRef"], dataOutput: ["itemSubjectRef"], dataObject: ["itemSubjectRef"], dataStore: ["itemSubjectRef"],
  dataObjectReference: ["itemSubjectRef", "dataObjectRef"], dataStoreReference: ["itemSubjectRef", "dataStoreRef"],
  error: ["structureRef"], errorEventDefinition: ["errorRef"], escalation: ["structureRef"], escalationEventDefinition: ["escalationRef"],
  extension: ["definition"], group: ["categoryValueRef"], interface: ["implementationRef"],
  inputOutputBinding: ["operationRef", "inputDataRef", "outputDataRef"], itemDefinition: ["structureRef"], lane: ["partitionElementRef"],
  message: ["itemRef"], messageEventDefinition: ["messageRef"], messageFlow: ["sourceRef", "targetRef", "messageRef"],
  messageFlowAssociation: ["innerMessageFlowRef", "outerMessageFlowRef"], multiInstanceLoopCharacteristics: ["oneBehaviorEventRef", "noneBehaviorEventRef"],
  operation: ["implementationRef"], participant: ["processRef"], process: ["definitionalCollaborationRef"], property: ["itemSubjectRef"],
  receiveTask: ["messageRef", "operationRef"], resourceParameter: ["type"], resourceParameterBinding: ["parameterRef"],
  sendTask: ["messageRef", "operationRef"], sequenceFlow: ["sourceRef", "targetRef"], serviceTask: ["operationRef"],
  signal: ["structureRef"], signalEventDefinition: ["signalRef"]
};
const defaultFlowOwners = new Set(["task", "userTask", "serviceTask", "manualTask", "scriptTask", "businessRuleTask", "sendTask", "receiveTask",
  "callActivity", "subProcess", "adHocSubProcess", "transaction", "exclusiveGateway", "inclusiveGateway", "complexGateway"]);
const diagramReferenceAttributes: Readonly<Record<string, readonly string[]>> = {
  BPMNPlane: ["bpmnElement"], BPMNShape: ["bpmnElement", "choreographyActivityShape"],
  BPMNEdge: ["bpmnElement", "sourceElement", "targetElement"], BPMNLabel: ["labelStyle"]
};
const modelReferenceText = new Set(["categoryValueRef", "choreographyRef", "correlationPropertyRef", "dataInputRefs", "dataOutputRefs", "endPointRef",
  "errorRef", "eventDefinitionRef", "flowNodeRef", "inMessageRef", "incoming", "innerParticipantRef", "inputSetRefs", "interfaceRef",
  "loopDataInputRef", "loopDataOutputRef", "messageFlowRef", "operationRef", "optionalInputRefs", "optionalOutputRefs", "outMessageRef",
  "outerParticipantRef", "outgoing", "outputSetRefs", "participantRef", "resourceRef", "source", "sourceRef", "supportedInterfaceRef",
  "supports", "target", "targetRef", "whileExecutingInputRefs", "whileExecutingOutputRefs"]);

function standardReferenceContext(element: Element) {
  for (let current: Element | null = element; current; current = current.parentElement) {
    if (!identityNamespaces.has(current.namespaceURI ?? "")) return false;
    if (current.namespaceURI === bpmnNamespaces.model && ["extensionElements", "documentation", "script"].includes(current.localName)) return false;
  }
  return true;
}
function standardReferenceAttribute(element: Element, attribute: Attr) {
  if (attribute.namespaceURI) return false;
  if (element.namespaceURI === bpmnNamespaces.diagram) return diagramReferenceAttributes[element.localName]?.includes(attribute.localName) ?? false;
  if (element.namespaceURI !== bpmnNamespaces.model) return false;
  if (attribute.localName === "default" && defaultFlowOwners.has(element.localName)) return true;
  if (attribute.localName === "evaluatesToTypeRef") {
    const type = element.getAttributeNS("http://www.w3.org/2001/XMLSchema-instance", "type")?.split(":");
    if (type?.at(-1) === "tFormalExpression" && element.lookupNamespaceURI(type.length === 2 ? type[0] : null) === bpmnNamespaces.model) return true;
  }
  return modelReferenceAttributes[element.localName]?.includes(attribute.localName) ?? false;
}

function rejectRemovedReferences(previousIds: ReadonlyMap<string, Element>, nextIds: ReadonlyMap<string, Element>, document: XMLDocument) {
  const removed = new Set([...previousIds.keys()].filter(id => !nextIds.has(id)));
  if (!removed.size) return;
  const removedReference = (element: Element, value: string) => removed.has(value)
    || value.includes(":") && element.lookupNamespaceURI(value.split(":")[0]) === document.documentElement.getAttribute("targetNamespace") && removed.has(value.split(":")[1]);
  for (const element of [...document.getElementsByTagName("*")]) {
    if (!standardReferenceContext(element)) continue;
    for (const attribute of [...element.attributes]) {
      if (!standardReferenceAttribute(element, attribute)) continue;
      if (attribute.value.trim().split(/\s+/).some(value => removedReference(element, value)))
        fail(`deletion leaves reference '${attribute.name}' to removed content`);
    }
    if (element.namespaceURI === bpmnNamespaces.model && modelReferenceText.has(element.localName) && removedReference(element, element.textContent?.trim() ?? ""))
      fail(`deletion leaves '${element.localName}' pointing to removed content`);
  }
}

function applyCommand(parsed: ParsedBpmnDocument, command: BpmnDocumentCommand) {
  switch (command.kind) {
    case "namespace": {
      const target = resolve(parsed, command.target);
      const namespace = requiredText(command.namespaceURI, "namespace URI");
      if (!validNcName(parsed.document, command.prefix) || command.prefix === "xmlns"
        || namespace === "http://www.w3.org/2000/xmlns/"
        || namespace === "http://www.w3.org/XML/1998/namespace" && command.prefix !== "xml")
        fail("namespace declaration needs a valid non-default prefix and namespace");
      const previous = target.lookupNamespaceURI(command.prefix);
      if (previous === namespace) return;
      if (previous || command.prefix === "xml") fail("namespace declaration cannot rebind an existing prefix");
      // A previously unresolved opaque QName must not acquire a new meaning as a side effect.
      // Declare at the narrowest owner, or choose an unused prefix for the actual new field.
      rejectOpaqueQNameResolution(target, command.prefix);
      target.setAttributeNS("http://www.w3.org/2000/xmlns/", `xmlns:${command.prefix}`, namespace);
      return;
    }
    case "attribute": {
      if (command.name === "id" || command.name === "xmlns" || command.name.startsWith("xmlns:") || command.namespaceURI === "http://www.w3.org/2000/xmlns/")
        fail("identity and namespace changes need an explicit reference-remapping operation");
      const target = resolve(parsed, command.target);
      if (command.namespaceURI) {
        if (command.value === null) target.removeAttributeNS(command.namespaceURI, command.name.split(":").pop()!);
        else {
          const prefix = command.name.includes(":") ? command.name.split(":")[0] : null;
          const existingNamespace = prefix ? target.lookupNamespaceURI(prefix) : null;
          if (!prefix || existingNamespace !== command.namespaceURI)
            fail("namespaced attributes require an explicitly declared matching prefix; introduce it with the namespace operation first");
          target.setAttributeNS(command.namespaceURI, command.name, command.value);
        }
      } else if (command.value === null) target.removeAttribute(command.name);
      else target.setAttribute(command.name, command.value);
      return;
    }
    case "text": {
      const target = resolve(parsed, command.target);
      if (target.children.length) fail("text replacement would discard child configuration");
      if (target.textContent !== command.value) {
        if ([...target.childNodes].some(node => node.nodeType !== Node.TEXT_NODE && node.nodeType !== Node.CDATA_SECTION_NODE))
          fail("text replacement needs explicit correspondence for retained comments or processing instructions");
        target.textContent = command.value;
      }
      return;
    }
    case "append":
    case "insert": {
      const target = resolve(parsed, command.parent);
      const fragment = parseXml(command.xml).documentElement;
      const before = command.kind === "insert" && command.before ? resolve(parsed, command.before) : null;
      if (before && before.parentElement !== target) fail("insertion anchor must be a direct child of its parent");
      target.insertBefore(parsed.document.importNode(fragment, true), before);
      return;
    }
    case "remove": {
      const target = resolve(parsed, command.target);
      if (target === parsed.process || target === parsed.document.documentElement) fail("selected process cannot be removed");
      const containedIds = new Set([target, ...target.getElementsByTagName("*")].map(element => element.getAttribute("id")).filter(Boolean));
      if (parsed.payload.bindings.some(binding => containedIds.has(binding.elementId)))
        fail("deletion needs an explicit native-binding transformation");
      target.remove();
      return;
    }
    case "reparent": {
      const target = resolve(parsed, command.target);
      const parent = resolve(parsed, command.parent);
      if (target === parsed.process || target === parsed.document.documentElement || target.contains(parent)) fail("containment would be cyclic or remove the selected process");
      const before = command.before ? resolve(parsed, command.before) : null;
      if (before && before.parentElement !== parent) fail("reparent anchor must be a direct child of its parent");
      if (before === target || target.parentElement === parent && command.before === undefined) return;
      preserveNamespaceContext(target, parent);
      parent.insertBefore(target, before);
      return;
    }
    case "replaceType": {
      const target = resolve(parsed, command.target);
      if (target.namespaceURI !== bpmnNamespaces.model || !validNcName(parsed.document, command.localName)) fail("replacement type is invalid");
      if (target.localName === command.localName) return;
      const replacement = parsed.document.createElementNS(bpmnNamespaces.model, target.prefix ? `${target.prefix}:${command.localName}` : command.localName);
      for (const attribute of [...target.attributes]) replacement.setAttributeNS(attribute.namespaceURI, attribute.name, attribute.value);
      while (target.firstChild) replacement.appendChild(target.firstChild);
      target.replaceWith(replacement);
      return;
    }
    case "reconnect": {
      const flow = resolve(parsed, { elementId: command.elementId });
      const source = resolveBpmnDocumentReference(parsed, flow, command.sourceRef);
      const target = resolveBpmnDocumentReference(parsed, flow, command.targetRef);
      if (!source || !target || source.namespaceURI !== bpmnNamespaces.model || target.namespaceURI !== bpmnNamespaces.model)
        fail("connection endpoints are unavailable");
      if (!["sequenceFlow", "messageFlow", "association"].includes(flow.localName)) fail("connection needs its own BPMN reference command");
      if (flow.getAttribute("sourceRef") === command.sourceRef && flow.getAttribute("targetRef") === command.targetRef) return;
      if (flow.localName === "sequenceFlow") {
        if (source.parentElement !== flow.parentElement || target.parentElement !== flow.parentElement)
          fail("sequence flow must stay in one standard scope");
        const oldSource = resolveBpmnDocumentReference(parsed, flow, flow.getAttribute("sourceRef"));
        if (oldSource && resolveBpmnDocumentReference(parsed, oldSource, oldSource.getAttribute("default")) === flow && source !== oldSource)
          oldSource.removeAttribute("default");
        updateSequenceReferences(parsed, flow, source.getAttribute("id")!, target.getAttribute("id")!);
      }
      flow.setAttribute("sourceRef", flow.localName === "sequenceFlow" ? source.getAttribute("id")! : command.sourceRef);
      flow.setAttribute("targetRef", flow.localName === "sequenceFlow" ? target.getAttribute("id")! : command.targetRef);
      return;
    }
    case "bounds": {
      const shape = resolve(parsed, { elementId: command.shapeId });
      if (!is(shape, bpmnNamespaces.diagram, "BPMNShape")) fail("bounds target is not a BPMN shape");
      writeBounds(uniqueChild(shape, bpmnNamespaces.bounds, "Bounds"), command.bounds);
      return;
    }
    case "labelBounds": {
      const target = resolve(parsed, { elementId: command.diId });
      if (target.namespaceURI !== bpmnNamespaces.diagram || !["BPMNShape", "BPMNEdge"].includes(target.localName)) fail("label owner must be a BPMN shape or edge");
      const labels = children(target, bpmnNamespaces.diagram, "BPMNLabel");
      if (labels.length > 1) fail("label owner has ambiguous labels");
      const label = labels[0];
      const previousBounds = label ? children(label, bpmnNamespaces.bounds, "Bounds") : [];
      if (previousBounds.length > 1) fail("label has ambiguous bounds");
      if (command.bounds === null) {
        previousBounds[0]?.remove();
        return;
      }
      const nextLabel = label ?? parsed.document.createElementNS(bpmnNamespaces.diagram, "bpmndi:BPMNLabel");
      const nextBounds = previousBounds[0] ?? parsed.document.createElementNS(bpmnNamespaces.bounds, "dc:Bounds");
      writeBounds(nextBounds, command.bounds);
      if (!previousBounds.length) nextLabel.appendChild(nextBounds);
      if (!label) target.appendChild(nextLabel);
      return;
    }
    case "dataReferences": {
      const association = resolve(parsed, { elementId: command.elementId });
      if (association.namespaceURI !== bpmnNamespaces.model || !["dataInputAssociation", "dataOutputAssociation"].includes(association.localName)) fail("reference owner must be a data association");
      const resolveItem = (reference: string) => {
        const item = resolveBpmnDocumentReference(parsed, association, reference);
        if (!item || item.namespaceURI !== bpmnNamespaces.model || !["dataInput", "dataOutput", "dataObject", "dataObjectReference", "dataStoreReference", "property"].includes(item.localName))
          fail(`data reference '${reference}' must resolve to an item-aware element`);
        return item.getAttribute("id")!;
      };
      const sources = command.sourceRefs.map(resolveItem);
      const targetId = resolveItem(command.targetRef);
      const oldSources = children(association, bpmnNamespaces.model, "sourceRef");
      const targets = children(association, bpmnNamespaces.model, "targetRef");
      if (targets.length > 1) fail("data association has ambiguous target references");
      if (oldSources.length === sources.length && oldSources.every((reference, index) =>
        resolveBpmnDocumentReference(parsed, reference, reference.textContent)?.getAttribute("id") === sources[index])
        && targets[0] && resolveBpmnDocumentReference(parsed, targets[0], targets[0].textContent)?.getAttribute("id") === targetId) return;
      const target = targets[0] ?? parsed.document.createElementNS(bpmnNamespaces.model, "bpmn:targetRef");
      if (target.textContent !== targetId) {
        if ([...target.childNodes].some(node => node.nodeType !== Node.TEXT_NODE && node.nodeType !== Node.CDATA_SECTION_NODE))
          fail("data target replacement needs explicit correspondence for retained content");
        target.textContent = targetId;
      }
      if (!targets.length) association.insertBefore(target, children(association, bpmnNamespaces.model, "transformation")[0]
        ?? children(association, bpmnNamespaces.model, "assignment")[0] ?? null);
      const retained = new Set<Element>();
      for (const source of sources) {
        const reference = oldSources.find(candidate => !retained.has(candidate)
          && resolveBpmnDocumentReference(parsed, candidate, candidate.textContent)?.getAttribute("id") === source)
          ?? parsed.document.createElementNS(bpmnNamespaces.model, "bpmn:sourceRef");
        retained.add(reference);
        if (!oldSources.includes(reference)) reference.textContent = source;
        association.insertBefore(reference, target);
      }
      for (const reference of oldSources) if (!retained.has(reference)) reference.remove();
      return;
    }
    case "waypoints": {
      const edge = resolve(parsed, { elementId: command.edgeId });
      if (!is(edge, bpmnNamespaces.diagram, "BPMNEdge") || command.points.length < 2) fail("edge needs at least two waypoints");
      const old = children(edge, bpmnNamespaces.coordinates, "waypoint");
      const mapping = command.sourceIndices ?? command.points.map((_, index) => index < old.length ? index : null);
      if (!Array.isArray(mapping) || mapping.length !== command.points.length) fail("waypoint source mapping must match the new route");
      const retained = new Set<number>();
      for (const index of mapping) {
        if (index === null) continue;
        if (!Number.isSafeInteger(index) || index < 0 || index >= old.length || retained.has(index)) fail("waypoint source mapping must use unique available vertices");
        retained.add(index);
      }
      const hasMetadata = (waypoint: Element) => [...waypoint.attributes].some(attribute => attribute.namespaceURI !== "http://www.w3.org/2000/xmlns/"
        && (attribute.namespaceURI !== null || !["x", "y"].includes(attribute.localName)))
        || [...waypoint.childNodes].some(node => node.nodeType === 1 || node.nodeType === 7 || node.nodeType === 8 || node.textContent?.trim());
      if (!command.sourceIndices && old.length !== command.points.length && old.slice(1).some(hasMetadata))
        fail("route membership needs an explicit source mapping to retain waypoint metadata");
      const sameSlots = old.length === mapping.length && mapping.every((source, index) => source === index);
      if (sameSlots && old.every((waypoint, index) => Number(waypoint.getAttribute("x")) === command.points[index].x
        && Number(waypoint.getAttribute("y")) === command.points[index].y)) return;
      // Empty temporary slots keep unowned siblings in their original order and namespace context.
      // Explicitly omitted source vertices alone lose their own metadata; retained nodes travel intact.
      const slots = old.map(waypoint => {
        const slot = parsed.document.createTextNode(""); waypoint.replaceWith(slot); return slot;
      });
      command.points.forEach((point, index) => {
        const source = mapping[index];
        const waypoint = source === null ? parsed.document.createElementNS(bpmnNamespaces.coordinates, "di:waypoint") : old[source];
        waypoint.setAttribute("x", coordinate(point.x));
        waypoint.setAttribute("y", coordinate(point.y));
        edge.insertBefore(waypoint, slots[Math.min(index, slots.length - 1)] ?? children(edge, bpmnNamespaces.diagram, "BPMNLabel")[0] ?? null);
      });
      slots.forEach(slot => slot.remove());
      return;
    }
  }
}

/** Source and native ownership are validated together, before the host admits one history edit. */
export function applyBpmnDocumentTransaction(owner: ActivityNode, transaction: BpmnDocumentTransaction): ActivityNode {
  const commands = transaction.commands ?? [];
  const configuration = transaction.configuration ?? {};
  const configurationKeys = Object.keys(configuration);
  if (configurationKeys.some(key => !["activities", "bindings", "variables", "scopeHeaders"].includes(key)))
    fail("configuration changes must not introduce another semantic source");
  if (configurationKeys.some(key => !Array.isArray(configuration[key as keyof BpmnDocumentConfigurationChange])))
    fail("configuration changes must contain explicit arrays");
  if (!commands.length && !configurationKeys.length) return owner;
  let parsed = readBpmnDocument(owner);
  const configurationChanged = configurationKeys.some(key => JSON.stringify(configuration[key as keyof BpmnDocumentConfigurationChange]) !== JSON.stringify(parsed.payload[key]));
  // A bound deletion/create can change both ownership lists and XML in this transaction. These
  // intermediate arrays are not projected or persisted; only the final source is admitted below.
  parsed = { ...parsed, payload: { ...parsed.payload, ...configuration } };
  const originalIds = parsed.byId;
  const before = new XMLSerializer().serializeToString(parsed.document);
  for (const command of commands) {
    applyCommand(parsed, command);
    // Refresh identity lookups between operations, catching collisions before committing anything.
    parsed = { ...parsed, byId: indexDocument(parsed.document) };
  }
  rejectRemovedReferences(originalIds, parsed.byId, parsed.document);
  const documentXml = new XMLSerializer().serializeToString(parsed.document);
  if (documentXml === before && !configurationChanged) return owner;
  const next = { ...owner, structure: { ...owner.structure!, payload: { ...owner.structure!.payload, ...configuration,
    documentXml: documentXml === before ? parsed.payload.documentXml : documentXml } } };
  readBpmnDocument(next);
  return next;
}

/** Atomic immutable XML-only transaction. The host owns revisions, history and persistence. */
export function applyBpmnDocumentCommands(owner: ActivityNode, commands: readonly BpmnDocumentCommand[]): ActivityNode {
  return applyBpmnDocumentTransaction(owner, { commands });
}

export interface BpmnDocumentShape {
  shapeId: string;
  elementId: string;
  elementType: string;
  element: Element;
  bounds: BpmnBounds;
  labelBounds?: BpmnBounds;
  isExpanded?: boolean;
  isHorizontal?: boolean;
  isMarkerVisible?: boolean;
}
export interface BpmnDocumentEdge {
  edgeId: string;
  elementId: string;
  elementType: string;
  sourceRef: string | null;
  targetRef: string | null;
  /** Data associations can have several semantic sources; never reduce them to one flow. */
  sourceRefs: string[];
  targetRefs: string[];
  element: Element;
  points: BpmnPoint[];
  labelBounds?: BpmnBounds;
}
export interface BpmnDocumentScene {
  diagramId: string;
  scopeId: string;
  shapes: BpmnDocumentShape[];
  edges: BpmnDocumentEdge[];
}
function readNumber(element: Element, name: string): number {
  const text = element.getAttribute(name);
  const value = text === null || !text.trim() ? NaN : Number(text);
  if (!Number.isFinite(value)) fail(`geometry '${name}' is invalid`);
  return value;
}
function readBounds(element: Element, allowEmpty = false): BpmnBounds {
  const value = { x: readNumber(element, "x"), y: readNumber(element, "y"), width: readNumber(element, "width"), height: readNumber(element, "height") };
  if (allowEmpty ? value.width < 0 || value.height < 0 : value.width <= 0 || value.height <= 0) fail("shape dimensions must be positive");
  return value;
}
function labelBounds(element: Element): BpmnBounds | undefined {
  const label = children(element, bpmnNamespaces.diagram, "BPMNLabel")[0];
  const bounds = label && children(label, bpmnNamespaces.bounds, "Bounds")[0];
  return bounds ? readBounds(bounds, true) : undefined;
}
function readBoolean(element: Element, name: string): boolean | undefined {
  const value = element.getAttribute(name);
  return value === "true" || value === "1" ? true : value === "false" || value === "0" ? false : undefined;
}
function connectionReferences(parsed: ParsedBpmnDocument, element: Element, name: "sourceRef" | "targetRef"): string[] {
  const references = element.hasAttribute(name) ? [{ context: element, value: element.getAttribute(name)! }]
    : children(element, bpmnNamespaces.model, name).map(child => ({ context: child, value: child.textContent?.trim() ?? "" }));
  return references.map(reference => resolveBpmnDocumentReference(parsed, reference.context, reference.value)?.getAttribute("id") ?? reference.value);
}

/** Complete DI projection, including pools, lanes, boundary events, data and artifact connections. */
export function projectBpmnDocumentScene(owner: ActivityNode, diagramId: string): BpmnDocumentScene {
  const parsed = readBpmnDocument(owner);
  const diagram = parsed.byId.get(diagramId);
  if (!diagram || !is(diagram, bpmnNamespaces.diagram, "BPMNDiagram")) fail(`diagram '${diagramId}' is unavailable`);
  const plane = uniqueChild(diagram, bpmnNamespaces.diagram, "BPMNPlane");
  const scope = resolveBpmnDocumentReference(parsed, plane, plane.getAttribute("bpmnElement"));
  if (!scope) fail("diagram scope is unavailable");
  const scene: BpmnDocumentScene = { diagramId, scopeId: requiredText(scope.getAttribute("id"), "plane scope"), shapes: [], edges: [] };
  for (const di of [...plane.children]) {
    const reference = di.getAttribute("bpmnElement");
    if (!reference) continue;
    const element = resolveBpmnDocumentReference(parsed, di, reference);
    if (!element) fail(`diagram references unavailable element '${reference}'`);
    const elementId = requiredText(element.getAttribute("id"), "diagram element identity");
    if (is(di, bpmnNamespaces.diagram, "BPMNShape")) {
      scene.shapes.push({ shapeId: requiredText(di.getAttribute("id"), "shape identity"), elementId, elementType: element.localName, element,
        bounds: readBounds(uniqueChild(di, bpmnNamespaces.bounds, "Bounds")), labelBounds: labelBounds(di),
        isExpanded: readBoolean(di, "isExpanded"), isHorizontal: readBoolean(di, "isHorizontal"), isMarkerVisible: readBoolean(di, "isMarkerVisible") });
    } else if (is(di, bpmnNamespaces.diagram, "BPMNEdge")) {
      const sourceRefs = connectionReferences(parsed, element, "sourceRef");
      const targetRefs = connectionReferences(parsed, element, "targetRef");
      scene.edges.push({ edgeId: requiredText(di.getAttribute("id"), "edge identity"), elementId, elementType: element.localName, element,
        sourceRef: sourceRefs[0] ?? null, targetRef: targetRefs[0] ?? null, sourceRefs, targetRefs,
        points: children(di, bpmnNamespaces.coordinates, "waypoint").map(point => ({ x: readNumber(point, "x"), y: readNumber(point, "y") })), labelBounds: labelBounds(di) });
    }
  }
  return scene;
}
