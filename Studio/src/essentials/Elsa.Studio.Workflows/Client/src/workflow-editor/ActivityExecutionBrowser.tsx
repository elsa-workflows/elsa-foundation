import { useEffect, useId, useRef, useState } from "react";
import { ChevronDown, ChevronLeft, ChevronRight, CornerDownRight } from "lucide-react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { browseActivityExecutions, supportsActivityExecutionBrowsing, type ActivityExecutionBrowseEntry, type ActivityExecutionBrowsePage, type ActivityExecutionBrowseRequest } from "../api/runtime";
import type { ActivityExecutionStateSummary } from "../workflowTypes";
import { formatDate, formatDuration } from "../workflowFormatting";
import { runtimeValueContextId } from "./RuntimeValueEvidence";
import { WorkflowStatusBadge } from "./WorkflowStatusBadge";
import { useExecutionFocusRequest, requestExecutionFocus, continueExecutionFocus, clearExecutionFocus, type ExecutionFocusRequest, type ExecutionFocusIntent, type ExecutionFocusOperation } from "./executionFocus";
import "./activityExecutionBrowser.css";

export type ExecutionPageState = { key: string; status: "loading" | "ready" | "unsupported" | "denied" | "failed"; page: ActivityExecutionBrowsePage | null };

export function useExecutionPage(context: StudioEndpointContext, workflowExecutionId: string,
  request: ActivityExecutionBrowseRequest, enabled = true, refreshKey = 0, initialPage?: ActivityExecutionBrowsePage) {
  const requestKey = JSON.stringify(request);
  const key = `${runtimeValueContextId(context)}:${workflowExecutionId}:${requestKey}`;
  const [state, setState] = useState<ExecutionPageState>({ key, status: "loading", page: null });
  const [retry, setRetry] = useState(0);
  const requestedRevision = useRef(""); requestedRevision.current = `${refreshKey}:${retry}`;
  const refreshPage = useRef<(() => void) | null>(null);
  useEffect(() => {
    if (!enabled) return;
    const controller = new AbortController();
    let running = false;
    let confirmed: string | null = null;
    setState({ key, status: "loading", page: null });
    const load = async () => {
      if (running || controller.signal.aborted || confirmed === requestedRevision.current) return;
      running = true;
      try {
        do {
          const revision = requestedRevision.current;
          setState(previous => previous.key === key && previous.status === "ready" ? previous : { key, status: "loading", page: null });
          try {
            const supported = await supportsActivityExecutionBrowsing(context);
            if (controller.signal.aborted) return;
            if (!supported) setState({ key, status: "unsupported", page: null });
            else {
              const page = await browseActivityExecutions(context, workflowExecutionId, JSON.parse(requestKey) as ActivityExecutionBrowseRequest, controller.signal);
              if (controller.signal.aborted) return;
              setState({ key, status: "ready", page });
            }
          } catch (error) {
            if (controller.signal.aborted) return;
            const status = typeof error === "object" && error !== null && "status" in error ? Number(error.status) : 0;
            setState({ key, status: status === 401 || status === 403 || status === 404 ? "denied" : "failed", page: null });
          }
          confirmed = revision;
        } while (confirmed !== requestedRevision.current);
      } finally { running = false; }
    };
    const start = () => { void load(); }; refreshPage.current = start; start();
    return () => { controller.abort(); if (refreshPage.current === start) refreshPage.current = null; };
  }, [context, enabled, key, requestKey, workflowExecutionId]);
  useEffect(() => { refreshPage.current?.(); }, [refreshKey, retry]);
  const current = state.key === key ? state : { key, status: "loading" as const, page: null };
  return { ...(current.status === "loading" && initialPage ? { key, status: "ready" as const, page: initialPage } : current), retry: () => setRetry(value => value + 1) };
}

function PageFeedback({ state }: { state: ReturnType<typeof useExecutionPage> }) {
  if (state.status === "ready") return null;
  return <div className="wf-execution-feedback" role="status">
    {state.status === "loading" ? "Loading recorded executions…" : state.status === "unsupported" ? "Execution browsing is unavailable on this host." : state.status === "denied" ? "Execution history is unavailable for this context." : "Execution history could not be loaded."}
    {state.status === "failed" ? <button type="button" onClick={state.retry}>Retry</button> : null}
  </div>;
}

function ExecutionRow({ entry, selected, label, onSelect, onIncident }: {
  entry: ActivityExecutionBrowseEntry; selected?: boolean; label: string;
  onSelect(): void; onIncident?(execution: ActivityExecutionStateSummary): void;
}) {
  const execution = entry.execution;
  const durationMs = execution.startedAt && execution.completedAt ? Date.parse(execution.completedAt) - Date.parse(execution.startedAt) : null;
  const duration = durationMs !== null && durationMs >= 0 && durationMs < 1000 ? `${durationMs} ms` : formatDuration(execution.startedAt, execution.completedAt) || "Duration unavailable";
  return <li className="wf-execution-row" data-selected={selected || undefined}>
    <button type="button" aria-current={selected ? "true" : undefined} onClick={onSelect}>
      <span className="wf-execution-row-title">{label}<WorkflowStatusBadge status={execution.status} subStatus={execution.subStatus} /></span>
      <span className="wf-execution-row-meta"><time dateTime={execution.startedAt ?? execution.scheduledAt}>{formatDate(execution.startedAt ?? execution.scheduledAt)}</time><span>{duration}</span></span>
      {entry.attempt ? <span className="wf-execution-row-meta">Attempt {entry.attempt.attemptNumber}</span> : null}
    </button>
    {(execution.incidentCount ?? 0) > 0 ? <button className="wf-execution-incident" type="button" onClick={() => onIncident?.(execution)} aria-label={`Open issues for ${label}`}>{execution.incidentCount} issues</button> : null}
  </li>;
}

function PageNavigation({ page, onPage }: { page: ActivityExecutionBrowsePage; onPage(cursor: string): void }) {
  if (!page.previousCursor && !page.nextCursor) return null;
  return <div className="wf-execution-paging" aria-label="Execution pages">
    <button type="button" disabled={!page.previousCursor} onClick={() => page.previousCursor && onPage(page.previousCursor)}><ChevronLeft size={14} />Earlier</button>
    <span>{page.items[0]?.position ?? 0}–{page.items.at(-1)?.position ?? 0} of {page.totalCount}</span>
    <button type="button" disabled={!page.nextCursor} onClick={() => page.nextCursor && onPage(page.nextCursor)}>Later<ChevronRight size={14} /></button>
  </div>;
}

function occurrenceLabel(entry: ActivityExecutionBrowseEntry) {
  return entry.execution.iterationId && entry.iterationPosition && !entry.attempt
    ? `Iteration ${entry.iterationPosition}` : `Execution ${entry.position}`;
}

export function ActivityExecutionNavigator({ context, workflowExecutionId, executableNodeId, ancestorActivityExecutionId,
  selectedActivityExecutionId, parentActivityExecutionId, refreshKey, onResolve, onSelect, onIncident, collapseOnSelect = false, requireExplicitSelection = false, initialPage, contextLabel, hideSingleton = false, restoreChoiceFocus, captureFocusOperation }: {
  initialPage?: ActivityExecutionBrowsePage;
  contextLabel?: string;
  hideSingleton?: boolean;
  restoreChoiceFocus?: { request: ExecutionFocusRequest; ready: boolean };
  captureFocusOperation?(): ExecutionFocusOperation;
  context: StudioEndpointContext; workflowExecutionId: string; executableNodeId: string;
  ancestorActivityExecutionId?: string; selectedActivityExecutionId?: string; refreshKey: number;
  parentActivityExecutionId?: string;
  collapseOnSelect?: boolean;
  requireExplicitSelection?: boolean;
  onResolve(execution: ActivityExecutionStateSummary | null): void;
  onSelect(execution: ActivityExecutionStateSummary): void;
  onIncident(execution: ActivityExecutionStateSummary): void;
}) {
  const [expanded, setExpanded] = useState(false);
  const [popupHeight, setPopupHeight] = useState<number>();
  const [cursor, setCursor] = useState<{ query: string; value: string } | null>(null);
  const queryIdentity = JSON.stringify([executableNodeId, ancestorActivityExecutionId, parentActivityExecutionId, selectedActivityExecutionId, requireExplicitSelection]);
  const listId = useId();
  const triggerRef = useRef<HTMLButtonElement>(null);
  const previousRef = useRef<HTMLButtonElement>(null);
  const nextRef = useRef<HTMLButtonElement>(null);
  const focusIdentity = JSON.stringify([runtimeValueContextId(context), workflowExecutionId, executableNodeId]);
  const focusScope = JSON.stringify([runtimeValueContextId(context), workflowExecutionId, executableNodeId, ancestorActivityExecutionId, parentActivityExecutionId]);
  const requestFocus = (id: string | null | undefined, direction?: "previous" | "next") => {
    if (!restoreChoiceFocus) return;
    requestExecutionFocus(restoreChoiceFocus.request, id, { direction, scope: focusScope, identity: focusIdentity,
      from: selected?.execution.activityExecutionId,
      origin: direction === "previous" ? previousRef.current : direction === "next" ? nextRef.current : triggerRef.current }, captureFocusOperation?.());
  };
  const selectWithFocus = (execution: ActivityExecutionStateSummary, expected?: ExecutionFocusIntent) => {
    const request = restoreChoiceFocus?.request;
    const bind = request && expected && expected.targetId === execution.activityExecutionId && request.intent === expected ? continueExecutionFocus(request) : (_operation?: ExecutionFocusOperation) => {};
    try { onSelect(execution); }
    catch (error) { if (request && expected) clearExecutionFocus(request, expected); throw error; }
    bind(captureFocusOperation?.());
  };
  const listRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (expanded) listRef.current?.querySelector<HTMLButtonElement>('[aria-current="true"]')?.focus();
  }, [expanded]);
  const pageState = useExecutionPage(context, workflowExecutionId, {
    executableNodeId, ...(ancestorActivityExecutionId ? { ancestorActivityExecutionId } : {}),
    ...(parentActivityExecutionId ? { parentActivityExecutionId } : {}),
    ...(cursor?.query === queryIdentity ? { cursor: cursor.value } : selectedActivityExecutionId ? { anchorActivityExecutionId: selectedActivityExecutionId } : {})
  }, true, refreshKey, cursor?.query === queryIdentity ? undefined : initialPage);
  const resolved = useRef(onResolve);
  resolved.current = onResolve;
  useEffect(() => {
    if (pageState.status === "denied") { resolved.current(null); return; }
    if (pageState.status !== "ready" || !pageState.page || cursor?.query === queryIdentity) return;
    const selected = selectedActivityExecutionId
      ? pageState.page.items.find(item => item.execution.activityExecutionId === selectedActivityExecutionId)
      : !requireExplicitSelection || pageState.page.totalCount === 1 ? pageState.page.items.at(-1) : undefined;
    resolved.current(selected?.execution ?? null);
  }, [cursor, pageState.key, pageState.page, pageState.status, queryIdentity, requireExplicitSelection, selectedActivityExecutionId]);
  const page = pageState.page;
  const retainedSelection = useRef<{ query: string; entry: ActivityExecutionBrowseEntry } | null>(null);
  const pageSelection = page?.items.find(item => item.execution.activityExecutionId === selectedActivityExecutionId) ?? (!selectedActivityExecutionId && (!requireExplicitSelection || page?.totalCount === 1) ? page?.items.at(-1) : undefined);
  useEffect(() => { if (pageSelection) retainedSelection.current = { query: queryIdentity, entry: pageSelection }; }, [pageSelection, queryIdentity]);
  const retainSelection = pageState.status !== "ready" || cursor?.query === queryIdentity;
  const selected = pageState.status === "denied" ? undefined : pageSelection ?? (retainSelection && retainedSelection.current?.query === queryIdentity ? retainedSelection.current.entry : undefined);
  const navigate = async (id: string | null | undefined) => {
    if (!id) return;
    const available = page?.items.find(item => item.execution.activityExecutionId === id);
    if (available) selectWithFocus(available.execution, restoreChoiceFocus?.request.intent);
    else {
      // Selecting a neighbor beyond the loaded page uses an exact anchor, never a wider run download.
      setNeighbor({ id, source: queryIdentity, focusIntent: restoreChoiceFocus?.request.intent });
    }
  };
  const [neighbor, setNeighbor] = useState<{ id: string; source: string; focusIntent?: ExecutionFocusIntent } | null>(null);
  const neighborState = useExecutionPage(context, workflowExecutionId, { executableNodeId,
    ...(parentActivityExecutionId ? { parentActivityExecutionId } : {}),
    ...(ancestorActivityExecutionId ? { ancestorActivityExecutionId } : {}), anchorActivityExecutionId: neighbor?.id, limit: 1 }, Boolean(neighbor && neighbor.source === queryIdentity), refreshKey);
  const selectRef = useRef(selectWithFocus); selectRef.current = selectWithFocus;
  useEffect(() => {
    if (neighbor && neighbor.source !== queryIdentity) { setNeighbor(null); return; }
    if (neighbor && neighborState.status === "denied") { resolved.current(null); setNeighbor(null); return; }
    if (neighbor && neighborState.status === "ready") {
      const entry = neighborState.page?.items.find(item => item.execution.activityExecutionId === neighbor.id);
      if (entry) selectRef.current(entry.execution, neighbor.focusIntent);
      else if (restoreChoiceFocus && neighbor.focusIntent) clearExecutionFocus(restoreChoiceFocus.request, neighbor.focusIntent);
      setNeighbor(null);
    }
  }, [neighbor, neighborState.page, neighborState.status, queryIdentity, restoreChoiceFocus]);
  useEffect(() => {
    const request = restoreChoiceFocus?.request;
    const controls = [previousRef.current, nextRef.current, triggerRef.current];
    return () => {
      const intent = request?.intent;
      if (request && intent && !intent.selectionStarted && controls.includes(intent.origin as HTMLButtonElement | null)) clearExecutionFocus(request, intent);
    };
  }, [focusIdentity, restoreChoiceFocus?.request]);
  useEffect(() => {
    const request = restoreChoiceFocus?.request;
    if (!request?.current) return;
    const intent = request.intent;
    if (intent && (!intent.isCurrent() || intent.identity !== focusIdentity || intent.targetId !== request.current
      || (!intent.ownerBound && intent.scope !== focusScope))) {
      clearExecutionFocus(request, intent);
      return;
    }
    const errors = ["denied", "failed", "unsupported"];
    // A terminal neighbor read failed before owner selection started; it cannot retain a future focus claim.
    if (intent && neighbor && neighbor.focusIntent === intent && errors.includes(neighborState.status)) {
      clearExecutionFocus(request, intent);
      return;
    }
    // An owning prepare may transiently change ancestor filters before its exact destination commits.
    if (intent && (!intent.selectionStarted || !intent.isReady())) return;
    const rejected = selectedActivityExecutionId === request.current && errors.includes(pageState.status);
    const replaced = Boolean(intent?.from && selectedActivityExecutionId && selectedActivityExecutionId !== intent.from && selectedActivityExecutionId !== request.current);
    const unavailable = pageState.status === "ready" && selectedActivityExecutionId === request.current && !selected;
    if (rejected || replaced || unavailable) {
      clearExecutionFocus(request, intent);
      return;
    }
    if (!restoreChoiceFocus?.ready || neighbor || request.current !== selected?.execution.activityExecutionId || pageState.status !== "ready") return;
    const origin = intent?.direction === "previous" ? previousRef.current : intent?.direction === "next" ? nextRef.current : triggerRef.current;
    const target = origin?.disabled ? triggerRef.current : origin;
    // Recover only the initiating control or native focus loss; later owner-observed focus wins.
    if (target && (document.activeElement === document.body || document.activeElement === origin)) target.focus();
    clearExecutionFocus(request, intent);
  }, [focusIdentity, focusScope, neighbor, neighborState.status, pageState.status, queryIdentity, restoreChoiceFocus, selected, selectedActivityExecutionId]);
  // Keep all reads and resolution effects active: a singleton can become unavailable or gain another occurrence.
  if (hideSingleton && pageState.status === "ready" && page?.totalCount === 1 && selected && !neighbor
    && !page.previousCursor && !page.nextCursor) return null;
  const currentLabel = selected ? occurrenceLabel(selected) : pageState.status === "ready" ? `${page?.totalCount ?? 0} recorded executions` : "Executions";
  const iteration = selected && Boolean(selected.execution.iterationId && selected.iterationPosition && !selected.attempt);
  return <section className="wf-execution-browser" aria-label={contextLabel ? `${contextLabel} occurrences` : "Activity executions"}>
    <div className="wf-execution-navigator">
      <button ref={previousRef} type="button" aria-label={contextLabel ? `Previous occurrence of ${contextLabel}` : "Previous activity execution"} disabled={!selected?.previousActivityExecutionId || Boolean(neighbor)} onClick={() => { requestFocus(selected?.previousActivityExecutionId, "previous"); void navigate(selected?.previousActivityExecutionId); }}><ChevronLeft size={16} /></button>
      <button ref={triggerRef} className="wf-execution-choice" type="button" aria-label={contextLabel ? `Choose occurrence of ${contextLabel}: ${currentLabel}${selected && page ? `, recorded execution ${selected.position} of ${page.totalCount}` : ""}` : undefined} aria-expanded={expanded} aria-controls={listId} onClick={() => {
        if (collapseOnSelect) setPopupHeight(Math.max(120, Math.min(360, window.innerHeight - (triggerRef.current?.getBoundingClientRect().bottom ?? 0) - 20)));
        setExpanded(value => !value);
      }}>
        <span className="wf-execution-choice-copy">{contextLabel ? <span className="wf-execution-context" title={contextLabel}>{contextLabel}</span> : null}<span>{currentLabel}{selected && !iteration && page ? ` of ${page.totalCount}` : null}</span></span>
        {selected && iteration && page ? <span className="wf-execution-position" aria-label={`Recorded execution ${selected.position} of ${page.totalCount}`}>{selected.position} / {page.totalCount}</span> : null}<ChevronDown size={14} />
      </button>
      <button ref={nextRef} type="button" aria-label={contextLabel ? `Next occurrence of ${contextLabel}` : "Next activity execution"} disabled={!selected?.nextActivityExecutionId || Boolean(neighbor)} onClick={() => { requestFocus(selected?.nextActivityExecutionId, "next"); void navigate(selected?.nextActivityExecutionId); }}><ChevronRight size={16} /></button>
    </div>
    <PageFeedback state={pageState} />
    {neighbor ? <PageFeedback state={neighborState} /> : null}
    {pageState.status === "ready" && !selected && !cursor ? <p role="status">{selectedActivityExecutionId ? "This execution is not available in this scope." : page?.totalCount ? "Choose a recorded execution to inspect its evidence." : "No execution recorded in this scope."}</p> : null}
    {expanded && page ? <div ref={listRef} id={listId} className="wf-execution-list-container" style={collapseOnSelect ? { maxHeight: popupHeight } : undefined} onKeyDown={event => {
      if (event.key === "Escape") { event.preventDefault(); setExpanded(false); triggerRef.current?.focus(); }
    }}>
      <ol className="wf-execution-list" aria-label="Recorded activity executions">{page.items.map(entry => <ExecutionRow key={entry.execution.activityExecutionId} entry={entry} selected={entry.execution.activityExecutionId === selectedActivityExecutionId}
        label={occurrenceLabel(entry)} onSelect={() => {
          setCursor(null);
          if (collapseOnSelect) {
            setExpanded(false); triggerRef.current?.focus();
            // Resolution can remount the navigator; retain the intended chooser in this scope.
            requestFocus(entry.execution.activityExecutionId);
          }
          selectWithFocus(entry.execution, restoreChoiceFocus?.request.intent);
        }} onIncident={onIncident} />)}</ol>
      <PageNavigation page={page} onPage={value => setCursor({ query: queryIdentity, value })} />
      <p className="wf-execution-note">Recorded evidence · counts reflect this read</p>
    </div> : null}
  </section>;
}

export function ChildExecutionBrowser({ context, execution, refreshKey, labelFor, onEnter, onIncident }: {
  context: StudioEndpointContext; execution: ActivityExecutionStateSummary; refreshKey: number;
  labelFor(execution: ActivityExecutionStateSummary): string;
  onEnter(entry: ActivityExecutionBrowseEntry): void;
  onIncident(execution: ActivityExecutionStateSummary): void;
}) {
  const [expanded, setExpanded] = useState(false);
  const [cursor, setCursor] = useState<string>();
  const state = useExecutionPage(context, execution.workflowExecutionId, { parentActivityExecutionId: execution.activityExecutionId, cursor, limit: expanded ? 50 : 1 }, true, refreshKey);
  const groups = new Map<string, ActivityExecutionStateSummary>();
  for (const entry of state.page?.items ?? []) groups.set(entry.execution.executableNodeId, entry.execution);
  if (state.status === "ready" && state.page?.totalCount === 0) return null;
  return <section className="wf-execution-children" aria-label="Child executions">
    <button type="button" aria-expanded={expanded} onClick={() => setExpanded(value => !value)}><CornerDownRight size={15} />Child executions{state.page ? <span>{state.page.totalCount}</span> : null}<ChevronDown size={14} /></button>
    {expanded ? <>
      <PageFeedback state={state} />
      {state.status === "ready" && state.page?.totalCount === 0 ? <p>No child executions were recorded.</p> : null}
      {[...groups].map(([node, child]) => <ChildExecutionGroup key={node} context={context} execution={execution} node={node} label={labelFor(child)} refreshKey={refreshKey} onEnter={onEnter} onIncident={onIncident} />)}
      {state.page ? <PageNavigation page={state.page} onPage={setCursor} /> : null}
    </> : null}
  </section>;
}

function ChildExecutionGroup({ context, execution, node, label, refreshKey, onEnter, onIncident }: {
  context: StudioEndpointContext; execution: ActivityExecutionStateSummary; node: string; label: string; refreshKey: number;
  onEnter(entry: ActivityExecutionBrowseEntry): void; onIncident(execution: ActivityExecutionStateSummary): void;
}) {
  const [cursor, setCursor] = useState<string>();
  const state = useExecutionPage(context, execution.workflowExecutionId, { parentActivityExecutionId: execution.activityExecutionId, executableNodeId: node, cursor }, true, refreshKey);
  return <div className="wf-execution-child-group"><h5>{label}{state.page ? <span>{state.page.totalCount} recorded</span> : null}</h5><PageFeedback state={state} />
    <ol className="wf-execution-list" aria-label={`${label} child executions`}>{state.page?.items.map(entry => <ExecutionRow key={entry.execution.activityExecutionId} entry={entry}
      label={entry.execution.iterationId && entry.iterationPosition && !entry.attempt ? `Iteration ${entry.iterationPosition}` : `Execution ${entry.position}`} onSelect={() => onEnter(entry)} onIncident={onIncident} />)}</ol>
    {state.page ? <PageNavigation page={state.page} onPage={setCursor} /> : null}
  </div>;
}

export function ActivityExecutionInspectionPanel({ context, workflowExecutionId, nodeId, nodeLabel, exactId, seed, ancestorId,
  refreshKey, labelFor, onSelect, onEnter, onIncident, renderDetails, requireExplicitSelection = false, confirmedExecution, onUnavailable, initialPage, choiceFocus, captureFocusOperation }: {
  choiceFocus?: ExecutionFocusRequest;
  captureFocusOperation?(): ExecutionFocusOperation;
  initialPage?: ActivityExecutionBrowsePage;
  confirmedExecution?: ActivityExecutionStateSummary | null;
  onUnavailable?(): void;
  context: StudioEndpointContext; workflowExecutionId: string; nodeId: string; nodeLabel: string;
  requireExplicitSelection?: boolean;
  exactId?: string; seed: ActivityExecutionStateSummary | null; ancestorId?: string; refreshKey: number;
  labelFor(execution: ActivityExecutionStateSummary): string;
  onSelect(execution: ActivityExecutionStateSummary): void; onEnter(entry: ActivityExecutionBrowseEntry): void;
  onIncident(execution: ActivityExecutionStateSummary): void;
  renderDetails(execution: ActivityExecutionStateSummary, navigation: React.ReactNode, children: React.ReactNode): React.ReactNode;
}) {
  // A refresh of this same exact selection must not unmount its expanded evidence while the read is pending.
  const key = `${runtimeValueContextId(context)}:${workflowExecutionId}:${nodeId}:${ancestorId ?? ""}:${exactId ?? ""}`;
  const [resolved, setResolved] = useState<{ key: string; execution: ActivityExecutionStateSummary | null } | null>(null);
  const [supported, setSupported] = useState<{ context: StudioEndpointContext; value: boolean } | null>(null);
  const localChoiceFocus = useExecutionFocusRequest();
  const restoreChoiceFocus = choiceFocus ?? localChoiceFocus;
  useEffect(() => {
    let active = true;
    void Promise.resolve().then(() => supportsActivityExecutionBrowsing(context)).then(value => { if (active) setSupported({ context, value }); }).catch(() => {});
    return () => { active = false; };
  }, [context]);
  const execution = resolved?.key === key ? resolved.execution
    : confirmedExecution && confirmedExecution.activityExecutionId === exactId && confirmedExecution.workflowExecutionId === workflowExecutionId && confirmedExecution.executableNodeId === nodeId ? confirmedExecution
    : supported?.context === context && !supported.value ? seed : null;
  const navigation = <ActivityExecutionNavigator context={context} workflowExecutionId={workflowExecutionId} executableNodeId={nodeId}
    ancestorActivityExecutionId={ancestorId} selectedActivityExecutionId={exactId} refreshKey={refreshKey} requireExplicitSelection={requireExplicitSelection} initialPage={initialPage} hideSingleton collapseOnSelect
    restoreChoiceFocus={{ request: restoreChoiceFocus, ready: Boolean(execution) }} captureFocusOperation={captureFocusOperation}
    onResolve={value => { setResolved({ key, execution: value }); if (!value && confirmedExecution) onUnavailable?.(); if (value && !exactId) onSelect(value); }} onSelect={onSelect} onIncident={onIncident} />;
  const children = execution ? <ChildExecutionBrowser key={execution.activityExecutionId} context={context} execution={execution} refreshKey={refreshKey} labelFor={labelFor} onEnter={onEnter} onIncident={onIncident} /> : null;
  return execution ? renderDetails(execution, navigation, children) : <>
    <section className="wf-instance-section"><h4>{nodeLabel}</h4></section>{navigation}
  </>;
}


export { useScopedCanvasActivities } from "./scopedExecutionEvidence";

export function ExecutionScopeNavigator({ context, execution, label, refreshKey, onSelect, onUnavailable, onIncident, initialPage, choiceFocus, captureFocusOperation }: {
  initialPage?: ActivityExecutionBrowsePage;
  choiceFocus?: ExecutionFocusRequest;
  captureFocusOperation?(): ExecutionFocusOperation;
  label: string;
  context: StudioEndpointContext; execution: ActivityExecutionStateSummary; refreshKey: number;
  onSelect(execution: ActivityExecutionStateSummary): void; onUnavailable(): void;
  onIncident(execution: ActivityExecutionStateSummary): void;
}) {
  const localChoiceFocus = useExecutionFocusRequest();
  const focusRequest = choiceFocus ?? localChoiceFocus;
  return <div className="wf-execution-scope-occurrence">
    <ActivityExecutionNavigator context={context} workflowExecutionId={execution.workflowExecutionId}
      executableNodeId={execution.executableNodeId} parentActivityExecutionId={execution.parentActivityExecutionId ?? undefined}
      selectedActivityExecutionId={execution.activityExecutionId} refreshKey={refreshKey}
      collapseOnSelect hideSingleton contextLabel={label} initialPage={initialPage}
      restoreChoiceFocus={{ request: focusRequest, ready: true }} captureFocusOperation={captureFocusOperation}
      onResolve={value => { if (!value) onUnavailable(); }} onSelect={onSelect} onIncident={onIncident} />
  </div>;
}
