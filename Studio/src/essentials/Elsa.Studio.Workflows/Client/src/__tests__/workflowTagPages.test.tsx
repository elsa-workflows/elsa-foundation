import { Activity } from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { createEndpointContext } from "@elsa-workflows/studio-sdk";
import { WorkflowTagsRoute } from "../workflow-editor/WorkflowTagsPage";
import { describe, expect, it, vi } from "vitest";
import { button, click, fill, pagedDefinitionCapabilities, renderWorkflowRoute, response, waitForText } from "./workflowDefinitionTestHelpers";
import type { WorkflowTag } from "../api/workflowOrganization";

const tag: WorkflowTag = { id: "region", name: "Region", description: "Geographical area", mode: "controlled", cardinality: "single", revision: "1", deprecated: false, values: [{ id: "eu", name: "Europe" }] };
const retired: WorkflowTag = { ...tag, id: "old", name: "Legacy", deprecated: true };
function capabilities() {
  const caps = pagedDefinitionCapabilities();
  caps.capabilities[0].links.push({ rel: "workflow-tags", href: "tags" }, { rel: "workflow-tag", href: "tags/{tagId}", templated: true });
  return caps;
}
function fixture(options: { denied?: boolean; fail?: boolean; deferred?: Promise<Response> } = {}) {
  const reads: string[] = [];
  const fetcher = vi.fn(async (input: RequestInfo | URL) => {
    const url = new URL(String(input)); reads.push(url.pathname + url.search);
    if (options.fail) throw new Error("Catalog offline");
    if (url.pathname === "/tags/region") return options.deferred ?? response(tag);
    if (url.pathname === "/tags") return response({ items: url.searchParams.has("search") ? [] : url.searchParams.has("continuationToken") ? [retired] : [tag], permissions: { canManageTags: !options.denied }, nextContinuationToken: url.searchParams.has("search") || url.searchParams.has("continuationToken") ? null : "next-page" });
    throw new Error(`Unexpected request ${url}`);
  });
  return { fetcher, reads };
}

describe("workflow tag pages", () => {
  it("opens a tag from the collection at its edit URL and returns to the collection", async () => {
    const { fetcher, reads } = fixture();
    const { container } = await renderWorkflowRoute("/workflows/tags", fetcher, capabilities(), { followNavigation: true });
    await waitForText(container, "Region");
    expect(container.querySelector('[role="dialog"]')).toBeNull();
    await click(container.querySelector('a[href="/workflows/tags/region"]'));
    await waitForText(container, "Edit workflow tag");
    expect(window.location.pathname).toBe("/workflows/tags/region");
    expect(container.querySelector<HTMLInputElement>('[aria-label="Tag name"]')?.value).toBe("Region");
    expect(container.querySelector('[aria-label="Tag to manage"]')).toBeNull();
    expect(reads).toContain("/tags/region");
    await click(button(container, "Back to tags"));
    await waitForText(container, "Region");
    expect(window.location.pathname).toBe("/workflows/tags");
    await click(button(container, "Create tag"));
    await waitForText(container, "Create workflow tag");
    expect(window.location.pathname).toBe("/workflows/tags/new");
    expect(container.querySelector<HTMLInputElement>('[aria-label="Tag name"]')?.value).toBe("");
  });

  it("loads continuation pages including retired tags and searches on the server", async () => {
    const { fetcher, reads } = fixture();
    const { container } = await renderWorkflowRoute("/workflows/tags", fetcher, capabilities());
    await waitForText(container, "1 tags loaded");
    await click(button(container, "Load more tags"));
    await waitForText(container, "2 tags loaded");
    expect(container.textContent).toContain("Retired");
    expect(reads).toContain("/tags?pageSize=100&continuationToken=next-page");
    await fill(container.querySelector('[aria-label="Search workflow tags"]'), "missing");
    await click(button(container, "Search"));
    await waitForText(container, "No matching tags");
    expect(container.querySelectorAll("tbody tr")).toHaveLength(0);
    expect(reads).toContain("/tags?pageSize=100&search=missing");
  });

  for (const path of ["/workflows/tags/new", "/workflows/tags/region"]) it(`denies direct mutation navigation to ${path}`, async () => {
    const { fetcher, reads } = fixture({ denied: true });
    const { container } = await renderWorkflowRoute(path, fetcher, capabilities());
    await waitForText(container, "Tag management is not allowed");
    expect(container.querySelector("form")).toBeNull();
    expect(reads).not.toContain("/tags/region");
  });

  it("offers retry after an initial load failure", async () => {
    const options = { fail: true };
    const { fetcher } = fixture(options);
    const { container } = await renderWorkflowRoute("/workflows/tags", fetcher, capabilities());
    await waitForText(container, "Could not load workflow tags");
    options.fail = false;
    await click(button(container, "Retry"));
    await waitForText(container, "Region");
  });

  it("distinguishes an unsupported backend without reading its tag catalog", async () => {
    const { fetcher, reads } = fixture();
    const { container } = await renderWorkflowRoute("/workflows/tags", fetcher, pagedDefinitionCapabilities());
    await waitForText(container, "Workflow tags unavailable");
    expect(reads).toEqual([]);
  });

  it("discards an old tag detail after navigating to the create page", async () => {
    let resolve!: (value: Response) => void;
    const { fetcher, reads } = fixture({ deferred: new Promise<Response>(done => { resolve = done; }) });
    const { container } = await renderWorkflowRoute("/workflows/tags/region", fetcher, capabilities(), { followNavigation: true });
    await vi.waitFor(() => expect(reads).toContain("/tags/region"));
    window.history.pushState({}, "", "/workflows/tags/new");
    window.dispatchEvent(new PopStateEvent("popstate"));
    await waitForText(container, "Create workflow tag");
    resolve(response(tag));
    await new Promise(done => setTimeout(done, 20));
    expect(container.querySelector<HTMLInputElement>('[aria-label="Tag name"]')?.value).toBe("");
    expect(container.textContent).not.toContain("Edit workflow tag");
  });
  it("retains unsaved tag fields when a workspace tab is restored offline", async () => {
    const options = { fail: false };
    const { fetcher, reads } = fixture(options);
    vi.stubGlobal("fetch", (input: RequestInfo | URL) => String(input).includes("/capabilities") ? Promise.resolve(response(capabilities())) : fetcher(input));
    window.history.replaceState({}, "", "/workflows/tags/region");
    const context = createEndpointContext("https://server.example/");
    const container = document.createElement("div"); document.body.append(container);
    const root = createRoot(container);
    const render = (mode: "visible" | "hidden") => flushSync(() => root.render(<Activity mode={mode}><WorkflowTagsRoute context={context} view="edit" navigate={() => undefined} /></Activity>));
    try {
      render("visible");
      await waitForText(container, "Edit workflow tag");
      await fill(container.querySelector('[aria-label="Tag name"]'), "Unsaved region");
      render("hidden");
      await new Promise(done => setTimeout(done, 20));
      options.fail = true;
      render("visible");
      await new Promise(done => setTimeout(done, 20));
      expect(container.querySelector<HTMLInputElement>('[aria-label="Tag name"]')?.value).toBe("Unsaved region");
      expect(reads.filter(path => path === "/tags/region")).toHaveLength(1);
    } finally { flushSync(() => root.unmount()); container.remove(); }
  });

  it("finishes a pending catalog search while its workspace tab is hidden", async () => {
    const { fetcher } = fixture();
    let resolve!: (value: Response) => void;
    const searchResult = new Promise<Response>(done => { resolve = done; });
    vi.stubGlobal("fetch", (input: RequestInfo | URL) => String(input).includes("/capabilities") ? Promise.resolve(response(capabilities())) : String(input).includes("search=missing") ? searchResult : fetcher(input));
    window.history.replaceState({}, "", "/workflows/tags");
    const context = createEndpointContext("https://server.example/");
    const container = document.createElement("div"); document.body.append(container);
    const root = createRoot(container);
    const render = (mode: "visible" | "hidden") => flushSync(() => root.render(<Activity mode={mode}><WorkflowTagsRoute context={context} view="list" navigate={() => undefined} /></Activity>));
    try {
      render("visible");
      await waitForText(container, "Region");
      await fill(container.querySelector('[aria-label="Search workflow tags"]'), "missing");
      await click(button(container, "Search"));
      render("hidden");
      await new Promise(done => setTimeout(done, 20));
      resolve(response({ items: [], permissions: { canManageTags: true } }));
      await new Promise(done => setTimeout(done, 20));
      render("visible");
      await waitForText(container, "No matching tags");
      expect(button(container, "Search")?.hasAttribute("disabled")).toBe(false);
    } finally { flushSync(() => root.unmount()); container.remove(); }
  });
});
