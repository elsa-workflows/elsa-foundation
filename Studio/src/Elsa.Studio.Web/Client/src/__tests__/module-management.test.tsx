import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { describe, expect, it, vi } from "vitest";
import { ModuleManagementPage } from "../app/modules/ModuleManagementPage";
import { PackageFeedsPage } from "../app/modules/PackageFeedsPage";
import type { ElsaStudioModuleApi } from "../sdk";

describe("module management page", () => {
  it("renders Studio host module and package registry details", async () => {
    const { container, unmount } = await renderModuleManagementPage(stubApi());

    expect(container.textContent).toContain("Studio");
    expect(container.textContent).toContain("Feature management");
    expect(container.textContent).toContain("Elsa.Studio.FeatureManagement");
    expect(container.textContent).toContain("studio-local-packages");
    expect(container.textContent).toContain("Upload Packages");
    expect(container.textContent).not.toContain("Elsa.Studio.FeatureManagement.1.0.1.nupkg");

    await unmount();
  });

  it("opens drop-folder package management from the Upload Packages button", async () => {
    const { container, unmount } = await renderModuleManagementPage(stubApi());

    expect(container.querySelector(".modules-dialog-backdrop")).toBeNull();
    expect(container.textContent).not.toContain("Package drop folder");

    const uploadPackagesButton = Array.from(container.querySelectorAll("button")).find(button => button.textContent === "Upload Packages");
    expect(uploadPackagesButton).toBeTruthy();

    flushSync(() => {
      uploadPackagesButton!.dispatchEvent(new MouseEvent("click", { bubbles: true }));
    });
    await flushPromises();

    expect(container.querySelector(".modules-dialog-backdrop")).toBeTruthy();
    expect(container.textContent).toContain("Elsa.Studio.FeatureManagement.1.0.1.nupkg");

    const packageTab = Array.from(container.querySelectorAll("button")).find(button => button.textContent === "Package");
    expect(packageTab).toBeTruthy();

    flushSync(() => {
      packageTab!.dispatchEvent(new MouseEvent("click", { bubbles: true }));
    });
    await flushPromises();

    expect(container.querySelectorAll(".modules-dialog-backdrop")).toHaveLength(1);
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

  it("shows package dependencies in the package inspector tab", async () => {
    const { container, unmount } = await renderModuleManagementPage(stubApi());
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

  it("shows uploaded drop-folder packages as table rows before they are loaded", async () => {
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

    expect(container.textContent).toContain("Elsa.Custom.Widget");
    expect(container.textContent).toContain("2.4.6");
    expect(container.textContent).toContain("drop-folder");
    expect(container.textContent).toContain("uploaded");

    await unmount();
  });

  it("renders host-scoped package feeds on a dedicated page", async () => {
    const backendGetJson = vi.fn(async () => serverRegistry());
    const { container, unmount } = await renderPackageFeedsPage(stubApi({ backendGetJson }));

    expect(container.textContent).toContain("Package feeds");
    expect(container.textContent).toContain("Studio host");
    expect(container.textContent).toContain("studio-local-packages");
    expect(container.textContent).toContain("Add feed");
    expect(container.textContent).toContain("Retention");
    expect(container.textContent).toContain("Reconcile");
    expect(container.textContent).toContain("Prune old versions");
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

    const serverTab = Array.from(container.querySelectorAll("button")).find(button => button.textContent === "Server");
    expect(serverTab).toBeTruthy();

    flushSync(() => {
      serverTab!.dispatchEvent(new MouseEvent("click", { bubbles: true }));
    });
    await flushPromises();

    expect(backendGetJson).toHaveBeenCalledWith("/_elsa/module-management/registry");
    expect(container.textContent).toContain("Server host");
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
    expect(container.textContent).toContain("Workflows Runtime API");
    expect(container.textContent).toContain("local-packages");

    await unmount();
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
      http: {
        getJson: options?.hostGetJson ?? (async () => studioRegistry()),
        postJson: async () => ({})
      }
    },
    backend: {
      baseUrl: "https://foundation.example/",
      http: {
        getJson: options?.backendGetJson ?? (async () => serverRegistry()),
        postJson: async () => ({})
      }
    },
    diagnostics: {
      add() {},
      list: () => []
    }
  } as ElsaStudioModuleApi;
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
    root.render(<ModuleManagementPage api={api} />);
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
    root.render(<PackageFeedsPage api={api} />);
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
  await new Promise(resolve => setTimeout(resolve, 0));
}
