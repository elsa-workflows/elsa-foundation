import type { ActivityNode } from "../workflowTypes";
import type { ActivityCatalogLookup } from "../workflowAdapter";
import type { AuthoringCheckpointCoordinator, AuthoringCheckpointToken } from "../graph-authoring/authoringCheckpoint";
import { readBpmnDocument, type BpmnDocumentSelection, type BpmnScopeAddress } from "./bpmnDocument";
import { projectBpmnDocumentCanvas } from "./bpmnDocumentCanvas";
import type { BpmnDocumentCanvasAcknowledgement, BpmnDocumentCanvasRequest, BpmnDocumentCanvasSnapshot } from "./bpmnDocumentCanvasPort";
import type { BpmnDocumentOperationPlan } from "./bpmnDocumentCreateOperations";
import { createBpmnDocumentSessionHost, type BpmnDocumentSessionCommit, type BpmnDocumentSessionSource } from "./bpmnDocumentSessionHost";
import { planBpmnDocumentSyntaxChange, type BpmnDocumentSyntaxPlan } from "./bpmnDocumentSyntaxChange";
import type { BpmnDocumentPropertyDraft } from "./BpmnDocumentProperties";
import type { BpmnDocumentInspection } from "./bpmnDocumentInspection";

const sameToken = (left: AuthoringCheckpointToken, right: AuthoringCheckpointToken) => left.authorityId === right.authorityId && left.resourceId === right.resourceId && left.scopeId === right.scopeId && left.revision === right.revision;
const sameScope = (left: BpmnScopeAddress, right: BpmnScopeAddress) => left.documentOwnerNodeId === right.documentOwnerNodeId && left.diagramId === right.diagramId && left.scopeElementId === right.scopeElementId;
const fingerprint = (source: BpmnDocumentCanvasSnapshot, plan: BpmnDocumentOperationPlan) => JSON.stringify([source.token, source.scope, source.owner, plan.transaction]);
export interface BpmnIntegratedGeometry { source: BpmnDocumentCanvasSnapshot; plan: BpmnDocumentOperationPlan; label: string }
export interface BpmnIntegratedNativeDraft { source: BpmnDocumentCanvasSnapshot; inspection: BpmnDocumentInspection; next: ActivityNode }
interface PendingGroup extends BpmnIntegratedGeometry { request: BpmnDocumentSessionCommit; fingerprint: string }

/** Retained by the resource's checkpoint registration; its state does not belong to a fallible canvas view. */
export function createBpmnDocumentIntegratedSession(options: {
  id: string;
  scope: BpmnScopeAddress;
  coordinator: Pick<AuthoringCheckpointCoordinator<unknown>, "register">;
  readLive(): BpmnDocumentSessionSource;
  canEdit(): boolean;
  /** Visible-view permission is separate from parent authority for clean hidden checkpoints. */
  canMutate?(): boolean;
  /** Backend format capability can block writes while clean receipt refresh remains available. */
  canCommit?(): boolean;
  catalog?: ActivityCatalogLookup;
  admit(expected: BpmnDocumentSessionSource, candidate: ActivityNode, plan: BpmnDocumentSyntaxPlan, nextScope?: BpmnScopeAddress): boolean;
  planProperty(draft: BpmnDocumentPropertyDraft): BpmnDocumentOperationPlan;
  canReconcileNative?(previous: BpmnDocumentCanvasSnapshot, live: BpmnDocumentCanvasSnapshot): boolean;
  planNative?(draft: BpmnIntegratedNativeDraft): { owner: ActivityNode; plan: BpmnDocumentSyntaxPlan };
  onWorkChanged?(): void;
}) {
  let scope = options.scope, generation = 1, sequence = 0, acceptedSequence = 0, version = 0, fences = 0;
  let selection: BpmnDocumentSelection | null = null, property: BpmnDocumentPropertyDraft | null = null;
  let geometry: BpmnIntegratedGeometry | null = null, pending: PendingGroup | null = null, failure: string | null = null;
  let nativeDraft: BpmnIntegratedNativeDraft | null = null;
  let lastCompleted: string | null = null;
  const listeners = new Set<() => void>();
  const notify = () => { version++; for (const listener of listeners) { try { listener(); } catch { /* A view failure cannot revoke retained work. */ } } try { options.onWorkChanged?.(); } catch { /* Observation cannot revoke canonical admission. */ } };
  const snapshot = (): BpmnDocumentCanvasSnapshot => ({ ...options.readLive(), scope });
  const work = () => ({ sequence, acceptedSequence, pending: !!geometry || !!property || !!pending || !!nativeDraft });
  const assertAvailable = (visible = true) => { if (!options.canEdit() || visible && options.canMutate && !options.canMutate()) throw new Error("This BPMN source is read-only. Pending edits are retained."); };
  const assertIdle = () => { assertAvailable(); if (fences || pending) throw new Error("Finish the retained BPMN edit before starting another operation."); };
  const assertBaseline = (expected: BpmnDocumentCanvasSnapshot) => {
    const live = snapshot();
    if (!sameScope(expected.scope, scope) || !sameToken(expected.token, live.token) || JSON.stringify(expected.owner) !== JSON.stringify(live.owner)) throw new Error("The BPMN edit belongs to an earlier source or scope. It has been retained.");
  };
  const host = createBpmnDocumentSessionHost({ id: options.id, coordinator: options.coordinator, readLive: options.readLive,
    canEdit: options.canEdit, onWorkChanged: options.onWorkChanged,
    plan: (owner, commands) => planBpmnDocumentSyntaxChange(owner, commands, options.catalog),
    admit(expected, candidate, plan) {
      const nextScope = pending?.plan.nextScope;
      if (nextScope) projectBpmnDocumentCanvas(candidate, nextScope); // No fallible scope preparation after canonical admission.
      if (!options.admit(expected, candidate, plan, nextScope)) return false;
      if (nextScope) { scope = nextScope; generation++; }
      return true;
    },
    participant: { readWork: work, async checkpoint() {
      fences++; notify();
      let released = false;
      const release = () => { if (!released) { released = true; fences--; notify(); } };
      try {
        completeEditing(true);
        return { acknowledgement: { ...options.readLive(), acceptedSequence }, release };
      } catch (error) { release(); throw error; }
    } }
  });

  function acceptPending() {
    if (!pending) return;
    const group = pending;
    // A historical fact proves whether admission occurred, even when a later canonical edit changed its receipt token.
    const fact = host.readAdmission(group.request);
    if (!fact && options.canCommit && !options.canCommit()) throw new Error("The backend has not confirmed this BPMN authoring format. Pending edits are retained.");
    const acknowledgement = fact?.acknowledgement ?? host.commit(group.request);
    acceptedSequence = acknowledgement.acceptedSequence;
    lastCompleted = group.fingerprint;
    selection = group.plan.selection;
    pending = null; geometry = null; property = null; failure = null; generation++; notify();
  }
  function stage(source: BpmnDocumentCanvasSnapshot, plan: BpmnDocumentOperationPlan, label: string, checkpoint = false) {
    assertAvailable(!checkpoint); assertBaseline(source);
    if (options.canCommit && !options.canCommit()) throw new Error("The backend has not confirmed this BPMN authoring format. Pending edits are retained.");
    if (pending) throw new Error("Another BPMN command is still retained.");
    if (plan.transaction.configuration) throw new Error("Native configuration changes use the resource's Elsa inspector and ownership port.");
    if (!plan.transaction.commands?.length) { geometry = null; property = null; failure = null; generation++; notify(); return; }
    pending = { source, plan, label, fingerprint: fingerprint(source, plan), request: { token: source.token,
      sourceXml: readBpmnDocument(source.owner).payload.documentXml, importGeneration: generation, sequence: ++sequence, commands: plan.transaction.commands } };
    notify();
    try { acceptPending(); }
    catch (error) { failure = error instanceof Error ? error.message : String(error); notify(); throw error; }
  }
  function completeEditing(checkpoint = false) {
    assertAvailable(!checkpoint);
    if (pending) acceptPending();
    if (geometry) {
      const active = geometry;
      stage(active.source, active.plan, active.label, checkpoint);
    }
    if (property) {
      const draft = property;
      stage(draft.source, { ...options.planProperty(draft), selection: draft.selection }, "Edit BPMN property", checkpoint);
    }
    if (nativeDraft) completeNative(checkpoint);
  }
  function completeNative(checkpoint = false) {
    if (!nativeDraft) return;
    assertAvailable(!checkpoint);
    const live = snapshot(), previous = nativeDraft.source;
    if (!sameScope(previous.scope, live.scope) || previous.token.authorityId !== live.token.authorityId || previous.token.resourceId !== live.token.resourceId || previous.token.scopeId !== live.token.scopeId)
      throw new Error("The Elsa edit belongs to another authority or scope. Its values are retained.");
    if ((!sameToken(previous.token, live.token) || JSON.stringify(previous.owner) !== JSON.stringify(live.owner)) && !options.canReconcileNative?.(previous, live))
      throw new Error("The Elsa edit belongs to an earlier source revision. Its values are retained.");
    if (options.canCommit && !options.canCommit()) throw new Error("The backend has not confirmed this BPMN authoring format. Pending edits are retained.");
    if (!options.planNative) throw new Error("The resource has no native configuration port.");
    // The BPMN-owned planner checks the exact invocation fields and XML ownership chain. This is
    // limited native configuration reconciliation after an earlier native XML checkpoint, not an XML rebase.
    const candidate = options.planNative({ ...nativeDraft, source: live });
    if (candidate.plan.unqualifiedOwnershipIds.length) throw new Error("This configuration change needs an exact native ownership descriptor.");
    if (!options.admit({ owner: live.owner, token: live.token }, candidate.owner, candidate.plan)) throw new Error("The parent refused this Elsa configuration. Its values are retained.");
    nativeDraft = null; failure = null; generation++; notify();
  }
  return {
    subscribe(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener); }; },
    getVersion: () => version,
    refresh: notify,
    /** External history changes authority first. Reconcile only clean view addresses, never retained edits. */
    reconcile(scopes: readonly BpmnScopeAddress[]) {
      if (work().pending || !scopes.length) return;
      const live = options.readLive();
      if (live.owner.structure?.schemaVersion !== "2.0.0") return;
      const parsed = readBpmnDocument(live.owner);
      let changed = false;
      if (!scopes.some(next => sameScope(next, scope))) { scope = scopes[0]; changed = true; }
      if (selection && (!parsed.byId.has(selection.elementId) || selection.diId && !parsed.byId.has(selection.diId))) { selection = null; changed = true; }
      if (changed) { generation++; notify(); }
    },
    read: () => ({ snapshot: snapshot(), selection, property, nativeDraft, geometry, pending: !!pending, fenced: fences > 0, failure, generation }),
    hasWork: () => work().pending,
    select(next: BpmnDocumentSelection | null) {
      if (work().pending) throw new Error("Apply or discard the retained BPMN edit before changing selection.");
      selection = next; notify();
    },
    setProperty(next: BpmnDocumentPropertyDraft | null) {
      if (next) assertIdle();
      else if (fences || pending) throw new Error("The retained command must finish before discarding its property draft.");
      if (geometry || nativeDraft) throw new Error("Finish the active BPMN edit before changing properties.");
      if (next) assertBaseline(next.source);
      property = next; failure = null; notify();
    },
    beginGeometry(source: BpmnDocumentCanvasSnapshot) {
      assertIdle(); assertBaseline(source);
      if (property || nativeDraft) throw new Error("Apply or discard the retained BPMN property before moving shapes.");
      geometry = { source, plan: { transaction: { commands: [] }, selection }, label: "Move BPMN shapes" }; notify();
    },
    previewGeometry(next: BpmnIntegratedGeometry) {
      assertIdle(); assertBaseline(next.source);
      if (property || nativeDraft) throw new Error("A BPMN property edit is still retained.");
      geometry = next; notify();
    },
    applyPlan(plan: BpmnDocumentOperationPlan, expected: BpmnDocumentCanvasSnapshot, label: string) {
      assertIdle();
      if (geometry || property || nativeDraft) throw new Error("Finish the retained geometry or property edit before another BPMN action.");
      stage(expected, plan, label);
    },
    applyProperty() {
      assertIdle();
      if (geometry || nativeDraft) throw new Error("Finish the active geometry edit first.");
      if (property) stage(property.source, { ...options.planProperty(property), selection: property.selection }, "Edit BPMN property");
    },
    async onTransaction(request: BpmnDocumentCanvasRequest): Promise<BpmnDocumentCanvasAcknowledgement> {
      const source = { owner: request.expectedOwner, token: request.expected, scope: request.scope }, plan = { transaction: request.transaction, selection };
      if (lastCompleted === fingerprint(source, plan)) return { kind: "accepted", current: snapshot() };
      try { assertIdle(); if (property || nativeDraft) throw new Error("A property edit is still retained."); stage(source, plan, request.label); return { kind: "accepted", current: snapshot() }; }
      catch (error) { return { kind: "refused", reason: error instanceof Error ? error.message : String(error), current: snapshot() }; }
    },
    retry() { assertAvailable(); if (fences) throw new Error("The source is fenced by a lifecycle action."); completeEditing(); },
    changeNative(draft: BpmnIntegratedNativeDraft): boolean {
      try {
        if (nativeDraft) throw new Error("An Elsa configuration is already retained.");
        nativeDraft = draft; notify();
        assertIdle(); if (geometry || property) throw new Error("Finish the retained BPMN edit first.");
        completeNative(); return true;
      } catch (error) { failure = error instanceof Error ? error.message : String(error); notify(); return false; }
    },
    discard() {
      if (fences) throw new Error("The source is fenced by a lifecycle action.");
      if (pending) {
        const fact = host.abandon(pending.request);
        acceptedSequence = fact.acknowledgement.acceptedSequence;
      }
      pending = null; geometry = null; property = null; nativeDraft = null; failure = null; generation++; notify();
    },
    /** Called only inside the resource's fenced scope transition, after its monotonic scope invalidation. */
    activateScope(next: BpmnScopeAddress) {
      if (!fences || work().pending) throw new Error("Changing a standard BPMN scope requires a completed checkpoint fence.");
      projectBpmnDocumentCanvas(options.readLive().owner, next);
      scope = next; selection = null; generation++; notify();
    },
    detach: host.detach, reattach: host.reattach, retire: host.retire
  };
}
export type BpmnDocumentIntegratedSession = ReturnType<typeof createBpmnDocumentIntegratedSession>;
