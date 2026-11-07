import type { ActivityNode, DesignMetadataRecord, VariableDefinition } from "../workflowTypes";
import { getChildSlots } from "../workflowAdapter";
import {
  bpmnDocumentSchemaVersion, bpmnNamespaces as ns, readBpmnDocument,
  type BpmnDocumentBinding, type BpmnDocumentPayload, type BpmnScopeHeader
} from "./bpmnDocument";
import { bpmnStructureKind, isSubProcessElementType, type BpmnElement, type BpmnSequenceFlow } from "./bpmnTypes";

type RecordValue = Record<string, unknown>;
type LegacyScope = {
  owner: ActivityNode;
  scopeId: string;
  elements: BpmnElement[];
  flows: BpmnSequenceFlow[];
  pools: RecordValue[];
  lanes: RecordValue[];
  messages: RecordValue[];
  diagram: RecordValue;
};

const vendor = "https://elsa-workflows.io/schemas/bpmn";
const xmlns = "http://www.w3.org/2000/xmlns/";
const payloadFields = ["activities", "elements", "sequenceFlows", "pools", "lanes", "variables", "diagram", "isTransaction", "messageFlows"];
const elementFields = ["elementId", "elementType", "name", "childNodeId", "laneId", "defaultFlowId", "eventDefinitions", "properties",
  "attachedToRef", "cancelActivity", "loopCharacteristics", "isForCompensation", "compensationHandlerElementId", "isTransaction", "triggeredByEvent", "listenerNodeId"];
const conditionPrefix = "elsa.bpmn.retained-condition:";

function fail(message: string): never { throw new Error(`Cannot upgrade this BPMN draft: ${message}. The original process is retained.`); }
function record(value: unknown, label: string): RecordValue {
  if (!value || typeof value !== "object" || Array.isArray(value)) fail(`${label} must be an object`);
  return value as RecordValue;
}
function rows(value: unknown, label: string): RecordValue[] {
  if (value === undefined || value === null) return [];
  if (!Array.isArray(value)) fail(`${label} must be an array`);
  return value.map(item => record(item, label));
}
function text(value: unknown, label: string): string {
  if (typeof value !== "string" || !value.trim()) fail(`${label} is missing`);
  return value;
}
function known(value: RecordValue, keys: readonly string[], label: string) {
  for (const key of Object.keys(value)) if (!keys.includes(key) && value[key] !== undefined)
    fail(`${label} has retained '${key}' content with no qualified XML mapping`);
}
function clone<T>(value: T): T { return structuredClone(value); }

/**
 * A conversion plan is a detached preview. Merely requesting it never writes to the draft, allocates
 * native identities or creates history. Its result is admitted together with the first real edit.
 * The existing server/library XML writer remains responsible for legacy BPMN semantics.
 */
export interface BpmnDocumentMigrationPlan {
  readonly processId: string;
  readonly exportOwner: ActivityNode;
  complete(documentXml: string): ActivityNode;
}

export function planLegacyBpmnDocumentMigration(owner: ActivityNode, layout: readonly DesignMetadataRecord[] = []): BpmnDocumentMigrationPlan {
  if (owner.structure?.kind !== bpmnStructureKind || owner.structure.schemaVersion !== "1.0.0")
    fail("the legacy authoring version is unavailable");
  const source = clone(owner);
  const scopes: LegacyScope[] = [];
  const nativeActivities: ActivityNode[] = [];
  const bindings: BpmnDocumentBinding[] = [];
  const headers: BpmnScopeHeader[] = [];
  const ids = new Set<string>();
  const nativeIds = new Set<string>([source.nodeId]);
  const activeScopes = new Set<ActivityNode>();
  const idDocument = new DOMParser().parseFromString("<root/>", "application/xml");
  function identity(value: unknown, label: string) {
    const result = text(value, label);
    try { if (result.includes(":")) fail(`${label} '${result}' is not an XML ID`); idDocument.createElement(result); }
    catch { fail(`${label} '${result}' is not an XML ID`); }
    if (ids.has(result)) fail(`standard identity '${result}' occurs in more than one scope`);
    ids.add(result);
    return result;
  }
  function nativeIdentity(node: ActivityNode) {
    const id = text(node.nodeId, "native node identity");
    text(node.activityVersionId, "native activity version");
    if (nativeIds.has(id)) fail(`native identity '${id}' occurs more than once`);
    nativeIds.add(id);
  }
  function inventory(node: ActivityNode, active = new Set<ActivityNode>()) {
    if (active.has(node)) fail("the native activity forest contains a cycle");
    nativeIdentity(node);
    const next = new Set(active).add(node);
    for (const child of getChildSlots(node).flatMap(slot => slot.activities)) inventory(child, next);
  }
  function scan(node: ActivityNode, scopeId: string): ActivityNode {
    if (activeScopes.has(node)) fail("standard subprocess ownership contains a cycle");
    activeScopes.add(node);
    if (node.structure?.kind !== bpmnStructureKind || node.structure.schemaVersion !== "1.0.0")
      fail(`standard body '${scopeId}' has an unsupported source version`);
    known(node.structure as unknown as RecordValue, ["kind", "schemaVersion", "payload"], `structure '${scopeId}'`);
    const payload = record(node.structure.payload, "legacy payload");
    known(payload, payloadFields, `scope '${scopeId}'`);
    const elements = rows(payload.elements, "elements") as unknown as BpmnElement[];
    const flows = rows(payload.sequenceFlows, "sequence flows") as unknown as BpmnSequenceFlow[];
    const pools = rows(payload.pools, "pools"), lanes = rows(payload.lanes, "lanes"), messages = rows(payload.messageFlows, "message flows");
    for (const element of elements) {
      identity(element.elementId, "element"); known(element, elementFields, `element '${element.elementId}'`);
      text(element.elementType, "element type");
      for (const definition of rows(element.eventDefinitions, "event definitions")) {
        known(definition, ["type", "properties"], `event '${element.elementId}'`);
        const properties = definition.properties == null ? {} : record(definition.properties, "event properties");
        known(properties, ["name", "interval", "cron", "cycle", "activityRef", "code"], `event '${element.elementId}' properties`);
      }
      if (element.loopCharacteristics != null) known(record(element.loopCharacteristics, "loop"),
        ["isSequential", "cardinality", "collectionVariable", "itemVariable"], `loop '${element.elementId}'`);
      for (const key of Object.keys(element.properties ?? {}))
        if (!["calledElement", "messageName", "errorRef", "elsa.direct.binding"].includes(key) && !key.startsWith(conditionPrefix))
          fail(`element '${element.elementId}' property '${key}' has no qualified XML mapping`);
    }
    for (const flow of flows) {
      identity(flow.flowId, "flow"); known(flow, ["flowId", "sourceRef", "targetRef", "name", "conditionOutcome", "isDefault"], `flow '${flow.flowId}'`);
    }
    for (const pool of pools) { identity(pool.poolId, "pool"); known(pool, ["poolId", "name", "processRef", "isExecutable"], "pool"); }
    for (const lane of lanes) { identity(lane.laneId, "lane"); known(lane, ["laneId", "poolId", "name"], "lane"); }
    for (const flow of messages) { identity(flow.flowId, "message flow"); known(flow,
      ["flowId", "name", "sourceElementId", "sourcePoolId", "targetElementId", "targetPoolId", "messageName"], "message flow"); }
    const diagram = payload.diagram == null ? {} : record(payload.diagram, "diagram");
    known(diagram, ["shapes", "edges"], `diagram '${scopeId}'`);
    scopes.push({ owner: node, scopeId, elements, flows, pools, lanes, messages, diagram });
    const children = rows(payload.activities, "activities") as unknown as ActivityNode[];
    const byId = new Map<string, ActivityNode>();
    for (const child of children) {
      if (byId.has(child.nodeId)) fail(`native identity '${child.nodeId}' is repeated in '${scopeId}'`);
      byId.set(child.nodeId, child);
    }
    const used = new Set<string>();
    const exportedChildren: ActivityNode[] = [];
    for (const element of elements) {
      const binding: BpmnDocumentBinding = { elementId: element.elementId };
      for (const channel of ["childNodeId", "listenerNodeId"] as const) {
        const id = element[channel];
        if (id == null) continue;
        const child = typeof id === "string" ? byId.get(id) : undefined;
        if (!child || used.has(child.nodeId)) fail(`element '${element.elementId}' has an unavailable or shared native ${channel}`);
        used.add(child.nodeId);
        if (channel === "childNodeId" && isSubProcessElementType(element.elementType) && child.structure?.kind === bpmnStructureKind) {
          nativeIdentity(child);
          const { structure: _structure, ...invocation } = child;
          const variables = rows(child.structure.payload.variables, "scope variables") as unknown as VariableDefinition[];
          headers.push({ ...invocation, scopeElementId: element.elementId, variables });
          exportedChildren.push(scan(child, element.elementId));
        } else {
          inventory(child);
          nativeActivities.push(child);
          binding[channel] = child.nodeId;
          // Ordinary tasks bound to a BpmnProcess or Sequence are native children, not standard
          // subprocess bodies. Do not let the legacy exporter's broad nested-process test inline them.
          exportedChildren.push({ ...child, inputs: [], outputs: [], structure: null });
        }
      }
      if (binding.childNodeId || binding.listenerNodeId) bindings.push(binding);
    }
    if (children.some(child => !used.has(child.nodeId))) fail(`scope '${scopeId}' contains an unbound native activity`);
    activeScopes.delete(node);
    return { ...node, inputs: [], outputs: [], structure: { ...node.structure, payload: { ...payload, activities: exportedChildren } } };
  }
  // Reserve all standard IDs before allocating representation IDs. Legacy owner identities remain
  // opaque and unchanged; they are never sanitized into a different native identity.
  const exported = scan(source, "");
  function allocate(seed: string) {
    let candidate = seed;
    for (let suffix = 1; ids.has(candidate); suffix++) candidate = `${seed}_${suffix}`;
    ids.add(candidate); return candidate;
  }
  const rootScope = scopes[0];
  const processRefs = [...new Set(rootScope.pools.map(pool => pool.processRef).filter((value): value is string => typeof value === "string" && !!value))];
  const processId = processRefs.length === 1 ? identity(processRefs[0], "process") : allocate("ElsaProcess");
  rootScope.scopeId = processId;
  const layoutById = new Map<string, DesignMetadataRecord>();
  for (const position of layout) {
    if (layoutById.has(position.nodeId)) fail(`layout '${position.nodeId}' is repeated`);
    layoutById.set(position.nodeId, clone(position));
  }

  return { processId, exportOwner: clone(exported), complete(documentXml) {
    const candidate = (): ActivityNode => ({ ...source, structure: { ...source.structure!, schemaVersion: bpmnDocumentSchemaVersion,
      payload: { documentXml, processId, activities: clone(nativeActivities), bindings: clone(bindings),
        variables: clone((rootScope.owner.structure!.payload.variables ?? []) as VariableDefinition[]), scopeHeaders: clone(headers) } satisfies BpmnDocumentPayload } });
    // The strict source reader checks XML IDs, binding ownership and complete header chains before
    // anything is written to the host. DTDs and malformed input cannot reach DOM mutation.
    const parsed = readBpmnDocument(candidate());
    const document = parsed.document;
    if (document.documentElement.lookupNamespaceURI("elsa") && document.documentElement.lookupNamespaceURI("elsa") !== vendor)
      fail("the XML writer uses a conflicting Elsa namespace prefix");
    document.documentElement.setAttributeNS(xmlns, "xmlns:elsa", vendor);
    const existingIds = new Set(parsed.byId.keys());
    const xmlIds = new Set([...ids, ...existingIds]);
    const allocate = (seed: string) => {
      let candidate = seed;
      for (let suffix = 1; xmlIds.has(candidate); suffix++) candidate = `${seed}_${suffix}`;
      xmlIds.add(candidate); return candidate;
    };
    const elementById = (id: unknown, label: string) => {
      const element = parsed.byId.get(text(id, label));
      if (!element || element.namespaceURI !== ns.model) fail(`${label} '${id}' was omitted by the XML writer`);
      return element;
    };
    const set = (element: Element, name: string, value: unknown) => {
      if (value === null || value === undefined) element.removeAttribute(name);
      else element.setAttribute(name, String(value));
    };
    for (const scope of scopes) {
      const body = elementById(scope.scopeId, "scope");
      if (scope.owner.structure!.payload.isTransaction === true && body.localName !== "transaction")
        body.setAttributeNS(vendor, "elsa:isTransaction", "true");
      for (const row of [...scope.elements, ...scope.flows]) {
        const id = "elementId" in row ? row.elementId : row.flowId;
        const element = elementById(id, "element");
        if (element.parentElement !== body) fail(`element '${id}' changed its standard scope`);
        set(element, "name", row.name); // The legacy typed reader trims; migration preserves authored text.
      }
      for (const flow of scope.flows) {
        const element = elementById(flow.flowId, "sequence flow");
        if (element.getAttribute("sourceRef") !== flow.sourceRef || element.getAttribute("targetRef") !== flow.targetRef)
          fail(`flow '${flow.flowId}' changed its endpoints`);
        if (flow.conditionOutcome != null) element.setAttributeNS(vendor, "elsa:conditionOutcome", flow.conditionOutcome);
        // Bpmn.Interchange0.5.0 writes a redundant predicate for its Elsa outcome attribute.
        // This legacy graph did not author that expression. Remove only that exact generated
        // representation; explicitly retained author expressions and any opaque content survive.
        const retainedCondition = scope.elements.some(source => Object.hasOwn(source.properties ?? {}, conditionPrefix + flow.flowId));
        if (flow.conditionOutcome && !retainedCondition) {
          for (const condition of [...element.children].filter(child => child.namespaceURI === ns.model && child.localName === "conditionExpression")) {
            if ([...condition.attributes].every(attribute => attribute.namespaceURI === xmlns)
              && [...condition.childNodes].every(child => child.nodeType === Node.TEXT_NODE)
              && condition.textContent === `outcome == '${flow.conditionOutcome}'`) condition.remove();
          }
        }
      }
      for (const pool of scope.pools) {
        const element = elementById(pool.poolId, "pool"); set(element, "name", pool.name); set(element, "processRef", pool.processRef);
        if (pool.isExecutable !== undefined) element.setAttributeNS(vendor, "elsa:isExecutable", String(pool.isExecutable));
      }
      for (const lane of scope.lanes) {
        const element = elementById(lane.laneId, "lane"); set(element, "name", lane.name);
        if (lane.poolId != null) element.setAttributeNS(vendor, "elsa:poolId", String(lane.poolId));
      }
      for (const message of scope.messages) {
        const element = elementById(message.flowId, "message flow"); set(element, "name", message.name);
        for (const field of ["sourcePoolId", "targetPoolId"])
          if (message[field] != null) element.setAttributeNS(vendor, `elsa:${field}`, String(message[field]));
      }
    }
    // Portable task-binding metadata is irrelevant to an internal upgrade: exact native objects are
    // already in the disjoint forest. Never introduce a second binding authority or export their inputs.
    for (const element of [...document.getElementsByTagNameNS(ns.model, "*")]) element.removeAttributeNS(vendor, "taskBinding");
    for (const variable of [...document.getElementsByTagNameNS(vendor, "variable")]) variable.remove();
    for (const extension of [...document.getElementsByTagNameNS(ns.model, "extensionElements")])
      if (!extension.children.length && !extension.textContent?.trim()) extension.remove();
    function node(namespaceURI: string, prefix: string, name: string, attributes: RecordValue = {}) {
      const element = document.createElementNS(namespaceURI, `${prefix}:${name}`);
      for (const [key, value] of Object.entries(attributes)) set(element, key, value);
      return element;
    }
    function finite(value: unknown, label: string) {
      if (typeof value !== "number" || !Number.isFinite(value)) fail(`${label} must be finite`);
      return value;
    }
    function bounds(value: unknown, label: string) {
      const raw = record(value, label); known(raw, ["x", "y", "width", "height"], label);
      const result = { x: finite(raw.x, label), y: finite(raw.y, label), width: finite(raw.width, label), height: finite(raw.height, label) };
      if (result.width <= 0 || result.height <= 0) fail(`${label} requires positive dimensions`);
      return node(ns.bounds, "dc", "Bounds", result);
    }
    function label(parent: Element, value: unknown) {
      if (value == null) return;
      const element = node(ns.diagram, "bpmndi", "BPMNLabel"); element.append(bounds(value, "label bounds")); parent.append(element);
    }
    // The old server exporter only reads shape bounds and edge waypoints. Upgrade DI from the raw
    // authored records instead, including labels, markers and exact fractional coordinates.
    for (const diagram of [...document.getElementsByTagNameNS(ns.diagram, "BPMNDiagram")]) diagram.remove();
    for (const scope of scopes) {
      const diagram = node(ns.diagram, "bpmndi", "BPMNDiagram", { id: allocate("ElsaDiagram") });
      let planeElement = scope.scopeId;
      if (scope.pools.length || scope.messages.length) {
        const members = [...scope.pools.map(pool => elementById(pool.poolId, "pool")), ...scope.messages.map(message => elementById(message.flowId, "message flow"))];
        const collaboration = members[0].parentElement;
        if (!collaboration || collaboration.namespaceURI !== ns.model || collaboration.localName !== "collaboration"
          || members.some(member => member.parentElement !== collaboration) || !collaboration.id)
          fail("collaboration members have no shared XML collaboration owner");
        planeElement = collaboration.id;
      }
      const plane = node(ns.diagram, "bpmndi", "BPMNPlane", { id: allocate("ElsaPlane"), bpmnElement: planeElement });
      diagram.append(plane);
      const rawShapes = scope.diagram.shapes == null ? {} : record(scope.diagram.shapes, "shapes");
      const rawEdges = scope.diagram.edges == null ? {} : record(scope.diagram.edges, "edges");
      const shapeIds = [...scope.pools.map(pool => text(pool.poolId, "pool")), ...scope.lanes.map(lane => text(lane.laneId, "lane")), ...scope.elements.map(element => element.elementId)];
      const edgeIds = [...scope.flows.map(flow => flow.flowId), ...scope.messages.map(flow => text(flow.flowId, "message flow"))];
      for (const id of Object.keys(rawShapes)) if (!shapeIds.includes(id)) fail(`diagram retains an unavailable shape '${id}'`);
      for (const id of Object.keys(rawEdges)) if (!edgeIds.includes(id)) fail(`diagram retains an unavailable edge '${id}'`);
      const geometry = new Map<string, { x: number; y: number; width: number; height: number }>();
      for (const [index, id] of shapeIds.entries()) {
        const raw = rawShapes[id] == null ? {} : record(rawShapes[id], `shape '${id}'`);
        known(raw, ["x", "y", "width", "height", "label", "isExpanded", "isHorizontal", "isMarkerVisible", "isMessageVisible", "participantBandKind", "choreographyActivityShape"], `shape '${id}'`);
        const element = scope.elements.find(element => element.elementId === id);
        const position = layoutById.get(id);
        const event = element?.elementType.endsWith("Event"), gateway = element?.elementType.endsWith("Gateway");
        const shapeBounds = { x: position?.x ?? raw.x ?? 100 + index % 5 * 200, y: position?.y ?? raw.y ?? 100 + Math.floor(index / 5) * 140,
          width: raw.width ?? position?.width ?? (event ? 36 : gateway ? 50 : 100), height: raw.height ?? position?.height ?? (event ? 36 : gateway ? 50 : 80) };
        const shape = node(ns.diagram, "bpmndi", "BPMNShape", { id: allocate("ElsaShape"), bpmnElement: id,
          ...(element && isSubProcessElementType(element.elementType) ? { isExpanded: false } : {}),
          ...Object.fromEntries(Object.entries(raw).filter(([key]) => !["x", "y", "width", "height", "label"].includes(key))) });
        shape.append(bounds(shapeBounds, `shape '${id}'`)); label(shape, raw.label); plane.append(shape);
        geometry.set(id, shapeBounds as { x: number; y: number; width: number; height: number });
      }
      for (const id of edgeIds) {
        const raw = rawEdges[id] == null ? {} : record(rawEdges[id], `edge '${id}'`);
        known(raw, ["waypoints", "label", "messageVisibleKind", "sourceElement", "targetElement"], `edge '${id}'`);
        const flow = scope.flows.find(flow => flow.flowId === id);
        const message = scope.messages.find(flow => flow.flowId === id);
        const sourceId = flow?.sourceRef ?? message?.sourceElementId ?? message?.sourcePoolId;
        const targetId = flow?.targetRef ?? message?.targetElementId ?? message?.targetPoolId;
        const start = geometry.get(sourceId as string), end = geometry.get(targetId as string);
        const points = raw.waypoints == null ? (start && end ? [{ x: start.x + start.width, y: start.y + start.height / 2 }, { x: end.x, y: end.y + end.height / 2 }] : [])
          : rows(raw.waypoints, "waypoints");
        if (points.length < 2) fail(`edge '${id}' has fewer than two available waypoints`);
        const edge = node(ns.diagram, "bpmndi", "BPMNEdge", { id: allocate("ElsaEdge"), bpmnElement: id,
          ...Object.fromEntries(Object.entries(raw).filter(([key]) => !["waypoints", "label"].includes(key))) });
        for (const point of points) {
          known(point, ["x", "y"], `waypoint '${id}'`);
          edge.append(node(ns.coordinates, "di", "waypoint", { x: finite(point.x, "waypoint x"), y: finite(point.y, "waypoint y") }));
        }
        label(edge, raw.label); plane.append(edge);
      }
      document.documentElement.append(diagram);
    }
    documentXml = new XMLSerializer().serializeToString(document);
    const result = candidate(); readBpmnDocument(result); return result;
  } };
}

/** The caller owns availability, authority, cancellation and the final source CAS. */
export async function prepareLegacyBpmnDocument(
  owner: ActivityNode,
  layout: readonly DesignMetadataRecord[],
  exportXml: (owner: ActivityNode, processId: string) => Promise<string>
): Promise<ActivityNode> {
  if (owner.structure?.kind === bpmnStructureKind && owner.structure.schemaVersion === bpmnDocumentSchemaVersion) {
    readBpmnDocument(owner); return owner;
  }
  const plan = planLegacyBpmnDocumentMigration(owner, layout);
  return plan.complete(await exportXml(plan.exportOwner, plan.processId));
}
