import React, { useEffect, useMemo, useRef, useState } from "react";
import { Boxes, FilePlus2, FolderPlus, GitBranch, Hammer, PackageCheck, Pencil, Play, RefreshCcw, RotateCcw, Save, Trash2 } from "lucide-react";
import type { ElsaStudioModuleApi } from "../../sdk";
import { EmptyState, StatusChip, StudioAlert, StudioTabs, StudioToolbar, StudioToolbarGroup, type StudioStatusTone } from "../ui";
import {
  applyRepositoryTemplate,
  createProject,
  createWorkspace,
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
  type ExtensionTemplateParameter,
  type ExtensionWorkspace,
  getSourceControlDiff,
  getSourceControlStatus,
  type PackagePromotionResult,
  type RepositoryFileSummary,
  type RepositorySolutionSummary,
  type RepositoryBuildCommand,
  type RuntimeVersion,
  type SourceControlDiff,
  type SourceControlStatus,
  stageAllRepositoryChanges,
  stageRepositoryFile,
  unstageRepositoryFile
} from "./extensionBuilderApi";
import { getErrorMessage } from "./moduleManagementApi";

type BuilderState = "loading" | "ready" | "failed";
type InspectorTab = "build" | "source" | "promote" | "runtime";

interface ProjectDraft {
  name: string;
  packageId: string;
  packageVersion: string;
  templateId: string;
}

interface TemplateApplicationDraft {
  templateId: string;
  targetPath: string;
  parameters: Record<string, string>;
}

interface EditorTab {
  path: string;
  content: string;
  savedContent: string;
}

const inspectorTabs = [
  { id: "build", label: "Build" },
  { id: "source", label: "Source" },
  { id: "promote", label: "Promote" },
  { id: "runtime", label: "Runtime" }
];

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
  const [workspaceName, setWorkspaceName] = useState("Extension workspace");
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
  const selectedWorkspace = workspaces.find(workspace => workspace.id === selectedWorkspaceId) ?? (!selectedWorkspaceId ? workspaces[0] ?? null : null);
  const selectedRepository = repositories.find(repository => repository.id === selectedWorkspaceId) ?? repositories.find(repository => repository.id === selectedWorkspace?.id) ?? repositories[0] ?? null;
  const selectedProject = selectedWorkspace?.projects.find(project => project.id === selectedProjectId) ?? selectedWorkspace?.projects[0] ?? null;
  selectedIds.current = { workspaceId: selectedWorkspace?.id ?? "", projectId: selectedProject?.id ?? "" };
  const fileRows = useMemo(() => [...files].sort((a, b) => fileSortKey(a).localeCompare(fileSortKey(b))), [files]);
  const projectTemplates = useMemo(() => templates.filter(template => template.scope === "Project"), [templates]);
  const activeTab = editorTabs.find(tab => tab.path === activeFilePath) ?? null;
  const editorDirty = activeTab ? activeTab.content !== activeTab.savedContent : editorText !== savedEditorText;
  const hasRepositoryRows = repositories.length > 0 || workspaces.length > 0;
  const latestArtifact = activeBuild?.artifact ?? null;
  const canBuild = !!capabilities?.canBuild && !!selectedProject && !editorDirty && !isBuildRunning(activeBuild);
  const canPromote = !!capabilities?.canPromote && !!latestArtifact && isBuildForCurrentRevision(activeBuild, selectedProject);
  const defaultWorkingBranchName = useMemo(() => `extension-builder/${sessionId}`, [sessionId]);

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
    if (templateDraft.templateId && templates.some(template => template.id === templateDraft.templateId)) return;
    const initialTemplate = templates.find(template => template.scope === "Item") ?? templates.find(template => template.scope !== "Project") ?? templates[0];
    setTemplateDraft(createTemplateApplicationDraft(initialTemplate));
  }, [templates, templateDraft.templateId]);

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
    if (!selectedWorkspace) {
      projectDetailsRequestId.current += 1;
      setFiles([]);
      setSolutions([]);
      setSelectedSolutionPath("");
      setEditorTabs([]);
      setSourceControlStatus(null);
      setSourceControlDiff(null);
      setCommitMessage("");
      setActiveFilePath("");
      setEditorText("");
      setSavedEditorText("");
      setActiveBuild(null);
      setBuildHistory([]);
      setBuildLog("");
      runtimeStatusRequestId.current += 1;
      setRuntimeStatus(null);
      return;
    }

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
  }, [selectedWorkspace?.id, selectedProject?.id, selectedSolutionPath]);

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
        const file = await readRepositoryFile(context, workspaceId, firstFile.path);
        if (!canApplyProjectState(mounted.current, requestId, projectDetailsRequestId.current, selectedIds.current, workspaceId, projectId)) return;
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
      }
    } catch (e) {
      setError(getErrorMessage(e));
    }
  }

  async function openFile(workspaceId: string, path: string, options?: { force?: boolean }) {
    if (!options?.force && editorDirty && !window.confirm("Discard unsaved file changes?")) return false;
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

  function handleSelectEditorTab(path: string) {
    const tab = editorTabs.find(item => item.path === path);
    if (!tab) return;
    if (editorDirty && activeFilePath !== path && !window.confirm("Discard unsaved file changes?")) return;
    setActiveFilePath(tab.path);
    setEditorText(tab.content);
    setSavedEditorText(tab.savedContent);
    setLineHint(null);
  }

  function handleCloseEditorTab(path: string) {
    const tab = editorTabs.find(item => item.path === path);
    if (!tab) return;
    if (tab.content !== tab.savedContent && !window.confirm("Discard unsaved file changes?")) return;
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
    if (project) {
      if (await refreshWorkspacesSafely({ preserveSelection: true })) {
        setSelectedProjectId(project.id);
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
      () => writeRepositoryFile(context, workspaceId, path, { content }),
      `Saved ${path}.`
    );
    if (saved && selectedIds.current.workspaceId === workspaceId) {
      setSavedEditorText(content);
      setFiles(current => upsertFile(current, saved));
      setEditorTabs(current => upsertEditorTab(current, { path: saved.path, content, savedContent: content }));
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
    if (!selectedWorkspace || !window.confirm(`Delete ${path}?`)) return;
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
    const destinationPath = window.prompt("Rename file", path)?.trim();
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

  async function handleSubmitBuild() {
    if (!selectedWorkspace || !selectedProject) return;
    if (editorDirty) {
      setError("Save or discard file changes before building.");
      return;
    }

    const requestedRevision = selectedProject.currentRevision ?? null;
    const command = buildCommand || "Build";
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
    if (!selectedWorkspace || !selectedProject || !window.confirm(`Roll back ${selectedProject.packageId} to ${version}?`)) return;
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
    if (!selectedWorkspace || !selectedProject || !window.confirm(`Delete project ${selectedProject.name}?`)) return;
    const deleted = await runOperation(() => deleteProject(context, selectedWorkspace.id, selectedProject.id), `Deleted project ${selectedProject.name}.`);
    if (deleted) await refreshWorkspacesSafely({ preserveSelection: true });
  }

  async function handleDeleteWorkspace() {
    if (!selectedWorkspace || !window.confirm(`Delete workspace ${selectedWorkspace.name}?`)) return;
    const deleted = await runOperation(() => deleteWorkspace(context, selectedWorkspace.id), `Deleted workspace ${selectedWorkspace.name}.`);
    if (deleted) await refreshWorkspacesSafely();
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

  return (
    <section className="extension-builder-page modules-page">
      <div className="section-header modules-header">
        <div>
          <h2>Extension Builder</h2>
          <p>{selectedRepository ? `${selectedRepository.name}: ${selectedRepository.projectCount} project(s), ${formatRemoteState(selectedRepository.remoteState)}${selectedRepository.activeBranch ? ` on ${selectedRepository.activeBranch}` : ""}.` : "Select, create, or clone a Git-backed repository to start building extensions."}</p>
        </div>
        <StudioToolbar>
          <StudioToolbarGroup>
            <button type="button" className="studio-button" onClick={() => bootstrap()} disabled={state === "loading" || operationBusy}>
              <RefreshCcw size={15} />
              Refresh
            </button>
          </StudioToolbarGroup>
        </StudioToolbar>
      </div>

      {error ? <StudioAlert tone="danger">{error}</StudioAlert> : null}
      {status ? <StudioAlert tone="success">{status}</StudioAlert> : null}

      <div className="modules-summary-strip">
        <SummaryItem label="Repositories" value={repositories.length || workspaces.length} />
        <SummaryItem label="Projects" value={repositories.reduce((count, repository) => count + repository.projectCount, 0) || workspaces.reduce((count, workspace) => count + workspace.projects.length, 0)} />
        <SummaryItem label="Build" value={activeBuild?.status ?? selectedProject?.latestBuildStatus ?? "none"} />
        <SummaryItem label="Remote" value={selectedRepository ? formatRemoteState(selectedRepository.remoteState) : "unknown"} />
        <SummaryItem label="Attention" value={selectedRepository?.attentionCount ?? 0} />
      </div>

      {!hasRepositoryRows ? (
        <EmptyState icon={<Boxes size={22} />}>No Extension Builder repositories are available for this owner.</EmptyState>
      ) : null}

      <div className="extension-builder-workbench">
        <WorkspaceBrowser
          capabilities={capabilities!}
          templates={projectTemplates}
          repositories={repositories}
          workspaces={workspaces}
          selectedWorkspaceId={selectedRepository?.id ?? selectedWorkspace?.id ?? ""}
          selectedProjectId={selectedProject?.id ?? ""}
          workspaceName={workspaceName}
          workingBranchName={workingBranchName || defaultWorkingBranchName}
          allowProtectedBranchEdit={allowProtectedBranchEdit}
          projectDraft={projectDraft}
          busy={operationBusy}
          onWorkspaceNameChange={setWorkspaceName}
          onWorkingBranchNameChange={setWorkingBranchName}
          onAllowProtectedBranchEditChange={setAllowProtectedBranchEdit}
          onProjectDraftChange={setProjectDraft}
          onCreateWorkspace={handleCreateWorkspace}
          onSelectWorkingCopy={handleSelectWorkingCopy}
          onCreateProject={handleCreateProject}
          onSelectWorkspace={workspaceId => {
            if (editorDirty && !window.confirm("Discard unsaved file changes?")) return;
            setSelectedWorkspaceId(workspaceId);
          }}
          onSelectProject={projectId => {
            if (editorDirty && !window.confirm("Discard unsaved file changes?")) return;
            setSelectedProjectId(projectId);
          }}
          onDeleteWorkspace={handleDeleteWorkspace}
          onDeleteProject={handleDeleteProject}
        />

        <ProjectWorkspace
          capabilities={capabilities!}
          workspace={selectedWorkspace}
          project={selectedProject}
          templates={templates}
          templateDraft={templateDraft}
          files={fileRows}
          solutions={solutions}
          selectedSolutionPath={selectedSolutionPath}
          editorTabs={editorTabs}
          activeFilePath={activeFilePath}
          editorText={editorText}
          editorDirty={editorDirty}
          lineHint={lineHint}
          newFilePath={newFilePath}
          busy={operationBusy}
          diagnostics={activeBuild?.diagnostics ?? []}
          onTemplateDraftChange={setTemplateDraft}
          onApplyTemplate={handleApplyTemplate}
          onSelectSolution={handleSelectSolution}
          onNewFilePathChange={setNewFilePath}
          onCreateFile={handleCreateFile}
          onDeleteFile={handleDeleteFile}
          onRenameFile={handleRenameFile}
          onOpenFile={path => selectedWorkspace ? openFile(selectedWorkspace.id, path) : undefined}
          onSelectEditorTab={handleSelectEditorTab}
          onCloseEditorTab={handleCloseEditorTab}
          onEditorTextChange={handleEditorTextChange}
          onSaveFile={handleSaveFile}
          onSubmitBuild={handleSubmitBuild}
          canBuild={canBuild}
        />

        <BuildRuntimeInspector
          capabilities={capabilities!}
          project={selectedProject}
          activeTab={activeInspectorTab}
          onSelectTab={tab => setActiveInspectorTab(tab as InspectorTab)}
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
          onSelectBuild={build => {
            setActiveBuild(build);
            setActiveInspectorTab("build");
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
    </section>
  );
}

function WorkspaceBrowser({
  capabilities,
  templates,
  repositories,
  workspaces,
  selectedWorkspaceId,
  selectedProjectId,
  workspaceName,
  workingBranchName,
  allowProtectedBranchEdit,
  projectDraft,
  busy,
  onWorkspaceNameChange,
  onWorkingBranchNameChange,
  onAllowProtectedBranchEditChange,
  onProjectDraftChange,
  onCreateWorkspace,
  onSelectWorkingCopy,
  onCreateProject,
  onSelectWorkspace,
  onSelectProject,
  onDeleteWorkspace,
  onDeleteProject
}: {
  capabilities: ExtensionBuilderCapabilities;
  templates: ExtensionTemplate[];
  repositories: ExtensionRepositorySummary[];
  workspaces: ExtensionWorkspace[];
  selectedWorkspaceId: string;
  selectedProjectId: string;
  workspaceName: string;
  workingBranchName: string;
  allowProtectedBranchEdit: boolean;
  projectDraft: ProjectDraft;
  busy: boolean;
  onWorkspaceNameChange(value: string): void;
  onWorkingBranchNameChange(value: string): void;
  onAllowProtectedBranchEditChange(value: boolean): void;
  onProjectDraftChange(value: ProjectDraft): void;
  onCreateWorkspace(): void;
  onSelectWorkingCopy(): void;
  onCreateProject(): void;
  onSelectWorkspace(workspaceId: string): void;
  onSelectProject(projectId: string): void;
  onDeleteWorkspace(): void;
  onDeleteProject(): void;
}) {
  const selectedWorkspace = workspaces.find(workspace => workspace.id === selectedWorkspaceId) ?? null;
  const canCreate = capabilities.canCreateWorkspace;
  const repositoryRows = repositories.length > 0
    ? repositories
    : workspaces.map(workspace => ({
      id: workspace.id,
      name: workspace.name,
      owner: workspace.owner,
      activeBranch: null,
      isDirty: false,
      remoteState: "unknown",
      latestBuildStatus: workspace.projects[0]?.latestBuildStatus ?? null,
      attentionCount: 0,
      projectCount: workspace.projects.length,
      updatedAt: workspace.updatedAt
    } satisfies ExtensionRepositorySummary));
  const selectedRepository = repositoryRows.find(repository => repository.id === selectedWorkspaceId) ?? null;

  return (
    <aside className="modules-source-nav extension-builder-browser" aria-label="Extension Builder repositories">
      <div className="modules-source-nav-heading">
        <span>Repositories</span>
        <strong>{repositoryRows.length}</strong>
      </div>
      <details className="extension-builder-create">
        <summary>New or Clone</summary>
        <label>
          <span>Repository name</span>
          <input aria-label="Repository name" value={workspaceName} disabled={busy || !canCreate} onChange={event => onWorkspaceNameChange(event.target.value)} />
        </label>
        <button type="button" className="studio-button" disabled={busy || !canCreate} title={canCreate ? "Create managed repository" : "Requires canCreateWorkspace"} onClick={onCreateWorkspace}>
          <FolderPlus size={15} />
          Create managed repo
        </button>
      </details>
      {repositoryRows.map(repository => (
        <button key={repository.id} type="button" className={repository.id === selectedWorkspaceId ? "modules-source-button active" : "modules-source-button"} onClick={() => onSelectWorkspace(repository.id)}>
          <span>
            <strong>{repository.name}</strong>
            <small>{repository.owner ?? "current owner"} · {repository.projectCount} project(s)</small>
            <small>{repository.activeBranch ?? "no branch"} · {formatRemoteState(repository.remoteState)}{repository.isDirty ? " · dirty" : ""}</small>
          </span>
          <em>{repository.attentionCount}</em>
        </button>
      ))}

      {selectedRepository ? (
        <details className="extension-builder-create">
          <summary>Working copy</summary>
          <label>
            <span>Working branch</span>
            <input aria-label="Working branch" value={workingBranchName} disabled={busy || !capabilities.canEditFiles} onChange={event => onWorkingBranchNameChange(event.target.value)} />
          </label>
          <label className="extension-builder-checkbox">
            <input type="checkbox" aria-label="Allow default branch editing" checked={allowProtectedBranchEdit} disabled={busy || !capabilities.canEditFiles} onChange={event => onAllowProtectedBranchEditChange(event.target.checked)} />
            <span>Allow default branch editing</span>
          </label>
          <button type="button" className="studio-button" disabled={busy || !capabilities.canEditFiles || !workingBranchName.trim()} title={capabilities.canEditFiles ? "Open working branch" : "Requires canEditFiles"} onClick={onSelectWorkingCopy}>
            <GitBranch size={15} />
            Open working branch
          </button>
        </details>
      ) : null}

      <div className="modules-source-group">
        <span>Projects</span>
        {selectedWorkspace?.projects.length === 0 ? <p className="modules-muted">No projects in this workspace.</p> : null}
        {selectedWorkspace?.projects.map(project => (
          <button key={project.id} type="button" className={project.id === selectedProjectId ? "active" : ""} onClick={() => onSelectProject(project.id)}>
            <span>{project.name}</span>
            <StatusChip tone={runtimeTone(project.runtimeStatus)}>{project.runtimeStatus ?? project.latestBuildStatus ?? "new"}</StatusChip>
          </button>
        ))}
      </div>

      {selectedWorkspace ? (
        <details className="extension-builder-create">
          <summary>Add project</summary>
          <label>
            <span>Template</span>
            <select aria-label="Project template" value={projectDraft.templateId} disabled={busy || !canCreate} onChange={event => onProjectDraftChange(applyTemplateDefaults({ ...projectDraft, templateId: event.target.value }, templates.find(template => template.id === event.target.value)))}>
              {templates.map(template => <option key={template.id} value={template.id}>{template.name}</option>)}
            </select>
          </label>
          <label>
            <span>Project name</span>
            <input aria-label="Project name" value={projectDraft.name} disabled={busy || !canCreate} onChange={event => onProjectDraftChange({ ...projectDraft, name: event.target.value })} />
          </label>
          <label>
            <span>Package id</span>
            <input aria-label="Package id" value={projectDraft.packageId} disabled={busy || !canCreate} onChange={event => onProjectDraftChange({ ...projectDraft, packageId: event.target.value })} />
          </label>
          <label>
            <span>Initial version</span>
            <input aria-label="Initial version" value={projectDraft.packageVersion} disabled={busy || !canCreate} onChange={event => onProjectDraftChange({ ...projectDraft, packageVersion: event.target.value })} />
          </label>
          <button type="button" className="studio-button" disabled={busy || !canCreate} title={canCreate ? "Create project" : "Requires canCreateWorkspace"} onClick={onCreateProject}>
            <PackageCheck size={15} />
            Create project
          </button>
          <div className="extension-builder-danger-actions">
            <button type="button" className="studio-button" disabled={busy || !selectedProjectId} onClick={onDeleteProject}>
              <Trash2 size={15} />
              Delete project
            </button>
            <button type="button" className="studio-button" disabled={busy || !selectedWorkspaceId} onClick={onDeleteWorkspace}>
              <Trash2 size={15} />
              Delete workspace
            </button>
          </div>
        </details>
      ) : null}
    </aside>
  );
}

function ProjectWorkspace({
  capabilities,
  workspace,
  project,
  templates,
  templateDraft,
  files,
  solutions,
  selectedSolutionPath,
  editorTabs,
  activeFilePath,
  editorText,
  editorDirty,
  lineHint,
  newFilePath,
  busy,
  diagnostics,
  canBuild,
  onTemplateDraftChange,
  onApplyTemplate,
  onSelectSolution,
  onNewFilePathChange,
  onCreateFile,
  onDeleteFile,
  onRenameFile,
  onOpenFile,
  onSelectEditorTab,
  onCloseEditorTab,
  onEditorTextChange,
  onSaveFile,
  onSubmitBuild
}: {
  capabilities: ExtensionBuilderCapabilities;
  workspace: ExtensionWorkspace | null;
  project: ExtensionProject | null;
  templates: ExtensionTemplate[];
  templateDraft: TemplateApplicationDraft;
  files: RepositoryFileSummary[];
  solutions: RepositorySolutionSummary[];
  selectedSolutionPath: string;
  editorTabs: EditorTab[];
  activeFilePath: string;
  editorText: string;
  editorDirty: boolean;
  lineHint: string | null;
  newFilePath: string;
  busy: boolean;
  diagnostics: BuildDiagnostic[];
  canBuild: boolean;
  onTemplateDraftChange(value: TemplateApplicationDraft): void;
  onApplyTemplate(): void;
  onSelectSolution(path: string): void;
  onNewFilePathChange(value: string): void;
  onCreateFile(): void;
  onDeleteFile(path: string): void;
  onRenameFile(path: string): void;
  onOpenFile(path: string): void;
  onSelectEditorTab(path: string): void;
  onCloseEditorTab(path: string): void;
  onEditorTextChange(value: string): void;
  onSaveFile(): void;
  onSubmitBuild(): void;
}) {
  const canEdit = capabilities.canEditFiles;
  const selectedTemplate = templates.find(template => template.id === templateDraft.templateId) ?? templates[0] ?? null;

  if (!workspace) {
    return (
      <div className="modules-grid-panel extension-builder-editor">
        <EmptyState icon={<Boxes size={22} />}>Select or create a repository to edit source files.</EmptyState>
      </div>
    );
  }

  return (
    <div className="modules-grid-panel extension-builder-editor">
      <div className="modules-grid-heading">
        <div>
          <h3>{project?.name ?? workspace.name}</h3>
          <p><code>{workspace.name}</code>{project ? ` · ${project.packageId} ${project.packageVersion}` : ""}</p>
        </div>
        <StudioToolbar>
          <StudioToolbarGroup>
            <button type="button" className="studio-button" disabled={busy || !canEdit || !activeFilePath} title={!canEdit ? "Requires canEditFiles" : undefined} onClick={onSaveFile}>
              <Save size={15} />
              Save
            </button>
            <button type="button" className="studio-button" disabled={busy || !canBuild} title={editorDirty ? "Save file changes before building" : !capabilities.canBuild ? "Requires canBuild" : undefined} onClick={onSubmitBuild}>
              <Play size={15} />
              Build
            </button>
          </StudioToolbarGroup>
        </StudioToolbar>
      </div>

      <div className="extension-builder-editor-layout">
        <aside className="extension-builder-file-tree" aria-label="Repository files">
          {solutions.length > 1 ? (
            <label className="extension-builder-solution-picker">
              <span>Solution</span>
              <select aria-label="Solution" value={selectedSolutionPath} disabled={busy} onChange={event => onSelectSolution(event.target.value)}>
                <option value="">Select solution</option>
                {solutions.map(solution => <option key={solution.path} value={solution.path}>{solution.name}</option>)}
              </select>
            </label>
          ) : solutions.length === 1 ? (
            <div className="extension-builder-selected-solution">
              <span>Solution</span>
              <code>{solutions[0].path}</code>
            </div>
          ) : null}
          <div className="extension-builder-file-create">
            <input aria-label="New file path" placeholder="src/MyActivity.cs" value={newFilePath} disabled={busy || !canEdit} onChange={event => onNewFilePathChange(event.target.value)} />
            <button type="button" className="studio-icon-button" disabled={busy || !canEdit || !newFilePath.trim()} title={canEdit ? "Create file" : "Requires canEditFiles"} aria-label="Create file" onClick={onCreateFile}>
              <FilePlus2 size={14} />
            </button>
          </div>
          {templates.length > 0 ? (
            <details className="extension-builder-template-apply">
              <summary>Apply template</summary>
              <label>
                <span>Template</span>
                <select aria-label="Repository template" value={selectedTemplate?.id ?? ""} disabled={busy || !canEdit} onChange={event => {
                  const template = templates.find(item => item.id === event.target.value);
                  if (template) onTemplateDraftChange(createTemplateApplicationDraft(template, templateDraft));
                }}>
                  {templates.map(template => <option key={template.id} value={template.id}>{template.name} · {template.scope}</option>)}
                </select>
              </label>
              <label>
                <span>Target path</span>
                <input aria-label="Template target path" placeholder={defaultTemplateTargetPath(selectedTemplate)} value={templateDraft.targetPath} disabled={busy || !canEdit} onChange={event => onTemplateDraftChange({ ...templateDraft, targetPath: event.target.value })} />
              </label>
              {selectedTemplate?.parameters.map(parameter => (
                <label key={parameter.name}>
                  <span>{parameter.displayName}</span>
                  <input aria-label={`Template parameter ${parameter.displayName}`} value={templateDraft.parameters[parameter.name] ?? parameter.defaultValue ?? ""} disabled={busy || !canEdit} onChange={event => onTemplateDraftChange({
                    ...templateDraft,
                    parameters: { ...templateDraft.parameters, [parameter.name]: event.target.value }
                  })} />
                </label>
              ))}
              <button type="button" className="studio-button" disabled={busy || !canEdit || !selectedTemplate} title={canEdit ? "Apply template" : "Requires canEditFiles"} onClick={onApplyTemplate}>
                <FilePlus2 size={15} />
                Apply
              </button>
            </details>
          ) : null}
          {files.length === 0 ? <p className="modules-muted">No files reported for this repository.</p> : null}
          {files.map(file => (
            <div key={file.path} className={file.path === activeFilePath ? "extension-builder-file-row active" : "extension-builder-file-row"}>
              <button type="button" disabled={file.type !== "file"} onClick={() => onOpenFile(file.path)}>
                <span>{file.type === "folder" ? "▸" : file.kind === "Solution" ? "S" : file.kind === "Project" ? "P" : "•"}</span>
                <code>{file.path}{file.isDirty ? " *" : ""}</code>
              </button>
              {file.type === "file" ? (
                <>
                  <button type="button" className="studio-icon-button" disabled={busy || !canEdit} title={`Rename ${file.path}`} aria-label={`Rename ${file.path}`} onClick={() => onRenameFile(file.path)}>
                    <Pencil size={13} />
                  </button>
                  <button type="button" className="studio-icon-button" disabled={busy || !canEdit} title={`Delete ${file.path}`} aria-label={`Delete ${file.path}`} onClick={() => onDeleteFile(file.path)}>
                    <Trash2 size={13} />
                  </button>
                </>
              ) : null}
            </div>
          ))}
        </aside>

        <div className="extension-builder-code-panel">
          {editorTabs.length > 0 ? (
            <div className="extension-builder-editor-tabs" role="tablist" aria-label="Open repository files">
              {editorTabs.map(tab => (
                <button key={tab.path} type="button" role="tab" aria-selected={tab.path === activeFilePath} className={tab.path === activeFilePath ? "active" : ""} onClick={() => onSelectEditorTab(tab.path)}>
                  <span>{tab.path}{tab.content !== tab.savedContent ? " *" : ""}</span>
                  <em aria-label={`Close ${tab.path}`} onClick={event => {
                    event.stopPropagation();
                    onCloseEditorTab(tab.path);
                  }}>x</em>
                </button>
              ))}
            </div>
          ) : null}
          <div className="extension-builder-editor-status">
            <span>{activeFilePath ? <code>{activeFilePath}</code> : "No file selected"}</span>
            {editorDirty ? <StatusChip tone="warning">dirty</StatusChip> : <StatusChip tone="success">saved</StatusChip>}
            {lineHint ? <StatusChip tone="accent">{lineHint}</StatusChip> : null}
          </div>
          <textarea
            aria-label="Project file editor"
            className="extension-builder-code-editor extension-builder-monaco-shell"
            spellCheck={false}
            value={editorText}
            disabled={!canEdit || !activeFilePath}
            onChange={event => onEditorTextChange(event.target.value)}
          />
          {diagnostics.some(diagnostic => diagnostic.filePath === activeFilePath || formatDiagnosticLocation(diagnostic) === lineHint) ? (
            <div className="extension-builder-inline-diagnostics" aria-label="Inline diagnostics">
              {diagnostics.filter(diagnostic => diagnostic.filePath === activeFilePath || formatDiagnosticLocation(diagnostic) === lineHint).map((diagnostic, index) => (
                <span key={`${diagnostic.message}-${index}`}>
                  <StatusChip tone={diagnosticTone(diagnostic.severity)}>{diagnostic.severity}</StatusChip>
                  {formatDiagnosticLocation(diagnostic)} {diagnostic.message}
                </span>
              ))}
            </div>
          ) : null}
        </div>
      </div>
    </div>
  );
}

function BuildRuntimeInspector({
  capabilities,
  project,
  activeTab,
  onSelectTab,
  activeBuild,
  buildHistory,
  buildLog,
  sourceControlStatus,
  sourceControlDiff,
  commitMessage,
  buildCommand,
  buildTargetPath,
  artifact,
  promotionResult,
  runtimeStatus,
  busy,
  canBuild,
  canPromote,
  onBuildCommandChange,
  onBuildTargetPathChange,
  onSubmitBuild,
  onSelectBuild,
  onSelectSourceDiff,
  onStageFile,
  onUnstageFile,
  onStageAll,
  onCommitMessageChange,
  onCommit,
  onPush,
  onPull,
  onPromote,
  onRefreshRuntime,
  onRetryReconciliation,
  onRollback,
  onDiagnosticSelect
}: {
  capabilities: ExtensionBuilderCapabilities;
  project: ExtensionProject | null;
  activeTab: InspectorTab;
  onSelectTab(tab: string): void;
  activeBuild: BuildResult | null;
  buildHistory: BuildResult[];
  buildLog: string;
  sourceControlStatus: SourceControlStatus | null;
  sourceControlDiff: SourceControlDiff | null;
  commitMessage: string;
  buildCommand: RepositoryBuildCommand;
  buildTargetPath: string;
  artifact: BuildArtifact | null;
  promotionResult: PackagePromotionResult | null;
  runtimeStatus: ExtensionRuntimeStatus | null;
  busy: boolean;
  canBuild: boolean;
  canPromote: boolean;
  onBuildCommandChange(value: RepositoryBuildCommand): void;
  onBuildTargetPathChange(value: string): void;
  onSubmitBuild(): void;
  onSelectBuild(build: BuildResult): void;
  onSelectSourceDiff(path: string, staged: boolean): void;
  onStageFile(path: string): void;
  onUnstageFile(path: string): void;
  onStageAll(): void;
  onCommitMessageChange(value: string): void;
  onCommit(): void;
  onPush(): void;
  onPull(): void;
  onPromote(): void;
  onRefreshRuntime(): void;
  onRetryReconciliation(): void;
  onRollback(version: string): void;
  onDiagnosticSelect(diagnostic: BuildDiagnostic): void;
}) {
  return (
    <aside className="modules-inspector extension-builder-inspector" aria-label="Build and runtime inspector">
      <div className="modules-inspector-heading">
        <div>
          <span>Inspector</span>
          <h3>{project?.name ?? "No project"}</h3>
        </div>
        {activeBuild ? <StatusChip tone={buildTone(activeBuild.status)}>{activeBuild.status}</StatusChip> : null}
      </div>
      <StudioTabs tabs={inspectorTabs} activeTab={activeTab} onSelect={onSelectTab} />

      {activeTab === "build" ? (
        <BuildPanel
          activeBuild={activeBuild}
          buildHistory={buildHistory}
          buildLog={buildLog}
          buildCommand={buildCommand}
          buildTargetPath={buildTargetPath}
          busy={busy}
          canBuild={canBuild}
          capabilities={capabilities}
          onBuildCommandChange={onBuildCommandChange}
          onBuildTargetPathChange={onBuildTargetPathChange}
          onSubmitBuild={onSubmitBuild}
          onSelectBuild={onSelectBuild}
          onDiagnosticSelect={onDiagnosticSelect}
        />
      ) : null}

      {activeTab === "source" ? (
        <SourceControlPanel
          status={sourceControlStatus}
          diff={sourceControlDiff}
          commitMessage={commitMessage}
          busy={busy}
          onSelectDiff={onSelectSourceDiff}
          onStageFile={onStageFile}
          onUnstageFile={onUnstageFile}
          onStageAll={onStageAll}
          onCommitMessageChange={onCommitMessageChange}
          onCommit={onCommit}
          onPush={onPush}
          onPull={onPull}
        />
      ) : null}

      {activeTab === "promote" ? (
        <PromotePanel
          capabilities={capabilities}
          activeBuild={activeBuild}
          artifact={artifact}
          promotionResult={promotionResult}
          busy={busy}
          canPromote={canPromote}
          isBuildCurrent={isBuildForCurrentRevision(activeBuild, project)}
          onPromote={onPromote}
        />
      ) : null}

      {activeTab === "runtime" ? (
        <RuntimePanel
          capabilities={capabilities}
          runtimeStatus={runtimeStatus}
          busy={busy}
          onRefreshRuntime={onRefreshRuntime}
          onRetryReconciliation={onRetryReconciliation}
          onRollback={onRollback}
        />
      ) : null}
    </aside>
  );
}

function BuildPanel({
  activeBuild,
  buildHistory,
  buildLog,
  buildCommand,
  buildTargetPath,
  busy,
  canBuild,
  capabilities,
  onBuildCommandChange,
  onBuildTargetPathChange,
  onSubmitBuild,
  onSelectBuild,
  onDiagnosticSelect
}: {
  activeBuild: BuildResult | null;
  buildHistory: BuildResult[];
  buildLog: string;
  buildCommand: RepositoryBuildCommand;
  buildTargetPath: string;
  busy: boolean;
  canBuild: boolean;
  capabilities: ExtensionBuilderCapabilities;
  onBuildCommandChange(value: RepositoryBuildCommand): void;
  onBuildTargetPathChange(value: string): void;
  onSubmitBuild(): void;
  onSelectBuild(build: BuildResult): void;
  onDiagnosticSelect(diagnostic: BuildDiagnostic): void;
}) {
  return (
    <div className="modules-inspector-section">
      <h4>Run</h4>
      <label>
        <span>Command</span>
        <select aria-label="Build command" value={buildCommand} disabled={busy || !capabilities.canBuild} onChange={event => onBuildCommandChange(event.target.value as RepositoryBuildCommand)}>
          <option value="Restore">Restore</option>
          <option value="Build">Build</option>
          <option value="Test">Test</option>
          <option value="Pack">Pack</option>
        </select>
      </label>
      <label>
        <span>Target path</span>
        <input aria-label="Build target path" placeholder="Repository solution or project path" value={buildTargetPath} disabled={busy || !capabilities.canBuild} onChange={event => onBuildTargetPathChange(event.target.value)} />
      </label>
      <button type="button" className="studio-button" disabled={busy || !canBuild} title={!capabilities.canBuild ? "Requires canBuild" : undefined} onClick={onSubmitBuild}>
        <Play size={15} />
        Run command
      </button>
      <h4>Build status</h4>
      {activeBuild ? (
        <dl className="modules-metadata">
          <div><dt>Build</dt><dd>{activeBuild.id}</dd></div>
          <div><dt>Status</dt><dd><StatusChip tone={buildTone(activeBuild.status)}>{activeBuild.status}</StatusChip></dd></div>
          <div><dt>Started</dt><dd>{formatDate(activeBuild.startedAt)}</dd></div>
          <div><dt>Finished</dt><dd>{formatDate(activeBuild.finishedAt)}</dd></div>
        </dl>
      ) : <p className="modules-muted">No build has been selected.</p>}
      <h4>Package outputs</h4>
      <div className="modules-list">
        {(activeBuild?.artifacts ?? []).length === 0 ? <p className="modules-muted">No package artifacts reported.</p> : null}
        {activeBuild?.artifacts.map(artifact => (
          <div key={artifact.id} className="modules-list-row">
            <span>
              <strong>{artifact.packageId} {artifact.version}</strong>
              <small>{artifact.fileName ?? artifact.id}{artifact.branch ? ` · ${artifact.branch}` : ""}{artifact.sourceRevisionId ? ` · ${artifact.sourceRevisionId.slice(0, 8)}` : ""}</small>
            </span>
            <StatusChip tone="accent">{formatArtifactSize(artifact.size)}</StatusChip>
          </div>
        ))}
      </div>
      <h4>Diagnostics</h4>
      <div className="modules-diagnostics-list">
        {(activeBuild?.diagnostics ?? []).length === 0 ? <p className="modules-muted">No diagnostics reported.</p> : null}
        {activeBuild?.diagnostics.map((diagnostic, index) => (
          <button key={`${diagnostic.message}-${index}`} type="button" className="modules-diagnostic-row extension-builder-diagnostic-button" onClick={() => onDiagnosticSelect(diagnostic)}>
            <StatusChip tone={diagnosticTone(diagnostic.severity)}>{diagnostic.severity}</StatusChip>
            <span>{diagnostic.message}<small>{formatDiagnosticLocation(diagnostic)}</small></span>
          </button>
        ))}
      </div>
      <h4>Log</h4>
      <pre className="modules-manifest-code extension-builder-build-log">{buildLog || "Build log will appear here."}</pre>
      <h4>History</h4>
      <div className="modules-list">
        {buildHistory.length === 0 ? <p className="modules-muted">No build history reported for this project.</p> : null}
        {buildHistory.map(build => (
          <button key={build.id} type="button" className="modules-list-row extension-builder-history-row" onClick={() => onSelectBuild(build)}>
            <span>
              <strong>{build.id}</strong>
              <small>{formatDate(build.finishedAt ?? build.startedAt)}</small>
            </span>
            <StatusChip tone={buildTone(build.status)}>{build.status}</StatusChip>
          </button>
        ))}
      </div>
    </div>
  );
}

function SourceControlPanel({
  status,
  diff,
  commitMessage,
  busy,
  onSelectDiff,
  onStageFile,
  onUnstageFile,
  onStageAll,
  onCommitMessageChange,
  onCommit,
  onPush,
  onPull
}: {
  status: SourceControlStatus | null;
  diff: SourceControlDiff | null;
  commitMessage: string;
  busy: boolean;
  onSelectDiff(path: string, staged: boolean): void;
  onStageFile(path: string): void;
  onUnstageFile(path: string): void;
  onStageAll(): void;
  onCommitMessageChange(value: string): void;
  onCommit(): void;
  onPush(): void;
  onPull(): void;
}) {
  const stagedFiles = status?.stagedFiles ?? [];
  const unstagedFiles = status?.unstagedFiles ?? [];
  const hasBranch = !!status?.activeBranch;
  return (
    <div className="extension-builder-source-control">
      <div className="modules-inspector-section">
        <h4>Working copy</h4>
        <p><code>{status?.activeBranch ?? "No branch"}</code></p>
        <StatusChip tone={status?.isDirty ? "warning" : "success"}>{status?.isDirty ? "dirty" : "clean"}</StatusChip>
      </div>

      <div className="extension-builder-source-actions">
        <button type="button" className="studio-button" disabled={busy || unstagedFiles.length === 0} onClick={onStageAll}>
          <Save size={14} />
          Stage all
        </button>
        <button type="button" className="studio-button" disabled={busy || !hasBranch || !!status?.isDirty} title={status?.isDirty ? "Commit or discard changes before pushing" : undefined} onClick={onPush}>
          <GitBranch size={14} />
          Push
        </button>
        <button type="button" className="studio-button" disabled={busy || !hasBranch || !!status?.isDirty} title={status?.isDirty ? "Commit or discard changes before pulling" : undefined} onClick={onPull}>
          <RefreshCcw size={14} />
          Pull
        </button>
      </div>

      <SourceControlFileList
        title="Staged"
        files={stagedFiles}
        empty="No staged changes."
        busy={busy}
        actionLabel="Unstage"
        onAction={onUnstageFile}
        onDiff={path => onSelectDiff(path, true)}
      />

      <SourceControlFileList
        title="Unstaged"
        files={unstagedFiles}
        empty="No unstaged changes."
        busy={busy}
        actionLabel="Stage"
        onAction={onStageFile}
        onDiff={path => onSelectDiff(path, false)}
      />

      <div className="modules-inspector-section">
        <h4>Commit</h4>
        <textarea
          aria-label="Commit message"
          className="extension-builder-commit-message"
          value={commitMessage}
          disabled={busy}
          onChange={event => onCommitMessageChange(event.target.value)}
        />
        <button type="button" className="studio-button" disabled={busy || stagedFiles.length === 0 || !commitMessage.trim()} onClick={onCommit}>
          <GitBranch size={14} />
          Commit staged
        </button>
      </div>

      <div className="modules-inspector-section">
        <h4>Diff</h4>
        {diff ? (
          <>
            <p><code>{diff.path}</code>{diff.isStaged ? " staged" : ""}</p>
            <pre className="extension-builder-diff">{diff.patch || "No diff available."}</pre>
          </>
        ) : (
          <p className="modules-muted">Select a changed file to inspect its diff.</p>
        )}
      </div>
    </div>
  );
}

function SourceControlFileList({
  title,
  files,
  empty,
  busy,
  actionLabel,
  onAction,
  onDiff
}: {
  title: string;
  files: SourceControlStatus["changedFiles"];
  empty: string;
  busy: boolean;
  actionLabel: string;
  onAction(path: string): void;
  onDiff(path: string): void;
}) {
  return (
    <div className="modules-inspector-section extension-builder-source-list">
      <h4>{title}</h4>
      {files.length === 0 ? <p className="modules-muted">{empty}</p> : null}
      {files.map(file => (
        <div key={`${title}-${file.path}`} className="extension-builder-source-row">
          <button type="button" onClick={() => onDiff(file.path)}>
            <code>{file.path}</code>
            <StatusChip tone="neutral">{file.status || "changed"}</StatusChip>
          </button>
          <button type="button" className="studio-button" disabled={busy} onClick={() => onAction(file.path)}>{actionLabel}</button>
        </div>
      ))}
    </div>
  );
}

function PromotePanel({
  capabilities,
  activeBuild,
  artifact,
  promotionResult,
  busy,
  canPromote,
  isBuildCurrent,
  onPromote
}: {
  capabilities: ExtensionBuilderCapabilities;
  activeBuild: BuildResult | null;
  artifact: BuildArtifact | null;
  promotionResult: PackagePromotionResult | null;
  busy: boolean;
  canPromote: boolean;
  isBuildCurrent: boolean;
  onPromote(): void;
}) {
  const promoteReason = !capabilities.canPromote
    ? "Requires canPromote"
    : !isBuildSucceeded(activeBuild)
      ? "A succeeded build is required"
      : !artifact
        ? "A build artifact is required"
        : !isBuildCurrent
          ? "Build artifact is stale. Rebuild the current source revision before promoting."
          : undefined;

  return (
    <div className="modules-inspector-section">
      <h4>Artifact</h4>
      {artifact ? (
        <dl className="modules-metadata">
          <div><dt>Package</dt><dd>{artifact.packageId}</dd></div>
          <div><dt>Version</dt><dd>{artifact.version}</dd></div>
          <div><dt>File</dt><dd>{artifact.fileName ?? artifact.id}</dd></div>
          <div><dt>Size</dt><dd>{artifact.size ? `${artifact.size} B` : "n/a"}</dd></div>
        </dl>
      ) : <p className="modules-muted">Promote becomes available after a successful build produces a package artifact.</p>}
      <button type="button" className="studio-button" disabled={busy || !canPromote} title={promoteReason} onClick={onPromote}>
        <PackageCheck size={15} />
        Promote build
      </button>
      {promotionResult ? (
        <StudioAlert tone={promotionResult.accepted ? "success" : "warning"}>
          {promotionMessage(promotionResult)}
        </StudioAlert>
      ) : null}
    </div>
  );
}

function RuntimePanel({
  capabilities,
  runtimeStatus,
  busy,
  onRefreshRuntime,
  onRetryReconciliation,
  onRollback
}: {
  capabilities: ExtensionBuilderCapabilities;
  runtimeStatus: ExtensionRuntimeStatus | null;
  busy: boolean;
  onRefreshRuntime(): void;
  onRetryReconciliation(): void;
  onRollback(version: string): void;
}) {
  return (
    <div className="modules-inspector-section">
      <div className="extension-builder-runtime-heading">
        <h4>Runtime status</h4>
        <button type="button" className="studio-button" disabled={busy} onClick={onRefreshRuntime}>
          <RefreshCcw size={15} />
          Refresh
        </button>
      </div>
      {!runtimeStatus ? <p className="modules-muted">Runtime status is not available for this project yet.</p> : null}
      {runtimeStatus ? (
        <>
          <dl className="modules-metadata">
            <div><dt>Package</dt><dd>{runtimeStatus.packageId}</dd></div>
            <div><dt>Version</dt><dd>{runtimeStatus.version ?? "n/a"}</dd></div>
            <div><dt>State</dt><dd><StatusChip tone={runtimeTone(runtimeStatus.state)}>{runtimeStatus.state}</StatusChip></dd></div>
            <div><dt>Features</dt><dd>{runtimeStatus.features.length}</dd></div>
          </dl>
          <StudioAlert tone={runtimeMessageTone(runtimeStatus.state)}>{runtimeMessage(runtimeStatus)}</StudioAlert>
          <h4>Contributions</h4>
          <div className="modules-contribution-list">
            {runtimeStatus.features.length === 0 ? <p className="modules-muted">This package contributed no runtime capabilities or activities.</p> : null}
            {runtimeStatus.features.map(feature => (
              <div key={feature.id} className="modules-contribution-row">
                <span>{feature.label}</span>
                <code>{feature.type ?? feature.id}</code>
              </div>
            ))}
          </div>
          <h4>Recovery</h4>
          <div className="modules-operation-grid">
            <button type="button" className="studio-button" disabled={busy || runtimeStatus.state !== "FailedReconciliation"} onClick={onRetryReconciliation}>
              <RefreshCcw size={15} />
              Retry reconciliation
            </button>
          </div>
          <h4>Version history</h4>
          <div className="modules-list">
            {runtimeStatus.history.length === 0 ? <p className="modules-muted">No rollback history reported.</p> : null}
            {runtimeStatus.history.map(version => (
              <RuntimeVersionRow
                key={version.version}
                version={version}
                busy={busy}
                canRollback={capabilities.canRollback}
                currentVersion={runtimeStatus.version}
                onRollback={onRollback}
              />
            ))}
          </div>
          {runtimeStatus.diagnostics.length > 0 ? (
            <>
              <h4>Runtime diagnostics</h4>
              <div className="modules-diagnostics-list">
                {runtimeStatus.diagnostics.map((diagnostic, index) => (
                  <div key={`${diagnostic.message}-${index}`} className="modules-diagnostic-row">
                    <StatusChip tone={diagnosticTone(diagnostic.severity ?? "warning")}>{diagnostic.severity ?? "warning"}</StatusChip>
                    <span>{diagnostic.message}<small>{diagnostic.source ?? "runtime"}</small></span>
                  </div>
                ))}
              </div>
            </>
          ) : null}
        </>
      ) : null}
    </div>
  );
}

function RuntimeVersionRow({
  version,
  busy,
  canRollback,
  currentVersion,
  onRollback
}: {
  version: RuntimeVersion;
  busy: boolean;
  canRollback: boolean;
  currentVersion?: string | null;
  onRollback(version: string): void;
}) {
  const disabled = busy || !canRollback || !version.available || version.version === currentVersion;
  return (
    <div className="modules-list-row">
      <span>
        <strong>{version.version}</strong>
        <small>{version.available ? formatDate(version.promotedAt) : "Rollback target unavailable"}</small>
      </span>
      <button type="button" className="studio-button" disabled={disabled} title={!canRollback ? "Requires canRollback" : !version.available ? "Rollback target is unavailable" : undefined} onClick={() => onRollback(version.version)}>
        <RotateCcw size={15} />
        Rollback
      </button>
    </div>
  );
}

function SummaryItem({ label, value }: { label: string; value: React.ReactNode }) {
  return <div className="modules-summary-item"><strong>{value}</strong><span>{label}</span></div>;
}

function patchProject(workspaces: ExtensionWorkspace[], workspaceId: string, project: ExtensionProject) {
  return workspaces.map(workspace => workspace.id === workspaceId
    ? { ...workspace, projects: workspace.projects.map(item => item.id === project.id ? project : item) }
    : workspace);
}

function isCurrentSelection(selected: { workspaceId: string; projectId: string }, workspaceId: string, projectId: string) {
  return selected.workspaceId === workspaceId && selected.projectId === projectId;
}

function canApplyProjectState(
  mounted: boolean,
  requestId: number,
  currentRequestId: number,
  selected: { workspaceId: string; projectId: string },
  workspaceId: string,
  projectId: string
) {
  return mounted && requestId === currentRequestId && isCurrentSelection(selected, workspaceId, projectId);
}

function upsertFile<T extends { path: string }>(files: T[], file: T) {
  return files.some(item => item.path === file.path) ? files.map(item => item.path === file.path ? file : item) : [...files, file];
}

function upsertEditorTab(tabs: EditorTab[], tab: EditorTab) {
  return tabs.some(item => item.path === tab.path) ? tabs.map(item => item.path === tab.path ? tab : item) : [...tabs, tab];
}

function mergeBuildHistory(builds: BuildResult[], build: BuildResult) {
  return [build, ...builds.filter(item => item.id !== build.id)];
}

function validateProjectDraft(draft: ProjectDraft) {
  if (!draft.templateId) return "Select a project template.";
  if (!draft.name.trim()) return "Project name is required.";
  if (!/^[A-Za-z][A-Za-z0-9_.-]*$/.test(draft.packageId.trim())) return "Package id must start with a letter and contain only letters, numbers, dots, underscores, or hyphens.";
  if (!/^\d+\.\d+\.\d+([-.][A-Za-z0-9.-]+)?$/.test(draft.packageVersion.trim())) return "Package version must use a SemVer-like format such as 1.0.0.";
  return null;
}

function applyTemplateDefaults(draft: ProjectDraft, template?: ExtensionTemplate) {
  if (!template) return draft;
  return {
    ...draft,
    name: draft.name || defaultProjectName(template),
    packageId: draft.packageId || defaultPackageId(template),
    packageVersion: draft.packageVersion || template.defaultPackageVersion || "1.0.0"
  };
}

function createTemplateApplicationDraft(template: ExtensionTemplate, current?: TemplateApplicationDraft): TemplateApplicationDraft {
  return {
    templateId: template.id,
    targetPath: current?.targetPath ?? defaultTemplateTargetPath(template),
    parameters: Object.fromEntries(template.parameters.map(parameter => [
      parameter.name,
      current?.parameters[parameter.name] ?? parameter.defaultValue ?? defaultTemplateParameterValue(parameter, template)
    ]))
  };
}

function collectTemplateParameters(template: ExtensionTemplate, draft: TemplateApplicationDraft) {
  const parameters = Object.fromEntries(template.parameters.map(parameter => [
    parameter.name,
    (draft.parameters[parameter.name] ?? parameter.defaultValue ?? "").trim()
  ]));
  const missing = template.parameters.find(parameter => parameter.required && !parameters[parameter.name]);
  if (missing) return `${missing.displayName} is required.`;
  return parameters;
}

function defaultTemplateTargetPath(template: ExtensionTemplate | null) {
  if (!template) return "";
  if (template.scope === "Project") return `src/${defaultProjectName(template)}`;
  if (template.scope === "Item") return "src";
  if (template.scope === "Solution") return "solutions";
  return "";
}

function defaultTemplateParameterValue(parameter: ExtensionTemplateParameter, template: ExtensionTemplate) {
  if (parameter.name === "name") return template.scope === "Item" ? "NewClass" : defaultProjectName(template);
  if (parameter.name === "packageId") return defaultPackageId(template);
  if (parameter.name === "packageVersion") return template.defaultPackageVersion ?? "1.0.0";
  if (parameter.name === "targetFramework") return template.defaultTargetFramework ?? "net10.0";
  return "";
}

function defaultProjectName(template: ExtensionTemplate) {
  return /generic/i.test(`${template.name} ${template.id}`) ? "GenericExtension" : "ElsaActivityExtension";
}

function defaultPackageId(template: ExtensionTemplate) {
  if (template.defaultPackageId) return template.defaultPackageId;
  return /generic/i.test(`${template.name} ${template.id}`) ? "Company.Extensions.Generic" : "Company.Extensions.ElsaActivity";
}

function fileSortKey(file: RepositoryFileSummary) {
  return `${file.type === "folder" ? "0" : "1"}:${file.path}`;
}

function isBuildRunning(build: BuildResult | null) {
  return build?.status === "queued" || build?.status === "running" || build?.status === "Pending" || build?.status === "Running";
}

function isBuildForCurrentRevision(build: BuildResult | null, project: ExtensionProject | null) {
  if (!build || !project || !isBuildSucceeded(build)) return false;
  return !project.currentRevision || build.revision === project.currentRevision;
}

function isBuildSucceeded(build: BuildResult | null) {
  return build?.status === "succeeded" || build?.status === "Succeeded";
}

function isProtectedBranchName(value: string) {
  return value.toLowerCase() === "main" || value.toLowerCase() === "master";
}

function getExtensionBuilderSessionId() {
  const key = "elsa-extension-builder-session-id";
  try {
    const existing = window.sessionStorage.getItem(key);
    if (existing) return existing;
    const created = `studio-${crypto.randomUUID()}`;
    window.sessionStorage.setItem(key, created);
    return created;
  } catch {
    return `studio-${Math.random().toString(36).slice(2)}`;
  }
}

function formatRemoteState(value?: string | null) {
  if (!value) return "unknown remote";
  return value
    .split(/[-_\s]+/)
    .filter(Boolean)
    .join(" ");
}

function buildTone(status?: string | null): StudioStatusTone {
  if (status === "succeeded" || status === "Succeeded") return "success";
  if (status === "failed" || status === "Failed") return "danger";
  if (status === "queued" || status === "running" || status === "Pending" || status === "Running") return "warning";
  return "neutral";
}

function runtimeTone(status?: string | null): StudioStatusTone {
  if (status === "Loaded") return "success";
  if (status === "PendingRestart") return "warning";
  if (status === "FailedReconciliation") return "danger";
  return "neutral";
}

function diagnosticTone(severity?: string | null): StudioStatusTone {
  if (severity === "error" || severity === "Error") return "danger";
  if (severity === "warning" || severity === "Warning") return "warning";
  if (severity === "info" || severity === "Info") return "accent";
  return "neutral";
}

function runtimeMessageTone(state: string): "success" | "warning" | "danger" | "info" {
  if (state === "Loaded") return "success";
  if (state === "PendingRestart") return "warning";
  if (state === "FailedReconciliation") return "danger";
  return "info";
}

function runtimeMessage(status: ExtensionRuntimeStatus) {
  if (status.state === "Loaded") return `${status.packageId} ${status.version ?? ""} is loaded at runtime.`;
  if (status.state === "PendingRestart") return `${status.packageId} ${status.version ?? ""} is staged and requires a runtime restart before it is loaded.`;
  if (status.state === "FailedReconciliation") return `${status.packageId} ${status.version ?? ""} failed reconciliation. Retry reconciliation or roll back to an available prior version.`;
  return `${status.packageId} is ${status.state}.`;
}

function promotionMessage(result: PackagePromotionResult) {
  if (result.accepted) return result.message ?? "Package validation passed and promotion was accepted.";
  if (result.category === "Duplicate") return result.message ?? "Duplicate package id and version rejected. Bump the version before promoting; existing packages are never silently overwritten.";
  if (result.category === "InvalidManifest") return result.message ?? "Package manifest validation failed. Fix the manifest metadata and rebuild.";
  if (result.category === "DependencyPolicy") return result.message ?? "Package dependencies violate policy. Review the rejected dependency and rebuild with an allowed version.";
  if (result.category === "MalformedPackage") return result.message ?? "The package artifact is malformed or corrupt. Rebuild the project before promoting.";
  return result.message ?? "Promotion was rejected by server-side validation.";
}

function formatDiagnosticLocation(diagnostic: BuildDiagnostic) {
  const path = diagnostic.filePath ?? "project";
  const line = diagnostic.line ? `:${diagnostic.line}` : "";
  const column = diagnostic.column ? `:${diagnostic.column}` : "";
  return `${path}${line}${column}`;
}

function formatDate(value?: string | null) {
  if (!value) return "n/a";
  return new Date(value).toLocaleString();
}

function formatArtifactSize(size?: number | null) {
  if (!size) return "0 B";
  if (size < 1024) return `${size} B`;
  return `${Math.round(size / 1024)} KB`;
}
