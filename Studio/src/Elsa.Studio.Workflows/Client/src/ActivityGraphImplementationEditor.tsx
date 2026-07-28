import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  addEdge,
  type Connection,
  type Edge,
  type Node
} from "@xyflow/react";
import { ArrowLeft, ArrowRight, Boxes, ChevronRight, ListTree, Network, Redo2, Trash2, Undo2 } from "lucide-react";
import type {
  StudioActivityDefinitionContract,
  StudioActivityDefinitionImplementationEditorProps,
  StudioActivityDiagnosticFocusResult,
  StudioActivityInputDescriptor,
  StudioExpressionDescriptor,
  StudioExpressionEditorContribution
} from "@elsa-workflows/studio-sdk";
import type { ActivityCatalogItem, ActivityNode, VariableDefinition, WorkflowDefinitionState, WorkflowInput } from "./workflowTypes";
import {
  buildCanvas,
  createActivityNode,
  getActivityDisplay,
  getChildSlots,
  replaceSlotActivities,
  syncCanvasToScope,
  updateActivity,
  withFlowchartConnections,
  type WorkflowNodeData
} from "./workflowAdapter";
import { createNodeId } from "./workflow-editor/editorHelpers";
import { useWorkflowActivities } from "./api/activityDesign";
import { activityGraphDiagnosticFocusEvent, type ActivityGraphDiagnosticFocusEventDetail } from "./activityGraphDiagnosticFocus";
import { ActivityPalettePanel } from "./workflow-editor/ActivityPalettePanel";
import { groupActivityPalette } from "./workflow-editor/editorHelpers";
import { GraphAuthoringCanvas } from "./graph-authoring/GraphAuthoringCanvas";
import { activityGraphDocumentAdapter, activityGraphLayoutToDesign } from "./activityGraphDocumentAdapter";
import type { WorkflowEdge, WorkflowEditorPanelTab } from "./workflow-editor/editorTypes";
import { GraphAuthoringWorkbench } from "./graph-authoring/GraphAuthoringWorkbench";
import { useGraphAuthoringCanvas } from "./graph-authoring/useGraphAuthoringCanvas";
import { filterGraphAuthoringContributions } from "./graph-authoring/graphAuthoringContributions";
import { ScopedVariablesEditor } from "./WorkflowPropertiesView";
import { toActivityDescriptor } from "./workflow-editor/useWorkflowEditorData";
import { decorateWorkflowCanvasElements } from "./workflow-editor/workflowAccessibility";
import { listExpressionDescriptors } from "./api/expressions";
import { WorkflowReferenceAuthoringProvider } from "./workflowReferenceAuthoring";
import {
  activityGraphInspectorCollapsedStorageKey,
  activityGraphInspectorWidthStorageKey,
  activityGraphPaletteCollapsedStorageKey,
  activityGraphPaletteWidthStorageKey,
  activityGraphSidePanelMaximizedStorageKey
} from "./workflow-editor/constants";
import { useSidePanelLayout, type SidePanelLayoutStorageKeys } from "./workflow-editor/useSidePanelLayout";
import {
  InspectorPanel,
  type ActivityInspectorTabId
} from "./workflow-editor/InspectorPanel";
import { compareWorkflowPanelTabs } from "./workflow-editor/PanelTabList";
import { supportsScopedVariables } from "./scopedVariables";
import { computeAutoLayout } from "./workflowLayout";

export interface ActivityGraphOutcomeMapping {
  sourceOutcomeReferenceKey: string;
  boundaryOutcomeReferenceKey: string;
}

export interface ActivityGraphOutcomeChoice {
  referenceKey: string;
  name: string;
}

export interface ActivityGraphPayload {
  rootActivity: ActivityNode;
  variables: unknown[];
  outputMappings: ActivityGraphOutputMapping[];
  outcomeMappings: ActivityGraphOutcomeMapping[];
}

export interface ActivityGraphOutputMapping {
  outputReferenceKey: string;
  source: {
    syntax: string;
    value: unknown;
    [key: string]: unknown;
  };
  [key: string]: unknown;
}

const emptyActivities: ActivityNode[] = [];
const emptyOutcomeContracts: StudioActivityDefinitionContract["outcomes"] = [];
const activityGraphSidePanelStorageKeys: SidePanelLayoutStorageKeys = {
  paletteWidth: activityGraphPaletteWidthStorageKey,
  inspectorWidth: activityGraphInspectorWidthStorageKey,
  paletteCollapsed: activityGraphPaletteCollapsedStorageKey,
  inspectorCollapsed: activityGraphInspectorCollapsedStorageKey,
  maximized: activityGraphSidePanelMaximizedStorageKey
};

export function ActivityGraphPublicInterfaceEditor({
  context,
  contract,
  providerSchemaVersion,
  value,
  expressionEditors = [],
  readOnly,
  onChange
}: StudioActivityDefinitionImplementationEditorProps) {
  const catalogQuery = useWorkflowActivities(context);
  const catalog = useMemo(() => catalogQuery.data?.activities ?? [], [catalogQuery.data?.activities]);
  const catalogByVersion = useMemo(() => new Map(catalog.map(item => [item.activityVersionId, item])), [catalog]);
  const payload = normalizePayload(value.payload);
  const root = payload.rootActivity;
  const outcomeMappingOptions = useMemo(
    () => getActivityGraphOutcomeMappingOptions(
      root,
      catalogByVersion.get(root.activityVersionId),
      { outcomes: contract?.outcomes ?? emptyOutcomeContracts }
    ),
    [catalogByVersion, contract, root]
  );
  const [sourceOutcomeReferenceKey, setSourceOutcomeReferenceKey] = useState("");
  const [boundaryOutcomeReferenceKey, setBoundaryOutcomeReferenceKey] = useState("");
  const expressionState = useActivityGraphExpressionDescriptors(context);
  const publicInputs = useMemo(
    () => (contract?.inputs ?? []).map(toWorkflowInput),
    [contract?.inputs]
  );
  const visibleVariables = useMemo(
    () => payload.variables.flatMap(toVisibleGraphVariable),
    [payload.variables]
  );
  const workflowState = useMemo<WorkflowDefinitionState>(
    () => ({ inputs: publicInputs, variables: toWorkflowVariables(payload.variables) }),
    [payload.variables, publicInputs]
  );

  useEffect(() => {
    setSourceOutcomeReferenceKey(current => outcomeMappingOptions.sourceOutcomes.some(outcome => outcome.referenceKey === current) ? current : "");
    setBoundaryOutcomeReferenceKey(current => outcomeMappingOptions.boundaryOutcomes.some(outcome => outcome.referenceKey === current) ? current : "");
  }, [outcomeMappingOptions]);

  const commitPayload = (nextPayload: ActivityGraphPayload) => {
    onChange({ ...value, payload: { ...(isRecord(value.payload) ? value.payload : {}), ...nextPayload } });
  };
  const updateOutcomeMappings = (outcomeMappings: ActivityGraphOutcomeMapping[]) => {
    commitPayload({ ...payload, outcomeMappings });
  };
  const addOutcomeMapping = () => {
    if (!sourceOutcomeReferenceKey || !boundaryOutcomeReferenceKey) return;
    if (!canAddActivityGraphOutcomeMapping(payload.outcomeMappings, sourceOutcomeReferenceKey)) return;
    updateOutcomeMappings([
      ...payload.outcomeMappings,
      { sourceOutcomeReferenceKey, boundaryOutcomeReferenceKey }
    ]);
    setSourceOutcomeReferenceKey("");
    setBoundaryOutcomeReferenceKey("");
  };
  const updateOutputMapping = (
    outputReferenceKey: string,
    source: ActivityGraphOutputMapping["source"] | null
  ) => {
    const retained = payload.outputMappings.filter(mapping => mapping.outputReferenceKey !== outputReferenceKey);
    commitPayload({
      ...payload,
      outputMappings: source ? [...retained, { outputReferenceKey, source }] : retained
    });
  };
  const mappedOutcomeTargets = new Set(payload.outcomeMappings.map(mapping => mapping.boundaryOutcomeReferenceKey));
  const unresolvedOutcomes = outcomeMappingOptions.boundaryOutcomes.filter(outcome => !mappedOutcomeTargets.has(outcome.referenceKey));

  return (
    <div className="ad-graph-public-interface">
      <section className="ad-graph-output-mappings" aria-labelledby="activity-graph-output-mappings-title">
        <h3 id="activity-graph-output-mappings-title">Boundary output mappings</h3>
        <p>Supply public outputs from public inputs, graph variables, node outputs, or another supported expression.</p>
        {(contract?.outputs ?? []).length === 0
          ? <p className="wf-muted">No public outputs are defined.</p>
          : <WorkflowReferenceAuthoringProvider
              workflowState={workflowState}
              workflowInputs={publicInputs}
              visibleVariables={visibleVariables}
              status="ready"
            >
              {(contract?.outputs ?? []).map(output => {
                const mapping = payload.outputMappings.find(item => item.outputReferenceKey === output.referenceKey);
                return (
                  <ActivityGraphOutputMappingEditor
                    key={output.referenceKey}
                    output={output}
                    mapping={mapping}
                    expressionDescriptors={expressionState.descriptors}
                    expressionDescriptorStatus={expressionState.status}
                    expressionEditors={expressionEditors}
                    root={root}
                    readOnly={readOnly}
                    onChange={source => updateOutputMapping(output.referenceKey, source)}
                  />
                );
              })}
            </WorkflowReferenceAuthoringProvider>}
      </section>

      {providerSchemaVersion === "2" ? <section className="ad-graph-outcome-mappings" aria-labelledby="activity-graph-outcome-mappings-title">
        <h3 id="activity-graph-outcome-mappings-title">Boundary outcome mappings</h3>
        <p>Map each outcome emitted by the root activity to an emitted public boundary outcome. Several sources may converge on one public outcome.</p>
        {payload.outcomeMappings.length ? <ul>{payload.outcomeMappings.map(mapping => <li key={mapping.sourceOutcomeReferenceKey}>
          <span>{outcomeName(outcomeMappingOptions.sourceOutcomes, mapping.sourceOutcomeReferenceKey)} → {outcomeName(outcomeMappingOptions.boundaryOutcomes, mapping.boundaryOutcomeReferenceKey)}</span>
          <button type="button" onClick={() => updateOutcomeMappings(payload.outcomeMappings.filter(item => item !== mapping))} disabled={readOnly}>Remove mapping</button>
        </li>)}</ul> : <p>No boundary outcome mappings are defined.</p>}
        {unresolvedOutcomes.length > 0 ? <p role="alert">Emitted outcomes still needing a mapping: {unresolvedOutcomes.map(item => item.name).join(", ")}.</p> : null}
        {outcomeMappingOptions.sourceOutcomes.length === 0 ? <p role="status">The selected root activity does not expose stable outcome reference keys.</p> : outcomeMappingOptions.boundaryOutcomes.length === 0 ? <p role="status">Add and emit a public contract outcome before creating a mapping.</p> : <div className="ad-graph-outcome-mapping-add">
          <label><span>Root outcome</span><select aria-label="Root outcome reference key" value={sourceOutcomeReferenceKey} onChange={event => setSourceOutcomeReferenceKey(event.target.value)} disabled={readOnly}><option value="">Select an outcome</option>{outcomeMappingOptions.sourceOutcomes.map(outcome => <option key={outcome.referenceKey} value={outcome.referenceKey} disabled={payload.outcomeMappings.some(mapping => mapping.sourceOutcomeReferenceKey === outcome.referenceKey)}>{outcome.name}</option>)}</select></label>
          <label><span>Boundary outcome</span><select aria-label="Boundary outcome reference key" value={boundaryOutcomeReferenceKey} onChange={event => setBoundaryOutcomeReferenceKey(event.target.value)} disabled={readOnly}><option value="">Select an emitted outcome</option>{outcomeMappingOptions.boundaryOutcomes.map(outcome => <option key={outcome.referenceKey} value={outcome.referenceKey}>{outcome.name}</option>)}</select></label>
          <button type="button" onClick={addOutcomeMapping} disabled={readOnly || !sourceOutcomeReferenceKey || !boundaryOutcomeReferenceKey}>Add mapping</button>
        </div>}
      </section> : <section className="ad-inline-status" role="status">
        Schema 1 retains its historical single <code>done</code> boundary. Migrate explicitly to schema 2 to author outcome mappings.
      </section>}
    </div>
  );
}

export function ActivityGraphImplementationEditor({
  context,
  definitionId,
  draftId,
  contract,
  value,
  propertyEditors = [],
  expressionEditors = [],
  graphAuthoringPanels = [],
  historyResetKey = `${draftId}:active`,
  readOnly,
  onChange
}: StudioActivityDefinitionImplementationEditorProps) {
  const catalogQuery = useWorkflowActivities(context);
  const catalog = useMemo(() => catalogQuery.data?.activities ?? [], [catalogQuery.data?.activities]);
  const catalogByVersion = useMemo(() => new Map(catalog.map(item => [item.activityVersionId, item])), [catalog]);
  const [scopePath, setScopePath] = useState<string[]>([]);
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);
  const [paletteSearch, setPaletteSearch] = useState("");
  const [expandedCategories, setExpandedCategories] = useState<Set<string>>(() => new Set());
  const [activeLeftPanelId, setActiveLeftPanelId] = useState("activities");
  const [activeRightPanelId, setActiveRightPanelId] = useState("inspector");
  const [activeInspectorTabId, setActiveInspectorTabId] = useState<ActivityInspectorTabId>("inputs");
  const [diagnosticFocusVersion, setDiagnosticFocusVersion] = useState(0);
  const draggedActivityRef = useRef<ActivityCatalogItem | null>(null);
  const editorRef = useRef<HTMLDivElement | null>(null);
  const pendingDiagnosticFocusRef = useRef<{
    target: "root" | string;
    complete(result: StudioActivityDiagnosticFocusResult): void;
  } | null>(null);
  const expressionState = useActivityGraphExpressionDescriptors(context);
  const sidePanelLayout = useSidePanelLayout(activityGraphSidePanelStorageKeys);

  const buildModel = useCallback((documentRoot: ActivityNode, layout: ReturnType<typeof activityGraphLayoutToDesign>) => {
    const documentOwner = findNodeByPath(documentRoot, scopePath, catalogByVersion) ?? documentRoot;
    const documentOwnerItem = catalogByVersion.get(documentOwner.activityVersionId);
    const documentSlot = supportsActivityNode(documentOwner, documentOwnerItem)
      ? getChildSlots(documentOwner, catalogByVersion)[0]
      : undefined;
    if (!documentSlot) return { nodes: [], edges: [] };
    const model = buildCanvas({ owner: documentOwner, slot: documentSlot }, catalog, layout);
    return { nodes: model.nodes, edges: model.edges as WorkflowEdge[] };
  }, [catalog, catalogByVersion, scopePath]);

  const applyModel = useCallback((
    document: typeof value,
    nextNodes: Node<WorkflowNodeData>[],
    nextEdges: WorkflowEdge[]
  ) => {
    const documentRoot = activityGraphDocumentAdapter.readRoot(document);
    const documentOwner = findNodeByPath(documentRoot, scopePath, catalogByVersion) ?? documentRoot;
    const documentOwnerItem = catalogByVersion.get(documentOwner.activityVersionId);
    const documentSlot = supportsActivityNode(documentOwner, documentOwnerItem)
      ? getChildSlots(documentOwner, catalogByVersion)[0]
      : undefined;
    if (!documentSlot) return document;
    let nextOwner = syncCanvasToScope({ owner: documentOwner, slot: documentSlot }, nextNodes, nextEdges);
    if (documentSlot.mode === "flowchart") nextOwner = withFlowchartConnections(nextOwner, nextEdges);
    const nextRoot = updateActivity(documentRoot, documentOwner.nodeId, () => nextOwner, catalogByVersion);
    return activityGraphDocumentAdapter.replaceGraph(
      document,
      nextRoot,
      mergeCanvasLayout(activityGraphDocumentAdapter.readLayout(document), nextNodes)
    );
  }, [catalogByVersion, scopePath]);

  const canvas = useGraphAuthoringCanvas({
    document: value,
    adapter: activityGraphDocumentAdapter,
    resetKey: historyResetKey,
    buildModel,
    applyModel,
    onChange
  });
  const { nodes, edges, setNodes, setEdges, onNodesChange, onEdgesChange, commitCanvas, commitDocument, undo, redo, canUndo, canRedo } = canvas;
  const payload = normalizePayload(value.payload);
  const root = payload.rootActivity;
  const owner = findNodeByPath(root, scopePath, catalogByVersion) ?? root;
  const ownerCatalogItem = catalogByVersion.get(owner.activityVersionId);
  const ownerSupported = supportsActivityNode(owner, ownerCatalogItem);
  const slot = ownerSupported ? getChildSlots(owner, catalogByVersion)[0] : undefined;
  const activities = slot?.activities ?? emptyActivities;
  const selected = activities.find(activity => activity.nodeId === selectedNodeId) ?? null;
  const selectedCatalogItem = selected ? catalogByVersion.get(selected.activityVersionId) : undefined;
  const selectedIndex = selected ? activities.findIndex(activity => activity.nodeId === selected.nodeId) : -1;
  const selectedSupported = selected ? supportsActivityNode(selected, selectedCatalogItem) : true;
  const availableActivities = catalog.filter(item => item.available !== false && supportsActivityGraphAuthoring(item));
  const filteredPaletteGroups = useMemo(() => {
    const search = paletteSearch.trim().toLocaleLowerCase();
    return groupActivityPalette(availableActivities.filter(activity => !search ||
      getActivityDisplay(activity).toLocaleLowerCase().includes(search) ||
      activity.activityTypeKey.toLocaleLowerCase().includes(search) ||
      activity.category.toLocaleLowerCase().includes(search)));
  }, [availableActivities, paletteSearch]);
  const supportedPanels = filterGraphAuthoringContributions(graphAuthoringPanels, "activity-definition-graph");
  const panelContext = {
    resourceKind: "activity-definition-graph" as const,
    context,
    definitionId,
    draftId,
    rootActivity: root,
    selectedActivity: selected,
    readOnly
  };
  const publicInputs = useMemo(
    () => (contract?.inputs ?? []).map(toWorkflowInput),
    [contract?.inputs]
  );
  const workflowState = useMemo<WorkflowDefinitionState>(
    () => ({ inputs: publicInputs, variables: toWorkflowVariables(payload.variables) }),
    [payload.variables, publicInputs]
  );
  const visibleVariables = useMemo(
    () => payload.variables.flatMap(toVisibleGraphVariable),
    [payload.variables]
  );
  const accessibleCanvas = useMemo(() => {
    const decorated = decorateWorkflowCanvasElements(
      nodes.map(node => ({ ...node, selected: node.id === selectedNodeId })),
      edges
    );
    return {
      ...decorated,
      nodes: decorated.nodes.map(node => ({
        ...node,
        domAttributes: {
          ...node.domAttributes,
          "data-graph-node-id": node.id
        }
      }))
    };
  }, [edges, nodes, selectedNodeId]);

  useEffect(() => {
    if (!selectedNodeId || activities.some(activity => activity.nodeId === selectedNodeId)) return;
    setSelectedNodeId(null);
  }, [activities, selectedNodeId]);

  useEffect(() => {
    const editor = editorRef.current;
    if (!editor) return;
    const focusDiagnostic = (rawEvent: Event) => {
      const event = rawEvent as CustomEvent<ActivityGraphDiagnosticFocusEventDetail>;
      const location = event.detail.location;
      if (location.jsonPointer?.startsWith("/contract") || location.jsonPointer?.startsWith("/variables") || location.jsonPointer?.startsWith("/outputMappings")) return;
      const nodeId = location.referenceKey || nodeIdAtJsonPointer(payload, location.jsonPointer);
      if (!nodeId || nodeId === root.nodeId) {
        if (!location.jsonPointer?.startsWith("/rootActivity")) return;
        event.preventDefault();
        setScopePath([]);
        setSelectedNodeId(null);
        pendingDiagnosticFocusRef.current?.complete(unsupportedGraphFocus());
        pendingDiagnosticFocusRef.current = { target: "root", complete: event.detail.complete };
        setDiagnosticFocusVersion(version => version + 1);
        return;
      }
      const target = findNodeFocusTarget(root, nodeId, catalogByVersion);
      if (!target) return;
      event.preventDefault();
      setScopePath(target.ownerPath);
      setSelectedNodeId(nodeId);
      pendingDiagnosticFocusRef.current?.complete(unsupportedGraphFocus());
      pendingDiagnosticFocusRef.current = { target: nodeId, complete: event.detail.complete };
      setDiagnosticFocusVersion(version => version + 1);
    };
    editor.addEventListener(activityGraphDiagnosticFocusEvent, focusDiagnostic);
    return () => editor.removeEventListener(activityGraphDiagnosticFocusEvent, focusDiagnostic);
  }, [catalogByVersion, payload, root]);

  useEffect(() => {
    const pending = pendingDiagnosticFocusRef.current;
    if (!pending || !editorRef.current) return;
    const target = pending.target === "root"
      ? editorRef.current.querySelector<HTMLElement>("[data-graph-root-location]")
      : [...editorRef.current.querySelectorAll<HTMLElement>("[data-graph-node-id]")]
        .find(element => element.dataset.graphNodeId === pending.target);
    pendingDiagnosticFocusRef.current = null;
    if (!target) {
      pending.complete(unsupportedGraphFocus());
      return;
    }
    target.focus();
    target.scrollIntoView?.({ block: "center", inline: "center" });
    pending.complete(document.activeElement === target
      ? {
          kind: "focused",
          announcement: "Focused the provider-owned Activity Graph location. Use Return to diagnostic to restore the diagnostics context."
        }
      : unsupportedGraphFocus());
  }, [diagnosticFocusVersion, scopePath, selectedNodeId]);

  useEffect(() => () => {
    pendingDiagnosticFocusRef.current?.complete(unsupportedGraphFocus());
    pendingDiagnosticFocusRef.current = null;
  }, []);

  const commitRoot = (nextRoot: ActivityNode) => {
    commitDocument(activityGraphDocumentAdapter.replaceGraph(
      value,
      nextRoot,
      reconcileGraphLayout(nextRoot, catalogByVersion, activityGraphDocumentAdapter.readLayout(value))
    ));
  };
  const chooseRoot = (activityVersionId: string) => {
    const activity = catalogByVersion.get(activityVersionId);
    if (!activity || !supportsActivityGraphAuthoring(activity) || activityVersionId === root.activityVersionId) return;
    if (root.activityVersionId && !window.confirm("Replace the root activity and all nested graph content? This change will autosave.")) return;
    setScopePath([]);
    setSelectedNodeId(null);
    const nextRoot = createActivityNode(activity, root.nodeId || "root");
    commitDocument({
      ...activityGraphDocumentAdapter.replaceGraph(value, nextRoot, []),
      payload: { ...(isRecord(value.payload) ? value.payload : {}), ...payload, rootActivity: nextRoot, outcomeMappings: [] }
    });
  };
  const updateOwnerActivities = (nextActivities: ActivityNode[]) => {
    if (!slot) return;
    const nextRoot = updateActivity(root, owner.nodeId, current => replaceSlotActivities(current, slot, nextActivities), catalogByVersion);
    commitRoot(nextRoot);
  };
  const addActivity = () => {
    const activity = draggedActivityRef.current;
    if (!activity || !slot) return;
    const next = createActivityNode(activity, createNodeId(activity));
    updateOwnerActivities([...activities, next]);
    setSelectedNodeId(next.nodeId);
    draggedActivityRef.current = null;
  };
  const deleteSelected = () => {
    if (!selected) return;
    if (!window.confirm("Remove this activity and any nested graph content? This change will autosave.")) return;
    updateOwnerActivities(activities.filter(activity => activity.nodeId !== selected.nodeId));
    setSelectedNodeId(null);
  };
  const moveSelected = (offset: -1 | 1) => {
    if (!selected) return;
    const target = selectedIndex + offset;
    if (selectedIndex < 0 || target < 0 || target >= activities.length) return;
    const next = [...activities];
    [next[selectedIndex], next[target]] = [next[target], next[selectedIndex]];
    updateOwnerActivities(next);
  };
  const updateSelected = (nextSelected: ActivityNode) => {
    const nextRoot = updateActivity(root, nextSelected.nodeId, () => nextSelected, catalogByVersion);
    commitRoot(nextRoot);
  };
  const updateVariables = (next: unknown[]) => {
    commitDocument({
      ...value,
      payload: {
        ...(isRecord(value.payload) ? value.payload : {}),
        ...payload,
        variables: fromVariableEditorRecords(next)
      }
    });
  };
  const deleteCanvasNodes = (deleted: Node<WorkflowNodeData>[]) => {
    const deletedIds = new Set(deleted.map(node => node.id));
    const nextNodes = nodes.filter(node => !deletedIds.has(node.id));
    const nextEdges = edges.filter(edge => !deletedIds.has(edge.source) && !deletedIds.has(edge.target));
    setEdges(nextEdges);
    commitCanvas(nextNodes, nextEdges);
    if (selectedNodeId && deletedIds.has(selectedNodeId)) setSelectedNodeId(null);
  };
  const deleteCanvasEdges = (deleted: Edge[]) => {
    const deletedIds = new Set(deleted.map(edge => edge.id));
    commitCanvas(nodes, edges.filter(edge => !deletedIds.has(edge.id)));
  };
  const connectCanvas = (connection: Connection) => {
    const nextEdges = addEdge({ ...connection, type: "workflow" }, edges) as WorkflowEdge[];
    setEdges(nextEdges);
    commitCanvas(nodes, nextEdges);
  };
  const autoLayoutCanvas = () => {
    if (!slot || nodes.length === 0) return;
    const positions = computeAutoLayout(nodes, edges, slot.mode === "sequence" ? "sequence" : "flowchart");
    const nextNodes = nodes.map(node => {
      const position = positions.get(node.id);
      return position ? { ...node, position } : node;
    });
    setNodes(nextNodes);
    commitCanvas(nextNodes, edges);
  };

  const inspectedNode = selected ?? owner;
  const inspectedCatalogItem = selected ? selectedCatalogItem : ownerCatalogItem;
  const inspectedDescriptor = inspectedCatalogItem ? toActivityDescriptor(inspectedCatalogItem) : null;
  const inspectedSlots = inspectedNode ? getChildSlots(inspectedNode, catalogByVersion) : [];
  const inspectedSupportsVariables = selected
    ? supportsScopedVariables(selected, selectedCatalogItem)
    : scopePath.length === 0 || supportsScopedVariables(owner, ownerCatalogItem);
  const scopedVariableAnalysis = {
    visibleVariables,
    shadowingWarnings: [],
    status: "ready" as const
  };
  const replaceInspectedSlotActivity = (ownerNodeId: string, targetSlot: ReturnType<typeof getChildSlots>[number], activity: ActivityCatalogItem) => {
    const next = createActivityNode(activity, createNodeId(activity));
    const nextRoot = updateActivity(
      root,
      ownerNodeId,
      current => replaceSlotActivities(current, targetSlot, [next]),
      catalogByVersion
    );
    commitRoot(nextRoot);
    setSelectedNodeId(next.nodeId);
  };

  const contributedPanelTabs = supportedPanels.map(panel => {
    const Panel = panel.component;
    return {
      id: panel.id,
      title: panel.title,
      side: panel.side,
      order: panel.order ?? 500,
      icon: null,
      render: () => <Panel context={panelContext} />
    };
  });
  const leftPanelTabs: WorkflowEditorPanelTab[] = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: <Boxes size={15} />,
      render: () => (
        <div className="ad-graph-palette">
          <section data-graph-root-location tabIndex={-1} className="ad-graph-scope-owner"><span>Activity graph</span><strong>{ownerCatalogItem ? getActivityDisplay(ownerCatalogItem) : "Choose a composition"}</strong><small>The root owns this graph scope and is not a canvas node.</small></section>
          {catalogQuery.isPending ? <span role="status">Loading the authorized activity catalog…</span> : null}
          {catalogQuery.isError ? <span role="alert">The activity catalog is unavailable. Existing graph state remains unchanged.</span> : null}
          {!root.activityVersionId ? <div className="ad-graph-root-options" role="group" aria-label="Graph composition">{availableActivities.filter(activity => getChildSlots(createActivityNode(activity, "root"), activity).length > 0).map(activity => <button key={activity.activityVersionId} type="button" onClick={() => chooseRoot(activity.activityVersionId)} disabled={readOnly}>{getActivityDisplay(activity)}</button>)}</div> : null}
          <ActivityPalettePanel
            paletteSearch={paletteSearch}
            onSearchChange={setPaletteSearch}
            groups={filteredPaletteGroups}
            expandedCategories={expandedCategories}
            disabled={readOnly}
            onToggleCategory={category => setExpandedCategories(current => {
              const next = new Set(current);
              if (next.has(category)) next.delete(category);
              else next.add(category);
              return next;
            })}
            onActivityClick={activity => {
              draggedActivityRef.current = activity;
              addActivity();
            }}
            onActivityDragStart={(event, activity) => {
              draggedActivityRef.current = activity;
              event.dataTransfer.effectAllowed = "copy";
            }}
            onActivityDragEnd={() => {
              draggedActivityRef.current = null;
            }}
            onActivityPointerDown={() => undefined}
          />
        </div>
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
        <>
          {selected ? <div className="ad-graph-node-actions">
            <button type="button" aria-label="Move activity left" onClick={() => moveSelected(-1)} disabled={readOnly || selectedIndex <= 0}><ArrowLeft size={15} /></button>
            <button type="button" aria-label="Move activity right" onClick={() => moveSelected(1)} disabled={readOnly || selectedIndex < 0 || selectedIndex === activities.length - 1}><ArrowRight size={15} /></button>
            <button type="button" onClick={deleteSelected} disabled={readOnly}><Trash2 size={15} /> Remove</button>
          </div> : null}
          {!selectedSupported ? <span role="alert">This existing nested structure is preserved but cannot be structurally edited here.</span> : null}
          {!ownerSupported ? <span role="alert">This existing multi-slot structure is preserved but cannot be structurally edited by this graph host.</span> : null}
          <InspectorPanel
            key={`${inspectedNode?.nodeId ?? "no-activity"}:${selected ? "selected" : "owner"}`}
            context={context}
            workflowState={workflowState}
            selectedNode={inspectedNode}
            selectedNodeLabel={inspectedCatalogItem ? getActivityDisplay(inspectedCatalogItem) : inspectedNode?.nodeId ?? "Activity graph"}
            selectedActivityType={inspectedDescriptor?.typeName ?? inspectedCatalogItem?.activityTypeKey ?? "Unknown"}
            selectedDescriptor={inspectedDescriptor}
            selectedNodeAvailability={null}
            selectedSlots={inspectedSlots}
            inspectingScopeOwner={!selected}
            catalog={availableActivities}
            catalogByVersion={catalogByVersion}
            selectedSupportsScopedVariables={inspectedSupportsVariables}
            readOnly={readOnly}
            variablesPanel={!selected && scopePath.length === 0 ? (
              <fieldset disabled={readOnly} className="wf-container-variables">
                <ScopedVariablesEditor
                  context={context}
                  variables={toVariableEditorRecords(payload.variables)}
                  title="Graph variables"
                  addLabel="Add graph variable"
                  emptyLabel="No graph variables are defined."
                  onChange={updateVariables}
                />
              </fieldset>
            ) : undefined}
            propertyEditors={propertyEditors}
            expressionEditors={expressionEditors}
            expressionDescriptors={expressionState.descriptors}
            expressionDescriptorStatus={expressionState.status}
            descriptorStatus={catalogQuery.isPending ? "loading" : catalogQuery.isError ? "failed" : "ready"}
            onRetryExpressionDescriptors={expressionState.retry}
            scopedVariableAnalysis={scopedVariableAnalysis}
            activeTabId={activeInspectorTabId}
            onActiveTabChange={setActiveInspectorTabId}
            onSelectedActivityChange={updateSelected}
            onEnterSlot={ownerNodeId => {
              if (ownerNodeId !== owner.nodeId) setScopePath(path => [...path, ownerNodeId]);
              setSelectedNodeId(null);
            }}
            onReplaceSlotActivity={(ownerNodeId, targetSlot, _label, activity) => replaceInspectedSlotActivity(ownerNodeId, targetSlot, activity)}
          />
        </>
      )
    },
    ...contributedPanelTabs.filter(tab => tab.side === "right")
  ].sort(compareWorkflowPanelTabs);
  const activeLeftPanel = leftPanelTabs.find(tab => tab.id === activeLeftPanelId) ?? leftPanelTabs[0];
  const activeRightPanel = rightPanelTabs.find(tab => tab.id === activeRightPanelId) ?? rightPanelTabs[0];

  return <div ref={editorRef} className="ad-graph-editor" data-provider-diagnostic-focus-root>
    <GraphAuthoringWorkbench
      resourceKind="activity-definition-graph"
      className="ad-graph-workspace"
      layout={sidePanelLayout}
      palette={{
        ariaLabel: "Activities panel",
        tabLabel: "Activities panel tabs",
        tabs: leftPanelTabs,
        activeTabId: activeLeftPanel.id,
        onSelect: setActiveLeftPanelId
      }}
      canvas={<main className="wf-canvas-shell ad-graph-canvas" aria-label="Activity Graph designer">
        <nav className="ad-graph-breadcrumb" aria-label="Graph scope"><button type="button" onClick={() => { setScopePath([]); setSelectedNodeId(null); }} disabled={scopePath.length === 0}>Root</button>{scopePath.map((nodeId, index) => <span key={nodeId}><ChevronRight size={14} /><button type="button" onClick={() => { setScopePath(path => path.slice(0, index + 1)); setSelectedNodeId(null); }}>{nodeLabel(findNodeByPath(root, scopePath.slice(0, index + 1), catalogByVersion), catalogByVersion)}</button></span>)}</nav>
        <div className="ad-graph-scope-label"><span>{ownerCatalogItem ? getActivityDisplay(ownerCatalogItem) : "Root"}</span><strong>{slot?.label ?? "Leaf implementation"}</strong><div className="wf-canvas-tools" role="group" aria-label="Activity Graph canvas tools"><button type="button" className="wf-icon-button" aria-label="Undo Activity Graph edit" onClick={undo} disabled={readOnly || !canUndo}><Undo2 size={16} /></button><button type="button" className="wf-icon-button" aria-label="Redo Activity Graph edit" onClick={redo} disabled={readOnly || !canRedo}><Redo2 size={16} /></button><button type="button" className="wf-icon-button" aria-label="Auto-layout Activity Graph" onClick={autoLayoutCanvas} disabled={readOnly || nodes.length === 0}><Network size={16} /></button></div></div>
        {slot ? <GraphAuthoringCanvas
          canvasProps={{
            onDragOver: event => {
              if (!readOnly && draggedActivityRef.current) event.preventDefault();
            },
            onDrop: event => {
              event.preventDefault();
              if (!readOnly) addActivity();
            }
          }}
          slotNavigation={(ownerNodeId, ownerLabel, childSlot) => {
            if (readOnly) return;
            const child = activities.find(activity => activity.nodeId === ownerNodeId);
            if (!child || !getChildSlots(child, catalogByVersion).some(candidate => candidate.id === childSlot.id)) return;
            setScopePath(path => [...path, ownerNodeId]);
            setSelectedNodeId(null);
          }}
          reactFlowProps={{
            nodes: accessibleCanvas.nodes,
            edges: accessibleCanvas.edges,
            onNodesChange,
            onEdgesChange,
            onNodesDelete: deleteCanvasNodes,
            onEdgesDelete: deleteCanvasEdges,
            onConnect: slot.mode === "flowchart" ? connectCanvas : undefined,
            onPaneClick: () => setSelectedNodeId(null),
            onNodeClick: (_, node) => setSelectedNodeId(node.id),
            onNodeDragStop: (_, node) => {
              const nextNodes = nodes.map(current => current.id === node.id ? { ...current, position: node.position } : current);
              setNodes(nextNodes);
              commitCanvas(nextNodes);
            },
            nodesConnectable: !readOnly && slot.mode === "flowchart",
            nodesDraggable: !readOnly,
            deleteKeyCode: readOnly ? null : ["Backspace", "Delete"]
          }}
          overlays={nodes.length === 0 ? <div className="ad-graph-empty"><strong>{slot.label} is empty</strong><span>Choose an authorized activity from the palette to compose this graph.</span></div> : null}
        /> : <div className="ad-graph-empty"><strong>{root.activityVersionId ? "Leaf implementation" : "Choose a composition"}</strong><span>{root.activityVersionId ? "This activity does not own a child graph scope." : "Select Flowchart, Sequence, or BPMN to begin."}</span></div>}
      </main>}
      inspector={{
        ariaLabel: "Inspector panel",
        tabLabel: "Inspector panel tabs",
        tabs: rightPanelTabs,
        activeTabId: activeRightPanel.id,
        onSelect: setActiveRightPanelId
      }}
    />
  </div>;
}

function ActivityGraphOutputMappingEditor({
  output,
  mapping,
  expressionDescriptors,
  expressionDescriptorStatus,
  expressionEditors,
  root,
  readOnly,
  onChange
}: {
  output: NonNullable<StudioActivityDefinitionContract["outputs"]>[number];
  mapping: ActivityGraphOutputMapping | undefined;
  expressionDescriptors: StudioExpressionDescriptor[];
  expressionDescriptorStatus: "loading" | "ready" | "failed";
  expressionEditors: StudioExpressionEditorContribution[];
  root: ActivityNode;
  readOnly: boolean;
  onChange(source: ActivityGraphOutputMapping["source"] | null): void;
}) {
  const descriptor = useMemo<StudioActivityInputDescriptor>(() => ({
    name: output.name,
    referenceKey: output.referenceKey,
    typeName: readTypeAlias(output.type),
    displayName: output.displayName ?? output.name,
    description: output.description
  }), [output]);
  const available = useMemo(() => expressionDescriptors.flatMap(expression => {
    const candidateContext = {
      syntax: expression.type,
      surface: "inline" as const,
      descriptor,
      activity: root,
      expressionDescriptors,
      readOnly
    };
    const contribution = expressionEditors.find(item =>
      item.surfaces.inline && item.supports(candidateContext));
    return contribution ? [{ descriptor: expression, contribution }] : [];
  }), [descriptor, expressionDescriptors, expressionEditors, readOnly, root]);
  const currentSyntax = mapping?.source.syntax ?? "";
  const active = available.find(item => item.descriptor.type === currentSyntax);
  const Editor = active?.contribution.surfaces.inline;
  const context = {
    syntax: currentSyntax,
    surface: "inline" as const,
    descriptor,
    activity: root,
    expressionDescriptors,
    readOnly
  };
  const diagnostics = active?.contribution.diagnostics?.(context, mapping?.source.value) ?? [];

  const add = () => {
    const first = available[0];
    if (!first) return;
    const nextContext = { ...context, syntax: first.descriptor.type };
    onChange({
      syntax: first.descriptor.type,
      value: first.contribution.createDefaultValue?.(nextContext) ?? null
    });
  };
  const changeSyntax = (syntax: string) => {
    const next = available.find(item => item.descriptor.type === syntax);
    if (!next) return;
    const nextContext = { ...context, syntax };
    onChange({
      ...(mapping?.source ?? {}),
      syntax,
      value: next.contribution.createDefaultValue?.(nextContext) ?? null
    });
  };

  return (
    <article className="ad-graph-output-mapping">
      <header><div><strong>{output.displayName || output.name}</strong><small><code>{output.referenceKey}</code>{output.isRequired ? " · Required" : " · Optional"}</small></div>{mapping ? <button type="button" onClick={() => onChange(null)} disabled={readOnly}>Remove mapping</button> : null}</header>
      {!mapping ? <>
        {output.isRequired ? <span role="alert">A required public output needs exactly one boundary expression.</span> : <span className="wf-muted">No boundary expression is configured.</span>}
        <button type="button" onClick={add} disabled={readOnly || available.length === 0}>Add expression</button>
      </> : <>
        <label><span>Expression type</span><select value={currentSyntax} onChange={event => changeSyntax(event.target.value)} disabled={readOnly}><option value="">Select an expression type</option>{available.map(item => <option key={item.descriptor.type} value={item.descriptor.type}>{item.descriptor.displayName || item.descriptor.type}</option>)}</select></label>
        {Editor ? <Editor
          descriptor={descriptor}
          syntax={currentSyntax}
          value={mapping.source.value}
          disabled={readOnly}
          context={context}
          onChange={nextValue => onChange({ ...mapping.source, value: nextValue })}
        /> : <span role="alert">No shared expression editor supports <code>{currentSyntax || "this expression"}</code>. The existing value is preserved.</span>}
        {diagnostics.map((diagnostic, index) => <span key={`${diagnostic.code ?? "diagnostic"}:${index}`} role={diagnostic.severity === "error" ? "alert" : "status"}>{diagnostic.message}</span>)}
      </>}
      {expressionDescriptorStatus === "loading" ? <span role="status">Loading expression types…</span> : null}
      {expressionDescriptorStatus === "failed" ? <span role="alert">Expression types could not be loaded. Existing mappings remain unchanged.</span> : null}
    </article>
  );
}

function useActivityGraphExpressionDescriptors(context: StudioActivityDefinitionImplementationEditorProps["context"]) {
  const [retryVersion, setRetryVersion] = useState(0);
  const [state, setState] = useState<{
    descriptors: StudioExpressionDescriptor[];
    status: "loading" | "ready" | "failed";
  }>({ descriptors: [], status: "loading" });

  useEffect(() => {
    let active = true;
    setState(current => ({ descriptors: current.descriptors, status: "loading" }));
    void listExpressionDescriptors(context).then(
      descriptors => {
        if (active) setState({ descriptors, status: "ready" });
      },
      () => {
        if (active) setState(current => ({ descriptors: current.descriptors, status: "failed" }));
      }
    );
    return () => {
      active = false;
    };
  }, [context, retryVersion]);

  return {
    ...state,
    retry: () => setRetryVersion(version => version + 1)
  };
}

function toWorkflowInput(input: NonNullable<StudioActivityDefinitionContract["inputs"]>[number]): WorkflowInput {
  const type = isRecord(input.type) ? input.type : {};
  return {
    ...input,
    referenceKey: input.referenceKey,
    name: input.name,
    displayName: input.displayName || input.name,
    description: input.description ?? "",
    category: typeof input.category === "string" ? input.category : "",
    uiHint: typeof input.uiHint === "string" ? input.uiHint : "",
    storageDriverType: typeof input.storageDriverKey === "string" ? input.storageDriverKey : null,
    isNullable: input.isNullable === true,
    type: {
      alias: typeof type.alias === "string" ? type.alias : "Object",
      collectionKind: isCollectionKind(type.collectionKind) ? type.collectionKind : "Single"
    }
  };
}

function toVisibleGraphVariable(value: unknown) {
  if (!isRecord(value)) return [];
  const referenceKey = typeof value.referenceKey === "string" ? value.referenceKey.trim() : "";
  const name = typeof value.name === "string" ? value.name.trim() : "";
  if (!referenceKey || !name) return [];
  return [{ referenceKey, name, scopeId: "activity-graph", isWorkflowScope: true }];
}

function toVariableEditorRecords(values: unknown[]) {
  return values.map(value => {
    if (!isRecord(value)) return value;
    const initialValue = isRecord(value.initialValue) ? value.initialValue : null;
    return {
      ...value,
      storageDriverType: value.storageDriverKey ?? null,
      default: initialValue
        ? { expressionType: initialValue.syntax, value: initialValue.value }
        : null
    };
  });
}

function toWorkflowVariables(values: unknown[]): VariableDefinition[] {
  return toVariableEditorRecords(values).flatMap(value => {
    if (!isRecord(value) ||
        typeof value.referenceKey !== "string" ||
        typeof value.name !== "string" ||
        !isRecord(value.type) ||
        typeof value.type.alias !== "string") return [];
    return [{
      ...value,
      referenceKey: value.referenceKey,
      name: value.name,
      type: {
        ...value.type,
        alias: value.type.alias,
        collectionKind: isCollectionKind(value.type.collectionKind) ? value.type.collectionKind : "Single"
      }
    } as VariableDefinition];
  });
}

function fromVariableEditorRecords(values: unknown[]) {
  return values.map(value => {
    if (!isRecord(value)) return value;
    const { storageDriverType, default: defaultValue, ...rest } = value;
    const authoredDefault = isRecord(defaultValue)
      ? { syntax: defaultValue.expressionType, value: defaultValue.value }
      : null;
    return {
      ...rest,
      storageDriverKey: typeof storageDriverType === "string" ? storageDriverType : "",
      initialValue: authoredDefault
    };
  });
}

function mergeCanvasLayout(
  layout: ReturnType<typeof activityGraphLayoutToDesign>,
  nodes: Node<WorkflowNodeData>[]
) {
  const byNodeId = new Map(layout.map(record => [record.nodeId, record]));
  for (const node of nodes) {
    const previous = byNodeId.get(node.id);
    byNodeId.set(node.id, {
      nodeId: node.id,
      x: node.position.x,
      y: node.position.y,
      width: previous?.width,
      height: previous?.height,
      additionalProperties: previous?.additionalProperties
    });
  }
  return [...byNodeId.values()];
}

function reconcileGraphLayout(
  root: ActivityNode,
  catalog: Map<string, ActivityCatalogItem>,
  current: ReturnType<typeof activityGraphLayoutToDesign>
) {
  const activeNodeIds: string[] = [];
  const visit = (owner: ActivityNode) => {
    for (const child of getChildSlots(owner, catalog).flatMap(slot => slot.activities)) {
      activeNodeIds.push(child.nodeId);
      visit(child);
    }
  };
  visit(root);
  const currentByNodeId = new Map(current.map(record => [record.nodeId, record]));
  return activeNodeIds.map((nodeId, index) => currentByNodeId.get(nodeId) ?? {
    nodeId,
    x: 80 + index % 4 * 240,
    y: 80 + Math.floor(index / 4) * 160
  });
}

function readTypeAlias(value: unknown) {
  return isRecord(value) && typeof value.alias === "string" ? value.alias : "Object";
}

function isCollectionKind(value: unknown): value is WorkflowInput["type"]["collectionKind"] {
  return typeof value === "string" && ["Single", "Array", "List", "HashSet"].includes(value);
}

export function normalizeActivityGraphPayload(value: unknown): ActivityGraphPayload {
  const payload = value && typeof value === "object" ? value as Partial<ActivityGraphPayload> : {};
  const root: Partial<ActivityNode> = payload.rootActivity && typeof payload.rootActivity === "object" ? payload.rootActivity : {};
  return {
    rootActivity: {
      ...root,
      nodeId: typeof root.nodeId === "string" ? root.nodeId : "root",
      activityVersionId: typeof root.activityVersionId === "string" ? root.activityVersionId : "",
      inputs: Array.isArray(root.inputs) ? root.inputs : [],
      outputs: Array.isArray(root.outputs) ? root.outputs : [],
      structure: root.structure ?? null
    },
    variables: Array.isArray(payload.variables) ? payload.variables : [],
    outputMappings: normalizeOutputMappings(payload.outputMappings),
    outcomeMappings: normalizeOutcomeMappings(payload.outcomeMappings)
  };
}

function normalizeOutputMappings(value: unknown): ActivityGraphOutputMapping[] {
  if (!Array.isArray(value)) return [];
  return value.flatMap(item => {
    if (!isRecord(item) || typeof item.outputReferenceKey !== "string" || !isRecord(item.source)) return [];
    const outputReferenceKey = item.outputReferenceKey.trim();
    const syntax = typeof item.source.syntax === "string" ? item.source.syntax.trim() : "";
    if (!outputReferenceKey || !syntax || !("value" in item.source)) return [];
    return [{
      ...item,
      outputReferenceKey,
      source: { ...item.source, syntax, value: item.source.value }
    }];
  });
}

export function canAddActivityGraphOutcomeMapping(
  mappings: ActivityGraphOutcomeMapping[],
  sourceOutcomeReferenceKey: string
) {
  return !mappings.some(mapping => mapping.sourceOutcomeReferenceKey === sourceOutcomeReferenceKey);
}

function normalizePayload(value: unknown) {
  return normalizeActivityGraphPayload(value);
}

export function getActivityGraphOutcomeMappingOptions(
  root: ActivityNode,
  catalogItem: ActivityCatalogItem | undefined,
  contract: Pick<StudioActivityDefinitionContract, "outcomes">
) {
  return {
    sourceOutcomes: uniqueOutcomeChoices([
      ...readStableOutcomeChoices((root as { outcomes?: unknown }).outcomes),
      ...readStableFlowOutcomeChoices(catalogItem?.ports),
      ...readStableFlowOutcomeChoices(catalogItem?.outputs),
      ...readStableFlowOutcomeChoices(catalogItem?.designFacets)
    ]),
    boundaryOutcomes: contract.outcomes
      .filter(outcome => outcome.isEmitted && outcome.referenceKey.trim())
      .map(outcome => ({ referenceKey: outcome.referenceKey, name: outcome.name || outcome.referenceKey }))
  };
}

function normalizeOutcomeMappings(value: unknown): ActivityGraphOutcomeMapping[] {
  if (!Array.isArray(value)) return [];
  return value.flatMap(item => {
    if (!item || typeof item !== "object") return [];
    const mapping = item as Partial<ActivityGraphOutcomeMapping>;
    const sourceOutcomeReferenceKey = typeof mapping.sourceOutcomeReferenceKey === "string" ? mapping.sourceOutcomeReferenceKey.trim() : "";
    const boundaryOutcomeReferenceKey = typeof mapping.boundaryOutcomeReferenceKey === "string" ? mapping.boundaryOutcomeReferenceKey.trim() : "";
    return sourceOutcomeReferenceKey && boundaryOutcomeReferenceKey ? [{ sourceOutcomeReferenceKey, boundaryOutcomeReferenceKey }] : [];
  });
}

function readStableOutcomeChoices(value: unknown): ActivityGraphOutcomeChoice[] {
  if (!Array.isArray(value)) return [];
  return value.flatMap(item => {
    if (!item || typeof item !== "object") return [];
    const record = item as Record<string, unknown>;
    const referenceKey = typeof record.referenceKey === "string" ? record.referenceKey.trim() : "";
    if (!referenceKey) return [];
    const name = typeof record.displayName === "string" && record.displayName.trim()
      ? record.displayName
      : typeof record.name === "string" && record.name.trim()
        ? record.name
        : referenceKey;
    return [{ referenceKey, name }];
  });
}

function readStableFlowOutcomeChoices(value: unknown): ActivityGraphOutcomeChoice[] {
  if (!Array.isArray(value)) return [];
  const choices: ActivityGraphOutcomeChoice[] = [];
  for (const item of value) {
    if (!item || typeof item !== "object") continue;
    const record = item as Record<string, unknown>;
    if (Array.isArray(record.ports)) choices.push(...readStableFlowOutcomeChoices(record.ports));
    const type = typeof record.type === "string" ? record.type : typeof record.portType === "string" ? record.portType : "";
    const referenceKey = typeof record.referenceKey === "string" ? record.referenceKey.trim() : "";
    if (!["flow", "outcome"].includes(type.toLowerCase()) || !referenceKey) continue;
    const name = typeof record.displayName === "string" && record.displayName.trim()
      ? record.displayName
      : typeof record.name === "string" && record.name.trim()
        ? record.name
        : referenceKey;
    choices.push({ referenceKey, name });
  }
  return choices;
}

function uniqueOutcomeChoices(choices: ActivityGraphOutcomeChoice[]) {
  const byReferenceKey = new Map<string, ActivityGraphOutcomeChoice>();
  for (const choice of choices) {
    if (!byReferenceKey.has(choice.referenceKey)) byReferenceKey.set(choice.referenceKey, choice);
  }
  return [...byReferenceKey.values()];
}

function outcomeName(choices: ActivityGraphOutcomeChoice[], referenceKey: string) {
  return choices.find(choice => choice.referenceKey === referenceKey)?.name ?? referenceKey;
}

function findNodeByPath(root: ActivityNode, path: string[], catalog: Map<string, ActivityCatalogItem>) {
  let current: ActivityNode | undefined = root;
  for (const nodeId of path) {
    current = getChildSlots(current, catalog).flatMap(slot => slot.activities).find(activity => activity.nodeId === nodeId);
    if (!current) return null;
  }
  return current;
}

function findNodeFocusTarget(root: ActivityNode, nodeId: string, catalog: Map<string, ActivityCatalogItem>) {
  const visit = (owner: ActivityNode, ownerPath: string[]): { ownerPath: string[] } | null => {
    for (const child of getChildSlots(owner, catalog).flatMap(slot => slot.activities)) {
      if (child.nodeId === nodeId) return { ownerPath };
      const nested = visit(child, [...ownerPath, child.nodeId]);
      if (nested) return nested;
    }
    return null;
  };
  return visit(root, []);
}

function nodeIdAtJsonPointer(payload: ActivityGraphPayload, pointer: string | null | undefined) {
  if (!pointer?.startsWith("/")) return null;
  const segments = pointer.slice(1).split("/").map(segment => segment.replaceAll("~1", "/").replaceAll("~0", "~"));
  let current: unknown = payload;
  let closestNodeId: string | null = null;
  for (const segment of segments) {
    if (current && typeof current === "object" && !Array.isArray(current)) {
      const nodeId = (current as { nodeId?: unknown }).nodeId;
      if (typeof nodeId === "string" && nodeId) closestNodeId = nodeId;
      current = (current as Record<string, unknown>)[segment];
    } else if (Array.isArray(current) && /^\d+$/.test(segment)) {
      current = current[Number(segment)];
    } else {
      return closestNodeId;
    }
  }
  if (current && typeof current === "object" && !Array.isArray(current)) {
    const nodeId = (current as { nodeId?: unknown }).nodeId;
    if (typeof nodeId === "string" && nodeId) closestNodeId = nodeId;
  }
  return closestNodeId;
}

function unsupportedGraphFocus(): StudioActivityDiagnosticFocusResult {
  return {
    kind: "unsupported",
    announcement: "The Activity Graph editor retained this diagnostic but cannot focus its typed location."
  };
}

function nodeLabel(node: ActivityNode | null, catalog: Map<string, ActivityCatalogItem>) {
  const item = node ? catalog.get(node.activityVersionId) : undefined;
  return item ? getActivityDisplay(item) : node?.activityVersionId ?? "Unknown scope";
}

function supportsActivityGraphAuthoring(activity: ActivityCatalogItem) {
  return supportsActivityNode(createActivityNode(activity, "support-check"), activity);
}

function supportsActivityNode(node: ActivityNode, activity?: ActivityCatalogItem) {
  const slots = getChildSlots(node, activity);
  return slots.length <= 1;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
