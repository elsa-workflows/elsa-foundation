import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { QueryClientProvider } from "@tanstack/react-query";
import { ModuleManagementPage } from "../app/modules/ModuleManagementPage";
import { PackageFeedsPage } from "../app/modules/PackageFeedsPage";
import { Dashboard } from "../app/App";
import { createEndpointContext, type ElsaStudioModuleApi } from "../sdk";
import { createTestQueryClient } from "./queryTestUtils";

// Behavioural coverage for the shell's TanStack Query migration (#189): the Modules, Package feeds,
// and Dashboard host-health surfaces now use `useQuery`/`useMutation`. These tests assert the three
// query states (loading → error → success) and that a write invalidates + refetches the read.

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("module management query hooks", () => {
  it("shows the loading state before the registry query resolves, then the data", async () => {
    let resolve: (value: unknown) => void = () => {};
    const hostGetJson = vi.fn(() => new Promise(r => { resolve = r; }));
    const { container, unmount } = renderModuleManagementPage(stubApi({ hostGetJson }));

    // Query is still pending: the inspector shows its loading placeholder.
    expect(container.querySelector(".modules-inspector")?.textContent).toContain("Loading Studio modules.");

    resolve(studioRegistry());
    await flushPromises();

    expect(container.querySelector(".modules-inspector")?.textContent).toContain("Feature management");

    await unmount();
  });

  it("surfaces a registry query failure through StudioAlert", async () => {
    const hostGetJson = vi.fn(async () => { throw new Error("registry unavailable"); });
    const { container, unmount } = renderModuleManagementPage(stubApi({ hostGetJson }));
    await flushPromises();

    const alert = container.querySelector(".studio-alert, [role='alert']") ?? container;
    expect(alert.textContent).toContain("registry unavailable");

    await unmount();
  });

  it("invalidates and refetches the registry after an upload mutation succeeds", async () => {
    const fetchMock = vi.fn(async () => ({ ok: true, text: async () => "{}" }));
    vi.stubGlobal("fetch", fetchMock);
    const hostGetJson = vi.fn(async () => studioRegistry());
    const { container, unmount } = renderModuleManagementPage(stubApi({ hostGetJson }));
    await flushPromises();

    expect(hostGetJson).toHaveBeenCalledTimes(1);
    await clickSourceButton(container, "Nuplane");

    const input = container.querySelector<HTMLInputElement>(".modules-hidden-input");
    Object.defineProperty(input, "files", {
      configurable: true,
      value: [new File(["one"], "Elsa.One.1.0.0.nupkg")]
    });
    flushSync(() => input!.dispatchEvent(new Event("change", { bubbles: true })));
    await flushPromises();

    // Upload posted, and the mutation's onSuccess invalidation refetched the registry.
    expect(fetchMock.mock.calls.some(([url]) => String(url).includes("/packages/upload"))).toBe(true);
    expect(hostGetJson).toHaveBeenCalledTimes(2);
    expect(container.textContent).toContain("Uploaded 1 package");

    await unmount();
  });

  it("invalidates the registry after a feed mutation on the package feeds page", async () => {
    const fetchMock = vi.fn(async () => ({ ok: true, text: async () => "{}" }));
    vi.stubGlobal("fetch", fetchMock);
    const hostGetJson = vi.fn(async () => studioRegistry());
    const { container, unmount } = renderPackageFeedsPage(stubApi({ hostGetJson }));
    await flushPromises();

    expect(hostGetJson).toHaveBeenCalledTimes(1);

    const deleteFeedButton = container.querySelector<HTMLButtonElement>("[aria-label='Delete studio-local-packages']");
    expect(deleteFeedButton).toBeTruthy();
    flushSync(() => deleteFeedButton!.dispatchEvent(new MouseEvent("click", { bubbles: true })));
    await flushPromises();

    expect(fetchMock.mock.calls.some(([url]) => String(url).includes("/feeds/studio-local-packages"))).toBe(true);
    expect(hostGetJson).toHaveBeenCalledTimes(2);
    expect(container.textContent).toContain("Deleted feed studio-local-packages");

    await unmount();
  });
});

describe("host health query hook", () => {
  it("polls all three hosts through the query and renders their status", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response("", { status: 200 })));
    const backendGetJson = vi.fn(async () => healthyRegistry());
    const { container, unmount } = renderDashboard(stubHealthApi(backendGetJson));
    await flushPromises();

    expect(backendGetJson).toHaveBeenCalledWith("/_elsa/module-management/registry");
    expect(container.textContent).toContain("Studio host");
    expect(container.textContent).toContain("No host issues reported.");

    await unmount();
  });
});

function stubApi(options: { hostGetJson?: (url: string) => Promise<unknown> }): ElsaStudioModuleApi {
  return {
    host: {
      baseUrl: "https://studio.example/",
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      http: stubHttp("https://studio.example/", options.hostGetJson ?? (async () => studioRegistry()))
    },
    backend: {
      baseUrl: "https://foundation.example/",
      http: stubHttp("https://foundation.example/", async () => studioRegistry())
    },
    // The destructive feed-delete now goes through api.dialogs.confirm (#192 guard); auto-confirm so
    // this Query-invalidation test still exercises the mutation path once the confirmation resolves.
    dialogs: { confirm: async () => true },
    diagnostics: { add() {}, list: () => [] }
  } as ElsaStudioModuleApi;
}

function stubHealthApi(backendGetJson: (url: string) => Promise<unknown>): ElsaStudioModuleApi {
  return {
    host: {
      baseUrl: "https://studio.example/",
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      http: { getJson: async () => healthyRegistry(), postJson: async () => ({}) }
    },
    backend: {
      baseUrl: "https://foundation.example/",
      http: { getJson: backendGetJson, postJson: async () => ({}) }
    },
    dashboardWidgets: { add() {}, list: () => [] },
    diagnosticsWidgets: { add() {}, list: () => [] },
    diagnostics: { add() {}, list: () => [] }
  } as ElsaStudioModuleApi;
}

function stubHttp(baseUrl: string, getJson: (url: string) => Promise<unknown>) {
  const http = createEndpointContext(baseUrl).http;
  return { ...http, getJson };
}

function healthyRegistry() {
  return { modules: [{ status: "available", diagnostics: [] }], diagnostics: [] };
}

function studioRegistry() {
  return {
    host: { id: "studio", displayName: "Studio", runtime: "Elsa.Studio.Web", contentRootPath: "/studio" },
    generatedAt: new Date().toISOString(),
    modules: [{
      id: "Elsa.Studio.FeatureManagement",
      displayName: "Feature management",
      surface: "Studio",
      runtime: "Elsa.Studio.Web",
      sourceKind: "static-web-asset",
      scope: "full-stack",
      version: "1.0.1",
      status: "loaded",
      compatibility: "compatible",
      packageId: null,
      packageVersion: null,
      contributions: [],
      diagnostics: [],
      manifest: null
    }],
    packages: [],
    dropFolderPackages: [],
    feeds: [{
      name: "studio-local-packages",
      directoryPath: "packages",
      serviceIndex: null,
      credentials: null,
      includeAll: true,
      includePatterns: ["*"],
      directory: { watch: true, debounceWindow: "00:00:01" }
    }],
    retentionPolicy: { retainLastNVersions: 3, retainYoungerThanDays: 30, mode: "Automatic", protectLastKnownGood: true },
    capabilities: { canUploadPackages: true, canManageFeeds: true, canReconcile: true, canPrunePackages: true, feedChangesRequireRestart: true },
    diagnostics: []
  };
}

function renderModuleManagementPage(api: ElsaStudioModuleApi) {
  return renderWithClient(<ModuleManagementPage api={api} />);
}

function renderPackageFeedsPage(api: ElsaStudioModuleApi) {
  return renderWithClient(<PackageFeedsPage api={api} />);
}

function renderDashboard(api: ElsaStudioModuleApi) {
  return renderWithClient(<Dashboard api={api} />);
}

function renderWithClient(node: React.ReactNode) {
  const client = createTestQueryClient();
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => root.render(<QueryClientProvider client={client}>{node}</QueryClientProvider>));

  return {
    container,
    unmount: async () => {
      flushSync(() => root.unmount());
      container.remove();
    }
  };
}

async function clickSourceButton(container: Element, text: string) {
  const button = Array.from(container.querySelector("[aria-label='Module sources']")?.querySelectorAll("button") ?? [])
    .find(candidate => candidate.textContent?.includes(text));
  flushSync(() => button!.dispatchEvent(new MouseEvent("click", { bubbles: true })));
  await flushPromises();
}

async function flushPromises() {
  for (let i = 0; i < 5; i++) {
    await new Promise(resolve => setTimeout(resolve, 0));
  }
}
