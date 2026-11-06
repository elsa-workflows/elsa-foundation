import { describe, expect, it, vi } from "vitest";
import type { ActivityNode, AuthoringCheckpointToken } from "@elsa-workflows/studio-workflows";
import { createBpmnDocumentModelerSession, type NativeDocumentAcknowledgement, type NativeDocumentCommit, type NativeDocumentSource } from "../bpmnDocumentModelerSession";
import type { NativeCommandGroup } from "../bpmnDocumentCommands";
import { authoring, deferred, edit, owner, rawXml, scope } from "./bpmnDocumentFixture";
import { createBpmnDocumentSessionHost } from "../../../../../../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/bpmn/bpmnDocumentSessionHost";
import { createAuthoringCheckpoint } from "../../../../../../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/graph-authoring/authoringCheckpoint";

const rename = (text: string, id = 1): NativeCommandGroup => ({ id, trigger: "execute", actions: [{ command: "element.updateLabel", elementId: "work", diId: "work-di", newLabel: text }] });
function control(options: { recover?: boolean; onWorkChanged?(): void } = {}) {
  // Keep the port controls free of renderer normalization; its separate suite invokes the pinned module.
  let source = owner(rawXml.replace('id="sub-di" bpmnElement="sub"', 'id="sub-di" bpmnElement="sub" isExpanded="true"')), native = "";
  let token: AuthoringCheckpointToken = { authorityId: "authority", resourceId: "draft", scopeId: "root", revision: 0 };
  const commits: { request: NativeDocumentCommit; receipt: ReturnType<typeof deferred<NativeDocumentAcknowledgement>> }[] = [];
  const admissions = new Map<number, { digest: string; acknowledgement: NativeDocumentAcknowledgement }>();
  // Controlled ports prove asynchronous ordering/CAS, not actual Modeler import or UI behavior.
  const modeler = {
    importXML: vi.fn(async (xml: string, _diagram: string) => { native = xml; return { warnings: [] as unknown[] }; }),
    saveXML: vi.fn(() => Promise.resolve({ xml: native })),
    activateScope: vi.fn(), completeEditing: vi.fn(), clearHistory: vi.fn()
  };
  const readLive = () => ({ owner: source, token });
  const commit = vi.fn((request: NativeDocumentCommit) => {
    const receipt = deferred<NativeDocumentAcknowledgement>(); commits.push({ request, receipt }); return receipt.promise;
  });
  const lookup = vi.fn(async (request: NativeDocumentCommit) => {
    const admission = admissions.get(request.sequence);
    if (!admission) return undefined;
    if (admission.digest !== JSON.stringify(request)) throw new Error("Sequence was reused for different work");
    if (JSON.stringify(admission.acknowledgement.token) !== JSON.stringify(token)) throw new Error("Receipt authority changed");
    return admission.acknowledgement;
  });
  const session = createBpmnDocumentModelerSession({ authoring, modeler, scope, readLive, commit,
    lookup: options.recover ? lookup : undefined, onWorkChanged: options.onWorkChanged });
  const setName = (name: string) => { native = edit(native, (_document, byId) => byId.get("work")!.setAttribute("name", name)); return native; };
  const admit = (index = 0) => {
    const item = commits[index];
    expect(item.request.token).toEqual(token);
    expect(item.request.sourceXml).toBe(authoring.readBpmnDocument(source).payload.documentXml);
    source = authoring.applyBpmnDocumentTransaction(source, authoring.planBpmnDocumentSyntaxChange(source, item.request.commands).transaction);
    token = { ...token, revision: token.revision + 1 };
    const acknowledgement = { owner: source, token, acceptedSequence: item.request.sequence };
    admissions.set(item.request.sequence, { digest: JSON.stringify(item.request), acknowledgement });
    return acknowledgement;
  };
  const acknowledge = (index = 0) => commits[index].receipt.resolve(admit(index));
  return { session, modeler, commits, lookup, setName, admit, acknowledge, readLive, get native() { return native; },
    editNative: (action: Parameters<typeof edit>[1]) => { native = edit(native, action); },
    replaceToken: (next: AuthoringCheckpointToken) => { token = next; } };
}
async function request(h: ReturnType<typeof control>, count = 1) { await vi.waitFor(() => expect(h.commits).toHaveLength(count)); }

describe("unenabled schema2 session with deferred native/parent ports", () => {
  it("imports the exact selected diagram, establishes baseline after import, and leaves canonical source/history clean", async () => {
    const h = control(), before = h.readLive().owner;
    await h.session.open();
    expect(h.modeler.importXML.mock.calls[0][1]).toBe("main");
    expect(h.modeler.saveXML).toHaveBeenCalledOnce();
    expect(h.modeler.activateScope).toHaveBeenCalledWith("process");
    expect(h.modeler.clearHistory).toHaveBeenCalledOnce();
    expect(h.readLive().owner).toBe(before);
    expect(h.commits).toEqual([]);
    expect(h.session.readWork()).toEqual({ pending: false, sequence: 0, acceptedSequence: 0 });
    expect(h.session.canMutate()).toBe(true);
  });

  it("waits for import completion before baseline serialization and refuses a changed live source token", async () => {
    const h = control(), imported = deferred<{ warnings: unknown[] }>();
    const importXML = h.modeler.importXML.getMockImplementation()!;
    h.modeler.importXML.mockImplementationOnce(async (xml, diagram) => { await importXML(xml, diagram); return imported.promise; });
    const opened = expect(h.session.open()).rejects.toThrow(/source changed during native import/);
    expect(h.session.readWork().pending).toBe(true);
    expect(h.modeler.saveXML).not.toHaveBeenCalled();
    expect(h.session.canMutate()).toBe(false);
    h.replaceToken({ ...h.readLive().token, revision: 1 });
    imported.resolve({ warnings: [] }); await opened;
    expect(h.modeler.saveXML).toHaveBeenCalledOnce();
    expect(h.commits).toHaveLength(0);
    expect(h.session.readWork().pending).toBe(false);
  });

  it("captures each native group immediately and admits two delayed serializations/acknowledgements in order", async () => {
    const h = control(); await h.session.open();
    const first = deferred<{ xml: string }>(), second = deferred<{ xml: string }>();
    h.modeler.saveXML.mockImplementationOnce(() => first.promise).mockImplementationOnce(() => second.promise);
    const a = h.setName("First"); h.session.enqueue(rename("First"));
    const b = h.setName("Second"); h.session.enqueue(rename("Second", 2));
    expect(h.modeler.saveXML).toHaveBeenCalledTimes(3);
    expect(h.session.readWork()).toEqual({ pending: true, sequence: 2, acceptedSequence: 0 });
    second.resolve({ xml: b }); await Promise.resolve();
    expect(h.commits).toHaveLength(0);
    first.resolve({ xml: a }); await request(h);
    expect(h.commits[0].request.commands).toContainEqual({ kind: "attribute", target: { elementId: "work" }, name: "name", value: "First" });
    expect(h.session.readWork().acceptedSequence).toBe(0);
    const flushed = h.session.flush();
    h.acknowledge(); await request(h, 2);
    expect(h.session.readWork()).toEqual({ pending: true, sequence: 2, acceptedSequence: 1 });
    expect(h.commits[1].request.token.revision).toBe(1);
    expect(h.commits[1].request.commands).toContainEqual({ kind: "attribute", target: { elementId: "work" }, name: "name", value: "Second" });
    h.acknowledge(1);
    expect((await flushed).acceptedSequence).toBe(2);
    expect(authoring.readBpmnDocument(h.readLive().owner).byId.get("work")!.getAttribute("name")).toBe("Second");
    expect(h.session.readWork().pending).toBe(false);
  });

  it("uses the acknowledged structural baseline for a second gesture captured before create admission", async () => {
    const h = control(); await h.session.open();
    h.editNative((document, index) => {
      const task = document.createElementNS(authoring.bpmnNamespaces.model, "bpmn:task"); task.setAttribute("id", "created"); task.setAttribute("name", "Created"); index.get("process")!.appendChild(task);
    });
    h.session.enqueue({ id: 1, trigger: "execute", actions: [{ command: "shape.create", elementId: "created", parentId: "process" }] });
    h.editNative((_document, index) => index.get("created")!.setAttribute("name", "Renamed while admission pending"));
    h.session.enqueue({ id: 2, trigger: "execute", actions: [{ command: "element.updateLabel", elementId: "created", newLabel: "Renamed while admission pending" }] });
    await request(h); const flushed = h.session.flush();
    expect(h.commits[0].request.commands.some(command => command.kind === "insert")).toBe(true);
    h.acknowledge(); await request(h, 2);
    expect(h.commits[1].request.commands).toContainEqual({ kind: "attribute", target: { elementId: "created" }, name: "name", value: "Renamed while admission pending" });
    h.acknowledge(1); await flushed;
    expect(authoring.readBpmnDocument(h.readLive().owner).byId.get("created")!.getAttribute("name")).toBe("Renamed while admission pending");
    expect(h.session.readWork()).toEqual({ pending: false, sequence: 2, acceptedSequence: 2 });
  });

  it("prepares a bound semantic deletion atomically with its native activity/listener ownership", async () => {
    const h = control(); await h.session.open();
    h.editNative((_document, index) => {
      for (const id of ["work", "work-di", "flow", "flow-di"]) index.get(id)!.remove();
      [...index.get("gateway")!.children].find(child => child.localName === "incoming")!.remove();
    });
    h.session.enqueue({ id: 1, trigger: "execute", actions: [{ command: "shape.delete", elementId: "work", diId: "work-di" }, { command: "connection.delete", elementId: "flow", diId: "flow-di" }] });
    const flushed = h.session.flush(); await request(h); h.acknowledge(); await flushed;
    expect(authoring.readBpmnDocument(h.readLive().owner).payload.activities).toEqual([]);
    expect(authoring.readBpmnDocument(h.readLive().owner).payload.bindings).toEqual([]);
    expect(h.session.readWork().pending).toBe(false);
  });

  it("retains a refused parent's native group and retries the exact captured edit without overwriting its baseline", async () => {
    const h = control(); await h.session.open();
    const before = h.readLive().owner, failure = new Error("Parent admission refused");
    h.setName("Retained local edit"); h.session.enqueue(rename("Retained local edit"));
    const flushed = expect(h.session.flush()).rejects.toBe(failure);
    await request(h); h.commits[0].receipt.reject(failure); await flushed;
    expect(h.readLive().owner).toBe(before);
    expect(h.session.canMutate()).toBe(false);
    expect(h.session.readWork()).toEqual({ pending: true, sequence: 1, acceptedSequence: 0 });
    await expect(h.session.open()).rejects.toThrow(/must be retained/);
    const retry = h.session.retry(); await request(h, 2);
    expect(h.commits[1].request).toEqual(h.commits[0].request);
    h.acknowledge(1); await retry;
    expect(h.session.readWork()).toEqual({ pending: false, sequence: 1, acceptedSequence: 1 });
  });

  it("refuses a late serialization after the live authority changes and retains the native source", async () => {
    const h = control(); await h.session.open();
    const saved = deferred<{ xml: string }>(); h.modeler.saveXML.mockImplementationOnce(() => saved.promise);
    const native = h.setName("Still local"); h.session.enqueue(rename("Still local"));
    const result = expect(h.session.flush()).rejects.toThrow(/authority\/revision changed/);
    h.replaceToken({ ...h.readLive().token, scopeId: "another" });
    saved.resolve({ xml: native }); await result;
    expect(h.commits).toHaveLength(0);
    expect(h.session.readWork().pending).toBe(true);
    expect(h.native).toContain('name="Still local"');
  });

  it("checks the retained exact source string even if a caller mutates an owner without advancing its token", async () => {
    const h = control(); await h.session.open();
    const saved = deferred<{ xml: string }>(); h.modeler.saveXML.mockImplementationOnce(() => saved.promise);
    const native = h.setName("Local edit"); h.session.enqueue(rename("Local edit"));
    const result = expect(h.session.flush()).rejects.toThrow(/authority\/revision changed/);
    const source = h.readLive().owner;
    source.structure!.payload.documentXml = String(source.structure!.payload.documentXml).replace('name="Original"', 'name="External edit"');
    saved.resolve({ xml: native }); await result;
    expect(h.commits).toHaveLength(0);
    expect(h.session.readWork().pending).toBe(true);
  });

  it("requires a matching live parent receipt instead of an optimistic child's acknowledgement", async () => {
    const h = control(); await h.session.open();
    h.setName("Unacknowledged"); h.session.enqueue(rename("Unacknowledged"));
    const result = expect(h.session.flush()).rejects.toThrow(/parent receipt/);
    await request(h);
    h.commits[0].receipt.resolve({ ...h.readLive(), acceptedSequence: 1 });
    await result;
    expect(h.session.readWork().acceptedSequence).toBe(0);
    expect(h.session.readWork().pending).toBe(true);
    expect(authoring.readBpmnDocument(h.readLive().owner).byId.get("work")!.getAttribute("name")).toBe("Original");
  });

  it("retains a lost receipt after canonical admission and refuses to apply that edit twice", async () => {
    const h = control(); await h.session.open(); const failure = new Error("Parent receipt lost after admission");
    h.setName("Applied once"); h.session.enqueue(rename("Applied once"));
    const result = expect(h.session.flush()).rejects.toBe(failure);
    await request(h); h.admit(); h.commits[0].receipt.reject(failure); await result;
    expect(authoring.readBpmnDocument(h.readLive().owner).byId.get("work")!.getAttribute("name")).toBe("Applied once");
    expect(h.session.readWork()).toEqual({ pending: true, sequence: 1, acceptedSequence: 0 });
    await expect(h.session.retry()).rejects.toThrow(/authority\/revision changed/);
    expect(h.commits).toHaveLength(1);
    expect(h.session.canMutate()).toBe(false);
  });

  it("recovers an admitted edit through the exact retained request without applying it again", async () => {
    const h = control({ recover: true }); await h.session.open();
    h.setName("Applied once"); h.session.enqueue(rename("Applied once"));
    const lost = expect(h.session.flush()).rejects.toThrow("Receipt transport lost");
    await request(h); const retained = h.commits[0].request;
    const admitted = h.admit(); h.commits[0].receipt.reject(new Error("Receipt transport lost")); await lost;
    expect(h.lookup).not.toHaveBeenCalled();
    expect(await h.session.retry()).toEqual(admitted);
    expect(h.lookup).toHaveBeenCalledOnce();
    expect(h.lookup.mock.calls[0][0]).toBe(retained);
    expect(h.commits).toHaveLength(1);
    expect(h.readLive().token.revision).toBe(1);
    expect(h.session.readWork()).toEqual({ pending: false, sequence: 1, acceptedSequence: 1 });
  });

  it("recovers an actual parent-ledger admission and supplies its issued coordinator receipt", async () => {
    let live = { owner: owner(rawXml.replace('id="sub-di" bpmnElement="sub"', 'id="sub-di" bpmnElement="sub" isExpanded="true"')),
      token: { authorityId: "authority", resourceId: "draft", scopeId: "root", revision: 0 } };
    let native = "", loseReceipt = true;
    const coordinator = createAuthoringCheckpoint({ readLive: () => ({ ...live, snapshot: live.owner, lineage: ["canonical-line"] }) });
    const modeler = { importXML: async (xml: string) => { native = xml; return { warnings: [] }; },
      saveXML: async () => ({ xml: native }), activateScope() {}, completeEditing() {}, clearHistory() {} };
    const commit = vi.fn(async (request: NativeDocumentCommit) => {
      const receipt = parent.commit(request);
      if (loseReceipt) { loseReceipt = false; throw new Error("Admitted receipt lost"); }
      return receipt;
    });
    const session = createBpmnDocumentModelerSession({ authoring, modeler, scope, readLive: () => live, commit,
      lookup: async request => parent.lookup(request) });
    const admit = vi.fn((expected: NativeDocumentSource, candidate: ActivityNode) => {
      if (expected !== live) return false;
      live = { owner: candidate, token: { ...live.token, revision: live.token.revision + 1 } }; return true;
    });
    const parent: ReturnType<typeof createBpmnDocumentSessionHost> = createBpmnDocumentSessionHost({ id: "native", coordinator, readLive: () => live, canEdit: () => true, admit,
      participant: session });
    await session.open();
    native = edit(native, (_document, byId) => byId.get("work")!.setAttribute("name", "Actual parent recovery"));
    session.enqueue(rename("Actual parent recovery"));
    await expect(session.flush()).rejects.toThrow("Admitted receipt lost");
    expect(coordinator.probe()).toBe(true);
    await session.retry();
    const captured = await coordinator.capture("save");
    expect(authoring.readBpmnDocument(captured.snapshot).byId.get("work")!.getAttribute("name")).toBe("Actual parent recovery");
    expect(captured.lineage).toEqual(["canonical-line"]);
    expect(captured.token.revision).toBe(1);
    expect(admit).toHaveBeenCalledOnce(); expect(commit).toHaveBeenCalledOnce();
    expect(coordinator.probe()).toBe(false);
  });

  it("retains a failed lookup and never reapplies work whose receipt belongs to another authority", async () => {
    const h = control({ recover: true }); await h.session.open();
    h.setName("Original authority edit"); h.session.enqueue(rename("Original authority edit"));
    const lost = expect(h.session.flush()).rejects.toThrow("Receipt lost");
    await request(h); h.admit(); h.commits[0].receipt.reject(new Error("Receipt lost")); await lost;
    h.replaceToken({ ...h.readLive().token, authorityId: "another-authority" });
    await expect(h.session.retry()).rejects.toThrow(/authority/);
    expect(h.commits).toHaveLength(1);
    expect(h.session.readWork()).toEqual({ pending: true, sequence: 1, acceptedSequence: 0 });
    expect(h.native).toContain('name="Original authority edit"');
  });

  it("seals commands and the baseline token before exposing an uncertain request to the parent", async () => {
    const h = control({ recover: true }); await h.session.open();
    h.setName("Sealed edit"); h.session.enqueue(rename("Sealed edit"));
    const lost = expect(h.session.flush()).rejects.toThrow("Receipt lost");
    await request(h); const retained = h.commits[0].request;
    expect(() => { (retained.commands[0] as { value: string }).value = "Different edit"; }).toThrow();
    expect(() => { (retained.token as { revision: number }).revision = 999; }).toThrow();
    h.admit(); h.commits[0].receipt.reject(new Error("Receipt lost")); await lost;
    h.lookup.mockRejectedValueOnce(new Error("Sequence was reused for different work"));
    await expect(h.session.retry()).rejects.toThrow("different work");
    expect(h.lookup.mock.calls[0][0]).toBe(retained);
    expect(h.commits).toHaveLength(1);
    expect(h.session.readWork().pending).toBe(true);
    await h.session.retry();
    expect(h.lookup.mock.calls[1][0]).toBe(retained);
    expect(h.session.readWork().pending).toBe(false);
  });

  it("notifies pending work synchronously and contains observer failure through refusal and acknowledgement", async () => {
    const events: ReturnType<ReturnType<typeof control>["session"]["readWork"]>[] = [];
    const changed = vi.fn(() => { events.push(h.session.readWork()); throw new Error("Observer failed"); });
    const h = control({ recover: true, onWorkChanged: changed }); await h.session.open();
    expect(events).toEqual([{ pending: true, sequence: 0, acceptedSequence: 0 }, { pending: false, sequence: 0, acceptedSequence: 0 }]);
    events.length = 0;
    h.setName("Observed edit");
    expect(() => h.session.enqueue(rename("Observed edit"))).not.toThrow();
    expect(events.at(-1)).toEqual({ pending: true, sequence: 1, acceptedSequence: 0 });
    await request(h); const failure = new Error("Parent refused");
    const refused = expect(h.session.flush()).rejects.toBe(failure);
    h.commits[0].receipt.reject(failure); await refused;
    expect(events).toHaveLength(2);
    const retried = h.session.retry(); await request(h, 2); h.acknowledge(1); await retried;
    expect(events.at(-1)).toEqual({ pending: false, sequence: 1, acceptedSequence: 1 });
    expect(() => h.session.refuse(new Error("Collector refused"))).not.toThrow();
    expect(events.at(-1)).toEqual({ pending: true, sequence: 2, acceptedSequence: 1 });
  });

  it("completes active direct editing, waits parent admission, and holds an idempotent mutation fence", async () => {
    const h = control(); await h.session.open();
    h.modeler.completeEditing.mockImplementation(() => { h.setName("Completed direct editing"); h.session.enqueue(rename("Completed direct editing")); });
    const checkpoint = h.session.checkpoint();
    expect(h.session.canMutate()).toBe(false);
    await request(h);
    expect(() => h.session.enqueue(rename("Late gesture"))).toThrow(/unfenced/);
    h.acknowledge();
    const lease = await checkpoint;
    expect(lease.acknowledgement.acceptedSequence).toBe(1);
    expect(h.session.canMutate()).toBe(false);
    lease.release(); lease.release();
    expect(h.session.canMutate()).toBe(true);
  });

  it("keeps import failure source-clean and permits retry without emitting a document change", async () => {
    const h = control(), before = h.readLive().owner;
    h.modeler.importXML.mockResolvedValueOnce({ warnings: [{ message: "unexplained loss" }] });
    await expect(h.session.open()).rejects.toThrow(/unexplained warnings/);
    expect(h.readLive().owner).toBe(before);
    expect(h.session.canMutate()).toBe(false);
    expect(h.session.readWork().pending).toBe(false);
    expect(h.commits).toHaveLength(0);
    await h.session.open();
    expect(h.session.canMutate()).toBe(true);
  });

  it("refreshes a hidden clean receipt after a parent epoch change without reimporting or admitting source", async () => {
    const h = control(); await h.session.open();
    h.replaceToken({ ...h.readLive().token, revision: 2 });
    expect(h.session.canMutate()).toBe(false);
    const lease = await h.session.checkpoint();
    expect(lease.acknowledgement.token).toEqual(h.readLive().token);
    expect(h.modeler.importXML).toHaveBeenCalledOnce();
    expect(h.commits).toHaveLength(0);
    lease.release();
    expect(h.session.canMutate()).toBe(true);
  });

  it("retains unknown collector mutations and refuses silent reimport or retry as clean source", async () => {
    const h = control(); await h.session.open(); const failure = new Error("Unmapped group");
    h.session.refuse(failure);
    expect(h.session.readWork()).toEqual({ pending: true, sequence: 1, acceptedSequence: 0 });
    await expect(h.session.open()).rejects.toThrow(/must be retained/);
    await expect(h.session.retry()).rejects.toBe(failure);
    expect(h.modeler.importXML).toHaveBeenCalledOnce();
    expect(h.commits).toHaveLength(0);
  });

  it("does not let an earlier accepted group clear a later collector refusal", async () => {
    const h = control(); await h.session.open(); const failure = new Error("Later unmapped mutation");
    h.setName("Earlier mapped edit"); h.session.enqueue(rename("Earlier mapped edit"));
    await request(h);
    h.session.refuse(failure);
    h.acknowledge();
    await vi.waitFor(() => expect(h.session.readWork().acceptedSequence).toBe(1));
    expect(h.session.readWork()).toEqual({ pending: true, sequence: 2, acceptedSequence: 1 });
    expect(h.session.canMutate()).toBe(false);
    await expect(h.session.flush()).rejects.toBe(failure);
    expect(() => h.session.enqueue(rename("Unsafe resumed edit", 3))).toThrow(/unfenced/);
  });

  it("retains failed serialization without reimporting over the local view", async () => {
    const h = control(); await h.session.open(); const failure = new Error("saveXML failed");
    h.modeler.saveXML.mockRejectedValueOnce(failure);
    h.setName("Unsaved native edit"); h.session.enqueue(rename("Unsaved native edit"));
    await expect(h.session.flush()).rejects.toBe(failure);
    expect(h.session.readWork()).toEqual({ pending: true, sequence: 1, acceptedSequence: 0 });
    expect(h.native).toContain('name="Unsaved native edit"');
    expect(h.modeler.importXML).toHaveBeenCalledOnce();
    expect(h.commits).toHaveLength(0);
    expect(h.session.canMutate()).toBe(false);
  });
});
