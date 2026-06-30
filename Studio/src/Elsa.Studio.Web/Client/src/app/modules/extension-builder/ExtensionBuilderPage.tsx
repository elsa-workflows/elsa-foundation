import { useEffect, useMemo, useRef, useState } from "react";
import { ChevronLeft, Hammer, PackageCheck, Play, Save, Settings2 } from "lucide-react";
import type { ElsaStudioModuleApi } from "../../../sdk";
import { EmptyState, StatusChip, StudioAlert } from "../../ui";
import {
  applyRepositoryTemplate,
  attachServerLocalRepository,
  createProject,
  createWorkspace,
  cloneRepository,
  deleteProject,
  deleteRepositoryFile,
  deleteWorkspace,
  getBuild,
  getBuildLog,
  getCapabilities,
  getProject,
  getRepositoryTree,
  getRuntimeStatus,
  isTrusted,
  listRepositories,
  listTemplates,
  listWorkspaces,
  moveRepositoryFile,
  pullRepository,
  promoteBuildArtifact,
  promoteBuild,
  pushRepository,
  readRepositoryFile,
  retryReconciliation,
  rollbackPackage,
  selectWorkingCopy,
  submitBuild,
  writeRepositoryFile,
  type BuildArtifact,
  type BuildDiagnostic,
  type BuildResult,
  commitRepositoryChanges,
  type ExtensionBuilderCapabilities,
  type ExtensionProject,
  type ExtensionRepositorySummary,
  type ExtensionRuntimeStatus,
  type ExtensionTemplate,
  type ExtensionWorkspace,
  getSourceControlDiff,
  getSourceControlStatus,
  type PackagePromotionResult,
  type RepositoryFileSummary,
  type RepositorySolutionSummary,
  type RepositoryBuildCommand,
  type SourceControlDiff,
  type SourceControlStatus,
  stageAllRepositoryChanges,
  stageRepositoryFile,
  unstageRepositoryFile
} from "../extensionBuilderApi";
import { getErrorMessage } from "../moduleManagementApi";
import {
  canApplyProjectState,
  createTemplateApplicationDraft,
  collectTemplateParameters,
  defaultPackageId,
  defaultProjectName,
  derivePackageId,
  fileSortKey,
  formatDiagnosticLocation,
  getExtensionBuilderSessionId,
  isBuildForCurrentRevision,
  isBuildRunning,
  isCurrentSelection,
  isProtectedBranchName,
  mergeBuildHistory,
  patchProject,
  readAdvancedPreference,
  readAutoSavePreference,
  runtimeTone,
  upsertEditorTab,
  upsertFile,
  validateProjectDraft,
  writeAdvancedPreference,
  writeAutoSavePreference
} from "./helpers";
import { BottomDock } from "./BottomDock";
import { EditorSurface } from "./EditorSurface";
import { HomeView, type HomeStat } from "./HomeView";
import { SolutionExplorer } from "./SolutionExplorer";
import type { BuilderState, EditorTab, InspectorTab, ProjectDraft, TemplateApplicationDraft } from "./types";

export function ExtensionBuilderPage({ api }: { api: ElsaStudioModuleApi }) {
  const context = api.backend;
  const [state, setState] = useState<BuilderState>("loading");
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
  const [operationBusy, setOperationBusy] = useState(false);
  const [status, setStatus] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
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
  // Only enter the workspace view when a backing workspace record is actually resolved. A
  // repository summary can exist without a hydrated workspace; entering on the repository alone
  // would strand the user on a shell with no editable content.
  const inWorkspace = !!enteredWorkspaceId && selectedWorkspace?.id === enteredWorkspaceId;

  function clearBuildPoll() {
    if (pollTimerId.current) {
      window.clearTimeout(pollTimerId.current);
      pollTimerId.current = null;
    }
  }

  useEffect(() => {
    mounted.current = true;
    void bootstrap();

    return () => {
      mounted.current = false;
      projectDetailsRequestId.current += 1;
      clearBuildPoll();
    };
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
  useEffect(() => {
    if (!autoSave || !editorDirty || !activeFilePath || !selectedWorkspace || !capabilities?.canEditFiles || operationBusy) {
      return;
    }
    const workspaceId = selectedWorkspace.id;
    const path = activeFilePath;
    const content = editorText;
    const handle = window.setTimeout(() => { void autoSaveFile(workspaceId, path, content); }, 1000);
    return () => window.clearTimeout(handle);
  }, [autoSave, editorText, editorDirty, activeFilePath, selectedWorkspace?.id, capabilities?.canEditFiles, operationBusy]);

  // Best-effort flush on unmount (e.g. navigating to another studio route): the debounce timer is
  // cancelled by its own cleanup, so persist a still-dirty edit fire-and-forget if auto-save is on.
  useEffect(() => () => {
    const edit = latestEdit.current;
    const key = `${edit.workspaceId}::${edit.path}`;
    if (edit.autoSave && edit.dirty && edit.canEdit && edit.workspaceId && edit.path && lastSavedContent.current.get(key) !== edit.content) {
      // Chain onto the serialized save queue rather than writing directly, so this best-effort flush
      // cannot land out of order with an auto-save of older content that is still in flight. Re-check
      // the saved content at execution time to avoid a redundant write if the queue already caught up.
      saveChain.current = saveChain.current
        .catch(() => {})
        .then(() => {
          if (lastSavedContent.current.get(key) === edit.content) return;
          lastSavedContent.current.set(key, edit.content);
          return writeRepositoryFile(context, edit.workspaceId, edit.path, { content: edit.content });
        })
        .catch(() => {});
    }
  }, []);

  useEffect(() => {
    if (projectTemplates.length === 0 || projectDraft.templateId) return;
    const primary = projectTemplates.find(template => template.primary || /elsa/i.test(`${template.name} ${template.id}`)) ?? projectTemplates[0];
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
    // Simple mode only offers Item-scope (file) templates, so keep the draft on one to avoid
    // showing one template while applying another.
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
  }, [selectedWorkspace?.id, workspaces]);

  useEffect(() => {
    if (workingBranchName) return;
    setWorkingBranchName(selectedRepository?.activeBranch && !isProtectedBranchName(selectedRepository.activeBranch)
      ? selectedRepository.activeBranch
      : defaultWorkingBranchName);
  }, [selectedRepository?.id, selectedRepository?.activeBranch, defaultWorkingBranchName, workingBranchName]);

  useEffect(() => {
    // Only hydrate solution/project details once the user has opened a solution. On the home view
    // the editor, file tree, build and runtime state are not shown, so prefetching them eagerly
    // for the auto-selected workspace would waste several backend round trips per page load.
    if (!inWorkspace || !selectedWorkspace) {
      return;
    }

    // Hydrate once per selection. `inWorkspace` is a dependency so opening a solution triggers the
    // load, but re-entering a solution that is already loaded must not refetch and reset the editor.
    const selectionKey = `${selectedWorkspace.id}::${selectedProject?.id ?? ""}::${selectedSolutionPath}`;
    if (hydratedSelectionKey.current === selectionKey) {
      return;
    }
    hydratedSelectionKey.current = selectionKey;

    if (!selectedProject) {
      projectDetailsRequestId.current += 1;
      setActiveBuild(null);
      setBuildHistory([]);
      setBuildLog("");
      runtimeStatusRequestId.current += 1;
      setRuntimeStatus(null);
      void loadRepositoryTree(selectedWorkspace.id, selectedSolutionPath || null);
      void loadSourceControlStatus(selectedWorkspace.id);
      return;
    }

    void loadProjectDetails(selectedWorkspace.id, selectedProject.id, selectedSolutionPath || null);
  }, [inWorkspace, selectedWorkspace?.id, selectedProject?.id, selectedSolutionPath]);

  useEffect(() => {
    clearBuildPoll();

    if (!selectedWorkspace || !selectedProject || !activeBuild || !isBuildRunning(activeBuild)) {
      return;
    }

    pollTimerId.current = window.setTimeout(() => {
      void refreshBuild(selectedWorkspace.id, selectedProject.id, activeBuild.id);
    }, 900);

    return () => clearBuildPoll();
  }, [selectedWorkspace?.id, selectedProject?.id, activeBuild]);

  async function bootstrap() {
    setState("loading");
    setError(null);
    try {
      const loadedCapabilities = await getCapabilities(context);
      setCapabilities(loadedCapabilities);
      if (!isTrusted(loadedCapabilities)) {
        setState("ready");
        return;
      }

      const [repositoryList, workspaceList, templateList] = await Promise.all([
        listRepositories(context).catch(() => []),
        listWorkspaces(context),
        listTemplates(context)
      ]);
      setRepositories(repositoryList);
      setTemplates(templateList);
      setWorkspaces(workspaceList);
      setSelectedWorkspaceId(current => (current || workspaceList[0]?.id || repositoryList[0]?.id) ?? "");
      setState("ready");
    } catch (e) {
      setError(getErrorMessage(e));
      setState("failed");
    }
  }

  async function refreshWorkspaces(options?: { preserveSelection?: boolean }) {
    const [repositoryList, workspaceList] = await Promise.all([
      listRepositories(context).catch(() => []),
      listWorkspaces(context)
    ]);
    setRepositories(repositoryList);
    setWorkspaces(workspaceList);
    if (!options?.preserveSelection || !workspaceList.some(workspace => workspace.id === selectedWorkspaceId) && !repositoryList.some(repository => repository.id === selectedWorkspaceId)) {
      setSelectedWorkspaceId(workspaceList[0]?.id ?? repositoryList[0]?.id ?? "");
    }
  }

  async function refreshWorkspacesSafely(options?: { preserveSelection?: boolean }) {
    try {
      await refreshWorkspaces(options);
      return true;
    } catch (e) {
      setError(getErrorMessage(e));
      return false;
    }
  }

  async function loadRepositoryTree(workspaceId: string, solutionPath?: string | null) {
    try {
      const tree = await getRepositoryTree(context, workspaceId, solutionPath);
      if (!mounted.current || selectedIds.current.workspaceId !== workspaceId) return;
      setSolutions(tree.solutions);
      setFiles(tree.entries);
      const selected = tree.solutions.find(solution => solution.isSelected)?.path ?? "";
      const nextSelectedSolutionPath = tree.solutions.length > 1 ? selected : "";
      if (nextSelectedSolutionPath !== selectedSolutionPath) setSelectedSolutionPath(nextSelectedSolutionPath);
      return tree;
    } catch (e) {
      if (selectedIds.current.workspaceId === workspaceId) setError(getErrorMessage(e));
      return null;
    }
  }

  async function loadSourceControlStatus(workspaceId: string) {
    try {
      const gitStatus = await getSourceControlStatus(context, workspaceId);
      if (!mounted.current || selectedIds.current.workspaceId !== workspaceId) return null;
      setSourceControlStatus(gitStatus);
      if (sourceControlDiff && !gitStatus.changedFiles.some(file => file.path === sourceControlDiff.path)) {
        setSourceControlDiff(null);
      }
      return gitStatus;
    } catch (e) {
      if (selectedIds.current.workspaceId === workspaceId) setError(getErrorMessage(e));
      return null;
    }
  }

  async function loadProjectDetails(workspaceId: string, projectId: string, solutionPath?: string | null) {
    const requestId = ++projectDetailsRequestId.current;
    const runtimeRequestId = ++runtimeStatusRequestId.current;
    setError(null);
    try {
      const [project, repositoryTree, gitStatus, runtime] = await Promise.all([
        getProject(context, workspaceId, projectId),
        getRepositoryTree(context, workspaceId, solutionPath),
        getSourceControlStatus(context, workspaceId),
        getRuntimeStatus(context, workspaceId, projectId).catch(() => null)
      ]);
      if (!canApplyProjectState(mounted.current, requestId, projectDetailsRequestId.current, selectedIds.current, workspaceId, projectId)) return;
      setWorkspaces(current => patchProject(current, workspaceId, project));
      setSolutions(repositoryTree.solutions);
      setFiles(repositoryTree.entries);
      setSourceControlStatus(gitStatus);
      const selected = repositoryTree.solutions.find(solution => solution.isSelected)?.path ?? "";
      const nextSelectedSolutionPath = repositoryTree.solutions.length > 1 ? selected : "";
      if (nextSelectedSolutionPath !== selectedSolutionPath) setSelectedSolutionPath(nextSelectedSolutionPath);
      if (runtimeRequestId === runtimeStatusRequestId.current) {
        setRuntimeStatus(runtime);
      }
      setBuildHistory(project.builds ?? []);
      setActiveBuild(project.builds?.[0] ?? null);
      const firstFile = repositoryTree.entries.find(file => file.type === "file");
      if (firstFile) {
        const file = await readRepositoryFile(context, workspaceId, firstFile.path).catch(() => firstFile.content != null ? firstFile : null);
        if (!canApplyProjectState(mounted.current, requestId, projectDetailsRequestId.current, selectedIds.current, workspaceId, projectId)) return;
        if (file) {
          const content = file.content ?? "";
          setActiveFilePath(file.path);
          setEditorText(content);
          setSavedEditorText(content);
          setEditorTabs([{ path: file.path, content, savedContent: content }]);
          setLineHint(null);
        } else {
          setActiveFilePath("");
          setEditorText("");
          setSavedEditorText("");
          setEditorTabs([]);
          setLineHint(null);
        }
      } else {
        setActiveFilePath("");
        setEditorText("");
        setSavedEditorText("");
        setEditorTabs([]);
        setLineHint(null);
      }
    } catch (e) {
      setError(getErrorMessage(e));
    }
  }

  const confirmDiscard = () =>
    api.dialogs.confirm({ message: "Discard unsaved file changes?", confirmLabel: "Discard", tone: "danger" });

  async function openSolution(workspaceId: string) {
    if (!(await ensureSavedBeforeLeaving())) return;
    setSelectedWorkspaceId(workspaceId);
    setEnteredWorkspaceId(workspaceId);
    setDockOpen(false);
  }

  async function backToHome() {
    if (!(await ensureSavedBeforeLeaving())) return;
    setEnteredWorkspaceId("");
  }

  async function openFile(workspaceId: string, path: string, options?: { force?: boolean }) {
    if (!options?.force && !(await ensureSavedBeforeLeaving())) return false;
    setError(null);
    try {
      const file = await readRepositoryFile(context, workspaceId, path);
      if (selectedIds.current.workspaceId !== workspaceId) return false;
      const content = file.content ?? "";
      setActiveFilePath(file.path);
      setEditorText(content);
      setSavedEditorText(content);
      setEditorTabs(current => upsertEditorTab(current, { path: file.path, content, savedContent: content }));
      setLineHint(null);
      return true;
    } catch (e) {
      setError(getErrorMessage(e));
      return false;
    }
  }

  async function refreshProjectMetadata(workspaceId: string, projectId: string) {
    try {
      const refreshedProject = await getProject(context, workspaceId, projectId);
      if (!isCurrentSelection(selectedIds.current, workspaceId, projectId)) return null;
      setWorkspaces(current => patchProject(current, workspaceId, refreshedProject));
      return refreshedProject;
    } catch (e) {
      setError(getErrorMessage(e));
      return null;
    }
  }

  function clearSourceDependentState() {
    setActiveBuild(null);
    setBuildLog("");
    setPromotionResult(null);
  }

  function handleEditorTextChange(value: string) {
    setEditorText(value);
    if (activeFilePath) {
      setEditorTabs(current => current.map(tab => tab.path === activeFilePath ? { ...tab, content: value } : tab));
    }
  }

  // Serialize all file writes (manual Save and auto-save) so they cannot land out of order.
  function enqueueSave<T>(operation: () => Promise<T>): Promise<T> {
    const run = saveChain.current.catch(() => {}).then(operation);
    saveChain.current = run.catch(() => {});
    return run;
  }

  // Shared persistence: write the file and reconcile local editor/file state. Used by both the
  // explicit Save and auto-save so the two cannot drift. Returns the saved file.
  async function persistFile(workspaceId: string, path: string, content: string) {
    const saved = await writeRepositoryFile(context, workspaceId, path, { content });
    lastSavedContent.current.set(`${workspaceId}::${path}`, content);
    if (!mounted.current || selectedIds.current.workspaceId !== workspaceId) return saved;
    setFiles(current => upsertFile(current, saved));
    setEditorTabs(current => upsertEditorTab(current, { path: saved.path, content, savedContent: content }));
    if (activeFilePathRef.current === path) setSavedEditorText(content);
    return saved;
  }

  // Lightweight save used by auto-save: persists without the full tree/source/metadata refresh that
  // the explicit Save performs. Writes are serialized so overlapping saves cannot land out of order,
  // and identical content is not re-sent. Returns true on success (false lets callers block).
  async function autoSaveFile(workspaceId: string, path: string, content: string): Promise<boolean> {
    return enqueueSave(() => doAutoSave(workspaceId, path, content));
  }

  async function doAutoSave(workspaceId: string, path: string, content: string): Promise<boolean> {
    if (lastSavedContent.current.get(`${workspaceId}::${path}`) === content) return true;
    const showSaving = activeFilePathRef.current === path;
    if (showSaving) setAutoSaving(true);
    try {
      await persistFile(workspaceId, path, content);
      // Editing invalidates a prior build, but only for the file the user is actually looking at.
      if (mounted.current && activeFilePathRef.current === path && selectedIds.current.workspaceId === workspaceId) {
        clearSourceDependentState();
      }
      return true;
    } catch (e) {
      if (mounted.current) setError(getErrorMessage(e));
      return false;
    } finally {
      if (mounted.current && showSaving) setAutoSaving(false);
    }
  }

  // Navigation guard: when auto-save is on, flush the active file instead of prompting to discard.
  // If the flush fails, return false so the caller blocks navigation rather than losing the edit.
  async function ensureSavedBeforeLeaving() {
    if (!editorDirty) return true;
    if (autoSave && selectedWorkspace && activeFilePath && capabilities?.canEditFiles) {
      return autoSaveFile(selectedWorkspace.id, activeFilePath, editorText);
    }
    return confirmDiscard();
  }

  async function handleSelectEditorTab(path: string) {
    const tab = editorTabs.find(item => item.path === path);
    if (!tab) return;
    if (activeFilePath !== path && !(await ensureSavedBeforeLeaving())) return;
    setActiveFilePath(tab.path);
    setEditorText(tab.content);
    setSavedEditorText(tab.savedContent);
    setLineHint(null);
  }

  async function handleCloseEditorTab(path: string) {
    const tab = editorTabs.find(item => item.path === path);
    if (!tab) return;
    if (tab.content !== tab.savedContent) {
      if (autoSave && selectedWorkspace && capabilities?.canEditFiles) {
        if (!(await autoSaveFile(selectedWorkspace.id, tab.path, tab.content))) return;
      } else if (!(await confirmDiscard())) {
        return;
      }
    }
    const remaining = editorTabs.filter(item => item.path !== path);
    setEditorTabs(remaining);
    if (activeFilePath === path) {
      const next = remaining[0] ?? null;
      setActiveFilePath(next?.path ?? "");
      setEditorText(next?.content ?? "");
      setSavedEditorText(next?.savedContent ?? "");
      setLineHint(null);
    }
  }

  function handleSelectSolution(path: string) {
    setSelectedSolutionPath(path);
  }

  async function runOperation<T>(operation: () => Promise<T>, success: string) {
    setOperationBusy(true);
    setError(null);
    setStatus(null);
    try {
      const result = await operation();
      setStatus(success);
      return result;
    } catch (e) {
      setError(getErrorMessage(e));
      return null;
    } finally {
      setOperationBusy(false);
    }
  }

  async function handleCreateWorkspace() {
    if (!workspaceName.trim()) {
      setError("Repository name is required.");
      return;
    }

    const workspace = await runOperation(() => createWorkspace(context, { name: workspaceName.trim() }), `Created managed repository ${workspaceName.trim()}.`);
    if (workspace) {
      if (await refreshWorkspacesSafely({ preserveSelection: true })) {
        setSelectedWorkspaceId(workspace.id);
      }
    }
  }

  async function handleAttachServerLocalRepository() {
    if (!serverLocalPath.trim()) {
      setError("Server-local repository path is required.");
      return;
    }

    const displayName = serverLocalName.trim();
    const workspace = await runOperation(
      () => attachServerLocalRepository(context, { path: serverLocalPath.trim(), name: displayName || undefined }),
      `Attached server-local repository ${displayName || serverLocalPath.trim()}.`
    );
    if (workspace) {
      setServerLocalPath("");
      setServerLocalName("");
      if (await refreshWorkspacesSafely({ preserveSelection: true })) {
        setSelectedWorkspaceId(workspace.id);
      }
    }
  }

  async function handleCloneRepository() {
    if (!cloneRepositoryUrl.trim()) {
      setError("Repository URL is required.");
      return;
    }

    const displayName = cloneRepositoryName.trim();
    const workspace = await runOperation(
      () => cloneRepository(context, { repositoryUrl: cloneRepositoryUrl.trim(), name: displayName || undefined }),
      `Cloned repository ${displayName || cloneRepositoryUrl.trim()}.`
    );
    if (workspace) {
      setCloneRepositoryUrl("");
      setCloneRepositoryName("");
      if (await refreshWorkspacesSafely({ preserveSelection: true })) {
        setSelectedWorkspaceId(workspace.id);
      }
    }
  }

  async function handleCreateProject() {
    if (!selectedWorkspace) return;
    const validation = validateProjectDraft(projectDraft);
    if (validation) {
      setError(validation);
      return;
    }

    const project = await runOperation(
      () => createProject(context, selectedWorkspace.id, {
        templateId: projectDraft.templateId,
        name: projectDraft.name.trim(),
        packageId: projectDraft.packageId.trim(),
        packageVersion: projectDraft.packageVersion.trim()
      }),
      `Created project ${projectDraft.name.trim()}.`
    );
    await finishProjectCreation(project);
  }

  async function finishProjectCreation(project: ExtensionProject | null, workspaceId?: string) {
    if (!project) return;
    if (workspaceId) setSelectedWorkspaceId(workspaceId);
    if (await refreshWorkspacesSafely({ preserveSelection: true })) {
      setSelectedProjectId(project.id);
    }
  }

  async function handleCreateExtension() {
    const name = extensionName.trim();
    if (!name) {
      setError("Extension name is required.");
      return;
    }
    const template = projectTemplates.find(item => item.primary || /elsa/i.test(`${item.name} ${item.id}`)) ?? projectTemplates[0];
    if (!template) {
      setError("No project template is available to create an extension.");
      return;
    }
    const packageId = derivePackageId(name) || defaultPackageId(template);
    const packageVersion = template.defaultPackageVersion || "1.0.0";
    const branchName = (workingBranchName || defaultWorkingBranchName).trim();

    let createdWorkspaceId = "";
    const project = await runOperation(async () => {
      const workspace = await createWorkspace(context, { name });
      createdWorkspaceId = workspace.id;
      await selectWorkingCopy(context, workspace.id, { sessionId, branchName, allowProtectedBranchEdit: false });
      return createProject(context, workspace.id, { templateId: template.id, name, packageId, packageVersion });
    }, `Created extension ${name}.`);

    if (project) {
      setExtensionName("");
      await finishProjectCreation(project, createdWorkspaceId || undefined);
      if (createdWorkspaceId) {
        setEnteredWorkspaceId(createdWorkspaceId);
        setDockOpen(false);
      }
    }
  }

  async function handleSelectWorkingCopy() {
    const workspaceId = selectedWorkspace?.id ?? selectedRepository?.id;
    const branchName = workingBranchName.trim();
    if (!workspaceId) return;
    if (!branchName) {
      setError("Working branch is required.");
      return;
    }

    const workingCopy = await runOperation(
      () => selectWorkingCopy(context, workspaceId, { sessionId, branchName, allowProtectedBranchEdit }),
      `Opened working branch ${branchName}.`
    );
    if (workingCopy) {
      setWorkingBranchName(workingCopy.branchName);
      await refreshWorkspacesSafely({ preserveSelection: true });
      await loadRepositoryTree(workspaceId, selectedSolutionPath || null);
      await loadSourceControlStatus(workspaceId);
    }
  }

  async function handleSaveFile() {
    if (!selectedWorkspace || !activeFilePath) return;
    const workspaceId = selectedWorkspace.id;
    const path = activeFilePath;
    const content = editorText;
    const saved = await runOperation(
      () => enqueueSave(() => persistFile(workspaceId, path, content)),
      `Saved ${path}.`
    );
    if (saved && selectedIds.current.workspaceId === workspaceId) {
      clearSourceDependentState();
      await loadRepositoryTree(workspaceId, selectedSolutionPath || null);
      await loadSourceControlStatus(workspaceId);
      const refreshedProject = selectedProject ? await refreshProjectMetadata(workspaceId, selectedProject.id) : null;
      if (refreshedProject) setBuildHistory(refreshedProject.builds ?? []);
    }
  }

  async function handleCreateFile() {
    if (!selectedWorkspace || !newFilePath.trim()) return;
    const workspaceId = selectedWorkspace.id;
    const path = newFilePath.trim();
    const saved = await runOperation(
      () => writeRepositoryFile(context, workspaceId, path, { content: "" }),
      `Created ${path}.`
    );
    if (saved && selectedIds.current.workspaceId === workspaceId) {
      setNewFilePath("");
      setFiles(current => upsertFile(current, saved));
      setEditorTabs(current => upsertEditorTab(current, { path: saved.path, content: "", savedContent: "" }));
      clearSourceDependentState();
      await loadRepositoryTree(workspaceId, selectedSolutionPath || null);
      await loadSourceControlStatus(workspaceId);
      const refreshedProject = selectedProject ? await refreshProjectMetadata(workspaceId, selectedProject.id) : null;
      if (refreshedProject) setBuildHistory(refreshedProject.builds ?? []);
      await openFile(workspaceId, saved.path);
    }
  }

  async function handleApplyTemplate() {
    if (!selectedWorkspace) return;
    const template = templates.find(item => item.id === templateDraft.templateId);
    if (!template) {
      setError("Select a template to apply.");
      return;
    }
    const parameters = collectTemplateParameters(template, templateDraft);
    if (typeof parameters === "string") {
      setError(parameters);
      return;
    }

    const workspaceId = selectedWorkspace.id;
    const result = await runOperation(
      () => applyRepositoryTemplate(context, workspaceId, {
        templateId: template.id,
        scope: template.scope,
        targetPath: templateDraft.targetPath.trim() || null,
        parameters
      }),
      `Applied ${template.name}.`
    );
    if (!result || selectedIds.current.workspaceId !== workspaceId) return;

    setSolutions(result.tree.solutions);
    setFiles(result.tree.entries);
    const selected = result.tree.solutions.find(solution => solution.isSelected)?.path ?? "";
    setSelectedSolutionPath(result.tree.solutions.length > 1 ? selected : "");
    clearSourceDependentState();
    await loadSourceControlStatus(workspaceId);
    const refreshedProject = selectedProject ? await refreshProjectMetadata(workspaceId, selectedProject.id) : null;
    if (refreshedProject) setBuildHistory(refreshedProject.builds ?? []);
    const firstGeneratedFile = result.files.find(file => file.type === "file") ?? result.files[0];
    if (firstGeneratedFile) await openFile(workspaceId, firstGeneratedFile.path);
  }

  async function handleDeleteFile(path: string) {
    if (!selectedWorkspace) return;
    if (!(await api.dialogs.confirm({ message: `Delete ${path}?`, confirmLabel: "Delete", tone: "danger" }))) return;
    const workspaceId = selectedWorkspace.id;
    const deleted = await runOperation(() => deleteRepositoryFile(context, workspaceId, path), `Deleted ${path}.`);
    if (!deleted || selectedIds.current.workspaceId !== workspaceId) return;
    setFiles(current => current.filter(file => file.path !== path));
    setEditorTabs(current => current.filter(tab => tab.path !== path));
    clearSourceDependentState();
    await loadRepositoryTree(workspaceId, selectedSolutionPath || null);
    await loadSourceControlStatus(workspaceId);
    const refreshedProject = selectedProject ? await refreshProjectMetadata(workspaceId, selectedProject.id) : null;
    if (refreshedProject) setBuildHistory(refreshedProject.builds ?? []);
    if (path === activeFilePath) {
      const nextTab = editorTabs.find(tab => tab.path !== path);
      setActiveFilePath(nextTab?.path ?? "");
      setEditorText(nextTab?.content ?? "");
      setSavedEditorText(nextTab?.savedContent ?? "");
    }
  }

  async function handleRenameFile(path: string) {
    if (!selectedWorkspace) return;
    const destinationPath = (await api.dialogs.prompt({ title: "Rename file", message: "Enter a new path for the file.", defaultValue: path, confirmLabel: "Rename" }))?.trim();
    if (!destinationPath || destinationPath === path) return;
    const workspaceId = selectedWorkspace.id;
    const moved = await runOperation(() => moveRepositoryFile(context, workspaceId, path, destinationPath), `Renamed ${path} to ${destinationPath}.`);
    if (!moved || selectedIds.current.workspaceId !== workspaceId) return;
    setFiles(current => upsertFile(current.filter(file => file.path !== path), moved));
    setEditorTabs(current => current.map(tab => tab.path === path ? { ...tab, path: moved.path, content: moved.content ?? tab.content, savedContent: moved.content ?? tab.savedContent } : tab));
    if (activeFilePath === path) {
      setActiveFilePath(moved.path);
      setEditorText(moved.content ?? editorText);
      setSavedEditorText(moved.content ?? savedEditorText);
    }
    clearSourceDependentState();
    await loadRepositoryTree(workspaceId, selectedSolutionPath || null);
    await loadSourceControlStatus(workspaceId);
  }

  async function handleSelectSourceDiff(path: string, staged: boolean) {
    if (!selectedWorkspace) return;
    const diff = await runOperation(() => getSourceControlDiff(context, selectedWorkspace.id, path, staged), `Loaded diff for ${path}.`);
    if (diff && selectedIds.current.workspaceId === selectedWorkspace.id) {
      setSourceControlDiff(diff);
      setActiveInspectorTab("source");
      setDockOpen(true);
    }
  }

  async function handleStageFile(path: string) {
    if (!selectedWorkspace) return;
    const workspaceId = selectedWorkspace.id;
    const gitStatus = await runOperation(() => stageRepositoryFile(context, workspaceId, path), `Staged ${path}.`);
    if (gitStatus && selectedIds.current.workspaceId === workspaceId) {
      setSourceControlStatus(gitStatus);
      setSourceControlDiff(null);
      await refreshWorkspaces({ preserveSelection: true });
    }
  }

  async function handleUnstageFile(path: string) {
    if (!selectedWorkspace) return;
    const workspaceId = selectedWorkspace.id;
    const gitStatus = await runOperation(() => unstageRepositoryFile(context, workspaceId, path), `Unstaged ${path}.`);
    if (gitStatus && selectedIds.current.workspaceId === workspaceId) {
      setSourceControlStatus(gitStatus);
      setSourceControlDiff(null);
      await refreshWorkspaces({ preserveSelection: true });
    }
  }

  async function handleStageAll() {
    if (!selectedWorkspace) return;
    const workspaceId = selectedWorkspace.id;
    const gitStatus = await runOperation(() => stageAllRepositoryChanges(context, workspaceId), "Staged all changes.");
    if (gitStatus && selectedIds.current.workspaceId === workspaceId) {
      setSourceControlStatus(gitStatus);
      setSourceControlDiff(null);
      await refreshWorkspaces({ preserveSelection: true });
    }
  }

  async function handleCommitChanges() {
    if (!selectedWorkspace || !commitMessage.trim()) return;
    const workspaceId = selectedWorkspace.id;
    const result = await runOperation(() => commitRepositoryChanges(context, workspaceId, commitMessage.trim()), "Committed staged changes.");
    if (result && selectedIds.current.workspaceId === workspaceId) {
      setSourceControlStatus(result.status);
      setSourceControlDiff(null);
      setCommitMessage("");
      await refreshWorkspaces({ preserveSelection: true });
      await loadRepositoryTree(workspaceId, selectedSolutionPath || null);
    }
  }

  async function handlePushRepository() {
    if (!selectedWorkspace) return;
    const workspaceId = selectedWorkspace.id;
    const result = await runOperation(() => pushRepository(context, workspaceId), "Pushed committed changes.");
    if (result && selectedIds.current.workspaceId === workspaceId) {
      setSourceControlStatus(result.status);
      setSourceControlDiff(null);
      await refreshWorkspaces({ preserveSelection: true });
    }
  }

  async function handlePullRepository() {
    if (!selectedWorkspace) return;
    const workspaceId = selectedWorkspace.id;
    const result = await runOperation(() => pullRepository(context, workspaceId), "Pulled remote changes.");
    if (result && selectedIds.current.workspaceId === workspaceId) {
      setSourceControlStatus(result.status);
      setSourceControlDiff(null);
      await refreshWorkspaces({ preserveSelection: true });
      await loadRepositoryTree(workspaceId, selectedSolutionPath || null);
    }
  }

  async function handleSubmitBuild(commandOverride?: RepositoryBuildCommand) {
    if (!selectedWorkspace || !selectedProject) return;
    if (editorDirty) {
      setError("Save or discard file changes before building.");
      return;
    }

    setActiveInspectorTab("build");
    setDockOpen(true);
    const requestedRevision = selectedProject.currentRevision ?? null;
    const command = commandOverride || buildCommand || "Build";
    const targetPath = buildTargetPath.trim() || selectedSolutionPath || null;
    const build = await runOperation(
      () => submitBuild(context, selectedWorkspace.id, selectedProject.id, { projectId: selectedProject.id, revision: requestedRevision, command, targetPath }),
      `${command} submitted for ${selectedProject.name}.`
    );
    if (build) {
      const revisionedBuild = { ...build, revision: build.revision ?? requestedRevision };
      if (revisionedBuild.revision) {
        setWorkspaces(current => patchProject(current, selectedWorkspace.id, { ...selectedProject, currentRevision: revisionedBuild.revision }));
      }
      setActiveBuild(revisionedBuild);
      setBuildHistory(current => mergeBuildHistory(current, revisionedBuild));
      setBuildLog("");
      await refreshBuild(selectedWorkspace.id, selectedProject.id, revisionedBuild.id, revisionedBuild.revision);
    }
  }

  async function refreshBuild(workspaceId: string, projectId: string, buildId: string, fallbackRevision?: string | null) {
    clearBuildPoll();
    try {
      const [build, log] = await Promise.all([
        getBuild(context, workspaceId, projectId, buildId),
        getBuildLog(context, workspaceId, projectId, buildId).catch(() => "")
      ]);
      if (!mounted.current || !isCurrentSelection(selectedIds.current, workspaceId, projectId)) return;
      const artifact = build.artifact ?? null;
      if (!mounted.current || !isCurrentSelection(selectedIds.current, workspaceId, projectId)) return;
      const nextBuild = { ...build, revision: build.revision ?? fallbackRevision ?? activeBuild?.revision ?? null, artifact };
      setActiveBuild(nextBuild);
      setBuildHistory(current => mergeBuildHistory(current, nextBuild));
      setBuildLog(log);
    } catch (e) {
      setError(getErrorMessage(e));
    }
  }

  async function handlePromote() {
    if (!selectedWorkspace || !selectedProject || !activeBuild || !latestArtifact) return;
    if (!isBuildForCurrentRevision(activeBuild, selectedProject)) {
      setError("Build artifact is stale. Rebuild the current source revision before promoting.");
      return;
    }
    const workspaceId = selectedWorkspace.id;
    const projectId = selectedProject.id;
    const result = await runOperation(
      () => promoteBuild(context, workspaceId, projectId, activeBuild.id, { buildId: activeBuild.id }),
      `Promotion response received for ${latestArtifact.packageId} ${latestArtifact.version}.`
    );
    if (result && isCurrentSelection(selectedIds.current, workspaceId, projectId)) {
      setPromotionResult(result);
      if (result.accepted) {
        await refreshRuntimeStatus(workspaceId, projectId);
        if (!isCurrentSelection(selectedIds.current, workspaceId, projectId)) return;
      }
      setActiveInspectorTab(result.accepted ? "runtime" : "promote");
      setDockOpen(true);
    }
  }

  async function handlePromoteArtifact(artifact: BuildArtifact) {
    if (!selectedWorkspace || !selectedProject || !activeBuild) return;
    if (artifact.sourceIsDirty) {
      setError("Package artifact was built from uncommitted source. Commit and pack again before promoting.");
      return;
    }

    const workspaceId = selectedWorkspace.id;
    const projectId = selectedProject.id;
    const result = await runOperation(
      () => promoteBuildArtifact(context, workspaceId, projectId, activeBuild.id, artifact.id, { buildId: activeBuild.id }),
      `Promotion response received for ${artifact.packageId} ${artifact.version}.`
    );
    if (result && isCurrentSelection(selectedIds.current, workspaceId, projectId)) {
      setPromotionResult(result);
      if (result.accepted) {
        await refreshRuntimeStatus(workspaceId, projectId);
        if (!isCurrentSelection(selectedIds.current, workspaceId, projectId)) return;
      }
      setActiveInspectorTab(result.accepted ? "runtime" : "promote");
      setDockOpen(true);
    }
  }

  async function refreshRuntimeStatus(workspaceId = selectedWorkspace?.id, projectId = selectedProject?.id) {
    if (!workspaceId || !projectId) return;
    const requestId = ++runtimeStatusRequestId.current;
    try {
      const runtime = await getRuntimeStatus(context, workspaceId, projectId);
      if (!mounted.current || requestId !== runtimeStatusRequestId.current || !isCurrentSelection(selectedIds.current, workspaceId, projectId)) return;
      setRuntimeStatus(runtime);
    } catch (e) {
      if (!mounted.current || requestId !== runtimeStatusRequestId.current || !isCurrentSelection(selectedIds.current, workspaceId, projectId)) return;
      setError(getErrorMessage(e));
    }
  }

  async function handleRetryReconciliation() {
    if (!selectedWorkspace || !selectedProject) return;
    const workspaceId = selectedWorkspace.id;
    const projectId = selectedProject.id;
    const requestId = ++runtimeStatusRequestId.current;
    const updatedRuntime = await runOperation(
      () => retryReconciliation(context, workspaceId, projectId),
      `Retry reconciliation requested for ${selectedProject.name}.`
    );
    if (updatedRuntime && requestId === runtimeStatusRequestId.current && isCurrentSelection(selectedIds.current, workspaceId, projectId)) setRuntimeStatus(updatedRuntime);
  }

  async function handleRollback(version: string) {
    if (!selectedWorkspace || !selectedProject) return;
    if (!(await api.dialogs.confirm({ message: `Roll back ${selectedProject.packageId} to ${version}?`, confirmLabel: "Roll back", tone: "danger" }))) return;
    const workspaceId = selectedWorkspace.id;
    const projectId = selectedProject.id;
    const requestId = ++runtimeStatusRequestId.current;
    const updatedRuntime = await runOperation(
      () => rollbackPackage(context, workspaceId, projectId, version),
      `Rolled back ${selectedProject.packageId} to ${version}.`
    );
    if (updatedRuntime && requestId === runtimeStatusRequestId.current && isCurrentSelection(selectedIds.current, workspaceId, projectId)) setRuntimeStatus(updatedRuntime);
  }

  async function handleDiagnosticSelect(diagnostic: BuildDiagnostic) {
    if (!selectedWorkspace || !selectedProject || !diagnostic.filePath) return;
    const opened = await openFile(selectedWorkspace.id, diagnostic.filePath);
    if (opened) {
      setLineHint(formatDiagnosticLocation(diagnostic));
    }
  }

  async function handleDeleteProject() {
    if (!selectedWorkspace || !selectedProject) return;
    if (!(await api.dialogs.confirm({ message: `Delete project ${selectedProject.name}?`, confirmLabel: "Delete", tone: "danger" }))) return;
    const deleted = await runOperation(() => deleteProject(context, selectedWorkspace.id, selectedProject.id), `Deleted project ${selectedProject.name}.`);
    if (deleted) await refreshWorkspacesSafely({ preserveSelection: true });
  }

  async function handleDeleteWorkspace() {
    if (!selectedWorkspace) return;
    if (!(await api.dialogs.confirm({ message: `Delete workspace ${selectedWorkspace.name}?`, confirmLabel: "Delete", tone: "danger" }))) return;
    const deleted = await runOperation(() => deleteWorkspace(context, selectedWorkspace.id), `Deleted workspace ${selectedWorkspace.name}.`);
    if (deleted) {
      setEnteredWorkspaceId("");
      await refreshWorkspacesSafely();
    }
  }

  if (state === "loading") {
    return <EmptyState icon={<Hammer size={22} />}>Loading Extension Builder capabilities.</EmptyState>;
  }

  if (state === "failed") {
    return <StudioAlert tone="danger">{error ?? "Extension Builder is unavailable."}</StudioAlert>;
  }

  if (!isTrusted(capabilities)) {
    return (
      <section className="extension-builder-page">
        <div className="section-header modules-header">
          <div>
            <h2>Extension Builder</h2>
            <p>Trusted-team project workspaces, builds, promotion, and runtime recovery.</p>
          </div>
        </div>
        <StudioAlert tone="warning">
          You do not have Extension Builder capabilities for this backend. Access is gated by `GetCapabilities`; server enforcement remains authoritative.
        </StudioAlert>
      </section>
    );
  }

  const totalProjects = repositories.reduce((count, repository) => count + repository.projectCount, 0) || workspaces.reduce((count, workspace) => count + workspace.projects.length, 0);
  const loadedCount = workspaces.reduce((count, workspace) => count + workspace.projects.filter(project => project.runtimeStatus === "Loaded").length, 0);
  const attentionTotal = repositories.reduce((count, repository) => count + repository.attentionCount, 0);
  const stats: HomeStat[] = advanced
    ? [
      { label: "Repositories", value: repositories.length || workspaces.length },
      { label: "Projects", value: totalProjects },
      { label: "Loaded at runtime", value: loadedCount },
      { label: "Needs attention", value: attentionTotal }
    ]
    : [
      { label: "Extensions", value: repositories.length || workspaces.length },
      { label: "Projects", value: totalProjects },
      { label: "Loaded at runtime", value: loadedCount }
    ];
  const workspaceLabel = selectedWorkspace?.name ?? selectedRepository?.name ?? "Solution";

  return (
    <section className={inWorkspace ? "extension-builder-page extension-builder-page-workspace" : "extension-builder-page"}>
      {error ? <StudioAlert tone="danger">{error}</StudioAlert> : null}
      {status ? <StudioAlert tone="success">{status}</StudioAlert> : null}

      {!inWorkspace ? (
        <HomeView
          capabilities={capabilities!}
          advanced={advanced}
          busy={operationBusy}
          loading={state === "loading"}
          stats={stats}
          repositories={repositories}
          workspaces={workspaces}
          selectedWorkspaceId={selectedRepository?.id ?? selectedWorkspace?.id ?? ""}
          extensionName={extensionName}
          workspaceName={workspaceName}
          serverLocalPath={serverLocalPath}
          serverLocalName={serverLocalName}
          cloneRepositoryUrl={cloneRepositoryUrl}
          cloneRepositoryName={cloneRepositoryName}
          onToggleAdvanced={setAdvanced}
          onRefresh={() => bootstrap()}
          onOpenSolution={openSolution}
          onExtensionNameChange={setExtensionName}
          onCreateExtension={handleCreateExtension}
          onWorkspaceNameChange={setWorkspaceName}
          onCreateWorkspace={handleCreateWorkspace}
          onServerLocalPathChange={setServerLocalPath}
          onServerLocalNameChange={setServerLocalName}
          onAttachServerLocalRepository={handleAttachServerLocalRepository}
          onCloneRepositoryUrlChange={setCloneRepositoryUrl}
          onCloneRepositoryNameChange={setCloneRepositoryName}
          onCloneRepository={handleCloneRepository}
        />
      ) : (
        <div className="extension-builder-shell">
          <header className="extension-builder-commandbar">
            <div className="extension-builder-commandbar-left">
              <button type="button" className="extension-builder-back" onClick={() => void backToHome()}>
                <ChevronLeft size={15} />
                Solutions
              </button>
              <span className="extension-builder-crumb">
                <strong>{workspaceLabel}</strong>
                {selectedProject ? <span className="modules-muted"> / {selectedProject.packageId} {selectedProject.packageVersion}</span> : null}
                {advanced && selectedRepository?.activeBranch ? <span className="modules-muted"> · {selectedRepository.activeBranch}</span> : null}
              </span>
            </div>
            <div className="extension-builder-commandbar-right">
              {selectedProject ? <StatusChip tone={runtimeTone(selectedProject.runtimeStatus)}>{selectedProject.runtimeStatus ?? selectedProject.latestBuildStatus ?? "new"}</StatusChip> : null}
              <label className="extension-builder-autosave-toggle" title="Automatically save edits as you type">
                <input type="checkbox" aria-label="Auto-save" checked={autoSave} disabled={operationBusy} onChange={event => setAutoSave(event.target.checked)} />
                <span>Auto-save</span>
              </label>
              <button type="button" className="studio-button" disabled={operationBusy || !capabilities!.canEditFiles || !activeFilePath} title={!capabilities!.canEditFiles ? "Requires canEditFiles" : autoSave ? "Force save and resync (auto-save also saves as you type)" : undefined} onClick={handleSaveFile}>
                <Save size={15} />
                Save
              </button>
              {advanced ? (
                <button type="button" className="studio-button" disabled={operationBusy || !canBuild} title={buildDisabledReason ?? undefined} onClick={() => handleSubmitBuild()}>
                  <Play size={15} />
                  Build
                </button>
              ) : (
                <button type="button" className="studio-button studio-button-primary" disabled={operationBusy || !canBuild} title={buildDisabledReason ?? "Build and pack a NuGet package"} onClick={() => handleSubmitBuild("Pack")}>
                  <PackageCheck size={15} />
                  Pack
                </button>
              )}
              <button type="button" className="studio-icon-button" aria-label="Build &amp; properties" title="Build & properties" onClick={() => setDockOpen(open => !open)}>
                <Settings2 size={15} />
              </button>
            </div>
          </header>

          <div className="extension-builder-ide">
            {selectedWorkspace ? (
              <>
                <SolutionExplorer
                  capabilities={capabilities!}
                  advanced={advanced}
                  workspace={selectedWorkspace}
                  project={selectedProject}
                  templates={templates}
                  templateDraft={templateDraft}
                  files={fileRows}
                  solutions={solutions}
                  selectedSolutionPath={selectedSolutionPath}
                  selectedProjectId={selectedProject?.id ?? ""}
                  newFilePath={newFilePath}
                  busy={operationBusy}
                  projectDraft={projectDraft}
                  workingBranchName={workingBranchName || defaultWorkingBranchName}
                  allowProtectedBranchEdit={allowProtectedBranchEdit}
                  activeFilePath={activeFilePath}
                  onRefresh={() => loadProjectDetails(selectedWorkspace.id, selectedProject?.id ?? "", selectedSolutionPath || null)}
                  onSelectProject={async projectId => {
                    if (!(await ensureSavedBeforeLeaving())) return;
                    setSelectedProjectId(projectId);
                  }}
                  onTemplateDraftChange={setTemplateDraft}
                  onApplyTemplate={handleApplyTemplate}
                  onSelectSolution={handleSelectSolution}
                  onNewFilePathChange={setNewFilePath}
                  onCreateFile={handleCreateFile}
                  onDeleteFile={handleDeleteFile}
                  onRenameFile={handleRenameFile}
                  onOpenFile={path => { if (selectedWorkspace) void openFile(selectedWorkspace.id, path); }}
                  onProjectDraftChange={setProjectDraft}
                  onCreateProject={handleCreateProject}
                  onDeleteProject={handleDeleteProject}
                  onDeleteWorkspace={handleDeleteWorkspace}
                  onWorkingBranchNameChange={setWorkingBranchName}
                  onAllowProtectedBranchEditChange={setAllowProtectedBranchEdit}
                  onSelectWorkingCopy={handleSelectWorkingCopy}
                />

                <EditorSurface
                  workspace={selectedWorkspace}
                  project={selectedProject}
                  canEdit={capabilities!.canEditFiles}
                  editorTabs={editorTabs}
                  activeFilePath={activeFilePath}
                  editorText={editorText}
                  editorDirty={editorDirty}
                  autoSaving={autoSaving}
                  lineHint={lineHint}
                  diagnostics={activeBuild?.diagnostics ?? []}
                  onSelectEditorTab={handleSelectEditorTab}
                  onCloseEditorTab={handleCloseEditorTab}
                  onEditorTextChange={handleEditorTextChange}
                />
              </>
            ) : (
              <EmptyState icon={<Hammer size={22} />}>This solution is still loading.</EmptyState>
            )}
          </div>

          <BottomDock
            capabilities={capabilities!}
            advanced={advanced}
            project={selectedProject}
            open={dockOpen}
            onToggleOpen={() => setDockOpen(open => !open)}
            activeTab={activeInspectorTab}
            onSelectTab={tab => setActiveInspectorTab(tab)}
            activeBuild={activeBuild}
            buildHistory={buildHistory}
            buildLog={buildLog}
            sourceControlStatus={sourceControlStatus}
            sourceControlDiff={sourceControlDiff}
            commitMessage={commitMessage}
            buildCommand={buildCommand}
            buildTargetPath={buildTargetPath}
            artifact={latestArtifact}
            promotionResult={promotionResult}
            runtimeStatus={runtimeStatus}
            busy={operationBusy}
            canBuild={canBuild}
            canPromote={canPromote}
            onBuildCommandChange={setBuildCommand}
            onBuildTargetPathChange={setBuildTargetPath}
            onSubmitBuild={handleSubmitBuild}
            onPromoteArtifact={handlePromoteArtifact}
            onSelectBuild={build => {
              setActiveBuild(build);
              setActiveInspectorTab("build");
              setDockOpen(true);
              if (selectedWorkspace && selectedProject) void refreshBuild(selectedWorkspace.id, selectedProject.id, build.id, build.revision);
            }}
            onSelectSourceDiff={handleSelectSourceDiff}
            onStageFile={handleStageFile}
            onUnstageFile={handleUnstageFile}
            onStageAll={handleStageAll}
            onCommitMessageChange={setCommitMessage}
            onCommit={handleCommitChanges}
            onPush={handlePushRepository}
            onPull={handlePullRepository}
            onPromote={handlePromote}
            onRefreshRuntime={() => refreshRuntimeStatus()}
            onRetryReconciliation={handleRetryReconciliation}
            onRollback={handleRollback}
            onDiagnosticSelect={diagnostic => void handleDiagnosticSelect(diagnostic)}
          />
        </div>
      )}
    </section>
  );
}
