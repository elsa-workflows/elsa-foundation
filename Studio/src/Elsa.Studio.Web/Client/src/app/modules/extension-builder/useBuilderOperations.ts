import {
  applyRepositoryTemplate,
  attachServerLocalRepository,
  cloneRepository,
  createProject,
  createWorkspace,
  deleteProject,
  deleteWorkspace,
  retryReconciliation,
  rollbackPackage,
  selectWorkingCopy,
  type BuildDiagnostic,
  type ExtensionProject
} from "../extensionBuilderApi";
import {
  collectTemplateParameters,
  derivePackageId,
  defaultPackageId,
  findPrimaryProjectTemplate,
  formatDiagnosticLocation,
  isCurrentSelection,
  validateProjectDraft
} from "./helpers";
import { useBuildOperations, type BuildOperations } from "./useBuildOperations";
import { useSourceControlOperations, type SourceControlOperations } from "./useSourceControlOperations";
import type { BuilderData } from "./useBuilderData";
import type { BuilderFileEditing } from "./useFileEditing";
import type { BuilderCore } from "./store";

// Command side of the store: repository/extension/project creation, working copy, template apply,
// runtime recovery and deletion. Build/pack + promotion and source-control live in their own slices
// ({@link useBuildOperations}, {@link useSourceControlOperations}) and are merged in here. Each
// action runs under a scoped busy flag via `tracker.runOperation` so unrelated actions stay enabled.
export interface BuilderOperations extends SourceControlOperations, BuildOperations {
  openSolution(workspaceId: string): Promise<void>;
  backToHome(): Promise<void>;
  handleSelectSolution(path: string): void;
  handleCreateWorkspace(): Promise<void>;
  handleAttachServerLocalRepository(): Promise<void>;
  handleCloneRepository(): Promise<void>;
  handleCreateProject(): Promise<void>;
  handleCreateExtension(): Promise<void>;
  handleSelectWorkingCopy(): Promise<void>;
  handleApplyTemplate(): Promise<void>;
  handleRetryReconciliation(): Promise<void>;
  handleRollback(version: string): Promise<void>;
  handleDiagnosticSelect(diagnostic: BuildDiagnostic): Promise<void>;
  handleDeleteProject(): Promise<void>;
  handleDeleteWorkspace(): Promise<void>;
}

export function useBuilderOperations(core: BuilderCore, data: BuilderData, files: BuilderFileEditing): BuilderOperations {
  const { context, tracker, sessionId } = core;
  const { runOperation, setError } = tracker;
  const source = useSourceControlOperations(core, data);
  const build = useBuildOperations(core, data);

  async function finishProjectCreation(project: ExtensionProject | null, workspaceId?: string) {
    if (!project) return;
    if (workspaceId) core.setSelectedWorkspaceId(workspaceId);
    if (await data.refreshWorkspacesSafely({ preserveSelection: true })) {
      core.setSelectedProjectId(project.id);
    }
  }

  async function openSolution(workspaceId: string) {
    if (!(await files.ensureSavedBeforeLeaving())) return;
    core.setSelectedWorkspaceId(workspaceId);
    core.setEnteredWorkspaceId(workspaceId);
    core.setDockOpen(false);
  }

  async function backToHome() {
    if (!(await files.ensureSavedBeforeLeaving())) return;
    core.setEnteredWorkspaceId("");
  }

  function handleSelectSolution(path: string) {
    core.setSelectedSolutionPath(path);
  }

  async function handleCreateWorkspace() {
    if (!core.workspaceName.trim()) {
      setError("Repository name is required.");
      return;
    }
    const workspace = await runOperation("home", () => createWorkspace(context, { name: core.workspaceName.trim() }), `Created managed repository ${core.workspaceName.trim()}.`);
    if (workspace) {
      if (await data.refreshWorkspacesSafely({ preserveSelection: true })) {
        core.setSelectedWorkspaceId(workspace.id);
      }
    }
  }

  async function handleAttachServerLocalRepository() {
    if (!core.serverLocalPath.trim()) {
      setError("Server-local repository path is required.");
      return;
    }
    const displayName = core.serverLocalName.trim();
    const workspace = await runOperation(
      "home",
      () => attachServerLocalRepository(context, { path: core.serverLocalPath.trim(), name: displayName || undefined }),
      `Attached server-local repository ${displayName || core.serverLocalPath.trim()}.`
    );
    if (workspace) {
      core.setServerLocalPath("");
      core.setServerLocalName("");
      if (await data.refreshWorkspacesSafely({ preserveSelection: true })) {
        core.setSelectedWorkspaceId(workspace.id);
      }
    }
  }

  async function handleCloneRepository() {
    if (!core.cloneRepositoryUrl.trim()) {
      setError("Repository URL is required.");
      return;
    }
    const displayName = core.cloneRepositoryName.trim();
    const workspace = await runOperation(
      "home",
      () => cloneRepository(context, { repositoryUrl: core.cloneRepositoryUrl.trim(), name: displayName || undefined }),
      `Cloned repository ${displayName || core.cloneRepositoryUrl.trim()}.`
    );
    if (workspace) {
      core.setCloneRepositoryUrl("");
      core.setCloneRepositoryName("");
      if (await data.refreshWorkspacesSafely({ preserveSelection: true })) {
        core.setSelectedWorkspaceId(workspace.id);
      }
    }
  }

  async function handleCreateProject() {
    if (!core.selectedWorkspace) return;
    const validation = validateProjectDraft(core.projectDraft);
    if (validation) {
      setError(validation);
      return;
    }
    const project = await runOperation(
      "explorer",
      () => createProject(context, core.selectedWorkspace!.id, {
        templateId: core.projectDraft.templateId,
        name: core.projectDraft.name.trim(),
        packageId: core.projectDraft.packageId.trim(),
        packageVersion: core.projectDraft.packageVersion.trim()
      }),
      `Created project ${core.projectDraft.name.trim()}.`
    );
    await finishProjectCreation(project);
  }

  async function handleCreateExtension() {
    const name = core.extensionName.trim();
    if (!name) {
      setError("Extension name is required.");
      return;
    }
    const template = findPrimaryProjectTemplate(core.projectTemplates);
    if (!template) {
      setError("No project template is available to create an extension.");
      return;
    }
    const packageId = derivePackageId(name) || defaultPackageId(template);
    const packageVersion = template.defaultPackageVersion || "1.0.0";
    const branchName = (core.workingBranchName || core.defaultWorkingBranchName).trim();

    let createdWorkspaceId = "";
    const project = await runOperation("home", async () => {
      const workspace = await createWorkspace(context, { name });
      createdWorkspaceId = workspace.id;
      await selectWorkingCopy(context, workspace.id, { sessionId, branchName, allowProtectedBranchEdit: false });
      return createProject(context, workspace.id, { templateId: template.id, name, packageId, packageVersion });
    }, `Created extension ${name}.`);

    if (project) {
      core.setExtensionName("");
      await finishProjectCreation(project, createdWorkspaceId || undefined);
      if (createdWorkspaceId) {
        core.setEnteredWorkspaceId(createdWorkspaceId);
        core.setDockOpen(false);
      }
    }
  }

  async function handleSelectWorkingCopy() {
    const workspaceId = core.selectedWorkspace?.id ?? core.selectedRepository?.id;
    const branchName = core.workingBranchName.trim();
    if (!workspaceId) return;
    if (!branchName) {
      setError("Working branch is required.");
      return;
    }
    const workingCopy = await runOperation(
      "explorer",
      () => selectWorkingCopy(context, workspaceId, { sessionId, branchName, allowProtectedBranchEdit: core.allowProtectedBranchEdit }),
      `Opened working branch ${branchName}.`
    );
    if (workingCopy) {
      core.setWorkingBranchName(workingCopy.branchName);
      // The workspace refresh, repository tree, and source-control status are independent reads for the
      // same working copy, so fetch them concurrently rather than serially.
      await Promise.all([
        data.refreshWorkspacesSafely({ preserveSelection: true }),
        data.loadRepositoryTree(workspaceId, core.selectedSolutionPath || null),
        data.loadSourceControlStatus(workspaceId)
      ]);
    }
  }

  async function handleApplyTemplate() {
    if (!core.selectedWorkspace) return;
    const template = core.templates.find(item => item.id === core.templateDraft.templateId);
    if (!template) {
      setError("Select a template to apply.");
      return;
    }
    const parameters = collectTemplateParameters(template, core.templateDraft);
    if (typeof parameters === "string") {
      setError(parameters);
      return;
    }
    const workspaceId = core.selectedWorkspace.id;
    const result = await runOperation(
      "explorer",
      () => applyRepositoryTemplate(context, workspaceId, {
        templateId: template.id,
        scope: template.scope,
        targetPath: core.templateDraft.targetPath.trim() || null,
        parameters
      }),
      `Applied ${template.name}.`
    );
    if (!result || core.selectedIds.current.workspaceId !== workspaceId) return;

    core.setSolutions(result.tree.solutions);
    core.setFiles(result.tree.entries);
    const selected = result.tree.solutions.find(solution => solution.isSelected)?.path ?? "";
    core.setSelectedSolutionPath(result.tree.solutions.length > 1 ? selected : "");
    files.clearSourceDependentState();
    await data.loadSourceControlStatus(workspaceId);
    const refreshedProject = core.selectedProject ? await data.refreshProjectMetadata(workspaceId, core.selectedProject.id) : null;
    if (refreshedProject) core.setBuildHistory(refreshedProject.builds ?? []);
    const firstGeneratedFile = result.files.find(file => file.type === "file") ?? result.files[0];
    if (firstGeneratedFile) await files.openFile(workspaceId, firstGeneratedFile.path);
  }

  // On a relay timeout a runtime mutation (retry-reconcile, rollback) may still have landed backend-side: refresh
  // the runtime status before the error surfaces.
  function refreshRuntimeAfterRelayTimeout(error: unknown, workspaceId: string, projectId: string): never {
    return rethrowAfterRelayTimeoutRefresh(error, () => data.refreshRuntimeStatus(workspaceId, projectId));
  }

  async function handleRetryReconciliation() {
    if (!core.selectedWorkspace || !core.selectedProject) return;
    const workspaceId = core.selectedWorkspace.id;
    const projectId = core.selectedProject.id;
    const requestId = ++core.runtimeStatusRequestId.current;
    const updatedRuntime = await runOperation(
      "runtime",
      () => retryReconciliation(context, workspaceId, projectId)
        .catch(error => refreshRuntimeAfterRelayTimeout(error, workspaceId, projectId)),
      `Retry reconciliation requested for ${core.selectedProject.name}.`
    );
    if (updatedRuntime && requestId === core.runtimeStatusRequestId.current && isCurrentSelection(core.selectedIds.current, workspaceId, projectId)) core.setRuntimeStatus(updatedRuntime);
  }

  async function handleRollback(version: string) {
    if (!core.selectedWorkspace || !core.selectedProject) return;
    if (!(await core.api.dialogs.confirm({ message: `Roll back ${core.selectedProject.packageId} to ${version}?`, confirmLabel: "Roll back", tone: "danger" }))) return;
    const workspaceId = core.selectedWorkspace.id;
    const projectId = core.selectedProject.id;
    const requestId = ++core.runtimeStatusRequestId.current;
    const updatedRuntime = await runOperation(
      "runtime",
      () => rollbackPackage(context, workspaceId, projectId, version)
        .catch(error => refreshRuntimeAfterRelayTimeout(error, workspaceId, projectId)),
      `Rolled back ${core.selectedProject.packageId} to ${version}.`
    );
    if (updatedRuntime && requestId === core.runtimeStatusRequestId.current && isCurrentSelection(core.selectedIds.current, workspaceId, projectId)) core.setRuntimeStatus(updatedRuntime);
  }

  async function handleDiagnosticSelect(diagnostic: BuildDiagnostic) {
    if (!core.selectedWorkspace || !core.selectedProject || !diagnostic.filePath) return;
    const opened = await files.openFile(core.selectedWorkspace.id, diagnostic.filePath);
    if (opened) {
      core.setLineHint(formatDiagnosticLocation(diagnostic));
    }
  }

  async function handleDeleteProject() {
    if (!core.selectedWorkspace || !core.selectedProject) return;
    if (!(await core.api.dialogs.confirm({ message: `Delete project ${core.selectedProject.name}?`, confirmLabel: "Delete", tone: "danger" }))) return;
    const deleted = await runOperation("explorer", () => deleteProject(context, core.selectedWorkspace!.id, core.selectedProject!.id), `Deleted project ${core.selectedProject.name}.`);
    if (deleted) await data.refreshWorkspacesSafely({ preserveSelection: true });
  }

  async function handleDeleteWorkspace() {
    if (!core.selectedWorkspace) return;
    if (!(await core.api.dialogs.confirm({ message: `Delete workspace ${core.selectedWorkspace.name}?`, confirmLabel: "Delete", tone: "danger" }))) return;
    const deleted = await runOperation("explorer", () => deleteWorkspace(context, core.selectedWorkspace!.id), `Deleted workspace ${core.selectedWorkspace.name}.`);
    if (deleted) {
      core.setEnteredWorkspaceId("");
      await data.refreshWorkspacesSafely();
    }
  }

  return {
    ...source,
    ...build,
    openSolution,
    backToHome,
    handleSelectSolution,
    handleCreateWorkspace,
    handleAttachServerLocalRepository,
    handleCloneRepository,
    handleCreateProject,
    handleCreateExtension,
    handleSelectWorkingCopy,
    handleApplyTemplate,
    handleRetryReconciliation,
    handleRollback,
    handleDiagnosticSelect,
    handleDeleteProject,
    handleDeleteWorkspace
  };
}
