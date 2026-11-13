import type { ActivityNode } from "../workflowTypes";
import { applyBpmnDocumentTransaction, bpmnNamespaces as ns, resolveBpmnDocumentReference, type BpmnDocumentCommand, type BpmnDocumentConfigurationChange, type BpmnDocumentSelection, type BpmnScopeAddress } from "./bpmnDocument";
import { verifyBpmnEventContexts } from "./bpmnDocumentEventContext";
import { assessBpmnDocumentConnection, bpmnBoolean, bpmnEventDefinitions, bpmnFlowScope, isBpmnModelElement } from "./bpmnDocumentPolicy";
import { isActivityBearingElementType, isEventElementType, isGatewayElementType, isSubProcessElementType } from "./bpmnNotationTypes";
import { planBpmnCreateConnection, type BpmnCreateConnectionIntent, type BpmnDocumentOperationPlan } from "./bpmnDocumentCreateOperations";
import { bpmnDocumentToolboxShapes } from "./bpmnDocumentToolbox";
import { bpmnChildren, bpmnFragment, bpmnIdentity, bpmnOperationContext, bpmnSourceAddress, bpmnXml, isBpmnOwnedSyntax, refuseBpmnOperation, requireBpmnVisibleElement, resolveBpmnOperationAddress } from "./bpmnDocumentOperationXml";

/** Modeler-like replace intent with an explicit choice before removing existing standard configuration. */
export function planBpmnReplace(owner: ActivityNode, scope: BpmnScopeAddress, selection: BpmnDocumentSelection, toolId: string, replaceEventConfiguration = false): BpmnDocumentOperationPlan {
  const context = bpmnOperationContext(owner, scope), element = resolveBpmnOperationAddress(context, { elementId: selection.elementId });
  requireBpmnVisibleElement(context, element);
  const entry = bpmnDocumentToolboxShapes.find(tool => tool.id === toolId);
  if (!entry) refuseBpmnOperation("the replacement is not in this toolbox");
  if (selection.diId) {
    const di = context.parsed.byId.get(selection.diId);
    if (di?.namespaceURI !== ns.diagram || di.parentElement !== context.plane || resolveBpmnDocumentReference(context.parsed, di, di.getAttribute("bpmnElement")) !== element) refuseBpmnOperation("replacement needs the actual selected DI instance");
  }
  const body = isSubProcessElementType, event = isEventElementType, gateway = isGatewayElementType;
  const task = (type: string) => isActivityBearingElementType(type) && !body(type);
  if (!(task(element.localName) && task(entry.type) || gateway(element.localName) && gateway(entry.type) || event(element.localName) && event(entry.type) || body(element.localName) && body(entry.type))) refuseBpmnOperation("this replacement needs a separate construct/ownership transform");
  if (event(element.localName) && element.localName !== entry.type) refuseBpmnOperation("changing an event's flow role requires explicit incident-flow and attachment handling");
  if (entry.type === "startEvent" && bpmnBoolean(element.parentElement!, "triggeredByEvent") && !entry.eventSubprocessOnly) refuseBpmnOperation("choose a start trigger for the actual event subprocess");
  if (entry.transactionOnly && (entry.type === "boundaryEvent" ? resolveBpmnDocumentReference(context.parsed, element, element.getAttribute("attachedToRef"))?.localName !== "transaction" : element.parentElement?.localName !== "transaction")) refuseBpmnOperation("cancel events require the actual transaction context");
  if (gateway(entry.type) && !["exclusiveGateway", "inclusiveGateway", "complexGateway"].includes(entry.type) && element.hasAttribute("default")) refuseBpmnOperation("clear the existing default before changing to a gateway without default routing");
  if (entry.eventSubprocessOnly !== undefined && entry.eventSubprocessOnly !== bpmnBoolean(element.parentElement!, "triggeredByEvent")) refuseBpmnOperation("the replacement trigger does not match its real standard scope");
  if (body(element.localName) && bpmnBoolean(element, "triggeredByEvent") !== (entry.attributes?.triggeredByEvent === "true")) refuseBpmnOperation("changing event-body ownership requires an explicit scope/listener transform");
  const specialized = new Set(["script", "completionCondition", "activationCondition"]);
  if (element.localName !== entry.type && [...element.children].some(child => child.namespaceURI === ns.model && specialized.has(child.localName))) refuseBpmnOperation("edit or explicitly remove the existing type-specific properties before replacement");
  const allowed = new Set(["id", "name", "isForCompensation", "startQuantity", "completionQuantity", "default", "cancelActivity", "isInterrupting", "parallelMultiple", "attachedToRef", "gatewayDirection", "triggeredByEvent"]);
  if (element.localName !== entry.type && [...element.attributes].some(attribute => !attribute.namespaceURI && !allowed.has(attribute.name))) refuseBpmnOperation("the existing notation has type-specific attributes that need an explicit property choice");
  const commands: BpmnDocumentCommand[] = element.localName === entry.type ? [] : [{ kind: "replaceType", target: { elementId: selection.elementId }, localName: entry.type }];
  const oldDefinitions = [...element.children].filter(child => child.namespaceURI === ns.model && (child.localName.endsWith("EventDefinition") || child.localName === "eventDefinitionRef"));
  const current = bpmnEventDefinitions(element), requested = entry.eventDefinitions ?? [];
  if (event(element.localName) && JSON.stringify(current) !== JSON.stringify(requested)) {
    if (oldDefinitions.length && !replaceEventConfiguration) refuseBpmnOperation("replacing event definitions requires an explicit configuration replacement choice");
    for (const definition of [...oldDefinitions].reverse()) commands.push({ kind: "remove", target: bpmnSourceAddress(definition, context.parsed) });
    for (const type of requested) {
      const definition = bpmnFragment(context, type);
      if (type === "timerEventDefinition") { const duration = bpmnFragment(context, "timeDuration"); duration.textContent = "PT1H"; definition.append(duration); }
      if (type === "conditionalEventDefinition") definition.append(bpmnFragment(context, "condition"));
      if (type === "linkEventDefinition") definition.setAttribute("name", element.getAttribute("name") ?? "Link");
      // The existing definitions are removed first; inserting before them would use a stale property address.
      commands.push({ kind: "append", parent: { elementId: selection.elementId }, xml: bpmnXml(definition) });
    }
  }
  for (const name of ["cancelActivity", "isInterrupting", "parallelMultiple"]) if (event(element.localName)) commands.push({ kind: "attribute", target: { elementId: selection.elementId }, name, value: entry.attributes?.[name] ?? null });
  if (entry.expanded !== undefined && selection.diId) commands.push({ kind: "attribute", target: { elementId: selection.diId }, name: "isExpanded", value: String(entry.expanded) });
  const plan = { transaction: { commands }, selection };
  const next = applyBpmnDocumentTransaction(owner, plan.transaction), parsed = bpmnOperationContext(next, scope).parsed;
  verifyBpmnEventContexts(context.parsed, parsed, [selection.elementId]);
  for (const flow of parsed.byId.values()) if (["sequenceFlow", "messageFlow"].includes(flow.localName)) {
    const source = resolveBpmnDocumentReference(parsed, flow, flow.getAttribute("sourceRef")), target = resolveBpmnDocumentReference(parsed, flow, flow.getAttribute("targetRef"));
    if (source?.id !== selection.elementId && target?.id !== selection.elementId) continue;
    const policy = assessBpmnDocumentConnection(parsed, flow.localName as "sequenceFlow" | "messageFlow", source?.id ?? "", target?.id ?? "");
    if (!policy.allowed) refuseBpmnOperation(`the replacement requires changing an incident relationship: ${policy.reason}`);
  }
  return plan;
}

export interface BpmnReconnectIntent extends BpmnCreateConnectionIntent { elementId: string; diId: string; sourceIndices?: (number | null)[] }
/** Reuses the typed creation policy/I/O plan, retaining this relationship and its actual DI identity. */
export function planBpmnReconnect(owner: ActivityNode, scope: BpmnScopeAddress, intent: BpmnReconnectIntent): BpmnDocumentOperationPlan {
  const context = bpmnOperationContext(owner, scope), element = resolveBpmnOperationAddress(context, { elementId: intent.elementId });
  requireBpmnVisibleElement(context, element);
  const edge = context.scene.edges.find(candidate => candidate.edgeId === intent.diId && candidate.elementId === intent.elementId);
  const expectedType = intent.kind === "compensationAssociation" ? "association" : intent.kind;
  if (!edge || element.localName !== expectedType) refuseBpmnOperation("reconnect needs the actual relationship and DI instance of the same kind");
  const candidate = planBpmnCreateConnection(owner, scope, intent), commands: BpmnDocumentCommand[] = [];
  // The candidate is a pure, unpublished plan. Its standard I/O members are reused; its fresh relationship/DI never enter source.
  let replacement: Element | undefined;
  for (const command of candidate.transaction.commands ?? []) {
    if (command.kind === "insert" || command.kind === "append") {
      const fragment = new DOMParser().parseFromString(command.xml, "application/xml").documentElement;
      if (fragment.getAttribute("id") === candidate.selection!.elementId) { replacement = fragment; break; }
    }
    commands.push(command);
  }
  if (!replacement) refuseBpmnOperation("the replacement plan did not contain its standard relationship");
  const data = ["dataInputAssociation", "dataOutputAssociation"].includes(element.localName);
  if (data) {
    const nextOwnerId = intent.kind === "dataInputAssociation" ? intent.targetId : intent.sourceId;
    if (element.parentElement?.getAttribute("id") !== nextOwnerId) commands.push({ kind: "reparent", target: { elementId: intent.elementId }, parent: { elementId: nextOwnerId } });
    commands.push({ kind: "dataReferences", elementId: intent.elementId, sourceRefs: bpmnChildren(replacement, "sourceRef").map(child => child.textContent!), targetRef: bpmnChildren(replacement, "targetRef")[0].textContent! });
  } else {
    if (element.localName === "sequenceFlow" && bpmnFlowScope(context.parsed.byId.get(intent.sourceId)!) !== element.parentElement) refuseBpmnOperation("reconnecting across flow scopes requires an explicit containment transform");
    commands.push({ kind: "reconnect", elementId: intent.elementId, sourceRef: intent.sourceId, targetRef: intent.targetId });
  }
  const old = edge.points, metadata = (point: Element) => point.childNodes.length > 0 || [...point.attributes].some(attribute => attribute.namespaceURI !== "http://www.w3.org/2000/xmlns/" && (attribute.namespaceURI !== null || !["x", "y"].includes(attribute.name)));
  const vertices = [...context.parsed.byId.get(intent.diId)!.children].filter(child => child.namespaceURI === ns.coordinates && child.localName === "waypoint");
  if (!intent.sourceIndices && old.length !== intent.points.length && vertices.slice(1, -1).some(metadata)) refuseBpmnOperation("the changed route needs an explicit mapping for its retained bend metadata");
  const sourceIndices = intent.sourceIndices ?? (old.length === intent.points.length ? old.map((_, index) => index) : intent.points.map((_, index) => index === 0 ? 0 : index === intent.points.length - 1 ? old.length - 1 : null));
  commands.push({ kind: "waypoints", edgeId: intent.diId, points: intent.points, sourceIndices });
  if (intent.name !== undefined && element.localName !== "association" && !data) commands.push({ kind: "attribute", target: { elementId: intent.elementId }, name: "name", value: intent.name });
  const actualDi = context.parsed.byId.get(intent.diId)!;
  for (const [attribute, endpoint] of [["sourceElement", intent.sourceId], ["targetElement", intent.targetId]]) if (actualDi.hasAttribute(attribute)) commands.push({ kind: "attribute", target: { elementId: intent.diId }, name: attribute, value: context.scene.shapes.find(shape => shape.elementId === endpoint)!.shapeId });
  const plan = { transaction: { commands }, selection: { elementId: intent.elementId, diId: intent.diId } };
  applyBpmnDocumentTransaction(owner, plan.transaction);
  return plan;
}

export interface BpmnNativeCleanupRequirement { bindingElementIds: string[]; activityRootIds: string[]; headerScopeIds: string[] }
export type BpmnDeletePreparation = { kind: "ready"; plan: BpmnDocumentOperationPlan } | { kind: "requiresNativeCleanup"; cleanup: BpmnNativeCleanupRequirement; commands: readonly BpmnDocumentCommand[]; reason: string };
/** Whole syntax deletion includes incident relationships/DI, with disjoint native ownership explicitly admitted by the host. */
export function prepareBpmnDelete(owner: ActivityNode, scope: BpmnScopeAddress, elementIds: readonly string[], configuration?: BpmnDocumentConfigurationChange): BpmnDeletePreparation {
  return prepareBpmnDeleteCore(owner, scope, elementIds, configuration);
}

/** Internal shared planner. Pools enter only through their explicit process-retention preparation. */
export function prepareBpmnDeleteCore(owner: ActivityNode, scope: BpmnScopeAddress, elementIds: readonly string[], configuration?: BpmnDocumentConfigurationChange, allowParticipants = false): BpmnDeletePreparation {
  const context = bpmnOperationContext(owner, scope), removed = new Set<Element>();
  const standardContext = isBpmnOwnedSyntax;
  for (const id of elementIds) {
    const element = resolveBpmnOperationAddress(context, { elementId: id });
    if (element.namespaceURI !== ns.model) refuseBpmnOperation("construct deletion uses actual standard semantic identities, not diagram instances");
    if (!standardContext(element)) refuseBpmnOperation("this BPMN-looking member is retained opaque content, not an owned construct");
    if (element === context.parsed.process || element === context.parsed.document.documentElement) refuseBpmnOperation("replacing the selected process is a resource-host operation");
    if (element === context.selectedScope) refuseBpmnOperation("deleting the active scope requires a host navigation decision");
    if (element.localName === "participant" && !allowParticipants) refuseBpmnOperation("pool deletion uses its explicit process-retention preparation");
    removed.add(element);
  }
  const ids = () => new Set([...removed].flatMap(element => [element, ...element.getElementsByTagName("*")]).flatMap(element => {
    const id = element.getAttribute("id");
    return id && context.parsed.byId.get(id) === element ? [id] : [];
  }));
  let changed: boolean;
  do {
    changed = false; const deleted = ids();
    for (const element of context.parsed.byId.values()) {
      if (!isBpmnModelElement(element) || !standardContext(element) || removed.has(element)) continue;
      const reference = (value: string | null, at = element) => { const target = resolveBpmnDocumentReference(context.parsed, at, value); return target && deleted.has(bpmnIdentity(target)); };
      if (["sequenceFlow", "messageFlow", "association"].includes(element.localName) && ["sourceRef", "targetRef"].some(name => reference(element.getAttribute(name)))
        || ["dataInputAssociation", "dataOutputAssociation"].includes(element.localName) && [...element.children].some(child => isBpmnModelElement(child) && ["sourceRef", "targetRef"].includes(child.localName) && reference(child.textContent, child))
        || element.localName === "boundaryEvent" && reference(element.getAttribute("attachedToRef"))) { removed.add(element); changed = true; }
    }
  } while (changed);
  const deleted = ids(), bindings = context.parsed.payload.bindings.filter(binding => deleted.has(binding.elementId)), headers = (context.parsed.payload.scopeHeaders ?? []).filter(header => deleted.has(header.scopeElementId));
  const commands: BpmnDocumentCommand[] = [];
  const roots = [...removed].filter(element => ![...removed].some(parent => parent !== element && parent.contains(element)));
  const inside = (element: Element) => roots.some(root => root === element || root.contains(element));
  const removeMembers: Element[] = [];
  for (const element of context.parsed.document.getElementsByTagName("*")) {
    if (inside(element) || !standardContext(element)) continue;
    if (element.namespaceURI === ns.diagram && ["BPMNShape", "BPMNEdge", "BPMNPlane"].includes(element.localName)) {
      const target = resolveBpmnDocumentReference(context.parsed, element, element.getAttribute("bpmnElement"));
      if (target && deleted.has(bpmnIdentity(target))) {
        if (element.localName === "BPMNPlane") commands.push({ kind: "attribute", target: bpmnSourceAddress(element, context.parsed), name: "bpmnElement", value: null });
        else removeMembers.push(element);
      }
    }
    if (!isBpmnModelElement(element)) continue;
    if (["incoming", "outgoing", "flowNodeRef", "dataInputRefs", "dataOutputRefs", "optionalInputRefs", "optionalOutputRefs", "whileExecutingInputRefs", "whileExecutingOutputRefs"].includes(element.localName)) {
      const target = resolveBpmnDocumentReference(context.parsed, element, element.textContent);
      if (target && deleted.has(bpmnIdentity(target))) removeMembers.push(element);
    }
    const target = resolveBpmnDocumentReference(context.parsed, element, element.getAttribute("default"));
    if (target && deleted.has(bpmnIdentity(target))) commands.push({ kind: "attribute", target: bpmnSourceAddress(element, context.parsed), name: "default", value: null });
  }
  // Reverse document order retains exact idless sibling addresses while preceding members are removed.
  for (const element of [...new Set(removeMembers)].reverse().filter(element => !removeMembers.some(parent => parent !== element && parent.contains(element)))) commands.push({ kind: "remove", target: bpmnSourceAddress(element, context.parsed) });
  for (const element of roots.reverse()) commands.push({ kind: "remove", target: bpmnSourceAddress(element, context.parsed) });
  if ((bindings.length || headers.length) && !configuration) return { kind: "requiresNativeCleanup", commands, reason: "The host must resolve native ownership and references in the same transaction.", cleanup: { bindingElementIds: bindings.map(binding => binding.elementId), activityRootIds: bindings.flatMap(binding => [binding.childNodeId, binding.listenerNodeId]).filter((id): id is string => typeof id === "string"), headerScopeIds: headers.map(header => header.scopeElementId) } };
  const plan: BpmnDocumentOperationPlan = { transaction: { commands, ...(configuration ? { configuration } : {}) }, selection: null };
  applyBpmnDocumentTransaction(owner, plan.transaction); // Unhandled standard dependencies refuse before any host state changes.
  return { kind: "ready", plan };
}
