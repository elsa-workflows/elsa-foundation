import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { authSessionEndedEvent, authSessionStartedEvent, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { RuntimeHistoryPage, RuntimeHistoryRequest } from "../runtimeHistoryTypes";
import { mergeHistoryEvents, useRunHistory } from "../workflow-editor/run-history/useRunHistory";
import { historyEvent, historyPage } from "./runtimeHistoryFixtures";
const api = vi.hoisted(() => ({ getWorkflowHistoryPage: vi.fn(), supportsWorkflowHistory: vi.fn() }));
vi.mock("../api/runtimeHistory", async original => ({ ...await original<typeof import("../api/runtimeHistory")>(), ...api }));
const context = { baseUrl: "https://history.example" } as StudioEndpointContext;
let root: Root, container: HTMLDivElement;
let request: RuntimeHistoryRequest;
let observed: ReturnType<typeof useRunHistory>;
function Probe() { observed = useRunHistory(context, "run", request); return null; }
function render() { flushSync(() => root.render(<Probe />)); }
async function settle() { for (let i = 0; i < 30; i++) await Promise.resolve(); render(); }
beforeEach(() => {
  vi.useFakeTimers(); vi.resetAllMocks(); request = {};
  vi.spyOn(document, "hidden", "get").mockReturnValue(false); vi.spyOn(navigator, "onLine", "get").mockReturnValue(true);
  api.supportsWorkflowHistory.mockResolvedValue(true); api.getWorkflowHistoryPage.mockResolvedValue(historyPage());
  container = document.createElement("div"); document.body.append(container); root = createRoot(container);
});
afterEach(() => { flushSync(() => root.unmount()); container.remove(); vi.restoreAllMocks(); vi.useRealTimers(); });

it("continues bounded catch-up after a settled prefix and queues late events without replacing the reader's rows", async () => {
  render(); await settle();
  api.getWorkflowHistoryPage.mockResolvedValue(historyPage([historyEvent(4, { name: "DeliveryReceived", authority: "DeliveryObservation" })],
    { upperSequence: 4, scannedThroughSequence: 4, catchUpToken: "tail-4" }));
  await vi.advanceTimersByTimeAsync(2000); await settle();
  expect(observed.value?.items.map(item => item.historyEventId)).toEqual(["event-1"]);
  expect(observed.value?.pending.map(item => item.historyEventId)).toEqual(["event-4"]);
  expect(api.getWorkflowHistoryPage.mock.calls[1][2].continuationToken).toBe("tail-3");
  await vi.advanceTimersByTimeAsync(2000); await settle(); expect(api.getWorkflowHistoryPage).toHaveBeenCalledTimes(3);
  flushSync(() => observed.showNew()); await settle(); expect(observed.value?.items.map(item => item.sequence)).toEqual([1, 4]);
});
it("accepts empty searched windows with an advancing next cursor and retains incomplete ancestry across later pages", async () => {
  api.getWorkflowHistoryPage.mockResolvedValueOnce(historyPage([], { nextContinuationToken: "window-2", scopeComplete: false, scopeLimitation: "Ancestry unavailable" }))
    .mockResolvedValueOnce(historyPage([historyEvent(2)], { scopeComplete: true }));
  request = { search: "bookmark" }; render(); await settle();
  expect(observed.value?.items).toEqual([]); expect(observed.value?.next).toBe("window-2");
  flushSync(() => observed.loadMore()); await settle();
  expect(api.getWorkflowHistoryPage.mock.calls[1][2].continuationToken).toBe("window-2"); expect(observed.value?.items[0].sequence).toBe(2);
  expect(observed.value?.page.scopeComplete).toBe(false); expect(observed.value?.page.scopeLimitation).toBe("Ancestry unavailable");
});
it("retains readable rows and catch-up position after a transient failure", async () => {
  render(); await settle(); api.getWorkflowHistoryPage.mockRejectedValueOnce(new Error("temporary read failure"));
  await vi.advanceTimersByTimeAsync(2000); await settle();
  expect(observed.status).toBe("stale"); expect(observed.value?.items).toHaveLength(1); expect(observed.error).toBe("temporary read failure");
  api.getWorkflowHistoryPage.mockResolvedValue(historyPage([])); await vi.advanceTimersByTimeAsync(4000); await settle();
  expect(observed.status).toBe("ready"); expect(observed.value?.items).toHaveLength(1); expect(api.getWorkflowHistoryPage.mock.calls[2][2].continuationToken).toBe("tail-3");
});
it("drops denied rows immediately and does not retry a forbidden range automatically", async () => {
  render(); await settle(); api.getWorkflowHistoryPage.mockRejectedValueOnce(Object.assign(new Error("forbidden"), { status: 403 }));
  await vi.advanceTimersByTimeAsync(2000); await settle(); expect(observed.status).toBe("denied"); expect(observed.value).toBeNull();
  await vi.advanceTimersByTimeAsync(10000); expect(api.getWorkflowHistoryPage).toHaveBeenCalledTimes(2);
});
it("clears protected history on sign-out and waits for a fresh authorized read after sign-in", async () => {
  render(); await settle(); flushSync(() => window.dispatchEvent(new Event(authSessionEndedEvent))); await settle();
  expect(observed.value).toBeNull(); expect(observed.status).toBe("denied");
  let complete!: (page: RuntimeHistoryPage) => void;
  api.getWorkflowHistoryPage.mockImplementationOnce(() => new Promise(resolve => { complete = resolve; }));
  flushSync(() => window.dispatchEvent(new Event(authSessionStartedEvent))); await settle(); expect(observed.value).toBeNull();
  complete(historyPage([historyEvent(2)])); await settle(); expect(observed.value?.items[0].sequence).toBe(2);
});
it("rejects an obsolete scoped response and reads a new scope even while live updates are paused", async () => {
  let oldComplete!: (page: RuntimeHistoryPage) => void;
  api.getWorkflowHistoryPage.mockImplementationOnce(() => new Promise(resolve => { oldComplete = resolve; }));
  render(); await settle(); const oldSignal = api.getWorkflowHistoryPage.mock.calls[0][3];
  api.getWorkflowHistoryPage.mockResolvedValue(historyPage([historyEvent(2, { activityExecutionId: "activity-2" })]));
  request = { activityExecutionId: "activity-2" }; render(); await settle();
  expect(oldSignal.aborted).toBe(true); expect(observed.value?.items[0].activityExecutionId).toBe("activity-2");
  oldComplete(historyPage()); await settle(); expect(observed.value?.items[0].activityExecutionId).toBe("activity-2");
  flushSync(() => observed.setFollowing(false)); api.getWorkflowHistoryPage.mockResolvedValue(historyPage([historyEvent(3, { activityExecutionId: "activity-3" })]));
  request = { activityExecutionId: "activity-3" }; render(); await settle();
  expect(observed.value?.items[0].activityExecutionId).toBe("activity-3"); expect(observed.paused).toBe(true);
});
it("reports a genuinely unsupported host without claiming an empty captured range", async () => {
  api.supportsWorkflowHistory.mockResolvedValue(false); render(); await settle();
  expect(observed.status).toBe("unsupported"); expect(observed.value).toBeNull(); expect(api.getWorkflowHistoryPage).not.toHaveBeenCalled();
});
it("aborts a live read when hidden and resumes the same catch-up cursor without concurrent requests", async () => {
  render(); await settle(); let complete!: (page: RuntimeHistoryPage) => void;
  api.getWorkflowHistoryPage.mockImplementationOnce(() => new Promise(resolve => { complete = resolve; }));
  await vi.advanceTimersByTimeAsync(2000); await settle(); const signal = api.getWorkflowHistoryPage.mock.calls[1][3];
  vi.spyOn(document, "hidden", "get").mockReturnValue(true); flushSync(() => document.dispatchEvent(new Event("visibilitychange"))); await settle();
  expect(signal.aborted).toBe(true); expect(observed.paused).toBe(true); expect(observed.loading).toBe(false);
  complete(historyPage([historyEvent(4)])); await settle(); expect(observed.value?.items).toHaveLength(1);
  await vi.advanceTimersByTimeAsync(10000); expect(api.getWorkflowHistoryPage).toHaveBeenCalledTimes(2);
  vi.spyOn(document, "hidden", "get").mockReturnValue(false); flushSync(() => document.dispatchEvent(new Event("visibilitychange"))); await settle();
  expect(api.getWorkflowHistoryPage.mock.calls[2][2].continuationToken).toBe("tail-3");
});
it("keeps the original unloaded snapshot boundary when live pages are shown", async () => {
  api.getWorkflowHistoryPage.mockResolvedValueOnce(historyPage([historyEvent(1)], { upperSequence: 5, scannedThroughSequence: 1, nextContinuationToken: "snapshot-more", catchUpToken: "after-5" }));
  render(); await settle(); api.getWorkflowHistoryPage.mockResolvedValue(historyPage([historyEvent(6)], { upperSequence: 6, scannedThroughSequence: 6, catchUpToken: "after-6" }));
  await vi.advanceTimersByTimeAsync(2000); await settle(); flushSync(() => observed.showNew()); await settle();
  expect(observed.value?.snapshotUpperSequence).toBe(5); expect(observed.value?.snapshotScannedThroughSequence).toBe(1);
  expect(observed.value?.next).toBe("snapshot-more"); expect(observed.value?.items.map(item => item.sequence)).toEqual([1, 6]); expect(observed.value?.page.upperSequence).toBe(6);
});
it("invalidates contradictory event identities across separately loaded windows", async () => {
  api.getWorkflowHistoryPage.mockResolvedValueOnce(historyPage([historyEvent(1)], { nextContinuationToken: "more" }))
    .mockResolvedValueOnce(historyPage([historyEvent(1, { historyEventId: "contradictory-event" })]));
  render(); await settle(); flushSync(() => observed.loadMore()); await settle();
  expect(observed.status).toBe("unavailable"); expect(observed.value).toBeNull(); expect(observed.error).toContain("same recorded position");
});
it("compares immutable common event content while accepting equivalent nullable fields and reordered metadata", () => {
  const original = historyEvent(1, { recordedAt: null, causation: { deliveryId: "delivery" }, metadata: { "history.status": "Waiting", "history.reasonCode": "wait" } });
  const equivalent = historyEvent(1, { recordedAt: undefined, causation: { deliveryId: "delivery", historyEventId: null }, metadata: { "history.reasonCode": "wait", "history.status": "Waiting" } });
  expect(mergeHistoryEvents([original], [equivalent])).toHaveLength(1);
  expect(() => mergeHistoryEvents([original], [{ ...equivalent, authority: "DeliveryObservation" }])).toThrow("changed its recorded content");
  expect(() => mergeHistoryEvents([original], [{ ...equivalent, metadata: { "history.status": "Completed" } }])).toThrow("changed its recorded content");
});
it("rejects changed content in a live duplicate even when that event is already among the reader's loaded rows", async () => {
  render(); await settle(); api.getWorkflowHistoryPage.mockResolvedValueOnce(historyPage([historyEvent(1, { name: "BookmarkConsumed" })]));
  await vi.advanceTimersByTimeAsync(2000); await settle();
  expect(observed.status).toBe("unavailable"); expect(observed.value).toBeNull(); expect(observed.error).toContain("changed its recorded content");
});


it("refreshes from catch-up while paused without discarding manually loaded snapshot rows or its remaining cursor", async () => {
  api.getWorkflowHistoryPage.mockResolvedValueOnce(historyPage([historyEvent(1)], { nextContinuationToken: "snapshot-2", catchUpToken: "after-5", upperSequence: 5 }))
    .mockResolvedValueOnce(historyPage([historyEvent(2)], { nextContinuationToken: "snapshot-3", catchUpToken: "after-5", upperSequence: 5 }))
    .mockResolvedValueOnce(historyPage([historyEvent(6)], { upperSequence: 6, catchUpToken: "after-6" }));
  render(); await settle(); flushSync(() => observed.loadMore()); await settle();
  flushSync(() => observed.setFollowing(false)); await settle(); flushSync(() => observed.refresh()); await settle();
  expect(api.getWorkflowHistoryPage.mock.calls[2][2].continuationToken).toBe("after-5");
  expect(observed.value?.items.map(item => item.sequence)).toEqual([1, 2]); expect(observed.value?.pending.map(item => item.sequence)).toEqual([6]);
  expect(observed.value?.next).toBe("snapshot-3"); expect(observed.value?.snapshotUpperSequence).toBe(5); expect(observed.paused).toBe(true);
});
