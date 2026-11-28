import type { WorkflowNodeData } from "../workflowAdapter";
import { RuntimeIncidentAction } from "./RuntimeIncidentAction";
import { WorkflowStatusBadge } from "./WorkflowStatusBadge";

/** Shared runtime status and incident cues for workflow activity cards. */
export function WorkflowNodeRuntimeBadges({
  data,
  className = "",
  includeStatus = true
}: {
  data: WorkflowNodeData;
  className?: string;
  includeStatus?: boolean;
}) {
  const runtime = data.runtime;
  if (!runtime) return null;
  const status = includeStatus ? runtime.status : null;
  if (!status && !runtime.compensated && !runtime.primaryIncidentId && !runtime.containedPrimaryIncidentId && runtime.faultCount <= 0) return null;

  return (
    <div className={["wf-node-runtime-strip", className].filter(Boolean).join(" ")}>
      {status ? <WorkflowStatusBadge status={status} subStatus={runtime.subStatus} /> : null}
      {runtime.compensated ? <span className="wf-status-badge" data-status="compensated"><span aria-hidden="true">⟲</span> Compensated</span> : null}
      <RuntimeIncidentAction runtime={runtime} onOpenIncident={data.onIncidentClick} activityLabel={data.label} />
      {runtime.faultCount > 0 ? <span className="wf-node-runtime-count" data-health={runtime.hasBlockingIncident ? "blocking" : "neutral"}>{runtime.faultCount} faults</span> : null}
    </div>
  );
}
