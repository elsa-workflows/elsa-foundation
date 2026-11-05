import { afterEach, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { clearApiCapabilityCache } from "../api/capabilities";
import { getWorkflowHistoryPage, getWorkflowHistoryEvent, getWorkflowHistoryBookmark, listWorkflowHistoryBookmarks, RuntimeHistoryIdentityError } from "../api/runtimeHistory";
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
