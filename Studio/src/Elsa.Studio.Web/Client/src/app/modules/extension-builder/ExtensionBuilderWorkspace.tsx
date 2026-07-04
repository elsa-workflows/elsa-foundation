import { ChevronLeft, Hammer, Loader2, PackageCheck, Play, Save, Settings2 } from "lucide-react";
import { EmptyState, StatusChip } from "../../ui";
import { runtimeTone } from "./helpers";
import { BottomDock } from "./BottomDock";
import { EditorSurface } from "./EditorSurface";
import { SolutionExplorer } from "./SolutionExplorer";
import { useExtensionBuilder } from "./ExtensionBuilderContext";

// The focused workspace shell: command bar, explorer + editor, and bottom dock. It reads all state
// and actions from the ExtensionBuilder context instead of receiving them as threaded props.
export function ExtensionBuilderWorkspace() {
  const builder = useExtensionBuilder();
  const {
    capabilities, advanced, selectedWorkspace, selectedRepository, selectedProject, activeFilePath,
    autoSave, canBuild, buildDisabledReason, buildInProgress, isBusy
  } = builder;

  const workspaceLabel = selectedWorkspace?.name ?? selectedRepository?.name ?? "Solution";
  const saveBusy = isBusy("save");
  const explorerBusy = isBusy("explorer");
  // Building is "in progress" from submit until the polled build settles; disable the headline
  // action and swap its icon/label for an active spinner state (mirrors auto-save "Saving…").
  const buildBusy = isBusy("build");

  return (
    <div className="extension-builder-shell">
      <header className="extension-builder-commandbar">
        <div className="extension-builder-commandbar-left">
          <button type="button" className="extension-builder-back" onClick={() => builder.backToHome()}>
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
            <input type="checkbox" aria-label="Auto-save" checked={autoSave} disabled={saveBusy} onChange={event => builder.setAutoSave(event.target.checked)} />
            <span>Auto-save</span>
          </label>
          <button
            type="button"
            className="studio-button"
            disabled={saveBusy || !capabilities!.canEditFiles || !activeFilePath}
            title={!capabilities!.canEditFiles ? "Requires canEditFiles" : autoSave ? "Force save and resync (auto-save also saves as you type)" : undefined}
            onClick={() => builder.handleSaveFile()}
          >
            {saveBusy ? <Loader2 size={15} className="is-spinning" aria-hidden="true" /> : <Save size={15} />}
            {saveBusy ? "Saving…" : "Save"}
          </button>
          {advanced ? (
            <button
              type="button"
              className="studio-button"
              disabled={buildBusy || !canBuild}
              title={buildInProgress ? "Build in progress" : buildDisabledReason ?? undefined}
              onClick={() => builder.handleSubmitBuild()}
            >
              {buildInProgress ? <Loader2 size={15} className="is-spinning" aria-hidden="true" /> : <Play size={15} />}
              {buildInProgress ? "Building…" : "Build"}
            </button>
          ) : (
            <button
              type="button"
              className="studio-button studio-button-primary"
              disabled={buildBusy || !canBuild}
              title={buildInProgress ? "Packing in progress" : buildDisabledReason ?? "Build and pack a NuGet package"}
              onClick={() => builder.handleSubmitBuild("Pack")}
            >
              {buildInProgress ? <Loader2 size={15} className="is-spinning" aria-hidden="true" /> : <PackageCheck size={15} />}
              {buildInProgress ? "Packing…" : "Pack"}
            </button>
          )}
          <button type="button" className="studio-icon-button" aria-label="Build &amp; properties" title="Build & properties" onClick={() => builder.setDockOpen(open => !open)}>
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
              templates={builder.templates}
              templateDraft={builder.templateDraft}
              files={builder.fileRows}
              solutions={builder.solutions}
              selectedSolutionPath={builder.selectedSolutionPath}
              selectedProjectId={selectedProject?.id ?? ""}
              newFilePath={builder.newFilePath}
              busy={explorerBusy}
              projectDraft={builder.projectDraft}
              workingBranchName={builder.workingBranchName || builder.defaultWorkingBranchName}
              allowProtectedBranchEdit={builder.allowProtectedBranchEdit}
              activeFilePath={activeFilePath}
              onRefresh={() => builder.loadProjectDetails(selectedWorkspace.id, selectedProject?.id ?? "", builder.selectedSolutionPath || null)}
              onSelectProject={async projectId => {
                if (!(await builder.ensureSavedBeforeLeaving())) return;
                builder.setSelectedProjectId(projectId);
              }}
              onTemplateDraftChange={builder.setTemplateDraft}
              onApplyTemplate={() => builder.handleApplyTemplate()}
              onSelectSolution={builder.handleSelectSolution}
              onNewFilePathChange={builder.setNewFilePath}
              onCreateFile={() => builder.handleCreateFile()}
              onDeleteFile={builder.handleDeleteFile}
              onRenameFile={builder.handleRenameFile}
              onOpenFile={path => { if (selectedWorkspace) builder.openFile(selectedWorkspace.id, path); }}
              onProjectDraftChange={builder.setProjectDraft}
              onCreateProject={() => builder.handleCreateProject()}
              onDeleteProject={() => builder.handleDeleteProject()}
              onDeleteWorkspace={() => builder.handleDeleteWorkspace()}
              onWorkingBranchNameChange={builder.setWorkingBranchName}
              onAllowProtectedBranchEditChange={builder.setAllowProtectedBranchEdit}
              onSelectWorkingCopy={() => builder.handleSelectWorkingCopy()}
            />

            <EditorSurface
              workspace={selectedWorkspace}
              project={selectedProject}
              canEdit={capabilities!.canEditFiles}
              editorTabs={builder.editorTabs}
              activeFilePath={activeFilePath}
              editorText={builder.editorText}
              editorDirty={builder.editorDirty}
              autoSaving={builder.autoSaving}
              lineHint={builder.lineHint}
              diagnostics={builder.activeBuild?.diagnostics ?? []}
              onSelectEditorTab={builder.handleSelectEditorTab}
              onCloseEditorTab={builder.handleCloseEditorTab}
              onEditorTextChange={builder.handleEditorTextChange}
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
        open={builder.dockOpen}
        onToggleOpen={() => builder.setDockOpen(open => !open)}
        activeTab={builder.activeInspectorTab}
        onSelectTab={builder.setActiveInspectorTab}
        activeBuild={builder.activeBuild}
        buildHistory={builder.buildHistory}
        buildLog={builder.buildLog}
        sourceControlStatus={builder.sourceControlStatus}
        sourceControlDiff={builder.sourceControlDiff}
        commitMessage={builder.commitMessage}
        buildCommand={builder.buildCommand}
        buildTargetPath={builder.buildTargetPath}
        artifact={builder.latestArtifact}
        promotionResult={builder.promotionResult}
        runtimeStatus={builder.runtimeStatus}
        busy={buildBusy}
        buildInProgress={buildInProgress}
        sourceBusy={isBusy("source")}
        promoteBusy={isBusy("promote")}
        runtimeBusy={isBusy("runtime")}
        canBuild={canBuild}
        canPromote={builder.canPromote}
        onBuildCommandChange={builder.setBuildCommand}
        onBuildTargetPathChange={builder.setBuildTargetPath}
        onSubmitBuild={builder.handleSubmitBuild}
        onPromoteArtifact={builder.handlePromoteArtifact}
        onSelectBuild={build => {
          builder.setActiveBuild(build);
          builder.setActiveInspectorTab("build");
          builder.setDockOpen(true);
          if (selectedWorkspace && selectedProject) builder.refreshBuild(selectedWorkspace.id, selectedProject.id, build.id, build.revision);
        }}
        onSelectSourceDiff={builder.handleSelectSourceDiff}
        onStageFile={builder.handleStageFile}
        onUnstageFile={builder.handleUnstageFile}
        onStageAll={builder.handleStageAll}
        onCommitMessageChange={builder.setCommitMessage}
        onCommit={builder.handleCommitChanges}
        onPush={builder.handlePushRepository}
        onPull={builder.handlePullRepository}
        onPromote={builder.handlePromote}
        onRefreshRuntime={builder.refreshRuntimeStatus}
        onRetryReconciliation={builder.handleRetryReconciliation}
        onRollback={builder.handleRollback}
        onDiagnosticSelect={builder.handleDiagnosticSelect}
      />
    </div>
  );
}
