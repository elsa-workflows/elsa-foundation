import type { ActivityNode } from "../workflowTypes";
import type { ActivityCatalogLookup } from "../workflowAdapter";
import { applyBpmnDocumentCommands, applyBpmnDocumentTransaction, bpmnNamespaces, readBpmnDocument,
  type BpmnDocumentCommand, type BpmnDocumentTransaction } from "./bpmnDocument";
import { inventoryBpmnDocument } from "./bpmnDocumentInventory";

export interface BpmnDocumentSyntaxPlan {
  readonly transaction: BpmnDocumentTransaction;
  /** Global native identities only; XML IDs remain local to their physical document. */
  readonly removedNodeIds: readonly string[];
  readonly removedElementIds: readonly string[];
  /** A host needs the exact catalog ownership descriptor before cleaning these side tables. */
  readonly unqualifiedOwnershipIds: readonly string[];
}

/** Independently derives native deletion policy from actual XML operations, never child claims. */
export function planBpmnDocumentSyntaxChange(owner: ActivityNode, commands: readonly BpmnDocumentCommand[], catalog?: ActivityCatalogLookup): BpmnDocumentSyntaxPlan {
  const before = readBpmnDocument(owner);
  // Parse the proposed standard syntax independently of its disjoint native associations. The real
  // candidate below still goes through the one atomic source/configuration validator.
  const provisional: ActivityNode = { ...owner, structure: { ...owner.structure!, payload: {
    ...before.payload, activities: [], bindings: [], scopeHeaders: []
  } } };
  const after = readBpmnDocument(applyBpmnDocumentCommands(provisional, commands));
  const removedElementIds = [...before.byId].filter(([id, element]) => element.namespaceURI === bpmnNamespaces.model && !after.byId.has(id)).map(([id]) => id);
  const removed = new Set(removedElementIds);
  const bindings = before.payload.bindings.filter(binding => !removed.has(binding.elementId));
  const removedRoots = new Set(before.payload.bindings.filter(binding => removed.has(binding.elementId))
    .flatMap(binding => [binding.childNodeId, binding.listenerNodeId]).filter((id): id is string => typeof id === "string"));
  const activities = before.payload.activities.filter(activity => !removedRoots.has(activity.nodeId));
  const headers = before.payload.scopeHeaders ?? [];
  const scopeHeaders = headers.filter(header => !removed.has(header.scopeElementId));
  const removedNodeIds = new Set(headers.filter(header => removed.has(header.scopeElementId)).map(header => header.nodeId));
  const unqualifiedOwnershipIds: string[] = [];
  if (removedRoots.size) {
    const inventory = inventoryBpmnDocument(owner, catalog);
    const withinRemoved = (id: string) => {
      let node = inventory.nativeNodes.get(id);
      while (node) {
        if (removedRoots.has(node.nodeId)) return true;
        node = node.parentNodeId ? inventory.nativeNodes.get(node.parentNodeId) : undefined;
      }
      return false;
    };
    for (const id of inventory.nativeNodes.keys()) if (withinRemoved(id)) removedNodeIds.add(id);
    for (const boundary of inventory.unqualifiedStructures) if (withinRemoved(boundary.nodeId)) unqualifiedOwnershipIds.push(boundary.nodeId);
  }
  const hasNativeRemoval = bindings.length !== before.payload.bindings.length || scopeHeaders.length !== headers.length;
  const transaction: BpmnDocumentTransaction = { commands,
    ...(hasNativeRemoval ? { configuration: { activities, bindings, ...(before.payload.scopeHeaders !== undefined ? { scopeHeaders } : {}) } } : {}) };
  // Conversion/reparenting never erase incompatible intent or retarget scoped references. The
  // canonical reader either retains that configuration exactly or refuses the whole transaction.
  applyBpmnDocumentTransaction(owner, transaction);
  return { transaction, removedNodeIds: [...removedNodeIds], removedElementIds, unqualifiedOwnershipIds };
}
