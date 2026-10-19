import type { WorkflowNodeData } from "../workflowAdapter";
import { RuntimeIncidentAction } from "./RuntimeIncidentAction";
import { WorkflowStatusBadge } from "./WorkflowStatusBadge";

/** Shared runtime status and incident cues for workflow activity cards. */
export function WorkflowNodeRuntimeBadges({
  data,
  className = ""
}: {
  data: WorkflowNodeData;
  className?: string;
}) {
  const runtime = data.runtime;
  if (!runtime) return null;

  return (
    <div className={["wf-node-runtime-strip", className].filter(Boolean).join(" ")}>
      {runtime.status ? <WorkflowStatusBadge status={runtime.status} subStatus={runtime.subStatus} /> : null}
      <RuntimeIncidentAction runtime={runtime} onOpenIncident={data.onIncidentClick} activityLabel={data.label} />
      {runtime.faultCount > 0 ? <span className="wf-node-runtime-count" data-health={runtime.hasBlockingIncident ? "blocking" : "neutral"}>{runtime.faultCount} faults</span> : null}
    </div>
  );
}
