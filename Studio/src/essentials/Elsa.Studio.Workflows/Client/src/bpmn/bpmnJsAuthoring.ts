import type { ActivityNode, DesignMetadataRecord, WorkflowDraft } from "../workflowTypes";
import { resolveScope, updateScopeOwner, type ActivityCatalogLookup, type ScopeFrame } from "../workflowAdapter";
import { forgetRemovedNodes } from "../workflow-editor/workflowDocument";
import { collectOwnedNodeIds } from "./bpmnAdapter";
import { bpmnStructureKind } from "./bpmnTypes";

export interface BpmnJsCanvasChange {
  owner: ActivityNode;
  layout: DesignMetadataRecord[];
  baseOwner: ActivityNode;
  baseLayout: DesignMetadataRecord[];
}

/** Applies one acknowledged canvas edit to its live scope, including the side tables of deleted subtrees. */
export function applyBpmnJsCanvasChange(
  current: WorkflowDraft,
  frames: ScopeFrame[],
  change: BpmnJsCanvasChange,
  catalog: ActivityCatalogLookup
): WorkflowDraft | null {
  const root = current.state.rootActivity;
  if (!root) return null;
  const scope = resolveScope(root, frames, catalog);
  if (!scope || scope.slot.mode !== "bpmn" || scope.owner.structure?.kind !== bpmnStructureKind
    || change.owner.nodeId !== scope.owner.nodeId || change.baseOwner.nodeId !== scope.owner.nodeId
    || change.owner.activityVersionId !== scope.owner.activityVersionId
    || change.owner.structure?.kind !== scope.owner.structure.kind
    || change.owner.structure.schemaVersion !== scope.owner.structure.schemaVersion
    || JSON.stringify(scope.owner) !== JSON.stringify(change.baseOwner)
    || JSON.stringify(current.layout) !== JSON.stringify(change.baseLayout)) return null;

  const previousIds = collectOwnedNodeIds(scope.owner, catalog);
  const nextIds = collectOwnedNodeIds(change.owner, catalog);
  const removedIds = new Set([...previousIds].filter(id => !nextIds.has(id)));
  const forgotten = forgetRemovedNodes(current, removedIds);
  // A canvas owns only its scope's layout. Preserve live unrelated records, including records an
  // alternate-canvas payload omitted, while retaining the supplied order for an ordinary full snapshot.
  const unrelated = new Map(forgotten.layout.filter(record => !previousIds.has(record.nodeId)).map(record => [record.nodeId, record]));
  const layout: DesignMetadataRecord[] = [];
  const included = new Set<string>();
  for (const record of change.layout) {
    if (removedIds.has(record.nodeId)) continue;
    const accepted = nextIds.has(record.nodeId) ? record : unrelated.get(record.nodeId);
    if (!accepted) continue;
    if (included.has(record.nodeId)) return null;
    included.add(record.nodeId);
    layout.push(accepted);
  }
  for (const record of unrelated.values()) if (!included.has(record.nodeId)) layout.push(record);

  return {
    ...current,
    ...forgotten,
    layout,
    state: { ...current.state, rootActivity: updateScopeOwner(root, frames, change.owner, catalog) }
  };
}
