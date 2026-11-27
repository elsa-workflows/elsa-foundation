import { afterEach, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { clearApiCapabilityCache } from "../api/capabilities";
import { getWorkflowHistoryPage, getWorkflowHistoryStream, getWorkflowHistoryEvent, getWorkflowHistoryBookmark, listWorkflowHistoryBookmarks, RuntimeHistoryIdentityError } from "../api/runtimeHistory";
import { historyBookmark, historyCoverage, historyEvent, historyPage } from "./runtimeHistoryFixtures";
const links = [
  { rel: "workflow-history-page", href: "runtime/workflows/instances/{workflowExecutionId}/history" },
  { rel: "workflow-history-event", href: "runtime/workflows/instances/{workflowExecutionId}/history/events/{historyEventId}" },
  { rel: "workflow-history-bookmarks", href: "runtime/workflows/instances/{workflowExecutionId}/history/bookmarks" },
  { rel: "workflow-history-bookmark", href: "runtime/workflows/instances/{workflowExecutionId}/history/bookmarks/{bookmarkId}" }
];
function setup(value: unknown, available = links) {
  const getJson = vi.fn().mockResolvedValueOnce({ capabilities: [{ id: "elsa.api.runtime", contractVersion: "1", links: available }] }).mockResolvedValue(value);
  return { getJson, context: { baseUrl: "https://host.example/shell", http: { getJson } } as unknown as StudioEndpointContext };
}
afterEach(clearApiCapabilityCache);
it("uses capability templates, bound query filters and the cursor wire name", async () => {
  const { context, getJson } = setup(historyPage()); const signal = new AbortController().signal;
  await getWorkflowHistoryPage(context, "run", { continuationToken: "opaque+/=", activityExecutionId: "owner", includeDescendants: true, search: "x & y", category: "BookmarkLifecycle", limit: 50 }, signal);
  const [path, options] = getJson.mock.calls[1]; const url = new URL(path, "https://host.example");
  expect(url.pathname).toBe("/shell/runtime/workflows/instances/run/history");
  expect(Object.fromEntries(url.searchParams)).toEqual({ limit: "50", cursor: "opaque+/=", activityExecutionId: "owner", includeDescendants: "true", search: "x & y", category: "BookmarkLifecycle" });
  expect(options).toEqual({ signal });
});
it("never guesses history paths on an older host", async () => {
  const { context, getJson } = setup(null, []);
  await expect(getWorkflowHistoryPage(context, "run")).rejects.toMatchObject({ name: "ApiCapabilityUnavailableError" });
  expect(getJson).toHaveBeenCalledTimes(1);
});
it.each([
  historyPage([historyEvent(1, { workflowExecutionId: "other" })]),
  historyPage([historyEvent(2), historyEvent(1)]),
  historyPage([historyEvent(5)], { upperSequence: 4 })
])("rejects foreign or inconsistently ordered event pages", async page => {
  const { context } = setup(page); await expect(getWorkflowHistoryPage(context, "run")).rejects.toBeInstanceOf(RuntimeHistoryIdentityError);
});
it("validates exact event and bookmark identities without loading any payload", async () => {
  const { context, getJson } = setup({ coverage: historyCoverage, event: historyEvent(1, { historyEventId: "other" }), readAt: "now" });
  await expect(getWorkflowHistoryEvent(context, "run", "wanted")).rejects.toBeInstanceOf(RuntimeHistoryIdentityError);
  getJson.mockResolvedValue({ coverage: historyCoverage, bookmark: historyBookmark(), readAt: "now" });
  await getWorkflowHistoryBookmark(context, "run", "bookmark-1");
  expect(getJson.mock.calls.map(call => call[0])).toContain("/shell/runtime/workflows/instances/run/history/bookmarks/bookmark-1");
  expect(getJson.mock.calls.some(call => String(call[0]).includes("payload"))).toBe(false);
});
it("rejects a bookmark owned by another exact execution", async () => {
  const { context } = setup({ coverage: historyCoverage, items: [historyBookmark()], scopeComplete: true, readAt: "now" });
  await expect(listWorkflowHistoryBookmarks(context, "run", { activityExecutionId: "different-owner" })).rejects.toBeInstanceOf(RuntimeHistoryIdentityError);
});
it("preserves safe common fields for an unknown event schema and authority", async () => {
  const event = historyEvent(1, { schemaVersion: 99, name: "FutureFact", category: "99", authority: "99" });
  const { context } = setup({ coverage: historyCoverage, event, readAt: "now" });
  await expect(getWorkflowHistoryEvent(context, "run", "event-1")).resolves.toMatchObject({ event });
});
it("preserves unknown occurrence separately from its durable recording time", async () => {
  const event = historyEvent(1, { occurredAt: null, occurredAtKnown: false, activityAncestryKnown: false });
  const { context } = setup({ coverage: historyCoverage, event, readAt: "now" });
  await expect(getWorkflowHistoryEvent(context, "run", "event-1")).resolves.toMatchObject({ event });
});
it.each([{ activityExecutionId: "other" }, { bookmarkId: "other" }])("refuses events outside an exact requested scope", async request => {
  const { context } = setup(historyPage());
  await expect(getWorkflowHistoryPage(context, "run", request)).rejects.toBeInstanceOf(RuntimeHistoryIdentityError);
});
it("uses each advertised relation independently when a legacy reader has no exact detail support", async () => {
  const { context, getJson } = setup(historyPage(), [links[0], links[2]]);
  await getWorkflowHistoryPage(context, "run");
  await expect(getWorkflowHistoryEvent(context, "run", "event-1")).rejects.toMatchObject({ name: "ApiCapabilityUnavailableError" });
  expect(getJson).toHaveBeenCalledTimes(2);
});
it.each(["page", "event", "bookmarks", "bookmark"] as const)("refuses explicit invalid coverage for %s without presenting confirmed content", async kind => {
  const { context } = setup({ coverage: { ...historyCoverage, state: "invalid" }, items: [], event: historyEvent(), bookmark: historyBookmark(), readAt: "now" });
  const reads = {
    page: () => getWorkflowHistoryPage(context, "run"),
    event: () => getWorkflowHistoryEvent(context, "run", "event-1"),
    bookmarks: () => listWorkflowHistoryBookmarks(context, "run"),
    bookmark: () => getWorkflowHistoryBookmark(context, "run", "bookmark-1")
  };
  await expect(reads[kind]()).rejects.toBeInstanceOf(RuntimeHistoryIdentityError);
});

it("times out a stalled history body and can reconnect with the last confirmed bookmark", async () => {
  vi.useFakeTimers();
  try {
    const { context } = setup(null, [...links,
      { rel: "workflow-history-live", href: "runtime/workflows/instances/{workflowExecutionId}/history/live" }]);
    const page = historyPage([historyEvent(4)], { upperSequence: 4, scannedThroughSequence: 4, catchUpToken: "after-4" });
    const request = vi.fn(async (_url: string, options: RequestInit) => {
      const body = new ReadableStream<Uint8Array>({
        start(controller) {
          controller.enqueue(new TextEncoder().encode(JSON.stringify({ page }) + "\n"));
          options.signal!.addEventListener("abort", () => controller.error(options.signal!.reason), { once: true });
        }
      });
      return new Response(body, { headers: { "content-type": "application/x-ndjson" } });
    });
    context.http.requestResponse = request;
    const signal = new AbortController().signal;
    const stream = getWorkflowHistoryStream(context, "run", { continuationToken: "after-3" }, signal);
    const first = await stream.next();
    expect(first.value).toEqual(page);
    const stalled = expect(stream.next()).rejects.toMatchObject({ name: "TimeoutError" });
    await vi.advanceTimersByTimeAsync(35_000);
    await stalled;
    expect(signal.aborted).toBe(false);
    const resumed = getWorkflowHistoryStream(context, "run", { continuationToken: first.value!.catchUpToken }, signal);
    await resumed.next();
    expect(new URL(request.mock.calls[1][0], context.baseUrl).searchParams.get("cursor")).toBe("after-4");
    await resumed.return(undefined);
  } finally {
    vi.useRealTimers();
  }
});

it("retries an authenticated history link acquisition that ignored cancellation", async () => {
  vi.useFakeTimers();
  try {
    const live = { rel: "workflow-history-live", href: "runtime/workflows/instances/{workflowExecutionId}/history/live" };
    const { context, getJson } = setup(null, [live]);
    getJson.mockReset().mockImplementationOnce(() => new Promise(() => {}))
      .mockResolvedValue({ capabilities: [{ id: "elsa.api.runtime", contractVersion: "1", links: [live] }] });
    context.http.requestResponse = vi.fn(async () => new Response("", { headers: { "content-type": "application/x-ndjson" } }));
    const request = { continuationToken: "same-bookmark" };
    const signal = new AbortController().signal;
    const waiting = expect(getWorkflowHistoryStream(context, "run", request, signal).next()).rejects.toMatchObject({ name: "TimeoutError" });
    await vi.advanceTimersByTimeAsync(35_000);
    await waiting;
    expect(context.http.requestResponse).not.toHaveBeenCalled();
    const retry = getWorkflowHistoryStream(context, "run", request, signal);
    await expect(retry.next()).resolves.toMatchObject({ done: true });
    expect(getJson).toHaveBeenCalledTimes(2);
    expect(context.http.requestResponse).toHaveBeenCalledWith(expect.stringContaining("cursor=same-bookmark"), expect.anything());
  } finally {
    vi.useRealTimers();
  }
});

it("bounds a stalled authentication transport before headers and disposes its late response", async () => {
  vi.useFakeTimers();
  try {
    const { context } = setup(null, [{ rel: "workflow-history-live", href: "runtime/workflows/instances/{workflowExecutionId}/history/live" }]);
    let complete!: (response: Response) => void;
    let authSignal!: AbortSignal;
    const cancelled = vi.fn();
    context.http.requestResponse = vi.fn().mockImplementationOnce((_url, options: RequestInit) => {
      authSignal = options.signal!;
      return new Promise<Response>(resolve => { complete = resolve; });
    }).mockImplementation(async () => new Response("", { headers: { "content-type": "application/x-ndjson" } }));
    const request = { continuationToken: "confirmed-bookmark" };
    const signal = new AbortController().signal;
    const waiting = expect(getWorkflowHistoryStream(context, "run", request, signal).next()).rejects.toMatchObject({ name: "TimeoutError" });
    await vi.advanceTimersByTimeAsync(35_000);
    await waiting;
    expect(authSignal.aborted).toBe(true);
    complete(new Response(new ReadableStream({ cancel: cancelled }), { headers: { "content-type": "application/x-ndjson" } }));
    await expect(getWorkflowHistoryStream(context, "run", request, signal).next()).resolves.toMatchObject({ done: true });
    expect(cancelled).toHaveBeenCalledOnce();
    expect(context.http.requestResponse).toHaveBeenNthCalledWith(2, expect.stringContaining("cursor=confirmed-bookmark"), expect.anything());
  } finally {
    vi.useRealTimers();
  }
});
