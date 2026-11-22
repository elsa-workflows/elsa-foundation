import { Activity } from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { authSessionEndedEvent, authSessionStartedEvent, StudioWorkspaceTabProvider, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { readActivityPlayback, type PlaybackPage, type PlaybackOccurrence } from "../api/activityPlayback";
import { useActivityPlayback } from "../workflow-editor/useActivityPlayback";
import type { PlaybackOperation, PreparedPlaybackSelection } from "../workflow-editor/activityPlaybackModel";

vi.mock("../api/activityPlayback", async original => ({ ...await original<typeof import("../api/activityPlayback")>(), readActivityPlayback: vi.fn() }));
const read = vi.mocked(readActivityPlayback);
let root: Root, container: HTMLDivElement, context: StudioEndpointContext, run: string, scope: string | null;
let enabled: boolean, workspaceActive: boolean, hideWithActivity: boolean, preserveInitial: boolean, total: number;
let observed: ReturnType<typeof useActivityPlayback>, commits: string[], refused: string[];
let initialResolver: ((signal: AbortSignal) => Promise<{ activityExecutionId?: string | null; note?: string }>) | undefined;
const prepare = vi.fn<(entry: PlaybackOccurrence, target: unknown, operation: PlaybackOperation) => Promise<PreparedPlaybackSelection>>();
function response(position = 0, count = 5, owner = "run", upper = String(count + 5)): PlaybackPage {
  const start = Math.floor(position / 50) * 50;
  return { state: "available", reason: null, upperSequence: upper, retainedAfterSequence: "0", totalCount: String(count),
    selectedPosition: count ? String(position) : null, sequenceToken: `opaque-${upper}`, hasNewerHistory: false, readAt: "2026-11-14T06:00:00Z",
    coverage: { state: "captured", supportedKinds: ["ActivityCompleted"], capturedFromStart: true, hasGaps: false,
      firstSequence: "1", lastSequence: upper, settledSequence: upper, purgedThroughSequence: "0" },
    items: Array.from({ length: Math.max(0, Math.min(50, count - start)) }, (_, index) => {
      const p = start + index;
      return { position: String(p), sequence: String(p + 1), workflowExecutionId: owner, activityExecutionId: `${owner}-execution-${p}`,
        historyEventId: `${owner}-event-${p}`, observationKind: "ActivityCompleted", executableNodeId: `node-${p % 3}`,
        occurredAt: "2026-11-14T06:00:00Z", recordedAt: "2026-11-14T06:00:01Z",
        previousActivityExecutionId: p ? `${owner}-execution-${p - 1}` : null, nextActivityExecutionId: p < count - 1 ? `${owner}-execution-${p + 1}` : null };
    }) };
}
function Probe() {
  observed = useActivityPlayback({ context, workflowExecutionId: run, enabled, activityExecutionId: scope, prepareSelection: prepare, resolveInitialSelection: initialResolver, preserveInitialInspection: preserveInitial,
    onUnavailable: message => refused.push(message) });
  return <output>{observed.cursor?.activityExecutionId ?? "none"}</output>;
}
function render() {
  flushSync(() => root.render(<StudioWorkspaceTabProvider value={{ id: "tab", active: workspaceActive, location: "/run", isActive: () => workspaceActive,
    markPendingWork() {}, setTitle() {} }}><Activity mode={hideWithActivity && !workspaceActive ? "hidden" : "visible"}><Probe /></Activity></StudioWorkspaceTabProvider>));
}
async function settle() { for (let count = 0; count < 60; count++) await Promise.resolve(); render(); }
async function command(action: () => unknown) { flushSync(() => { void action(); }); await settle(); }
async function tick(ms: number) { await vi.advanceTimersByTimeAsync(ms); await settle(); }
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(finish => { resolve = finish; }); return { promise, resolve }; }

beforeEach(() => {
  vi.useFakeTimers(); read.mockReset(); prepare.mockReset(); commits = []; refused = [];
  run = "run"; scope = null; enabled = true; workspaceActive = true; hideWithActivity = false; preserveInitial = false; total = 5; initialResolver = undefined;
  context = { baseUrl: "https://playback.example" } as StudioEndpointContext;
  vi.spyOn(document, "hidden", "get").mockReturnValue(false);
  vi.spyOn(navigator, "onLine", "get").mockReturnValue(true);
  read.mockImplementation(async (_context, owner, request = {}) => {
    const position = request.position !== undefined ? Number(request.position)
      : request.anchorActivityExecutionId ? Number(request.anchorActivityExecutionId.split("-").at(-1)) : 0;
    const count = request.window?.totalCount == null ? total : Number(request.window.totalCount);
    return response(position, count, owner, request.window?.upperSequence);
  });
  prepare.mockImplementation(async entry => ({ commit: () => commits.push(entry.activityExecutionId) }));
  container = document.createElement("div"); document.body.append(container); root = createRoot(container);
});
afterEach(() => { flushSync(() => root.unmount()); container.remove(); vi.restoreAllMocks(); vi.useRealTimers(); });

it("opens paused and confirms one exact occurrence after owner revalidation", async () => {
  render(); await settle();
  expect(observed.cursor?.activityExecutionId).toBe("run-execution-0"); expect(observed.playing).toBe(false);
  expect(commits).toEqual(["run-execution-0"]); expect(read).toHaveBeenCalledTimes(2);
  expect(read.mock.calls[1][2]).toMatchObject({ position: "0", window: { sequenceToken: "opaque-10" } });
});
it("anchors an existing inspection only at fresh admission and retains the cursor when its workspace returns", async () => {
  initialResolver = vi.fn(async () => ({ activityExecutionId: "run-execution-2" })); hideWithActivity = true;
  render(); await settle(); expect(observed.cursor?.position).toBe("2");
  await command(observed.next); expect(observed.cursor?.position).toBe("3");
  workspaceActive = false; render(); await settle(); workspaceActive = true; render(); await settle();
  expect(observed.cursor?.position).toBe("3"); expect(initialResolver).toHaveBeenCalledTimes(1); expect(observed.playing).toBe(false);
});
it("retains an eligible newer selection when a deep-linked run changes activity scope", async () => {
  initialResolver = vi.fn(async () => ({ activityExecutionId: "run-execution-1" }));
  render(); await settle(); expect(observed.cursor?.position).toBe("1");
  await command(observed.next); expect(observed.cursor?.position).toBe("2");
  scope = "run-parent"; render(); await settle();
  expect(observed.cursor?.position).toBe("2"); expect(initialResolver).toHaveBeenCalledTimes(1);
});
it("aligns inspection with the first scoped occurrence when the former deep-linked child is excluded", async () => {
  preserveInitial = true; initialResolver = vi.fn(async () => ({ activityExecutionId: "run-execution-1" }));
  render(); await settle(); await command(observed.next); expect(observed.cursor?.position).toBe("2");
  read.mockImplementation(async (_context, owner, request = {}) => request.anchorActivityExecutionId === "run-execution-2"
    ? { ...response(0, 1), state: "target-unavailable", reason: "anchor-outside-scope", items: [], totalCount: null, selectedPosition: null, sequenceToken: null }
    : response(0, 1, owner));
  scope = "parent-only"; render(); await settle();
  expect(observed.cursor?.activityExecutionId).toBe("run-execution-0"); expect(observed.note).toContain("outside this playback scope");
  expect(prepare.mock.calls.at(-1)?.[1]).toMatchObject({ preserveSelection: false });
});
it.each(["__proto__", "constructor", "toString"])("keeps an unknown inherited-key boundary reason safe (%s)", async reason => {
  read.mockResolvedValueOnce({ ...response(), state: "unavailable", reason, items: [], totalCount: null, selectedPosition: null, sequenceToken: null });
  render(); await settle(); expect(typeof observed.error).toBe("string"); expect(observed.error).toContain("unavailable in this range");
});
it("steps forward/reverse exactly once and never wraps boundaries", async () => {
  total = 3; render(); await settle(); await command(observed.previous); expect(commits).toHaveLength(1);
  await command(observed.next); await command(observed.next); await command(observed.next);
  expect(commits).toEqual(["run-execution-0", "run-execution-1", "run-execution-2"]); expect(observed.phase).toBe("ended");
  await command(observed.previous); expect(observed.cursor?.position).toBe("1"); expect(observed.playing).toBe(false);
});
it.each([["0.5", 2000], ["1", 1000], ["2", 500], ["4", 250]] as const)("visits every step at %s× without skipping", async (speed, interval) => {
  total = 4; render(); await settle(); await command(() => observed.setSpeed(speed)); await command(observed.play);
  await tick(interval - 1); expect(commits).toHaveLength(1);
  await tick(1); expect(observed.cursor?.position).toBe("1");
  await tick(interval); await tick(interval); expect(commits).toEqual([0, 1, 2, 3].map(p => `run-execution-${p}`));
  expect(observed.playing).toBe(false); expect(observed.phase).toBe("ended");
});
it("pauses synchronously before an already-started automatic read can confirm", async () => {
  render(); await settle(); await command(observed.play);
  vi.advanceTimersByTime(1000); flushSync(() => observed.pause()); await settle();
  await tick(10000); expect(commits).toEqual(["run-execution-0"]); expect(observed.cursor?.position).toBe("0");
});
it("does not catch up or overlap ticks while preparation is buffered", async () => {
  render(); await settle(); const held = deferred<PreparedPlaybackSelection>(); prepare.mockReturnValueOnce(held.promise);
  await command(observed.play); await tick(1000);
  expect(observed.phase).toBe("buffering"); expect(observed.pending?.position).toBe("1"); expect(observed.cursor?.position).toBe("0");
  await tick(10000); expect(prepare).toHaveBeenCalledTimes(2); expect(commits).toHaveLength(1);
  held.resolve({ commit: () => commits.push("run-execution-1") }); await settle();
  expect(observed.cursor?.position).toBe("1"); await tick(999); expect(commits).toHaveLength(2);
  await tick(1); expect(observed.cursor?.position).toBe("2");
});
it("keeps a pause during buffering final even if the old preparation ignores cancellation", async () => {
  render(); await settle(); const held = deferred<PreparedPlaybackSelection>(); prepare.mockReturnValueOnce(held.promise);
  await command(observed.play); await tick(1000); const operation = prepare.mock.calls[1][2];
  await command(observed.pause); expect(operation.signal.aborted).toBe(true);
  held.resolve({ commit: () => commits.push("obsolete") }); await settle(); await tick(10000);
  expect(commits).toEqual(["run-execution-0"]); expect(observed.playing).toBe(false);
});
it("changes future pace during buffering without cancelling its exact pending step", async () => {
  render(); await settle(); const held = deferred<PreparedPlaybackSelection>(); prepare.mockReturnValueOnce(held.promise);
  await command(observed.play); await tick(1000); await command(() => observed.setSpeed("4"));
  expect(prepare.mock.calls[1][2].signal.aborted).toBe(false);
  held.resolve({ commit: () => commits.push("run-execution-1") }); await settle();
  await tick(249); expect(observed.cursor?.position).toBe("1"); await tick(1); expect(observed.cursor?.position).toBe("2");
});
it("treats repeated Play/same-speed actions as idempotent and requires explicit Replay at the end", async () => {
  total = 2; render(); await settle(); await command(observed.play); await tick(500);
  await command(observed.play); await command(() => observed.setSpeed("1")); await tick(500);
  expect(commits).toEqual(["run-execution-0", "run-execution-1"]);
  await command(observed.play); await tick(5000); expect(commits).toHaveLength(2);
  await command(observed.replay); expect(observed.cursor?.position).toBe("0"); expect(observed.playing).toBe(true);
});
it("manual exact selection/seek pauses and retains the specifically inspected event intent", async () => {
  render(); await settle(); await command(observed.play);
  await command(() => observed.selectOccurrence("run-execution-3", "run-later-event"));
  expect(observed.cursor?.position).toBe("3"); expect(observed.playing).toBe(false);
  expect(prepare.mock.calls.at(-1)?.[1]).toEqual({ activityExecutionId: "run-execution-3", historyEventId: "run-later-event", rememberChoice: true });
  await tick(5000); expect(observed.cursor?.position).toBe("3");
});
it("preserves the inspector only on initial admission and keeps explicit Activity scope intent", async () => {
  render(); await settle(); expect(prepare.mock.calls[0][1]).toEqual({ preserveInspector: true, preserveSelection: false, rememberChoice: false });
  await command(() => observed.inspectOccurrence("run-execution-2", true));
  expect(prepare.mock.calls.at(-1)?.[1]).toEqual({ activityExecutionId: "run-execution-2", inspectActivity: true, includeSelfScope: true, rememberChoice: true });
  expect(observed.cursor?.position).toBe("2"); expect(observed.playing).toBe(false);
});
it("retains the frozen cursor for a newer manual selection until explicit refresh admits it", async () => {
  render(); await settle(); const original = observed.window;
  read.mockResolvedValueOnce({ ...response(), state: "target-unavailable", reason: "anchor-outside-range", items: [],
    totalCount: null, selectedPosition: null, sequenceToken: null });
  let result: boolean | undefined;
  await command(async () => { result = await observed.selectOccurrence("run-execution-5", "new-event"); });
  expect(result).toBe(false); expect(observed.window).toEqual(original); expect(observed.cursor?.position).toBe("0");
  expect(observed.outsideSelection).toBe("run-execution-5"); expect(commits).toEqual(["run-execution-0"]);
  total = 6; await command(observed.refresh);
  expect(observed.cursor?.position).toBe("5"); expect(observed.window?.totalCount).toBe("6"); expect(observed.outsideSelection).toBeNull();
  expect(read.mock.calls.at(-2)?.[2]).toEqual({ activityExecutionId: null, includeDescendants: undefined, anchorActivityExecutionId: "run-execution-5" });
});
it("pauses offline, refuses new transport reads and does not resume on reconnect", async () => {
  render(); await settle(); await command(observed.play);
  vi.spyOn(navigator, "onLine", "get").mockReturnValue(false);
  flushSync(() => window.dispatchEvent(new Event("offline"))); await settle(); const count = read.mock.calls.length;
  await command(observed.play); await command(observed.next); await tick(10000);
  expect(read).toHaveBeenCalledTimes(count); expect(observed.playing).toBe(false); expect(observed.note).toContain("offline");
  vi.spyOn(navigator, "onLine", "get").mockReturnValue(true); flushSync(() => window.dispatchEvent(new Event("online"))); await settle();
  await tick(1000); expect(commits).toEqual(["run-execution-0"]); expect(observed.playing).toBe(false);
  await command(observed.next); expect(observed.cursor?.position).toBe("1");
});
it("seeks directly across a 200-occurrence range without walking intervening pages", async () => {
  total = 200; render(); await settle(); read.mockClear();
  for (const position of ["100", "199", "49", "0"]) await command(() => observed.seek(position));
  expect(observed.cursor?.position).toBe("0"); expect(read).toHaveBeenCalledTimes(8);
  expect(read.mock.calls.map(call => call[2]?.position)).toEqual(["100", "100", "199", "199", "49", "49", "0", "0"]);
  expect(read.mock.calls.every(call => call[2]?.window?.sequenceToken === "opaque-205")).toBe(true);
});
it("confirms only the final of ten cancellation-resistant rapid seeks", async () => {
  total = 200; render(); await settle(); const held = Array.from({ length: 10 }, () => deferred<PlaybackPage>());
  for (let i = 0; i < held.length; i++) {
    read.mockReturnValueOnce(held[i].promise); void observed.seek(String(100 + i)); await settle();
  }
  expect(observed.cursor?.position).toBe("0"); expect(observed.pending?.position).toBe("109");
  held[9].resolve(response(109, 200, "run", "205")); await settle(); expect(observed.cursor?.position).toBe("109");
  for (let i = 8; i >= 0; i--) held[i].resolve(response(100 + i, 200, "run", "205"));
  await settle(); expect(commits).toEqual(["run-execution-0", "run-execution-109"]);
});
it("keeps failed target separate and retries that target while staying paused", async () => {
  render(); await settle(); read.mockRejectedValueOnce({ status: 503 }); await command(() => observed.seek("3"));
  expect(observed.phase).toBe("failed"); expect(observed.cursor?.position).toBe("0"); expect(observed.pending?.position).toBe("3");
  await command(observed.retry); expect(observed.cursor?.position).toBe("3"); expect(observed.playing).toBe(false);
});
it("retries unresolved initial deep-link admission at its requested owner and preserves inspection", async () => {
  const resolve = vi.fn().mockRejectedValueOnce({ status: 503 }).mockResolvedValue({ activityExecutionId: "run-execution-2" });
  initialResolver = resolve; preserveInitial = true; render(); await settle(); expect(observed.phase).toBe("failed");
  await command(observed.retry); expect(observed.cursor?.position).toBe("2"); expect(resolve).toHaveBeenCalledTimes(2);
  expect(prepare.mock.calls.at(-1)?.[1]).toMatchObject({ preserveInspector: true, preserveSelection: true }); expect(observed.playing).toBe(false);
});
it("retries a failed explicit range refresh as fresh paused admission", async () => {
  render(); await settle(); total = 6; read.mockRejectedValueOnce({ status: 503 }); await command(observed.refresh);
  expect(observed.phase).toBe("failed"); expect(observed.window?.totalCount).toBe("5"); read.mockClear();
  await command(observed.retry); expect(observed.window?.totalCount).toBe("6"); expect(read.mock.calls[0][2]?.window).toBeUndefined();
  expect(observed.cursor?.position).toBe("0"); expect(observed.playing).toBe(false);
});
it.each(["manual", "background", "offline", "workspace"])("keeps failed seek recovery through a %s pause", async reason => {
  render(); await settle(); read.mockRejectedValueOnce({ status: 503 }); await command(() => observed.seek("3"));
  if (reason === "manual") await command(observed.pause);
  if (reason === "background") {
    vi.spyOn(document, "hidden", "get").mockReturnValue(true); flushSync(() => document.dispatchEvent(new Event("visibilitychange"))); await settle();
    vi.spyOn(document, "hidden", "get").mockReturnValue(false); flushSync(() => document.dispatchEvent(new Event("visibilitychange"))); await settle();
  }
  if (reason === "offline") {
    vi.spyOn(navigator, "onLine", "get").mockReturnValue(false); flushSync(() => window.dispatchEvent(new Event("offline"))); await settle();
    vi.spyOn(navigator, "onLine", "get").mockReturnValue(true); flushSync(() => window.dispatchEvent(new Event("online"))); await settle();
  }
  if (reason === "workspace") {
    hideWithActivity = true; workspaceActive = false; render(); await settle(); workspaceActive = true; render(); await settle();
  }
  expect(observed.phase).toBe("failed"); expect(observed.pending?.position).toBe("3");
  await command(observed.retry); expect(observed.cursor?.position).toBe("3"); expect(observed.playing).toBe(false);
});
it("revalidates a failed cursor without replacing its requested recovery target on transient or successful observations", async () => {
  render(); await settle(); read.mockRejectedValueOnce({ status: 503 }); await command(() => observed.seek("3")); read.mockClear();
  read.mockRejectedValueOnce({ status: 503 }); await tick(5000);
  expect(read).toHaveBeenCalledTimes(1); expect(observed.phase).toBe("failed"); expect(observed.pending?.position).toBe("3");
  await tick(5000); expect(observed.phase).toBe("failed"); expect(observed.pending?.position).toBe("3");
  await command(observed.retry); expect(observed.cursor?.position).toBe("3"); expect(observed.playing).toBe(false);
});
it.each([401, 403, 404, 409])("clears the cursor/count and never commits after final owner refusal (%s)", async status => {
  render(); await settle(); prepare.mockImplementationOnce(async entry => {
    read.mockRejectedValueOnce({ status }); return { commit: () => commits.push(entry.activityExecutionId) };
  });
  await command(() => observed.seek("2")); expect(observed.phase).toBe("unavailable");
  expect(observed.cursor).toBeNull(); expect(observed.window).toBeNull(); expect(observed.page).toBeNull();
  expect(commits).toEqual(["run-execution-0"]); expect(refused).toHaveLength(1);
});
it("clears prior protected inspection when scope re-admission is refused after retiring its old cursor", async () => {
  render(); await settle(); read.mockRejectedValueOnce({ status: 403 });
  scope = "new-scope"; render(); await settle();
  expect(observed.cursor).toBeNull(); expect(observed.window).toBeNull(); expect(refused).toHaveLength(1);
});
it("keeps live arrivals outside its frozen bound until explicit paused refresh", async () => {
  render(); await settle(); const original = observed.window;
  read.mockImplementation(async (_context, owner, request = {}) => {
    const frozen = request.window; const result = response(Number(request.position ?? request.anchorActivityExecutionId?.split("-").at(-1) ?? 0), frozen ? 5 : 6, owner, frozen?.upperSequence);
    return { ...result, hasNewerHistory: !!frozen, coverage: { ...result.coverage, lastSequence: "11" } };
  });
  await tick(5000); expect(observed.newer).toBe(true); expect(observed.window).toEqual(original); expect(observed.cursor?.position).toBe("0");
  await command(observed.refresh); expect(observed.window?.totalCount).toBe("6"); expect(observed.cursor?.position).toBe("0"); expect(observed.playing).toBe(false);
});
it("observes new history for an initially empty frozen range without implicitly admitting its first activity", async () => {
  total = 0; render(); await settle(); expect(observed.cursor).toBeNull(); expect(observed.window?.totalCount).toBe("0");
  total = 1; read.mockImplementation(async (_context, owner, request = {}) => ({
    ...response(0, request.window ? Number(request.window.totalCount) : total, owner, request.window?.upperSequence), hasNewerHistory: request.window?.totalCount === "0" }));
  await tick(5000); expect(observed.newer).toBe(true); expect(observed.cursor).toBeNull(); expect(commits).toEqual([]);
  await command(observed.refresh); expect(observed.cursor?.position).toBe("0"); expect(observed.window?.totalCount).toBe("1"); expect(observed.playing).toBe(false);
});
it("stops at an invalidated/gapped range instead of admitting a new implicit segment", async () => {
  render(); await settle(); await command(observed.play);
  read.mockResolvedValueOnce({ ...response(), state: "range-invalidated", reason: "retained-range-changed", items: [], totalCount: null, selectedPosition: null, sequenceToken: null });
  await tick(1000); expect(observed.phase).toBe("unavailable"); expect(observed.playing).toBe(false); expect(observed.cursor).toBeNull();
  expect(observed.error).toContain("retained activity range changed"); expect(commits).toHaveLength(1);
});
it("keeps authorized occurrences usable when the pinned diagram is unavailable", async () => {
  prepare.mockImplementation(async entry => ({ commit: () => commits.push(entry.activityExecutionId), limitation: "Pinned diagram unavailable; recorded occurrence remains inspectable." }));
  render(); await settle(); expect(observed.cursor?.position).toBe("0"); expect(observed.diagramLimitation).toContain("Pinned diagram unavailable");
});
it("retires old run/backend/scope reads before their delayed completion", async () => {
  render(); await settle(); const held = deferred<PlaybackPage>(); read.mockReturnValueOnce(held.promise);
  void observed.seek("3"); await settle(); const signal = read.mock.calls.at(-1)?.[3];
  run = "other"; context = { baseUrl: "https://other.example" } as StudioEndpointContext; scope = "other-parent"; render(); await settle();
  expect(signal?.aborted).toBe(true); expect(observed.cursor?.workflowExecutionId).toBe("other");
  held.resolve(response(3)); await settle(); expect(commits).not.toContain("run-execution-3");
});
it("drops protected cursor/reads on session end and admits only the new session", async () => {
  render(); await settle(); const held = deferred<PlaybackPage>(); read.mockReturnValueOnce(held.promise);
  void observed.seek("3"); await settle(); flushSync(() => window.dispatchEvent(new Event(authSessionEndedEvent))); await settle();
  expect(observed.cursor).toBeNull(); expect(observed.error).toContain("Sign in again");
  held.resolve(response(3)); await settle(); expect(commits).toEqual(["run-execution-0"]);
  flushSync(() => window.dispatchEvent(new Event(authSessionStartedEvent))); await settle(); expect(observed.cursor?.position).toBe("0");
});
it("pauses for document backgrounding with no resume or catch-up", async () => {
  render(); await settle(); await command(observed.play);
  vi.spyOn(document, "hidden", "get").mockReturnValue(true); flushSync(() => document.dispatchEvent(new Event("visibilitychange"))); await settle();
  await tick(60000); vi.spyOn(document, "hidden", "get").mockReturnValue(false); flushSync(() => document.dispatchEvent(new Event("visibilitychange"))); await settle();
  await tick(5000); expect(commits).toEqual(["run-execution-0"]); expect(observed.playing).toBe(false);
});
it("checks the workspace's current getter before a pending confirmation can write", async () => {
  render(); await settle(); const held = deferred<PreparedPlaybackSelection>(); prepare.mockReturnValueOnce(held.promise);
  void observed.seek("3"); await settle(); workspaceActive = false;
  held.resolve({ commit: () => commits.push("hidden") }); await settle(); expect(commits).toEqual(["run-execution-0"]);
});
it("tears down playback in a retained React Activity and revalidates the same frozen window on return", async () => {
  hideWithActivity = true; render(); await settle(); const original = observed.window;
  await command(observed.play); workspaceActive = false; render(); await settle(); await tick(60000);
  workspaceActive = true; render(); await settle(); expect(observed.playing).toBe(false); expect(observed.window).toEqual(original);
  expect(observed.cursor?.position).toBe("0"); expect(read.mock.calls.at(-1)?.[2]?.window).toEqual(original);
});
it("pauses a background workspace even when its host keeps the component mounted without React Activity", async () => {
  render(); await settle(); const held = deferred<PreparedPlaybackSelection>(); prepare.mockReturnValueOnce(held.promise);
  await command(observed.play); await tick(1000); workspaceActive = false; render(); await settle();
  expect(observed.playing).toBe(false); expect(observed.pending).toBeNull();
  held.resolve({ commit: () => commits.push("obsolete-background") }); await settle();
  workspaceActive = true; render(); await settle(); await tick(5000);
  expect(commits.every(id => id === "run-execution-0")).toBe(true); expect(observed.cursor?.position).toBe("0"); expect(observed.playing).toBe(false);
});
