import { ChevronDown, ChevronUp } from "lucide-react";
import { StatusChip } from "../../ui";
import type {
  BuildArtifact,
  BuildDiagnostic,
  BuildResult,
  ExtensionBuilderCapabilities,
  ExtensionProject,
  ExtensionRuntimeStatus,
  PackagePromotionResult,
  RepositoryBuildCommand,
  SourceControlDiff,
  SourceControlStatus
} from "../extensionBuilderApi";
import { BuildPanel } from "./BuildPanel";
import { buildTone, isBuildForCurrentRevision } from "./helpers";
import { PromotePanel } from "./PromotePanel";
import { RuntimePanel } from "./RuntimePanel";
import { SourceControlPanel } from "./SourceControlPanel";
import { dockTabs, type InspectorTab } from "./types";

export function BottomDock({
  capabilities,
  advanced,
  project,
  open,
  onToggleOpen,
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
  onPromoteArtifact,
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
  advanced: boolean;
  project: ExtensionProject | null;
  open: boolean;
  onToggleOpen(): void;
  activeTab: InspectorTab;
  onSelectTab(tab: InspectorTab): void;
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
  onSubmitBuild(command?: RepositoryBuildCommand): void;
  onPromoteArtifact(artifact: BuildArtifact): void;
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
  const tabs = advanced ? dockTabs : dockTabs.filter(tab => tab.id === "build");
  // Clamp to a tab that actually exists in the current mode. Promotion handlers may set the
  // active tab to "promote"/"runtime" even in simple mode, where only "build" is rendered;
  // without this the dock would open to an empty body.
  const effectiveTab: InspectorTab = tabs.some(tab => tab.id === activeTab) ? activeTab : "build";
  const changeCount = (sourceControlStatus?.changedFiles ?? []).length;
  const diagnosticCount = (activeBuild?.diagnostics ?? []).length;

  function selectTab(tab: InspectorTab) {
    onSelectTab(tab);
    if (!open) onToggleOpen();
  }

  return (
    <section className={open ? "extension-builder-dock open" : "extension-builder-dock"} aria-label="Build and runtime dock">
      <div className="extension-builder-dock-tabs" role="tablist">
        {tabs.map(tab => (
          <button
            key={tab.id}
            type="button"
            role="tab"
            aria-selected={open && tab.id === effectiveTab}
            className={open && tab.id === effectiveTab ? "active" : ""}
            onClick={() => selectTab(tab.id)}
          >
            {tab.label}
            {tab.id === "source" && changeCount > 0 ? <span className="extension-builder-dock-badge warn">{changeCount}</span> : null}
            {tab.id === "build" && diagnosticCount > 0 ? <span className="extension-builder-dock-badge">{diagnosticCount}</span> : null}
          </button>
        ))}
        <span className="extension-builder-dock-spacer" />
        {activeBuild ? <StatusChip tone={buildTone(activeBuild.status)}>{activeBuild.status}</StatusChip> : null}
        <button type="button" className="studio-icon-button" aria-label={open ? "Collapse dock" : "Expand dock"} title={open ? "Collapse" : "Expand"} onClick={onToggleOpen}>
          {open ? <ChevronDown size={15} /> : <ChevronUp size={15} />}
        </button>
      </div>

      {open ? (
        <div className="extension-builder-dock-body">
          {effectiveTab === "build" ? (
            <BuildPanel
              advanced={advanced}
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
              onPromoteArtifact={onPromoteArtifact}
              onSelectBuild={onSelectBuild}
              onDiagnosticSelect={onDiagnosticSelect}
            />
          ) : null}

          {advanced && effectiveTab === "source" ? (
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

          {advanced && effectiveTab === "promote" ? (
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

          {advanced && effectiveTab === "runtime" ? (
            <RuntimePanel
              capabilities={capabilities}
              runtimeStatus={runtimeStatus}
              busy={busy}
              onRefreshRuntime={onRefreshRuntime}
              onRetryReconciliation={onRetryReconciliation}
              onRollback={onRollback}
            />
          ) : null}
        </div>
      ) : null}
    </section>
  );
}
