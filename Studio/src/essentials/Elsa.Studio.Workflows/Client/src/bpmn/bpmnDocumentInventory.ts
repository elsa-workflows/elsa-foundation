import type { ActivityCatalogItem, ActivityNode, VariableDefinition } from "../workflowTypes";
import { getChildSlots, readStructureDesignFacet, sequenceStructureKind, type ActivityCatalogLookup, type ChildSlot } from "../workflowAdapter";
import { bpmnDocumentSchemaVersion, readBpmnDocument, type BpmnScopeHeader, type ParsedBpmnDocument } from "./bpmnDocument";
import { bpmnStructureKind } from "./bpmnTypes";

interface Ownership {
  nodeId: string;
  /** Physical native owner. XML scope containment is recorded separately. */
  parentNodeId: string | null;
  documentOwnerNodeId: string;
  path: string;
}

export type BpmnNativeInventoryEntry = Ownership & (
  | { kind: "activity"; activity: ActivityNode }
  | { kind: "scopeHeader"; header: BpmnScopeHeader }
);

export interface BpmnDocumentInventoryEntry {
  owner: ActivityNode;
  parsed: ParsedBpmnDocument;
  /** Standard model/DI IDs indexed by the canonical reader. Vendor IDs are not guessed.
   * These IDs can equal IDs in another document or native identities. */
  xmlIds: ReadonlySet<string>;
}

export interface BpmnDocumentInventory {
  documents: ReadonlyMap<string, BpmnDocumentInventoryEntry>;
  nativeNodes: ReadonlyMap<string, BpmnNativeInventoryEntry>;
  /** These boundaries have no qualified ownership descriptor; their opaque payload is not guessed. */
  unqualifiedStructures: readonly { nodeId: string; path: string; reason: string }[];
}

function fail(message: string): never { throw new Error(`Cannot inventory/remap BPMN native configuration: ${message}.`); }
function record(value: unknown): value is Record<string, unknown> { return !!value && typeof value === "object" && !Array.isArray(value); }
function text(value: unknown, name: string): string {
  if (typeof value !== "string" || !value.trim()) fail(`${name} is missing`);
  return value;
}
function completeSource(node: ActivityNode): boolean {
  return node.structure?.kind === bpmnStructureKind && node.structure.schemaVersion === bpmnDocumentSchemaVersion;
}
function catalogItem(node: ActivityNode, catalog: ActivityCatalogLookup): ActivityCatalogItem | undefined {
  return catalog instanceof Map ? catalog.get(node.activityVersionId) : Array.isArray(catalog)
    ? catalog.find(item => item.activityVersionId === node.activityVersionId)
    : catalog?.activityVersionId === node.activityVersionId ? catalog : undefined;
}
function qualifiedSlots(node: ActivityNode, catalog: ActivityCatalogLookup): ChildSlot[] | null {
  if (!node.structure) return [];
  const facet = readStructureDesignFacet(catalogItem(node, catalog));
  if (facet) return facet.kind === node.structure.kind && facet.schemaVersion === node.structure.schemaVersion ? getChildSlots(node, catalog) : null;
  // The exact Sequence ownership contract is known without a catalog. Other fallback heuristics
  // can mistake arbitrary metadata for a child slot and are not proof of complete ownership.
  if (node.structure.kind === sequenceStructureKind && node.structure.schemaVersion === "1.0.0") return getChildSlots(node, catalog);
  return null;
}
function validateSlot(owner: ActivityNode, slot: ChildSlot) {
  const payload = owner.structure!.payload;
  const item = slot.collectionProperty ? (payload[slot.collectionProperty] as unknown[])?.[slot.collectionIndex!] : payload;
  const value = record(item) ? item[slot.childProperty ?? slot.property] : undefined;
  if (slot.cardinality === "many") {
    if (!Array.isArray(value) || value.length !== slot.activities.length) fail(`owned slot '${slot.id}' contains malformed activities`);
  } else if (value != null && slot.activities.length !== 1) fail(`owned slot '${slot.id}' contains a malformed activity`);
}

/** Transient physical ownership only: no generated scope views, JSON ID search or source rewrite. */
export function inventoryBpmnDocument(owner: ActivityNode, catalog?: ActivityCatalogLookup): BpmnDocumentInventory {
  if (!completeSource(owner)) fail("the physical root must use complete-document schema2");
  const documents = new Map<string, BpmnDocumentInventoryEntry>();
  const nativeNodes = new Map<string, BpmnNativeInventoryEntry>();
  const unqualifiedStructures: { nodeId: string; path: string; reason: string }[] = [];
  const seen = new Map<object, string>(), active = new Set<object>();

  function own(value: ActivityNode | BpmnScopeHeader, ownership: Ownership, kind: "activity" | "scopeHeader") {
    const previous = seen.get(value);
    if (active.has(value)) fail(`cyclic ownership at '${ownership.path}'`);
    if (previous) fail(`repeated ownership at '${ownership.path}' (already '${previous}')`);
    if (nativeNodes.has(ownership.nodeId)) fail(`native identity '${ownership.nodeId}' is duplicated`);
    seen.set(value, ownership.path);
    nativeNodes.set(ownership.nodeId, kind === "activity" ? { ...ownership, kind, activity: value as ActivityNode } : { ...ownership, kind, header: value as BpmnScopeHeader });
  }
  function visit(node: ActivityNode, parentNodeId: string | null, documentOwnerNodeId: string, path: string) {
    if (!record(node)) fail(`activity at '${path}' is malformed`);
    own(node, { nodeId: text(node.nodeId, "native nodeId"), parentNodeId, documentOwnerNodeId: completeSource(node) ? node.nodeId : documentOwnerNodeId, path }, "activity");
    if (!Array.isArray(node.inputs) || !Array.isArray(node.outputs)) fail(`activity '${node.nodeId}' requires argument arrays`);
    text(node.activityVersionId, "exact native activity version");
    if (node.structure?.kind === "elsa.bpmn.scope-view") fail("a projected scope view cannot be physically owned");
    active.add(node);
    if (completeSource(node)) {
      const parsed = readBpmnDocument(node);
      documents.set(node.nodeId, { owner: node, parsed, xmlIds: new Set(parsed.byId.keys()) });
      for (const [index, header] of (parsed.payload.scopeHeaders ?? []).entries())
        own(header, { nodeId: header.nodeId, parentNodeId: node.nodeId, documentOwnerNodeId: node.nodeId, path: `${path}/scopeHeaders[${index}]` }, "scopeHeader");
      parsed.payload.activities.forEach((child, index) => visit(child, node.nodeId, node.nodeId, `${path}/activities[${index}]`));
    } else {
      const slots = qualifiedSlots(node, catalog);
      if (slots === null) unqualifiedStructures.push({ nodeId: node.nodeId, path, reason: "No exact native ownership descriptor is available" });
      else for (const slot of slots) {
        validateSlot(node, slot);
        slot.activities.forEach((child, index) => visit(child, node.nodeId, documentOwnerNodeId, `${path}/${slot.id}[${index}]`));
      }
    }
    active.delete(node);
  }
  visit(owner, null, owner.nodeId, "root");
  return { documents, nativeNodes, unqualifiedStructures };
}

/**
 * Explicit bounded copy transform. Standard XML is never parsed for native references or rewritten.
 * Qualified here: complete documents, exact Sequence ownership, canonical typed argument references,
 * catalog-declared expanded inputs/output captures, and Set/SetOutput intrinsics. Every ordinary
 * activity requires its exact catalog contract, including a document owner or Sequence. Other native
 * structures and expression/reference dialects require their own transform before this can be used.
 */
export function remapBpmnDocumentNativeIdentities(owner: ActivityNode, remap: ReadonlyMap<string, string>, catalog?: ActivityCatalogLookup,
  referenceRemap: ReadonlyMap<string, string> = remap): ActivityNode {
  const inventory = inventoryBpmnDocument(owner, catalog);
  if (inventory.unqualifiedStructures.length) fail(`unqualified native ownership on '${inventory.unqualifiedStructures[0].nodeId}'`);
  const targets = new Set<string>();
  for (const [source, target] of remap) {
    if (!inventory.nativeNodes.has(source)) fail(`mapping source '${source}' is outside this inventory`);
    if (source === "workflow") fail("a native identity conflicts with the workflow-scope sentinel");
    text(target, "mapped native identity");
    if (target === "workflow" || targets.has(target)) fail(`mapped native identity '${target}' is reserved or duplicated`);
    targets.add(target);
  }
  if (remap.size !== inventory.nativeNodes.size) fail("a whole-document transform requires every native identity");
  // A clipboard operation can copy this document together with its native parent or siblings.
  // That caller supplies its separately inventoried native map, never XML IDs or a JSON search.
  const referenceTargets = new Set<string>();
  for (const [source, target] of referenceRemap) {
    text(source, "reference mapping source"); text(target, "reference mapping target");
    if (source === "workflow" || target === "workflow" || referenceTargets.has(target)) fail("native reference mapping is reserved or ambiguous");
    if (remap.has(source) && remap.get(source) !== target) fail("reference and ownership mappings disagree");
    referenceTargets.add(target);
  }
  for (const [source, target] of remap) if (referenceRemap.get(source) !== target) fail("reference mapping omits an owned identity");
  const id = (value: unknown) => {
    if (typeof value !== "string" || value === "workflow") return value;
    if (!referenceRemap.has(value) && referenceTargets.has(value)) fail(`mapped identity would capture external native reference '${value}'`);
    return referenceRemap.get(value) ?? value;
  };
  function variable(value: unknown): unknown {
    if (!record(value) || typeof value.referenceKey !== "string" || !value.referenceKey.trim()
      || value.declaringScopeId != null && typeof value.declaringScopeId !== "string") fail("an unqualified Variable reference requires its own transform");
    return value.declaringScopeId == null || id(value.declaringScopeId) === value.declaringScopeId ? value : { ...value, declaringScopeId: id(value.declaringScopeId) };
  }
  function argument(value: unknown): unknown {
    if (!record(value) || typeof value.expressionType !== "string") fail("an argument requires a qualified expression type");
    switch (value.expressionType.toLowerCase()) {
      case "variable": return { ...value, value: variable(value.value) };
      case "activityresult": {
        const reference = value.value;
        if (!record(reference) || typeof reference.producerNodeId !== "string" || !reference.producerNodeId
          || typeof reference.projectionKey !== "string" || !reference.projectionKey
          || reference.producerScopeId != null && typeof reference.producerScopeId !== "string") fail("an unqualified ActivityResult reference requires its own transform");
        return { ...value, value: { ...reference, producerNodeId: id(reference.producerNodeId),
          ...(Object.hasOwn(reference, "producerScopeId") ? { producerScopeId: id(reference.producerScopeId) } : {}) } };
      }
      case "literal": case "object": case "workflowrequest": case "input": case "secret": case "connection": return value;
      default: return fail(`expression type '${value.expressionType}' requires a qualified reference transform`);
    }
  }
  function arguments_(values: unknown[]): unknown[] {
    return values.map(value => {
      if (!record(value) || typeof value.referenceKey !== "string") fail("an authored argument is malformed");
      return { ...value, value: argument(value.value) };
    });
  }
  const variables = (values: VariableDefinition[]) => {
    if (!Array.isArray(values)) fail("native variables require their exact authored array");
    return values.map(value => {
      if (!record(value)) fail("a native variable declaration is malformed");
      return value.default == null ? value : { ...value, default: argument(value.default) as VariableDefinition["default"] };
    });
  };
  function expanded(node: ActivityNode): ActivityNode {
    const item = catalogItem(node, catalog);
    let result = node;
    for (const descriptor of item?.inputs ?? []) {
      if (!record(descriptor) || typeof descriptor.referenceKey !== "string") continue;
      const value = node[descriptor.referenceKey];
      if (!record(value) || !record(value.expression)) continue;
      const mapped = argument({ expressionType: value.expression.type, value: value.expression.value });
      result = { ...result, [descriptor.referenceKey]: { ...value, expression: { ...value.expression, value: (mapped as Record<string, unknown>).value } } };
    }
    for (const descriptor of item?.outputs ?? []) {
      if (!record(descriptor) || typeof descriptor.referenceKey !== "string") continue;
      const value = node[descriptor.referenceKey];
      if (record(value) && Object.hasOwn(value, "target")) result = { ...result, [descriptor.referenceKey]: { ...value, target: variable(value.target) } };
    }
    return result;
  }
  function activity(node: ActivityNode): ActivityNode {
    if (node.structure && !completeSource(node) && (node.structure.kind !== sequenceStructureKind || node.structure.schemaVersion !== "1.0.0"))
      fail(`structure '${node.structure.kind}@${node.structure.schemaVersion}' requires a qualified reference transform`);
    if (!node.intrinsic && !catalogItem(node, catalog)) fail(`activity '${node.nodeId}' requires its exact catalog contract`);
    let next = expanded({ ...node, nodeId: remap.get(node.nodeId)!, inputs: arguments_(node.inputs), outputs: arguments_(node.outputs) });
    if (node.intrinsic) {
      if (!["set", "setoutput"].includes(node.intrinsic.kind.toLowerCase())) fail(`intrinsic '${node.intrinsic.kind}' requires a qualified reference transform`);
      next = { ...next, intrinsic: { ...node.intrinsic, ...(node.intrinsic.variable == null ? {} : { variable: variable(node.intrinsic.variable) as typeof node.intrinsic.variable }) } };
    }
    if (completeSource(node)) {
      const source = inventory.documents.get(node.nodeId)!.parsed.payload;
      next = { ...next, structure: { ...node.structure!, payload: { ...source,
        activities: source.activities.map(activity), variables: variables(source.variables),
        bindings: source.bindings.map(binding => ({ ...binding,
          ...(binding.childNodeId == null ? {} : { childNodeId: id(binding.childNodeId) }),
          ...(binding.listenerNodeId == null ? {} : { listenerNodeId: id(binding.listenerNodeId) }) })),
        ...(source.scopeHeaders === undefined ? {} : { scopeHeaders: source.scopeHeaders.map(header => ({ ...header, nodeId: remap.get(header.nodeId)!,
          inputs: arguments_(header.inputs), outputs: arguments_(header.outputs), variables: variables(header.variables) })) }) } } };
    } else if (node.structure) {
      const payload = node.structure.payload;
      if (!Array.isArray(payload.activities)) fail("the exact Sequence activities slot is unavailable");
      next = { ...next, structure: { ...node.structure, payload: { ...payload, activities: (payload.activities as ActivityNode[]).map(activity),
        ...(payload.variables === undefined ? {} : { variables: variables(payload.variables as VariableDefinition[]) }) } } };
    }
    return next;
  }
  const result = activity(owner);
  // Re-validate global ownership, links and every document after the complete immutable transform.
  inventoryBpmnDocument(result, catalog);
  return result;
}
