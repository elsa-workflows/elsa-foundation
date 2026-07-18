import { useState } from "react";
import { AlertTriangle, Repeat2 } from "lucide-react";
import type { StudioActivityDescriptor, StudioActivityPropertyEditorContribution, StudioEndpointContext, StudioExpressionDescriptor, StudioExpressionEditorContribution } from "@elsa-workflows/studio-sdk";
import type { ActivityAvailabilityDiagnosticEntry, ActivityCatalogItem, ActivityNode, VariableDefinition, WorkflowDefinitionState } from "../workflowTypes";
import type { ActivityDefinitionVersionView, RecommendedActivityDefinition } from "../activityDefinitionTypes";
import type { ScopedVariableAnalysis } from "../api/workflowDesign";
import { slotCrumbLabel, type ChildSlot } from "../workflowAdapter";
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
  workflowState?: WorkflowDefinitionState;
  selectedNode: ActivityNode | null;
  selectedNodeLabel: string;
  selectedActivityType: string;
  selectedDescriptor: StudioActivityDescriptor | null;
  selectedNodeAvailability: ActivityAvailabilityDiagnosticEntry | null;
  selectedReusableDefinitionId?: string | null;
  selectedReusableSemanticVersion?: string | null;
  selectedReusableVersion?: ActivityDefinitionVersionView | null;
  selectedReusableVersionStatus?: "idle" | "loading" | "ready" | "failed";
  selectedRecommendedVersion?: RecommendedActivityDefinition | null;
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
  expressionDescriptorStatus: "loading" | "ready" | "failed";
  descriptorStatus: "loading" | "ready" | "failed";
  onRetryExpressionDescriptors(): void;
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
  workflowState = {},
  selectedNode,
  selectedNodeLabel,
  selectedActivityType,
  selectedDescriptor,
  selectedNodeAvailability,
  selectedReusableDefinitionId,
  selectedReusableSemanticVersion,
  selectedReusableVersion,
  selectedReusableVersionStatus = "idle",
  selectedRecommendedVersion,
  selectedSlots,
  inspectingScopeOwner = false,
  catalog,
  catalogByVersion,
  selectedSupportsScopedVariables,
  propertyEditors,
  expressionEditors,
  expressionDescriptors,
  expressionDescriptorStatus,
  descriptorStatus,
  onRetryExpressionDescriptors,
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
      {selectedReusableDefinitionId ? (
        <ReusableActivityIdentity
          node={selectedNode}
          definitionId={selectedReusableDefinitionId}
          semanticVersion={selectedReusableSemanticVersion}
          version={selectedReusableVersion}
          status={selectedReusableVersionStatus}
          recommendation={selectedRecommendedVersion}
        />
      ) : null}
      {selectedNodeAvailability ? (
        <div className="wf-availability-notice">
          <AlertTriangle size={14} />
          <span>No longer available for new use · {getAvailabilityStateLabel(selectedNodeAvailability.state)}</span>
        </div>
      ) : null}
      <ActivityPropertiesPanel
        context={context}
        workflowState={workflowState}
        activity={selectedNode}
        descriptor={selectedDescriptor}
        editors={propertyEditors}
        expressionEditors={expressionEditors}
        expressionDescriptors={expressionDescriptors}
        expressionDescriptorStatus={expressionDescriptorStatus}
        onRetryDescriptors={onRetryExpressionDescriptors}
        descriptorStatus={descriptorStatus}
        visibleVariables={scopedVariableAnalysis.visibleVariables}
        scopeStatus={scopedVariableAnalysis.status}
        scopeRetry={scopedVariableAnalysis.retry}
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
            const label = slotCrumbLabel(selectedNodeLabel, slot);
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
            onReplaceSlotActivity(selectedNode.nodeId, slot, slotCrumbLabel(selectedNodeLabel, slot), activity);
          }}
          onClose={() => setSlotPicker(null)}
        />
      ) : null}
    </div>
  );
}

function ReusableActivityIdentity({
  node,
  definitionId,
  semanticVersion,
  version,
  status,
  recommendation
}: {
  node: ActivityNode;
  definitionId: string;
  semanticVersion?: string | null;
  version?: ActivityDefinitionVersionView | null;
  status: "idle" | "loading" | "ready" | "failed";
  recommendation?: RecommendedActivityDefinition | null;
}) {
  const upgradeAvailable = Boolean(recommendation
    && recommendation.isAvailable
    && recommendation.definitionId === (version?.definition.definitionId ?? definitionId)
    && recommendation.versionId !== node.activityVersionId);
  const recommendedVersion = upgradeAvailable ? recommendation : null;
  const exactDefinitionId = version?.definition.definitionId ?? definitionId;
  const exactVersionId = version?.versionId ?? node.activityVersionId;
  const sourceUrl = `/workflows/activity-definitions?definition=${encodeURIComponent(exactDefinitionId)}&section=versions&version=${encodeURIComponent(exactVersionId)}`;
  const draftUrl = `${sourceUrl}&createDraftFrom=${encodeURIComponent(exactVersionId)}`;
  return (
    <section className="wf-reusable-identity" aria-label="Reusable activity identity">
      <h4>Reusable boundary</h4>
      <p className="wf-muted">This placed occurrence is pinned and read-only. Authoring happens in a separate Activity Definition draft.</p>
      <dl>
        <dt>Definition ID</dt>
        <dd>{exactDefinitionId}</dd>
        <dt>Version ID</dt>
        <dd>{exactVersionId}</dd>
        <dt>Exact version</dt>
        <dd>{version?.version ?? semanticVersion ?? "Unknown"}</dd>
        {version ? (
          <>
            <dt>Provider</dt>
            <dd>{version.provider.providerKey}</dd>
            <dt>Provider schema</dt>
            <dd>{version.provider.schemaVersion}</dd>
            <dt>Lifecycle</dt>
            <dd>{version.lifecycle}</dd>
          </>
        ) : null}
      </dl>
      {status === "loading" ? <p className="wf-muted" role="status">Loading exact version details…</p> : null}
      {status === "failed" ? <p className="wf-muted" role="status">Exact authorized version details are unavailable.</p> : null}
      <div className="wf-reusable-actions">
        <a href={sourceUrl}>Open exact source definition</a>
        <a href={draftUrl}>Create a separate draft</a>
      </div>
      {recommendedVersion ? <p className="wf-upgrade-available">Recommended v{recommendedVersion.version} available</p> : null}
    </section>
  );
}
