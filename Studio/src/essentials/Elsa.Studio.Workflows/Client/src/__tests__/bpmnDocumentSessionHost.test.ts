import { describe, expect, it, vi } from "vitest";
import type { ActivityNode } from "../workflowTypes";
import { createAuthoringCheckpoint } from "../graph-authoring/authoringCheckpoint";
import { bpmnNamespaces, readBpmnDocument } from "../bpmn/bpmnDocument";
import { createBpmnDocumentSessionHost, type BpmnDocumentSessionAcknowledgement, type BpmnDocumentSessionSource } from "../bpmn/bpmnDocumentSessionHost";

function fixture() {
  const owner: ActivityNode = { nodeId: "owner", activityVersionId: "bpmn", inputs: [], outputs: [], structure: {
    kind: "elsa.bpmn.structure", schemaVersion: "2.0.0", payload: {
      documentXml: `<b:definitions xmlns:b="${bpmnNamespaces.model}" targetNamespace="urn:host"><b:process id="P"><b:task id="T" name="Before"/></b:process></b:definitions>`,
      processId: "P", activities: [], bindings: [], variables: []
    }
  } };
  let source: BpmnDocumentSessionSource = { owner, token: { resourceId: "resource", authorityId: "authority", scopeId: "scope", revision: 1 } };
  let authorized = true, accepts = true, sequence = 0, acceptedSequence = 0;
  let acknowledgement: BpmnDocumentSessionAcknowledgement = { ...source, acceptedSequence: 0 };
  const release = vi.fn();
  const checkpoint = createAuthoringCheckpoint({ readLive: () => ({ token: source.token, snapshot: source.owner, lineage: [] }) });
  const admit = vi.fn((expected: BpmnDocumentSessionSource, candidate: ActivityNode) => {
    if (!accepts || expected !== source) return false;
    source = { owner: candidate, token: { ...source.token, revision: source.token.revision + 1 } };
    return true;
  });
  const host = createBpmnDocumentSessionHost({ id: "native", coordinator: checkpoint, readLive: () => source, admit, canEdit: () => authorized,
    participant: { readWork: () => ({ sequence, acceptedSequence, pending: sequence !== acceptedSequence }),
      checkpoint: async () => ({ acknowledgement, release }) } });
  const request = (name = "After") => ({ token: source.token, sourceXml: readBpmnDocument(source.owner).payload.documentXml,
    importGeneration: 1, sequence: ++sequence, commands: [{ kind: "attribute" as const, target: { elementId: "T" }, name: "name", value: name }] });
  return { host, checkpoint, request, admit, release, read: () => source,
    accept: (receipt: BpmnDocumentSessionAcknowledgement) => { acknowledgement = receipt; acceptedSequence = receipt.acceptedSequence; },
    setAcknowledgement: (receipt: BpmnDocumentSessionAcknowledgement) => { acknowledgement = receipt; },
    refuse: () => { accepts = false; }, revoke: () => { authorized = false; },
    replace: (next: BpmnDocumentSessionSource) => { source = next; } };
}

describe("canonical BPMN session receipts", () => {
  it("explicitly discards an unadmitted refused group without a canonical/history edit", async () => {
    const view = fixture(), before = view.read();
    const request = view.request();
    view.refuse();
    expect(() => view.host.commit(request)).toThrow("refused");
    const fact = view.host.abandon(request);
    expect(fact.kind).toBe("discarded");
    expect(fact.acknowledgement.owner).toEqual(before.owner);
    expect(view.read()).toBe(before);
    expect(view.admit).toHaveBeenCalledTimes(1);
    view.accept(fact.acknowledgement);
    await view.checkpoint.capture("save");
    expect(view.checkpoint.probe()).toBe(false);
    expect(() => view.host.commit(request)).toThrow("explicitly discarded");
    expect(view.admit).toHaveBeenCalledTimes(1);
  });

  it("never discards or undoes a command that the canonical parent already admitted", () => {
    const view = fixture(), request = view.request();
    const receipt = view.host.commit(request);
    const fact = view.host.abandon(request);
    expect(fact.kind).toBe("admitted");
    expect(fact.acknowledgement).toBe(receipt);
    expect(view.read().owner).toEqual(receipt.owner);
    expect(view.admit).toHaveBeenCalledTimes(1);
  });

  it("provides the actual historical admission fact after canonical changes, then acknowledges a clean re-import", async () => {
    const view = fixture(), request = view.request();
    const admitted = view.host.commit(request);
    const current = { ...view.read(), owner: { ...view.read().owner, displayName: "Inspector" }, token: { ...view.read().token, revision: 3 } };
    view.replace(current);
    expect(() => view.host.lookup(request)).toThrow("different canonical");
    expect(view.host.readAdmission(request)).toEqual({ kind: "admitted", acknowledgement: admitted });
    expect(view.host.abandon(request).kind).toBe("admitted");
    expect(view.admit).toHaveBeenCalledTimes(1);
    view.accept({ ...current, acceptedSequence: 1 });
    expect((await view.checkpoint.capture("save")).snapshot.displayName).toBe("Inspector");
  });

  it("refuses discard of a sequence never issued by retained native work", () => {
    const view = fixture();
    expect(() => view.host.abandon({ token: view.read().token, sourceXml: "", sequence: 1, importGeneration: 1, commands: [] })).toThrow("no matching retained");
    expect(view.admit).not.toHaveBeenCalled();
    expect(view.read().token.revision).toBe(1);
  });

  it("issues a receipt only after canonical admission and checkpoints its exact sequence", async () => {
    const view = fixture();
    const request = view.request();
    expect(view.checkpoint.probe()).toBe(true);
    const receipt = view.host.commit(request);
    expect(receipt.token.revision).toBe(2);
    expect(readBpmnDocument(receipt.owner).byId.get("T")?.getAttribute("name")).toBe("After");
    view.accept(receipt);
    const capture = await view.checkpoint.capture("save");
    expect(capture.snapshot).toEqual(receipt.owner);
    expect(view.release).toHaveBeenCalledTimes(1);
    expect(view.checkpoint.probe()).toBe(false);
  });

  it("rejects a retired session before any canonical mutation", () => {
    const view = fixture();
    view.host.retire();
    const request = view.request();
    expect(() => view.host.commit(request)).toThrow("retired");
    expect(() => view.host.lookup(request)).toThrow("retired");
    expect(view.admit).not.toHaveBeenCalled();
    expect(view.read().token.revision).toBe(1);
  });

  it("recovers a lost post-admission receipt without applying the XML group twice", () => {
    const view = fixture();
    const request = view.request();
    const receipt = view.host.commit(request);
    expect(view.host.lookup(request)).toBe(receipt);
    expect(view.host.commit(request)).toBe(receipt);
    expect(view.admit).toHaveBeenCalledTimes(1);
    expect(view.read().token.revision).toBe(2);
  });

  it("refuses sequence reuse for different commands even if its baseline is identical", () => {
    const view = fixture();
    const request = view.request();
    view.host.commit(request);
    expect(() => view.host.lookup({ ...request, commands: [{ ...request.commands[0], value: "Different" }] })).toThrow("different work");
    expect(view.admit).toHaveBeenCalledTimes(1);
  });

  it.each(["authorityId", "resourceId", "scopeId", "revision"] as const)("refuses a stale %s without touching canonical state", field => {
    const view = fixture();
    const request = view.request();
    const token = { ...view.read().token, [field]: field === "revision" ? 2 : "other" };
    view.replace({ ...view.read(), token });
    expect(() => view.host.commit(request)).toThrow(/baseline|authority|scope/);
    expect(view.admit).not.toHaveBeenCalled();
  });

  it("retains an unacknowledged refused group and refuses to retire its registration", () => {
    const view = fixture();
    view.refuse();
    expect(() => view.host.commit(view.request())).toThrow("refused");
    expect(view.host.lookup({ token: view.read().token, sourceXml: "", importGeneration: 1, sequence: 1, commands: [] })).toBeUndefined();
    expect(view.checkpoint.probe()).toBe(true);
    expect(() => view.host.retire()).toThrow();
  });

  it("does not recover an old receipt after authorization or canonical source changes", () => {
    const view = fixture();
    const request = view.request();
    view.host.commit(request);
    view.revoke();
    expect(() => view.host.lookup(request)).toThrow("authorization");
    expect(view.admit).toHaveBeenCalledTimes(1);
  });

  it("acknowledges a clean re-import of a canonical inspector edit without a native command", async () => {
    const view = fixture();
    const source = { ...view.read(), token: { ...view.read().token, revision: 2 }, owner: { ...view.read().owner, displayName: "Inspector" } };
    view.replace(source);
    view.setAcknowledgement({ ...source, acceptedSequence: 0 });
    expect((await view.checkpoint.capture("save")).snapshot.displayName).toBe("Inspector");
    expect(view.admit).not.toHaveBeenCalled();
  });

  it("keeps failed cleanup of a forged checkpoint lease available for an explicit retry", async () => {
    const view = fixture();
    view.setAcknowledgement({ ...view.read(), acceptedSequence: 5 });
    view.release.mockImplementationOnce(() => { throw new Error("release failed"); });
    await expect(view.checkpoint.capture("save")).rejects.toThrow("acknowledge this exact");
    expect(view.checkpoint.probe()).toBe(true);
    view.host.retryRelease();
    expect(view.release).toHaveBeenCalledTimes(2);
  });
});
