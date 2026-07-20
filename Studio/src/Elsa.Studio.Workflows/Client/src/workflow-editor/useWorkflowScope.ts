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
import { useScopedVariableAnalysis } from "../api/workflowDesign";
import { findBpmnElement } from "../bpmn/bpmnAdapter";
import { supportsScopedVariables } from "../scopedVariables";
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
  const scopeOwner = useMemo(() => resolveScopeOwner(root, frames, catalogByVersion), [root, frames, catalogByVersion]);
  const designerSupport = getActivityDesignerSupport(scopeOwner, scopeOwner ? catalogByVersion.get(scopeOwner.activityVersionId) : undefined);
  const isUnsupportedDesigner = !!scopeOwner && designerSupport === "unsupported";
  const scope = useMemo(() => isUnsupportedDesigner ? null : resolveScope(root, frames, catalogByVersion), [root, frames, catalogByVersion, isUnsupportedDesigner]);
  // BPMN canvases select ELEMENTS (the canvas node id is the elementId). An activity-bearing element
  // (task/subprocess) resolves to its bound ActivityNode so the ordinary activity inspector applies; a
  // pure structure element (event/gateway) has no ActivityNode and is inspected via
  // `selectedBpmnElement` instead.
  const selectedBpmnElement = useMemo(
    () => scope?.slot.mode === "bpmn" ? findBpmnElement(scope.owner, selectedNodeId) : null,
    [scope, selectedNodeId]
  );
  const selectedNode = useMemo(() => {
    if (isUnsupportedDesigner && scopeOwner?.nodeId === selectedNodeId) return scopeOwner;
    if (selectedBpmnElement) {
      return selectedBpmnElement.childNodeId
        ? scope?.slot.activities.find(activity => activity.nodeId === selectedBpmnElement.childNodeId) ?? null
        : null;
    }
    return scope?.slot.activities.find(activity => activity.nodeId === selectedNodeId) ?? null;
  }, [isUnsupportedDesigner, scope, scopeOwner, selectedBpmnElement, selectedNodeId]);
  const selectedDescriptor = useMemo(
    () => selectedNode ? resolveActivityDescriptor(selectedNode, catalogByVersion, descriptorsByType) : null,
    [catalogByVersion, descriptorsByType, selectedNode]
  );
  const selectedSlots = useMemo(() => selectedNode ? getChildSlots(selectedNode, catalogByVersion) : [], [catalogByVersion, selectedNode]);
  // With nothing selected the inspector shows the scope OWNER — the container whose canvas is
  // displayed. That is the only inspection surface for containers a slot entry descends through
  // (they never appear as nodes), and it covers the root container too.
  const inspectedNode = selectedNode ?? scopeOwner;
  const inspectedIsScopeOwner = !selectedNode && !!scopeOwner;
  const inspectedDescriptor = useMemo(
    () => inspectedNode ? resolveActivityDescriptor(inspectedNode, catalogByVersion, descriptorsByType) : null,
    [catalogByVersion, descriptorsByType, inspectedNode]
  );
  const inspectedNodeAvailability = useMemo(
    () => inspectedNode
      ? availabilityLookup({ activityVersionId: inspectedNode.activityVersionId, activityTypeKey: catalogByVersion.get(inspectedNode.activityVersionId)?.activityTypeKey })
      : null,
    [availabilityLookup, catalogByVersion, inspectedNode]
  );
  const inspectedSlots = useMemo(() => inspectedNode ? getChildSlots(inspectedNode, catalogByVersion) : [], [catalogByVersion, inspectedNode]);
  const inspectedSupportsScopedVariables = inspectedNode
    ? supportsScopedVariables(inspectedNode, catalogByVersion.get(inspectedNode.activityVersionId))
    : false;
  // Scope-aware variable visibility + shadowing for the SELECTED activity (ADR-0027). Deliberately
  // NOT keyed to the inspected owner: the owner changes with every scope navigation, so keying the
  // fetch to it would re-fire the analyze request per navigation. Owner inspection degrades to the
  // hook's no-selection behaviour; revisit when the analyze endpoint ships (elsa-foundation#285).
  // Backed by a pending design endpoint; degrades to status "unavailable" until it ships.
  const scopedVariableAnalysis = useScopedVariableAnalysis(context, draft?.state, selectedNode?.nodeId ?? null, catalogByVersion);
  // Flowchart editing (free connections, empty-canvas add button) is driven by the RESOLVED slot's mode,
  // not the owner's primary-slot mode: with the new scope-frame semantics a container's non-primary slot
  // (e.g. a flowchart-mode branch of a multi-slot activity) can be viewed directly, and it should behave
  // as a flowchart even when the owner's primary slot is a sequence. The only gate is that the owner isn't
  // an opaque/unsupported designer (which suppresses the mirrored canvas entirely).
  const isFlowchartDesigner = !isUnsupportedDesigner && scope?.slot.mode === "flowchart";
  // BPMN canvases share the free-connection editing model with flowcharts but render structure
  // elements (events/gateways) alongside activity-bound tasks; see the bpmn adapter.
  const isBpmnDesigner = !isUnsupportedDesigner && scope?.slot.mode === "bpmn";
  const canAddActivitiesToCanvas = !root || !isUnsupportedDesigner;

  return {
    catalogByVersion,
    availabilityLookup,
    scopeOwner,
    isUnsupportedDesigner,
    scope,
    selectedNode,
    selectedDescriptor,
    selectedSlots,
    inspectedNode,
    inspectedIsScopeOwner,
    inspectedDescriptor,
    inspectedNodeAvailability,
    inspectedSlots,
    inspectedSupportsScopedVariables,
    scopedVariableAnalysis,
    isFlowchartDesigner,
    isBpmnDesigner,
    selectedBpmnElement,
    canAddActivitiesToCanvas
  };
}
