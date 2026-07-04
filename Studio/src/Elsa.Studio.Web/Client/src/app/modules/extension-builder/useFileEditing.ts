import { deleteRepositoryFile, moveRepositoryFile, readRepositoryFile, writeRepositoryFile } from "../extensionBuilderApi";
import { getErrorMessage } from "../moduleManagementApi";
import { upsertEditorTab, upsertFile } from "./helpers";
import type { BuilderData } from "./useBuilderData";
import type { BuilderCore } from "./store";

// File editing / persistence side of the store: editor text updates, the serialized save queue,
// manual + auto save, the navigation guard, editor-tab lifecycle and file create/delete/rename.
// Extracted verbatim from the monolithic page so behaviour is unchanged.
export interface BuilderFileEditing {
  openFile(workspaceId: string, path: string, options?: { force?: boolean }): Promise<boolean>;
  handleEditorTextChange(value: string): void;
  enqueueSave<T>(operation: () => Promise<T>): Promise<T>;
  persistFile(workspaceId: string, path: string, content: string): Promise<Awaited<ReturnType<typeof writeRepositoryFile>>>;
  autoSaveFile(workspaceId: string, path: string, content: string): Promise<boolean>;
  ensureSavedBeforeLeaving(): Promise<boolean>;
  handleSelectEditorTab(path: string): Promise<void>;
  handleCloseEditorTab(path: string): Promise<void>;
  handleSaveFile(): Promise<void>;
  handleCreateFile(): Promise<void>;
  handleDeleteFile(path: string): Promise<void>;
  handleRenameFile(path: string): Promise<void>;
  clearSourceDependentState(): void;
  confirmDiscard(): Promise<boolean>;
}

export function useFileEditing(core: BuilderCore, data: BuilderData): BuilderFileEditing {
  const { api, context, tracker } = core;
  const { setError } = tracker;

  const confirmDiscard = () =>
    api.dialogs.confirm({ message: "Discard unsaved file changes?", confirmLabel: "Discard", tone: "danger" });

  function clearSourceDependentState() {
    core.setActiveBuild(null);
    core.setBuildLog("");
    core.setPromotionResult(null);
  }

  async function openFile(workspaceId: string, path: string, options?: { force?: boolean }) {
    if (!options?.force && !(await ensureSavedBeforeLeaving())) return false;
    setError(null);
    try {
      const file = await readRepositoryFile(context, workspaceId, path);
      if (core.selectedIds.current.workspaceId !== workspaceId) return false;
      const content = file.content ?? "";
      core.setActiveFilePath(file.path);
      core.setEditorText(content);
      core.setSavedEditorText(content);
      core.setEditorTabs(current => upsertEditorTab(current, { path: file.path, content, savedContent: content }));
      core.setLineHint(null);
      return true;
    } catch (e) {
      setError(getErrorMessage(e));
      return false;
    }
  }

  function handleEditorTextChange(value: string) {
    core.setEditorText(value);
    if (core.activeFilePath) {
      core.setEditorTabs(current => current.map(tab => tab.path === core.activeFilePath ? { ...tab, content: value } : tab));
    }
  }

  // Serialize all file writes (manual Save and auto-save) so they cannot land out of order.
  function enqueueSave<T>(operation: () => Promise<T>): Promise<T> {
    const run = core.saveChain.current.catch(() => {}).then(operation);
    core.saveChain.current = run.catch(() => {});
    return run;
  }

  // Shared persistence: write the file and reconcile local editor/file state. Used by both the
  // explicit Save and auto-save so the two cannot drift. Returns the saved file.
  async function persistFile(workspaceId: string, path: string, content: string) {
    const saved = await writeRepositoryFile(context, workspaceId, path, { content });
    core.lastSavedContent.current.set(`${workspaceId}::${path}`, content);
    if (!core.mounted.current || core.selectedIds.current.workspaceId !== workspaceId) return saved;
    core.setFiles(current => upsertFile(current, saved));
    core.setEditorTabs(current => upsertEditorTab(current, { path: saved.path, content, savedContent: content }));
    if (core.activeFilePathRef.current === path) core.setSavedEditorText(content);
    return saved;
  }

  // Lightweight save used by auto-save: persists without the full tree/source/metadata refresh that
  // the explicit Save performs. Writes are serialized so overlapping saves cannot land out of order,
  // and identical content is not re-sent. Returns true on success (false lets callers block).
  async function autoSaveFile(workspaceId: string, path: string, content: string): Promise<boolean> {
    return enqueueSave(() => doAutoSave(workspaceId, path, content));
  }

  async function doAutoSave(workspaceId: string, path: string, content: string): Promise<boolean> {
    if (core.lastSavedContent.current.get(`${workspaceId}::${path}`) === content) return true;
    const showSaving = core.activeFilePathRef.current === path;
    if (showSaving) core.setAutoSaving(true);
    try {
      await persistFile(workspaceId, path, content);
      // Editing invalidates a prior build, but only for the file the user is actually looking at.
      if (core.mounted.current && core.activeFilePathRef.current === path && core.selectedIds.current.workspaceId === workspaceId) {
        clearSourceDependentState();
      }
      return true;
    } catch (e) {
      if (core.mounted.current) setError(getErrorMessage(e));
      return false;
    } finally {
      if (core.mounted.current && showSaving) core.setAutoSaving(false);
    }
  }

  // Navigation guard: when auto-save is on, flush the active file instead of prompting to discard.
  // If the flush fails, return false so the caller blocks navigation rather than losing the edit.
  async function ensureSavedBeforeLeaving() {
    if (!core.editorDirty) return true;
    if (core.autoSave && core.selectedWorkspace && core.activeFilePath && core.capabilities?.canEditFiles) {
      return autoSaveFile(core.selectedWorkspace.id, core.activeFilePath, core.editorText);
    }
    return confirmDiscard();
  }

  async function handleSelectEditorTab(path: string) {
    const tab = core.editorTabs.find(item => item.path === path);
    if (!tab) return;
    if (core.activeFilePath !== path && !(await ensureSavedBeforeLeaving())) return;
    core.setActiveFilePath(tab.path);
    core.setEditorText(tab.content);
    core.setSavedEditorText(tab.savedContent);
    core.setLineHint(null);
  }

  async function handleCloseEditorTab(path: string) {
    const tab = core.editorTabs.find(item => item.path === path);
    if (!tab) return;
    if (tab.content !== tab.savedContent) {
      if (core.autoSave && core.selectedWorkspace && core.capabilities?.canEditFiles) {
        if (!(await autoSaveFile(core.selectedWorkspace.id, tab.path, tab.content))) return;
      } else if (!(await confirmDiscard())) {
        return;
      }
    }
    const remaining = core.editorTabs.filter(item => item.path !== path);
    core.setEditorTabs(remaining);
    if (core.activeFilePath === path) {
      const next = remaining[0] ?? null;
      core.setActiveFilePath(next?.path ?? "");
      core.setEditorText(next?.content ?? "");
      core.setSavedEditorText(next?.savedContent ?? "");
      core.setLineHint(null);
    }
  }

  async function handleSaveFile() {
    if (!core.selectedWorkspace || !core.activeFilePath) return;
    const workspaceId = core.selectedWorkspace.id;
    const path = core.activeFilePath;
    const content = core.editorText;
    const saved = await tracker.runOperation("save", () => enqueueSave(() => persistFile(workspaceId, path, content)), `Saved ${path}.`);
    if (saved && core.selectedIds.current.workspaceId === workspaceId) {
      clearSourceDependentState();
      await data.loadRepositoryTree(workspaceId, core.selectedSolutionPath || null);
      await data.loadSourceControlStatus(workspaceId);
      const refreshedProject = core.selectedProject ? await data.refreshProjectMetadata(workspaceId, core.selectedProject.id) : null;
      if (refreshedProject) core.setBuildHistory(refreshedProject.builds ?? []);
    }
  }

  async function handleCreateFile() {
    if (!core.selectedWorkspace || !core.newFilePath.trim()) return;
    const workspaceId = core.selectedWorkspace.id;
    const path = core.newFilePath.trim();
    const saved = await tracker.runOperation("explorer", () => writeRepositoryFile(context, workspaceId, path, { content: "" }), `Created ${path}.`);
    if (saved && core.selectedIds.current.workspaceId === workspaceId) {
      core.setNewFilePath("");
      core.setFiles(current => upsertFile(current, saved));
      core.setEditorTabs(current => upsertEditorTab(current, { path: saved.path, content: "", savedContent: "" }));
      clearSourceDependentState();
      await data.loadRepositoryTree(workspaceId, core.selectedSolutionPath || null);
      await data.loadSourceControlStatus(workspaceId);
      const refreshedProject = core.selectedProject ? await data.refreshProjectMetadata(workspaceId, core.selectedProject.id) : null;
      if (refreshedProject) core.setBuildHistory(refreshedProject.builds ?? []);
      await openFile(workspaceId, saved.path);
    }
  }

  async function handleDeleteFile(path: string) {
    if (!core.selectedWorkspace) return;
    if (!(await api.dialogs.confirm({ message: `Delete ${path}?`, confirmLabel: "Delete", tone: "danger" }))) return;
    const workspaceId = core.selectedWorkspace.id;
    const deleted = await tracker.runOperation("explorer", () => deleteRepositoryFile(context, workspaceId, path), `Deleted ${path}.`);
    if (!deleted || core.selectedIds.current.workspaceId !== workspaceId) return;
    core.setFiles(current => current.filter(file => file.path !== path));
    core.setEditorTabs(current => current.filter(tab => tab.path !== path));
    clearSourceDependentState();
    await data.loadRepositoryTree(workspaceId, core.selectedSolutionPath || null);
    await data.loadSourceControlStatus(workspaceId);
    const refreshedProject = core.selectedProject ? await data.refreshProjectMetadata(workspaceId, core.selectedProject.id) : null;
    if (refreshedProject) core.setBuildHistory(refreshedProject.builds ?? []);
    if (path === core.activeFilePath) {
      const nextTab = core.editorTabs.find(tab => tab.path !== path);
      core.setActiveFilePath(nextTab?.path ?? "");
      core.setEditorText(nextTab?.content ?? "");
      core.setSavedEditorText(nextTab?.savedContent ?? "");
    }
  }

  async function handleRenameFile(path: string) {
    if (!core.selectedWorkspace) return;
    const destinationPath = (await api.dialogs.prompt({ title: "Rename file", message: "Enter a new path for the file.", defaultValue: path, confirmLabel: "Rename" }))?.trim();
    if (!destinationPath || destinationPath === path) return;
    const workspaceId = core.selectedWorkspace.id;
    const moved = await tracker.runOperation("explorer", () => moveRepositoryFile(context, workspaceId, path, destinationPath), `Renamed ${path} to ${destinationPath}.`);
    if (!moved || core.selectedIds.current.workspaceId !== workspaceId) return;
    core.setFiles(current => upsertFile(current.filter(file => file.path !== path), moved));
    core.setEditorTabs(current => current.map(tab => tab.path === path ? { ...tab, path: moved.path, content: moved.content ?? tab.content, savedContent: moved.content ?? tab.savedContent } : tab));
    if (core.activeFilePath === path) {
      core.setActiveFilePath(moved.path);
      core.setEditorText(moved.content ?? core.editorText);
      core.setSavedEditorText(moved.content ?? core.savedEditorText);
    }
    clearSourceDependentState();
    await data.loadRepositoryTree(workspaceId, core.selectedSolutionPath || null);
    await data.loadSourceControlStatus(workspaceId);
  }

  return {
    openFile,
    handleEditorTextChange,
    enqueueSave,
    persistFile,
    autoSaveFile,
    ensureSavedBeforeLeaving,
    handleSelectEditorTab,
    handleCloseEditorTab,
    handleSaveFile,
    handleCreateFile,
    handleDeleteFile,
    handleRenameFile,
    clearSourceDependentState,
    confirmDiscard
  };
}
