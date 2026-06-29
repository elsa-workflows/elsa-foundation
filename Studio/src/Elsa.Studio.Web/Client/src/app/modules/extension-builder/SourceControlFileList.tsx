import { StatusChip } from "../../ui";
import type { SourceControlStatus } from "../extensionBuilderApi";

export function SourceControlFileList({
  title,
  files,
  empty,
  busy,
  actionLabel,
  onAction,
  onDiff
}: {
  title: string;
  files: SourceControlStatus["changedFiles"];
  empty: string;
  busy: boolean;
  actionLabel: string;
  onAction(path: string): void;
  onDiff(path: string): void;
}) {
  return (
    <div className="modules-inspector-section extension-builder-source-list">
      <h4>{title}</h4>
      {files.length === 0 ? <p className="modules-muted">{empty}</p> : null}
      {files.map(file => (
        <div key={`${title}-${file.path}`} className="extension-builder-source-row">
          <button type="button" onClick={() => onDiff(file.path)}>
            <code>{file.path}</code>
            <StatusChip tone="neutral">{file.status || "changed"}</StatusChip>
          </button>
          <button type="button" className="studio-button" disabled={busy} onClick={() => onAction(file.path)}>{actionLabel}</button>
        </div>
      ))}
    </div>
  );
}
