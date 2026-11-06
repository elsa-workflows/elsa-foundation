import { Component, useEffect, useLayoutEffect, useMemo, useRef, useState, useSyncExternalStore, type ReactNode } from "react";
import type { StudioActivityDescriptor, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { ActivityCatalogItem, ActivityNode, ActivityPresentationRecord, DesignMetadataRecord, WorkflowDefinitionState } from "../workflowTypes";
import { getChildSlots, type ChildSlot } from "../workflowAdapter";
import { exportBpmnDocument, requireBpmnDocumentAuthoring } from "../api/bpmnInterchange";
import { createAuthoringCheckpoint } from "../graph-authoring/authoringCheckpoint";
import { CanvasEditorChoice, CanvasEditorHost, useCanvasEditorChoice } from "../graph-authoring/CanvasEditorHost";
import { readBpmnDocument, resolveBpmnDocumentReference, type BpmnScopeAddress } from "./bpmnDocument";
import { projectBpmnDocumentScene } from "./bpmnDocument";
import { prepareLegacyBpmnDocument } from "./bpmnDocumentMigration";
import { bpmnFlowScope, bpmnOwningProcess } from "./bpmnDocumentPolicy";
import { BpmnDocumentCanvasView } from "./BpmnDocumentCanvasView";
import { BpmnDocumentPalette, type BpmnDocumentTool } from "./BpmnDocumentPalette";
import { BpmnDocumentProperties, planBpmnInspectorDraft } from "./BpmnDocumentProperties";
import { BpmnDocumentNativeInspector, type BpmnDocumentNativeInspectorOverrides } from "./BpmnDocumentNativeInspector";
import { bpmnInitialConnectionPoints, planBpmnCreateConnection, planBpmnCreateShape, type BpmnDocumentOperationPlan } from "./bpmnDocumentCreateOperations";
import { planBpmnReplace, prepareBpmnDelete } from "./bpmnDocumentTransformOperations";
import { bpmnDocumentToolboxShapes } from "./bpmnDocumentToolbox";
import { createBpmnDocumentIntegratedSession, type BpmnDocumentIntegratedSession } from "./bpmnDocumentIntegratedSession";
import { planBpmnDocumentInspectionChange, readBpmnDocumentBindingConflicts, readBpmnDocumentInspection } from "./bpmnDocumentInspection";
import { createBpmnDocumentSessionHost } from "./bpmnDocumentSessionHost";
import type { BpmnDocumentNativeSlotContext } from "./bpmnDocumentNativeSlot";
import { planBpmnDocumentSyntaxChange } from "./bpmnDocumentSyntaxChange";
import type { BpmnDocumentCanonicalSource, BpmnDocumentCanvasEditorContext, BpmnDocumentPreparedSource, BpmnDocumentSurfaceHost, BpmnDocumentSurfaceSlots } from "./bpmnDocumentSurfacePort";

const protocol = { id: "elsa.workflows.bpmn-document-canvas", version: 1 } as const;
const isDocument = (owner: ActivityNode) => owner.structure?.kind === "elsa.bpmn.structure" && owner.structure.schemaVersion === "2.0.0";
const equal = (left: unknown, right: unknown) => JSON.stringify(left) === JSON.stringify(right);
function scopeOptions(owner: ActivityNode): { scope: BpmnScopeAddress; label: string }[] {
  const parsed = readBpmnDocument(owner), result: { scope: BpmnScopeAddress; label: string }[] = [];
  for (const diagram of [...parsed.document.documentElement.children].filter(element => element.localName === "BPMNDiagram" && element.namespaceURI === "http://www.omg.org/spec/BPMN/20100524/DI")) {
    const diagramId = diagram.getAttribute("id"); if (!diagramId) continue;
    try {
      const scene = projectBpmnDocumentScene(owner, diagramId), scope = parsed.byId.get(scene.scopeId)!;
      result.push({ scope: { documentOwnerNodeId: owner.nodeId, diagramId, scopeElementId: scene.scopeId }, label: scope.getAttribute("name") || `${scope.localName} ${scene.scopeId}` });
      const roots = [scope];
      if (scope.localName === "collaboration") for (const participant of [...scope.children].filter(element => element.localName === "participant" && element.namespaceURI === scope.namespaceURI)) {
        const process = resolveBpmnDocumentReference(parsed, participant, participant.getAttribute("processRef"));
        if (process) { roots.push(process); result.push({ scope: { documentOwnerNodeId: owner.nodeId, diagramId, scopeElementId: process.id }, label: process.getAttribute("name") || `process ${process.id}` }); }
      }
      for (const element of roots.flatMap(root => [...root.getElementsByTagName("*")]).filter(element => ["subProcess", "transaction", "adHocSubProcess"].includes(element.localName) && element.namespaceURI === scope.namespaceURI)) {
        const id = element.getAttribute("id"); if (id) result.push({ scope: { documentOwnerNodeId: owner.nodeId, diagramId, scopeElementId: id }, label: element.getAttribute("name") || `${element.localName} ${id}` });
      }
    } catch { /* A retained malformed/unassigned plane does not become a fictitious editing scope. */ }
  }
  return result.filter((entry, index) => result.findIndex(other => equal(other.scope, entry.scope)) === index);
}
export interface BpmnDocumentAuthoringSurfaceProps {
  context: StudioEndpointContext;
  resourceKind: BpmnDocumentCanvasEditorContext["resourceKind"];
  documentId: string;
  owner: ActivityNode;
  layout: DesignMetadataRecord[];
  workflowState: WorkflowDefinitionState;
  host?: BpmnDocumentSurfaceHost;
  readOnly: boolean;
  catalog: ActivityCatalogItem[];
  activityDescriptors: StudioActivityDescriptor[];
  presentation?: ActivityPresentationRecord[];
  editors: Parameters<typeof useCanvasEditorChoice>[0];
  preferences?: Parameters<typeof useCanvasEditorChoice>[4];
  onHistory(direction: "undo" | "redo"): void;
  onSelectNative?(nodeId: string | null): void;
  onOpenNativeSlot(ownerNodeId: string, slot: ChildSlot, label: string, context?: BpmnDocumentNativeSlotContext): void | Promise<void>;
  renderNativeInspector(overrides: BpmnDocumentNativeInspectorOverrides): ReactNode;
  children(slots: BpmnDocumentSurfaceSlots): ReactNode;
}
interface Current { props: BpmnDocumentAuthoringSurfaceProps; preparation: BpmnDocumentPreparedSource | null; capable: boolean }
type TrackedSession = { retire(): void; isRetired(): boolean };
function trackSession<T extends { retire(): void }>(host: T, records: Map<string, TrackedSession>, key: string): T {
  let retired = false;
  const retire = () => { if (!retired) { host.retire(); retired = true; } };
  records.set(key, { retire, isRetired: () => retired });
  return { ...host, retire };
}
interface Entry { resourceId: string; coordinator: BpmnDocumentSurfaceHost["coordinator"]; current: Current; attached: boolean; nativeAdmissions: Map<string, { before: { owner: ActivityNode; token: BpmnDocumentCanonicalSource["token"] }; after: { owner: ActivityNode; token: BpmnDocumentCanonicalSource["token"] }; scope: BpmnScopeAddress }>; session: BpmnDocumentIntegratedSession; controllers: Map<string, unknown>; failedRetirements: Map<unknown, { retry(): void; retried: boolean }>; subscriptions: Map<string, () => void>; hosts: Map<string, TrackedSession>; participants: Map<string, Parameters<typeof createBpmnDocumentSessionHost>[0]["participant"]> }
const resources = new WeakMap<object, Map<string, Entry>>();
const parkedViews = new WeakMap<object, Map<string, { scope: BpmnScopeAddress; selection: import("./bpmnDocument").BpmnDocumentSelection | null }>>();
function retireControllers(store: { controllers: Map<string, unknown>; failedRetirements: Map<unknown, { retry(): void; retried: boolean }>; hosts: Map<string, TrackedSession>; subscriptions: Map<string, () => void> }) {
  let clean = true;
  for (const [controller, failed] of store.failedRetirements) {
    try { failed.retry(); store.failedRetirements.delete(controller); } catch { clean = false; }
  }
  for (const [key, controller] of store.controllers) {
    try {
      const retire = (controller as { retire?(): void } | null)?.retire;
      retire?.call(controller);
      store.controllers.delete(key); store.subscriptions.get(key)?.(); store.subscriptions.delete(key);
    } catch {
      // A controller whose registration already retired cannot be reused after partial cleanup.
      if (store.hosts.get(key)?.isRetired()) {
        store.controllers.delete(key); store.subscriptions.get(key)?.(); store.subscriptions.delete(key);
        store.failedRetirements.set(controller, { retry: () => (controller as { retire(): void }).retire(), retried: false });
      }
      clean = false;
    }
  }
  if (clean) for (const host of store.hosts.values()) { try { host.retire(); } catch { clean = false; } }
  return clean;
}
function releaseReadonly(store: Parameters<typeof retireControllers>[0] & { attached: boolean; coordinator: { probe(): boolean } }) {
  if (store.attached || store.coordinator.probe()) return;
  if (!retireControllers(store) && [...store.failedRetirements.values()].some(failure => !failure.retried)) {
    for (const failure of store.failedRetirements.values()) failure.retried = true;
    queueMicrotask(() => releaseReadonly(store));
  }
}
function releaseEntry(entry: Entry) {
  if (entry.attached || entry.session.hasWork() || entry.coordinator.probe() || nativeBusy(entry)) return;
  let view: ReturnType<Entry["session"]["read"]> | null = null;
  try { view = entry.session.read(); } catch { /* Removed owners still release clean controllers. */ }
  if (!retireControllers(entry)) {
    if ([...entry.failedRetirements.values()].some(failure => !failure.retried)) {
      for (const failure of entry.failedRetirements.values()) failure.retried = true;
      queueMicrotask(() => releaseEntry(entry));
    }
    return;
  }
  try { entry.session.retire(); } catch { return; }
  if (view) {
    let views = parkedViews.get(entry.coordinator); if (!views) { views = new Map(); parkedViews.set(entry.coordinator, views); }
    views.set(entry.resourceId, { scope: view.snapshot.scope, selection: view.selection });
  }
  const entries = resources.get(entry.coordinator); if (entries?.get(entry.resourceId) === entry) entries.delete(entry.resourceId);
}
const emptySubscribe = () => () => undefined;
const zero = () => 0;
function mapped(entry: Pick<Entry, "current">) {
  const canonical = entry.current.props.host!.readCanonical(), preparation = entry.current.preparation;
  return { token: canonical.token, owner: !isDocument(canonical.owner) && preparation && equal(canonical.owner, preparation.canonicalBaseOwner) && equal(canonical.layout, preparation.canonicalBaseLayout) ? preparation.preparedOwner : canonical.owner };
}
function admit(entry: Entry, expected: { owner: ActivityNode; token: BpmnDocumentCanonicalSource["token"] }, owner: ActivityNode, plan: ReturnType<typeof planBpmnDocumentSyntaxChange>, nextScope?: BpmnScopeAddress) {
  const host = entry.current.props.host!, canonical = host.readCanonical();
  const live = mapped(entry);
  if (!entry.current.capable || !host.canEdit() || !equal(live.owner, expected.owner) || !equal(live.token, expected.token)) return false;
  if (equal(owner, expected.owner)) return true; // Import/no-op native groups never migrate a v1 authority.
  return host.admit(canonical, owner, plan, { preparation: isDocument(canonical.owner) ? null : entry.current.preparation, nextScope });
}
function nativeBusy(entry: Entry) {
  for (const participant of entry.participants.values()) {
    try { const facts = participant.readWork(); if (facts.pending || facts.sequence !== facts.acceptedSequence) return true; } catch { return true; }
  }
  return false;
}
function acquire(current: Current, scope: BpmnScopeAddress): Entry {
  const host = current.props.host!; let entries = resources.get(host.coordinator);
  if (!entries) { entries = new Map(); resources.set(host.coordinator, entries); }
  let entry = entries.get(host.id);
  if (!entry) {
    const state = { resourceId: host.id, coordinator: host.coordinator, current, attached: true, controllers: new Map<string, unknown>(), failedRetirements: new Map(), subscriptions: new Map(), hosts: new Map(), participants: new Map(), nativeAdmissions: new Map() } as Entry;
    const parked = parkedViews.get(host.coordinator)?.get(host.id);
    if (parked) { try { projectBpmnDocumentScene(mapped(state).owner, parked.scope.diagramId); if (readBpmnDocument(mapped(state).owner).byId.has(parked.scope.scopeElementId)) scope = parked.scope; } catch { /* A removed historical plane falls back to the actual source scope. */ } }
    state.session = createBpmnDocumentIntegratedSession({ id: `integrated:${host.id}`, scope, coordinator: host.coordinator,
      readLive: () => mapped(state), canEdit: () => state.current.props.host!.canEdit(),
      canMutate: () => state.attached && !state.current.props.readOnly && state.current.capable && !nativeBusy(state),
      canCommit: () => state.current.capable && !nativeBusy(state),
      get catalog() { return new Map(state.current.props.catalog.map(item => [item.activityVersionId, item])); },
      admit: (expected, owner, plan, nextScope) => admit(state, expected, owner, plan, nextScope),
      planProperty: planBpmnInspectorDraft,
      canReconcileNative(previous, live) {
        let source = { owner: previous.owner, token: previous.token };
        const visited = new Set<string>();
        while (!equal(source, { owner: live.owner, token: live.token })) {
          const key = JSON.stringify(source.token), fact = state.nativeAdmissions.get(key);
          if (visited.has(key) || !fact || !equal(fact.before, source) || !equal(fact.scope, previous.scope)) return false;
          visited.add(key); source = fact.after;
        }
        return true;
      },
      planNative: draft => planBpmnDocumentInspectionChange(draft.source.owner, draft.inspection, draft.next, new Map(state.current.props.catalog.map(item => [item.activityVersionId, item]))),
      onWorkChanged: () => state.current.props.host!.notifyWorkChanged() });
    if (parked?.selection) { const parsed = readBpmnDocument(mapped(state).owner); if (parsed.byId.has(parked.selection.elementId) && (!parked.selection.diId || parsed.byId.has(parked.selection.diId))) state.session.select(parked.selection); }
    entry = state; entries.set(host.id, entry);
  }
  entry.current = current; entry.attached = true; entry.session.reattach(); return entry;
}
class CanvasBoundary extends Component<{ children: ReactNode; onRetry(): void }, { failed: boolean }> {
  state = { failed: false };
  static getDerivedStateFromError() { return { failed: true }; }
  render() { return this.state.failed ? <div role="alert">The BPMN canvas could not render. Pending edits remain retained.<button type="button" onClick={() => { this.props.onRetry(); this.setState({ failed: false }); }}>Retry BPMN canvas</button></div> : this.props.children; }
}

/** Shared actual authoring surface. The resource host remains the only source/history/receipt authority. */
export function BpmnDocumentAuthoringSurface(props: BpmnDocumentAuthoringSurfaceProps) {
  const [capability, setCapability] = useState<{ ready: boolean; message: string | null }>({ ready: false, message: null });
  const [prepared, setPrepared] = useState<BpmnDocumentPreparedSource | null>(null), [prepareError, setPrepareError] = useState<string | null>(null);
  const [entry, setEntry] = useState<Entry | null>(null), [tool, setTool] = useState<BpmnDocumentTool | null>(null), [connectionSource, setConnectionSource] = useState<string | null>(null);
  const [bindingRole, setBindingRole] = useState<"child" | "listener">("child");
  const [notice, setNotice] = useState<string | null>(null), [nativeState, setNativeState] = useState<{ status: string; pending?: boolean }>({ status: "ready" });
  const [readonlyScope, setReadonlyScope] = useState<BpmnScopeAddress | null>(null), [readonlySelection, setReadonlySelection] = useState<import("./bpmnDocument").BpmnDocumentSelection | null>(null);
  const readonlyLive = useRef<import("./bpmnDocumentSessionHost").BpmnDocumentSessionSource>({ owner: props.owner, token: { authorityId: `readonly:${props.documentId}`, resourceId: props.documentId, scopeId: props.owner.nodeId, revision: 0 } });
  const readonlyResource = useMemo(() => ({ key: JSON.stringify([props.documentId, props.owner.nodeId]), attached: true, controllers: new Map<string, unknown>(), failedRetirements: new Map<unknown, { retry(): void; retried: boolean }>(), subscriptions: new Map<string, () => void>(), hosts: new Map<string, TrackedSession>(), coordinator: createAuthoringCheckpoint({ readLive: () => {
    const live = readonlyLive.current; if (!live) throw new Error("The read-only BPMN source is unavailable.");
    return { token: live.token, snapshot: live.owner, lineage: [] };
  } }) }), [props.documentId, props.owner.nodeId]);
  const authorityKey = props.host?.authorityKey ?? props.documentId;
  useEffect(() => {
    const request = new AbortController(); setCapability({ ready: false, message: null });
    void requireBpmnDocumentAuthoring(props.context, request.signal).then(() => { if (!request.signal.aborted) setCapability({ ready: true, message: null }); }, error => { if (!request.signal.aborted) setCapability({ ready: false, message: error instanceof Error ? error.message : String(error) }); });
    return () => request.abort();
  }, [authorityKey, props.context]);
  useEffect(() => {
    if (isDocument(props.owner)) { setPrepared(null); setPrepareError(null); return; }
    const request = new AbortController(), canonicalBaseOwner = structuredClone(props.owner), canonicalBaseLayout = structuredClone(props.layout);
    setPrepareError(null);
    void prepareLegacyBpmnDocument(canonicalBaseOwner, canonicalBaseLayout, async (owner, processId) => exportBpmnDocument(props.context, owner, processId, request.signal))
      .then(preparedOwner => { if (!request.signal.aborted) setPrepared({ canonicalBaseOwner, canonicalBaseLayout, preparedOwner }); }, error => { if (!request.signal.aborted) setPrepareError(error instanceof Error ? error.message : String(error)); });
    return () => request.abort();
  }, [authorityKey, props.context, props.layout, props.owner]);
  const preparation = prepared && equal(prepared.canonicalBaseOwner, props.owner) && equal(prepared.canonicalBaseLayout, props.layout) ? prepared : null;
  const owner = isDocument(props.owner) ? props.owner : preparation?.preparedOwner ?? null;
  const scopes = useMemo(() => owner ? scopeOptions(owner) : [], [owner]);
  const defaultScope = scopes[0]?.scope;
  const committedView = useRef({ props, preparation, capable: capability.ready, defaultScope });
  useLayoutEffect(() => { committedView.current = { props, preparation, capable: capability.ready, defaultScope }; });
  const coordinator = props.host?.coordinator, resourceId = props.host?.id, hasScope = !!defaultScope;
  useLayoutEffect(() => {
    const current = committedView.current;
    if (!coordinator || !resourceId || !hasScope || !current.defaultScope) return;
    const retained = acquire(current, current.defaultScope); setEntry(retained);
    return () => { retained.attached = false; retained.session.detach(); queueMicrotask(() => releaseEntry(retained)); };
  }, [coordinator, resourceId, hasScope]);
  useLayoutEffect(() => { if (entry && entry.coordinator === coordinator && entry.resourceId === resourceId) entry.current = committedView.current; });
  useSyncExternalStore(entry?.session.subscribe ?? emptySubscribe, entry?.session.getVersion ?? zero, zero);
  const state = entry?.session.read();
  const requestedScope = state?.snapshot.scope ?? readonlyScope ?? defaultScope;
  const scope = !state?.pending && !state?.geometry && !state?.property && !state?.nativeDraft && !scopes.some(option => equal(option.scope, requestedScope)) ? defaultScope : requestedScope;
  // Parent committed refs update later in the same layout pass. A second render reads that accepted
  // source before paint, instead of keeping a snapshot from the previous parent commit.
  useLayoutEffect(() => {
    let active = true;
    entry?.session.refresh();
    // Parent layout effects publish committed refs after child effects. Sample again after that pass.
    queueMicrotask(() => { if (active && entry) { if (owner && equal(mapped(entry).owner, owner)) entry.session.reconcile(scopes.map(option => option.scope)); entry.session.refresh(); } });
    return () => { active = false; };
  }, [entry, props.owner, props.layout, preparation, capability.ready, state?.generation, owner, scopes]);
  const choice = useCanvasEditorChoice(props.editors, props.resourceKind, owner?.structure, `${props.documentId}:${props.owner.nodeId}`, props.preferences, protocol);
  const native = !!choice.active;
  const session = entry?.session;
  const canonicalReady = !entry || !!owner && equal(state?.snapshot.owner, owner) && equal(state?.snapshot.scope, scope);
  const snapshot = state?.snapshot && isDocument(state.snapshot.owner) && owner && scope ? { ...state.snapshot, owner: state?.property || state?.geometry || state?.nativeDraft || state?.pending ? state.snapshot.owner : owner, scope } : owner && scope ? { owner, scope, token: props.host?.readCanonical().token ?? { authorityId: `readonly:${props.documentId}`, resourceId: props.documentId, scopeId: props.owner.nodeId, revision: 0 } } : null;
  useLayoutEffect(() => { if (snapshot) readonlyLive.current = snapshot; });
  useLayoutEffect(() => {
    readonlyResource.attached = true;
    return () => { readonlyResource.attached = false; queueMicrotask(() => { releaseReadonly(readonlyResource); }); };
  }, [readonlyResource]);
  const blocked = props.readOnly || !canonicalReady || !capability.ready || !session || !!state?.fenced || !!state?.pending;
  const sharedBlocked = blocked || !!nativeState.pending || !!entry && nativeBusy(entry) || native && nativeState.status !== "ready" || !!state?.geometry || !!state?.nativeDraft;
  const safely = (action: () => void) => { try { setNotice(null); action(); } catch (error) { setNotice(error instanceof Error ? error.message : String(error)); } };
  const navigate = async (next: BpmnScopeAddress) => {
    if (!props.host || !session) { setReadonlyScope(next); return; }
    try { await props.host.transitionScope(() => { session.activateScope(next); return undefined; }); }
    catch (error) { setNotice(error instanceof Error ? error.message : String(error)); }
  };
  const resolveScope = (elementId: string) => scopes.find(option => option.scope.scopeElementId === elementId)?.scope ?? (scope && snapshot && readBpmnDocument(snapshot.owner).byId.has(elementId) ? { ...scope, scopeElementId: elementId } : null);
  if (!snapshot || !scope) return props.children({ palette: null, canvas: <p role={prepareError ? "alert" : "status"}>{prepareError ?? "Preparing the BPMN authoring view…"}</p>, inspector: null, controlsBlocked: true });
  const parsed = readBpmnDocument(snapshot.owner), rawSelection = state?.selection ?? readonlySelection;
  const selected = rawSelection && parsed.byId.has(rawSelection.elementId) && (!rawSelection.diId || parsed.byId.has(rawSelection.diId)) ? rawSelection : null;
  const inspection = readBpmnDocumentInspection(snapshot.owner, scope, selected, bindingRole);
  const bindingConflicts = readBpmnDocumentBindingConflicts(snapshot.owner);
  const visibleInspection = state?.nativeDraft ? { ...state.nativeDraft.inspection, activity: state.nativeDraft.next } : inspection;
  const onSelect = (selection: typeof selected) => safely(() => { if (session) session.select(selection); else setReadonlySelection(selection); setBindingRole("child"); const inspected = readBpmnDocumentInspection(snapshot.owner, scope, selection); props.onSelectNative?.(inspected.activity?.nodeId ?? null); });
  const apply = (plan: BpmnDocumentOperationPlan, label: string) => safely(() => session!.applyPlan(plan, snapshot, label));
  const place = (point: { x: number; y: number }, hitElementId?: string) => {
    if (!tool || blocked) return;
    safely(() => {
      if (tool.kind === "connection") {
        if (!hitElementId) throw new Error("Choose an actual BPMN source and target shape.");
        if (!connectionSource) { setConnectionSource(hitElementId); return; }
        const scene = projectBpmnDocumentScene(snapshot.owner, scope.diagramId), from = scene.shapes.find(shape => shape.elementId === connectionSource)!, to = scene.shapes.find(shape => shape.elementId === hitElementId)!;
        if (!from || !to) throw new Error("The connection endpoints are not represented in this diagram.");
        apply(planBpmnCreateConnection(snapshot.owner, scope, { kind: tool.connectionKind, sourceId: connectionSource, targetId: hitElementId, points: bpmnInitialConnectionPoints(from.bounds, to.bounds, connectionSource === hitElementId) }), "Create BPMN connection"); setConnectionSource(null);
      } else {
        const hit = hitElementId ? parsed.byId.get(hitElementId) : undefined;
        const parent = scope.scopeElementId === parsed.payload.processId || ["subProcess", "transaction", "adHocSubProcess"].includes(parsed.byId.get(scope.scopeElementId)!.localName) ? scope.scopeElementId
          : hit?.localName === "participant" ? resolveBpmnDocumentReference(parsed, hit, hit.getAttribute("processRef"))?.id : hit ? bpmnFlowScope(hit)?.id ?? bpmnOwningProcess(hit)?.id : undefined;
        apply(planBpmnCreateShape(snapshot.owner, scope, { toolId: tool.entry.id, position: point, parentElementId: tool.entry.type === "lane" ? hitElementId : parent, attachedToElementId: tool.entry.type === "boundaryEvent" ? hitElementId : undefined }), "Create BPMN construct");
      }
      setTool(null);
    });
  };
  const nativeContext: BpmnDocumentCanvasEditorContext = {
    protocol, resourceKind: props.resourceKind, documentId: props.documentId, scope, selection: selected, owner: snapshot.owner, preparation,
    ready: !!owner && canonicalReady, readOnly: blocked || !!state?.property || !!state?.nativeDraft,
    readLive: () => entry ? mapped(entry) : snapshot,
    createSessionHost(id, participant) {
      if (!entry) {
        const key = JSON.stringify([id, scope]);
        return trackSession(createBpmnDocumentSessionHost({ id: `readonly:${key}`, coordinator: readonlyResource.coordinator,
          readLive: () => readonlyLive.current, canEdit: () => false, admit: () => false, participant }), readonlyResource.hosts, key);
      }
      const fixedScope = { ...scope }, prefix = JSON.stringify([id, fixedScope]);
      const registered = createBpmnDocumentSessionHost({ id: `native:${props.host!.id}:${prefix}`, coordinator: props.host!.coordinator,
        checkpointOrder: -100,
        readLive: () => mapped(entry), canEdit: () => entry.current.props.host!.canEdit(),
        plan: (owner, commands) => planBpmnDocumentSyntaxChange(owner, commands, new Map(entry.current.props.catalog.map(item => [item.activityVersionId, item]))),
        admit: (expected, owner, plan) => {
          if (!equal(entry.session.read().snapshot.scope, fixedScope) || !admit(entry, expected, owner, plan)) return false;
          const after = mapped(entry);
          if (!equal(expected, after)) entry.nativeAdmissions.set(JSON.stringify(expected.token), { before: structuredClone(expected), after: structuredClone(after), scope: fixedScope });
          return true;
        },
        participant, onWorkChanged: () => entry.current.props.host!.notifyWorkChanged() });
      entry.participants.set(prefix, participant);
      return trackSession(registered, entry.hosts, prefix);
    },
    canMutate: () => !!entry && entry.attached && !entry.current.props.readOnly && entry.current.capable && !entry.session.hasWork() && !entry.session.read().fenced && equal(entry.session.read().snapshot.scope, scope),
    retainCanvasController<T>(id: string, create: () => T): T {
      const controllers = entry?.controllers ?? readonlyResource.controllers;
      const key = JSON.stringify([id, scope]);
      if (!controllers.has(key)) {
        const controller = create(); controllers.set(key, controller);
        const subscribe = (controller as { subscribe?(listener: () => void): () => void } | null)?.subscribe;
        if (subscribe) (entry?.subscriptions ?? readonlyResource.subscriptions).set(key, subscribe.call(controller, () => {
          if (entry && !entry.attached) queueMicrotask(() => releaseEntry(entry));
          else if (!entry && !readonlyResource.attached) queueMicrotask(() => { releaseReadonly(readonlyResource); });
        }));
      }
      return controllers.get(key) as T;
    },
    onSelect, onNavigateScope: navigate, resolveStandardScope: resolveScope,
    onUndo: () => props.onHistory("undo"), onRedo: () => props.onHistory("redo"), onStateChange: setNativeState
  };
  const palette = native ? null : <BpmnDocumentPalette readOnly={blocked || !!state?.property || !!state?.nativeDraft} activeTool={tool?.kind === "shape" ? tool.entry.id : tool?.connectionKind}
    onChoose={next => { setTool(next); setConnectionSource(null); }} />;
  const inspector = <BpmnDocumentProperties snapshot={snapshot} selection={selected ?? { elementId: scope.scopeElementId }} readOnly={sharedBlocked} pending={!!state?.pending || !!state?.fenced}
    message={state?.failure} draft={state?.property ?? null} onDraftChange={draft => safely(() => session?.setProperty(draft))} onApply={() => safely(() => session?.applyProperty())}
    renderNativeConfiguration={() => visibleInspection.activity ? <>
      {selected && parsed.payload.bindings.some(binding => binding.elementId === selected.elementId && binding.childNodeId && binding.listenerNodeId) ? <label>Elsa configuration<select aria-label="Elsa configuration" value={bindingRole} disabled={sharedBlocked || !!state?.property} onChange={event => setBindingRole(event.target.value as "child" | "listener")}><option value="child">Activity</option><option value="listener">Listener</option></select></label> : null}
      <BpmnDocumentNativeInspector context={props.context} owner={snapshot.owner} inspection={{ ...visibleInspection, activity: visibleInspection.activity }} workflowState={props.workflowState}
      catalog={props.catalog} activityDescriptors={props.activityDescriptors} presentation={props.presentation} readOnly={sharedBlocked || !!state?.property}
      onChange={next => session?.changeNative({ source: snapshot, inspection, next }) ?? false}
      onOpenSlot={(ownerId, slot, label) => {
        const header = parsed.payload.scopeHeaders?.find(header => header.nodeId === ownerId);
        if (ownerId === snapshot.owner.nodeId || header) return navigate(resolveScope(header?.scopeElementId ?? parsed.payload.processId)!);
        return props.onOpenNativeSlot(ownerId, slot, label, { documentOwnerNodeId: snapshot.owner.nodeId, processId: visibleInspection.processId, nativeOwnerNodeId: ownerId });
      }} render={props.renderNativeInspector} /></> : null} />;
  const canvas = <div className="wf-bpmn-document-surface" data-bpmn-authoring-surface>
    <CanvasEditorChoice choice={choice} onChoose={async id => { if (props.host) await props.host.transitionScope(() => { choice.choose(id); return undefined; }); else choice.choose(id); }} />
    <div className="wf-bpmn-document-scope-tools"><label>BPMN scope<select aria-label="BPMN scope" value={JSON.stringify(scope)} onChange={event => { void navigate(JSON.parse(event.target.value) as BpmnScopeAddress); }}>{scopes.map((option, index) => <option key={index} value={JSON.stringify(option.scope)}>{option.label}</option>)}</select></label>
      {!native && selected && <><button type="button" disabled={sharedBlocked || !!state?.property} onClick={() => safely(() => { const result = prepareBpmnDelete(snapshot.owner, scope, [selected.elementId]); apply(result.kind === "ready" ? result.plan : { transaction: { commands: result.commands }, selection: null }, "Delete BPMN construct"); })}>Delete BPMN construct</button>
        <label>Replace BPMN construct<select aria-label="Replace BPMN construct" value="" disabled={sharedBlocked || !!state?.property} onChange={event => safely(() => session!.applyPlan(planBpmnReplace(snapshot.owner, scope, selected, event.target.value, true), snapshot, "Replace BPMN construct"))}><option value="">Choose notation…</option>{bpmnDocumentToolboxShapes.filter(entry => entry.type === parsed.byId.get(selected.elementId)?.localName || entry.group === "Tasks" && (parsed.byId.get(selected.elementId)?.localName.toLowerCase().endsWith("task") || parsed.byId.get(selected.elementId)?.localName === "callActivity") || entry.group === "Gateways" && parsed.byId.get(selected.elementId)?.localName.endsWith("Gateway")).map(entry => <option key={entry.id} value={entry.id}>{entry.label}</option>)}</select></label></>}
    </div>
    {capability.message || prepareError || notice ? <p role="alert">{notice ?? prepareError ?? capability.message}</p> : !capability.ready ? <p role="status">Checking BPMN authoring support…</p> : null}
    {entry && entry.failedRetirements.size > 0 ? <p role="status">A previous BPMN canvas could not finish releasing its resources. Cleanup remains retained.<button type="button" onClick={() => safely(() => {
      for (const [controller, failure] of entry.failedRetirements) { failure.retry(); entry.failedRetirements.delete(controller); }
      session?.refresh();
    })}>Retry canvas cleanup</button></p> : null}
    {bindingConflicts.map(conflict => <p key={conflict.elementId} role="alert">BPMN element {conflict.elementId} contains standard body syntax and a bound Elsa body. Both are preserved; resolve this execution/binding conflict before publishing.</p>)}
    {tool && !native ? <p role="status">{tool.kind === "connection" ? connectionSource ? "Choose the BPMN target." : "Choose the BPMN source." : `Place ${tool.entry.label}.`} <button type="button" onClick={() => { setTool(null); setConnectionSource(null); }}>Cancel placement</button></p> : null}
    {state?.failure ? <div role="alert">{state.failure}<button type="button" onClick={() => safely(() => session?.retry())}>Retry retained BPMN edit</button><button type="button" onClick={() => safely(() => session?.discard())}>Discard retained BPMN edit</button></div> : null}
    <CanvasBoundary onRetry={() => undefined}>{native && choice.active ? <CanvasEditorHost editor={choice.active} context={nativeContext} onFallback={() => { if (props.host) void props.host.transitionScope(() => { choice.choose("integrated"); return undefined; }).catch(error => setNotice(error instanceof Error ? error.message : String(error))); }} /> :
      <BpmnDocumentCanvasView snapshot={snapshot} selection={selected} readOnly={blocked || !!state?.property || !!state?.nativeDraft} placing={!!tool} onPlace={place} onSelect={onSelect}
        onTransaction={request => session ? session.onTransaction(request) : { kind: "refused", reason: "This source is read-only.", current: snapshot }} onHistory={props.onHistory}
        editing={session && state ? { generation: state.generation, fenced: state.fenced,
          begin: source => session.beginGeometry(source), preview: (source, commands, label) => session.previewGeometry({ source, plan: { transaction: { commands }, selection: selected }, label }) } : undefined}
        resolveNativePresentation={(_id, binding) => { const activity = parsed.payload.activities.find(item => item.nodeId === (binding.childNodeId ?? binding.listenerNodeId)); const item = activity && props.catalog.find(item => item.activityVersionId === activity.activityVersionId); return activity ? { activity: { nodeId: activity.nodeId, activityVersionId: activity.activityVersionId, label: item?.displayName ?? activity.nodeId, icon: item?.icon ?? "activity" }, childSlots: getChildSlots(activity, new Map(props.catalog.map(item => [item.activityVersionId, item]))) } : null; }}
        resolveStandardScope={elementId => resolveScope(elementId)} onNavigateStandard={navigate} onOpenNativeSlot={(id, slot) => {
          const binding = parsed.payload.bindings.find(binding => binding.childNodeId === id || binding.listenerNodeId === id), element = binding && parsed.byId.get(binding.elementId);
          const processId = element && bpmnOwningProcess(element)?.getAttribute("id");
          return props.onOpenNativeSlot(id, slot, slot.label, processId ? { documentOwnerNodeId: snapshot.owner.nodeId, processId, nativeOwnerNodeId: id } : undefined);
        }} />}</CanvasBoundary>
  </div>;
  return props.children({ palette, canvas, inspector, controlsBlocked: sharedBlocked });
}
