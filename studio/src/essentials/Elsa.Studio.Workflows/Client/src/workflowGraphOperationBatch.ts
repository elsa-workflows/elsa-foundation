import type { ActivityCatalogItem, ActivityNode, DesignMetadataRecord, WorkflowDraft } from "./workflowTypes";
import { createActivityNode, findActivityNode, flowchartStructureKind, getChildSlots, replaceSlotActivities, updateActivity, type ChildSlot } from "./workflowAdapter";
import { collectOwnedNodeIds } from "./bpmn/bpmnAdapter";
import { updateActivityPresentation } from "./activityPresentation";

/**
 * The designer applier for `workflow_apply_operation_batch` contract 1 (spec 1193 FR-007). It accepts only the batch the
 * server froze: every operation has exactly its declared fields and resolved node ids, so an unknown field, a missing
 * property name, an unknown target or slot refuses the whole batch with the failing operation named. It never rewrites an
 * activity's type, version, children or undeclared fields, and it applies all operations to one copy or none.
 */

export type WorkflowOperationRisk = "reviewRequired" | "destructive";

export interface FrozenPoint { x: number; y: number }
export type FrozenPropertyValue =
  | { literal: string | number | boolean }
  | { secretReference: { name: string; typeName: string | null; scope: string | null } };

export type FrozenWorkflowOperation =
  | { index: number; kind: "add"; risk: WorkflowOperationRisk; nodeId: string; activityVersionId: string; activityTypeKey: string; version: string; scopePath: string[]; slot: string; position: FrozenPoint | null }
  | { index: number; kind: "set-root"; risk: WorkflowOperationRisk; nodeId: string; replacesRoot: boolean }
  | { index: number; kind: "position"; risk: WorkflowOperationRisk; nodeId: string; x: number; y: number }
  | { index: number; kind: "set-property"; risk: WorkflowOperationRisk; nodeId: string; property: string; inputName: string; typeName: string; value: FrozenPropertyValue }
  | { index: number; kind: "update"; risk: WorkflowOperationRisk; nodeId: string; fields: { displayName?: string; description?: string; canStartWorkflow?: boolean }; startInput: string | null }
  | { index: number; kind: "remove"; risk: WorkflowOperationRisk; nodeId: string }
  | { index: number; kind: "connect"; risk: WorkflowOperationRisk; source: string; target: string; sourcePort: string; targetPort: string | null; scopeOwnerId: string; slot: string }
  | { index: number; kind: "disconnect"; risk: WorkflowOperationRisk; source: string; target: string; sourcePort: string };

export interface FrozenWorkflowBatch {
  schemaVersion: "1";
  highComplexity: boolean;
  risk: WorkflowOperationRisk;
  operations: FrozenWorkflowOperation[];
}

export interface WorkflowBatchApplyResult {
  draft: WorkflowDraft;
  appliedCount: number;
  addedNodeIds: string[];
  /** Ids that left the document: removed subtrees and a replaced root's tree. */
  removedNodeIds: string[];
}

/** A whole-batch refusal that names the failing operation. Nothing was applied. */
export class WorkflowBatchRefusal extends Error {
  constructor(readonly operationIndex: number | null, message: string) {
    super(operationIndex === null ? message : `Operation ${operationIndex}: ${message}`);
    this.name = "WorkflowBatchRefusal";
  }
}

const maxOperations = 64;
/** Node fields that carry identity, type, version or children; a property write never targets them. */
const structuralFields = new Set(["nodeId", "activityVersionId", "inputs", "outputs", "structure", "intrinsic", "executionPlacement"]);
const fieldsByKind: Record<FrozenWorkflowOperation["kind"], string[]> = {
  "add": ["index", "kind", "risk", "nodeId", "activityVersionId", "activityTypeKey", "version", "scopePath", "slot", "position"],
  "set-root": ["index", "kind", "risk", "nodeId", "replacesRoot"],
  "position": ["index", "kind", "risk", "nodeId", "x", "y"],
  "set-property": ["index", "kind", "risk", "nodeId", "property", "inputName", "typeName", "value"],
  "update": ["index", "kind", "risk", "nodeId", "fields", "startInput"],
  "remove": ["index", "kind", "risk", "nodeId"],
  "connect": ["index", "kind", "risk", "source", "target", "sourcePort", "targetPort", "scopeOwnerId", "slot"],
  "disconnect": ["index", "kind", "risk", "source", "target", "sourcePort"]
};

/** Reads the frozen batch with the same strict schema the server froze it with: exact fields, exact types. */
export function parseFrozenWorkflowBatch(value: unknown): FrozenWorkflowBatch {
  if (!isPlainRecord(value)) throw new WorkflowBatchRefusal(null, "The batch is not an object.");
  exactKeys(null, value, ["schemaVersion", "highComplexity", "risk", "operations"]);
  if (value.schemaVersion !== "1") throw new WorkflowBatchRefusal(null, "Only batch schema 1 is supported.");
  const risk = value.risk;
  if (typeof value.highComplexity !== "boolean" || !isRisk(risk)) throw new WorkflowBatchRefusal(null, "The batch risk or complexity label is malformed.");
  if (!Array.isArray(value.operations) || value.operations.length === 0 || value.operations.length > maxOperations)
    throw new WorkflowBatchRefusal(null, `A batch holds 1 to ${maxOperations} operations.`);
  const operations = value.operations.map((operation, position) => parseOperation(operation, position));
  return { schemaVersion: "1", highComplexity: value.highComplexity, risk, operations };
}

function parseOperation(input: unknown, position: number): FrozenWorkflowOperation {
  if (!isPlainRecord(input)) throw new WorkflowBatchRefusal(position, "The operation is not an object.");
  const value: Record<string, unknown> = input;
  if (value.index !== position) throw new WorkflowBatchRefusal(position, "The operation index does not match its position.");
  const kind = value.kind;
  if (typeof kind !== "string" || !Object.prototype.hasOwnProperty.call(fieldsByKind, kind)) throw new WorkflowBatchRefusal(position, `Unsupported operation kind '${String(kind)}'.`);
  exactKeys(position, value, fieldsByKind[kind as FrozenWorkflowOperation["kind"]]);
  const risk = value.risk;
  if (!isRisk(risk)) throw new WorkflowBatchRefusal(position, "The operation risk is malformed.");
  const text = (field: string): string => {
    const candidate = value[field];
    if (typeof candidate !== "string" || !candidate.trim() || candidate.length > 256) throw new WorkflowBatchRefusal(position, `'${field}' must be a non-empty string.`);
    return candidate;
  };
  const optionalText = (field: string): string | null => value[field] === null ? null : text(field);
  const number = (candidate: unknown, field: string): number => {
    if (typeof candidate !== "number" || !Number.isFinite(candidate)) throw new WorkflowBatchRefusal(position, `'${field}' must be a finite number.`);
    return candidate;
  };
  switch (kind as FrozenWorkflowOperation["kind"]) {
    case "add": {
      const scopePath = value.scopePath;
      if (!Array.isArray(scopePath) || scopePath.some(segment => typeof segment !== "string" || !segment))
        throw new WorkflowBatchRefusal(position, "'scopePath' must be an array of node ids.");
      let point: FrozenPoint | null = null;
      const frozenPosition = value.position;
      if (frozenPosition !== null) {
        if (!isPlainRecord(frozenPosition)) throw new WorkflowBatchRefusal(position, "'position' must be an object or null.");
        exactKeys(position, frozenPosition, ["x", "y"]);
        point = { x: number(frozenPosition.x, "position.x"), y: number(frozenPosition.y, "position.y") };
      }
      return { index: position, kind: "add", risk, nodeId: text("nodeId"), activityVersionId: text("activityVersionId"), activityTypeKey: text("activityTypeKey"),
        version: text("version"), scopePath: [...scopePath as string[]], slot: text("slot"), position: point };
    }
    case "set-root": {
      const replacesRoot = value.replacesRoot;
      if (typeof replacesRoot !== "boolean") throw new WorkflowBatchRefusal(position, "'replacesRoot' must be a boolean.");
      return { index: position, kind: "set-root", risk, nodeId: text("nodeId"), replacesRoot };
    }
    case "position":
      return { index: position, kind: "position", risk, nodeId: text("nodeId"), x: number(value.x, "x"), y: number(value.y, "y") };
    case "set-property": {
      const property = text("property");
      if (structuralFields.has(property)) throw new WorkflowBatchRefusal(position, `'${property}' is not an input; an activity's identity, type, version and children are never rewritten.`);
      const propertyValue = value.value;
      if (!isPlainRecord(propertyValue) || Object.keys(propertyValue).length !== 1) throw new WorkflowBatchRefusal(position, "'value' must hold exactly one of 'literal' or 'secretReference'.");
      let frozen: FrozenPropertyValue;
      if ("literal" in propertyValue) {
        const literal = propertyValue.literal;
        if (!(typeof literal === "string" || typeof literal === "boolean" || typeof literal === "number" && Number.isFinite(literal)))
          throw new WorkflowBatchRefusal(position, "'literal' must be a string, a finite number or a boolean.");
        frozen = { literal };
      } else if ("secretReference" in propertyValue && isPlainRecord(propertyValue.secretReference)) {
        const reference = propertyValue.secretReference;
        exactKeys(position, reference, ["name", "typeName", "scope"]);
        const { name, typeName, scope } = reference;
        if (typeof name !== "string" || !name.trim() || typeName !== null && typeof typeName !== "string" || scope !== null && typeof scope !== "string")
          throw new WorkflowBatchRefusal(position, "'secretReference' is malformed.");
        frozen = { secretReference: { name, typeName: typeName as string | null, scope: scope as string | null } };
      } else throw new WorkflowBatchRefusal(position, "'value' must hold exactly one of 'literal' or 'secretReference'.");
      return { index: position, kind: "set-property", risk, nodeId: text("nodeId"), property, inputName: text("inputName"), typeName: text("typeName"), value: frozen };
    }
    case "update": {
      const fields = value.fields;
      if (!isPlainRecord(fields) || Object.keys(fields).length === 0) throw new WorkflowBatchRefusal(position, "'fields' must set at least one allowed field.");
      const allowed = ["displayName", "description", "canStartWorkflow"];
      const unknown = Object.keys(fields).filter(key => !allowed.includes(key));
      if (unknown.length) throw new WorkflowBatchRefusal(position, `Unknown field(s) ${unknown.map(key => `'${key}'`).join(", ")}; an activity's type, version and children are never rewritten.`);
      if ("displayName" in fields && typeof fields.displayName !== "string" || "description" in fields && typeof fields.description !== "string"
        || "canStartWorkflow" in fields && typeof fields.canStartWorkflow !== "boolean")
        throw new WorkflowBatchRefusal(position, "'fields' holds a value of the wrong type.");
      const startInput = optionalText("startInput");
      if (("canStartWorkflow" in fields) !== (startInput !== null)) throw new WorkflowBatchRefusal(position, "'startInput' must name the declared start input exactly when 'canStartWorkflow' is set.");
      if (startInput !== null && structuralFields.has(startInput)) throw new WorkflowBatchRefusal(position, `'${startInput}' is not an input.`);
      return { index: position, kind: "update", risk, nodeId: text("nodeId"), fields: { ...fields } as { displayName?: string; description?: string; canStartWorkflow?: boolean }, startInput };
    }
    case "remove":
      return { index: position, kind: "remove", risk, nodeId: text("nodeId") };
    case "connect":
      return { index: position, kind: "connect", risk, source: text("source"), target: text("target"), sourcePort: text("sourcePort"),
        targetPort: optionalText("targetPort"), scopeOwnerId: text("scopeOwnerId"), slot: text("slot") };
    case "disconnect":
      return { index: position, kind: "disconnect", risk, source: text("source"), target: text("target"), sourcePort: text("sourcePort") };
  }
}

/**
 * Studio's own risk classification of a frozen batch against the live working draft (ADR 0003). It never trusts the
 * labels it was given: removals and disconnections are destructive, and so is a set-root that replaces a root the live
 * draft has. Callers compare the result with the reviewed labels and fail closed on any difference.
 */
export function classifyWorkflowOperationRisk(draft: WorkflowDraft, batch: FrozenWorkflowBatch): WorkflowOperationRisk[] {
  let hasRoot = !!draft.state.rootActivity;
  return batch.operations.map(operation => {
    if (operation.kind === "remove" || operation.kind === "disconnect") return "destructive";
    if (operation.kind === "set-root") {
      const replaces = hasRoot;
      hasRoot = true;
      return replaces ? "destructive" : "reviewRequired";
    }
    return "reviewRequired";
  });
}

/** Applies every operation of a frozen batch to one copy of the draft, or throws a {@link WorkflowBatchRefusal}. */
export function applyWorkflowOperationBatch(draft: WorkflowDraft, batch: FrozenWorkflowBatch, catalog: ActivityCatalogItem[]): WorkflowBatchApplyResult {
  const catalogByVersion = new Map(catalog.map(item => [item.activityVersionId, item]));
  const next: WorkflowDraft = clone(draft);
  let root: ActivityNode | null = next.state.rootActivity ?? null;
  let layout: DesignMetadataRecord[] = next.layout;
  let presentation = next.activityPresentation ?? [];
  // Every id the document already uses, in the tree or in a side table: a new node never reuses one of them, and
  // neither does it reuse an id this batch removes.
  const reserved = new Set<string>([...(root ? collectOwnedNodeIds(root, catalogByVersion) : []), ...layout.map(record => record.nodeId), ...presentation.map(record => record.nodeId)]);
  const rootCandidates = new Map<string, ActivityNode>();
  const added: string[] = [];
  let rootInstalled = false;
  let addedRoot = false;
  const removed = new Set<string>();

  for (const operation of batch.operations) {
    const refuse = (message: string): never => { throw new WorkflowBatchRefusal(operation.index, message); };
    const find = (nodeId: string): ActivityNode => (root ? findActivityNode(root, nodeId, catalogByVersion) : null) ?? refuse(`No activity '${nodeId}' exists in the working draft.`);
    switch (operation.kind) {
      case "add": {
        if (reserved.has(operation.nodeId)) refuse(`The new node id '${operation.nodeId}' is already used.`);
        const item = catalogByVersion.get(operation.activityVersionId);
        if (!item || item.activityTypeKey !== operation.activityTypeKey || item.version !== operation.version)
          return refuse(`The activity '${operation.activityTypeKey}' ${operation.version} is not in this designer's catalog.`);
        if (item.available === false) refuse(`'${item.displayName}' cannot be added here: ${item.availabilityReason ?? "the availability policy excludes it"}.`);
        const node = createActivityNode(item, operation.nodeId);
        reserved.add(operation.nodeId);
        if (operation.scopePath.length === 0) {
          if (operation.slot !== "root") refuse("A root candidate needs the 'root' slot.");
          rootCandidates.set(operation.nodeId, node);
        } else {
          if (!root || operation.scopePath[0] !== root.nodeId) return refuse("The scope path does not start at the root.");
          let owner: ActivityNode = root;
          for (const segment of operation.scopePath.slice(1)) {
            const holding = getChildSlots(owner, catalogByVersion).find(candidate => candidate.activities.some(child => child.nodeId === segment));
            if (!holding) return refuse(`The scope path is not a containment chain at '${segment}'.`);
            owner = holding.activities.find(child => child.nodeId === segment)!;
          }
          // As the server prepares it: nothing is added into an activity this batch adds, whose slots it could not confirm.
          if (added.includes(owner.nodeId) || rootCandidates.has(owner.nodeId) || owner === root && addedRoot)
            refuse(`'${owner.nodeId}' is added by this batch; activities are added into it in a following batch.`);
          const slot = getChildSlots(owner, catalogByVersion).find(candidate => candidate.id === operation.slot);
          if (!slot) return refuse(`'${owner.nodeId}' has no slot '${operation.slot}'.`);
          if (slot.mode === "bpmn") refuse("BPMN scopes are not changed by an Operation Batch.");
          if (slot.cardinality === "single" && slot.activities.length > 0) refuse(`The slot '${operation.slot}' holds a single activity and is occupied.`);
          root = updateActivity(root, owner.nodeId, current => {
            const target = getChildSlots(current, catalogByVersion).find(candidate => candidate.id === operation.slot)!;
            return replaceSlotActivities(current, target, [...target.activities, node]);
          }, catalogByVersion);
        }
        if (operation.position) layout = upsertLayout(layout, operation.nodeId, operation.position);
        added.push(operation.nodeId);
        break;
      }
      case "set-root": {
        if (rootInstalled) refuse("A batch installs at most one root.");
        const candidate = rootCandidates.get(operation.nodeId) ?? refuse(`'${operation.nodeId}' is not a root candidate of this batch.`);
        if (operation.replacesRoot !== !!root) refuse("The working draft's root changed since the batch was reviewed.");
        if (root) for (const id of collectOwnedNodeIds(root, catalogByVersion)) removed.add(id);
        rootCandidates.delete(operation.nodeId);
        root = candidate;
        rootInstalled = true;
        addedRoot = true;
        break;
      }
      case "position":
        find(operation.nodeId);
        layout = upsertLayout(layout, operation.nodeId, operation);
        break;
      case "set-property": {
        const node = find(operation.nodeId);
        const input = catalogInputs(catalogByVersion.get(node.activityVersionId)).find(candidate => candidate.referenceKey === operation.property)
          ?? refuse(`'${operation.inputName}' is not a declared input of this activity in the designer's catalog.`);
        if ("literal" in operation.value && input.isCredential) refuse(`'${operation.inputName}' is a credential input; only a Secret Reference may be bound.`);
        const expression = "literal" in operation.value
          ? { type: "Literal", value: operation.value.literal }
          : { type: "Secret", value: secretValue(operation.value.secretReference) };
        // Only the authored value changes: an existing wrapper keeps its type, memory reference, conversion and extras.
        root = updateActivity(root!, node.nodeId, current => {
          const previous = current[operation.property];
          const wrapper = isPlainRecord(previous) && isPlainRecord(previous.expression) ? previous : null;
          return { ...current, [operation.property]: wrapper
            ? { ...wrapper, typeName: typeof wrapper.typeName === "string" && wrapper.typeName ? wrapper.typeName : operation.typeName, expression }
            : { typeName: operation.typeName, expression } };
        }, catalogByVersion);
        break;
      }
      case "update": {
        const node = find(operation.nodeId);
        if (operation.fields.displayName !== undefined || operation.fields.description !== undefined) {
          const current = presentation.find(record => record.nodeId === node.nodeId);
          presentation = updateActivityPresentation(presentation, node.nodeId, {
            displayName: operation.fields.displayName ?? current?.displayName ?? undefined,
            description: operation.fields.description ?? current?.description ?? undefined
          });
        }
        if (operation.fields.canStartWorkflow !== undefined) {
          const startInput = operation.startInput!;
          if (!catalogInputs(catalogByVersion.get(node.activityVersionId)).some(input => input.referenceKey === startInput))
            refuse("The activity declares no start input in the designer's catalog.");
          const value = operation.fields.canStartWorkflow;
          root = updateActivity(root!, node.nodeId, current => ({ ...current, [startInput]: { typeName: "Boolean", expression: { type: "Literal", value } } }), catalogByVersion);
        }
        break;
      }
      case "remove": {
        const node = find(operation.nodeId);
        if (node.nodeId === root!.nodeId) refuse("The root cannot be removed.");
        const owner = parentOf(root!, node.nodeId, catalogByVersion) ?? refuse(`'${node.nodeId}' has no container.`);
        if (owner.slot.mode === "bpmn") refuse("BPMN scopes are not changed by an Operation Batch.");
        const owned = collectOwnedNodeIds(node, catalogByVersion);
        for (const id of owned) removed.add(id);
        root = updateActivity(root!, owner.owner.nodeId, current => {
          const slot = getChildSlots(current, catalogByVersion).find(candidate => candidate.id === owner.slot.id)!;
          return withoutConnections(replaceSlotActivities(current, slot, slot.activities.filter(child => child.nodeId !== node.nodeId)), owned);
        }, catalogByVersion);
        break;
      }
      case "connect": {
        const owner = root ? findActivityNode(root, operation.scopeOwnerId, catalogByVersion) : null;
        if (!owner || owner.structure?.kind !== flowchartStructureKind) return refuse(`'${operation.scopeOwnerId}' is not a flowchart.`);
        if (added.includes(owner.nodeId) || owner === root && addedRoot) refuse(`'${owner.nodeId}' is added by this batch; its activities are connected in a following batch.`);
        const slot = getChildSlots(owner, catalogByVersion).find(candidate => candidate.id === operation.slot);
        if (!slot || slot.mode !== "flowchart") return refuse(`'${operation.slot}' is not the flowchart's slot.`);
        if (!slot.activities.some(child => child.nodeId === operation.source) || !slot.activities.some(child => child.nodeId === operation.target))
          refuse("The connected activities are not both in this flowchart.");
        if (readConnections(owner).some(connection => connectionMatches(connection, operation.source, operation.target, operation.sourcePort)))
          refuse("The activities are already connected from this port.");
        root = updateActivity(root!, owner.nodeId, current => withConnections(current, [...readConnections(current), {
          id: `flow-${operation.source}-${operation.target}-${operation.sourcePort}-b${operation.index}`,
          source: { nodeId: operation.source, port: operation.sourcePort },
          target: operation.targetPort ? { nodeId: operation.target, port: operation.targetPort } : { nodeId: operation.target }
        }]), catalogByVersion);
        break;
      }
      case "disconnect": {
        find(operation.source);
        const owner = parentOf(root!, operation.source, catalogByVersion);
        if (!owner || owner.owner.structure?.kind !== flowchartStructureKind) return refuse("The source activity is not in a flowchart.");
        const matches = readConnections(owner.owner).filter(connection => connectionMatches(connection, operation.source, operation.target, operation.sourcePort));
        if (matches.length !== 1) refuse(matches.length ? "Several identical connections match; the batch cannot choose one." : "The connection no longer exists.");
        root = updateActivity(root!, owner.owner.nodeId, current => withConnections(current,
          readConnections(current).filter(connection => !connectionMatches(connection, operation.source, operation.target, operation.sourcePort))), catalogByVersion);
        break;
      }
    }
  }

  if (rootCandidates.size > 0) throw new WorkflowBatchRefusal(null, "A root candidate was added but never installed with set-root.");
  if (!root) throw new WorkflowBatchRefusal(null, "The batch did not produce a root activity.");

  const reachable = collectOwnedNodeIds(root, catalogByVersion);
  next.state = { ...next.state, rootActivity: root };
  next.layout = layout.filter(record => !removed.has(record.nodeId) || reachable.has(record.nodeId));
  next.activityPresentation = presentation.filter(record => reachable.has(record.nodeId));
  next.sourceVersionId = null;
  return { draft: next, appliedCount: batch.operations.length, addedNodeIds: added, removedNodeIds: [...removed].filter(id => !reachable.has(id)) };
}

/**
 * The single-operation change of `workflow_set_designer_position`, through the same strict applier. Kept for the
 * existing layout tool and its receipt.
 */
export function applyWorkflowDesignerPosition(draft: WorkflowDraft, activityId: string, x: number, y: number, catalog: ActivityCatalogItem[]) {
  return applyWorkflowOperationBatch(draft, { schemaVersion: "1", highComplexity: false, risk: "reviewRequired",
    operations: [{ index: 0, kind: "position", risk: "reviewRequired", nodeId: activityId, x, y }] }, catalog);
}

function parentOf(root: ActivityNode, nodeId: string, catalog: Map<string, ActivityCatalogItem>): { owner: ActivityNode; slot: ChildSlot } | null {
  for (const slot of getChildSlots(root, catalog)) {
    if (slot.activities.some(child => child.nodeId === nodeId)) return { owner: root, slot };
    for (const child of slot.activities) {
      const found = parentOf(child, nodeId, catalog);
      if (found) return found;
    }
  }
  return null;
}

interface CatalogInput { referenceKey: string; name: string; isCredential: boolean }

function catalogInputs(item: ActivityCatalogItem | undefined): CatalogInput[] {
  return (item?.inputs ?? []).flatMap(input => isPlainRecord(input) && typeof input.referenceKey === "string"
    ? [{ referenceKey: input.referenceKey, name: typeof input.name === "string" ? input.name : input.referenceKey, isCredential: input.isCredential === true }]
    : []);
}

function secretValue(reference: { name: string; typeName: string | null; scope: string | null }) {
  return { name: reference.name, ...(reference.typeName ? { typeName: reference.typeName } : {}), ...(reference.scope ? { scope: reference.scope } : {}) };
}

type FlowConnection = Record<string, unknown>;

function readConnections(owner: ActivityNode): FlowConnection[] {
  const connections = owner.structure?.payload.connections;
  return Array.isArray(connections) ? connections.filter(isPlainRecord) : [];
}

function withConnections(owner: ActivityNode, connections: FlowConnection[]): ActivityNode {
  if (!owner.structure) return owner;
  return { ...owner, structure: { ...owner.structure, payload: { ...owner.structure.payload, connections } } };
}

/** Drops the connections of a flowchart that touch any removed id, so a removal leaves no dangling wire. */
function withoutConnections(owner: ActivityNode, removed: Set<string>): ActivityNode {
  if (owner.structure?.kind !== flowchartStructureKind) return owner;
  return withConnections(owner, readConnections(owner).filter(connection => {
    const source = isPlainRecord(connection.source) ? connection.source.nodeId : null;
    const target = isPlainRecord(connection.target) ? connection.target.nodeId : null;
    return !(typeof source === "string" && removed.has(source)) && !(typeof target === "string" && removed.has(target));
  }));
}

function connectionMatches(connection: FlowConnection, source: string, target: string, port: string) {
  const from = isPlainRecord(connection.source) ? connection.source : null;
  const to = isPlainRecord(connection.target) ? connection.target : null;
  return !!from && from.nodeId === source && to?.nodeId === target && (typeof from.port === "string" && from.port ? from.port : "Done") === port;
}

function upsertLayout(layout: DesignMetadataRecord[], nodeId: string, point: FrozenPoint) {
  // The designer's own convention for authored positions: whole pixels, kept clear of the canvas edge.
  const x = Math.max(40, Math.round(point.x));
  const y = Math.max(40, Math.round(point.y));
  return layout.some(record => record.nodeId === nodeId)
    ? layout.map(record => record.nodeId === nodeId ? { ...record, x, y } : record)
    : [...layout, { nodeId, x, y }];
}

function exactKeys(index: number | null, value: Record<string, unknown>, keys: string[]) {
  const unknown = Object.keys(value).filter(key => !keys.includes(key));
  if (unknown.length) throw new WorkflowBatchRefusal(index, `Unknown field(s) ${unknown.map(key => `'${key}'`).join(", ")}.`);
  const missing = keys.filter(key => !Object.prototype.hasOwnProperty.call(value, key));
  if (missing.length) throw new WorkflowBatchRefusal(index, `Missing field(s) ${missing.map(key => `'${key}'`).join(", ")}.`);
}

function isRisk(value: unknown): value is WorkflowOperationRisk {
  return value === "reviewRequired" || value === "destructive";
}

function isPlainRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function clone<T>(value: T): T {
  return typeof structuredClone === "function" ? structuredClone(value) : JSON.parse(JSON.stringify(value)) as T;
}
