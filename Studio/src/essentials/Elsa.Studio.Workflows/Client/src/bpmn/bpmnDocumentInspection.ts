import type { ActivityNode, WorkflowDefinitionState } from "../workflowTypes";
import { canonicalizeStateForWire, expandStateFromWire } from "../activityInputWire";
import { updateActivity, type ActivityCatalogLookup } from "../workflowAdapter";
import { applyBpmnDocumentTransaction, bpmnNamespaces as ns, readBpmnDocument, type BpmnDocumentSelection, type BpmnScopeAddress } from "./bpmnDocument";
import { isActivityBearingElementType, isEventElementType, isGatewayElementType } from "./bpmnTypes";
import { bpmnOwningProcess } from "./bpmnDocumentPolicy";
import { inventoryBpmnDocument } from "./bpmnDocumentInventory";
import type { BpmnDocumentSyntaxPlan } from "./bpmnDocumentSyntaxChange";

export const bpmnDocumentScopeViewKind = "elsa.bpmn.scope-view";
export interface BpmnDocumentInspection {
  activity: ActivityNode | null;
  processId: string;
  headerScopeId?: string;
  bindingRole?: "child" | "listener";
  elementId: string;
  scopeChain: readonly string[];
}
const scopeType = (element: Element) => element.namespaceURI === ns.model && ["process", "subProcess", "transaction", "adHocSubProcess"].includes(element.localName);
function scopeChain(element: Element) {
  const chain: string[] = [];
  for (let current: Element | null = element; current; current = current.parentElement) if (scopeType(current)) chain.push(`${current.localName}:${current.getAttribute("id")}`);
  return chain;
}
function invocationWire(node: ActivityNode): ActivityNode {
  // Compact headers are not physical ActivityNodes. Normalize only this detached invocation view,
  // never its canonical owner, body, compact header records or opaque metadata.
  return canonicalizeStateForWire({ rootActivity: { ...node, structure: null } }).rootActivity!;
}
function headerConfiguration(node: ActivityNode) {
  const wire = invocationWire(node);
  return { nodeId: node.nodeId, activityVersionId: node.activityVersionId, inputs: wire.inputs, outputs: wire.outputs,
    executionPlacement: node.executionPlacement ?? null, intrinsic: node.intrinsic ?? null,
    scopeElementId: node.structure?.payload.scopeElementId, variables: node.structure?.payload.variables };
}

/** Query/inspector views have actual persisted native identities and never become physical authored nodes. */
export function projectBpmnDocumentScopeView(owner: ActivityNode, scopeElementId: string): ActivityNode {
  const source = readBpmnDocument(owner), scope = source.byId.get(scopeElementId);
  if (!scope || !scopeType(scope)) throw new Error("The standard BPMN scope is unavailable.");
  const header = source.payload.scopeHeaders?.find(item => item.scopeElementId === scopeElementId), selected = scopeElementId === source.payload.processId;
  if (!selected && !header) throw new Error("This standard scope has no Elsa invocation header.");
  const nativeIds = new Set(source.payload.bindings.filter(binding => source.byId.get(binding.elementId)?.parentElement === scope)
    .flatMap(binding => [binding.childNodeId, binding.listenerNodeId]).filter((id): id is string => typeof id === "string"));
  const activities = source.payload.activities.filter(activity => nativeIds.has(activity.nodeId));
  for (const child of [...scope.children].filter(scopeType)) if (source.payload.scopeHeaders?.some(item => item.scopeElementId === child.getAttribute("id"))) activities.push(projectBpmnDocumentScopeView(owner, child.getAttribute("id")!));
  const payload = { owner, scopeElementId, variables: selected ? source.payload.variables : header!.variables } as Record<string, unknown>;
  // Client walkers read the derived slot directly. Query JSON carries only canonical owner/context,
  // and the backend derives children again; no duplicate graph or hidden parent lookup is persisted.
  Object.defineProperty(payload, "activities", { value: activities, enumerable: false });
  const invocation = selected ? owner : expandStateFromWire({ rootActivity: { nodeId: header!.nodeId, activityVersionId: header!.activityVersionId,
    inputs: structuredClone(header!.inputs), outputs: structuredClone(header!.outputs), structure: null,
    ...(header!.executionPlacement !== undefined ? { executionPlacement: header!.executionPlacement } : {}) } }).rootActivity!;
  return { ...invocation,
    structure: { kind: bpmnDocumentScopeViewKind, schemaVersion: "1.0.0", payload } };
}

export function readBpmnDocumentInspection(owner: ActivityNode, scope: BpmnScopeAddress, selection: BpmnDocumentSelection | null, role: "child" | "listener" = "child"): BpmnDocumentInspection {
  const source = readBpmnDocument(owner), id = selection?.elementId ?? scope.scopeElementId, element = source.byId.get(id);
  if (scope.documentOwnerNodeId !== owner.nodeId || !element || element.namespaceURI !== ns.model) throw new Error("The BPMN inspection address is stale.");
  const process = bpmnOwningProcess(element), processId = process?.getAttribute("id") ?? source.payload.processId;
  const context = { elementId: id, scopeChain: scopeChain(element) };
  const binding = source.payload.bindings.find(item => item.elementId === id), selectedRole = role === "listener" && binding?.listenerNodeId ? "listener" : binding?.childNodeId ? "child" : "listener";
  const nativeId = selectedRole === "child" ? binding?.childNodeId : binding?.listenerNodeId;
  if (nativeId) return { ...context, activity: source.payload.activities.find(activity => activity.nodeId === nativeId)!, processId, bindingRole: selectedRole };
  if (scopeType(element) && (id === source.payload.processId || source.payload.scopeHeaders?.some(header => header.scopeElementId === id))) return { ...context, activity: projectBpmnDocumentScopeView(owner, id), processId, headerScopeId: id };
  return { ...context, activity: null, processId };
}

/** Independent processes retain global workflow contracts and omit the selected process/outer native scope declarations. */
export function bpmnDocumentInspectionState(state: WorkflowDefinitionState, owner: ActivityNode, processId: string, catalog?: ActivityCatalogLookup): WorkflowDefinitionState {
  const source = readBpmnDocument(owner), view = projectBpmnDocumentScopeView(owner, processId);
  return { ...state, rootActivity: processId === source.payload.processId && state.rootActivity
    ? updateActivity(state.rootActivity, owner.nodeId, () => view, catalog) : view };
}

/** Existing Elsa fields edit native/header configuration; standard XML stays byte-identical. */
export function planBpmnDocumentInspectionChange(owner: ActivityNode, inspection: BpmnDocumentInspection, next: ActivityNode, catalog?: ActivityCatalogLookup): { owner: ActivityNode; plan: BpmnDocumentSyntaxPlan } {
  const previous = inspection.activity;
  if (!previous || previous.nodeId !== next.nodeId) throw new Error("Changing native identity needs the BPMN-owned identity transform.");
  const source = readBpmnDocument(owner);
  const element = source.byId.get(inspection.elementId);
  if (!element || JSON.stringify(scopeChain(element)) !== JSON.stringify(inspection.scopeChain)) throw new Error("The standard ownership chain changed. The Elsa edit needs review.");
  if (inspection.bindingRole) {
    const binding = source.payload.bindings.find(item => item.elementId === inspection.elementId);
    if ((inspection.bindingRole === "child" ? binding?.childNodeId : binding?.listenerNodeId) !== previous.nodeId) throw new Error("The native binding ownership changed. The Elsa edit needs review.");
  }
  const current = inspection.headerScopeId ? projectBpmnDocumentScopeView(owner, inspection.headerScopeId) : source.payload.activities.find(activity => activity.nodeId === previous.nodeId);
  if (!current || JSON.stringify(inspection.headerScopeId ? headerConfiguration(current) : current) !== JSON.stringify(inspection.headerScopeId ? headerConfiguration(previous) : previous)) throw new Error("The Elsa inspector baseline is stale. The proposed configuration is retained for review.");
  let candidate: ActivityNode;
  if (inspection.headerScopeId) {
    if (next.activityVersionId !== previous.activityVersionId || next.structure?.kind !== bpmnDocumentScopeViewKind || next.structure.schemaVersion !== "1.0.0"
      || JSON.stringify(next.intrinsic ?? null) !== JSON.stringify(previous.intrinsic ?? null)) throw new Error("A standard scope header must keep its exact invocation type and role.");
    const payload = next.structure.payload;
    if (JSON.stringify(payload.owner) !== JSON.stringify(previous.structure!.payload.owner) || payload.scopeElementId !== inspection.headerScopeId || !Array.isArray(payload.variables)) throw new Error("The standard scope context was replaced by another source.");
    if (Object.hasOwn(payload, "activities") && JSON.stringify(payload.activities) !== JSON.stringify(previous.structure!.payload.activities)) throw new Error("Standard body edits use the XML canvas operation.");
    const wire = invocationWire(next);
    if (inspection.headerScopeId === source.payload.processId) {
      const invocation: ActivityNode = { ...owner, inputs: next.inputs, outputs: next.outputs, executionPlacement: next.executionPlacement };
      const keys = new Set([...invocationWire(previous).inputs, ...invocationWire(previous).outputs, ...wire.inputs, ...wire.outputs]
        .flatMap(value => value && typeof value === "object" && typeof (value as Record<string, unknown>).referenceKey === "string" ? [(value as Record<string, unknown>).referenceKey as string] : []));
      for (const key of keys) { if (["nodeId", "activityVersionId", "inputs", "outputs", "structure"].includes(key)) throw new Error("The invocation input uses a reserved activity field."); if (Object.hasOwn(next, key)) invocation[key] = next[key]; else delete invocation[key]; }
      candidate = applyBpmnDocumentTransaction(invocation, { configuration: { variables: payload.variables } });
    } else candidate = applyBpmnDocumentTransaction(owner, { configuration: { scopeHeaders: source.payload.scopeHeaders!.map(header => header.scopeElementId !== inspection.headerScopeId ? header : {
      ...header, inputs: wire.inputs, outputs: wire.outputs, variables: payload.variables as typeof header.variables,
      executionPlacement: next.executionPlacement as "inline" | "background" | undefined
    }) } });
  } else {
    if (next.structure?.kind === bpmnDocumentScopeViewKind) throw new Error("A native binding cannot persist a projected standard scope.");
    candidate = applyBpmnDocumentTransaction(owner, { configuration: { activities: source.payload.activities.map(activity => activity.nodeId === next.nodeId ? next : activity) } });
  }
  let removedNodeIds: string[] = [], unqualifiedOwnershipIds: string[] = [];
  if (!inspection.headerScopeId && JSON.stringify(previous.structure) !== JSON.stringify(next.structure)) {
    const before = inventoryBpmnDocument(owner, catalog), after = inventoryBpmnDocument(candidate, catalog);
    const changed = new Set([next.nodeId]);
    for (const entry of before.nativeNodes.values()) if (entry.parentNodeId && changed.has(entry.parentNodeId)) changed.add(entry.nodeId);
    unqualifiedOwnershipIds = before.unqualifiedStructures.filter(entry => changed.has(entry.nodeId)).map(entry => entry.nodeId);
    removedNodeIds = [...before.nativeNodes.keys()].filter(id => !after.nativeNodes.has(id));
  }
  return { owner: candidate, plan: { transaction: { configuration: {
    activities: readBpmnDocument(candidate).payload.activities, variables: readBpmnDocument(candidate).payload.variables,
    ...(source.payload.scopeHeaders !== undefined ? { scopeHeaders: readBpmnDocument(candidate).payload.scopeHeaders } : {})
  } }, removedNodeIds, removedElementIds: [], unqualifiedOwnershipIds } };
}

/** Draft authoring preserves both bodies; publication must resolve their competing execution intent. */
export function readBpmnDocumentBindingConflicts(owner: ActivityNode): { elementId: string; nativeOwnerNodeId: string }[] {
  const parsed = readBpmnDocument(owner);
  return parsed.payload.bindings.flatMap(binding => {
    const element = parsed.byId.get(binding.elementId);
    if (!binding.childNodeId || !element || !["subProcess", "transaction", "adHocSubProcess"].includes(element.localName)) return [];
    const standardBody = [...element.children].some(child => child.namespaceURI === ns.model && (isActivityBearingElementType(child.localName) || isEventElementType(child.localName)
      || isGatewayElementType(child.localName) || ["sequenceFlow", "dataObject", "dataObjectReference", "dataStoreReference"].includes(child.localName)));
    return standardBody ? [{ elementId: binding.elementId, nativeOwnerNodeId: binding.childNodeId }] : [];
  });
}
