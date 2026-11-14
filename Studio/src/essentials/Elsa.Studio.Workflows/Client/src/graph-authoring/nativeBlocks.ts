import type { ActivityCatalogItem, ActivityNode, ActivityPresentationRecord, DesignMetadataRecord } from "../workflowTypes";
import { createActivityNode, getChildSlots, resolveScope, sequenceStructureKind, flowchartStructureKind, updateScopeOwner, type ScopeFrame } from "../workflowAdapter";
import { compensationScopeStructureKind, compensationStepsSlotId } from "../compensationScope";
import { transferNativeBlockResults } from "./nativeBlockResultTransfers";

// Internal document edits only. Resource authorization, persistence and history remain with each host.
export interface NativeBlockDocument {
  rootActivity: ActivityNode;
  layout: DesignMetadataRecord[];
  activityPresentation: ActivityPresentationRecord[];
}

export interface NativeBlockContext {
  document: NativeBlockDocument;
  catalog: ActivityCatalogItem[];
  scope: ScopeFrame[];
  canEdit: boolean;
  /** Resource-owned declarations/bindings outside rootActivity, including reusable output mappings. */
  referenceContext: unknown[];
  /** Host resource/revision identity, including changes outside the normalized graph document. */
  sourceKey: string;
  /** Expression-module verdict, supplied by the composing host; absence refuses variable introduction around portable expressions. */
  expressionScopePreservation?: {
    /** Changes when the contributing expression support changes. Included in preview staleness checks. */
    revision: string;
    allowsVariableAddition(expression: Readonly<Record<string, unknown>>): boolean;
  };
}

export interface NativeBlockDiagnostic {
  code: string;
  path: string;
  message: string;
}

export type NativeBlockPlan =
  | { status: "refused"; diagnostics: NativeBlockDiagnostic[] }
  | { status: "ready"; document: NativeBlockDocument; selection: string[]; source: string; candidate: string };

export type NativeBlockKind = "sequence" | "flowchart";

interface BlockIdentity {
  nodeId: string;
  activityVersionId: string;
  displayName: string;
  description?: string;
}

export interface CreateNativeBlock extends BlockIdentity {
  kind: NativeBlockKind;
  beforeNodeId?: string;
  position: { x: number; y: number };
}

export interface ExtractNativeBlock extends BlockIdentity {
  selectedNodeIds: string[];
}

type RecordValue = Record<string, unknown>;
interface Endpoint extends RecordValue { nodeId: string; port?: string | null }
interface Connection extends RecordValue { id?: string; source: Endpoint; target: Endpoint }
interface Region { ordered: ActivityNode[]; internal: Connection[]; incoming: Connection[]; outgoing: Connection[] }

const kinds = { sequence: sequenceStructureKind, flowchart: flowchartStructureKind };
const typeKeys = {
  sequence: "Elsa.Activities.Sequence.Activities.Sequence",
  flowchart: "Elsa.Activities.Flowchart.Activities.Flowchart"
};
const record = (value: unknown): value is RecordValue => value !== null && typeof value === "object" && !Array.isArray(value);
const empty = (value: unknown) => value == null || (Array.isArray(value) ? value.length === 0 : record(value) && Object.keys(value).length === 0);
const signature = (value: unknown) => JSON.stringify(value);
const port = (endpoint: Endpoint) => endpoint.port?.trim() || "Done";

class Refusal extends Error {
  constructor(readonly diagnostic: NativeBlockDiagnostic) { super(diagnostic.message); }
}

function refuse(code: string, path: string, message: string): never {
  throw new Refusal({ code, path, message });
}

function guarded(action: () => NativeBlockPlan): NativeBlockPlan {
  try { return action(); }
  catch (error) {
    if (error instanceof Refusal) return { status: "refused", diagnostics: [error.diagnostic] };
    throw error;
  }
}

function source(context: NativeBlockContext) {
  return signature([context.sourceKey, context.canEdit, context.document, context.referenceContext, context.scope, context.catalog, context.expressionScopePreservation?.revision]);
}

function ready(context: NativeBlockContext, document: NativeBlockDocument, selection: string[]): NativeBlockPlan {
  // Detach the proposal: neither edits to the caller's source nor edits to the preview may leak into it.
  const candidate = structuredClone(document);
  return { status: "ready", document: candidate, selection, source: source(context), candidate: signature(candidate) };
}

/** The host commits a successful result once through its existing history/document adapter. */
export function applyNativeBlockPlan(context: NativeBlockContext, plan: NativeBlockPlan): NativeBlockPlan {
  return guarded(() => {
    if (plan.status === "refused") return plan;
    if (!context.canEdit || source(context) !== plan.source)
      refuse("stale-source", "source", "The resource, edit permission, scope or activity catalog changed. Review a new plan.");
    if (signature(plan.document) !== plan.candidate)
      refuse("changed-preview", "preview", "The proposed edit changed. Review a new plan.");
    return { ...plan, document: structuredClone(plan.document), selection: [...plan.selection] };
  });
}

function prepare(context: NativeBlockContext) {
  if (!context.canEdit) refuse("read-only", "source", "This resource cannot be edited.");
  if (!context.sourceKey) refuse("missing-source", "source", "A current resource identity is required.");
  if (!Array.isArray(context.referenceContext))
    refuse("missing-reference-context", "referenceContext", "The resource's declarations and bindings outside this graph must be included before refactoring.");
  const identities = new Set<string>();
  const retainedMetadataIdentities = new Set<string>();
  const visit = (node: ActivityNode, path: string) => {
    if (!node.nodeId || !node.activityVersionId || !Array.isArray(node.inputs) || !Array.isArray(node.outputs))
      refuse("malformed-node", path, "The activity graph contains an incomplete node.");
    if (identities.has(node.nodeId)) refuse("duplicate-node", path, "Node identities must be unique before refactoring.");
    identities.add(node.nodeId);
    if (node.structure?.kind === flowchartStructureKind) {
      Object.keys(metadata(node.structure.payload.nodeMetadata, `${path}.structure.payload.nodeMetadata`)).forEach(id => retainedMetadataIdentities.add(id));
    }
    const compensation = node.structure?.kind === compensationScopeStructureKind;
    if (compensation) {
      const steps = node.structure!.payload.steps;
      if (!Array.isArray(steps) || steps.some(step => !record(step)))
        refuse("malformed-children", `${path}.structure.payload.steps`, "Repair the compensation step collection before refactoring.");
    }
    for (const slot of getChildSlots(node, context.catalog)) {
      // The Steps overview projects the same children as the authored forward/handler slots.
      if (compensation && slot.id === compensationStepsSlotId) continue;
      // Adapter reads are deliberately tolerant for rendering; edits must never silently drop bad entries.
      {
        const collection = slot.collectionProperty ? node.structure?.payload[slot.collectionProperty] : undefined;
        const item = Array.isArray(collection) ? collection[slot.collectionIndex ?? -1] : undefined;
        const raw = slot.collectionProperty && record(item) && slot.childProperty ? item[slot.childProperty] : node.structure?.payload[slot.property];
        if (slot.cardinality === "many" && (!Array.isArray(raw) || raw.length !== slot.activities.length))
          refuse("malformed-children", `${path}.structure.payload.${slot.property}`, "Repair the child collection before refactoring.");
        if (slot.cardinality === "single" && raw != null && slot.activities.length !== 1)
          refuse("malformed-children", `${path}.structure.payload.${slot.property}`, "Repair the child slot before refactoring.");
      }
      slot.activities.forEach((child, index) => visit(child, `${path}.${slot.id}[${index}]`));
    }
  };
  visit(context.document.rootActivity, "rootActivity");
  retainedMetadataIdentities.forEach(id => identities.add(id));
  for (const [name, records] of [["layout", context.document.layout], ["activityPresentation", context.document.activityPresentation]] as const) {
    const seen = new Set<string>();
    records.forEach((item, index) => {
      if (seen.has(item.nodeId)) refuse("duplicate-sidecar", `${name}[${index}]`, "Presentation and layout identities must be unique.");
      seen.add(item.nodeId);
      if (name === "layout" && (!Number.isFinite((item as DesignMetadataRecord).x) || !Number.isFinite((item as DesignMetadataRecord).y)))
        refuse("invalid-position", `${name}[${index}]`, "Saved positions must be finite.");
    });
  }
  const scope = resolveScope(context.document.rootActivity, context.scope, context.catalog);
  const kind = Object.entries(kinds).find(([, value]) => value === scope?.owner.structure?.kind)?.[0] as NativeBlockKind | undefined;
  if (!scope || !kind || scope.slot.property !== "activities" || scope.slot.cardinality !== "many" || scope.owner.structure?.schemaVersion !== "1.0.0")
    refuse("unsupported-scope", "scope", "Choose a current native Sequence or Flowchart activities scope.");
  const raw = scope.owner.structure!.payload.activities;
  if (!Array.isArray(raw) || raw.length !== scope.slot.activities.length)
    refuse("malformed-children", "scope.activities", "Repair the child collection before refactoring.");
  return { ...scope, kind, identities };
}

function nativeCatalog(context: NativeBlockContext, kind: NativeBlockKind, versionId: string) {
  const matches = context.catalog.filter(item => item.activityVersionId === versionId);
  const item = matches[0];
  if (matches.length !== 1 || item.available === false || item.activityTypeKey !== typeKeys[kind])
    refuse("unavailable-composite", "activityVersionId", "Choose an exact available native composite version from the current catalog.");
  const template = createActivityNode(item, "new-block");
  if (template.structure?.kind !== kinds[kind] || template.structure.schemaVersion !== "1.0.0")
    refuse("unsupported-composite", "activityVersionId", "This native composite structure version is not supported.");
  return item;
}

function newBlock(context: NativeBlockContext, kind: NativeBlockKind, request: BlockIdentity, identities: Set<string>) {
  if (!request.nodeId.trim() || request.nodeId !== request.nodeId.trim() || identities.has(request.nodeId) || context.document.layout.some(x => x.nodeId === request.nodeId) || context.document.activityPresentation.some(x => x.nodeId === request.nodeId))
    refuse("occupied-identity", "nodeId", "Choose a new, nonempty node identity without surrounding whitespace or retained metadata.");
  const name = request.displayName.trim();
  const description = request.description?.trim();
  if (!name || name.length > 200 || (description?.length ?? 0) > 2000)
    refuse("invalid-name", "displayName", "Enter a name of 1–200 characters and a description of at most 2,000 characters.");
  const block = createActivityNode(nativeCatalog(context, kind, request.activityVersionId), request.nodeId);
  assertDisposableBoundary(block, context, kind);
  return { block, presentation: { nodeId: block.nodeId, displayName: name, ...(description ? { description } : {}) } };
}

function withActivities(owner: ActivityNode, activities: ActivityNode[], extra: RecordValue = {}): ActivityNode {
  return { ...owner, structure: { ...owner.structure!, payload: { ...owner.structure!.payload, ...extra, activities } } };
}

function edited(context: NativeBlockContext, owner: ActivityNode, layout = context.document.layout, activityPresentation = context.document.activityPresentation): NativeBlockDocument {
  return { ...context.document, rootActivity: updateScopeOwner(context.document.rootActivity, context.scope, owner, context.catalog, { preserveAncestorStart: true }), layout, activityPresentation };
}

export function planCreateNativeBlock(context: NativeBlockContext, request: CreateNativeBlock): NativeBlockPlan {
  return guarded(() => {
    const scope = prepare(context);
    const { block, presentation } = newBlock(context, request.kind, request, scope.identities);
    if (!Number.isFinite(request.position.x) || !Number.isFinite(request.position.y))
      refuse("invalid-position", "position", "Choose a finite block position.");
    const index = request.beforeNodeId === undefined ? scope.slot.activities.length : scope.slot.activities.findIndex(x => x.nodeId === request.beforeNodeId);
    if (index < 0) refuse("stale-selection", "beforeNodeId", "The insertion target is no longer in this scope.");
    if (scope.kind === "flowchart") readConnections(scope.owner);
    const children = [...scope.slot.activities];
    children.splice(index, 0, block);
    const extra = scope.kind === "flowchart" ? { startNodeId: scope.slot.activities.length ? pinnedStart(effectiveStart(scope.owner)) : block.nodeId } : {};
    const document = edited(context, withActivities(scope.owner, children, extra), [...context.document.layout, { nodeId: block.nodeId, ...request.position }], [...context.document.activityPresentation, presentation]);
    return ready(context, document, [block.nodeId]);
  });
}

function selection(activities: ActivityNode[], ids: string[]) {
  const selected = new Set(ids);
  if (!ids.length || selected.size !== ids.length || ids.some(id => !activities.some(x => x.nodeId === id)))
    refuse("stale-selection", "selectedNodeIds", "Select distinct activities in the current scope.");
  return selected;
}

function origin(layout: DesignMetadataRecord[], ids: Set<string>) {
  const positions = layout.filter(item => ids.has(item.nodeId));
  return positions.length ? { x: Math.min(...positions.map(x => x.x)), y: Math.min(...positions.map(x => x.y)) } : { x: 0, y: 0 };
}

function translateLayout(layout: DesignMetadataRecord[], ids: Set<string>, delta: { x: number; y: number }) {
  return layout.map(item => ids.has(item.nodeId) ? { ...item, x: item.x + delta.x, y: item.y + delta.y } : item);
}

export function planExtractNativeBlock(context: NativeBlockContext, request: ExtractNativeBlock): NativeBlockPlan {
  return guarded(() => {
    const scope = prepare(context);
    const selected = selection(scope.slot.activities, request.selectedNodeIds);
    assertMemberReadsRemainInScope(context, selected);
    const { block, presentation } = newBlock(context, scope.kind, request, scope.identities);
    let ordered = scope.slot.activities.filter(x => selected.has(x.nodeId));
    const first = scope.slot.activities.findIndex(x => selected.has(x.nodeId));
    const position = origin(context.document.layout, selected);
    let owner = scope.owner;
    let composite = block;
    if (scope.kind === "sequence") {
      if (scope.slot.activities.slice(first, first + selected.size).some(x => !selected.has(x.nodeId)))
        refuse("noncontiguous-sequence", "selectedNodeIds", "Select a contiguous range in authored Sequence order.");
      composite = withActivities(block, ordered);
    } else {
      const region = linearRegion(scope.owner, selected, context.catalog);
      ordered = region.ordered;
      const payload = scope.owner.structure!.payload;
      const internalIds = new Set(region.internal.map(connectionKey));
      const nodes = metadata(payload.nodeMetadata, "scope.nodeMetadata");
      const connections = metadata(payload.connectionMetadata, "scope.connectionMetadata");
      composite = withActivities(block, ordered, {
        startNodeId: pinnedStart(ordered[0].nodeId),
        connections: region.internal.map(x => translateVertices(x, { x: -position.x, y: -position.y })),
        nodeMetadata: pick(nodes, selected), connectionMetadata: pick(connections, internalIds)
      });
      const all = readConnections(scope.owner);
      owner = withActivities(scope.owner, scope.slot.activities, {
        connections: all.filter(x => !region.internal.includes(x)).map(x => redirect(x, selected, block.nodeId)),
        startNodeId: selected.has(effectiveStart(scope.owner)) ? block.nodeId : pinnedStart(effectiveStart(scope.owner)),
        nodeMetadata: omit(nodes, selected), connectionMetadata: omit(connections, internalIds)
      });
    }
    const children = scope.slot.activities.filter(x => !selected.has(x.nodeId));
    children.splice(first, 0, composite);
    const layout = [...translateLayout(context.document.layout, selected, { x: -position.x, y: -position.y }), { nodeId: block.nodeId, ...position }];
    const document = edited(context, withActivities(owner, children), layout, [...context.document.activityPresentation, presentation]);
    return ready(context, transferNativeBlockResults(context, document, scope.owner.nodeId, block.nodeId, selected, refuse,
      scope.kind === "flowchart" ? effectiveStart(scope.owner) : undefined), [block.nodeId]);
  });
}

function assertMemberReadsRemainInScope(context: NativeBlockContext, selected: Set<string>) {
  const locations: { node: ActivityNode; moved: boolean }[] = [];
  const visit = (node: ActivityNode, moved = false) => {
    moved ||= selected.has(node.nodeId);
    locations.push({ node, moved });
    for (const slot of getChildSlots(node, context.catalog)) {
      if (node.structure?.kind === compensationScopeStructureKind && slot.id === compensationStepsSlotId) continue;
      slot.activities.forEach(child => visit(child, moved));
    }
  };
  visit(context.document.rootActivity);
  const movedIds = new Set(locations.filter(item => item.moved).map(item => item.node.nodeId));
  const inspect = (value: unknown, path: string) => {
    if (Array.isArray(value)) { value.forEach((item, index) => inspect(item, `${path}[${index}]`)); return; }
    if (!record(value)) return;
    if (typeof value.expressionType === "string") {
      // This role names its producer by lineage, without a declaring scope. Moving it below
      // a block cannot preserve a read from outside that block under the shared authority.
      if (value.expressionType.toLowerCase() === "activityresultmember" && record(value.value)
        && typeof value.value.producerNodeId === "string" && movedIds.has(value.value.producerNodeId))
        refuse("member-result-scope-crossing", path, "This member read would cross the new block boundary. Include its consuming activity in the selection or keep the producer in its current scope.");
      // Literal/object payloads are data. Other expression roles keep their own contracts;
      // a nested object resembling a member binding is not itself an authored member read.
      return;
    }
    Object.entries(value).forEach(([key, child]) => inspect(child, `${path}.${key}`));
  };
  locations.filter(item => !item.moved).forEach(({ node }) => inspect(node.inputs, `nodes.${node.nodeId}.inputs`));
  inspect(context.referenceContext, "referenceContext");
}

function readConnections(owner: ActivityNode): Connection[] {
  const raw = owner.structure!.payload.connections;
  const children = owner.structure!.payload.activities as ActivityNode[];
  if (!Array.isArray(raw)) refuse("malformed-connections", "scope.connections", "The Flowchart connection collection is missing.");
  const ids = new Set<string>();
  return raw.map((value, index) => {
    const path = `scope.connections[${index}]`;
    if (!record(value) || !record(value.source) || !record(value.target) || typeof value.source.nodeId !== "string" || typeof value.target.nodeId !== "string")
      refuse("malformed-connection", path, "Repair this Flowchart connection before refactoring.");
    const connection = value as Connection;
    if ([connection.source, connection.target].some(x => !children.some(child => child.nodeId === x.nodeId) || (x.port != null && typeof x.port !== "string")))
      refuse("invalid-endpoint", path, "Flowchart endpoints must reference current children and named ports.");
    if (connection.id != null && typeof connection.id !== "string") refuse("invalid-connection-id", path, "Connection identities must be strings.");
    const key = connectionKey(connection);
    if (ids.has(key)) refuse("duplicate-connection", path, "Flowchart connection identities must be unique.");
    ids.add(key);
    if (value.vertices !== undefined && (!Array.isArray(value.vertices) || value.vertices.some(x => !record(x) || !Number.isFinite(x.x) || !Number.isFinite(x.y))))
      refuse("invalid-vertices", `${path}.vertices`, "Connection positions must be finite.");
    return connection;
  });
}

// Same fallback identity used by the native FlowchartGraph, not a new authored connection ID.
function connectionKey(connection: Connection) {
  return connection.id?.trim() || `${connection.source.nodeId}:${port(connection.source)}->${connection.target.nodeId}`;
}

function metadata(value: unknown, path: string): RecordValue {
  if (value == null) return {};
  if (!record(value)) refuse("malformed-metadata", path, "Flowchart metadata must be an identity map.");
  return value;
}

const pick = (value: RecordValue, ids: Set<string>) => Object.fromEntries(Object.entries(value).filter(([key]) => ids.has(key)));
const omit = (value: RecordValue, ids: Set<string>) => Object.fromEntries(Object.entries(value).filter(([key]) => !ids.has(key)));

function assertPlainPolicy(value: unknown, path: string, connection = false) {
  if (value == null) return;
  const allowed = ["policyKind", "configuration", ...(connection ? ["label", "isDefault"] : [])];
  if (!record(value) || (!empty(value.policyKind) && value.policyKind !== "") || !empty(value.configuration) || (connection && value.isDefault != null && value.isDefault !== false) || Object.keys(value).some(key => !allowed.includes(key)))
    refuse("policy-boundary", path, "This selection crosses configured Flowchart routing or join policy. Keep it in its current scope.");
}

function effectiveStart(owner: ActivityNode) {
  const payload = owner.structure!.payload;
  const children = payload.activities as ActivityNode[];
  if (payload.startNodeId != null && typeof payload.startNodeId !== "string")
    refuse("invalid-start", "scope.startNodeId", "Choose a current Flowchart start activity before refactoring.");
  const explicit = typeof payload.startNodeId === "string" ? payload.startNodeId.trim() : "";
  if (explicit && !children.some(x => x.nodeId === explicit))
    refuse("invalid-start", "scope.startNodeId", "Choose a current Flowchart start activity before refactoring.");
  const inbound = new Set(readConnections(owner).map(x => x.target.nodeId));
  return explicit || (children.find(x => !inbound.has(x.nodeId)) ?? children[0])?.nodeId || "";
}

function pinnedStart(nodeId: string) {
  if (nodeId !== nodeId.trim())
    refuse("unsafe-start-identity", "scope.startNodeId", "This implicit start uses an identity that native start normalization would change. Keep it in its current scope.");
  return nodeId;
}

function assertNormalCompletion(node: ActivityNode, catalog: ActivityCatalogItem[]) {
  const entries = catalog.filter(x => x.activityVersionId === node.activityVersionId);
  const entry = entries[0];
  const facets = entry?.designFacets?.filter(x => record(x) && x.kind === "elsa.outcomes") as RecordValue[] | undefined;
  const payload = facets?.length === 1 && record(facets[0].payload) ? facets[0].payload : undefined;
  // No rendering fallback and no browsability filter: hidden and dynamic outcomes are executable too.
  const ports = payload?.ports ?? entry?.ports;
  if (entries.length !== 1 || payload?.dynamicOutcomes != null || !Array.isArray(ports) || ports.length === 0 || ports.some(x => !record(x) || typeof x.name !== "string" || !["Done", "Break"].includes(x.name)))
    refuse("unproven-outcomes", `nodes.${node.nodeId}.outcomes`, "This activity can complete through an unconnected or unknown outcome. Extract only an unconditional Done path.");
}

function linearRegion(owner: ActivityNode, selected: Set<string>, catalog: ActivityCatalogItem[]): Region {
  const all = readConnections(owner);
  const children = owner.structure!.payload.activities as ActivityNode[];
  const internal = all.filter(x => selected.has(x.source.nodeId) && selected.has(x.target.nodeId));
  const incoming = all.filter(x => !selected.has(x.source.nodeId) && selected.has(x.target.nodeId));
  const outgoing = all.filter(x => selected.has(x.source.nodeId) && !selected.has(x.target.nodeId));
  const entries = children.filter(x => selected.has(x.nodeId) && !internal.some(c => c.target.nodeId === x.nodeId));
  if (entries.length !== 1 || internal.length !== selected.size - 1 || incoming.length > 1 || outgoing.length > 1)
    refuse("nonlinear-flowchart", "selectedNodeIds", "Select one acyclic linear region with a single entry and normal exit.");
  const ordered: ActivityNode[] = [];
  let next: ActivityNode | undefined = entries[0];
  while (next && !ordered.includes(next)) {
    ordered.push(next);
    const edges = internal.filter(x => x.source.nodeId === next!.nodeId);
    if (edges.length > 1) refuse("branching-region", `nodes.${next.nodeId}`, "The selected region contains a branch.");
    next = children.find(x => x.nodeId === edges[0]?.target.nodeId);
  }
  if (ordered.length !== selected.size || incoming.some(x => x.target.nodeId !== ordered[0].nodeId) || outgoing.some(x => x.source.nodeId !== ordered.at(-1)!.nodeId))
    refuse("crossing-region", "selectedNodeIds", "Connections must enter the first activity and leave the last activity only.");
  const start = effectiveStart(owner);
  if (selected.has(start) && start !== ordered[0].nodeId)
    refuse("crossing-start", "scope.startNodeId", "The Flowchart start enters the middle of the selected region.");
  const nodes = metadata(owner.structure!.payload.nodeMetadata, "scope.nodeMetadata");
  const connections = metadata(owner.structure!.payload.connectionMetadata, "scope.connectionMetadata");
  for (const node of ordered) {
    assertNormalCompletion(node, catalog);
    const descriptor = catalog.find(x => x.activityVersionId === node.activityVersionId)!;
    if (descriptor.executionType === "Trigger" && (node.nodeId !== ordered[0].nodeId || start !== node.nodeId))
      refuse("alternate-trigger-entry", `nodes.${node.nodeId}`, "This trigger has a different entry path. Keep it in its current Flowchart scope.");
    assertPlainPolicy(nodes[node.nodeId], `scope.nodeMetadata.${node.nodeId}`);
  }
  for (const connection of [...internal, ...incoming, ...outgoing]) {
    if ((selected.has(connection.source.nodeId) && port(connection.source) !== "Done") || port(connection.target) !== "Done")
      refuse("conditional-route", `connections.${connectionKey(connection)}`, "Only unconditional Done routing is supported inside or out of the region.");
    assertPlainPolicy(connections[connectionKey(connection)], `scope.connectionMetadata.${connectionKey(connection)}`, true);
  }
  for (const connection of [...incoming, ...outgoing]) {
    const outside = selected.has(connection.source.nodeId) ? connection.target.nodeId : connection.source.nodeId;
    if (all.filter(x => x.source.nodeId === outside).length > 1 || all.filter(x => x.target.nodeId === outside).length > 1)
      refuse("crossing-join", `nodes.${outside}`, "The block boundary crosses a branch or join. Keep that routing in its current scope.");
    assertPlainPolicy(nodes[outside], `scope.nodeMetadata.${outside}`);
  }
  return { ordered, internal, incoming, outgoing };
}

function redirect(connection: Connection, ids: Set<string>, blockId: string): Connection {
  return { ...connection, id: connectionKey(connection),
    source: ids.has(connection.source.nodeId) ? { ...connection.source, nodeId: blockId } : connection.source,
    target: ids.has(connection.target.nodeId) ? { ...connection.target, nodeId: blockId } : connection.target
  };
}

function translateVertices(connection: Connection, delta: { x: number; y: number }): Connection {
  if (!Array.isArray(connection.vertices)) return connection;
  return { ...connection, vertices: connection.vertices.map(value => ({ ...value, x: value.x + delta.x, y: value.y + delta.y })) };
}

function assertDisposableBoundary(block: ActivityNode, context: NativeBlockContext, kind: NativeBlockKind) {
  const defaults = createActivityNode(nativeCatalog(context, kind, block.activityVersionId), block.nodeId);
  for (const [key, value] of Object.entries(block)) {
    if (["nodeId", "activityVersionId", "structure"].includes(key)) continue;
    if (signature(value) !== signature(defaults[key]) || (["inputs", "outputs"].includes(key) && !empty(value)))
      refuse("boundary-settings", `nodes.${block.nodeId}.${key}`, "Ungroup would discard block inputs, outputs or execution settings.");
  }
  const allowed = kind === "sequence" ? ["activities"] : ["activities", "connections", "startNodeId", "nodeMetadata", "connectionMetadata"];
  for (const [key, value] of Object.entries(block.structure!.payload)) {
    if (allowed.includes(key)) continue;
    if (key === "variables") {
      if (!empty(value)) refuse("boundary-declarations", `nodes.${block.nodeId}.structure.payload.variables`, "Ungroup would discard block-local variable declarations.");
      continue;
    }
    if (signature(value) !== signature(defaults.structure?.payload[key]))
      refuse("boundary-declarations", `nodes.${block.nodeId}.structure.payload.${key}`, "Ungroup would discard block-local declarations or structure settings.");
  }
}

function assertUnreferenced(context: NativeBlockContext, owner: ActivityNode, block: ActivityNode) {
  const connections = owner.structure?.kind === flowchartStructureKind && Array.isArray(owner.structure.payload.connections) ? owner.structure.payload.connections : [];
  const endpoints = new Set(connections.flatMap(value => record(value) ? [value.source, value.target] : []));
  const walk = (value: unknown, path: string, expression = false) => {
    if (Array.isArray(value)) { value.forEach((item, index) => walk(item, `${path}[${index}]`, expression)); return; }
    if (record(value)) {
      const syntaxes = [value.expressionType, value.syntax, value.type]
        .filter((item): item is string => typeof item === "string")
        .map(item => item.toLowerCase());
      const literal = syntaxes.length > 0 && syntaxes.every(item => item === "literal");
      const variable = syntaxes.length > 0 && syntaxes.every(item => item === "variable");
      const opaque = expression || typeof value.expressionType === "string" || typeof value.syntax === "string" || syntaxes.some(item => ["javascript", "csharp", "liquid", "python"].includes(item));
      for (const [key, child] of Object.entries(value)) {
        if (variable && ["expressionType", "syntax", "type"].includes(key)) continue;
        if (key === "value" && variable) {
          // A variable key is not a node identity. Its declaring scope and unknown fields still
          // fence Ungroup; keeping explicit transfers must not depend on the variable's spelling.
          if (record(child)) Object.entries(child).forEach(([field, item]) => {
            if (field !== "referenceKey") walk(item, `${path}.value.${field}`, true);
          });
          continue;
        }
        if (value.kind === "Variable" && typeof value.declaringScopeNodeId === "string" && typeof value.variableKey === "string" && (key === "kind" || key === "variableKey")) continue;
        // Literal scalars are data, even when text happens to contain the wrapper ID. Objects can
        // carry typed references; those and conflicting syntax markers stay conservative.
        if (key === "value" && literal && (child === null || typeof child !== "object")) continue;
        if (value === block && key === "nodeId") continue;
        if (endpoints.has(value) && key === "nodeId" && child === block.nodeId) continue;
        if (owner.structure?.kind === flowchartStructureKind && value === owner.structure.payload && key === "startNodeId" && child === block.nodeId) continue;
        walk(child, `${path}.${key}`, opaque || /expression|script|code/i.test(key));
      }
      return;
    }
    if (typeof value === "string" && (value === block.nodeId || (expression && value.includes(block.nodeId))))
      refuse("referenced-boundary", path, "This value refers to the block identity. Keep the block or remove that reference before Ungroup.");
  };
  walk(context.document.rootActivity, "rootActivity");
  walk(context.referenceContext, "referenceContext");
}

export function planUngroupNativeBlock(context: NativeBlockContext, blockId: string): NativeBlockPlan {
  return guarded(() => {
    const scope = prepare(context);
    const block = scope.slot.activities.find(x => x.nodeId === blockId);
    if (!block || block.structure?.kind !== kinds[scope.kind] || block.structure.schemaVersion !== "1.0.0")
      refuse("unsupported-block", `nodes.${blockId}`, "Choose a native block of the enclosing structure kind.");
    assertDisposableBoundary(block, context, scope.kind);
    assertUnreferenced(context, scope.owner, block);
    const children = block.structure.payload.activities as ActivityNode[];
    const ids = new Set(children.map(x => x.nodeId));
    const blockPosition = context.document.layout.find(x => x.nodeId === blockId) ?? { x: 0, y: 0 };
    let owner = scope.owner;
    if (scope.kind === "flowchart") {
      const bodyStart = effectiveStart(block);
      const boundary = linearRegion(scope.owner, new Set([blockId]), context.catalog);
      if (!children.length) {
        if (!empty(block.structure.payload.nodeMetadata) || !empty(block.structure.payload.connectionMetadata))
          refuse("boundary-metadata", `nodes.${blockId}.structure.payload`, "Ungroup would discard retained metadata in this empty block.");
        if (boundary.incoming.length || boundary.outgoing.length || (scope.slot.activities.length !== 1 && effectiveStart(scope.owner) === blockId))
          refuse("empty-flowchart-boundary", `nodes.${blockId}`, "An empty connected Flowchart cannot be ungrouped without changing routing.");
        owner = withActivities(scope.owner, scope.slot.activities, {
          startNodeId: scope.slot.activities.length === 1 ? null : pinnedStart(effectiveStart(scope.owner)),
          nodeMetadata: omit(metadata(scope.owner.structure!.payload.nodeMetadata, "scope.nodeMetadata"), new Set([blockId]))
        });
      } else {
        const region = linearRegion(block, ids, context.catalog);
        if (bodyStart !== region.ordered[0].nodeId)
          refuse("different-entry", `nodes.${blockId}.startNodeId`, "The block start does not match its linear entry.");
        const payload = scope.owner.structure!.payload;
        const nodes = metadata(payload.nodeMetadata, "scope.nodeMetadata");
        const connections = metadata(payload.connectionMetadata, "scope.connectionMetadata");
        const innerConnections = metadata(block.structure.payload.connectionMetadata, `nodes.${blockId}.connectionMetadata`);
        const innerNodes = metadata(block.structure.payload.nodeMetadata, `nodes.${blockId}.nodeMetadata`);
        const inner = readConnections(block);
        const innerKeys = new Set(inner.map(connectionKey));
        for (const key of Object.keys(innerNodes)) {
          if (!ids.has(key) || key in nodes) refuse("metadata-collision", `nodes.${key}`, "Ungroup would overwrite or relocate unrelated node metadata.");
        }
        for (const key of Object.keys(innerConnections)) {
          if (!innerKeys.has(key) || key in connections) refuse("metadata-collision", `connections.${key}`, "Ungroup would overwrite or activate unrelated connection metadata.");
        }
        const outer = readConnections(scope.owner);
        if (inner.some(x => outer.some(y => connectionKey(x) === connectionKey(y))))
          refuse("connection-collision", `nodes.${blockId}.connections`, "Ungroup would duplicate a connection identity.");
        owner = withActivities(scope.owner, scope.slot.activities, {
          startNodeId: pinnedStart(effectiveStart(scope.owner) === blockId ? region.ordered[0].nodeId : effectiveStart(scope.owner)),
          connections: [...outer.map(x => ({ ...x, id: connectionKey(x),
            source: x.source.nodeId === blockId ? { ...x.source, nodeId: region.ordered.at(-1)!.nodeId } : x.source,
            target: x.target.nodeId === blockId ? { ...x.target, nodeId: region.ordered[0].nodeId } : x.target
          })), ...inner.map(x => translateVertices(x, blockPosition))],
          nodeMetadata: { ...omit(nodes, new Set([blockId])), ...innerNodes },
          connectionMetadata: { ...connections, ...innerConnections }
        });
      }
    }
    const activities = scope.slot.activities.flatMap(x => x === block ? children : [x]);
    const layout = translateLayout(context.document.layout.filter(x => x.nodeId !== blockId), ids, blockPosition);
    const presentation = context.document.activityPresentation.filter(x => x.nodeId !== blockId);
    return ready(context, edited(context, withActivities(owner, activities), layout, presentation), children.map(x => x.nodeId));
  });
}
