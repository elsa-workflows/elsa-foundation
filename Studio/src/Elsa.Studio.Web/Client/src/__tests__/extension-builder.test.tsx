import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ExtensionBuilderPage } from "../app/modules/ExtensionBuilderPage";
import type { ExtensionRuntimeStatus, ProjectFile } from "../app/modules/extensionBuilderApi";
import type { ElsaStudioModuleApi } from "../sdk";

describe("extension builder page", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it("gates interactive builder surfaces with ExtensionBuilderCapabilities", async () => {
    const { container, unmount } = await renderExtensionBuilderPage(stubApi({
      getJson: async url => url.endsWith("/capabilities") ? deniedCapabilities() : []
    }));

    await flushPromises();

    expect(container.textContent).toContain("You do not have Extension Builder capabilities");
    expect(container.textContent).toContain("GetCapabilities");
    expect(container.textContent).not.toContain("Create workspace");

    await unmount();
  });

  it("renders owner-scoped workspaces, templates, files, build status, and runtime state", async () => {
    const { container, unmount } = await renderExtensionBuilderPage(stubApi());

    await flushPromises();
    await flushPromises();

    expect(container.textContent).toContain("Extension Builder");
    expect(container.textContent).toContain("Team Extensions");
    expect(container.textContent).toContain("owner alice");
    expect(container.textContent).toContain("Elsa activity/module");
    expect(container.textContent).toContain("Generic .NET class library");
    expect(container.textContent).toContain("ElsaActivityExtension");
    expect(container.textContent).toContain("Activities/HelloActivity.cs");
    expect(container.querySelector<HTMLTextAreaElement>("[aria-label='Project file editor']")?.value).toContain("HelloActivity");
    expect(container.textContent).toContain("Loaded");
    expect(container.textContent).toContain("Hello activity");

    await unmount();
  });

  it("saves file edits, builds, promotes, and refreshes loaded runtime status through canonical endpoints", async () => {
    const postJson = vi.fn(async (url: string) => {
      if (url.endsWith("/builds")) return succeededBuild({ sourceRevisionId: null });
      if (url.endsWith("/promote")) return acceptedPromotion();
      return {};
    });
    const getJson = vi.fn(async (url: string) => {
      if (url.includes("/builds/build-1")) return succeededBuild({ sourceRevisionId: null });
      return defaultGetJson(url);
    });
    const fetchMock = mockFetch();
    const { container, unmount } = await renderExtensionBuilderPage(stubApi({ getJson, postJson }));
    await flushPromises();
    await flushPromises();

    const editor = container.querySelector<HTMLTextAreaElement>("[aria-label='Project file editor']");
    expect(editor).toBeTruthy();
    await fill(editor!, `${editor!.value}\n// updated`);
    await flushPromises();

    await clickButton(container, "Save");
    await flushPromises();
    expect(fetchMock).toHaveBeenCalledWith(
      "https://foundation.example/_elsa/extension-builder/projects/proj-1/files/Activities%2FHelloActivity.cs",
      expect.objectContaining({ method: "PUT" })
    );

    await clickButton(container, "Build");
    await flushPromises();
    await flushPromises();
    expect(postJson).toHaveBeenCalledWith("/_elsa/extension-builder/projects/proj-1/builds", {});
    expect(container.textContent).toContain("Build submitted");
    expect(container.textContent).toContain("Succeeded");

    await clickTab(container, "Promote");
    await clickButton(container, "Promote build");
    await flushPromises();

    expect(postJson).toHaveBeenCalledWith("/_elsa/extension-builder/builds/build-1/promote", {});
    expect(container.textContent).toContain("is loaded at runtime");

    await unmount();
  });

  it("shows diagnostics and opens the referenced source location", async () => {
    const { container, unmount } = await renderExtensionBuilderPage(stubApi({
      getJson: async url => {
        if (url.endsWith("/capabilities")) return trustedCapabilities();
        if (url.endsWith("/workspaces")) return [workspaceWithProject()];
        if (url.endsWith("/templates")) return templates();
        if (url.endsWith("/files")) return projectFiles();
        if (url.endsWith("/runtime-status")) return loadedRuntime();
        if (url.includes("/builds/build-failed")) return failedBuild();
        if (url.endsWith("Activities%2FHelloActivity.cs")) return projectFiles()[0];
        if (url.includes("/projects/proj-1")) return { ...project(), builds: [failedBuild()] };
        return {};
      }
    }));
    await flushPromises();
    await flushPromises();

    expect(container.textContent).toContain("CS1002");
    await clickButton(container, "CS1002");
    await flushPromises();

    expect(container.textContent).toContain("Activities/HelloActivity.cs:7:18");
    expect(container.querySelector("[aria-label='Inline diagnostics']")?.textContent).toContain("Expected ;");

    await unmount();
  });

  it("renders distinct promotion rejection guidance", async () => {
    for (const [category, expected] of [
      ["Duplicate", "never silently overwritten"],
      ["InvalidManifest", "manifest"],
      ["DependencyPolicy", "dependency"],
      ["MalformedPackage", "malformed"]
    ] as const) {
      const postJson = vi.fn(async (url: string) => url.endsWith("/promote") ? rejectedPromotion(category) : {});
      const { container, unmount } = await renderExtensionBuilderPage(stubApi({ postJson }));
      await flushPromises();
      await flushPromises();

      await clickTab(container, "Promote");
      await clickButton(container, "Promote build");
      await flushPromises();

      expect(container.textContent?.toLowerCase()).toContain(expected.toLowerCase());
      await unmount();
    }
  });

  it("normalizes numeric enum values and backend file kind fallbacks", async () => {
    const postJson = vi.fn(async (url: string) => url.endsWith("/promote") ? rejectedPromotion(0) : {});
    const { container, unmount } = await renderExtensionBuilderPage(stubApi({
      postJson,
      getJson: async url => {
        if (url.endsWith("/capabilities")) return trustedCapabilities();
        if (url.endsWith("/workspaces")) return [workspaceWithProject()];
        if (url.endsWith("/templates")) return templates();
        if (url.endsWith("/files")) return [
          { path: "Activities", kind: 1 },
          { path: "Activities/HelloActivity.cs", kind: 2, content: "public sealed class HelloActivity { }" }
        ];
        if (url.endsWith("/runtime-status")) return { ...loadedRuntime(), state: 0, history: [{ version: "1.0.0", state: 0, available: true }] };
        if (url.includes("/builds/build-1")) return succeededBuild({ status: 2 });
        if (url.endsWith("Activities%2FHelloActivity.cs")) return { path: "Activities/HelloActivity.cs", kind: 2, content: "public sealed class HelloActivity { }" };
        if (url.includes("/projects/proj-1")) return { ...project(), latestBuildStatus: 2, runtimeStatus: 0, builds: [succeededBuild({ status: 2 })] };
        return {};
      }
    }));
    await flushPromises();
    await flushPromises();

    expect(container.textContent).toContain("Succeeded");
    expect(container.textContent).toContain("Loaded");
    expect(buttonContaining(container, "Activities")?.disabled).toBe(true);

    await clickTab(container, "Promote");
    await clickButton(container, "Promote build");
    await flushPromises();

    expect(container.textContent).toContain("Duplicate package id and version rejected");
    await unmount();
  });

  it("explains stale successful artifacts instead of allowing promotion", async () => {
    const postJson = vi.fn(async () => acceptedPromotion());
    const { container, unmount } = await renderExtensionBuilderPage(stubApi({
      postJson,
      getJson: async url => {
        if (url.endsWith("/capabilities")) return trustedCapabilities();
        if (url.endsWith("/workspaces")) return [workspaceWithProject()];
        if (url.endsWith("/templates")) return templates();
        if (url.endsWith("/files")) return projectFiles();
        if (url.endsWith("/runtime-status")) return loadedRuntime();
        if (url.endsWith("Activities%2FHelloActivity.cs")) return projectFiles()[0];
        if (url.includes("/projects/proj-1")) return { ...project(), currentSourceRevisionId: "rev-2", builds: [succeededBuild({ sourceRevisionId: "rev-1" })] };
        return {};
      }
    }));
    await flushPromises();
    await flushPromises();

    await clickTab(container, "Promote");
    const promoteButton = buttonContaining(container, "Promote build");

    expect(promoteButton?.disabled).toBe(true);
    expect(promoteButton?.title).toContain("stale");
    expect(postJson).not.toHaveBeenCalledWith("/_elsa/extension-builder/builds/build-1/promote", {});

    await unmount();
  });

  it("supports retry reconciliation, rollback gating, and generic template no-contributions messaging", async () => {
    const postJson = vi.fn(async (url: string) => {
      if (url.endsWith("/retry-reconcile")) return loadedRuntime({ features: [] });
      if (url.endsWith("/rollback")) return loadedRuntime({ version: "0.9.0", features: [] });
      return {};
    });
    vi.spyOn(window, "confirm").mockReturnValue(true);
    const { container, unmount } = await renderExtensionBuilderPage(stubApi({
      postJson,
      getJson: async url => {
        if (url.endsWith("/capabilities")) return trustedCapabilities();
        if (url.endsWith("/workspaces")) return [workspaceWithProject({ project: genericProject() })];
        if (url.endsWith("/templates")) return templates();
        if (url.endsWith("/files")) return projectFiles();
        if (url.endsWith("/runtime-status")) return failedRuntime({ features: [] });
        if (url.endsWith("Activities%2FHelloActivity.cs")) return projectFiles()[0];
        if (url.includes("/projects/proj-1")) return { ...genericProject(), builds: [succeededBuild()] };
        return {};
      }
    }));
    await flushPromises();
    await flushPromises();

    expect(container.textContent).toContain("Generic .NET class library");
    expect(container.textContent).toContain("FailedReconciliation");
    expect(container.textContent).toContain("contributed no runtime capabilities");

    await clickButton(container, "Retry reconciliation");
    await flushPromises();
    expect(postJson).toHaveBeenCalledWith("/_elsa/extension-builder/projects/proj-1/retry-reconcile", {});

    await clickButton(container, "Rollback");
    await flushPromises();
    expect(postJson).toHaveBeenCalledWith("/_elsa/extension-builder/projects/proj-1/rollback", { version: "0.9.0" });

    await unmount();
  });
});

function stubApi(options?: {
  getJson?: (url: string) => Promise<unknown>;
  postJson?: (url: string, body: unknown) => Promise<unknown>;
}): ElsaStudioModuleApi {
  return {
    backend: {
      baseUrl: "https://foundation.example/",
      http: {
        getJson: options?.getJson ?? defaultGetJson,
        postJson: options?.postJson ?? defaultPostJson
      }
    }
  } as ElsaStudioModuleApi;
}

async function defaultGetJson(url: string): Promise<unknown> {
  if (url.endsWith("/capabilities")) return trustedCapabilities();
  if (url.endsWith("/workspaces")) return [workspaceWithProject()];
  if (url.endsWith("/templates")) return templates();
  if (url.endsWith("/files")) return projectFiles();
  if (url.endsWith("/runtime-status")) return loadedRuntime();
  if (url.includes("/builds/build-1")) return succeededBuild();
  if (url.endsWith("Activities%2FHelloActivity.cs")) return projectFiles()[0];
  if (url.includes("/projects/proj-1")) return { ...project(), builds: [succeededBuild()] };
  return {};
}

async function defaultPostJson(url: string): Promise<unknown> {
  if (url.endsWith("/builds")) return succeededBuild();
  if (url.endsWith("/promote")) return acceptedPromotion();
  return {};
}

async function renderExtensionBuilderPage(api: ElsaStudioModuleApi) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);

  flushSync(() => {
    root.render(<ExtensionBuilderPage api={api} />);
  });

  return {
    container,
    unmount: async () => {
      flushSync(() => root.unmount());
      container.remove();
    }
  };
}

function mockFetch() {
  const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
    if (String(url).includes("/log")) {
      return new Response("Build succeeded.", { status: 200, headers: { "content-type": "text/plain" } });
    }

    return new Response(JSON.stringify({ path: "Activities/HelloActivity.cs", type: "file", content: JSON.parse(String(init?.body ?? "{}")).content }), {
      status: 200,
      headers: { "content-type": "application/json" }
    });
  });
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

async function clickButton(container: Element, text: string) {
  const buttons = Array.from(container.querySelectorAll<HTMLButtonElement>("button"))
    .filter(candidate => !candidate.disabled && (candidate.textContent?.includes(text) || candidate.getAttribute("aria-label")?.includes(text)));
  const button = buttons.find(candidate => candidate.getAttribute("role") !== "tab") ?? buttons[0];
  if (!button) throw new Error(`Could not find button containing '${text}'.`);
  flushSync(() => button.dispatchEvent(new MouseEvent("click", { bubbles: true })));
  await flushPromises();
}

async function fill(element: HTMLInputElement | HTMLTextAreaElement, value: string) {
  flushSync(() => {
    const setter = Object.getOwnPropertyDescriptor(Object.getPrototypeOf(element), "value")?.set;
    setter?.call(element, value);
    element.dispatchEvent(new Event("input", { bubbles: true }));
  });
  await flushPromises();
}

async function clickTab(container: Element, text: string) {
  const button = Array.from(container.querySelectorAll<HTMLButtonElement>("[role='tab']"))
    .find(candidate => candidate.textContent?.includes(text));
  if (!button) throw new Error(`Could not find tab containing '${text}'.`);
  flushSync(() => button.dispatchEvent(new MouseEvent("click", { bubbles: true })));
  await flushPromises();
}

async function flushPromises() {
  await new Promise(resolve => setTimeout(resolve, 0));
}

function trustedCapabilities() {
  return {
    canCreateWorkspace: true,
    canEditFiles: true,
    canBuild: true,
    canPromote: true,
    canRollback: true
  };
}

function deniedCapabilities() {
  return {
    canCreateWorkspace: false,
    canEditFiles: false,
    canBuild: false,
    canPromote: false,
    canRollback: false
  };
}

function workspaceWithProject(options?: { project?: ReturnType<typeof project> }) {
  const currentProject = options?.project ?? project();
  return {
    id: "ws-1",
    displayName: "Team Extensions",
    ownerId: "alice",
    trustContext: "trusted-team",
    projectIds: [currentProject.id]
  };
}

function project() {
  return {
    id: "proj-1",
    workspaceId: "ws-1",
    name: "Hello Activity",
    templateId: "elsa-activity",
    packageId: "Company.Extensions.Hello",
    packageVersion: "1.0.0",
    currentSourceRevisionId: "rev-1",
    latestBuildStatus: "Succeeded",
    runtimeStatus: "Loaded"
  };
}

function genericProject() {
  return {
    ...project(),
    name: "Generic Extension",
    templateId: "generic-dotnet",
    packageId: "Company.Extensions.Generic"
  };
}

function templates() {
  return [
    { id: "elsa-activity", name: "Elsa activity/module", description: "Creates an Elsa activity extension.", primary: true, tags: ["elsa"] },
    { id: "generic-dotnet", name: "Generic .NET class library", description: "Creates a generic .NET package.", primary: false, tags: ["dotnet"] }
  ];
}

function projectFiles(): ProjectFile[] {
  return [
    { path: "Activities/HelloActivity.cs", type: "file", content: "public sealed class HelloActivity { }" },
    { path: "Hello.csproj", type: "file", content: "<Project />" }
  ];
}

function artifact() {
  return {
    id: "artifact-1",
    buildId: "build-1",
    packageId: "Company.Extensions.Hello",
    version: "1.0.0",
    fileName: "Company.Extensions.Hello.1.0.0.nupkg",
    size: 4096
  };
}

function succeededBuild(overrides?: { id?: string; sourceRevisionId?: string | null; status?: string | number; artifact?: ReturnType<typeof artifact> | null }) {
  return {
    id: "build-1",
    projectId: "proj-1",
    sourceRevisionId: "rev-1",
    status: "Succeeded",
    startedAt: new Date().toISOString(),
    completedAt: new Date().toISOString(),
    diagnostics: [],
    artifact: artifact(),
    ...overrides
  };
}

function failedBuild() {
  return {
    id: "build-failed",
    projectId: "proj-1",
    sourceRevisionId: "rev-1",
    status: "Failed",
    startedAt: new Date().toISOString(),
    completedAt: new Date().toISOString(),
    diagnostics: [{ severity: "Error", message: "CS1002 Expected ;", file: "Activities/HelloActivity.cs", line: 7, column: 18 }],
    artifact: null
  };
}

function loadedRuntime(overrides?: Partial<ExtensionRuntimeStatus>): ExtensionRuntimeStatus {
  return {
    packageId: "Company.Extensions.Hello",
    version: "1.0.0",
    state: "Loaded",
    features: [{ id: "hello-activity", label: "Hello activity", type: "activity" }],
    history: [
      { version: "1.0.0", state: "Loaded", available: true, promotedAt: new Date().toISOString() },
      { version: "0.9.0", state: "Loaded", available: true, promotedAt: new Date().toISOString() }
    ],
    diagnostics: [],
    ...overrides
  };
}

function failedRuntime(overrides?: Partial<ExtensionRuntimeStatus>): ExtensionRuntimeStatus {
  return {
    ...loadedRuntime(overrides),
    state: "FailedReconciliation",
    diagnostics: [{ severity: "error", message: "Package failed reconciliation.", source: "Nuplane" }]
  };
}

function acceptedPromotion() {
  return {
    status: "Accepted",
    reconcileOutcome: { outcome: "Succeeded", correlationId: "corr-1", reason: "Package validation passed.", isDegraded: false, failedPackages: [] },
    publishedPackage: { packageId: "Company.Extensions.Hello", version: "1.0.0", feedName: "Nuplane", path: "/packages/Company.Extensions.Hello.1.0.0.nupkg" },
    requiresReload: false,
    requiresRestart: false
  };
}

function rejectedPromotion(category: string | number) {
  return {
    status: "Rejected",
    rejectionReason: category
  };
}

function buttonContaining(container: Element, text: string) {
  return Array.from(container.querySelectorAll<HTMLButtonElement>("button"))
    .find(candidate => candidate.textContent?.includes(text) || candidate.getAttribute("aria-label")?.includes(text));
}
