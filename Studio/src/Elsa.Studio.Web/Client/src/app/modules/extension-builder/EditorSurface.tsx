import { FilePlus2 } from "lucide-react";
import { javaScriptLanguageAdapter, StudioCodeEditor } from "@elsa-workflows/studio-code-editor";
import { EmptyState, StatusChip } from "../../ui";
import type {
  BuildDiagnostic,
  ExtensionProject,
  ExtensionWorkspace
} from "../extensionBuilderApi";
import {
  createEditorDiagnostics,
  createProjectFileDocument,
  diagnosticTone,
  formatDiagnosticLocation,
  isJavaScriptLikeDocument
} from "./helpers";
import type { EditorTab } from "./types";

export function EditorSurface({
  workspace,
  project,
  canEdit,
  editorTabs,
  activeFilePath,
  editorText,
  editorDirty,
  lineHint,
  diagnostics,
  onSelectEditorTab,
  onCloseEditorTab,
  onEditorTextChange
}: {
  workspace: ExtensionWorkspace;
  project: ExtensionProject | null;
  canEdit: boolean;
  editorTabs: EditorTab[];
  activeFilePath: string;
  editorText: string;
  editorDirty: boolean;
  lineHint: string | null;
  diagnostics: BuildDiagnostic[];
  onSelectEditorTab(path: string): void;
  onCloseEditorTab(path: string): void;
  onEditorTextChange(value: string): void;
}) {
  const activeDocument = activeFilePath
    ? createProjectFileDocument(workspace, project, activeFilePath, editorText)
    : null;
  const activeLanguageAdapter = activeDocument && isJavaScriptLikeDocument(activeDocument) ? javaScriptLanguageAdapter : undefined;
  const editorDiagnostics = createEditorDiagnostics(workspace, project, diagnostics);
  const activeFileDiagnostics = diagnostics.filter(diagnostic => diagnostic.filePath === activeFilePath || formatDiagnosticLocation(diagnostic) === lineHint);
  const fileName = (path: string) => path.split("/").pop() || path;

  return (
    <div className="extension-builder-code-panel">
      {editorTabs.length > 0 ? (
        <div className="extension-builder-editor-tabs" role="tablist" aria-label="Open repository files">
          {editorTabs.map(tab => (
            <div key={tab.path} className={tab.path === activeFilePath ? "extension-builder-editor-tab active" : "extension-builder-editor-tab"}>
              <button type="button" role="tab" aria-selected={tab.path === activeFilePath} className="extension-builder-editor-tab-select" title={tab.path} onClick={() => onSelectEditorTab(tab.path)}>
                <span>{fileName(tab.path)}{tab.content !== tab.savedContent ? " ●" : ""}</span>
              </button>
              <button type="button" className="extension-builder-editor-tab-close" aria-label={`Close ${tab.path}`} onClick={() => onCloseEditorTab(tab.path)}>×</button>
            </div>
          ))}
        </div>
      ) : null}
      <div className="extension-builder-editor-status">
        <span>{activeFilePath ? <code>{activeFilePath}</code> : "No file selected"}</span>
        {activeFilePath ? (editorDirty ? <StatusChip tone="warning">unsaved</StatusChip> : <StatusChip tone="success">saved</StatusChip>) : null}
        {lineHint ? <StatusChip tone="accent">{lineHint}</StatusChip> : null}
      </div>
      {activeDocument ? (
        <StudioCodeEditor
          ariaLabel="Project file editor"
          document={activeDocument}
          diagnostics={editorDiagnostics}
          languageAdapter={activeLanguageAdapter}
          minHeight="100%"
          readOnly={!canEdit}
          theme="studio"
          onChange={nextDocument => onEditorTextChange(nextDocument.value)}
        />
      ) : (
        <EmptyState icon={<FilePlus2 size={22} />}>Select a file in the Explorer to start editing.</EmptyState>
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
  );
}
