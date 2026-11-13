import type { ActivityNode, BpmnBounds, BpmnDocumentAuthoring, BpmnPoint, BpmnScopeAddress, BpmnXmlAddress } from "@elsa-workflows/studio-workflows";
import { nativeChildren, nativeImportSignature, nativeIndex, nativeNumber, nativeRefuse, nativeXml } from "./bpmnDocumentXml";
import { normalizeNativeDiIds } from "./bpmnDocumentDiAliases";
import { captureNativeNamespaceContexts, contextualizeNativeNamespacePair, type NativeNamespaceContext } from "./bpmnDocumentNamespaceContext";

export interface NativeReferenceRepair { readonly address: BpmnXmlAddress; readonly attribute?: string; readonly original: string; readonly projected: string }
export interface NativeDiCorrespondence {
  readonly id: string;
  readonly elementId: string;
  readonly sourcePlaneId: string;
  readonly sourceBounds?: BpmnBounds;
  readonly sourcePoints?: readonly BpmnPoint[];
  readonly sourceLabel?: BpmnBounds;
}
export interface BpmnNativeProjection {
  readonly owner: ActivityNode;
  /** Exact source bytes/string, never the native serializer's output. */
  readonly sourceXml: string;
  readonly xml: string;
  readonly scope: Readonly<BpmnScopeAddress>;
  readonly repairs: readonly NativeReferenceRepair[];
  readonly omitted: readonly BpmnXmlAddress[];
  readonly di: ReadonlyMap<string, NativeDiCorrespondence>;
  readonly reservedIds: ReadonlySet<string>;
  readonly scopeElementIds: ReadonlySet<string>;
  readonly scopeDiIds: ReadonlySet<string>;
  /** Projection-owned contexts survive native namespace-declaration normalization. */
  readonly namespaceContexts: ReadonlyMap<string, NativeNamespaceContext>;
}
export interface NativeDiNormalization extends NativeDiCorrespondence {
  readonly nativePlaneScopeId: string;
  readonly boundsOffset?: BpmnPoint;
  readonly pointsOffset?: BpmnPoint;
  readonly labelOffset?: BpmnPoint;
}
export interface BpmnNativeBaseline {
  readonly projection: BpmnNativeProjection;
  /** Actual captured native XML; source namespace meaning is kept separately in the projection. */
  readonly xml: string;
  readonly di: ReadonlyMap<string, NativeDiNormalization>;
}

function address(element: Element): BpmnXmlAddress {
  const path: NonNullable<BpmnXmlAddress["path"]> = [];
  let current = element;
  while (!current.getAttribute("id")) {
    const parent = current.parentElement;
    if (!parent) return { documentRoot: true, ...(path.length ? { path } : {}) };
    path.unshift({ namespaceURI: current.namespaceURI!, localName: current.localName,
      index: nativeChildren(parent, current.namespaceURI!, current.localName).indexOf(current) });
    current = parent;
  }
  return { elementId: current.getAttribute("id")!, ...(path.length ? { path } : {}) };
}
function bounds(element: Element): BpmnBounds {
  const result = { x: nativeNumber(element, "x"), y: nativeNumber(element, "y"), width: nativeNumber(element, "width"), height: nativeNumber(element, "height") };
  if (result.width <= 0 || result.height <= 0) nativeRefuse("DI bounds must have positive dimensions");
  return result;
}
function singleton(element: Element, namespace: string, name: string): Element | undefined {
  const found = nativeChildren(element, namespace, name);
  if (found.length > 1) nativeRefuse(`'${element.getAttribute("id")}' has ambiguous ${name}`);
  return found[0];
}
function plane(element: Element, diagramNamespace: string): Element {
  const parent = element.parentElement;
  if (parent?.namespaceURI !== diagramNamespace || parent.localName !== "BPMNPlane") nativeRefuse("DI entry has no direct plane");
  return parent;
}

/** Raw canonical DOM comes from the Workflows owner; only a clone is repaired for native import. */
export function projectBpmnNativeDocument(authoring: BpmnDocumentAuthoring, owner: ActivityNode, scope: BpmnScopeAddress): BpmnNativeProjection {
  const source = authoring.readBpmnDocument(owner), ns = authoring.bpmnNamespaces;
  if (scope.documentOwnerNodeId !== owner.nodeId) nativeRefuse("document owner is stale");
  const diagram = source.byId.get(scope.diagramId), selected = source.byId.get(scope.scopeElementId);
  if (diagram?.namespaceURI !== ns.diagram || diagram.localName !== "BPMNDiagram") nativeRefuse("selected DI diagram is unavailable");
  if (selected?.namespaceURI !== ns.model || !["collaboration", "process", "subProcess", "transaction", "adHocSubProcess"].includes(selected.localName))
    nativeRefuse("selected standard scope is unavailable");
  const selectedPlane = singleton(diagram, ns.diagram, "BPMNPlane");
  const represented = selectedPlane && authoring.resolveBpmnDocumentReference(source, selectedPlane, selectedPlane.getAttribute("bpmnElement"));
  const representedProcesses = represented?.localName === "collaboration" ? nativeChildren(represented, ns.model, "participant")
    .map(participant => authoring.resolveBpmnDocumentReference(source, participant, participant.getAttribute("processRef"))) : [];
  if (!represented || represented !== selected && !represented.contains(selected) && !representedProcesses.some(process => process?.contains(selected)))
    nativeRefuse("selected scope is not represented by the selected DI diagram");
  const scopeElementIds = new Set([...source.byId].filter(([, element]) => element.namespaceURI === ns.model
    && (selected === element || selected.contains(element) || selected.localName === "collaboration"
      && representedProcesses.some(process => process === element || process?.contains(element)))).map(([id]) => id));
  const document = source.document.cloneNode(true) as XMLDocument;
  const repairs: NativeReferenceRepair[] = [], omitted: BpmnXmlAddress[] = [];
  const standardNamespaces = new Set<string>(Object.values(ns));
  const safeAttributeNamespaces = new Set([...standardNamespaces, "http://www.omg.org/spec/BPMN/non-normative/color/1.0", "http://bpmn.io/schema/bpmn/biocolor/1.0",
    "http://www.w3.org/2001/XMLSchema-instance", "http://www.w3.org/XML/1998/namespace", "http://www.w3.org/2000/xmlns/"]);
  // This table intentionally distinguishes same-named QName and IDREF properties in the pinned XSD.
  const qNameAttributes: Record<string, readonly string[]> = {
    participant: ["processRef"], boundaryEvent: ["attachedToRef"], association: ["sourceRef", "targetRef"],
    messageFlow: ["sourceRef", "targetRef", "messageRef"], messageEventDefinition: ["messageRef"],
    signalEventDefinition: ["signalRef"], errorEventDefinition: ["errorRef"], escalationEventDefinition: ["escalationRef"],
    compensateEventDefinition: ["activityRef"], group: ["categoryValueRef"], dataStoreReference: ["itemSubjectRef", "dataStoreRef"],
    dataInput: ["itemSubjectRef"], dataOutput: ["itemSubjectRef"], dataObject: ["itemSubjectRef"], dataObjectReference: ["itemSubjectRef"],
    dataStore: ["itemSubjectRef"], property: ["itemSubjectRef"], message: ["itemRef"], error: ["structureRef"], signal: ["structureRef"], escalation: ["structureRef"],
    sendTask: ["messageRef", "operationRef"], receiveTask: ["messageRef", "operationRef"], serviceTask: ["operationRef"],
    multiInstanceLoopCharacteristics: ["oneBehaviorEventRef", "noneBehaviorEventRef"],
    process: ["definitionalCollaborationRef"], correlationProperty: ["type"], correlationPropertyBinding: ["correlationPropertyRef"],
    correlationPropertyRetrievalExpression: ["messageRef"], correlationSubscription: ["correlationKeyRef"], resourceParameter: ["type"],
    resourceParameterBinding: ["parameterRef"], inputOutputBinding: ["inputDataRef", "outputDataRef", "operationRef"],
    BPMNPlane: ["bpmnElement"], BPMNShape: ["bpmnElement", "choreographyActivityShape"], BPMNEdge: ["bpmnElement", "sourceElement", "targetElement"], BPMNLabel: ["labelStyle"]
  };
  const qNameText = new Set(["incoming", "outgoing", "categoryValueRef", "loopDataInputRef", "loopDataOutputRef", "eventDefinitionRef", "operationRef", "resourceRef", "supportedInterfaceRef", "interfaceRef", "supports", "inMessageRef", "outMessageRef", "errorRef"]);
  const referenceTypes: Readonly<Record<string, string>> = { processRef: "process", messageRef: "message", signalRef: "signal", errorRef: "error", escalationRef: "escalation",
    itemSubjectRef: "itemDefinition", itemRef: "itemDefinition", structureRef: "itemDefinition", categoryValueRef: "categoryValue", dataStoreRef: "dataStore", operationRef: "operation", labelStyle: "BPMNLabelStyle" };
  for (const element of [...document.getElementsByTagName("*")]) {
    if (!document.documentElement.contains(element)) continue;
    if (!standardNamespaces.has(element.namespaceURI ?? "")) continue;
    const sourceElement = source.byId.get(element.getAttribute("id") ?? "");
    const resolveReference = (value: string, expectedType?: string, namespaceURI: string = ns.model) => {
      const context = sourceElement ?? element;
      const resolved = authoring.resolveBpmnDocumentReference(source, context, value);
      if (!resolved) nativeRefuse(`modeled reference '${value}' cannot resolve in its original namespace scope`);
      if (resolved.namespaceURI !== namespaceURI || expectedType && resolved.localName !== expectedType)
        nativeRefuse(`modeled reference '${value}' has an incompatible standard target`);
      return resolved.getAttribute("id")!;
    };
    const referenceNames = [...(element.namespaceURI === ns.model || element.namespaceURI === ns.diagram ? qNameAttributes[element.localName] ?? [] : [])];
    const expressionType = element.getAttributeNS("http://www.w3.org/2001/XMLSchema-instance", "type")?.split(":");
    if (expressionType?.at(-1) === "tFormalExpression" && element.lookupNamespaceURI(expressionType.length === 2 ? expressionType[0] : null) === ns.model) referenceNames.push("evaluatesToTypeRef");
    for (const name of referenceNames) {
      const value = element.getAttribute(name);
      if (!value) continue;
      const expectedType = referenceTypes[name];
      const projected = resolveReference(value, expectedType, ["labelStyle", "sourceElement", "targetElement", "choreographyActivityShape"].includes(name) ? ns.diagram : ns.model);
      if (projected !== value) { repairs.push({ address: address(element), attribute: name, original: value, projected }); element.setAttribute(name, projected); }
    }
    if (element.namespaceURI === ns.model && qNameText.has(element.localName)) {
      const original = element.textContent?.trim() ?? "", projected = resolveReference(original, ["incoming", "outgoing"].includes(element.localName) ? "sequenceFlow" : referenceTypes[element.localName]);
      if (original !== projected) { repairs.push({ address: address(element), original, projected }); element.textContent = projected; }
    }
    for (const attribute of [...element.attributes]) {
      if (attribute.namespaceURI && !safeAttributeNamespaces.has(attribute.namespaceURI)) {
        omitted.push(address(element)); element.removeAttributeNode(attribute);
      }
    }
    if (element.namespaceURI === ns.model && element.localName === "extensionElements") {
      if (element.childNodes.length) { omitted.push(address(element)); element.replaceChildren(); }
    }
    if (element.namespaceURI === ns.model && element.localName === "documentation" && element.children.length) {
      omitted.push(address(element)); element.remove();
    }
  }
  const projectedById = nativeIndex(document), di = new Map<string, NativeDiCorrespondence>();
  for (const [id, element] of projectedById) {
    if (element.namespaceURI !== ns.diagram || !["BPMNShape", "BPMNEdge"].includes(element.localName)) continue;
    const raw = source.byId.get(id)!;
    const sourcePlaneId = plane(raw, ns.diagram).getAttribute("id");
    if (!sourcePlaneId) nativeRefuse("DI plane needs a source identity for reversible correspondence");
    const elementId = element.getAttribute("bpmnElement")!;
    const label = singleton(raw, ns.diagram, "BPMNLabel"), labelBounds = label && singleton(label, ns.bounds, "Bounds");
    const shapeBounds = singleton(raw, ns.bounds, "Bounds");
    const points = nativeChildren(raw, ns.coordinates, "waypoint").map(point => ({ x: nativeNumber(point, "x"), y: nativeNumber(point, "y") }));
    di.set(id, Object.freeze({ id, elementId, sourcePlaneId,
      ...(shapeBounds ? { sourceBounds: bounds(shapeBounds) } : {}), ...(points.length ? { sourcePoints: Object.freeze(points) } : {}),
      ...(labelBounds ? { sourceLabel: bounds(labelBounds) } : {}) }));
  }
  return Object.freeze({ owner, sourceXml: source.payload.documentXml, xml: authoring.serializeBpmnXml(document),
    namespaceContexts: captureNativeNamespaceContexts(document), scope: Object.freeze({ ...scope }), repairs: Object.freeze(repairs), omitted: Object.freeze(omitted), di, reservedIds: new Set(source.byId.keys()),
    scopeElementIds, scopeDiIds: new Set([...di].filter(([, entry]) => entry.sourcePlaneId === selectedPlane!.getAttribute("id") && scopeElementIds.has(entry.elementId)).map(([id]) => id)) });
}

/** DOM equivalent of the pinned SubprocessCompatibility import transformation, with no semantic edits. */
function normalizedView(authoring: BpmnDocumentAuthoring, projection: BpmnNativeProjection, previous?: BpmnNativeBaseline): XMLDocument {
  const ns = authoring.bpmnNamespaces, document = nativeXml(projection.xml), byId = nativeIndex(document);
  const subprocess = (element: Element | null | undefined) => element?.namespaceURI === ns.model && ["subProcess", "adHocSubProcess", "transaction"].includes(element.localName);
  const diagrams = [...document.documentElement.children].filter(element => element.namespaceURI === ns.diagram && element.localName === "BPMNDiagram");
  const planes = diagrams.map(diagram => singleton(diagram, ns.diagram, "BPMNPlane")).filter((value): value is Element => !!value);
  const byScope = new Map(planes.map(plane => [plane.getAttribute("bpmnElement"), plane]));
  const created: Element[] = [];
  for (const sourcePlane of planes) {
    const entries = [...sourcePlane.children].filter(element => element.namespaceURI === ns.diagram && ["BPMNShape", "BPMNEdge"].includes(element.localName));
    const collapsed = entries.map(element => ({ element, semantic: byId.get(element.getAttribute("bpmnElement") ?? "") }))
      .filter(item => subprocess(item.semantic) && !["true", "1"].includes(item.element.getAttribute("isExpanded") ?? "")).map(item => item.semantic!);
    for (const semantic of collapsed) {
      const id = semantic.getAttribute("id")!;
      if (byScope.has(id)) continue;
      const diagram = document.createElementNS(ns.diagram, "bpmndi:BPMNDiagram"), plane = document.createElementNS(ns.diagram, "bpmndi:BPMNPlane");
      plane.setAttribute("bpmnElement", id); diagram.appendChild(plane); document.documentElement.appendChild(diagram);
      byScope.set(id, plane); created.push(plane);
    }
    for (const entry of entries) {
      const semantic = byId.get(entry.getAttribute("bpmnElement") ?? ""), parent = semantic?.parentElement;
      if (!subprocess(parent) || parent!.getAttribute("id") === sourcePlane.getAttribute("bpmnElement")
        || semantic && ["dataInputAssociation", "dataOutputAssociation"].includes(semantic.localName)) continue;
      let target = parent;
      while (target && !collapsed.includes(target)) target = target.parentElement;
      if (target) byScope.get(target.getAttribute("id"))!.appendChild(entry);
    }
  }
  for (const plane of created) {
    const shapes = [...plane.children].flatMap(entry => nativeChildren(entry, ns.bounds, "Bounds"));
    if (!shapes.length) {
      if ([...plane.children].some(entry => nativeChildren(entry, ns.coordinates, "waypoint").length)) nativeRefuse("new native plane has no finite shape origin");
      continue;
    }
    const delta = { x: 180 - Math.min(...shapes.map(shape => nativeNumber(shape, "x"))), y: 160 - Math.min(...shapes.map(shape => nativeNumber(shape, "y"))) };
    for (const entry of [...plane.children]) {
      for (const point of [...nativeChildren(entry, ns.bounds, "Bounds"), ...nativeChildren(entry, ns.coordinates, "waypoint")]) {
        point.setAttribute("x", String(nativeNumber(point, "x") + delta.x)); point.setAttribute("y", String(nativeNumber(point, "y") + delta.y));
      }
    }
  }
  if (previous) {
    const oldScope = previous.projection.scope, nextScope = projection.scope;
    if (oldScope.documentOwnerNodeId !== nextScope.documentOwnerNodeId || oldScope.diagramId !== nextScope.diagramId
      || oldScope.scopeElementId !== nextScope.scopeElementId) nativeRefuse("import-generation DI authority changed");
    const normalized = nativeIndex(document);
    for (const [id, source] of projection.di) {
      const before = [...previous.di.values()].find(value => value.id === id), entry = normalized.get(id)!;
      const nativeScope = plane(entry, ns.diagram).getAttribute("bpmnElement");
      if (before && before.elementId !== source.elementId) nativeRefuse("import-generation DI semantic correspondence changed");
      const candidates = [...previous.di.values()].filter(value => value.sourcePlaneId === source.sourcePlaneId && value.nativePlaneScopeId === nativeScope);
      const offsets = (name: "boundsOffset" | "pointsOffset" | "labelOffset") => {
        const values = candidates.flatMap(value => value[name] ? [value[name]!] : []);
        if (values.some(value => value.x !== values[0].x || value.y !== values[0].y)) nativeRefuse("import-generation plane translations are ambiguous");
        return values[0];
      };
      const retained = before && before.sourcePlaneId === source.sourcePlaneId && before.nativePlaneScopeId === nativeScope ? before : undefined;
      const boundsOffset = retained?.boundsOffset ?? offsets("boundsOffset") ?? offsets("pointsOffset");
      const pointsOffset = retained?.pointsOffset ?? offsets("pointsOffset") ?? offsets("boundsOffset");
      const labelOffset = retained?.labelOffset ?? boundsOffset ?? pointsOffset;
      // New planes use only the declared pinned normalization above. Existing plane translations
      // remain stable when a structural command inserts/removes/moves one of their DI entries.
      if (!retained && !candidates.length) continue;
      const translate = (element: Element, value: BpmnPoint, offset?: BpmnPoint) => {
        element.setAttribute("x", String(value.x + (offset?.x ?? 0))); element.setAttribute("y", String(value.y + (offset?.y ?? 0)));
      };
      const shape = singleton(entry, ns.bounds, "Bounds");
      if (shape && source.sourceBounds) translate(shape, source.sourceBounds, boundsOffset);
      nativeChildren(entry, ns.coordinates, "waypoint").forEach((point, i) => translate(point, source.sourcePoints![i], pointsOffset));
      const label = singleton(entry, ns.diagram, "BPMNLabel"), labelBounds = label && singleton(label, ns.bounds, "Bounds");
      if (labelBounds && source.sourceLabel) translate(labelBounds, source.sourceLabel, labelOffset);
    }
  }
  return document;
}

/** Call only with saveXML captured AFTER the actual awaited native import/render has completed. */
export function bindBpmnNativeBaseline(authoring: BpmnDocumentAuthoring, projection: BpmnNativeProjection, xml: string, previous?: BpmnNativeBaseline,
  aliases: ReadonlyMap<string, string> = new Map()): BpmnNativeBaseline {
  const document = nativeXml(normalizeNativeDiIds(authoring, xml, aliases)), index = nativeIndex(document), ns = authoring.bpmnNamespaces;
  const nativeIds = new Map([...aliases].map(([nativeId, sourceId]) => [sourceId, nativeId]));
  for (const [id, source] of nativeIndex(nativeXml(projection.xml))) {
    if (source.namespaceURI !== ns.model) continue;
    const modeled = index.get(id);
    if (modeled?.namespaceURI !== ns.model || modeled.localName !== source.localName)
      nativeRefuse(`import removed or replaced standard identity '${id}'`);
  }
  const di = new Map<string, NativeDiNormalization>();
  const offset = (source: BpmnPoint, next: BpmnPoint) => ({ x: next.x - source.x, y: next.y - source.y });
  for (const [id, source] of projection.di) {
    const element = index.get(id);
    if (!element || element.namespaceURI !== ns.diagram || element.getAttribute("bpmnElement") !== source.elementId)
      nativeRefuse(`import changed DI identity '${id}' or its semantic target`);
    const nativePlaneScopeId = plane(element, ns.diagram).getAttribute("bpmnElement");
    if (!nativePlaneScopeId) nativeRefuse("native DI plane has no scope");
    const nextBounds = singleton(element, ns.bounds, "Bounds"), label = singleton(element, ns.diagram, "BPMNLabel"), nextLabel = label && singleton(label, ns.bounds, "Bounds");
    let boundsOffset: BpmnPoint | undefined, labelOffset: BpmnPoint | undefined, pointsOffset: BpmnPoint | undefined;
    if (source.sourceBounds) {
      if (!nextBounds) nativeRefuse(`import removed bounds '${id}'`);
      const next = bounds(nextBounds);
      if (next.width !== source.sourceBounds.width || next.height !== source.sourceBounds.height) nativeRefuse("import resized authored DI");
      boundsOffset = offset(source.sourceBounds, next);
    }
    if (source.sourceLabel) {
      if (!nextLabel) nativeRefuse(`import removed label bounds '${id}'`);
      const next = bounds(nextLabel);
      if (next.width !== source.sourceLabel.width || next.height !== source.sourceLabel.height) nativeRefuse("import resized authored label DI");
      labelOffset = offset(source.sourceLabel, next);
    }
    if (source.sourcePoints) {
      const next = nativeChildren(element, ns.coordinates, "waypoint").map(point => ({ x: nativeNumber(point, "x"), y: nativeNumber(point, "y") }));
      if (next.length !== source.sourcePoints.length) nativeRefuse("import changed authored route membership");
      pointsOffset = offset(source.sourcePoints[0], next[0]);
      if (next.some((point, i) => point.x - source.sourcePoints![i].x !== pointsOffset!.x || point.y - source.sourcePoints![i].y !== pointsOffset!.y))
        nativeRefuse("import route normalization is not a reversible translation");
    }
    di.set(nativeIds.get(id) ?? id, Object.freeze({ ...source, nativePlaneScopeId, boundsOffset, labelOffset, pointsOffset }));
  }
  const expected = normalizedView(authoring, projection, previous);
  const contextual = contextualizeNativeNamespacePair(authoring, projection.namespaceContexts, authoring.serializeBpmnXml(expected),
    authoring.serializeBpmnXml(document), [], projection.owner.nodeId, true);
  if (nativeImportSignature(nativeXml(contextual.before).documentElement, ns.model) !== nativeImportSignature(nativeXml(contextual.next).documentElement, ns.model))
    nativeRefuse("native import changed projected semantic/DI content outside the declared normalization ledger");
  return Object.freeze({ projection, xml, di });
}
