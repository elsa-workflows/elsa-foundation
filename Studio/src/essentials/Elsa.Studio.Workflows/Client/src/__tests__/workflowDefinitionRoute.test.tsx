import { authSessionEndedEvent, authSessionStartedEvent } from "@elsa-workflows/studio-sdk";
import { describe, expect, it, onTestFinished, vi } from "vitest";
import {
  button,
  click,
  definition,
  fill,
  pagedDefinitionCapabilities,
  renderWorkflowRoute,
  response,
  row,
  select,
  waitForCondition,
  waitForText,
  workflowEditorResponse
} from "./workflowDefinitionTestHelpers";

async function openSecondPageEditor(fetchMock: typeof fetch) {
  const rendered = await renderWorkflowRoute("/workflows/definitions", fetchMock, pagedDefinitionCapabilities());
  await waitForText(rendered.container, "First page workflow");
  await click(button(rendered.container, "Next"));
  await waitForText(rendered.container, "Second page workflow");
  await click(row(rendered.container, "Second page workflow"));
  await waitForCondition(() => expect(button(rendered.container, "Definitions")).not.toBeNull());
  return rendered;
}

describe("workflow definition route browse session", () => {
  it("clears retained folder actions when the endpoint context is replaced at the same base URL", async () => {
    const folder = { id: "folder-context", parentId: null, name: "Previous context folder", normalizedName: "context", createdAt: "", lastModifiedAt: "" };
    const currentFolder = { ...folder, name: "Current context folder" };
    let detailRequests = 0;
    let resolveDetail: ((value: Response) => void) | undefined;
    onTestFinished(() => resolveDetail?.(response({ folder: currentFolder, ancestors: [] })));
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      if (url.pathname.endsWith("/folders/folder-context")) {
        if (++detailRequests === 1) return response({ folder, ancestors: [] });
        return new Promise<Response>(resolve => { resolveDetail = resolve; });
      }
      return response({ items: [], nextContinuationToken: null });
    });
    const capabilities = pagedDefinitionCapabilities({ folders: true });
    capabilities.capabilities[0].links.push({ rel: "workflow-folder-rename", href: "design/workflows/folders/{folderId}/rename", templated: true });
    const { container, updateEndpoint } = await renderWorkflowRoute(
      "/workflows/definitions?folderId=folder-context", fetchMock, capabilities
    );
    await waitForCondition(() => expect(container.querySelector(".wf-folder-actions")?.getAttribute("aria-label")).toBe("Actions for Previous context folder"));

    updateEndpoint("https://server.example/");
    await waitForCondition(() => expect(detailRequests).toBe(2));
    expect(container.querySelector(".wf-folder-actions")).toBeNull();
    expect(container.textContent).not.toContain("Previous context folder");

    resolveDetail?.(response({ folder: currentFolder, ancestors: [] }));
    await waitForCondition(() => expect(container.querySelector(".wf-folder-actions")?.getAttribute("aria-label")).toBe("Actions for Current context folder"));
  });

  it("returns from an editor to the same cursor page and page size after folder capability resolves", async () => {
    const folder = { id: "folder-operations", parentId: null, name: "Operations", normalizedName: "operations", createdAt: "", lastModifiedAt: "" };
    const pageRequests: string[] = [];
    let folderListRequests = 0;
    let resolveReturnedFolders: ((value: Response) => void) | undefined;
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      if (url.pathname.endsWith("/folders") && url.searchParams.has("pageSize")) {
        folderListRequests++;
        if (folderListRequests === 2) return new Promise<Response>(resolve => { resolveReturnedFolders = resolve; });
        return response({ items: [folder], nextContinuationToken: null });
      }
      if (url.pathname.endsWith("/folders/folder-operations")) return response({ folder, ancestors: [] });
      if (url.pathname.endsWith("/definition-pages")) {
        pageRequests.push(url.toString());
        const token = url.searchParams.get("continuationToken");
        return token === "opaque-page-two"
          ? response({ items: [definition({ id: "definition-second", name: "Second page workflow" })], nextContinuationToken: "opaque-page-three" })
          : response({ items: [definition({ name: "First page workflow" })], nextContinuationToken: "opaque-page-two" });
      }
      if (url.pathname.endsWith("/definitions/definition-second")) {
        return response({
          definition: definition({ id: "definition-second", name: "Second page workflow" }),
          draft: { id: "draft-second", definitionId: "definition-second", state: { variables: [], rootActivity: null, inputs: [], outputs: [] }, layout: [], validationErrors: [] },
          versions: []
        });
      }
      if (url.pathname.includes("/activities/availability/diagnostics")) return response({ items: [], sets: [] });
      if (url.pathname.includes("/activities")) return response({ activities: [] });
      return response({ items: [] });
    });
    const { container } = await renderWorkflowRoute(
      "/workflows/definitions?folderId=folder-operations&state=all&search=approval",
      fetchMock,
      pagedDefinitionCapabilities({ folders: true })
    );

    await waitForText(container, "First page workflow");
    await select(container.querySelector<HTMLSelectElement>(".wf-page-size select"), "25");
    await waitForCondition(() => expect(new URL(pageRequests.at(-1)!).searchParams.get("pageSize")).toBe("25"));
    await click(button(container, "Next"));
    await waitForText(container, "Second page workflow");
    expect(new URL(pageRequests.at(-1)!).searchParams.get("continuationToken")).toBe("opaque-page-two");

    const requestsBeforeEditor = pageRequests.length;
    await click(row(container, "Second page workflow"));
    await waitForCondition(() => expect(button(container, "Definitions")).not.toBeNull());
    expect(requestsBeforeEditor).toBe(3);
    expect(pageRequests).toHaveLength(requestsBeforeEditor);
    await click(button(container, "Definitions"));

    await waitForCondition(() => expect(folderListRequests).toBe(2));
    resolveReturnedFolders?.(response({ items: [folder], nextContinuationToken: null }));
    await waitForCondition(() => expect(pageRequests.length).toBeGreaterThan(requestsBeforeEditor));

    const returnedPage = new URL(pageRequests.at(-1)!);
    expect(returnedPage.searchParams.get("pageSize")).toBe("25");
    expect(returnedPage.searchParams.get("continuationToken")).toBe("opaque-page-two");
    expect(returnedPage.searchParams.get("folderId")).toBe("folder-operations");
    expect(returnedPage.searchParams.get("state")).toBe("all");
    expect(returnedPage.searchParams.get("searchTerm")).toBe("approval");
    await waitForText(container, "Second page workflow");
  });

  it("uses the selected page cursor and rebases changed search, lifecycle, and page-size keys", async () => {
    const pageRequests: string[] = [];
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      if (url.pathname.endsWith("/definition-pages")) {
        pageRequests.push(url.toString());
        const token = url.searchParams.get("continuationToken");
        const current = url.searchParams.get("searchTerm") ?? url.searchParams.get("state") ?? "active";
        return response({
          items: [definition({ name: `${current} ${token ? "page two" : "page one"}` })],
          nextContinuationToken: token ? "opaque-page-three" : "opaque-page-two"
        });
      }
      return response({ items: [] });
    });
    const { container } = await renderWorkflowRoute("/workflows/definitions", fetchMock, pagedDefinitionCapabilities());

    await waitForText(container, "active page one");
    await click(button(container, "Next"));
    await waitForText(container, "active page two");
    expect(new URL(pageRequests.at(-1)!).searchParams.get("continuationToken")).toBe("opaque-page-two");

    await click(button(container, "Deleted"));
    await waitForCondition(() => expect(new URL(pageRequests.at(-1)!).searchParams.get("state")).toBe("deleted"));
    expect(new URL(pageRequests.at(-1)!).searchParams.has("continuationToken")).toBe(false);

    await click(button(container, "Next"));
    await waitForCondition(() => expect(new URL(pageRequests.at(-1)!).searchParams.get("continuationToken")).toBe("opaque-page-two"));
    await fill(container.querySelector<HTMLInputElement>(".wf-search input"), "new search");
    await waitForCondition(() => expect(new URL(pageRequests.at(-1)!).searchParams.get("searchTerm")).toBe("new search"));
    expect(new URL(pageRequests.at(-1)!).searchParams.has("continuationToken")).toBe(false);

    await click(button(container, "Next"));
    await waitForCondition(() => expect(new URL(pageRequests.at(-1)!).searchParams.has("continuationToken")).toBe(true));
    await select(container.querySelector<HTMLSelectElement>(".wf-page-size select"), "25");
    await waitForCondition(() => expect(new URL(pageRequests.at(-1)!).searchParams.get("pageSize")).toBe("25"));
    expect(new URL(pageRequests.at(-1)!).searchParams.has("continuationToken")).toBe(false);
  });

  it("drops the saved cursor after an endpoint change while the editor is open", async () => {
    const requests: string[] = [];
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      requests.push(url.toString());
      if (url.pathname.endsWith("/definition-pages")) {
        const token = url.searchParams.get("continuationToken");
        return response({ items: [definition({ name: token ? "Second page workflow" : "First page workflow" })], nextContinuationToken: token ? null : "old-context-cursor" });
      }
      const editorResponse = workflowEditorResponse(url);
      if (editorResponse) return editorResponse;
      return response({ items: [] });
    });
    const { container, updateEndpoint } = await openSecondPageEditor(fetchMock);

    updateEndpoint("https://new-server.example/");
    await click(button(container, "Definitions"));

    await waitForCondition(() => expect(requests.some(url => url.startsWith("https://new-server.example/") && url.includes("definition-pages"))).toBe(true));
    const newEndpointRequests = requests
      .filter(url => url.startsWith("https://new-server.example/") && url.includes("definition-pages"))
      .map(url => new URL(url));
    expect(newEndpointRequests.length).toBeGreaterThan(0);
    expect(newEndpointRequests.every(request => !request.searchParams.has("continuationToken"))).toBe(true);
    expect(row(container, "Second page workflow")).toBeNull();
    await waitForText(container, "First page workflow");
  });

  it.each([401, 403])("clears protected rows and retries the current query from page one after HTTP %i", async status => {
    const requests: string[] = [];
    let pageTwoFailed = false;
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      if (url.pathname.endsWith("/definition-pages")) {
        requests.push(url.toString());
        const token = url.searchParams.get("continuationToken");
        if (token && !pageTwoFailed) {
          pageTwoFailed = true;
          return response("Access denied.", status);
        }
        const name = token ? "Unexpected protected page two" : requests.length === 1 ? "Initial approval workflow" : "Recovered approval workflow";
        return response({ items: [definition({ name })], nextContinuationToken: token ? null : "approval-page-two" });
      }
      return response({ items: [] });
    });
    const { container } = await renderWorkflowRoute("/workflows/definitions?search=approval", fetchMock, pagedDefinitionCapabilities());

    await waitForText(container, "Initial approval workflow");
    await click(button(container, "Next"));
    await waitForCondition(() => expect(container.querySelector("[role='alert']")?.textContent).toContain("Couldn't load workflow definitions"));
    expect(row(container, "Recovered approval workflow")).toBeNull();
    expect(button(container, "Next")).toBeNull();
    expect(requests).toHaveLength(2);
    await click(button(container, "Refresh"));
    await waitForCondition(() => expect(requests).toHaveLength(3));
    const retry = new URL(requests.at(-1)!);
    expect(retry.searchParams.get("searchTerm")).toBe("approval");
    expect(retry.searchParams.has("continuationToken")).toBe(false);
    await waitForText(container, "Recovered approval workflow");
  });

  it.each([
    ["auth start", authSessionStartedEvent],
    ["auth end", authSessionEndedEvent]
  ])("drops the saved cursor after public %s while the editor is open", async (_label, eventName) => {
    const requests: string[] = [];
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      if (url.pathname.endsWith("/definition-pages")) {
        requests.push(url.toString());
        const token = url.searchParams.get("continuationToken");
        return response({ items: [definition({ name: token ? "Second page workflow" : "First page workflow" })], nextContinuationToken: token ? null : "auth-page-two" });
      }
      const editorResponse = workflowEditorResponse(url);
      if (editorResponse) return editorResponse;
      return response({ items: [] });
    });
    const { container } = await openSecondPageEditor(fetchMock);

    window.dispatchEvent(new Event(eventName));
    await click(button(container, "Definitions"));
    await waitForCondition(() => expect(requests).toHaveLength(3));
    const returnedRequest = new URL(requests.at(-1)!);
    expect(returnedRequest.searchParams.has("continuationToken")).toBe(false);
    expect(row(container, "Second page workflow")).toBeNull();
    await waitForText(container, "First page workflow");
  });
});
