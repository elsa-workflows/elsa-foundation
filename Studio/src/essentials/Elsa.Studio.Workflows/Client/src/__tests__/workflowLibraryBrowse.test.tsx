import { describe, expect, it, onTestFinished, vi } from "vitest";
import {
  click, definition, pagedDefinitionCapabilities, renderWorkflowRoute, response, row,
  waitForCondition, waitForText
} from "./workflowDefinitionTestHelpers";
import { parseWorkflowDefinitionBrowseLocation, updateWorkflowDefinitionBrowseUrl, workflowLibraryFilterKey } from "../workflow-editor/workflowDefinitionBrowseLocation";

function libraryCapabilities() {
  const capabilities = pagedDefinitionCapabilities();
  capabilities.capabilities[0].links.push({ rel: "workflow-organization", href: "design/workflows/organization/{definitionId}" }, { rel: "workflow-favorites", href: "design/workflows/favorites/{definitionId}" });
  return capabilities;
}

describe("workflow library query boundaries", () => {
  it("round-trips the complete library query with opaque classification identities", () => {
    const location = { folderSelection: { id: "folder/opaque" }, listState: "all" as const, search: "invoice",
      tagFilters: [{ tagId: "team/ü", operator: "any" as const, valueIds: ["finance#1"] }], includeDescendants: true,
      favoritesOnly: true, sort: "modified" as const, publicationState: "published", viewId: "view/shared", columns: ["tags", "location"] };
    const url = updateWorkflowDefinitionBrowseUrl(new URL("https://studio.test/workflows/definitions?definition=selected"), location);
    expect(parseWorkflowDefinitionBrowseLocation(url.search)).toEqual(location);
    expect(url.searchParams.get("definition")).toBe("selected");
    expect(workflowLibraryFilterKey(location)).not.toBe(workflowLibraryFilterKey({ ...location, favoritesOnly: false }));
  });

  it.each(["not-json", JSON.stringify([{ tagId: "team", operator: "unknown" }])])("retains an invalid filter for a visible repair instead of broadening results: %s", raw => {
    const parsed = parseWorkflowDefinitionBrowseLocation(`?tagFilters=${encodeURIComponent(raw)}`);
    expect(parsed.invalidTagFilters).toBe(raw);
    const url = updateWorkflowDefinitionBrowseUrl(new URL("https://studio.test/"), parsed);
    expect(url.searchParams.get("tagFilters")).toBe(raw);
  });

  it.each([
    ["favorites", "favoritesOnly", "true"],
    ["descendants", "includeDescendants", "true"],
    ["sort", "sort", "modified"],
    ["publication", "publicationState", "published"],
    ["tag", "tagFilters", JSON.stringify([{ tagId: "critical", operator: "exists" }])]
  ])("clears old rows and selections during a deferred %s query", async (_name, key, value) => {
    let complete: ((value: Response) => void) | undefined;
    onTestFinished(() => complete?.(response({ items: [], nextContinuationToken: null })));
    const requests: URL[] = [];
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      if (url.pathname.endsWith("definition-pages")) {
        requests.push(url);
        if (url.searchParams.has(key)) return new Promise<Response>(resolve => { complete = resolve; });
        return response({ items: [definition({ name: "Previous library member" })], nextContinuationToken: "old-cursor", totalCount: 500 });
      }
      return response({ items: [] });
    });
    const { container } = await renderWorkflowRoute("/workflows/definitions", fetchMock, libraryCapabilities());
    await waitForText(container, "Previous library member");
    await click(container.querySelector('input[aria-label="Select workflow definition Previous library member"]'));
    await waitForText(container, "1 selected");
    const url = new URL(window.location.href); url.searchParams.set(key, value);
    window.history.pushState({}, "", `${url.pathname}${url.search}`); window.dispatchEvent(new PopStateEvent("popstate"));
    await waitForCondition(() => expect(requests.some(request => request.searchParams.get(key) === value)).toBe(true));
    expect(row(container, "Previous library member")).toBeNull();
    expect(container.textContent).not.toContain("1 selected");
    expect(requests.at(-1)?.searchParams.has("continuationToken")).toBe(false);
    complete?.(response({ items: [definition({ name: "Current library member" })], nextContinuationToken: null, totalCount: 1 }));
    await waitForText(container, "Current library member");
    expect(container.textContent).toContain("of 1");
  });

  it("rejects a late previous-tag response after switching to another classification", async () => {
    let completeOld: ((value: Response) => void) | undefined;
    const filters = (id: string) => JSON.stringify([{ tagId: id, operator: "exists" }]);
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      if (!url.pathname.endsWith("definition-pages")) return response({ items: [] });
      if (url.searchParams.get("tagFilters") === filters("old")) return new Promise<Response>(resolve => { completeOld = resolve; });
      return response({ items: [definition({ name: url.searchParams.has("tagFilters") ? "New tag member" : "Initial member" })], nextContinuationToken: null });
    });
    const { container } = await renderWorkflowRoute("/workflows/definitions", fetchMock, libraryCapabilities());
    await waitForText(container, "Initial member");
    const navigate = (id: string) => { const url = new URL(window.location.href); url.searchParams.set("tagFilters", filters(id)); window.history.pushState({}, "", `${url.pathname}${url.search}`); window.dispatchEvent(new PopStateEvent("popstate")); };
    navigate("old");
    await waitForCondition(() => expect(completeOld).toBeDefined());
    navigate("new"); await waitForText(container, "New tag member");
    completeOld?.(response({ items: [definition({ name: "Stale tag member" })], nextContinuationToken: null }));
    await waitForCondition(() => expect(row(container, "New tag member")).not.toBeNull());
    expect(row(container, "Stale tag member")).toBeNull();
  });

  it("does not let an older paged host silently ignore a favorite predicate", async () => {
    const requests: URL[] = [];
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input)); requests.push(url);
      return response({ items: [definition({ name: "Unfiltered workflow" })], nextContinuationToken: null });
    });
    const { container } = await renderWorkflowRoute("/workflows/definitions?favoritesOnly=true", fetchMock, pagedDefinitionCapabilities());
    await waitForText(container, "Clear organization filters");
    expect(requests.some(url => url.pathname.endsWith("definition-pages"))).toBe(false);
    expect(row(container, "Unfiltered workflow")).toBeNull();
    await click(container.querySelector("button") && Array.from(container.querySelectorAll("button")).find(item => item.textContent === "Clear organization filters")!);
    await waitForText(container, "Unfiltered workflow");
    expect(new URL(window.location.href).searchParams.has("favoritesOnly")).toBe(false);
  });
});
