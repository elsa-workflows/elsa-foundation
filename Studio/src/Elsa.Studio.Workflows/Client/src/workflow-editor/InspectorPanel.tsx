import { AlertTriangle } from "lucide-react";
import type { StudioActivityDescriptor, StudioActivityPropertyEditorContribution, StudioEndpointContext, StudioExpressionDescriptor, StudioExpressionEditorContribution } from "@elsa-workflows/studio-sdk";
import type { ActivityAvailabilityDiagnosticEntry, ActivityCatalogItem, ActivityNode, VariableDefinition } from "../workflowTypes";
import type { ScopedVariableAnalysis } from "../api/workflows";
import type { ChildSlot } from "../workflowAdapter";
import { getAvailabilityStateLabel } from "../activityAvailability";
import { ActivityPropertiesPanel } from "../ActivityPropertiesPanel";
import { ScopedVariablesEditor } from "../WorkflowPropertiesView";
import { readContainerVariables, shadowingWarningMap, writeContainerVariables } from "../scopedVariables";
import { describeSlotContents } from "./editorHelpers";

interface InspectorPanelProps {
  context: StudioEndpointContext;
  selectedNode: ActivityNode | null;
  selectedNodeLabel: string;
  selectedActivityType: string;
  selectedDescriptor: StudioActivityDescriptor | null;
  selectedNodeAvailability: ActivityAvailabilityDiagnosticEntry | null;
  selectedSlots: ChildSlot[];
  catalogByVersion?: Map<string, ActivityCatalogItem>;
  selectedSupportsScopedVariables: boolean;
  propertyEditors: StudioActivityPropertyEditorContribution[];
  expressionEditors: StudioExpressionEditorContribution[];
  expressionDescriptors: StudioExpressionDescriptor[];
  descriptorStatus: "loading" | "ready" | "failed";
  scopedVariableAnalysis: ScopedVariableAnalysis;
  onSelectedActivityChange(activity: ActivityNode): void;
  onEnterSlot(owner: ActivityNode, slotId: string, label: string): void;
}

// The right-hand inspector for the selected activity: identity, availability notice, property editors,
// container-variable editor, and embedded-slot navigation. Pure view driven by the resolved selection.
export function InspectorPanel({
  context,
  selectedNode,
  selectedNodeLabel,
  selectedActivityType,
  selectedDescriptor,
  selectedNodeAvailability,
  selectedSlots,
  catalogByVersion,
  selectedSupportsScopedVariables,
  propertyEditors,
  expressionEditors,
  expressionDescriptors,
  descriptorStatus,
  scopedVariableAnalysis,
  onSelectedActivityChange,
  onEnterSlot
}: InspectorPanelProps) {
  if (!selectedNode) {
    return <p className="wf-muted">Select an activity to inspect properties and embedded slots.</p>;
  }

  return (
    <div className="wf-inspector-content">
      <h3>{selectedNodeLabel}</h3>
      <dl>
        <dt>Node ID</dt>
        <dd>{selectedNode.nodeId}</dd>
        <dt>Activity type</dt>
        <dd>{selectedActivityType}</dd>
        <dt>Activity version</dt>
        <dd>{selectedNode.activityVersionId}</dd>
      </dl>
      {selectedNodeAvailability ? (
        <div className="wf-availability-notice">
          <AlertTriangle size={14} />
          <span>No longer available for new use · {getAvailabilityStateLabel(selectedNodeAvailability.state)}</span>
        </div>
      ) : null}
      <ActivityPropertiesPanel
        activity={selectedNode}
        descriptor={selectedDescriptor}
        editors={propertyEditors}
        expressionEditors={expressionEditors}
        expressionDescriptors={expressionDescriptors}
        descriptorStatus={descriptorStatus}
        visibleVariables={scopedVariableAnalysis.visibleVariables}
        scopeStatus={scopedVariableAnalysis.status}
        onChange={onSelectedActivityChange}
      />
      {selectedSupportsScopedVariables ? (
        <div className="wf-container-variables">
          <ScopedVariablesEditor
            context={context}
            variables={readContainerVariables(selectedNode)}
            title="Container variables"
            addLabel="Add container variable"
            emptyLabel="No container variables declared on this activity."
            warnings={shadowingWarningMap(scopedVariableAnalysis.shadowingWarnings, selectedNode.nodeId)}
            onChange={next => onSelectedActivityChange(writeContainerVariables(selectedNode, next as VariableDefinition[]))}
          />
        </div>
      ) : null}
      {selectedSlots.length > 0 ? (
        <div className="wf-slot-list">
          <span>Embedded slots</span>
          {selectedSlots.map(slot => (
            <button type="button" key={slot.id} onClick={() => onEnterSlot(selectedNode, slot.id, `${selectedNodeLabel} / ${slot.label}`)}>
              {slot.label}
              <small>{describeSlotContents(slot, catalogByVersion)}</small>
            </button>
          ))}
        </div>
      ) : <p className="wf-muted">This activity does not expose embedded child slots.</p>}
    </div>
  );
}
