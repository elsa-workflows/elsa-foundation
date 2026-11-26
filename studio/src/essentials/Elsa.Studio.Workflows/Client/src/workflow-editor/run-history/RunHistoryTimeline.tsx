import { useEffect, useId, useRef, useState } from "react";
import type { ReactNode } from "react";
import { ArrowDownToLine, Info, ListFilter, ListTree, Pause, Play, RefreshCw, ShieldCheck, X } from "lucide-react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { AnchoredPopover, StudioButton, StudioField, StudioSearchInput } from "@elsa-workflows/studio-ui";
import type { RuntimeHistoryEvent } from "../../runtimeHistoryTypes";
import { HistoryCoverage, HistoryEventList, historyClock, historyCoverageSummaryLabel, historyEventLabel, historyZone, isHistoryCoverageDegraded, matchesHistoryDetail } from "./historyPresentation";
import { useRunHistory } from "./useRunHistory";
import { defaultHistoryFilters, type RunHistoryDetailLevel, type RunHistoryFilters } from "./historyLocation";
import "./runHistory.css";
export { defaultHistoryFilters, type RunHistoryDetailLevel, type RunHistoryFilters } from "./historyLocation";
const detailPresets: ReadonlyArray<[RunHistoryDetailLevel, string]> = [
  ["lifecycle", "Lifecycle"],
  ["highlights", "Highlights"],
  ["all", "All"]
];
const categories = [["WorkflowLifecycle", "Run lifecycle"], ["ActivityLifecycle", "Activities"], ["BookmarkLifecycle", "Bookmarks"],
  ["DeliveryObservation", "Stimuli and delivery"], ["IncidentLifecycle", "Incidents"], ["SchedulerLifecycle", "Scheduling"],
  ["OperationalLifecycle", "Operations"], ["ValueLifecycle", "Value observations"]];

export function RunHistoryTimeline({ context, workflowExecutionId, filters, onFiltersChange, selectedActivityExecutionId,
  selectedActivityLabel, selectedEventId, selectedEvent, onSelectEvent, labelForEvent, enabled = true, onManualNavigation, headerActions, overviewContent, embedded = false }: {
  context: StudioEndpointContext; workflowExecutionId: string; filters: RunHistoryFilters;
  onFiltersChange(filters: RunHistoryFilters): void; selectedActivityExecutionId?: string | null; selectedActivityLabel?: string;
  selectedEventId?: string | null; onSelectEvent(event: RuntimeHistoryEvent): void;
  selectedEvent?: RuntimeHistoryEvent;
  labelForEvent?(event: RuntimeHistoryEvent): string | null; enabled?: boolean;
  onManualNavigation?(): void;
  headerActions?: ReactNode;
  overviewContent?: ReactNode;
  embedded?: boolean;
}) {
  const history = useRunHistory(context, workflowExecutionId, { activityExecutionId: filters.activityExecutionId,
    includeDescendants: filters.includeDescendants, category: filters.category, name: filters.name, search: filters.search }, enabled);
  const [search, setSearch] = useState(filters.search);
  const [followEnd, setFollowEnd] = useState(false);
  const [filtersOpen, setFiltersOpen] = useState(false);
  const [overviewOpen, setOverviewOpen] = useState(false);
  const [coverageOpen, setCoverageOpen] = useState(false);
  const filtersPopoverId = useId();
  const overviewPopoverId = useId();
  const coveragePopoverId = useId();
  const filtersTriggerRef = useRef<HTMLButtonElement>(null);
  const overviewTriggerRef = useRef<HTMLButtonElement>(null);
  const coverageTriggerRef = useRef<HTMLButtonElement>(null);
  const end = useRef<HTMLDivElement>(null);
  const window = history.value;
  const items = window?.items ?? [];
  const visible = items.filter(event => matchesHistoryDetail(event, filters.detail));
  const outsideSelection = selectedEvent?.workflowExecutionId === workflowExecutionId && selectedEvent.historyEventId === selectedEventId
    && !visible.some(event => event.historyEventId === selectedEventId) ? selectedEvent : null;
  const pending = window?.pending.length ?? 0;
  const showNew = history.showNew;
  const hasNarrowingFilters = Boolean(filters.search || filters.category || filters.name || filters.activityExecutionId);
  const hasActiveFilters = hasNarrowingFilters || filters.detail !== defaultHistoryFilters.detail;
  const categoryLabel = categories.find(([value]) => value === filters.category)?.[1] ?? filters.category;
  const scopeLabel = filters.activityExecutionId
    ? filters.activityExecutionId === selectedActivityExecutionId
      ? selectedActivityLabel || "Selected execution"
      : `Execution ${filters.activityExecutionId}`
    : null;
  const coverageDegraded = window ? isHistoryCoverageDegraded(window.page.coverage, window.page.scopeComplete, window.page.scopeLimitation) : false;

  useEffect(() => { setSearch(filters.search); }, [filters.search]);
  useEffect(() => { if (followEnd && pending) showNew(); }, [followEnd, showNew, pending]);
  useEffect(() => { if (followEnd && enabled) end.current?.scrollIntoView({ block: "nearest" }); }, [enabled, followEnd, items.length]);
  useEffect(() => {
    if (!filtersOpen && !overviewOpen && !coverageOpen) return;
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key !== "Escape") return;
      event.preventDefault();
      if (filtersOpen) { setFiltersOpen(false); filtersTriggerRef.current?.focus(); }
      else if (overviewOpen) { setOverviewOpen(false); overviewTriggerRef.current?.focus(); }
      else if (coverageOpen) { setCoverageOpen(false); coverageTriggerRef.current?.focus(); }
    };
    document.addEventListener("keydown", handleKeyDown);
    return () => document.removeEventListener("keydown", handleKeyDown);
  }, [coverageOpen, filtersOpen, overviewOpen]);

  const revealNew = () => {
    onManualNavigation?.();
    history.showNew();
    requestAnimationFrame(() => {
      const rows = end.current?.parentElement?.querySelectorAll<HTMLButtonElement>(".wf-history-event");
      // One deliberate reveal, independent of the persistent Follow latest preference.
      rows?.[rows.length - 1]?.focus();
      end.current?.scrollIntoView({ block: "nearest" });
    });
  };
  const change = (value: Partial<RunHistoryFilters>) => { onManualNavigation?.(); setFollowEnd(false); onFiltersChange({ ...filters, ...value }); };
  const clearFilters = () => { onManualNavigation?.(); setSearch(""); onFiltersChange({ ...defaultHistoryFilters, detail: filters.detail }); };

  return <section className={`wf-run-history${embedded ? " is-embedded" : ""}`} aria-label="Run timeline" aria-busy={history.loading}>
    <header className="wf-history-heading">
      <div className="wf-history-title-group">
        {!embedded ? <>
          <ListTree size={15} aria-hidden="true" />
          <h3>Timeline</h3>
        </> : <span className="wf-history-embedded-label">Runtime events</span>}
        {window ? <span className="wf-history-title-count" title={`Recorded order · ${historyZone()} · ${visible.length} shown · ${items.length} loaded · ${window.scannedCount} snapshot records searched`}>{visible.length}</span> : null}
      </div>
      <div className="wf-history-heading-actions" role="toolbar" aria-label="Timeline controls">
        <StudioButton
          ref={filtersTriggerRef}
          type="button"
          variant="ghost"
          size="sm"
          className="wf-history-toolbar-btn"
          aria-label="Filter events"
          title="Filter events"
          aria-haspopup="dialog"
          aria-expanded={filtersOpen}
          aria-controls={filtersOpen ? filtersPopoverId : undefined}
          data-active={hasActiveFilters || undefined}
          onClick={() => { setOverviewOpen(false); setCoverageOpen(false); setFiltersOpen(open => !open); }}
        >
          <ListFilter size={14} aria-hidden="true" />
          {hasActiveFilters ? <span className="wf-history-toolbar-dot" aria-hidden="true" /> : null}
        </StudioButton>
        <StudioButton
          type="button"
          variant="ghost"
          size="sm"
          className="wf-history-toolbar-btn"
          aria-pressed={history.following}
          aria-label={history.following ? "Pause live updates" : "Resume live updates"}
          title={history.following ? "Live updates active · click to pause" : "Live updates paused · click to resume"}
          onClick={() => { onManualNavigation?.(); history.setFollowing(!history.following); }}
        >
          {history.following ? <Pause size={14} aria-hidden="true" /> : <Play size={14} aria-hidden="true" />}
        </StudioButton>
        <StudioButton
          type="button"
          variant="ghost"
          size="sm"
          className="wf-history-toolbar-btn"
          aria-pressed={followEnd}
          aria-label={followEnd ? "Following latest" : "Follow latest"}
          title={followEnd ? "Following latest events" : "Follow latest events"}
          onClick={() => { onManualNavigation?.(); setFollowEnd(!followEnd); }}
        >
          <ArrowDownToLine size={14} aria-hidden="true" />
        </StudioButton>
        <StudioButton
          type="button"
          variant="ghost"
          size="sm"
          className="wf-history-toolbar-btn"
          aria-label="Refresh run history"
          title="Refresh timeline"
          onClick={() => { onManualNavigation?.(); history.refresh(); }}
        >
          <RefreshCw size={14} aria-hidden="true" />
        </StudioButton>
        {overviewContent ? (
          <StudioButton
            ref={overviewTriggerRef}
            type="button"
            variant="ghost"
            size="sm"
            className="wf-history-toolbar-btn"
            aria-label="Run overview"
            title="Run overview"
            aria-haspopup="dialog"
            aria-expanded={overviewOpen}
            aria-controls={overviewOpen ? overviewPopoverId : undefined}
            onClick={() => { setFiltersOpen(false); setCoverageOpen(false); setOverviewOpen(open => !open); }}
          >
            <Info size={14} aria-hidden="true" />
          </StudioButton>
        ) : null}
        {headerActions}
      </div>
    </header>

    <div className="wf-history-detail-bar" role="group" aria-label="Event detail level">
      {detailPresets.map(([value, label]) => (
        <button
          key={value}
          type="button"
          className="wf-activity-filter-pill"
          aria-pressed={filters.detail === value}
          onClick={() => change({ detail: value })}
        >
          {label}
        </button>
      ))}
    </div>

    <AnchoredPopover
      anchorRef={filtersTriggerRef}
      open={filtersOpen}
      id={filtersPopoverId}
      role="dialog"
      aria-label="Filter events"
      className="wf-history-popover"
      minWidth={300}
      maxHeight={460}
      onDismiss={() => setFiltersOpen(false)}
      onKeyDown={event => {
        if (event.key === "Escape") {
          event.preventDefault();
          event.stopPropagation();
          setFiltersOpen(false);
          filtersTriggerRef.current?.focus();
        }
      }}
    >
      <div className="wf-history-popover-content">
        <div className="wf-history-popover-header">
          <strong>Filter events</strong>
          {hasNarrowingFilters ? <StudioButton type="button" variant="ghost" size="sm" onClick={clearFilters}>Clear filters</StudioButton> : null}
        </div>
        <div className="wf-history-popover-fields">
          <StudioField label="Scope"><select aria-label="History scope" value={filters.activityExecutionId ?? ""} onChange={event => change({ activityExecutionId: event.target.value || null, includeDescendants: false })}>
            <option value="">Entire run</option>
            {selectedActivityExecutionId ? <option value={selectedActivityExecutionId}>{selectedActivityLabel ?? "Selected activity"} · selected execution</option> : null}
            {filters.activityExecutionId && filters.activityExecutionId !== selectedActivityExecutionId ? <option value={filters.activityExecutionId}>Execution {filters.activityExecutionId}</option> : null}
          </select></StudioField>
          <StudioField label="Detail"><select aria-label="History detail" value={filters.detail} onChange={event => change({ detail: event.target.value as RunHistoryFilters["detail"] })}>
            <option value="lifecycle">Lifecycle</option><option value="highlights">Highlights</option><option value="all">All events</option></select></StudioField>
          <StudioField label="Category"><select aria-label="History category" value={filters.category} onChange={event => change({ category: event.target.value })}>
            <option value="">All categories</option>{categories.map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></StudioField>
          {filters.activityExecutionId ? <label className="wf-history-checkbox"><input type="checkbox" checked={filters.includeDescendants}
            onChange={event => change({ includeDescendants: event.target.checked })} />Include descendants</label> : null}
        </div>
        <form className="wf-history-popover-search" onSubmit={event => { event.preventDefault(); change({ search: search.trim() }); }}>
          <div className="wf-history-search-field"><StudioField label="Search recorded events"><StudioSearchInput aria-label="Search recorded events" maxLength={256} value={search} onChange={event => { onManualNavigation?.(); setSearch(event.target.value); }} placeholder="Event, bookmark, delivery…" /></StudioField></div>
          <StudioButton type="submit" size="sm">Search</StudioButton>
        </form>
      </div>
    </AnchoredPopover>

    {overviewContent ? (
      <AnchoredPopover
        anchorRef={overviewTriggerRef}
        open={overviewOpen}
        id={overviewPopoverId}
        role="dialog"
        aria-label="Run overview"
        className="wf-history-popover"
        minWidth={300}
        maxHeight={420}
        onDismiss={() => setOverviewOpen(false)}
        onKeyDown={event => {
          if (event.key === "Escape") {
            event.preventDefault();
            event.stopPropagation();
            setOverviewOpen(false);
            overviewTriggerRef.current?.focus();
          }
        }}
      >
        <div className="wf-history-overview-popover">{overviewContent}</div>
      </AnchoredPopover>
    ) : null}

    {hasActiveFilters ? (
      <div className="wf-history-active-filters" aria-label="Active timeline filters">
        {scopeLabel ? <button type="button" className="wf-history-filter-chip" onClick={() => change({ activityExecutionId: null, includeDescendants: false })}><span>{scopeLabel}{filters.includeDescendants ? " + descendants" : ""}</span><X size={11} aria-hidden="true" /></button> : null}
        {filters.detail === "all" ? <button type="button" className="wf-history-filter-chip" onClick={() => change({ detail: defaultHistoryFilters.detail })}><span>All events</span><X size={11} aria-hidden="true" /></button> : null}
        {filters.detail === "highlights" ? <button type="button" className="wf-history-filter-chip" onClick={() => change({ detail: defaultHistoryFilters.detail })}><span>Highlights</span><X size={11} aria-hidden="true" /></button> : null}
        {filters.category ? <button type="button" className="wf-history-filter-chip" onClick={() => change({ category: "" })}><span>{categoryLabel}</span><X size={11} aria-hidden="true" /></button> : null}
        {filters.name ? <button type="button" className="wf-history-filter-chip" onClick={() => change({ name: "" })}><span>Kind: {filters.name}</span><X size={11} aria-hidden="true" /></button> : null}
        {filters.search ? <button type="button" className="wf-history-filter-chip" onClick={() => { setSearch(""); change({ search: "" }); }}><span>“{filters.search}”</span><X size={11} aria-hidden="true" /></button> : null}
      </div>
    ) : null}

    {history.status === "stale" ? <p className="wf-history-status-strip" role="status">Showing last confirmed history</p> : null}
    {history.error ? <p className="wf-history-message" role="status"><span>{history.error}</span> <StudioButton type="button" size="sm" onClick={() => { onManualNavigation?.(); history.refresh(); }}>Retry history</StudioButton></p> : null}

    {outsideSelection ? (
      <aside className="wf-playback-selected-observation" aria-label="Selected recorded observation">
        <button
          type="button"
          className="wf-history-outside-btn"
          aria-pressed="true"
          data-history-event-id={outsideSelection.historyEventId}
          onClick={() => onSelectEvent(outsideSelection)}
        >
          <strong>{historyEventLabel(outsideSelection)}</strong>
          <span>{labelForEvent?.(outsideSelection) ?? "Recorded activity"} · #{outsideSelection.sequence} · {historyClock(outsideSelection.occurredAtKnown === false ? null : outsideSelection.occurredAt)} · Hidden by filter</span>
        </button>
        {filters.detail !== "all" && !matchesHistoryDetail(outsideSelection, filters.detail) ? (
          <StudioButton type="button" variant="ghost" size="sm" className="wf-history-outside-reveal" onClick={() => change({ detail: "all" })}>Show all</StudioButton>
        ) : null}
      </aside>
    ) : null}

    {pending ? <div className="wf-history-new" role="status"><StudioButton type="button" size="sm" onClick={revealNew}>{pending} new recorded events · Show</StudioButton>{history.bufferLimited ? <span>Load these events to continue live catch-up.</span> : null}</div> : null}

    {window ? <>
      <div className="wf-history-list-region" onScroll={event => { const element = event.currentTarget; if (followEnd && element.scrollHeight - element.scrollTop - element.clientHeight > 40) setFollowEnd(false); }}>
        {visible.length ? <HistoryEventList unloadedAfterSequence={window.next ? window.snapshotUpperSequence : undefined} items={visible} selectedEventId={selectedEventId} labelForEvent={labelForEvent} onSelect={event => { setFollowEnd(false); onSelectEvent(event); }} />
          : <p className="wf-history-empty">{filters.search || filters.category || filters.name ? "No matching events in this searched range." : filters.detail === "lifecycle" && items.length ? "No lifecycle events in this loaded range. Choose Highlights or All to inspect more recorded detail." : filters.detail === "highlights" && items.length ? "No highlight events in this loaded range. Choose All events to inspect its recorded detail." : "No recorded events in this range."}</p>}
        {window.next ? <StudioButton className="wf-history-more" type="button" size="sm" disabled={history.loading} onClick={() => { onManualNavigation?.(); history.loadMore(); }}>{filters.search ? "Continue searching recorded history" : "Load more history"}</StudioButton> : null}
        <div ref={end} />
      </div>
      {coverageDegraded ? <HistoryCoverage coverage={window.page.coverage} readAt={window.page.readAt} scopeComplete={window.page.scopeComplete} scopeLimitation={window.page.scopeLimitation} /> : null}
      <footer className="wf-history-footer">
        <span className="wf-history-count" title={`Recorded order · ${historyZone()} · Snapshot through #${window.snapshotUpperSequence}; searched through #${window.snapshotScannedThroughSequence}`}>
          {visible.length} shown{visible.length !== items.length ? ` · ${items.length} loaded` : ""}{window.next ? " · more history not loaded" : ""}
        </span>
        <div className="wf-history-footer-actions">
          {history.paused ? <span className="wf-history-paused-tag" role="status">Paused</span> : null}
          {!coverageDegraded ? (
            <>
              <StudioButton
                ref={coverageTriggerRef}
                type="button"
                variant="ghost"
                size="sm"
                className="wf-history-footer-btn"
                aria-label="Recording coverage"
                title={historyCoverageSummaryLabel(window.page.coverage, window.page.scopeComplete)}
                aria-haspopup="dialog"
                aria-expanded={coverageOpen}
                aria-controls={coverageOpen ? coveragePopoverId : undefined}
                onClick={() => { setFiltersOpen(false); setOverviewOpen(false); setCoverageOpen(open => !open); }}
              >
                <ShieldCheck size={13} aria-hidden="true" />
              </StudioButton>
              <AnchoredPopover
                anchorRef={coverageTriggerRef}
                open={coverageOpen}
                id={coveragePopoverId}
                role="dialog"
                aria-label="Recording coverage"
                className="wf-history-popover"
                minWidth={300}
                maxHeight={420}
                onDismiss={() => setCoverageOpen(false)}
                onKeyDown={event => {
                  if (event.key === "Escape") {
                    event.preventDefault();
                    event.stopPropagation();
                    setCoverageOpen(false);
                    coverageTriggerRef.current?.focus();
                  }
                }}
              >
                <div className="wf-history-popover-content">
                  <HistoryCoverage coverage={window.page.coverage} readAt={window.page.readAt} scopeComplete={window.page.scopeComplete} scopeLimitation={window.page.scopeLimitation} />
                </div>
              </AnchoredPopover>
            </>
          ) : null}
        </div>
      </footer>
    </> : history.status === "loading" ? <p className="wf-history-empty" role="status">Loading recorded history…</p> : null}
  </section>;
}
