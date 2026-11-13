import type { BpmnDocumentAuthoring, BpmnDocumentCommand, BpmnXmlAddress } from "@elsa-workflows/studio-workflows";
import type { NativeCanvasTarget, NativeCommandAction, NativeCommandGroup } from "./bpmnDocumentCommands";
import type { BpmnNativeBaseline } from "./bpmnDocumentProjection";
import { nativeChildren, nativeImportSignature, nativeIndex, nativePrimitiveMatches, nativeRefuse, nativeSignature, nativeXml } from "./bpmnDocumentXml";
import { assertNativePropertyIntents, nativeIntentElement, nativeModeledProperty as modeledProperty } from "./bpmnDocumentIntent";

const xmlns = "http://www.w3.org/2000/xmlns/";
const xsi = "http://www.w3.org/2001/XMLSchema-instance";
const colorNamespaces = ["http://www.omg.org/spec/BPMN/non-normative/color/1.0", "http://bpmn.io/schema/bpmn/biocolor/1.0"];
/** Exact handler names registered by pinned bpmn-js18.31.1 / diagram-js15.28.1. */
export const nativeModelingCommands: ReadonlySet<string> = new Set([
  "shape.append", "shape.create", "shape.delete", "shape.move", "shape.resize", "shape.replace", "shape.toggleCollapse", "spaceTool", "label.create",
  "connection.create", "connection.delete", "connection.move", "connection.layout", "connection.updateWaypoints", "connection.reconnect",
  "elements.create", "elements.move", "elements.delete", "elements.distribute", "elements.align", "element.updateAttachment",
  "element.updateModdleProperties", "element.updateProperties", "canvas.updateRoot", "lane.add", "lane.resize", "lane.split", "lane.updateRefs",
  "group.updateRefs", "id.updateClaim", "element.setColor", "element.updateLabel"
]);

function kind(element: Element) { return `${element.namespaceURI}|${element.localName}`; }
function address(element: Element): BpmnXmlAddress {
  const path: NonNullable<BpmnXmlAddress["path"]> = [];
  let current = element;
  while (!current.hasAttribute("id") && current.parentElement) {
    path.unshift({ namespaceURI: current.namespaceURI!, localName: current.localName,
      index: [...current.parentElement.children].filter(child => kind(child) === kind(current)).indexOf(current) });
    current = current.parentElement;
  }
  return { ...(current.hasAttribute("id") ? { elementId: current.getAttribute("id")! } : { documentRoot: true as const }), ...(path.length ? { path } : {}) };
}
function contextXml(authoring: BpmnDocumentAuthoring, element: Element, context = element) {
  const clone = element.cloneNode(true) as Element;
  const declarations = new Map<string, string>();
  for (let current: Element | null = context; current; current = current.parentElement)
    for (const attribute of [...current.attributes]) if (attribute.namespaceURI === xmlns && !declarations.has(attribute.name)) declarations.set(attribute.name, attribute.value);
  for (const [name, value] of declarations) clone.setAttributeNS(xmlns, name, value);
  return authoring.serializeBpmnXml(clone);
}
function fieldNames(action: NativeCommandAction) { return action.propertyNames ?? Object.keys(action.properties ?? {}); }
function propertyMatch(name: string, property: Element) {
  return name === property.localName || name.replace(/s$/, "") === property.localName
    || name === "eventDefinitions" && property.localName.endsWith("EventDefinition")
    || name === "loopCharacteristics" && ["standardLoopCharacteristics", "multiInstanceLoopCharacteristics"].includes(property.localName)
    || ({ label: "BPMNLabel", labelStyle: "BPMNLabelStyle", font: "Font", bounds: "Bounds" } as Record<string, string>)[name] === property.localName;
}

function nativePlaneSources(baseline: BpmnNativeBaseline, plane: Element): Set<string> {
  const sources = new Set([...baseline.di.values()].filter(value => value.nativePlaneScopeId === plane.getAttribute("bpmnElement")).map(value => value.sourcePlaneId));
  if (!sources.size) for (const [id, container] of baseline.projection.di)
    if (baseline.projection.scopeDiIds.has(id) && container.elementId === plane.getAttribute("bpmnElement")) sources.add(container.sourcePlaneId);
  return sources;
}

/** Inert S10b boundary; S10c owns destination frames and complete generated-plane provenance. */
export function assertBpmnNativePlaneFrames(authoring: BpmnDocumentAuthoring, baseline: BpmnNativeBaseline, nextXml: string, fullDiff = false): void {
  const ns = authoring.bpmnNamespaces, before = nativeXml(baseline.xml), next = nativeXml(nextXml), oldIndex = nativeIndex(before);
  const planes = [...before.getElementsByTagNameNS(ns.diagram, "BPMNPlane")];
  const key = (plane: Element) => JSON.stringify([plane.getAttribute("id"), plane.parentElement?.namespaceURI, plane.parentElement?.localName, plane.parentElement?.getAttribute("id"), plane.getAttribute("bpmnElement")]);
  const authored = new Set([...nativeXml(baseline.projection.xml).getElementsByTagNameNS(ns.diagram, "BPMNPlane")].filter(plane => plane.hasAttribute("id")).map(key));
  if (JSON.stringify(planes.map(key).sort()) !== JSON.stringify([...next.getElementsByTagNameNS(ns.diagram, "BPMNPlane")].map(key).sort()))
    nativeRefuse("S10c is required before native plane membership/identity/scope can change");
  for (const entry of [...next.getElementsByTagNameNS(ns.diagram, "BPMNShape"), ...next.getElementsByTagNameNS(ns.diagram, "BPMNEdge")]) {
    const destination = entry.parentElement;
    if (destination?.namespaceURI !== ns.diagram || destination.localName !== "BPMNPlane") nativeRefuse("S10c is required for an unestablished native plane");
    const old = oldIndex.get(entry.getAttribute("id") ?? "");
    if (old) {
      if (!old.parentElement || key(old.parentElement) !== key(destination)) nativeRefuse("S10c is required before native DI can change planes");
    } else if (!authored.has(key(destination))) {
      const previous = planes.filter(plane => key(plane) === key(destination));
      if (previous.length !== 1 || ![...previous[0].children].some(child => baseline.di.has(child.getAttribute("id") ?? "")))
        nativeRefuse("S10c is required for an empty or unestablished generated destination frame");
    }
  }
  if (fullDiff) for (const plane of planes) if (!authored.has(key(plane)) && nativePlaneSources(baseline, plane).size !== 1)
    nativeRefuse("S10c is required for unresolved generated-plane provenance");
}

/** Fresh IDs carry no authority: captured canvas intent and the selected source root must agree. */
function assertNativeCanvasScope(authoring: BpmnDocumentAuthoring, baseline: BpmnNativeBaseline, group: NativeCommandGroup, oldIndex: ReadonlyMap<string, Element>, newIndex: ReadonlyMap<string, Element>): void {
  const ns = authoring.bpmnNamespaces, selected = newIndex.get(baseline.projection.scope.scopeElementId), roots = selected ? [selected] : [];
  const previous = oldIndex.get(baseline.projection.scope.scopeElementId);
  if (previous?.localName === "collaboration") for (const participant of nativeChildren(previous, ns.model, "participant")) {
    const process = newIndex.get(participant.getAttribute("processRef") ?? ""); if (process && baseline.projection.scopeElementIds.has(process.getAttribute("id")!)) roots.push(process);
  }
  const active = (element: Element | undefined) => !!element && element.namespaceURI === ns.model && roots.some(root => root === element || root.contains(element));
  const captured = new Map<string, NativeCanvasTarget>();
  for (const action of group.actions) {
    for (const target of action.canvasTargets ?? []) captured.set(target.elementId, target);
    for (const id of [action.elementId, action.oldElementId, action.newElementId, ...(action.elementIds ?? [])].filter((id): id is string => !!id)) {
      const old = oldIndex.get(id);
      if (old?.namespaceURI === ns.model && !baseline.projection.scopeElementIds.has(id)) nativeRefuse("native canvas target is outside the selected source scope");
      if (!captured.has(id)) captured.set(id, { elementId: id, parentId: action.parentId, semanticParentId: action.semanticParentId, diId: action.diId, sourceId: action.sourceId, targetId: action.targetId });
    }
  }
  if (selected?.localName === "collaboration") for (const target of captured.values()) {
    const participant = newIndex.get(target.elementId), process = newIndex.get(target.processId ?? "");
    if (participant?.localName === "participant" && participant.parentElement === selected && target.semanticParentId === selected.getAttribute("id")
      && process?.localName === "process" && !oldIndex.has(target.processId!) && participant.getAttribute("processRef") === target.processId) roots.push(process);
  }
  for (const action of group.actions) if (action.parentId && [action.elementId, action.newElementId, ...(action.elementIds ?? [])].some(id => id && !oldIndex.has(id) && newIndex.get(id)?.namespaceURI === ns.model)
    && !active(newIndex.get(action.parentId))) nativeRefuse("captured canvas parent is outside the selected source scope");
  for (const target of captured.values()) {
    const element = newIndex.get(target.elementId);
    if (!element && !oldIndex.has(target.elementId) && !group.actions.some(action => ["shape.delete", "connection.delete", "elements.delete"].includes(action.command) && [action.elementId, ...(action.elementIds ?? [])].includes(target.elementId))) nativeRefuse("new canvas target is unavailable for captured intent");
    if (!element || element.namespaceURI !== ns.model) continue;
    if (!active(element)) nativeRefuse("native canvas target is outside the selected source scope");
    if (oldIndex.has(target.elementId)) continue;
    let parent = element.parentElement; while (parent && !parent.hasAttribute("id")) parent = parent.parentElement;
    if (!active(element) || !active(newIndex.get(target.parentId ?? target.semanticParentId ?? ""))) nativeRefuse("new canvas target/parent is outside the selected source scope");
    if (!parent || parent.getAttribute("id") !== (target.semanticParentId ?? target.parentId)) nativeRefuse("new canvas semantic parent differs from captured intent");
    if (["sequenceFlow", "messageFlow", "association", "dataInputAssociation", "dataOutputAssociation"].includes(element.localName)) {
      const refs = (name: string) => element.hasAttribute(name) ? [element.getAttribute(name)!] : nativeChildren(element, ns.model, name).map(child => child.textContent?.trim() ?? "");
      const sources = target.semanticSourceIds ?? (target.sourceId ? [target.sourceId] : undefined), destination = target.semanticTargetId ?? target.targetId;
      if (!sources || !destination || JSON.stringify(refs("sourceRef")) !== JSON.stringify(sources) || JSON.stringify(refs("targetRef")) !== JSON.stringify([destination])) nativeRefuse("new connection endpoints differ from captured intent");
      for (const id of [...sources, destination, target.sourceId, target.targetId].filter((id): id is string => !!id)) if (!active(newIndex.get(id))) nativeRefuse("new connection endpoint is outside the selected source scope");
    }
  }
  const projected = nativeIndex(nativeXml(baseline.projection.xml)), diagram = projected.get(baseline.projection.scope.diagramId)!;
  const selectedPlaneId = nativeChildren(diagram, ns.diagram, "BPMNPlane")[0]?.getAttribute("id");
  for (const [id, entry] of newIndex) if (!oldIndex.has(id) && entry.namespaceURI === ns.diagram && ["BPMNShape", "BPMNEdge"].includes(entry.localName)) {
    const plane = entry.parentElement!, semantic = newIndex.get(entry.getAttribute("bpmnElement") ?? ""), original = projected.get(plane.getAttribute("id") ?? "");
    const target = captured.get(semantic?.getAttribute("id") ?? ""), propertyOwned = group.actions.some(action => action.propertyIntents?.some(intent => nativeIndex(nativeIntentElement(intent).ownerDocument).has(id)));
    if (!propertyOwned && (!target || !active(newIndex.get(target.parentId ?? target.semanticParentId ?? "")) || target.diId !== id)) nativeRefuse("new canvas DI identity/parent differs from captured intent");
    const sources = nativePlaneSources(baseline, plane);
    if (!active(semantic) || (original?.localName === "BPMNPlane" ? original.getAttribute("id") !== selectedPlaneId : sources.size !== 1 || !sources.has(selectedPlaneId ?? "") || !active(newIndex.get(plane.getAttribute("bpmnElement") ?? ""))))
      nativeRefuse("new canvas DI destination is outside the selected source plane");
  }
}

/**
 * Reconcile modeled changes through granular source operations. B and N are transient native trees;
 * the retained raw tree supplies every existing node, namespace context and omitted property.
 */
export function diffBpmnNativeCommandGroup(authoring: BpmnDocumentAuthoring, baseline: BpmnNativeBaseline, group: NativeCommandGroup, nextXml: string,
  geometry: (group: NativeCommandGroup, xml: string) => readonly BpmnDocumentCommand[]): readonly BpmnDocumentCommand[] {
  assertBpmnNativePlaneFrames(authoring, baseline, nextXml, true);
  const ns = authoring.bpmnNamespaces, standard = new Set<string>(Object.values(ns));
  const before = nativeXml(baseline.xml), next = nativeXml(nextXml), oldIndex = nativeIndex(before), newIndex = nativeIndex(next);
  assertNativeCanvasScope(authoring, baseline, group, oldIndex, newIndex);
  if (!group.actions.length || group.actions.some(action => !nativeModelingCommands.has(action.command))) nativeRefuse("unmapped native command family");
  const targets = new Set<string>(), geometryIds = new Set<string>(), structural = new Set<string>(), propertyFields = new Map<string, Set<string>>();
  const removals = new Set<string>(), moves = new Set<string>(), replacements = new Set<string>(), connections = new Set<string>(), attachments = new Set<string>();
  const laneRefs = new Set<string>(), categoryRefs = new Set<string>();
  const inserted = new Set<Element>();
  const rootFields = new Set<string>();
  let rootProperties = false;
  for (const action of group.actions) {
    const ids = [action.elementId, action.oldElementId, action.newElementId, action.moddleOwnerId, ...(action.elementIds ?? [])].filter((value): value is string => !!value);
    for (const id of ids) targets.add(id);
    if (action.diId) geometryIds.add(action.diId);
    for (const id of action.diIds ?? []) geometryIds.add(id);
    if (!action.label && !["element.updateProperties", "element.updateModdleProperties", "element.updateLabel", "element.setColor", "id.updateClaim", "lane.updateRefs", "group.updateRefs"].includes(action.command))
      for (const id of ids) structural.add(id);
    if (!action.label) for (const id of ids) {
      if (["shape.delete", "connection.delete", "elements.delete"].includes(action.command)) removals.add(id);
      if (["shape.move", "connection.move", "elements.move", "shape.replace", "canvas.updateRoot"].includes(action.command)) moves.add(id);
      if (["shape.replace", "canvas.updateRoot"].includes(action.command)) replacements.add(id);
      if (action.command === "connection.reconnect") connections.add(id);
      if (action.command === "element.updateAttachment") attachments.add(id);
      if (action.command === "lane.updateRefs") laneRefs.add(id);
      if (action.command === "group.updateRefs") categoryRefs.add(id);
    }
    if (action.command === "element.updateProperties" || action.command === "element.updateModdleProperties") {
      const id = action.moddleOwnerId ?? action.elementId;
      if (id && !action.moddlePath?.length) propertyFields.set(id, new Set([...(propertyFields.get(id) ?? []), ...fieldNames(action)]));
      rootProperties ||= !!action.moddleDocumentRoot;
      if (action.moddleDocumentRoot) fieldNames(action).forEach(name => rootFields.add(name));
    }
  }
  if (!targets.size && !rootProperties) nativeRefuse("native command targets are unavailable");
  // Connection maintenance legitimately updates the endpoints' incoming/outgoing/default fields.
  const endpointOwners = new Set<string>();
  const reference = (element: Element, value: string) => {
    if (!value.includes(":")) return value;
    const [prefix, local] = value.split(":");
    return element.lookupNamespaceURI(prefix) === element.ownerDocument.documentElement.getAttribute("targetNamespace") ? local : undefined;
  };
  const groupLabels = new Map<string, string | null>();
  for (const action of group.actions) if (action.command === "element.updateLabel") {
    if (action.newLabel !== null && typeof action.newLabel !== "string") nativeRefuse("label command has no captured text intent");
    const element = newIndex.get(action.elementId ?? "");
    if (element?.localName === "textAnnotation") {
      const text = nativeChildren(element, ns.model, "text");
      if (action.newLabel === null ? text.length !== 0 : text.length !== 1 || text[0].textContent !== action.newLabel) nativeRefuse("annotation label differs from completed command intent");
    }
    if (element?.localName === "group") {
      const category = reference(element, element.getAttribute("categoryValueRef") ?? "");
      if (!category || newIndex.get(category)?.getAttribute("value") !== action.newLabel) nativeRefuse("group category label differs from completed command intent");
      targets.add(category); groupLabels.set(category, action.newLabel);
    } else if (element?.localName !== "textAnnotation" && element?.getAttribute("name") !== action.newLabel) nativeRefuse("label differs from completed command intent");
  }
  const references = (element: Element): string[] => [...element.attributes].flatMap(attribute =>
    !attribute.namespaceURI && (attribute.localName.endsWith("Ref") || ["default", "bpmnElement"].includes(attribute.localName))
      ? attribute.value.trim().split(/\s+/).map(value => reference(element, value)).filter((value): value is string => !!value) : [])
    .concat([...element.children].flatMap(child => child.namespaceURI === ns.model && (child.localName.endsWith("Ref") || ["incoming", "outgoing", "categoryValueRef"].includes(child.localName))
      ? [reference(child, child.textContent?.trim() ?? "")].filter((value): value is string => !!value) : []));
  for (const action of group.actions) {
    if (!["element.updateProperties", "element.updateModdleProperties"].includes(action.command)) continue;
    const owner = action.moddleDocumentRoot ? next.documentElement : newIndex.get(action.moddleOwnerId ?? action.elementId ?? "");
    if (!owner) nativeRefuse("property command owner is unavailable");
    const element = modeledProperty(owner, action.moddlePath ?? []);
    const intents = action.propertyIntents ?? [], captured = new Set(intents.map(intent => intent.name));
    if (captured.size !== intents.length || intents.some(intent => !fieldNames(action).includes(intent.name))) nativeRefuse("property intent membership is unqualified");
    assertNativePropertyIntents(intents, element, oldIndex, newIndex, ns.model);
    for (const name of fieldNames(action)) {
      if (captured.has(name)) continue;
      if (!action.properties || !Object.hasOwn(action.properties, name)) nativeRefuse("modeled property intent snapshot is unavailable");
      const value = action.properties[name];
      if (name === "di") continue;
      if (typeof value === "object" && value !== null || value == null && !element.hasAttribute(name) && !oldIndex.get(action.moddleOwnerId ?? action.elementId ?? "")?.hasAttribute(name)) nativeRefuse("modeled child/reference property needs its captured intent snapshot");
      const actual = name === "body" ? element.textContent : element.getAttribute(name);
      if (!(name === "body" ? actual === (value == null ? null : String(value)) : nativePrimitiveMatches(element, name, value))) nativeRefuse("modeled property differs from completed command intent");
    }
  }
  for (const id of structural) for (const index of [oldIndex, newIndex]) {
    const element = index.get(id);
    if (element && ["sequenceFlow", "messageFlow", "association", "dataInputAssociation", "dataOutputAssociation"].includes(element.localName))
      references(element).forEach(value => endpointOwners.add(value));
  }
  // Added dependent globals (message/signal/dataObject/category etc.) must be reachable from real
  // command targets. This never authorizes a property edit to an unrelated existing global.
  const related = new Set(targets);
  for (const id of related) {
    const element = newIndex.get(id);
    if (element) for (const child of [element, ...element.getElementsByTagName("*")]) {
      const childId = child.getAttribute("id"); if (childId && !oldIndex.has(childId)) related.add(childId);
      for (const value of references(child)) if (!oldIndex.has(value)) related.add(value);
    }
  }
  for (const action of group.actions) for (const value of action.relatedIds ?? []) related.add(value);
  const selectedDiagram = authoring.readBpmnDocument(baseline.projection.owner).byId.get(baseline.projection.scope.diagramId)!;
  const selectedContainers = new Set([baseline.projection.scope.diagramId, nativeChildren(selectedDiagram, ns.diagram, "BPMNPlane")[0]?.getAttribute("id")]);
  const inAuthority = (element: Element) => {
    const id = element.getAttribute("id");
    if (id && baseline.projection.reservedIds.has(id) && !baseline.projection.scopeElementIds.has(id)
      && !baseline.projection.scopeDiIds.has(id) && !targets.has(id) && !selectedContainers.has(id)) return false;
    return true;
  };
  const nearestId = (element: Element): string | undefined => {
    for (let current: Element | null = element; current; current = current.parentElement) if (current.hasAttribute("id")) return current.getAttribute("id")!;
  };
  const authorized = (element: Element, operation: "insert" | "remove" | "property" | "move", field?: string): boolean => {
    if (!inAuthority(element)) return false;
    const id = nearestId(element);
    let scopedProperty = false;
    if (id) for (const action of group.actions) if ((action.moddleOwnerId ?? action.elementId) === id && action.moddlePath?.length) {
      let owner = element;
      while (owner.parentElement && owner.getAttribute("id") !== id) owner = owner.parentElement;
      const property = modeledProperty(owner, action.moddlePath);
      if (property === element) {
        scopedProperty = true;
        if (operation === "property" && fieldNames(action).includes(field ?? "body")) return true;
      }
      else if (property.contains(element)) {
        let child = element;
        while (child.parentElement !== property) child = child.parentElement!;
        scopedProperty = true;
        if (fieldNames(action).some(name => propertyMatch(name, child))) return true;
      }
    }
    if (scopedProperty) return false;
    if (id && propertyFields.has(id)) {
      if (element.hasAttribute("id") && operation === "property" && propertyFields.get(id)!.has(field ?? "")) return true;
      if (!element.hasAttribute("id")) {
        let property = element;
        while (property.parentElement && property.parentElement.getAttribute("id") !== id) property = property.parentElement;
        if ([...propertyFields.get(id)!].some(name => propertyMatch(name, property))) return true;
      }
    }
    if (element.namespaceURI === ns.diagram || element.namespaceURI === ns.bounds || element.namespaceURI === ns.coordinates) {
      let entry: Element | null = element;
      while (entry && !(entry.namespaceURI === ns.diagram && ["BPMNShape", "BPMNEdge"].includes(entry.localName))) entry = entry.parentElement;
      if (id && (geometryIds.has(id) || structural.has(entry?.getAttribute("bpmnElement") ?? ""))) return true;
      if (operation === "insert" && id && !oldIndex.has(id)) return related.has(entry?.getAttribute("bpmnElement") ?? "") || related.has(id);
      if (element.namespaceURI === ns.diagram && ["BPMNDiagram", "BPMNPlane"].includes(element.localName))
        return (!id || !oldIndex.has(id) || selectedContainers.has(id)) && group.actions.some(action => ["canvas.updateRoot", "shape.toggleCollapse", "shape.create", "shape.replace"].includes(action.command));
      return false;
    }
    if (element === next.documentElement || element === before.documentElement) return rootProperties && (field === undefined || rootFields.has(field));
    if (id && structural.has(id)) {
      if (operation === "property" && (replacements.has(id) || connections.has(id) && ["sourceRef", "targetRef"].includes(field ?? "") || attachments.has(id) && field === "attachedToRef")) return true;
      if (operation === "move" && moves.has(id)) return true;
      if (operation === "remove" && removals.has(id)) return true;
      if (operation === "insert" && !oldIndex.has(id)) return true;
      if (!element.hasAttribute("id") && replacements.has(id)) return true;
    }
    if (id && targets.has(id)) {
      const fields = propertyFields.get(id);
      if (operation === "property" && element.hasAttribute("id") && (fields?.has(field ?? "") || group.actions.some(action => action.elementId === id && action.command === "element.updateLabel" && ["name", "value"].includes(field ?? "")))) return true;
      if (!element.hasAttribute("id") && group.actions.some(action => action.elementId === id && action.command === "element.updateLabel") && ["text", "categoryValue"].includes(element.localName)) return true;
      if (operation === "property" && field === "value" && groupLabels.has(id)) return true;
    }
    if (operation === "insert" && id && !oldIndex.has(id) && related.has(id)) return true;
    if (operation === "insert" && [...element.getElementsByTagName("*")].some(child => related.has(child.getAttribute("id") ?? ""))) return true;
    if (operation === "insert") for (let parent = element.parentElement; parent; parent = parent.parentElement) if (inserted.has(parent)) return true;
    if (id && endpointOwners.has(id) && ["incoming", "outgoing", "sourceRef", "targetRef"].includes(element.localName)) return true;
    if (id && endpointOwners.has(id) && field === "default") return true;
    if (id && laneRefs.has(id) && element.localName === "flowNodeRef" || id && categoryRefs.has(id) && element.localName === "categoryValueRef") return true;
    for (let parent = element.parentElement; parent; parent = parent.parentElement)
      if (removals.has(parent.getAttribute("id") ?? "") && operation === "remove" && !newIndex.has(parent.getAttribute("id")!)
        || structural.has(parent.getAttribute("id") ?? "") && operation === "insert" && !oldIndex.has(parent.getAttribute("id")!)) return true;
    return false;
  };
  const requireAuthorized = (element: Element, operation: Parameters<typeof authorized>[1], field?: string) => {
    if (!authorized(element, operation, field)) nativeRefuse(`unaccounted ${operation} outside completed native command targets (${element.localName}${field ? `.${field}` : ""})`);
  };

  // Existing DI coordinates keep the separately qualified waypoint/label correspondence algorithm.
  const geometryView = before.cloneNode(true) as XMLDocument, geometryIndex = nativeIndex(geometryView), actions: NativeCommandAction[] = [];
  for (const [id, correspondence] of baseline.di) {
    const old = oldIndex.get(id), value = newIndex.get(id), target = geometryIndex.get(id);
    if (!old || !value || !target || old.localName !== value.localName) continue;
    const changed = (namespace: string, name: string) => nativeChildren(old, namespace, name).map(nativeSignature).join() !== nativeChildren(value, namespace, name).map(nativeSignature).join();
    for (const [namespace, name, command, label] of [[ns.bounds, "Bounds", "shape.resize", false], [ns.coordinates, "waypoint", "connection.updateWaypoints", false], [ns.diagram, "BPMNLabel", "shape.move", true]] as const) {
      if (!changed(namespace, name)) continue;
      if (!geometryIds.has(id) && !structural.has(correspondence.elementId)) nativeRefuse("unaccounted native geometry mutation");
      if (name === "BPMNLabel") {
        const oldLabel = nativeChildren(old, ns.diagram, name)[0], nextLabel = nativeChildren(value, ns.diagram, name)[0];
        // Paint/style changes are properties; only bounds belong to geometry.
        const oldBounds = oldLabel && nativeChildren(oldLabel, ns.bounds, "Bounds")[0], nextBounds = nextLabel && nativeChildren(nextLabel, ns.bounds, "Bounds")[0];
        if ((!oldBounds && !nextBounds) || oldBounds && nextBounds && nativeSignature(oldBounds) === nativeSignature(nextBounds)) continue;
        let labelTarget = nativeChildren(target, ns.diagram, name)[0];
        if (!labelTarget && nextBounds) { labelTarget = geometryView.createElementNS(ns.diagram, "bpmndi:BPMNLabel"); target.appendChild(labelTarget); }
        if (labelTarget) { nativeChildren(labelTarget, ns.bounds, "Bounds").forEach(node => node.remove()); if (nextBounds) labelTarget.appendChild(geometryView.importNode(nextBounds, true)); }
      } else {
        const oldChildren = nativeChildren(target, namespace, name), anchor = oldChildren.at(-1)?.nextSibling ?? null;
        oldChildren.forEach(node => node.remove());
        nativeChildren(value, namespace, name).forEach(node => target.insertBefore(geometryView.importNode(node, true), anchor));
      }
      actions.push({ command, elementId: correspondence.elementId, diId: id, label });
    }
  }
  const commands: BpmnDocumentCommand[] = actions.length ? [...geometry({ id: group.id, trigger: "execute", actions }, authoring.serializeBpmnXml(geometryView))] : [];
  const modeled = actions.length ? geometryView : before;
  const modelIndex = nativeIndex(modeled);
  const parsed = authoring.readBpmnDocument(authoring.applyBpmnDocumentCommands(baseline.projection.owner, commands));
  const raw = parsed.document, rawIndex = parsed.byId;
  const rawFor = new Map<Element, Element>();
  const sourceNode = (element: Element): Element => {
    const known = rawFor.get(element); if (known) return known;
    const id = element.getAttribute("id");
    let source = id ? rawIndex.get(id) : undefined;
    if (element === modeled.documentElement) source = raw.documentElement;
    if (!source && element.namespaceURI === ns.diagram && element.localName === "BPMNPlane") {
      const candidates = nativePlaneSources(baseline, element);
      if (candidates.size > 1) nativeRefuse("generated native plane has ambiguous canonical provenance");
      if (candidates.size === 1) source = rawIndex.get([...candidates][0]);
    }
    if (!source && element.parentElement) {
      const parent = sourceNode(element.parentElement);
      const siblings = [...element.parentElement.children].filter(child => kind(child) === kind(element));
      const originals = [...parent.children].filter(child => kind(child) === kind(element)
        && !(child.namespaceURI === ns.model && child.localName === "documentation" && child.children.length));
      source = originals[siblings.indexOf(element)];
    }
    if (!source) nativeRefuse(`raw source correspondence is unavailable for ${element.localName}`);
    rawFor.set(element, source); return source;
  };
  // Bind correspondence before edits shift ordinal property paths.
  for (const element of [...modeled.getElementsByTagName("*")]) {
    if (element.namespaceURI === ns.diagram && ["BPMNDiagram", "BPMNPlane"].includes(element.localName) && !rawIndex.has(element.getAttribute("id") ?? "")) continue;
    sourceNode(element);
  }
  const matched = new Map<Element, Element>([[next.documentElement, modeled.documentElement]]), used = new Set<Element>([modeled.documentElement]);
  const opaque = (element: Element) => {
    const source = sourceNode(element);
    const carriesOpaque = (member: Element): boolean => [...member.attributes].some(attribute => attribute.namespaceURI && !standard.has(attribute.namespaceURI) && ![xmlns, xsi, ...colorNamespaces].includes(attribute.namespaceURI))
      || [...member.childNodes].some(node => node.nodeType === 7 || node.nodeType === 8
        || node.nodeType === 1 && (!standard.has((node as Element).namespaceURI ?? "") || carriesOpaque(node as Element)));
    return carriesOpaque(source);
  };
  const matchChildren = (parent: Element) => {
    const previous = matched.get(parent);
    for (const child of [...parent.children]) {
      const old = child.hasAttribute("id") ? modelIndex.get(child.getAttribute("id")!) : undefined;
      if (old) { matched.set(child, old); used.add(old); }
    }
    const groups = new Set([...parent.children].filter(child => !child.hasAttribute("id")).map(kind));
    for (const key of groups) {
      const values = [...parent.children].filter(child => !child.hasAttribute("id") && kind(child) === key);
      const candidates = [...previous?.children ?? []].filter(child => !child.hasAttribute("id") && kind(child) === key && !used.has(child));
      if (values.length === candidates.length && values.every((value, i) => nativeSignature(value) === nativeSignature(candidates[i]))) {
        values.forEach((value, i) => { matched.set(value, candidates[i]); used.add(candidates[i]); });
        continue;
      }
      for (const value of values) {
        const exact = candidates.filter(child => !used.has(child) && nativeImportSignature(child, ns.model) === nativeImportSignature(value, ns.model));
        if (exact.length === 1) { matched.set(value, exact[0]); used.add(exact[0]); }
      }
      const remaining = values.filter(child => !matched.has(child)), unmatched = candidates.filter(child => !used.has(child));
      if (remaining.length === unmatched.length) {
        if (remaining.length > 1 && unmatched.some(opaque)) nativeRefuse("ambiguous idless configuration correspondence carries opaque source metadata");
        remaining.forEach((value, i) => { matched.set(value, unmatched[i]); used.add(unmatched[i]); });
      } else if (unmatched.some(opaque) && remaining.length) nativeRefuse("changed idless membership needs explicit opaque source correspondence");
    }
    for (const child of [...parent.children]) matchChildren(child);
  };
  matchChildren(next.documentElement);

  const replaceModel = (old: Element, localName: string) => {
    const replacement = modeled.createElementNS(old.namespaceURI, old.prefix ? `${old.prefix}:${localName}` : localName);
    for (const attr of [...old.attributes]) replacement.setAttributeNS(attr.namespaceURI, attr.name, attr.value);
    while (old.firstChild) replacement.appendChild(old.firstChild);
    old.replaceWith(replacement); return replacement;
  };
  const coordinateShell = (value: Element, shell: Element) => {
    if (!(value.namespaceURI === ns.bounds && value.localName === "Bounds") && !(value.namespaceURI === ns.coordinates && value.localName === "waypoint")) return;
    for (const name of value.namespaceURI === ns.bounds ? ["x", "y", "width", "height"] : ["x", "y"]) {
      const number = value.getAttribute(name);
      if (number === null || !number.trim() || !Number.isFinite(Number(number)) || ["width", "height"].includes(name) && Number(number) <= 0) nativeRefuse("new native geometry must be finite with positive bounds");
    }
    let entry = value.parentElement;
    while (entry && !(entry.namespaceURI === ns.diagram && ["BPMNShape", "BPMNEdge"].includes(entry.localName))) entry = entry.parentElement;
    if (!entry || baseline.di.has(entry.getAttribute("id")!)) return;
    const nativeScope = entry.parentElement?.getAttribute("bpmnElement");
    const candidates = [...baseline.di.values()].filter(item => item.nativePlaneScopeId === nativeScope);
    const label = value.parentElement?.localName === "BPMNLabel";
    const offsets = candidates.flatMap(item => {
      const offset = label ? item.labelOffset ?? item.boundsOffset ?? item.pointsOffset
        : value.localName === "waypoint" ? item.pointsOffset ?? item.boundsOffset : item.boundsOffset ?? item.pointsOffset;
      return offset ? [offset] : [];
    });
    if (offsets.some(offset => offset.x !== offsets[0].x || offset.y !== offsets[0].y)) nativeRefuse("new DI has ambiguous source plane translation");
    if (offsets.length) for (const name of ["x", "y"] as const) shell.setAttribute(name, String(Number(value.getAttribute(name)) - offsets[0][name]));
  };
  const sourceQName = (source: Element, context: Element, value: string | null, typed = false) => {
    if (value === null) return null;
    const match = /^(?:([\p{L}_][\p{L}\p{N}\p{M}_.-]*):)?([\p{L}_][\p{L}\p{N}\p{M}_.-]*)$/u.exec(value);
    if (!match || !match[1] && !typed) return value;
    const namespace = context.lookupNamespaceURI(match[1] ?? null);
    if (!namespace) {
      if (match[1] || source.lookupNamespaceURI(null)) nativeRefuse("new QName has no representable native namespace context");
      return value;
    }
    if (source.lookupNamespaceURI(match[1] ?? null) === namespace) return value;
    const existing = source.lookupPrefix(namespace);
    if (existing) return `${existing}:${match[2]}`;
    const stem = match[1] ?? "nativeQName";
    let prefix = stem, suffix = 0;
    // A fresh prefix avoids changing the meaning of retained opaque strings or rebinding a source prefix.
    while (source.lookupNamespaceURI(prefix) || [source, ...source.getElementsByTagName("*")].some(element => [...element.attributes].some(attr => attr.value.includes(`${prefix}:`))
      || element.textContent?.includes(`${prefix}:`))) prefix = `${stem}_native_${++suffix}`;
    commands.push({ kind: "namespace", target: address(source), prefix, namespaceURI: namespace });
    source.setAttributeNS(xmlns, `xmlns:${prefix}`, namespace);
    return `${prefix}:${match[2]}`;
  };
  const patch = (value: Element, parent?: Element, anchor?: Element) => {
    let target = matched.get(value), source: Element;
    // Pinned SubprocessCompatibility creates an idless diagram wrapper with no raw counterpart.
    // Its children retain explicit DI correspondence; the wrapper itself cannot become source.
    if (target?.namespaceURI === ns.diagram && target.localName === "BPMNDiagram" && !rawIndex.has(target.getAttribute("id") ?? "")) {
      if (nativeSignature(target.cloneNode(false) as Element) !== nativeSignature(value.cloneNode(false) as Element)) nativeRefuse("generated diagram wrapper changed");
      let childAnchor: Element | undefined;
      for (const child of [...value.children].reverse()) { patch(child, target, childAnchor); childAnchor = matched.get(child)!; }
      return;
    }
    if (!target) {
      if (!standard.has(value.namespaceURI ?? "")) nativeRefuse("new native content is outside the pinned modeled namespaces");
      if (value.namespaceURI === ns.diagram && value.localName === "BPMNEdge" && nativeChildren(value, ns.coordinates, "waypoint").length < 2) nativeRefuse("new native route needs two endpoints");
      requireAuthorized(value, "insert");
      inserted.add(value);
      const shell = value.cloneNode(false) as Element;
      // The child walk inserts/reparents every child separately; never clone an existing ID into a new container.
      if (!value.children.length) for (const child of [...value.childNodes]) shell.appendChild(child.cloneNode(true));
      const rawParent = sourceNode(parent!);
      const rawAnchor = anchor ? sourceNode(anchor) : undefined;
      const rawShell = shell.cloneNode(true) as Element; coordinateShell(value, rawShell);
      const fragment = contextXml(authoring, rawShell, value);
      commands.push({ kind: "insert", parent: address(rawParent), xml: fragment, ...(rawAnchor ? { before: address(rawAnchor) } : {}) });
      source = raw.importNode(nativeXml(fragment).documentElement, true); rawParent.insertBefore(source, rawAnchor ?? null);
      target = modeled.importNode(shell, true); parent!.insertBefore(target, anchor ?? null);
      rawFor.set(target, source); matched.set(value, target); used.add(target);
    } else {
      source = sourceNode(target);
      if (target.namespaceURI !== value.namespaceURI) nativeRefuse("existing modeled identity changed namespace");
      if (target.localName !== value.localName) {
        requireAuthorized(value, "property");
        if (value.namespaceURI !== ns.model) nativeRefuse("DI identity changed its notation kind");
        commands.push({ kind: "replaceType", target: address(source), localName: value.localName });
        const rawReplacement = raw.createElementNS(source.namespaceURI, source.prefix ? `${source.prefix}:${value.localName}` : value.localName);
        for (const attr of [...source.attributes]) rawReplacement.setAttributeNS(attr.namespaceURI, attr.name, attr.value);
        while (source.firstChild) rawReplacement.appendChild(source.firstChild); source.replaceWith(rawReplacement); source = rawReplacement;
        target = replaceModel(target, value.localName); rawFor.set(target, source); matched.set(value, target); used.add(target);
      }
      let nextRetained = target.nextElementSibling;
      while (nextRetained && !used.has(nextRetained)) nextRetained = nextRetained.nextElementSibling;
      if (parent && (target.parentElement !== parent || nextRetained !== (anchor ?? null))) {
        requireAuthorized(value, "move");
        commands.push({ kind: "reparent", target: address(source), parent: address(sourceNode(parent)), before: anchor ? address(sourceNode(anchor)) : null });
        sourceNode(parent).insertBefore(source, anchor ? sourceNode(anchor) : null); parent.insertBefore(target, anchor ?? null);
      }
      const attributes = new Map([...target.attributes, ...value.attributes].map(attribute => [`${attribute.namespaceURI}|${attribute.localName}`, attribute]));
      for (const attribute of attributes.values()) {
        const oldValue = target.getAttributeNS(attribute.namespaceURI, attribute.localName), nextValue = value.getAttributeNS(attribute.namespaceURI, attribute.localName);
        if (oldValue === nextValue) continue;
        if (attribute.namespaceURI === xmlns) {
          const usedByIntent = [...next.getElementsByTagName("*")].some(element => [...element.attributes].some(field =>
            (field.namespaceURI === xsi && field.localName === "type" || ["calledElement", "structureRef", "implementationRef"].includes(field.localName))
            && field.value.startsWith(`${attribute.localName}:`) && element.lookupNamespaceURI(attribute.localName) === nextValue && authorized(element, "property", field.localName)));
          if (oldValue !== null || nextValue === null || ![...standard, ...colorNamespaces, xsi].includes(nextValue) && !usedByIntent) nativeRefuse("unaccounted native namespace rebinding/removal");
          target.setAttributeNS(xmlns, attribute.name, nextValue); continue;
        }
        if (attribute.localName === "id" && !attribute.namespaceURI) nativeRefuse("identity changes require an explicit reference-remapping operation");
        if (target.namespaceURI === ns.diagram && ["BPMNShape", "BPMNEdge"].includes(target.localName) && attribute.localName === "bpmnElement") nativeRefuse("retained DI identity cannot change its semantic target");
        requireAuthorized(value, "property", attribute.localName);
        if (attribute.namespaceURI && ![...standard, ...colorNamespaces, xsi, "http://www.w3.org/XML/1998/namespace"].includes(attribute.namespaceURI)) nativeRefuse("unmapped native attribute namespace");
        let name = attribute.name;
        if (attribute.prefix && attribute.namespaceURI && source.lookupNamespaceURI(attribute.prefix) !== attribute.namespaceURI) {
          const rewritten = sourceQName(source, value, `${attribute.prefix}:${attribute.localName}`, true)!;
          name = rewritten;
        }
        const qNameValue = attribute.namespaceURI === xsi && attribute.localName === "type" || ["calledElement", "structureRef", "implementationRef"].includes(attribute.localName);
        const rawValue = qNameValue ? sourceQName(source, value, nextValue, true) : nextValue;
        commands.push({ kind: "attribute", target: address(source), name, ...(attribute.namespaceURI ? { namespaceURI: attribute.namespaceURI } : {}), value: rawValue });
        if (nextValue === null) { source.removeAttributeNS(attribute.namespaceURI, attribute.localName); target.removeAttributeNS(attribute.namespaceURI, attribute.localName); }
        else { source.setAttributeNS(attribute.namespaceURI, name, rawValue!); target.setAttributeNS(attribute.namespaceURI, attribute.name, nextValue); }
      }
      if (!value.children.length && !target.children.length && target.textContent !== value.textContent) {
        requireAuthorized(value, "property");
        if ([...source.childNodes].some(node => node.nodeType === 7 || node.nodeType === 8 || node.nodeType === 1)) nativeRefuse("text edit needs explicit opaque child preservation");
        commands.push({ kind: "text", target: address(source), value: value.textContent ?? "" });
        source.textContent = value.textContent; target.textContent = value.textContent;
      }
    }
    let beforeChild: Element | undefined;
    for (const child of [...value.children].reverse()) { patch(child, target, beforeChild); beforeChild = matched.get(child)!; }
  };
  patch(next.documentElement);
  // Descendants retained elsewhere have already moved. Delete only the remaining unmatched roots.
  for (const element of [...modeled.getElementsByTagName("*")].reverse()) {
    if (used.has(element) || !element.parentElement || !modeled.documentElement.contains(element)) continue;
    requireAuthorized(element, "remove");
    const source = sourceNode(element);
    commands.push({ kind: "remove", target: address(source) }); source.remove(); element.remove();
  }
  if (nativeSignature(modeled.documentElement) !== nativeSignature(next.documentElement)) nativeRefuse("the complete native command group contains unaccounted XML/DI changes");
  return Object.freeze(commands);
}
