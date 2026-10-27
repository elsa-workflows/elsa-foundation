import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { authSessionEndedEvent, authSessionStartedEvent, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { ExecutableActivityGraph } from "../executableGraph";
import type { ActivityExecutionInspection, ActivityExecutionStateSummary } from "../workflowTypes";
import { loadExecutionScope } from "../workflow-editor/executionScope";
import { useAttachedExecutionSelection, type AttachedExecutionSelection } from "../workflow-editor/useAttachedExecutionSelection";

const api = vi.hoisted(() => ({ browse: vi.fn(), inspect: vi.fn(), layout: vi.fn(), supported: vi.fn() }));
vi.mock("../api/runtime", () => ({ browseActivityExecutions: api.browse, getActivityExecutionInspection: api.inspect,
  getActivityExecutionLayout: api.layout, supportsActivityExecutionBrowsing: api.supported }));
const context = {} as StudioEndpointContext;
const summary = (id = "write-4", node = "write", parent: string | null = "body-4"): ActivityExecutionStateSummary => ({
  activityExecutionId: id, executableNodeId: node, authoredActivityId: node, workflowExecutionId: "run",
  activityType: "Test.Activity", activityTypeVersion: "1", status: "Completed", scheduledAt: "2026-10-27T10:00:00Z",
  parentActivityExecutionId: parent, metadata: {}
});
const inspection = (value: ActivityExecutionStateSummary): ActivityExecutionInspection => ({ ...value,
  executionSequence: 1, provenance: { parentActivityExecutionId: value.parentActivityExecutionId, iterationId: value.iterationId, metadata: {} },
  outcomeNames: [], valueSnapshots: [], incidents: [], bookmarks: []
});
const graph = { factsByNodeId: new Map(["root", "do", "body", "write"].map(node => [node, { authoredActivityId: node, executableNodeId: node }])) } as unknown as ExecutableActivityGraph;
const labelFor = (value: ActivityExecutionStateSummary) => value.authoredActivityId;
const tree = new Map<string, ActivityExecutionInspection>();
const options = (extra = {}) => ({ graph, includeSelf: false, labelFor, signal: new AbortController().signal, ...extra });

beforeEach(() => {
  vi.resetAllMocks(); tree.clear();
  for (const value of [summary(), { ...summary("body-4", "body", "do-1"), iterationId: "iteration-4" }, summary("do-1", "do", "root-1"), summary("root-1", "root", null)]) tree.set(value.activityExecutionId, inspection(value));
  api.inspect.mockImplementation(async (_context, _run, id) => { const value = tree.get(id); if (!value) throw new Error("Unavailable"); return value; });
  api.supported.mockResolvedValue(true);
  api.browse.mockImplementation(async (_context, _run, query) => ({ totalCount: 10, items: [{ execution: summary(query.anchorActivityExecutionId, query.executableNodeId), iterationPosition: 4 }] }));
});

it("resolves exact nested ancestry and reads an iteration position inside its exact parent", async () => {
  const result = await loadExecutionScope(context, "run", summary(), options());
  expect(result.scopes.map(item => [item.execution.activityExecutionId, item.label])).toEqual([
    ["root-1", "root"], ["do-1", "do"], ["body-4", "body · Iteration 4"]
  ]);
  expect(api.browse).toHaveBeenCalledWith(context, "run", { executableNodeId: "body", parentActivityExecutionId: "do-1", anchorActivityExecutionId: "body-4", limit: 1 }, expect.any(AbortSignal));
  expect(result.activities[0].activityExecutionId).toBe("write-4");
});

it("keeps retry attempts distinct from iteration numbering", async () => {
  tree.set("body-4", { ...tree.get("body-4")!, attempt: { attemptNumber: 2, firstAttemptActivityExecutionId: "body-first" } });
  const result = await loadExecutionScope(context, "run", summary(), options());
  expect(result.scopes.at(-1)?.label).toBe("body");
  expect(api.browse).not.toHaveBeenCalled();
});

it("does not cross from one parallel parent into another", async () => {
  tree.set("write-4", inspection(summary("write-4", "write", "branch-left")));
  tree.set("branch-left", inspection(summary("branch-left", "body", "root-1")));
  const result = await loadExecutionScope(context, "run", summary(), options());
  expect(result.scopes.map(item => item.execution.activityExecutionId)).toEqual(["root-1", "branch-left"]);
  expect(api.inspect.mock.calls.map(call => call[2])).not.toContain("branch-right");
});

it("rejects a foreign run and an identity-mismatched ancestry response", async () => {
  await expect(loadExecutionScope(context, "another-run", summary(), options())).rejects.toThrow("identity");
  expect(api.inspect).not.toHaveBeenCalled();
  tree.set("body-4", { ...tree.get("body-4")!, activityExecutionId: "body-10" });
  await expect(loadExecutionScope(context, "run", summary(), options())).rejects.toThrow("identity");
});

it("rejects cyclic recorded ancestry", async () => {
  tree.set("root-1", inspection(summary("root-1", "root", "body-4")));
  await expect(loadExecutionScope(context, "run", summary(), options())).rejects.toThrow("cyclic");
});

it("requires pinned boundary structure for an execution absent from the outer graph", async () => {
  const inner = summary("inner", "private-child", "body-4"); tree.set("inner", inspection(inner));
  await expect(loadExecutionScope(context, "run", inner, options())).rejects.toThrow("boundary structure");
  tree.set("body-4", { ...tree.get("body-4")!, boundary: { layoutAvailable: true } as ActivityExecutionInspection["boundary"] });
  api.layout.mockResolvedValue({ activityExecutionId: "body-4", workflowExecutionId: "run", nodes: [] });
  const result = await loadExecutionScope(context, "run", inner, options());
  expect(result.layout?.activityExecutionId).toBe("body-4");
  api.layout.mockResolvedValue({ activityExecutionId: "wrong", workflowExecutionId: "run", nodes: [] });
  await expect(loadExecutionScope(context, "run", inner, options())).rejects.toThrow("boundary identity");
});

it("stops a late ancestry read after cancellation", async () => {
  let release!: (value: ActivityExecutionInspection) => void;
  api.inspect.mockImplementationOnce(() => new Promise(resolve => { release = resolve; }));
  const controller = new AbortController();
  const task = loadExecutionScope(context, "run", summary(), options({ signal: controller.signal }));
  controller.abort(); release(tree.get("write-4")!);
  await expect(task).rejects.toMatchObject({ name: "AbortError" });
  expect(api.inspect).toHaveBeenCalledTimes(1);
});

let root: Root | undefined;
let container: HTMLDivElement | undefined;
let current: AttachedExecutionSelection;
let props: Parameters<typeof useAttachedExecutionSelection>[0];
function mount(update = {}) {
  props = { ...(props ?? { context, attachmentId: "test:artifact:snapshot", workflowExecutionId: "run", enabled: true, nodeId: "write" }), ...update };
  if (!container) { container = document.createElement("div"); document.body.append(container); root = createRoot(container); }
  // Keep the probe's component identity stable across renders.
  flushSync(() => root!.render(<SelectionProbe />));
}
function SelectionProbe() { current = useAttachedExecutionSelection(props); return null; }
afterEach(() => { if (root) flushSync(() => root!.unmount()); container?.remove(); root = undefined; container = undefined; props = undefined as unknown as typeof props; });

it("retains verified exact selection while the inspector changes tabs, and clears it on draft drift", async () => {
  mount(); await current.choose(summary(), graph, labelFor);
  mount(); expect(current.execution?.activityExecutionId).toBe("write-4");
  expect(current.scopes.at(-1)?.execution.activityExecutionId).toBe("body-4");
  mount({ enabled: false }); expect(current.execution).toBeNull(); expect(current.scopes).toEqual([]);
});

it("prevents an obsolete attachment response from restoring old evidence", async () => {
  mount(); let release!: (value: ActivityExecutionInspection) => void;
  api.inspect.mockImplementationOnce(() => new Promise(resolve => { release = resolve; }));
  const task = current.choose(summary(), graph, labelFor);
  await vi.waitFor(() => expect(api.inspect).toHaveBeenCalledTimes(1));
  mount({ attachmentId: "new-test" }); expect(current.execution).toBeNull();
  release(tree.get("write-4")!); await task;
  expect(current.execution).toBeNull(); expect(current.scopes).toEqual([]);
});

it("revokes selection on sign-out and does not reuse it after sign-in", async () => {
  mount(); await current.choose(summary(), graph, labelFor); mount();
  flushSync(() => window.dispatchEvent(new Event(authSessionEndedEvent)));
  expect(current.execution).toBeNull(); expect(current.scopes).toEqual([]);
  expect(await current.choose(summary(), graph, labelFor)).toBe(false);
  flushSync(() => window.dispatchEvent(new Event(authSessionStartedEvent)));
  expect(current.execution).toBeNull();
});

it("rejects an in-flight selection when authorization refreshes without a sign-out", async () => {
  mount(); let release!: (value: ActivityExecutionInspection) => void;
  api.inspect.mockImplementationOnce(() => new Promise(resolve => { release = resolve; }));
  const task = current.choose(summary(), graph, labelFor);
  await vi.waitFor(() => expect(api.inspect).toHaveBeenCalledTimes(1));
  flushSync(() => window.dispatchEvent(new Event(authSessionStartedEvent)));
  expect(current.execution).toBeNull();
  release(tree.get("write-4")!);
  expect(await task).toBe(false);
  mount(); expect(current.execution).toBeNull(); expect(current.scopes).toEqual([]);
});

it("rejects foreign-run selections and clears evidence across backend contexts", async () => {
  mount(); expect(await current.choose({ ...summary(), workflowExecutionId: "other" }, graph, labelFor)).toBe(false);
  await current.choose(summary(), graph, labelFor); mount();
  mount({ workflowExecutionId: "replacement-run" }); expect(current.execution).toBeNull(); expect(current.scopes).toEqual([]);
  mount({ context: {} as StudioEndpointContext }); expect(current.execution).toBeNull(); expect(current.scopes).toEqual([]);
});

it("binds an incident selection after its different canvas activity becomes selected", async () => {
  mount(); await current.choose(summary(), graph, labelFor); mount();
  const target = summary("body-4", "body", "do-1");
  flushSync(() => {
    current.queue(target, graph, labelFor, "body");
    props = { ...props, nodeId: "body" };
    root!.render(<SelectionProbe />);
  });
  await vi.waitFor(() => expect(current.execution?.activityExecutionId).toBe("body-4"));
  expect(current.execution?.activityExecutionId).not.toBe("write-4");
});

it("keeps an unavailable scope blocked until an explicit reset", async () => {
  mount(); await current.choose(summary(), graph, labelFor); mount();
  flushSync(() => current.unavailable());
  expect(current.execution).toBeNull(); expect(current.scopes).toEqual([]); expect(current.error).toContain("unavailable");
  flushSync(() => current.clear()); expect(current.error).toBe("");
});

it.each(["incidents", "bookmarks"] as const)("keeps exact attached selection when %s are unavailable in selected and ancestor responses", async collection => {
  for (const value of tree.values()) Reflect.deleteProperty(value, collection);
  mount();
  expect(await current.choose(summary(), graph, labelFor)).toBe(true);
  mount();
  expect(current.execution?.activityExecutionId).toBe("write-4");
  expect(current.scopes.map(item => item.execution.activityExecutionId)).toEqual(["root-1", "do-1", "body-4"]);
  const count = collection === "incidents" ? "incidentCount" : "bookmarkCount";
  const ids = collection === "incidents" ? "incidentIds" : "bookmarkIds";
  expect(current.scopes.map(item => item.execution[count])).toEqual([null, null, null]);
  expect(current.scopes.every(item => item.execution[ids] === undefined)).toBe(true);
  expect(current.error).toBe("");
});
