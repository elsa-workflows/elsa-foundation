import { authSessionEndedEvent, authSessionStartedEvent } from "@elsa-workflows/studio-sdk";
import { describe, expect, it, onTestFinished, vi } from "vitest";
import { flowchartActivity } from "./fixtures/workflowCatalog";
import { setDialogs } from "../workflow-editor/dialogs";
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
  it("keeps the current backend's search after a retired saved-view deletion completes", async () => {
    let finishDelete: ((value: Response) => void) | undefined;
    onTestFinished(() => finishDelete?.(new Response(null, { status: 204 })));
    const requests: URL[] = [];
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = new URL(String(input));
      if (url.pathname.endsWith("views/previous-view") && init?.method === "DELETE") return new Promise<Response>(resolve => { finishDelete = resolve; });
      if (url.pathname.endsWith("/views")) return response({ items: url.host === "server.example" ? [{ id: "previous-view", name: "Previous view", shared: false, ownerId: "owner", query: { searchTerm: "original" }, columns: [], revision: "v1", canEdit: true, canDelete: true }] : [] });
      if (url.pathname.endsWith("definition-pages")) {
        requests.push(url);
        return response({ items: [definition({ name: url.host === "server.example" ? "Previous backend workflow" : "Current backend workflow" })] });
      }
      return response({ items: [] });
    });
    const capabilities = pagedDefinitionCapabilities({ queryOptions: true });
    capabilities.capabilities[0].links.push({ rel: "workflow-views", href: "design/workflows/views" }, { rel: "workflow-view", href: "design/workflows/views/{viewId}", templated: true });
    const { container, updateEndpoint } = await renderWorkflowRoute("/workflows/definitions?search=original&view=previous-view", fetchMock, capabilities);
    await waitForCondition(() => expect(button(container, "Save view")).not.toBeNull());
    await click(button(container, "Save view"));
    await waitForCondition(() => expect(button(container, "Delete saved view")).not.toBeNull());
    await click(button(container, "Delete saved view"));
    await waitForCondition(() => expect(finishDelete).toBeTypeOf("function"));
    updateEndpoint("https://current.example/");
    await click(button(container, "Refresh"));
    await waitForText(container, "Current backend workflow");
    await fill(container.querySelector<HTMLInputElement>('input[aria-label="Search all workflows"]'), "current search");
    await waitForCondition(() => expect(requests.at(-1)?.searchParams.get("searchTerm")).toBe("current search"));
    finishDelete?.(new Response(null, { status: 204 }));
    await new Promise(resolve => setTimeout(resolve, 0));
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(new URLSearchParams(window.location.search).get("search")).toBe("current search");
  }, 20_000);

  it("keeps the current folder scope after a retired folder deletion completes", async () => {
    let finishDelete: ((value: Response) => void) | undefined;
    onTestFinished(() => finishDelete?.(new Response(null, { status: 204 })));
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = new URL(String(input));
      if (url.pathname.endsWith("folders/same-folder") && init?.method === "DELETE") return new Promise<Response>(resolve => { finishDelete = resolve; });
      if (url.pathname.endsWith("folders/same-folder")) return response({ folder: { id: "same-folder", name: "Selected folder", parentId: null, revision: "r1" }, ancestors: [] });
      if (url.pathname.endsWith("definition-pages")) return response({ items: [definition({ name: url.host === "server.example" ? "Previous backend workflow" : "Current backend workflow" })] });
      return response({ items: [] });
    });
    const capabilities = pagedDefinitionCapabilities({ folders: true });
    capabilities.capabilities[0].links.push({ rel: "workflow-folder-delete-empty", href: "design/workflows/folders/{folderId}", templated: true });
    const { container, updateEndpoint } = await renderWorkflowRoute("/workflows/definitions?folderId=same-folder", fetchMock, capabilities);
    setDialogs({ confirm: async () => true, prompt: async () => null, alert: async () => undefined });
    await waitForCondition(() => expect(button(container, "Delete")).not.toBeNull());
    await click(button(container, "Delete"));
    await waitForCondition(() => expect(finishDelete).toBeTypeOf("function"));
    updateEndpoint("https://current.example/");
    await click(button(container, "Refresh"));
    await waitForText(container, "Current backend workflow");
    await click(container.querySelector('[role="treeitem"][data-tree-key="unfiled"]'));
    expect(new URLSearchParams(window.location.search).get("unfiled")).toBe("true");
    finishDelete?.(new Response(null, { status: 204 }));
    await new Promise(resolve => setTimeout(resolve, 0));
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(new URLSearchParams(window.location.search).get("unfiled")).toBe("true");
  }, 20_000);

  it("does not open a retired backend's created definition in the current backend", async () => {
    let finishCreate: ((value: Response) => void) | undefined;
    const created = () => response({ definition: definition({ id: "old-created", name: "Created on previous backend" }), draft: null, versions: [] });
    onTestFinished(() => finishCreate?.(created()));
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = new URL(String(input));
      if (url.pathname.endsWith("/definitions") && init?.method === "POST") return new Promise<Response>(resolve => { finishCreate = resolve; });
      if (url.pathname.includes("/activities")) return response({ activities: [flowchartActivity()] });
      return response({ items: [definition({ name: url.host === "server.example" ? "Previous backend workflow" : "Current backend workflow" })] });
    });
    const { container, updateEndpoint } = await renderWorkflowRoute("/workflows/definitions", fetchMock, pagedDefinitionCapabilities());
    await waitForText(container, "Previous backend workflow");
    await click(button(container, "Create workflow"));
    await waitForCondition(() => expect(container.querySelector<HTMLInputElement>('input[aria-label="Flowchart"]')?.disabled).toBe(false));
    await fill(container.querySelector<HTMLInputElement>('input[aria-label="Display name"]'), "Created on previous backend");
    await click(container.querySelector('input[aria-label="Flowchart"]'));
    await click(button(container, "Create"));
    await waitForCondition(() => expect(finishCreate).toBeTypeOf("function"));
    updateEndpoint("https://current.example/");
    await click(button(container, "Refresh"));
    await waitForText(container, "Current backend workflow");
    finishCreate?.(created());
    await new Promise(resolve => setTimeout(resolve, 0));
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(new URLSearchParams(window.location.search).has("definition")).toBe(false);
    expect(container.textContent).toContain("Current backend workflow");
    expect(container.querySelector(".wf-editor")).toBeNull();
  }, 20_000);

  it("ignores a retired move completion while a new backend's move dialog is open", async () => {
    let finishMove: ((value: Response) => void) | undefined;
    onTestFinished(() => finishMove?.(new Response(null, { status: 204 })));
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = new URL(String(input));
      if (url.pathname.endsWith("definitions/move") && init?.method === "POST") return new Promise<Response>(resolve => { finishMove = resolve; });
      if (url.pathname.endsWith("definition-pages")) return response({ items: [definition({ id: url.host === "server.example" ? "old" : "current", name: url.host === "server.example" ? "Previous backend workflow" : "Current backend workflow" })] });
      return response({ items: [] });
    });
    const capabilities = pagedDefinitionCapabilities({ folders: true });
    capabilities.capabilities[0].links.push({ rel: "workflow-definition-folder-move", href: "design/workflows/definitions/move" });
    const { api, container, updateEndpoint } = await renderWorkflowRoute("/workflows/definitions", fetchMock, capabilities);
    await waitForText(container, "Previous backend workflow");
    await click(row(container, "Previous backend workflow")!.querySelector("input[type='checkbox']"));
    await click(button(container, "Move to folder"));
    await waitForCondition(() => expect(button(container, "Move")).not.toBeNull());
    await click(button(container, "Move"));
    await waitForCondition(() => expect(finishMove).toBeTypeOf("function"));
    updateEndpoint("https://current.example/");
    await click(button(container, "Refresh"));
    await waitForText(container, "Current backend workflow");
    await click(row(container, "Current backend workflow")!.querySelector("input[type='checkbox']"));
    await click(button(container, "Move to folder"));
    await waitForCondition(() => expect(button(container, "Move")).not.toBeNull());
    finishMove?.(new Response(null, { status: 204 }));
    await new Promise(resolve => setTimeout(resolve, 0));
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(container.querySelector('[role="dialog"]')).not.toBeNull();
    expect(container.textContent).toContain("1 selected");
    expect(api.notifications!.notify).not.toHaveBeenCalled();
  }, 20_000);

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
    expect(container.querySelector(".wf-folder-actions")).toBeNull();
    expect(container.textContent).not.toContain("Previous context folder");
    await waitForCondition(() => expect(detailRequests).toBe(2));

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
          ? response({ items: [definition({ id: "definition-second", name: pageRequests.length > 3 ? "Returned second page workflow" : "Second page workflow" })], nextContinuationToken: "opaque-page-three" })
          : response({ items: [definition({ name: url.searchParams.get("pageSize") === "25" ? "Resized first page workflow" : "First page workflow" })], nextContinuationToken: "opaque-page-two" });
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
    await waitForText(container, "Resized first page workflow");
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
    await waitForText(container, "Returned second page workflow");
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
    await waitForText(container, "deleted page one");

    await click(button(container, "Next"));
    await waitForCondition(() => expect(new URL(pageRequests.at(-1)!).searchParams.get("continuationToken")).toBe("opaque-page-two"));
    await waitForText(container, "deleted page two");
    await fill(container.querySelector<HTMLInputElement>(".studio-search input"), "new search");
    await waitForCondition(() => expect(new URL(pageRequests.at(-1)!).searchParams.get("searchTerm")).toBe("new search"));
    expect(new URL(pageRequests.at(-1)!).searchParams.has("continuationToken")).toBe(false);
    await waitForText(container, "new search page one");

    await click(button(container, "Next"));
    await waitForCondition(() => expect(new URL(pageRequests.at(-1)!).searchParams.has("continuationToken")).toBe(true));
    await waitForText(container, "new search page two");
    await select(container.querySelector<HTMLSelectElement>(".wf-page-size select"), "25");
    await waitForCondition(() => expect(new URL(pageRequests.at(-1)!).searchParams.get("pageSize")).toBe("25"));
    expect(new URL(pageRequests.at(-1)!).searchParams.has("continuationToken")).toBe(false);
    await waitForText(container, "new search page one");
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
