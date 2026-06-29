import { StatusChip, StudioTabs } from "../../ui";
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
import { inspectorTabs, type InspectorTab } from "./types";

export function BuildRuntimeInspector({
  capabilities,
  advanced,
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
  return (
    <aside className="modules-inspector extension-builder-inspector" aria-label="Build and runtime inspector">
      <div className="modules-inspector-heading">
        <div>
          <span>Inspector</span>
          <h3>{project?.name ?? "No project"}</h3>
        </div>
        {activeBuild ? <StatusChip tone={buildTone(activeBuild.status)}>{activeBuild.status}</StatusChip> : null}
      </div>
      {advanced ? <StudioTabs tabs={inspectorTabs} activeTab={activeTab} onSelect={onSelectTab} /> : null}

      {!advanced || activeTab === "build" ? (
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

      {advanced && activeTab === "source" ? (
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

      {advanced && activeTab === "promote" ? (
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

      {advanced && activeTab === "runtime" ? (
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
