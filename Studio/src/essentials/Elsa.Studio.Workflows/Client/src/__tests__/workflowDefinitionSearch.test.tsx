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
  settleReact,
  text,
  waitForCondition,
  waitForText
} from "./workflowDefinitionTestHelpers";

describe("workflow definition search refresh", () => {
  it("keeps a later-page result visible as labelled, non-actionable previous data while a search is pending", async () => {
    const requests: string[] = [];
    let resolveSearch: ((value: Response) => void) | undefined;
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      requests.push(url.toString());
      if (url.pathname.endsWith("/definition-pages")) {
        const token = url.searchParams.get("continuationToken");
        if (url.searchParams.get("searchTerm") === "invoice approval") {
          return new Promise<Response>(resolve => { resolveSearch = resolve; });
        }
        if (token === "opaque-second-page") {
          return response({ items: [definition({ id: "definition-second", name: "Second page workflow" })], nextContinuationToken: "opaque-third-page" });
        }
        return response({ items: [definition({ name: "First page workflow" })], nextContinuationToken: "opaque-second-page" });
      }
      return response({ items: [] });
    });
    const { container } = await renderWorkflowRoute("/workflows/definitions?search=first+query", fetchMock, pagedDefinitionCapabilities());

    await waitForText(container, "First page workflow");
    await click(button(container, "Next"));
    await waitForText(container, "Second page workflow");

    vi.useFakeTimers();
    onTestFinished(() => {
      resolveSearch?.(response({ items: [], nextContinuationToken: null }));
      vi.useRealTimers();
    });
    await fill(input(container, "Search all workflows"), "invoice approval");
    await vi.advanceTimersByTimeAsync(0);
    await settleReact();

    expect(row(container, "Second page workflow")).not.toBeNull();
    const refreshStatus = container.querySelector("[role='status']")?.textContent ?? "";
    expect(refreshStatus.toLowerCase()).toMatch(/previous|out of date/);
    expect(refreshStatus).toContain("first query");
    expect(refreshStatus).toContain("invoice approval");
    expect(refreshStatus.toLowerCase()).toContain("page 2");

    const previousRow = row(container, "Second page workflow");
    expect(previousRow?.querySelector("a")).toBeNull();
    expect(previousRow?.querySelector("input[type='checkbox']")).toBeNull();
    expect(previousRow?.querySelector("button")).toBeNull();
    expect(previousRow?.getAttribute("aria-label") ?? "").not.toMatch(/open workflow definition/i);
    expect(button(container, "Next")).toBeNull();
    expect(container.querySelector(".wf-page-size")).toBeNull();
    expect(new URLSearchParams(window.location.search).get("search")).toBe("invoice approval");
    expect(requests.filter(url => new URL(url).searchParams.get("searchTerm") === "invoice approval")).toHaveLength(0);

    await click(previousRow);
    expect(new URLSearchParams(window.location.search).has("definition")).toBe(false);

    await vi.advanceTimersByTimeAsync(250);
    vi.useRealTimers();
    await waitForCondition(() => expect(requests.filter(url => new URL(url).searchParams.get("searchTerm") === "invoice approval")).toHaveLength(1));
    resolveSearch?.(response({ items: [definition({ name: "Invoice approval workflow" })], nextContinuationToken: null }));
    await waitForText(container, "Invoice approval workflow");
  });

  it("updates the search address immediately but waits for settled text before requesting and trims only the wire value", async () => {
    const requests: string[] = [];
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      requests.push(url.toString());
      return response({ items: [definition({ name: "Workflow result" })], nextContinuationToken: null });
    });
    const { container } = await renderWorkflowRoute("/workflows/definitions", fetchMock, pagedDefinitionCapabilities());
    await waitForText(container, "Workflow result");

    vi.useFakeTimers();
    onTestFinished(() => { vi.useRealTimers(); });
    const search = input(container, "Search all workflows");
    await fill(search, "i");
    await fill(search, "inv");
    await fill(search, "  invoice  ");
    expect(search?.value).toBe("  invoice  ");
    expect(new URLSearchParams(window.location.search).get("search")).toBe("  invoice  ");
    await vi.advanceTimersByTimeAsync(0);
    expect(requests.filter(url => new URL(url).searchParams.has("searchTerm"))).toHaveLength(0);

    await vi.advanceTimersByTimeAsync(249);
    expect(requests.filter(url => new URL(url).searchParams.has("searchTerm"))).toHaveLength(0);
    await vi.advanceTimersByTimeAsync(1);
    await waitForCondition(() => {
      const searched = requests.map(url => new URL(url)).filter(url => url.searchParams.has("searchTerm"));
      expect(searched).toHaveLength(1);
      expect(searched[0].searchParams.get("searchTerm")).toBe("invoice");
    });
    await fill(search, "");
    expect(search?.value).toBe("");
    expect(new URLSearchParams(window.location.search).has("search")).toBe(false);
    await vi.advanceTimersByTimeAsync(250);
    await waitForCondition(() => {
      const pages = requests.map(url => new URL(url)).filter(url => url.pathname.endsWith("/definition-pages"));
      expect(pages).toHaveLength(3);
      expect(pages.at(-1)?.searchParams.has("searchTerm")).toBe(false);
    });
  });

  it("keeps the debounce when text returns to the already-settled search", async () => {
    const requests: string[] = [];
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      requests.push(url.toString());
      return response({ items: [definition({ name: "Settled workflow" })], nextContinuationToken: null });
    });
    const { container } = await renderWorkflowRoute("/workflows/definitions?search=settled", fetchMock, pagedDefinitionCapabilities());
    await waitForText(container, "Settled workflow");

    vi.useFakeTimers();
    onTestFinished(() => { vi.useRealTimers(); });
    const search = input(container, "Search all workflows");
    await fill(search, "temporary");
    await fill(search, "settled");
    await vi.advanceTimersByTimeAsync(0);
    expect(requests.filter(url => new URL(url).searchParams.get("searchTerm") === "settled")).toHaveLength(1);
    expect(row(container, "Settled workflow")?.querySelector("a")).toBeNull();
    expect(container.querySelector("[role='status']")?.textContent).toContain("waits to refresh");

    await vi.advanceTimersByTimeAsync(249);
    expect(requests.filter(url => new URL(url).searchParams.get("searchTerm") === "settled")).toHaveLength(1);
    await vi.advanceTimersByTimeAsync(1);
    await waitForCondition(() => expect(
      requests.filter(url => new URL(url).searchParams.get("searchTerm") === "settled")
    ).toHaveLength(2));
  });

  it("lets an explicit refresh flush a pending search immediately without a duplicate delayed request", async () => {
    const requests: string[] = [];
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      requests.push(url.toString());
      return response({ items: [definition({ name: url.searchParams.get("searchTerm") === "invoice" ? "Refreshed invoice workflow" : "Initial workflow" })], nextContinuationToken: null });
    });
    const { container } = await renderWorkflowRoute("/workflows/definitions", fetchMock, pagedDefinitionCapabilities());
    await waitForText(container, "Initial workflow");

    vi.useFakeTimers();
    onTestFinished(() => { vi.useRealTimers(); });
    await fill(input(container, "Search all workflows"), "invoice");
    expect(requests.filter(url => new URL(url).searchParams.get("searchTerm") === "invoice")).toHaveLength(0);

    await click(button(container, "Refresh"));
    await waitForCondition(() => expect(
      requests.filter(url => new URL(url).searchParams.get("searchTerm") === "invoice")
    ).toHaveLength(1));
    await vi.advanceTimersByTimeAsync(300);
    expect(requests.filter(url => new URL(url).searchParams.get("searchTerm") === "invoice")).toHaveLength(1);
    await waitForText(container, "Refreshed invoice workflow");
  });

  it.each(["during the newer query's delay", "after the newer result", "after the newer result (failed older request)"])("ignores an older response %s", async completion => {
    const requests: string[] = [];
    let resolveAlpha: ((value: Response) => void) | undefined;
    let resolveBeta: ((value: Response) => void) | undefined;
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      if (url.pathname.endsWith("/definition-pages")) {
        const query = url.searchParams.get("searchTerm");
        if (query) {
          requests.push(query);
          return new Promise<Response>(resolve => {
            if (query === "alpha") resolveAlpha = resolve;
            if (query === "beta") resolveBeta = resolve;
          });
        }
        return response({ items: [definition({ name: "Initial workflow" })], nextContinuationToken: null });
      }
      return response({ items: [] });
    });
    const { container } = await renderWorkflowRoute("/workflows/definitions", fetchMock, pagedDefinitionCapabilities());
    await waitForText(container, "Initial workflow");

    vi.useFakeTimers();
    onTestFinished(() => {
      resolveAlpha?.(response({ items: [definition({ name: "Stale alpha workflow" })], nextContinuationToken: null }));
      resolveBeta?.(response({ items: [definition({ name: "Current beta workflow" })], nextContinuationToken: null }));
      vi.useRealTimers();
    });

    const search = input(container, "Search all workflows");
    await fill(search, "alpha");
    await vi.advanceTimersByTimeAsync(250);
    await waitForCondition(() => expect(requests).toContain("alpha"));

    await fill(search, "beta");
    if (completion === "during the newer query's delay") {
      resolveAlpha?.(response({ items: [definition({ name: "Stale alpha workflow" })], nextContinuationToken: null }));
      await vi.advanceTimersByTimeAsync(1);
      await settleReact();
      expect(text(container, "Stale alpha workflow")).toBe(false);
      expect(new URLSearchParams(window.location.search).get("search")).toBe("beta");
    }
    await vi.advanceTimersByTimeAsync(250);
    await waitForCondition(() => expect(requests).toContain("beta"));

    vi.useRealTimers();
    resolveBeta?.(response({ items: [definition({ name: "Current beta workflow" })], nextContinuationToken: null }));
    await waitForText(container, "Current beta workflow");
    resolveAlpha?.(completion === "after the newer result (failed older request)"
      ? response("Late alpha failure.", 503)
      : response({ items: [definition({ name: "Stale alpha workflow" })], nextContinuationToken: null }));
    // Let the resolved HTTP body finish before checking that it cannot replace beta.
    await new Promise<void>(resolve => window.setTimeout(resolve, 0));
    await settleReact();
    expect(text(container, "Stale alpha workflow")).toBe(false);
    expect(text(container, "Current beta workflow")).toBe(true);
    expect(container.querySelector("[role='alert']")).toBeNull();
    expect(container.querySelector(".wf-alert")).toBeNull();
  });

  it("keeps first-load waiting truthful when the first search request is pending", async () => {
    let resolveInitial: ((value: Response) => void) | undefined;
    onTestFinished(() => resolveInitial?.(response({ items: [], nextContinuationToken: null })));
    const requests: string[] = [];
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      requests.push(url.toString());
      if (url.pathname.endsWith("/definition-pages")) {
        return new Promise<Response>(resolve => { resolveInitial = resolve; });
      }
      return response({ items: [] });
    });
    const { container } = await renderWorkflowRoute("/workflows/definitions?search=invoice", fetchMock, pagedDefinitionCapabilities());

    await waitForCondition(() => expect(requests.some(url => new URL(url).searchParams.has("searchTerm"))).toBe(true));
    expect(container.querySelector(".wf-grid[aria-busy='true']")).not.toBeNull();
    expect(container.textContent).not.toContain("No workflows match");
    expect(container.querySelector("[role='alert']")?.textContent ?? "").not.toContain("failed");

    resolveInitial?.(response({ items: [definition({ name: "Invoice workflow" })], nextContinuationToken: null }));
    await waitForText(container, "Invoice workflow");
  });
});
