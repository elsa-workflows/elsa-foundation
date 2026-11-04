import { ArrowLeft, Eye } from "lucide-react";
import { RunObservationControls, RunObservationNotice } from "./RunObservationControls";
import { isTerminalExecution } from "./useExecutionObservation";
import type { EditorRunCanvas } from "./useEditorRunCanvas";
import "./attachedTestRun.css";

export function AttachedTestRunControls({ canvas }: {
  canvas: EditorRunCanvas;
}) {
  return <div className="wf-attached-test-run" role="group" aria-label="Execution controls">
    {canvas.eligible ? <>
      <span role="status">{canvas.snapshotView ? "Captured snapshot · read only. Your draft edits are preserved." : canvas.stale ? "The design changed. Run overlays are hidden." : canvas.attached ? "Execution on this canvas" : "Editing the current draft"}</span>
      <div className="wf-attached-run-actions">
        {canvas.attached ? <>
          <button type="button" className="wf-run-tool" onClick={canvas.detach}><ArrowLeft size={14} aria-hidden="true" />Return to editing</button>
          <RunObservationControls following={canvas.following} onFollowingChange={canvas.setFollowing} onRefresh={canvas.refresh}
            loading={canvas.loading} complete={canvas.observationStatus === "connected" && isTerminalExecution(canvas.status)} />
        </> : <button type="button" className="wf-run-tool" onClick={canvas.attach}><Eye size={14} aria-hidden="true" />{canvas.stale ? "Inspect captured snapshot" : "Show execution"}</button>}
      </div>
      {canvas.loading && !canvas.details ? <span role="status">Loading execution evidence…</span> : null}
      {canvas.repeated ? <span>Some activities ran more than once. Choose an occurrence in Run data.</span> : null}
      {canvas.attached && canvas.granularity === "boundary-level" ? <span>Activity state is recorded at commit boundaries.</span> : null}
      {canvas.attached ? <RunObservationNotice status={canvas.observationStatus} /> : null}
    </> : null}
    {canvas.error ? <span role="status">{canvas.error}</span> : null}
  </div>;
}
