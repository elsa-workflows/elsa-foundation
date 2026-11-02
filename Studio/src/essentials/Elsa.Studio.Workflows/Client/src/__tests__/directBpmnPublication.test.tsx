import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { StudioBackendSession } from "@elsa-workflows/studio-sdk";
import { DirectBpmnPublishAction } from "../workflow-editor/DirectBpmnPublishAction";
import { getDraft, promoteDraft } from "../api/workflowDesign";
import { clearApiCapabilityCache } from "../api/capabilities";
import { assertLosslessBpmnImport } from "../api/bpmnInterchange";
import type { WorkflowDraft } from "../workflowTypes";

vi.mock("../api/workflowDesign", () => ({ getDraft: vi.fn(), promoteDraft: vi.fn() }));
let operationSequence = 0;
beforeEach(() => { vi.stubGlobal("crypto", { randomUUID: () => `operation-${++operationSequence}`, subtle: { digest: vi.fn(async (_algorithm: string, bytes: Uint8Array) => {
  // This fixture only needs stable, content-sensitive fingerprints; Web Crypto itself is not under test.
  const result = new Uint8Array(32); bytes.forEach((value, index) => { result[index % 32] ^= value; }); return result.buffer;
}) } }); sessionStorage.clear(); clearApiCapabilityCache(); });
afterEach(() => { vi.unstubAllGlobals(); vi.clearAllMocks(); sessionStorage.clear(); });

function fixture() {
  const draft = { id: "draft", definitionId: "definition", state: { rootActivity: { nodeId: "root" } }, layout: [], activityPresentation: [], validationErrors: [] } as unknown as WorkflowDraft;
  const capabilities = { capabilities: [{ id: "elsa.api.bpmn-publishing", contractVersion: "1", links: [{ rel: "bpmn-authored-publish", href: "interchange/bpmn/versions/{versionId}/publish-direct", templated: true }] }] };
  const postJson = vi.fn().mockResolvedValue({ sourceReferenceId: "published" });
  const session = { key: "connection", profile: { id: "backend", baseUrl: "https://backend.example", name: "Backend", source: "personal", environment: "Development" },
    context: { baseUrl: "https://backend.example", http: { getJson: vi.fn().mockResolvedValue(capabilities), postJson } },
    capabilities, disposed: false, identity: { status: "authenticated", subject: "author", tenantId: "tenant" } } as unknown as StudioBackendSession;
  vi.mocked(promoteDraft).mockResolvedValue({ id: "version", version: "1.0.0" }); vi.mocked(getDraft).mockResolvedValue(draft);
  const saveDraft = vi.fn().mockResolvedValue(draft), flushMetadata = vi.fn().mockResolvedValue(true), reload = vi.fn().mockResolvedValue(undefined);
  const setPaused = vi.fn(), setBusy = vi.fn(), setError = vi.fn(), setStatus = vi.fn(), openProcesses = vi.fn();
  const container = document.createElement("div"); document.body.appendChild(container); const root = createRoot(container);
  let currentSession = session, currentDraft = draft;
  const render = (options: { busy?: boolean; readOnly?: boolean; session?: StudioBackendSession; draft?: WorkflowDraft } = {}) => {
    currentSession = options.session ?? currentSession; currentDraft = options.draft ?? currentDraft;
    flushSync(() => root.render(<DirectBpmnPublishAction key={currentSession.key} session={currentSession} definitionId="definition" draft={currentDraft}
      busy={options.busy ?? false} readOnly={options.readOnly ?? false} saveDraft={saveDraft} flushMetadata={flushMetadata}
      onOpenProcesses={openProcesses} setPaused={setPaused} setBusy={setBusy} setError={setError} setStatus={setStatus} reload={reload} />));
  };
  const click = () => flushSync(() => container.querySelector<HTMLButtonElement>("button")!.click());
  const dispose = () => { flushSync(() => root.unmount()); container.remove(); };
  render();
  return { session, draft, postJson, saveDraft, flushMetadata, reload, setPaused, setBusy, setError, setStatus, openProcesses, container, render, click, dispose };
}

describe("Studio direct BPMN publication", () => {
  it("saves and promotes a version before publishing, without native publication", async () => {
    const view = fixture();
    try {
      view.click(); await vi.waitFor(() => expect(view.setStatus).toHaveBeenCalledWith("BPMN process published."));
      expect(view.saveDraft).toHaveBeenCalledWith(view.draft, "BPMN publication snapshot saved");
      expect(promoteDraft).toHaveBeenCalledWith(view.session.context, "draft", undefined, expect.any(String), expect.any(AbortSignal));
      expect(view.postJson).toHaveBeenCalledTimes(1);
      expect(view.postJson).toHaveBeenCalledWith("/interchange/bpmn/versions/version/publish-direct", { requestId: expect.any(String) }, { retryOnUnauthorized: false, signal: expect.any(AbortSignal) });
      expect(sessionStorage.length).toBe(0); expect(view.reload).toHaveBeenCalledTimes(1);
      const open = [...view.container.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent === "BPMN processes")!;
      expect(open).toBeTruthy(); flushSync(() => open.click()); expect(view.openProcesses).toHaveBeenCalledTimes(1);
    } finally { view.dispose(); }
  });

  it("retains the publication after a lost response and replays the exact request in a fresh matching connection", async () => {
    const view = fixture(); view.postJson.mockRejectedValueOnce(new TypeError("Response lost"));
    try {
      view.click(); await vi.waitFor(() => expect(view.setError).toHaveBeenCalledWith(expect.stringContaining("identity is retained")));
      const retained = sessionStorage.getItem(sessionStorage.key(0)!);
      expect(retained).not.toContain("rootActivity"); expect(retained).not.toContain("state");
      const first = view.postJson.mock.calls[0]; expect(view.postJson).toHaveBeenCalledTimes(1);
      view.render({ session: { ...view.session, key: "fresh-connection" } });
      expect(view.container.textContent).toContain("Resume BPMN publication"); view.click();
      await vi.waitFor(() => expect(view.setStatus).toHaveBeenCalledWith("BPMN process published."));
      expect(view.postJson).toHaveBeenCalledTimes(2); expect(view.postJson.mock.calls[1].slice(0, 2)).toEqual(first.slice(0, 2));
      expect(promoteDraft).toHaveBeenCalledTimes(1); expect(view.saveDraft).toHaveBeenCalledTimes(1);
    } finally { view.dispose(); }
  });

  it("retains the promotion identity when its response is lost", async () => {
    const view = fixture(); vi.mocked(promoteDraft).mockRejectedValueOnce(new TypeError("Promotion response lost"));
    try {
      view.click(); await vi.waitFor(() => expect(view.setError).toHaveBeenCalledWith(expect.stringContaining("identity is retained")));
      const first = vi.mocked(promoteDraft).mock.calls[0]; expect(view.postJson).not.toHaveBeenCalled();
      view.render({ session: { ...view.session, key: "fresh-connection" } }); view.click();
      await vi.waitFor(() => expect(view.setStatus).toHaveBeenCalledWith("BPMN process published."));
      expect(vi.mocked(promoteDraft).mock.calls[1].slice(0, 4)).toEqual(first.slice(0, 4)); expect(view.saveDraft).toHaveBeenCalledTimes(1);
    } finally { view.dispose(); }
  });

  it("refuses to promote changed authoring on a retained operation", async () => {
    const view = fixture(); vi.mocked(promoteDraft).mockRejectedValueOnce(new TypeError("Response lost"));
    try {
      view.click(); await vi.waitFor(() => expect(view.setError).toHaveBeenCalledWith(expect.stringContaining("identity is retained")));
      vi.mocked(getDraft).mockResolvedValue({ ...view.draft, layout: [{ nodeId: "new", x: 5, y: 8 }] });
      view.click(); await vi.waitFor(() => expect(view.setError).toHaveBeenCalledWith(expect.stringContaining("saved draft changed")));
      expect(promoteDraft).toHaveBeenCalledTimes(1); expect(view.postJson).not.toHaveBeenCalled();
      // The author can leave the stuck operation explicitly: nothing is sent and autosave resumes.
      const discard = [...view.container.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent === "Discard retained publication")!;
      expect(discard).toBeTruthy(); flushSync(() => discard.click());
      expect(sessionStorage.length).toBe(0); expect(view.setPaused).toHaveBeenLastCalledWith(false);
      expect(promoteDraft).toHaveBeenCalledTimes(1); expect(view.postJson).not.toHaveBeenCalled();
      expect(view.container.textContent).toContain("Publish BPMN"); expect(view.container.textContent).not.toContain("Discard retained publication");
    } finally { view.dispose(); }
  });

  it.each(["readOnly", "busy"] as const)("does not write while %s", async flag => {
    const view = fixture();
    try { view.render({ [flag]: true }); view.click(); expect(view.saveDraft).not.toHaveBeenCalled(); expect(promoteDraft).not.toHaveBeenCalled(); }
    finally { view.dispose(); }
  });

  it("refuses promotion when the browser cannot retain its request identity", async () => {
    const view = fixture(); const fail = vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => { throw new Error("Storage unavailable"); });
    try { view.click(); await vi.waitFor(() => expect(view.setError).toHaveBeenCalledWith("Storage unavailable")); expect(promoteDraft).not.toHaveBeenCalled(); }
    finally { fail.mockRestore(); view.dispose(); }
  });

  it("ignores a late save after the selected connection is retired", async () => {
    const view = fixture(); let resolve!: (draft: WorkflowDraft) => void;
    view.saveDraft.mockReturnValueOnce(new Promise<WorkflowDraft>(done => { resolve = done; }));
    try {
      view.click(); await vi.waitFor(() => expect(view.saveDraft).toHaveBeenCalled());
      Object.assign(view.session, { disposed: true }); resolve(view.draft);
      await vi.waitFor(() => expect(view.setBusy).toHaveBeenLastCalledWith(false));
      expect(promoteDraft).not.toHaveBeenCalled(); expect(view.postJson).not.toHaveBeenCalled();
    } finally { view.dispose(); }
  });

  it("does not replace newer local edits after publication", async () => {
    const view = fixture(); let resolve!: (value: object) => void;
    view.postJson.mockReturnValueOnce(new Promise<object>(done => { resolve = done; }));
    try {
      view.click(); await vi.waitFor(() => expect(view.postJson).toHaveBeenCalled());
      view.render({ draft: { ...view.draft, layout: [{ nodeId: "new", x: 5, y: 8 }] } }); resolve({ sourceReferenceId: "source" });
      await vi.waitFor(() => expect(view.reload).toHaveBeenCalled());
      expect(view.reload.mock.calls[0][0].shouldLoadDraft()).toBe(false);
    } finally { view.dispose(); }
  });

  it("preserves edits already present before resuming an older saved publication", async () => {
    const view = fixture(); view.postJson.mockRejectedValueOnce(new TypeError("Response lost"));
    try {
      view.click(); await vi.waitFor(() => expect(view.setError).toHaveBeenCalledWith(expect.stringContaining("identity is retained")));
      view.render({ session: { ...view.session, key: "fresh-connection" }, draft: { ...view.draft, layout: [{ nodeId: "new", x: 42, y: 84 }] } });
      view.click(); await vi.waitFor(() => expect(view.reload).toHaveBeenCalled());
      expect(view.reload.mock.calls[0][0].shouldLoadDraft()).toBe(false);
      expect(view.saveDraft).toHaveBeenCalledTimes(1); expect(promoteDraft).toHaveBeenCalledTimes(1);
    } finally { view.dispose(); }
  });

  it("allows an author to correct a definitive validation refusal without retaining a fictitious publication", async () => {
    const view = fixture(); view.postJson.mockRejectedValueOnce(Object.assign(new Error("Unsupported binding"), { status: 422 }));
    try {
      view.click(); await vi.waitFor(() => expect(view.setError).toHaveBeenCalledWith("Unsupported binding"));
      expect(sessionStorage.length).toBe(0); expect(view.setPaused).toHaveBeenLastCalledWith(false);
      expect(view.postJson).toHaveBeenCalledTimes(1);
    } finally { view.dispose(); }
  });

  it("resolves the original promotion receipt even after its draft was removed", async () => {
    const view = fixture(); vi.mocked(promoteDraft).mockRejectedValueOnce(new TypeError("Response lost"));
    try {
      view.click(); await vi.waitFor(() => expect(view.setError).toHaveBeenCalledWith(expect.stringContaining("identity is retained")));
      const first = vi.mocked(promoteDraft).mock.calls[0]; vi.mocked(getDraft).mockRejectedValueOnce(Object.assign(new Error("Missing draft"), { status: 404 }));
      view.click(); await vi.waitFor(() => expect(view.setStatus).toHaveBeenCalledWith("BPMN process published."));
      expect(vi.mocked(promoteDraft).mock.calls[1].slice(0, 4)).toEqual(first.slice(0, 4));
    } finally { view.dispose(); }
  });

  it("deduplicates repeated clicks before save or promotion completes", async () => {
    const view = fixture(); let resolve!: (draft: WorkflowDraft) => void;
    view.saveDraft.mockReturnValueOnce(new Promise<WorkflowDraft>(done => { resolve = done; }));
    try {
      view.click(); view.click(); await vi.waitFor(() => expect(view.saveDraft).toHaveBeenCalledTimes(1));
      resolve(view.draft); await vi.waitFor(() => expect(view.setStatus).toHaveBeenCalledWith("BPMN process published."));
      expect(promoteDraft).toHaveBeenCalledTimes(1); expect(view.postJson).toHaveBeenCalledTimes(1);
    } finally { view.dispose(); }
  });
});

describe("Loss-refusing direct BPMN import", () => {
  it.each([1, 2, "Degraded", "Dropped", "unknown"])("refuses severity %s before authoring replacement", severity => {
    expect(() => assertLosslessBpmnImport({ processIds: ["process"], elementCounts: {}, issues: [{ severity, message: "Extension would be lost" }] }))
      .toThrow("current process is unchanged");
  });
  it.each([0, "Info", "info"])("accepts preserved information severity %s", severity => {
    expect(() => assertLosslessBpmnImport({ processIds: ["process"], elementCounts: {}, issues: [{ severity, message: "Unbound task retained" }] })).not.toThrow();
  });
});
