import { GitBranch, RefreshCcw, Save } from "lucide-react";
import { StatusChip } from "../../ui";
import type { SourceControlDiff, SourceControlStatus } from "../extensionBuilderApi";
import { SourceControlFileList } from "./SourceControlFileList";

export function SourceControlPanel({
  status,
  diff,
  commitMessage,
  busy,
  onSelectDiff,
  onStageFile,
  onUnstageFile,
  onStageAll,
  onCommitMessageChange,
  onCommit,
  onPush,
  onPull
}: {
  status: SourceControlStatus | null;
  diff: SourceControlDiff | null;
  commitMessage: string;
  busy: boolean;
  onSelectDiff(path: string, staged: boolean): void;
  onStageFile(path: string): void;
  onUnstageFile(path: string): void;
  onStageAll(): void;
  onCommitMessageChange(value: string): void;
  onCommit(): void;
  onPush(): void;
  onPull(): void;
}) {
  const stagedFiles = status?.stagedFiles ?? [];
  const unstagedFiles = status?.unstagedFiles ?? [];
  const hasBranch = !!status?.activeBranch;
  return (
    <div className="extension-builder-source-control">
      <div className="modules-inspector-section">
        <h4>Working copy</h4>
        <p><code>{status?.activeBranch ?? "No branch"}</code></p>
        <StatusChip tone={status?.isDirty ? "warning" : "success"}>{status?.isDirty ? "dirty" : "clean"}</StatusChip>
      </div>

      <div className="extension-builder-source-actions">
        <button type="button" className="studio-button" disabled={busy || unstagedFiles.length === 0} onClick={onStageAll}>
          <Save size={14} />
          Stage all
        </button>
        <button type="button" className="studio-button" disabled={busy || !hasBranch || !!status?.isDirty} title={status?.isDirty ? "Commit or discard changes before pushing" : undefined} onClick={onPush}>
          <GitBranch size={14} />
          Push
        </button>
        <button type="button" className="studio-button" disabled={busy || !hasBranch || !!status?.isDirty} title={status?.isDirty ? "Commit or discard changes before pulling" : undefined} onClick={onPull}>
          <RefreshCcw size={14} />
          Pull
        </button>
      </div>

      <SourceControlFileList
        title="Staged"
        files={stagedFiles}
        empty="No staged changes."
        busy={busy}
        actionLabel="Unstage"
        onAction={onUnstageFile}
        onDiff={path => onSelectDiff(path, true)}
      />

      <SourceControlFileList
        title="Unstaged"
        files={unstagedFiles}
        empty="No unstaged changes."
        busy={busy}
        actionLabel="Stage"
        onAction={onStageFile}
        onDiff={path => onSelectDiff(path, false)}
      />

      <div className="modules-inspector-section">
        <h4>Commit</h4>
        <textarea
          aria-label="Commit message"
          className="extension-builder-commit-message"
          value={commitMessage}
          disabled={busy}
          onChange={event => onCommitMessageChange(event.target.value)}
        />
        <button type="button" className="studio-button" disabled={busy || stagedFiles.length === 0 || !commitMessage.trim()} onClick={onCommit}>
          <GitBranch size={14} />
          Commit staged
        </button>
      </div>

      <div className="modules-inspector-section">
        <h4>Diff</h4>
        {diff ? (
          <>
            <p><code>{diff.path}</code>{diff.isStaged ? " staged" : ""}</p>
            <pre className="extension-builder-diff">{diff.patch || "No diff available."}</pre>
          </>
        ) : (
          <p className="modules-muted">Select a changed file to inspect its diff.</p>
        )}
      </div>
    </div>
  );
}
