import { useEffect, useId, useState, type ReactNode } from "react";
import { AlertTriangle, Repeat2 } from "lucide-react";
import { CopyableIdentifier, StudioTabPanel, StudioTabs, type StudioTabItem } from "@elsa-workflows/studio-ui";
import type {
  StudioActivityDescriptor,
  StudioActivityPropertyEditorContribution,
  StudioEndpointContext,
  StudioExpressionDescriptor,
  StudioExpressionEditorContribution,
  StudioExpressionToolingClient
} from "@elsa-workflows/studio-sdk";
import type { ActivityAvailabilityDiagnosticEntry, ActivityCatalogItem, ActivityNode, ActivityPresentationRecord, VariableDefinition, WorkflowDefinitionState } from "../workflowTypes";
import type { ActivityDefinitionVersionView, RecommendedActivityDefinition } from "../activityDefinitionTypes";
import type { ScopedVariableAnalysis } from "../api/workflowDesign";
import { slotCrumbLabel, type ChildSlot } from "../workflowAdapter";
import { getAvailabilityStateLabel } from "../activityAvailability";
import { ActivityPropertiesPanel } from "../ActivityPropertiesPanel";
import { ActivityOutputsPanel } from "../ActivityOutputsPanel";
import { IntrinsicInspector } from "../IntrinsicInspector";
import { readIntrinsicDescriptor } from "../intrinsicActivities";
import { ScopedVariablesEditor } from "../WorkflowPropertiesView";
import { readContainerVariables, shadowingWarningMap, writeContainerVariables } from "../scopedVariables";
import { describeSlotContents } from "./editorHelpers";
import { ConnectMenu } from "./graph";
import { activityDescriptionMaxLength, activityDisplayNameMaxLength } from "../activityPresentation";

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

export type ActivityInspectorTabId = "inputs" | "outputs" | "variables" | "slots" | "details" | "version";

export function resolveActivityInspectorTabId(
  requestedTabId: ActivityInspectorTabId,
  supportsVariables: boolean,
  hasSlots: boolean
): ActivityInspectorTabId {
  if (requestedTabId === "variables" && !supportsVariables) return "inputs";
  if (requestedTabId === "slots" && !hasSlots) return "inputs";
  return requestedTabId;
}

interface InspectorPanelProps {
  context: StudioEndpointContext;
  draftId?: string;
  expressionTooling?: StudioExpressionToolingClient;
  expressionEditorSessionScope?: string;
  workflowState?: WorkflowDefinitionState;
  selectedNode: ActivityNode | null;
  selectedNodeLabel: string;
  selectedActivityType: string;
  selectedPresentation?: ActivityPresentationRecord | null;
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
  variablesPanel?: ReactNode;
  readOnly?: boolean;
  propertyEditors: StudioActivityPropertyEditorContribution[];
  expressionEditors: StudioExpressionEditorContribution[];
  expressionDescriptors: StudioExpressionDescriptor[];
  expressionDescriptorStatus: "loading" | "ready" | "failed";
  descriptorStatus: "loading" | "ready" | "failed";
  onRetryExpressionDescriptors(): void;
  scopedVariableAnalysis: ScopedVariableAnalysis;
  activeTabId?: ActivityInspectorTabId;
  onActiveTabChange?(tabId: ActivityInspectorTabId): void;
  onSelectedActivityChange(activity: ActivityNode): void;
  onSelectedPresentationChange?(presentation: Pick<ActivityPresentationRecord, "displayName" | "description">): void;
  onChangeReusableVersion?(activity: ActivityNode, version: ActivityDefinitionVersionView): void;
  onEnterSlot(ownerNodeId: string, slot: ChildSlot, label: string): void;
  // Assign or replace the activity of a single-cardinality slot with a fresh instance of `activity`.
  onReplaceSlotActivity(ownerNodeId: string, slot: ChildSlot, label: string, activity: ActivityCatalogItem): void;
}

// The right-hand inspector for the selected activity: identity, availability notice, property editors,
// container-variable editor, and embedded-slot navigation. Pure view driven by the resolved selection.
export function InspectorPanel({
  context,
  draftId,
  expressionTooling,
  expressionEditorSessionScope,
  workflowState = {},
  selectedNode,
  selectedNodeLabel,
  selectedActivityType,
  selectedPresentation,
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
  variablesPanel,
  readOnly = false,
  propertyEditors,
  expressionEditors,
  expressionDescriptors,
  expressionDescriptorStatus,
  descriptorStatus,
  onRetryExpressionDescriptors,
  scopedVariableAnalysis,
  activeTabId,
  onActiveTabChange,
  onSelectedActivityChange,
  onSelectedPresentationChange,
  onChangeReusableVersion,
  onEnterSlot,
  onReplaceSlotActivity
}: InspectorPanelProps) {
  const [slotPicker, setSlotPicker] = useState<SlotPickerState | null>(null);
  const [localActiveTabId, setLocalActiveTabId] = useState<ActivityInspectorTabId>("inputs");
  const tabBaseId = useId();
  const hasSlots = selectedSlots.length > 0;
  const requestedTabId = activeTabId ?? localActiveTabId;
  const effectiveActiveTabId = resolveActivityInspectorTabId(requestedTabId, selectedSupportsScopedVariables, hasSlots);
  const inspectorTabs: StudioTabItem[] = [
    { id: "inputs", label: "Inputs" },
    { id: "outputs", label: "Outputs" },
    ...(selectedSupportsScopedVariables ? [{ id: "variables" as const, label: "Variables" }] : []),
    ...(hasSlots ? [{ id: "slots" as const, label: "Slots" }] : []),
    { id: "details", label: "Details" },
    { id: "version", label: "Version" }
  ];
  const selectTab = onActiveTabChange ?? setLocalActiveTabId;
  const tabIndexById = new Map(inspectorTabs.map((tab, index) => [tab.id, index]));
  const tabPanelProps = (tabId: ActivityInspectorTabId) => {
    const index = tabIndexById.get(tabId)!;
    return {
      index,
      baseId: tabBaseId,
      className: "wf-inspector-tab-panel",
      hidden: tabId !== effectiveActiveTabId
    };
  };

  useEffect(() => {
    if (effectiveActiveTabId !== requestedTabId) {
      selectTab(effectiveActiveTabId);
    }
  }, [effectiveActiveTabId, requestedTabId, selectTab]);

  // Adjust-during-render: an open picker belongs to the node it was opened for; drop it the moment the
  // selection moves, so it neither survives a selection change nor resurrects on reselection.
  if (slotPicker && slotPicker.nodeId !== selectedNode?.nodeId) {
    setSlotPicker(null);
  }

  if (!selectedNode) {
    return <p className="wf-muted">Select an activity to inspect properties and embedded slots.</p>;
  }

  // An engine-intrinsic node (Set Variable / Set Output) authors a variable target / output name alongside
  // the value, so it uses a dedicated inspector body; everything else uses the standard properties panel.
  const intrinsicDescriptor = readIntrinsicDescriptor(
    catalogByVersion?.get(selectedNode.activityVersionId) ?? catalog.find(item => item.activityVersionId === selectedNode.activityVersionId)
  );

  const propertiesPanel = intrinsicDescriptor ? (
    <IntrinsicInspector
      intrinsic={intrinsicDescriptor}
      context={context}
      draftId={draftId}
      expressionTooling={expressionTooling}
      expressionEditorSessionScope={expressionEditorSessionScope}
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
      showHeading={false}
      emptyLabel="This activity has no configurable inputs."
      onChange={onSelectedActivityChange}
    />
  ) : (
    <ActivityPropertiesPanel
      context={context}
      draftId={draftId}
      expressionTooling={expressionTooling}
      expressionEditorSessionScope={expressionEditorSessionScope}
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
      showHeading={false}
      emptyLabel="This activity has no configurable inputs."
      onChange={onSelectedActivityChange}
    />
  );

  return (
    <div className="wf-inspector-content">
      <div className="wf-inspector-context">
        <h3>{selectedNodeLabel}</h3>
        <CopyableIdentifier label="Node ID" value={selectedNode.nodeId} />
        {inspectingScopeOwner ? (
          <p className="wf-muted wf-inspector-owner-hint">Container of this canvas — select a node to inspect it instead.</p>
        ) : null}
        {selectedNodeAvailability ? (
          <div className="wf-availability-notice">
            <AlertTriangle size={14} />
            <span>No longer available for new use · {getAvailabilityStateLabel(selectedNodeAvailability.state)}</span>
          </div>
        ) : null}
      </div>
      <StudioTabs
        baseId={tabBaseId}
        className="wf-inspector-tabs"
        tabs={inspectorTabs}
        activeTab={effectiveActiveTabId}
        ariaLabel="Activity inspector sections"
        onSelect={tabId => selectTab(tabId as ActivityInspectorTabId)}
      />
      <fieldset className="wf-inspector-tab-panels" disabled={readOnly} aria-disabled={readOnly || undefined}>
        <StudioTabPanel {...tabPanelProps("inputs")}>
          {propertiesPanel}
        </StudioTabPanel>
        <StudioTabPanel {...tabPanelProps("outputs")}>
          <ActivityOutputsPanel
            descriptor={selectedDescriptor}
            activity={selectedNode}
            context={context}
            visibleVariables={scopedVariableAnalysis.visibleVariables}
            scopeStatus={scopedVariableAnalysis.status}
            scopeRetry={scopedVariableAnalysis.retry}
            showHeading={false}
            emptyLabel="This activity has no outputs."
            onChange={onSelectedActivityChange}
          />
        </StudioTabPanel>
        {selectedSupportsScopedVariables ? (
          <StudioTabPanel {...tabPanelProps("variables")}>
            {variablesPanel ?? (
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
            )}
          </StudioTabPanel>
        ) : null}
        {hasSlots ? (
          <StudioTabPanel {...tabPanelProps("slots")}>
            <div className="wf-slot-list">
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
          </StudioTabPanel>
        ) : null}
        <StudioTabPanel {...tabPanelProps("details")}>
          <div className="wf-activity-presentation-fields">
            <label>
              <span>Display name</span>
              <input
                type="text"
                maxLength={activityDisplayNameMaxLength}
                value={selectedPresentation?.displayName ?? ""}
                placeholder={selectedNodeLabel}
                disabled={!onSelectedPresentationChange}
                onChange={event => onSelectedPresentationChange?.({
                  displayName: event.target.value,
                  description: selectedPresentation?.description
                })}
              />
              <small>{selectedPresentation?.displayName?.length ?? 0}/{activityDisplayNameMaxLength}</small>
            </label>
            <label>
              <span>Description</span>
              <textarea
                rows={5}
                maxLength={activityDescriptionMaxLength}
                value={selectedPresentation?.description ?? ""}
                placeholder="Explain what this activity does in this workflow."
                disabled={!onSelectedPresentationChange}
                onChange={event => onSelectedPresentationChange?.({
                  displayName: selectedPresentation?.displayName,
                  description: event.target.value
                })}
              />
              <small>{selectedPresentation?.description?.length ?? 0}/{activityDescriptionMaxLength}</small>
            </label>
          </div>
          <CopyableIdentifier label="Activity type" value={selectedActivityType} />
        </StudioTabPanel>
        <StudioTabPanel {...tabPanelProps("version")}>
          <div className="wf-inspector-version">
            <CopyableIdentifier label="Activity version ID" value={selectedNode.activityVersionId} />
          {selectedReusableDefinitionId ? (
            <ReusableActivityIdentity
              node={selectedNode}
              definitionId={selectedReusableDefinitionId}
              semanticVersion={selectedReusableSemanticVersion}
              version={selectedReusableVersion}
              status={selectedReusableVersionStatus}
              recommendation={selectedRecommendedVersion}
              onChangeVersion={onChangeReusableVersion}
            />
          ) : null}
          </div>
        </StudioTabPanel>
      </fieldset>
    </div>
  );
}

function ReusableActivityIdentity({
  node,
  definitionId,
  semanticVersion,
  version,
  status,
  recommendation,
  onChangeVersion
}: {
  node: ActivityNode;
  definitionId: string;
  semanticVersion?: string | null;
  version?: ActivityDefinitionVersionView | null;
  status: "idle" | "loading" | "ready" | "failed";
  recommendation?: RecommendedActivityDefinition | null;
  onChangeVersion?(activity: ActivityNode, version: ActivityDefinitionVersionView): void;
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
      <p className="wf-muted">This placed occurrence is pinned and read-only at its immutable version. Contract authoring happens in a separate Activity Definition draft.</p>
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
        <button type="button" disabled={!version || !onChangeVersion} onClick={() => {
          if (version) onChangeVersion?.(node, version);
        }}>
          <Repeat2 size={14} /> Change exact version
        </button>
        <a href={sourceUrl}>Open exact source definition</a>
        <a href={draftUrl}>Create a separate draft</a>
      </div>
      {recommendedVersion ? <p className="wf-upgrade-available">Recommended v{recommendedVersion.version} available</p> : null}
    </section>
  );
}
