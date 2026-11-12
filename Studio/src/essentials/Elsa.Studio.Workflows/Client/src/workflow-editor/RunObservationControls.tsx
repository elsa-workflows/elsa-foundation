import { Check, Pause, Play, RefreshCw } from "lucide-react";
import type { ExecutionObservationStatus } from "./useExecutionObservation";
import "./runInspectionToolbar.css";

/** Observation changes never issue a workflow command or change the selected occurrence. */
export function RunObservationControls({ following, onFollowingChange, onRefresh, loading = false, complete = false }: {
  following?: boolean;
  onFollowingChange?(following: boolean): void;
  onRefresh(): void;
  loading?: boolean;
  complete?: boolean;
}) {
  const finished = complete && following !== false;
  return <div className="wf-run-observation-controls" role="group" aria-label="Live updates">
    <button type="button" className="wf-run-tool wf-run-icon-tool" aria-label="Refresh execution" title="Refresh execution"
      disabled={loading} onClick={onRefresh}><RefreshCw size={15} aria-hidden="true" /></button>
    {typeof following === "boolean" && onFollowingChange ? <button type="button" className="wf-run-tool wf-run-live-tool"
      aria-label={finished ? "Live updates complete" : following ? "Pause live updates" : "Resume live updates"}
      aria-pressed={!finished && following} disabled={finished}
      title={finished ? "The run has finished. Refresh to read its latest evidence." : "Pause or resume recorded updates without changing the inspected occurrence. Pausing updates does not stop the workflow."}
      onClick={() => onFollowingChange(!following)}>
      {finished ? <Check size={14} aria-hidden="true" /> : following ? <Pause size={14} aria-hidden="true" /> : <Play size={14} aria-hidden="true" />}
      <span>{finished ? "Updates complete" : following ? "Live updates" : "Updates paused"}</span>
    </button> : null}
  </div>;
}

export function RunObservationNotice({ status, error }: { status?: ExecutionObservationStatus; error?: string }) {
  const label = status === "paused" ? "Live updates paused · showing last confirmed evidence. Pausing updates does not stop the workflow."
    : status === "disconnected" ? "Live updates disconnected · showing last confirmed evidence."
    : status === "unavailable" ? "Live updates unavailable."
    : status === "expired" ? "Test Run expired."
    : status === "loading" ? "Connecting to execution…" : "";
  return label || error ? <p className="wf-run-observation-notice" role="status">{label}{error ? ` ${error}` : ""}</p> : null;
}
