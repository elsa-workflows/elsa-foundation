import { Boxes, FilePlus2, PackageCheck, Pencil, Play, Save, Trash2 } from "lucide-react";
import { javaScriptLanguageAdapter, StudioCodeEditor } from "@elsa-workflows/studio-code-editor";
import { EmptyState, StatusChip, StudioToolbar, StudioToolbarGroup } from "../../ui";
import type {
  BuildDiagnostic,
  ExtensionBuilderCapabilities,
  ExtensionProject,
  ExtensionTemplate,
  ExtensionWorkspace,
  RepositoryBuildCommand,
  RepositoryFileSummary,
  RepositorySolutionSummary
} from "../extensionBuilderApi";
import {
  createEditorDiagnostics,
  createProjectFileDocument,
  createTemplateApplicationDraft,
  defaultTemplateTargetPath,
  diagnosticTone,
  formatDiagnosticLocation,
  isJavaScriptLikeDocument
} from "./helpers";
import type { EditorTab, TemplateApplicationDraft } from "./types";

export function ProjectWorkspace({
  capabilities,
  advanced,
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
  advanced: boolean;
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
  onSubmitBuild(command?: RepositoryBuildCommand): void;
}) {
  const canEdit = capabilities.canEditFiles;
  const availableTemplates = advanced ? templates : templates.filter(template => template.scope === "Item");
  const selectedTemplate = availableTemplates.find(template => template.id === templateDraft.templateId) ?? availableTemplates[0] ?? null;

  if (!workspace) {
    return (
      <div className="modules-grid-panel extension-builder-editor">
        <EmptyState icon={<Boxes size={22} />}>Select or create a repository to edit source files.</EmptyState>
      </div>
    );
  }

  const activeDocument = activeFilePath
    ? createProjectFileDocument(workspace, project, activeFilePath, editorText)
    : null;
  const activeLanguageAdapter = activeDocument && isJavaScriptLikeDocument(activeDocument) ? javaScriptLanguageAdapter : undefined;
  const editorDiagnostics = createEditorDiagnostics(workspace, project, diagnostics);
  const activeFileDiagnostics = diagnostics.filter(diagnostic => diagnostic.filePath === activeFilePath || formatDiagnosticLocation(diagnostic) === lineHint);

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
            {advanced ? (
              <button type="button" className="studio-button" disabled={busy || !canBuild} title={editorDirty ? "Save file changes before building" : !capabilities.canBuild ? "Requires canBuild" : undefined} onClick={() => onSubmitBuild()}>
                <Play size={15} />
                Build
              </button>
            ) : (
              <button type="button" className="studio-button studio-button-primary" disabled={busy || !canBuild} title={editorDirty ? "Save file changes before packing" : !capabilities.canBuild ? "Requires canBuild" : "Build and pack a NuGet package"} onClick={() => onSubmitBuild("Pack")}>
                <PackageCheck size={15} />
                Pack
              </button>
            )}
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
          {availableTemplates.length > 0 ? (
            <details className="extension-builder-template-apply">
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
          {activeDocument ? (
            <StudioCodeEditor
              ariaLabel="Project file editor"
              document={activeDocument}
              diagnostics={editorDiagnostics}
              languageAdapter={activeLanguageAdapter}
              minHeight="360px"
              readOnly={!canEdit}
              theme="studio"
              onChange={nextDocument => onEditorTextChange(nextDocument.value)}
            />
          ) : (
            <EmptyState icon={<FilePlus2 size={22} />}>Select or create a project file to edit source.</EmptyState>
          )}
          {activeFileDiagnostics.length > 0 ? (
            <div className="extension-builder-inline-diagnostics" aria-label="Inline diagnostics">
              {activeFileDiagnostics.map((diagnostic, index) => (
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
