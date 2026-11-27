import { useEffect, useMemo, useRef, useState, type CSSProperties } from "react";
import { Activity, ChevronRight, ChevronsDownUp, ChevronsUpDown, CircleAlert, CircleCheck, CirclePlay, CircleSlash, Pause, RefreshCw } from "lucide-react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { StudioButton, StudioSearchInput } from "@elsa-workflows/studio-ui";
import { browseActivityExecutions, supportsActivityExecutionBrowsing } from "../../api/runtime";
import { isCompensationHandlerExecution } from "../../compensationRuntime";
import { compareIsoTimestampTicks, parseIsoTimestampTicks } from "../../runtimeActivityOrder";
import { formatDuration } from "../../workflowFormatting";
import type { ActivityExecutionStateSummary } from "../../workflowTypes";
import { historyClock } from "./historyPresentation";
import "./runHistory.css";

export type ActivityTimelineFilter = "all" | "faulted" | "waiting" | "running";

export interface ActivityTimelineSpan {
  startPercent: number;
  widthPercent: number;
  open: boolean;
}

export interface ActivityTimelineEntry {
  execution: ActivityExecutionStateSummary;
  depth: number;
  parentId: string | null;
  hasChildren: boolean;
  descendantCount: number;
  faultedDescendantCount: number;
  ancestorIds: readonly string[];
  span: ActivityTimelineSpan | null;
}

function parentExecutionId(execution: ActivityExecutionStateSummary): string | null {
  return execution.parentActivityExecutionId ?? execution.provenance?.parentActivityExecutionId ?? null;
}

function executionIncidentCount(execution: ActivityExecutionStateSummary): number {
  if (typeof execution.incidentCount === "number") return execution.incidentCount;
  if (Array.isArray(execution.incidentIds)) return execution.incidentIds.length;
  return 0;
}

export function isFaultedActivityExecution(execution: ActivityExecutionStateSummary): boolean {
  return execution.status === "Faulted" || executionIncidentCount(execution) > 0;
}

export function isWaitingActivityExecution(execution: ActivityExecutionStateSummary): boolean {
  return execution.status === "Suspended";
}

export function isRunningActivityExecution(execution: ActivityExecutionStateSummary): boolean {
  return execution.status === "Running" || execution.status === "Executing";
}

export function meaningfulActivityOutcomes(execution: ActivityExecutionStateSummary): string[] {
  const outcomes = (execution.outcomeNames ?? []).map(item => item.trim()).filter(Boolean);
  if (outcomes.length === 1 && outcomes[0].toLowerCase() === "done") return [];
  return outcomes;
}

function compareSiblingExecutions(
  left: { execution: ActivityExecutionStateSummary; index: number },
  right: { execution: ActivityExecutionStateSummary; index: number }
): number {
  const startOrder = compareIsoTimestampTicks(
    left.execution.startedAt ?? left.execution.scheduledAt,
    right.execution.startedAt ?? right.execution.scheduledAt
  );
  if (startOrder !== 0) return startOrder;

  const scheduledOrder = compareIsoTimestampTicks(left.execution.scheduledAt, right.execution.scheduledAt);
  if (scheduledOrder !== 0) return scheduledOrder;

  const leftSeq = left.execution.executionSequence;
  const rightSeq = right.execution.executionSequence;
  if (typeof leftSeq === "number" && typeof rightSeq === "number" && leftSeq !== rightSeq) {
    return leftSeq - rightSeq;
  }

  return left.index - right.index;
}

function computeActivitySpans(activities: readonly ActivityExecutionStateSummary[]): ReadonlyMap<string, ActivityTimelineSpan> {
  const spans = new Map<string, ActivityTimelineSpan>();
  if (activities.length < 2) return spans;

  let windowStart: bigint | null = null;
  let windowEnd: bigint | null = null;
  const parsed = new Map<string, { start: bigint | null; completed: bigint | null; open: boolean }>();

  for (const execution of activities) {
    const start = parseIsoTimestampTicks(execution.startedAt ?? execution.scheduledAt);
    const completed = parseIsoTimestampTicks(execution.completedAt);
    const open = completed === null && (isRunningActivityExecution(execution) || isWaitingActivityExecution(execution));
    parsed.set(execution.activityExecutionId, { start, completed, open });
    if (start !== null) {
      if (windowStart === null || start < windowStart) windowStart = start;
      if (windowEnd === null || start > windowEnd) windowEnd = start;
    }
    if (completed !== null) {
      if (windowStart === null || completed < windowStart) windowStart = completed;
      if (windowEnd === null || completed > windowEnd) windowEnd = completed;
    }
  }

  if (windowStart === null || windowEnd === null || windowEnd <= windowStart) return spans;
  const totalTicks = windowEnd - windowStart;

  for (const execution of activities) {
    const timing = parsed.get(execution.activityExecutionId);
    if (!timing || timing.start === null) continue;
    const rawEnd = timing.completed ?? (timing.open ? windowEnd : timing.start);
    const clampedEnd = rawEnd < timing.start ? timing.start : rawEnd > windowEnd ? windowEnd : rawEnd;
    const startPercent = Number(((timing.start - windowStart) * 10000n) / totalTicks) / 100;
    const rawWidth = Number(((clampedEnd - timing.start) * 10000n) / totalTicks) / 100;
    const widthPercent = Math.max(0, Math.min(100 - startPercent, rawWidth));
    spans.set(execution.activityExecutionId, { startPercent, widthPercent, open: timing.open });
  }

  return spans;
}

/** Builds a start-time-ordered hierarchical activity tree (pre-order DFS) with capped depth and relative timing spans. */
export function buildActivityTimelineEntries(activities: readonly ActivityExecutionStateSummary[]): ActivityTimelineEntry[] {
  const indexed = activities.map((execution, index) => ({ execution, index }));
  const byId = new Map<string, { execution: ActivityExecutionStateSummary; index: number }>();
  for (const item of indexed) byId.set(item.execution.activityExecutionId, item);

  // Resolve parent links within the loaded set while guarding against cycles.
  const resolvedParent = new Map<string, string | null>();
  for (const { execution } of indexed) {
    const id = execution.activityExecutionId;
    const candidate = parentExecutionId(execution);
    if (!candidate || candidate === id || !byId.has(candidate)) {
      resolvedParent.set(id, null);
      continue;
    }
    let cursor: string | null = candidate;
    let cyclic = false;
    const seen = new Set<string>([id]);
    while (cursor !== null) {
      if (seen.has(cursor)) {
        cyclic = true;
        break;
      }
      seen.add(cursor);
      const nextExecution: ActivityExecutionStateSummary | undefined = byId.get(cursor)?.execution;
      const nextParent: string | null = nextExecution ? parentExecutionId(nextExecution) : null;
      cursor = nextParent && byId.has(nextParent) ? nextParent : null;
    }
    resolvedParent.set(id, cyclic ? null : candidate);
  }

  const childrenByParent = new Map<string, Array<{ execution: ActivityExecutionStateSummary; index: number }>>();
  const roots: Array<{ execution: ActivityExecutionStateSummary; index: number }> = [];
  for (const item of indexed) {
    const parentId = resolvedParent.get(item.execution.activityExecutionId) ?? null;
    if (parentId) {
      const siblings = childrenByParent.get(parentId);
      if (siblings) siblings.push(item);
      else childrenByParent.set(parentId, [item]);
    } else {
      roots.push(item);
    }
  }

  roots.sort(compareSiblingExecutions);
  for (const siblings of childrenByParent.values()) siblings.sort(compareSiblingExecutions);

  const subtreeStats = new Map<string, { descendants: number; faultedDescendants: number }>();
  const computeStats = (id: string): { descendants: number; faultedDescendants: number } => {
    const cached = subtreeStats.get(id);
    if (cached) return cached;
    const children = childrenByParent.get(id) ?? [];
    let descendants = 0;
    let faultedDescendants = 0;
    for (const child of children) {
      const childStats = computeStats(child.execution.activityExecutionId);
      descendants += 1 + childStats.descendants;
      faultedDescendants += (isFaultedActivityExecution(child.execution) ? 1 : 0) + childStats.faultedDescendants;
    }
    const result = { descendants, faultedDescendants };
    subtreeStats.set(id, result);
    return result;
  };

  const spans = computeActivitySpans(activities);
  const result: ActivityTimelineEntry[] = [];

  const visit = (
    item: { execution: ActivityExecutionStateSummary; index: number },
    rawDepth: number,
    parentId: string | null,
    ancestorIds: readonly string[]
  ) => {
    const id = item.execution.activityExecutionId;
    const children = childrenByParent.get(id) ?? [];
    const stats = computeStats(id);
    const effectiveDepth = parentId === null && typeof item.execution.callStackDepth === "number" && item.execution.callStackDepth > 0
      ? item.execution.callStackDepth
      : rawDepth;

    result.push({
      execution: item.execution,
      depth: Math.min(effectiveDepth, 4),
      parentId,
      hasChildren: children.length > 0,
      descendantCount: stats.descendants,
      faultedDescendantCount: stats.faultedDescendants,
      ancestorIds,
      span: spans.get(id) ?? null
    });

    if (children.length > 0) {
      const nextAncestors = [...ancestorIds, id];
      for (const child of children) {
        visit(child, effectiveDepth + 1, id, nextAncestors);
      }
    }
  };

  for (const root of roots) visit(root, 0, null, []);
  return result;
}

type ActivityStatusTone = "danger" | "success" | "warning" | "accent" | "neutral";

function activityStatusTone(execution: ActivityExecutionStateSummary): ActivityStatusTone {
  if (isFaultedActivityExecution(execution)) return "danger";
  if (execution.status === "Completed") return "success";
  if (isWaitingActivityExecution(execution)) return "warning";
  if (isRunningActivityExecution(execution)) return "accent";
  return "neutral";
}

function ActivityStatusIcon({ execution }: { execution: ActivityExecutionStateSummary }) {
  const props = { size: 14, "aria-hidden": true as const };
  if (isFaultedActivityExecution(execution)) return <CircleAlert {...props} />;
  if (execution.status === "Completed") return <CircleCheck {...props} />;
  if (isWaitingActivityExecution(execution)) return <Pause {...props} />;
  if (isRunningActivityExecution(execution)) return <CirclePlay {...props} />;
  if (execution.status === "Cancelled" || execution.status === "Aborted") return <CircleSlash {...props} />;
  return <Activity {...props} />;
}

function activityTimingTitle(execution: ActivityExecutionStateSummary, duration: string): string {
  const startClock = historyClock(execution.startedAt ?? execution.scheduledAt);
  const startLabel = execution.startedAt ? `Started ${startClock}` : `Scheduled ${startClock}`;
  if (execution.completedAt) {
    const endLabel = `Completed ${historyClock(execution.completedAt)}`;
    return duration ? `${startLabel} · ${endLabel} · ${duration}` : `${startLabel} · ${endLabel}`;
  }
  if (isWaitingActivityExecution(execution)) return `${startLabel} · Waiting`;
  if (isRunningActivityExecution(execution)) return `${startLabel} · Running`;
  return startLabel;
}

export function RunActivitiesTimeline({
  context,
  workflowExecutionId,
  activities,
  hasMoreFromSummary = false,
  selectedActivityExecutionId,
  labelForActivity,
  onSelectActivity,
  onRefresh,
  onExecutionsLoaded
}: {
  context: StudioEndpointContext;
  workflowExecutionId: string;
  activities: readonly ActivityExecutionStateSummary[];
  hasMoreFromSummary?: boolean;
  selectedActivityExecutionId?: string | null;
  labelForActivity(execution: ActivityExecutionStateSummary): string;
  onSelectActivity(execution: ActivityExecutionStateSummary): void;
  onRefresh(): void;
  onExecutionsLoaded?(executions: ActivityExecutionStateSummary[]): void;
}) {
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState<ActivityTimelineFilter>("all");
  const [collapsedIds, setCollapsedIds] = useState<ReadonlySet<string>>(() => new Set());
  const [extraActivities, setExtraActivities] = useState<{ runId: string; items: ActivityExecutionStateSummary[]; nextCursor: string | null; exhausted: boolean }>({
    runId: workflowExecutionId,
    items: [],
    nextCursor: null,
    exhausted: false
  });
  const [loadingMore, setLoadingMore] = useState(false);
  const [loadMoreError, setLoadMoreError] = useState("");
  const [browsingSupported, setBrowsingSupported] = useState<boolean | null>(null);
  const abortRef = useRef<AbortController | null>(null);
  const entriesRef = useRef<readonly ActivityTimelineEntry[]>([]);

  useEffect(() => {
    setSearch("");
    setStatusFilter("all");
    setCollapsedIds(new Set());
    setLoadMoreError("");
    setExtraActivities({ runId: workflowExecutionId, items: [], nextCursor: null, exhausted: false });
    abortRef.current?.abort();
  }, [workflowExecutionId]);

  useEffect(() => {
    if (!hasMoreFromSummary) {
      setBrowsingSupported(null);
      return;
    }
    let cancelled = false;
    void supportsActivityExecutionBrowsing(context).then(
      supported => { if (!cancelled) setBrowsingSupported(supported); },
      () => { if (!cancelled) setBrowsingSupported(false); }
    );
    return () => { cancelled = true; };
  }, [context, hasMoreFromSummary, workflowExecutionId]);

  useEffect(() => () => { abortRef.current?.abort(); }, []);

  const mergedActivities = useMemo(() => {
    const byId = new Map<string, ActivityExecutionStateSummary>();
    for (const item of activities) byId.set(item.activityExecutionId, item);
    if (extraActivities.runId === workflowExecutionId) {
      for (const item of extraActivities.items) {
        if (!byId.has(item.activityExecutionId)) byId.set(item.activityExecutionId, item);
      }
    }
    return [...byId.values()];
  }, [activities, extraActivities, workflowExecutionId]);

  const entries = useMemo(() => buildActivityTimelineEntries(mergedActivities), [mergedActivities]);
  entriesRef.current = entries;

  // When selection changes externally (e.g. diagram click or playback), reveal it if an ancestor was collapsed.
  useEffect(() => {
    if (!selectedActivityExecutionId) return;
    const match = entriesRef.current.find(entry => entry.execution.activityExecutionId === selectedActivityExecutionId);
    if (!match || match.ancestorIds.length === 0) return;
    setCollapsedIds(previous => {
      if (!match.ancestorIds.some(id => previous.has(id))) return previous;
      const next = new Set(previous);
      for (const id of match.ancestorIds) next.delete(id);
      return next;
    });
  }, [selectedActivityExecutionId]);

  const parentIds = useMemo(
    () => entries.filter(entry => entry.hasChildren).map(entry => entry.execution.activityExecutionId),
    [entries]
  );
  const hasTreeBranches = parentIds.length > 0;
  const allCollapsed = hasTreeBranches && parentIds.every(id => collapsedIds.has(id));

  const toggleCollapsed = (id: string) => {
    setCollapsedIds(previous => {
      const next = new Set(previous);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const toggleAllCollapsed = () => {
    setCollapsedIds(allCollapsed ? new Set() : new Set(parentIds));
  };

  const counts = useMemo(() => {
    let faulted = 0;
    let waiting = 0;
    let running = 0;
    for (const { execution } of entries) {
      if (isFaultedActivityExecution(execution)) faulted++;
      if (isWaitingActivityExecution(execution)) waiting++;
      if (isRunningActivityExecution(execution)) running++;
    }
    return { all: entries.length, faulted, waiting, running };
  }, [entries]);

  const activeFilter: ActivityTimelineFilter =
    statusFilter === "faulted" && counts.faulted === 0
      ? "all"
      : statusFilter === "waiting" && counts.waiting === 0
        ? "all"
        : statusFilter === "running" && counts.running === 0
          ? "all"
          : statusFilter;

  const trimmedSearch = search.trim().toLowerCase();
  const isFiltered = Boolean(trimmedSearch) || activeFilter !== "all";

  const filteredEntries = useMemo(() => entries.filter(entry => {
    const { execution } = entry;
    if (isFiltered) {
      if (activeFilter === "faulted" && !isFaultedActivityExecution(execution)) return false;
      if (activeFilter === "waiting" && !isWaitingActivityExecution(execution)) return false;
      if (activeFilter === "running" && !isRunningActivityExecution(execution)) return false;
      if (!trimmedSearch) return true;
      const label = labelForActivity(execution).toLowerCase();
      const outcomes = (execution.outcomeNames ?? []).join(" ").toLowerCase();
      return label.includes(trimmedSearch)
        || execution.activityType.toLowerCase().includes(trimmedSearch)
        || execution.status.toLowerCase().includes(trimmedSearch)
        || execution.authoredActivityId.toLowerCase().includes(trimmedSearch)
        || execution.activityExecutionId.toLowerCase().includes(trimmedSearch)
        || outcomes.includes(trimmedSearch);
    }
    return !entry.ancestorIds.some(id => collapsedIds.has(id));
  }), [activeFilter, collapsedIds, entries, isFiltered, labelForActivity, trimmedSearch]);

  const showQuickFilters = counts.faulted > 0 || counts.waiting > 0 || counts.running > 0;
  const currentExtras = extraActivities.runId === workflowExecutionId ? extraActivities : null;
  const canLoadMore = hasMoreFromSummary && browsingSupported !== false && !(currentExtras?.exhausted);

  const loadMoreActivities = async () => {
    if (loadingMore || !canLoadMore) return;
    abortRef.current?.abort();
    const controller = new AbortController();
    abortRef.current = controller;
    setLoadingMore(true);
    setLoadMoreError("");
    try {
      const page = await browseActivityExecutions(
        context,
        workflowExecutionId,
        { limit: 100, ...(currentExtras?.nextCursor ? { cursor: currentExtras.nextCursor } : {}) },
        controller.signal
      );
      if (controller.signal.aborted) return;
      const loaded = page.items.map(item => item.execution);
      setExtraActivities(previous => {
        const base = previous.runId === workflowExecutionId ? previous.items : [];
        const byId = new Map(base.map(item => [item.activityExecutionId, item]));
        for (const item of loaded) byId.set(item.activityExecutionId, item);
        return {
          runId: workflowExecutionId,
          items: [...byId.values()],
          nextCursor: page.nextCursor,
          exhausted: !page.nextCursor
        };
      });
      onExecutionsLoaded?.(loaded);
    } catch {
      if (!controller.signal.aborted) setLoadMoreError("Could not load more activities.");
    } finally {
      if (!controller.signal.aborted) setLoadingMore(false);
    }
  };

  return (
    <section className="wf-run-activities" aria-label="Executed activities">
      <div className="wf-activity-timeline-controls">
        <div className="wf-activity-timeline-search-row">
          <StudioSearchInput
            aria-label="Search activities"
            placeholder="Search activities…"
            value={search}
            onChange={event => setSearch(event.target.value)}
          />
          {hasTreeBranches && !isFiltered ? (
            <StudioButton
              type="button"
              variant="ghost"
              size="sm"
              className="wf-history-toolbar-btn"
              aria-label={allCollapsed ? "Expand all activities" : "Collapse all activities"}
              title={allCollapsed ? "Expand all" : "Collapse all"}
              onClick={toggleAllCollapsed}
            >
              {allCollapsed ? <ChevronsUpDown size={14} aria-hidden="true" /> : <ChevronsDownUp size={14} aria-hidden="true" />}
            </StudioButton>
          ) : null}
          <StudioButton
            type="button"
            variant="ghost"
            size="sm"
            className="wf-history-toolbar-btn"
            aria-label="Refresh activities"
            title="Refresh activities"
            onClick={onRefresh}
          >
            <RefreshCw size={14} aria-hidden="true" />
          </StudioButton>
        </div>
        {showQuickFilters ? (
          <div className="wf-activity-timeline-filters" role="group" aria-label="Filter activities by status">
            <button
              type="button"
              className="wf-activity-filter-pill"
              aria-pressed={activeFilter === "all"}
              onClick={() => setStatusFilter("all")}
            >
              All <span>{counts.all}</span>
            </button>
            {counts.faulted > 0 ? (
              <button
                type="button"
                className="wf-activity-filter-pill"
                data-tone="danger"
                aria-pressed={activeFilter === "faulted"}
                onClick={() => setStatusFilter(activeFilter === "faulted" ? "all" : "faulted")}
              >
                Faulted <span>{counts.faulted}</span>
              </button>
            ) : null}
            {counts.waiting > 0 ? (
              <button
                type="button"
                className="wf-activity-filter-pill"
                data-tone="warning"
                aria-pressed={activeFilter === "waiting"}
                onClick={() => setStatusFilter(activeFilter === "waiting" ? "all" : "waiting")}
              >
                Waiting <span>{counts.waiting}</span>
              </button>
            ) : null}
            {counts.running > 0 ? (
              <button
                type="button"
                className="wf-activity-filter-pill"
                data-tone="accent"
                aria-pressed={activeFilter === "running"}
                onClick={() => setStatusFilter(activeFilter === "running" ? "all" : "running")}
              >
                Running <span>{counts.running}</span>
              </button>
            ) : null}
          </div>
        ) : null}
      </div>

      <div className="wf-history-list-region">
        {filteredEntries.length > 0 ? (
          <ol className="wf-activity-timeline-list">
            {filteredEntries.map(entry => {
              const { execution, depth, hasChildren, descendantCount, faultedDescendantCount, span } = entry;
              const label = labelForActivity(execution);
              const tone = activityStatusTone(execution);
              const incidentCount = executionIncidentCount(execution);
              const outcomes = meaningfulActivityOutcomes(execution);
              const duration = formatDuration(execution.startedAt, execution.completedAt);
              const clock = historyClock(execution.startedAt ?? execution.scheduledAt);
              const timingTitle = activityTimingTitle(execution, duration);
              const selected = selectedActivityExecutionId === execution.activityExecutionId;
              const visualDepth = isFiltered ? 0 : depth;
              const collapsed = !isFiltered && hasChildren && collapsedIds.has(execution.activityExecutionId);
              const compensation = isCompensationHandlerExecution(execution);
              const showStatusChip =
                (execution.status === "Faulted" && incidentCount === 0)
                || execution.status === "Suspended"
                || execution.status === "Running"
                || execution.status === "Cancelled"
                || execution.status === "Aborted";

              return (
                <li
                  key={execution.activityExecutionId}
                  className={visualDepth > 0 ? "wf-activity-timeline-item is-nested" : "wf-activity-timeline-item"}
                  style={{ "--wf-activity-depth": visualDepth } as CSSProperties}
                >
                  <div className="wf-activity-timeline-row-shell">
                    {!isFiltered && hasTreeBranches ? (
                      hasChildren ? (
                        <button
                          type="button"
                          className="wf-activity-tree-toggle"
                          aria-expanded={!collapsed}
                          aria-label={collapsed ? `Expand ${label}` : `Collapse ${label}`}
                          onClick={() => toggleCollapsed(execution.activityExecutionId)}
                        >
                          <ChevronRight size={13} aria-hidden="true" />
                        </button>
                      ) : (
                        <span className="wf-activity-tree-spacer" aria-hidden="true" />
                      )
                    ) : null}
                    <button
                      type="button"
                      className="wf-activity-timeline-row"
                      aria-pressed={selected}
                      data-activity-execution-id={execution.activityExecutionId}
                      data-status={execution.status}
                      onClick={() => onSelectActivity(execution)}
                    >
                      <span className="wf-history-event-icon" data-tone={tone}>
                        <ActivityStatusIcon execution={execution} />
                      </span>
                      <span className="wf-activity-timeline-body">
                        <span className="wf-activity-timeline-top">
                          <strong className="wf-activity-timeline-name" title={label}>{label}</strong>
                          {duration ? <span className="wf-activity-timeline-duration" title={timingTitle}>{duration}</span> : null}
                        </span>
                        <span className="wf-activity-timeline-meta">
                          <span className="wf-activity-timeline-time" title={timingTitle}>{clock}</span>
                          {collapsed ? (
                            <span className="wf-activity-timeline-chip" data-tone={faultedDescendantCount > 0 ? "danger" : "neutral"}>
                              {descendantCount === 1 ? "1 step" : `${descendantCount} steps`}
                              {faultedDescendantCount > 0 ? ` · ${faultedDescendantCount} faulted` : ""}
                            </span>
                          ) : null}
                          {incidentCount > 0 ? (
                            <span className="wf-activity-timeline-chip" data-tone="danger">
                              {incidentCount === 1 ? "1 incident" : `${incidentCount} incidents`}
                            </span>
                          ) : null}
                          {showStatusChip ? (
                            <span className="wf-activity-timeline-chip" data-tone={tone}>
                              {execution.status === "Suspended" ? "Waiting" : execution.status}
                            </span>
                          ) : null}
                          {outcomes.map((outcome, index) => (
                            <span className="wf-activity-timeline-chip" data-tone="neutral" key={`${outcome}:${index}`}>
                              {outcome}
                            </span>
                          ))}
                          {compensation ? (
                            <span className="wf-activity-timeline-chip" data-tone="neutral">Compensation</span>
                          ) : null}
                        </span>
                        {span ? (
                          <span className="wf-activity-span-track" aria-hidden="true" title={timingTitle}>
                            <span
                              className="wf-activity-span-fill"
                              data-tone={tone}
                              data-open={span.open || undefined}
                              style={{
                                "--wf-span-start": `${span.startPercent}%`,
                                "--wf-span-width": `${span.widthPercent}%`
                              } as CSSProperties}
                            />
                          </span>
                        ) : null}
                      </span>
                    </button>
                  </div>
                </li>
              );
            })}
          </ol>
        ) : (
          <p className="wf-history-empty">
            {isFiltered ? "No activities match the current filter." : "No activities have been recorded for this run yet."}
          </p>
        )}

        {loadMoreError ? <p className="wf-history-message" role="status">{loadMoreError}</p> : null}
        {canLoadMore ? (
          <StudioButton
            className="wf-history-more"
            type="button"
            size="sm"
            disabled={loadingMore}
            onClick={() => { void loadMoreActivities(); }}
          >
            {loadingMore ? "Loading more activities…" : "Load more activities"}
          </StudioButton>
        ) : null}
      </div>

      <footer className="wf-history-footer">
        <span className="wf-history-count">
          {isFiltered
            ? `${filteredEntries.length} of ${entries.length} ${entries.length === 1 ? "activity" : "activities"}`
            : `${entries.length} ${entries.length === 1 ? "activity" : "activities"}`}
          {canLoadMore ? " · more available" : ""}
        </span>
      </footer>
    </section>
  );
}
