import { useEffect, useMemo, useState } from "react";
import { ArrowLeft, ArrowRight, ChevronRight, CornerDownRight, Plus, Trash2 } from "lucide-react";
import type { StudioActivityDefinitionImplementationEditorProps } from "@elsa-workflows/studio-sdk";
import type { ActivityCatalogItem, ActivityNode } from "./workflowTypes";
import { createActivityNode, getActivityDisplay, getChildSlots, replaceSlotActivities, updateActivity } from "./workflowAdapter";
import { createNodeId } from "./workflow-editor/editorHelpers";
import { useWorkflowActivities } from "./api/activityDesign";

interface ActivityGraphPayload {
  rootActivity: ActivityNode;
  variables: unknown[];
  outputMappings: unknown[];
}

const emptyActivities: ActivityNode[] = [];

export function ActivityGraphImplementationEditor({ context, value, readOnly, onChange }: StudioActivityDefinitionImplementationEditorProps) {
  const catalogQuery = useWorkflowActivities(context);
  const catalog = useMemo(() => catalogQuery.data?.activities ?? [], [catalogQuery.data?.activities]);
  const payload = normalizePayload(value.payload);
  const root = payload.rootActivity;
  const catalogByVersion = useMemo(() => new Map(catalog.map(item => [item.activityVersionId, item])), [catalog]);
  const [scopePath, setScopePath] = useState<string[]>([]);
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);
  const [paletteActivityVersionId, setPaletteActivityVersionId] = useState("");
  const owner = findNodeByPath(root, scopePath, catalogByVersion) ?? root;
  const ownerCatalogItem = catalogByVersion.get(owner.activityVersionId);
  const ownerSupported = supportsActivityNode(owner, ownerCatalogItem);
  const slot = useMemo(() => ownerSupported ? getChildSlots(owner, catalogByVersion)[0] : undefined, [catalogByVersion, owner, ownerSupported]);
  const activities = slot?.activities ?? emptyActivities;
  const selected = activities.find(activity => activity.nodeId === selectedNodeId) ?? null;
  const selectedCatalogItem = selected ? catalogByVersion.get(selected.activityVersionId) : undefined;
  const [inputsDraft, setInputsDraft] = useState("[]");
  const [inputsError, setInputsError] = useState("");

  useEffect(() => {
    setInputsDraft(JSON.stringify(selected?.inputs ?? [], null, 2));
    setInputsError("");
  }, [selected]);

  useEffect(() => {
    if (!selectedNodeId || activities.some(activity => activity.nodeId === selectedNodeId)) return;
    setSelectedNodeId(null);
  }, [activities, selectedNodeId]);

  const commitRoot = (nextRoot: ActivityNode) => {
    onChange({
      payload: { ...payload, rootActivity: nextRoot },
      layout: createGraphLayout(nextRoot, catalogByVersion)
    });
  };

  const chooseRoot = (activityVersionId: string) => {
    const activity = catalogByVersion.get(activityVersionId);
    if (!activity || !supportsActivityGraphAuthoring(activity) || activityVersionId === root.activityVersionId) return;
    if (root.activityVersionId && !window.confirm("Replace the root activity and all nested graph content? This change will autosave.")) return;
    setScopePath([]);
    setSelectedNodeId(null);
    commitRoot(createActivityNode(activity, root.nodeId || "root"));
  };

  const updateOwnerActivities = (nextActivities: ActivityNode[]) => {
    if (!slot) return;
    const nextRoot = updateActivity(root, owner.nodeId, current => replaceSlotActivities(current, slot, nextActivities), catalogByVersion);
    commitRoot(nextRoot);
  };

  const addActivity = () => {
    const activity = catalogByVersion.get(paletteActivityVersionId);
    if (!activity || !slot) return;
    const next = createActivityNode(activity, createNodeId(activity));
    updateOwnerActivities([...activities, next]);
    setSelectedNodeId(next.nodeId);
  };

  const deleteSelected = () => {
    if (!selected) return;
    if (!window.confirm("Remove this activity and any nested graph content? This change will autosave.")) return;
    updateOwnerActivities(activities.filter(activity => activity.nodeId !== selected.nodeId));
    setSelectedNodeId(null);
  };

  const moveSelected = (offset: -1 | 1) => {
    if (!selected) return;
    const index = activities.findIndex(activity => activity.nodeId === selected.nodeId);
    const target = index + offset;
    if (index < 0 || target < 0 || target >= activities.length) return;
    const next = [...activities];
    [next[index], next[target]] = [next[target], next[index]];
    updateOwnerActivities(next);
  };

  const applyInputs = () => {
    if (!selected) return;
    try {
      const inputs = JSON.parse(inputsDraft) as unknown;
      if (!Array.isArray(inputs)) throw new Error("Inputs must be an array.");
      const nextRoot = updateActivity(root, selected.nodeId, current => ({ ...current, inputs }), catalogByVersion);
      setInputsError("");
      commitRoot(nextRoot);
    } catch {
      setInputsError("Inputs must be a valid JSON array. Existing provider state was not changed.");
    }
  };

  const availableActivities = catalog.filter(item => item.available !== false && supportsActivityGraphAuthoring(item));
  const selectedIndex = selected ? activities.findIndex(activity => activity.nodeId === selected.nodeId) : -1;
  const selectedSupported = selected ? supportsActivityNode(selected, selectedCatalogItem) : true;
  const selectedChildSlot = selected && selectedSupported ? getChildSlots(selected, catalogByVersion)[0] : undefined;

  return <div className="ad-graph-editor">
    <aside className="ad-graph-tools" aria-label="Activity Graph tools">
      <label><span>Root activity</span><select value={root.activityVersionId} onChange={event => chooseRoot(event.target.value)} disabled={readOnly || catalogQuery.isPending}><option value="">Select an activity</option>{root.activityVersionId && !availableActivities.some(item => item.activityVersionId === root.activityVersionId) ? <option value={root.activityVersionId} disabled>Existing unsupported structure</option> : null}{availableActivities.map(item => <option key={item.activityVersionId} value={item.activityVersionId}>{getActivityDisplay(item)} · {item.version}</option>)}</select></label>
      {catalogQuery.isPending ? <span role="status">Loading the authorized activity catalog…</span> : null}
      {catalogQuery.isError ? <span role="alert">The activity catalog is unavailable. Existing graph state remains unchanged.</span> : null}
      {slot ? <div className="ad-graph-add"><label><span>Add to {slot.label}</span><select aria-label={`Activity for ${slot.label}`} value={paletteActivityVersionId} onChange={event => setPaletteActivityVersionId(event.target.value)} disabled={readOnly}><option value="">Choose an activity</option>{availableActivities.map(item => <option key={item.activityVersionId} value={item.activityVersionId}>{getActivityDisplay(item)}</option>)}</select></label><button type="button" onClick={addActivity} disabled={readOnly || !paletteActivityVersionId}><Plus size={15} /> Add activity</button></div> : root.activityVersionId && ownerSupported ? <p>The selected root is a leaf activity. Choose a supported single-slot sequence container to compose multiple activities.</p> : null}
      {!ownerSupported ? <span role="alert">This existing flowchart or multi-slot structure is outside this editor's authoring contract. Its opaque state is preserved; choose another root only if you intend to replace the whole graph.</span> : null}
      {selected ? <section className="ad-graph-inspector" aria-label="Selected activity configuration"><h3>{selectedCatalogItem ? getActivityDisplay(selectedCatalogItem) : selected.activityVersionId}</h3><div className="ad-graph-node-actions"><button type="button" aria-label="Move activity left" onClick={() => moveSelected(-1)} disabled={readOnly || selectedIndex <= 0}><ArrowLeft size={15} /></button><button type="button" aria-label="Move activity right" onClick={() => moveSelected(1)} disabled={readOnly || selectedIndex < 0 || selectedIndex === activities.length - 1}><ArrowRight size={15} /></button><button type="button" onClick={deleteSelected} disabled={readOnly}><Trash2 size={15} /> Remove</button></div>{!selectedSupported ? <span role="alert">This existing nested structure is preserved but cannot be structurally edited here.</span> : null}{selectedChildSlot ? <button type="button" onClick={() => { setScopePath(path => [...path, selected.nodeId]); setSelectedNodeId(null); }} disabled={readOnly}><CornerDownRight size={15} /> Edit {selectedChildSlot.label}</button> : null}<label><span>Inputs (JSON array)</span><textarea aria-label="Activity inputs JSON" rows={8} value={inputsDraft} onChange={event => setInputsDraft(event.target.value)} readOnly={readOnly} /></label><button type="button" onClick={applyInputs} disabled={readOnly}>Apply inputs</button>{inputsError ? <span role="alert">{inputsError}</span> : null}</section> : null}
      <p>The Activity Graph provider owns composition, structure, and input configuration. Studio persists the full opaque manifest through exact-revision autosave.</p>
    </aside>
    <section className="ad-graph-canvas" aria-label="Activity Graph canvas">
      <nav className="ad-graph-breadcrumb" aria-label="Graph scope"><button type="button" onClick={() => { setScopePath([]); setSelectedNodeId(null); }} disabled={scopePath.length === 0}>Root</button>{scopePath.map((nodeId, index) => <span key={nodeId}><ChevronRight size={14} /><button type="button" onClick={() => { setScopePath(path => path.slice(0, index + 1)); setSelectedNodeId(null); }}>{nodeLabel(findNodeByPath(root, scopePath.slice(0, index + 1), catalogByVersion), catalogByVersion)}</button></span>)}</nav>
      <div className="ad-graph-scope-label"><span>{ownerCatalogItem ? getActivityDisplay(ownerCatalogItem) : "Root"}</span><strong>{slot?.label ?? "Leaf implementation"}</strong></div>
      {slot ? activities.length ? <div className="ad-graph-flow">{activities.map((activity, index) => { const item = catalogByVersion.get(activity.activityVersionId); return <div className="ad-graph-flow-step" key={activity.nodeId}>{index ? <ChevronRight className="ad-graph-connector" aria-hidden /> : null}<button type="button" className={`ad-graph-node ${selectedNodeId === activity.nodeId ? "is-selected" : ""}`} onClick={() => setSelectedNodeId(activity.nodeId)} aria-pressed={selectedNodeId === activity.nodeId}><span>Step {index + 1}</span><strong>{item ? getActivityDisplay(item) : activity.activityVersionId}</strong><small>{item ? `${item.activityTypeKey} · ${item.version}` : activity.nodeId}</small>{getChildSlots(activity, catalogByVersion).length ? <em>Contains child activities</em> : null}</button></div>; })}</div> : <div className="ad-graph-empty"><strong>{slot.label} is empty</strong><span>Choose an authorized catalog activity and add it to compose this reusable graph.</span></div> : <div className={`ad-graph-node ${root.activityVersionId ? "is-configured" : "is-placeholder"}`}><span>Root</span><strong>{ownerCatalogItem ? getActivityDisplay(ownerCatalogItem) : "Choose a root activity"}</strong><small>{ownerCatalogItem ? `${ownerCatalogItem.activityTypeKey} · ${ownerCatalogItem.version}` : "A graph can be saved while incomplete; validation identifies what must be resolved."}</small></div>}
    </section>
  </div>;
}

function normalizePayload(value: unknown): ActivityGraphPayload {
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
    outputMappings: Array.isArray(payload.outputMappings) ? payload.outputMappings : []
  };
}

function findNodeByPath(root: ActivityNode, path: string[], catalog: Map<string, ActivityCatalogItem>) {
  let current: ActivityNode | undefined = root;
  for (const nodeId of path) {
    current = getChildSlots(current, catalog).flatMap(slot => slot.activities).find(activity => activity.nodeId === nodeId);
    if (!current) return null;
  }
  return current;
}

function nodeLabel(node: ActivityNode | null, catalog: Map<string, ActivityCatalogItem>) {
  const item = node ? catalog.get(node.activityVersionId) : undefined;
  return item ? getActivityDisplay(item) : node?.activityVersionId ?? "Unknown scope";
}

function createGraphLayout(root: ActivityNode, catalog: Map<string, ActivityCatalogItem>) {
  const records: Array<{ nodeId: string; data: { x: number; y: number } }> = [];
  const visit = (node: ActivityNode, depth: number) => {
    getChildSlots(node, catalog).forEach(slot => slot.activities.forEach((child, index) => {
      records.push({ nodeId: child.nodeId, data: { x: 80 + index * 260, y: 72 + depth * 180 } });
      visit(child, depth + 1);
    }));
  };
  records.push({ nodeId: root.nodeId, data: { x: 80, y: 72 } });
  visit(root, 1);
  return records;
}

function supportsActivityGraphAuthoring(activity: ActivityCatalogItem) {
  return supportsActivityNode(createActivityNode(activity, "support-check"), activity);
}

function supportsActivityNode(node: ActivityNode, activity?: ActivityCatalogItem) {
  const slots = getChildSlots(node, activity);
  return slots.length === 0 || slots.length === 1 && slots[0].mode !== "flowchart";
}
