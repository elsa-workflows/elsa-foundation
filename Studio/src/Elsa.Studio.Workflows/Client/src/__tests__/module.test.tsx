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
      expect.objectContaining({ id: "workflows", path: "/workflows/definitions" })
    ]);
    expect(api.routes.list()).toEqual([
      expect.objectContaining({ id: "workflows-definitions", path: "/workflows/definitions" })
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
    routes: registry()
  };
}

async function renderRegisteredRoute() {
  window.history.replaceState({}, "", "/workflows/definitions");
  const api = testApi();
  register(api);
  const route = api.routes.list()[0];
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
}

function buttonByText(container: HTMLElement, text: string) {
  return Array.from(container.querySelectorAll("button"))
    .find(button => button.textContent?.trim() === text) ?? null;
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

function registry<T>(): StudioContributionRegistry<T> {
  const items: T[] = [];
  return {
    add: item => items.push(item),
    list: () => [...items]
  };
}
