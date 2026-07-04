import type { ElsaStudioModuleApi } from "../../../sdk";
import type {
  BuildArtifact,
  BuildDiagnostic,
  BuildResult,
  ExtensionBuilderCapabilities,
  ExtensionProject,
  ExtensionRepositorySummary,
  ExtensionRuntimeStatus,
  ExtensionTemplate,
  ExtensionWorkspace,
  PackagePromotionResult,
  RepositoryBuildCommand,
  RepositoryFileSummary,
  RepositorySolutionSummary,
  SourceControlDiff,
  SourceControlStatus
} from "../extensionBuilderApi";
import type { OperationScope } from "./useOperationTracker";
import type { BuilderState, EditorTab, InspectorTab, ProjectDraft, TemplateApplicationDraft } from "./types";

// The value exposed to every panel via `useExtensionBuilder()`. It is the union of raw state the
// panels render, the derived selections/flags they gate on, and the action handlers they invoke.
// Panels read from here instead of receiving ~50 threaded props from a single giant component.
export interface ExtensionBuilderContextValue {
  api: ElsaStudioModuleApi;
  state: BuilderState;
  capabilities: ExtensionBuilderCapabilities | null;
  repositories: ExtensionRepositorySummary[];
  workspaces: ExtensionWorkspace[];
  templates: ExtensionTemplate[];
  projectTemplates: ExtensionTemplate[];
  solutions: RepositorySolutionSummary[];
  selectedSolutionPath: string;
  fileRows: RepositoryFileSummary[];
  editorTabs: EditorTab[];
  activeFilePath: string;
  editorText: string;
  editorDirty: boolean;
  lineHint: string | null;
  autoSave: boolean;
  autoSaving: boolean;
  advanced: boolean;
  activeBuild: BuildResult | null;
  buildHistory: BuildResult[];
  buildLog: string;
  buildCommand: RepositoryBuildCommand;
  buildTargetPath: string;
  runtimeStatus: ExtensionRuntimeStatus | null;
  promotionResult: PackagePromotionResult | null;
  sourceControlStatus: SourceControlStatus | null;
  sourceControlDiff: SourceControlDiff | null;
  commitMessage: string;
  activeInspectorTab: InspectorTab;
  dockOpen: boolean;
  extensionName: string;
  workspaceName: string;
  serverLocalPath: string;
  serverLocalName: string;
  cloneRepositoryUrl: string;
  cloneRepositoryName: string;
  projectDraft: ProjectDraft;
  templateDraft: TemplateApplicationDraft;
  workingBranchName: string;
  allowProtectedBranchEdit: boolean;
  newFilePath: string;
  status: string | null;
  error: string | null;

  // Derived selections + gates.
  selectedWorkspace: ExtensionWorkspace | null;
  selectedRepository: ExtensionRepositorySummary | null;
  selectedProject: ExtensionProject | null;
  selectedProjectId: string;
  latestArtifact: BuildArtifact | null;
  inWorkspace: boolean;
  canBuild: boolean;
  buildDisabledReason: string | null;
  canPromote: boolean;
  defaultWorkingBranchName: string;
  // Active build/pack progress feedback: true while a build is being submitted or is running.
  buildInProgress: boolean;
  // Per-operation busy flags so one action does not disable unrelated actions.
  isBusy(scope: OperationScope): boolean;

  // Setters exposed to the view for local input/toggle state.
  setAdvanced(value: boolean): void;
  setAutoSave(value: boolean): void;
  setActiveInspectorTab(value: InspectorTab): void;
  setDockOpen(updater: boolean | ((open: boolean) => boolean)): void;
  setExtensionName(value: string): void;
  setWorkspaceName(value: string): void;
  setServerLocalPath(value: string): void;
  setServerLocalName(value: string): void;
  setCloneRepositoryUrl(value: string): void;
  setCloneRepositoryName(value: string): void;
  setProjectDraft(value: ProjectDraft | ((current: ProjectDraft) => ProjectDraft)): void;
  setTemplateDraft(value: TemplateApplicationDraft): void;
  setWorkingBranchName(value: string): void;
  setAllowProtectedBranchEdit(value: boolean): void;
  setNewFilePath(value: string): void;
  setSelectedProjectId(value: string): void;
  setActiveBuild(value: BuildResult | null): void;
  setBuildCommand(value: RepositoryBuildCommand): void;
  setBuildTargetPath(value: string): void;
  setCommitMessage(value: string): void;

  // Actions.
  bootstrap(): void;
  openSolution(workspaceId: string): void;
  backToHome(): void;
  openFile(workspaceId: string, path: string): void;
  loadProjectDetails(workspaceId: string, projectId: string, solutionPath?: string | null): void;
  refreshRuntimeStatus(): void;
  refreshBuild(workspaceId: string, projectId: string, buildId: string, fallbackRevision?: string | null): void;
  ensureSavedBeforeLeaving(): Promise<boolean>;
  handleEditorTextChange(value: string): void;
  handleSelectEditorTab(path: string): void;
  handleCloseEditorTab(path: string): void;
  handleSaveFile(): void;
  handleCreateFile(): void;
  handleDeleteFile(path: string): void;
  handleRenameFile(path: string): void;
  handleSelectSolution(path: string): void;
  handleCreateWorkspace(): void;
  handleAttachServerLocalRepository(): void;
  handleCloneRepository(): void;
  handleCreateProject(): void;
  handleCreateExtension(): void;
  handleSelectWorkingCopy(): void;
  handleApplyTemplate(): void;
  handleSelectSourceDiff(path: string, staged: boolean): void;
  handleStageFile(path: string): void;
  handleUnstageFile(path: string): void;
  handleStageAll(): void;
  handleCommitChanges(): void;
  handlePushRepository(): void;
  handlePullRepository(): void;
  handleSubmitBuild(command?: RepositoryBuildCommand): void;
  handlePromote(): void;
  handlePromoteArtifact(artifact: BuildArtifact): void;
  handleRetryReconciliation(): void;
  handleRollback(version: string): void;
  handleDiagnosticSelect(diagnostic: BuildDiagnostic): void;
  handleDeleteProject(): void;
  handleDeleteWorkspace(): void;
}
