import type { WorkflowTestRunView } from "../workflowTypes";
import { TestRunStatus } from "./editorPanels";
import { WorkflowStatusBadge } from "./WorkflowStatusBadge";
import type { EditorRunCanvas } from "./useEditorRunCanvas";
import "./attachedTestRun.css";

export function AttachedTestRunStatus({ view, canvas, onOpenDetails, onOpenRun }: {
  view: WorkflowTestRunView;
  canvas: EditorRunCanvas;
  onOpenDetails(): void;
  onOpenRun(): void;
}) {
  const observationLabel = canvas.observationStatus === "disconnected" ? "Execution observation disconnected"
    : canvas.observationStatus === "paused" ? "Observation paused. The workflow continues."
    : canvas.observationStatus === "expired" ? "Test Run expired"
    : canvas.observationStatus === "unavailable" ? "Execution observation unavailable"
    : canvas.observationStatus === "loading" ? "Connecting to execution…" : "Execution on this canvas";
  return <div className="wf-attached-test-run" role="group" aria-label="Attached Test Run">
    <TestRunStatus testRun={view} stale={canvas.stale} onOpenDetails={onOpenDetails} />
    {canvas.eligible ? <>
      {canvas.status && (!canvas.stale || canvas.snapshotView) && (!canvas.observationStatus || canvas.observationStatus === "connected") ? <WorkflowStatusBadge status={canvas.status} /> : null}
      <span role="status">{canvas.snapshotView ? "Captured snapshot · read only. Your draft edits are preserved." : canvas.stale ? "The design changed. Run overlays are hidden." : canvas.attached ? observationLabel : "Editing the current draft"}</span>
      {canvas.attached ? <>
        {typeof canvas.following === "boolean" && canvas.setFollowing ? <button type="button" aria-pressed={canvas.following}
          title="Follow recorded execution updates without changing the inspected occurrence. Pausing observation does not stop the workflow."
          onClick={() => canvas.setFollowing?.(!canvas.following)}>{canvas.following ? "Pause following" : "Follow execution"}</button> : null}
        <button type="button" onClick={canvas.refresh} disabled={canvas.loading}>Refresh execution</button>
        <button type="button" onClick={canvas.detach}>Return to editing</button>
      </> : <button type="button" onClick={canvas.attach}>{canvas.stale ? "Inspect captured snapshot" : "Show execution"}</button>}
      <button type="button" onClick={onOpenRun}>Open run</button>
      {canvas.loading ? <span role="status">Loading execution evidence…</span> : null}
      {canvas.repeated ? <span>Some activities ran more than once. Choose an occurrence in Run data.</span> : null}
      {canvas.attached && canvas.granularity === "boundary-level" ? <span>Activity state is recorded at commit boundaries.</span> : null}
    </> : null}
    {canvas.error ? <span role="status">{canvas.error}</span> : null}
  </div>;
}
