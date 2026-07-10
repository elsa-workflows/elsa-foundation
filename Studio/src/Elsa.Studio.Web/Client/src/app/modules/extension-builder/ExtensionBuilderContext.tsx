import { createContext, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import type { ElsaStudioModuleApi } from "../../../sdk";
import { writeRepositoryFile } from "../extensionBuilderApi";
import type {
  BuildResult,
  ExtensionBuilderCapabilities,
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
import {
  createTemplateApplicationDraft,
  defaultPackageId,
  defaultProjectName,
  fileSortKey,
  findPrimaryProjectTemplate,
  getExtensionBuilderSessionId,
  isBuildForCurrentRevision,
  isBuildRunning,
  isProtectedBranchName,
  readAdvancedPreference,
  readAutoSavePreference,
  writeAdvancedPreference,
  writeAutoSavePreference
} from "./helpers";
import { useBuilderData } from "./useBuilderData";
import { buildPollDelayMs } from "./useBuildOperations";
import { useBuilderOperations } from "./useBuilderOperations";
import { useFileEditing } from "./useFileEditing";
import { useOperationTracker } from "./useOperationTracker";
import type { BackendManagementUnavailable, ExtensionBuilderContextValue } from "./contextValue";
import type { BuilderCore } from "./store";
import type { ProjectDraft, TemplateApplicationDraft } from "./types";

const ExtensionBuilderContext = createContext<ExtensionBuilderContextValue | null>(null);

export function useExtensionBuilder(): ExtensionBuilderContextValue {
  const value = useContext(ExtensionBuilderContext);
  if (!value) throw new Error("useExtensionBuilder must be used within an ExtensionBuilderProvider.");
  return value;
}

export function ExtensionBuilderProvider({ api, children }: { api: ElsaStudioModuleApi; children: ReactNode }) {
  // Every Extension Builder call is routed through the Studio management bridge on the Studio origin (ADR 0037, #256),
  // so the whole module runs on api.host and the browser never talks to the backend host-control surface directly.
  const context = api.host;
  const hostContext = api.host;
  const tracker = useOperationTracker();

  const [state, setState] = useState<BuilderState>("loading");
  const [managementUnavailable, setManagementUnavailable] = useState<BackendManagementUnavailable | null>(null);
  const [capabilities, setCapabilities] = useState<ExtensionBuilderCapabilities | null>(null);
  const [repositories, setRepositories] = useState<ExtensionRepositorySummary[]>([]);
  const [workspaces, setWorkspaces] = useState<ExtensionWorkspace[]>([]);
  const [templates, setTemplates] = useState<ExtensionTemplate[]>([]);
  const [selectedWorkspaceId, setSelectedWorkspaceId] = useState("");
  const [selectedProjectId, setSelectedProjectId] = useState("");
  const [files, setFiles] = useState<RepositoryFileSummary[]>([]);
  const [solutions, setSolutions] = useState<RepositorySolutionSummary[]>([]);
  const [selectedSolutionPath, setSelectedSolutionPath] = useState("");
  const [editorTabs, setEditorTabs] = useState<EditorTab[]>([]);
  const [activeFilePath, setActiveFilePath] = useState("");
  const [editorText, setEditorText] = useState("");
  const [savedEditorText, setSavedEditorText] = useState("");
  const [lineHint, setLineHint] = useState<string | null>(null);
  const [activeBuild, setActiveBuild] = useState<BuildResult | null>(null);
  const [buildHistory, setBuildHistory] = useState<BuildResult[]>([]);
  const [buildLog, setBuildLog] = useState("");
  const [runtimeStatus, setRuntimeStatus] = useState<ExtensionRuntimeStatus | null>(null);
  const [promotionResult, setPromotionResult] = useState<PackagePromotionResult | null>(null);
  const [sourceControlStatus, setSourceControlStatus] = useState<SourceControlStatus | null>(null);
  const [sourceControlDiff, setSourceControlDiff] = useState<SourceControlDiff | null>(null);
  const [commitMessage, setCommitMessage] = useState("");
  const [buildCommand, setBuildCommand] = useState<RepositoryBuildCommand>("Build");
  const [buildTargetPath, setBuildTargetPath] = useState("");
  const [activeInspectorTab, setActiveInspectorTab] = useState<InspectorTab>("build");
  const [advanced, setAdvanced] = useState(readAdvancedPreference);
  const [autoSave, setAutoSave] = useState(readAutoSavePreference);
  const [autoSaving, setAutoSaving] = useState(false);
  const [enteredWorkspaceId, setEnteredWorkspaceId] = useState("");
  const [dockOpen, setDockOpen] = useState(false);
  const [extensionName, setExtensionName] = useState("");
  const [workspaceName, setWorkspaceName] = useState("Extension workspace");
  const [serverLocalPath, setServerLocalPath] = useState("");
  const [serverLocalName, setServerLocalName] = useState("");
  const [cloneRepositoryUrl, setCloneRepositoryUrl] = useState("");
  const [cloneRepositoryName, setCloneRepositoryName] = useState("");
  const [projectDraft, setProjectDraft] = useState<ProjectDraft>({ name: "", packageId: "", packageVersion: "", templateId: "" });
  const [templateDraft, setTemplateDraft] = useState<TemplateApplicationDraft>({ templateId: "", targetPath: "", parameters: {} });
  const [workingBranchName, setWorkingBranchName] = useState("");
  const [allowProtectedBranchEdit, setAllowProtectedBranchEdit] = useState(false);
  const [newFilePath, setNewFilePath] = useState("");

  const pollTimerId = useRef<number | null>(null);
  const mounted = useRef(true);
  const sessionId = useRef(getExtensionBuilderSessionId()).current;
  const projectDetailsRequestId = useRef(0);
  const runtimeStatusRequestId = useRef(0);
  const selectedIds = useRef({ workspaceId: "", projectId: "" });
  const hydratedSelectionKey = useRef("");
  const activeFilePathRef = useRef("");
  const saveChain = useRef<Promise<unknown>>(Promise.resolve());
  const lastSavedContent = useRef<Map<string, string>>(new Map());
  const buildPollFailures = useRef(0);
  const latestEdit = useRef({ autoSave: true, dirty: false, workspaceId: "", path: "", content: "", canEdit: false });

  const selectedWorkspace = workspaces.find(workspace => workspace.id === selectedWorkspaceId) ?? (!selectedWorkspaceId ? workspaces[0] ?? null : null);
  const selectedRepository = repositories.find(repository => repository.id === selectedWorkspaceId) ?? repositories.find(repository => repository.id === selectedWorkspace?.id) ?? repositories[0] ?? null;
  const selectedProject = selectedWorkspace?.projects.find(project => project.id === selectedProjectId) ?? selectedWorkspace?.projects[0] ?? null;
  selectedIds.current = { workspaceId: selectedWorkspace?.id ?? "", projectId: selectedProject?.id ?? "" };
  activeFilePathRef.current = activeFilePath;
  const fileRows = useMemo(() => [...files].sort((a, b) => fileSortKey(a).localeCompare(fileSortKey(b))), [files]);
  const projectTemplates = useMemo(() => templates.filter(template => template.scope === "Project"), [templates]);
  const activeTab = editorTabs.find(tab => tab.path === activeFilePath) ?? null;
  const editorDirty = activeTab ? activeTab.content !== activeTab.savedContent : editorText !== savedEditorText;
  latestEdit.current = { autoSave, dirty: editorDirty, workspaceId: selectedWorkspace?.id ?? "", path: activeFilePath, content: editorText, canEdit: !!capabilities?.canEditFiles };
  const latestArtifact = activeBuild?.artifact ?? null;
  const canBuild = !!capabilities?.canBuild && !!selectedProject && !editorDirty && !isBuildRunning(activeBuild);
  const buildDisabledReason = !capabilities?.canBuild
    ? "Requires canBuild"
    : !selectedProject
      ? "Add a project to this solution to build a package"
      : editorDirty
        ? "Save or discard file changes first"
        : isBuildRunning(activeBuild)
          ? "A build is already running"
          : null;
  const canPromote = !!capabilities?.canPromote && !!latestArtifact && isBuildForCurrentRevision(activeBuild, selectedProject);
  const defaultWorkingBranchName = useMemo(() => `extension-builder/${sessionId}`, [sessionId]);
  const inWorkspace = !!enteredWorkspaceId && selectedWorkspace?.id === enteredWorkspaceId;
  // The build/pack action is "in progress" while its submit is in flight (scoped busy) OR while the
  // polled server-side build is still queued/running. The command-bar + panel buttons render an
  // active spinner state from this, mirroring the auto-save "Saving…" affordance.
  const buildInProgress = tracker.isBusy("build") || isBuildRunning(activeBuild);

  function clearBuildPoll() {
    if (pollTimerId.current) {
      window.clearTimeout(pollTimerId.current);
      pollTimerId.current = null;
    }
  }

  const core: BuilderCore = {
    api, context, hostContext, tracker, sessionId, defaultWorkingBranchName,
    setState, setManagementUnavailable, capabilities, setCapabilities, repositories, setRepositories, workspaces, setWorkspaces,
    templates, setTemplates, projectTemplates, selectedWorkspaceId, setSelectedWorkspaceId, selectedProjectId, setSelectedProjectId,
    setFiles, setSolutions, selectedSolutionPath, setSelectedSolutionPath, editorTabs, setEditorTabs,
    activeFilePath, setActiveFilePath, editorText, setEditorText, savedEditorText, setSavedEditorText, setLineHint,
    activeBuild, setActiveBuild, setBuildHistory, setBuildLog, setRuntimeStatus, setPromotionResult,
    sourceControlDiff, setSourceControlStatus, setSourceControlDiff, commitMessage, setCommitMessage,
    buildCommand, buildTargetPath, setActiveInspectorTab, advanced, autoSave, setAutoSaving,
    setEnteredWorkspaceId, setDockOpen, setExtensionName, workspaceName, serverLocalPath, setServerLocalPath,
    serverLocalName, setServerLocalName, cloneRepositoryUrl, setCloneRepositoryUrl, cloneRepositoryName, setCloneRepositoryName,
    projectDraft, templateDraft, extensionName, workingBranchName, setWorkingBranchName, allowProtectedBranchEdit,
    newFilePath, setNewFilePath,
    pollTimerId, mounted, projectDetailsRequestId, runtimeStatusRequestId, selectedIds, hydratedSelectionKey,
    activeFilePathRef, saveChain, lastSavedContent, buildPollFailures,
    selectedWorkspace, selectedRepository, selectedProject, editorDirty,
    clearBuildPoll
  };

  const data = useBuilderData(core);
  const fileEditing = useFileEditing(core, data);
  const operations = useBuilderOperations(core, data, fileEditing);

  useEffect(() => {
    mounted.current = true;
    void data.bootstrap();
    return () => {
      mounted.current = false;
      projectDetailsRequestId.current += 1;
      clearBuildPoll();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [api]);

  useEffect(() => {
    writeAdvancedPreference(advanced);
    if (!advanced) setActiveInspectorTab("build");
  }, [advanced]);

  useEffect(() => {
    writeAutoSavePreference(autoSave);
  }, [autoSave]);

  // Debounced auto-save of the active file. The timer is keyed on the edited content, so each
  // keystroke reschedules it; it fires ~1s after the user stops typing.
  //
  // saveBusy is a dependency, not just a guard: while an explicit Save is in flight we skip
  // scheduling (avoiding a double-save), but the effect must re-run when that Save completes so a
  // trailing dirty edit made during the save still gets auto-persisted without a further keystroke.
  // Omitting it (as the pre-decomposition code did after dropping operationBusy) silently dropped
  // that last edit. autoSaveFile itself no-ops when the content already matches, so re-running after
  // the Save settles cannot double-write the same content.
  const saveBusy = tracker.isBusy("save");
  useEffect(() => {
    if (!autoSave || !editorDirty || !activeFilePath || !selectedWorkspace || !capabilities?.canEditFiles || saveBusy) {
      return;
    }
    const workspaceId = selectedWorkspace.id;
    const path = activeFilePath;
    const content = editorText;
    const handle = window.setTimeout(() => { void fileEditing.autoSaveFile(workspaceId, path, content); }, 1000);
    return () => window.clearTimeout(handle);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [autoSave, editorText, editorDirty, activeFilePath, selectedWorkspace?.id, capabilities?.canEditFiles, saveBusy]);

  // Best-effort flush on unmount (e.g. navigating to another studio route).
  useEffect(() => () => {
    const edit = latestEdit.current;
    const key = `${edit.workspaceId}::${edit.path}`;
    if (edit.autoSave && edit.dirty && edit.canEdit && edit.workspaceId && edit.path && lastSavedContent.current.get(key) !== edit.content) {
      saveChain.current = saveChain.current
        .catch(() => {})
        .then(() => {
          if (lastSavedContent.current.get(key) === edit.content) return;
          lastSavedContent.current.set(key, edit.content);
          return writeRepositoryFile(context, edit.workspaceId, edit.path, { content: edit.content });
        })
        .catch(() => {});
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (projectTemplates.length === 0 || projectDraft.templateId) return;
    const primary = findPrimaryProjectTemplate(projectTemplates);
    if (!primary) return;
    setProjectDraft(current => ({
      ...current,
      templateId: primary.id,
      name: current.name || defaultProjectName(primary),
      packageId: current.packageId || defaultPackageId(primary),
      packageVersion: current.packageVersion || primary.defaultPackageVersion || "1.0.0"
    }));
  }, [projectTemplates, projectDraft.templateId]);

  useEffect(() => {
    if (templates.length === 0) return;
    const current = templates.find(template => template.id === templateDraft.templateId) ?? null;
    const draftValid = current && (advanced || current.scope === "Item");
    if (draftValid) return;
    const initialTemplate = templates.find(template => template.scope === "Item") ?? templates.find(template => template.scope !== "Project") ?? templates[0];
    setTemplateDraft(createTemplateApplicationDraft(initialTemplate));
  }, [templates, templateDraft.templateId, advanced]);

  useEffect(() => {
    if (!selectedWorkspace) return;
    const nextProjectId = selectedWorkspace.projects.some(project => project.id === selectedProjectId)
      ? selectedProjectId
      : selectedWorkspace.projects[0]?.id ?? "";
    if (nextProjectId !== selectedProjectId) {
      setSelectedProjectId(nextProjectId);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedWorkspace?.id, workspaces]);

  useEffect(() => {
    if (workingBranchName) return;
    setWorkingBranchName(selectedRepository?.activeBranch && !isProtectedBranchName(selectedRepository.activeBranch)
      ? selectedRepository.activeBranch
      : defaultWorkingBranchName);
  }, [selectedRepository?.id, selectedRepository?.activeBranch, defaultWorkingBranchName, workingBranchName]);

  useEffect(() => {
    if (!inWorkspace || !selectedWorkspace) return;
    const selectionKey = `${selectedWorkspace.id}::${selectedProject?.id ?? ""}::${selectedSolutionPath}`;
    if (hydratedSelectionKey.current === selectionKey) return;
    hydratedSelectionKey.current = selectionKey;

    if (!selectedProject) {
      projectDetailsRequestId.current += 1;
      setActiveBuild(null);
      setBuildHistory([]);
      setBuildLog("");
      runtimeStatusRequestId.current += 1;
      setRuntimeStatus(null);
      void data.loadRepositoryTree(selectedWorkspace.id, selectedSolutionPath || null);
      void data.loadSourceControlStatus(selectedWorkspace.id);
      return;
    }
    void data.loadProjectDetails(selectedWorkspace.id, selectedProject.id, selectedSolutionPath || null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [inWorkspace, selectedWorkspace?.id, selectedProject?.id, selectedSolutionPath]);

  // Build poll. The happy-path cadence is 900ms; refreshBuild owns the failure handling — after a transient bridge
  // failure it bumps buildPollFailures and retriggers this effect by cloning activeBuild, so the retry is rescheduled
  // here with backoff (2s, then 5s) instead of the 900ms cadence. The third consecutive bridge failure — or any domain
  // error — leaves activeBuild untouched and sets a tracker error, so this effect never reschedules and polling stops
  // (the manual refresh affordance is the retry).
  useEffect(() => {
    clearBuildPoll();
    if (!selectedWorkspace || !selectedProject || !activeBuild || !isBuildRunning(activeBuild)) return;
    pollTimerId.current = window.setTimeout(() => {
      void operations.refreshBuild(selectedWorkspace.id, selectedProject.id, activeBuild.id);
    }, buildPollDelayMs(buildPollFailures.current));
    return () => clearBuildPoll();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedWorkspace?.id, selectedProject?.id, activeBuild]);

  const value: ExtensionBuilderContextValue = {
    api, state, managementUnavailable, capabilities, repositories, workspaces, templates, projectTemplates, solutions, selectedSolutionPath,
    fileRows, editorTabs, activeFilePath, editorText, editorDirty, lineHint, autoSave, autoSaving, advanced,
    activeBuild, buildHistory, buildLog, buildCommand, buildTargetPath, runtimeStatus, promotionResult,
    sourceControlStatus, sourceControlDiff, commitMessage, activeInspectorTab, dockOpen, extensionName,
    workspaceName, serverLocalPath, serverLocalName, cloneRepositoryUrl, cloneRepositoryName, projectDraft,
    templateDraft, workingBranchName, allowProtectedBranchEdit, newFilePath, status: tracker.status, error: tracker.error,
    selectedWorkspace, selectedRepository, selectedProject, selectedProjectId, latestArtifact, inWorkspace,
    canBuild, buildDisabledReason, canPromote, defaultWorkingBranchName, buildInProgress,
    isBusy: tracker.isBusy,

    setAdvanced, setAutoSave, setActiveInspectorTab, setDockOpen, setExtensionName, setWorkspaceName,
    setServerLocalPath, setServerLocalName, setCloneRepositoryUrl, setCloneRepositoryName, setProjectDraft,
    setTemplateDraft, setWorkingBranchName, setAllowProtectedBranchEdit, setNewFilePath, setSelectedProjectId,
    setActiveBuild, setBuildCommand, setBuildTargetPath, setCommitMessage,

    bootstrap: () => void data.bootstrap(),
    openSolution: workspaceId => void operations.openSolution(workspaceId),
    backToHome: () => void operations.backToHome(),
    openFile: (workspaceId, path) => void fileEditing.openFile(workspaceId, path),
    loadProjectDetails: (workspaceId, projectId, solutionPath) => void data.loadProjectDetails(workspaceId, projectId, solutionPath),
    refreshRuntimeStatus: () => void data.refreshRuntimeStatus(),
    refreshBuild: (workspaceId, projectId, buildId, fallbackRevision) => void operations.refreshBuild(workspaceId, projectId, buildId, fallbackRevision),
    ensureSavedBeforeLeaving: fileEditing.ensureSavedBeforeLeaving,
    handleEditorTextChange: fileEditing.handleEditorTextChange,
    handleSelectEditorTab: path => void fileEditing.handleSelectEditorTab(path),
    handleCloseEditorTab: path => void fileEditing.handleCloseEditorTab(path),
    handleSaveFile: () => void fileEditing.handleSaveFile(),
    handleCreateFile: () => void fileEditing.handleCreateFile(),
    handleDeleteFile: path => void fileEditing.handleDeleteFile(path),
    handleRenameFile: path => void fileEditing.handleRenameFile(path),
    handleSelectSolution: operations.handleSelectSolution,
    handleCreateWorkspace: () => void operations.handleCreateWorkspace(),
    handleAttachServerLocalRepository: () => void operations.handleAttachServerLocalRepository(),
    handleCloneRepository: () => void operations.handleCloneRepository(),
    handleCreateProject: () => void operations.handleCreateProject(),
    handleCreateExtension: () => void operations.handleCreateExtension(),
    handleSelectWorkingCopy: () => void operations.handleSelectWorkingCopy(),
    handleApplyTemplate: () => void operations.handleApplyTemplate(),
    handleSelectSourceDiff: (path, staged) => void operations.handleSelectSourceDiff(path, staged),
    handleStageFile: path => void operations.handleStageFile(path),
    handleUnstageFile: path => void operations.handleUnstageFile(path),
    handleStageAll: () => void operations.handleStageAll(),
    handleCommitChanges: () => void operations.handleCommitChanges(),
    handlePushRepository: () => void operations.handlePushRepository(),
    handlePullRepository: () => void operations.handlePullRepository(),
    handleSubmitBuild: command => void operations.handleSubmitBuild(command),
    handlePromote: () => void operations.handlePromote(),
    handlePromoteArtifact: artifact => void operations.handlePromoteArtifact(artifact),
    handleRetryReconciliation: () => void operations.handleRetryReconciliation(),
    handleRollback: version => void operations.handleRollback(version),
    handleDiagnosticSelect: diagnostic => void operations.handleDiagnosticSelect(diagnostic),
    handleDeleteProject: () => void operations.handleDeleteProject(),
    handleDeleteWorkspace: () => void operations.handleDeleteWorkspace()
  };

  return <ExtensionBuilderContext.Provider value={value}>{children}</ExtensionBuilderContext.Provider>;
}
