import { useState } from "react";
import { AlertTriangle, Repeat2 } from "lucide-react";
import type { StudioActivityDescriptor, StudioActivityPropertyEditorContribution, StudioEndpointContext, StudioExpressionDescriptor, StudioExpressionEditorContribution } from "@elsa-workflows/studio-sdk";
import type { ActivityAvailabilityDiagnosticEntry, ActivityCatalogItem, ActivityNode, VariableDefinition } from "../workflowTypes";
import type { ScopedVariableAnalysis } from "../api/workflows";
import type { ChildSlot } from "../workflowAdapter";
import { getAvailabilityStateLabel } from "../activityAvailability";
import { ActivityPropertiesPanel } from "../ActivityPropertiesPanel";
import { ScopedVariablesEditor } from "../WorkflowPropertiesView";
import { readContainerVariables, shadowingWarningMap, writeContainerVariables } from "../scopedVariables";
import { describeSlotContents } from "./editorHelpers";
import { ConnectMenu } from "./graph";

// The open change-activity picker. Holds only the slot ID (not the ChildSlot descriptor): the menu can
// stay open across draft edits (autosave merge, Weaver batch), so the pick handler re-resolves the slot
// from the live selectedSlots and discards the pick if it no longer exists — a snapshot descriptor could
// silently write to a stale collection index or a removed owner.
interface SlotPickerState {
  nodeId: string;
  slotId: string;
  clientX: number;
  clientY: number;
}

interface InspectorPanelProps {
  context: StudioEndpointContext;
  selectedNode: ActivityNode | null;
  selectedNodeLabel: string;
  selectedActivityType: string;
  selectedDescriptor: StudioActivityDescriptor | null;
  selectedNodeAvailability: ActivityAvailabilityDiagnosticEntry | null;
  selectedSlots: ChildSlot[];
  // True when the inspected node is the scope OWNER (shown because nothing on the canvas is selected):
  // the container whose contents the canvas displays, e.g. the flowchart a slot entry descended into.
  inspectingScopeOwner?: boolean;
  catalog: ActivityCatalogItem[];
  catalogByVersion?: Map<string, ActivityCatalogItem>;
  selectedSupportsScopedVariables: boolean;
  propertyEditors: StudioActivityPropertyEditorContribution[];
  expressionEditors: StudioExpressionEditorContribution[];
  expressionDescriptors: StudioExpressionDescriptor[];
  descriptorStatus: "loading" | "ready" | "failed";
  scopedVariableAnalysis: ScopedVariableAnalysis;
  onSelectedActivityChange(activity: ActivityNode): void;
  onEnterSlot(ownerNodeId: string, slot: ChildSlot, label: string): void;
  // Assign or replace the activity of a single-cardinality slot with a fresh instance of `activity`.
  onReplaceSlotActivity(ownerNodeId: string, slot: ChildSlot, label: string, activity: ActivityCatalogItem): void;
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
  inspectingScopeOwner = false,
  catalog,
  catalogByVersion,
  selectedSupportsScopedVariables,
  propertyEditors,
  expressionEditors,
  expressionDescriptors,
  descriptorStatus,
  scopedVariableAnalysis,
  onSelectedActivityChange,
  onEnterSlot,
  onReplaceSlotActivity
}: InspectorPanelProps) {
  const [slotPicker, setSlotPicker] = useState<SlotPickerState | null>(null);

  // Adjust-during-render: an open picker belongs to the node it was opened for; drop it the moment the
  // selection moves, so it neither survives a selection change nor resurrects on reselection.
  if (slotPicker && slotPicker.nodeId !== selectedNode?.nodeId) {
    setSlotPicker(null);
  }

  if (!selectedNode) {
    return <p className="wf-muted">Select an activity to inspect properties and embedded slots.</p>;
  }

  return (
    <div className="wf-inspector-content">
      <h3>{selectedNodeLabel}</h3>
      {inspectingScopeOwner ? (
        <p className="wf-muted wf-inspector-owner-hint">Container of this canvas — select a node to inspect it instead.</p>
      ) : null}
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
          {selectedSlots.map(slot => {
            const label = `${selectedNodeLabel} / ${slot.label}`;
            return (
              <div className="wf-slot-row" key={slot.id}>
                <button type="button" onClick={() => onEnterSlot(selectedNode.nodeId, slot, label)}>
                  {slot.label}
                  <small>{describeSlotContents(slot, catalogByVersion)}</small>
                </button>
                {slot.cardinality === "single" ? (
                  <button
                    type="button"
                    className="wf-slot-change"
                    aria-label={`${slot.activities.length > 0 ? "Change" : "Choose"} ${slot.label} activity`}
                    title={slot.activities.length > 0 ? "Change activity" : "Choose activity"}
                    onClick={event => setSlotPicker({ nodeId: selectedNode.nodeId, slotId: slot.id, clientX: event.clientX, clientY: event.clientY })}
                  >
                    <Repeat2 size={14} />
                  </button>
                ) : null}
              </div>
            );
          })}
        </div>
      ) : <p className="wf-muted">This activity does not expose embedded child slots.</p>}
      {slotPicker ? (
        <ConnectMenu
          clientX={slotPicker.clientX}
          clientY={slotPicker.clientY}
          activities={catalog}
          onPick={activity => {
            setSlotPicker(null);
            const slot = selectedSlots.find(candidate => candidate.id === slotPicker.slotId);
            if (!slot) return;
            onReplaceSlotActivity(selectedNode.nodeId, slot, `${selectedNodeLabel} / ${slot.label}`, activity);
          }}
          onClose={() => setSlotPicker(null)}
        />
      ) : null}
    </div>
  );
}
