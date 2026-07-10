import type { Edge, Node, XYPosition } from "@xyflow/react";
import type { ActivityCatalogItem, ActivityExecutionStateSummary, ActivityNode, ActivityNodeStructure, DesignMetadataRecord, IncidentStateSummary } from "./workflowTypes";

export const sequenceStructureKind = "elsa.sequence.structure";
export const flowchartStructureKind = "elsa.flowchart.structure";

export interface WorkflowNodeData extends Record<string, unknown> {
  label: string;
  activityVersionId: string;
  activityTypeKey?: string;
  category?: string;
  executionType?: string;
  icon?: WorkflowNodeIcon;
  childSlots: ChildSlot[];
  acceptsInbound: boolean;
  sourcePorts: WorkflowPortDescriptor[];
  suppressFlowPorts?: boolean;
  runtime?: WorkflowRuntimeNodeOverlay;
  // Set by the Executable Inspector for activity-catalog misses: the node renders as an honest
  // ghost ("not available in this environment") instead of pretending the activity resolves.
  ghost?: boolean;
  onEnterSlot?(slot: ChildSlot): void;
}

export type WorkflowNodeIcon = "activity" | "flowchart" | "sequence" | "terminal" | "runtime" | "trigger";

export interface WorkflowPortDescriptor {
  name: string;
  displayName: string;
}

export interface WorkflowEdgeData extends Record<string, unknown> {
  vertices?: XYPosition[];
}

export interface WorkflowRuntimeNodeOverlay {
  status?: string;
  subStatus?: string | null;
  activityExecutionId?: string;
  faultCount: number;
  incidentCount: number;
  hasBlockingIncident: boolean;
  selected: boolean;
}

export interface ChildSlot {
  id: string;
  label: string;
  property: string;
  cardinality: "single" | "many";
  mode: "sequence" | "flowchart" | "generic";
  activities: ActivityNode[];
  collectionProperty?: string;
  childProperty?: string;
  labelProperty?: string;
  slotNameTemplate?: string;
  collectionIndex?: number;
  collectionItemLabel?: string;
}

export interface StructureDesignFacet {
  kind: string;
  schemaVersion: string;
  payload: StructureDesignFacetPayload;
}

export interface StructureDesignFacetPayload {
  mode: ChildSlot["mode"];
  supportsScopedVariables: boolean;
  slots: StructureDesignSlotDescriptor[];
  initialPayload: Record<string, unknown>;
}

export interface StructureDesignSlotDescriptor {
  name: string;
  property: string;
  displayName: string;
  cardinality: ChildSlot["cardinality"];
  collectionProperty?: string;
  childProperty?: string;
  labelProperty?: string;
  slotNameTemplate?: string;
}

export type ActivityCatalogLookup = ActivityCatalogItem | ActivityCatalogItem[] | Map<string, ActivityCatalogItem> | null | undefined;

export interface CanvasScope {
  owner: ActivityNode;
  slot: ChildSlot;
}

// A ScopeFrame {ownerNodeId, slotId, label} means: descend into the container activity `ownerNodeId`
// (which must live in *some* slot of the CURRENT owner), then view THAT container's slot with id
// `slotId`. Intermediate frames only descend; the *last* frame's `slotId` names the slot the canvas
// renders. This makes non-primary slots (e.g. a ForEach body or a Switch case) addressable, and
// matches what workflowDocument.enterSlot / InspectorPanel emit: enterSlot(owner, slotId) records
// {ownerNodeId: owner.nodeId, slotId: <owner's own slot id>}.
export interface ScopeFrame {
  ownerNodeId: string;
  slotId: string;
  label: string;
}

export type WorkflowDesignerSupport = "flowchart" | "sequence" | "unsupported";

// Descends through the frame chain to the container the last frame names. Each frame's `ownerNodeId`
// must be found among ANY slot of the current owner; we descend into it and continue. Returns null the
// moment a hop can't be resolved (stale/broken frame), so callers can fall back to the root scope.
export function resolveScopeOwner(root: ActivityNode | null | undefined, frames: ScopeFrame[], catalog?: ActivityCatalogLookup): ActivityNode | null {
  if (!root) return null;

  let owner = root;
  for (const frame of frames) {
    const nextOwner = findChildInAnySlot(owner, frame.ownerNodeId, catalog);
    if (!nextOwner) return null;
    owner = nextOwner;
  }

  return owner;
}

// One containment step on the way to a target node: `child` lives in `slot` of `parent`.
interface ScopeHop {
  parent: ActivityNode;
  slot: ChildSlot;
  child: ActivityNode;
}

// Depth-first locates `nodeId`, returning the containment hops from `root` down to the container whose
// `targetSlot` directly holds it (zero hops when a root slot holds it). Null when absent from the tree.
function findContainmentChain(
  root: ActivityNode,
  nodeId: string,
  catalog?: ActivityCatalogLookup
): { hops: ScopeHop[]; targetSlot: ChildSlot } | null {
  const visit = (owner: ActivityNode, hops: ScopeHop[]): { hops: ScopeHop[]; targetSlot: ChildSlot } | null => {
    const slots = getChildSlots(owner, catalog);

    for (const slot of slots) {
      if (slot.activities.some(activity => activity.nodeId === nodeId)) return { hops, targetSlot: slot };
    }

    for (const slot of slots) {
      for (const child of slot.activities) {
        const found = visit(child, [...hops, { parent: owner, slot, child }]);
        if (found) return found;
      }
    }

    return null;
  };

  return visit(root, []);
}

// The descend policy shared by slot entry (planSlotNavigation) and diagnostics navigation
// (findNodeScopePath), so the two can never disagree: a single-cardinality slot whose only child is
// itself a canvas container (non-generic primary slot) is entered by descending THROUGH the child.
function resolveDescendThroughChild(slot: ChildSlot, catalog?: ActivityCatalogLookup): { child: ActivityNode; childPrimary: ChildSlot } | null {
  if (slot.cardinality !== "single" || slot.activities.length !== 1) return null;
  const child = slot.activities[0];
  const childPrimary = getChildSlots(child, catalog)[0];
  return childPrimary && childPrimary.mode !== "generic" ? { child, childPrimary } : null;
}

// Finds the scope frames that bring `nodeId` into view, so a diagnostic can navigate the designer to a
// node anywhere in the tree. `labelFor` supplies the display name used in breadcrumb labels. The frames
// follow planSlotNavigation's conventions, so the landing breadcrumb reads exactly as if the author had
// navigated there through slot entry: a descend-through entry contributes a hidden empty-label frame on
// the slot owner, and each visible frame carries an "Owner / Slot" label. With the target ScopeFrame
// semantics a node in ANY slot is landable: the returned path's last frame names the slot that directly
// contains `nodeId`. Contract: resolveScope(root, path).slot.activities contains `nodeId` (an empty
// path lands `nodeId` in the root owner's primary slot). Returns null when the node is not present in
// the tree at all.
export function findNodeScopePath(
  root: ActivityNode | null | undefined,
  nodeId: string,
  labelFor: (activity: ActivityNode) => string = activity => activity.nodeId,
  catalog?: ActivityCatalogLookup
): ScopeFrame[] | null {
  if (!root) return null;
  // The root scope itself is reached with no frames.
  if (root.nodeId === nodeId) return [];

  const located = findContainmentChain(root, nodeId, catalog);
  if (!located) return null;
  const { hops, targetSlot } = located;

  // A direct child of the root: every frame descends INTO a child (the root is the implicit starting
  // owner and never appears as a frame's ownerNodeId), so only the root's primary slot is reachable —
  // the empty path lands it via resolveScope's slots[0] fallback. A node in the root's non-primary
  // slot has no navigable path; treat it as not found.
  if (hops.length === 0) {
    return targetSlot.id === getChildSlots(root, catalog)[0]?.id ? [] : null;
  }

  // Replay the slot entries a user would perform along the chain, consuming hops greedily: an entry
  // that descends THROUGH its single container child consumes TWO hops and emits the hidden owner
  // frame + visible container frame pair planSlotNavigation produces; any other entry consumes one hop
  // and emits one visible frame. planSlotNavigation descends one level per entry, so a frame is hidden
  // only as the first half of its own pair — a chained descend-through further down must not blank the
  // previous pair's visible crumb.
  const frames: ScopeFrame[] = [];
  for (let index = 0; index < hops.length;) {
    const hop = hops[index];
    const nextHop = hops[index + 1];
    // Each visible frame views the slot the chain continues through; the last frame views the slot
    // that directly holds the target, honouring the resolveScope contract.
    if (nextHop && resolveDescendThroughChild(nextHop.slot, catalog)) {
      const viewedSlot = hops[index + 2]?.slot ?? targetSlot;
      frames.push({ ownerNodeId: hop.child.nodeId, slotId: nextHop.slot.id, label: "" });
      frames.push({
        ownerNodeId: nextHop.child.nodeId,
        slotId: viewedSlot.id,
        // Descending lands on the child's primary slot, labeled after the slot that was entered
        // ("For Each / Body"); a non-primary viewed slot is the retarget case, labeled after the
        // container itself.
        label: viewedSlot.id === getChildSlots(nextHop.child, catalog)[0]?.id
          ? slotCrumbLabel(labelFor(nextHop.parent), nextHop.slot)
          : slotCrumbLabel(labelFor(nextHop.child), viewedSlot)
      });
      index += 2;
    } else {
      const viewedSlot = nextHop?.slot ?? targetSlot;
      frames.push({
        ownerNodeId: hop.child.nodeId,
        slotId: viewedSlot.id,
        label: slotCrumbLabel(labelFor(hop.child), viewedSlot)
      });
      index += 1;
    }
  }

  return frames;
}

export interface SlotNavigationPlan {
  frames: ScopeFrame[];
  selectedNodeId: string | null;
}

// Computes the frame path for entering `slot` on the activity `ownerNodeId`, given the live `frames`
// (whose resolved owner is `scopeOwner`). Returns null when the slot has no addressable path (a
// non-primary slot of the root scope owner — every frame descends INTO a child, so the root's own
// non-primary slots are unreachable; see findNodeScopePath).
//
// Policy:
// - A single-cardinality slot whose only child is itself a canvas container (primary slot in
//   flowchart/sequence mode) is entered by descending THROUGH the child: the canvas shows the
//   container's contents rather than a one-node canvas holding the container. The descent's
//   intermediate frame gets an empty label (the breadcrumb hides it) so the hop reads as one crumb,
//   carried by the leaf frame.
// - A single slot holding a leaf is entered with the leaf pre-selected.
// - Entering a slot of the CURRENT scope owner retargets the last frame instead of appending one
//   (an owner is never a child of itself, so an appended frame would not resolve).
export function planSlotNavigation(
  frames: ScopeFrame[],
  scopeOwner: ActivityNode | null,
  ownerNodeId: string,
  slot: ChildSlot,
  label: string,
  catalog?: ActivityCatalogLookup
): SlotNavigationPlan | null {
  const child = slot.cardinality === "single" && slot.activities.length === 1 ? slot.activities[0] : null;
  const descend = resolveDescendThroughChild(slot, catalog);
  const ownerIsScopeOwner = scopeOwner?.nodeId === ownerNodeId;

  if (ownerIsScopeOwner && frames.length === 0) {
    // The root scope owner. Its primary slot IS the root canvas (frames=[]); other slots are not
    // addressable as frames.
    if (scopeOwner && getChildSlots(scopeOwner, catalog)[0]?.id !== slot.id) return null;
    if (descend) {
      return { frames: [{ ownerNodeId: descend.child.nodeId, slotId: descend.childPrimary.id, label }], selectedNodeId: null };
    }
    return { frames: [], selectedNodeId: child?.nodeId ?? null };
  }

  const base = ownerIsScopeOwner ? frames.slice(0, -1) : frames;
  if (descend) {
    return {
      frames: [
        ...base,
        { ownerNodeId, slotId: slot.id, label: "" },
        { ownerNodeId: descend.child.nodeId, slotId: descend.childPrimary.id, label }
      ],
      selectedNodeId: null
    };
  }

  return {
    frames: [...base, { ownerNodeId, slotId: slot.id, label }],
    selectedNodeId: child?.nodeId ?? null
  };
}

// Breadcrumb label for entering `slot` on the activity labelled `ownerLabel` — the one format every
// slot-entry surface (editor canvas badges, inspector slot list, run viewer) shares.
export function slotCrumbLabel(ownerLabel: string, slot: ChildSlot): string {
  return `${ownerLabel} / ${slot.label}`;
}

// Picks the slot the last frame named on `owner`; falls back to the primary slot when there are no
// frames (root scope). Returns null when the named slot no longer exists (stale frame after an edit).
function resolveScopeSlot(owner: ActivityNode, frames: ScopeFrame[], catalog?: ActivityCatalogLookup): ChildSlot | null {
  const slots = getChildSlots(owner, catalog);
  const lastFrame = frames.at(-1);
  if (!lastFrame) return slots[0] ?? null;
  return slots.find(slot => slot.id === lastFrame.slotId) ?? null;
}

export function resolveScope(root: ActivityNode | null | undefined, frames: ScopeFrame[], catalog?: ActivityCatalogLookup): CanvasScope | null {
  const owner = resolveScopeOwner(root, frames, catalog);
  if (!owner) return null;

  const slot = resolveScopeSlot(owner, frames, catalog);
  if (!slot) return null;

  return { owner, slot };
}

// Finds the child node `nodeId` in whichever of `owner`'s slots contains it, along with that slot (so a
// caller can rewrite it). Returns null when no slot holds the child.
function locateChildSlot(owner: ActivityNode, nodeId: string, catalog?: ActivityCatalogLookup): { slot: ChildSlot; child: ActivityNode } | null {
  for (const slot of getChildSlots(owner, catalog)) {
    const child = slot.activities.find(activity => activity.nodeId === nodeId);
    if (child) return { slot, child };
  }
  return null;
}

function findChildInAnySlot(owner: ActivityNode, nodeId: string, catalog?: ActivityCatalogLookup): ActivityNode | null {
  return locateChildSlot(owner, nodeId, catalog)?.child ?? null;
}

export function getChildSlots(activity: ActivityNode, catalog?: ActivityCatalogLookup): ChildSlot[] {
  const structure = activity.structure;
  if (!structure || !structure.payload || typeof structure.payload !== "object") return [];

  const payload = structure.payload;
  const structureFacet = readStructureDesignFacet(resolveCatalogItem(activity, catalog));
  if (structureFacet?.kind === structure.kind) return getFacetChildSlots(structure, structureFacet);

  const mode = getStructureMode(structure);
  const activities = readActivityArray(payload.activities);
  if (activities) {
    return [{
      id: `${structure.kind}:activities`,
      label: getDefaultSlotLabel(structure),
      property: "activities",
      cardinality: "many",
      mode,
      activities
    }];
  }

  return Object.entries(payload)
    .filter(([, value]) => readActivityArray(value) || isActivityNode(value))
    .map(([property, value]) => ({
      id: `${structure.kind}:${property}`,
      label: toDisplayLabel(property),
      property,
      cardinality: readActivityArray(value) ? "many" as const : "single" as const,
      mode: "generic",
      activities: readActivityArray(value) ?? (isActivityNode(value) ? [value] : [])
    }));
}

export function readStructureDesignFacet(activity: ActivityCatalogItem | null | undefined): StructureDesignFacet | null {
  for (const facet of activity?.designFacets ?? []) {
    if (!isRecord(facet)) continue;
    const kind = typeof facet.kind === "string" ? facet.kind : "";
    const schemaVersion = typeof facet.schemaVersion === "string" ? facet.schemaVersion : "";
    if (!kind || !schemaVersion || !isRecord(facet.payload)) continue;

    const payload = readStructureDesignFacetPayload(facet.payload);
    if (payload) return { kind, schemaVersion, payload };
  }

  return null;
}

function readStructureDesignFacetPayload(payload: Record<string, unknown>): StructureDesignFacetPayload | null {
  const mode = payload.mode;
  if (mode !== "sequence" && mode !== "flowchart" && mode !== "generic") return null;
  if (typeof payload.supportsScopedVariables !== "boolean") return null;
  if (!Array.isArray(payload.slots) || !isRecord(payload.initialPayload)) return null;

  const slots = payload.slots.map(readStructureDesignSlotDescriptor).filter(slot => slot !== null);
  return {
    mode,
    supportsScopedVariables: payload.supportsScopedVariables,
    slots,
    initialPayload: payload.initialPayload
  };
}

function readStructureDesignSlotDescriptor(value: unknown): StructureDesignSlotDescriptor | null {
  if (!isRecord(value)) return null;
  const name = typeof value.name === "string" ? value.name : "";
  const property = typeof value.property === "string" ? value.property : "";
  const displayName = typeof value.displayName === "string" ? value.displayName : "";
  const cardinality = value.cardinality;
  if (!name || !property || !displayName || (cardinality !== "single" && cardinality !== "many")) return null;

  return {
    name,
    property,
    displayName,
    cardinality,
    collectionProperty: readOptionalString(value.collectionProperty),
    childProperty: readOptionalString(value.childProperty),
    labelProperty: readOptionalString(value.labelProperty),
    slotNameTemplate: readOptionalString(value.slotNameTemplate)
  };
}

function getFacetChildSlots(structure: ActivityNodeStructure, facet: StructureDesignFacet): ChildSlot[] {
  return facet.payload.slots.flatMap(descriptor => {
    if (descriptor.collectionProperty && descriptor.childProperty) {
      // Capture the narrowed values into locals so the `string` narrowing survives into the
      // nested flatMap callback (TS drops property-access narrowing across function boundaries).
      const childProperty = descriptor.childProperty;
      const collection = structure.payload[descriptor.collectionProperty];
      if (!Array.isArray(collection)) return [];

      return collection.flatMap((item, index) => {
        if (!isRecord(item)) return [];
        const labelValue = descriptor.labelProperty ? readOptionalString(item[descriptor.labelProperty]) : undefined;
        return [createChildSlot(structure.kind, descriptor, item[childProperty], facet.payload.mode, index, labelValue)];
      });
    }

    return [createChildSlot(structure.kind, descriptor, structure.payload[descriptor.property], facet.payload.mode)];
  });
}

function createChildSlot(
  structureKind: string,
  descriptor: StructureDesignSlotDescriptor,
  value: unknown,
  mode: ChildSlot["mode"],
  collectionIndex?: number,
  collectionItemLabel?: string
): ChildSlot {
  const collectionSuffix = collectionIndex === undefined
    ? ""
    : `:${descriptor.collectionProperty}:${descriptor.childProperty}:${collectionIndex}`;

  return {
    id: `${structureKind}:${descriptor.property}${collectionSuffix}`,
    label: collectionIndex === undefined ? descriptor.displayName : formatCollectionSlotLabel(descriptor, collectionIndex, collectionItemLabel),
    property: descriptor.property,
    cardinality: descriptor.cardinality,
    mode,
    activities: readSlotActivities(value, descriptor.cardinality),
    collectionProperty: descriptor.collectionProperty,
    childProperty: descriptor.childProperty,
    labelProperty: descriptor.labelProperty,
    slotNameTemplate: descriptor.slotNameTemplate,
    collectionIndex,
    collectionItemLabel
  };
}

function readSlotActivities(value: unknown, cardinality: ChildSlot["cardinality"]) {
  if (cardinality === "many") return readActivityArray(value) ?? [];
  return isActivityNode(value) ? [value] : [];
}

function formatCollectionSlotLabel(descriptor: StructureDesignSlotDescriptor, index: number, labelValue?: string) {
  if (descriptor.slotNameTemplate) {
    return descriptor.slotNameTemplate
      .replaceAll("{name}", descriptor.name)
      .replaceAll("{displayName}", descriptor.displayName)
      .replaceAll("{label}", labelValue ?? String(index + 1))
      .replaceAll("{index}", String(index + 1));
  }

  return labelValue ? `${descriptor.displayName}: ${labelValue}` : `${descriptor.displayName} ${index + 1}`;
}

function resolveCatalogItem(activity: ActivityNode, catalog: ActivityCatalogLookup): ActivityCatalogItem | undefined {
  if (!catalog) return undefined;
  if (catalog instanceof Map) return catalog.get(activity.activityVersionId);
  if (Array.isArray(catalog)) return catalog.find(item => item.activityVersionId === activity.activityVersionId);
  return catalog.activityVersionId === activity.activityVersionId ? catalog : undefined;
}

function findCollectionItemIndex(collection: unknown[], slot: ChildSlot) {
  if (slot.labelProperty && slot.collectionItemLabel) {
    const matches = collection
      .map((item, index) => isRecord(item) && readOptionalString(item[slot.labelProperty!]) === slot.collectionItemLabel ? index : -1)
      .filter(index => index >= 0);
    if (matches.length === 1) return matches[0];
  }

  return typeof slot.collectionIndex === "number" && slot.collectionIndex >= 0 && slot.collectionIndex < collection.length
    ? slot.collectionIndex
    : -1;
}

export function buildCanvas(scope: CanvasScope, catalog: ActivityCatalogItem[], layout: DesignMetadataRecord[]) {
  const catalogByVersion = new Map(catalog.map(activity => [activity.activityVersionId, activity]));
  const layoutByNodeId = new Map(layout.map(record => [record.nodeId, record]));
  const nodes: Node<WorkflowNodeData>[] = scope.slot.activities.map((activity, index) => {
    const catalogItem = catalogByVersion.get(activity.activityVersionId);
    const position = layoutByNodeId.get(activity.nodeId) ?? defaultPosition(scope.slot.mode, index);
    return createWorkflowNode(activity, catalogItem, { x: position.x, y: position.y });
  });

  return {
    nodes,
    edges: scope.slot.mode === "flowchart" ? flowchartEdges(scope.owner) : buildEdges(scope.slot, nodes)
  };
}

export function buildUnsupportedActivityCanvas(activity: ActivityNode, catalog: ActivityCatalogItem[], layout: DesignMetadataRecord[]) {
  const catalogItem = catalog.find(candidate => candidate.activityVersionId === activity.activityVersionId);
  const position = layout.find(record => record.nodeId === activity.nodeId) ?? { x: 0, y: 0 };

  return {
    nodes: [createWorkflowNode(activity, catalogItem, { x: position.x, y: position.y }, {
      connectable: false,
      deletable: false,
      draggable: false,
      suppressFlowPorts: true
    })],
    edges: [] satisfies Edge[]
  };
}

export function applyRuntimeOverlays(
  nodes: Node<WorkflowNodeData>[],
  activities: ActivityExecutionStateSummary[],
  incidents: IncidentStateSummary[],
  selectedEvidenceId: string | null = null
) {
  const activityByExecutionId = new Map(activities.map(activity => [activity.activityExecutionId, activity]));
  const activitiesByNodeId = groupBy(activities, activity => activity.authoredActivityId || activity.executableNodeId);
  const incidentsByNodeId = groupBy(incidents, incident => {
    if (incident.executableNodeId) return incident.executableNodeId;
    return incident.activityExecutionId ? activityByExecutionId.get(incident.activityExecutionId)?.authoredActivityId ?? "" : "";
  });

  return nodes.map(node => {
    const nodeActivities = activitiesByNodeId.get(node.id) ?? [];
    const nodeIncidents = incidentsByNodeId.get(node.id) ?? [];
    if (nodeActivities.length === 0 && nodeIncidents.length === 0) return node;

    const latestActivity = latestActivityExecution(nodeActivities);
    const selected = selectedEvidenceId === node.id ||
      nodeActivities.some(activity => activity.activityExecutionId === selectedEvidenceId) ||
      nodeIncidents.some(incident => incident.incidentId === selectedEvidenceId);
    const runtime: WorkflowRuntimeNodeOverlay = {
      status: latestActivity?.status,
      subStatus: latestActivity?.subStatus,
      activityExecutionId: latestActivity?.activityExecutionId,
      faultCount: nodeActivities.reduce((sum, activity) => sum + activity.faultCount + activity.aggregateFaultCount, 0),
      incidentCount: nodeIncidents.length,
      hasBlockingIncident: nodeIncidents.some(incident => incident.isBlocking),
      selected
    };

    return {
      ...node,
      selected,
      className: selected ? "wf-runtime-node-selected" : node.className,
      data: {
        ...node.data,
        runtime
      }
    };
  });
}

export function getActivityDesignerSupport(activity: ActivityNode | null | undefined, catalogItem?: ActivityCatalogItem): WorkflowDesignerSupport {
  if (activity?.structure?.kind === flowchartStructureKind || isFlowchartCatalogItem(catalogItem)) return "flowchart";
  if (activity?.structure?.kind === sequenceStructureKind || isSequenceCatalogItem(catalogItem)) return "sequence";
  if (activity) {
    const primarySlot = getChildSlots(activity, catalogItem)[0];
    if (primarySlot) return primarySlot.mode === "flowchart" ? "flowchart" : "sequence";
  }
  return "unsupported";
}

// Writes `activities` into the slot the frame chain resolves to. frames=[] targets the root owner's
// primary slot (used by the wrap-root recipe). Otherwise the LAST frame names the slot on the resolved
// owner; intermediate frames only locate the child in whichever slot holds it (matching
// resolveScopeOwner), so the write lands in the same slot resolveScope surfaces.
export function updateScopeActivities(root: ActivityNode, frames: ScopeFrame[], activities: ActivityNode[], catalog?: ActivityCatalogLookup): ActivityNode {
  return updateScopeAtFrames(root, frames, catalog, owner => {
    const slot = resolveScopeSlot(owner, frames, catalog);
    return slot ? replaceSlotActivities(owner, slot, activities) : owner;
  });
}

// Replaces the resolved owner node itself with `owner`. frames=[] replaces the root. Intermediate hops
// locate the child in whichever slot contains it; the leaf hop swaps that child for `owner`.
export function updateScopeOwner(root: ActivityNode, frames: ScopeFrame[], owner: ActivityNode, catalog?: ActivityCatalogLookup): ActivityNode {
  if (frames.length === 0) return owner;
  return updateScopeAtFrames(root, frames, catalog, () => owner);
}

// Shared recursive walk for the update helpers: descends through the frames (finding each frame's
// ownerNodeId in whichever slot holds it) and applies `apply` to the container the chain lands on,
// rebuilding the ancestors immutably. Returns `root` unchanged when any hop can't be resolved.
function updateScopeAtFrames(
  root: ActivityNode,
  frames: ScopeFrame[],
  catalog: ActivityCatalogLookup,
  apply: (owner: ActivityNode) => ActivityNode
): ActivityNode {
  if (frames.length === 0) return apply(root);

  const [frame, ...rest] = frames;
  const located = locateChildSlot(root, frame.ownerNodeId, catalog);
  if (!located) return root;

  const updatedChild = updateScopeAtFrames(located.child, rest, catalog, apply);
  if (updatedChild === located.child) return root;

  const updatedActivities = located.slot.activities.map(activity =>
    activity.nodeId === frame.ownerNodeId ? updatedChild : activity);

  return replaceSlotActivities(root, located.slot, updatedActivities);
}

export function updateActivity(root: ActivityNode, nodeId: string, update: (activity: ActivityNode) => ActivityNode, catalog?: ActivityCatalogLookup): ActivityNode {
  if (root.nodeId === nodeId) return update(root);

  const slots = getChildSlots(root, catalog);
  if (slots.length === 0) return root;

  let changed = false;
  let next = root;
  for (const slot of slots) {
    const updatedActivities = slot.activities.map(activity => {
      const updated = updateActivity(activity, nodeId, update, catalog);
      if (updated !== activity) changed = true;
      return updated;
    });

    if (changed) next = replaceSlotActivities(next, slot, updatedActivities);
  }

  return changed ? next : root;
}

export function replaceSlotActivities(activity: ActivityNode, slot: ChildSlot, activities: ActivityNode[]): ActivityNode {
  if (!activity.structure) return activity;
  const nextValue = slot.cardinality === "single" ? activities[0] ?? null : activities;

  if (slot.collectionProperty && slot.childProperty) {
    const previousCollection = activity.structure.payload[slot.collectionProperty];
    if (!Array.isArray(previousCollection)) return activity;

    const collectionIndex = findCollectionItemIndex(previousCollection, slot);
    if (collectionIndex < 0) return activity;

    const previousItem = previousCollection[collectionIndex];
    if (!isRecord(previousItem)) return activity;

    const nextCollection = [...previousCollection];
    nextCollection[collectionIndex] = {
      ...previousItem,
      [slot.childProperty]: nextValue
    };

    return {
      ...activity,
      structure: {
        ...activity.structure,
        payload: {
          ...activity.structure.payload,
          [slot.collectionProperty]: nextCollection
        }
      }
    };
  }

  return {
    ...activity,
    structure: {
      ...activity.structure,
      payload: {
        ...activity.structure.payload,
        [slot.property]: nextValue
      }
    }
  };
}

export function syncCanvasToScope(scope: CanvasScope, nodes: Node<WorkflowNodeData>[], edges: Edge[], additionalActivities: ActivityNode[] = []) {
  const existing = new Map(scope.slot.activities.map(activity => [activity.nodeId, activity]));
  for (const activity of additionalActivities) {
    existing.set(activity.nodeId, activity);
  }
  const activities = nodes.map(node => existing.get(node.id)).filter((activity): activity is ActivityNode => !!activity);

  if (scope.slot.mode === "sequence") {
    activities.sort((left, right) => {
      const leftNode = nodes.find(node => node.id === left.nodeId);
      const rightNode = nodes.find(node => node.id === right.nodeId);
      return (leftNode?.position.x ?? 0) - (rightNode?.position.x ?? 0);
    });
  }

  return replaceSlotActivities(scope.owner, scope.slot, activities);
}

export function withFlowchartConnections(owner: ActivityNode, edges: Edge[]) {
  return {
    ...owner,
    structure: updateFlowchartConnections(owner.structure, edges)
  };
}

export function updateLayout(layout: DesignMetadataRecord[], nodes: Node[]) {
  const byNodeId = new Map(layout.map(record => [record.nodeId, record]));
  for (const node of nodes) {
    byNodeId.set(node.id, {
      ...(byNodeId.get(node.id) ?? { nodeId: node.id }),
      nodeId: node.id,
      x: Math.round(node.position.x),
      y: Math.round(node.position.y)
    });
  }
  return [...byNodeId.values()];
}

export function createActivityNode(activity: ActivityCatalogItem, nodeId: string): ActivityNode {
  return {
    nodeId,
    activityVersionId: activity.activityVersionId,
    inputs: [],
    outputs: [],
    structure: createStructureForActivity(activity)
  };
}

export function normalizeActivityStructures(root: ActivityNode | null | undefined, catalog: ActivityCatalogLookup): ActivityNode | null {
  if (!root) return null;

  const catalogItem = resolveCatalogItem(root, catalog);
  const normalizedStructure = root.structure ?? (catalogItem ? createStructureForActivity(catalogItem) : null);
  let next: ActivityNode = normalizedStructure === root.structure ? root : { ...root, structure: normalizedStructure };

  const slots = getChildSlots(next, catalog);
  for (const slot of slots) {
    const normalizedActivities = slot.activities.map(activity => normalizeActivityStructures(activity, catalog) ?? activity);
    if (normalizedActivities.some((activity, index) => activity !== slot.activities[index])) {
      next = replaceSlotActivities(next, slot, normalizedActivities);
    }
  }

  return next;
}

export function getActivityDisplay(activity: ActivityCatalogItem) {
  const shortName = activity.activityTypeKey.split(".").at(-1) || activity.activityTypeKey;
  const displayName = activity.displayName?.trim();
  if (!displayName || displayName === activity.activityTypeKey || displayName.includes(".")) {
    return humanizeActivityTypeName(shortName);
  }

  return displayName;
}

function createWorkflowNode(
  activity: ActivityNode,
  catalogItem: ActivityCatalogItem | undefined,
  position: XYPosition,
  options: { connectable?: boolean; deletable?: boolean; draggable?: boolean; suppressFlowPorts?: boolean } = {}
): Node<WorkflowNodeData> {
  return {
    id: activity.nodeId,
    type: "workflowActivity",
    position,
    connectable: options.connectable,
    deletable: options.deletable,
    draggable: options.draggable,
    data: {
      label: catalogItem ? getActivityDisplay(catalogItem) : activity.activityVersionId,
      activityVersionId: activity.activityVersionId,
      activityTypeKey: catalogItem?.activityTypeKey,
      category: catalogItem?.category,
      executionType: catalogItem?.executionType,
      icon: resolveActivityIcon(catalogItem),
      childSlots: getChildSlots(activity, catalogItem),
      acceptsInbound: activityAcceptsInbound(activity, catalogItem),
      sourcePorts: options.suppressFlowPorts ? [] : getActivitySourcePorts(activity, catalogItem),
      suppressFlowPorts: options.suppressFlowPorts
    }
  };
}

export function resolveActivityIcon(activity: ActivityCatalogItem | undefined): WorkflowNodeIcon {
  if (!activity) return "activity";

  const configuredIcon = normalizeWorkflowNodeIcon(activity.icon);
  if (configuredIcon) return configuredIcon;

  const typeKey = activity.activityTypeKey.toLowerCase();
  const displayName = getActivityDisplay(activity).toLowerCase();
  const category = activity.category?.toLowerCase() ?? "";
  const executionType = activity.executionType?.toLowerCase() ?? "";

  if (typeKey.endsWith(".flowchart") || displayName === "flowchart") return "flowchart";
  if (typeKey.endsWith(".sequence") || displayName === "sequence") return "sequence";
  if (typeKey.includes("writeline") || displayName.includes("write line")) return "terminal";
  if (category.includes("runtime")) return "runtime";
  if (executionType === "trigger") return "trigger";
  return "activity";
}

function normalizeWorkflowNodeIcon(icon: string | null | undefined): WorkflowNodeIcon | null {
  if (!icon) return null;

  const normalized = icon.trim().toLowerCase();
  if (["activity", "flowchart", "sequence", "terminal", "runtime", "trigger"].includes(normalized)) {
    return normalized as WorkflowNodeIcon;
  }

  return null;
}

function isFlowchartCatalogItem(activity: ActivityCatalogItem | undefined) {
  return !!activity && (getActivityDisplay(activity) === "Flowchart" || activity.activityTypeKey.endsWith(".Flowchart"));
}

function isSequenceCatalogItem(activity: ActivityCatalogItem | undefined) {
  return !!activity && (getActivityDisplay(activity) === "Sequence" || activity.activityTypeKey.endsWith(".Sequence"));
}

function humanizeActivityTypeName(name: string) {
  return name
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2")
    .trim();
}

function createStructureForActivity(activity: ActivityCatalogItem): ActivityNodeStructure | null {
  const structureFacet = readStructureDesignFacet(activity);
  if (structureFacet) {
    return {
      kind: structureFacet.kind,
      schemaVersion: structureFacet.schemaVersion,
      payload: clonePayload(structureFacet.payload.initialPayload)
    };
  }

  if (activity.activityTypeKey.endsWith(".Sequence") || activity.displayName === "Sequence") {
    return {
      kind: sequenceStructureKind,
      schemaVersion: "1.0.0",
      payload: { activities: [] }
    };
  }

  if (activity.activityTypeKey.endsWith(".Flowchart") || activity.displayName === "Flowchart") {
    return {
      kind: flowchartStructureKind,
      schemaVersion: "1.0.0",
      payload: {
        activities: [],
        connections: [],
        startNodeId: null,
        nodeMetadata: {},
        connectionMetadata: {}
      }
    };
  }

  return null;
}

function updateFlowchartConnections(structure: ActivityNodeStructure | null | undefined, edges: Edge<WorkflowEdgeData>[]) {
  if (!structure) return structure ?? null;
  const previousConnections = Array.isArray(structure.payload.connections) ? structure.payload.connections : [];
  const previousById = new Map<string, Record<string, unknown>>();

  for (const connection of previousConnections) {
    if (!isRecord(connection)) continue;
    const id = connection.id;
    if (typeof id === "string") previousById.set(id, connection);
  }

  return {
    ...structure,
    payload: {
      ...structure.payload,
      connections: edges.map(edge => {
        const previous = previousById.get(edge.id) ?? {};
        const vertices = edge.data?.vertices;
        const { vertices: _oldVertices, ...previousWithoutVertices } = previous;
        return {
          ...previousWithoutVertices,
          id: edge.id,
          source: { nodeId: edge.source, port: edge.sourceHandle ?? "Done" },
          target: edge.targetHandle ? { nodeId: edge.target, port: edge.targetHandle } : { nodeId: edge.target },
          ...(vertices?.length ? { vertices: vertices.map(vertex => ({ x: Math.round(vertex.x), y: Math.round(vertex.y) })) } : {})
        };
      })
    }
  };
}

function buildEdges(slot: ChildSlot, nodes: Node<WorkflowNodeData>[]): Edge[] {
  if (slot.mode === "sequence") {
    return nodes.slice(0, -1).map((node, index) => ({
      id: `sequence-${node.id}-${nodes[index + 1].id}`,
      source: node.id,
      target: nodes[index + 1].id,
      type: "smoothstep",
      animated: false
    }));
  }

  return [];
}

export function flowchartEdges(owner: ActivityNode): Edge<WorkflowEdgeData>[] {
  if (owner.structure?.kind !== flowchartStructureKind) return [];
  const connections = owner.structure.payload.connections;
  if (!Array.isArray(connections)) return [];

  return connections
    .map((connection, index) => {
      if (!connection || typeof connection !== "object") return null;
      const source = (connection as { source?: { nodeId?: string; port?: string } }).source;
      const target = (connection as { target?: { nodeId?: string; port?: string } }).target;
      if (!source?.nodeId || !target?.nodeId) return null;
      const vertices = Array.isArray((connection as { vertices?: unknown }).vertices)
        ? (connection as { vertices: unknown[] }).vertices.filter(isPosition)
        : [];
      return {
        id: typeof (connection as { id?: unknown }).id === "string" ? String((connection as { id?: unknown }).id) : `flow-${index}-${source.nodeId}-${target.nodeId}`,
        source: source.nodeId,
        target: target.nodeId,
        sourceHandle: source.port,
        targetHandle: target.port && target.port !== "Done" ? target.port : undefined,
        type: "workflow",
        label: source.port && source.port !== "Done" ? source.port : undefined,
        data: vertices.length ? { vertices } : undefined
      } satisfies Edge<WorkflowEdgeData>;
    })
    .filter(edge => edge !== null);
}

export function getActivitySourcePorts(activity: ActivityNode, catalogItem?: ActivityCatalogItem): WorkflowPortDescriptor[] {
  const cases = readStringList((activity as { cases?: unknown }).cases);
  if (isFlowSwitch(activity, catalogItem) && cases.length > 0) {
    return [...cases.map(name => ({ name, displayName: name })), { name: "Default", displayName: "Default" }];
  }

  const descriptorPorts = [
    ...readFlowPorts(catalogItem?.designFacets),
    ...readFlowPorts((catalogItem as { ports?: unknown } | undefined)?.ports),
    ...readFlowPorts(catalogItem?.outputs)
  ];
  if (descriptorPorts.length > 0) return uniquePorts(descriptorPorts);

  const outcomes = readStringList((activity as { outcomes?: unknown }).outcomes);
  if (outcomes.length > 0) return outcomes.map(name => ({ name, displayName: name }));

  return [{ name: "Done", displayName: "Done" }];
}

export function activityAcceptsInbound(_activity: ActivityNode, catalogItem?: ActivityCatalogItem) {
  return String(catalogItem?.executionType ?? "").toLowerCase() !== "trigger";
}

export function createWorkflowEdge(source: string, target: string, sourceHandle?: string | null, targetHandle?: string | null): Edge<WorkflowEdgeData> {
  const port = sourceHandle ?? "Done";
  return {
    id: `flow-${source}-${target}-${port}-${crypto.randomUUID().slice(0, 8)}`,
    source,
    target,
    sourceHandle: port,
    targetHandle: targetHandle ?? undefined,
    type: "workflow",
    label: port !== "Done" ? port : undefined
  };
}

export function spliceWorkflowEdge(edges: Edge<WorkflowEdgeData>[], edge: Edge<WorkflowEdgeData>, newNodeId: string) {
  const first = createWorkflowEdge(edge.source, newNodeId, edge.sourceHandle ?? "Done", undefined);
  const second = createWorkflowEdge(newNodeId, edge.target, "Done", edge.targetHandle ?? undefined);
  return edges.filter(candidate => candidate.id !== edge.id).concat(first, second);
}

function readActivityArray(value: unknown): ActivityNode[] | null {
  if (!Array.isArray(value)) return null;
  return value.filter(isActivityNode);
}

function isFlowSwitch(activity: ActivityNode, catalogItem?: ActivityCatalogItem) {
  const key = catalogItem?.activityTypeKey ?? activity.activityVersionId;
  const name = catalogItem?.displayName ?? "";
  return key.endsWith(".FlowSwitch") || key === "FlowSwitch" || name === "FlowSwitch";
}

function readFlowPorts(value: unknown): WorkflowPortDescriptor[] {
  if (!Array.isArray(value)) return [];
  const ports: WorkflowPortDescriptor[] = [];

  for (const item of value) {
    if (!isRecord(item)) continue;
    if (Array.isArray(item.ports)) {
      ports.push(...readFlowPorts(item.ports));
      continue;
    }

    const type = typeof item.type === "string" ? item.type : typeof item.portType === "string" ? item.portType : "";
    const isBrowsable = item.isBrowsable !== false && item.browsable !== false;
    const name = typeof item.name === "string" ? item.name : typeof item.id === "string" ? item.id : "";
    if (isBrowsable && type.toLowerCase() === "flow" && name) {
      const displayName = typeof item.displayName === "string" ? item.displayName : name;
      ports.push({ name, displayName });
    }
  }

  return ports;
}

function uniquePorts(ports: WorkflowPortDescriptor[]) {
  const byName = new Map<string, WorkflowPortDescriptor>();
  for (const port of ports) {
    if (!byName.has(port.name)) byName.set(port.name, port);
  }
  return [...byName.values()];
}

function readStringList(value: unknown) {
  return Array.isArray(value) ? value.filter((item): item is string => typeof item === "string" && item.length > 0) : [];
}

function readOptionalString(value: unknown) {
  return typeof value === "string" && value.length > 0 ? value : undefined;
}

function clonePayload(payload: Record<string, unknown>): Record<string, unknown> {
  return JSON.parse(JSON.stringify(payload)) as Record<string, unknown>;
}

function groupBy<T>(items: T[], getKey: (item: T) => string) {
  const groups = new Map<string, T[]>();
  for (const item of items) {
    const key = getKey(item);
    if (!key) continue;
    groups.set(key, [...(groups.get(key) ?? []), item]);
  }

  return groups;
}

export function latestActivityExecution(activities: ActivityExecutionStateSummary[]) {
  return [...activities].sort((left, right) =>
    activityExecutionTime(right).localeCompare(activityExecutionTime(left)))[0];
}

function activityExecutionTime(activity: ActivityExecutionStateSummary) {
  return activity.completedAt ?? activity.startedAt ?? activity.scheduledAt;
}

function isPosition(value: unknown): value is XYPosition {
  return isRecord(value) && typeof value.x === "number" && typeof value.y === "number";
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function isActivityNode(value: unknown): value is ActivityNode {
  return typeof value === "object" &&
    value !== null &&
    typeof (value as ActivityNode).nodeId === "string" &&
    typeof (value as ActivityNode).activityVersionId === "string";
}

function getStructureMode(structure: ActivityNodeStructure): ChildSlot["mode"] {
  if (structure.kind === sequenceStructureKind) return "sequence";
  if (structure.kind === flowchartStructureKind) return "flowchart";
  return "generic";
}

function getDefaultSlotLabel(structure: ActivityNodeStructure) {
  if (structure.kind === sequenceStructureKind) return "Activities";
  if (structure.kind === flowchartStructureKind) return "Activities";
  return "Activities";
}

function defaultPosition(mode: ChildSlot["mode"], index: number) {
  if (mode === "sequence") return { nodeId: "", x: index * 280, y: 0 };
  return { nodeId: "", x: (index % 4) * 280, y: Math.floor(index / 4) * 150 };
}

function toDisplayLabel(value: string) {
  return value
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .replace(/[-_]/g, " ")
    .replace(/^\w/, char => char.toUpperCase());
}
