import type { ActivityNode } from "../workflowTypes";
import { applyBpmnDocumentTransaction, bpmnNamespaces as ns, readBpmnDocument, resolveBpmnDocumentReference,
  type BpmnDocumentCommand, type BpmnDocumentConfigurationChange, type BpmnPoint, type BpmnScopeAddress } from "./bpmnDocument";
import { planBpmnDocumentMove, projectBpmnDocumentCanvas } from "./bpmnDocumentCanvas";
import { assessBpmnDocumentConnection, bpmnFlowScope, bpmnOwningProcess, isBpmnModelElement } from "./bpmnDocumentPolicy";
import { isActivityBearingElementType, isEventElementType, isGatewayElementType, isSubProcessElementType } from "./bpmnNotationTypes";
import { bpmnChildren, bpmnFragment, bpmnIdentity, bpmnOperationContext, bpmnSourceAddress, insertBpmnFragment,
  isBpmnOwnedSyntax, refuseBpmnOperation, requireBpmnFlowScope, requireBpmnVisibleElement, resolveBpmnOperationAddress } from "./bpmnDocumentOperationXml";
import type { BpmnDocumentOperationPlan } from "./bpmnDocumentCreateOperations";

export interface BpmnReparentIntent {
  elementIds: readonly string[];
  parentElementId: string;
  /** Omission retains valid membership; null explicitly clears it. */
  laneId?: string | null;
  /** Ordinary positions in the original view, keyed by actual DI instance. */
  positions?: ReadonlyMap<string, BpmnPoint>;
}
export interface BpmnContainmentOwnershipRequirement {
  elementIds: string[];
  activityRootIds: string[];
  headerScopeIds: string[];
  sourceScopeIds: string[];
  targetScopeId: string;
  missingHeaderScopeIds: string[];
}
export type BpmnReparentPreparation = { kind: "ready"; plan: BpmnDocumentOperationPlan }
  | { kind: "requiresNativeOwnership"; ownership: BpmnContainmentOwnershipRequirement; commands: readonly BpmnDocumentCommand[]; reason: string };

const flowNode = (type: string) => isActivityBearingElementType(type) || isEventElementType(type) || isGatewayElementType(type);
const movable = (type: string) => flowNode(type) || ["dataObject", "dataObjectReference", "dataStoreReference", "textAnnotation", "group"].includes(type);

/** Move syntax, incident flows, lane records and optional geometry as one resource-owned edit. */
export function prepareBpmnReparent(owner: ActivityNode, scope: BpmnScopeAddress, intent: BpmnReparentIntent,
  configuration?: BpmnDocumentConfigurationChange): BpmnReparentPreparation {
  const context = bpmnOperationContext(owner, scope), target = requireBpmnFlowScope(context, intent.parentElementId);
  if (target.localName !== "process" && target.parentElement !== bpmnFlowScope(target)) refuseBpmnOperation("the target is not an actual standard body");
  requireBpmnVisibleElement(context, target);
  const view = projectBpmnDocumentCanvas(owner, scope);
  const visibleInstance = (element: Element) => {
    const nodes = view.nodes.filter(node => node.data.semanticElementId === bpmnIdentity(element) && !node.hidden);
    if (nodes.length !== 1) refuseBpmnOperation("containment needs one visible actual DI instance");
    return nodes[0];
  };
  if (target !== context.selectedScope && isSubProcessElementType(target.localName)
    && context.scene.shapes.find(shape => shape.shapeId === visibleInstance(target).id)?.isExpanded !== true)
    refuseBpmnOperation("open the target standard body before moving content into it");
  const selected = [...new Set(intent.elementIds)].map(id => resolveBpmnOperationAddress(context, { elementId: id }));
  if (!selected.length) refuseBpmnOperation("choose actual standard constructs to move");
  const roots = new Set(selected.filter(element => !selected.some(parent => parent !== element && parent.contains(element))));
  for (const element of roots) {
    if (!isBpmnModelElement(element) || !movable(element.localName) || !bpmnFlowScope(element)
      || element.parentElement !== bpmnFlowScope(element)) refuseBpmnOperation("containment requires a direct standard flow-scope member");
    requireBpmnVisibleElement(context, element); visibleInstance(element);
    if (element === target || element.contains(target)) refuseBpmnOperation("containment cannot be cyclic");
  }
  // A boundary remains a sibling of its host, never a child moved independently across scopes.
  for (const element of [...roots]) if (element.localName === "boundaryEvent") {
    const host = resolveBpmnDocumentReference(context.parsed, element, element.getAttribute("attachedToRef"));
    if (!host || !roots.has(host) && (element.parentElement !== target || intent.laneId !== undefined)) refuseBpmnOperation("move the boundary's actual host with its scope or lane");
  }
  for (const element of context.parsed.byId.values()) if (isBpmnOwnedSyntax(element) && element.localName === "boundaryEvent"
    && roots.has(resolveBpmnDocumentReference(context.parsed, element, element.getAttribute("attachedToRef"))!)) {
    requireBpmnVisibleElement(context, element); visibleInstance(element); roots.add(element);
  }
  const inside = (element: Element) => [...roots].some(root => root === element || root.contains(element));
  // Data-object definitions travel only when every actual reference travels; shared data is never stolen.
  for (const element of [...roots]) if (element.localName === "dataObjectReference" && element.parentElement !== target) {
    const objectId = (reference: Element) => reference.getAttribute("dataObjectRef")?.replace(/^[\t\n\r ]+|[\t\n\r ]+$/g, "") ?? "";
    const object = context.parsed.byId.get(objectId(element));
    if (!object || object.localName !== "dataObject" || object.parentElement !== element.parentElement)
      refuseBpmnOperation("the data object's existing ownership needs an explicit reconciliation");
    if ([...context.parsed.byId.values()].some(reference => isBpmnOwnedSyntax(reference) && reference.localName === "dataObjectReference"
      && objectId(reference) === bpmnIdentity(object) && !inside(reference))) refuseBpmnOperation("shared data-object references must move together");
    roots.add(object);
  }
  const futureScope = (element: Element) => roots.has(element) ? target : bpmnFlowScope(element);
  const flows: Element[] = [];
  for (const element of context.parsed.byId.values()) if (isBpmnOwnedSyntax(element) && element.localName === "sequenceFlow") {
    const source = resolveBpmnDocumentReference(context.parsed, element, element.getAttribute("sourceRef"));
    const destination = resolveBpmnDocumentReference(context.parsed, element, element.getAttribute("targetRef"));
    if (!source || !destination || !inside(source) && !inside(destination)) continue;
    if (futureScope(source) !== futureScope(destination)) refuseBpmnOperation("an incident sequence flow would cross standard scopes");
    if (element.parentElement !== futureScope(source)) flows.push(element);
  }
  const lane = intent.laneId ? context.parsed.byId.get(intent.laneId) : undefined;
  if (intent.laneId && (!lane || lane.localName !== "lane" || !isBpmnOwnedSyntax(lane) || bpmnFlowScope(lane) !== target))
    refuseBpmnOperation("the target lane must belong to the actual target flow scope");
  if (lane) { requireBpmnVisibleElement(context, lane); visibleInstance(lane); }
  const commands: BpmnDocumentCommand[] = [], memberships: { element: Element; command: BpmnDocumentCommand }[] = [];
  for (const element of roots) if (flowNode(element.localName) && (element.parentElement !== target || intent.laneId !== undefined)) {
    const references = [...context.parsed.document.getElementsByTagNameNS(ns.model, "flowNodeRef")].filter(reference => isBpmnOwnedSyntax(reference)
      && resolveBpmnDocumentReference(context.parsed, reference, reference.textContent) === element);
    const retained = lane ? references.shift() : undefined;
    for (const reference of references) {
      if ([...reference.attributes].some(attribute => attribute.namespaceURI !== "http://www.w3.org/2000/xmlns/")
        || [...reference.childNodes].some(node => ![3, 4].includes(node.nodeType))) refuseBpmnOperation("opaque lane membership needs an explicit retention decision");
      memberships.push({ element: reference, command: { kind: "remove", target: bpmnSourceAddress(reference, context.parsed) } });
    }
    if (lane && retained) {
      const before = bpmnChildren(lane, "childLaneSet")[0];
      memberships.push({ element: retained, command: { kind: "reparent", target: bpmnSourceAddress(retained, context.parsed), parent: { elementId: bpmnIdentity(lane) },
        ...(before ? { before: bpmnSourceAddress(before, context.parsed) } : {}) } });
    }
    else if (lane) { const reference = bpmnFragment(context, "flowNodeRef"); reference.textContent = bpmnIdentity(element); commands.push(insertBpmnFragment(context, lane, reference)); }
  }
  // Descending document order keeps all idless membership addresses valid.
  const documentOrder = [...context.parsed.document.getElementsByTagName("*")];
  commands.unshift(...memberships.sort((a, b) => documentOrder.indexOf(b.element) - documentOrder.indexOf(a.element)).map(edit => edit.command));
  for (const element of roots) if (element.parentElement !== target)
    commands.push({ kind: "reparent", target: { elementId: bpmnIdentity(element) }, parent: { elementId: bpmnIdentity(target) } });
  for (const element of flows) commands.push({ kind: "reparent", target: { elementId: bpmnIdentity(element) },
    parent: { elementId: bpmnIdentity(futureScope(resolveBpmnDocumentReference(context.parsed, element, element.getAttribute("sourceRef"))!)!) } });
  if (intent.positions) {
    const allowed = new Set([...roots].flatMap(element => view.nodes.filter(node => node.data.semanticElementId === bpmnIdentity(element)).map(node => node.id)));
    if ([...intent.positions.keys()].some(id => !allowed.has(id))) refuseBpmnOperation("containment geometry must address the moved actual DI instances");
    commands.push(...planBpmnDocumentMove(view, intent.positions));
  }
  // Native configuration is omitted only from this transient syntax assessment, never from the returned plan.
  const syntax = readBpmnDocument(applyBpmnDocumentTransaction(owner, { commands, configuration: { activities: [], bindings: [], scopeHeaders: [] } }));
  for (const edge of syntax.byId.values()) if (isBpmnOwnedSyntax(edge) && ["sequenceFlow", "messageFlow"].includes(edge.localName)) {
    const source = resolveBpmnDocumentReference(syntax, edge, edge.getAttribute("sourceRef")), destination = resolveBpmnDocumentReference(syntax, edge, edge.getAttribute("targetRef"));
    if (!source || !destination || !inside(context.parsed.byId.get(bpmnIdentity(source))!) && !inside(context.parsed.byId.get(bpmnIdentity(destination))!)) continue;
    const policy = assessBpmnDocumentConnection(syntax, edge.localName as "sequenceFlow" | "messageFlow", bpmnIdentity(source), bpmnIdentity(destination));
    if (!policy.allowed) refuseBpmnOperation(`containment changes an incident relationship: ${policy.reason}`);
  }
  for (const edge of syntax.byId.values()) if (isBpmnOwnedSyntax(edge) && ["dataInputAssociation", "dataOutputAssociation"].includes(edge.localName)) {
    const references = [edge.parentElement!, ...[...bpmnChildren(edge, "sourceRef"), ...bpmnChildren(edge, "targetRef")]
      .map(reference => resolveBpmnDocumentReference(syntax, reference, reference.textContent)).filter((element): element is Element => !!element)];
    if (references.some(element => inside(context.parsed.byId.get(bpmnIdentity(element))!))
      && new Set(references.map(bpmnFlowScope)).size > 1) refuseBpmnOperation("an incident data association needs an explicit scope mapping");
  }
  // Retain independent DI planes; refuse a move that would leave their actual source outside their scope.
  for (const di of syntax.byId.values()) if (di.namespaceURI === ns.diagram && ["BPMNShape", "BPMNEdge"].includes(di.localName)) {
    const element = resolveBpmnDocumentReference(syntax, di, di.getAttribute("bpmnElement"));
    if (!element || !inside(context.parsed.byId.get(bpmnIdentity(element))!) && !flows.includes(context.parsed.byId.get(bpmnIdentity(element))!)) continue;
    const plane = di.parentElement!, planeScope = resolveBpmnDocumentReference(syntax, plane, plane.getAttribute("bpmnElement"));
    const represented = planeScope?.localName === "collaboration" && [...planeScope.children].some(participant => participant.localName === "participant"
      && resolveBpmnDocumentReference(syntax, participant, participant.getAttribute("processRef")) === bpmnOwningProcess(element));
    if (!planeScope || !planeScope.contains(element) && !represented) refuseBpmnOperation("a retained DI plane needs an explicit relocation decision");
  }
  const changed = [...roots].filter(element => element.parentElement !== target);
  const bindings = context.parsed.payload.bindings.filter(binding => inside(context.parsed.byId.get(binding.elementId)!));
  const headers = (context.parsed.payload.scopeHeaders ?? []).filter(header => inside(context.parsed.byId.get(header.scopeElementId)!));
  const missing: string[] = [];
  for (let parent: Element | null = target; parent && parent !== context.parsed.document.documentElement; parent = parent.parentElement)
    if (parent !== context.parsed.process && !context.parsed.payload.scopeHeaders?.some(header => header.scopeElementId === bpmnIdentity(parent))) missing.push(bpmnIdentity(parent));
  if (changed.length && (bindings.length || headers.length) && !configuration) return { kind: "requiresNativeOwnership", commands,
    reason: "The host must review scoped native references and supply exact invocation headers in the same edit; expression text is never rewritten.",
    ownership: { elementIds: [...roots].map(bpmnIdentity), activityRootIds: bindings.flatMap(binding => [binding.childNodeId, binding.listenerNodeId]).filter((id): id is string => typeof id === "string"),
      headerScopeIds: headers.map(header => header.scopeElementId), sourceScopeIds: [...new Set(changed.map(element => bpmnIdentity(bpmnFlowScope(element)!)))], targetScopeId: bpmnIdentity(target), missingHeaderScopeIds: missing } };
  const plan: BpmnDocumentOperationPlan = { transaction: { commands, ...(configuration ? { configuration } : {}) }, selection: { elementId: bpmnIdentity(selected[0]), diId: visibleInstance(selected[0]).id } };
  applyBpmnDocumentTransaction(owner, plan.transaction);
  return { kind: "ready", plan };
}
