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
import type { OperationTracker } from "./useOperationTracker";
import type { BuilderState, EditorTab, InspectorTab, ProjectDraft, TemplateApplicationDraft } from "./types";

type Setter<T> = Dispatch<SetStateAction<T>>;

// Shared, mutable core wired up by ExtensionBuilderProvider and threaded into the domain hooks
// (data loading, file editing, operations). It exposes the raw state setters, refs and derived
// selections so the split hooks operate on one source of truth without re-declaring state.
export interface BuilderCore {
  api: ElsaStudioModuleApi;
  context: ElsaStudioModuleApi["backend"];
  tracker: OperationTracker;

  // Session-scoped identifiers.
  sessionId: string;
  defaultWorkingBranchName: string;

  // Raw state + setters.
  setState: Setter<BuilderState>;
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

  // Derived selections (recomputed each render).
  selectedWorkspace: ExtensionWorkspace | null;
  selectedRepository: ExtensionRepositorySummary | null;
  selectedProject: ExtensionProject | null;
  editorDirty: boolean;

  clearBuildPoll(): void;
}
