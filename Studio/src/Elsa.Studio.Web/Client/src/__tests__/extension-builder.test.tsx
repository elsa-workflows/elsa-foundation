import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { derivePackageId, ExtensionBuilderPage } from "../app/modules/ExtensionBuilderPage";
import type { ExtensionRepositorySummary, ExtensionRuntimeStatus } from "../app/modules/extensionBuilderApi";
import { StudioHttpError, type ElsaStudioModuleApi } from "../sdk";

describe("extension builder page", () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
    window.localStorage?.clear();
    // Backstop for ADR 0037/#256: the browser must never call the backend host-control surface. Any backend-context
    // http call recorded by the failing stub during a test fails that test here.
    expect(backendCalls.splice(0)).toEqual([]);
  });

  it("gates interactive builder surfaces with ExtensionBuilderCapabilities", async () => {
    // Bridge reports "available" but with all-false capability flags (an authenticated-but-untrusted caller): the UI
    // renders the not-trusted gate rather than the backend-management-unavailable surface.
    const { container, unmount } = await renderExtensionBuilderPage(stubApi({
      hostGetJson: async url => url.endsWith(CAPABILITIES_BRIDGE_PATH)
        ? availableCapabilitiesBridgeResult(deniedCapabilities())
        : {}
    }));

    await flushPromises();

    expect(container.textContent).toContain("You do not have Extension Builder capabilities");
    expect(container.textContent).toContain("GetCapabilities");
    expect(container.textContent).not.toContain("Create workspace");
    expect(container.textContent).not.toContain("Attach server-local");

    await unmount();
  });

  it("routes capability and workspace-surface reads through the Studio management bridge on the host context, never the backend context", async () => {
    const api = stubApi();
    const { container, unmount } = await renderExtensionBuilderPage(api);

    await waitForText(container, "Team Extensions");

    // The capability read and every bootstrap read hit the Studio-owned bridge route group on api.host…
    const hostGet = hostHttpMock(api).getJson;
    expect(hostGet).toHaveBeenCalledWith(CAPABILITIES_BRIDGE_PATH);
    expect(hostGet).toHaveBeenCalledWith(`${BRIDGE_ROOT}/repositories`);
    expect(hostGet).toHaveBeenCalledWith(`${BRIDGE_ROOT}/workspaces`);
    expect(hostGet).toHaveBeenCalledWith(`${BRIDGE_ROOT}/templates`);
    // …and the browser never issues a backend-context request (the afterEach guard also fails on any recorded call).
    expect(backendCalls).toEqual([]);

    await unmount();
  });

  it.each([
    ["unconfigured", "not configured"],
    ["unreachable", "could not be reached"],
    ["unauthorized", "rejected the Studio management credential"],
    ["degraded", "degraded"]
  ] as const)("renders an explicit unavailable state and gates actions when the bridge reports %s", async (status, expected) => {
    const workspaceGetJson = vi.fn(defaultGetJson);
    const { container, unmount } = await renderExtensionBuilderPage(stubApi({
      getJson: workspaceGetJson,
      hostGetJson: async url => url.endsWith(CAPABILITIES_BRIDGE_PATH)
        ? bridgeStatusResult(status, defaultBridgeDetail(status))
        : {}
    }));

    await waitForText(container, "optional privileged host-management integration");

    // The explicit reason is named and actions are gated: no create/attach affordances, no doomed workspace reads.
    expect(container.textContent?.toLowerCase()).toContain(expected.toLowerCase());
    expect(container.textContent?.toLowerCase()).toContain("privileged host-management");
    if (status === "unconfigured") {
      expect(container.textContent).toContain("Studio:BackendBaseUrl");
      expect(container.textContent).toContain("Studio:BackendModuleManagementApiKey");
    }
    expect(container.textContent).not.toContain("Create workspace");
    expect(container.textContent).not.toContain("Attach server-local");
    expect(container.querySelector(".extension-builder-solution-card")).toBeNull();
    // Only the bridge capability read happened; no workspace-surface bridge read was issued.
    expect(workspaceGetJson).not.toHaveBeenCalled();

    // Retry re-runs the bridge read.
    await clickButton(container, "Retry");
    await flushPromises();
    expect(container.textContent).toContain("optional privileged host-management integration");

    await unmount();
  });

  it("renders a distinct permission-denied state (not backend-unavailable) when the capabilities read is forbidden", async () => {
    // A 403 on the bridge capabilities read is a Studio authorization failure — the signed-in user lacks
    // extension-builder.read (#249). It must render "do not have permission" / name the permission, NOT the
    // backend-unavailable/retry surface, and NOT a login prompt.
    const workspaceGetJson = vi.fn(defaultGetJson);
    const { container, unmount } = await renderExtensionBuilderPage(stubApi({
      getJson: workspaceGetJson,
      hostGetJson: async url => {
        if (url.endsWith(CAPABILITIES_BRIDGE_PATH)) throw new StudioHttpError(403, "Forbidden");
        return {};
      }
    }));

    await waitForText(container, "do not have permission");

    expect(container.textContent).toContain("extension-builder.read");
    // Not conflated with the backend-management-unavailable surface or its retry affordance.
    expect(container.textContent).not.toContain("optional privileged host-management integration");
    expect(container.textContent).not.toContain("Retry");
    expect(container.textContent).not.toContain("Create workspace");
    // No workspace-surface bridge read was issued.
    expect(workspaceGetJson).not.toHaveBeenCalled();

    await unmount();
  });

  it("completes a mutation through the Studio management bridge without ever calling the backend context", async () => {
    // PR2 of #256: the bridge forwards mutations to the backend (PR1's 501 not-yet-available answer is gone), so a
    // workspace mutation succeeds through the host-context bridge route and the backend context is never called (the
    // afterEach guard enforces the latter for every test).
    const api = stubApi();
    const { container, unmount } = await renderExtensionBuilderPage(api);
    await waitForText(container, "Team Extensions");

    await fill(await waitForElement<HTMLInputElement>(container, "[aria-label='Repository name']"), "Managed Repository");
    await clickButton(container, "Create managed repo");
    await waitForText(container, "Created managed repository Managed Repository.");

    expect(hostHttpMock(api).postJson).toHaveBeenCalledWith(`${BRIDGE_ROOT}/workspaces`, { displayName: "Managed Repository" });
    expect(backendCalls).toEqual([]);

    await unmount();
  });

  it("keeps polling a running build across transient bridge failures with backoff and recovers on success", async () => {
    // The build poll must not die silently on a bridge hiccup: the first two consecutive 503-management failures are
    // retried with backoff (2s, then 5s, instead of the 900ms cadence), a successful poll resets the counter, and no
    // tracker error is surfaced for a transient failure.
    mockFetch();
    const runningResult = runningBuild();
    let buildReads = 0;
    const postJson = vi.fn(async (url: string) => url.endsWith("/builds") ? runningResult : defaultPostJson(url));
    const getJson = vi.fn(async (url: string) => {
      if (url.includes("/builds/build-running")) {
        buildReads += 1;
        if (buildReads <= 2) throw managementBridgeError(503, "degraded", "The backend management relay hiccupped.");
        return { ...runningResult, status: "Succeeded", completedAt: new Date().toISOString(), artifact: artifact(), artifacts: [artifact()] };
      }
      return defaultGetJson(url);
    });
    const { container, unmount } = await renderExtensionBuilderPage(stubApi({ getJson, postJson }));
    await openSolution(container);
    await waitForText(container, "Activities/HelloActivity.cs");

    // Fake timers (auto-advancing so the suite's promise-flush helpers keep working) drive the backoff waits.
    vi.useFakeTimers({ shouldAdvanceTime: true });
    await clickButton(container, "Build");
    await waitFor(() => buildReads >= 1, "Expected the submit-time build poll.");

    await advancePollTime(2600);
    await waitFor(() => buildReads >= 2, "Expected the first backoff retry.");
    await advancePollTime(5600);
    await waitFor(() => buildReads >= 3, "Expected the second backoff retry.");

    // The third poll succeeded: the build leaves the running state and no bridge tracker error was surfaced.
    await waitFor(() => buttonContaining(container, "Building…") === undefined, "Expected the build to complete.");
    expect(container.textContent).not.toContain("Privileged host management is degraded");

    // Polling ended with the completed build; no stray retry remains scheduled.
    await advancePollTime(10_000);
    expect(buildReads).toBe(3);

    await unmount();
  });

  it("stops the build poll with a tracker error after three consecutive bridge failures, leaving the build running", async () => {
    mockFetch();
    const runningResult = runningBuild();
    let buildReads = 0;
    const postJson = vi.fn(async (url: string) => url.endsWith("/builds") ? runningResult : defaultPostJson(url));
    const getJson = vi.fn(async (url: string) => {
      if (url.includes("/builds/build-running")) {
        buildReads += 1;
        // The submit-time poll succeeds (the build starts fine); every cadence poll after it hits the bridge failure.
        // Starting the failures on the cadence keeps the count deterministic: the poll effect grants a fresh retry
        // budget when it first sees the new build, so a submit-time failure would or would not be counted depending on
        // microtask ordering.
        if (buildReads === 1) return runningResult;
        throw managementBridgeError(503, "unreachable", "The backend management surface could not be reached.");
      }
      return defaultGetJson(url);
    });
    const { container, unmount } = await renderExtensionBuilderPage(stubApi({ getJson, postJson }));
    await openSolution(container);
    await waitForText(container, "Activities/HelloActivity.cs");

    vi.useFakeTimers({ shouldAdvanceTime: true });
    await clickButton(container, "Build");
    await waitFor(() => buildReads >= 1, "Expected the submit-time build poll.");
    await advancePollTime(2600);
    await waitFor(() => buildReads >= 2, "Expected the first failing cadence poll.");
    await advancePollTime(5600);
    await waitFor(() => buildReads >= 4, "Expected the 2s and 5s backoff retries.");

    // The third consecutive failure stops polling and names the backend-management state…
    await waitForText(container, "Privileged host management is unreachable: The backend management surface could not be reached.");
    // …while the build stays rendered as running: the manual refresh affordance is the retry.
    expect(buttonContaining(container, "Building…")).toBeDefined();

    // Polling stopped: no further reads even well past every cadence.
    await advancePollTime(10_000);
    expect(buildReads).toBe(4);

    await unmount();
  });

  it("surfaces the relay-timeout detail on a promote and refreshes build and runtime state best-effort", async () => {
    // A 504/unreachable on promote means the relay gave up waiting but the promotion may still have completed
    // backend-side: the tracker error must carry the bridge's detail saying so, and the build + runtime status must be
    // re-read so a promotion that actually landed becomes visible.
    mockFetch();
    const detail = "The backend management relay timed out; the promotion may still have completed on the backend.";
    const postJson = vi.fn(async (url: string) => {
      if (url.endsWith("/promote")) throw managementBridgeError(504, "unreachable", detail);
      return defaultPostJson(url);
    });
    const getJson = vi.fn(defaultGetJson);
    const { container, unmount } = await renderExtensionBuilderPage(stubApi({ getJson, postJson }));
    await openSolution(container);
    await waitForText(container, "Activities/HelloActivity.cs");
    const runtimeReads = () => getJson.mock.calls.filter(([url]) => String(url).endsWith("/runtime-status")).length;
    const runtimeReadsBefore = runtimeReads();

    await clickTab(container, "Promote");
    await clickButton(container, "Promote build");
    await waitForText(container, "Privileged host management is unreachable");

    // The tracker error carries the bridge's may-still-have-completed detail…
    expect(container.textContent).toContain("may still have completed");
    // …and the mutation's effects are re-read best-effort: the runtime status and the promoted build.
    await waitFor(() => runtimeReads() > runtimeReadsBefore, "Expected a best-effort runtime-status refresh after the relay timeout.");
    expect(getJson).toHaveBeenCalledWith(`${BRIDGE_ROOT}/builds/build-1`);

    await unmount();
  });

  it("flips to the backend-management-unavailable surface when a bootstrap read rejects with the bridge's 503 envelope", async () => {
    const { container, unmount } = await renderExtensionBuilderPage(stubApi({
      getJson: async url => {
        if (url.endsWith("/workspaces")) throw managementBridgeError(503, "unreachable", "The backend management surface could not be reached.");
        return defaultGetJson(url);
      }
    }));

    await waitForText(container, "optional privileged host-management integration");

    // The surface names the reported state's detail and gates all actions.
    expect(container.textContent).toContain("could not be reached");
    expect(container.textContent).not.toContain("Create workspace");
    expect(container.querySelector(".extension-builder-solution-card")).toBeNull();

    await unmount();
  });

  it("surfaces a mid-session bridge failure as a tracker error naming the state without flipping the page", async () => {
    let failSourceControlReads = false;
    const { container, unmount } = await renderExtensionBuilderPage(stubApi({
      getJson: async url => {
        if (failSourceControlReads && url.endsWith("/source-control/status")) {
          throw managementBridgeError(504, "degraded", "The backend management relay timed out.");
        }
        return defaultGetJson(url);
      }
    }));
    await waitForText(container, "Team Extensions");

    failSourceControlReads = true;
    await openSolution(container);
    await waitForText(container, "Privileged host management is degraded: The backend management relay timed out.");

    // A mid-session failure is a tracker error inside the workspace — the page does not flip to unavailable.
    expect(container.textContent).not.toContain("optional privileged host-management integration");
    expect(container.querySelector(".extension-builder-solution-card")).toBeNull(); // still inside the workspace

    await unmount();
  });

  it("defaults to a simplified single-step extension flow", async () => {
    const { container, unmount } = await renderExtensionBuilderPage(stubApi(), { advanced: false });

    await waitForText(container, "Team Extensions");

    // Simplified vocabulary and a single create affordance on the home view.
    expect(container.textContent).toContain("Extensions");
    expect(container.querySelector("[aria-label='Extension name']")).not.toBeNull();

    // Advanced-only create surfaces are hidden on the home view.
    expect(container.querySelector("[aria-label='Server-local repository path']")).toBeNull();
    expect(container.querySelector("[aria-label='Clone repository URL']")).toBeNull();

    await openSolution(container);

    // Advanced-only workspace surfaces are hidden.
    expect(container.querySelector("[aria-label='Working branch']")).toBeNull();
    expect(container.querySelector("[aria-label='Project name']")).toBeNull();
    expect(container.querySelector("[aria-label='Build command']")).toBeNull();
    expect(container.querySelector("[aria-label='Build target path']")).toBeNull();
    expect(hasTab(container, "Runtime")).toBe(false);
    expect(hasTab(container, "Source control")).toBe(false);

    // Pack is the prominent action in the command bar.
    expect(buttonContaining(container, "Pack")).not.toBeNull();

    await unmount();
  });

  it("reveals the full workbench when advanced mode is enabled", async () => {
    const { container, unmount } = await renderExtensionBuilderPage(stubApi(), { advanced: false });
    await waitForText(container, "Team Extensions");

    expect(container.querySelector("[aria-label='Build command']")).toBeNull();

    await enableAdvanced(container);

    // Advanced create affordances appear on the home view.
    expect(container.querySelector("[aria-label='Server-local repository path']")).not.toBeNull();
    expect(container.querySelector("[aria-label='Clone repository URL']")).not.toBeNull();

    await openSolution(container);
    await openDock(container, "Build output");

    // The full workspace surfaces are revealed.
    expect(container.querySelector("[aria-label='Build command']")).not.toBeNull();
    expect(container.querySelector("[aria-label='Project name']")).not.toBeNull();
    expect(hasTab(container, "Runtime")).toBe(true);
    expect(hasTab(container, "Source control")).toBe(true);

    await unmount();
  });

  it("keeps the build panel visible when promoting a package in simplified mode", async () => {
    // In simplified mode only the build tab exists; promotion handlers still switch the active
    // inspector tab to runtime/promote, so the dock must clamp back to build instead of rendering
    // an empty body.
    const { container, unmount } = await renderExtensionBuilderPage(stubApi(), { advanced: false });
    await waitForText(container, "Team Extensions");
    await openSolution(container);
    await openDock(container, "Build output");

    await waitForText(container, "Package outputs");
    await clickButton(container, "Promote package");
    await flushPromises();

    // The dock stays on the build panel (no Runtime/Promote tab to switch to) and is not blank.
    expect(container.textContent).toContain("Package outputs");
    expect(container.querySelector(".extension-builder-dock-body")?.textContent?.trim()).not.toBe("");
    expect(hasTab(container, "Runtime")).toBe(false);

    await unmount();
  });

  it("creates a file with a path separator as a slash-delimited URL, not an encoded segment", async () => {
    // The backend file routes are catch-all and do not decode "%2F"; encoding the whole path would
    // create a file literally named "Activities%2FHelloWorld.cs" (and 404 on read).
    const api = stubApi();
    const { container, unmount } = await renderExtensionBuilderPage(api);
    await openSolution(container);
    await waitForText(container, "Activities/HelloActivity.cs");

    await fill(await waitForElement<HTMLInputElement>(container, "[aria-label='New file path']"), "Activities/HelloWorld.cs");
    await clickButton(container, "Create file");
    await flushPromises();

    expect(hostHttpMock(api).putJson).toHaveBeenCalledWith(
      `${BRIDGE_ROOT}/workspaces/ws-1/files/Activities/HelloWorld.cs`,
      expect.objectContaining({ content: expect.any(String) })
    );

    await unmount();
  });

  it("surfaces Add project and explains the blocked Pack when a solution has no project (simple mode)", async () => {
    const emptyWorkspace = { id: "ws-1", displayName: "Team Extensions", ownerId: "alice", trustContext: "trusted-team", projectIds: [] };
    const { container, unmount } = await renderExtensionBuilderPage(stubApi({
      getJson: async url => {
        if (url.endsWith("/capabilities")) return trustedCapabilities();
        if (url.endsWith("/repositories")) return [repositorySummary({ projectCount: 0 })];
        if (url.endsWith("/workspaces")) return [emptyWorkspace];
        if (url.endsWith("/templates")) return templates();
        if (url.endsWith("/repository-tree")) return repositoryTree();
        if (url.endsWith("/source-control/status")) return sourceControlStatus();
        return {};
      }
    }), { advanced: false });
    await waitForText(container, "Team Extensions");
    await openSolution(container);

    // Simple mode still offers a way to add a project when the solution has none.
    expect(await waitForElement<HTMLInputElement>(container, "[aria-label='Project name']")).not.toBeNull();

    // The Pack button explains why it is disabled instead of claiming it is ready.
    const pack = buttonContaining(container, "Pack");
    expect(pack?.disabled).toBe(true);
    expect(pack?.title).toContain("Add a project");

    await unmount();
  });

  it("creates an extension in a single step (repository, working copy, and project)", async () => {
    const postJson = vi.fn(async (url: string, body: unknown) => {
      if (url.endsWith("/workspaces")) return managedWorkspace();
      if (url.endsWith("/working-copies/select")) return workingCopySummary();
      if (url.endsWith("/projects")) return { ...project(), id: "proj-new", workspaceId: "ws-managed" };
      return defaultPostJson(url);
    });
    const getJson = vi.fn(defaultGetJson);
    const { container, unmount } = await renderExtensionBuilderPage(stubApi({ getJson, postJson }), { advanced: false });
    await waitForText(container, "Team Extensions");

    await fill(await waitForElement<HTMLInputElement>(container, "[aria-label='Extension name']"), "Acme Orders");
    await clickButton(container, "New extension");
    await waitForText(container, "Created extension Acme Orders.");

    expect(postJson).toHaveBeenCalledWith("/_elsa/studio/backend-management/extension-builder/workspaces", { displayName: "Acme Orders" });
    expect(postJson).toHaveBeenCalledWith(
      "/_elsa/studio/backend-management/extension-builder/workspaces/ws-managed/working-copies/select",
      expect.objectContaining({ allowProtectedBranchEdit: false }),
      { timeoutMs: 40_000 }
    );
    expect(postJson).toHaveBeenCalledWith(
      "/_elsa/studio/backend-management/extension-builder/workspaces/ws-managed/projects",
      expect.objectContaining({ templateId: "elsa-activity", displayName: "Acme Orders", packageId: "Acme.Orders", packageVersion: "1.0.0" }),
      { timeoutMs: 70_000 }
    );

    await unmount();
  });

  it("derives a clean package id from an extension name", () => {
    expect(derivePackageId("Acme Orders")).toBe("Acme.Orders");
    expect(derivePackageId("acme-orders.api")).toBe("Acme.Orders.Api");
    expect(derivePackageId("123")).toBe("");
  });

  it("renders owner-scoped workspaces, templates, files, build status, and runtime state", async () => {
    const { container, unmount } = await renderExtensionBuilderPage(stubApi());

    await waitForText(container, "Team Extensions");

    // Home view surfaces repository summaries.
    expect(container.textContent).toContain("Extension Builder");
    expect(container.textContent).toContain("Repositories");
    expect(container.textContent).toContain("Team Extensions");
    expect(container.textContent).toContain("alice");
    expect(container.textContent).toContain("not connected");
    expect(container.textContent).toContain("main");

    await openSolution(container);
    await waitForText(container, "Activities/HelloActivity.cs");

    // Workspace surfaces templates, files, editor, and runtime state.
    expect(container.textContent).toContain("Elsa activity/module");
    expect(container.textContent).toContain("Generic .NET class library");
    expect(container.querySelector<HTMLInputElement>("[aria-label='Project name']")?.value).toBe("ElsaActivityExtension");
    expect(container.textContent).toContain("Activities/HelloActivity.cs");
    expect(container.querySelector<HTMLTextAreaElement>("[aria-label='Project file editor']")?.value).toContain("HelloActivity");
    expect(container.querySelector(".studio-code-editor")?.getAttribute("data-language")).toBe("csharp");
    expect(container.querySelector(".studio-code-editor-header code")?.textContent).toBe("elsa://extension-builder/workspaces/ws-1/projects/proj-1/files/Activities%2FHelloActivity.cs");
    expect(container.textContent).toContain("Loaded");
    await clickTab(container, "Runtime");
    expect(container.textContent).toContain("Hello activity");

    await unmount();
  });

  it("renders repository summaries even when workspace details are not hydrated", async () => {
    const { container, unmount } = await renderExtensionBuilderPage(stubApi({
      getJson: async url => {
        if (url.endsWith("/capabilities")) return trustedCapabilities();
        if (url.endsWith("/repositories")) return [repositorySummary()];
        if (url.endsWith("/workspaces")) return [];
        if (url.endsWith("/templates")) return templates();
        return {};
      }
    }));

    await waitForText(container, "Team Extensions");

    expect(container.textContent).toContain("Repositories");
    expect(container.textContent).toContain("not connected");
    expect(container.textContent).not.toContain("No Extension Builder repositories are available");

    await unmount();
  });

  it("does not show a global 404 when the initial file detail endpoint is unavailable", async () => {
    const { container, unmount } = await renderExtensionBuilderPage(stubApi({
      getJson: async url => {
        if (url.endsWith("/capabilities")) return trustedCapabilities();
        if (url.endsWith("/repositories")) return [repositorySummary()];
        if (url.endsWith("/workspaces")) return [workspaceWithProject()];
        if (url.endsWith("/templates")) return templates();
        if (url.endsWith("/repository-tree")) return repositoryTree();
        if (url.endsWith("/source-control/status")) return sourceControlStatus();
        if (url.endsWith("/files")) return projectFiles();
        if (url.endsWith("/runtime-status")) return loadedRuntime();
        if (url.endsWith("Activities/HelloActivity.cs")) throw new Error("Request failed with 404.");
        if (url.includes("/projects/proj-1")) return { ...project(), builds: [succeededBuild()] };
        return {};
      }
    }));

    await openSolution(container);
    await waitForText(container, "HelloActivity.cs");
    await flushPromises();

    expect(container.textContent).not.toContain("Request failed with 404.");

    await unmount();
  });

  it("attaches a server-local repository, refreshes the rail, and selects it", async () => {
    let attached = false;
    const serverPath = "/srv/elsa/extensions/server-repo";
    const remoteState = "https://github.com/acme/server-extension.git";
    const postJson = vi.fn(async (url: string, body: unknown) => {
      if (url.endsWith("/repositories/server-local")) {
        attached = true;
        return { id: "ws-server", displayName: "Server Extensions", ownerId: "alice", projectIds: [] };
      }

      return defaultPostJson(url);
    });
    const getJson = vi.fn(async (url: string) => {
      if (url.endsWith("/capabilities")) return trustedCapabilities();
      if (url.endsWith("/repositories")) {
        return attached ? [repositorySummary({
          id: "ws-server",
          name: "Server Extensions",
          activeBranch: "release",
          remoteState,
          projectCount: 0
        })] : [repositorySummary()];
      }
      if (url.endsWith("/workspaces")) {
        return attached ? [{ id: "ws-server", displayName: "Server Extensions", ownerId: "alice", projectIds: [] }] : [workspaceWithProject()];
      }
      if (url.endsWith("/templates")) return templates();
      return defaultGetJson(url);
    });
    const { container, unmount } = await renderExtensionBuilderPage(stubApi({ getJson, postJson }));
    await waitForText(container, "Team Extensions");

    await fill(await waitForElement<HTMLInputElement>(container, "[aria-label='Server-local repository path']"), serverPath);
    await fill(await waitForElement<HTMLInputElement>(container, "[aria-label='Server-local repository name']"), "Server Extensions");
    await clickButton(container, "Attach server-local");
    await waitForText(container, "Server Extensions");

    expect(postJson).toHaveBeenCalledWith("/_elsa/studio/backend-management/extension-builder/repositories/server-local", {
      path: serverPath,
      displayName: "Server Extensions"
    }, { timeoutMs: 40_000 });
    expect(container.querySelector(".extension-builder-solution-card.active")?.textContent).toContain("Server Extensions");
    expect(container.querySelector(".extension-builder-solution-card.active")?.textContent).toContain(`release · ${remoteState}`);
    expect(container.textContent).toContain("Attached server-local repository Server Extensions.");

    await unmount();
  });

  it("clones a Git repository, refreshes the rail, and selects the clone", async () => {
    let cloneCreated = false;
    const repositoryUrl = "https://github.com/acme/extension-pack.git";
    const postJson = vi.fn(async (url: string, body: unknown) => {
      if (url.endsWith("/repositories/clone")) {
        cloneCreated = true;
        return { id: "ws-clone", displayName: "Cloned Extensions", ownerId: "alice", projectIds: [] };
      }

      return defaultPostJson(url);
    });
    const getJson = vi.fn(async (url: string) => {
      if (url.endsWith("/capabilities")) return trustedCapabilities();
      if (url.endsWith("/repositories")) {
        return cloneCreated ? [repositorySummary({
          id: "ws-clone",
          name: "Cloned Extensions",
          activeBranch: "main",
          remoteState: repositoryUrl,
          projectCount: 0
        })] : [repositorySummary()];
      }
      if (url.endsWith("/workspaces")) {
        return cloneCreated ? [{ id: "ws-clone", displayName: "Cloned Extensions", ownerId: "alice", projectIds: [] }] : [workspaceWithProject()];
      }
      if (url.endsWith("/templates")) return templates();
      return defaultGetJson(url);
    });
    const { container, unmount } = await renderExtensionBuilderPage(stubApi({ getJson, postJson }));
    await waitForText(container, "Team Extensions");

    await fill(await waitForElement<HTMLInputElement>(container, "[aria-label='Clone repository URL']"), repositoryUrl);
    await fill(await waitForElement<HTMLInputElement>(container, "[aria-label='Clone repository name']"), "Cloned Extensions");
    await clickButton(container, "Clone from Git");
    await waitForText(container, "Cloned Extensions");

    expect(postJson).toHaveBeenCalledWith("/_elsa/studio/backend-management/extension-builder/repositories/clone", {
      repositoryUrl,
      displayName: "Cloned Extensions"
    }, { timeoutMs: 130_000 });
    expect(container.querySelector(".extension-builder-solution-card.active")?.textContent).toContain("Cloned Extensions");
    expect(container.querySelector(".extension-builder-solution-card.active")?.textContent).toContain(`main · ${repositoryUrl}`);
    expect(container.textContent).toContain("Cloned repository Cloned Extensions.");

    await unmount();
  });

  it("creates a managed repository, refreshes the rail, and selects it", async () => {
    let created = false;
    const postJson = vi.fn(async (url: string, body: unknown) => {
      if (url.endsWith("/workspaces")) {
        created = true;
        expect(body).toEqual({ displayName: "Managed Repository" });
        return managedWorkspace();
      }
      return defaultPostJson(url);
    });
    const getJson = vi.fn(async (url: string) => {
      if (url.endsWith("/capabilities")) return trustedCapabilities();
      if (url.endsWith("/repositories")) return created ? [repositorySummary(), managedRepositorySummary()] : [repositorySummary()];
      if (url.endsWith("/workspaces")) return created ? [workspaceWithProject(), managedWorkspace()] : [workspaceWithProject()];
      return defaultGetJson(url);
    });
    const { container, unmount } = await renderExtensionBuilderPage(stubApi({ getJson, postJson }));
    await waitForText(container, "Team Extensions");

    const repositoryName = await waitForElement<HTMLInputElement>(container, "[aria-label='Repository name']");
    await fill(repositoryName, "Managed Repository");
    await clickButton(container, "Create managed repo");
    await waitForText(container, "Created managed repository Managed Repository.");

    expect(postJson).toHaveBeenCalledWith("/_elsa/studio/backend-management/extension-builder/workspaces", { displayName: "Managed Repository" });
    const activeRepository = container.querySelector(".extension-builder-solution-card.active");
    expect(activeRepository?.textContent).toContain("Managed Repository");
    expect(activeRepository?.textContent).toContain("main · not connected");
    expect(activeRepository?.textContent).not.toContain("dirty");

    await unmount();
  });

  it("opens an explicit working branch and refreshes active branch metadata", async () => {
    let opened = false;
    const postJson = vi.fn(async (url: string, body: unknown) => {
      if (url.endsWith("/working-copies/select")) {
        opened = true;
        expect(url).toBe("/_elsa/studio/backend-management/extension-builder/workspaces/ws-1/working-copies/select");
        expect(body).toEqual({
          sessionId: expect.any(String),
          branchName: "feature/session-work",
          allowProtectedBranchEdit: false
        });
        return workingCopySummary();
      }
      return defaultPostJson(url);
    });
    const getJson = vi.fn(async (url: string) => {
      if (url.endsWith("/capabilities")) return trustedCapabilities();
      if (url.endsWith("/repositories")) return [opened ? repositorySummary({ activeBranch: "feature/session-work", isDirty: true }) : repositorySummary()];
      return defaultGetJson(url);
    });
    const { container, unmount } = await renderExtensionBuilderPage(stubApi({ getJson, postJson }));
    await waitForText(container, "Team Extensions");
    await openSolution(container);

    const branchInput = await waitForElement<HTMLInputElement>(container, "[aria-label='Working branch']");
    await fill(branchInput, "feature/session-work");
    await clickButton(container, "Open working branch");
    await waitForText(container, "Opened working branch feature/session-work.");

    expect(postJson).toHaveBeenCalledWith("/_elsa/studio/backend-management/extension-builder/workspaces/ws-1/working-copies/select", expect.objectContaining({
      branchName: "feature/session-work",
      allowProtectedBranchEdit: false
    }), { timeoutMs: 40_000 });
    // The command-bar breadcrumb reflects the refreshed active branch.
    expect(container.querySelector(".extension-builder-crumb")?.textContent).toContain("feature/session-work");

    await unmount();
  });

  it("applies a trusted repository template and refreshes generated files and source status", async () => {
    let applied = false;
    const generatedFile = {
      path: "src/Generated/GeneratedActivity.cs",
      kind: "File",
      size: 72,
      isDirty: true,
      updatedAt: new Date().toISOString()
    };
    const postJson = vi.fn(async (url: string, body: unknown) => {
      if (url.endsWith("/templates/apply")) {
        applied = true;
        expect(body).toEqual({
          templateId: "csharp-class",
          scope: "Item",
          targetPath: "src/Generated",
          parameters: {
            name: "GeneratedActivity",
            namespace: "Company.Extensions.Generated"
          }
        });
        return {
          templateId: "csharp-class",
          scope: "Item",
          files: [generatedFile],
          tree: { ...repositoryTree(), isDirty: true, entries: [...repositoryTree().entries, generatedFile] }
        };
      }
      return defaultPostJson(url);
    });
    const getJson = vi.fn(async (url: string) => {
      if (url.endsWith("/source-control/status")) return applied ? sourceControlStatus({ unstaged: [generatedFile.path] }) : sourceControlStatus();
      if (url.endsWith("src/Generated/GeneratedActivity.cs")) return {
        ...generatedFile,
        content: "namespace Company.Extensions.Generated;\n\npublic sealed class GeneratedActivity\n{\n}"
      };
      return defaultGetJson(url);
    });
    const { container, unmount } = await renderExtensionBuilderPage(stubApi({ getJson, postJson }));
    await openSolution(container);
    await waitForText(container, "Activities/HelloActivity.cs");

    await fill(await waitForElement<HTMLInputElement>(container, "[aria-label='Template target path']"), "src/Generated");
    await fill(await waitForElement<HTMLInputElement>(container, "[aria-label='Template parameter Class name']"), "GeneratedActivity");
    await fill(await waitForElement<HTMLInputElement>(container, "[aria-label='Template parameter Namespace']"), "Company.Extensions.Generated");
    await clickExactButton(container, "Apply");
    await waitForText(container, "Applied C# class.");

    expect(postJson).toHaveBeenCalledWith("/_elsa/studio/backend-management/extension-builder/workspaces/ws-1/templates/apply", expect.objectContaining({ templateId: "csharp-class" }), { timeoutMs: 70_000 });
    // The generated file opens in the editor (full path shown in the editor status bar).
    expect(container.querySelector(".extension-builder-editor-status")?.textContent).toContain("src/Generated/GeneratedActivity.cs");
    expect(container.querySelector<HTMLTextAreaElement>("[aria-label='Project file editor']")?.value).toContain("GeneratedActivity");
    await clickTab(container, "Source control");
    expect(container.textContent).toContain("src/Generated/GeneratedActivity.cs");

    await unmount();
  });

  it("stages, unstages, stages all, inspects diffs, and commits from the source inspector", async () => {
    let gitStatus = sourceControlStatus({ unstaged: ["src/Status.cs"] });
    const postJson = vi.fn(async (url: string, body: unknown) => {
      if (url.endsWith("/source-control/stage")) {
        expect(body).toEqual({ path: "src/Status.cs" });
        gitStatus = sourceControlStatus({ staged: ["src/Status.cs"] });
        return gitStatus;
      }
      if (url.endsWith("/source-control/unstage")) {
        expect(body).toEqual({ path: "src/Status.cs" });
        gitStatus = sourceControlStatus({ unstaged: ["src/Status.cs"] });
        return gitStatus;
      }
      if (url.endsWith("/source-control/stage-all")) {
        gitStatus = sourceControlStatus({ staged: ["src/Status.cs"] });
        return gitStatus;
      }
      if (url.endsWith("/source-control/commit")) {
        expect(body).toEqual({ message: "Add source control file" });
        gitStatus = sourceControlStatus();
        return { commitId: "abc123", message: "Add source control file", status: gitStatus };
      }
      if (url.endsWith("/source-control/push")) {
        return { operation: "Push", state: "Completed", message: "Pushed feature/source-control to origin.", remote: "origin", branch: "feature/source-control", ahead: 0, behind: 0, status: gitStatus };
      }
      if (url.endsWith("/source-control/pull")) {
        return { operation: "Pull", state: "Completed", message: "Already up to date.", remote: "origin", branch: "feature/source-control", ahead: 0, behind: 0, status: gitStatus };
      }
      return defaultPostJson(url);
    });
    const getJson = vi.fn(async (url: string) => {
      if (url.endsWith("/source-control/status")) return gitStatus;
      if (url.includes("/source-control/diff/")) return { path: "src/Status.cs", isStaged: url.includes("staged=true"), patch: "+namespace Status;" };
      if (url.endsWith("/repositories")) return [repositorySummary({ isDirty: gitStatus.isDirty })];
      return defaultGetJson(url);
    });
    const { container, unmount } = await renderExtensionBuilderPage(stubApi({ getJson, postJson }));
    await openSolution(container);
    await waitForText(container, "Activities/HelloActivity.cs");

    await clickTab(container, "Source control");
    await waitForText(container, "src/Status.cs");
    await clickExactButton(container, "Stage");
    await waitForText(container, "No unstaged changes.");
    await clickExactButton(container, "Unstage");
    await waitForText(container, "No staged changes.");
    await clickButton(container, "Stage all");
    await waitForText(container, "No unstaged changes.");
    await clickButton(container, "src/Status.cs");
    await waitForText(container, "+namespace Status;");
    await fill(await waitForElement<HTMLTextAreaElement>(container, "[aria-label='Commit message']"), "Add source control file");
    await clickButton(container, "Commit staged");
    await waitForText(container, "Committed staged changes.");
    await clickExactButton(container, "Push");
    await waitForText(container, "Pushed committed changes.");
    await clickExactButton(container, "Pull");
    await waitForText(container, "Pulled remote changes.");

    expect(postJson).toHaveBeenCalledWith("/_elsa/studio/backend-management/extension-builder/workspaces/ws-1/source-control/stage", { path: "src/Status.cs" });
    expect(postJson).toHaveBeenCalledWith("/_elsa/studio/backend-management/extension-builder/workspaces/ws-1/source-control/unstage", { path: "src/Status.cs" });
    expect(postJson).toHaveBeenCalledWith("/_elsa/studio/backend-management/extension-builder/workspaces/ws-1/source-control/stage-all", {});
    expect(postJson).toHaveBeenCalledWith("/_elsa/studio/backend-management/extension-builder/workspaces/ws-1/source-control/commit", { message: "Add source control file" }, { timeoutMs: 40_000 });
    expect(postJson).toHaveBeenCalledWith("/_elsa/studio/backend-management/extension-builder/workspaces/ws-1/source-control/push", {}, { timeoutMs: 130_000 });
    expect(postJson).toHaveBeenCalledWith("/_elsa/studio/backend-management/extension-builder/workspaces/ws-1/source-control/pull", {}, { timeoutMs: 130_000 });
    expect(getJson).toHaveBeenCalledWith("/_elsa/studio/backend-management/extension-builder/workspaces/ws-1/source-control/diff/src/Status.cs?staged=true");

    await unmount();
  });

  it("blocks navigation and keeps the edit when an auto-save flush fails", async () => {
    const { container, unmount } = await renderExtensionBuilderPage(stubApi({
      putJson: async () => { throw new Error("Request failed with 503."); }
    }));
    await openSolution(container);
    await waitForText(container, "Activities/HelloActivity.cs");

    const editor = await waitForElement<HTMLTextAreaElement>(container, "[aria-label='Project file editor']");
    await waitFor(() => editor.value.includes("HelloActivity"), "Expected editor content to load.");
    await fill(editor, `${editor.value}\n// will fail to save`);

    // The flush fails, so going back must NOT proceed (no silent data loss).
    await clickButton(container, "Solutions");
    await flushPromises();

    expect(container.textContent).toContain("503");
    expect(container.querySelector(".extension-builder-solution-card")).toBeNull(); // still in the workspace
    expect(container.querySelector("[aria-label='Project file editor']")).not.toBeNull();

    await unmount();
  });

  it("keeps Save available as a force-save when the file is clean", async () => {
    const { container, unmount } = await renderExtensionBuilderPage(stubApi());
    await openSolution(container);
    await waitForText(container, "Activities/HelloActivity.cs");

    expect(buttonContaining(container, "Save")?.disabled).toBe(false);

    await unmount();
  });

  it("auto-saves the active file after a debounce when auto-save is on", async () => {
    const api = stubApi();
    const { container, unmount } = await renderExtensionBuilderPage(api);
    await openSolution(container);
    await waitForText(container, "Activities/HelloActivity.cs");

    const editor = await waitForElement<HTMLTextAreaElement>(container, "[aria-label='Project file editor']");
    await waitFor(() => editor.value.includes("HelloActivity"), "Expected editor content to load.");
    expect((container.querySelector("[aria-label='Auto-save']") as HTMLInputElement | null)?.checked).toBe(true);

    await fill(editor, `${editor.value}\n// auto`);
    // No manual Save; wait out the ~1s debounce and let the write settle.
    await new Promise(resolve => setTimeout(resolve, 1200));
    await flushPromises();

    expect(hostHttpMock(api).putJson).toHaveBeenCalledWith(
      `${BRIDGE_ROOT}/workspaces/ws-1/files/Activities/HelloActivity.cs`,
      expect.objectContaining({ content: expect.stringContaining("// auto") })
    );

    await unmount();
  });

  it("auto-saves a trailing edit made during an in-flight explicit Save (no further keystroke)", async () => {
    // Regression (#210): the debounced auto-save effect omitted the save-busy signal from its deps, so a
    // dirty edit made while an explicit Save was in flight was never rescheduled once the Save settled.
    // Hold the first PUT (the explicit Save) open, edit again, then release it and assert the trailing
    // edit auto-persists on its own.
    const putBodies: string[] = [];
    const firstPut = deferred<void>();
    const putJson = vi.fn(async (url: string, body: unknown) => {
      const content = (body as { content?: string })?.content ?? "";
      putBodies.push(content);
      // Only the first PUT (the explicit Save) is held open; the trailing auto-save resolves immediately.
      if (putBodies.length === 1) await firstPut.promise;
      return { path: "Activities/HelloActivity.cs", type: "file", content };
    });

    const { container, unmount } = await renderExtensionBuilderPage(stubApi({ putJson }));
    await openSolution(container);
    await waitForText(container, "Activities/HelloActivity.cs");

    const editor = await waitForElement<HTMLTextAreaElement>(container, "[aria-label='Project file editor']");
    await waitFor(() => editor.value.includes("HelloActivity"), "Expected editor content to load.");

    // First edit + explicit Save; the PUT hangs, so the "save" scope stays busy.
    await fill(editor, `${editor.value}\n// explicit-save`);
    await clickButton(container, "Save");
    await flushPromises();
    await waitFor(() => putBodies.length === 1, "Expected the explicit Save PUT to be in flight.");

    // Trailing edit made while the Save is still in flight — no further keystroke after the Save settles.
    await fill(editor, `${editor.value}\n// trailing-edit`);

    // Release the explicit Save. The auto-save effect must re-run (save no longer busy) and persist the
    // trailing edit after its debounce, with no additional user input.
    firstPut.resolve();
    await new Promise(resolve => setTimeout(resolve, 1200));
    await flushPromises();

    await waitFor(() => putBodies.some(content => content.includes("// trailing-edit")),
      "Expected the trailing edit to auto-save after the in-flight Save completed.");

    await unmount();
  });

  it("flushes a pending edit when navigating away with auto-save on", async () => {
    const api = stubApi();
    const { container, unmount } = await renderExtensionBuilderPage(api);
    await openSolution(container);
    await waitForText(container, "Activities/HelloActivity.cs");

    const editor = await waitForElement<HTMLTextAreaElement>(container, "[aria-label='Project file editor']");
    await waitFor(() => editor.value.includes("HelloActivity"), "Expected editor content to load.");
    await fill(editor, `${editor.value}\n// nav`);

    // Navigate back without waiting for the debounce: the edit is flushed (not discarded).
    await clickButton(container, "Solutions");
    await flushPromises();

    expect(hostHttpMock(api).putJson).toHaveBeenCalledWith(
      `${BRIDGE_ROOT}/workspaces/ws-1/files/Activities/HelloActivity.cs`,
      expect.objectContaining({ content: expect.stringContaining("// nav") })
    );
    await waitForText(container, "Team Extensions");

    await unmount();
  });

  it("does not auto-save when the toggle is turned off", async () => {
    const api = stubApi();
    const { container, unmount } = await renderExtensionBuilderPage(api);
    await openSolution(container);
    await waitForText(container, "Activities/HelloActivity.cs");

    const toggle = await waitForElement<HTMLInputElement>(container, "[aria-label='Auto-save']");
    await setChecked(toggle, false);
    expect(window.localStorage?.getItem("elsa.extensionBuilder.autoSave")).toBe("false");

    const editor = await waitForElement<HTMLTextAreaElement>(container, "[aria-label='Project file editor']");
    await waitFor(() => editor.value.includes("HelloActivity"), "Expected editor content to load.");
    await fill(editor, `${editor.value}\n// no-auto`);
    await new Promise(resolve => setTimeout(resolve, 1200));
    await flushPromises();

    expect(hostHttpMock(api).putJson).not.toHaveBeenCalled();

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
    mockFetch();
    const api = stubApi({ getJson, postJson });
    const { container, unmount } = await renderExtensionBuilderPage(api);
    await openSolution(container);
    await waitForText(container, "Activities/HelloActivity.cs");

    const editor = await waitForElement<HTMLTextAreaElement>(container, "[aria-label='Project file editor']");
    await waitFor(() => editor.value.includes("HelloActivity"), "Expected editor content to load.");
    await fill(editor, `${editor.value}\n// updated`);
    await waitFor(() => editor.value.includes("// updated"), "Expected editor edit to apply.");

    await clickButton(container, "Save");
    await flushPromises();
    expect(hostHttpMock(api).putJson).toHaveBeenCalledWith(
      `${BRIDGE_ROOT}/workspaces/ws-1/files/Activities/HelloActivity.cs`,
      expect.objectContaining({ content: expect.stringContaining("// updated") })
    );

    await clickButton(container, "Build");
    await flushPromises();
    await flushPromises();
    expect(postJson).toHaveBeenCalledWith("/_elsa/studio/backend-management/extension-builder/workspaces/ws-1/builds", { projectId: "proj-1", command: "Build", targetPath: null });
    expect(container.textContent).toContain("Build submitted");
    expect(container.textContent).toContain("Succeeded");

    await clickTab(container, "Promote");
    await clickButton(container, "Promote build");
    await flushPromises();

    expect(postJson).toHaveBeenCalledWith("/_elsa/studio/backend-management/extension-builder/builds/build-1/promote", {}, { timeoutMs: 130_000 });
    expect(container.textContent).toContain("is loaded at runtime");

    await unmount();
  });

  it("submits restore build and test jobs with explicit repository targets", async () => {
    const postJson = vi.fn(async (url: string) => {
      if (url.endsWith("/builds")) return succeededBuild({ sourceRevisionId: null });
      return defaultPostJson(url);
    });
    const { container, unmount } = await renderExtensionBuilderPage(stubApi({ postJson }));
    await openSolution(container);
    await waitForText(container, "Activities/HelloActivity.cs");
    await openDock(container, "Build output");

    await selectValue(await waitForElement<HTMLSelectElement>(container, "[aria-label='Build command']"), "Test");
    await fill(await waitForElement<HTMLInputElement>(container, "[aria-label='Build target path']"), "src/TeamExtensions.Tests/TeamExtensions.Tests.csproj");
    await clickButton(container, "Run command");
    await flushPromises();

    expect(postJson).toHaveBeenCalledWith("/_elsa/studio/backend-management/extension-builder/workspaces/ws-1/builds", {
      projectId: "proj-1",
      command: "Test",
      targetPath: "src/TeamExtensions.Tests/TeamExtensions.Tests.csproj"
    });

    await selectValue(await waitForElement<HTMLSelectElement>(container, "[aria-label='Build command']"), "Restore");
    await fill(await waitForElement<HTMLInputElement>(container, "[aria-label='Build target path']"), "");
    await clickButton(container, "Run command");
    await flushPromises();

    expect(postJson).toHaveBeenCalledWith("/_elsa/studio/backend-management/extension-builder/workspaces/ws-1/builds", {
      projectId: "proj-1",
      command: "Restore",
      targetPath: null
    });

    await unmount();
  });

  it("packs repository targets and shows package artifact outputs", async () => {
    const artifacts = [
      artifact({ id: "artifact-a", packageId: "Company.Extensions.One", version: "1.0.0", fileName: "Company.Extensions.One.1.0.0.nupkg", size: 4096, branch: "feature/pack", sourceRevisionId: "abcdef123456" }),
      artifact({ id: "artifact-b", packageId: "Company.Extensions.Two", version: "2.1.0", fileName: "Company.Extensions.Two.2.1.0.nupkg", size: 2048, branch: "feature/pack", sourceRevisionId: "abcdef123456" })
    ];
    const postJson = vi.fn(async (url: string) => {
      if (url.endsWith("/builds")) return succeededBuild({ sourceRevisionId: "abcdef123456", artifact: artifacts[0], artifacts });
      if (url.includes("/artifacts/artifact-a/promote")) return acceptedPromotion({ packageId: "Company.Extensions.One", version: "1.0.0" });
      return defaultPostJson(url);
    });
    const getJson = vi.fn(async (url: string) => {
      if (url.includes("/builds/build-1")) return succeededBuild({ sourceRevisionId: "abcdef123456", artifact: artifacts[0], artifacts });
      return defaultGetJson(url);
    });
    const { container, unmount } = await renderExtensionBuilderPage(stubApi({ getJson, postJson }));
    await openSolution(container);
    await waitForText(container, "Activities/HelloActivity.cs");
    await openDock(container, "Build output");

    await selectValue(await waitForElement<HTMLSelectElement>(container, "[aria-label='Build command']"), "Pack");
    await fill(await waitForElement<HTMLInputElement>(container, "[aria-label='Build target path']"), "Hello.slnx");
    await clickButton(container, "Run command");
    await flushPromises();

    expect(postJson).toHaveBeenCalledWith("/_elsa/studio/backend-management/extension-builder/workspaces/ws-1/builds", {
      projectId: "proj-1",
      command: "Pack",
      targetPath: "Hello.slnx"
    });
    expect(container.textContent).toContain("Package outputs");
    expect(container.textContent).toContain("Company.Extensions.One 1.0.0");
    expect(container.textContent).toContain("Company.Extensions.Two 2.1.0");
    expect(container.textContent).toContain("feature/pack");
    expect(container.textContent).toContain("abcdef12");

    await clickButton(container, "Promote package");
    await flushPromises();

    expect(postJson).toHaveBeenCalledWith("/_elsa/studio/backend-management/extension-builder/builds/build-1/artifacts/artifact-a/promote", {}, { timeoutMs: 130_000 });

    await unmount();
  });

  it("shows diagnostics and opens the referenced source location", async () => {
    const activeFileDiagnostic = { severity: "Error", message: "CS1002 Expected ;", file: "Activities/HelloActivity.cs", line: 7, column: 18 };
    const otherFileDiagnostic = { severity: "Warning", message: "CS0168 Unused variable", file: "Activities/OtherActivity.cs", line: 3, column: 12 };
    const { container, unmount } = await renderExtensionBuilderPage(stubApi({
      getJson: async url => {
        if (url.endsWith("/capabilities")) return trustedCapabilities();
        if (url.endsWith("/workspaces")) return [workspaceWithProject()];
        if (url.endsWith("/templates")) return templates();
        if (url.endsWith("/repository-tree")) return repositoryTree();
        if (url.endsWith("/source-control/status")) return sourceControlStatus();
        if (url.endsWith("/files")) return projectFiles();
        if (url.endsWith("/runtime-status")) return loadedRuntime();
        if (url.includes("/builds/build-failed")) return failedBuild([activeFileDiagnostic, otherFileDiagnostic]);
        if (url.endsWith("Activities/HelloActivity.cs")) return repositoryFile();
        if (url.includes("/projects/proj-1")) return { ...project(), builds: [failedBuild([activeFileDiagnostic, otherFileDiagnostic])] };
        return {};
      }
    }));
    await openSolution(container);
    await openDock(container, "Build output");
    await waitForText(container, "CS1002");

    expect(container.textContent).toContain("CS1002");
    await clickButton(container, "CS1002");
    await flushPromises();

    expect(container.textContent).toContain("Activities/HelloActivity.cs:7:18");
    expect(container.querySelector("[aria-label='Inline diagnostics']")?.textContent).toContain("Expected ;");
    expect(container.querySelector(".studio-code-editor-diagnostics")?.textContent).toContain("CS1002");
    expect(container.querySelector(".studio-code-editor-diagnostics")?.textContent).not.toContain("CS0168");

    await unmount();
  });

  it.each([
    ["Duplicate", "never silently overwritten"],
    ["InvalidManifest", "manifest"],
    ["DependencyPolicy", "dependency"],
    ["MalformedPackage", "malformed"]
  ] as const)("renders distinct promotion rejection guidance for %s", async (category, expected) => {
    const postJson = vi.fn(async (url: string) => url.endsWith("/promote") ? rejectedPromotion(category) : {});
    const { container, unmount } = await renderExtensionBuilderPage(stubApi({ postJson }));
    await openSolution(container);
    await waitForText(container, "Activities/HelloActivity.cs");

    await clickTab(container, "Promote");
    await clickButton(container, "Promote build");
    await waitForText(container, expected);

    expect(container.textContent?.toLowerCase()).toContain(expected.toLowerCase());
    await unmount();
  });

  it("normalizes numeric enum values and backend file kind fallbacks", async () => {
    const postJson = vi.fn(async (url: string) => url.endsWith("/promote") ? rejectedPromotion(0) : {});
    const { container, unmount } = await renderExtensionBuilderPage(stubApi({
      postJson,
      getJson: async url => {
        if (url.endsWith("/capabilities")) return trustedCapabilities();
        if (url.endsWith("/workspaces")) return [workspaceWithProject()];
        if (url.endsWith("/templates")) return templates();
        if (url.endsWith("/repository-tree")) return {
          ...repositoryTree(),
          entries: [
            { path: "Activities", kind: "Folder", size: 0, isDirty: false },
            { path: "Activities/HelloActivity.cs", kind: 3, size: 37, isDirty: false }
          ]
        };
        if (url.endsWith("/source-control/status")) return sourceControlStatus();
        if (url.endsWith("/files")) return [
          { path: "Activities", type: "folder" },
          { path: "Activities/HelloActivity.cs", kind: 2, content: "public sealed class HelloActivity { }" }
        ];
        if (url.endsWith("/runtime-status")) return { ...loadedRuntime(), state: 0, history: [{ version: "1.0.0", state: 0, available: true }] };
        if (url.includes("/builds/build-1")) return succeededBuild({ status: 2 });
        if (url.endsWith("Activities/HelloActivity.cs")) return { path: "Activities/HelloActivity.cs", kind: 2, content: "public sealed class HelloActivity { }" };
        if (url.includes("/projects/proj-1")) return { ...project(), latestBuildStatus: 2, runtimeStatus: 0, builds: [succeededBuild({ status: 2 })] };
        return {};
      }
    }));
    await openSolution(container);
    await waitForText(container, "Activities/HelloActivity.cs");

    expect(container.textContent).toContain("Succeeded");
    expect(container.textContent).toContain("Loaded");
    expect(buttonContaining(container, "Activities")?.disabled).toBe(true);

    await clickTab(container, "Promote");
    await clickButton(container, "Promote build");
    await flushPromises();

    expect(container.textContent).toContain("Duplicate package id and version rejected");
    await unmount();
  });

  it("normalizes null new-format runtime features to an empty contribution list", async () => {
    const { container, unmount } = await renderExtensionBuilderPage(stubApi({
      getJson: async url => {
        if (url.endsWith("/capabilities")) return trustedCapabilities();
        if (url.endsWith("/workspaces")) return [workspaceWithProject()];
        if (url.endsWith("/templates")) return templates();
        if (url.endsWith("/repository-tree")) return repositoryTree();
        if (url.endsWith("/source-control/status")) return sourceControlStatus();
        if (url.endsWith("/files")) return projectFiles();
        if (url.endsWith("/runtime-status")) return { ...loadedRuntime(), features: null };
        if (url.includes("/builds/build-1")) return succeededBuild();
        if (url.endsWith("Activities/HelloActivity.cs")) return repositoryFile();
        if (url.includes("/projects/proj-1")) return { ...project(), builds: [succeededBuild()] };
        return {};
      }
    }));
    await openSolution(container);
    await waitForText(container, "Activities/HelloActivity.cs");

    await clickTab(container, "Runtime");
    expect(container.textContent).toContain("contributed no runtime capabilities");

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
        if (url.endsWith("/repository-tree")) return repositoryTree();
        if (url.endsWith("/source-control/status")) return sourceControlStatus();
        if (url.endsWith("/files")) return projectFiles();
        if (url.endsWith("/runtime-status")) return loadedRuntime();
        if (url.endsWith("Activities/HelloActivity.cs")) return repositoryFile();
        if (url.includes("/projects/proj-1")) return { ...project(), currentSourceRevisionId: "rev-2", builds: [succeededBuild({ sourceRevisionId: "rev-1" })] };
        return {};
      }
    }));
    await openSolution(container);
    await waitForText(container, "Activities/HelloActivity.cs");

    await clickTab(container, "Promote");
    const promoteButton = buttonContaining(container, "Promote build");

    expect(promoteButton?.disabled).toBe(true);
    expect(promoteButton?.title).toContain("stale");
    expect(postJson).not.toHaveBeenCalledWith("/_elsa/studio/backend-management/extension-builder/builds/build-1/promote", {}, { timeoutMs: 130_000 });

    await unmount();
  });

  it("supports retry reconciliation, rollback gating, and generic template no-contributions messaging", async () => {
    const selectedProject = genericProject();
    const postJson = vi.fn(async (url: string) => {
      if (url.endsWith("/retry-reconcile")) return loadedRuntime({ features: [] });
      if (url.endsWith("/rollback")) return loadedRuntime({ version: "0.9.0", features: [] });
      return {};
    });
    const { container, unmount } = await renderExtensionBuilderPage(stubApi({
      postJson,
      getJson: async url => {
        if (url.endsWith("/capabilities")) return trustedCapabilities();
        if (url.endsWith("/workspaces")) return [workspaceWithProject({ project: selectedProject })];
        if (url.endsWith("/templates")) return templates();
        if (url.endsWith("/repository-tree")) return repositoryTree();
        if (url.endsWith("/source-control/status")) return sourceControlStatus();
        if (url.endsWith("/files")) return projectFiles();
        if (url.endsWith("/runtime-status")) return failedRuntime({ features: [] });
        if (url.endsWith("Activities/HelloActivity.cs")) return repositoryFile();
        if (url.includes(`/projects/${selectedProject.id}`)) return { ...selectedProject, builds: [succeededBuild()] };
        return {};
      }
    }));
    await openSolution(container);
    await waitForText(container, "Activities/HelloActivity.cs");

    expect(container.textContent).toContain("Generic .NET class library");
    await clickTab(container, "Runtime");
    expect(container.textContent).toContain("FailedReconciliation");
    expect(container.textContent).toContain("contributed no runtime capabilities");

    await clickButton(container, "Retry reconciliation");
    await flushPromises();
    expect(postJson).toHaveBeenCalledWith(`/_elsa/studio/backend-management/extension-builder/projects/${selectedProject.id}/retry-reconcile`, {}, { timeoutMs: 130_000 });
    await waitForText(container, "0.9.0");

    await clickButton(container, "Rollback");
    await flushPromises();
    expect(postJson).toHaveBeenCalledWith(`/_elsa/studio/backend-management/extension-builder/projects/${selectedProject.id}/rollback`, { version: "0.9.0" }, { timeoutMs: 130_000 });

    await unmount();
  });

  it("shows active build progress in the command bar while a build is running", async () => {
    // A submitted build that is still Running server-side must give live in-button feedback
    // ("Building…"/"Packing…" + spinner), not merely a disabled control.
    const runningResult = runningBuild();
    const postJson = vi.fn(async (url: string) => url.endsWith("/builds") ? runningResult : defaultPostJson(url));
    const getJson = vi.fn(async (url: string) => {
      if (url.includes("/builds/build-running")) return runningResult;
      return defaultGetJson(url);
    });

    // Advanced mode: the headline action is "Build".
    const advanced = await renderExtensionBuilderPage(stubApi({ getJson, postJson }));
    await openSolution(advanced.container);
    await waitForText(advanced.container, "Activities/HelloActivity.cs");
    await clickButton(advanced.container, "Build");
    await flushPromises();

    const buildButton = buttonContaining(advanced.container, "Building…");
    expect(buildButton).not.toBeNull();
    expect(buildButton?.disabled).toBe(true);
    expect(buildButton?.querySelector(".is-spinning")).not.toBeNull();
    await advanced.unmount();

    // Simple mode: the headline action is "Pack".
    const simple = await renderExtensionBuilderPage(stubApi({ getJson, postJson }), { advanced: false });
    await waitForText(simple.container, "Team Extensions");
    await openSolution(simple.container);
    await waitForText(simple.container, "Activities/HelloActivity.cs");
    await clickButton(simple.container, "Pack");
    await flushPromises();

    const packButton = buttonContaining(simple.container, "Packing…");
    expect(packButton).not.toBeNull();
    expect(packButton?.querySelector(".is-spinning")).not.toBeNull();
    await simple.unmount();
  });

  it("scopes busy state per operation so an in-flight source action does not disable the build action", async () => {
    // Regression for the single global operationBusy flag that disabled every action at once. A
    // slow source-control stage must not disable the unrelated Build/Pack headline action.
    const staged = deferred<ReturnType<typeof sourceControlStatus>>();
    const postJson = vi.fn(async (url: string) => {
      if (url.endsWith("/source-control/stage")) return staged.promise;
      if (url.endsWith("/builds")) return succeededBuild({ sourceRevisionId: null });
      return defaultPostJson(url);
    });
    const getJson = vi.fn(async (url: string) => {
      if (url.endsWith("/source-control/status")) return sourceControlStatus({ unstaged: ["src/Status.cs"] });
      return defaultGetJson(url);
    });
    const { container, unmount } = await renderExtensionBuilderPage(stubApi({ getJson, postJson }));
    await openSolution(container);
    await waitForText(container, "Activities/HelloActivity.cs");

    await clickTab(container, "Source control");
    await waitForText(container, "src/Status.cs");

    // Start a stage that never resolves; the source panel Stage button becomes busy…
    await clickExactButton(container, "Stage");
    await flushPromises();

    // …but the command-bar Build action remains enabled (per-operation, not global, busy state).
    const buildButton = buttonContaining(container, "Build");
    expect(buildButton?.disabled).toBe(false);

    // Let the stage settle so teardown does not leak a pending promise.
    staged.resolve(sourceControlStatus({ staged: ["src/Status.cs"] }));
    await flushPromises();

    await unmount();
  });
});

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(res => { resolve = res; });
  return { promise, resolve };
}

function runningBuild() {
  return {
    id: "build-running",
    projectId: "proj-1",
    sourceRevisionId: "rev-1",
    status: "Running",
    startedAt: new Date().toISOString(),
    completedAt: null,
    diagnostics: [],
    artifact: null,
    artifacts: []
  };
}

// The Studio-owned bridge route group every Extension Builder call is routed through (ADR 0037, #256). Mirrors
// `studioExtensionBuilderBridgeRoot` / `studioExtensionBuilderCapabilitiesPath` in the SDK.
const BRIDGE_ROOT = "/_elsa/studio/backend-management/extension-builder";
const CAPABILITIES_BRIDGE_PATH = `${BRIDGE_ROOT}/capabilities`;

// Backend-context calls recorded by the failing stub. The afterEach guard asserts this stays empty: after
// ADR 0037/#256 the browser must never talk to the backend host-control surface.
const backendCalls: string[] = [];

function failingBackendHttp() {
  const reject = (verb: string) => vi.fn(async (url: string) => {
    backendCalls.push(`${verb} ${url}`);
    throw new Error(`Unexpected backend-context ${verb} ${url}: Extension Builder must use the Studio management bridge (ADR 0037, #256).`);
  });
  return {
    requestJson: reject("REQUEST"),
    getJson: reject("GET"),
    postJson: reject("POST"),
    putJson: reject("PUT"),
    deleteJson: reject("DELETE"),
    postForm: reject("POST-FORM")
  };
}

function hostHttpMock(api: ElsaStudioModuleApi) {
  return api.host.http as unknown as Record<"getJson" | "postJson" | "putJson" | "deleteJson", ReturnType<typeof vi.fn>>;
}

// The bridge's infrastructure error envelope (503, or 504 for a relay timeout): camelCase body with a top-level
// detail and a management status block, thrown as the StudioHttpError the sdk client would produce.
function managementBridgeError(status: 503 | 504, kind: string, detail: string) {
  return new StudioHttpError(status, detail, null, {
    detail,
    management: { status: kind, detail, backendBaseUrl: null, checkedAt: new Date().toISOString() }
  });
}

function stubApi(options?: {
  getJson?: (url: string) => Promise<unknown>;
  postJson?: (url: string, body: unknown) => Promise<unknown>;
  putJson?: (url: string, body: unknown) => Promise<unknown>;
  deleteJson?: (url: string) => Promise<unknown>;
  hostGetJson?: (url: string) => Promise<unknown>;
}): ElsaStudioModuleApi {
  // Every Extension Builder call goes through the Studio management bridge on the Studio origin (api.host): the
  // capabilities probe (hostGetJson override) and all workspace-surface reads (getJson override) share the host
  // getJson, dispatched on the capabilities path. The backend context's http verbs FAIL any test that calls them —
  // the browser never talks to the backend host-control surface (ADR 0037, #256).
  const capabilitiesGetJson = options?.hostGetJson ?? defaultHostGetJson;
  const workspaceGetJson = options?.getJson ?? defaultGetJson;
  return {
    host: {
      baseUrl: "https://studio.example/",
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      http: {
        getJson: vi.fn(async (url: string) => url.endsWith(CAPABILITIES_BRIDGE_PATH) ? capabilitiesGetJson(url) : workspaceGetJson(url)),
        postJson: vi.fn(options?.postJson ?? defaultPostJson),
        putJson: vi.fn(options?.putJson ?? defaultPutJson),
        deleteJson: vi.fn(options?.deleteJson ?? (async () => ({})))
      }
    },
    backend: {
      baseUrl: "https://foundation.example/",
      http: failingBackendHttp()
    },
    dialogs: {
      confirm: vi.fn(async () => true),
      prompt: vi.fn(async () => null),
      alert: vi.fn(async () => {})
    }
  } as unknown as ElsaStudioModuleApi;
}

async function defaultHostGetJson(url: string): Promise<unknown> {
  if (url.endsWith(CAPABILITIES_BRIDGE_PATH)) return availableCapabilitiesBridgeResult(trustedCapabilities());
  return {};
}

function availableCapabilitiesBridgeResult(capabilities: ReturnType<typeof trustedCapabilities>) {
  return {
    status: "available",
    detail: "The backend Extension Builder capabilities are reachable.",
    capabilities,
    backendBaseUrl: "https://foundation.example",
    checkedAt: new Date().toISOString()
  };
}

function bridgeStatusResult(status: string, detail: string) {
  return { status, detail, capabilities: null, backendBaseUrl: null, checkedAt: new Date().toISOString() };
}

function defaultBridgeDetail(status: string) {
  switch (status) {
    case "unconfigured": return "Privileged host management is not configured on the Studio host. Set Studio:BackendServerBaseUrl (or Studio:BackendBaseUrl for a shared URL) and Studio:BackendModuleManagementApiKey to enable Extension Builder.";
    case "unreachable": return "The privileged host-management surface could not be reached.";
    case "unauthorized": return "The backend rejected the Studio management credential.";
    case "degraded": return "The privileged host-management surface is degraded.";
    default: return "Privileged host-management status is unknown.";
  }
}

async function defaultGetJson(url: string): Promise<unknown> {
  if (url.endsWith("/repositories")) return [repositorySummary()];
  if (url.endsWith("/workspaces")) return [workspaceWithProject()];
  if (url.endsWith("/templates")) return templates();
  if (url.endsWith("/repository-tree")) return repositoryTree();
  if (url.endsWith("/source-control/status")) return sourceControlStatus();
  if (url.endsWith("/files")) return projectFiles();
  if (url.endsWith("/runtime-status")) return loadedRuntime();
  if (url.includes("/builds/build-1")) return succeededBuild();
  if (url.endsWith("Activities/HelloActivity.cs")) return repositoryFile();
  // Read-back of a file a test just created or saved: echo an empty file for the decoded /files/ suffix so the
  // editor always opens a valid file (an empty object would crash it on the missing path).
  if (url.includes("/files/")) return { path: decodeURIComponent(url.split("/files/")[1]), kind: "File", content: "" };
  if (url.includes("/projects/proj-1")) return { ...project(), builds: [succeededBuild()] };
  return {};
}

async function defaultPostJson(url: string): Promise<unknown> {
  if (url.endsWith("/builds")) return succeededBuild();
  if (url.endsWith("/promote")) return acceptedPromotion();
  return {};
}

// Echoes a file write back like the backend does, deriving the path from the decoded URL suffix after
// /files/ so the page keeps a valid active file (an empty echo would crash the explorer/editor on
// path.split) no matter which path a test saved.
async function defaultPutJson(url: string, body: unknown): Promise<unknown> {
  const path = decodeURIComponent(url.split("/files/")[1] ?? "Activities/HelloActivity.cs");
  return { path, type: "file", content: (body as { content?: string })?.content };
}

const ADVANCED_PREFERENCE_KEY = "elsa.extensionBuilder.advanced";

// Most of this suite exercises the full (advanced) workbench. Default render to advanced mode so
// those surfaces are present; simple-mode tests opt out with { advanced: false }.
async function renderExtensionBuilderPage(api: ElsaStudioModuleApi, options?: { advanced?: boolean }) {
  if (options?.advanced === false) {
    window.localStorage?.removeItem(ADVANCED_PREFERENCE_KEY);
  } else {
    window.localStorage?.setItem(ADVANCED_PREFERENCE_KEY, "true");
  }

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

// The build-log read is the only Extension Builder call left on raw fetch (plain text); everything else goes
// through the host http client, so any other fetch in a test is a routing regression and fails loudly.
function mockFetch() {
  const fetchMock = vi.fn(async (url: string) => {
    if (!String(url).includes("/log")) throw new Error(`Unexpected fetch ${url}: Extension Builder must use the host http client.`);
    return new Response("Build succeeded.", { status: 200, headers: { "content-type": "text/plain" } });
  });
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

async function clickButton(container: Element, text: string) {
  const findButton = () => {
    const buttons = Array.from(container.querySelectorAll<HTMLButtonElement>("button"))
      .filter(candidate => !candidate.disabled && (candidate.textContent?.includes(text) || candidate.getAttribute("aria-label")?.includes(text)));
    return buttons.find(candidate => candidate.getAttribute("role") !== "tab") ?? buttons[0];
  };
  await waitFor(() => findButton() !== undefined, `Could not find button containing '${text}'.`);
  flushSync(() => findButton()!.dispatchEvent(new MouseEvent("click", { bubbles: true })));
  await flushPromises();
}

async function clickExactButton(container: Element, text: string) {
  const button = Array.from(container.querySelectorAll<HTMLButtonElement>("button"))
    .find(candidate => !candidate.disabled && candidate.textContent?.trim() === text);
  if (!button) throw new Error(`Could not find enabled button '${text}'.`);
  flushSync(() => button.dispatchEvent(new MouseEvent("click", { bubbles: true })));
  await flushPromises();
}

async function fill(element: HTMLInputElement | HTMLTextAreaElement, value: string) {
  flushSync(() => {
    const setter = Object.getOwnPropertyDescriptor(Object.getPrototypeOf(element), "value")?.set;
    setter?.call(element, value);
    element.dispatchEvent(new Event("input", { bubbles: true }));
    element.dispatchEvent(new Event("change", { bubbles: true }));
  });
  await flushPromises();
}

async function selectValue(element: HTMLSelectElement, value: string) {
  flushSync(() => {
    const setter = Object.getOwnPropertyDescriptor(Object.getPrototypeOf(element), "value")?.set;
    setter?.call(element, value);
    element.dispatchEvent(new Event("change", { bubbles: true }));
  });
  await flushPromises();
}

async function enableAdvanced(container: Element) {
  const toggle = await waitForElement<HTMLInputElement>(container, "[aria-label='Advanced mode']");
  flushSync(() => {
    const setter = Object.getOwnPropertyDescriptor(Object.getPrototypeOf(toggle), "checked")?.set;
    setter?.call(toggle, true);
    toggle.dispatchEvent(new Event("click", { bubbles: true }));
    toggle.dispatchEvent(new Event("change", { bubbles: true }));
  });
  await flushPromises();
}

async function setChecked(element: HTMLInputElement, value: boolean) {
  flushSync(() => {
    const setter = Object.getOwnPropertyDescriptor(Object.getPrototypeOf(element), "checked")?.set;
    setter?.call(element, value);
    element.dispatchEvent(new Event("click", { bubbles: true }));
    element.dispatchEvent(new Event("change", { bubbles: true }));
  });
  await flushPromises();
}

async function clickTab(container: Element, text: string) {
  const findTab = () => Array.from(container.querySelectorAll<HTMLButtonElement>("[role='tab']"))
    .find(candidate => candidate.textContent?.includes(text));
  await waitFor(() => findTab() !== undefined, `Could not find tab containing '${text}'.`);
  flushSync(() => findTab()!.dispatchEvent(new MouseEvent("click", { bubbles: true })));
  await flushPromises();
}

// The redesigned module opens on a home/solution-browser view; opening a solution card
// enters the focused workspace (explorer + editor + bottom dock).
async function openSolution(container: Element, name?: string) {
  await waitForElement(container, ".extension-builder-solution-card");
  const cards = Array.from(container.querySelectorAll<HTMLButtonElement>(".extension-builder-solution-card"));
  const card = (name ? cards.find(candidate => candidate.textContent?.includes(name)) : cards[0]) ?? cards[0];
  if (!card) throw new Error("Could not find a solution card to open.");
  flushSync(() => card.dispatchEvent(new MouseEvent("click", { bubbles: true })));
  await flushPromises();
}

// Build/Source/Promote/Runtime controls live in the bottom dock, collapsed by default.
async function openDock(container: Element, label: string) {
  await clickTab(container, label);
}

async function flushPromises() {
  await new Promise(resolve => setTimeout(resolve, 0));
}

// Advances auto-advancing fake timers in steps, flushing between steps so React can commit state updates made from
// promise callbacks (its scheduler uses MessageChannel macrotasks, which vi.advanceTimersByTimeAsync alone never
// yields to) and effects can schedule the next poll timer before time moves past it.
async function advancePollTime(totalMs: number) {
  const step = 500;
  for (let elapsed = 0; elapsed < totalMs; elapsed += step) {
    await flushPromises();
    await vi.advanceTimersByTimeAsync(step);
  }
  await flushPromises();
}

async function waitForText(container: Element, text: string) {
  await waitFor(() => container.textContent?.includes(text) === true, `Expected text '${text}'.`);
}

async function waitForElement<T extends Element>(container: Element, selector: string) {
  let element = container.querySelector<T>(selector);
  await waitFor(() => {
    element = container.querySelector<T>(selector);
    return element !== null;
  }, `Expected element '${selector}'.`);
  return element!;
}

async function waitFor(predicate: () => boolean, message: string) {
  for (let attempt = 0; attempt < 30; attempt += 1) {
    if (predicate()) return;
    await flushPromises();
  }
  throw new Error(message);
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
  const selectedProject = options?.project ?? project();
  return {
    id: "ws-1",
    displayName: "Team Extensions",
    ownerId: "alice",
    trustContext: "trusted-team",
    projectIds: [selectedProject.id]
  };
}

function repositorySummary(overrides?: Partial<ExtensionRepositorySummary>) {
  return {
    id: "ws-1",
    name: "Team Extensions",
    ownerId: "alice",
    activeBranch: "main",
    isDirty: false,
    remoteState: "not-connected",
    latestBuildStatus: "Succeeded",
    attentionCount: 0,
    projectCount: 1,
    updatedAt: new Date().toISOString(),
    ...overrides
  };
}

function workingCopySummary() {
  return {
    id: "wc-1",
    workspaceId: "ws-1",
    ownerId: "alice",
    sessionId: "studio-session",
    branchName: "feature/session-work",
    isActive: true,
    isProtectedBranch: false,
    isDirty: true,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString()
  };
}

function managedWorkspace() {
  return {
    id: "ws-managed",
    displayName: "Managed Repository",
    ownerId: "alice",
    trustContext: "trusted-team",
    projectIds: []
  };
}

function managedRepositorySummary() {
  return {
    id: "ws-managed",
    name: "Managed Repository",
    ownerId: "alice",
    activeBranch: "main",
    isDirty: false,
    remoteState: "not-connected",
    latestBuildStatus: null,
    attentionCount: 0,
    projectCount: 0,
    updatedAt: new Date().toISOString()
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
    id: "generic-proj",
    name: "Generic Extension",
    templateId: "generic-dotnet",
    packageId: "Company.Extensions.Generic"
  };
}

function templates() {
  return [
    {
      id: "elsa-activity",
      name: "Elsa activity/module",
      description: "Creates an Elsa activity extension.",
      scope: "Project",
      compatibleFileExtensions: [".slnx", ".csproj"],
      parameters: [
        { name: "packageId", displayName: "Package id", required: true, defaultValue: "Company.Extensions.ElsaActivity" },
        { name: "packageVersion", displayName: "Package version", required: true, defaultValue: "1.0.0" }
      ],
      primary: true,
      tags: ["elsa"]
    },
    {
      id: "generic-dotnet",
      name: "Generic .NET class library",
      description: "Creates a generic .NET package.",
      scope: "Project",
      compatibleFileExtensions: [".slnx", ".csproj"],
      parameters: [
        { name: "packageId", displayName: "Package id", required: true, defaultValue: "Company.Extensions.Generic" },
        { name: "packageVersion", displayName: "Package version", required: true, defaultValue: "1.0.0" }
      ],
      primary: false,
      tags: ["dotnet"]
    },
    {
      id: "csharp-class",
      name: "C# class",
      description: "Creates a C# source file.",
      scope: "Item",
      compatibleFileExtensions: [".csproj", ".cs"],
      parameters: [
        { name: "name", displayName: "Class name", required: true, defaultValue: "NewClass" },
        { name: "namespace", displayName: "Namespace", required: true, defaultValue: "Company.Extensions" }
      ],
      primary: false,
      tags: ["source"]
    }
  ];
}

function projectFiles() {
  return [
    { path: "Activities/HelloActivity.cs", kind: "Source", content: "public sealed class HelloActivity { }" },
    { path: "Hello.csproj", kind: "Project", content: "<Project />" }
  ];
}

function repositoryFile() {
  return { ...projectFiles()[0], kind: "File" };
}

function repositoryTree() {
  return {
    workspaceId: "ws-1",
    activeBranch: "main",
    isDirty: false,
    solutions: [{ path: "Hello.slnx", name: "Hello", isSelected: true }],
    entries: projectFiles().map(file => ({
      path: file.path,
      kind: file.path.endsWith(".csproj") ? "Project" : "File",
      size: file.content.length,
      isDirty: false,
      updatedAt: new Date().toISOString()
    }))
  };
}

function sourceControlStatus(options?: { staged?: string[]; unstaged?: string[] }) {
  const staged = options?.staged ?? [];
  const unstaged = options?.unstaged ?? [];
  const stagedFiles = staged.map(path => ({ path, status: "A", isStaged: true, isUnstaged: false }));
  const unstagedFiles = unstaged.map(path => ({ path, status: "??", isStaged: false, isUnstaged: true }));
  return {
    workspaceId: "ws-1",
    activeBranch: "feature/source-control",
    isDirty: stagedFiles.length + unstagedFiles.length > 0,
    changedFiles: [...stagedFiles, ...unstagedFiles],
    stagedFiles,
    unstagedFiles
  };
}

function artifact(overrides?: Partial<ReturnType<typeof artifactShape>>) {
  return {
    ...artifactShape(),
    ...overrides
  };
}

function artifactShape() {
  return {
    id: "artifact-1",
    buildId: "build-1",
    packageId: "Company.Extensions.Hello",
    version: "1.0.0",
    fileName: "Company.Extensions.Hello.1.0.0.nupkg",
    size: 4096,
    workspaceId: "ws-1",
    sourceRevisionId: "rev-1",
    branch: "main",
    sourceIsDirty: false
  };
}

function succeededBuild(overrides?: { id?: string; sourceRevisionId?: string | null; status?: string | number; artifact?: ReturnType<typeof artifact> | null; artifacts?: ReturnType<typeof artifact>[] }) {
  const buildArtifact = artifact();
  return {
    id: "build-1",
    projectId: "proj-1",
    sourceRevisionId: "rev-1",
    status: "Succeeded",
    startedAt: new Date().toISOString(),
    completedAt: new Date().toISOString(),
    diagnostics: [],
    artifact: buildArtifact,
    artifacts: [buildArtifact],
    ...overrides
  };
}

function failedBuild(diagnostics = [{ severity: "Error", message: "CS1002 Expected ;", file: "Activities/HelloActivity.cs", line: 7, column: 18 }]) {
  return {
    id: "build-failed",
    projectId: "proj-1",
    sourceRevisionId: "rev-1",
    status: "Failed",
    startedAt: new Date().toISOString(),
    completedAt: new Date().toISOString(),
    diagnostics,
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

function acceptedPromotion(overrides?: { packageId?: string; version?: string }) {
  const packageId = overrides?.packageId ?? "Company.Extensions.Hello";
  const version = overrides?.version ?? "1.0.0";
  return {
    status: "Accepted",
    reconcileOutcome: { outcome: "Succeeded", correlationId: "corr-1", reason: "Package validation passed.", isDegraded: false, failedPackages: [] },
    publishedPackage: { packageId, version, feedName: "Nuplane", path: `/packages/${packageId}.${version}.nupkg` },
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

function hasTab(container: Element, text: string) {
  return Array.from(container.querySelectorAll<HTMLButtonElement>("[role='tab']"))
    .some(candidate => candidate.textContent?.trim() === text);
}

function buttonContaining(container: Element, text: string) {
  return Array.from(container.querySelectorAll<HTMLButtonElement>("button"))
    .find(candidate => candidate.textContent?.includes(text) || candidate.getAttribute("aria-label")?.includes(text));
}
