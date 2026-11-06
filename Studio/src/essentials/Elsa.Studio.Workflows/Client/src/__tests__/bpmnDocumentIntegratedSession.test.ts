import { describe, expect, it, vi } from "vitest";
import { createAuthoringCheckpoint } from "../graph-authoring/authoringCheckpoint";
import { createBpmnDocumentIntegratedSession } from "../bpmn/bpmnDocumentIntegratedSession";
import { planBpmnInspectorDraft } from "../bpmn/BpmnDocumentProperties";
import type { BpmnDocumentSessionSource } from "../bpmn/bpmnDocumentSessionHost";
import { readBpmnDocument } from "../bpmn/bpmnDocument";
import { planBpmnDocumentInspectionChange, readBpmnDocumentInspection } from "../bpmn/bpmnDocumentInspection";
import { createBpmnDocumentSessionHost } from "../bpmn/bpmnDocumentSessionHost";
import { planBpmnLabel } from "../bpmn/bpmnDocumentPropertyOperations";
import { operationOwner, operationScope } from "./bpmnDocumentOperationsFixture";

function fixture() {
  let live: BpmnDocumentSessionSource = { owner: operationOwner(), token: { authorityId: "parent", resourceId: "draft", scopeId: "actual-owner", revision: 1 } };
  let canEdit = true, accepts = true, visible = true;
  const coordinator = createAuthoringCheckpoint({ readLive: () => ({ ...live, snapshot: live.owner, lineage: [] }) });
  const admit = vi.fn((expected: BpmnDocumentSessionSource, owner: BpmnDocumentSessionSource["owner"]) => {
    if (!accepts || JSON.stringify(expected) !== JSON.stringify(live)) return false;
    live = { owner, token: { ...live.token, revision: live.token.revision + 1 } }; return true;
  });
  const session = createBpmnDocumentIntegratedSession({ id: "integrated", scope: operationScope, coordinator, readLive: () => live, canEdit: () => canEdit, canMutate: () => visible, admit, planProperty: planBpmnInspectorDraft });
  return { session, coordinator, admit, read: () => live, refuse: () => { accepts = false; }, allow: () => { accepts = true; }, revoke: () => { canEdit = false; },
    advance: () => { live = { ...live, token: { ...live.token, revision: live.token.revision + 1 } }; }, hide: () => { visible = false; }, show: () => { visible = true; } };
}
describe("retained integrated source session", () => {
  it("checkpoints an active preview before pointerup, including after its view detached", async () => {
    const { session, coordinator, read, admit } = fixture(), source = session.read().snapshot;
    session.beginGeometry(source);
    session.previewGeometry({ source, label: "Move", plan: { transaction: { commands: [{ kind: "bounds", shapeId: "BDI", bounds: { x: 365.125, y: 110.75, width: 100, height: 80 } }] }, selection: { elementId: "B", diId: "BDI" } } });
    session.detach();
    expect(coordinator.probe()).toBe(true);
    const captured = await coordinator.capture("save");
    expect(captured.snapshot).toEqual(read().owner); // Checkpoint snapshots are immutable copies of canonical state.
    expect(readBpmnDocument(read().owner).byId.get("BDI")!.firstElementChild!.getAttribute("x")).toBe("365.125");
    expect(admit).toHaveBeenCalledTimes(1);
    expect(session.hasWork()).toBe(false);
    expect(coordinator.probe()).toBe(false);
  });

  it("holds a mutation fence through lifecycle transitions and never creates a second history entry for late pointerup", async () => {
    const { session, coordinator, admit } = fixture(), source = session.read().snapshot;
    const plan = planBpmnLabel(source.owner, source.scope, { elementId: "B", diId: "BDI" }, "Completed before Save");
    session.beginGeometry(source); session.previewGeometry({ source, plan, label: "Preview" });
    await coordinator.transition("view-change", () => {
      expect(session.read().fenced).toBe(true);
      expect(() => session.beginGeometry(session.read().snapshot)).toThrow("retained");
      return undefined;
    });
    const result = await session.onTransaction({ expected: source.token, expectedOwner: source.owner, scope: source.scope, sessionId: "view", sequence: 1, label: "Move", transaction: plan.transaction });
    expect(result.kind).toBe("accepted");
    expect(admit).toHaveBeenCalledTimes(1);
    expect(session.read().fenced).toBe(false);
  });

  it("completes a controlled property draft for autosave without discarding its native configuration", async () => {
    const { session, coordinator, read } = fixture(), source = session.read().snapshot;
    session.setProperty({ source, selection: { elementId: "A", diId: "ADI" }, intent: { kind: "label", value: "Edited bound task" } });
    expect(() => session.select({ elementId: "B" })).toThrow("retained");
    expect(() => session.applyPlan(planBpmnLabel(source.owner, source.scope, { elementId: "B" }, "Other"), source, "Other")).toThrow("retained");
    await coordinator.capture("autosave");
    expect(readBpmnDocument(read().owner).byId.get("A")!.getAttribute("name")).toBe("Edited bound task");
    expect(readBpmnDocument(read().owner).payload.activities).toEqual(readBpmnDocument(source.owner).payload.activities);
    expect(session.read().selection).toEqual({ elementId: "A", diId: "ADI" });
  });

  it("retains refusal across detachment, then explicitly discards through the parent's sequence ledger", async () => {
    const { session, coordinator, admit, refuse } = fixture(), source = session.read().snapshot;
    refuse(); session.beginGeometry(source);
    session.previewGeometry({ source, plan: planBpmnLabel(source.owner, source.scope, { elementId: "B" }, "Refused"), label: "Move" });
    await expect(coordinator.capture("save")).rejects.toThrow("refused");
    expect(session.read().pending).toBe(true); expect(session.hasWork()).toBe(true);
    session.detach(); session.discard();
    expect(session.hasWork()).toBe(false);
    await coordinator.capture("save");
    expect(admit).toHaveBeenCalledTimes(1); // The discard acknowledges current source, with no history edit.
    expect(readBpmnDocument(session.read().snapshot.owner).byId.get("B")!.getAttribute("name")).toBeNull();
  });

  it("retains stale property values until explicit discard and does not overwrite newer canonical source", async () => {
    const { session, coordinator, advance, admit } = fixture(), source = session.read().snapshot;
    session.setProperty({ source, selection: { elementId: "B" }, intent: { kind: "label", value: "Old revision" } });
    advance(); await expect(coordinator.capture("save")).rejects.toThrow("earlier source");
    expect(session.read().property!.intent).toEqual({ kind: "label", value: "Old revision" });
    expect(admit).not.toHaveBeenCalled();
    session.discard(); expect((await coordinator.capture("save")).token.revision).toBe(2);
  });

  it("prevents stale work after standard A→B→A scope epochs and blocks read-only editing", async () => {
    const { session, coordinator, advance, revoke, admit } = fixture(), old = session.read().snapshot;
    const inner = { ...operationScope, diagramId: "SubDiagram", scopeElementId: "S" };
    await coordinator.transition("scope-change", () => { advance(); session.activateScope(inner); return undefined; });
    await coordinator.transition("scope-change", () => { advance(); session.activateScope(operationScope); return undefined; });
    expect(() => session.applyPlan(planBpmnLabel(old.owner, old.scope, { elementId: "B" }, "Stale"), old, "Old")).toThrow("earlier source");
    revoke(); expect(() => session.beginGeometry(session.read().snapshot)).toThrow("read-only");
    expect(admit).not.toHaveBeenCalled();
  });

  it("does not let observer failures turn an actual canonical admission into a refused edit", () => {
    const { session, admit } = fixture(), source = session.read().snapshot;
    session.subscribe(() => { throw new Error("fallible view"); });
    session.applyPlan(planBpmnLabel(source.owner, source.scope, { elementId: "B" }, "Accepted"), source, "Label");
    expect(session.read().failure).toBeNull(); expect(session.hasWork()).toBe(false); expect(admit).toHaveBeenCalledTimes(1);
  });

  it("refreshes clean parent receipts while hidden but refuses late hidden-view mutation callbacks", async () => {
    const { session, coordinator, advance, hide, show, admit } = fixture();
    await coordinator.transition("view-change", () => { advance(); hide(); return undefined; });
    expect(() => session.beginGeometry(session.read().snapshot)).toThrow("read-only");
    expect((await coordinator.capture("save")).token.revision).toBe(2);
    await coordinator.transition("view-change", () => { advance(); show(); return undefined; });
    expect((await coordinator.capture("save")).token.revision).toBe(3);
    expect(admit).not.toHaveBeenCalled();
  });
  it("completes cached native work before reconciling an unchanged Elsa invocation, through the real parent checkpoint order", async () => {
    let live: BpmnDocumentSessionSource = { owner: operationOwner(), token: { authorityId: "parent", resourceId: "draft", scopeId: "root", revision: 1 } };
    const baseline = live, coordinator = createAuthoringCheckpoint({ readLive: () => ({ token: live.token, snapshot: live.owner, lineage: [] }) });
    let pending = true, accepted = 0, proof: BpmnDocumentSessionSource | null = null;
    const changes: string[] = [];
    const admit = (expected: BpmnDocumentSessionSource, owner: typeof live.owner) => {
      if (JSON.stringify(expected) !== JSON.stringify(live)) return false;
      live = { owner, token: { ...live.token, revision: live.token.revision + 1 } }; return true;
    };
    const native = createBpmnDocumentSessionHost({ id: "native-before-fields", checkpointOrder: -100, coordinator, readLive: () => live, canEdit: () => true, admit,
      participant: { readWork: () => ({ pending, sequence: 1, acceptedSequence: accepted }), async checkpoint() {
        const result = native.commit({ token: baseline.token, sourceXml: readBpmnDocument(baseline.owner).payload.documentXml, importGeneration: 1, sequence: 1,
          commands: [{ kind: "attribute", target: { elementId: "B" }, name: "name", value: "Native XML first" }] });
        pending = false; accepted = 1; proof = { owner: result.owner, token: result.token }; changes.push("native");
        return { acknowledgement: result, release() {} };
      } } });
    const session = createBpmnDocumentIntegratedSession({ id: "fields-after-native", scope: operationScope, coordinator, readLive: () => live,
      canEdit: () => true, canMutate: () => !pending, planProperty: planBpmnInspectorDraft,
      canReconcileNative: (previous, current) => JSON.stringify(previous.owner) === JSON.stringify(baseline.owner) && JSON.stringify(current.token) === JSON.stringify(proof?.token),
      planNative: draft => planBpmnDocumentInspectionChange(draft.source.owner, draft.inspection, draft.next),
      admit(expected, owner) { changes.push("fields"); return admit(expected, owner); } });
    const source = session.read().snapshot, inspection = readBpmnDocumentInspection(source.owner, source.scope, { elementId: "A" });
    expect(session.changeNative({ source, inspection, next: { ...inspection.activity!, outputs: [{ name: "actual-output" }] } })).toBe(false);
    expect(session.read().nativeDraft).not.toBeNull();
    await coordinator.capture("save");
    expect(changes).toEqual(["native", "fields"]);
    expect(readBpmnDocument(live.owner).byId.get("B")!.getAttribute("name")).toBe("Native XML first");
    expect(readBpmnDocument(live.owner).payload.activities[0].outputs).toEqual([{ name: "actual-output" }]);
    expect(session.hasWork()).toBe(false);
  });

  it("retains an old Elsa callback after a source/scope ABA epoch without treating matching content as permission to rebase", async () => {
    let live: BpmnDocumentSessionSource = { owner: operationOwner(), token: { authorityId: "parent", resourceId: "draft", scopeId: "root", revision: 1 } };
    const coordinator = createAuthoringCheckpoint({ readLive: () => ({ token: live.token, snapshot: live.owner, lineage: [] }) }), admit = vi.fn(() => true);
    const session = createBpmnDocumentIntegratedSession({ id: "native-field-epoch", scope: operationScope, coordinator, readLive: () => live, canEdit: () => true,
      planProperty: planBpmnInspectorDraft, planNative: draft => planBpmnDocumentInspectionChange(draft.source.owner, draft.inspection, draft.next), admit });
    const source = session.read().snapshot, inspection = readBpmnDocumentInspection(source.owner, source.scope, { elementId: "A" });
    live = { ...live, token: { ...live.token, revision: 3 } };
    expect(session.changeNative({ source, inspection, next: { ...inspection.activity!, outputs: [{ name: "stale" }] } })).toBe(false);
    await expect(coordinator.capture("save")).rejects.toThrow("earlier source revision");
    expect(session.read().nativeDraft!.next.outputs).toEqual([{ name: "stale" }]); expect(admit).not.toHaveBeenCalled();
  });

});
