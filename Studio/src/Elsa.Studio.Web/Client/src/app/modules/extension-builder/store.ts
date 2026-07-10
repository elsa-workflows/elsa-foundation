import type { Dispatch, MutableRefObject, SetStateAction } from "react";
import type { ElsaStudioModuleApi } from "../../../sdk";
import type {
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
import type { BackendManagementUnavailable } from "./contextValue";
import type { OperationTracker } from "./useOperationTracker";
import type { BuilderState, EditorTab, InspectorTab, ProjectDraft, TemplateApplicationDraft } from "./types";

type Setter<T> = Dispatch<SetStateAction<T>>;

// Shared, mutable core wired up by ExtensionBuilderProvider and threaded into the domain hooks
// (data loading, file editing, operations). It exposes the raw state setters, refs and derived
// selections so the split hooks operate on one source of truth without re-declaring state.
export interface BuilderCore {
  api: ElsaStudioModuleApi;
  // The Studio-origin context every Extension Builder call goes through: the browser talks only to the Studio
  // management bridge, never to the backend host-control surface directly. See ADR 0037.
  context: ElsaStudioModuleApi["host"];
  tracker: OperationTracker;

  // Session-scoped identifiers.
  sessionId: string;
  defaultWorkingBranchName: string;

  // Raw state + setters.
  setState: Setter<BuilderState>;
  setManagementUnavailable: Setter<BackendManagementUnavailable | null>;
  capabilities: ExtensionBuilderCapabilities | null;
  setCapabilities: Setter<ExtensionBuilderCapabilities | null>;
  repositories: ExtensionRepositorySummary[];
  setRepositories: Setter<ExtensionRepositorySummary[]>;
  workspaces: ExtensionWorkspace[];
  setWorkspaces: Setter<ExtensionWorkspace[]>;
  templates: ExtensionTemplate[];
  setTemplates: Setter<ExtensionTemplate[]>;
  projectTemplates: ExtensionTemplate[];
  selectedWorkspaceId: string;
  setSelectedWorkspaceId: Setter<string>;
  selectedProjectId: string;
  setSelectedProjectId: Setter<string>;
  setFiles: Setter<RepositoryFileSummary[]>;
  setSolutions: Setter<RepositorySolutionSummary[]>;
  selectedSolutionPath: string;
  setSelectedSolutionPath: Setter<string>;
  editorTabs: EditorTab[];
  setEditorTabs: Setter<EditorTab[]>;
  activeFilePath: string;
  setActiveFilePath: Setter<string>;
  editorText: string;
  setEditorText: Setter<string>;
  savedEditorText: string;
  setSavedEditorText: Setter<string>;
  setLineHint: Setter<string | null>;
  activeBuild: BuildResult | null;
  setActiveBuild: Setter<BuildResult | null>;
  setBuildHistory: Setter<BuildResult[]>;
  setBuildLog: Setter<string>;
  setRuntimeStatus: Setter<ExtensionRuntimeStatus | null>;
  setPromotionResult: Setter<PackagePromotionResult | null>;
  sourceControlDiff: SourceControlDiff | null;
  setSourceControlStatus: Setter<SourceControlStatus | null>;
  setSourceControlDiff: Setter<SourceControlDiff | null>;
  commitMessage: string;
  setCommitMessage: Setter<string>;
  buildCommand: RepositoryBuildCommand;
  buildTargetPath: string;
  setActiveInspectorTab: Setter<InspectorTab>;
  advanced: boolean;
  autoSave: boolean;
  setAutoSaving: Setter<boolean>;
  setEnteredWorkspaceId: Setter<string>;
  setDockOpen: Setter<boolean>;
  setExtensionName: Setter<string>;
  workspaceName: string;
  serverLocalPath: string;
  setServerLocalPath: Setter<string>;
  serverLocalName: string;
  setServerLocalName: Setter<string>;
  cloneRepositoryUrl: string;
  setCloneRepositoryUrl: Setter<string>;
  cloneRepositoryName: string;
  setCloneRepositoryName: Setter<string>;
  projectDraft: ProjectDraft;
  templateDraft: TemplateApplicationDraft;
  extensionName: string;
  workingBranchName: string;
  setWorkingBranchName: Setter<string>;
  allowProtectedBranchEdit: boolean;
  newFilePath: string;
  setNewFilePath: Setter<string>;

  // Refs used to guard async state application against races and unmount.
  pollTimerId: MutableRefObject<number | null>;
  mounted: MutableRefObject<boolean>;
  projectDetailsRequestId: MutableRefObject<number>;
  runtimeStatusRequestId: MutableRefObject<number>;
  selectedIds: MutableRefObject<{ workspaceId: string; projectId: string }>;
  hydratedSelectionKey: MutableRefObject<string>;
  activeFilePathRef: MutableRefObject<string>;
  saveChain: MutableRefObject<Promise<unknown>>;
  lastSavedContent: MutableRefObject<Map<string, string>>;
  // Consecutive bridge failures on the build poll (ADR 0037/#256). refreshBuild bumps it on a bridge-envelope error
  // and the poll effect backs off (2s, then 5s) instead of the 900ms cadence; the third consecutive failure stops the
  // poll with a tracker error, and any successful poll (or a fresh build submit) resets it.
  buildPollFailures: MutableRefObject<number>;

  // Derived selections (recomputed each render).
  selectedWorkspace: ExtensionWorkspace | null;
  selectedRepository: ExtensionRepositorySummary | null;
  selectedProject: ExtensionProject | null;
  editorDirty: boolean;

  clearBuildPoll(): void;
}
