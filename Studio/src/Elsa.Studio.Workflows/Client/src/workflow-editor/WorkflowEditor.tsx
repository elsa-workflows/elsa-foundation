import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { ReactFlow, Background, Controls, MiniMap } from "@xyflow/react";
import { AlertCircle, Boxes, Check, ChevronLeft, ChevronRight, Code2, Download, GitBranch, ListTree, Maximize2, Minimize2, Network, Package, Play, Plus, Redo2, Save, SlidersHorizontal, Sparkles, Undo2, Workflow as WorkflowIcon } from "lucide-react";
import type { StudioActivityPropertyEditorContribution, StudioAiContributionApi, StudioEndpointContext, StudioExpressionEditorContribution, StudioWorkflowDesignerPanelContribution, StudioWorkflowRunInputEditorContribution } from "@elsa-workflows/studio-sdk";
import type { ActivityCatalogItem, ActivityNode, WorkflowDraft } from "../workflowTypes";
import {
  createActivityNode,
  findNodeScopePath,
  getActivityDisplay,
  normalizeActivityStructures,
  planSlotNavigation,
  replaceSlotActivities,
  slotCrumbLabel,
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
import { workflowCanvasAriaLabelConfig } from "./workflowAccessibility";
import { PanelTabList, compareWorkflowPanelTabs } from "./PanelTabList";
import { ScopeBreadcrumb } from "./ScopeBreadcrumb";
import { ValidationPanel, TestRunStatus, WorkflowRuntimePanel } from "./editorPanels";
import { WorkflowArtifactsPanel } from "./WorkflowExecutables";
import { WorkflowRunInputDialog } from "./WorkflowRunInputDialog";
import { useSidePanelLayout } from "./useSidePanelLayout";
import { useDraftHistory } from "./useDraftHistory";
import { useWorkflowDocument } from "./workflowDocument";
import { useWorkflowCanvas } from "./useWorkflowCanvas";
import { useWorkflowGraphOperationBatch } from "./useWorkflowGraphOperationBatch";
import { useWorkflowPersistence } from "./useWorkflowPersistence";
import { useWorkflowEditorData } from "./useWorkflowEditorData";
import { useDefinitionMetadata } from "./useDefinitionMetadata";
import { useWorkflowOperations } from "./useWorkflowOperations";
import { useDraftEquivalence } from "./useDraftEquivalence";
import { useWorkflowScope } from "./useWorkflowScope";
import { useWorkflowContextBridge } from "./useWorkflowContextBridge";
import { ActivityPalettePanel } from "./ActivityPalettePanel";
import { InspectorPanel } from "./InspectorPanel";
import { BpmnElementInspector } from "../bpmn/BpmnElementInspector";
import { BpmnShapePalette } from "../bpmn/BpmnShapePalette";
import { findBpmnElement, readBpmnSequenceFlows, updateBpmnDefaultFlow, updateBpmnElement, updateBpmnFlow } from "../bpmn/bpmnAdapter";
import { bpmnElementTypeLabel, type BpmnElement, type BpmnSequenceFlow } from "../bpmn/bpmnTypes";
import { SlotEmptyState } from "./SlotEmptyState";
import type { PublicationIntent } from "../api/publishing";
import { publicationChangesFor, publicationIntentFor, publicationPreflightMatchesIntent, type PublicationChangeCount, type PublicationReviewState } from "./publicationReview";
import { useDialogFocus } from "./useDialogFocus";
import { useFullActivityDefinitionVersion } from "../api/activityDesign";
import type { ActivityDefinitionVersionView, RecommendedActivityDefinition } from "../activityDefinitionTypes";
import type { ActivityVersionChangeApplyRequest } from "./ActivityVersionChangeDialog";
import {
  applyActivityVersionChange,
  findActivityOccurrence,
  validateActivityVersionChangePrecondition
} from "./activityVersionChangeModel";

const ActivityVersionChangeDialog = React.lazy(() =>
  import("./ActivityVersionChangeDialog").then(module => ({ default: module.ActivityVersionChangeDialog })));

export function WorkflowEditor({
  context,
  definitionId,
  ai,
  propertyEditors,
  expressionEditors,
  runInputEditors,
  workflowDesignerPanels,
  autosaveEnabledByDefault,
  onBack
}: {
  context: StudioEndpointContext;
  definitionId: string;
  ai: StudioAiContributionApi;
  propertyEditors: StudioActivityPropertyEditorContribution[];
  expressionEditors: StudioExpressionEditorContribution[];
  runInputEditors: StudioWorkflowRunInputEditorContribution[];
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
  const [versionChange, setVersionChange] = useState<{
    occurrence: ActivityNode;
    current: ActivityDefinitionVersionView;
    recommendation?: RecommendedActivityDefinition | null;
  } | null>(null);

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
  const { saveDraft, autosaveEnabled, setAutosaveEnabled, setAutosavePaused, markSaved } = useWorkflowPersistence({ context, draft, autosaveEnabledByDefault, editDraft, setStatus, setError });
  const {
    details,
    setDetails,
    catalog,
    paletteCatalog,
    recommendedDefinitions,
    activityDescriptors,
    availabilityDiagnostics,
    expressionDescriptors,
    expressionDescriptorStatus,
    descriptorStatus,
    reload,
    reloadExpressionDescriptors
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
  } = useWorkflowScope({ context, draft, frames, selectedNodeId, catalog, activityDescriptors, availabilityDiagnostics });
  const inspectedCatalogItem = inspectedNode ? catalogByVersion.get(inspectedNode.activityVersionId) : null;
  const inspectedReusableDefinitionId = inspectedCatalogItem?.activityDefinitionId ?? null;
  const inspectedReusableVersion = useFullActivityDefinitionVersion(
    context,
    inspectedNode?.activityVersionId ?? null,
    Boolean(inspectedReusableDefinitionId)
  );
  const inspectedRecommendationDefinitionId = inspectedReusableVersion.data?.definition.definitionId
    ?? inspectedReusableDefinitionId;
  const inspectedRecommendation = inspectedRecommendationDefinitionId
    ? recommendedDefinitions.find(item => item.definitionId === inspectedRecommendationDefinitionId) ?? null
    : null;

  const paletteGroups = useMemo(() => groupActivityPalette(paletteCatalog), [paletteCatalog]);
  const filteredPaletteGroups = useMemo(() => {
    const term = paletteSearch.trim().toLowerCase();
    if (!term) return paletteGroups;
    const matches = paletteCatalog.filter(activity =>
      getActivityDisplay(activity).toLowerCase().includes(term) ||
      activity.activityTypeKey.toLowerCase().includes(term) ||
      (activity.category ?? "").toLowerCase().includes(term) ||
      (activity.description ?? "").toLowerCase().includes(term));
    return groupActivityPalette(matches);
  }, [paletteCatalog, paletteSearch, paletteGroups]);
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
    isBpmnDesigner,
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
    canCreateActivityFromPort,
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
    addBpmnShape,
    onPaletteClick,
    onPaletteDragStart,
    onPaletteDragEnd,
    onPalettePointerDown
  } = canvas;

  // Inside a slot (breadcrumb non-empty) whose resolved slot is empty, show a guided picker instead of a
  // blank grid. Root-level empty canvas keeps its own affordances (the flowchart "Add activity" button or
  // the first-drop-becomes-root flow), so this is gated on frames.length > 0. Flowchart-mode scopes are
  // excluded: they are already a full canvas (slot entry descends into containers), so they get the same
  // empty-canvas "Add activity" button as the root instead of the choose-a-container card.
  const insideEmptySlot = !isUnsupportedDesigner
    && !isFlowchartDesigner
    && !isBpmnDesigner
    && frames.length > 0
    && !!scope
    && scope.slot.activities.length === 0
    && nodes.length === 0;

  // SlotEmptyState pick: after the add lands, descend into the picked activity when it is a canvas
  // container — the same view slot entry would choose (a one-node canvas holding a container helps
  // nobody). For leaves the plan only retargets, so addActivity's own selection stands and no
  // navigation happens.
  const pickActivityForEmptySlot = useCallback((activity: ActivityCatalogItem) => {
    const added = addActivity(activity);
    if (!added || !scope || !scopeOwner || frames.length === 0) return;
    const label = frames[frames.length - 1].label;
    const plan = planSlotNavigation(frames, scopeOwner, scopeOwner.nodeId, { ...scope.slot, activities: [added] }, label, catalogByVersion);
    if (plan && plan.frames.length > frames.length) navigateToScope(plan.frames, plan.selectedNodeId);
  }, [addActivity, catalogByVersion, frames, navigateToScope, scope, scopeOwner]);

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
  useWorkflowContextBridge({ details, draft, selectedNode, selectedNodeId, selectedDescriptor, inspectedNode, inspectedDescriptor, inspectedIsScopeOwner, catalogByVersion });

  // Memoized so contributed panels get a stable context identity and can bail out of re-renders
  // (React.memo / effect deps) while the editor re-renders for unrelated reasons.
  const panelContext = useMemo<WorkflowDesignerPanelContext | null>(() => {
    if (!details || !draft) return null;
    return {
      definition: details.definition,
      draft,
      selectedActivity: selectedNode,
      selectedActivityDescriptor: selectedDescriptor,
      selectedActivitySlots: selectedSlots,
      inspectedActivity: inspectedNode,
      inspectedActivityDescriptor: inspectedDescriptor,
      inspectedActivitySlots: inspectedSlots,
      inspectedIsScopeOwner,
      catalog,
      currentScopeOwner: scopeOwner,
      frames
    };
  }, [catalog, details, draft, frames, inspectedDescriptor, inspectedIsScopeOwner, inspectedNode, inspectedSlots, scopeOwner, selectedDescriptor, selectedNode, selectedSlots]);

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
  const {
    exportJson,
    save,
    preparePublication,
    publicationReview,
    confirmPublication,
    cancelPublication,
    runInputPrompt,
    confirmRunInputs,
    cancelRunInputs,
    run
  } = useWorkflowOperations({
    context,
    draft,
    details,
    catalog,
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
    setInspectorCollapsed,
    setAutosavePaused
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

  // Enter a slot from the inspector list or a canvas slot badge, along the planned frame path: a slot
  // holding a single canvas container is descended THROUGH (the canvas shows the container's contents),
  // a slot holding a single leaf lands with the leaf selected, and slots of the current scope owner
  // retarget the last frame. A null plan (non-primary slot of the root owner) is not navigable.
  const enterSlotScope = useCallback((ownerNodeId: string, slot: ChildSlot, label: string) => {
    const plan = planSlotNavigation(frames, scopeOwner, ownerNodeId, slot, label, catalogByVersion);
    if (plan) navigateToScope(plan.frames, plan.selectedNodeId);
  }, [catalogByVersion, frames, navigateToScope, scopeOwner]);

  // Assign or replace the activity of a single-cardinality slot, then navigate the same way slot entry
  // would: inside a just-assigned container's canvas, or landing on a just-assigned leaf selected.
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
    const plan = planSlotNavigation(frames, scopeOwner, ownerNodeId, { ...slot, activities: [next] }, label, catalogByVersion);
    if (plan) navigateToScope(plan.frames, plan.selectedNodeId);
    setError("");
    setStatus(replaced ? `Replaced ${slot.label} content` : `Assigned ${getActivityDisplay(activity)} to ${slot.label}`);
  }, [catalogByVersion, editDraft, frames, navigateToScope, scopeOwner]);

  // Canvas slot badges navigate into their slot (matching the run viewer), wired through a context so
  // node data identity stays stable across drag re-renders. Null (static badges) for the
  // unsupported-designer placeholder, whose node IS the scope owner and has no navigable frame.
  const slotNavigation = useMemo<WorkflowSlotNavigation | null>(() => {
    if (isUnsupportedDesigner) return null;
    return (ownerNodeId, ownerLabel, slot) => enterSlotScope(ownerNodeId, slot, slotCrumbLabel(ownerLabel, slot));
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

  // Rewrites the current scope owner's BPMN payload through `apply` (inspector edits for pure
  // elements and their outbound sequence flows).
  const updateScopeOwnerBpmnPayload = useCallback((apply: (owner: ActivityNode) => ActivityNode) => {
    const ownerNodeId = scopeOwner?.nodeId;
    if (!ownerNodeId) return;
    editDraft(({ draft: current }) => {
      const rootActivity = current?.state.rootActivity;
      if (!current || !rootActivity) return null;
      return {
        ...current,
        state: {
          ...current.state,
          rootActivity: updateActivity(rootActivity, ownerNodeId, apply, catalogByVersion)
        }
      };
    });
  }, [catalogByVersion, editDraft, scopeOwner?.nodeId]);

  const updateSelectedBpmnElement = useCallback((elementId: string, patch: Partial<BpmnElement>) =>
    updateScopeOwnerBpmnPayload(owner => updateBpmnElement(owner, elementId, patch)), [updateScopeOwnerBpmnPayload]);

  const updateSelectedBpmnFlow = useCallback((flowId: string, patch: Partial<BpmnSequenceFlow>) =>
    updateScopeOwnerBpmnPayload(owner => updateBpmnFlow(owner, flowId, patch)), [updateScopeOwnerBpmnPayload]);

  const setSelectedBpmnDefaultFlow = useCallback((sourceElementId: string, flowId: string | null) =>
    updateScopeOwnerBpmnPayload(owner => updateBpmnDefaultFlow(owner, sourceElementId, flowId)), [updateScopeOwnerBpmnPayload]);

  const selectedBpmnElementFlows = useMemo(() =>
    selectedBpmnElement && scopeOwner
      ? readBpmnSequenceFlows(scopeOwner).filter(flow => flow.sourceRef === selectedBpmnElement.elementId)
      : [], [scopeOwner, selectedBpmnElement]);

  const bpmnFlowTargetLabel = useCallback((flow: BpmnSequenceFlow) => {
    const target = findBpmnElement(scopeOwner, flow.targetRef);
    return target ? target.name?.trim() || bpmnElementTypeLabel(target) : flow.targetRef;
  }, [scopeOwner]);

  const openVersionChange = useCallback((occurrence: ActivityNode, current: ActivityDefinitionVersionView) => {
    setError("");
    setAutosavePaused(true);
    setVersionChange({ occurrence, current, recommendation: inspectedRecommendation });
  }, [inspectedRecommendation, setAutosavePaused]);

  const cancelVersionChange = useCallback(() => {
    setVersionChange(null);
    setAutosavePaused(false);
  }, [setAutosavePaused]);

  const applyVersionChange = useCallback(async (request: ActivityVersionChangeApplyRequest) => {
    if (!draft) throw new Error("No workflow draft is loaded.");
    const staleReason = validateActivityVersionChangePrecondition(draft, request.precondition);
    if (staleReason) throw new Error(staleReason);
    const next = applyActivityVersionChange(
      draft,
      request.precondition.occurrenceId,
      request.precondition.fromVersionId,
      request.targetVersionId,
      request.scope
    );
    if (next === draft) throw new Error("The reviewed occurrence no longer matches this version change.");

    const saved = await saveDraft(next, "Activity version changed");
    const savedOccurrence = findActivityOccurrence(saved.state.rootActivity, request.precondition.occurrenceId);
    if (!savedOccurrence || savedOccurrence.activityVersionId !== request.targetVersionId) {
      throw new Error("The server did not confirm the reviewed exact version change.");
    }
    loadDraft(saved);
    setVersionChange(null);
    setAutosavePaused(false);
  }, [draft, loadDraft, saveDraft, setAutosavePaused]);

  // Navigates the designer to the activity that owns an invalid scoped variable reference so the
  // author can deliberately re-pick a variable in its scope. We never auto-retarget (ADR-0027). The
  // path follows planSlotNavigation's breadcrumb conventions, so landing here reads exactly like
  // having navigated through slot entry.
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

  const renderedTestRun = draft && testRun?.draftSignature === getDraftSignature(draft)
    ? testRun.view
    : null;
  // ADR 0040's equivalence signal: a test run resolving to a published artifact id proves the current
  // draft is behaviorally identical to that published version. Above the loading return — hooks must
  // run on every render.
  const publishedEquivalent = useDraftEquivalence(context, definitionId, renderedTestRun);

  if (!details || !draft) {
    return <div className="wf-empty">{error || "Loading workflow editor..."}</div>;
  }

  // The inspected node is either a canvas node (labelled by its node data) or the scope owner, which
  // has no node on its own canvas — fall back to its catalog display name.
  const inspectedLabel = inspectedNode
    ? (nodes.find(node => node.id === inspectedNode.nodeId)?.data.label
      ?? (inspectedCatalogItem ? getActivityDisplay(inspectedCatalogItem) : inspectedNode.nodeId))
    : "";

  const visibleStatus = renderedTestRun && status.startsWith("Test run") ? "" : status;
  const openWorkflowRun = (workflowExecutionId: string) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(workflowExecutionId)}`);
    window.dispatchEvent(new PopStateEvent("popstate"));
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
        render: () => panelContext && <ContributedPanel context={panelContext} />
      };
    });
  const leftPanelTabs: WorkflowEditorPanelTab[] = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: <Boxes size={15} />,
      render: () => (
        <>
          {isBpmnDesigner ? <BpmnShapePalette onAddShape={addBpmnShape} /> : null}
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
        </>
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
      render: () => selectedBpmnElement && !selectedNode ? (
        <BpmnElementInspector
          element={selectedBpmnElement}
          outboundFlows={selectedBpmnElementFlows}
          onChangeElement={updateSelectedBpmnElement}
          onChangeFlow={updateSelectedBpmnFlow}
          onSetDefaultFlow={setSelectedBpmnDefaultFlow}
          targetLabelFor={bpmnFlowTargetLabel}
        />
      ) : (
        <InspectorPanel
          context={context}
          workflowState={draft.state}
          selectedNode={inspectedNode}
          selectedNodeLabel={inspectedLabel}
          selectedActivityType={inspectedNode ? (inspectedDescriptor?.typeName ?? catalogByVersion.get(inspectedNode.activityVersionId)?.activityTypeKey ?? "Unknown") : ""}
          selectedDescriptor={inspectedDescriptor}
          selectedNodeAvailability={inspectedNodeAvailability}
          selectedReusableDefinitionId={inspectedReusableDefinitionId}
          selectedReusableSemanticVersion={inspectedCatalogItem?.activityDefinitionVersion}
          selectedReusableVersion={inspectedReusableVersion.data}
          selectedReusableVersionStatus={!inspectedReusableDefinitionId
            ? "idle"
            : inspectedReusableVersion.isPending
              ? "loading"
              : inspectedReusableVersion.isError
                ? "failed"
                : "ready"}
          selectedRecommendedVersion={inspectedRecommendation}
          selectedSlots={inspectedSlots}
          inspectingScopeOwner={inspectedIsScopeOwner}
          catalog={paletteCatalog}
          catalogByVersion={catalogByVersion}
          selectedSupportsScopedVariables={inspectedSupportsScopedVariables}
          propertyEditors={propertyEditors}
          expressionEditors={expressionEditors}
          expressionDescriptors={expressionDescriptors}
          expressionDescriptorStatus={expressionDescriptorStatus}
          descriptorStatus={descriptorStatus}
          onRetryExpressionDescriptors={() => { void reloadExpressionDescriptors(); }}
          scopedVariableAnalysis={scopedVariableAnalysis}
          onSelectedActivityChange={updateSelectedActivity}
          onChangeReusableVersion={openVersionChange}
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
      render: () => <WorkflowRuntimePanel testRun={renderedTestRun} publishedEquivalent={publishedEquivalent} onOpenRun={openWorkflowRun} />
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
          runInputEditors={runInputEditors}
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
          <button type="button" disabled={busy} onClick={() => void preparePublication()}><GitBranch size={15} /> Review &amp; publish</button>
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

      {versionChange ? (
        <React.Suspense fallback={<p role="status">Loading exact version review…</p>}>
          <ActivityVersionChangeDialog
            context={context}
            draft={draft}
            occurrence={versionChange.occurrence}
            current={versionChange.current}
            recommendation={versionChange.recommendation}
            onApply={applyVersionChange}
            onCancel={cancelVersionChange}
          />
        </React.Suspense>
      ) : null}

      {publicationReview ? (
        <PublicationReviewDialog
          review={publicationReview}
          busy={busy}
          onPublish={confirmPublication}
          onCancel={cancelPublication}
        />
      ) : null}

      {runInputPrompt ? (
        <WorkflowRunInputDialog
          inputs={runInputPrompt.inputs}
          editors={runInputEditors}
          onSubmit={values => { void confirmRunInputs(values); }}
          onCancel={cancelRunInputs}
        />
      ) : null}

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
          <ScopeBreadcrumb frames={frames} onNavigate={next => navigateToScope(next, null)} />
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
                onConnectStart={canCreateActivityFromPort ? onConnectStart : undefined}
                onConnectEnd={canCreateActivityFromPort ? onConnectEnd : undefined}
                onReconnect={isFlowchartDesigner || isBpmnDesigner ? onReconnect : undefined}
                isValidConnection={isValidConnection}
                onDragOver={onCanvasDragOver}
                onDragLeave={onCanvasDragLeave}
                onDrop={onCanvasDrop}
                onPaneClick={() => select(null)}
                onNodeClick={(_, node) => select(node.id)}
                onNodeDragStop={isUnsupportedDesigner ? undefined : commitLayout}
                minZoom={0.2}
                maxZoom={1.8}
                nodesConnectable={canCreateActivityFromPort}
                nodesDraggable={!isUnsupportedDesigner}
                selectionOnDrag
                multiSelectionKeyCode={["Shift", "Meta", "Control"]}
                deleteKeyCode={isUnsupportedDesigner ? null : ["Backspace", "Delete"]}
                panActivationKeyCode={null}
                ariaLabelConfig={workflowCanvasAriaLabelConfig}
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
                catalog={paletteCatalog}
                onPickActivity={pickActivityForEmptySlot}
                onBrowseAll={openEmptyConnectMenu}
              />
            ) : (isFlowchartDesigner || isBpmnDesigner) && nodes.length === 0 ? (
              <button type="button" className="wf-empty-canvas-add" onClick={() => openEmptyConnectMenu()}>
                <Plus size={15} /> Add activity
              </button>
            ) : null}
            {connectMenu ? (
              <ConnectMenu
                clientX={connectMenu.clientX}
                clientY={connectMenu.clientY}
                activities={paletteCatalog}
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

export function PublicationReviewDialog({ review, busy, onPublish, onCancel }: {
  review: PublicationReviewState;
  busy: boolean;
  onPublish(intent: PublicationIntent): Promise<void>;
  onCancel(): void;
}) {
  const dialogRef = useRef<HTMLElement>(null);
  const [action, setAction] = useState<"replace" | "sideBySide">(review.intent.action ?? "replace");
  const [slotName, setSlotName] = useState(review.intent.slotName ?? review.policy.defaultSlotName);
  const canEdit = review.phase === "review" || review.phase === "validationBlocked" || review.phase === "savedFailure" || review.phase === "partialFailure";
  const reservedSideBySideSlot = action === "sideBySide" && slotName.trim().toLowerCase() === "default";
  const slotIsValid = action !== "sideBySide" || Boolean(slotName.trim()) && !reservedSideBySideSlot;
  const intent = publicationIntentFor(review, action, slotName);
  const reviewedPreflight = publicationPreflightMatchesIntent(review.preflight, intent) ? review.preflight : undefined;
  const closeOnEscape = !busy && review.phase !== "publishing" ? onCancel : null;
  useDialogFocus(dialogRef, closeOnEscape);

  const targetSlotName = intent.slotName || (action === "replace" ? review.policy.defaultSlotName : "the named slot");
  const activeSlot = review.slots.find(slot => slot.slotName === targetSlotName);
  const currentTargetVersion = activeSlot?.publication?.artifactVersion ?? activeSlot?.publication?.versionId ?? "None";
  const targetDescription = activeSlot?.publication
    ? `Replace executable ${activeSlot.publication.artifactId} and source reference ${activeSlot.publication.sourceReferenceId} in occupied slot ${activeSlot.slotName}. Concurrency protection requires publication ${activeSlot.publication.publicationId} to remain current.`
    : `Create a new executable source reference in slot ${targetSlotName}.`;
  const changes = publicationChangesFor(review, activeSlot?.slotName ?? "");
  const preflightChanges = reviewedPreflight?.triggers ?? reviewedPreflight?.changes ?? [];
  const triggerSummary = reviewedPreflight
    ? preflightChanges.length
      ? preflightChanges.map(change => `${change.change} ${change.key} (${change.cardinality})`).join("; ")
      : "No trigger changes."
    : formatChangeCount(changes.triggers);
  const statusMessage = publicationStatusMessage(review);
  const validationStatus = publicationPreflightValidationStatus(review, reviewedPreflight, busy);

  return (
    <div className="wf-dialog-backdrop" role="presentation">
      <section
        ref={dialogRef}
        className="wf-dialog wf-publication-review"
        role="dialog"
        aria-modal="true"
        aria-labelledby="publication-review-title"
        aria-describedby="publication-review-status"
        tabIndex={-1}
      >
        <form onSubmit={event => {
          event.preventDefault();
          void onPublish(intent);
        }}>
          <header className="wf-dialog-heading">
            <div>
              <span>Workflow publication</span>
              <h3 id="publication-review-title">Review publication</h3>
            </div>
          </header>

          <output
            id="publication-review-status"
            className="wf-publication-status"
            data-phase={review.phase}
            aria-live="polite"
          >
            {statusMessage}
          </output>

          <dl className="wf-publication-facts">
            <div><dt>Current published version</dt><dd>{currentTargetVersion}</dd></div>
            <div><dt>Proposed next version</dt><dd>{review.proposedVersion} (confirmed after promotion)</dd></div>
            <div><dt>Unsaved changes</dt><dd>Included — the captured editor state is saved only after Publish.</dd></div>
            <div><dt>Validation</dt><dd>{validationStatus}</dd></div>
            <div><dt>Executable status</dt><dd>{review.executableStatus === "ready" ? "Executable after server preflight" : "Not executable until blocking validation is resolved"}</dd></div>
            <div><dt>Resolved action</dt><dd>{reviewedPreflight?.resolvedAction ?? "Requires authoritative review"}</dd></div>
            <div><dt>Target slot</dt><dd>{reviewedPreflight?.slotName ?? targetSlotName}</dd></div>
            <div><dt>Policy source</dt><dd>{reviewedPreflight ? `${reviewedPreflight.policySource}${reviewedPreflight.policyRevision == null ? "" : ` (revision ${reviewedPreflight.policyRevision})`}` : "Requires authoritative review"}</dd></div>
          </dl>

          {review.validationErrors.length ? (
            <div className="wf-publication-risks" role="alert">
              <strong>Publication blocked before mutation</strong>
              <ul>{review.validationErrors.map((message, index) => <li key={`${index}-${message}`}>{message}</li>)}</ul>
            </div>
          ) : null}

          <section className="wf-publication-changes" aria-labelledby="publication-changes-title">
            <h4 id="publication-changes-title">Changes in the captured draft</h4>
            <dl>
              <ChangeSummary label="Activities" value={changes.activities} />
              <ChangeSummary label="Inputs" value={changes.inputs} />
              <ChangeSummary label="Outputs" value={changes.outputs} />
              <div><dt>Triggers</dt><dd>{triggerSummary}</dd></div>
            </dl>
          </section>

          <section className="wf-publication-changes" aria-labelledby="publication-claims-title">
            <h4 id="publication-claims-title">Authoritative trigger claims</h4>
            {reviewedPreflight
              ? reviewedPreflight.claims.length
                ? <ul>{reviewedPreflight.claims.map(claim => <li key={`${claim.key}-${claim.cardinality}`}>{claim.key} ({claim.cardinality})</li>)}</ul>
                : <p>No trigger claims.</p>
              : <p>Review this target to resolve its claims and conflicts before publishing.</p>}
          </section>

          <fieldset className="wf-publication-behavior" disabled={!canEdit || busy}>
            <legend>Publication behavior</legend>
            <p>The {review.policy.source} policy defaults to {review.policy.defaultAction === "replace" ? "replacement" : "an explicit side-by-side slot"}.</p>
            <label><input type="radio" name="publication-action" checked={action === "replace"} onChange={() => setAction("replace")} /> Replace authority in this slot</label>
            <label><input type="radio" name="publication-action" checked={action === "sideBySide"} onChange={() => setAction("sideBySide")} /> Publish side by side</label>
          </fieldset>
          <label className="wf-form-field">
            <span>{action === "sideBySide" ? "Named slot" : "Slot"}</span>
            <input
              aria-label="Publication slot"
              value={slotName}
              onChange={event => setSlotName(event.target.value)}
              required={action === "sideBySide"}
              disabled={!canEdit || busy}
            />
            {action === "sideBySide" && !slotName.trim() ? <small role="alert">A meaningful slot name is required for side-by-side publication.</small> : null}
            {reservedSideBySideSlot ? <small role="alert">The default slot is reserved for replacement publication. Choose another named slot.</small> : null}
          </label>
          <p className="wf-dialog-note"><strong>Executable impact:</strong> {targetDescription}</p>

          {reviewedPreflight?.conflicts.length ? (
            <div className="wf-publication-risks" role="alert">
              <strong>Server policy conflicts</strong>
              <ul>{reviewedPreflight.conflicts.map(conflict => <li key={`${conflict.publicationId}-${conflict.key}`}>Conflict with slot {conflict.slotName}: {conflict.key}</li>)}</ul>
            </div>
          ) : null}
          {review.failureMessage ? <p className="wf-publication-recovery" role="alert">{review.failureMessage}</p> : null}
          {review.phase === "success" && review.published ? (
            <p className="wf-publication-success">Published executable {review.published.artifactId} with source reference {review.published.sourceReferenceId} in slot {review.published.slotName}.</p>
          ) : null}

          <div className="wf-dialog-actions">
            <button type="button" onClick={onCancel} disabled={busy}>{review.phase === "review" || review.phase === "validationBlocked" ? "Cancel" : "Close"}</button>
            {review.phase !== "success" ? (
              <button type="submit" disabled={busy || !slotIsValid || review.validationErrors.length > 0 || reviewedPreflight?.canActivate === false}>
                {!reviewedPreflight ? "Review target" : review.phase === "partialFailure" || review.phase === "savedFailure" ? "Retry Publish" : "Publish"}
              </button>
            ) : null}
          </div>
        </form>
      </section>
    </div>
  );
}

function ChangeSummary({ label, value }: { label: string; value: PublicationChangeCount }) {
  return <div><dt>{label}</dt><dd>{formatChangeCount(value)}</dd></div>;
}

function formatChangeCount(value: PublicationChangeCount) {
  return `${value.added} added, ${value.changed} changed, ${value.removed} removed`;
}

function publicationPreflightValidationStatus(
  review: PublicationReviewState,
  reviewedPreflight: PublicationReviewState["preflight"],
  busy: boolean
) {
  if (review.validationErrors.length) {
    return `${review.validationErrors.length} blocking issue${review.validationErrors.length === 1 ? "" : "s"}`;
  }
  if (reviewedPreflight) {
    return reviewedPreflight.canActivate
      ? "Server preflight completed — validation passed."
      : "Server preflight completed — activation conflicts found.";
  }
  if (busy) return "Server preflight in progress.";
  if (/stale/i.test(review.failureMessage ?? "")) return "Server preflight is stale — review the target again.";
  if (/preflight failed/i.test(review.failureMessage ?? "")) return "Server preflight failed — review the target again.";
  return "Server preflight required for this target.";
}

function publicationStatusMessage(review: PublicationReviewState) {
  if (review.phase === "success") return "Publication completed successfully.";
  if (review.phase === "validationBlocked") return "Publication is blocked by validation. No version has been promoted.";
  if (review.phase === "savedFailure") return "The reviewed draft was saved, but no version was promoted and nothing was published.";
  if (review.phase === "partialFailure") return `Publication did not complete. Promoted version ${review.promotedVersionId} remains available.`;
  if (review.phase === "publishing") {
    const messages = {
      saving: "Saving the captured workflow state...",
      promoting: "Promoting the saved workflow version...",
      preflight: "Reviewing server publication policy and trigger conflicts...",
      publishing: "Publishing the executable source reference..."
    };
    return review.progressStep ? messages[review.progressStep] : "Publishing...";
  }
  return "Review the target and captured changes. Nothing is saved, promoted, or published until you choose Publish.";
}
