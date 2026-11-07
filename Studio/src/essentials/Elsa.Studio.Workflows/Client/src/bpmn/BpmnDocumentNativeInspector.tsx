import { useMemo, type ComponentProps, type ReactNode } from "react";
import type { StudioActivityDescriptor, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { ActivityCatalogItem, ActivityNode, ActivityPresentationRecord, WorkflowDefinitionState } from "../workflowTypes";
import { createActivityNode, getChildSlots, replaceSlotActivities } from "../workflowAdapter";
import { useScopedVariableAnalysis } from "../api/workflowDesign";
import { supportsScopedVariables } from "../scopedVariables";
import { indexActivityDescriptors, resolveActivityDescriptor } from "../workflow-editor/editorHelpers";
import { toActivityDescriptor } from "../workflow-editor/useWorkflowEditorData";
import { InspectorPanel } from "../workflow-editor/InspectorPanel";
import { createNodeId } from "../workflow-editor/editorHelpers";
import { bpmnDocumentInspectionState, type BpmnDocumentInspection } from "./bpmnDocumentInspection";

export type BpmnDocumentNativeInspectorOverrides = Partial<ComponentProps<typeof InspectorPanel>>;
/** Reuses the existing Elsa panel; only source-owned scope resolution/configuration callbacks differ. */
export function BpmnDocumentNativeInspector(props: {
  context: StudioEndpointContext;
  owner: ActivityNode;
  inspection: BpmnDocumentInspection & { activity: ActivityNode };
  workflowState: WorkflowDefinitionState;
  catalog: ActivityCatalogItem[];
  activityDescriptors: StudioActivityDescriptor[];
  presentation?: ActivityPresentationRecord[];
  readOnly: boolean;
  onChange(next: ActivityNode): boolean;
  onOpenSlot: NonNullable<ComponentProps<typeof InspectorPanel>["onEnterSlot"]>;
  render(overrides: BpmnDocumentNativeInspectorOverrides): ReactNode;
}) {
  const { activity } = props.inspection;
  const catalog = useMemo(() => new Map(props.catalog.map(item => [item.activityVersionId, item])), [props.catalog]);
  const queryState = useMemo(() => bpmnDocumentInspectionState(props.workflowState, props.owner, props.inspection.processId, catalog), [catalog, props.inspection.processId, props.owner, props.workflowState]);
  const analysis = useScopedVariableAnalysis(props.context, queryState, activity.nodeId, catalog);
  const descriptor = useMemo(() => resolveActivityDescriptor(activity, catalog, indexActivityDescriptors(props.activityDescriptors), new Map(props.catalog.map(item => [item.activityVersionId, toActivityDescriptor(item)]))), [activity, catalog, props.activityDescriptors, props.catalog]);
  const item = catalog.get(activity.activityVersionId), presentation = props.presentation?.find(record => record.nodeId === activity.nodeId);
  return props.render({ workflowState: queryState, selectedNode: activity,
    selectedNodeLabel: presentation?.displayName || item?.displayName || activity.nodeId,
    selectedPresentation: presentation, selectedActivityType: descriptor?.typeName ?? item?.activityTypeKey ?? "Unknown", selectedDescriptor: descriptor,
    selectedNodeAvailability: null, selectedSlots: getChildSlots(activity, catalog), inspectingScopeOwner: !!props.inspection.headerScopeId,
    selectedSupportsScopedVariables: !!props.inspection.headerScopeId || supportsScopedVariables(activity, item), scopedVariableAnalysis: analysis,
    readOnly: props.readOnly, variablesPanel: undefined, bpmnRoutingPanel: undefined,
    onSelectedActivityChange: props.onChange, onSelectedStructureChange: props.onChange,
    onSelectedPresentationChange: undefined, onChangeReusableVersion: undefined,
    onReplaceSlotActivity: (ownerNodeId, slot, _label, chosen) => {
      if (props.inspection.headerScopeId || ownerNodeId !== activity.nodeId) return;
      const actualSlot = getChildSlots(activity, catalog).find(candidate => candidate.property === slot.property);
      if (!actualSlot || actualSlot.cardinality !== "single" || !catalog.has(chosen.activityVersionId)) return;
      props.onChange(replaceSlotActivities(activity, actualSlot, [createActivityNode(chosen, createNodeId(chosen))]));
    }, onEnterSlot: props.onOpenSlot });
}

/** Existing opaque Elsa Slot surfaces keep their actual source-owned BPMN lexical query context. */
export function BpmnDocumentQueryInspector(props: {
  context: StudioEndpointContext; workflowState: WorkflowDefinitionState; selectedNodeId: string;
  catalog: ActivityCatalogItem[]; render(overrides: BpmnDocumentNativeInspectorOverrides): ReactNode;
}) {
  const catalog = useMemo(() => new Map(props.catalog.map(item => [item.activityVersionId, item])), [props.catalog]);
  const analysis = useScopedVariableAnalysis(props.context, props.workflowState, props.selectedNodeId, catalog);
  return props.render({ workflowState: props.workflowState, scopedVariableAnalysis: analysis });
}
