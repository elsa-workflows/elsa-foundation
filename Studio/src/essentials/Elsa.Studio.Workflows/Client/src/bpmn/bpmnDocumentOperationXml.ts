import type { ActivityNode } from "../workflowTypes";
import { bpmnNamespaces as ns, projectBpmnDocumentScene, readBpmnDocument, resolveBpmnDocumentReference,
  type BpmnDocumentCommand, type BpmnDocumentScene, type BpmnScopeAddress, type BpmnXmlAddress, type ParsedBpmnDocument } from "./bpmnDocument";
import { bpmnOwningProcess, isBpmnModelElement } from "./bpmnDocumentPolicy";
import { serializeBpmnXml } from "./bpmnDocumentXml";
import { isBpmnOwnedSyntax } from "./bpmnDocumentSyntaxOwnership";
export { isBpmnOwnedSyntax };

export function refuseBpmnOperation(reason: string): never { throw new Error(`Cannot author BPMN source: ${reason}.`); }

export interface BpmnOperationContext {
  owner: ActivityNode;
  scope: BpmnScopeAddress;
  parsed: ParsedBpmnDocument;
  scene: BpmnDocumentScene;
  plane: Element;
  selectedScope: Element;
  allocate(prefix: string): string;
}
export function bpmnOperationContext(owner: ActivityNode, scope: BpmnScopeAddress): BpmnOperationContext {
  const parsed = readBpmnDocument(owner), scene = projectBpmnDocumentScene(owner, scope.diagramId);
  const selectedScope = parsed.byId.get(scope.scopeElementId);
  if (owner.nodeId !== scope.documentOwnerNodeId || !isBpmnModelElement(selectedScope) || !["process", "subProcess", "transaction", "adHocSubProcess", "collaboration"].includes(selectedScope.localName)) refuseBpmnOperation("the document/scope address is stale");
  if (!isBpmnOwnedSyntax(selectedScope)) refuseBpmnOperation("the selected scope is retained payload, not owned standard syntax");
  const diagram = parsed.byId.get(scope.diagramId)!;
  const planes = [...diagram.children].filter(child => child.namespaceURI === ns.diagram && child.localName === "BPMNPlane");
  if (planes.length !== 1) refuseBpmnOperation("the diagram needs one unambiguous plane");
  const represented = resolveBpmnDocumentReference(parsed, planes[0], planes[0].getAttribute("bpmnElement"));
  const processes = represented?.localName === "collaboration" ? [...represented.children].filter(child => isBpmnModelElement(child) && child.localName === "participant")
    .map(child => resolveBpmnDocumentReference(parsed, child, child.getAttribute("processRef"))) : [];
  if (!represented || represented !== selectedScope && !represented.contains(selectedScope) && !processes.some(process => process?.contains(selectedScope)))
    refuseBpmnOperation("the selected scope is not represented by this diagram");
  // Avoid even opaque vendor id spellings when allocating. Existing namespaces/identities remain untouched.
  const reserved = new Set([...parsed.document.getElementsByTagName("*")].map(element => element.getAttribute("id")).filter(Boolean));
  return { owner, scope, parsed, scene, plane: planes[0], selectedScope, allocate(prefix) {
    let index = 1, candidate = prefix;
    while (reserved.has(candidate)) candidate = `${prefix}_${++index}`;
    reserved.add(candidate); return candidate;
  } };
}

/** Actual XML address, including idless members and idless definitions; never an invented element ID. */
export function bpmnSourceAddress(element: Element, parsed: ParsedBpmnDocument): BpmnXmlAddress {
  const path: NonNullable<BpmnXmlAddress["path"]> = [];
  for (let current: Element | null = element; current; current = current.parentElement) {
    const id = current.getAttribute("id");
    if (id && parsed.byId.get(id) === current) return { elementId: id, ...(path.length ? { path } : {}) };
    if (current === parsed.document.documentElement) return { documentRoot: true, ...(path.length ? { path } : {}) };
    const parent: Element | null = current.parentElement;
    if (!parent) break;
    const siblings = [...parent.children].filter(child => child.namespaceURI === current!.namespaceURI && child.localName === current!.localName);
    path.unshift({ namespaceURI: current.namespaceURI ?? "", localName: current.localName, index: siblings.indexOf(current) });
  }
  return refuseBpmnOperation("the XML property does not belong to this source");
}

export function bpmnFragment(context: BpmnOperationContext, localName: string, attributes: Readonly<Record<string, string>> = {}, namespace: string = ns.model): Element {
  const root = context.parsed.document.documentElement;
  // Reuse source namespace context. Fresh prefixes are local to a new fragment, never rebound globally.
  const prefix = root.lookupPrefix(namespace) ?? (root.lookupNamespaceURI(null) === namespace ? "" : context.allocate(namespace === ns.model ? "bpmnNs" : namespace === ns.diagram ? "bpmndiNs" : namespace === ns.bounds ? "dcNs" : "diNs"));
  const element = context.parsed.document.createElementNS(namespace, prefix ? `${prefix}:${localName}` : localName);
  for (const attribute of [...root.attributes]) if (attribute.namespaceURI === "http://www.w3.org/2000/xmlns/")
    element.setAttributeNS(attribute.namespaceURI, attribute.name, attribute.value);
  element.setAttributeNS("http://www.w3.org/2000/xmlns/", prefix ? `xmlns:${prefix}` : "xmlns", namespace);
  for (const [name, value] of Object.entries(attributes)) element.setAttribute(name, value);
  return element;
}
export function bpmnXml(element: Element) { return serializeBpmnXml(element); }
/** Clone a configuration fragment with its QName context; do not infer references in opaque text. */
export function cloneBpmnPropertyFragment(element: Element, destination: Element): Element {
  const clone = element.cloneNode(true) as Element, declarations = new Map<string, string>();
  for (let current: Element | null = element; current; current = current.parentElement) for (const attribute of [...current.attributes])
    if (attribute.namespaceURI === "http://www.w3.org/2000/xmlns/" && !declarations.has(attribute.name)) declarations.set(attribute.name, attribute.value);
  if (!declarations.has("xmlns")) declarations.set("xmlns", element.lookupNamespaceURI(null) ?? "");
  for (const [name, value] of declarations) clone.setAttributeNS("http://www.w3.org/2000/xmlns/", name, value);
  const xml = "http://www.w3.org/XML/1998/namespace";
  for (const [name, fallback] of [["lang", ""], ["space", "default"]]) {
    const effective = (node: Element) => { for (let current: Element | null = node; current; current = current.parentElement) if (current.hasAttributeNS(xml, name)) return current.getAttributeNS(xml, name)!; return fallback; };
    if (effective(element) !== effective(destination)) clone.setAttributeNS(xml, `xml:${name}`, effective(element));
  }
  const bases = (node: Element) => { const result: string[] = []; for (let current: Element | null = node; current; current = current.parentElement) if (current.hasAttributeNS(xml, "base")) result.unshift(current.getAttributeNS(xml, "base")!); return result; };
  if (JSON.stringify(bases(element)) !== JSON.stringify(bases(destination))) {
    let absolute: string | undefined;
    for (const base of bases(element)) { try { absolute = absolute ? new URL(base, absolute).href : new URL(base).href; } catch { refuseBpmnOperation("cloning this configuration changes an unresolved XML base"); } }
    if (absolute) clone.setAttributeNS(xml, "xml:base", absolute);
    else if (bases(destination).length) refuseBpmnOperation("cloning this configuration would introduce a destination XML base");
  }
  return clone;
}
export function bpmnChildren(element: Element, name: string) { return [...element.children].filter(child => child.namespaceURI === ns.model && child.localName === name); }
export function bpmnIdentity(element: Element) { return element.getAttribute("id") || refuseBpmnOperation("this operation requires the actual authored element identity"); }

/** Declare narrowly, avoiding both bound prefixes and retained, currently unresolved QName-looking content. */
export function bpmnAttributePrefix(context: BpmnOperationContext, target: Element, namespaceURI: string, preferred: string, commands: BpmnDocumentCommand[]): string {
  const existing = target.lookupPrefix(namespaceURI);
  if (existing && target.lookupNamespaceURI(existing) === namespaceURI) return existing;
  let prefix = preferred, index = 1;
  const mentioned = (candidate: string) => [target, ...target.getElementsByTagName("*")].some(element => !element.lookupNamespaceURI(candidate)
    && ([...element.attributes].some(attribute => attribute.value.includes(`${candidate}:`))
      || [...element.childNodes].filter(node => [3, 4].includes(node.nodeType)).map(node => node.textContent ?? "").join("").includes(`${candidate}:`)));
  while (target.lookupNamespaceURI(prefix) || mentioned(prefix)) prefix = `${preferred}${++index}`;
  commands.push({ kind: "namespace", target: bpmnSourceAddress(target, context.parsed), prefix, namespaceURI });
  target.setAttributeNS("http://www.w3.org/2000/xmlns/", `xmlns:${prefix}`, namespaceURI); // Advance only this transient planning DOM.
  return prefix;
}

export function resolveBpmnOperationAddress(context: BpmnOperationContext, address: BpmnXmlAddress): Element {
  let element = address.documentRoot === true ? context.parsed.document.documentElement : context.parsed.byId.get(address.elementId);
  if (!element) refuseBpmnOperation("the XML address is stale");
  for (const step of address.path ?? []) {
    const candidates: Element[] = [...element.children].filter(child => (child.namespaceURI ?? "") === step.namespaceURI && child.localName === step.localName);
    if (step.index === undefined && candidates.length !== 1) refuseBpmnOperation("the XML property address is ambiguous");
    element = candidates[step.index ?? 0];
    if (!element) refuseBpmnOperation("the XML property address is stale");
  }
  const current = context.selectedScope, root = context.parsed.document.documentElement;
  const diagram = context.parsed.byId.get(context.scope.diagramId)!;
  if (element !== root && element !== current && !current.contains(element) && element !== diagram && !diagram.contains(element)) {
    const process = bpmnOwningProcess(element);
    if (process) requireBpmnFlowScope(context, bpmnIdentity(process));
    else {
      let global = element;
      while (global.parentElement && global.parentElement !== root) global = global.parentElement;
      if (global.parentElement !== root || global.namespaceURI !== ns.model || ["process", "collaboration"].includes(global.localName)) refuseBpmnOperation("the XML property is outside this standard scope");
    }
  }
  return element;
}

const flow = new Set(["task", "userTask", "serviceTask", "manualTask", "scriptTask", "businessRuleTask", "sendTask", "receiveTask", "callActivity", "subProcess", "adHocSubProcess", "transaction",
  "startEvent", "endEvent", "intermediateCatchEvent", "intermediateThrowEvent", "boundaryEvent", "exclusiveGateway", "inclusiveGateway", "parallelGateway", "eventBasedGateway", "complexGateway", "sequenceFlow", "dataObject", "dataObjectReference", "dataStoreReference"]);
const artifacts = new Set(["textAnnotation", "association", "group"]);
function rank(parent: Element, child: Element) {
  const name = child.localName;
  if (name === "documentation") return 0;
  if (name === "extensionElements") return 1;
  if (parent.localName === "definitions") return child.namespaceURI === ns.diagram ? 4 : name === "import" ? 2 : name === "relationship" ? 5 : 3;
  if (parent.localName === "collaboration") return ({ participant: 2, messageFlow: 3, conversation: 5, subConversation: 5, callConversation: 5, conversationAssociation: 6, participantAssociation: 7, messageFlowAssociation: 8, correlationKey: 9, choreographyRef: 10, conversationLink: 11 } as Record<string, number>)[name] ?? (artifacts.has(name) ? 4 : undefined);
  if (parent.localName === "ioSpecification") return ({ dataInput: 2, dataOutput: 3, inputSet: 4, outputSet: 5 } as Record<string, number>)[name];
  if (parent.localName === "lane") return name === "flowNodeRef" ? 2 : name === "childLaneSet" ? 3 : undefined;
  if (parent.localName === "process") return ({ supportedInterfaceRef: 2, ioSpecification: 3, ioBinding: 4, auditing: 5, monitoring: 6, property: 7, laneSet: 8,
    resourceRole: 11, correlationSubscription: 12, supports: 13 } as Record<string, number>)[name] ?? (flow.has(name) ? 9 : artifacts.has(name) ? 10 : undefined);
  return ({ auditing: 2, monitoring: 3, categoryValueRef: 4, incoming: 5, outgoing: 6, ioSpecification: 7, property: 8, dataInput: 9, dataOutput: 9,
    dataInputAssociation: 10, dataOutputAssociation: 10.5, inputSet: 11, outputSet: 11, resourceRole: 11, standardLoopCharacteristics: 12, multiInstanceLoopCharacteristics: 12,
    laneSet: 13, completionCondition: 16 } as Record<string, number>)[name] ?? (flow.has(name) ? 14 : artifacts.has(name) ? 15 : name.endsWith("EventDefinition") || name === "eventDefinitionRef" ? 12 : undefined);
}
/** Inserts only the new member at its schema position, retaining every existing sibling and its order. */
export function insertBpmnFragment(context: BpmnOperationContext, parent: Element, element: Element): BpmnDocumentCommand {
  const order = rank(parent, element);
  const before = order === undefined ? undefined : [...parent.children].find(child => child.namespaceURI === ns.model || child.namespaceURI === ns.diagram
    ? (rank(parent, child) ?? -1) > order : false);
  return { kind: "insert", parent: bpmnSourceAddress(parent, context.parsed), xml: bpmnXml(element), ...(before ? { before: bpmnSourceAddress(before, context.parsed) } : {}) };
}

export function requireBpmnFlowScope(context: BpmnOperationContext, elementId?: string): Element {
  const element = elementId ? context.parsed.byId.get(elementId) : context.selectedScope;
  if (!isBpmnModelElement(element) || !["process", "subProcess", "transaction", "adHocSubProcess"].includes(element.localName))
    refuseBpmnOperation("choose the actual process/subprocess that will own this syntax");
  if (!isBpmnOwnedSyntax(element)) refuseBpmnOperation("the requested scope is retained payload, not owned standard syntax");
  const root = context.selectedScope;
  if (root !== element && !root.contains(element) && root.localName !== "collaboration") refuseBpmnOperation("the target flow scope is outside this view");
  if (root.localName === "collaboration") {
    const process = bpmnOwningProcess(element);
    if (![...root.children].some(child => isBpmnModelElement(child) && child.localName === "participant" && resolveBpmnDocumentReference(context.parsed, child, child.getAttribute("processRef")) === process))
      refuseBpmnOperation("the target process is not represented by this collaboration");
  }
  return element;
}

export function requireBpmnVisibleElement(context: BpmnOperationContext, element: Element): void {
  if (!isBpmnOwnedSyntax(element)) refuseBpmnOperation("the requested construct is retained payload, not owned standard syntax");
  for (let parent = element.parentElement; parent && parent !== context.selectedScope; parent = parent.parentElement) {
    if (!isBpmnModelElement(parent) || !["subProcess", "transaction", "adHocSubProcess"].includes(parent.localName)) continue;
    const instances = context.scene.shapes.filter(shape => shape.elementId === bpmnIdentity(parent));
    if (instances.length !== 1 || instances[0].isExpanded !== true) refuseBpmnOperation("open the actual standard child scope before editing content hidden by a collapsed or ambiguous body");
  }
}
