import { describe, expect, it, onTestFinished, vi } from "vitest";
import { authSessionStartedEvent } from "@elsa-workflows/studio-sdk";
import {
  button, click, definition, pagedDefinitionCapabilities, renderWorkflowRoute, response, row, select,
  waitForCondition, waitForText
} from "./workflowDefinitionTestHelpers";
import { parseWorkflowDefinitionBrowseLocation, updateWorkflowDefinitionBrowseUrl, workflowLibraryFilterKey } from "../workflow-editor/workflowDefinitionBrowseLocation";

function libraryCapabilities() {
  const capabilities = pagedDefinitionCapabilities({ folders: true, queryOptions: true });
  capabilities.capabilities[0].links.push({ rel: "workflow-tags", href: "design/workflows/tags" }, { rel: "workflow-organization", href: "design/workflows/organization/{definitionId}" }, { rel: "workflow-favorites", href: "design/workflows/favorites/{definitionId}" });
  return capabilities;
}

describe("workflow library query boundaries", () => {
  it("withholds newer query controls on an older paged host and clears only its unsupported options", async () => {
    const requests: URL[] = [];
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      if (url.pathname.endsWith("definition-pages")) {
        requests.push(url);
        // This host deliberately ignores the newer publication/sort/descendant query grammar.
        return response({ items: [definition({ name: "Older host draft", publicationState: "draft" })] });
      }
      if (url.pathname.endsWith("folders/finance")) return response({ folder: { id: "finance", name: "Finance", parentId: null }, ancestors: [] });
      return response({ items: [] });
    });
    const { container } = await renderWorkflowRoute("/workflows/definitions?folderId=finance&includeDescendants=true&sort=modified&publicationState=published&columns=location", fetchMock, pagedDefinitionCapabilities({ folders: true }));
    await waitForText(container, "Some organization filters are unavailable");
    expect(requests).toHaveLength(0);
    expect(container.querySelector('[aria-label="Sort workflows"]')).toBeNull();
    await click(button(container, "Clear unavailable organization filters"));
    await waitForText(container, "Older host draft");
    expect(requests.at(-1)?.searchParams.get("folderId")).toBe("finance");
    for (const key of ["includeDescendants", "sort", "publicationState"]) expect(requests.at(-1)?.searchParams.has(key)).toBe(false);
    await waitForCondition(() => expect(button(container, "Columns"), [...container.querySelectorAll("button")].map(button => button.textContent).join(" | ")).not.toBeNull());
    await click(button(container, "Columns"));
    expect(container.querySelector('[aria-label="Publication filter"]')).toBeNull();
    expect(container.textContent).toContain("Optional columns");
  });

  it("recovers a missing tag operation without losing supported folder, favorite and column choices", async () => {
    const capabilities = pagedDefinitionCapabilities({ folders: true, queryOptions: true });
    capabilities.capabilities[0].links.push({ rel: "workflow-favorites", href: "design/workflows/favorites/{definitionId}", templated: true });
    const requests: URL[] = [];
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      if (url.pathname.endsWith("definition-pages")) {
        requests.push(url);
        return response({ items: [definition({ name: "Invoice in finance", favorite: true, folderId: "finance", folderBreadcrumb: [{ id: "finance", name: "Finance" }], tags: [{ tagId: "critical" }] })] });
      }
      if (url.pathname.endsWith("folders/finance")) return response({ folder: { id: "finance", name: "Finance", parentId: null }, ancestors: [] });
      return response({ items: [] });
    });
    const tags = encodeURIComponent(JSON.stringify([{ tagId: "critical", operator: "exists" }]));
    const { container } = await renderWorkflowRoute(`/workflows/definitions?folderId=finance&favoritesOnly=true&tagFilters=${tags}&columns=tags,location&view=mine`, fetchMock, capabilities);
    await waitForText(container, "Some organization filters are unavailable");
    expect(requests).toHaveLength(0);
    await click(button(container, "Clear unavailable organization filters"));
    await waitForText(container, "Invoice in finance");
    expect(requests.at(-1)?.searchParams.get("folderId")).toBe("finance");
    expect(requests.at(-1)?.searchParams.get("favoritesOnly")).toBe("true");
    expect(requests.at(-1)?.searchParams.has("tagFilters")).toBe(false);
    const headings = [...container.querySelectorAll('[role="columnheader"]')].map(item => item.textContent);
    expect(headings).toContain("Location");
    expect(headings).not.toContain("Tags");
    expect(new URLSearchParams(window.location.search).get("columns")).toBe("tags,location");
    expect(new URLSearchParams(window.location.search).has("view")).toBe(false);
  });

  it.each(["folderId=old-folder", "unfiled=true", "includeDescendants=true", "favoritesOnly=true", "view=private-view",
    `tagFilters=${encodeURIComponent(JSON.stringify([{ tagId: "old-tag", operator: "exists" }]))}`])(
    "holds unavailable scope %s until explicit recovery, retaining ordinary filters and saved columns", async scope => {
      const requests: URL[] = [];
      const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
        const url = new URL(String(input));
        if (url.pathname.endsWith("definition-pages")) {
          requests.push(url);
          return response({ items: [definition({ name: "Recovered ordinary result" })], totalCount: 1 });
        }
        return response({ items: [] });
      });
      const { container } = await renderWorkflowRoute(`/workflows/definitions?${scope}&search=invoice&state=all&sort=modified&publicationState=published&columns=tags,location&custom=keep`, fetchMock, pagedDefinitionCapabilities({ queryOptions: true }));
      await waitForText(container, "Some organization filters are unavailable");
      expect(requests).toHaveLength(0);
      expect(container.querySelector('[role="table"]')).toBeNull();
      await click(button(container, "Clear unavailable organization filters"));
      await waitForText(container, "Recovered ordinary result");
      const query = requests.at(-1)!.searchParams;
      expect(query.get("searchTerm")).toBe("invoice");
      expect(query.get("state")).toBe("all");
      expect(query.get("sort")).toBe("modified");
      expect(query.get("publicationState")).toBe("published");
      expect(window.location.search).toContain("custom=keep");
      expect(new URLSearchParams(window.location.search).get("columns")).toBe("tags,location");
      expect(container.querySelector('[role="columnheader"]:last-child')?.textContent).toBe("Actions");
      expect([...container.querySelectorAll('[role="columnheader"]')].map(item => item.textContent)).not.toContain("Tags");
    });

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
    await waitForText(container, "Clear unavailable organization filters");
    expect(requests.some(url => url.pathname.endsWith("definition-pages"))).toBe(false);
    expect(row(container, "Unfiltered workflow")).toBeNull();
    await click(container.querySelector("button") && Array.from(container.querySelectorAll("button")).find(item => item.textContent === "Clear unavailable organization filters")!);
    await waitForText(container, "Unfiltered workflow");
    expect(new URL(window.location.href).searchParams.has("favoritesOnly")).toBe(false);
  });

  it.each([false, true])("restores a tagged history entry's page and cursors only while its access scope is valid (invalidate=%s)", async invalidate => {
    const requests: URL[] = [];
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      if (!url.pathname.endsWith("definition-pages")) return response({ items: [] });
      requests.push(url);
      const token = url.searchParams.get("continuationToken");
      const page = token === "tag-page-three" ? 3 : token === "tag-page-two" ? 2 : 1;
      const scope = url.searchParams.has("favoritesOnly") ? "Favorite" : "Tagged";
      return response({ items: [definition({ name: `${scope} page ${page}` })], nextContinuationToken: page < 3 ? `tag-page-${page === 1 ? "two" : "three"}` : null, totalCount: 75 });
    });
    const tagged = encodeURIComponent(JSON.stringify([{ tagId: "critical", operator: "exists" }]));
    const { container } = await renderWorkflowRoute(`/workflows/definitions?tagFilters=${tagged}`, fetchMock, libraryCapabilities());
    await waitForText(container, "Tagged page 1");
    await select(container.querySelector<HTMLSelectElement>(".wf-page-size select"), "25");
    await waitForCondition(() => expect(requests.at(-1)?.searchParams.get("pageSize")).toBe("25"));
    await click(button(container, "Next")); await waitForText(container, "Tagged page 2");
    await click(button(container, "Next")); await waitForText(container, "Tagged page 3");
    await click(button(container, "Favorites")); await waitForText(container, "Favorite page 1");
    if (!invalidate) {
      await select(container.querySelector<HTMLSelectElement>(".wf-page-size select"), "50");
      await waitForCondition(() => expect(requests.at(-1)?.searchParams.get("pageSize")).toBe("50"));
    }
    if (invalidate) {
      window.dispatchEvent(new Event(authSessionStartedEvent));
      await click(button(container, "Refresh"));
      await waitForText(container, "Favorite page 1");
    }
    const beforeBack = requests.length;
    window.history.back();
    await waitForCondition(() => expect(requests.length).toBeGreaterThan(beforeBack));
    const restored = requests.at(-1)!;
    expect(restored.searchParams.get("pageSize")).toBe("25");
    expect(restored.searchParams.get("continuationToken")).toBe(invalidate ? null : "tag-page-three");
    expect(restored.searchParams.has("favoritesOnly")).toBe(false);
    expect(JSON.parse(restored.searchParams.get("tagFilters")!)).toEqual([{ tagId: "critical", operator: "exists" }]);
    await waitForText(container, `Tagged page ${invalidate ? 1 : 3}`);
  });

  it.each([
    { name: "shrinking count with another cursor", totalCount: 100, cursor: "A100" },
    { name: "growing count without another cursor", totalCount: 200, cursor: null }
  ])("uses cursor continuation rather than the $name", async ({ totalCount, cursor }) => {
    const requests: URL[] = [];
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      if (!url.pathname.endsWith("definition-pages")) return response({ items: [] });
      requests.push(url);
      const token = url.searchParams.get("continuationToken");
      const page = token === "A100" ? 3 : token === "A50" ? 2 : 1;
      const size = Number(url.searchParams.get("pageSize"));
      const items = Array.from({ length: size }, (_, index) => definition({ id: `workflow-${page}-${index}`, name: `Cursor page ${page} workflow ${index + 1}` }));
      return response({ items, totalCount: page === 1 ? 150 : totalCount,
        nextContinuationToken: page === 1 ? `A${size}` : page === 2 ? cursor : null });
    });
    const { container } = await renderWorkflowRoute("/workflows/definitions", fetchMock, pagedDefinitionCapabilities());
    await waitForText(container, "Cursor page 1 workflow 1");
    await select(container.querySelector<HTMLSelectElement>(".wf-page-size select"), "50");
    await waitForCondition(() => expect(requests.at(-1)?.searchParams.get("pageSize")).toBe("50"));
    await click(button(container, "Next")); await waitForText(container, "Cursor page 2 workflow 1");
    const next = button(container, "Next") as HTMLButtonElement;
    expect(next.disabled).toBe(!cursor);
    expect(container.querySelector(".wf-pagination-summary")?.textContent).toContain(`of ${totalCount}`);
    expect(container.querySelector(".wf-page-controls")?.textContent).not.toContain("Page 2 of");
    const before = requests.length;
    await click(next);
    if (cursor) {
      await waitForText(container, "Cursor page 3 workflow 1");
      expect(requests.at(-1)?.searchParams.get("continuationToken")).toBe("A100");
    } else expect(requests).toHaveLength(before);
  });
});
