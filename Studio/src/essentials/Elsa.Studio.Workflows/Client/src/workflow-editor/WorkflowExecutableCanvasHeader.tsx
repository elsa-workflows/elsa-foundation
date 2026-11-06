import { CopyableIdentifier } from "@elsa-workflows/studio-ui";

export function WorkflowExecutableCanvasHeader({
  displayName,
  version,
  artifactId,
  selectionLabel
}: {
  displayName: string;
  version: string | null;
  artifactId: string;
  selectionLabel: string;
}) {
  return (
    <header>
      <div>
        <span className="wf-canvas-kicker">Executable</span>
        <h3>
          {displayName}
          {version ? <> <small>Version {version}</small></> : null}
        </h3>
        <CopyableIdentifier
          label="Artifact ID"
          value={artifactId}
          className="wf-executable-artifact-id"
        />
      </div>
      <span className="wf-executable-selection" title={selectionLabel}>{selectionLabel}</span>
    </header>
  );
}
