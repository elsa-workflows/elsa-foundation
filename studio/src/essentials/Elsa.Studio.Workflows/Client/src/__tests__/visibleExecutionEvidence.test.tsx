import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { ActivityExecutionStateSummary } from "../workflowTypes";
import { readVisibleActivityExecutions, useScopedCanvasActivities } from "../workflow-editor/scopedExecutionEvidence";
const api = vi.hoisted(() => ({ browse: vi.fn() }));
vi.mock("../api/runtime", () => ({ browseActivityExecutions: api.browse }));
const context = {} as StudioEndpointContext;
const execution = (id = "chosen", node = "node"): ActivityExecutionStateSummary => ({ activityExecutionId: id, executableNodeId: node,
  workflowExecutionId: "run", authoredActivityId: node, activityType: "Test", activityTypeVersion: "1", status: "Completed", scheduledAt: "2026-10-27T10:00:00Z", metadata: {} });
let root: Root;
let container: HTMLDivElement;
beforeEach(() => { vi.resetAllMocks(); container = document.createElement("div"); document.body.append(container); root = createRoot(container); });
afterEach(() => { flushSync(() => root.unmount()); container.remove(); });
it("requires an exact choice for repeated root occurrences and rejects a mismatched anchor response", async () => {
  api.browse.mockResolvedValue({ totalCount: 10, items: [{ execution: execution("latest") }] });
  const signal = new AbortController().signal;
  expect((await readVisibleActivityExecutions(context, "run", ["node"], undefined, undefined, signal)).activities).toEqual([]);
  expect((await readVisibleActivityExecutions(context, "run", ["node"], undefined, execution(), signal)).activities).toEqual([]);
  api.browse.mockResolvedValue({ totalCount: 10, items: [{ execution: execution() }] });
  expect((await readVisibleActivityExecutions(context, "run", ["node"], undefined, execution(), signal)).activities[0].activityExecutionId).toBe("chosen");
  expect(api.browse).toHaveBeenLastCalledWith(context, "run", { executableNodeId: "node", anchorActivityExecutionId: "chosen", limit: 1 }, signal);
});
it("coalesces refreshes behind a slow scope read and retains the exact parent", async () => {
  const releases: Array<() => void> = [];
  api.browse.mockImplementation((_context, _run, query) => new Promise(resolve => { releases.push(() => resolve({ totalCount: 1, items: [{ execution: execution(`read-${releases.length}`) }] })); expect(query.ancestorActivityExecutionId).toBe("parent"); }));
  let refresh = 0;
  let result: ReturnType<typeof useScopedCanvasActivities>;
  function Probe() { result = useScopedCanvasActivities(context, "run", "parent", ["node"], refresh); return null; }
  const render = () => flushSync(() => root.render(<Probe />));
  render(); const signal = api.browse.mock.calls[0][3] as AbortSignal;
  refresh = 1; render(); refresh = 2; render(); expect(signal.aborted).toBe(false); expect(api.browse).toHaveBeenCalledTimes(1);
  releases[0](); await vi.waitFor(() => expect(api.browse).toHaveBeenCalledTimes(2));
  expect(result!.activities).toHaveLength(1);
  releases[1](); await vi.waitFor(() => expect(result!.loading).toBe(false));
  expect(api.browse).toHaveBeenCalledTimes(2);
});
it("bounds visible queries to four in flight and a single row per node", async () => {
  let active = 0; let maximum = 0;
  api.browse.mockImplementation(async (_context, _run, query) => {
    active++; maximum = Math.max(maximum, active); await Promise.resolve(); active--;
    expect(query.limit).toBe(1); return { totalCount: 1, items: [{ execution: execution(query.executableNodeId, query.executableNodeId) }] };
  });
  const result = await readVisibleActivityExecutions(context, "run", Array.from({ length: 50 }, (_, i) => `node-${i}`), "parent", undefined, new AbortController().signal);
  expect(maximum).toBe(4); expect(result.activities).toHaveLength(50); expect(result.failed).toBe(false);
});
it("does not refetch or flash loading when selection moves across singleton nodes in the same scope", async () => {
  const first = execution("exec-1", "node-1");
  const second = execution("exec-2", "node-2");
  const outside = execution("exec-root", "root");
  api.browse.mockImplementation(async (_context, _run, query) => ({
    totalCount: 1,
    items: [{ execution: query.executableNodeId === "node-1" ? first : second }]
  }));
  let selected: ActivityExecutionStateSummary | undefined;
  let result: ReturnType<typeof useScopedCanvasActivities>;
  function Probe() { result = useScopedCanvasActivities(context, "run", "parent", ["node-1", "node-2"], 0, selected); return null; }
  const render = () => flushSync(() => root.render(<Probe />));
  render();
  await vi.waitFor(() => expect(result!.loading).toBe(false));
  expect(api.browse).toHaveBeenCalledTimes(2);

  selected = first; render();
  selected = second; render();
  selected = outside; render();
  selected = undefined; render();
  await Promise.resolve();

  expect(result!.loading).toBe(false);
  expect(result!.anchoring).toBe(false);
  expect(result!.activities.map(item => item.activityExecutionId)).toEqual(["exec-1", "exec-2"]);
  expect(api.browse).toHaveBeenCalledTimes(2);
});
it("anchors and un-anchors repeated nodes in the background without flashing loading or clearing other nodes", async () => {
  const single = execution("exec-single", "node-single");
  const repeatA = execution("exec-repeat-a", "node-repeat");
  const repeatB = execution("exec-repeat-b", "node-repeat");
  const pending: Array<() => void> = [];
  api.browse.mockImplementation((_context, _run, query) => new Promise(resolve => {
    pending.push(() => {
      if (query.executableNodeId === "node-single") {
        resolve({ totalCount: 1, items: [{ execution: single }] });
        return;
      }
      const chosen = query.anchorActivityExecutionId === repeatB.activityExecutionId ? repeatB : repeatA;
      resolve({ totalCount: 2, items: [{ execution: chosen }] });
    });
  }));
  let selected: ActivityExecutionStateSummary | undefined;
  let result: ReturnType<typeof useScopedCanvasActivities>;
  function Probe() { result = useScopedCanvasActivities(context, "run", "parent", ["node-single", "node-repeat"], 0, selected); return null; }
  const render = () => flushSync(() => root.render(<Probe />));
  render();
  while (pending.length) pending.shift()!();
  await vi.waitFor(() => expect(result!.loading).toBe(false));
  expect(result!.repeated).toBe(true);
  expect(result!.activities.map(item => item.activityExecutionId)).toEqual(["exec-single"]);

  selected = repeatB; render();
  expect(result!.loading).toBe(false);
  expect(result!.anchoring).toBe(true);
  expect(result!.activities.map(item => item.activityExecutionId)).toEqual(["exec-single"]);
  await vi.waitFor(() => expect(pending.length).toBeGreaterThan(0));
  while (pending.length) pending.shift()!();
  await vi.waitFor(() => expect(result!.anchoring).toBe(false));
  expect(result!.repeated).toBe(false);
  expect(result!.activities.map(item => item.activityExecutionId).sort()).toEqual(["exec-repeat-b", "exec-single"]);

  selected = single; render();
  expect(result!.loading).toBe(false);
  expect(result!.anchoring).toBe(true);
  await vi.waitFor(() => expect(pending.length).toBeGreaterThan(0));
  while (pending.length) pending.shift()!();
  await vi.waitFor(() => expect(result!.repeated).toBe(true));
  expect(result!.anchoring).toBe(false);
  expect(result!.activities.map(item => item.activityExecutionId)).toEqual(["exec-single"]);
});
