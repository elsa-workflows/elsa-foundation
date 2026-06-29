import { PackageCheck, Play } from "lucide-react";
import { StatusChip } from "../../ui";
import type { BuildArtifact, BuildDiagnostic, BuildResult, ExtensionBuilderCapabilities, RepositoryBuildCommand } from "../extensionBuilderApi";
import { buildTone, diagnosticTone, formatArtifactSize, formatDate, formatDiagnosticLocation } from "./helpers";

export function BuildPanel({
  advanced,
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
  onPromoteArtifact,
  onSelectBuild,
  onDiagnosticSelect
}: {
  advanced: boolean;
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
  onSubmitBuild(command?: RepositoryBuildCommand): void;
  onPromoteArtifact(artifact: BuildArtifact): void;
  onSelectBuild(build: BuildResult): void;
  onDiagnosticSelect(diagnostic: BuildDiagnostic): void;
}) {
  return (
    <div className="modules-inspector-section">
      <h4>Run</h4>
      {advanced ? (
        <>
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
          <button type="button" className="studio-button" disabled={busy || !canBuild} title={!capabilities.canBuild ? "Requires canBuild" : undefined} onClick={() => onSubmitBuild()}>
            <Play size={15} />
            Run command
          </button>
        </>
      ) : (
        <button type="button" className="studio-button studio-button-primary" disabled={busy || !canBuild} title={!capabilities.canBuild ? "Requires canBuild" : "Build and pack a NuGet package"} onClick={() => onSubmitBuild("Pack")}>
          <PackageCheck size={15} />
          Pack NuGet package
        </button>
      )}
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
              <small>{artifact.fileName ?? artifact.id}{artifact.branch ? ` · ${artifact.branch}` : ""}{artifact.sourceRevisionId ? ` · ${artifact.sourceRevisionId.slice(0, 8)}` : ""}{artifact.sourceIsDirty ? " · uncommitted" : ""}</small>
            </span>
            <button type="button" className="studio-button" disabled={busy || artifact.sourceIsDirty} title={artifact.sourceIsDirty ? "Commit and pack again before promoting" : "Promote package artifact"} onClick={() => onPromoteArtifact(artifact)}>
              <PackageCheck size={15} />
              Promote package
            </button>
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
