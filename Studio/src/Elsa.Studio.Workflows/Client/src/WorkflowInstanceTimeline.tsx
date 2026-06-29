import { useMemo } from "react";
import { getActivityDisplay, resolveActivityIcon } from "./workflowAdapter";
import { formatDuration, renderActivityIcon, shortTypeName } from "./workflowFormatting";
import type { ActivityCatalogItem, ActivityExecutionStateSummary } from "./workflowTypes";

export function WorkflowExecutionTimeline({ activities, activityCatalog, selectedEvidenceId = null, onSelectEvidence }: {
  activities: ActivityExecutionStateSummary[];
  activityCatalog: ActivityCatalogItem[];
  selectedEvidenceId?: string | null;
  onSelectEvidence?(evidenceId: string): void;
}) {
  const catalogByTypeKey = useMemo(
    () => new Map(activityCatalog.map(item => [item.activityTypeKey, item])),
    [activityCatalog]
  );
  const ordered = useMemo(() => sortExecutionTimeline(activities), [activities]);

  if (ordered.length === 0) {
    return <div className="wf-empty wf-timeline-empty">No activity executions recorded yet.</div>;
  }

  return (
    <ol className="wf-timeline" aria-label="Execution timeline">
      {ordered.map(activity => {
        const catalogItem = catalogByTypeKey.get(activity.activityType);
        const icon = resolveActivityIcon(catalogItem);
        const label = catalogItem ? getActivityDisplay(catalogItem) : (shortTypeName(activity.activityType) ?? activity.activityType);
        const typeName = shortTypeName(activity.activityType) ?? activity.activityType;
        const timestamp = formatClockTime(activity.startedAt ?? activity.scheduledAt);
        const duration = formatDuration(activity.startedAt, activity.completedAt);
        return (
          <li key={activity.activityExecutionId}>
            <button
              type="button"
              className="wf-timeline-entry"
              data-selected={activity.activityExecutionId === selectedEvidenceId}
              onClick={() => onSelectEvidence?.(activity.activityExecutionId)}
            >
              <span className="wf-timeline-icon wf-activity-icon" data-icon={icon} aria-hidden="true">{renderActivityIcon(icon)}</span>
              <span className="wf-timeline-body">
                <strong title={label}>{label}</strong>
                <small title={typeName}>{typeName}</small>
              </span>
              <span className="wf-timeline-meta">
                {timestamp ? <time>{timestamp}</time> : null}
                {duration ? <small>took {duration}</small> : null}
              </span>
              <WorkflowStatusPill status={activity.status} />
            </button>
          </li>
        );
      })}
    </ol>
  );
}

function WorkflowStatusPill({ status }: { status: string }) {
  const normalized = status.toLowerCase();
  const label = normalized === "completed" ? "OK" : status;
  return <span className="wf-status-badge wf-timeline-pill" data-status={normalized}>{label}</span>;
}

function sortExecutionTimeline(activities: ActivityExecutionStateSummary[]) {
  return activities
    .map((activity, index) => ({ activity, index }))
    .sort((left, right) => timelineSortKey(left.activity) - timelineSortKey(right.activity) || left.index - right.index)
    .map(entry => entry.activity);
}

function timelineSortKey(activity: ActivityExecutionStateSummary) {
  const value = activity.startedAt ?? activity.scheduledAt;
  const parsed = value ? Date.parse(value) : Number.NaN;
  return Number.isNaN(parsed) ? Number.MAX_SAFE_INTEGER : parsed;
}

function formatClockTime(value?: string | null) {
  if (!value) return "";
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? ""
    : date.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: false });
}
