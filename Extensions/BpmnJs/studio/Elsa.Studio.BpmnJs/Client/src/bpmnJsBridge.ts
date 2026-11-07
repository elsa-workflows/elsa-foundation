import type { ActivityNode, DesignMetadataRecord, BpmnElement, BpmnSequenceFlow } from "@elsa-workflows/studio-workflows";

const bpmnStructureKind = "elsa.bpmn.structure";

/** Experimental, scope-local canvas bridge. The Elsa tree owns configuration; XML is a projection. */
export interface BpmnJsDefinitions {
  $type?: string;
  rootElements?: unknown[];
  diagrams?: unknown[];
  [key: string]: unknown;
}

type RecordValue = Record<string, unknown>;
type Bounds = { x: number; y: number; width: number; height: number };
type Point = { x: number; y: number };
type Snapshot = {
  payload: RecordValue;
  elements: BpmnElement[];
  flows: BpmnSequenceFlow[];
  activities: ActivityNode[];
  reservedIds: Set<string>;
  processId: string;
};

const namespace = "https://elsa-workflows.io/schemas/bpmn";
const types: Record<string, string> = {
  startEvent: "StartEvent", endEvent: "EndEvent", task: "Task", userTask: "UserTask",
  serviceTask: "ServiceTask", scriptTask: "ScriptTask", manualTask: "ManualTask",
  businessRuleTask: "BusinessRuleTask", sendTask: "SendTask", receiveTask: "ReceiveTask",
  exclusiveGateway: "ExclusiveGateway", inclusiveGateway: "InclusiveGateway",
  parallelGateway: "ParallelGateway", subProcess: "SubProcess"
};
const reverseTypes = new Map(Object.entries(types).map(([key, value]) => [`bpmn:${value}`, key]));
const advancedElementFields = ["attachedToRef", "loopCharacteristics", "compensationHandlerElementId", "listenerNodeId", "laneId"];
const advancedFlags = ["isForCompensation", "isTransaction", "triggeredByEvent"];
const xmlId = /^[\p{L}_][\p{L}\p{N}\p{M}_.-]*$/u;
const conditionPrefix = "elsa.bpmn.retained-condition:";

function refuse(reason: string): never {
  throw new Error(`The optional BPMN canvas cannot apply this process: ${reason}. The Elsa process is unchanged.`);
}
function object(value: unknown, label: string): RecordValue {
  if (value === null || typeof value !== "object" || Array.isArray(value)) refuse(`${label} must be an object`);
  return value as RecordValue;
}
function array(value: unknown, label: string): unknown[] {
  if (value === undefined) return [];
  if (!Array.isArray(value)) refuse(`${label} must be an array`);
  return value;
}
function identifier(value: unknown, label: string): string {
  if (typeof value !== "string" || !xmlId.test(value)) refuse(`${label} requires an XML-compatible identity`);
  return value;
}
function optionalText(value: unknown, label: string): string | null | undefined {
  if (value !== undefined && value !== null && typeof value !== "string") refuse(`${label} must be text`);
  return value as string | null | undefined;
}
function optionalBoolean(value: unknown, label: string): boolean | undefined {
  if (value !== undefined && typeof value !== "boolean") refuse(`${label} must be boolean`);
  return value as boolean | undefined;
}
function number(value: unknown, label: string): number {
  if (typeof value !== "number" || !Number.isFinite(value)) refuse(`${label} must be a finite number`);
  return value;
}
function bounds(value: unknown, label: string): Bounds {
  const source = object(value, label);
  const result = { x: number(source.x, label), y: number(source.y, label), width: number(source.width, label), height: number(source.height, label) };
  if (result.width <= 0 || result.height <= 0) refuse(`${label} requires positive dimensions`);
  return result;
}
function point(value: unknown, label: string): Point {
  const source = object(value, label);
  return { x: number(source.x, label), y: number(source.y, label) };
}
function unique(ids: Set<string>, value: string) {
  if (ids.has(value)) refuse(`identity '${value}' is used more than once`);
  ids.add(value);
}
function generatedId(seed: string, reserved: Set<string>): string {
  let candidate = seed;
  for (let suffix = 1; reserved.has(candidate); suffix++) candidate = `${seed}_${suffix}`;
  reserved.add(candidate);
  return candidate;
}
function projectionId(owner: ActivityNode, reserved: Set<string>): string {
  // Owner ids are opaque Elsa identities and need not be XML names. This id never writes back to Elsa.
  let hash = 2166136261;
  for (let index = 0; index < owner.nodeId.length; index++) hash = Math.imul(hash ^ owner.nodeId.charCodeAt(index), 16777619);
  return generatedId(`ElsaProcess_${(hash >>> 0).toString(16)}`, reserved);
}
function hasValue(value: unknown) { return value !== undefined && value !== null && value !== false; }
function properties(value: unknown, label: string): RecordValue {
  return value === undefined || value === null ? {} : object(value, label);
}
function validateReferences(elements: BpmnElement[], flows: BpmnSequenceFlow[], activities: ActivityNode[]) {
  const byId = new Map(elements.map(element => [element.elementId, element]));
  const childIds = new Set(activities.map(activity => activity.nodeId));
  const bound = new Set<string>();
  for (const element of elements) {
    for (const field of ["childNodeId", "listenerNodeId"] as const) {
      const id = element[field];
      if (id === undefined || id === null) continue;
      if (typeof id !== "string" || !childIds.has(id)) refuse(`element '${element.elementId}' has an unavailable ${field}`);
      if (bound.has(id)) refuse(`activity '${id}' is bound more than once`);
      bound.add(id);
    }
    if (element.defaultFlowId !== undefined && element.defaultFlowId !== null) {
      identifier(element.defaultFlowId, "BPMN default flow");
      const flow = flows.find(flow => flow.flowId === element.defaultFlowId);
      if (!flow || flow.sourceRef !== element.elementId) refuse(`element '${element.elementId}' has an invalid default flow`);
    }
  }
  for (const flow of flows) {
    if (!byId.has(flow.sourceRef) || !byId.has(flow.targetRef)) refuse(`flow '${flow.flowId}' references an unavailable element`);
    if (flow.isDefault && byId.get(flow.sourceRef)?.defaultFlowId && byId.get(flow.sourceRef)?.defaultFlowId !== flow.flowId)
      refuse(`flow '${flow.flowId}' conflicts with its source's default flow`);
  }
  for (const source of elements) {
    const defaults = flows.filter(flow => flow.sourceRef === source.elementId && (flow.isDefault || source.defaultFlowId === flow.flowId));
    if (defaults.length > 1) refuse(`element '${source.elementId}' has multiple default flows`);
    if (defaults.length && ["startEvent", "endEvent", "parallelGateway"].includes(source.elementType))
      refuse(`element '${source.elementId}' cannot declare a default flow in this profile`);
  }
  if (activities.some(activity => !bound.has(activity.nodeId))) refuse("the process contains an orphan Elsa activity binding");
}
function snapshot(owner: ActivityNode): Snapshot {
  if (typeof owner.nodeId !== "string" || !owner.nodeId || owner.structure?.kind !== bpmnStructureKind || owner.structure.schemaVersion !== "1.0.0")
    refuse("the authored structure version is unavailable");
  const payload = object(owner.structure.payload, "BPMN structure");
  const reservedIds = new Set<string>();
  const activityIds = new Set<string>([owner.nodeId]);
  // Include nested BPMN ids in the reservation set, although nested bodies stay collapsed and opaque here.
  function scan(node: ActivityNode, active: Set<ActivityNode>): { elements: BpmnElement[]; flows: BpmnSequenceFlow[]; activities: ActivityNode[] } {
    if (active.has(node)) refuse("the authored activity tree contains a cycle");
    const nextActive = new Set(active).add(node);
    const data = object(node.structure!.payload, "BPMN structure");
    const elements = array(data.elements, "BPMN elements").map(value => {
      const element = object(value, "BPMN element");
      unique(reservedIds, identifier(element.elementId, "BPMN element"));
      if (typeof element.elementType !== "string") refuse("a BPMN element type is missing");
      optionalText(element.name, "BPMN element name");
      optionalText(element.childNodeId, "Elsa child identity");
      optionalText(element.listenerNodeId, "Elsa listener identity");
      optionalText(element.defaultFlowId, "BPMN default flow");
      return element as BpmnElement;
    });
    const flows = array(data.sequenceFlows, "BPMN sequence flows").map(value => {
      const flow = object(value, "BPMN sequence flow");
      unique(reservedIds, identifier(flow.flowId, "BPMN sequence flow"));
      identifier(flow.sourceRef, "BPMN source reference");
      identifier(flow.targetRef, "BPMN target reference");
      optionalText(flow.name, "BPMN flow name");
      optionalText(flow.conditionOutcome, "BPMN condition outcome");
      if (flow.isDefault !== undefined && typeof flow.isDefault !== "boolean") refuse("a default-flow marker must be boolean");
      return flow as BpmnSequenceFlow;
    });
    const activities = array(data.activities, "Elsa activity bindings").map(value => {
      const activity = object(value, "Elsa activity binding") as ActivityNode;
      if (typeof activity.nodeId !== "string" || !activity.nodeId || typeof activity.activityVersionId !== "string") refuse("an Elsa activity identity is missing");
      if (activityIds.has(activity.nodeId)) refuse(`Elsa activity '${activity.nodeId}' is duplicated`);
      activityIds.add(activity.nodeId);
      if (activity.structure?.kind === bpmnStructureKind) {
        if (activity.structure.schemaVersion !== "1.0.0") refuse(`nested BPMN activity '${activity.nodeId}' has an unsupported schema`);
        scan(activity, nextActive);
      }
      return activity;
    });
    validateReferences(elements, flows, activities);
    return { elements, flows, activities };
  }
  const { elements, flows, activities } = scan(owner, new Set());
  if (hasValue(payload.isTransaction)) refuse("transaction processes need a supported editing profile");
  for (const key of ["pools", "lanes", "messageFlows"])
    if (array(payload[key], key).length) refuse("collaboration and lanes need a supported editing profile");
  for (const element of elements) {
    if (!Object.hasOwn(types, element.elementType)) refuse(`element '${element.elementId}' uses unsupported type '${element.elementType}'`);
    for (const field of advancedElementFields)
      if (element[field] !== undefined && element[field] !== null) refuse(`element '${element.elementId}' uses unsupported ${field}`);
    for (const field of advancedFlags)
      if (element[field] !== undefined && element[field] !== false) refuse(`element '${element.elementId}' uses unsupported ${field}`);
    if (element.cancelActivity !== undefined && element.cancelActivity !== true) refuse(`element '${element.elementId}' uses boundary interruption settings`);
    if (element.childNodeId && ["startEvent", "endEvent", "parallelGateway"].includes(element.elementType))
      refuse(`element '${element.elementId}' cannot represent an Elsa child binding in this profile`);
    const defaults = element.defaultFlowId || flows.some(flow => flow.sourceRef === element.elementId && flow.isDefault);
    if (defaults && ["startEvent", "endEvent", "parallelGateway"].includes(element.elementType))
      refuse(`element '${element.elementId}' cannot declare a default flow in this profile`);
    const events = array(element.eventDefinitions, "BPMN event definitions");
    if (events.length && (element.elementType !== "endEvent" || events.length !== 1 || object(events[0], "BPMN event").type !== "terminate" ||
      Object.keys(properties(object(events[0], "BPMN event").properties, "BPMN event properties")).length))
      refuse(`element '${element.elementId}' uses an unsupported event definition`);
    if (element.elementType === "subProcess" && !element.childNodeId) refuse(`subprocess '${element.elementId}' requires an Elsa-bound body`);
    const retained = properties(element.properties, "BPMN element properties");
    if (Object.keys(retained).some(key => key.startsWith(conditionPrefix) && flows.some(flow => flow.flowId === key.slice(conditionPrefix.length))))
      refuse(`element '${element.elementId}' retains an unsupported expression condition`);
  }
  return { payload, elements, flows, activities, reservedIds, processId: projectionId(owner, reservedIds) };
}
function escape(value: string): string { return value.replace(/[&<>"']/g, character => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&apos;" })[character]!); }
function attr(name: string, value: unknown): string { return value === undefined || value === null ? "" : ` ${name}="${escape(String(value))}"`; }
function oldDiagram(source: Snapshot): { root: RecordValue; shapes: RecordValue; edges: RecordValue } {
  const root = properties(source.payload.diagram, "BPMN diagram");
  return { root, shapes: properties(root.shapes, "BPMN shapes"), edges: properties(root.edges, "BPMN edges") };
}
function indexedLayout(layout: DesignMetadataRecord[]): Map<string, DesignMetadataRecord> {
  const result = new Map<string, DesignMetadataRecord>();
  for (const record of layout) {
    if (result.has(record.nodeId)) refuse(`layout '${record.nodeId}' is duplicated`);
    number(record.x, "layout x"); number(record.y, "layout y");
    result.set(record.nodeId, record);
  }
  return result;
}
function projectedBounds(source: Snapshot, layout: DesignMetadataRecord[]): Map<string, Bounds> {
  const diagram = oldDiagram(source);
  const records = indexedLayout(layout);
  return new Map(source.elements.map((element, index) => {
    const shape = properties(diagram.shapes[element.elementId], "BPMN shape");
    const record = records.get(element.elementId);
    const event = element.elementType.endsWith("Event");
    const gateway = element.elementType.endsWith("Gateway");
    const size = event ? [36, 36] : gateway ? [50, 50] : [element.elementType === "subProcess" ? 120 : 100, 80];
    return [element.elementId, bounds({ x: record?.x ?? shape.x ?? 100 + index % 5 * 200, y: record?.y ?? shape.y ?? 100 + Math.floor(index / 5) * 140,
      width: shape.width ?? record?.width ?? size[0], height: shape.height ?? record?.height ?? size[1] }, `shape '${element.elementId}'`)];
  }));
}

export function projectBpmnJsXml(owner: ActivityNode, layout: DesignMetadataRecord[]): string {
  const source = snapshot(owner);
  const diagram = oldDiagram(source);
  const shapes = projectedBounds(source, layout);
  const allocate = (seed: string) => generatedId(seed, source.reservedIds);
  const elementXml = source.elements.map(element => {
    const type = element.elementType;
    const defaultFlow = element.defaultFlowId ?? source.flows.find(flow => flow.sourceRef === element.elementId && flow.isDefault)?.flowId;
    const events = array(element.eventDefinitions, "BPMN events");
    return `<bpmn:${type}${attr("id", element.elementId)}${attr("name", element.name)}${attr("default", defaultFlow)}>${events.length ? `<bpmn:terminateEventDefinition${attr("id", allocate("ElsaTerminate"))}/>` : ""}</bpmn:${type}>`;
  });
  const flowXml = source.flows.map(flow => `<bpmn:sequenceFlow${attr("id", flow.flowId)}${attr("sourceRef", flow.sourceRef)}${attr("targetRef", flow.targetRef)}${attr("name", flow.name)}${attr("elsa:conditionOutcome", flow.conditionOutcome)}/>`);
  const shapeXml = source.elements.map(element => {
    const shape = shapes.get(element.elementId)!;
    const previous = properties(diagram.shapes[element.elementId], "BPMN shape");
    const label = previous.label ? bounds(previous.label, "BPMN label") : null;
    return `<bpmndi:BPMNShape${attr("id", allocate("ElsaShape"))}${attr("bpmnElement", element.elementId)}${element.elementType === "subProcess" ? ' isExpanded="false"' : ""}${attr("isMarkerVisible", optionalBoolean(previous.isMarkerVisible, "BPMN marker visibility"))}><dc:Bounds${attr("x", shape.x)}${attr("y", shape.y)}${attr("width", shape.width)}${attr("height", shape.height)}/>${label ? `<bpmndi:BPMNLabel><dc:Bounds${attr("x", label.x)}${attr("y", label.y)}${attr("width", label.width)}${attr("height", label.height)}/></bpmndi:BPMNLabel>` : ""}</bpmndi:BPMNShape>`;
  });
  const edgeXml = source.flows.map(flow => {
    const previous = properties(diagram.edges[flow.flowId], "BPMN edge");
    const start = shapes.get(flow.sourceRef)!; const end = shapes.get(flow.targetRef)!;
    const points = previous.waypoints === undefined ? [{ x: start.x + start.width, y: start.y + start.height / 2 }, { x: end.x, y: end.y + end.height / 2 }]
      : array(previous.waypoints, "BPMN waypoints").map(value => point(value, "BPMN waypoint"));
    if (points.length < 2) refuse(`flow '${flow.flowId}' needs at least two waypoints`);
    const label = previous.label ? bounds(previous.label, "BPMN edge label") : null;
    return `<bpmndi:BPMNEdge${attr("id", allocate("ElsaEdge"))}${attr("bpmnElement", flow.flowId)}>${points.map(value => `<di:waypoint${attr("x", value.x)}${attr("y", value.y)}/>`).join("")}${label ? `<bpmndi:BPMNLabel><dc:Bounds${attr("x", label.x)}${attr("y", label.y)}${attr("width", label.width)}${attr("height", label.height)}/></bpmndi:BPMNLabel>` : ""}</bpmndi:BPMNEdge>`;
  });
  return `<?xml version="1.0" encoding="UTF-8"?><bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI" xmlns:dc="http://www.omg.org/spec/DD/20100524/DC" xmlns:di="http://www.omg.org/spec/DD/20100524/DI" xmlns:elsa="${namespace}" targetNamespace="${namespace}"><bpmn:process${attr("id", source.processId)} isExecutable="true">${elementXml.join("")}${flowXml.join("")}</bpmn:process><bpmndi:BPMNDiagram${attr("id", allocate("ElsaDiagram"))}><bpmndi:BPMNPlane${attr("id", allocate("ElsaPlane"))}${attr("bpmnElement", source.processId)}>${shapeXml.join("")}${edgeXml.join("")}</bpmndi:BPMNPlane></bpmndi:BPMNDiagram></bpmn:definitions>`;
}

function reference(value: unknown, label: string): string { return identifier(typeof value === "string" ? value : object(value, label).id, label); }
function supportedFields(value: RecordValue, fields: string[], label: string) {
  const allowed = new Set(["$type", "$parent", ...fields]);
  for (const key of Object.keys(value))
    if (!allowed.has(key) && value[key] !== undefined) refuse(`${label} contains unsupported '${key}' configuration`);
}
function attributes(value: RecordValue, allowed: string[], label: string): RecordValue {
  const result = properties(value.$attrs, `${label} attributes`);
  for (const key of Object.keys(result)) if (!allowed.includes(key)) refuse(`${label} contains unsupported XML attribute '${key}'`);
  return result;
}

export function mergeBpmnJsDefinitions(owner: ActivityNode, definitions: unknown, layout: DesignMetadataRecord[]): { owner: ActivityNode; layout: DesignMetadataRecord[] } {
  const source = snapshot(owner);
  const document = object(definitions, "BPMN definitions");
  if (document.$type !== "bpmn:Definitions") refuse("the modeler document is not BPMN definitions");
  supportedFields(document, ["id", "targetNamespace", "exporter", "exporterVersion", "rootElements", "diagrams", "$attrs"], "BPMN definitions");
  // Namespace declarations are representation data, never executable extension intent.
  const rootAttributes = properties(document.$attrs, "BPMN definition attributes");
  if (Object.keys(rootAttributes).some(key => key !== "xmlns" && !key.startsWith("xmlns:"))) refuse("the BPMN document has unsupported extension attributes");
  const roots = array(document.rootElements, "BPMN root elements");
  if (roots.length !== 1) refuse("exactly one projected process is required");
  const process = object(roots[0], "BPMN process");
  if (process.$type !== "bpmn:Process" || process.id !== source.processId || process.isExecutable !== true) refuse("the projected process identity or execution intent changed");
  supportedFields(process, ["id", "isExecutable", "flowElements", "$attrs"], "BPMN process"); attributes(process, [], "BPMN process");
  const previousElements = new Map(source.elements.map(element => [element.elementId, element]));
  const previousFlows = new Map(source.flows.map(flow => [flow.flowId, flow]));
  const visibleIds = new Set([...previousElements.keys(), ...previousFlows.keys()]);
  const ids = new Set([...source.reservedIds].filter(id => !visibleIds.has(id)));
  const admitOwnedId = (value: unknown) => { if (value !== undefined) unique(ids, identifier(value, "BPMN document identity")); };
  admitOwnedId(document.id);
  const elements: BpmnElement[] = [];
  const flows: BpmnSequenceFlow[] = [];
  for (const raw of array(process.flowElements, "BPMN flow elements")) {
    const value = object(raw, "BPMN flow element");
    const id = identifier(value.id, "BPMN flow element"); unique(ids, id);
    optionalText(value.name, "BPMN name");
    if (value.$type === "bpmn:SequenceFlow") {
      if (previousElements.has(id)) refuse(`element '${id}' became a sequence flow`);
      supportedFields(value, ["id", "name", "sourceRef", "targetRef", "$attrs"], `flow '${id}'`);
      const attrs = attributes(value, ["elsa:conditionOutcome"], `flow '${id}'`);
      optionalText(attrs["elsa:conditionOutcome"], "BPMN outcome");
      const previous = previousFlows.get(id);
      flows.push({ ...previous, flowId: id, sourceRef: reference(value.sourceRef, "BPMN flow source"), targetRef: reference(value.targetRef, "BPMN flow target"),
        name: value.name as string | undefined, conditionOutcome: (attrs["elsa:conditionOutcome"] ?? previous?.conditionOutcome) as string | null | undefined, isDefault: false });
      continue;
    }
    const type = reverseTypes.get(String(value.$type));
    if (!type) refuse(`element '${id}' uses unsupported type '${String(value.$type)}'`);
    if (previousFlows.has(id)) refuse(`flow '${id}' became a BPMN element`);
    supportedFields(value, ["id", "name", "incoming", "outgoing", "default", "eventDefinitions", "isForCompensation", "flowElements", "$attrs"], `element '${id}'`);
    attributes(value, [], `element '${id}'`);
    if (value.isForCompensation !== undefined && value.isForCompensation !== false) refuse(`element '${id}' became a compensation handler`);
    const previous = previousElements.get(id);
    if (previous && previous.elementType !== type && (previous.childNodeId || properties(previous.properties, "BPMN properties")["elsa.direct.binding"]))
      refuse(`changing the type of bound element '${id}' requires the Elsa inspector`);
    if (type === "subProcess" && (!previous?.childNodeId || array(value.flowElements, "subprocess body").length)) refuse(`subprocess '${id}' must retain its collapsed Elsa body`);
    if (type !== "subProcess" && array(value.flowElements, "nested flow elements").length) refuse(`element '${id}' has an unsupported nested body`);
    const events = array(value.eventDefinitions, "BPMN event definitions");
    if (events.length) {
      const event = object(events[0], "BPMN event definition");
      if (type !== "endEvent" || events.length !== 1 || event.$type !== "bpmn:TerminateEventDefinition") refuse(`element '${id}' has an unsupported event definition`);
      supportedFields(event, ["id", "$attrs"], "BPMN terminate event"); attributes(event, [], "BPMN terminate event"); admitOwnedId(event.id);
    }
    const eventDefinitions = events.length ? previous?.eventDefinitions?.[0]?.type === "terminate" ? previous.eventDefinitions : [{ type: "terminate" }] : [];
    elements.push({ ...previous, elementId: id, elementType: type, name: value.name as string | undefined,
      defaultFlowId: value.default === undefined || value.default === null ? null : reference(value.default, "default flow"), eventDefinitions });
  }
  const byId = new Map(elements.map(element => [element.elementId, element]));
  for (const raw of array(process.flowElements, "BPMN flow elements")) {
    const value = object(raw, "BPMN flow element");
    if (value.$type === "bpmn:SequenceFlow") continue;
    for (const [field, endpoint] of [["incoming", "targetRef"], ["outgoing", "sourceRef"]] as const)
      for (const item of array(value[field], `BPMN ${field} references`)) {
        const related = flows.find(flow => flow.flowId === reference(item, `BPMN ${field} flow`));
        if (!related || related[endpoint] !== value.id) refuse(`element '${String(value.id)}' has inconsistent ${field} flow references`);
      }
  }
  for (const flow of flows) {
    const element = byId.get(flow.sourceRef);
    flow.isDefault = element?.defaultFlowId === flow.flowId;
    if (flow.isDefault && !previousFlows.get(flow.flowId)?.isDefault && previousElements.get(flow.sourceRef)?.defaultFlowId !== flow.flowId) flow.conditionOutcome = null;
  }
  const retainedBindings = new Set(elements.flatMap(element => [element.childNodeId, element.listenerNodeId]).filter((id): id is string => typeof id === "string"));
  const activities = source.activities.filter(activity => retainedBindings.has(activity.nodeId));
  validateReferences(elements, flows, activities);
  const diagrams = array(document.diagrams, "BPMN diagrams");
  let plane: RecordValue | undefined;
  const auxiliaryBodies = new Set<string>();
  for (const raw of diagrams) {
    const diagram = object(raw, "BPMN diagram");
    if (diagram.$type !== "bpmndi:BPMNDiagram") refuse("the diagram has an unsupported type");
    supportedFields(diagram, ["id", "plane", "$attrs"], "BPMN diagram"); attributes(diagram, [], "BPMN diagram");
    const candidate = object(diagram.plane, "BPMN plane");
    if (candidate.$type !== "bpmndi:BPMNPlane") refuse("the diagram plane has an unsupported type");
    supportedFields(candidate, ["id", "bpmnElement", "planeElement", "$attrs"], "BPMN plane"); attributes(candidate, [], "BPMN plane");
    const target = reference(candidate.bpmnElement, "diagram process");
    if (target === source.processId) {
      if (plane) refuse("exactly one root process BPMN diagram is required");
      admitOwnedId(diagram.id); admitOwnedId(candidate.id);
      plane = candidate;
      continue;
    }
    // bpmn-js 18.31.1 SubprocessCompatibility creates an idless empty child diagram for every
    // collapsed subprocess on import, including our placeholders for opaque Elsa bodies. It is
    // transient modeler bookkeeping, not another authored body. Admit only that exact empty shape:
    // identified, annotated, populated, unrelated or repeated planes must not disappear silently.
    const body = byId.get(target);
    const retained = previousElements.get(target);
    const projected = array(process.flowElements, "BPMN flow elements").find(value => object(value, "BPMN flow element").id === target);
    if (!body || body.elementType !== "subProcess" || !body.childNodeId || retained?.elementType !== "subProcess"
      || body.childNodeId !== retained.childNodeId || candidate.bpmnElement !== projected
      || diagram.id !== undefined || candidate.id !== undefined
      || array(candidate.planeElement, "auxiliary BPMN diagram elements").length || auxiliaryBodies.has(target))
      refuse(`diagram for '${target}' is not an empty modeler-generated plane of a retained Elsa body`);
    auxiliaryBodies.add(target);
  }
  if (!plane) refuse("exactly one root process BPMN diagram is required");
  const priorDiagram = oldDiagram(source);
  const shapes = { ...priorDiagram.shapes }; const edges = { ...priorDiagram.edges };
  const records = indexedLayout(layout);
  const observed = new Set<string>();
  for (const raw of array(plane.planeElement, "BPMN diagram elements")) {
    const value = object(raw, "BPMN diagram element");
    const id = reference(value.bpmnElement, "diagram element"); admitOwnedId(value.id);
    if (observed.has(id)) refuse(`diagram element '${id}' is duplicated`); observed.add(id);
    if (value.$type === "bpmndi:BPMNShape" && byId.has(id)) {
      supportedFields(value, ["id", "bpmnElement", "bounds", "isExpanded", "isMarkerVisible", "label", "$attrs"], `shape '${id}'`); attributes(value, [], `shape '${id}'`);
      if (value.isExpanded !== undefined && value.isExpanded !== false) refuse(`subprocess '${id}' must stay collapsed; open its Elsa body to edit it`);
      const shape = bounds(value.bounds, `shape '${id}'`);
      const isMarkerVisible = optionalBoolean(value.isMarkerVisible, "BPMN marker visibility");
      const previous = properties(shapes[id], "BPMN shape");
      let label: Bounds | undefined;
      if (value.label !== undefined) {
        const sourceLabel = object(value.label, "BPMN label");
        supportedFields(sourceLabel, ["id", "bounds", "$attrs"], "BPMN label"); attributes(sourceLabel, [], "BPMN label"); admitOwnedId(sourceLabel.id);
        if (sourceLabel.bounds !== undefined) label = bounds(sourceLabel.bounds, "BPMN label");
      }
      shapes[id] = { ...previous, ...shape, ...(isMarkerVisible !== undefined ? { isMarkerVisible } : {}), ...(label ? { label } : {}) };
      records.set(id, { ...records.get(id), nodeId: id, ...shape });
    } else if (value.$type === "bpmndi:BPMNEdge" && flows.some(flow => flow.flowId === id)) {
      supportedFields(value, ["id", "bpmnElement", "waypoint", "label", "$attrs"], `edge '${id}'`); attributes(value, [], `edge '${id}'`);
      const waypoints = array(value.waypoint, "BPMN waypoints").map(value => point(value, "BPMN waypoint"));
      if (waypoints.length < 2) refuse(`flow '${id}' needs at least two waypoints`);
      let label: Bounds | undefined;
      if (value.label !== undefined) {
        const sourceLabel = object(value.label, "BPMN label");
        supportedFields(sourceLabel, ["id", "bounds", "$attrs"], "BPMN label"); attributes(sourceLabel, [], "BPMN label"); admitOwnedId(sourceLabel.id);
        if (sourceLabel.bounds !== undefined) label = bounds(sourceLabel.bounds, "BPMN label");
      }
      edges[id] = { ...properties(edges[id], "BPMN edge"), waypoints, ...(label ? { label } : {}) };
    } else refuse(`diagram element '${id}' is unsupported or has no matching process element`);
  }
  for (const id of [...byId.keys(), ...flows.map(flow => flow.flowId)]) if (!observed.has(id)) refuse(`diagram element '${id}' is missing`);
  for (const id of previousElements.keys()) if (!byId.has(id)) { delete shapes[id]; records.delete(id); }
  const flowIds = new Set(flows.map(flow => flow.flowId));
  for (const id of previousFlows.keys()) if (!flowIds.has(id)) delete edges[id];
  return { owner: { ...owner, structure: { ...owner.structure!, payload: { ...source.payload, elements, sequenceFlows: flows, activities,
    diagram: { ...priorDiagram.root, shapes, edges } } } }, layout: [...records.values()] };
}
