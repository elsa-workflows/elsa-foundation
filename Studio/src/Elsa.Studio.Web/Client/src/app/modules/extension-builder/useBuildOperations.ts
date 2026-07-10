import {
  getBuild,
  getBuildLog,
  isManagementRelayTimeout,
  promoteBuild,
  promoteBuildArtifact,
  readManagementBridgeFailure,
  submitBuild,
  type BuildArtifact,
  type RepositoryBuildCommand
} from "../extensionBuilderApi";
import { describeExtensionBuilderError, isBuildForCurrentRevision, isCurrentSelection, mergeBuildHistory, patchProject } from "./helpers";
import type { BuilderData } from "./useBuilderData";
import type { BuilderCore } from "./store";

// Build-poll cadence: 900ms while polls succeed. After a consecutive bridge failure on the poll the retry backs off —
// 2s after the first failure, 5s after the second — before the third consecutive failure stops the poll entirely
// (see refreshBuild). Shared with the poll effect in ExtensionBuilderContext, which owns the timer.
export function buildPollDelayMs(consecutiveFailures: number) {
  if (consecutiveFailures >= 2) return 5000;
  if (consecutiveFailures === 1) return 2000;
  return 900;
}

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
      // A fresh submit starts a fresh poll: consecutive-failure state from a previous build must not shorten or stop
      // the new build's poll budget.
      core.buildPollFailures.current = 0;
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
      core.buildPollFailures.current = 0;
      const artifact = build.artifact ?? null;
      const nextBuild = { ...build, revision: build.revision ?? fallbackRevision ?? core.activeBuild?.revision ?? null, artifact };
      core.setActiveBuild(nextBuild);
      core.setBuildHistory(current => mergeBuildHistory(current, nextBuild));
      core.setBuildLog(log);
    } catch (e) {
      if (!core.mounted.current || !isCurrentSelection(core.selectedIds.current, workspaceId, projectId)) return;
      // A bridge infrastructure failure (503/504 envelope) is retried with backoff: bump the consecutive-failure count
      // and retrigger the poll effect by cloning the active build — the effect reads the count and reschedules at
      // 2s/5s instead of the 900ms cadence (buildPollDelayMs). The third consecutive bridge failure — or any domain
      // error the bridge relayed verbatim (e.g. a 404 build-not-found) — stops polling and surfaces a tracker error,
      // leaving the build rendered as running so the manual refresh affordance is the retry.
      if (readManagementBridgeFailure(e) && ++core.buildPollFailures.current < 3) {
        core.setActiveBuild(current => current ? { ...current } : current);
        return;
      }
      core.buildPollFailures.current = 0;
      setError(describeExtensionBuilderError(e));
    }
  }

  // A 504/unreachable answer on a promote means the Studio→backend relay timed out but the promotion may still have
  // completed backend-side (the bridge detail says so, and runOperation surfaces it as the tracker error when the
  // error is rethrown). Best-effort refresh the state the promotion would have changed — the build and the runtime
  // status — so a mutation that actually landed becomes visible.
  function refreshAfterPromoteRelayTimeout(error: unknown, workspaceId: string, projectId: string, buildId: string): never {
    if (isManagementRelayTimeout(error)) {
      void refreshBuild(workspaceId, projectId, buildId);
      void data.refreshRuntimeStatus(workspaceId, projectId);
    }
    throw error;
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
    const buildId = core.activeBuild.id;
    const result = await runOperation(
      "promote",
      () => promoteBuild(context, workspaceId, projectId, buildId, { buildId })
        .catch(error => refreshAfterPromoteRelayTimeout(error, workspaceId, projectId, buildId)),
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
    const buildId = core.activeBuild.id;
    const result = await runOperation(
      "promote",
      () => promoteBuildArtifact(context, workspaceId, projectId, buildId, artifact.id, { buildId })
        .catch(error => refreshAfterPromoteRelayTimeout(error, workspaceId, projectId, buildId)),
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
