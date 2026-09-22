import {
  commitRepositoryChanges,
  getSourceControlDiff,
  pullRepository,
  pushRepository,
  stageAllRepositoryChanges,
  stageRepositoryFile,
  unstageRepositoryFile
} from "../extensionBuilderApi";
import type { BuilderData } from "./useBuilderData";
import type { BuilderCore } from "./store";

// Source-control command slice of the store: diff selection, stage/unstage/stage-all, commit, push
// and pull. Each runs under the "source" busy scope so an in-flight git action does not disable
// unrelated actions. Extracted from the operations hook to keep files focused.
export interface SourceControlOperations {
  handleSelectSourceDiff(path: string, staged: boolean): Promise<void>;
  handleStageFile(path: string): Promise<void>;
  handleUnstageFile(path: string): Promise<void>;
  handleStageAll(): Promise<void>;
  handleCommitChanges(): Promise<void>;
  handlePushRepository(): Promise<void>;
  handlePullRepository(): Promise<void>;
}

export function useSourceControlOperations(core: BuilderCore, data: BuilderData): SourceControlOperations {
  const { context, tracker } = core;
  const { runOperation } = tracker;

  // Shared post-git-op reconciliation: apply the returned status, drop any stale diff and refresh
  // the workspace list (so dirty badges update). Tree reload is optional for commit/pull.
  async function applyStatus(workspaceId: string, status: Awaited<ReturnType<typeof stageRepositoryFile>>, reloadTree = false) {
    if (!status || core.selectedIds.current.workspaceId !== workspaceId) return;
    core.setSourceControlStatus(status);
    core.setSourceControlDiff(null);
    await data.refreshWorkspaces({ preserveSelection: true });
    if (reloadTree) await data.loadRepositoryTree(workspaceId, core.selectedSolutionPath || null);
  }

  async function handleSelectSourceDiff(path: string, staged: boolean) {
    if (!core.selectedWorkspace) return;
    const diff = await runOperation("source", () => getSourceControlDiff(context, core.selectedWorkspace!.id, path, staged), `Loaded diff for ${path}.`);
    if (diff && core.selectedIds.current.workspaceId === core.selectedWorkspace.id) {
      core.setSourceControlDiff(diff);
      core.setActiveInspectorTab("source");
      core.setDockOpen(true);
    }
  }

  async function handleStageFile(path: string) {
    if (!core.selectedWorkspace) return;
    const workspaceId = core.selectedWorkspace.id;
    const status = await runOperation("source", () => stageRepositoryFile(context, workspaceId, path), `Staged ${path}.`);
    await applyStatus(workspaceId, status);
  }

  async function handleUnstageFile(path: string) {
    if (!core.selectedWorkspace) return;
    const workspaceId = core.selectedWorkspace.id;
    const status = await runOperation("source", () => unstageRepositoryFile(context, workspaceId, path), `Unstaged ${path}.`);
    await applyStatus(workspaceId, status);
  }

  async function handleStageAll() {
    if (!core.selectedWorkspace) return;
    const workspaceId = core.selectedWorkspace.id;
    const status = await runOperation("source", () => stageAllRepositoryChanges(context, workspaceId), "Staged all changes.");
    await applyStatus(workspaceId, status);
  }

  async function handleCommitChanges() {
    if (!core.selectedWorkspace || !core.commitMessage.trim()) return;
    const workspaceId = core.selectedWorkspace.id;
    const result = await runOperation("source", () => commitRepositoryChanges(context, workspaceId, core.commitMessage.trim()), "Committed staged changes.");
    if (result && core.selectedIds.current.workspaceId === workspaceId) {
      core.setCommitMessage("");
      await applyStatus(workspaceId, result.status, true);
    }
  }

  async function handlePushRepository() {
    if (!core.selectedWorkspace) return;
    const workspaceId = core.selectedWorkspace.id;
    const result = await runOperation("source", () => pushRepository(context, workspaceId), "Pushed committed changes.");
    if (result) await applyStatus(workspaceId, result.status);
  }

  async function handlePullRepository() {
    if (!core.selectedWorkspace) return;
    const workspaceId = core.selectedWorkspace.id;
    const result = await runOperation("source", () => pullRepository(context, workspaceId), "Pulled remote changes.");
    if (result) await applyStatus(workspaceId, result.status, true);
  }

  return {
    handleSelectSourceDiff,
    handleStageFile,
    handleUnstageFile,
    handleStageAll,
    handleCommitChanges,
    handlePushRepository,
    handlePullRepository
  };
}
