import { useCallback, useMemo } from "react";
import type { StudioActivityDescriptor, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { ActivityAvailabilityDiagnostics, ActivityCatalogItem, WorkflowDraft } from "../workflowTypes";
import { findActivityAvailabilityDiagnostic } from "../activityAvailability";
import {
  getActivityDesignerSupport,
  getChildSlots,
  resolveScope,
  resolveScopeOwner,
  type ScopeFrame
} from "../workflowAdapter";
import { useScopedVariableAnalysis } from "../api/workflows";
import { indexActivityDescriptors, resolveActivityDescriptor } from "./editorHelpers";
import type { WorkflowNodeAvailabilityLookup } from "./editorTypes";

interface WorkflowScopeParams {
  context: StudioEndpointContext;
  draft: WorkflowDraft | null;
  frames: ScopeFrame[];
  selectedNodeId: string | null;
  catalog: ActivityCatalogItem[];
  activityDescriptors: StudioActivityDescriptor[];
  availabilityDiagnostics: ActivityAvailabilityDiagnostics | null;
}

// Resolves "where are we and what is selected" from the raw draft/frames/selection plus the loaded
// catalog + descriptors: the current scope owner and slot, the designer mode (flowchart / sequence /
// unsupported), and the selected activity's descriptor, availability and scoped-variable analysis.
// These derivations feed both the canvas (which mutates the scope) and the inspector (which renders it).
export function useWorkflowScope({
  context,
  draft,
  frames,
  selectedNodeId,
  catalog,
  activityDescriptors,
  availabilityDiagnostics
}: WorkflowScopeParams) {
  const root = draft?.state.rootActivity ?? null;
  const catalogByVersion = useMemo(() => new Map(catalog.map(activity => [activity.activityVersionId, activity])), [catalog]);
  const availabilityLookup = useCallback<WorkflowNodeAvailabilityLookup>(
    input => findActivityAvailabilityDiagnostic([input.activityVersionId, input.activityTypeKey], availabilityDiagnostics),
    [availabilityDiagnostics]
  );
  const descriptorsByType = useMemo(() => indexActivityDescriptors(activityDescriptors), [activityDescriptors]);
  const scopeOwner = useMemo(() => resolveScopeOwner(root, frames), [root, frames]);
  const designerSupport = getActivityDesignerSupport(scopeOwner, scopeOwner ? catalogByVersion.get(scopeOwner.activityVersionId) : undefined);
  const isUnsupportedDesigner = !!scopeOwner && designerSupport === "unsupported";
  const scope = useMemo(() => isUnsupportedDesigner ? null : resolveScope(root, frames), [root, frames, isUnsupportedDesigner]);
  const selectedNode = useMemo(() => {
    if (isUnsupportedDesigner && scopeOwner?.nodeId === selectedNodeId) return scopeOwner;
    return scope?.slot.activities.find(activity => activity.nodeId === selectedNodeId) ?? null;
  }, [isUnsupportedDesigner, scope, scopeOwner, selectedNodeId]);
  const selectedDescriptor = useMemo(
    () => selectedNode ? resolveActivityDescriptor(selectedNode, catalogByVersion, descriptorsByType) : null,
    [catalogByVersion, descriptorsByType, selectedNode]
  );
  const selectedNodeAvailability = useMemo(
    () => selectedNode
      ? availabilityLookup({ activityVersionId: selectedNode.activityVersionId, activityTypeKey: catalogByVersion.get(selectedNode.activityVersionId)?.activityTypeKey })
      : null,
    [availabilityLookup, catalogByVersion, selectedNode]
  );
  const selectedSlots = selectedNode ? getChildSlots(selectedNode) : [];
  // Scope-aware variable visibility + shadowing for the selected activity (ADR-0027). Backed by a
  // pending design endpoint; degrades to status "unavailable" until it ships.
  const scopedVariableAnalysis = useScopedVariableAnalysis(context, draft?.state, selectedNodeId);
  const isFlowchartDesigner = designerSupport === "flowchart" && scope?.slot.mode === "flowchart";
  const canAddActivitiesToCanvas = !root || !isUnsupportedDesigner;

  return {
    catalogByVersion,
    availabilityLookup,
    scopeOwner,
    isUnsupportedDesigner,
    scope,
    selectedNode,
    selectedDescriptor,
    selectedNodeAvailability,
    selectedSlots,
    scopedVariableAnalysis,
    isFlowchartDesigner,
    canAddActivitiesToCanvas
  };
}
