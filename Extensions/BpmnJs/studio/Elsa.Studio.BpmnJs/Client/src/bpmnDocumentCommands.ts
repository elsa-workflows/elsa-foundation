import type { BpmnBounds, BpmnDocumentAuthoring, BpmnDocumentCommand, BpmnPoint } from "@elsa-workflows/studio-workflows";
import type { BpmnNativeBaseline } from "./bpmnDocumentProjection";
import { nativeChildren, nativeGeometrySignature, nativeIndex, nativeNumber, nativeRefuse, nativeSignature, nativeXml } from "./bpmnDocumentXml";
import { guardBpmnNativeCopies } from "./bpmnDocumentCopyGuard";
import { assertBpmnNativePlaneFrames, diffBpmnNativeCommandGroup } from "./bpmnDocumentDiff";
import { nativeDiAliases, normalizeNativeDiIds } from "./bpmnDocumentDiAliases";
import { contextualizeNativeNamespacePair, type NativeNamespaceOrigin, type NativeNamespaceSnapshot } from "./bpmnDocumentNamespaceContext";
import { captureNativePropertyIntents, type NativePropertyIntent } from "./bpmnDocumentIntent";

/** Immutable, optional-module-only snapshots of the pinned command context. No modeler objects escape. */
export interface NativeCommandAction {
  readonly command: string;
  readonly elementId?: string;
  readonly diId?: string;
  readonly label?: boolean;
  readonly oldParentId?: string;
  readonly parentId?: string;
  readonly properties?: Readonly<Record<string, unknown>>;
  readonly propertyIntents?: readonly NativePropertyIntent[];
  readonly newLabel?: string | null;
  readonly oldElementId?: string;
  readonly newElementId?: string;
  readonly elementIds?: readonly string[];
  readonly moddleOwnerId?: string;
  readonly moddleDocumentRoot?: boolean;
  readonly moddlePath?: readonly string[];
  readonly propertyNames?: readonly string[];
  readonly relatedIds?: readonly string[];
  readonly diIds?: readonly string[];
}
export interface NativeCommandGroup { readonly id: number; readonly trigger: "execute"; readonly actions: readonly NativeCommandAction[]; readonly namespaceOrigins?: readonly NativeNamespaceOrigin[] }
interface NativeEvent { id?: number; command?: string; context?: Record<string, unknown>; trigger?: string; descriptor?: Record<string, unknown>; element?: unknown }
export interface NativeCommandEvents {
  on(name: string, priority: number, handler: (event: NativeEvent) => void): void;
  off(name: string, handler: (event: NativeEvent) => void): void;
}
function record(value: unknown): Record<string, unknown> | undefined {
  return value && typeof value === "object" ? value as Record<string, unknown> : undefined;
}
function id(value: unknown): string | undefined {
  const item = record(value), business = record(item?.businessObject);
  return typeof business?.id === "string" ? business.id : typeof item?.id === "string" ? item.id : undefined;
}
function snapshotAction(command: string, context: Record<string, unknown>): NativeCommandAction {
  const element = record(context.shape ?? context.connection ?? context.element);
  const target = record(element?.labelTarget ?? context.labelTarget) ?? element;
  const di = record(target?.di);
  const properties = record(context.properties);
  const propertyIntents = properties && captureNativePropertyIntents(context.moddleElement ?? target?.businessObject, properties);
  if (command === "element.updateLabel" && context.newLabel !== null && typeof context.newLabel !== "string") nativeRefuse("label command has no qualified text intent");
  const relatedIds = new Set<string>();
  const references = (value: unknown, seen = new Set<unknown>()) => {
    if (!value || typeof value !== "object" || seen.has(value)) return;
    seen.add(value);
    const identity = id(value); if (identity) relatedIds.add(identity);
    for (const [name, child] of Object.entries(value)) if (!name.startsWith("$") && name !== "businessObject" && name !== "parent") references(child, seen);
  };
  const copied = properties && Object.fromEntries(Object.entries(properties).flatMap(([name, value]) => {
    references(value);
    if (name === "default") return [[name, value == null ? null : id(value) ?? value]];
    // DI properties are a nested plain map; retain only primitives, never live moddle objects.
    if (name === "di" && record(value)) return [[name, { ...record(value) }]];
    if (value && typeof value === "object" && (Array.isArray(value) || typeof record(value)?.$type === "string")) return [];
    return [[name, value]];
  }));
  if (copied && Object.values(copied).some(value => value && typeof value === "object" && !Object.values(value).every(child => child == null || ["string", "boolean", "number"].includes(typeof child))))
    nativeRefuse("command properties contain an unrecognized modeled object");
  // structuredClone also prevents a later native command mutating this group's context facts.
  let moddle = record(context.moddleElement);
  const moddleDocumentRoot = moddle?.$type === "bpmn:Definitions";
  const moddlePath: string[] = [];
  while (moddle && !id(moddle) && moddle.$parent) {
    const parent = record(moddle.$parent)!;
    const property = Object.entries(parent).find(([name, value]) => !name.startsWith("$") && (value === moddle || Array.isArray(value) && value.includes(moddle)));
    if (!property) nativeRefuse("idless modeled command has no owning property correspondence");
    moddlePath.unshift(property[0]); moddle = parent;
  }
  const elementIds = [context.elements, context.shapes, context.flowNodeShapes, context.laneShapes, context.affectedFlowElements, context.affectedGroups]
    .flatMap(value => Array.isArray(value) ? value.map(id).filter((value): value is string => !!value) : []);
  if (Array.isArray(context.affectedGroupEntries)) elementIds.push(...context.affectedGroupEntries.flatMap(entry => {
    const identity = id(record(entry)?.groupShape); return identity ? [identity] : [];
  }));
  if (Array.isArray(context.updates)) for (const update of context.updates) {
    const value = record(update);
    for (const item of [value?.flowNode, ...(Array.isArray(value?.add) ? value.add : []), ...(Array.isArray(value?.remove) ? value.remove : [])]) {
      const identity = id(item); if (identity) elementIds.push(identity);
    }
  }
  const diIds = [context.elements, context.shapes].flatMap(value => Array.isArray(value)
    ? value.flatMap(element => typeof record(record(element)?.di)?.id === "string" ? [record(record(element)?.di)!.id as string] : []) : []);
  return Object.freeze({ command, elementId: id(target), diId: typeof di?.id === "string" ? di.id : undefined,
    label: !!element?.labelTarget || command === "label.create", oldParentId: id(context.oldParent ?? element?.parent), parentId: id(context.newParent ?? context.parent ?? element?.parent),
    ...(context.oldShape || context.oldRoot ? { oldElementId: id(context.oldShape ?? context.oldRoot) } : {}),
    ...(context.newShape || context.newRoot ? { newElementId: id(context.newShape ?? context.newRoot) } : {}),
    ...(elementIds.length ? { elementIds: Object.freeze(elementIds) } : {}),
    ...(diIds.length ? { diIds: Object.freeze(diIds) } : {}),
    ...(moddle ? { moddleOwnerId: id(moddle), ...(moddleDocumentRoot ? { moddleDocumentRoot: true } : {}) } : {}),
    ...(moddlePath.length ? { moddlePath: Object.freeze(moddlePath) } : {}),
    ...(properties ? { properties: Object.freeze(Object.fromEntries(Object.entries(structuredClone(copied!)).map(([name, value]) => [name, value && typeof value === "object" ? Object.freeze(value) : value]))), propertyNames: Object.freeze(Object.keys(properties)), propertyIntents } : {}),
    ...(relatedIds.size ? { relatedIds: Object.freeze([...relatedIds]) } : {}), ...(command === "element.updateLabel" ? { newLabel: context.newLabel as string | null } : {}) });
}

/** Contexts copy at the complete group boundary. Teardown belongs to clean controller retirement, not fallible view detachment. */
export function collectBpmnNativeCommandGroups(events: NativeCommandEvents, complete: (group: NativeCommandGroup) => void, refuse: (error: unknown) => void,
  snapshotNamespaces?: (elementId: string) => NativeNamespaceSnapshot | undefined): () => void {
  let groupId: number | undefined;
  let invalid: unknown;
  let contexts: { command: string; context: Record<string, unknown> }[] = [];
  // Supported pinned copy events preserve provenance without marking native objects or clipboard data.
  const copied = new WeakMap<object, NativeNamespaceSnapshot | undefined>(), pasting = new WeakMap<object, NativeNamespaceSnapshot | undefined>();
  const pastingDi = new WeakMap<object, NativeNamespaceSnapshot | undefined>();
  const namespaceOrigins = new Map<string, NativeNamespaceOrigin>();
  const copy = (event: NativeEvent) => {
    const business = record(event.descriptor?.businessObject), sourceId = id(event.element);
    if (business && sourceId) {
      const snapshot = snapshotNamespaces?.(sourceId);
      copied.set(business, snapshot && Object.freeze({ ownerNodeId: snapshot.ownerNodeId, xml: snapshot.xml }));
    }
    const di = record(event.descriptor?.di), sourceDiId = id(record(event.element)?.di);
    if (di && sourceDiId) {
      const snapshot = snapshotNamespaces?.(sourceDiId);
      copied.set(di, snapshot && Object.freeze({ ownerNodeId: snapshot.ownerNodeId, xml: snapshot.xml }));
    }
  };
  const pasteBefore = (event: NativeEvent) => {
    const descriptor = event.descriptor, business = record(descriptor?.businessObject);
    if (descriptor && business) pasting.set(descriptor, copied.get(business));
    const di = record(descriptor?.di); if (descriptor && di) pastingDi.set(descriptor, copied.get(di));
  };
  const pasteAfter = (event: NativeEvent) => {
    const descriptor = event.descriptor, targetId = id(descriptor);
    if (descriptor && targetId) namespaceOrigins.set(targetId, Object.freeze({ elementId: targetId, snapshot: pasting.get(descriptor) }));
    const diId = id(descriptor?.di);
    if (descriptor && diId) namespaceOrigins.set(diId, Object.freeze({ elementId: diId, snapshot: pastingDi.get(descriptor) }));
  };
  const before = (event: NativeEvent) => {
    if (!event.command || !event.context || !Number.isSafeInteger(event.id)) { invalid = new Error("Native command context is unavailable"); return; }
    if (groupId === undefined) groupId = event.id;
    else if (groupId !== event.id) invalid = new Error("Native command grouping changed");
    contexts.push({ command: event.command, context: event.context });
  };
  const changed = (event: NativeEvent) => {
    const currentId = groupId, actions = contexts, failure = invalid;
    groupId = undefined; contexts = []; invalid = undefined;
    if (event.trigger === "clear" && !actions.length) { namespaceOrigins.clear(); return; }
    try {
      if (failure) throw failure;
      if (event.trigger !== "execute" || currentId === undefined || !actions.length) nativeRefuse("native history cannot replace the shared host history");
      const snapshots = actions.map(action => snapshotAction(action.command, action.context));
      const created = new Set(snapshots.flatMap(action => [action.elementId, action.diId, ...(action.elementIds ?? []), ...(action.diIds ?? [])]));
      const origins = [...namespaceOrigins.values()].filter(origin => created.has(origin.elementId));
      complete(Object.freeze({ id: currentId, trigger: "execute", actions: Object.freeze(snapshots), ...(origins.length ? { namespaceOrigins: Object.freeze(origins) } : {}) }));
      namespaceOrigins.clear();
    } catch (error) { refuse(error); }
  };
  events.on("commandStack.preExecute", 20000, before);
  events.on("commandStack.changed", -20000, changed);
  events.on("copyPaste.copyElement", -20000, copy);
  events.on("copyPaste.pasteElement", 20000, pasteBefore);
  events.on("copyPaste.pasteElement", -20000, pasteAfter);
  return () => {
    events.off("commandStack.preExecute", before); events.off("commandStack.changed", changed);
    events.off("copyPaste.copyElement", copy); events.off("copyPaste.pasteElement", pasteBefore); events.off("copyPaste.pasteElement", pasteAfter);
    namespaceOrigins.clear();
  };
}

/** Bounded first slice. All unaccounted standard/DI changes refuse atomically; raw source is never replaced by nextXml. */
export function mapBpmnNativeCommandGroup(authoring: BpmnDocumentAuthoring, baseline: BpmnNativeBaseline, group: NativeCommandGroup, nextXml: string): readonly BpmnDocumentCommand[] {
  return mapBpmnNativeCommandPlan(authoring, baseline, group, nextXml).commands;
}
export function mapBpmnNativeCommandPlan(authoring: BpmnDocumentAuthoring, baseline: BpmnNativeBaseline, group: NativeCommandGroup, nextXml: string): {
  readonly commands: readonly BpmnDocumentCommand[]; readonly diAliases: ReadonlyMap<string, string>;
} {
  guardBpmnNativeCopies(authoring, baseline.projection, group.namespaceOrigins, nextXml);
  const diAliases = nativeDiAliases(authoring, baseline, group, nextXml);
  const oldAliases = new Map([...baseline.di].map(([id, value]) => [id, value.id]));
  const contextual = contextualizeNativeNamespacePair(authoring, baseline.projection.namespaceContexts,
    normalizeNativeDiIds(authoring, baseline.xml, oldAliases), normalizeNativeDiIds(authoring, nextXml, diAliases), group.namespaceOrigins, baseline.projection.owner.nodeId, false, group.actions);
  const normalized: BpmnNativeBaseline = { ...baseline, xml: contextual.before,
    di: new Map([...baseline.di.values()].map(value => [value.id, value])) };
  const rawIndex = nativeIndex(nativeXml(nextXml)), contextualIndex = nativeIndex(nativeXml(contextual.next));
  const properties = (action: NativeCommandAction) => action.properties && Object.fromEntries(Object.entries(action.properties).map(([name, value]) => {
    if (!["calledElement", "structureRef", "implementationRef"].includes(name) || typeof value !== "string" || action.moddlePath?.length) return [name, value];
    const targetId = action.moddleOwnerId ?? action.elementId ?? "";
    // Validate the raw captured intent first; only its proved QName spelling is normalized for comparison.
    return [name, rawIndex.get(targetId)?.getAttribute(name) === value ? contextualIndex.get(targetId)?.getAttribute(name) ?? value : value];
  }));
  const normalizedGroup: NativeCommandGroup = { ...group, actions: group.actions.map(action => ({ ...action,
    ...(action.properties ? { properties: properties(action) } : {}),
    ...(action.diId ? { diId: diAliases.get(action.diId) ?? oldAliases.get(action.diId) ?? action.diId } : {}),
    ...(action.diIds ? { diIds: action.diIds.map(id => diAliases.get(id) ?? oldAliases.get(id) ?? id) } : {}) })) };
  assertBpmnNativePlaneFrames(authoring, normalized, contextual.next);
  return { commands: mapNormalizedBpmnNativeCommandGroup(authoring, normalized, normalizedGroup,
    contextual.next), diAliases };
}
function mapNormalizedBpmnNativeCommandGroup(authoring: BpmnDocumentAuthoring, baseline: BpmnNativeBaseline, group: NativeCommandGroup, nextXml: string): readonly BpmnDocumentCommand[] {
  const bounded = new Set(["element.updateLabel", "element.updateProperties", "shape.move", "shape.resize", "label.create", "shape.delete", "connection.layout", "connection.updateWaypoints", "connection.move"]);
  if (group.actions.some(action => !bounded.has(action.command) || action.command === "shape.delete" && !action.label
    || action.command === "shape.move" && action.oldParentId !== action.parentId
    || action.command === "element.updateProperties" && fieldNeedsFullDiff(action)
    || action.command === "element.updateLabel" && !["name"].includes(labelProperty(baseline, action))))
    return diffBpmnNativeCommandGroup(authoring, baseline, group, nextXml, (geometryGroup, geometryXml) => mapBoundedBpmnNativeCommandGroup(authoring, baseline, geometryGroup, geometryXml));
  return mapBoundedBpmnNativeCommandGroup(authoring, baseline, group, nextXml);
}
function fieldNeedsFullDiff(action: NativeCommandAction) {
  return (action.propertyNames ?? Object.keys(action.properties ?? {})).some(name => !["name", "default", "di"].includes(name));
}
function labelProperty(baseline: BpmnNativeBaseline, action: NativeCommandAction) {
  const target = nativeIndex(nativeXml(baseline.xml)).get(action.elementId ?? "");
  return target?.localName === "textAnnotation" ? "text" : target?.localName === "group" ? "value" : "name";
}
function mapBoundedBpmnNativeCommandGroup(authoring: BpmnDocumentAuthoring, baseline: BpmnNativeBaseline, group: NativeCommandGroup, nextXml: string): readonly BpmnDocumentCommand[] {
  if (group.trigger !== "execute" || !group.actions.length) nativeRefuse("a complete native execute group is required");
  const ns = authoring.bpmnNamespaces;
  const expected = nativeXml(baseline.xml), next = nativeXml(nextXml);
  const expectedById = nativeIndex(expected), nextById = nativeIndex(next);
  const commands: BpmnDocumentCommand[] = [];
  const seen = new Set<string>();
  const add = (command: BpmnDocumentCommand) => {
    const key = JSON.stringify(command);
    if (!seen.has(key)) { seen.add(key); commands.push(command); }
  };
  const pair = (elementId?: string) => {
    const before = expectedById.get(elementId ?? ""), after = nextById.get(elementId ?? "");
    if (!before || !after || before.namespaceURI !== after.namespaceURI || before.localName !== after.localName)
      nativeRefuse(`modeled owner '${elementId}' was removed or replaced`);
    return [before, after] as const;
  };
  const sameScope = (before: Element, after: Element) => {
    if (before.parentElement?.getAttribute("id") !== after.parentElement?.getAttribute("id")) nativeRefuse("this operation needs an explicit source reparenting policy");
  };
  const attribute = (elementId: string | undefined, name: string, expectedValue: unknown, di = false) => {
    if (!(di ? baseline.projection.scopeDiIds : baseline.projection.scopeElementIds).has(elementId ?? "")) nativeRefuse("property target is outside the selected source scope/DI plane");
    const [before, after] = pair(elementId);
    if (di ? before.namespaceURI !== ns.diagram : before.namespaceURI !== ns.model) nativeRefuse("property owner has an unexpected namespace");
    const value = after.getAttribute(name);
    const intended = expectedValue == null ? null : String(expectedValue);
    if (value !== intended) nativeRefuse(`'${elementId}.${name}' differs from the completed command intent`);
    if (before.getAttribute(name) === value) return;
    if (name === "default" && value !== null) {
      const flow = nextById.get(value);
      if (flow?.namespaceURI !== ns.model || flow.localName !== "sequenceFlow" || flow.getAttribute("sourceRef") !== elementId || flow.parentElement !== after.parentElement)
        nativeRefuse("default must address an outgoing sequence flow in the same standard scope");
    }
    if (value === null) before.removeAttribute(name); else before.setAttribute(name, value);
    add({ kind: "attribute", target: { elementId: elementId! }, name, value });
  };
  const geometry = (diId: string | undefined, kind: "shape" | "label" | "edge") => {
    if (!baseline.projection.scopeDiIds.has(diId ?? "")) nativeRefuse("geometry target is outside the selected source scope/DI plane");
    const normalization = baseline.di.get(diId ?? "");
    if (!normalization) nativeRefuse("geometry has no canonical DI correspondence");
    const [before, after] = pair(diId);
    sameScope(before, after);
    const readBounds = (element: Element): BpmnBounds => {
      const bounds = { x: nativeNumber(element, "x"), y: nativeNumber(element, "y"), width: nativeNumber(element, "width"), height: nativeNumber(element, "height") };
      if (bounds.width <= 0 || bounds.height <= 0) nativeRefuse("modeled bounds must be positive");
      return bounds;
    };
    const untranslate = <T extends BpmnPoint>(value: T, offset?: BpmnPoint): T => ({ ...value, x: value.x - (offset?.x ?? 0), y: value.y - (offset?.y ?? 0) });
    const single = (parent: Element, namespace: string, name: string) => {
      const elements = nativeChildren(parent, namespace, name);
      if (elements.length > 1) nativeRefuse(`ambiguous native ${name}`);
      return elements[0];
    };
    if (kind === "shape") {
      const old = single(before, ns.bounds, "Bounds"), value = single(after, ns.bounds, "Bounds");
      if (!old || !value || !normalization.sourceBounds) nativeRefuse("shape bounds have no source correspondence");
      if (nativeSignature(old) === nativeSignature(value)) return;
      const bounds = readBounds(value);
      for (const [name, number] of Object.entries(bounds)) old.setAttribute(name, String(number));
      add({ kind: "bounds", shapeId: normalization.id, bounds: untranslate(bounds, normalization.boundsOffset) });
    } else if (kind === "edge") {
      const old = nativeChildren(before, ns.coordinates, "waypoint"), values = nativeChildren(after, ns.coordinates, "waypoint");
      if (old.map(nativeSignature).join() === values.map(nativeSignature).join()) return;
      if (values.length < 2) nativeRefuse("an authored route needs at least two points");
      const points = values.map(value => ({ x: nativeNumber(value, "x"), y: nativeNumber(value, "y") }));
      let sourceIndices: (number | null)[] | undefined;
      if (old.length !== values.length) {
        const sourceEdge = authoring.readBpmnDocument(baseline.projection.owner).byId.get(normalization.id)!;
        const rawPoints = nativeChildren(sourceEdge, ns.coordinates, "waypoint");
        if (old.length < 2 || rawPoints.length !== old.length) nativeRefuse("route has no stable source endpoint correspondence");
        const pointType = (element: Element, attribute: Attr) => {
          if (attribute.namespaceURI !== "http://www.w3.org/2001/XMLSchema-instance" || attribute.localName !== "type") return false;
          const parts = attribute.value.trim().split(":");
          return parts.length === 1 ? parts[0] === "Point" && element.lookupNamespaceURI(null) === ns.bounds
            : parts.length === 2 && parts[1] === "Point" && element.lookupNamespaceURI(parts[0]) === ns.bounds;
        };
        const metadata = (point: Element) => [...point.attributes].some(attribute => attribute.namespaceURI !== "http://www.w3.org/2000/xmlns/"
          && (attribute.namespaceURI !== null || !["x", "y"].includes(attribute.localName)) && !pointType(point, attribute))
          || [...point.childNodes].some(node => node.nodeType === 1 || node.nodeType === 7 || node.nodeType === 8 || node.textContent?.trim());
        if (rawPoints.slice(1, -1).some(metadata)) nativeRefuse("route membership changed with an unmapped opaque source bend");
        sourceIndices = values.map((_, i) => i === 0 ? 0 : i === values.length - 1 ? old.length - 1 : null);
        const slots = old.map(point => { const slot = expected.createTextNode(""); point.replaceWith(slot); return slot; });
        values.forEach((value, i) => {
          const source = sourceIndices![i];
          let target = source === null ? undefined : old[source];
          if (!target) {
            if (metadata(value)) nativeRefuse("new native route vertex contains unmapped metadata");
            target = expected.importNode(value, true);
          }
          target.setAttribute("x", String(points[i].x)); target.setAttribute("y", String(points[i].y));
          before.insertBefore(target, slots[Math.min(i, slots.length - 1)]);
        });
        slots.forEach(slot => slot.remove());
      } else points.forEach((point, i) => { old[i].setAttribute("x", String(point.x)); old[i].setAttribute("y", String(point.y)); });
      add({ kind: "waypoints", edgeId: normalization.id, points: points.map(point => untranslate(point, normalization.pointsOffset)), ...(sourceIndices ? { sourceIndices } : {}) });
    } else {
      const oldLabel = single(before, ns.diagram, "BPMNLabel"), nextLabel = single(after, ns.diagram, "BPMNLabel");
      const old = oldLabel && single(oldLabel, ns.bounds, "Bounds"), value = nextLabel && single(nextLabel, ns.bounds, "Bounds");
      if (old && value && nativeSignature(old) === nativeSignature(value) || !old && !value) return;
      if (!value) { old?.remove(); add({ kind: "labelBounds", diId: normalization.id, bounds: null }); return; }
      const bounds = readBounds(value);
      const label = oldLabel ?? expected.createElementNS(ns.diagram, "bpmndi:BPMNLabel");
      const target = old ?? expected.createElementNS(ns.bounds, "dc:Bounds");
      for (const [name, number] of Object.entries(bounds)) target.setAttribute(name, String(number));
      if (!old) label.appendChild(target);
      if (!oldLabel) before.appendChild(label);
      add({ kind: "labelBounds", diId: normalization.id, bounds: untranslate(bounds, normalization.labelOffset ?? normalization.boundsOffset ?? normalization.pointsOffset) });
    }
  };
  for (const action of group.actions) {
    switch (action.command) {
      case "element.updateLabel": {
        if (action.newLabel !== null && typeof action.newLabel !== "string") nativeRefuse("label command has no captured text intent");
        const [target] = pair(action.elementId);
        if (target.localName === "textAnnotation" || target.localName === "group") nativeRefuse("this label needs its own typed source text/category command");
        attribute(action.elementId, "name", action.newLabel);
        if (action.diId) geometry(action.diId, "label");
        break;
      }
      case "element.updateProperties": {
        if (!action.properties) nativeRefuse("property command has no fields");
        for (const [name, value] of Object.entries(action.properties)) {
          if ((name === "name" || name === "default") && (typeof value === "string" || value == null)) attribute(action.elementId, name, value == null ? null : value);
          else if (name === "di" && record(value)) {
            for (const [flag, flagValue] of Object.entries(record(value)!)) {
              if (!["isMarkerVisible", "isExpanded", "isHorizontal"].includes(flag) || typeof flagValue !== "boolean") nativeRefuse("unmapped DI property");
              attribute(action.diId, flag, flagValue, true);
            }
          } else nativeRefuse(`unmapped native property '${name}'`);
        }
        break;
      }
      case "shape.move":
      case "shape.resize":
        if (action.oldParentId !== action.parentId) nativeRefuse("native move changes visual/semantic parent");
        geometry(action.diId, action.label ? "label" : "shape");
        break;
      case "label.create":
        if (!action.label) nativeRefuse("label creation context is unavailable");
        geometry(action.diId, "label");
        break;
      case "shape.delete":
        if (!action.label) nativeRefuse("semantic deletion needs its own source/native ownership transaction");
        geometry(action.diId, "label");
        break;
      case "connection.layout":
      case "connection.updateWaypoints":
      case "connection.move":
        geometry(action.diId, "edge");
        break;
      default: nativeRefuse(`unmapped native command '${action.command}'`);
    }
  }
  if (nativeGeometrySignature(expected.documentElement) !== nativeGeometrySignature(next.documentElement)) nativeRefuse("the complete native command group contains unaccounted XML/DI changes");
  return Object.freeze(commands);
}
