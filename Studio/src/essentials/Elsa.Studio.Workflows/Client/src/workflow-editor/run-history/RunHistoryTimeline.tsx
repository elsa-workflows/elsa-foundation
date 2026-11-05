import { useEffect, useRef, useState } from "react";
import { ListTree, RefreshCw } from "lucide-react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { StudioButton, StudioCollectionToolbar, StudioField, StudioSearchInput } from "@elsa-workflows/studio-ui";
import type { RuntimeHistoryEvent } from "../../runtimeHistoryTypes";
import { HistoryCoverage, HistoryEventList, historyZone, isHistoryHighlight } from "./historyPresentation";
import { useRunHistory } from "./useRunHistory";
import { defaultHistoryFilters, type RunHistoryFilters } from "./historyLocation";
import "./runHistory.css";
export { defaultHistoryFilters, type RunHistoryFilters } from "./historyLocation";
const categories = [["WorkflowLifecycle", "Run lifecycle"], ["ActivityLifecycle", "Activities"], ["BookmarkLifecycle", "Bookmarks"],
  ["DeliveryObservation", "Stimuli and delivery"], ["IncidentLifecycle", "Incidents"], ["SchedulerLifecycle", "Scheduling"],
  ["OperationalLifecycle", "Operations"], ["ValueLifecycle", "Value observations"]];

export function RunHistoryTimeline({ context, workflowExecutionId, filters, onFiltersChange, selectedActivityExecutionId,
  selectedActivityLabel, selectedEventId, onSelectEvent, labelForEvent, enabled = true }: {
  context: StudioEndpointContext; workflowExecutionId: string; filters: RunHistoryFilters;
  onFiltersChange(filters: RunHistoryFilters): void; selectedActivityExecutionId?: string | null; selectedActivityLabel?: string;
  selectedEventId?: string | null; onSelectEvent(event: RuntimeHistoryEvent): void;
  labelForEvent?(event: RuntimeHistoryEvent): string | null; enabled?: boolean;
}) {
  const history = useRunHistory(context, workflowExecutionId, { activityExecutionId: filters.activityExecutionId,
    includeDescendants: filters.includeDescendants, category: filters.category, name: filters.name, search: filters.search }, enabled);
  const [search, setSearch] = useState(filters.search);
  const [followEnd, setFollowEnd] = useState(false);
  const end = useRef<HTMLDivElement>(null);
  const window = history.value;
  const items = window?.items ?? [];
  const visible = filters.detail === "all" ? items : items.filter(isHistoryHighlight);
  const pending = window?.pending.length ?? 0;
  const showNew = history.showNew;
  useEffect(() => { setSearch(filters.search); }, [filters.search]);
  useEffect(() => { if (followEnd && pending) showNew(); }, [followEnd, showNew, pending]);
  useEffect(() => { if (followEnd && enabled) end.current?.scrollIntoView({ block: "nearest" }); }, [enabled, followEnd, items.length]);
  const change = (value: Partial<RunHistoryFilters>) => { setFollowEnd(false); onFiltersChange({ ...filters, ...value }); };
  return <section className="wf-run-history" aria-label="Run timeline" aria-busy={history.loading}>
    <header className="wf-history-heading"><div><h3><ListTree size={18} aria-hidden="true" /> Timeline</h3><p>Recorded order · {historyZone()}</p></div>
      <StudioButton type="button" onClick={history.refresh} aria-label="Refresh run history"><RefreshCw size={14} aria-hidden="true" /></StudioButton></header>
    <StudioCollectionToolbar className="wf-history-controls">
      <StudioField label="Scope"><select aria-label="History scope" value={filters.activityExecutionId ?? ""} onChange={event => change({ activityExecutionId: event.target.value || null, includeDescendants: false })}>
        <option value="">Entire run</option>
        {selectedActivityExecutionId ? <option value={selectedActivityExecutionId}>{selectedActivityLabel ?? "Selected activity"} · selected execution</option> : null}
        {filters.activityExecutionId && filters.activityExecutionId !== selectedActivityExecutionId ? <option value={filters.activityExecutionId}>Execution {filters.activityExecutionId}</option> : null}
      </select></StudioField>
      <StudioField label="Detail"><select aria-label="History detail" value={filters.detail} onChange={event => change({ detail: event.target.value as RunHistoryFilters["detail"] })}>
        <option value="highlights">Highlights</option><option value="all">All events</option></select></StudioField>
      <StudioField label="Category"><select aria-label="History category" value={filters.category} onChange={event => change({ category: event.target.value })}>
        <option value="">All categories</option>{categories.map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></StudioField>
      {filters.activityExecutionId ? <label className="wf-history-checkbox"><input type="checkbox" checked={filters.includeDescendants}
        onChange={event => change({ includeDescendants: event.target.checked })} />Include descendants</label> : null}
    </StudioCollectionToolbar>
    <form className="studio-collection-toolbar wf-history-search" onSubmit={event => { event.preventDefault(); change({ search: search.trim() }); }}>
      <div className="wf-history-search-field"><StudioField label="Search recorded events"><StudioSearchInput aria-label="Search recorded events" maxLength={256} value={search} onChange={event => setSearch(event.target.value)} placeholder="Event, bookmark, delivery or command…" /></StudioField></div>
      <StudioButton type="submit">Search</StudioButton>
      {filters.search || filters.category || filters.name || filters.activityExecutionId ? <StudioButton type="button" onClick={() => { setSearch(""); onFiltersChange({ ...defaultHistoryFilters, detail: filters.detail }); }}>Clear filters</StudioButton> : null}
    </form>
    {filters.name ? <p className="wf-history-note">Kind filter: {filters.name} <StudioButton type="button" onClick={() => change({ name: "" })}>Remove kind filter</StudioButton></p> : null}
    <div className="wf-history-reading-controls"><StudioButton type="button" aria-pressed={history.following} onClick={() => history.setFollowing(!history.following)}>{history.following ? "Pause live updates" : "Resume live updates"}</StudioButton>
      <StudioButton type="button" aria-pressed={followEnd} onClick={() => setFollowEnd(!followEnd)}>{followEnd ? "Following latest" : "Follow latest"}</StudioButton>
      <span role="status">{history.paused ? "Live observation paused" : history.status === "stale" ? "Showing last confirmed history" : "Live history"}</span></div>
    {history.error ? <p className="wf-history-message" role="status">{history.error} <StudioButton type="button" onClick={history.refresh}>Retry history</StudioButton></p> : null}
    {window ? <>
      <HistoryCoverage coverage={window.page.coverage} readAt={window.page.readAt} scopeComplete={window.page.scopeComplete} scopeLimitation={window.page.scopeLimitation} />
      <p className="wf-history-count" title={`Snapshot through #${window.snapshotUpperSequence}; searched through #${window.snapshotScannedThroughSequence}`}>{visible.length} shown · {items.length} loaded · {window.scannedCount} snapshot records searched{window.next ? " · more history not loaded" : ""}</p>
      {pending ? <div className="wf-history-new" role="status"><StudioButton type="button" onClick={() => { history.showNew(); setFollowEnd(true); }}>{pending} new recorded events · Show</StudioButton>{pending >= 500 ? <span>Load these events to continue live catch-up.</span> : null}</div> : null}
      <div className="wf-history-list-region" onScroll={event => { const element = event.currentTarget; if (followEnd && element.scrollHeight - element.scrollTop - element.clientHeight > 40) setFollowEnd(false); }}>
        {visible.length ? <HistoryEventList unloadedAfterSequence={window.next ? window.snapshotUpperSequence : undefined} items={visible} selectedEventId={selectedEventId} labelForEvent={labelForEvent} onSelect={event => { setFollowEnd(false); onSelectEvent(event); }} />
          : <p className="wf-history-empty">{filters.search || filters.category || filters.name ? "No matching events in this searched range." : filters.detail === "highlights" && items.length ? "No highlight events in this loaded range. Choose All events to inspect its recorded detail." : "No recorded events in this range."}</p>}
        {window.next ? <StudioButton className="wf-history-more" type="button" disabled={history.loading} onClick={history.loadMore}>{filters.search ? "Continue searching recorded history" : "Load more history"}</StudioButton> : null}
        <div ref={end} />
      </div>
    </> : history.status === "loading" ? <p className="wf-history-empty" role="status">Loading recorded history…</p> : null}
  </section>;
}

export function ActivityHistoryJournal({ context, workflowExecutionId, activityExecutionId, selectedEventId, onSelectEvent, onOpenTimeline }: {
  context: StudioEndpointContext; workflowExecutionId: string; activityExecutionId: string;
  selectedEventId?: string | null; onSelectEvent?(event: RuntimeHistoryEvent): void; onOpenTimeline?(activityExecutionId: string): void;
}) {
  const [includeDescendants, setIncludeDescendants] = useState(false);
  const history = useRunHistory(context, workflowExecutionId, { activityExecutionId, includeDescendants });
  const value = history.value;
  return <section className="wf-activity-history" aria-label="Activity journal">
    <p className="wf-history-note">This execution{includeDescendants ? " and its descendants" : " only, including directly related delivery observations"}.</p>
    <label className="wf-history-checkbox"><input type="checkbox" checked={includeDescendants} onChange={event => setIncludeDescendants(event.target.checked)} />Include descendants</label>
    {history.error ? <p role="status">{history.error} <StudioButton type="button" onClick={history.refresh}>Retry journal</StudioButton></p> : null}
    {value ? <><HistoryCoverage coverage={value.page.coverage} readAt={value.page.readAt} scopeComplete={value.page.scopeComplete} scopeLimitation={value.page.scopeLimitation} />
      <HistoryEventList compact unloadedAfterSequence={value.next ? value.snapshotUpperSequence : undefined} items={value.items} selectedEventId={selectedEventId} onSelect={event => onSelectEvent?.(event)} />
      {!value.items.length ? <p>No recorded events in this activity range.</p> : null}
      {value.pending.length ? <StudioButton type="button" onClick={history.showNew}>Show {value.pending.length} new journal events</StudioButton> : null}
      {value.next ? <StudioButton type="button" disabled={history.loading} onClick={history.loadMore}>Load more journal events</StudioButton> : null}
    </> : history.loading ? <p role="status">Loading activity journal…</p> : null}
    {onOpenTimeline ? <p><StudioButton type="button" onClick={() => onOpenTimeline(activityExecutionId)}>Focus this execution in timeline</StudioButton></p> : null}
  </section>;
}
