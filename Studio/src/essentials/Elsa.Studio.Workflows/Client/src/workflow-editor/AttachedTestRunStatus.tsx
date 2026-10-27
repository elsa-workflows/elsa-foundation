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
  return <div className="wf-attached-test-run" role="group" aria-label="Attached Test Run">
    <TestRunStatus testRun={view} stale={canvas.stale} onOpenDetails={onOpenDetails} />
    {canvas.eligible ? <>
      {canvas.status && !canvas.stale ? <WorkflowStatusBadge status={canvas.status} /> : null}
      <span role="status">{canvas.stale ? "The design changed. Run overlays are hidden." : canvas.attached ? "Execution on this canvas" : "Editing the current draft"}</span>
      {canvas.attached ? <>
        <button type="button" onClick={canvas.refresh} disabled={canvas.loading}>Refresh execution</button>
        <button type="button" onClick={canvas.detach}>Return to editing</button>
      </> : !canvas.stale ? <button type="button" onClick={canvas.attach}>Show execution</button> : null}
      <button type="button" onClick={onOpenRun}>Open run</button>
      {canvas.loading ? <span role="status">Loading execution evidence…</span> : null}
      {canvas.repeated ? <span>Some activities ran more than once. Choose an occurrence in Run data.</span> : null}
      {canvas.attached && canvas.granularity === "boundary-level" ? <span>Activity state is recorded at commit boundaries.</span> : null}
    </> : null}
    {canvas.error ? <span role="status">{canvas.error}</span> : null}
  </div>;
}
