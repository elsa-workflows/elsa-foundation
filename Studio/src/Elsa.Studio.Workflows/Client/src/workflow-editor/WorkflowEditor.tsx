import React, { useCallback, useEffect, useMemo, useState } from "react";
import { ReactFlow, Background, Controls, MiniMap } from "@xyflow/react";
import { AlertCircle, Boxes, Check, ChevronLeft, ChevronRight, Code2, Download, GitBranch, ListTree, Maximize2, Minimize2, Network, Package, Play, Plus, Redo2, Save, SlidersHorizontal, Sparkles, Undo2, Workflow as WorkflowIcon } from "lucide-react";
import type { StudioActivityPropertyEditorContribution, StudioAiContributionApi, StudioEndpointContext, StudioExpressionEditorContribution, StudioWorkflowDesignerPanelContribution } from "@elsa-workflows/studio-sdk";
import type { ActivityCatalogItem, ActivityNode, WorkflowDraft } from "../workflowTypes";
import {
  createActivityNode,
  findNodeScopePath,
  getActivityDisplay,
  normalizeActivityStructures,
  replaceSlotActivities,
  updateActivity,
  type ChildSlot
} from "../workflowAdapter";
import { buildDraftFromJson } from "../workflowSerialization";
import { WorkflowCodeView } from "../WorkflowCodeView";
import { WorkflowPropertiesView } from "../WorkflowPropertiesView";
import { maxInspectorWidth, maxPaletteWidth, minInspectorWidth, minPaletteWidth } from "./constants";
import type { CanvasView, WorkflowDesignerPanelContext, WorkflowEditorOperation, WorkflowEditorPanelTab } from "./editorTypes";
import { WorkflowEdgeActionsContext, WorkflowNodeAvailabilityContext, WorkflowSlotNavigationContext, type WorkflowSlotNavigation } from "./contexts";
import {
  createNodeId,
  dispatchAiAction,
  findAiAction,
  getDraftSignature,
  groupActivityPalette
} from "./editorHelpers";
import { nodeTypes, edgeTypes, ConnectMenu } from "./graph";
import { PanelTabList, compareWorkflowPanelTabs } from "./PanelTabList";
import { ValidationPanel, TestRunStatus, WorkflowRuntimePanel } from "./editorPanels";
import { WorkflowArtifactsPanel } from "./WorkflowExecutables";
import { useSidePanelLayout } from "./useSidePanelLayout";
import { useDraftHistory } from "./useDraftHistory";
import { useWorkflowDocument } from "./workflowDocument";
import { useWorkflowCanvas } from "./useWorkflowCanvas";
import { useWorkflowGraphOperationBatch } from "./useWorkflowGraphOperationBatch";
import { useWorkflowPersistence } from "./useWorkflowPersistence";
import { useWorkflowEditorData } from "./useWorkflowEditorData";
import { useDefinitionMetadata } from "./useDefinitionMetadata";
import { useWorkflowOperations } from "./useWorkflowOperations";
import { useWorkflowScope } from "./useWorkflowScope";
import { useWorkflowContextBridge } from "./useWorkflowContextBridge";
import { ActivityPalettePanel } from "./ActivityPalettePanel";
import { InspectorPanel } from "./InspectorPanel";
import { SlotEmptyState } from "./SlotEmptyState";

export function WorkflowEditor({
  context,
  definitionId,
  ai,
  propertyEditors,
  expressionEditors,
  workflowDesignerPanels,
  autosaveEnabledByDefault,
  onBack
}: {
  context: StudioEndpointContext;
  definitionId: string;
  ai: StudioAiContributionApi;
  propertyEditors: StudioActivityPropertyEditorContribution[];
  expressionEditors: StudioExpressionEditorContribution[];
  workflowDesignerPanels: StudioWorkflowDesignerPanelContribution[];
  autosaveEnabledByDefault?: boolean;
  onBack(): void;
}) {
  // The interdependent draft/scope/selection/test-run/artifact cluster now lives in an explicit reducer,
  // so each mutation (below) declares the transition it makes instead of cascading loose setState calls.
  const editorDoc = useWorkflowDocument();
  const { draft, frames, selectedNodeId, testRun, publishedArtifactId } = editorDoc.state;
  const {
    loadDraft,
    replaceDraftByBatch,
    editDraft,
    editDraftAndSelect,
    select,
    navigateToScope,
    resetToRoot,
    enterSlot,
    startTestRun,
    clearTestRun,
    setPublishedArtifact
  } = editorDoc;
  const [error, setError] = useState("");
  const [status, setStatus] = useState("");
  const [operation, setOperation] = useState<WorkflowEditorOperation>("idle");
  const [expandedPaletteCategories, setExpandedPaletteCategories] = useState<Set<string>>(() => new Set());
  const [paletteSearch, setPaletteSearch] = useState("");
  const [activeLeftPanelId, setActiveLeftPanelId] = useState("activities");
  const [activeRightPanelId, setActiveRightPanelId] = useState("inspector");
  const [canvasView, setCanvasView] = useState<CanvasView>("designer");

  const {
    paletteWidth,
    inspectorWidth,
    paletteCollapsed,
    inspectorCollapsed,
    maximizedSidePanel,
    setInspectorCollapsed,
    paletteExpanded,
    inspectorExpanded,
    editorBodyClassName,
    editorBodyStyle,
    toggleSidePanelCollapsed,
    toggleSidePanelMaximized,
    startSidePanelResize,
    handleSidePanelResizeKeyDown
  } = useSidePanelLayout();

  const { resetHistory, undo, redo, canUndoNow, canRedoNow } = useDraftHistory({ draft, restoreDraft: loadDraft });

  // Draft persistence (serialised save queue + debounced autosave) and the definition data load both live
  // in dedicated hooks; `markSaved`/`reload` let the loader seed the save baseline and reload after promote.
  const { saveDraft, autosaveEnabled, setAutosaveEnabled, markSaved } = useWorkflowPersistence({ context, draft, autosaveEnabledByDefault, editDraft, setStatus, setError });
  const {
    details,
    setDetails,
    catalog,
    activityDescriptors,
    availabilityDiagnostics,
    expressionDescriptors,
    descriptorStatus,
    reload
  } = useWorkflowEditorData({ context, definitionId, resetHistory, loadDraft, markSaved, setError });
  // Debounced name/description save; edits are optimistic and flushed on unmount.
  const { updateDefinitionMeta } = useDefinitionMetadata({ context, details, setDetails, setStatus });

  // Resolve the current scope + selection (designer mode, selected activity, descriptor/availability,
  // scoped-variable analysis) from the raw draft/frames/selection and the loaded catalog + descriptors.
  const {
    catalogByVersion,
    availabilityLookup,
    scopeOwner,
    isUnsupportedDesigner,
    scope,
    selectedNode,
    selectedDescriptor,
    selectedNodeAvailability,
    selectedSlots,
    selectedSupportsScopedVariables,
    scopedVariableAnalysis,
    isFlowchartDesigner,
    canAddActivitiesToCanvas
  } = useWorkflowScope({ context, draft, frames, selectedNodeId, catalog, activityDescriptors, availabilityDiagnostics });

  const paletteGroups = useMemo(() => groupActivityPalette(catalog), [catalog]);
  const filteredPaletteGroups = useMemo(() => {
    const term = paletteSearch.trim().toLowerCase();
    if (!term) return paletteGroups;
    const matches = catalog.filter(activity =>
      getActivityDisplay(activity).toLowerCase().includes(term) ||
      activity.activityTypeKey.toLowerCase().includes(term) ||
      (activity.category ?? "").toLowerCase().includes(term) ||
      (activity.description ?? "").toLowerCase().includes(term));
    return groupActivityPalette(matches);
  }, [catalog, paletteSearch, paletteGroups]);
  const busy = operation !== "idle";
  const canRunTest = !!draft?.state.rootActivity && !busy;
  const findRisksAction = findAiAction(ai, "weaver.workflows.find-draft-risks");
  const proposeUpdateAction = findAiAction(ai, "weaver.workflows.propose-update");

  // The React Flow canvas mirror and every graph interaction (add/drag/connect/splice/delete/auto-layout)
  // live here; it commits edits back into the draft through the document reducer.
  const canvas = useWorkflowCanvas({
    draft,
    scope,
    scopeOwner,
    frames,
    catalog,
    catalogByVersion,
    isUnsupportedDesigner,
    isFlowchartDesigner,
    canAddActivitiesToCanvas,
    selectedNodeId,
    editDraft,
    editDraftAndSelect,
    select,
    resetToRoot,
    setStatus,
    setError
  });
  const {
    nodes,
    edges,
    canvasRef,
    setReactFlowInstance,
    connectMenu,
    setConnectMenu,
    edgeActions,
    onNodesChange,
    onEdgesChange,
    onNodesDelete,
    onEdgesDelete,
    isValidConnection,
    onConnect,
    onConnectStart,
    onConnectEnd,
    onReconnect,
    commitLayout,
    canAutoLayout,
    autoLayout,
    onCanvasDragOver,
    onCanvasDragLeave,
    onCanvasDrop,
    openEmptyConnectMenu,
    onConnectMenuPick,
    addActivity,
    onPaletteClick,
    onPaletteDragStart,
    onPaletteDragEnd,
    onPalettePointerDown
  } = canvas;

  // Inside a slot (breadcrumb non-empty) whose resolved slot is empty, show a guided picker instead of a
  // blank grid. Root-level empty canvas keeps its own affordances (the flowchart "Add activity" button or
  // the first-drop-becomes-root flow), so this is gated on frames.length > 0.
  const insideEmptySlot = !isUnsupportedDesigner
    && frames.length > 0
    && !!scope
    && scope.slot.activities.length === 0
    && nodes.length === 0;

  // Weaver's graph-operation batch apply/undo, dispatched via window events, funnels through the document
  // reducer's batch transition.
  useWorkflowGraphOperationBatch({ draft, details, catalog, replaceDraftByBatch, setStatus, setError });

  useEffect(() => {
    if (!draft?.state.rootActivity || catalog.length === 0) return;

    editDraft(({ draft: current }) => {
      if (!current?.state.rootActivity) return null;
      const normalizedRoot = normalizeActivityStructures(current.state.rootActivity, catalogByVersion);
      if (!normalizedRoot || normalizedRoot === current.state.rootActivity) return null;

      return {
        ...current,
        state: {
          ...current.state,
          rootActivity: normalizedRoot
        }
      };
    });
  }, [catalog.length, catalogByVersion, draft?.state.rootActivity, editDraft]);

  // Mirror the live design onto window for Weaver's out-of-band tooling.
  useWorkflowContextBridge({ details, draft, selectedNode, selectedNodeId, selectedDescriptor, catalogByVersion });

  useEffect(() => {
    setExpandedPaletteCategories(current => {
      let changed = false;
      const next = new Set(current);

      for (const group of paletteGroups) {
        if (!next.has(group.category)) {
          next.add(group.category);
          changed = true;
        }
      }

      return changed ? next : current;
    });
  }, [paletteGroups]);

  // Async toolbar commands (export / save / promote+publish / test run) live in a dedicated hook.
  const { exportJson, save, promoteAndPublish, run } = useWorkflowOperations({
    context,
    draft,
    details,
    busy,
    saveDraft,
    reload,
    startTestRun,
    clearTestRun,
    setPublishedArtifact,
    setOperation,
    setStatus,
    setError,
    setActiveRightPanelId,
    setInspectorCollapsed
  });

  // Properties-tab edits (variables/inputs/outputs) flow through here. Routing them through an in-place
  // draft edit means autosave and undo/redo pick them up automatically — no separate save path.
  const updateDraftState = useCallback((producer: (state: WorkflowDraft["state"]) => WorkflowDraft["state"]) => {
    editDraft(({ draft: current }) => current ? { ...current, state: producer(current.state) } : null);
  }, [editDraft]);

  const applyEditedJson = useCallback((text: string): string | null => {
    if (!draft) return "No draft is loaded.";
    const result = buildDraftFromJson(text, draft);
    if (!result.ok) return result.error;
    loadDraft(result.draft);
    setStatus("Applied workflow JSON.");
    return null;
  }, [draft, loadDraft]);

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (canvasView !== "designer") return;
      if (!(event.metaKey || event.ctrlKey)) return;
      const target = event.target as HTMLElement | null;
      if (target && (target.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(target.tagName))) return;
      const key = event.key.toLowerCase();
      if (key === "z" && !event.shiftKey) {
        event.preventDefault();
        undo();
      } else if ((key === "z" && event.shiftKey) || key === "y") {
        event.preventDefault();
        redo();
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [canvasView, undo, redo]);

  // Enter a slot from the inspector list or a canvas slot badge. A filled single-cardinality slot lands
  // with its assigned activity selected, so the inspector immediately shows that activity's properties.
  const enterSlotScope = useCallback((ownerNodeId: string, slot: ChildSlot, label: string) => {
    const assigned = slot.cardinality === "single" ? slot.activities[0]?.nodeId ?? null : null;
    enterSlot(ownerNodeId, slot.id, label, assigned);
  }, [enterSlot]);

  // Assign or replace the activity of a single-cardinality slot, then land inside the slot with the new
  // activity selected — the same place the initial pick-from-empty-slot flow ends up.
  const replaceSlotActivity = useCallback((ownerNodeId: string, slot: ChildSlot, label: string, activity: ActivityCatalogItem) => {
    const replaced = slot.activities.length > 0;
    const next = createActivityNode(activity, createNodeId(activity));
    editDraft(({ draft: current }) => {
      const rootActivity = current?.state.rootActivity;
      if (!current || !rootActivity) return null;
      return {
        ...current,
        state: {
          ...current.state,
          rootActivity: updateActivity(rootActivity, ownerNodeId, owner => replaceSlotActivities(owner, slot, [next]), catalogByVersion)
        }
      };
    });
    enterSlot(ownerNodeId, slot.id, label, next.nodeId);
    setError("");
    setStatus(replaced ? `Replaced ${slot.label} content` : `Assigned ${getActivityDisplay(activity)} to ${slot.label}`);
  }, [catalogByVersion, editDraft, enterSlot]);

  // Canvas slot badges navigate into their slot (matching the run viewer), wired through a context so
  // node data identity stays stable across drag re-renders. Null (static badges) for the
  // unsupported-designer placeholder, whose node IS the scope owner and has no navigable frame.
  const slotNavigation = useMemo<WorkflowSlotNavigation | null>(() => {
    if (isUnsupportedDesigner) return null;
    return (ownerNodeId, ownerLabel, slot) => enterSlotScope(ownerNodeId, slot, `${ownerLabel} / ${slot.label}`);
  }, [enterSlotScope, isUnsupportedDesigner]);

  const updateSelectedActivity = useCallback((activity: ActivityNode) => {
    editDraft(({ draft: current }) => {
      const rootActivity = current?.state.rootActivity;
      if (!current || !rootActivity) return null;
      return {
        ...current,
        state: {
          ...current.state,
          rootActivity: updateActivity(rootActivity, activity.nodeId, () => activity, catalogByVersion)
        }
      };
    });
  }, [catalogByVersion, editDraft]);

  // Navigates the designer to the activity that owns an invalid scoped variable reference so the
  // author can deliberately re-pick a variable in its scope. We never auto-retarget (ADR-0027).
  const repairVariableReference = useCallback((nodeId: string | null) => {
    if (!nodeId) return;
    const currentRoot = draft?.state.rootActivity;
    if (!currentRoot) return;
    const path = findNodeScopePath(currentRoot, nodeId, activity => {
      const item = catalogByVersion.get(activity.activityVersionId);
      return item ? getActivityDisplay(item) : activity.nodeId;
    }, catalogByVersion);
    if (!path) return;
    setCanvasView("designer");
    navigateToScope(path, nodeId);
    setInspectorCollapsed(false);
  }, [draft?.state.rootActivity, catalogByVersion, setInspectorCollapsed, navigateToScope]);

  const togglePaletteCategory = (category: string) => {
    setExpandedPaletteCategories(current => {
      const next = new Set(current);
      if (next.has(category)) {
        next.delete(category);
      } else {
        next.add(category);
      }
      return next;
    });
  };

  if (!details || !draft) {
    return <div className="wf-empty">{error || "Loading workflow editor..."}</div>;
  }

  const renderedTestRun = testRun?.draftSignature === getDraftSignature(draft)
    ? testRun.view
    : null;
  const visibleStatus = renderedTestRun && status.startsWith("Test run") ? "" : status;
  const openWorkflowRun = (workflowExecutionId: string) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(workflowExecutionId)}`);
    window.dispatchEvent(new PopStateEvent("popstate"));
  };
  const panelContext: WorkflowDesignerPanelContext = {
    definition: details.definition,
    draft,
    selectedActivity: selectedNode,
    selectedActivityDescriptor: selectedDescriptor,
    selectedActivitySlots: selectedSlots,
    catalog,
    currentScopeOwner: scopeOwner,
    frames
  };
  const contributedPanelTabs = workflowDesignerPanels
    .map(panel => {
      const ContributedPanel = panel.component;
      return {
        id: panel.id,
        title: panel.title,
        side: panel.side,
        order: panel.order ?? 500,
        icon: null,
        render: () => <ContributedPanel context={panelContext} />
      };
    });
  const leftPanelTabs: WorkflowEditorPanelTab[] = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: <Boxes size={15} />,
      render: () => (
        <ActivityPalettePanel
          paletteSearch={paletteSearch}
          onSearchChange={setPaletteSearch}
          groups={filteredPaletteGroups}
          expandedCategories={expandedPaletteCategories}
          onToggleCategory={togglePaletteCategory}
          onActivityClick={onPaletteClick}
          onActivityDragStart={onPaletteDragStart}
          onActivityDragEnd={onPaletteDragEnd}
          onActivityPointerDown={onPalettePointerDown}
        />
      )
    },
    ...contributedPanelTabs.filter(tab => tab.side === "left")
  ].sort(compareWorkflowPanelTabs);
  const rightPanelTabs: WorkflowEditorPanelTab[] = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: <ListTree size={15} />,
      render: () => (
        <InspectorPanel
          context={context}
          selectedNode={selectedNode}
          selectedNodeLabel={selectedNode ? (nodes.find(node => node.id === selectedNode.nodeId)?.data.label ?? selectedNode.nodeId) : ""}
          selectedActivityType={selectedNode ? (selectedDescriptor?.typeName ?? catalogByVersion.get(selectedNode.activityVersionId)?.activityTypeKey ?? "Unknown") : ""}
          selectedDescriptor={selectedDescriptor}
          selectedNodeAvailability={selectedNodeAvailability}
          selectedSlots={selectedSlots}
          catalog={catalog}
          catalogByVersion={catalogByVersion}
          selectedSupportsScopedVariables={selectedSupportsScopedVariables}
          propertyEditors={propertyEditors}
          expressionEditors={expressionEditors}
          expressionDescriptors={expressionDescriptors}
          descriptorStatus={descriptorStatus}
          scopedVariableAnalysis={scopedVariableAnalysis}
          onSelectedActivityChange={updateSelectedActivity}
          onEnterSlot={enterSlotScope}
          onReplaceSlotActivity={replaceSlotActivity}
        />
      )
    },
    {
      id: "runtime",
      title: "Runtime",
      order: 5,
      icon: <Play size={15} />,
      render: () => <WorkflowRuntimePanel testRun={renderedTestRun} onOpenRun={openWorkflowRun} />
    },
    {
      id: "artifacts",
      title: "Artifacts",
      order: 10,
      icon: <Package size={15} />,
      render: () => (
        <WorkflowArtifactsPanel
          context={context}
          ai={ai}
          definitionId={details.definition.id}
          publishedArtifactId={publishedArtifactId}
        />
      )
    },
    ...contributedPanelTabs.filter(tab => tab.side === "right")
  ].sort(compareWorkflowPanelTabs);
  const activeLeftPanel = leftPanelTabs.find(tab => tab.id === activeLeftPanelId) ?? leftPanelTabs[0];
  const activeRightPanel = rightPanelTabs.find(tab => tab.id === activeRightPanelId) ?? rightPanelTabs[0];
  const canvasViewTabs: WorkflowEditorPanelTab[] = [
    { id: "designer", title: "Designer", order: 0, icon: <WorkflowIcon size={14} />, render: () => null },
    { id: "code", title: "Code", order: 1, icon: <Code2 size={14} />, render: () => null },
    { id: "properties", title: "Properties", order: 2, icon: <SlidersHorizontal size={14} />, render: () => null }
  ];

  return (
    <section className="wf-editor">
      <div className="wf-editor-top">
        <button type="button" className="wf-link-button" onClick={onBack}>Definitions</button>
        <ChevronRight size={14} />
        <strong>{details.definition.name}</strong>
        <span className="wf-chip">Draft</span>
        {visibleStatus ? <span className="wf-status"><Check size={13} /> {visibleStatus}</span> : null}
        <div className="wf-editor-actions">
          <div className="wf-canvas-tools" role="group" aria-label="Canvas tools">
            <button
              type="button"
              className="wf-icon-button"
              aria-label="Undo"
              title="Undo (Ctrl+Z)"
              disabled={!canUndoNow}
              onClick={undo}>
              <Undo2 size={16} />
            </button>
            <button
              type="button"
              className="wf-icon-button"
              aria-label="Redo"
              title="Redo (Ctrl+Shift+Z)"
              disabled={!canRedoNow}
              onClick={redo}>
              <Redo2 size={16} />
            </button>
            <button
              type="button"
              className="wf-icon-button"
              aria-label="Auto-layout"
              title="Auto-layout the canvas"
              disabled={!canAutoLayout}
              onClick={autoLayout}>
              <Network size={16} />
            </button>
          </div>
          <label className="wf-autosave-toggle">
            <input className="wf-switch-input" type="checkbox" checked={autosaveEnabled} onChange={event => setAutosaveEnabled(event.target.checked)} />
            <span>Autosave</span>
          </label>
          {findRisksAction ? (
            <button type="button" onClick={() => dispatchAiAction(ai, findRisksAction, { definition: details.definition, draft })}><Sparkles size={15} /> Risks</button>
          ) : null}
          {proposeUpdateAction ? (
            <button type="button" onClick={() => dispatchAiAction(ai, proposeUpdateAction, { definition: details.definition, draft })}><Sparkles size={15} /> Propose</button>
          ) : null}
          <button type="button" title="Export workflow as JSON" onClick={exportJson}><Download size={15} /> Export</button>
          <button type="button" disabled={busy} onClick={() => void save()}><Save size={15} /> Save</button>
          <button type="button" disabled={busy} onClick={() => void promoteAndPublish()}><GitBranch size={15} /> Promote</button>
          {renderedTestRun ? (
            <TestRunStatus
              testRun={renderedTestRun}
              onOpenDetails={() => {
                setActiveRightPanelId("runtime");
                setInspectorCollapsed(false);
              }}
            />
          ) : null}
          <button
            type="button"
            disabled={!canRunTest}
            title={draft.state.rootActivity ? "Run a transient test of the current design" : "Add a root activity before running"}
            onClick={() => void run()}>
            <Play size={15} /> Run
          </button>
        </div>
      </div>

      {error ? <div className="wf-alert"><AlertCircle size={16} /> {error}</div> : null}

      <div className={editorBodyClassName} style={editorBodyStyle}>
        <aside className="wf-palette" aria-label="Activities panel">
          <div className="wf-panel-title">
            <PanelTabList
              label="Activities panel tabs"
              tabs={leftPanelTabs}
              activeTabId={activeLeftPanel.id}
              onSelect={setActiveLeftPanelId}
            />
            <span className="wf-panel-actions">
              <button
                type="button"
                className="wf-panel-action-button"
                aria-label={paletteCollapsed ? "Expand activities panel" : "Collapse activities panel"}
                title={paletteCollapsed ? "Expand" : "Collapse"}
                onClick={() => toggleSidePanelCollapsed("palette")}
              >
                {paletteCollapsed ? <ChevronRight size={14} /> : <ChevronLeft size={14} />}
              </button>
              {!paletteCollapsed ? (
                <button
                  type="button"
                  className="wf-panel-action-button"
                  aria-label={maximizedSidePanel === "palette" ? "Restore activities panel" : "Maximize activities panel"}
                  title={maximizedSidePanel === "palette" ? "Restore" : "Maximize"}
                  onClick={() => toggleSidePanelMaximized("palette")}
                >
                  {maximizedSidePanel === "palette" ? <Minimize2 size={14} /> : <Maximize2 size={14} />}
                </button>
              ) : null}
            </span>
          </div>
          {paletteExpanded ? activeLeftPanel.render() : null}
        </aside>

        {paletteExpanded && !maximizedSidePanel ? (
          <div
            className="wf-side-resize-handle left"
            role="separator"
            aria-label="Resize activities panel"
            aria-orientation="vertical"
            aria-valuemin={minPaletteWidth}
            aria-valuemax={maxPaletteWidth}
            aria-valuenow={paletteWidth}
            tabIndex={0}
            onPointerDown={event => startSidePanelResize("palette", event)}
            onKeyDown={event => handleSidePanelResizeKeyDown("palette", event)}
          />
        ) : <div className="wf-side-resize-spacer" />}

        <main className="wf-canvas-shell">
          <div className="wf-canvas-tabs">
            <PanelTabList
              label="Editor view tabs"
              tabs={canvasViewTabs}
              activeTabId={canvasView}
              onSelect={tabId => setCanvasView(tabId as CanvasView)}
            />
          </div>
          {canvasView === "code" ? (
            <WorkflowCodeView draft={draft} onApply={applyEditedJson} />
          ) : canvasView === "properties" ? (
            <WorkflowPropertiesView details={details} draft={draft} context={context} onStateChange={updateDraftState} onDefinitionMetaChange={updateDefinitionMeta} />
          ) : (
          <>
          <div className="wf-breadcrumb">
            <button type="button" onClick={() => resetToRoot()}>Root</button>
            {frames.map((frame, index) => (
              <React.Fragment key={`${frame.ownerNodeId}-${frame.slotId}-${index}`}>
                <ChevronRight size={13} />
                <button type="button" onClick={() => navigateToScope(frames.slice(0, index + 1), null)}>{frame.label}</button>
              </React.Fragment>
            ))}
          </div>
          <div className="wf-canvas" ref={canvasRef} onDragOver={onCanvasDragOver} onDragLeave={onCanvasDragLeave} onDrop={onCanvasDrop}>
            <WorkflowEdgeActionsContext.Provider value={edgeActions}>
              <WorkflowNodeAvailabilityContext.Provider value={availabilityLookup}>
              <WorkflowSlotNavigationContext.Provider value={slotNavigation}>
              <ReactFlow
                nodes={nodes}
                edges={edges}
                nodeTypes={nodeTypes}
                edgeTypes={edgeTypes}
                onInit={setReactFlowInstance}
                onNodesChange={onNodesChange}
                onEdgesChange={onEdgesChange}
                onNodesDelete={onNodesDelete}
                onEdgesDelete={onEdgesDelete}
                onConnect={onConnect}
                onConnectStart={isFlowchartDesigner ? onConnectStart : undefined}
                onConnectEnd={isFlowchartDesigner ? onConnectEnd : undefined}
                onReconnect={isFlowchartDesigner ? onReconnect : undefined}
                isValidConnection={isValidConnection}
                onDragOver={onCanvasDragOver}
                onDragLeave={onCanvasDragLeave}
                onDrop={onCanvasDrop}
                onPaneClick={() => select(null)}
                onNodeClick={(_, node) => select(node.id)}
                onNodeDragStop={isUnsupportedDesigner ? undefined : commitLayout}
                minZoom={0.2}
                maxZoom={1.8}
                nodesConnectable={isFlowchartDesigner}
                nodesDraggable={!isUnsupportedDesigner}
                selectionOnDrag
                multiSelectionKeyCode={["Shift", "Meta", "Control"]}
                deleteKeyCode={isUnsupportedDesigner ? null : ["Backspace", "Delete"]}
                panActivationKeyCode={null}
                defaultEdgeOptions={{ type: "workflow" }}
              >
                <Background gap={18} size={1} />
                <Controls />
                <MiniMap pannable zoomable />
              </ReactFlow>
              </WorkflowSlotNavigationContext.Provider>
              </WorkflowNodeAvailabilityContext.Provider>
            </WorkflowEdgeActionsContext.Provider>
            {insideEmptySlot ? (
              <SlotEmptyState
                slotLabel={scope?.slot.label ?? "this slot"}
                catalog={catalog}
                onPickActivity={addActivity}
                onBrowseAll={openEmptyConnectMenu}
              />
            ) : isFlowchartDesigner && nodes.length === 0 ? (
              <button type="button" className="wf-empty-canvas-add" onClick={() => openEmptyConnectMenu()}>
                <Plus size={15} /> Add activity
              </button>
            ) : null}
            {connectMenu ? (
              <ConnectMenu
                clientX={connectMenu.clientX}
                clientY={connectMenu.clientY}
                activities={catalog}
                onPick={onConnectMenuPick}
                onClose={() => setConnectMenu(null)}
              />
            ) : null}
          </div>
          <ValidationPanel draft={draft} onRepair={repairVariableReference} />
          </>
          )}
        </main>

        {inspectorExpanded && !maximizedSidePanel ? (
          <div
            className="wf-side-resize-handle right"
            role="separator"
            aria-label="Resize inspector panel"
            aria-orientation="vertical"
            aria-valuemin={minInspectorWidth}
            aria-valuemax={maxInspectorWidth}
            aria-valuenow={inspectorWidth}
            tabIndex={0}
            onPointerDown={event => startSidePanelResize("inspector", event)}
            onKeyDown={event => handleSidePanelResizeKeyDown("inspector", event)}
          />
        ) : <div className="wf-side-resize-spacer" />}

        <aside className="wf-inspector" aria-label="Inspector panel">
          <div className="wf-panel-title">
            <PanelTabList
              label="Inspector panel tabs"
              tabs={rightPanelTabs}
              activeTabId={activeRightPanel.id}
              onSelect={setActiveRightPanelId}
            />
            <span className="wf-panel-actions">
              <button
                type="button"
                className="wf-panel-action-button"
                aria-label={inspectorCollapsed ? "Expand inspector panel" : "Collapse inspector panel"}
                title={inspectorCollapsed ? "Expand" : "Collapse"}
                onClick={() => toggleSidePanelCollapsed("inspector")}
              >
                {inspectorCollapsed ? <ChevronLeft size={14} /> : <ChevronRight size={14} />}
              </button>
              {!inspectorCollapsed ? (
                <button
                  type="button"
                  className="wf-panel-action-button"
                  aria-label={maximizedSidePanel === "inspector" ? "Restore inspector panel" : "Maximize inspector panel"}
                  title={maximizedSidePanel === "inspector" ? "Restore" : "Maximize"}
                  onClick={() => toggleSidePanelMaximized("inspector")}
                >
                  {maximizedSidePanel === "inspector" ? <Minimize2 size={14} /> : <Maximize2 size={14} />}
                </button>
              ) : null}
            </span>
          </div>
          {inspectorExpanded ? activeRightPanel.render() : null}
        </aside>
      </div>
    </section>
  );
}
