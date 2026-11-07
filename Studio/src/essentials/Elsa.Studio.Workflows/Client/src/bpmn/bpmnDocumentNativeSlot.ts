import type { ActivityNode, WorkflowDefinitionState } from "../workflowTypes";
import { findActivityNode, type ActivityCatalogLookup, type ScopeFrame } from "../workflowAdapter";
import { readBpmnDocument } from "./bpmnDocument";
import { bpmnOwningProcess } from "./bpmnDocumentPolicy";
import { bpmnDocumentInspectionState } from "./bpmnDocumentInspection";

/** Navigation-only context. It never enters ActivityNode, layout, history or a save payload. */
export interface BpmnDocumentNativeSlotContext {
  documentOwnerNodeId: string;
  processId: string;
  nativeOwnerNodeId: string;
}
export interface BpmnDocumentNativeSlotQuery { state: WorkflowDefinitionState; owner: ActivityNode; context: BpmnDocumentNativeSlotContext }
/** A stale/deleted/retargeted native chain cannot keep using a former process's lexical environment. */
export function resolveBpmnDocumentNativeSlotState(state: WorkflowDefinitionState, frames: readonly ScopeFrame[], context: BpmnDocumentNativeSlotContext | null | undefined, catalog?: ActivityCatalogLookup): BpmnDocumentNativeSlotQuery | null {
  if (!context || !frames.some(frame => frame.ownerNodeId === context.nativeOwnerNodeId) || !state.rootActivity) return null;
  const owner = findActivityNode(state.rootActivity, context.documentOwnerNodeId, catalog);
  if (owner?.structure?.kind !== "elsa.bpmn.structure" || owner.structure.schemaVersion !== "2.0.0") return null;
  const parsed = readBpmnDocument(owner), binding = parsed.payload.bindings.find(binding => binding.childNodeId === context.nativeOwnerNodeId || binding.listenerNodeId === context.nativeOwnerNodeId);
  const element = binding && parsed.byId.get(binding.elementId);
  if (!element || bpmnOwningProcess(element)?.getAttribute("id") !== context.processId) return null;
  return { state: bpmnDocumentInspectionState(state, owner, context.processId, catalog), owner, context };
}
