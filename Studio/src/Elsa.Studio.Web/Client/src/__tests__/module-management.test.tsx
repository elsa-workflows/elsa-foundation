import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ModuleManagementPage } from "../app/modules/ModuleManagementPage";
import { PackageFeedsPage } from "../app/modules/PackageFeedsPage";
import { createEndpointContext, type ElsaStudioModuleApi } from "../sdk";
import { withQueryClient } from "./queryTestUtils";

describe("module management page", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it("renders Studio host module and package registry details", async () => {
    const { container, unmount } = await renderModuleManagementPage(stubApi());
    const sourceNav = sourceNavigator(container);
    const gridPanel = activeGridPanel(container);

    expect(container.textContent).toContain("Studio");
    expect(sourceNav?.textContent).toContain("Built-in");
    expect(sourceNav?.textContent).toContain("Nuplane");
    expect(sourceNav?.textContent).toContain("studio-local-packages");
    expect(sourceNav?.textContent).not.toContain("Drop folder");
    expect(gridPanel?.textContent).toContain("Built-in modules");
    expect(gridPanel?.textContent).toContain("Feature management");
    expect(gridPanel?.textContent).toContain("Elsa.Studio.FeatureManagement");
    expect(gridPanel?.textContent).toContain("Source kind");
    expect(gridPanel?.textContent).toContain("static-web-asset");
    expect(gridPanel?.textContent).not.toContain("Upload Packages");
    expect(container.querySelector(".modules-header")?.textContent).not.toContain("Upload Packages");
    expect(container.textContent).not.toContain("Elsa.Studio.FeatureManagement.1.0.1.nupkg");

    await clickSourceButton(container, "Nuplane");

    expect(activeGridPanel(container)?.textContent).toContain("Nuplane packages");
    expect(activeGridPanel(container)?.textContent).toContain("Package source");
    expect(activeGridPanel(container)?.textContent).toContain("studio-local-packages");
    expect(activeGridPanel(container)?.textContent).toContain("installed");
    expect(activeGridPanel(container)?.textContent).toContain("Drop .nupkg files here");
    expect(activeGridPanel(container)?.textContent).toContain("Upload Packages");
    expect(activeGridPanel(container)?.textContent).toContain("Staged uploads");
    expect(activeGridPanel(container)?.textContent).toContain("Elsa.Studio.FeatureManagement.1.0.1.nupkg");
    expect(container.querySelector(".modules-dialog-backdrop")).toBeNull();

    await unmount();
  });

  it("keeps drop-folder package management inline instead of opening a dialog", async () => {
    const { container, unmount } = await renderModuleManagementPage(stubApi());
    await clickSourceButton(container, "Nuplane");

    expect(container.querySelector(".modules-dialog-backdrop")).toBeNull();
    expect(container.textContent).not.toContain("Package drop folder");

    const uploadPackagesButton = Array.from(container.querySelectorAll("button")).find(button => button.textContent === "Upload Packages");
    expect(uploadPackagesButton).toBeTruthy();
    expect(stagedUploads(container)?.textContent).toContain("Elsa.Studio.FeatureManagement.1.0.1.nupkg");

    flushSync(() => {
      uploadPackagesButton!.dispatchEvent(new MouseEvent("click", { bubbles: true }));
    });
    await flushPromises();

    expect(container.querySelector(".modules-dialog-backdrop")).toBeNull();

    const packageTab = Array.from(container.querySelectorAll("button")).find(button => button.textContent === "Package");
    expect(packageTab).toBeTruthy();

    flushSync(() => {
      packageTab!.dispatchEvent(new MouseEvent("click", { bubbles: true }));
    });
    await flushPromises();

    expect(container.querySelectorAll(".modules-dialog-backdrop")).toHaveLength(0);
    expect(container.querySelector(".modules-inspector")?.textContent).not.toContain("Package drop folder");
    expect(container.querySelector(".modules-inspector")?.textContent).not.toContain("Upload .nupkg");
    expect(container.querySelector(".modules-inspector")?.textContent).not.toContain("package correlation");

    await unmount();
  });

  it("keeps host operations and feed management outside the selected module inspector", async () => {
    const { container, unmount } = await renderModuleManagementPage(stubApi());

    expect(container.querySelector(".modules-inspector")?.textContent).not.toContain("Operations");
    expect(container.querySelector(".modules-inspector")?.textContent).not.toContain("Host operations");
    expect(container.querySelector(".modules-inspector")?.textContent).not.toContain("Add feed");
    expect(container.querySelector(".modules-inspector")?.textContent).not.toContain("Save retention");

    await unmount();
  });

  it("renders module and package manifests in a dedicated inspector tab", async () => {
    const { container, unmount } = await renderModuleManagementPage(stubApi());
    await clickSourceButton(container, "Nuplane");
    await clickRowContaining(activeGridPanel(container), "Elsa.Studio.FeatureManagement");

    const manifestTab = Array.from(container.querySelectorAll("button")).find(button => button.textContent === "Manifest");
    expect(manifestTab).toBeTruthy();

    flushSync(() => {
      manifestTab!.dispatchEvent(new MouseEvent("click", { bubbles: true }));
    });
    await flushPromises();

    expect(container.querySelector(".modules-inspector")?.textContent).toContain("elsa-package");
    expect(container.querySelector(".modules-inspector")?.textContent).toContain("Elsa.Studio.FeatureManagement");

    await unmount();
  });

  it("shows module load status and reasons under module management diagnostics", async () => {
    const { container, unmount } = await renderModuleManagementPage(stubApi());
    await clickRowContaining(activeGridPanel(container), "Elsa.Studio.FeatureManagement");

    const diagnosticsTab = Array.from(container.querySelectorAll("button")).find(button => button.textContent === "Diagnostics");
    expect(diagnosticsTab).toBeTruthy();

    flushSync(() => {
      diagnosticsTab!.dispatchEvent(new MouseEvent("click", { bubbles: true }));
    });
    await flushPromises();

    const inspector = container.querySelector(".modules-inspector");
    expect(inspector?.textContent).toContain("Diagnostics");
    expect(inspector?.textContent).toContain("available");
    expect(inspector?.textContent).toContain("Module manifest accepted.");

    await unmount();
  });

  it("shows package dependencies in the package inspector tab", async () => {
    const { container, unmount } = await renderModuleManagementPage(stubApi());
    await clickSourceButton(container, "Nuplane");
    await clickRowContaining(activeGridPanel(container), "Elsa.Studio.FeatureManagement");

    const packageTab = Array.from(container.querySelectorAll("button")).find(button => button.textContent === "Package");
    expect(packageTab).toBeTruthy();

    flushSync(() => {
      packageTab!.dispatchEvent(new MouseEvent("click", { bubbles: true }));
    });
    await flushPromises();

    expect(container.querySelector(".modules-inspector")?.textContent).toContain("Dependencies");
    expect(container.querySelector(".modules-inspector")?.textContent).toContain("Elsa.Studio.Core");
    expect(container.querySelector(".modules-inspector")?.textContent).toContain("[1.0.0, )");

    await unmount();
  });

  it("shows staged drop-folder packages inline without treating them as package source rows", async () => {
    const { container, unmount } = await renderModuleManagementPage(stubApi({
      hostGetJson: async () => ({
        ...studioRegistry(),
        packages: [],
        dropFolderPackages: [{
          fileName: "Elsa.Custom.Widget.2.4.6.nupkg",
          path: "/studio/packages/Elsa.Custom.Widget.2.4.6.nupkg",
          size: 4096,
          lastWriteTimeUtc: new Date().toISOString()
        }]
      })
    }));
    await clickSourceButton(container, "Nuplane");
    const gridPanel = activeGridPanel(container);

    expect(stagedUploads(container)?.textContent).toContain("Elsa.Custom.Widget.2.4.6.nupkg");
    expect(stagedUploads(container)?.textContent).toContain("Elsa.Custom.Widget");
    expect(stagedUploads(container)?.textContent).toContain("2.4.6");
    expect(sourceNavigator(container)?.textContent).not.toContain("Drop folder");
    expect(gridPanel?.textContent).toContain("No active Nuplane packages are installed for Studio.");
    expect(gridPanel?.textContent).not.toContain("drop-folder");

    await unmount();
  });

  it("triggers the hidden file input from the inline Upload Packages button", async () => {
    const { container, unmount } = await renderModuleManagementPage(stubApi());
    await clickSourceButton(container, "Nuplane");

    const input = container.querySelector<HTMLInputElement>(".modules-hidden-input");
    const uploadPackagesButton = Array.from(activeGridPanel(container)?.querySelectorAll("button") ?? [])
      .find(button => button.textContent === "Upload Packages");
    expect(input).toBeTruthy();
    expect(uploadPackagesButton).toBeTruthy();
    const clickSpy = vi.spyOn(input!, "click").mockImplementation(() => undefined);

    flushSync(() => {
      uploadPackagesButton!.dispatchEvent(new MouseEvent("click", { bubbles: true }));
    });
    await flushPromises();

    expect(clickSpy).toHaveBeenCalled();

    await unmount();
  });

  it("uploads multiple valid package files from the file picker and ignores non-packages", async () => {
    const fetchMock = mockFetch();
    const hostGetJson = vi.fn(async () => studioRegistry());
    const { container, unmount } = await renderModuleManagementPage(stubApi({ hostGetJson }));
    await clickSourceButton(container, "Nuplane");

    const input = container.querySelector<HTMLInputElement>(".modules-hidden-input");
    expect(input).toBeTruthy();
    Object.defineProperty(input, "files", {
      configurable: true,
      value: [
        new File(["one"], "Elsa.One.1.0.0.nupkg"),
        new File(["notes"], "notes.txt"),
        new File(["two"], "Elsa.Two.1.0.0.nupkg")
      ]
    });

    flushSync(() => {
      input!.dispatchEvent(new Event("change", { bubbles: true }));
    });
    await flushPromises();
    await flushPromises();

    const uploadCalls = fetchMock.mock.calls.filter(([url]) => String(url).includes("/_elsa/module-management/packages/upload"));
    expect(uploadCalls).toHaveLength(2);
    expect(hostGetJson).toHaveBeenCalledTimes(2);

    await unmount();
  });

  it("shows an error when the selected upload files contain no packages", async () => {
    const fetchMock = mockFetch();
    const { container, unmount } = await renderModuleManagementPage(stubApi());
    await clickSourceButton(container, "Nuplane");

    const input = container.querySelector<HTMLInputElement>(".modules-hidden-input");
    expect(input).toBeTruthy();
    Object.defineProperty(input, "files", {
      configurable: true,
      value: [new File(["notes"], "notes.txt")]
    });

    flushSync(() => {
      input!.dispatchEvent(new Event("change", { bubbles: true }));
    });
    await flushPromises();

    expect(fetchMock).not.toHaveBeenCalled();
    expect(container.textContent).toContain("Select one or more .nupkg package files.");

    await unmount();
  });

  it("uploads package files dropped on the inline drop zone", async () => {
    const fetchMock = mockFetch();
    const { container, unmount } = await renderModuleManagementPage(stubApi());
    await clickSourceButton(container, "Nuplane");

    const dropZone = container.querySelector(".modules-upload-dropzone");
    expect(dropZone).toBeTruthy();
    const dropEvent = new Event("drop", { bubbles: true, cancelable: true });
    Object.defineProperty(dropEvent, "dataTransfer", {
      value: {
        files: [
          new File(["one"], "Elsa.One.1.0.0.nupkg"),
          new File(["notes"], "notes.txt")
        ]
      }
    });

    flushSync(() => {
      dropZone!.dispatchEvent(dropEvent);
    });
    await flushPromises();
    await flushPromises();

    const uploadCalls = fetchMock.mock.calls.filter(([url]) => String(url).includes("/_elsa/module-management/packages/upload"));
    expect(uploadCalls).toHaveLength(1);

    await unmount();
  });

  it("deletes staged uploads through the existing drop-folder API", async () => {
    const fetchMock = mockFetch();
    const { container, unmount } = await renderModuleManagementPage(stubApi());
    await clickSourceButton(container, "Nuplane");

    const deleteButton = stagedUploads(container)?.querySelector<HTMLButtonElement>("[title*='Delete Elsa.Studio.FeatureManagement.1.0.1.nupkg']");
    expect(deleteButton).toBeTruthy();

    flushSync(() => {
      deleteButton!.dispatchEvent(new MouseEvent("click", { bubbles: true }));
    });
    await flushPromises();
    await flushPromises();

    expect(fetchMock).toHaveBeenCalledWith(
      "https://studio.example/_elsa/module-management/packages/drop-folder/Elsa.Studio.FeatureManagement.1.0.1.nupkg",
      expect.objectContaining({ method: "DELETE" })
    );
    expect(container.textContent).toContain("Nuplane reconciliation is running.");
    expect(container.textContent).not.toContain("Reconcile or restart may be required.");

    await unmount();
  });

  it("filters Nuplane package rows by package source while keeping built-in modules reachable", async () => {
    const { container, unmount } = await renderModuleManagementPage(stubApi({
      hostGetJson: async () => ({
        ...studioRegistry(),
        packages: [
          ...studioRegistry().packages,
          {
            id: "Elsa.External.Widget",
            version: "2.0.0",
            feedName: "external-feed",
            sourceName: "external-feed",
            installPath: "/studio/packages/Elsa.External.Widget/2.0.0",
            manifest: null,
            dependencies: []
          }
        ],
        dropFolderPackages: []
      })
    }));

    expect(sourceNavigator(container)?.textContent).toContain("external-feed");
    expect(sourceNavigator(container)?.textContent).toContain("studio-local-packages");
    expect(sourceNavigator(container)?.textContent).not.toContain("Drop folder");
    expect(activeGridPanel(container)?.textContent).toContain("Feature management");

    await clickSourceButton(container, "external-feed");

    expect(activeGridPanel(container)?.textContent).toContain("Elsa.External.Widget");
    expect(activeGridPanel(container)?.textContent).not.toContain("Elsa.Studio.FeatureManagement");

    await clickSourceButton(container, "Built-in");

    expect(activeGridPanel(container)?.textContent).toContain("Feature management");
    expect(activeGridPanel(container)?.textContent).not.toContain("Elsa.External.Widget");

    await unmount();
  });

  it("shows origin, source kind, package source, and Nuplane state in the inspector", async () => {
    const { container, unmount } = await renderModuleManagementPage(stubApi());
    const overview = container.querySelector(".modules-inspector");

    expect(overview?.textContent).toContain("Origin");
    expect(overview?.textContent).toContain("Built-in");
    expect(overview?.textContent).toContain("Source kind");
    expect(overview?.textContent).toContain("static-web-asset");
    expect(overview?.textContent).toContain("Package source");
    expect(overview?.textContent).toContain("Nuplane state");

    await clickSourceButton(container, "Nuplane");
    await clickRowContaining(activeGridPanel(container), "Elsa.Studio.FeatureManagement");

    expect(container.querySelector(".modules-inspector")?.textContent).toContain("Nuplane");
    expect(container.querySelector(".modules-inspector")?.textContent).toContain("studio-local-packages");
    expect(container.querySelector(".modules-inspector")?.textContent).toContain("installed");

    await unmount();
  });

  it("renders host-scoped package feeds on a dedicated page", async () => {
    const backendGetJson = vi.fn(async () => serverRegistry());
    const { container, unmount } = await renderPackageFeedsPage(stubApi({ backendGetJson }));

    expect(container.textContent).toContain("Package feeds");
    expect(container.textContent).not.toContain("Studio host");
    expect(container.textContent).toContain("studio-local-packages");
    expect(container.textContent).toContain("Add feed");
    expect(container.textContent).toContain("Retention");
    expect(container.textContent).toContain("Reconcile");
    expect(container.textContent).toContain("Prune old versions");
    expect(container.textContent).toContain("Change ledger");
    expect(container.textContent).toContain("Feed registration changes require restart");
    const operationsPanel = container.querySelector("[aria-label='Studio feed operations']");
    const retentionPanel = container.querySelector("[aria-label='Studio retention settings']");
    expect(operationsPanel?.textContent).toContain("Reconcile");
    expect(operationsPanel?.textContent).toContain("Prune old versions");
    expect(operationsPanel?.textContent).not.toContain("Retention");
    expect(retentionPanel?.textContent).toContain("Retention");
    expect(retentionPanel?.textContent).toContain("Save retention");
    expect(container.querySelector("[aria-label='Feed name']")).toBeNull();

    const editFeedButton = container.querySelector("[aria-label='Edit studio-local-packages']");
    expect(editFeedButton).toBeTruthy();

    flushSync(() => {
      editFeedButton!.dispatchEvent(new MouseEvent("click", { bubbles: true }));
    });
    await flushPromises();

    expect(container.querySelector(".modules-dialog-backdrop")).toBeTruthy();
    expect(container.querySelector<HTMLInputElement>("[aria-label='Feed name']")?.value).toBe("studio-local-packages");
    expect(container.querySelector<HTMLInputElement>("[aria-label='Directory path']")?.value).toBe("packages");
    expect(container.textContent).toContain("Save feed");

    const closeEditFeedButton = container.querySelector("[aria-label='Close edit feed']");
    expect(closeEditFeedButton).toBeTruthy();

    flushSync(() => {
      closeEditFeedButton!.dispatchEvent(new MouseEvent("click", { bubbles: true }));
    });
    await flushPromises();

    const addFeedButton = Array.from(container.querySelectorAll("button")).find(button => button.textContent === "Add feed");
    expect(addFeedButton).toBeTruthy();

    flushSync(() => {
      addFeedButton!.dispatchEvent(new MouseEvent("click", { bubbles: true }));
    });
    await flushPromises();

    expect(container.querySelector(".modules-dialog-backdrop")).toBeTruthy();
    expect(container.querySelector("[aria-label='Feed name']")).toBeTruthy();
    expect(container.querySelector("[aria-label='Include all packages']")).toBeTruthy();

    const serverTab = Array.from(container.querySelectorAll("button")).find(button => button.textContent === "Server");
    expect(serverTab).toBeTruthy();

    flushSync(() => {
      serverTab!.dispatchEvent(new MouseEvent("click", { bubbles: true }));
    });
    await flushPromises();

    expect(backendGetJson).toHaveBeenCalledWith("/_elsa/module-management/registry");
    expect(container.textContent).not.toContain("Server host");
    expect(container.textContent).toContain("local-packages");

    await unmount();
  });

  it("loads Server registry from the backend context when the Server tab is selected", async () => {
    const backendGetJson = vi.fn(async () => serverRegistry());
    const { container, unmount } = await renderModuleManagementPage(stubApi({ backendGetJson }));

    const serverTab = Array.from(container.querySelectorAll("button")).find(button => button.textContent === "Server");
    expect(serverTab).toBeTruthy();

    flushSync(() => {
      serverTab!.dispatchEvent(new MouseEvent("click", { bubbles: true }));
    });
    await flushPromises();

    expect(backendGetJson).toHaveBeenCalledWith("/_elsa/module-management/registry");
    expect(container.textContent).toContain("Server");
    expect(activeGridPanel(container)?.textContent).toContain("Workflows Runtime API");
    expect(container.textContent).toContain("local-packages");

    await unmount();
  });

  it("renders a sparse Server registry without blanking the page", async () => {
    const backendGetJson = vi.fn(async () => ({
      host: { id: "server", displayName: "Server", runtime: "Elsa.Server", contentRootPath: "/server" },
      generatedAt: new Date().toISOString(),
      modules: [{
        id: "ServerOnlyModule",
        displayName: "Server only module",
        surface: "Server",
        runtime: "Elsa.Server",
        sourceKind: "backend",
        scope: "backend",
        version: "",
        status: "available",
        compatibility: "unknown",
        packageId: null,
        packageVersion: null,
        contributions: null,
        diagnostics: null,
        manifest: null
      }],
      packages: null,
      dropFolderPackages: null,
      feeds: null,
      retentionPolicy: null,
      capabilities: null,
      diagnostics: null
    }));
    const { container, unmount } = await renderModuleManagementPage(stubApi({ backendGetJson }));

    const serverTab = Array.from(container.querySelectorAll("button")).find(button => button.textContent === "Server");
    expect(serverTab).toBeTruthy();

    flushSync(() => {
      serverTab!.dispatchEvent(new MouseEvent("click", { bubbles: true }));
    });
    await flushPromises();

    expect(container.textContent).toContain("Server only module");
    expect(container.textContent).not.toBe("");

    await unmount();
  });

  it("includes endpoint context headers in module management operations", async () => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify({ ok: true }), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    const context = createEndpointContext("https://foundation.example/", { headers: { "X-Elsa-Module-Management-Key": "secret" } });
    await context.http.getJson("/_elsa/module-management/registry");

    const headers = new Headers(fetchMock.mock.calls[0]?.[1]?.headers);
    expect(headers.get("X-Elsa-Module-Management-Key")).toBe("secret");
  });
});

function stubApi(options?: {
  hostGetJson?: (url: string) => Promise<unknown>;
  backendGetJson?: (url: string) => Promise<unknown>;
}): ElsaStudioModuleApi {
  return {
    host: {
      baseUrl: "https://studio.example/",
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      http: stubHttp("https://studio.example/", options?.hostGetJson ?? (async () => studioRegistry()))
    },
    backend: {
      baseUrl: "https://foundation.example/",
      http: stubHttp("https://foundation.example/", options?.backendGetJson ?? (async () => serverRegistry()))
    },
    diagnostics: {
      add() {},
      list: () => []
    }
  } as ElsaStudioModuleApi;
}

function stubHttp(baseUrl: string, getJson: (url: string) => Promise<unknown>) {
  const http = createEndpointContext(baseUrl).http;
  return {
    ...http,
    requestJson: http.requestJson,
    getJson,
  };
}

function sourceNavigator(container: Element) {
  return container.querySelector("[aria-label='Module sources']");
}

function activeGridPanel(container: Element) {
  return container.querySelector(".modules-grid-panel");
}

function stagedUploads(container: Element) {
  return container.querySelector("[aria-label='Studio staged uploads'], [aria-label='Server staged uploads']");
}

function mockFetch() {
  const fetchMock = vi.fn(async () => ({
    ok: true,
    text: async () => "{}"
  }));
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

async function clickSourceButton(container: Element, text: string) {
  const button = Array.from(sourceNavigator(container)?.querySelectorAll("button") ?? [])
    .find(candidate => candidate.textContent?.includes(text));

  if (!button) {
    throw new Error(`Could not find source button containing '${text}'.`);
  }

  flushSync(() => {
    button.dispatchEvent(new MouseEvent("click", { bubbles: true }));
  });
  await flushPromises();
}

async function clickRowContaining(section: Element | null, text: string) {
  const row = Array.from(section?.querySelectorAll(".studio-data-grid-row") ?? [])
    .find(button => button.textContent?.includes(text));

  if (!row) {
    throw new Error(`Could not find row containing '${text}'.`);
  }

  flushSync(() => {
    row.dispatchEvent(new MouseEvent("click", { bubbles: true }));
  });
  await flushPromises();
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
      contributions: [{ id: "feature-management:http", type: "http", label: "HTTP endpoints", status: "active" }],
      diagnostics: [{ source: "Elsa.Studio.FeatureManagement", status: "available", reason: "Module manifest accepted." }],
      manifest: {
        entry: "/_content/Elsa.Studio.FeatureManagement/module.js",
        styles: ["/_content/Elsa.Studio.FeatureManagement/module.css"],
        capabilities: ["navigation", "routes", "setting-editors"]
      }
    }],
    packages: [{
      id: "Elsa.Studio.FeatureManagement",
      version: "1.0.1",
      feedName: "studio-local-packages",
      sourceName: "studio-local-packages",
      installPath: "/studio/packages/Elsa.Studio.FeatureManagement/1.0.1",
      manifest: {
        kind: "elsa-package",
        path: "/studio/packages/Elsa.Studio.FeatureManagement/1.0.1/elsa-package.json",
        content: {
          schemaVersion: "1.0",
          package: { id: "Elsa.Studio.FeatureManagement", version: "1.0.1" }
        },
        text: null
      },
      dependencies: [{ id: "Elsa.Studio.Core", versionRange: "[1.0.0, )", group: "net10.0", source: "nuspec" }]
    }],
    dropFolderPackages: [{
      fileName: "Elsa.Studio.FeatureManagement.1.0.1.nupkg",
      path: "/studio/packages/Elsa.Studio.FeatureManagement.1.0.1.nupkg",
      size: 4096,
      lastWriteTimeUtc: new Date().toISOString()
    }],
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

function serverRegistry() {
  return {
    host: { id: "server", displayName: "Server", runtime: "Elsa.Server", contentRootPath: "/server" },
    generatedAt: new Date().toISOString(),
    modules: [{
      id: "WorkflowsRuntimeApi",
      displayName: "Workflows Runtime API",
      surface: "Server",
      runtime: "Elsa.Server",
      sourceKind: "cshells-feature",
      scope: "backend",
      version: "",
      status: "available",
      compatibility: "unknown",
      packageId: null,
      packageVersion: null,
      contributions: [],
      diagnostics: [],
      manifest: null
    }],
    packages: [{
      id: "Elsa.Samples.Nuplane.Activities",
      version: "1.0.0",
      feedName: "local-packages",
      sourceName: "local-packages",
      installPath: "/server/packages/Elsa.Samples.Nuplane.Activities/1.0.0",
      manifest: {
        kind: "elsa-package",
        path: "/server/packages/Elsa.Samples.Nuplane.Activities/1.0.0/elsa-package.json",
        content: {
          schemaVersion: "1.0",
          package: { id: "Elsa.Samples.Nuplane.Activities", version: "1.0.0" }
        },
        text: null
      },
      dependencies: [{ id: "Elsa.Activities.Design.Core", versionRange: "[1.0.0, )", group: "net10.0", source: "nuspec" }]
    }],
    dropFolderPackages: [{
      fileName: "Elsa.Samples.Nuplane.Activities.1.0.0.nupkg",
      path: "/server/packages/Elsa.Samples.Nuplane.Activities.1.0.0.nupkg",
      size: 4096,
      lastWriteTimeUtc: new Date().toISOString()
    }],
    feeds: [{
      name: "local-packages",
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

async function renderModuleManagementPage(api: ElsaStudioModuleApi) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);

  flushSync(() => {
    root.render(withQueryClient(<ModuleManagementPage api={api} />));
  });
  await flushPromises();

  return {
    container,
    unmount: async () => {
      flushSync(() => root.unmount());
      container.remove();
    }
  };
}

async function renderPackageFeedsPage(api: ElsaStudioModuleApi) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);

  flushSync(() => {
    root.render(withQueryClient(<PackageFeedsPage api={api} />));
  });
  await flushPromises();

  return {
    container,
    unmount: async () => {
      flushSync(() => root.unmount());
      container.remove();
    }
  };
}

async function flushPromises() {
  // TanStack Query settles across several microtask/render cycles (queryFn resolve → cache commit →
  // re-render), so flush a handful of macrotasks to let the registry queries reach their terminal state.
  for (let i = 0; i < 5; i++) {
    await new Promise(resolve => setTimeout(resolve, 0));
  }
}
