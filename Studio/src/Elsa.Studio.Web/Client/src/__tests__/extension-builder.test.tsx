import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { derivePackageId, ExtensionBuilderPage } from "../app/modules/ExtensionBuilderPage";
import type { ExtensionRepositorySummary, ExtensionRuntimeStatus } from "../app/modules/extensionBuilderApi";
import type { ElsaStudioModuleApi } from "../sdk";

describe("extension builder page", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
    window.localStorage?.clear();
  });

  it("gates interactive builder surfaces with ExtensionBuilderCapabilities", async () => {
    const { container, unmount } = await renderExtensionBuilderPage(stubApi({
      getJson: async url => url.endsWith("/capabilities") ? deniedCapabilities() : []
    }));

    await flushPromises();

    expect(container.textContent).toContain("You do not have Extension Builder capabilities");
    expect(container.textContent).toContain("GetCapabilities");
    expect(container.textContent).not.toContain("Create workspace");
    expect(container.textContent).not.toContain("Attach server-local");

    await unmount();
  });

  it("defaults to a simplified single-step extension flow", async () => {
    const { container, unmount } = await renderExtensionBuilderPage(stubApi(), { advanced: false });

    await waitForText(container, "Team Extensions");

    // Simplified vocabulary and a single create affordance.
    expect(container.textContent).toContain("Extensions");
    expect(container.querySelector("[aria-label='Extension name']")).not.toBeNull();

    // Advanced-only surfaces are hidden.
    expect(container.querySelector("[aria-label='Server-local repository path']")).toBeNull();
    expect(container.querySelector("[aria-label='Clone repository URL']")).toBeNull();
    expect(container.querySelector("[aria-label='Working branch']")).toBeNull();
    expect(container.querySelector("[aria-label='Project name']")).toBeNull();
    expect(container.querySelector("[aria-label='Build command']")).toBeNull();
    expect(container.querySelector("[aria-label='Build target path']")).toBeNull();
    expect(hasTab(container, "Runtime")).toBe(false);
    expect(hasTab(container, "Source")).toBe(false);

    // Pack is the prominent action.
    expect(buttonContaining(container, "Pack")).not.toBeNull();

    await unmount();
  });

  it("reveals the full workbench when advanced mode is enabled", async () => {
    const { container, unmount } = await renderExtensionBuilderPage(stubApi(), { advanced: false });
    await waitForText(container, "Team Extensions");

    expect(container.querySelector("[aria-label='Build command']")).toBeNull();

    await enableAdvanced(container);

    expect(container.querySelector("[aria-label='Server-local repository path']")).not.toBeNull();
    expect(container.querySelector("[aria-label='Clone repository URL']")).not.toBeNull();
    expect(container.querySelector("[aria-label='Build command']")).not.toBeNull();
    expect(container.querySelector("[aria-label='Project name']")).not.toBeNull();
    expect(hasTab(container, "Runtime")).toBe(true);
    expect(hasTab(container, "Source")).toBe(true);

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

    expect(postJson).toHaveBeenCalledWith("/_elsa/extension-builder/workspaces", { displayName: "Acme Orders" });
    expect(postJson).toHaveBeenCalledWith(
      "/_elsa/extension-builder/workspaces/ws-managed/working-copies/select",
      expect.objectContaining({ allowProtectedBranchEdit: false })
    );
    expect(postJson).toHaveBeenCalledWith(
      "/_elsa/extension-builder/workspaces/ws-managed/projects",
      expect.objectContaining({ templateId: "elsa-activity", displayName: "Acme Orders", packageId: "Acme.Orders", packageVersion: "1.0.0" })
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

    await waitForText(container, "Activities/HelloActivity.cs");

    expect(container.textContent).toContain("Extension Builder");
    expect(container.textContent).toContain("Repositories");
    expect(container.textContent).toContain("Team Extensions");
    expect(container.textContent).toContain("alice");
    expect(container.textContent).toContain("not connected");
    expect(container.textContent).toContain("main");
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
        if (url.endsWith("Activities%2FHelloActivity.cs")) throw new Error("Request failed with 404.");
        if (url.includes("/projects/proj-1")) return { ...project(), builds: [succeededBuild()] };
        return {};
      }
    }));

    await waitForText(container, "Activities/HelloActivity.cs");
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

    expect(postJson).toHaveBeenCalledWith("/_elsa/extension-builder/repositories/server-local", {
      path: serverPath,
      displayName: "Server Extensions"
    });
    expect(container.querySelector(".modules-source-button.active")?.textContent).toContain("Server Extensions");
    expect(container.querySelector(".modules-source-button.active")?.textContent).toContain(`release · ${remoteState}`);
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

    expect(postJson).toHaveBeenCalledWith("/_elsa/extension-builder/repositories/clone", {
      repositoryUrl,
      displayName: "Cloned Extensions"
    });
    expect(container.querySelector(".modules-source-button.active")?.textContent).toContain("Cloned Extensions");
    expect(container.querySelector(".modules-source-button.active")?.textContent).toContain(`main · ${repositoryUrl}`);
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

    expect(postJson).toHaveBeenCalledWith("/_elsa/extension-builder/workspaces", { displayName: "Managed Repository" });
    expect(container.textContent).toContain("Managed Repository: 0 project(s), not connected on main.");
    const activeRepository = container.querySelector(".modules-source-button.active");
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
        expect(url).toBe("/_elsa/extension-builder/workspaces/ws-1/working-copies/select");
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

    const branchInput = await waitForElement<HTMLInputElement>(container, "[aria-label='Working branch']");
    await fill(branchInput, "feature/session-work");
    await clickButton(container, "Open working branch");
    await waitForText(container, "Opened working branch feature/session-work.");

    expect(postJson).toHaveBeenCalledWith("/_elsa/extension-builder/workspaces/ws-1/working-copies/select", expect.objectContaining({
      branchName: "feature/session-work",
      allowProtectedBranchEdit: false
    }));
    const activeRepository = container.querySelector(".modules-source-button.active");
    expect(activeRepository?.textContent).toContain("feature/session-work · not connected · dirty");

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
      if (url.endsWith("src%2FGenerated%2FGeneratedActivity.cs")) return {
        ...generatedFile,
        content: "namespace Company.Extensions.Generated;\n\npublic sealed class GeneratedActivity\n{\n}"
      };
      return defaultGetJson(url);
    });
    const { container, unmount } = await renderExtensionBuilderPage(stubApi({ getJson, postJson }));
    await waitForText(container, "Activities/HelloActivity.cs");

    await fill(await waitForElement<HTMLInputElement>(container, "[aria-label='Template target path']"), "src/Generated");
    await fill(await waitForElement<HTMLInputElement>(container, "[aria-label='Template parameter Class name']"), "GeneratedActivity");
    await fill(await waitForElement<HTMLInputElement>(container, "[aria-label='Template parameter Namespace']"), "Company.Extensions.Generated");
    await clickExactButton(container, "Apply");
    await waitForText(container, "Applied C# class.");

    expect(postJson).toHaveBeenCalledWith("/_elsa/extension-builder/workspaces/ws-1/templates/apply", expect.objectContaining({ templateId: "csharp-class" }));
    expect(container.textContent).toContain("src/Generated/GeneratedActivity.cs *");
    expect(container.querySelector<HTMLTextAreaElement>("[aria-label='Project file editor']")?.value).toContain("GeneratedActivity");
    await clickTab(container, "Source");
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
    await waitForText(container, "Activities/HelloActivity.cs");

    await clickTab(container, "Source");
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

    expect(postJson).toHaveBeenCalledWith("/_elsa/extension-builder/workspaces/ws-1/source-control/stage", { path: "src/Status.cs" });
    expect(postJson).toHaveBeenCalledWith("/_elsa/extension-builder/workspaces/ws-1/source-control/unstage", { path: "src/Status.cs" });
    expect(postJson).toHaveBeenCalledWith("/_elsa/extension-builder/workspaces/ws-1/source-control/stage-all", {});
    expect(postJson).toHaveBeenCalledWith("/_elsa/extension-builder/workspaces/ws-1/source-control/commit", { message: "Add source control file" });
    expect(postJson).toHaveBeenCalledWith("/_elsa/extension-builder/workspaces/ws-1/source-control/push", {});
    expect(postJson).toHaveBeenCalledWith("/_elsa/extension-builder/workspaces/ws-1/source-control/pull", {});
    expect(getJson).toHaveBeenCalledWith("/_elsa/extension-builder/workspaces/ws-1/source-control/diff/src%2FStatus.cs?staged=true");

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
    await waitForText(container, "Activities/HelloActivity.cs");

    const editor = await waitForElement<HTMLTextAreaElement>(container, "[aria-label='Project file editor']");
    await waitFor(() => editor.value.includes("HelloActivity"), "Expected editor content to load.");
    await fill(editor, `${editor.value}\n// updated`);
    await waitFor(() => editor.value.includes("// updated"), "Expected editor edit to apply.");

    await clickButton(container, "Save");
    await flushPromises();
    expect(fetchMock).toHaveBeenCalledWith(
      "https://foundation.example/_elsa/extension-builder/workspaces/ws-1/files/Activities%2FHelloActivity.cs",
      expect.objectContaining({ method: "PUT" })
    );

    await clickButton(container, "Build");
    await flushPromises();
    await flushPromises();
    expect(postJson).toHaveBeenCalledWith("/_elsa/extension-builder/workspaces/ws-1/builds", { projectId: "proj-1", command: "Build", targetPath: null });
    expect(container.textContent).toContain("Build submitted");
    expect(container.textContent).toContain("Succeeded");

    await clickTab(container, "Promote");
    await clickButton(container, "Promote build");
    await flushPromises();

    expect(postJson).toHaveBeenCalledWith("/_elsa/extension-builder/builds/build-1/promote", {});
    expect(container.textContent).toContain("is loaded at runtime");

    await unmount();
  });

  it("submits restore build and test jobs with explicit repository targets", async () => {
    const postJson = vi.fn(async (url: string) => {
      if (url.endsWith("/builds")) return succeededBuild({ sourceRevisionId: null });
      return defaultPostJson(url);
    });
    const { container, unmount } = await renderExtensionBuilderPage(stubApi({ postJson }));
    await waitForText(container, "Activities/HelloActivity.cs");

    await selectValue(await waitForElement<HTMLSelectElement>(container, "[aria-label='Build command']"), "Test");
    await fill(await waitForElement<HTMLInputElement>(container, "[aria-label='Build target path']"), "src/TeamExtensions.Tests/TeamExtensions.Tests.csproj");
    await clickButton(container, "Run command");
    await flushPromises();

    expect(postJson).toHaveBeenCalledWith("/_elsa/extension-builder/workspaces/ws-1/builds", {
      projectId: "proj-1",
      command: "Test",
      targetPath: "src/TeamExtensions.Tests/TeamExtensions.Tests.csproj"
    });

    await selectValue(await waitForElement<HTMLSelectElement>(container, "[aria-label='Build command']"), "Restore");
    await fill(await waitForElement<HTMLInputElement>(container, "[aria-label='Build target path']"), "");
    await clickButton(container, "Run command");
    await flushPromises();

    expect(postJson).toHaveBeenCalledWith("/_elsa/extension-builder/workspaces/ws-1/builds", {
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
    await waitForText(container, "Activities/HelloActivity.cs");

    await selectValue(await waitForElement<HTMLSelectElement>(container, "[aria-label='Build command']"), "Pack");
    await fill(await waitForElement<HTMLInputElement>(container, "[aria-label='Build target path']"), "Hello.slnx");
    await clickButton(container, "Run command");
    await flushPromises();

    expect(postJson).toHaveBeenCalledWith("/_elsa/extension-builder/workspaces/ws-1/builds", {
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

    expect(postJson).toHaveBeenCalledWith("/_elsa/extension-builder/builds/build-1/artifacts/artifact-a/promote", {});

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
        if (url.endsWith("Activities%2FHelloActivity.cs")) return repositoryFile();
        if (url.includes("/projects/proj-1")) return { ...project(), builds: [failedBuild([activeFileDiagnostic, otherFileDiagnostic])] };
        return {};
      }
    }));
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

  it("renders distinct promotion rejection guidance", async () => {
    for (const [category, expected] of [
      ["Duplicate", "never silently overwritten"],
      ["InvalidManifest", "manifest"],
      ["DependencyPolicy", "dependency"],
      ["MalformedPackage", "malformed"]
    ] as const) {
      const postJson = vi.fn(async (url: string) => url.endsWith("/promote") ? rejectedPromotion(category) : {});
      const { container, unmount } = await renderExtensionBuilderPage(stubApi({ postJson }));
      await waitForText(container, "Activities/HelloActivity.cs");

      await clickTab(container, "Promote");
      await clickButton(container, "Promote build");
      await waitForText(container, expected);

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
        if (url.endsWith("Activities%2FHelloActivity.cs")) return { path: "Activities/HelloActivity.cs", kind: 2, content: "public sealed class HelloActivity { }" };
        if (url.includes("/projects/proj-1")) return { ...project(), latestBuildStatus: 2, runtimeStatus: 0, builds: [succeededBuild({ status: 2 })] };
        return {};
      }
    }));
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
        if (url.endsWith("Activities%2FHelloActivity.cs")) return repositoryFile();
        if (url.includes("/projects/proj-1")) return { ...project(), builds: [succeededBuild()] };
        return {};
      }
    }));
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
        if (url.endsWith("Activities%2FHelloActivity.cs")) return repositoryFile();
        if (url.includes("/projects/proj-1")) return { ...project(), currentSourceRevisionId: "rev-2", builds: [succeededBuild({ sourceRevisionId: "rev-1" })] };
        return {};
      }
    }));
    await waitForText(container, "Activities/HelloActivity.cs");

    await clickTab(container, "Promote");
    const promoteButton = buttonContaining(container, "Promote build");

    expect(promoteButton?.disabled).toBe(true);
    expect(promoteButton?.title).toContain("stale");
    expect(postJson).not.toHaveBeenCalledWith("/_elsa/extension-builder/builds/build-1/promote", {});

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
        if (url.endsWith("Activities%2FHelloActivity.cs")) return repositoryFile();
        if (url.includes(`/projects/${selectedProject.id}`)) return { ...selectedProject, builds: [succeededBuild()] };
        return {};
      }
    }));
    await waitForText(container, "Activities/HelloActivity.cs");

    expect(container.textContent).toContain("Generic .NET class library");
    await clickTab(container, "Runtime");
    expect(container.textContent).toContain("FailedReconciliation");
    expect(container.textContent).toContain("contributed no runtime capabilities");

    await clickButton(container, "Retry reconciliation");
    await flushPromises();
    expect(postJson).toHaveBeenCalledWith(`/_elsa/extension-builder/projects/${selectedProject.id}/retry-reconcile`, {});
    await waitForText(container, "0.9.0");

    await clickButton(container, "Rollback");
    await flushPromises();
    expect(postJson).toHaveBeenCalledWith(`/_elsa/extension-builder/projects/${selectedProject.id}/rollback`, { version: "0.9.0" });

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
    },
    dialogs: {
      confirm: vi.fn(async () => true),
      prompt: vi.fn(async () => null),
      alert: vi.fn(async () => {})
    }
  } as ElsaStudioModuleApi;
}

async function defaultGetJson(url: string): Promise<unknown> {
  if (url.endsWith("/capabilities")) return trustedCapabilities();
  if (url.endsWith("/repositories")) return [repositorySummary()];
  if (url.endsWith("/workspaces")) return [workspaceWithProject()];
  if (url.endsWith("/templates")) return templates();
  if (url.endsWith("/repository-tree")) return repositoryTree();
  if (url.endsWith("/source-control/status")) return sourceControlStatus();
  if (url.endsWith("/files")) return projectFiles();
  if (url.endsWith("/runtime-status")) return loadedRuntime();
  if (url.includes("/builds/build-1")) return succeededBuild();
  if (url.endsWith("Activities%2FHelloActivity.cs")) return repositoryFile();
  if (url.includes("/projects/proj-1")) return { ...project(), builds: [succeededBuild()] };
  return {};
}

async function defaultPostJson(url: string): Promise<unknown> {
  if (url.endsWith("/builds")) return succeededBuild();
  if (url.endsWith("/promote")) return acceptedPromotion();
  return {};
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
