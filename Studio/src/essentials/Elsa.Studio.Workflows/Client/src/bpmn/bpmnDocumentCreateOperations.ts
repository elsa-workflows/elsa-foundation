import type { ActivityNode } from "../workflowTypes";
import { applyBpmnDocumentTransaction, bpmnNamespaces as ns, readBpmnDocument, resolveBpmnDocumentReference, type BpmnBounds, type BpmnDocumentCommand, type BpmnDocumentSelection, type BpmnDocumentTransaction, type BpmnPoint, type BpmnScopeAddress } from "./bpmnDocument";
import { verifyBpmnEventContexts } from "./bpmnDocumentEventContext";
import { assessBpmnBoundaryAttachment, assessBpmnDocumentConnection, bpmnBoolean, bpmnFlowScope, bpmnOwningProcess, isBpmnModelElement, type BpmnDocumentConnectionKind } from "./bpmnDocumentPolicy";
import { bpmnDocumentToolboxShapes, type BpmnToolboxShape } from "./bpmnDocumentToolbox";
import { bpmnChildren, bpmnFragment, bpmnIdentity, bpmnOperationContext, bpmnSourceAddress, bpmnXml, insertBpmnFragment, refuseBpmnOperation, requireBpmnFlowScope, requireBpmnVisibleElement, resolveBpmnOperationAddress, type BpmnOperationContext } from "./bpmnDocumentOperationXml";

export interface BpmnDocumentOperationPlan {
  transaction: BpmnDocumentTransaction;
  selection: BpmnDocumentSelection | null;
  /** The host must admit this source/scope transition atomically; the view does not guess it. */
  nextScope?: BpmnScopeAddress;
}
export interface BpmnCreateShapeIntent {
  toolId: string;
  position: BpmnPoint;
  name?: string;
  parentElementId?: string;
  attachedToElementId?: string;
  bounds?: BpmnBounds;
}
function bounds(value: BpmnBounds) {
  if (Object.values(value).some(number => !Number.isFinite(number)) || value.width <= 0 || value.height <= 0) refuseBpmnOperation("shape bounds must be finite and positive");
  return value;
}
/** Initial orthogonal route docks on real side midpoints for circles, diamonds and activities. */
export function bpmnInitialConnectionPoints(source: BpmnBounds, target: BpmnBounds, self = false): BpmnPoint[] {
  bounds(source); bounds(target);
  const center = (value: BpmnBounds) => ({ x: value.x + value.width / 2, y: value.y + value.height / 2 });
  const from = center(source), to = center(target);
  if (self) return [{ x: source.x + source.width, y: from.y }, { x: source.x + source.width + 40, y: from.y },
    { x: source.x + source.width + 40, y: source.y - 40 }, { x: from.x, y: source.y - 40 }, { x: from.x, y: source.y }];
  if (from.x === to.x && from.y === to.y) refuseBpmnOperation("overlapping connection endpoints need an explicit route");
  if (Math.abs(to.x - from.x) >= Math.abs(to.y - from.y)) {
    const sign = to.x > from.x ? 1 : -1, start = { x: from.x + sign * source.width / 2, y: from.y }, end = { x: to.x - sign * target.width / 2, y: to.y };
    if ((end.x - start.x) * sign < 0) refuseBpmnOperation("overlapping connection endpoints need an explicit route");
    const middle = (start.x + end.x) / 2;
    return start.y === end.y ? [start, end] : [start, { x: middle, y: start.y }, { x: middle, y: end.y }, end];
  }
  const sign = to.y > from.y ? 1 : -1, start = { x: from.x, y: from.y + sign * source.height / 2 }, end = { x: to.x, y: to.y - sign * target.height / 2 };
  if ((end.y - start.y) * sign < 0) refuseBpmnOperation("overlapping connection endpoints need an explicit route");
  const middle = (start.y + end.y) / 2;
  return start.x === end.x ? [start, end] : [start, { x: start.x, y: middle }, { x: end.x, y: middle }, end];
}
function diagramShape(context: BpmnOperationContext, id: string, elementId: string, value: BpmnBounds, expanded?: boolean, horizontal?: boolean) {
  const shape = bpmnFragment(context, "BPMNShape", { id, bpmnElement: elementId,
    ...(expanded === undefined ? {} : { isExpanded: String(expanded) }), ...(horizontal === undefined ? {} : { isHorizontal: String(horizontal) }) }, ns.diagram);
  shape.append(bpmnFragment(context, "Bounds", Object.fromEntries(Object.entries(bounds(value)).map(([name, number]) => [name, String(number)])), ns.bounds));
  return insertBpmnFragment(context, context.plane, shape);
}
function shapePlan(context: BpmnOperationContext, element: Element, parent: Element, entry: BpmnToolboxShape, value: BpmnBounds, commands: BpmnDocumentCommand[] = []): BpmnDocumentOperationPlan {
  const elementId = bpmnIdentity(element), diId = context.allocate(`${elementId}_di`);
  return { transaction: { commands: [...commands, insertBpmnFragment(context, parent, element), diagramShape(context, diId, elementId, value, entry.expanded,
    ["participant", "lane"].includes(entry.type) ? true : undefined)] }, selection: { elementId, diId } };
}

export function createBpmnIo(context: BpmnOperationContext, owner: Element, direction: "dataInput" | "dataOutput", name: string, commands: BpmnDocumentCommand[]) {
  const element = bpmnFragment(context, direction, { id: context.allocate(direction), name });
  if (["startEvent", "boundaryEvent", "intermediateCatchEvent", "intermediateThrowEvent", "endEvent"].includes(owner.localName)) {
    const catches = ["startEvent", "boundaryEvent", "intermediateCatchEvent"].includes(owner.localName);
    if (catches !== (direction === "dataOutput")) refuseBpmnOperation("this event's standard data direction is incompatible");
    commands.push(insertBpmnFragment(context, owner, element));
    const setName = direction === "dataInput" ? "inputSet" : "outputSet", referenceName = direction === "dataInput" ? "dataInputRefs" : "dataOutputRefs";
    const set = bpmnChildren(owner, setName)[0];
    const reference = bpmnFragment(context, referenceName); reference.textContent = bpmnIdentity(element);
    if (set) commands.push(insertBpmnFragment(context, set, reference));
    else { const created = bpmnFragment(context, setName); created.append(reference); commands.push(insertBpmnFragment(context, owner, created)); }
    return bpmnIdentity(element);
  }
  const specifications = bpmnChildren(owner, "ioSpecification");
  if (specifications.length > 1) refuseBpmnOperation("the data owner has ambiguous I/O specifications");
  const specification = specifications[0];
  const setName = direction === "dataInput" ? "inputSet" : "outputSet", referenceName = direction === "dataInput" ? "dataInputRefs" : "dataOutputRefs";
  const reference = bpmnFragment(context, referenceName); reference.textContent = bpmnIdentity(element);
  if (!specification) {
    const created = bpmnFragment(context, "ioSpecification"), input = bpmnFragment(context, "inputSet"), output = bpmnFragment(context, "outputSet");
    (direction === "dataInput" ? input : output).append(reference); created.append(element, input, output);
    commands.push(insertBpmnFragment(context, owner, created));
  } else {
    commands.push(insertBpmnFragment(context, specification, element));
    const sets = bpmnChildren(specification, setName);
    if (sets.length !== 1) refuseBpmnOperation("choose an actual I/O set before extending a missing or multi-set specification");
    commands.push(insertBpmnFragment(context, sets[0], reference));
  }
  return bpmnIdentity(element);
}

function createPool(context: BpmnOperationContext, entry: BpmnToolboxShape, value: BpmnBounds, name: string) {
  const commands: BpmnDocumentCommand[] = [], root = context.parsed.document.documentElement;
  let collaboration = context.selectedScope.localName === "collaboration" ? context.selectedScope
    : resolveBpmnDocumentReference(context.parsed, context.plane, context.plane.getAttribute("bpmnElement"));
  let collaborationId: string;
  if (collaboration?.localName !== "collaboration") {
    const process = bpmnOwningProcess(context.selectedScope);
    if (!process || process.parentElement !== root) refuseBpmnOperation("creating a collaboration requires a represented top-level process");
    collaborationId = context.allocate("Collaboration");
    collaboration = bpmnFragment(context, "collaboration", { id: collaborationId });
    const currentPoolId = context.allocate("Participant"), pool = bpmnFragment(context, "participant", { id: currentPoolId, processRef: bpmnIdentity(process), name: process.getAttribute("name") ?? "Process" });
    collaboration.append(pool);
    const content = context.scene.shapes.filter(shape => bpmnOwningProcess(shape.element)?.getAttribute("id") === bpmnIdentity(process));
    const left = content.length ? Math.min(...content.map(shape => shape.bounds.x)) : value.x - 650;
    const top = content.length ? Math.min(...content.map(shape => shape.bounds.y)) : value.y;
    const right = content.length ? Math.max(...content.map(shape => shape.bounds.x + shape.bounds.width)) : left + 540;
    const bottom = content.length ? Math.max(...content.map(shape => shape.bounds.y + shape.bounds.height)) : top + 210;
    commands.push(insertBpmnFragment(context, root, collaboration),
      { kind: "attribute", target: bpmnSourceAddress(context.plane, context.parsed), name: "bpmnElement", value: collaborationId },
      diagramShape(context, context.allocate(`${currentPoolId}_di`), currentPoolId, { x: left - 40, y: top - 20, width: Math.max(600, right - left + 60), height: Math.max(250, bottom - top + 40) }, true, true));
  } else collaborationId = bpmnIdentity(collaboration);
  const elementId = context.allocate("Participant"), diId = context.allocate(`${elementId}_di`);
  const participant = bpmnFragment(context, "participant", { id: elementId, name });
  if (entry.expanded) {
    const processId = context.allocate("Process"); participant.setAttribute("processRef", processId);
    commands.push(insertBpmnFragment(context, root, bpmnFragment(context, "process", { id: processId, isExecutable: "false" })));
  }
  // The collaboration may have been created earlier in this same transaction, so address its real allocated ID.
  commands.push({ kind: "append", parent: { elementId: collaborationId }, xml: bpmnXml(participant) }, diagramShape(context, diId, elementId, value, entry.expanded, true));
  return { transaction: { commands }, selection: { elementId, diId }, nextScope: { ...context.scope, scopeElementId: collaborationId } };
}

function createLane(context: BpmnOperationContext, entry: BpmnToolboxShape, intent: BpmnCreateShapeIntent) {
  const target = resolveBpmnOperationAddress(context, { elementId: intent.parentElementId ?? "" });
  if (!isBpmnModelElement(target) || !["participant", "lane"].includes(target.localName)) refuseBpmnOperation("select the actual pool or lane that owns the new lane");
  const targetShapes = context.scene.shapes.filter(shape => shape.elementId === bpmnIdentity(target));
  if (targetShapes.length !== 1) refuseBpmnOperation("lane creation needs one unambiguous container DI instance");
  const frame = targetShapes[0].bounds, commands: BpmnDocumentCommand[] = [];
  const laneId = context.allocate("Lane"), lane = bpmnFragment(context, "lane", { id: laneId, name: intent.name ?? "Lane" });
  const moveMembership = (references: Element[]) => [...references].reverse().forEach((reference, index) => commands.push({ kind: "reparent", target: bpmnSourceAddress(reference, context.parsed), parent: { elementId: laneId },
    ...(index ? { before: { elementId: laneId, path: [{ namespaceURI: ns.model, localName: "flowNodeRef", index: 0 }] } } : {}) }));
  const horizontal = targetShapes[0].isHorizontal !== false;
  const owner = target.localName === "participant" ? resolveBpmnDocumentReference(context.parsed, target, target.getAttribute("processRef")) : target;
  if (!owner || target.localName === "participant" && owner.localName !== "process") refuseBpmnOperation("an empty pool needs an explicit process before it can own lanes");
  const setName = target.localName === "lane" ? "childLaneSet" : "laneSet", sets = bpmnChildren(owner, setName);
  if (sets.length > 1) refuseBpmnOperation("choose a specific lane set before extending multiple lane classifications");
  const set = sets[0], siblings = set ? bpmnChildren(set, "lane") : [];
  let value: BpmnBounds;
  if (!siblings.length) {
    value = horizontal ? { x: frame.x + 30, y: frame.y, width: frame.width - 30, height: frame.height }
      : { x: frame.x, y: frame.y + 30, width: frame.width, height: frame.height - 30 };
    if (target.localName === "participant") for (const node of [...owner.children].filter(child => isBpmnModelElement(child)
      && ["task", "userTask", "serviceTask", "manualTask", "scriptTask", "businessRuleTask", "sendTask", "receiveTask", "callActivity", "subProcess", "adHocSubProcess", "transaction", "startEvent", "endEvent", "intermediateCatchEvent", "intermediateThrowEvent", "boundaryEvent", "exclusiveGateway", "inclusiveGateway", "parallelGateway", "eventBasedGateway", "complexGateway"].includes(child.localName))) {
      const reference = bpmnFragment(context, "flowNodeRef"); reference.textContent = bpmnIdentity(node); lane.append(reference);
    }
    if (set) commands.push(insertBpmnFragment(context, set, lane));
    else { const created = bpmnFragment(context, setName); created.append(lane); commands.push(insertBpmnFragment(context, owner, created)); }
    // Move actual membership elements so QName and vendor context survives. No input/metadata ID search.
    moveMembership(target.localName === "lane" ? bpmnChildren(target, "flowNodeRef") : []);
  } else {
    const previous = siblings.at(-1)!;
    if (bpmnChildren(previous, "childLaneSet").length) refuseBpmnOperation("select a leaf lane before splitting a nested lane partition");
    const previousShapes = context.scene.shapes.filter(shape => shape.elementId === bpmnIdentity(previous));
    if (previousShapes.length !== 1) refuseBpmnOperation("lane split needs one actual leaf DI instance");
    const old = previousShapes[0].bounds, half = (horizontal ? old.height : old.width) / 2;
    const retained = horizontal ? { ...old, height: half } : { ...old, width: half };
    value = horizontal ? { ...old, y: old.y + half, height: half } : { ...old, x: old.x + half, width: half };
    commands.push(insertBpmnFragment(context, set!, lane), { kind: "bounds", shapeId: previousShapes[0].shapeId, bounds: retained });
    const movedMembership: Element[] = [];
    for (const reference of bpmnChildren(previous, "flowNodeRef")) {
      const element = resolveBpmnDocumentReference(context.parsed, reference, reference.textContent);
      const instances = context.scene.shapes.filter(shape => shape.elementId === element?.getAttribute("id"));
      if (instances.length > 1) refuseBpmnOperation("lane membership has ambiguous DI instances");
      if (instances.length && (horizontal ? instances[0].bounds.y + instances[0].bounds.height / 2 >= value.y : instances[0].bounds.x + instances[0].bounds.width / 2 >= value.x))
        movedMembership.push(reference);
    }
    moveMembership(movedMembership);
  }
  const diId = context.allocate(`${laneId}_di`);
  commands.push(diagramShape(context, diId, laneId, bounds(value), undefined, horizontal));
  return { transaction: { commands }, selection: { elementId: laneId, diId } };
}

/** Creates standard syntax and its actual DI together; native activity/header choices remain in the resource host. */
export function planBpmnCreateShape(owner: ActivityNode, scope: BpmnScopeAddress, intent: BpmnCreateShapeIntent): BpmnDocumentOperationPlan {
  const context = bpmnOperationContext(owner, scope), entry = bpmnDocumentToolboxShapes.find(item => item.id === intent.toolId);
  if (!entry) refuseBpmnOperation("the selected construct is not in this toolbox catalog");
  let value = bounds(intent.bounds ?? { ...intent.position, width: entry.width, height: entry.height });
  if (entry.type === "participant") return createPool(context, entry, value, intent.name ?? "Pool");
  if (entry.type === "lane") return createLane(context, entry, intent);
  const commands: BpmnDocumentCommand[] = [];
  const host = entry.placement === "activityBoundary" ? context.parsed.byId.get(intent.attachedToElementId ?? "") : undefined;
  if (entry.placement === "activityBoundary" && !isBpmnModelElement(host)) refuseBpmnOperation("a boundary event needs a real activity in a represented flow scope");
  const hostScope = host && bpmnFlowScope(host);
  if (host && !hostScope) refuseBpmnOperation("the boundary host has no standard flow scope");
  const parent = requireBpmnFlowScope(context, hostScope ? bpmnIdentity(hostScope) : intent.parentElementId);
  requireBpmnVisibleElement(context, host ?? parent);
  if (parent !== context.selectedScope && ["subProcess", "transaction", "adHocSubProcess"].includes(parent.localName)) {
    const instances = context.scene.shapes.filter(shape => shape.elementId === bpmnIdentity(parent));
    if (instances.length !== 1 || instances[0].isExpanded !== true) refuseBpmnOperation("open the actual standard child scope before adding to a collapsed or ambiguous body");
  }
  if (entry.eventSubprocessOnly && !bpmnBoolean(parent, "triggeredByEvent")) refuseBpmnOperation("this trigger belongs to an event subprocess");
  if (entry.type === "startEvent" && bpmnBoolean(parent, "triggeredByEvent") && !entry.eventSubprocessOnly) refuseBpmnOperation("choose a start trigger for the event subprocess");
  if (entry.type === "startEvent" && parent.localName !== "process" && !bpmnBoolean(parent, "triggeredByEvent") && entry.eventDefinitions?.length)
    refuseBpmnOperation("ordinary subprocess bodies require a none start event");
  if (entry.transactionOnly && (entry.type === "boundaryEvent" ? context.parsed.byId.get(intent.attachedToElementId ?? "")?.localName !== "transaction" : parent.localName !== "transaction"))
    refuseBpmnOperation("cancel events require the actual transaction context");
  const elementId = context.allocate(entry.type), element = bpmnFragment(context, entry.type, { id: elementId, ...(entry.attributes ?? {}) });
  if (!["textAnnotation", "group", "dataInput", "dataOutput"].includes(entry.type)) element.setAttribute("name", intent.name ?? entry.label);
  for (const type of entry.eventDefinitions ?? []) {
    const definition = bpmnFragment(context, type);
    // New drafts have actual standard members; their content is configured through the properties port.
    if (type === "timerEventDefinition") { const duration = bpmnFragment(context, "timeDuration"); duration.textContent = "PT1H"; definition.append(duration); }
    if (type === "conditionalEventDefinition") definition.append(bpmnFragment(context, "condition"));
    if (type === "linkEventDefinition") definition.setAttribute("name", intent.name ?? "Link");
    element.append(definition);
  }
  if (entry.type === "boundaryEvent") {
    element.setAttribute("attachedToRef", intent.attachedToElementId!);
    const temporary = { ...context.parsed, byId: new Map(context.parsed.byId).set(elementId, element) };
    // Validate the planned sibling in this private parse, then detach the fragment for the transaction.
    parent.append(element);
    try {
      const policy = assessBpmnBoundaryAttachment(temporary, elementId, intent.attachedToElementId!);
      if (!policy.allowed) refuseBpmnOperation(policy.reason);
    } finally { element.remove(); }
    const instances = context.scene.shapes.filter(shape => shape.elementId === bpmnIdentity(host!));
    if (instances.length !== 1) refuseBpmnOperation("boundary attachment needs one actual activity DI instance");
    const frame = instances[0].bounds, center = { x: value.x + value.width / 2, y: value.y + value.height / 2 };
    const clamp = (number: number, low: number, high: number) => Math.max(low, Math.min(number, high));
    const x = clamp(center.x, frame.x + Math.min(12, frame.width / 2), frame.x + frame.width - Math.min(12, frame.width / 2));
    const y = clamp(center.y, frame.y + Math.min(12, frame.height / 2), frame.y + frame.height - Math.min(12, frame.height / 2));
    const anchor = [{ x: frame.x, y }, { x: frame.x + frame.width, y }, { x, y: frame.y }, { x, y: frame.y + frame.height }]
      .sort((a, b) => Math.hypot(a.x - center.x, a.y - center.y) - Math.hypot(b.x - center.x, b.y - center.y))[0];
    value = { ...value, x: anchor.x - value.width / 2, y: anchor.y - value.height / 2 };
  }
  if (entry.type === "textAnnotation") { const text = bpmnFragment(context, "text"); text.textContent = intent.name ?? "Annotation"; element.append(text); }
  if (entry.type === "group") {
    const category = bpmnFragment(context, "category", { id: context.allocate("Category") });
    const categoryValue = bpmnFragment(context, "categoryValue", { id: context.allocate("CategoryValue"), value: intent.name ?? "Group" });
    category.append(categoryValue); element.setAttribute("categoryValueRef", bpmnIdentity(categoryValue));
    commands.push(insertBpmnFragment(context, context.parsed.document.documentElement, category));
  }
  if (entry.type === "dataObjectReference") {
    const object = bpmnFragment(context, "dataObject", { id: context.allocate("DataObject") }); element.setAttribute("dataObjectRef", bpmnIdentity(object));
    commands.push(insertBpmnFragment(context, parent, object));
  }
  if (entry.type === "dataStoreReference") {
    const store = bpmnFragment(context, "dataStore", { id: context.allocate("DataStore"), name: intent.name ?? "Data store", isUnlimited: "true" });
    element.setAttribute("dataStoreRef", bpmnIdentity(store)); commands.push(insertBpmnFragment(context, context.parsed.document.documentElement, store));
  }
  if (entry.type === "dataInput" || entry.type === "dataOutput") {
    const ioId = createBpmnIo(context, parent, entry.type, intent.name ?? entry.label, commands), diId = context.allocate(`${ioId}_di`);
    commands.push(diagramShape(context, diId, ioId, value)); return { transaction: { commands }, selection: { elementId: ioId, diId } };
  }
  return shapePlan(context, element, parent, entry, value, commands);
}

export interface BpmnCreateConnectionIntent {
  kind: BpmnDocumentConnectionKind;
  sourceId: string;
  targetId: string;
  sourceRefs?: string[];
  targetRef?: string;
  points: BpmnPoint[];
  name?: string;
}
export function planBpmnCreateConnection(owner: ActivityNode, scope: BpmnScopeAddress, intent: BpmnCreateConnectionIntent): BpmnDocumentOperationPlan {
  const context = bpmnOperationContext(owner, scope), policy = assessBpmnDocumentConnection(context.parsed, intent.kind, intent.sourceId, intent.targetId);
  if (!policy.allowed) refuseBpmnOperation(policy.reason);
  if (intent.points.length < 2 || intent.points.some(point => !Number.isFinite(point.x) || !Number.isFinite(point.y))) refuseBpmnOperation("a connection needs at least two finite source waypoints");
  const source = context.parsed.byId.get(intent.sourceId)!, target = context.parsed.byId.get(intent.targetId)!, commands: BpmnDocumentCommand[] = [];
  for (const endpoint of [source, target]) {
    requireBpmnVisibleElement(context, endpoint);
    const instances = context.scene.shapes.filter(shape => shape.elementId === bpmnIdentity(endpoint));
    if (instances.length !== 1) refuseBpmnOperation("connection endpoints need one actual DI instance in this plane");
    if (endpoint.localName === "participant") {
      if (context.selectedScope.localName !== "collaboration" || endpoint.parentElement !== context.selectedScope) refuseBpmnOperation("the pool is outside this standard scope");
    } else {
      const flowScope = bpmnFlowScope(endpoint);
      if (!flowScope) refuseBpmnOperation("the connection endpoint has no represented flow scope");
      requireBpmnFlowScope(context, bpmnIdentity(flowScope));
    }
  }
  const type = intent.kind === "compensationAssociation" ? "association" : intent.kind, elementId = context.allocate(type), diId = context.allocate(`${elementId}_di`);
  const connection = bpmnFragment(context, type, { id: elementId });
  let parent: Element;
  if (type === "dataInputAssociation" || type === "dataOutputAssociation") {
    parent = type === "dataInputAssociation" ? target : source;
    const input = type === "dataInputAssociation";
    const references = intent.sourceRefs ?? (input ? [intent.sourceId] : [createBpmnIo(context, source, "dataOutput", "Output", commands)]);
    const targetId = intent.targetRef ?? (input ? createBpmnIo(context, target, "dataInput", "Input", commands) : intent.targetId);
    if (!references.length) refuseBpmnOperation("a data association needs its actual source references");
    if (intent.sourceRefs) for (const id of references) {
      const item = context.parsed.byId.get(id);
      if (!isBpmnModelElement(item) || (input ? !assessBpmnDocumentConnection(context.parsed, "dataInputAssociation", id, intent.targetId).allowed || bpmnFlowScope(item) !== bpmnFlowScope(source)
        : item.localName !== "dataOutput" || !source.contains(item))) refuseBpmnOperation("data sources must be same-scope references or actual owner outputs");
    }
    if (intent.targetRef) {
      const item = context.parsed.byId.get(targetId);
      if (!isBpmnModelElement(item) || (input ? item.localName !== "dataInput" || !target.contains(item) : item !== target)) refuseBpmnOperation("the data target must be the selected reference or actual owner input");
    }
    for (const referenceId of references) { const reference = bpmnFragment(context, "sourceRef"); reference.textContent = referenceId; connection.append(reference); }
    const reference = bpmnFragment(context, "targetRef"); reference.textContent = targetId; connection.append(reference);
  } else {
    parent = type === "messageFlow" ? context.parsed.byId.get(context.scene.scopeId)! : bpmnFlowScope(source) ?? bpmnFlowScope(target)!;
    if (!parent || type === "messageFlow" && parent.localName !== "collaboration") refuseBpmnOperation("this relationship needs its actual collaboration or flow-scope owner");
    connection.setAttribute("sourceRef", intent.sourceId); connection.setAttribute("targetRef", intent.targetId);
    if (type !== "association" && intent.name) connection.setAttribute("name", intent.name);
    if (intent.kind === "compensationAssociation") {
      connection.setAttribute("associationDirection", "One");
      commands.push({ kind: "attribute", target: { elementId: intent.targetId }, name: "isForCompensation", value: "true" });
    }
  }
  commands.push(insertBpmnFragment(context, parent, connection));
  if (type === "sequenceFlow") for (const [element, localName] of [[target, "incoming"], [source, "outgoing"]] as const) {
    const reference = bpmnFragment(context, localName); reference.textContent = elementId;
    commands.push(insertBpmnFragment(context, element, reference));
  }
  const edge = bpmnFragment(context, "BPMNEdge", { id: diId, bpmnElement: elementId }, ns.diagram);
  for (const point of intent.points) edge.append(bpmnFragment(context, "waypoint", { x: String(point.x), y: String(point.y) }, ns.coordinates));
  commands.push(insertBpmnFragment(context, context.plane, edge));
  if (type === "sequenceFlow") verifyBpmnEventContexts(context.parsed, readBpmnDocument(applyBpmnDocumentTransaction(owner, { commands })), [intent.sourceId, intent.targetId]);
  return { transaction: { commands }, selection: { elementId, diId } };
}
