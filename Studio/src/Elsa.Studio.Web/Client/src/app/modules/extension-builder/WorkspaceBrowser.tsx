import { FolderGit2, FolderPlus, GitBranch, PackageCheck, Trash2 } from "lucide-react";
import { StatusChip } from "../../ui";
import type {
  ExtensionBuilderCapabilities,
  ExtensionRepositorySummary,
  ExtensionTemplate,
  ExtensionWorkspace
} from "../extensionBuilderApi";
import { applyTemplateDefaults, formatRemoteState, runtimeTone } from "./helpers";
import type { ProjectDraft } from "./types";

export function WorkspaceBrowser({
  capabilities,
  advanced,
  templates,
  extensionName,
  onExtensionNameChange,
  onCreateExtension,
  repositories,
  workspaces,
  selectedWorkspaceId,
  selectedProjectId,
  workspaceName,
  workingBranchName,
  allowProtectedBranchEdit,
  serverLocalPath,
  serverLocalName,
  cloneRepositoryUrl,
  cloneRepositoryName,
  onServerLocalPathChange,
  onServerLocalNameChange,
  onAttachServerLocalRepository,
  onCloneRepositoryUrlChange,
  onCloneRepositoryNameChange,
  onCloneRepository,
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
  advanced: boolean;
  templates: ExtensionTemplate[];
  extensionName: string;
  onExtensionNameChange(value: string): void;
  onCreateExtension(): void;
  repositories: ExtensionRepositorySummary[];
  workspaces: ExtensionWorkspace[];
  selectedWorkspaceId: string;
  selectedProjectId: string;
  workspaceName: string;
  workingBranchName: string;
  allowProtectedBranchEdit: boolean;
  serverLocalPath: string;
  serverLocalName: string;
  cloneRepositoryUrl: string;
  cloneRepositoryName: string;
  onServerLocalPathChange(value: string): void;
  onServerLocalNameChange(value: string): void;
  onAttachServerLocalRepository(): void;
  onCloneRepositoryUrlChange(value: string): void;
  onCloneRepositoryNameChange(value: string): void;
  onCloneRepository(): void;
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
        <span>{advanced ? "Repositories" : "Extensions"}</span>
        <strong>{repositoryRows.length}</strong>
      </div>
      {advanced ? (
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
            <span>Server path</span>
            <input aria-label="Server-local repository path" value={serverLocalPath} disabled={busy || !canCreate} onChange={event => onServerLocalPathChange(event.target.value)} />
          </label>
          <label>
            <span>Server name</span>
            <input aria-label="Server-local repository name" value={serverLocalName} disabled={busy || !canCreate} onChange={event => onServerLocalNameChange(event.target.value)} />
          </label>
          <button type="button" className="studio-button" disabled={busy || !canCreate || !serverLocalPath.trim()} title={canCreate ? "Attach server-local repository" : "Requires canCreateWorkspace"} onClick={onAttachServerLocalRepository}>
            <FolderPlus size={15} />
            Attach server-local
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
      ) : (
        <div className="extension-builder-create extension-builder-new-extension">
          <label>
            <span>Extension name</span>
            <input aria-label="Extension name" placeholder="Acme Orders" value={extensionName} disabled={busy || !canCreate} onChange={event => onExtensionNameChange(event.target.value)} onKeyDown={event => { if (event.key === "Enter" && extensionName.trim()) onCreateExtension(); }} />
          </label>
          <button type="button" className="studio-button studio-button-primary" disabled={busy || !canCreate || !extensionName.trim()} title={canCreate ? "Create a new extension" : "Requires canCreateWorkspace"} onClick={onCreateExtension}>
            <FolderPlus size={15} />
            New extension
          </button>
        </div>
      )}
      {repositoryRows.map(repository => (
        <button key={repository.id} type="button" className={repository.id === selectedWorkspaceId ? "modules-source-button active" : "modules-source-button"} onClick={() => onSelectWorkspace(repository.id)}>
          <span>
            <strong>{repository.name}</strong>
            <small>{repository.owner ?? "current owner"} · {repository.projectCount} project(s)</small>
            {advanced ? <small>{repository.activeBranch ?? "no branch"} · {formatRemoteState(repository.remoteState)}{repository.isDirty ? " · dirty" : ""}</small> : null}
          </span>
          {advanced ? <em>{repository.attentionCount}</em> : null}
        </button>
      ))}

      {advanced && selectedRepository ? (
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

      {advanced && selectedWorkspace ? (
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
