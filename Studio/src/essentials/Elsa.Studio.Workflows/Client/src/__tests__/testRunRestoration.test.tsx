import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { authSessionEndedEvent, authSessionStartedEvent, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { WorkflowDraft, WorkflowTestRunView } from "../workflowTypes";
import { getDraftSignature } from "../workflow-editor/editorHelpers";
import { useTestRunRestoration } from "../workflow-editor/useTestRunRestoration";

const api = vi.hoisted(() => ({ read: vi.fn() }));
vi.mock("../api/publishing", () => ({ getWorkflowTestRun: api.read }));
const context = { baseUrl: "https://backend.example" } as StudioEndpointContext;
const draft = { id: "draft", definitionId: "definition", state: { rootActivity: null, name: "PRIVATE_AUTHORED_VALUE" }, layout: [], validationErrors: [] } as WorkflowDraft;
const view: WorkflowTestRunView = { testRunId: "test", definitionId: "definition", definitionVersionId: "draft:1", artifactId: "artifact", workflowExecutionId: "run", status: "DispatchAccepted" };
let root: Root;
let container: HTMLDivElement;
let props: Parameters<typeof useTestRunRestoration>[0];
let current: ReturnType<typeof useTestRunRestoration>;
const restored = vi.fn();
function Probe() { current = useTestRunRestoration(props); return null; }
function render(update: Partial<typeof props> = {}) {
  props = { ...props, ...update };
  flushSync(() => root.render(<Probe />));
}
async function settled(check: () => void) { await vi.waitFor(() => { flushSync(() => root.render(<Probe />)); check(); }); }
async function saveReceipt() {
  render({ testRun: { view, snapshot: draft, draftSignature: getDraftSignature(draft), endpointContext: context } });
  await settled(() => expect(sessionStorage.length).toBe(1));
  flushSync(() => root.unmount()); root = createRoot(container);
  render({ testRun: null });
  expect(current.receipt?.testRunId).toBe("test");
}
beforeEach(() => {
  vi.resetAllMocks(); sessionStorage.clear();
  // Model the digest contract deterministically; browser coverage exercises Web Crypto itself.
  const digests = new Map<string, number>();
  vi.stubGlobal("crypto", { subtle: { digest: vi.fn(async (_algorithm: string, bytes: Uint8Array) => {
    const input = Array.from(bytes).join(",");
    if (!digests.has(input)) digests.set(input, digests.size + 1);
    return new Uint8Array(32).fill(digests.get(input)!).buffer;
  }) } });
  api.read.mockResolvedValue(view);
  container = document.createElement("div"); document.body.append(container); root = createRoot(container);
  props = { context, definitionId: "definition", draft, testRun: null, onRestore: restored };
});
afterEach(() => { flushSync(() => root.unmount()); container.remove(); vi.unstubAllGlobals(); vi.restoreAllMocks(); sessionStorage.clear(); });

it("stores references and an opaque digest, then reauthorizes the same run after a reload", async () => {
  await saveReceipt();
  const stored = sessionStorage.getItem(sessionStorage.key(0)!);
  expect(stored).not.toContain("PRIVATE_AUTHORED_VALUE"); expect(stored).not.toContain("rootActivity");
  expect(stored).not.toContain("snapshot\":"); expect(api.read).not.toHaveBeenCalled();
  await current.restore();
  expect(api.read).toHaveBeenCalledTimes(1); expect(api.read).toHaveBeenCalledWith(context, "test");
  expect(restored).toHaveBeenCalledWith({ view, snapshot: draft, draftSignature: getDraftSignature(draft), endpointContext: context });
  expect(restored.mock.calls[0][0].snapshot).not.toBe(draft);
});

it("does not relabel a changed saved draft as the executed snapshot", async () => {
  await saveReceipt(); render({ draft: { ...draft, layout: [{ nodeId: "node", x: 4, y: 10 }] } });
  await current.restore();
  await settled(() => expect(current.message).toContain("saved design differs"));
  expect(api.read).not.toHaveBeenCalled(); expect(restored).not.toHaveBeenCalled();
});

it.each(["testRunId", "definitionId", "definitionVersionId", "artifactId", "workflowExecutionId"] as const)("rejects a foreign %s on a fresh read", async field => {
  await saveReceipt(); api.read.mockResolvedValue({ ...view, [field]: "foreign" });
  await current.restore();
  await settled(() => expect(current.message).toContain("identity")); expect(restored).not.toHaveBeenCalled();
});

it.each([authSessionEndedEvent, authSessionStartedEvent])("revokes the receipt and late results on %s", async event => {
  await saveReceipt(); let release!: (value: WorkflowTestRunView) => void;
  api.read.mockImplementationOnce(() => new Promise(resolve => { release = resolve; }));
  const pending = current.restore(); await settled(() => expect(api.read).toHaveBeenCalledTimes(1));
  flushSync(() => window.dispatchEvent(new Event(event)));
  release(view); await pending;
  expect(restored).not.toHaveBeenCalled(); expect(current.receipt).toBeNull(); expect(sessionStorage.length).toBe(0);
});

it("revokes same-URL authority changes and pending verification", async () => {
  await saveReceipt(); let release!: (value: WorkflowTestRunView) => void;
  api.read.mockImplementationOnce(() => new Promise(resolve => { release = resolve; }));
  const pending = current.restore(); await settled(() => expect(api.read).toHaveBeenCalledTimes(1));
  render({ context: { ...context } }); release(view); await pending;
  expect(restored).not.toHaveBeenCalled(); await settled(() => expect(current.receipt).toBeNull()); expect(sessionStorage.length).toBe(0);
});

it("ignores a late restoration after a new dispatch or draft edit", async () => {
  await saveReceipt(); let release!: (value: WorkflowTestRunView) => void;
  api.read.mockImplementationOnce(() => new Promise(resolve => { release = resolve; }));
  const pending = current.restore(); await settled(() => expect(api.read).toHaveBeenCalledTimes(1));
  render({ testRun: { view: { ...view, testRunId: "new" }, draftSignature: "new" } });
  release(view); await pending; expect(restored).not.toHaveBeenCalled();
});

it("reports an expired or unavailable run without automatically dispatching", async () => {
  await saveReceipt(); api.read.mockResolvedValue({ ...view, status: "Expired" });
  await current.restore(); await settled(() => expect(current.message).toContain("expired"));
  expect(restored).not.toHaveBeenCalled();
  api.read.mockRejectedValue(new Error("Test Run not found or expired"));
  await current.restore(); await settled(() => expect(current.message).toContain("not found or expired"));
  expect(restored).not.toHaveBeenCalled();
});

it("avoids reading an already expired receipt and permits dismissing it", async () => {
  render({ testRun: { view: { ...view, expiresAt: "2000-01-01T00:00:00Z" }, snapshot: draft, draftSignature: getDraftSignature(draft), endpointContext: context } });
  await settled(() => expect(sessionStorage.length).toBe(1));
  flushSync(() => root.unmount()); root = createRoot(container); render({ testRun: null });
  await current.restore(); await settled(() => expect(current.message).toContain("expired"));
  expect(api.read).not.toHaveBeenCalled();
  flushSync(() => current.dismiss()); expect(current.receipt).toBeNull(); expect(sessionStorage.length).toBe(0);
});

it("leaves observation functional when optional storage is unavailable", async () => {
  vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => { throw new Error("blocked storage"); });
  render({ testRun: { view, snapshot: draft, draftSignature: getDraftSignature(draft), endpointContext: context } });
  await new Promise(resolve => setTimeout(resolve, 20));
  expect(restored).not.toHaveBeenCalled(); expect(api.read).not.toHaveBeenCalled(); expect(sessionStorage.length).toBe(0);
});
