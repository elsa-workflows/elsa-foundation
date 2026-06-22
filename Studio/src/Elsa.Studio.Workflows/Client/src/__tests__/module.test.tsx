import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { ElsaStudioModuleApi, StudioContributionRegistry } from "@elsa-workflows/studio-sdk";
import { register } from "../module";

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("workflows module", () => {
  it("registers navigation and definitions route", () => {
    const api = testApi();

    register(api);

    expect(api.navigation.list()).toEqual([
      expect.objectContaining({ id: "workflows", path: "/workflows/definitions", activePathPrefix: "/workflows" })
    ]);
    expect(api.routes.list()).toEqual([
      expect.objectContaining({ id: "workflows-definitions", path: "/workflows/definitions" }),
      expect.objectContaining({ id: "workflows-executables", path: "/workflows/executables" }),
      expect.objectContaining({ id: "workflows-instances", path: "/workflows/instances" })
    ]);
  });

  it("renders active definition actions and soft-deletes with confirmation", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (init?.method === "DELETE") return response(null, 204);
      expect(url).toContain("state=active");
      return response({ definitions: [definition()] });
    });
    vi.stubGlobal("fetch", fetchMock);
    vi.stubGlobal("confirm", vi.fn(() => true));
    const { container, unmount } = await renderRegisteredRoute();

    await waitForText(container, "Latest version");

    expect(container.textContent).toContain("Latest version");
    expect(container.textContent).toContain("Open");
    expect(container.textContent).toContain("Artifacts");
    expect(container.textContent).toContain("Delete");

    await click(buttonByText(container, "Delete"));
    await flushPromises();

    expect(window.confirm).toHaveBeenCalledWith(expect.stringContaining("Delete workflow definition"));
    expect(fetchMock).toHaveBeenCalledWith("https://server.example/_elsa/workflow-management/definitions/definition-1", { method: "DELETE" });

    await unmount();
  });

  it("renders deleted definition actions and calls restore and permanent delete endpoints", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (init?.method === "POST" || init?.method === "DELETE") return response(null, 204);
      return response({ definitions: url.includes("state=deleted") ? [definition({ deletedAt: "2026-06-18T01:00:00Z" })] : [] });
    });
    vi.stubGlobal("fetch", fetchMock);
    vi.stubGlobal("confirm", vi.fn(() => true));
    const { container, unmount } = await renderRegisteredRoute();

    await waitForText(container, "No active workflow definitions found.");
    await click(buttonByText(container, "Deleted"));
    await waitForText(container, "Delete permanently");

    expect(container.textContent).toContain("Restore");
    expect(container.textContent).toContain("Delete permanently");

    await click(buttonByText(container, "Restore"));
    await flushPromises();

    expect(fetchMock).toHaveBeenCalledWith(
      "https://server.example/_elsa/workflow-management/definitions/definition-1/restore",
      expect.objectContaining({ method: "POST" })
    );

    await click(buttonByText(container, "Delete permanently"));
    await flushPromises();

    expect(window.confirm).toHaveBeenCalledWith(expect.stringContaining("Permanently delete workflow definition"));
    expect(fetchMock).toHaveBeenCalledWith("https://server.example/_elsa/workflow-management/definitions/definition-1/permanent", { method: "DELETE" });

    await unmount();
  });

  it("requests paged definitions and navigates between pages", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      expect(url.searchParams.get("pageSize")).toBe("10");

      if (url.searchParams.get("page") === "2") {
        return response({
          definitions: [definition({ id: "definition-2", name: "Second page" })],
          page: 2,
          pageSize: 10,
          totalCount: 11
        });
      }

      return response({
        definitions: [definition()],
        page: 1,
        pageSize: 10,
        totalCount: 11
      });
    });
    vi.stubGlobal("fetch", fetchMock);
    const { container, unmount } = await renderRegisteredRoute();

    await waitForText(container, "Page 1 of 2");
    await click(buttonByText(container, "Next"));
    await waitForText(container, "Second page");

    expect(String(fetchMock.mock.calls[0]?.[0])).toContain("page=1");
    expect(String(fetchMock.mock.calls.at(-1)?.[0])).toContain("page=2");

    await unmount();
  });

  it("opens the workflow editor when a definition row is clicked", async () => {
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes("/activities")) return response({ activities: [] });
      if (url.includes("/definitions/definition-1")) return response({ definition: definition(), draft: null, versions: [] });
      return response({ definitions: [definition()] });
    }));
    const { container, unmount } = await renderRegisteredRoute();

    await waitForText(container, "Hello World");
    await click(rowByLabel(container, "Open workflow definition Hello World"));

    expect(window.location.pathname).toBe("/workflows/definitions");
    expect(new URLSearchParams(window.location.search).get("definition")).toBe("definition-1");

    await unmount();
  });

  it("renders the activity palette as a category tree with activity descriptions", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes("/activities")) return response({ activities: [
        activity({
          activityVersionId: "write-line-v1",
          activityTypeKey: "Elsa.Activities.Primitives.Activities.WriteLine",
          category: "Primitives",
          displayName: "Write Line",
          description: "Writes a line to the console."
        }),
        activity({
          activityVersionId: "flowchart-v1",
          activityTypeKey: "Elsa.Activities.Flowchart.Activities.Flowchart",
          category: "Composition",
          displayName: "Flowchart",
          description: "Runs activities as a graph."
        })
      ] });
      if (url.includes("/definitions/definition-1")) return response({ definition: definition(), draft: workflowDraft(), versions: [] });
      return response({ definitions: [definition()] });
    }));
    const { container, unmount } = await renderRegisteredRoute("/workflows/definitions?definition=definition-1");

    await waitForText(container, "Write Line");

    const tree = container.querySelector("[role='tree'][aria-label='Available activities']");
    expect(tree).toBeTruthy();
    expect(tree?.textContent).toContain("Composition");
    expect(tree?.textContent).toContain("Primitives");
    expect(tree?.textContent).toContain("Write Line");
    expect(tree?.textContent).not.toContain("Elsa.Activities.Primitives.Activities.WriteLine");

    const writeLine = Array.from(container.querySelectorAll<HTMLButtonElement>(".wf-palette-activity"))
      .find(button => button.textContent?.includes("Write Line"));
    expect(writeLine?.getAttribute("role")).toBe("treeitem");
    expect(writeLine?.getAttribute("title")).toBe("Writes a line to the console.");
    expect(writeLine?.textContent).toContain("Writes a line to the console.");

    const primitives = Array.from(container.querySelectorAll<HTMLButtonElement>(".wf-palette-category-toggle"))
      .find(button => button.textContent?.includes("Primitives"));
    expect(primitives?.getAttribute("aria-expanded")).toBe("true");
    await click(primitives ?? null);

    expect(primitives?.getAttribute("aria-expanded")).toBe("false");
    expect(tree?.textContent).not.toContain("Write Line");

    await unmount();
  });

  it("opens a filtered executable artifacts view for a definition", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => response({ definitions: [definition()] })));
    const { container, unmount } = await renderRegisteredRoute();

    await waitForText(container, "Hello World");
    await click(buttonByText(container, "Artifacts"));

    expect(window.location.pathname).toBe("/workflows/executables");
    expect(new URLSearchParams(window.location.search).get("definition")).toBe("definition-1");

    await unmount();
  });

  it("creates a workflow definition from the sequence action", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (init?.method === "POST" && url.endsWith("/_elsa/workflow-management/definitions")) {
        expect(JSON.parse(String(init.body))).toEqual({
          name: "Customer onboarding",
          description: "Creates the first customer workflow.",
          rootKind: "sequence"
        });
        return response({ definition: definition({ id: "created-definition", name: "Customer onboarding" }), draft: null, versions: [] });
      }
      if (url.includes("/activities")) return response({ activities: [
        activity({ activityVersionId: "flowchart-v1", activityTypeKey: "Elsa.Activities.Flowchart", category: "Composition", displayName: "Flowchart" }),
        activity({ activityVersionId: "sequence-v1", activityTypeKey: "Elsa.Activities.Sequence", category: "Composition", displayName: "Sequence" }),
        activity({ activityVersionId: "write-line-v1", activityTypeKey: "Elsa.Activities.WriteLine", category: "Primitives", displayName: "Write Line" })
      ] });
      if (url.includes("/definitions/created-definition")) return response({ definition: definition({ id: "created-definition", name: "Customer onboarding" }), draft: null, versions: [] });
      return response({ definitions: [definition()] });
    });
    vi.stubGlobal("fetch", fetchMock);
    const { container, unmount } = await renderRegisteredRoute();

    await waitForText(container, "Hello World");
    await click(buttonByText(container, "Create"));
    await waitForText(container, "Create Workflow");
    await waitForText(container, "Write Line");
    expect(selectByLabel(container, "Root activity")?.querySelector("optgroup[label='Composite roots']")).toBeTruthy();
    expect(selectByLabel(container, "Root activity")?.querySelector("optgroup[label='Primitives']")).toBeTruthy();

    await fill(inputByLabel(container, "Display name"), "Customer onboarding");
    await fill(textareaByLabel(container, "Description"), "Creates the first customer workflow.");
    await select(selectByLabel(container, "Root activity"), "sequence");
    await click(buttonByText(dialog(container), "Create"));

    expect(fetchMock).toHaveBeenCalledWith(
      "https://server.example/_elsa/workflow-management/definitions",
      expect.objectContaining({ method: "POST" })
    );
    await waitForUrlParam("definition", "created-definition");

    await unmount();
  });

  it("selects multiple workflow definition rows and clears selection", async () => {
    const fetchMock = vi.fn(async () => response({
      definitions: [
        definition(),
        definition({ id: "definition-2", name: "Second workflow" })
      ],
      page: 1,
      pageSize: 10,
      totalCount: 2
    }));
    vi.stubGlobal("fetch", fetchMock);
    const { container, unmount } = await renderRegisteredRoute();

    await waitForText(container, "Second workflow");

    await click(checkboxByLabel(container, "Select workflow definition Hello World"));
    await waitForText(container, "1 selected");

    await click(checkboxByLabel(container, "Select workflow definition Second workflow"));
    await waitForText(container, "2 selected");

    await click(buttonByText(container, "Clear selection"));
    await waitForTextToDisappear(container, "2 selected");

    await click(checkboxByLabel(container, "Select visible workflow definitions"));
    await waitForText(container, "2 selected");

    await unmount();
  });

  it("renders workflow executables and runs an artifact", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (init?.method === "POST") return response(null, 204);
      expect(url).toBe("https://server.example/_demo/workflows/executables");
      return response([executable({ rootActivityType: "Elsa.Activities.Flowchart.Activities.Flowchart" })]);
    });
    vi.stubGlobal("fetch", fetchMock);
    const { container, unmount } = await renderRegisteredRoute("/workflows/executables");

    await waitForText(container, "artifact-1");
    expect(container.textContent).toContain("Executables");
    expect(container.textContent).toContain("Definitions");
    expect(container.textContent).toContain("Instances");
    expect(container.textContent).toContain("Flowchart");
    expect(container.textContent).not.toContain("Elsa.Activities.Flowchart.Activities.Flowchart");

    await click(buttonByText(container, "Run"));

    expect(fetchMock).toHaveBeenCalledWith(
      "https://server.example/_elsa/workflow-management/executables/artifact-1/run",
      expect.objectContaining({ method: "POST" })
    );

    await unmount();
  });

  it("filters workflow executables by definition query parameter", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => response([
      executable({ artifactId: "artifact-visible", definitionId: "definition-1" }),
      executable({ artifactId: "artifact-hidden", definitionId: "definition-2", sourceId: "definition-2" })
    ])));
    const { container, unmount } = await renderRegisteredRoute("/workflows/executables?definition=definition-1");

    await waitForText(container, "artifact-visible");
    expect(container.textContent).toContain("Definition definition-1");
    expect(container.textContent).not.toContain("artifact-hidden");

    await unmount();
  });

  it("renders the workflow instances placeholder route", async () => {
    const { container, unmount } = await renderRegisteredRoute("/workflows/instances");

    await waitForText(container, "Workflow instance history will appear here");
    expect(container.textContent).toContain("Instances");

    await unmount();
  });
});

function testApi(): ElsaStudioModuleApi {
  return {
    backend: {
      baseUrl: "https://server.example/",
      http: {
        getJson: async () => ({}),
        postJson: async () => ({})
      }
    },
    navigation: registry(),
    routes: registry(),
    ai: {
      promptActions: registry(),
      dispatchPrompt: vi.fn()
    }
  };
}

async function renderRegisteredRoute(path = "/workflows/definitions") {
  window.history.replaceState({}, "", path);
  const api = testApi();
  register(api);
  const routePath = new URL(path, window.location.origin).pathname;
  const route = api.routes.list().find(candidate => candidate.path === routePath) ?? api.routes.list()[0];
  const Component = route.component;
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);

  flushSync(() => {
    root.render(<Component />);
  });

  return {
    container,
    unmount: async () => {
      flushSync(() => root.unmount());
      container.remove();
    }
  };
}

function definition(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    id: "definition-1",
    name: "Hello World",
    description: "Writes text.",
    createdAt: "2026-06-18T01:00:00Z",
    lastModifiedAt: "2026-06-18T01:10:00Z",
    deletedAt: null,
    draftId: "draft-1",
    latestVersionId: "version-1",
    latestVersion: "1.0.0",
    versionCount: 1,
    ...overrides
  };
}

function executable(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    artifactId: "artifact-1",
    artifactVersion: "1.0.0",
    artifactHash: "sha256:abc",
    definitionId: "definition-1",
    definitionVersionId: "version-1",
    createdAt: "2026-06-18T01:00:00Z",
    publishedAt: "2026-06-18T01:10:00Z",
    sourceKind: "definition",
    sourceId: "definition-1",
    sourceVersion: "1.0.0",
    rootActivityType: "Sequence",
    rootActivityVersion: "1.0.0",
    nodeCount: 3,
    resumeTargetCount: 0,
    ...overrides
  };
}

function workflowDraft(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    id: "draft-1",
    definitionId: "definition-1",
    sourceVersionId: null,
    state: {
      variables: [],
      rootActivity: null,
      inputs: [],
      outputs: []
    },
    layout: [],
    validationErrors: [],
    ...overrides
  };
}

function activity(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    activityVersionId: "activity-1",
    activityTypeKey: "Elsa.Activities.Activity",
    version: "1.0.0",
    category: "General",
    displayName: "Activity",
    description: null,
    executionType: "Action",
    inputs: [],
    outputs: [],
    designFacets: [],
    ...overrides
  };
}

function response(body: unknown, status = 200) {
  return new Response(body ? JSON.stringify(body) : "", {
    status,
    headers: { "Content-Type": "application/json" }
  });
}

async function click(element: Element | null) {
  if (!(element instanceof HTMLElement)) throw new Error("Element not found");
  element.click();
  await flushPromises();
  flushSync(() => {});
}

function buttonByText(container: HTMLElement, text: string) {
  return Array.from(container.querySelectorAll("button"))
    .find(button => button.textContent?.trim() === text) ?? null;
}

function checkboxByLabel(container: HTMLElement, label: string) {
  return Array.from(container.querySelectorAll<HTMLInputElement>("input[type='checkbox']"))
    .find(input => input.getAttribute("aria-label") === label) ?? null;
}

function inputByLabel(container: HTMLElement, label: string) {
  return Array.from(container.querySelectorAll<HTMLInputElement>("input"))
    .find(input => input.getAttribute("aria-label") === label) ?? null;
}

function textareaByLabel(container: HTMLElement, label: string) {
  return Array.from(container.querySelectorAll<HTMLTextAreaElement>("textarea"))
    .find(input => input.getAttribute("aria-label") === label) ?? null;
}

function selectByLabel(container: HTMLElement, label: string) {
  return Array.from(container.querySelectorAll<HTMLSelectElement>("select"))
    .find(input => input.getAttribute("aria-label") === label) ?? null;
}

function dialog(container: HTMLElement) {
  const element = container.querySelector<HTMLElement>(".wf-dialog");
  if (!element) throw new Error("Dialog not found");
  return element;
}

function rowByLabel(container: HTMLElement, label: string) {
  return Array.from(container.querySelectorAll<HTMLElement>("[role='row']"))
    .find(row => row.getAttribute("aria-label") === label) ?? null;
}

async function fill(element: HTMLInputElement | HTMLTextAreaElement | null, value: string) {
  if (!element) throw new Error("Input not found");
  const prototype = element instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
  const valueSetter = Object.getOwnPropertyDescriptor(prototype, "value")?.set;
  valueSetter?.call(element, value);
  element.dispatchEvent(new Event("input", { bubbles: true }));
  await flushPromises();
  flushSync(() => {});
}

async function select(element: HTMLSelectElement | null, value: string) {
  if (!element) throw new Error("Select not found");
  const valueSetter = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, "value")?.set;
  valueSetter?.call(element, value);
  element.dispatchEvent(new Event("change", { bubbles: true }));
  await flushPromises();
  flushSync(() => {});
}

async function flushPromises() {
  await new Promise(resolve => setTimeout(resolve, 0));
}

async function waitForText(container: HTMLElement, text: string) {
  for (let attempt = 0; attempt < 20; attempt += 1) {
    await flushPromises();
    if (container.textContent?.includes(text)) return;
  }

  throw new Error(`Timed out waiting for text: ${text}`);
}

async function waitForTextToDisappear(container: HTMLElement, text: string) {
  for (let attempt = 0; attempt < 20; attempt += 1) {
    await flushPromises();
    if (!container.textContent?.includes(text)) return;
  }

  throw new Error(`Timed out waiting for text to disappear: ${text}`);
}

async function waitForUrlParam(name: string, value: string) {
  for (let attempt = 0; attempt < 20; attempt += 1) {
    await flushPromises();
    if (new URLSearchParams(window.location.search).get(name) === value) return;
  }

  throw new Error(`Timed out waiting for URL parameter: ${name}=${value}`);
}

function registry<T>(): StudioContributionRegistry<T> {
  const items: T[] = [];
  return {
    add: item => items.push(item),
    list: () => [...items]
  };
}
