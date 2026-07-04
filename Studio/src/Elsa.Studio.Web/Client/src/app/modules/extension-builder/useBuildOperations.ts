import {
  getBuild,
  getBuildLog,
  promoteBuild,
  promoteBuildArtifact,
  submitBuild,
  type BuildArtifact,
  type RepositoryBuildCommand
} from "../extensionBuilderApi";
import { getErrorMessage } from "../moduleManagementApi";
import { isBuildForCurrentRevision, isCurrentSelection, mergeBuildHistory, patchProject } from "./helpers";
import type { BuilderData } from "./useBuilderData";
import type { BuilderCore } from "./store";

// Build/pack + promotion command slice of the store: submit, poll-driven refresh, and promote of
// either the primary artifact or a chosen package artifact. Submit runs under the "build" scope and
// promotion under "promote" so neither disables unrelated actions. Extracted from the operations
// hook to keep files focused.
export interface BuildOperations {
  handleSubmitBuild(commandOverride?: RepositoryBuildCommand): Promise<void>;
  refreshBuild(workspaceId: string, projectId: string, buildId: string, fallbackRevision?: string | null): Promise<void>;
  handlePromote(): Promise<void>;
  handlePromoteArtifact(artifact: BuildArtifact): Promise<void>;
}

export function useBuildOperations(core: BuilderCore, data: BuilderData): BuildOperations {
  const { context, tracker } = core;
  const { runOperation, setError } = tracker;

  async function handleSubmitBuild(commandOverride?: RepositoryBuildCommand) {
    if (!core.selectedWorkspace || !core.selectedProject) return;
    if (core.editorDirty) {
      setError("Save or discard file changes before building.");
      return;
    }
    core.setActiveInspectorTab("build");
    core.setDockOpen(true);
    const requestedRevision = core.selectedProject.currentRevision ?? null;
    const command = commandOverride || core.buildCommand || "Build";
    const targetPath = core.buildTargetPath.trim() || core.selectedSolutionPath || null;
    const build = await runOperation(
      "build",
      () => submitBuild(context, core.selectedWorkspace!.id, core.selectedProject!.id, { projectId: core.selectedProject!.id, revision: requestedRevision, command, targetPath }),
      `${command} submitted for ${core.selectedProject.name}.`
    );
    if (build) {
      const revisionedBuild = { ...build, revision: build.revision ?? requestedRevision };
      if (revisionedBuild.revision) {
        core.setWorkspaces(current => patchProject(current, core.selectedWorkspace!.id, { ...core.selectedProject!, currentRevision: revisionedBuild.revision }));
      }
      core.setActiveBuild(revisionedBuild);
      core.setBuildHistory(current => mergeBuildHistory(current, revisionedBuild));
      core.setBuildLog("");
      await refreshBuild(core.selectedWorkspace.id, core.selectedProject.id, revisionedBuild.id, revisionedBuild.revision);
    }
  }

  async function refreshBuild(workspaceId: string, projectId: string, buildId: string, fallbackRevision?: string | null) {
    core.clearBuildPoll();
    try {
      const [build, log] = await Promise.all([
        getBuild(context, workspaceId, projectId, buildId),
        getBuildLog(context, workspaceId, projectId, buildId).catch(() => "")
      ]);
      if (!core.mounted.current || !isCurrentSelection(core.selectedIds.current, workspaceId, projectId)) return;
      const artifact = build.artifact ?? null;
      if (!core.mounted.current || !isCurrentSelection(core.selectedIds.current, workspaceId, projectId)) return;
      const nextBuild = { ...build, revision: build.revision ?? fallbackRevision ?? core.activeBuild?.revision ?? null, artifact };
      core.setActiveBuild(nextBuild);
      core.setBuildHistory(current => mergeBuildHistory(current, nextBuild));
      core.setBuildLog(log);
    } catch (e) {
      setError(getErrorMessage(e));
    }
  }

  async function handlePromote() {
    const latestArtifact = core.activeBuild?.artifact ?? null;
    if (!core.selectedWorkspace || !core.selectedProject || !core.activeBuild || !latestArtifact) return;
    if (!isBuildForCurrentRevision(core.activeBuild, core.selectedProject)) {
      setError("Build artifact is stale. Rebuild the current source revision before promoting.");
      return;
    }
    const workspaceId = core.selectedWorkspace.id;
    const projectId = core.selectedProject.id;
    const result = await runOperation(
      "promote",
      () => promoteBuild(context, workspaceId, projectId, core.activeBuild!.id, { buildId: core.activeBuild!.id }),
      `Promotion response received for ${latestArtifact.packageId} ${latestArtifact.version}.`
    );
    await applyPromotionResult(result, workspaceId, projectId);
  }

  async function handlePromoteArtifact(artifact: BuildArtifact) {
    if (!core.selectedWorkspace || !core.selectedProject || !core.activeBuild) return;
    if (artifact.sourceIsDirty) {
      setError("Package artifact was built from uncommitted source. Commit and pack again before promoting.");
      return;
    }
    const workspaceId = core.selectedWorkspace.id;
    const projectId = core.selectedProject.id;
    const result = await runOperation(
      "promote",
      () => promoteBuildArtifact(context, workspaceId, projectId, core.activeBuild!.id, artifact.id, { buildId: core.activeBuild!.id }),
      `Promotion response received for ${artifact.packageId} ${artifact.version}.`
    );
    await applyPromotionResult(result, workspaceId, projectId);
  }

  async function applyPromotionResult(result: Awaited<ReturnType<typeof promoteBuild>> | null, workspaceId: string, projectId: string) {
    if (result && isCurrentSelection(core.selectedIds.current, workspaceId, projectId)) {
      core.setPromotionResult(result);
      if (result.accepted) {
        await data.refreshRuntimeStatus(workspaceId, projectId);
        if (!isCurrentSelection(core.selectedIds.current, workspaceId, projectId)) return;
      }
      core.setActiveInspectorTab(result.accepted ? "runtime" : "promote");
      core.setDockOpen(true);
    }
  }

  return { handleSubmitBuild, refreshBuild, handlePromote, handlePromoteArtifact };
}
