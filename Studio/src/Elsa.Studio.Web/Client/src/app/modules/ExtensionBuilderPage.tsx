import React, { useEffect, useMemo, useRef, useState } from "react";
import { Boxes, FilePlus2, FolderGit2, FolderPlus, Hammer, PackageCheck, Play, RefreshCcw, RotateCcw, Save, Trash2 } from "lucide-react";
import type { ElsaStudioModuleApi } from "../../sdk";
import { EmptyState, StatusChip, StudioAlert, StudioTabs, StudioToolbar, StudioToolbarGroup, type StudioStatusTone } from "../ui";
import {
  createProject,
  createWorkspace,
  cloneRepository,
  deleteProject,
  deleteProjectFile,
  deleteWorkspace,
  getBuild,
  getBuildLog,
  getCapabilities,
  getProject,
  getRuntimeStatus,
  isTrusted,
  listProjectFiles,
  listRepositories,
  listTemplates,
  listWorkspaces,
  promoteBuild,
  readProjectFile,
  retryReconciliation,
  rollbackPackage,
  submitBuild,
  writeProjectFile,
  type BuildArtifact,
  type BuildDiagnostic,
  type BuildResult,
  type ExtensionBuilderCapabilities,
  type ExtensionProject,
  type ExtensionRepositorySummary,
  type ExtensionRuntimeStatus,
  type ExtensionTemplate,
  type ExtensionWorkspace,
  type PackagePromotionResult,
  type ProjectFile,
  type RuntimeVersion
} from "./extensionBuilderApi";
import { getErrorMessage } from "./moduleManagementApi";

type BuilderState = "loading" | "ready" | "failed";
type InspectorTab = "build" | "promote" | "runtime";

interface ProjectDraft {
  name: string;
  packageId: string;
  packageVersion: string;
  templateId: string;
}

const inspectorTabs = [
  { id: "build", label: "Build" },
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
  const [files, setFiles] = useState<ProjectFile[]>([]);
  const [activeFilePath, setActiveFilePath] = useState("");
  const [editorText, setEditorText] = useState("");
  const [savedEditorText, setSavedEditorText] = useState("");
  const [lineHint, setLineHint] = useState<string | null>(null);
  const [activeBuild, setActiveBuild] = useState<BuildResult | null>(null);
  const [buildHistory, setBuildHistory] = useState<BuildResult[]>([]);
  const [buildLog, setBuildLog] = useState("");
  const [runtimeStatus, setRuntimeStatus] = useState<ExtensionRuntimeStatus | null>(null);
  const [promotionResult, setPromotionResult] = useState<PackagePromotionResult | null>(null);
  const [activeInspectorTab, setActiveInspectorTab] = useState<InspectorTab>("build");
  const [workspaceName, setWorkspaceName] = useState("Extension workspace");
  const [cloneRepositoryUrl, setCloneRepositoryUrl] = useState("");
  const [cloneRepositoryName, setCloneRepositoryName] = useState("");
  const [projectDraft, setProjectDraft] = useState<ProjectDraft>({ name: "", packageId: "", packageVersion: "", templateId: "" });
  const [newFilePath, setNewFilePath] = useState("");
  const [operationBusy, setOperationBusy] = useState(false);
  const [status, setStatus] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const pollTimerId = useRef<number | null>(null);
  const mounted = useRef(true);
  const projectDetailsRequestId = useRef(0);
  const runtimeStatusRequestId = useRef(0);
  const selectedIds = useRef({ workspaceId: "", projectId: "" });
  const editorDirty = editorText !== savedEditorText;
  const selectedWorkspace = workspaces.find(workspace => workspace.id === selectedWorkspaceId) ?? (!selectedWorkspaceId ? workspaces[0] ?? null : null);
  const selectedRepository = repositories.find(repository => repository.id === selectedWorkspaceId) ?? repositories.find(repository => repository.id === selectedWorkspace?.id) ?? repositories[0] ?? null;
  const selectedProject = selectedWorkspace?.projects.find(project => project.id === selectedProjectId) ?? selectedWorkspace?.projects[0] ?? null;
  selectedIds.current = { workspaceId: selectedWorkspace?.id ?? "", projectId: selectedProject?.id ?? "" };
  const fileRows = useMemo(() => [...files].sort((a, b) => fileSortKey(a).localeCompare(fileSortKey(b))), [files]);
  const hasRepositoryRows = repositories.length > 0 || workspaces.length > 0;
  const latestArtifact = activeBuild?.artifact ?? null;
  const canBuild = !!capabilities?.canBuild && !!selectedProject && !editorDirty && !isBuildRunning(activeBuild);
  const canPromote = !!capabilities?.canPromote && !!latestArtifact && isBuildForCurrentRevision(activeBuild, selectedProject);

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
    if (templates.length === 0 || projectDraft.templateId) return;
    const primary = templates.find(template => template.primary || /elsa/i.test(`${template.name} ${template.id}`)) ?? templates[0];
    setProjectDraft(current => ({
      ...current,
      templateId: primary.id,
      name: current.name || defaultProjectName(primary),
      packageId: current.packageId || defaultPackageId(primary),
      packageVersion: current.packageVersion || primary.defaultPackageVersion || "1.0.0"
    }));
  }, [templates, projectDraft.templateId]);

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
    if (!selectedWorkspace || !selectedProject) {
      projectDetailsRequestId.current += 1;
      setFiles([]);
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

    void loadProjectDetails(selectedWorkspace.id, selectedProject.id);
  }, [selectedWorkspace?.id, selectedProject?.id]);

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

  async function loadProjectDetails(workspaceId: string, projectId: string) {
    const requestId = ++projectDetailsRequestId.current;
    const runtimeRequestId = ++runtimeStatusRequestId.current;
    setError(null);
    try {
      const [project, projectFiles, runtime] = await Promise.all([
        getProject(context, workspaceId, projectId),
        listProjectFiles(context, workspaceId, projectId),
        getRuntimeStatus(context, workspaceId, projectId).catch(() => null)
      ]);
      if (!canApplyProjectState(mounted.current, requestId, projectDetailsRequestId.current, selectedIds.current, workspaceId, projectId)) return;
      setWorkspaces(current => patchProject(current, workspaceId, project));
      setFiles(projectFiles);
      if (runtimeRequestId === runtimeStatusRequestId.current) {
        setRuntimeStatus(runtime);
      }
      setBuildHistory(project.builds ?? []);
      setActiveBuild(project.builds?.[0] ?? null);
      const firstFile = projectFiles.find(file => file.type === "file");
      if (firstFile) {
        const file = await readProjectFile(context, workspaceId, projectId, firstFile.path);
        if (!canApplyProjectState(mounted.current, requestId, projectDetailsRequestId.current, selectedIds.current, workspaceId, projectId)) return;
        const content = file.content ?? "";
        setActiveFilePath(file.path);
        setEditorText(content);
        setSavedEditorText(content);
        setLineHint(null);
      } else {
        setActiveFilePath("");
        setEditorText("");
        setSavedEditorText("");
      }
    } catch (e) {
      setError(getErrorMessage(e));
    }
  }

  async function openFile(workspaceId: string, projectId: string, path: string, options?: { force?: boolean }) {
    if (!options?.force && editorDirty && !window.confirm("Discard unsaved file changes?")) return false;
    setError(null);
    try {
      const file = await readProjectFile(context, workspaceId, projectId, path);
      if (!isCurrentSelection(selectedIds.current, workspaceId, projectId)) return false;
      const content = file.content ?? "";
      setActiveFilePath(file.path);
      setEditorText(content);
      setSavedEditorText(content);
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
    if (project) {
      if (await refreshWorkspacesSafely({ preserveSelection: true })) {
        setSelectedProjectId(project.id);
      }
    }
  }

  async function handleSaveFile() {
    if (!selectedWorkspace || !selectedProject || !activeFilePath) return;
    const workspaceId = selectedWorkspace.id;
    const projectId = selectedProject.id;
    const path = activeFilePath;
    const content = editorText;
    const saved = await runOperation(
      () => writeProjectFile(context, workspaceId, projectId, path, { content }),
      `Saved ${path}.`
    );
    if (saved && isCurrentSelection(selectedIds.current, workspaceId, projectId)) {
      setSavedEditorText(content);
      setFiles(current => upsertFile(current, saved));
      clearSourceDependentState();
      const refreshedProject = await refreshProjectMetadata(workspaceId, projectId);
      if (refreshedProject) setBuildHistory(refreshedProject.builds ?? []);
    }
  }

  async function handleCreateFile() {
    if (!selectedWorkspace || !selectedProject || !newFilePath.trim()) return;
    const workspaceId = selectedWorkspace.id;
    const projectId = selectedProject.id;
    const path = newFilePath.trim();
    const saved = await runOperation(
      () => writeProjectFile(context, workspaceId, projectId, path, { content: "" }),
      `Created ${path}.`
    );
    if (saved && isCurrentSelection(selectedIds.current, workspaceId, projectId)) {
      setNewFilePath("");
      setFiles(current => upsertFile(current, saved));
      clearSourceDependentState();
      const refreshedProject = await refreshProjectMetadata(workspaceId, projectId);
      if (refreshedProject) setBuildHistory(refreshedProject.builds ?? []);
      await openFile(workspaceId, projectId, saved.path);
    }
  }

  async function handleDeleteFile(path: string) {
    if (!selectedWorkspace || !selectedProject || !window.confirm(`Delete ${path}?`)) return;
    const workspaceId = selectedWorkspace.id;
    const projectId = selectedProject.id;
    const deleted = await runOperation(() => deleteProjectFile(context, workspaceId, projectId, path), `Deleted ${path}.`);
    if (!deleted || !isCurrentSelection(selectedIds.current, workspaceId, projectId)) return;
    setFiles(current => current.filter(file => file.path !== path));
    clearSourceDependentState();
    const refreshedProject = await refreshProjectMetadata(workspaceId, projectId);
    if (refreshedProject) setBuildHistory(refreshedProject.builds ?? []);
    if (path === activeFilePath) {
      setActiveFilePath("");
      setEditorText("");
      setSavedEditorText("");
    }
  }

  async function handleSubmitBuild() {
    if (!selectedWorkspace || !selectedProject) return;
    if (editorDirty) {
      setError("Save or discard file changes before building.");
      return;
    }

    const requestedRevision = selectedProject.currentRevision ?? null;
    const build = await runOperation(
      () => submitBuild(context, selectedWorkspace.id, selectedProject.id, { projectId: selectedProject.id, revision: requestedRevision }),
      `Build submitted for ${selectedProject.name}.`
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
    const opened = await openFile(selectedWorkspace.id, selectedProject.id, diagnostic.filePath);
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
          templates={templates}
          repositories={repositories}
          workspaces={workspaces}
          selectedWorkspaceId={selectedRepository?.id ?? selectedWorkspace?.id ?? ""}
          selectedProjectId={selectedProject?.id ?? ""}
          workspaceName={workspaceName}
          cloneRepositoryUrl={cloneRepositoryUrl}
          cloneRepositoryName={cloneRepositoryName}
          projectDraft={projectDraft}
          busy={operationBusy}
          onWorkspaceNameChange={setWorkspaceName}
          onCloneRepositoryUrlChange={setCloneRepositoryUrl}
          onCloneRepositoryNameChange={setCloneRepositoryName}
          onProjectDraftChange={setProjectDraft}
          onCreateWorkspace={handleCreateWorkspace}
          onCloneRepository={handleCloneRepository}
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
          files={fileRows}
          activeFilePath={activeFilePath}
          editorText={editorText}
          editorDirty={editorDirty}
          lineHint={lineHint}
          newFilePath={newFilePath}
          busy={operationBusy}
          diagnostics={activeBuild?.diagnostics ?? []}
          onNewFilePathChange={setNewFilePath}
          onCreateFile={handleCreateFile}
          onDeleteFile={handleDeleteFile}
          onOpenFile={path => selectedWorkspace && selectedProject ? openFile(selectedWorkspace.id, selectedProject.id, path) : undefined}
          onEditorTextChange={setEditorText}
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
          artifact={latestArtifact}
          promotionResult={promotionResult}
          runtimeStatus={runtimeStatus}
          busy={operationBusy}
          canPromote={canPromote}
          onSelectBuild={build => {
            setActiveBuild(build);
            setActiveInspectorTab("build");
            if (selectedWorkspace && selectedProject) void refreshBuild(selectedWorkspace.id, selectedProject.id, build.id, build.revision);
          }}
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
  cloneRepositoryUrl,
  cloneRepositoryName,
  projectDraft,
  busy,
  onWorkspaceNameChange,
  onCloneRepositoryUrlChange,
  onCloneRepositoryNameChange,
  onProjectDraftChange,
  onCreateWorkspace,
  onCloneRepository,
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
  cloneRepositoryUrl: string;
  cloneRepositoryName: string;
  projectDraft: ProjectDraft;
  busy: boolean;
  onWorkspaceNameChange(value: string): void;
  onCloneRepositoryUrlChange(value: string): void;
  onCloneRepositoryNameChange(value: string): void;
  onProjectDraftChange(value: ProjectDraft): void;
  onCreateWorkspace(): void;
  onCloneRepository(): void;
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
        <label>
          <span>Clone URL</span>
          <input aria-label="Clone repository URL" value={cloneRepositoryUrl} disabled={busy || !canCreate} onChange={event => onCloneRepositoryUrlChange(event.target.value)} />
        </label>
        <label>
          <span>Clone name</span>
          <input aria-label="Clone repository name" value={cloneRepositoryName} disabled={busy || !canCreate} onChange={event => onCloneRepositoryNameChange(event.target.value)} />
        </label>
        <button type="button" className="studio-button" disabled={busy || !canCreate || !cloneRepositoryUrl.trim()} title={canCreate ? "Clone from Git" : "Requires canCreateWorkspace"} onClick={onCloneRepository}>
          <FolderGit2 size={15} />
          Clone from Git
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
  files,
  activeFilePath,
  editorText,
  editorDirty,
  lineHint,
  newFilePath,
  busy,
  diagnostics,
  canBuild,
  onNewFilePathChange,
  onCreateFile,
  onDeleteFile,
  onOpenFile,
  onEditorTextChange,
  onSaveFile,
  onSubmitBuild
}: {
  capabilities: ExtensionBuilderCapabilities;
  workspace: ExtensionWorkspace | null;
  project: ExtensionProject | null;
  files: ProjectFile[];
  activeFilePath: string;
  editorText: string;
  editorDirty: boolean;
  lineHint: string | null;
  newFilePath: string;
  busy: boolean;
  diagnostics: BuildDiagnostic[];
  canBuild: boolean;
  onNewFilePathChange(value: string): void;
  onCreateFile(): void;
  onDeleteFile(path: string): void;
  onOpenFile(path: string): void;
  onEditorTextChange(value: string): void;
  onSaveFile(): void;
  onSubmitBuild(): void;
}) {
  const canEdit = capabilities.canEditFiles;

  if (!workspace || !project) {
    return (
      <div className="modules-grid-panel extension-builder-editor">
        <EmptyState icon={<Boxes size={22} />}>Select or create a project to edit source files.</EmptyState>
      </div>
    );
  }

  return (
    <div className="modules-grid-panel extension-builder-editor">
      <div className="modules-grid-heading">
        <div>
          <h3>{project.name}</h3>
          <p><code>{project.packageId}</code> {project.packageVersion} · revision {project.currentRevision ?? "draft"}</p>
        </div>
        <StudioToolbar>
          <StudioToolbarGroup>
            <button type="button" className="studio-button" disabled={busy || !canEdit || !activeFilePath || !editorDirty} title={!canEdit ? "Requires canEditFiles" : undefined} onClick={onSaveFile}>
              <Save size={15} />
              {editorDirty ? "Save" : "Saved"}
            </button>
            <button type="button" className="studio-button" disabled={busy || !canBuild} title={editorDirty ? "Save file changes before building" : !capabilities.canBuild ? "Requires canBuild" : undefined} onClick={onSubmitBuild}>
              <Play size={15} />
              Build
            </button>
          </StudioToolbarGroup>
        </StudioToolbar>
      </div>

      <div className="extension-builder-editor-layout">
        <aside className="extension-builder-file-tree" aria-label="Project files">
          <div className="extension-builder-file-create">
            <input aria-label="New file path" placeholder="src/MyActivity.cs" value={newFilePath} disabled={busy || !canEdit} onChange={event => onNewFilePathChange(event.target.value)} />
            <button type="button" className="studio-icon-button" disabled={busy || !canEdit || !newFilePath.trim()} title={canEdit ? "Create file" : "Requires canEditFiles"} aria-label="Create file" onClick={onCreateFile}>
              <FilePlus2 size={14} />
            </button>
          </div>
          {files.length === 0 ? <p className="modules-muted">No files reported for this project.</p> : null}
          {files.map(file => (
            <div key={file.path} className={file.path === activeFilePath ? "extension-builder-file-row active" : "extension-builder-file-row"}>
              <button type="button" disabled={file.type !== "file"} onClick={() => onOpenFile(file.path)}>
                <span>{file.type === "folder" ? "▸" : "•"}</span>
                <code>{file.path}</code>
              </button>
              {file.type === "file" ? (
                <button type="button" className="studio-icon-button" disabled={busy || !canEdit} title={`Delete ${file.path}`} aria-label={`Delete ${file.path}`} onClick={() => onDeleteFile(file.path)}>
                  <Trash2 size={13} />
                </button>
              ) : null}
            </div>
          ))}
        </aside>

        <div className="extension-builder-code-panel">
          <div className="extension-builder-editor-status">
            <span>{activeFilePath ? <code>{activeFilePath}</code> : "No file selected"}</span>
            {editorDirty ? <StatusChip tone="warning">dirty</StatusChip> : <StatusChip tone="success">saved</StatusChip>}
            {lineHint ? <StatusChip tone="accent">{lineHint}</StatusChip> : null}
          </div>
          <textarea
            aria-label="Project file editor"
            className="extension-builder-code-editor"
            spellCheck={false}
            value={editorText}
            disabled={!canEdit || !activeFilePath}
            onChange={event => onEditorTextChange(event.target.value)}
          />
          {diagnostics.some(diagnostic => diagnostic.filePath === activeFilePath) ? (
            <div className="extension-builder-inline-diagnostics" aria-label="Inline diagnostics">
              {diagnostics.filter(diagnostic => diagnostic.filePath === activeFilePath).map((diagnostic, index) => (
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
  artifact,
  promotionResult,
  runtimeStatus,
  busy,
  canPromote,
  onSelectBuild,
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
  artifact: BuildArtifact | null;
  promotionResult: PackagePromotionResult | null;
  runtimeStatus: ExtensionRuntimeStatus | null;
  busy: boolean;
  canPromote: boolean;
  onSelectBuild(build: BuildResult): void;
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
          onSelectBuild={onSelectBuild}
          onDiagnosticSelect={onDiagnosticSelect}
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
  onSelectBuild,
  onDiagnosticSelect
}: {
  activeBuild: BuildResult | null;
  buildHistory: BuildResult[];
  buildLog: string;
  onSelectBuild(build: BuildResult): void;
  onDiagnosticSelect(diagnostic: BuildDiagnostic): void;
}) {
  return (
    <div className="modules-inspector-section">
      <h4>Build status</h4>
      {activeBuild ? (
        <dl className="modules-metadata">
          <div><dt>Build</dt><dd>{activeBuild.id}</dd></div>
          <div><dt>Status</dt><dd><StatusChip tone={buildTone(activeBuild.status)}>{activeBuild.status}</StatusChip></dd></div>
          <div><dt>Started</dt><dd>{formatDate(activeBuild.startedAt)}</dd></div>
          <div><dt>Finished</dt><dd>{formatDate(activeBuild.finishedAt)}</dd></div>
        </dl>
      ) : <p className="modules-muted">No build has been selected.</p>}
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

function upsertFile(files: ProjectFile[], file: ProjectFile) {
  return files.some(item => item.path === file.path) ? files.map(item => item.path === file.path ? file : item) : [...files, file];
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

function defaultProjectName(template: ExtensionTemplate) {
  return /generic/i.test(`${template.name} ${template.id}`) ? "GenericExtension" : "ElsaActivityExtension";
}

function defaultPackageId(template: ExtensionTemplate) {
  if (template.defaultPackageId) return template.defaultPackageId;
  return /generic/i.test(`${template.name} ${template.id}`) ? "Company.Extensions.Generic" : "Company.Extensions.ElsaActivity";
}

function fileSortKey(file: ProjectFile) {
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

function formatRemoteState(value?: string | null) {
  if (!value) return "unknown remote";
  if (value.includes("://") || value.includes("/") || value.includes("@")) return value;
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
