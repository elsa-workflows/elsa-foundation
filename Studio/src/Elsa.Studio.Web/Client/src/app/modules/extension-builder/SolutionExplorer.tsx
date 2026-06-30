import { FilePlus2, GitBranch, PackageCheck, Pencil, RefreshCcw, Trash2 } from "lucide-react";
import { StatusChip } from "../../ui";
import type {
  ExtensionBuilderCapabilities,
  ExtensionProject,
  ExtensionTemplate,
  ExtensionWorkspace,
  RepositoryFileSummary,
  RepositorySolutionSummary
} from "../extensionBuilderApi";
import { applyTemplateDefaults, createTemplateApplicationDraft, defaultTemplateTargetPath, runtimeTone } from "./helpers";
import type { ProjectDraft, TemplateApplicationDraft } from "./types";

export function SolutionExplorer({
  capabilities,
  advanced,
  workspace,
  project,
  templates,
  templateDraft,
  files,
  solutions,
  selectedSolutionPath,
  selectedProjectId,
  newFilePath,
  busy,
  projectDraft,
  workingBranchName,
  allowProtectedBranchEdit,
  activeFilePath,
  onRefresh,
  onSelectProject,
  onTemplateDraftChange,
  onApplyTemplate,
  onSelectSolution,
  onNewFilePathChange,
  onCreateFile,
  onDeleteFile,
  onRenameFile,
  onOpenFile,
  onProjectDraftChange,
  onCreateProject,
  onDeleteProject,
  onDeleteWorkspace,
  onWorkingBranchNameChange,
  onAllowProtectedBranchEditChange,
  onSelectWorkingCopy
}: {
  capabilities: ExtensionBuilderCapabilities;
  advanced: boolean;
  workspace: ExtensionWorkspace;
  project: ExtensionProject | null;
  templates: ExtensionTemplate[];
  templateDraft: TemplateApplicationDraft;
  files: RepositoryFileSummary[];
  solutions: RepositorySolutionSummary[];
  selectedSolutionPath: string;
  selectedProjectId: string;
  newFilePath: string;
  busy: boolean;
  projectDraft: ProjectDraft;
  workingBranchName: string;
  allowProtectedBranchEdit: boolean;
  activeFilePath: string;
  onRefresh(): void;
  onSelectProject(projectId: string): void;
  onTemplateDraftChange(value: TemplateApplicationDraft): void;
  onApplyTemplate(): void;
  onSelectSolution(path: string): void;
  onNewFilePathChange(value: string): void;
  onCreateFile(): void;
  onDeleteFile(path: string): void;
  onRenameFile(path: string): void;
  onOpenFile(path: string): void;
  onProjectDraftChange(value: ProjectDraft): void;
  onCreateProject(): void;
  onDeleteProject(): void;
  onDeleteWorkspace(): void;
  onWorkingBranchNameChange(value: string): void;
  onAllowProtectedBranchEditChange(value: boolean): void;
  onSelectWorkingCopy(): void;
}) {
  const canEdit = capabilities.canEditFiles;
  const canCreate = capabilities.canCreateWorkspace;
  const availableTemplates = advanced ? templates : templates.filter(template => template.scope === "Item");
  const selectedTemplate = availableTemplates.find(template => template.id === templateDraft.templateId) ?? availableTemplates[0] ?? null;
  const projects = workspace.projects ?? [];
  const fileName = (path: string) => path.split("/").pop() || path;

  return (
    <aside className="extension-builder-explorer" aria-label="Solution explorer">
      <div className="extension-builder-explorer-head">
        <span>Explorer</span>
        <button type="button" className="studio-icon-button" disabled={busy} title="Refresh solution" aria-label="Refresh solution" onClick={onRefresh}>
          <RefreshCcw size={13} />
        </button>
      </div>

      <div className="extension-builder-explorer-scroll">
        {projects.length > 1 ? (
          <div className="extension-builder-explorer-section">
            <span className="extension-builder-explorer-label">Projects</span>
            <div className="extension-builder-project-switch">
              {projects.map(item => (
                <button key={item.id} type="button" className={item.id === selectedProjectId ? "active" : ""} onClick={() => onSelectProject(item.id)}>
                  <span>{item.name}</span>
                  <StatusChip tone={runtimeTone(item.runtimeStatus)}>{item.runtimeStatus ?? item.latestBuildStatus ?? "new"}</StatusChip>
                </button>
              ))}
            </div>
          </div>
        ) : null}

        {solutions.length > 1 ? (
          <label className="extension-builder-solution-picker">
            <span>Solution</span>
            <select aria-label="Solution" value={selectedSolutionPath} disabled={busy} onChange={event => onSelectSolution(event.target.value)}>
              <option value="">Select solution</option>
              {solutions.map(solution => <option key={solution.path} value={solution.path}>{solution.name}</option>)}
            </select>
          </label>
        ) : null}

        <div className="extension-builder-file-create">
          <input aria-label="New file path" placeholder="Activities/MyActivity.cs" value={newFilePath} disabled={busy || !canEdit} onChange={event => onNewFilePathChange(event.target.value)} onKeyDown={event => { if (event.key === "Enter" && newFilePath.trim()) onCreateFile(); }} />
          <button type="button" className="studio-icon-button" disabled={busy || !canEdit || !newFilePath.trim()} title={canEdit ? "Create file" : "Requires canEditFiles"} aria-label="Create file" onClick={onCreateFile}>
            <FilePlus2 size={14} />
          </button>
        </div>

        <div className="extension-builder-tree" aria-label="Repository files">
          {files.length === 0 ? <p className="modules-muted">No files in this solution yet.</p> : null}
          {files.map(file => (
            <div key={file.path} className={file.path === activeFilePath ? "extension-builder-file-row active" : "extension-builder-file-row"}>
              <button type="button" disabled={file.type !== "file"} title={file.path} onClick={() => onOpenFile(file.path)}>
                <span className="extension-builder-file-glyph">{file.type === "folder" ? "▸" : file.kind === "Solution" ? "S" : file.kind === "Project" ? "P" : "•"}</span>
                <code>{fileName(file.path)}{file.isDirty ? " ●" : ""}</code>
              </button>
              {file.type === "file" ? (
                <span className="extension-builder-file-actions">
                  <button type="button" className="studio-icon-button" disabled={busy || !canEdit} title={`Rename ${file.path}`} aria-label={`Rename ${file.path}`} onClick={() => onRenameFile(file.path)}>
                    <Pencil size={12} />
                  </button>
                  <button type="button" className="studio-icon-button" disabled={busy || !canEdit} title={`Delete ${file.path}`} aria-label={`Delete ${file.path}`} onClick={() => onDeleteFile(file.path)}>
                    <Trash2 size={12} />
                  </button>
                </span>
              ) : null}
            </div>
          ))}
        </div>

        {availableTemplates.length > 0 ? (
          <details className="extension-builder-explorer-details">
            <summary>{advanced ? "Apply template" : "New file from template"}</summary>
            <label>
              <span>Template</span>
              <select aria-label="Repository template" value={selectedTemplate?.id ?? ""} disabled={busy || !canEdit} onChange={event => {
                const template = availableTemplates.find(item => item.id === event.target.value);
                if (template) onTemplateDraftChange(createTemplateApplicationDraft(template, templateDraft));
              }}>
                {availableTemplates.map(template => <option key={template.id} value={template.id}>{advanced ? `${template.name} · ${template.scope}` : template.name}</option>)}
              </select>
            </label>
            {advanced ? (
              <label>
                <span>Target path</span>
                <input aria-label="Template target path" placeholder={defaultTemplateTargetPath(selectedTemplate)} value={templateDraft.targetPath} disabled={busy || !canEdit} onChange={event => onTemplateDraftChange({ ...templateDraft, targetPath: event.target.value })} />
              </label>
            ) : null}
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

        {advanced ? (
          <details className="extension-builder-explorer-details">
            <summary>Working copy</summary>
            <label>
              <span>Working branch</span>
              <input aria-label="Working branch" value={workingBranchName} disabled={busy || !canEdit} onChange={event => onWorkingBranchNameChange(event.target.value)} />
            </label>
            <label className="extension-builder-checkbox">
              <input type="checkbox" aria-label="Allow default branch editing" checked={allowProtectedBranchEdit} disabled={busy || !canEdit} onChange={event => onAllowProtectedBranchEditChange(event.target.checked)} />
              <span>Allow default branch editing</span>
            </label>
            <button type="button" className="studio-button" disabled={busy || !canEdit || !workingBranchName.trim()} title={canEdit ? "Open working branch" : "Requires canEditFiles"} onClick={onSelectWorkingCopy}>
              <GitBranch size={15} />
              Open working branch
            </button>
          </details>
        ) : null}

        {advanced ? (
          <details className="extension-builder-explorer-details">
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
              <button type="button" className="studio-button" disabled={busy} onClick={onDeleteWorkspace}>
                <Trash2 size={15} />
                Delete solution
              </button>
            </div>
          </details>
        ) : null}
      </div>
    </aside>
  );
}
