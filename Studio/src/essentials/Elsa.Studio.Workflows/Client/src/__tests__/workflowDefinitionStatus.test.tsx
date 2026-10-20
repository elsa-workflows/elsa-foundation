import { describe, expect, it, onTestFinished, vi } from "vitest";
import {
  button,
  click,
  definition,
  fill,
  input,
  pagedDefinitionCapabilities,
  renderWorkflowRoute,
  response,
  row,
  waitForCondition,
  waitForText
} from "./workflowDefinitionTestHelpers";

describe("workflow definition request status", () => {
  it.each([true, false])("treats text entered after a failed search as pending, with previous data: %s", async hasPreviousData => {
    const requests: URL[] = [];
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      if (!url.pathname.endsWith("/definition-pages")) return response({ items: [] });
      requests.push(url);
      const search = url.searchParams.get("searchTerm");
      if (search === "failed") return response("Temporary search failure.", 503);
      return response({ items: [definition({ name: search === "latest" ? "Latest workflow" : "Previous workflow" })], nextContinuationToken: null });
    });
    const { container } = await renderWorkflowRoute(
      `/workflows/definitions?search=${hasPreviousData ? "initial" : "failed"}`,
      fetchMock,
      pagedDefinitionCapabilities()
    );
    if (hasPreviousData) {
      await waitForText(container, "Previous workflow");
      await fill(input(container, "Search all workflows"), "failed");
      await click(button(container, "Refresh"));
    }
    await waitForCondition(() => expect(container.querySelector("[role='alert']")).not.toBeNull());

    vi.useFakeTimers();
    onTestFinished(() => { vi.useRealTimers(); });
    await fill(input(container, "Search all workflows"), "latest");
    expect(container.querySelector("[role='alert']")).toBeNull();
    expect(requests.some(url => url.searchParams.get("searchTerm") === "latest")).toBe(false);
    if (hasPreviousData) {
      expect(row(container, "Previous workflow")).not.toBeNull();
      expect(container.querySelector("[role='status']")?.textContent).toContain("“latest” waits to refresh");
      expect(container.querySelector("[role='status']")?.textContent).not.toContain("failed");
    } else {
      expect(container.querySelector(".wf-grid[aria-busy='true']")).not.toBeNull();
    }
    await vi.advanceTimersByTimeAsync(250);
    vi.useRealTimers();
    await waitForText(container, "Latest workflow");
  });

  it("keeps the current rows visible during same-view refresh and marks them stale after a failed refresh", async () => {
    const requests: string[] = [];
    let resolveRefresh: ((value: Response) => void) | undefined;
    onTestFinished(() => resolveRefresh?.(response({ items: [], nextContinuationToken: null })));
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes("definition-pages")) {
        requests.push(url);
        if (requests.length === 1) return response({ items: [definition({ name: "Current workflow" })], nextContinuationToken: null });
        if (requests.length === 2) return new Promise<Response>(resolve => { resolveRefresh = resolve; });
        return response({ items: [definition({ name: "Recovered workflow" })], nextContinuationToken: null });
      }
      return response({ items: [] });
    });
    const { container } = await renderWorkflowRoute("/workflows/definitions", fetchMock, pagedDefinitionCapabilities());

    await waitForText(container, "Current workflow");
    await click(button(container, "Refresh"));
    await waitForCondition(() => expect(requests).toHaveLength(2));

    expect(row(container, "Current workflow")).not.toBeNull();
    expect(container.querySelector("[role='status']")?.textContent?.toLowerCase()).toContain("refresh");

    resolveRefresh?.(response("The refresh was denied.", 503));
    await waitForCondition(() => expect(container.querySelector("[role='alert']")?.textContent).toContain("Couldn't load workflow definitions"));
    expect(row(container, "Current workflow")).not.toBeNull();
    expect(container.querySelector("[role='status']")?.textContent?.toLowerCase()).toMatch(/previous|out of date/);

    await click(button(container, "Refresh"));
    await waitForText(container, "Recovered workflow");
    expect(row(container, "Current workflow")).toBeNull();
  });

  it("distinguishes a successful empty search from a failed current search and retries the exact current query", async () => {
    const requests: string[] = [];
    let failedAttempts = 0;
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      if (url.pathname.endsWith("/definition-pages")) {
        requests.push(url.toString());
        const search = url.searchParams.get("searchTerm");
        if (search === "no-match") return response({ items: [], nextContinuationToken: null });
        if (search === "retryable" && failedAttempts++ === 0) return response("Temporary failure.", 503);
        if (search === "retryable") return response({ items: [definition({ name: "Recovered retryable workflow" })], nextContinuationToken: null });
      }
      return response({ items: [] });
    });
    const { container } = await renderWorkflowRoute("/workflows/definitions?search=no-match", fetchMock, pagedDefinitionCapabilities());

    await waitForText(container, "No workflows match “no-match”");
    await click(button(container, "Refresh"));
    await waitForText(container, "No workflows match “no-match”");

    await fill(input(container, "Search all workflows"), "retryable");
    await waitForCondition(() => expect(container.querySelector("[role='alert']")?.textContent).toContain("Couldn't load workflow definitions"));
    expect(container.textContent).not.toContain("No workflows match “retryable”");
    expect(new URLSearchParams(window.location.search).get("search")).toBe("retryable");

    await click(button(container, "Refresh"));
    await waitForText(container, "Recovered retryable workflow");
    expect(new URL(requests.at(-1)!).searchParams.get("searchTerm")).toBe("retryable");
  });

  it("keeps identifiable previous-page rows through a failed new search and retries that search from page one", async () => {
    const requests: string[] = [];
    let failedNewQuery = false;
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      if (url.pathname.endsWith("/definition-pages")) {
        requests.push(url.toString());
        const query = url.searchParams.get("searchTerm");
        if (query === "new query") {
          if (!failedNewQuery) {
            failedNewQuery = true;
            return response("Temporary failure.", 503);
          }
          return response({ items: [definition({ name: "Recovered new-query workflow" })], nextContinuationToken: null });
        }
        if (url.searchParams.get("continuationToken") === "first-page-cursor") {
          return response({ items: [definition({ id: "definition-second", name: "Second-page first-query workflow" })], nextContinuationToken: null });
        }
        return response({ items: [definition({ name: "First-page first-query workflow" })], nextContinuationToken: "first-page-cursor" });
      }
      return response({ items: [] });
    });
    const { container } = await renderWorkflowRoute("/workflows/definitions?search=first+query", fetchMock, pagedDefinitionCapabilities());
    await waitForText(container, "First-page first-query workflow");
    await click(button(container, "Next"));
    await waitForText(container, "Second-page first-query workflow");

    vi.useFakeTimers();
    onTestFinished(() => { vi.useRealTimers(); });
    await fill(input(container, "Search all workflows"), "new query");
    expect(new URLSearchParams(window.location.search).get("search")).toBe("new query");
    expect(row(container, "Second-page first-query workflow")).not.toBeNull();
    expect(container.querySelector("[role='status']")?.textContent ?? "").toContain("first query");
    await vi.advanceTimersByTimeAsync(250);
    await waitForCondition(() => expect(requests.some(url => new URL(url).searchParams.get("searchTerm") === "new query")).toBe(true));
    vi.useRealTimers();

    const failedRequest = requests.map(url => new URL(url)).find(url => url.searchParams.get("searchTerm") === "new query");
    expect(failedRequest).toBeDefined();
    expect(failedRequest?.searchParams.has("continuationToken")).toBe(false);
    expect(row(container, "Second-page first-query workflow")).not.toBeNull();
    expect(container.querySelector("[role='alert']")?.textContent).toContain("Couldn't load workflow definitions");
    expect(container.querySelector("[role='status']")?.textContent).toContain("The latest search “new query” failed.");
    expect(button(container, "Refresh")).not.toBeNull();

    await click(button(container, "Refresh"));
    await waitForText(container, "Recovered new-query workflow");
    const retry = new URL(requests.at(-1)!);
    expect(retry.searchParams.get("searchTerm")).toBe("new query");
    expect(retry.searchParams.has("continuationToken")).toBe(false);
    expect(requests.filter(url => new URL(url).searchParams.get("searchTerm") === "new query")).toHaveLength(2);
  });
});
