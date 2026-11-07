import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { requestStudioNavigation, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { WorkflowDraft } from "../workflowTypes";
import type { WorkflowEditorDocumentAdapter } from "../workflow-editor/WorkflowEditorDocumentAdapter";
import { useWorkflowDocument, type WorkflowDocumentApi } from "../workflow-editor/workflowDocument";
import { useWorkflowPersistence, type PreparedWorkflowSave, type WorkflowSaveCheckpoint } from "../workflow-editor/useWorkflowPersistence";
import type { WorkflowLineageHold } from "../workflow-editor/useDraftHistory";
import { autosaveDelayMs } from "../workflow-editor/constants";

function deferred<T>() {
  let resolve!: (value: T) => void, reject!: (error: unknown) => void;
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no; });
  return { promise, resolve, reject };
}
function draft(name = "Initial"): WorkflowDraft {
  return { id: "draft", definitionId: "definition", layout: [], activityPresentation: [], validationErrors: [],
    state: { rootActivity: { nodeId: "root", activityVersionId: "activity@1", inputs: [], outputs: [], displayName: name } } };
}
function name(value: WorkflowDraft) { const label = value.state.rootActivity!.displayName; return typeof label === "string" ? label : ""; }
const context = { baseUrl: "test://checkpointed-persistence", http: {} } as unknown as StudioEndpointContext;
const mounted: (() => void)[] = [];
beforeEach(() => vi.useFakeTimers());
afterEach(() => { for (const unmount of mounted.splice(0)) unmount(); vi.useRealTimers(); });
async function settle() { await vi.advanceTimersByTimeAsync(0); flushSync(() => {}); }

function renderPersistence({ port = true, autosave = false }: { port?: boolean; autosave?: boolean } = {}) {
  const container = document.createElement("div"); document.body.append(container); const root = createRoot(container);
  let documentApi!: WorkflowDocumentApi, persistence!: ReturnType<typeof useWorkflowPersistence>;
  let pending = false, revision = 0, line = ["initial-line"];
  const preparations: { request: Parameters<WorkflowSaveCheckpoint["prepare"]>[0]; result: ReturnType<typeof deferred<PreparedWorkflowSave>> }[] = [];
  const prepare = vi.fn<WorkflowSaveCheckpoint["prepare"]>(request => {
    const result = deferred<PreparedWorkflowSave>(); preparations.push({ request, result }); return result.promise;
  });
  const checkpoint: WorkflowSaveCheckpoint = { prepare, probe: vi.fn(() => pending), get revision() { return revision; } };
  const recordSave = vi.fn<WorkflowLineageHold["recordSave"]>();
  const lineageHold: WorkflowLineageHold = { held: false, isHeld: () => false, lineage: () => new Set(line), recordSave };
  const saved = vi.fn<WorkflowEditorDocumentAdapter["save"]>(async (_context, value) => structuredClone(value));
  const adapter: WorkflowEditorDocumentAdapter = {
    save: saved,
    async load() { throw new Error("The persistence control never loads a resource"); },
    async metadata() { throw new Error("The persistence control never edits metadata"); }
  };
  const errors = vi.fn(), statuses = vi.fn(), reconciled = vi.fn();
  function Harness() {
    documentApi = useWorkflowDocument();
    persistence = useWorkflowPersistence({ context, draft: documentApi.state.draft, documentAdapter: adapter,
      autosaveEnabledByDefault: autosave, editDraft: documentApi.editDraft, onSaveReconciled: reconciled,
      setError: errors, setStatus: statuses, lineageHold, authoringCheckpoint: port ? checkpoint : undefined });
    return <output>{documentApi.state.draft && name(documentApi.state.draft)}</output>;
  }
  const render = () => flushSync(() => root.render(<Harness />));
  render(); const initial = draft();
  flushSync(() => { documentApi.loadDraft(initial); persistence.markSaved(initial); });
  mounted.push(() => { flushSync(() => root.unmount()); container.remove(); });
  return {
    get persistence() { return persistence; }, get document() { return documentApi; }, initial, prepare, preparations,
    checkpoint, saved, errors, statuses, reconciled, recordSave, render,
    setPending(value: boolean) { pending = value; revision++; render(); },
    setLineage(value: string[]) { line = value; },
    admit(value: WorkflowDraft, lineage = line) { line = lineage; pending = false; revision++; flushSync(() => documentApi.editDraft(() => value)); },
    navigationAllowed: () => requestStudioNavigation("/workflows/editor", "/settings/backends"),
    unloadAllowed: () => window.dispatchEvent(new Event("beforeunload", { cancelable: true }))
  };
}

describe("checkpointed workflow persistence contract", () => {
  it("writes the delayed acknowledged native snapshot and its paired lineage", async () => {
    const h = renderPersistence(); h.setPending(true);
    const result = h.persistence.saveDraft(h.initial, "Saved", { capture: "current", lineage: ["click-line"] });
    expect(h.prepare).toHaveBeenCalledWith({ snapshot: h.initial, intent: "save", capture: "current", lineage: ["click-line"] });
    expect(h.saved).not.toHaveBeenCalled();
    const acknowledged = draft("Native flush accepted"); h.admit(acknowledged, ["native-line"]);
    h.preparations[0].result.resolve({ draft: acknowledged, lineage: ["native-line"] });
    expect(name(await result)).toBe("Native flush accepted"); await settle();
    expect(h.saved).toHaveBeenCalledWith(context, acknowledged);
    expect(h.recordSave).toHaveBeenCalledWith(["native-line"], true);
    expect(h.reconciled).toHaveBeenCalledOnce();
    expect(h.persistence.persistedDraft).toEqual(acknowledged);
  });

  it("reserves HTTP order when later preparation resolves first", async () => {
    const h = renderPersistence(), a = draft("A"), b = draft("B");
    const first = h.persistence.saveDraft(a, "Saved", { lineage: ["line-A"] });
    const second = h.persistence.saveDraft(b, "Saved", { lineage: ["line-B"] });
    expect(h.prepare).toHaveBeenCalledTimes(2);
    h.preparations[1].result.resolve({ draft: b }); await settle();
    expect(h.saved).not.toHaveBeenCalled();
    h.preparations[0].result.resolve({ draft: a }); await Promise.all([first, second]); await settle();
    expect(h.saved.mock.calls.map(([, value]) => name(value))).toEqual(["A", "B"]);
    expect(h.recordSave.mock.calls.map(([lineage]) => lineage)).toEqual([["line-A"], ["line-B"]]);
  });

  it("reserves the first HTTP slot before preparation synchronously requests another save", async () => {
    const h = renderPersistence(), a = draft("Outer A"), b = draft("Reentrant B"), gate = deferred<PreparedWorkflowSave>();
    let inner: Promise<WorkflowDraft> | undefined;
    h.prepare.mockImplementationOnce(() => {
      inner = h.persistence.saveDraft(b, "Saved");
      return gate.promise;
    }).mockResolvedValueOnce({ draft: b });
    const outer = h.persistence.saveDraft(a, "Saved");
    await settle(); expect(h.saved).not.toHaveBeenCalled();
    gate.resolve({ draft: a }); await outer; await inner; await settle();
    expect(h.saved.mock.calls.map(([, value]) => name(value))).toEqual(["Outer A", "Reentrant B"]);
  });

  it("seals prepared producer data and lineage while waiting behind an earlier HTTP save", async () => {
    const h = renderPersistence(), firstHttp = deferred<WorkflowDraft>(), a = draft("A"), producer = draft("Prepared B"), line = ["B-line"];
    h.saved.mockImplementationOnce(() => firstHttp.promise);
    h.prepare.mockResolvedValueOnce({ draft: a });
    const first = h.persistence.saveDraft(a, "Saved"); await settle();
    const second = h.persistence.saveDraft(producer, "Saved");
    h.preparations[0].result.resolve({ draft: producer, lineage: line }); await settle();
    producer.state.rootActivity!.displayName = "Mutated after preparation"; line.push("unsaved-future-line");
    firstHttp.resolve(a); await first; await second; await settle();
    expect(name(h.saved.mock.calls[1][1])).toBe("Prepared B");
    expect(h.recordSave.mock.calls[1]).toEqual([["B-line"], true]);
  });

  it("does not credit producer mutations made while HTTP is in flight", async () => {
    const h = renderPersistence(), http = deferred<WorkflowDraft>(), producer = draft("Written"), line = ["written-line"];
    h.prepare.mockResolvedValueOnce({ draft: producer, lineage: line }); h.saved.mockImplementationOnce(() => http.promise);
    const result = h.persistence.saveDraft(producer, "Saved"); await settle();
    const written = h.saved.mock.calls[0][1];
    producer.state.rootActivity!.displayName = "Later producer value"; line.push("not-written");
    expect(name(written)).toBe("Written"); http.resolve(written); await result; await settle();
    expect(h.recordSave).toHaveBeenCalledWith(["written-line"], true);
  });

  it("observes an early queued preparation refusal, sends no rejected HTTP request, and continues later saves", async () => {
    const h = renderPersistence(), http = deferred<WorkflowDraft>(), a = draft("A"), b = draft("Refused B"), c = draft("C");
    h.prepare.mockResolvedValueOnce({ draft: a }); h.saved.mockImplementationOnce(() => http.promise);
    const first = h.persistence.saveDraft(a, "Saved"); await settle();
    const failure = new Error("Native flush refused");
    const second = h.persistence.saveDraft(b, "Saved"); const refused = expect(second).rejects.toBe(failure);
    h.preparations[0].result.reject(failure); await settle();
    expect(h.saved).toHaveBeenCalledOnce();
    http.resolve(a); await first; await refused; await settle();
    expect(h.errors).toHaveBeenLastCalledWith(expect.objectContaining({ message: "Native flush refused" }));
    expect(h.recordSave).toHaveBeenCalledTimes(1);
    h.prepare.mockResolvedValueOnce({ draft: c }); await h.persistence.saveDraft(c, "Saved"); await settle();
    expect(h.saved.mock.calls.map(([, value]) => name(value))).toEqual(["A", "C"]);
  });

  it("rechecks a prepared queued request's authority before sending HTTP", async () => {
    const h = renderPersistence(), http = deferred<WorkflowDraft>(), a = draft("A"), b = draft("B"); let authorized = true;
    h.prepare.mockResolvedValueOnce({ draft: a }); h.saved.mockImplementationOnce(() => http.promise);
    const first = h.persistence.saveDraft(a, "Saved"); await settle();
    const second = h.persistence.saveDraft(b, "Saved"); const refused = expect(second).rejects.toThrow("Authority revoked");
    h.preparations[0].result.resolve({ draft: b, assertCurrent() { if (!authorized) throw new Error("Authority revoked"); } });
    await settle(); authorized = false; http.resolve(a); await first; await refused; await settle();
    expect(h.saved).toHaveBeenCalledOnce(); expect(h.recordSave).toHaveBeenCalledTimes(1);
  });

  it("does not reconcile or credit an old-authority HTTP response into a new resource", async () => {
    const h = renderPersistence(), http = deferred<WorkflowDraft>(); let authorized = true;
    h.prepare.mockResolvedValueOnce({ draft: h.initial, lineage: ["old-authority-line"], assertCurrent() { if (!authorized) throw new Error("Authority revoked"); } });
    h.saved.mockImplementationOnce(() => http.promise);
    const result = h.persistence.saveDraft(h.initial, "Saved"); const refused = expect(result).rejects.toThrow("Authority revoked");
    await settle(); const replacement = draft("New authority"); h.admit(replacement); authorized = false;
    flushSync(() => h.persistence.markSaved(replacement));
    const oldSaved = { ...h.initial, validationErrors: [{ type: "old", message: "Must not leak into new resource" }] };
    http.resolve(oldSaved); await refused; await settle();
    expect(h.persistence.persistedDraft).toEqual(replacement);
    expect(h.document.state.draft).toEqual(replacement);
    expect(h.recordSave).not.toHaveBeenCalled(); expect(h.reconciled).not.toHaveBeenCalled();
  });

  it("preserves explicit derived snapshots and their caller lineage when no checkpoint is composed", async () => {
    const h = renderPersistence({ port: false }), derived = draft("Reviewed version change");
    derived.state.rootActivity!.activityVersionId = "activity@2";
    await h.persistence.saveDraft(derived, "Activity version changed", { lineage: ["derived-source-line"] }); await settle();
    expect(h.prepare).not.toHaveBeenCalled(); expect(h.saved).toHaveBeenCalledWith(context, derived);
    expect(h.recordSave).toHaveBeenCalledWith(["derived-source-line"], true);
    expect(h.document.state.draft!.state.rootActivity!.activityVersionId).toBe("activity@1");
  });

  it("defaults reviewed saves to snapshot capture even when their contents equal the live click-time draft", async () => {
    const h = renderPersistence(), reviewed = structuredClone(h.initial);
    const result = h.persistence.saveDraft(reviewed, "Publication snapshot saved", { lineage: ["reviewed-line"] });
    const refused = expect(result).rejects.toThrow("Reviewed snapshot is stale");
    expect(h.preparations[0].request.capture).toBe("snapshot");
    expect(h.preparations[0].request.lineage).toEqual(["reviewed-line"]);
    h.admit(draft("New native source")); h.preparations[0].result.reject(new Error("Reviewed snapshot is stale")); await refused;
    expect(h.saved).not.toHaveBeenCalled(); expect(h.recordSave).not.toHaveBeenCalled();
  });

  it("distinguishes omitted prepared lineage from an explicitly unknown written line", async () => {
    const h = renderPersistence();
    h.prepare.mockResolvedValueOnce({ draft: h.initial }).mockResolvedValueOnce({ draft: h.initial, lineage: null });
    await h.persistence.saveDraft(h.initial, "Saved", { lineage: ["captured"] });
    await h.persistence.saveDraft(h.initial, "Saved", { lineage: ["must-not-credit"] }); await settle();
    expect(h.recordSave.mock.calls).toEqual([[["captured"], true], [null, true]]);
  });

  it("blocks clean-signature navigation synchronously for native pending work without starting serialization", () => {
    const h = renderPersistence(); expect(h.navigationAllowed()).toBe(true);
    h.setPending(true);
    expect(h.navigationAllowed()).toBe(false); expect(h.unloadAllowed()).toBe(false);
    expect(h.prepare).not.toHaveBeenCalled(); expect(h.saved).not.toHaveBeenCalled();
  });

  it("flushes native work with current capture even when the canonical signature is already saved", async () => {
    const h = renderPersistence(); h.setPending(true);
    const result = h.persistence.flushPendingSave();
    expect(h.preparations[0].request).toMatchObject({ capture: "current", intent: "save" });
    const acknowledged = draft("Flushed before review"); h.admit(acknowledged, ["flushed-line"]);
    h.preparations[0].result.resolve({ draft: acknowledged, lineage: ["flushed-line"] }); await result; await settle();
    expect(h.saved).toHaveBeenCalledWith(context, acknowledged);
    expect(h.navigationAllowed()).toBe(true);
  });

  it("starts clean-signature native autosave from the checkpoint revision and captures current source", async () => {
    const h = renderPersistence({ autosave: true }); h.setPending(true);
    await vi.advanceTimersByTimeAsync(autosaveDelayMs);
    expect(h.preparations).toHaveLength(1);
    expect(h.preparations[0].request).toMatchObject({ capture: "current", intent: "autosave" });
    expect(h.saved).not.toHaveBeenCalled();
    const acknowledged = draft("Native autosaved"); h.admit(acknowledged, ["autosaved-line"]);
    h.preparations[0].result.resolve({ draft: acknowledged, lineage: ["autosaved-line"] }); await settle();
    expect(h.saved).toHaveBeenCalledWith(context, acknowledged);
    expect(h.recordSave).toHaveBeenCalledWith(["autosaved-line"], false);
    expect(h.navigationAllowed()).toBe(true);
  });
});
