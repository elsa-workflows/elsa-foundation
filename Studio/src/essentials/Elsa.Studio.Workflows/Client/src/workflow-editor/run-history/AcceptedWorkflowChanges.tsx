import type { ActivityExecutionStateSummary } from "../../workflowTypes";
import { workflowStateEffectLabels } from "../../workflowInstanceInspection";
import { historyClock } from "./historyPresentation";

/** Accepted evaluation receipts are distinct from the independently captured history stream. */
export function AcceptedWorkflowChanges({ activities, selectedActivityExecutionId, labelForActivity }: {
  activities: readonly ActivityExecutionStateSummary[];
  selectedActivityExecutionId?: string | null;
  labelForActivity(activity: ActivityExecutionStateSummary): string;
}) {
  const accepted = activities.filter(activity => !selectedActivityExecutionId || activity.activityExecutionId === selectedActivityExecutionId)
    .flatMap(activity => {
      const receipts = (activity.workflowStateEffectReceipts ?? []).filter(receipt =>
        (receipt.transition === "Completion" || receipt.transition === "Suspension") && workflowStateEffectLabels(receipt.kinds).length > 0);
      const labels = workflowStateEffectLabels([...(activity.workflowStateEffects ?? []), ...receipts.flatMap(receipt => receipt.kinds)]);
      return labels.length || receipts.length ? [{ activity, labels, receipts }] : [];
    });
  if (!accepted.length) return null;
  return <section className="wf-history-accepted-changes" aria-label="Accepted workflow changes">
    <h4>Accepted workflow changes</h4>
    <p>{selectedActivityExecutionId ? "Selected activity." : "Loaded activity executions only."} Current values may differ.</p>
    <ul>{accepted.map(({ activity, labels, receipts }) => <li key={activity.activityExecutionId}>
      <strong>{labelForActivity(activity)}</strong>
      {labels.length ? <p>Committed changes: {labels.join(", ")}</p> : null}
      {receipts.map((receipt, index) => <p key={index}>{receipt.transition === "Suspension" ? "Suspension" : "Completion"} accepted <time dateTime={receipt.acceptedAt}>{historyClock(receipt.acceptedAt)}</time>: {workflowStateEffectLabels(receipt.kinds).join(", ")}</p>)}
    </li>)}</ul>
  </section>;
}
