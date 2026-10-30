import type { WorkflowEditorDocumentAdapter } from "./WorkflowEditorDocumentAdapter";
import { activityPaletteNotice, getActivityPaletteContext, isActivityOfferedInContext } from "../graph-authoring/activityPaletteContext";
import React, { useCallback, useEffect, useId, useMemo, useRef, useState } from "react";
import { Boxes, Code2, Download, ListTree, Package, PackageOpen, Play, Plus, SlidersHorizontal, Sparkles, Upload, Workflow as WorkflowIcon } from "lucide-react";
import { authSessionEndedEvent, authSessionStartedEvent, expressionEditorSessionEndedEvent, useStudioThemeLayout, type StudioActivityPropertyEditorContribution, type StudioAiContributionApi, type StudioEndpointContext, type StudioExpressionEditorContribution, type StudioExpressionToolingClient, type StudioWorkflowDesignerPanelContribution, type StudioWorkflowRunInputEditorContribution, type StudioTestStimulusEditorContribution } from "@elsa-workflows/studio-sdk";
import type { ActivityCatalogItem, ActivityNode, WorkflowDraft } from "../workflowTypes";
import {
  collectActivityNodeIds,
  createActivityNode,
  readFlowchartStartNodeId,
  setFlowchartStartNode,
  findActivityNode,
  findNodeScopePath,
  getActivityDisplay,
  getChildSlots,
  normalizeActivityStructures,
  planSlotNavigation,
  replaceSlotActivities,
  slotCrumbLabel,
  updateActivity,
  type ChildSlot
} from "../workflowAdapter";
import {
  indexActivityPresentation,
  resolveActivityLabel,
  updateActivityPresentation
} from "../activityPresentation";
import { buildDraftFromJson } from "../workflowSerialization";
import { WorkflowCodeView } from "../WorkflowCodeView";
import { withWorkflowStartEnabled } from "../workflowStartEditing";
import { getWorkflowStartEnabled } from "../workflowStart";
import { WorkflowPropertiesView } from "../WorkflowPropertiesView";
import { loadActivityIconRegistry } from "../activityIconView";
import { weaverUnavailableTitle } from "./constants";

const AttachedRunActivityInspector = React.lazy(async () => ({ default: (await import("./AttachedRunActivityInspector")).AttachedRunActivityInspector }));

void loadActivityIconRegistry().catch(() => undefined);
import type { CanvasView, WorkflowDesignerPanelContext, WorkflowEditorError, WorkflowEditorOperation, WorkflowEditorPanelTab, WorkflowErrorInput, WorkflowTestDriveSetupState } from "./editorTypes";
import type { WorkflowNodeActions, WorkflowSlotNavigation } from "./contexts";
import {
  createNodeId,
  dispatchAiAction,
  describeWorkflowError,
  findAiAction,
  getDraftSignature,
  groupActivityPalette,
  normalizeWorkflowError
} from "./editorHelpers";
import {
  hasNonCollapsedTextSelection,
  isActivityClipboardShortcutBlocked,
  isWorkflowEditorKeyboardTarget
} from "./activityClipboardShortcuts";
import { ActivityCanvasContextMenu } from "./ActivityCanvasContextMenu";
import { matchActivityClipboardShortcut } from "./duplicateActivities";
import { useActivityClipboard } from "./useActivityClipboard";
import { WorkflowAlert } from "./WorkflowAlert";
import { WorkflowEditorToolbar } from "./WorkflowEditorToolbar";
import { ConnectMenu } from "./graph";
import { PanelTabList, compareWorkflowPanelTabs } from "./PanelTabList";
import { ScopeBreadcrumb } from "./ScopeBreadcrumb";
import { ValidationPanel, WorkflowRuntimePanel } from "./editorPanels";
import { AttachedTestRunStatus } from "./AttachedTestRunStatus";
import { useTestRunObservation } from "./useTestRunObservation";
import { useTestRunRestoration } from "./useTestRunRestoration";
import { usePinnedEditorDocument } from "./usePinnedEditorDocument";
import { useEditorRunCanvas } from "./useEditorRunCanvas";
import { useAttachedExecutionSelection } from "./useAttachedExecutionSelection";
import { activateWorkflowCanvasNode } from "./workflowAccessibility";
import { useDraftValidations } from "./useDraftValidations";
import { useExecutableArtifactExport } from "./useExecutableArtifactExport";
import { combineValidationErrors, detectStructuralValidationErrors } from "../draftValidation";
import { createValidationActivityLabels, humanizeValidationErrors } from "../validationActivityLabels";
import { WorkflowArtifactsPanel } from "./WorkflowExecutables";
import { LazyWorkflowTestDrivePanel } from "./lazyTestDriveUi";
import { useSidePanelLayout } from "./useSidePanelLayout";
import { useDraftHistory } from "./useDraftHistory";
import { forgetRemovedNodes, useWorkflowDocument } from "./workflowDocument";
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
import { SequenceDesigner } from "../graph-authoring/SequenceDesigner";
import { CompensationScopeDesigner, type CompensationActivityTarget } from "../graph-authoring/CompensationScopeDesigner";
import {
  addCompensationStep,
  compensationStepsSlotId,
  compensationValidationTarget,
  readCompensationSteps,
  updateCompensationStep,
  withCompensationSteps,
  type CompensationStep
} from "../compensationScope";
import { GraphAuthoringCanvas } from "../graph-authoring/GraphAuthoringCanvas";
import { GraphAuthoringWorkbench } from "../graph-authoring/GraphAuthoringWorkbench";
import { AddStepButton, authorsInline } from "../graph-authoring/AddStepButton";
import { filterGraphAuthoringContributions } from "../graph-authoring/graphAuthoringContributions";
import {
  InspectorPanel,
  resolveActivityInspectorTabId,
  type ActivityInspectorTabId
} from "./InspectorPanel";
import { BpmnElementInspector } from "../bpmn/BpmnElementInspector";
import { bpmnPaletteElements } from "../bpmn/bpmnPaletteElements";
import { emptyGraphElements } from "../graph-authoring/graphPaletteElements";
import { collectOwnedNodeIds, findBpmnElement, readBpmnSequenceFlows, updateBpmnDefaultFlow, updateBpmnElement, updateBpmnFlow } from "../bpmn/bpmnAdapter";
import { bpmnElementTypeLabel, bpmnStructureKind, type BpmnElement, type BpmnSequenceFlow } from "../bpmn/bpmnTypes";
import { exportBpmnDocument, importBpmnDocument, layoutFromBpmnDiagram, summarizeBpmnImportIssues, withDiagramFromLayout } from "../api/bpmnInterchange";
import { SlotEmptyState } from "./SlotEmptyState";
import { PublicationReviewDialog } from "./PublicationReviewDialog";
import { useAiProviderAvailability } from "./useAiProviderAvailability";
import { useFullActivityDefinitionVersion } from "../api/activityDesign";
import type { ActivityDefinitionVersionView, RecommendedActivityDefinition } from "../activityDefinitionTypes";
import { isHttpEndpointType } from "./httpTestRequest";
import type { ActivityVersionChangeApplyRequest } from "./ActivityVersionChangeDialog";
import {
  applyActivityVersionChange,
  findActivityOccurrence,
  validateActivityVersionChangePrecondition
} from "./activityVersionChangeModel";
import { createLazyExpressionToolingClient } from "../expression-tooling/lazyExpressionToolingClient";
import type { ExpressionToolingCacheIdentity } from "../expression-tooling/expressionToolingClient";

const SaveWorkflowTemplateDialog = React.lazy(() => import("./SaveWorkflowTemplateDialog").then(module => ({ default: module.SaveWorkflowTemplateDialog })));

const ActivityVersionChangeDialog = React.lazy(() =>
  import("./ActivityVersionChangeDialog").then(module => ({ default: module.ActivityVersionChangeDialog })));
const HttpTestRequestDialog = React.lazy(() =>
  import("./HttpTestRequestDialog").then(module => ({ default: module.HttpTestRequestDialog })));

export { PublicationReviewDialog } from "./PublicationReviewDialog";

export function WorkflowEditor({
  context,
  definitionId,
  documentAdapter,
  ai,
  propertyEditors,
  expressionEditors,
  expressionTooling,
  expressionToolingIdentity,
  runInputEditors,
  testStimulusEditors,
  workflowDesignerPanels,
  autosaveEnabledByDefault,
  onBack
}: {
  context: StudioEndpointContext;
  definitionId: string;
  documentAdapter?: WorkflowEditorDocumentAdapter;
  ai: StudioAiContributionApi;
  propertyEditors: StudioActivityPropertyEditorContribution[];
  expressionEditors: StudioExpressionEditorContribution[];
  expressionTooling?: StudioExpressionToolingClient;
  expressionToolingIdentity?: ExpressionToolingCacheIdentity;
  runInputEditors: StudioWorkflowRunInputEditorContribution[];
  testStimulusEditors: StudioTestStimulusEditorContribution[];
  workflowDesignerPanels: StudioWorkflowDesignerPanelContribution[];
  autosaveEnabledByDefault?: boolean;
  onBack(): void;
}) {
  const [saveTemplateOpen, setSaveTemplateOpen] = useState(false);
  const [templateNavigationPending, setTemplateNavigationPending] = useState(false);
  const [templatesAvailable, setTemplatesAvailable] = useState(false);
  useEffect(() => {
    let active = true;
    void import("../workflowTemplates").then(module => module.workflowTemplatesPath(context)).then(path => { if (active) setTemplatesAvailable(!!path); }).catch(() => { if (active) setTemplatesAvailable(false); });
    return () => { active = false; };
  }, [context]);
  const expressionEditorSessionScope = useId();
  const ownedExpressionTooling = useMemo(
    () => expressionToolingIdentity
      ? createLazyExpressionToolingClient(context, expressionToolingIdentity, expressionEditorSessionScope)
      : undefined,
    [context, expressionEditorSessionScope, expressionToolingIdentity]
  );
  const resolvedExpressionTooling = expressionTooling ?? ownedExpressionTooling;
  useEffect(() => {
    if (!resolvedExpressionTooling) return;
    const revoke = () => resolvedExpressionTooling.revokeAuthorization?.();
    const restore = () => resolvedExpressionTooling.restoreAuthorization?.();
    window.addEventListener(authSessionEndedEvent, revoke);
    window.addEventListener(authSessionStartedEvent, restore);
    return () => {
      window.removeEventListener(authSessionEndedEvent, revoke);
      window.removeEventListener(authSessionStartedEvent, restore);
    };
  }, [resolvedExpressionTooling]);
  useEffect(() => () => ownedExpressionTooling?.dispose(), [ownedExpressionTooling]);
  useEffect(() => () => {
    window.dispatchEvent(new CustomEvent(expressionEditorSessionEndedEvent, {
      detail: { scope: expressionEditorSessionScope }
    }));
  }, [expressionEditorSessionScope]);

  // The interdependent draft/scope/selection/test-run/artifact cluster now lives in an explicit reducer,
  // so each mutation (below) declares the transition it makes instead of cascading loose setState calls.
  const editableDoc = useWorkflowDocument();
  const { draft: editableDraft, testRun, publishedArtifactId } = editableDoc.state;
  const observation = useTestRunObservation({ context, testRun, onRunViewUpdated: editableDoc.updateTestRunView });
  const restoration = useTestRunRestoration({ context, definitionId, draft: editableDraft, testRun, onRestore: editableDoc.startTestRun });
  const [error, setErrorState] = useState<WorkflowEditorError | null>(null);
  const setError = useCallback((value: WorkflowErrorInput) => setErrorState(normalizeWorkflowError(value)), []);
  const [status, setStatus] = useState("");
  const [operation, setOperation] = useState<WorkflowEditorOperation>("idle");
  const [expandedPaletteCategories, setExpandedPaletteCategories] = useState<Set<string>>(() => new Set());
  const [paletteSearch, setPaletteSearch] = useState("");
  const [activeLeftPanelId, setActiveLeftPanelId] = useState("activities");
  const themeLayout = useStudioThemeLayout();
  const [activeRightPanelId, setActiveRightPanelId] = useState("inspector");
  const [testDriveSetup, setTestDriveSetup] = useState<WorkflowTestDriveSetupState | null>(null);
  const [inspectorView, setInspectorView] = useState<"configuration" | "run-data">("configuration");
  const [activeInspectorTabId, setActiveInspectorTabId] = useState<ActivityInspectorTabId>("inputs");
  const [canvasView, setCanvasView] = useState<CanvasView>("designer");
  const [httpTestRequestActivityId, setHttpTestRequestActivityId] = useState<string | null>(null);
  const [versionChange, setVersionChange] = useState<{
    occurrence: ActivityNode;
    current: ActivityDefinitionVersionView;
    recommendation?: RecommendedActivityDefinition | null;
  } | null>(null);

  const { resetHistory, undo, redo, canUndoNow, canRedoNow } = useDraftHistory({ draft: editableDraft, restoreDraft: editableDoc.loadDraft });

  // Draft persistence (serialised save queue + debounced autosave) and the definition data load both live
  // in dedicated hooks; `markSaved`/`reload` let the loader seed the save baseline and reload after promote.
  const { saveDraft, flushPendingSave, saving, autosaveEnabled, setAutosaveEnabled, setAutosavePaused, markSaved } = useWorkflowPersistence({ context, documentAdapter, draft: editableDraft, autosaveEnabledByDefault, editDraft: editableDoc.editDraft, setStatus, setError });
  const {
    details,
    setDetails,
    catalog,
    paletteCatalog: availablePaletteCatalog,
    recommendedDefinitions,
    activityDescriptors,
    availabilityDiagnostics,
    expressionDescriptors,
    expressionDescriptorStatus,
    descriptorStatus,
    reload,
    reloadExpressionDescriptors
  } = useWorkflowEditorData({ context, definitionId, documentAdapter, resetHistory, loadDraft: editableDoc.loadDraft, markSaved, setError });
  const retryLoad = useCallback(() => {
    void reload().catch(e => setError(describeWorkflowError(e, "Could not load the workflow editor.")));
  }, [reload, setError]);
  const editorDoc = usePinnedEditorDocument({ document: editableDoc, context, definitionId, catalog });
  const { draft, frames, selectedNodeId } = editorDoc.state;
  const { loadDraft, replaceDraftByBatch, editDraft, editDraftAndSelect, select, navigateToScope, resetToRoot, startTestRun, setPublishedArtifact } = editorDoc;
  // Debounced metadata saves still belong to the editable definition.
  const { updateDefinitionMeta: updateEditableDefinitionMeta, flushDefinitionMeta } = useDefinitionMetadata({ context, documentAdapter, details, setDetails, setStatus });
  const updateDefinitionMeta: typeof updateEditableDefinitionMeta = (...args) => { if (editorDoc.canEdit()) updateEditableDefinitionMeta(...args); };

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
    canAddActivitiesToCanvas: scopeAllowsAuthoring
  } = useWorkflowScope({ context, draft, frames, selectedNodeId, catalog, activityDescriptors, availabilityDiagnostics });
  const canAddActivitiesToCanvas = scopeAllowsAuthoring && !editorDoc.pinned;
  const elements = isBpmnDesigner ? bpmnPaletteElements : emptyGraphElements;
  const inlineSteps = authorsInline(themeLayout, isBpmnDesigner);
  const availableDesignerPanels = filterGraphAuthoringContributions(workflowDesignerPanels, "workflow-definition");
  const sidePanelLayout = useSidePanelLayout(undefined, {
    palettePresent: !inlineSteps || availableDesignerPanels.some(panel => panel.side === "left")
  });
  const { setInspectorCollapsed } = sidePanelLayout;
  useEffect(() => {
    setActiveInspectorTabId(current =>
      resolveActivityInspectorTabId(current, inspectedSupportsScopedVariables, inspectedSlots.length > 0));
  }, [inspectedSlots.length, inspectedSupportsScopedVariables]);
  const inspectedCatalogItem = inspectedNode ? catalogByVersion.get(inspectedNode.activityVersionId) : null;
  const inspectedIsHttpEndpoint = !!inspectedNode && isHttpEndpointType(
    inspectedDescriptor?.typeName ?? inspectedCatalogItem?.activityTypeKey);
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

  const paletteContext = useMemo(() => getActivityPaletteContext(
    "workflow-definition", draft?.state.rootActivity, scope?.owner.nodeId, scope?.slot, catalogByVersion
  ), [draft?.state.rootActivity, scope, catalogByVersion]);
  const isOfferedActivity = useCallback((activity: ActivityCatalogItem) => isActivityOfferedInContext(activity, paletteContext), [paletteContext]);
  const paletteCatalog = useMemo(() => availablePaletteCatalog.filter(isOfferedActivity), [availablePaletteCatalog, isOfferedActivity]);
  const choicesForSlot = (owner: ActivityNode, slot: ChildSlot) => availablePaletteCatalog.filter(activity => isActivityOfferedInContext(activity,
    getActivityPaletteContext("workflow-definition", draft?.state.rootActivity, owner.nodeId, slot, catalogByVersion)));
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
  const busy = operation !== "idle" || editorDoc.pinned;
  const canRunTest = !!draft?.state.rootActivity && !busy;

  // Draft validation surface (#453): fetch server-derived validations after the draft settles (when the
  // backend advertises the relation), and combine them with the draft's reconciled errors and
  // designer-side structural checks so the editor panel agrees with the promotion gate.
  const draftValidations = useDraftValidations({ context, draft, enabled: !documentAdapter });

  // Executable-artifact export (#493, foundation #1304): available only when the workflow has a
  // published version and the server advertises the export relation, so a runtime-less or older server
  // simply renders no button.
  const executableArtifactExport = useExecutableArtifactExport({ context, definitionId, publishedArtifactId });
  const publishedExecutable = executableArtifactExport.target;
  // Live HTTP requests need only the published runtime artifact; they do not depend on export support.
  const publishedRuntimeExecutable = executableArtifactExport.publishedTarget;
  const activityLabels = useMemo(
    () => createValidationActivityLabels({
      root: draft?.state.rootActivity,
      presentation: draft?.activityPresentation,
      catalogByVersion
    }),
    [catalogByVersion, draft?.activityPresentation, draft?.state.rootActivity]);
  const structuralValidationErrors = useMemo(
    () => detectStructuralValidationErrors(draft?.state, activityLabels),
    [activityLabels, draft?.state]);
  const combinedValidationErrors = useMemo(
    () => humanizeValidationErrors(combineValidationErrors({
      draftErrors: draft?.validationErrors,
      serverErrors: draftValidations.available ? draftValidations.errors : null,
      structuralErrors: structuralValidationErrors
    }), activityLabels),
    [activityLabels, draft?.validationErrors, draftValidations.available, draftValidations.errors, structuralValidationErrors]);
  const findRisksAction = findAiAction(ai, "weaver.workflows.find-draft-risks");
  const proposeUpdateAction = findAiAction(ai, "weaver.workflows.propose-update");
  const aiProviderAvailable = useAiProviderAvailability(ai);

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
    isBpmnDesigner,
    isOfferedActivity,
    elements,
    // A compensation scope's steps change only through its step list, never by a palette drop on the canvas.
    canAddActivitiesToCanvas: canAddActivitiesToCanvas && scope?.slot.id !== compensationStepsSlotId,
    readOnly: editorDoc.pinned,
    selectedNodeId,
    editDraft,
    editDraftAndSelect,
    select,
    resetToRoot,
    setStatus,
    setError
  });
  const {
    nodes: authoringNodes,
    edges: authoringEdges,
    canvasRef,
    setReactFlowInstance,
    queueCanvasNodeFocus,
    onMoveEnd,
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
    openAddStepMenu,
    openSequenceInsertMenu,
    insertSequenceActivity,
    moveSequenceActivity,
    onConnectMenuPick,
    addActivity,
    onElementClick,
    onElementDragStart,
    onElementDragEnd,
    onElementPointerDown,
    onPaletteClick,
    onPaletteDragStart,
    onPaletteDragEnd,
    onPalettePointerDown
  } = canvas;
  const runSelection = useAttachedExecutionSelection({ context, attachmentId: JSON.stringify([testRun?.view.testRunId, testRun?.view.artifactId, testRun?.draftSignature]),
    workflowExecutionId: testRun?.view.workflowExecutionId ?? "", nodeId: inspectedNode?.nodeId ?? null,
    enabled: Boolean(testRun?.snapshot && testRun.endpointContext === context && draft && testRun.draftSignature === getDraftSignature(draft)
      && testRun.view.definitionId === definitionId && testRun.snapshot.definitionId === definitionId) });
  const runScope = [...runSelection.scopes].reverse().find(item => item.execution.authoredActivityId === scopeOwner?.nodeId)?.execution;
  const runCanvas: ReturnType<typeof useEditorRunCanvas> = useEditorRunCanvas({ context, definitionId, testRun, observation, snapshotView: editorDoc.pinned, draftSignature: editableDraft ? getDraftSignature(editableDraft) : "", catalog, nodes: authoringNodes, edges: authoringEdges, mode: scope?.slot.mode, selectedNodeId,
    ancestorId: runScope?.activityExecutionId, selectedExecution: runSelection.execution,
    onOpenIncident: (incidentId, targetNodeId) => {
      const incident = runCanvas.details?.incidents?.find(item => item.incidentId === incidentId);
      const execution = runCanvas.activities.find(item => item.activityExecutionId === incident?.activityExecutionId);
      const nodeId = targetNodeId ?? (execution ? authoringNodes.find(node => (node.data.runtimeNodeId ?? node.id) === execution.authoredActivityId)?.id : null);
      if (execution && nodeId && runCanvas.graph) {
        runSelection.queue(execution, runCanvas.graph, value => {
          const item = catalog.find(candidate => candidate.activityTypeKey === value.activityType);
          return item ? getActivityDisplay(item) : value.activityType;
        }, execution.authoredActivityId);
        select(nodeId);
      } else {
        select(nodeId ?? null);
        runSelection.unavailable();
      }
      setInspectorView("run-data"); setActiveRightPanelId("inspector"); setInspectorCollapsed(false);
    } });
  const nodes = useMemo(() => {
    const activities = new Map((scope?.slot.activities ?? (scopeOwner ? [scopeOwner] : [])).map(activity => [activity.nodeId, activity]));
    return runCanvas.nodes.map(node => {
      const activity = activities.get(node.id);
      return activity ? { ...node, data: { ...node.data,
        workflowStartEnabled: getWorkflowStartEnabled(activity, catalogByVersion.get(activity.activityVersionId))
      } } : node;
    });
  }, [catalogByVersion, runCanvas.nodes, scope?.slot.activities, scopeOwner]);
  const edges = runCanvas.edges;
  const selectEditorNode = (nodeId: string | null) => {
    select(nodeId);
    if (runCanvas.attached) { setInspectorView("run-data"); setActiveRightPanelId("inspector"); setInspectorCollapsed(false); }
  };
  const selectedCanvasNodeIds = useMemo(() => {
    const selected = nodes.filter(node => node.selected).map(node => node.id);
    return selected.length > 0 ? selected : (selectedNodeId ? [selectedNodeId] : []);
  }, [nodes, selectedNodeId]);
  const {
    copy: copySelectedActivities,
    paste: pasteCopiedActivities,
    duplicate: duplicateSelectedActivities,
    contextMenu: activityContextMenu,
    openContextMenu: openActivityContextMenu,
    closeContextMenu: closeActivityContextMenu
  } = useActivityClipboard({
    draft,
    frames,
    catalogByVersion,
    selectedNodeIds: selectedCanvasNodeIds,
    // The compensation scope's Steps view is not a slot to paste into: an activity belongs to a step, so it is added
    // through the scope designer.
    disabled: isUnsupportedDesigner || !canAddActivitiesToCanvas || scope?.slot.id === compensationStepsSlotId,
    definitionId,
    editDraft,
    select: nodeId => {
      select(nodeId);
      if (nodeId) queueCanvasNodeFocus(nodeId);
    },
    setStatus
  });

  const deleteCanvasNode = useCallback((nodeId: string) => {
    const node = nodes.find(candidate => candidate.id === nodeId);
    if (!node) return;
    onNodesDelete([node]);
    if (nodes.length === 1) canvasRef.current?.focus({ preventScroll: true });
  }, [nodes, onNodesDelete, canvasRef]);

  const setWorkflowStart = useCallback((nodeId: string, enabled: boolean) => {
    if (!canAddActivitiesToCanvas) return;
    editDraft(({ draft: current }) => current?.state.rootActivity ? {
      ...current,
      state: { ...current.state, rootActivity: updateActivity(current.state.rootActivity, nodeId,
        activity => withWorkflowStartEnabled(activity, catalogByVersion.get(activity.activityVersionId), enabled), catalogByVersion) }
    } : null);
  }, [canAddActivitiesToCanvas, catalogByVersion, editDraft]);

  const nodeActions = useMemo<WorkflowNodeActions | null>(() => {
    if (isUnsupportedDesigner || isBpmnDesigner || !canAddActivitiesToCanvas) return null;
    return {
      selectedNodeCount: selectedCanvasNodeIds.length,
      canAddAfter: canCreateActivityFromPort,
      addAfter: (nodeId, portName, anchor) => {
        if (selectedCanvasNodeIds.length !== 1 || selectedCanvasNodeIds[0] !== nodeId) return;
        openAddStepMenu(anchor, { nodeId, handleId: portName });
      },
      duplicate: nodeId => { duplicateSelectedActivities([nodeId]); },
      delete: deleteCanvasNode,
      setWorkflowStart
    };
  }, [
    canAddActivitiesToCanvas,
    canCreateActivityFromPort,
    deleteCanvasNode,
    duplicateSelectedActivities,
    isBpmnDesigner,
    isUnsupportedDesigner,
    openAddStepMenu,
    selectedCanvasNodeIds,
    setWorkflowStart
  ]);

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
  useWorkflowGraphOperationBatch({ draft: editableDraft, details, catalog, replaceDraftByBatch, canEdit: editorDoc.canEdit, setStatus, setError });

  useEffect(() => {
    if (!draft?.state.rootActivity || catalog.length === 0) return;

    editDraft(({ draft: current }) => {
      if (!current?.state.rootActivity) return null;
      const normalizedRoot = normalizeActivityStructures(current.state.rootActivity, catalogByVersion);
      if (!normalizedRoot || normalizedRoot === current.state.rootActivity) return null;
      const previousNodeIds = collectActivityNodeIds(current.state.rootActivity, catalogByVersion);
      const normalizedNodeIds = collectActivityNodeIds(normalizedRoot, catalogByVersion);
      const removedNodeIds = new Set(
        [...previousNodeIds].filter(nodeId => !normalizedNodeIds.has(nodeId)));

      return {
        ...current,
        ...forgetRemovedNodes(current, removedNodeIds),
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
    exportExecutableArtifact,
    save,
    preparePublication,
    publicationReview,
    reviewPublication,
    confirmPublication,
    cancelPublication,
    openPublishedExecutable,
    expressionValidationWarning,
    confirmUnavailableExpressionValidation,
    cancelUnavailableExpressionValidation,
    run,
    runTestDrive,
    updateTestDriveResult
  } = useWorkflowOperations({
    context,
    draft: editableDraft,
    details,
    catalog,
    busy,
    publishedExecutable,
    saveDraft,
    flushPendingSave,
    reload,
    startTestRun,
    updateTestRunResult: editorDoc.updateTestRunResult,
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

  const editorRootRef = useRef<HTMLElement | null>(null);

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (canvasView !== "designer" || editorDoc.pinned) return;
      if (!(event.metaKey || event.ctrlKey)) return;
      const clipboardShortcut = matchActivityClipboardShortcut(event);
      if (clipboardShortcut === "copy") {
        if (isActivityClipboardShortcutBlocked(event, editorRootRef.current)) return;
        if (hasNonCollapsedTextSelection()) return;
        if (copySelectedActivities()) event.preventDefault();
        return;
      }
      if (clipboardShortcut === "paste") {
        if (isActivityClipboardShortcutBlocked(event, editorRootRef.current)) return;
        if (pasteCopiedActivities()) event.preventDefault();
        return;
      }
      if (clipboardShortcut === "duplicate") {
        if (isActivityClipboardShortcutBlocked(event, editorRootRef.current)) return;
        if (duplicateSelectedActivities()) event.preventDefault();
        return;
      }
      if (isWorkflowEditorKeyboardTarget(event)) return;
      const key = event.key.toLowerCase();
      if (key === "z" && !event.shiftKey) {
        event.preventDefault();
        undo();
      } else if ((key === "z" && event.shiftKey) || key === "y") {
        event.preventDefault();
        redo();
      }
    };

    window.addEventListener("keydown", handleKeyDown, true);
    return () => window.removeEventListener("keydown", handleKeyDown, true);
  }, [canvasView, copySelectedActivities, duplicateSelectedActivities, editorDoc.pinned, pasteCopiedActivities, undo, redo]);

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
        ...forgetRemovedNodes(current, slot.activities.reduce(
          (nodeIds, oldActivity) => collectOwnedNodeIds(oldActivity, catalogByVersion, nodeIds),
          new Set<string>())),
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
  }, [catalogByVersion, editDraft, frames, navigateToScope, scopeOwner, setError]);

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

  const updateSelectedPresentation = useCallback((
    presentation: { displayName?: string | null; description?: string | null }
  ) => {
    const nodeId = inspectedNode?.nodeId;
    if (!nodeId) return;
    editDraft(({ draft: current }) => current
      ? {
          ...current,
          activityPresentation: updateActivityPresentation(
            current.activityPresentation ?? [],
            nodeId,
            presentation)
        }
      : null);
  }, [editDraft, inspectedNode?.nodeId]);

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

  // A compensation scope's designer edits the owner's steps directly, as the BPMN canvas edits its payload.
  const isCompensationScopeCanvas = scope?.slot.id === compensationStepsSlotId;
  const [compensationPick, setCompensationPick] = useState<{ target: CompensationActivityTarget; clientX: number; clientY: number } | null>(null);
  const updateCompensationSteps = useCallback((edit: (steps: CompensationStep[]) => CompensationStep[], nextStatus: string, selectNodeId?: string) => {
    const ownerNodeId = scopeOwner?.nodeId;
    if (!ownerNodeId) return;
    const recipe = ({ draft: current }: { draft: WorkflowDraft | null }) => {
      const rootActivity = current?.state.rootActivity;
      if (!current || !rootActivity) return null;
      // The edit runs against the scope as it is in this draft, not as it was when the designer rendered.
      const owner = findActivityNode(rootActivity, ownerNodeId, catalogByVersion);
      if (!owner) return null;
      const nextOwner = withCompensationSteps(owner, edit(readCompensationSteps(owner)));
      // Deleting a step, removing a compensation or replacing an activity drops nodes: forget their layout and
      // presentation records, as replacing a slot activity does.
      const kept = collectOwnedNodeIds(nextOwner, catalogByVersion, new Set<string>());
      const removed = new Set([...collectOwnedNodeIds(owner, catalogByVersion, new Set<string>())].filter(nodeId => !kept.has(nodeId)));
      return {
        ...current,
        ...forgetRemovedNodes(current, removed),
        state: {
          ...current.state,
          rootActivity: updateActivity(rootActivity, ownerNodeId, () => nextOwner, catalogByVersion)
        }
      };
    };
    if (selectNodeId) editDraftAndSelect(recipe, selectNodeId);
    else editDraft(recipe);
    setStatus(nextStatus);
  }, [catalogByVersion, editDraft, editDraftAndSelect, scopeOwner?.nodeId]);
  const pickCompensationActivity = useCallback((activity: ActivityCatalogItem) => {
    const pick = compensationPick;
    setCompensationPick(null);
    if (!pick || !scopeOwner) return;
    const node = createActivityNode(activity, createNodeId(activity));
    const label = getActivityDisplay(activity);
    const { target } = pick;
    if (target.kind === "new-step") {
      updateCompensationSteps(steps => addCompensationStep(steps, target.index, node), `Added step ${label}`, node.nodeId);
    } else if (target.kind === "step-activity") {
      updateCompensationSteps(steps => updateCompensationStep(steps, target.index, { activity: node }), `Set ${label} as the step's activity`, node.nodeId);
    } else {
      const stepName = readCompensationSteps(scopeOwner)[target.index]?.name || `step ${target.index + 1}`;
      updateCompensationSteps(steps => updateCompensationStep(steps, target.index, { compensation: node }), `${label} now undoes ${stepName}`, node.nodeId);
    }
  }, [compensationPick, scopeOwner, updateCompensationSteps]);
  // On the compensation scope's canvas, a palette click adds a new last step with that activity.
  const addCompensationStepFromPalette = useCallback((activity: ActivityCatalogItem) => {
    if (!canAddActivitiesToCanvas || isUnsupportedDesigner) return;
    const node = createActivityNode(activity, createNodeId(activity));
    setCompensationPick(null);
    updateCompensationSteps(steps => addCompensationStep(steps, steps.length, node), `Added step ${getActivityDisplay(activity)}`, node.nodeId);
  }, [canAddActivitiesToCanvas, isUnsupportedDesigner, updateCompensationSteps]);

  // A start node is a Flowchart concept; a Sequence runs in authored order and has none.
  const isFlowchartScope = scope?.slot.mode === "flowchart";
  // The Flowchart start node is authored, not positional: this is the only writer besides the
  // normalizer's auto-assignment, so an operator can move the start (e.g. onto a trigger added later)
  // instead of being told at publish time that a node is unreachable.
  const flowchartStartNodeId = isFlowchartScope ? readFlowchartStartNodeId(scopeOwner) : null;
  const setStartNode = useCallback((nodeId: string) => {
    const ownerNodeId = scopeOwner?.nodeId;
    if (!ownerNodeId || !isFlowchartScope) return;
    editDraft(({ draft: current }) => {
      const rootActivity = current?.state.rootActivity;
      if (!current || !rootActivity) return null;
      return {
        ...current,
        state: {
          ...current.state,
          rootActivity: updateActivity(
            rootActivity,
            ownerNodeId,
            owner => setFlowchartStartNode(owner, nodeId),
            catalogByVersion)
        }
      };
    });
    setStatus("Start node updated.");
  }, [catalogByVersion, editDraft, isFlowchartScope, scopeOwner?.nodeId, setStatus]);

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

  // BPMN XML interchange (root scope of a BPMN workflow only): export downloads the root with the
  // canvas layout stamped into BPMNDI; import replaces the root's structure with the converted
  // document (keeping the root's node/version identity) and positions the canvas from its DI.
  const isBpmnRoot = draft?.state.rootActivity?.structure?.kind === bpmnStructureKind;
  const canUseBpmnInterchange = isBpmnRoot && frames.length === 0;
  const bpmnFileInputRef = useRef<HTMLInputElement | null>(null);

  const exportBpmn = useCallback(async () => {
    const rootActivity = draft?.state.rootActivity;
    if (!rootActivity) return;
    try {
      const xml = await exportBpmnDocument(context, withDiagramFromLayout(rootActivity, draft.layout));
      const blob = new Blob([xml], { type: "application/xml" });
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement("a");
      anchor.href = url;
      anchor.download = `${details?.definition?.name || "workflow"}.bpmn`;
      anchor.click();
      URL.revokeObjectURL(url);
      setError("");
      setStatus("Exported BPMN 2.0 XML.");
    } catch {
      setStatus("");
      setError("BPMN export failed.");
    }
  }, [context, details?.definition?.name, draft, setError, setStatus]);

  const importBpmn = useCallback(async (file: File) => {
    const rootActivity = draft?.state.rootActivity;
    if (!rootActivity) return;
    try {
      const xml = await file.text();
      const result = await importBpmnDocument(context, xml);
      const importedLayout = layoutFromBpmnDiagram(result.processNode);
      editDraftAndSelect(({ draft: current }) => {
        const currentRoot = current?.state.rootActivity;
        if (!current || !currentRoot) return null;
        return {
          ...current,
          layout: importedLayout,
          state: {
            ...current.state,
            rootActivity: {
              ...result.processNode,
              nodeId: currentRoot.nodeId,
              activityVersionId: currentRoot.activityVersionId
            }
          }
        };
      }, null);
      const issueSummary = summarizeBpmnImportIssues(result.analysis);
      setError("");
      setStatus(issueSummary ? `Imported BPMN document (${issueSummary}).` : "Imported BPMN document.");
    } catch {
      setStatus("");
      setError("BPMN import failed — check that the file is a valid BPMN 2.0 document.");
    }
  }, [context, draft, editDraftAndSelect, setError, setStatus]);

  const openVersionChange = useCallback((occurrence: ActivityNode, current: ActivityDefinitionVersionView) => {
    setError("");
    setAutosavePaused(true);
    setVersionChange({ occurrence, current, recommendation: inspectedRecommendation });
  }, [inspectedRecommendation, setAutosavePaused, setError]);

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

  // "Go to" for a validation error: a compensation scope row's path selects that row's activity inside the scope.
  const goToValidationNode = useCallback((nodeId: string, path?: string) => {
    const currentRoot = draft?.state.rootActivity;
    const scope = currentRoot ? findActivityNode(currentRoot, nodeId, catalogByVersion) : null;
    repairVariableReference(compensationValidationTarget(scope, path) ?? nodeId);
  }, [draft?.state.rootActivity, catalogByVersion, repairVariableReference]);

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

  const currentTestRun = draft && testRun?.draftSignature === getDraftSignature(draft) ? testRun.view : null;
  const renderedTestRun = testRun?.view ?? null;
  // ADR 0040's equivalence signal: a test run resolving to a published artifact id proves the current
  // draft is behaviorally identical to that published version. Above the loading return — hooks must
  // run on every render.
  const publishedEquivalent = useDraftEquivalence(context, definitionId, currentTestRun);

  if (!details) {
    return (
      <div className="wf-empty wf-editor-empty" role={error ? "alert" : "status"}>
        {error?.message ? <>
          <p>{error.message}</p>
          <p>Selected workflow definition: <code>{definitionId}</code></p>
        </> : <p>Loading workflow editor…</p>}
        {error ? <div className="wf-actions wf-empty-actions">
          <button type="button" onClick={retryLoad}>Retry loading this workflow</button>
          <button type="button" onClick={onBack}>Browse workflows</button>
        </div> : null}
      </div>
    );
  }

  if (!draft) {
    return (
      <div className="wf-empty wf-editor-empty" role={error ? "alert" : "status"}>
        <p>This workflow definition has no current draft.</p>
        <p>Selected workflow definition: <code>{definitionId}</code></p>
        {error?.message ? <p>{error.message}</p> : null}
        <div className="wf-actions wf-empty-actions">
          <button type="button" onClick={retryLoad}>Reload this workflow</button>
          <button type="button" onClick={onBack}>Browse workflows</button>
        </div>
      </div>
    );
  }

  // The inspected node is either a canvas node (labelled by its node data) or the scope owner, which
  // has no node on its own canvas — fall back to its catalog display name.
  const presentationByNodeId = indexActivityPresentation(draft.activityPresentation);
  const visibleFrames = frames.map(frame => {
    const owner = findActivityNode(draft.state.rootActivity, frame.ownerNodeId, catalogByVersion);
    const slot = owner
      ? getChildSlots(owner, catalogByVersion).find(candidate => candidate.id === frame.slotId)
      : undefined;
    const catalogItem = owner ? catalogByVersion.get(owner.activityVersionId) : undefined;
    return owner && slot
      ? {
          ...frame,
          label: slotCrumbLabel(
            resolveActivityLabel(
              presentationByNodeId.get(owner.nodeId),
              catalogItem,
              catalogItem?.activityTypeKey ?? owner.activityVersionId),
            slot)
        }
      : frame;
  });
  const inspectedPresentation = inspectedNode
    ? presentationByNodeId.get(inspectedNode.nodeId)
    : undefined;
  const inspectedLabel = inspectedNode
    ? (nodes.find(node => node.id === inspectedNode.nodeId)?.data.label
      ?? resolveActivityLabel(
        inspectedPresentation,
        inspectedCatalogItem,
        inspectedCatalogItem?.activityTypeKey ?? inspectedNode.activityVersionId))
    : "";

  const visibleStatus = renderedTestRun && status.startsWith("Test run") ? "" : status;
  const openWorkflowRun = (workflowExecutionId: string, initialTab?: "issues") => {
    const tabQuery = initialTab ? `?tab=${encodeURIComponent(initialTab)}` : "";
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(workflowExecutionId)}${tabQuery}`);
    window.dispatchEvent(new PopStateEvent("popstate"));
  };
  const contributedPanelTabs = availableDesignerPanels
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
    ...(inlineSteps ? [] : [{
      id: "activities",
      title: elements.length ? "Elements" : "Activities",
      order: 0,
      icon: <Boxes size={15} />,
      render: () => (
        <>
          <ActivityPalettePanel
            paletteSearch={paletteSearch}
            onSearchChange={setPaletteSearch}
            groups={filteredPaletteGroups}
            elements={elements}
            disabled={!canAddActivitiesToCanvas}
            onElementClick={onElementClick}
            onElementDragStart={onElementDragStart}
            onElementDragEnd={onElementDragEnd}
            onElementPointerDown={onElementPointerDown}
            expandedCategories={expandedPaletteCategories}
            onToggleCategory={togglePaletteCategory}
            onActivityClick={isCompensationScopeCanvas ? addCompensationStepFromPalette : onPaletteClick}
            onActivityDragStart={onPaletteDragStart}
            onActivityDragEnd={onPaletteDragEnd}
            onActivityPointerDown={onPalettePointerDown}
          />
        </>
      )
    }]),
    ...contributedPanelTabs.filter(tab => tab.side === "left")
  ].sort(compareWorkflowPanelTabs);
  const rightPanelTabs: WorkflowEditorPanelTab[] = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: <ListTree size={15} />,
      render: () => <>
        {runCanvas.eligible ? <div className="wf-attached-inspector-views"><PanelTabList label="Activity view" activeTabId={inspectorView} onSelect={value => setInspectorView(value as "configuration" | "run-data")}
          tabs={[{ id: "configuration", title: "Configuration", order: 0, icon: null, render: () => null }, { id: "run-data", title: "Run data", order: 1, icon: null, render: () => null }]} /></div> : null}
        {inspectorView === "run-data" && runCanvas.eligible ? <React.Suspense fallback={<p role="status">Loading activity inspection…</p>}>
          <AttachedRunActivityInspector context={context} canvas={runCanvas} selection={runSelection} activityId={inspectedNode?.nodeId ?? null}
            ownerId={scopeOwner?.nodeId ?? null} catalog={catalog} expressionEditors={expressionEditors} onEnter={execution => {
              const root = draft.state.rootActivity;
              const node = root ? findActivityNode(root, execution.authoredActivityId, catalogByVersion) : null;
              const firstChild = node ? getChildSlots(node, catalogByVersion)[0]?.activities[0] : null;
              const path = root ? findNodeScopePath(root, firstChild?.nodeId ?? execution.authoredActivityId, activity => { const item = catalogByVersion.get(activity.activityVersionId); return item ? getActivityDisplay(item) : activity.nodeId; }, catalogByVersion) : null;
              if (path) navigateToScope(path, firstChild?.nodeId ?? execution.authoredActivityId);
            }} />
        </React.Suspense> : selectedBpmnElement && !selectedNode ? (
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
          key={inspectedNode?.nodeId ?? "no-activity"}
          context={context}
          readOnly={editorDoc.pinned}
          draftId={draft.id}
          expressionTooling={resolvedExpressionTooling}
          expressionEditorSessionScope={expressionEditorSessionScope}
          workflowState={draft.state}
          selectedNode={inspectedNode}
          selectedNodeLabel={inspectedLabel}
          selectedActivityType={inspectedNode ? (inspectedDescriptor?.typeName ?? catalogByVersion.get(inspectedNode.activityVersionId)?.activityTypeKey ?? "Unknown") : ""}
          selectedPresentation={inspectedPresentation}
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
          catalog={availablePaletteCatalog}
          choicesForSlot={choicesForSlot}
          placementNotice={selectedNode ? activityPaletteNotice(inspectedCatalogItem, paletteContext) : undefined}
          catalogByVersion={catalogByVersion}
          selectedSupportsScopedVariables={inspectedSupportsScopedVariables}
          propertyEditors={propertyEditors}
          expressionEditors={expressionEditors}
          expressionDescriptors={expressionDescriptors}
          expressionDescriptorStatus={expressionDescriptorStatus}
          descriptorStatus={descriptorStatus}
          onRetryExpressionDescriptors={() => { void reloadExpressionDescriptors(); }}
          scopedVariableAnalysis={scopedVariableAnalysis}
          activeTabId={activeInspectorTabId}
          onActiveTabChange={setActiveInspectorTabId}
          onSelectedActivityChange={updateSelectedActivity}
          onSelectedPresentationChange={updateSelectedPresentation}
          onChangeReusableVersion={openVersionChange}
          onEnterSlot={enterSlotScope}
          onReplaceSlotActivity={replaceSlotActivity}
          isStartNode={isFlowchartScope && inspectedNode ? inspectedNode.nodeId === flowchartStartNodeId : undefined}
          onSetAsStartNode={isFlowchartScope ? setStartNode : undefined}
          httpTestRequest={inspectedIsHttpEndpoint ? {
            available: !!publishedRuntimeExecutable?.artifactId,
            onOpen: () => { if (inspectedNode) setHttpTestRequestActivityId(inspectedNode.nodeId); }
          } : undefined}
        />
      )}
      </>
    },
    {
      id: "runtime",
      title: "Runtime",
      order: 5,
      icon: <Play size={15} />,
      render: () => <WorkflowRuntimePanel testRun={testRun} context={context} observation={observation} currentDraftSignature={draft ? getDraftSignature(draft) : ""} stimulusEditors={testStimulusEditors} publishedEquivalent={publishedEquivalent} onOpenRun={openWorkflowRun} activityLabels={activityLabels} onRunViewUpdated={editorDoc.updateTestRunView} onWaitDraftUpdated={editorDoc.updateTestRunWaitDraft} />
    },
    {
      id: "test-drive",
      title: "Test Run",
      order: 6,
      icon: <Play size={15} />,
      render: () => (
        <LazyWorkflowTestDrivePanel
          context={context}
          draft={draft}
          runInputEditors={runInputEditors}
          stimulusEditors={testStimulusEditors}
          setupState={testDriveSetup}
          onSetupStateChange={setTestDriveSetup}
          busy={busy}
          onStart={runTestDrive}
          onUpdateRun={updateTestDriveResult}
        />
      )
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
  ].filter(tab => !documentAdapter || !["runtime", "test-drive", "artifacts"].includes(tab.id)).sort(compareWorkflowPanelTabs);
  const activeLeftPanel = leftPanelTabs.find(tab => tab.id === activeLeftPanelId) ?? leftPanelTabs[0];
  const activeRightPanel = rightPanelTabs.find(tab => tab.id === activeRightPanelId) ?? rightPanelTabs[0];
  const canvasViewTabs: WorkflowEditorPanelTab[] = [
    { id: "designer", title: "Designer", order: 0, icon: <WorkflowIcon size={14} />, render: () => null },
    { id: "code", title: "Code", order: 1, icon: <Code2 size={14} />, render: () => null },
    { id: "properties", title: "Properties", order: 2, icon: <SlidersHorizontal size={14} />, render: () => null }
  ];

  return (
    <section ref={editorRootRef} className="wf-editor" data-studio-workflow-editor="" inert={templateNavigationPending} aria-busy={templateNavigationPending}>
      <WorkflowEditorToolbar
        templateMode={!!documentAdapter}
        name={details.definition.name}
        status={visibleStatus}
        saving={saving}
        busy={busy}
        autosaveEnabled={autosaveEnabled}
        onAutosaveChange={setAutosaveEnabled}
        canUndo={canUndoNow && !editorDoc.pinned}
        canRedo={canRedoNow && !editorDoc.pinned}
        canAutoLayout={canAutoLayout && !editorDoc.pinned}
        onUndo={() => { if (!editorDoc.pinned) undo(); }}
        onRedo={() => { if (!editorDoc.pinned) redo(); }}
        onAutoLayout={autoLayout}
        onBack={() => {
          if (!documentAdapter || !editableDraft) { onBack(); return; }
          if (templateNavigationPending) return;
          setTemplateNavigationPending(true);
          setStatus("Saving…");
          void flushDefinitionMeta().then(async saved => {
            if (saved) { await saveDraft(editableDraft, "Saved"); onBack(); }
          }).catch(() => undefined).finally(() => setTemplateNavigationPending(false));
        }}
        onSave={() => { if (documentAdapter) void flushDefinitionMeta().then(saved => { if (saved) void save(); }); else void save(); }}
        onPublish={mode => void preparePublication(mode)}
        onRun={() => void run()}
        canRun={canRunTest}
        runTitle={draft.state.rootActivity ? "Run a transient test of the current design" : "Add a root activity before running"}
        runStatus={renderedTestRun ? (
          <AttachedTestRunStatus
            view={renderedTestRun}
            canvas={{ ...runCanvas,
              attach: () => { if (!runCanvas.stale || editorDoc.showSnapshot()) { runCanvas.attach(); setInspectorView("run-data"); setActiveRightPanelId("inspector"); setInspectorCollapsed(false); } },
              detach: () => { editorDoc.returnToEditing(); runCanvas.detach(); } }}
            onOpenRun={() => { if (renderedTestRun.workflowExecutionId) openWorkflowRun(renderedTestRun.workflowExecutionId); }}
            onOpenDetails={() => {
              setActiveRightPanelId("runtime");
              setInspectorCollapsed(false);
            }}
          />
        ) : restoration.receipt ? <div className="wf-attached-test-run" role="group" aria-label="Previous Test Run">
          <span>Restore the previous Test Run after verifying the saved design and current access.</span>
          <button type="button" disabled={restoration.pending} onClick={() => void restoration.restore()}>{restoration.pending ? "Verifying Test Run…" : "Restore Test Run"}</button>
          {restoration.receipt.workflowExecutionId ? <button type="button" onClick={() => openWorkflowRun(restoration.receipt!.workflowExecutionId!)}>Open run</button> : null}
          <button type="button" onClick={restoration.dismiss}>Dismiss</button>
          {restoration.message ? <span role="status">{restoration.message}</span> : null}
        </div> : null}
        moreActions={documentAdapter ? [{ label: "Document", actions: [{ label: "Save a copy", icon: <Boxes size={15} />, onSelect: () => setSaveTemplateOpen(true) }, { label: "Export JSON", icon: <Download size={15} />, onSelect: exportJson }] }] : [
          ...(templatesAvailable ? [{ label: "Templates", actions: [{ label: "Save as template", icon: <Boxes size={15} />, disabled: busy, onSelect: () => setSaveTemplateOpen(true) }] }] : []),
          {
            label: "Weaver",
            actions: [
              ...(findRisksAction ? [{
                label: "Review risks",
                icon: <Sparkles size={15} />,
                disabled: busy || !aiProviderAvailable,
                title: aiProviderAvailable ? "Ask Weaver to review this draft for risks" : weaverUnavailableTitle,
                onSelect: () => dispatchAiAction(ai, findRisksAction, { definition: details.definition, draft })
              }] : []),
              ...(proposeUpdateAction ? [{
                label: "Propose update",
                icon: <Sparkles size={15} />,
                disabled: busy || !aiProviderAvailable,
                title: aiProviderAvailable ? "Ask Weaver to propose a reviewed update" : weaverUnavailableTitle,
                onSelect: () => dispatchAiAction(ai, proposeUpdateAction, { definition: details.definition, draft })
              }] : [])
            ]
          },
          {
            label: "Import & export",
            actions: [
              { label: "Export JSON", icon: <Download size={15} />, onSelect: exportJson },
              ...(executableArtifactExport.supported ? [{
                label: operation === "exportingArtifact" ? "Exporting artifact…" : "Export artifact",
                icon: <PackageOpen size={15} />,
                disabled: busy || !publishedExecutable,
                title: publishedExecutable
                  ? "Download the compiled executable artifact for a runtime engine"
                  : "Publish this workflow first: only a published version has a compiled executable artifact",
                onSelect: () => void exportExecutableArtifact()
              }] : []),
              ...(canUseBpmnInterchange ? [
                {
                  label: "Import BPMN",
                  icon: <Upload size={15} />,
                  disabled: busy,
                  title: "Import a BPMN 2.0 document, replacing this process",
                  onSelect: () => bpmnFileInputRef.current?.click()
                },
                {
                  label: "Export BPMN",
                  icon: <Download size={15} />,
                  title: "Export as BPMN 2.0 XML with diagram interchange",
                  onSelect: () => void exportBpmn()
                }
              ] : [])
            ]
          }
        ]}
      />
      {canUseBpmnInterchange ? (
        <input
          ref={bpmnFileInputRef}
          type="file"
          accept=".bpmn,.xml"
          style={{ display: "none" }}
          aria-label="Import BPMN file"
          onChange={event => {
            const file = event.target.files?.[0];
            event.target.value = "";
            if (file) void importBpmn(file);
          }}
        />
      ) : null}

      {saveTemplateOpen ? <React.Suspense fallback={<p role="status">Loading template dialog…</p>}><SaveWorkflowTemplateDialog context={context} draft={draft} name={details.definition.name + (documentAdapter ? " copy" : "")} description={details.definition.description} onClose={() => setSaveTemplateOpen(false)} /></React.Suspense> : null}
      {error ? (
        <WorkflowAlert
          error={error}
          onDismiss={() => setError("")}
          onCopied={label => setStatus(`${label} copied.`)}
          onCopyFailed={label => setStatus(`Could not copy ${label}.`)}
        />
      ) : null}

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
          onReview={reviewPublication}
          onPublish={confirmPublication}
          onCancel={cancelPublication}
          onOpenPublishedExecutable={openPublishedExecutable}
        />
      ) : null}

      {httpTestRequestActivityId ? (
        <React.Suspense fallback={<p role="status">Loading HTTP request tool…</p>}>
          <HttpTestRequestDialog
            context={context}
            open
            artifactId={publishedRuntimeExecutable?.artifactId ?? null}
            authoredActivityId={httpTestRequestActivityId}
            onClose={() => setHttpTestRequestActivityId(null)}
          />
        </React.Suspense>
      ) : null}

      {expressionValidationWarning ? (
        <div className="wf-dialog-backdrop" role="presentation">
          <section
            className="wf-dialog"
            role="alertdialog"
            aria-modal="true"
            aria-labelledby="expression-validation-warning-title"
            aria-describedby="expression-validation-warning-description"
          >
            <h3 id="expression-validation-warning-title">Expression validation is unavailable</h3>
            <p id="expression-validation-warning-description">
              The backend cannot currently verify every expression. No known blocking error was found,
              but this Test Run may fail. Proceed only if you accept that risk.
            </p>
            <div className="wf-dialog-actions">
              <button type="button" onClick={cancelUnavailableExpressionValidation}>Cancel</button>
              <button type="button" onClick={() => { void confirmUnavailableExpressionValidation(); }}>
                Proceed with Test Run
              </button>
            </div>
          </section>
        </div>
      ) : null}

      <GraphAuthoringWorkbench
        resourceKind="workflow-definition"
        layout={sidePanelLayout}
        palette={activeLeftPanel ? {
          ariaLabel: elements.length ? "Elements panel" : "Activities panel",
          tabLabel: elements.length ? "Elements panel tabs" : "Activities panel tabs",
          tabs: leftPanelTabs,
          activeTabId: activeLeftPanel.id,
          onSelect: setActiveLeftPanelId
        } : undefined}
        canvas={<main className="wf-canvas-shell">
          <div className="wf-canvas-tabs">
            <PanelTabList
              label="Editor view tabs"
              tabs={canvasViewTabs}
              activeTabId={canvasView}
              onSelect={tabId => setCanvasView(tabId as CanvasView)}
            />
          </div>
          {canvasView === "code" ? (
            <WorkflowCodeView draft={editableDraft ?? draft} inspectionSnapshot={editorDoc.pinned ? draft : undefined} onApply={applyEditedJson} />
          ) : canvasView === "properties" ? (
            <fieldset className="wf-snapshot-properties" disabled={editorDoc.pinned}><WorkflowPropertiesView details={details} draft={draft} context={context} onStateChange={updateDraftState} onDefinitionMetaChange={updateDefinitionMeta} /></fieldset>
          ) : (
          <>
          <ScopeBreadcrumb frames={visibleFrames} onNavigate={next => navigateToScope(next, null)} />
          {isCompensationScopeCanvas && scopeOwner && !isUnsupportedDesigner ? (
            <CompensationScopeDesigner
              owner={scopeOwner}
              nodes={nodes}
              selectedNodeId={selectedNodeId}
              readOnly={!canAddActivitiesToCanvas}
              canvasRef={canvasRef}
              onSelect={selectEditorNode}
              onChangeSteps={(edit, nextStatus) => {
                // An open picker targets a row by index; once rows change, that index may name another step.
                setCompensationPick(null);
                updateCompensationSteps(edit, nextStatus);
              }}
              onRequestActivity={(target, anchor) => setCompensationPick({ target, ...anchor })}
              onOpenSlot={(ownerId, label, slot) => enterSlotScope(ownerId, slot, slotCrumbLabel(label, slot))}
            >
              {compensationPick ? (
                <ConnectMenu
                  clientX={compensationPick.clientX}
                  clientY={compensationPick.clientY}
                  activities={paletteCatalog}
                  onPick={pickCompensationActivity}
                  onClose={() => setCompensationPick(null)}
                />
              ) : null}
            </CompensationScopeDesigner>
          ) : scope?.slot.mode === "sequence" && scope.slot.cardinality === "many" && !isUnsupportedDesigner ? (
            <SequenceDesigner
              nodes={nodes}
              selectedNodeId={selectedNodeId}
              readOnly={!canAddActivitiesToCanvas}
              canvasRef={canvasRef}
              availabilityLookup={availabilityLookup}
              onSelect={selectEditorNode}
              onInsert={openSequenceInsertMenu}
              onDropActivity={(versionId, index) => {
                const activity = paletteCatalog.find(item => item.activityVersionId === versionId);
                if (activity) insertSequenceActivity(activity, index);
              }}
              onMove={moveSequenceActivity}
              onDelete={deleteCanvasNode}
              onDuplicate={nodeId => duplicateSelectedActivities([nodeId])}
              onSetWorkflowStart={setWorkflowStart}
              onOpenSlot={(ownerId, label, slot) => enterSlotScope(ownerId, slot, slotCrumbLabel(label, slot))}
            >
              {connectMenu ? <ConnectMenu clientX={connectMenu.clientX} clientY={connectMenu.clientY} activities={paletteCatalog} onPick={onConnectMenuPick} onClose={() => setConnectMenu(null)} /> : null}
            </SequenceDesigner>
          ) : <GraphAuthoringCanvas
            canvasRef={canvasRef}
            nodeActions={nodeActions}
            canvasProps={{
              tabIndex: -1,
              "aria-label": "Workflow canvas",
              onDragOver: onCanvasDragOver,
              onDragLeave: onCanvasDragLeave,
              onDrop: onCanvasDrop,
              onKeyDownCapture: event => activateWorkflowCanvasNode(event, nodeId => {
                closeActivityContextMenu();
                selectEditorNode(nodeId);
              })
            }}
            edgeActions={edgeActions}
            availabilityLookup={availabilityLookup}
            slotNavigation={slotNavigation}
            reactFlowProps={{
              nodes,
              edges,
              onInit: setReactFlowInstance,
              onMoveEnd,
              onNodesChange,
              onEdgesChange,
              onNodesDelete,
              onEdgesDelete,
              onConnect,
              onConnectStart: canCreateActivityFromPort ? onConnectStart : undefined,
              onConnectEnd: canCreateActivityFromPort ? onConnectEnd : undefined,
              onReconnect: isFlowchartDesigner || isBpmnDesigner ? onReconnect : undefined,
              isValidConnection,
              onDragOver: onCanvasDragOver,
              onDragLeave: onCanvasDragLeave,
              onDrop: onCanvasDrop,
              onPaneClick: () => {
                closeActivityContextMenu();
                select(null);
              },
              onPaneContextMenu: event => {
                event.preventDefault();
                closeActivityContextMenu();
              },
              onNodeClick: (_, node) => {
                closeActivityContextMenu();
                selectEditorNode(node.id);
              },
              onNodeContextMenu: isUnsupportedDesigner || editorDoc.pinned ? undefined : (event, node) => {
                event.preventDefault();
                const selectedIds = nodes.filter(candidate => candidate.selected).map(candidate => candidate.id);
                const nodeIds = selectedIds.includes(node.id) ? selectedIds : [node.id];
                if (!selectedIds.includes(node.id)) select(node.id);
                openActivityContextMenu(event.clientX, event.clientY, nodeIds);
              },
              onNodeDragStop: isUnsupportedDesigner || editorDoc.pinned ? undefined : commitLayout,
              nodesConnectable: canCreateActivityFromPort,
              nodesDraggable: !isUnsupportedDesigner && !editorDoc.pinned,
              deleteKeyCode: isUnsupportedDesigner || editorDoc.pinned ? null : ["Backspace", "Delete"]
            }}
            overlays={<>
            {insideEmptySlot ? (
              <SlotEmptyState
                slotLabel={scope?.slot.label ?? "this slot"}
                catalog={paletteCatalog}
                onPickActivity={pickActivityForEmptySlot}
                onBrowseAll={openEmptyConnectMenu}
              />
            ) : inlineSteps ? (
              <AddStepButton disabled={!canAddActivitiesToCanvas} onOpen={openAddStepMenu} />
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
            {activityContextMenu ? (
              <ActivityCanvasContextMenu
                x={activityContextMenu.x}
                y={activityContextMenu.y}
                onDuplicate={() => duplicateSelectedActivities(activityContextMenu.nodeIds)}
                onClose={closeActivityContextMenu}
              />
            ) : null}
            </>}
          />}
          <ValidationPanel
            draft={draft}
            errors={combinedValidationErrors}
            onRepair={repairVariableReference}
            onSelectNode={goToValidationNode}
            onSetAsStartNode={isFlowchartScope ? setStartNode : undefined}
            unavailable={draftValidations.available === false}
          />
          </>
          )}
        </main>}
        inspector={{
          ariaLabel: "Inspector panel",
          tabLabel: "Inspector panel tabs",
          tabs: rightPanelTabs,
          activeTabId: activeRightPanel.id,
          onSelect: setActiveRightPanelId
        }}
      />
    </section>
  );
}
