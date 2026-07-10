import {
  getBackendManagementCapabilities,
  getProject,
  getRepositoryTree,
  getRuntimeStatus,
  getSourceControlStatus,
  isTrusted,
  listRepositories,
  listTemplates,
  listWorkspaces,
  readManagementBridgeFailure,
  readRepositoryFile
} from "../extensionBuilderApi";
import { canApplyProjectState, describeExtensionBuilderError, isCurrentSelection, patchProject } from "./helpers";
import type { BuilderCore } from "./store";

// Data-loading side of the store: bootstrap, workspace refresh, repository tree / source status /
// project detail hydration and single-file reads. All functions guard against stale responses via
// the request-id and selection refs on `core`. Extracted verbatim from the monolithic page so
// behaviour is unchanged.
export interface BuilderData {
  bootstrap(): Promise<void>;
  refreshWorkspaces(options?: { preserveSelection?: boolean }): Promise<void>;
  refreshWorkspacesSafely(options?: { preserveSelection?: boolean }): Promise<boolean>;
  loadRepositoryTree(workspaceId: string, solutionPath?: string | null): Promise<Awaited<ReturnType<typeof getRepositoryTree>> | null | undefined>;
  loadSourceControlStatus(workspaceId: string): Promise<Awaited<ReturnType<typeof getSourceControlStatus>> | null>;
  loadProjectDetails(workspaceId: string, projectId: string, solutionPath?: string | null): Promise<void>;
  refreshProjectMetadata(workspaceId: string, projectId: string): Promise<Awaited<ReturnType<typeof getProject>> | null>;
  refreshRuntimeStatus(workspaceId?: string, projectId?: string): Promise<void>;
}

export function useBuilderData(core: BuilderCore): BuilderData {
  const { context, tracker } = core;
  const { setError } = tracker;

  async function bootstrap() {
    core.setState("loading");
    core.setManagementUnavailable(null);
    setError(null);
    try {
      // ADR 0037 / #256: every Extension Builder call is routed through the Studio management bridge on the Studio
      // origin (api.host), so the browser never carries the backend host management key. The capability read answers
      // with an explicit envelope; when backend management is not usable we render a dedicated unavailable surface and
      // gate actions instead of issuing doomed requests.
      const management = await getBackendManagementCapabilities(core.hostContext);
      if (management.status !== "available" || !management.capabilities) {
        core.setCapabilities(null);
        core.setManagementUnavailable({ kind: management.status, detail: management.detail });
        core.setState("unavailable");
        return;
      }

      const loadedCapabilities = management.capabilities;
      core.setCapabilities(loadedCapabilities);
      if (!isTrusted(loadedCapabilities)) {
        core.setState("ready");
        return;
      }

      const [repositoryList, workspaceList, templateList] = await Promise.all([
        listRepositories(context).catch(() => []),
        listWorkspaces(context),
        listTemplates(context)
      ]);
      core.setRepositories(repositoryList);
      core.setTemplates(templateList);
      core.setWorkspaces(workspaceList);
      core.setSelectedWorkspaceId(current => (current || workspaceList[0]?.id || repositoryList[0]?.id) ?? "");
      core.setState("ready");
    } catch (e) {
      // A bridge infrastructure failure (503/504 + management envelope) on the bootstrap reads is the same "backend
      // management is not usable" condition the capability probe reports as data, so it flips to the explicit
      // unavailable surface. Anything else keeps the generic failed state.
      const bridgeFailure = readManagementBridgeFailure(e);
      if (bridgeFailure) {
        core.setCapabilities(null);
        core.setManagementUnavailable(bridgeFailure);
        core.setState("unavailable");
        return;
      }
      setError(describeExtensionBuilderError(e));
      core.setState("failed");
    }
  }

  async function refreshWorkspaces(options?: { preserveSelection?: boolean }) {
    const [repositoryList, workspaceList] = await Promise.all([
      listRepositories(context).catch(() => []),
      listWorkspaces(context)
    ]);
    core.setRepositories(repositoryList);
    core.setWorkspaces(workspaceList);
    if (!options?.preserveSelection || !workspaceList.some(workspace => workspace.id === core.selectedWorkspaceId) && !repositoryList.some(repository => repository.id === core.selectedWorkspaceId)) {
      core.setSelectedWorkspaceId(workspaceList[0]?.id ?? repositoryList[0]?.id ?? "");
    }
  }

  async function refreshWorkspacesSafely(options?: { preserveSelection?: boolean }) {
    try {
      await refreshWorkspaces(options);
      return true;
    } catch (e) {
      setError(describeExtensionBuilderError(e));
      return false;
    }
  }

  async function loadRepositoryTree(workspaceId: string, solutionPath?: string | null) {
    try {
      const tree = await getRepositoryTree(context, workspaceId, solutionPath);
      if (!core.mounted.current || core.selectedIds.current.workspaceId !== workspaceId) return;
      core.setSolutions(tree.solutions);
      core.setFiles(tree.entries);
      const selected = tree.solutions.find(solution => solution.isSelected)?.path ?? "";
      const nextSelectedSolutionPath = tree.solutions.length > 1 ? selected : "";
      if (nextSelectedSolutionPath !== core.selectedSolutionPath) core.setSelectedSolutionPath(nextSelectedSolutionPath);
      return tree;
    } catch (e) {
      if (core.selectedIds.current.workspaceId === workspaceId) setError(describeExtensionBuilderError(e));
      return null;
    }
  }

  async function loadSourceControlStatus(workspaceId: string) {
    try {
      const gitStatus = await getSourceControlStatus(context, workspaceId);
      if (!core.mounted.current || core.selectedIds.current.workspaceId !== workspaceId) return null;
      core.setSourceControlStatus(gitStatus);
      if (core.sourceControlDiff && !gitStatus.changedFiles.some(file => file.path === core.sourceControlDiff!.path)) {
        core.setSourceControlDiff(null);
      }
      return gitStatus;
    } catch (e) {
      if (core.selectedIds.current.workspaceId === workspaceId) setError(describeExtensionBuilderError(e));
      return null;
    }
  }

  async function loadProjectDetails(workspaceId: string, projectId: string, solutionPath?: string | null) {
    const requestId = ++core.projectDetailsRequestId.current;
    const runtimeRequestId = ++core.runtimeStatusRequestId.current;
    setError(null);
    try {
      const [project, repositoryTree, gitStatus, runtime] = await Promise.all([
        getProject(context, workspaceId, projectId),
        getRepositoryTree(context, workspaceId, solutionPath),
        getSourceControlStatus(context, workspaceId),
        getRuntimeStatus(context, workspaceId, projectId).catch(() => null)
      ]);
      if (!canApplyProjectState(core.mounted.current, requestId, core.projectDetailsRequestId.current, core.selectedIds.current, workspaceId, projectId)) return;
      core.setWorkspaces(current => patchProject(current, workspaceId, project));
      core.setSolutions(repositoryTree.solutions);
      core.setFiles(repositoryTree.entries);
      core.setSourceControlStatus(gitStatus);
      const selected = repositoryTree.solutions.find(solution => solution.isSelected)?.path ?? "";
      const nextSelectedSolutionPath = repositoryTree.solutions.length > 1 ? selected : "";
      if (nextSelectedSolutionPath !== core.selectedSolutionPath) core.setSelectedSolutionPath(nextSelectedSolutionPath);
      if (runtimeRequestId === core.runtimeStatusRequestId.current) {
        core.setRuntimeStatus(runtime);
      }
      core.setBuildHistory(project.builds ?? []);
      core.setActiveBuild(project.builds?.[0] ?? null);
      const firstFile = repositoryTree.entries.find(file => file.type === "file");
      if (firstFile) {
        const file = await readRepositoryFile(context, workspaceId, firstFile.path).catch(() => firstFile.content != null ? firstFile : null);
        if (!canApplyProjectState(core.mounted.current, requestId, core.projectDetailsRequestId.current, core.selectedIds.current, workspaceId, projectId)) return;
        if (file) {
          const content = file.content ?? "";
          core.setActiveFilePath(file.path);
          core.setEditorText(content);
          core.setSavedEditorText(content);
          core.setEditorTabs([{ path: file.path, content, savedContent: content }]);
          core.setLineHint(null);
        } else {
          resetEditor(core);
        }
      } else {
        resetEditor(core);
      }
    } catch (e) {
      setError(describeExtensionBuilderError(e));
    }
  }

  async function refreshProjectMetadata(workspaceId: string, projectId: string) {
    try {
      const refreshedProject = await getProject(context, workspaceId, projectId);
      if (!isCurrentSelection(core.selectedIds.current, workspaceId, projectId)) return null;
      core.setWorkspaces(current => patchProject(current, workspaceId, refreshedProject));
      return refreshedProject;
    } catch (e) {
      setError(describeExtensionBuilderError(e));
      return null;
    }
  }

  async function refreshRuntimeStatus(workspaceId = core.selectedWorkspace?.id, projectId = core.selectedProject?.id) {
    if (!workspaceId || !projectId) return;
    const requestId = ++core.runtimeStatusRequestId.current;
    try {
      const runtime = await getRuntimeStatus(context, workspaceId, projectId);
      if (!core.mounted.current || requestId !== core.runtimeStatusRequestId.current || !isCurrentSelection(core.selectedIds.current, workspaceId, projectId)) return;
      core.setRuntimeStatus(runtime);
    } catch (e) {
      if (!core.mounted.current || requestId !== core.runtimeStatusRequestId.current || !isCurrentSelection(core.selectedIds.current, workspaceId, projectId)) return;
      setError(describeExtensionBuilderError(e));
    }
  }

  return {
    bootstrap,
    refreshWorkspaces,
    refreshWorkspacesSafely,
    loadRepositoryTree,
    loadSourceControlStatus,
    loadProjectDetails,
    refreshProjectMetadata,
    refreshRuntimeStatus
  };
}

function resetEditor(core: BuilderCore) {
  core.setActiveFilePath("");
  core.setEditorText("");
  core.setSavedEditorText("");
  core.setEditorTabs([]);
  core.setLineHint(null);
}
