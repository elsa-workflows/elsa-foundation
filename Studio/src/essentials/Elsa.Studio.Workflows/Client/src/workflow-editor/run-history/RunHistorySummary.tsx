import { useState } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { StudioButton } from "@elsa-workflows/studio-ui";
import type { ActivityExecutionStateSummary, WorkflowInstanceSummary } from "../../workflowTypes";
import { listWorkflowHistoryBookmarks } from "../../api/runtimeHistory";
import { formatDuration, humanizeTypeName } from "../../workflowFormatting";
import { historyInstant } from "./historyPresentation";
import { useHistoryResource } from "./useHistoryResource";

export function RunHistorySummary({ context, instance, name, activities, activitiesLimited, onBookmark, labelForActivity, enabled }: {
  context: StudioEndpointContext; instance: WorkflowInstanceSummary; name: string;
  activities: ActivityExecutionStateSummary[]; activitiesLimited: boolean; onBookmark(id: string): void; labelForActivity?(activity: ActivityExecutionStateSummary): string; enabled: boolean;
}) {
  const bookmarks = useHistoryResource(context, JSON.stringify([instance.workflowExecutionId, "waiting-summary"]),
    signal => listWorkflowHistoryBookmarks(context, instance.workflowExecutionId, { status: "Active", limit: 100 }, signal), true, enabled);
  const [expanded, setExpanded] = useState<{ run: string; value: boolean } | null>(null);
  const showAll = expanded?.run === instance.workflowExecutionId && expanded.value;
  const waiting = new Set(activities.filter(activity => activity.status === "Suspended").map(activity => activity.activityExecutionId));
  const terminal = ["Completed", "Faulted", "Cancelled", "Aborted"].includes(instance.status);
  const recorded = bookmarks.value;
  const current = terminal ? [] : recorded?.items.filter(item => item.status === "Active" && waiting.has(item.bookmark.identity.activityExecutionId)) ?? [];
  const observedAt = recorded?.readAt ?? instance.updatedAt;
  const elapsed = formatDuration(instance.startedAt, instance.completedAt ?? observedAt);
  return <header className="wf-history-run-summary" aria-label="Run history summary">
    <div><h2>{name}</h2><span className="wf-history-badge">{instance.status}</span></div>
    <p>{elapsed ? <>Elapsed {elapsed}{!instance.completedAt ? " at last observation" : ""}. </> : null}Elapsed includes waiting. Active execution time is not recorded.</p>
    {current.length ? <><h3>Waiting at last observation</h3><ul className="wf-history-waits">{current.slice(0, showAll ? undefined : 3).map(item => {
      const bookmark = item.bookmark;
      const owner = activities.find(activity => activity.activityExecutionId === bookmark.identity.activityExecutionId);
      const age = item.priorState !== "Unknown" && ["eligible", "expired", "ineligible"].includes(item.eligibility) ? formatDuration(bookmark.createdAt, item.eligibilityObservedAt) : "";
      return <li key={bookmark.identity.bookmarkId}><button type="button" onClick={() => onBookmark(bookmark.identity.bookmarkId)}>
        <strong>{humanizeTypeName(bookmark.stimulusType) || "Recorded stimulus"}</strong><span>{(owner && labelForActivity?.(owner)) || humanizeTypeName(owner?.activityType) || "Activity"} · execution {bookmark.identity.activityExecutionId}</span>
        {age ? <span>Handle open for {age}</span> : null}{item.eligibility !== "eligible" ? <span>Eligibility: {item.eligibility}{item.eligibilityReason ? ` · ${item.eligibilityReason.replaceAll("-", " ")}` : ""}</span> : null}
      </button></li>;
    })}</ul>{current.length > 3 ? <StudioButton type="button" aria-expanded={showAll} onClick={() => setExpanded({ run: instance.workflowExecutionId, value: !showAll })}>{showAll ? "Show fewer waiting handles" : `Show all ${current.length} loaded waiting handles`}</StudioButton> : null}</> : !terminal && waiting.size ? <p>Waiting activity observed. Expected stimuli are {bookmarks.status === "loading" ? "loading…" : "unavailable in this recorded range."}</p> : null}
    {current.length > 0 && observedAt ? <p>Waiting handles observed {historyInstant(observedAt)}.</p> : null}
    {activitiesLimited || recorded?.nextContinuationToken || recorded && !recorded.scopeComplete ? <p>Waiting summary is limited to loaded executions and handles.</p> : null}
    {bookmarks.error ? <p role="status">{bookmarks.error}</p> : null}
  </header>;
}
