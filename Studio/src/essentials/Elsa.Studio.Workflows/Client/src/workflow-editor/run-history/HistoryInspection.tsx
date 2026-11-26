import { formatStudioDate, useStudioTimeZone } from "@elsa-workflows/studio-sdk";
import { lazy, Suspense, useCallback, useEffect, useLayoutEffect, useRef, useState, type KeyboardEvent as ReactKeyboardEvent } from "react";
import { ArrowLeft, BookmarkCheck, ChevronRight, CircleAlert, Compass, ExternalLink, GitBranch, ListTree, RefreshCw } from "lucide-react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { CopyableIdentifier, StudioButton } from "@elsa-workflows/studio-ui";
import { getWorkflowHistoryBookmark, getWorkflowHistoryEvent, listWorkflowHistoryBookmarks } from "../../api/runtimeHistory";
import type { RunHistorySelection, RuntimeHistoryBookmark, RuntimeHistoryBookmarkPage, RuntimeHistoryEvent, RuntimeHistoryEventDetails } from "../../runtimeHistoryTypes";
import { formatDuration, humanizeTypeName } from "../../workflowFormatting";
import { CopyValueButton } from "../executableShared";
import { clearExecutionFocus, requestExecutionFocus, useExecutionFocusRequest } from "../executionFocus";
import { HistoryCoverage, HistoryEventIcon, HistoryEventList, historyAuthorityLabel, historyCompactInstant, historyEventExplanation, historyEventLabel, historyEventTone, historyInstant, isKnownHistoryEvent } from "./historyPresentation";
import { useHistoryResource } from "./useHistoryResource";
import { useRunHistory } from "./useRunHistory";
import { runtimeValueContextId } from "../RuntimeValueEvidence";

const HistoryValueInspection = lazy(async () => ({ default: (await import("./HistoryValueInspection")).HistoryValueInspection }));

export interface PreparedHistoryEvent {
  contextIdentity: number | string; workflowExecutionId: string; historyEventId: string; value: RuntimeHistoryEventDetails;
}
export function HistoryInspection({ context, workflowExecutionId, selection, onSelect, onBack, onActivity, onFocusTimeline, onIncident, refreshKey = 0, navigate, focusOnSelection = true, preparedEvent, onEventUnavailable, variant = "panel", currentActivityExecutionId }: {
  context: StudioEndpointContext; workflowExecutionId: string; selection: Exclude<RunHistorySelection, null>;
  onSelect(selection: RunHistorySelection): void; onBack?(): void; onActivity?(activityExecutionId: string): void;
  focusOnSelection?: boolean;
  preparedEvent?: PreparedHistoryEvent;
  onEventUnavailable?(target: Pick<PreparedHistoryEvent, "contextIdentity" | "workflowExecutionId" | "historyEventId">): void;
  refreshKey?: string | number; onFocusTimeline?(activityExecutionId: string): void; onIncident?(incidentId: string): void; navigate?(path: string): void;
  variant?: "panel" | "inline";
  currentActivityExecutionId?: string;
}) {
  useStudioTimeZone();
  const container = useRef<HTMLDivElement>(null);
  const focusedSelection = useRef<string | null>(null);
  const focusRequest = useExecutionFocusRequest();
  const shouldFocus = focusOnSelection && variant !== "inline";
  const focusKey = JSON.stringify([runtimeValueContextId(context), workflowExecutionId, selection.kind, selection.id]);
  useLayoutEffect(() => {
    requestExecutionFocus(focusRequest, shouldFocus && focusedSelection.current !== focusKey ? focusKey : null, {
      scope: focusKey, identity: focusKey,
      origin: document.activeElement instanceof HTMLElement ? document.activeElement : null
    });
    const intent = focusRequest.intent;
    return () => clearExecutionFocus(focusRequest, intent);
  }, [focusKey, shouldFocus, focusRequest]);
  const focusReady = useCallback(() => {
    const intent = focusRequest.intent;
    if (!shouldFocus || focusedSelection.current === focusKey || intent?.targetId !== focusKey) return;
    const heading = container.current?.querySelector<HTMLElement>("[data-history-heading]");
    if (!heading) return;
    focusedSelection.current = focusKey;
    clearExecutionFocus(focusRequest, intent);
    // Navigate only once the exact recorded target has rendered. Bring its inspection
    // context to the start of the usable pane, including stacked narrow layouts.
    heading.focus({ preventScroll: true });
    heading.closest<HTMLElement>(".wf-history-inspection")?.scrollIntoView?.({ block: "start", inline: "nearest" });
  }, [focusKey, shouldFocus, focusRequest]);
  const prepared = preparedEvent?.contextIdentity === runtimeValueContextId(context) && preparedEvent.workflowExecutionId === workflowExecutionId
    && preparedEvent.historyEventId === selection.id && preparedEvent.value.event.workflowExecutionId === workflowExecutionId
    && preparedEvent.value.event.historyEventId === selection.id ? preparedEvent : undefined;
  return <div ref={container}>{selection.kind === "event" ? <HistoryEventInspection key={`${runtimeValueContextId(context)}:${workflowExecutionId}:${selection.id}`} context={context} workflowExecutionId={workflowExecutionId}
    historyEventId={selection.id} preparedEvent={prepared} onEventUnavailable={onEventUnavailable} refreshKey={refreshKey} onSelect={onSelect} onBack={onBack} onActivity={onActivity} onFocusTimeline={onFocusTimeline} onIncident={onIncident} navigate={navigate} onReady={focusReady} variant={variant} currentActivityExecutionId={currentActivityExecutionId} />
    : <HistoryBookmarkInspection key={`${runtimeValueContextId(context)}:${workflowExecutionId}:${selection.id}`} context={context} workflowExecutionId={workflowExecutionId}
      bookmarkId={selection.id} refreshKey={refreshKey} onSelect={onSelect} onBack={onBack ?? (() => {})} onActivity={onActivity} onReady={focusReady} />}</div>;
}

function HistoryFact({ label, value, copyValue, title, mono = false }: {
  label: string; value?: string | number | null; copyValue?: string; title?: string; mono?: boolean;
}) {
  const [message, setMessage] = useState("");
  const empty = value === undefined || value === null || value === "";
  const rawCopy = copyValue ?? (typeof value === "string" ? value : undefined);
  return <div className="wf-history-fact-row"><dt>{label}</dt><dd>{empty ? <span className="wf-history-fact-empty">Not recorded</span> : <>
    <span className="wf-history-fact-value" title={title ?? copyValue}>{mono ? <code>{value}</code> : value}</span>
    <span className="wf-history-fact-tools">
      {message ? <span className="wf-history-copy-state" role="status">{message}</span> : null}
      {rawCopy ? <CopyValueButton value={rawCopy} ariaLabel={`Copy ${label.toLowerCase()}`} copiedLabel={label} onCopied={() => setMessage("Copied")}
        onCopyFailed={() => setMessage("Copy failed")} /> : null}
    </span>
  </>}</dd></div>;
}

const metadataLabels: Record<string, string> = {
  "history.reasonCode": "Reason code", "history.outcomeNames": "Recorded outcomes", "history.stimulusType": "Stimulus type",
  "history.outcomeCaptureState": "Outcome capture", "history.outcomeCount": "Outcome count", "history.outcomeContentHash": "Outcome content hash",
  "history.resumeTargetId": "Resume target", "history.selectedBookmarkId": "Selected bookmark", "history.timerId": "Timer",
  "history.scheduledAt": "Scheduled instant", "history.controlKind": "Control kind", "history.incidentFailureType": "Failure type",
  "history.relatedActivityExecutionId": "Related execution", "history.compensationEntryId": "Compensation entry",
  "history.valuePhase": "Value phase", "history.valueEvaluationId": "Value evaluation",
  "history.originalCheckpoint": "Original checkpoint", "history.capturePolicyId": "Capture policy",
  "history.durableStateAddress": "Durable state address", "history.operation": "Recorded operation", "history.lifecycle": "State lifecycle",
  "history.storage": "State storage policy", "history.sourceCapturedAt": "Source captured instant"
};

function HistoryEventInspection({ context, workflowExecutionId, historyEventId, onSelect, onBack, onActivity, onFocusTimeline, onIncident, navigate, preparedEvent, onEventUnavailable, refreshKey, onReady, variant = "panel", currentActivityExecutionId }: {
  context: StudioEndpointContext; workflowExecutionId: string; historyEventId: string; onSelect(selection: RunHistorySelection): void;
  onBack?(): void; onActivity?(id: string): void; onFocusTimeline?(id: string): void; onIncident?(id: string): void; navigate?(path: string): void;
  preparedEvent?: PreparedHistoryEvent;
  refreshKey: string | number; onReady(): void;
  onEventUnavailable?(target: Pick<PreparedHistoryEvent, "contextIdentity" | "workflowExecutionId" | "historyEventId">): void;
  variant?: "panel" | "inline";
  currentActivityExecutionId?: string;
}) {
  const identity = JSON.stringify([workflowExecutionId, "event", historyEventId]);
  const resource = useHistoryResource(context, identity, signal => getWorkflowHistoryEvent(context, workflowExecutionId, historyEventId, signal), false, true, refreshKey,
    preparedEvent ? { contextIdentity: preparedEvent.contextIdentity, identity, value: preparedEvent.value } : undefined);
  const unavailable = useRef(onEventUnavailable); unavailable.current = onEventUnavailable;
  useEffect(() => {
    if (resource.status === "unavailable") unavailable.current?.({ contextIdentity: runtimeValueContextId(context), workflowExecutionId, historyEventId });
  }, [context, historyEventId, resource.status, workflowExecutionId]);
  const event = resource.value?.event;
  useEffect(() => { if (event) onReady(); }, [event, onReady]);
  const valueTriggers = useRef(new Map<string, HTMLButtonElement>());
  const [valueTarget, setValueTarget] = useState<string | null>(null);
  const [showSuccessors, setShowSuccessors] = useState(false);
  const showCause = () => {
    const cause = event?.causation;
    if (!cause?.historyEventId) return;
    if (cause.workflowExecutionId && cause.workflowExecutionId !== workflowExecutionId) {
      navigate?.(`/workflows/instances/${encodeURIComponent(cause.workflowExecutionId)}?view=timeline&historyEvent=${encodeURIComponent(cause.historyEventId)}`);
    } else onSelect({ kind: "event", id: cause.historyEventId });
  };
  let valueIds: string[] = [];
  try { const parsed: unknown = JSON.parse(event?.metadata["history.valueEvidenceIds"] ?? "[]"); if (Array.isArray(parsed)) valueIds = [...new Set(parsed.filter((item): item is string => typeof item === "string" && !!item))]; } catch { /* Unknown metadata remains unavailable, never an inline payload. */ }
  const relatedRun = event?.metadata["history.relatedWorkflowExecutionId"] || event?.metadata["history.childWorkflowId"];
  const explanation = event ? historyEventExplanation(event) : null;
  const recordedMetadata = event ? Object.entries(event.metadata).filter(([key]) => metadataLabels[key]) : [];
  const occurredAt = event ? (event.occurredAtKnown === false ? null : event.occurredAt) : null;
  const inline = variant === "inline";
  const sameActivity = Boolean(inline && currentActivityExecutionId && event?.activityExecutionId === currentActivityExecutionId);
  const showActivityAction = Boolean(event?.activityExecutionId && onActivity && !sameActivity);
  const showFocusTimelineAction = Boolean(event?.activityExecutionId && onFocusTimeline && !sameActivity);
  const hasActions = Boolean(event && (
    showActivityAction
    || event.bookmarkId
    || event.metadata["history.selectedBookmarkId"]
    || (event.incidentId && onIncident)
    || event.causation?.historyEventId
    || showFocusTimelineAction
    || (relatedRun && navigate)
  ));
  const hasHeaderNotes = Boolean(event && (
    !isKnownHistoryEvent(event)
    || event.schemaVersion !== 1
    || explanation
    || event.metadata["history.outcomeCaptureState"] === "size-omitted"
  ));

  return <section className={`wf-history-inspection${inline ? " wf-history-inspection-inline" : ""}`} aria-label="History event details">
    {!inline && onBack ? (
      <div className="wf-history-nav-bar">
        <StudioButton type="button" variant="ghost" size="sm" className="wf-history-back" onClick={onBack}><ArrowLeft size={14} aria-hidden="true" /> Back to activity inspection</StudioButton>
      </div>
    ) : null}
    <div className="wf-history-inspection-body">
      {!inline ? (
        <header className="wf-history-header-card">
          <div className="wf-history-header-band">
            <span className="wf-history-header-icon" data-tone={event ? historyEventTone(event) : "neutral"}>
              {event ? <HistoryEventIcon event={event} size={17} /> : null}
            </span>
            <div className="wf-history-header-title">
              <span className="wf-history-eyebrow">Recorded event</span>
              <h3 data-history-heading tabIndex={-1}>{event ? historyEventLabel(event) : "History event"}</h3>
              {event ? <p className="wf-history-header-subtitle">{historyAuthorityLabel(event.authority)}</p> : null}
            </div>
            {event ? (
              <div className="wf-history-header-badges">
                <span className="wf-history-seq-pill" title={`Recorded position #${event.sequence}`}>#{event.sequence}</span>
                {event.metadata["history.status"] ? <span className="wf-history-badge" data-status={event.metadata["history.status"]}>{event.metadata["history.status"]}</span> : null}
              </div>
            ) : null}
          </div>
          {event && hasHeaderNotes ? (
            <div className="wf-history-header-notes">
              {!isKnownHistoryEvent(event) ? <p className="wf-history-note">This event kind is unrecognized. Its safe common recorded fields remain inspectable.</p> : null}
              {event.schemaVersion !== 1 ? <p className="wf-history-note">This event uses an unrecognized schema version. Only its safe common recorded fields are available here.</p> : null}
              {explanation ? <p className="wf-history-explanation">{explanation}</p> : null}
              {event.metadata["history.outcomeCaptureState"] === "size-omitted" ? <p className="wf-history-note" role="status">Full outcome detail was not captured within the metadata size policy. This completion remains recorded; its outcome count and content hash are available in recorded metadata.</p> : null}
            </div>
          ) : null}
        </header>
      ) : event ? (
        <>
          <div className="wf-history-inline-meta">
            <span className="wf-history-header-subtitle">{historyAuthorityLabel(event.authority)}</span>
            <span className="wf-history-seq-pill" title={`Recorded position #${event.sequence}`}>#{event.sequence}</span>
          </div>
          {hasHeaderNotes ? (
            <div className="wf-history-inline-notes">
              {!isKnownHistoryEvent(event) ? <p className="wf-history-note">This event kind is unrecognized. Its safe common recorded fields remain inspectable.</p> : null}
              {event.schemaVersion !== 1 ? <p className="wf-history-note">This event uses an unrecognized schema version. Only its safe common recorded fields are available here.</p> : null}
              {explanation ? <p className="wf-history-explanation">{explanation}</p> : null}
              {event.metadata["history.outcomeCaptureState"] === "size-omitted" ? <p className="wf-history-note" role="status">Full outcome detail was not captured within the metadata size policy. This completion remains recorded; its outcome count and content hash are available in recorded metadata.</p> : null}
            </div>
          ) : null}
        </>
      ) : null}

      {resource.error ? <p className="wf-history-message" role="status"><span>{resource.error}</span> <StudioButton type="button" size="sm" onClick={resource.retry}>Retry event</StudioButton></p> : null}

      {event && resource.value ? <>
        <div className="wf-history-card">
          <dl className="wf-history-facts">
            <HistoryFact label="Recorded position" value={event.sequence} mono />
            <HistoryFact label="Occurred" value={occurredAt ? historyCompactInstant(occurredAt) : null} copyValue={occurredAt ?? undefined} title={occurredAt ? historyInstant(occurredAt) : undefined} />
            {event.metadata["history.status"] ? <HistoryFact label="Recorded status" value={event.metadata["history.status"]} /> : null}
            {event.executableNodeId ? <HistoryFact label="Activity node" value={event.executableNodeId} mono /> : null}
            {event.metadata["history.valuePhase"] ? <HistoryFact label="Value phase" value={event.metadata["history.valuePhase"]} /> : null}
          </dl>
        </div>

        {hasActions ? (
          <div className="wf-history-lifecycle-links">
            {showActivityAction ? <StudioButton type="button" size="sm" className="wf-history-action-btn" onClick={() => onActivity!(event.activityExecutionId!)}><Compass size={14} aria-hidden="true" /><span>Inspect owning execution →</span></StudioButton> : null}
            {event.bookmarkId ? <StudioButton type="button" size="sm" className="wf-history-action-btn" onClick={() => onSelect({ kind: "bookmark", id: event.bookmarkId! })}><BookmarkCheck size={14} aria-hidden="true" /><span>Inspect bookmark →</span></StudioButton> : null}
            {event.metadata["history.selectedBookmarkId"] ? <StudioButton type="button" size="sm" className="wf-history-action-btn" onClick={() => onSelect({ kind: "bookmark", id: event.metadata["history.selectedBookmarkId"] })}><BookmarkCheck size={14} aria-hidden="true" /><span>Inspect selected bookmark →</span></StudioButton> : null}
            {event.incidentId && onIncident ? <StudioButton type="button" size="sm" className="wf-history-action-btn" onClick={() => onIncident(event.incidentId!)}><CircleAlert size={14} aria-hidden="true" /><span>Inspect incident →</span></StudioButton> : null}
            {event.causation?.historyEventId ? <StudioButton type="button" size="sm" className="wf-history-action-btn" onClick={showCause} disabled={!!event.causation.workflowExecutionId && event.causation.workflowExecutionId !== workflowExecutionId && !navigate}><GitBranch size={14} aria-hidden="true" /><span>Inspect causal event →</span></StudioButton> : null}
            {showFocusTimelineAction ? <StudioButton type="button" size="sm" className="wf-history-action-btn" onClick={() => onFocusTimeline!(event.activityExecutionId!)}><ListTree size={14} aria-hidden="true" /><span>Focus this execution in timeline</span></StudioButton> : null}
            {relatedRun && navigate ? <StudioButton type="button" size="sm" className="wf-history-action-btn" onClick={() => navigate(`/workflows/instances/${encodeURIComponent(relatedRun)}?view=timeline`)}><ExternalLink size={14} aria-hidden="true" /><span>Open related run →</span></StudioButton> : null}
          </div>
        ) : null}

        {valueIds.length ? (
          <section className="wf-history-card wf-history-values-card">
            <div className="wf-history-card-header">
              <span>Recorded values</span>
              <small>{valueIds.length}</small>
            </div>
            <p className="wf-history-card-note">{valueIds.length} phase-specific value reference{valueIds.length === 1 ? "" : "s"}. Values load only when requested through their existing inspection policy.</p>
            <ul className="wf-history-value-list">
              {valueIds.map(id => <li key={id}>
                <code>{id}</code>
                {event.activityExecutionId ? <StudioButton ref={element => { if (element) valueTriggers.current.set(id, element); else valueTriggers.current.delete(id); }} type="button" size="sm" aria-expanded={valueTarget === id} onClick={() => setValueTarget(valueTarget === id ? null : id)}>Inspect recorded value →</StudioButton> : <span className="wf-history-fact-empty">Owning execution unavailable.</span>}
              </li>)}
            </ul>
          </section>
        ) : null}

        {valueTarget && valueIds.includes(valueTarget) && event.activityExecutionId ? <Suspense fallback={<p className="wf-history-note" role="status">Loading recorded value view…</p>}><HistoryValueInspection key={valueTarget} context={context} workflowExecutionId={workflowExecutionId} activityExecutionId={event.activityExecutionId} evidenceId={valueTarget} onClose={() => { const id = valueTarget; setValueTarget(null); requestAnimationFrame(() => valueTriggers.current.get(id)?.focus()); }} /></Suspense> : null}

        <details className="wf-history-disclosure wf-history-technical">
          <summary><ChevronRight size={14} className="wf-history-disclosure-chevron" aria-hidden="true" /><span>Technical details</span></summary>
          <div className="wf-history-disclosure-body">
            <dl className="wf-history-facts">
              <HistoryFact label="Event ID" value={event.historyEventId} mono />
              <HistoryFact label="Recorded" value={historyInstant(event.recordedAt)} />
              <HistoryFact label="Kind" value={`${event.name} · schema ${event.schemaVersion}`} mono />
              <HistoryFact label="Category" value={event.category} />
              <HistoryFact label="Authority" value={event.authority} />
              {event.activityExecutionId ? <HistoryFact label="Activity execution" value={event.activityExecutionId} mono /> : null}
              {event.bookmarkId ? <HistoryFact label="Bookmark ID" value={event.bookmarkId} mono /> : null}
              {event.incidentId ? <HistoryFact label="Incident ID" value={event.incidentId} mono /> : null}
              {event.parentActivityExecutionId ? <HistoryFact label="Parent execution" value={event.parentActivityExecutionId} mono /> : null}
              {event.executionScopeId ? <HistoryFact label="Execution scope" value={event.executionScopeId} mono /> : null}
              <HistoryFact label="Logical checkpoint" value={event.checkpointId} mono />
              <HistoryFact label="Checkpoint ordinal" value={event.checkpointOrdinal} mono />
              <HistoryFact label="Atomic commit group" value={event.physicalCommitId} mono />
              {event.attemptId ? <HistoryFact label="Attempt" value={event.attemptId} mono /> : null}
              {event.iterationId ? <HistoryFact label="Iteration" value={event.iterationId} mono /> : null}
              {relatedRun ? <HistoryFact label="Related run" value={relatedRun} mono /> : null}
              {event.causation?.deliveryId ? <HistoryFact label="Delivery" value={event.causation.deliveryId} mono /> : null}
              {event.causation?.commandId ? <HistoryFact label="Command" value={event.causation.commandId} mono /> : null}
              {event.causation?.schedulerWorkItemId ? <HistoryFact label="Scheduled work" value={event.causation.schedulerWorkItemId} mono /> : null}
            </dl>
            {event.physicalCommitId ? <p className="wf-history-card-note">Events in one atomic group became durable together. Their local ordinal does not imply separately committed intermediate states.</p> : null}
          </div>
        </details>

        {recordedMetadata.length ? (
          <details className="wf-history-disclosure">
            <summary><ChevronRight size={14} className="wf-history-disclosure-chevron" aria-hidden="true" /><span>Recorded metadata</span><small>{recordedMetadata.length}</small></summary>
            <div className="wf-history-disclosure-body">
              <dl className="wf-history-facts">
                {recordedMetadata.map(([key, value]) => <HistoryFact key={key} label={metadataLabels[key]} value={value} mono={/Hash|Id|Address|Target/.test(key)} />)}
              </dl>
            </div>
          </details>
        ) : null}

        <div className="wf-history-disclosure-card" data-open={showSuccessors || undefined}>
          <button type="button" className="wf-history-disclosure-trigger" aria-expanded={showSuccessors} onClick={() => setShowSuccessors(value => !value)}>
            <ChevronRight size={14} className="wf-history-disclosure-chevron" aria-hidden="true" />
            <span>Find causal successors in this run</span>
          </button>
          {showSuccessors ? <div className="wf-history-disclosure-body"><CausalSuccessors context={context} workflowExecutionId={workflowExecutionId} historyEventId={event.historyEventId} onSelect={onSelect} /></div> : null}
        </div>

        <HistoryCoverage variant="disclosure" coverage={resource.value.coverage} readAt={resource.value.readAt} />
      </> : resource.status === "loading" ? <p className="wf-history-note" role="status">Loading exact recorded event…</p> : null}
    </div>
  </section>;
}

function CausalSuccessors({ context, workflowExecutionId, historyEventId, onSelect }: {
  context: StudioEndpointContext; workflowExecutionId: string; historyEventId: string; onSelect(selection: RunHistorySelection): void;
}) {
  const history = useRunHistory(context, workflowExecutionId, { search: historyEventId });
  const value = history.value;
  const items = value?.items.filter(item => item.causation?.historyEventId === historyEventId
    && (!item.causation.workflowExecutionId || item.causation.workflowExecutionId === workflowExecutionId)) ?? [];
  return <section className="wf-history-successors" aria-label="Recorded causal successors">
    <p className="wf-history-card-note">Explicit causal references in this run. Other runs require their own authorized inspection.</p>
    {history.error ? <p className="wf-history-message" role="status"><span>{history.error}</span> <StudioButton type="button" size="sm" onClick={history.refresh}>Retry causal search</StudioButton></p> : null}
    {value ? <><HistoryEventList compact items={items} unloadedAfterSequence={value.next ? value.snapshotUpperSequence : undefined} onSelect={item => onSelect({ kind: "event", id: item.historyEventId })} />
      {!items.length ? <p className="wf-history-card-note">No recorded causal successors in this searched range.</p> : null}
      <div className="wf-history-successors-actions">
        {value.next ? <StudioButton type="button" size="sm" disabled={history.loading} onClick={history.loadMore}>Continue searching causal history</StudioButton> : null}
        {value.pending.length ? <StudioButton type="button" size="sm" onClick={history.showNew}>Show new causal observations</StudioButton> : null}
      </div>
      <HistoryCoverage variant="disclosure" coverage={value.page.coverage} readAt={value.page.readAt} scopeComplete={value.page.scopeComplete} scopeLimitation={value.page.scopeLimitation} />
    </> : history.loading ? <p className="wf-history-card-note" role="status">Searching recorded causal references…</p> : null}
  </section>;
}

export function bookmarkEligibilityLabel(value: string) {
  const labels: Record<string, string> = { eligible: "Eligible", expired: "Expired", "not-active": "Not active", ineligible: "Not eligible", unavailable: "Unavailable", unknown: "Unknown" };
  return labels[value] ?? `Unrecognized eligibility (${value})`;
}

function BookmarkRecord({ item, onSelect }: { item: RuntimeHistoryBookmark; onSelect(selection: RunHistorySelection): void }) {
  const bookmark = item.bookmark;
  return <article className="wf-history-bookmark-record"><div className="wf-history-bookmark-title"><strong><BookmarkCheck size={15} aria-hidden="true" /> {humanizeTypeName(bookmark.stimulusType) || "Recorded wait"}</strong><span className="wf-history-badge" data-status={item.status}>{item.status}</span></div>
    <p><code>{bookmark.identity.bookmarkId}</code></p>
    <p>{item.priorState === "Unknown" ? "Creation metadata unavailable" : `Created ${historyInstant(bookmark.createdAt)}`}</p>
    <p>Current eligibility: {bookmarkEligibilityLabel(item.eligibility)} · observed {historyInstant(item.eligibilityObservedAt)}</p>
    {item.endedAt ? <p>{item.status} {historyInstant(item.endedAt)}</p> : null}
    <StudioButton type="button" size="sm" onClick={() => onSelect({ kind: "bookmark", id: bookmark.identity.bookmarkId })}>View lifecycle →</StudioButton>
  </article>;
}

function bookmarkMoment(value: string) {
  const time = new Date(value);
  return Number.isFinite(time.getTime()) ? formatStudioDate(time) : "Invalid recorded time";
}

function HistoryBookmarkInspection({ context, workflowExecutionId, bookmarkId, onSelect, onBack, onActivity, refreshKey, onReady }: {
  context: StudioEndpointContext; workflowExecutionId: string; bookmarkId: string; onSelect(selection: RunHistorySelection): void; refreshKey: string | number; onReady(): void;
  onBack(): void; onActivity?(id: string): void;
}) {
  const resource = useHistoryResource(context, JSON.stringify([workflowExecutionId, "bookmark", bookmarkId]), signal => getWorkflowHistoryBookmark(context, workflowExecutionId, bookmarkId, signal), true, true, refreshKey);
  const item = resource.value?.bookmark;
  const bookmark = item?.bookmark;
  useEffect(() => { if (bookmark) onReady(); }, [bookmark, onReady]);
  const openAtObservation = item && (item.status === "Active" || item.status === "Unknown")
    && ["eligible", "expired", "ineligible"].includes(item.eligibility);
  const endAt = item?.endedAt ?? (openAtObservation ? item?.eligibilityObservedAt : null);
  const age = item && bookmark && item.priorState !== "Unknown" && endAt ? formatDuration(bookmark.createdAt, endAt) : "";
  const eligibilityNote = item?.eligibility === "expired"
    ? "Expiry makes this handle unmatchable. It does not wake, time out or cancel the activity."
    : item?.eligibility === "unavailable" || item?.eligibility === "unknown"
      ? "Current eligibility cannot be established. A missing handle does not prove consumption."
      : null;

  return <section className="wf-history-inspection" aria-label="Bookmark lifecycle" aria-busy={resource.loading}>
    <div className="wf-history-nav-bar">
      <StudioButton type="button" variant="ghost" size="sm" className="wf-history-back" onClick={onBack}><ArrowLeft size={14} aria-hidden="true" /> Back to activity inspection</StudioButton>
      <StudioButton type="button" variant="ghost" size="sm" className="wf-history-nav-refresh" onClick={resource.retry} disabled={resource.loading} title="Refresh bookmark observation"><RefreshCw size={13} aria-hidden="true" />Refresh bookmark observation</StudioButton>
    </div>
    <div className="wf-history-inspection-body">
      {resource.loading && item ? <p className="wf-history-status-strip">Refreshing this exact bookmark. Showing its previous recorded observation until the read completes.</p> : null}
      <header className="wf-history-header-card">
        <div className="wf-history-header-band">
          <span className="wf-history-header-icon" data-tone={item?.status === "Consumed" ? "success" : item?.status === "Active" ? "warning" : "accent"}>
            <BookmarkCheck size={17} aria-hidden="true" />
          </span>
          <div className="wf-history-header-title">
            <span className="wf-history-eyebrow">Bookmark lifecycle</span>
            <h3 data-history-heading tabIndex={-1}>{bookmark ? humanizeTypeName(bookmark.stimulusType) || "Recorded wait" : "Bookmark"}</h3>
            {item ? <p className="wf-history-header-subtitle">current eligibility: {bookmarkEligibilityLabel(item.eligibility)}</p> : null}
          </div>
          {item ? (
            <div className="wf-history-header-badges">
              <span className="wf-history-badge" data-status={item.status}>{item.status}</span>
            </div>
          ) : null}
        </div>
        {eligibilityNote ? <div className="wf-history-header-notes"><p className="wf-history-note">{eligibilityNote}</p></div> : null}
      </header>

      {resource.error ? <p className="wf-history-message" role="status"><span>{resource.error}</span> <StudioButton type="button" size="sm" onClick={resource.retry}>Retry bookmark</StudioButton></p> : null}

      {item && bookmark && resource.value ? <>
        <div className="wf-history-card">
          <dl className="wf-history-facts">
            <HistoryFact label="Created" value={item.priorState === "Unknown" ? null : bookmarkMoment(bookmark.createdAt)} copyValue={item.priorState === "Unknown" ? undefined : bookmark.createdAt} />
            <HistoryFact label="Ended" copyValue={item.endedAt ?? undefined} value={item.endedAt ? bookmarkMoment(item.endedAt) : item.endedHistoryEventId || ["Consumed", "Removed", "Superseded"].includes(item.status) ? "End time not recorded" : "No end transition recorded"} />
            <HistoryFact label="Recorded reason" value={item.reason} />
            <HistoryFact label="Handle lifetime" value={age || "Unknown"} />
          </dl>
        </div>

        <div className="wf-history-lifecycle-links">
          {item.endedHistoryEventId ? <StudioButton type="button" size="sm" className="wf-history-action-btn" onClick={() => onSelect({ kind: "event", id: item.endedHistoryEventId! })}>Inspect {item.status.toLowerCase()} event →</StudioButton> : null}
          {item.createdHistoryEventId ? <StudioButton type="button" size="sm" className="wf-history-action-btn" onClick={() => onSelect({ kind: "event", id: item.createdHistoryEventId! })}>Inspect creation event →</StudioButton> : <p className="wf-history-note">Creation event not captured{item.priorState === "CreationMetadata" ? "; creation metadata comes from the recorded handle" : ""}.</p>}
          {item.selectedBookmarkId && item.selectedBookmarkId !== bookmarkId ? <StudioButton type="button" size="sm" className="wf-history-action-btn" onClick={() => onSelect({ kind: "bookmark", id: item.selectedBookmarkId! })}>Inspect winning bookmark →</StudioButton> : null}
          <StudioButton type="button" size="sm" className="wf-history-action-btn" onClick={() => onSelect({ kind: "event", id: item.lastHistoryEventId })}>Inspect last recorded transition →</StudioButton>
        </div>

        <details className="wf-history-disclosure wf-history-technical">
          <summary><ChevronRight size={14} className="wf-history-disclosure-chevron" aria-hidden="true" /><span>Technical details</span></summary>
          <div className="wf-history-disclosure-body">
            <dl className="wf-history-facts">
              <HistoryFact label="Created (exact)" value={item.priorState === "Unknown" ? null : historyInstant(bookmark.createdAt)} />
              <HistoryFact label="Ended (exact)" value={item.endedAt ? historyInstant(item.endedAt) : null} />
              <HistoryFact label="Bookmark ID" value={bookmark.identity.bookmarkId} mono />
              <div className="wf-history-fact-row"><dt>Owning execution</dt><dd>{onActivity ? <StudioButton type="button" size="sm" onClick={() => onActivity(bookmark.identity.activityExecutionId)}>{bookmark.identity.activityExecutionId} →</StudioButton> : <code>{bookmark.identity.activityExecutionId}</code>}</dd></div>
              <HistoryFact label="Stimulus type" value={bookmark.stimulusType} mono />
              <HistoryFact label="Stimulus hash" value={bookmark.stimulusHash} mono />
              <HistoryFact label="Resume target" value={bookmark.identity.resumeTargetId} mono />
              <HistoryFact label="Expiry" value={bookmark.expiresAt ? historyInstant(bookmark.expiresAt) : "No expiry"} />
              <HistoryFact label="Eligibility observed" value={historyInstant(item.eligibilityObservedAt)} />
              {item.eligibilityReason ? <HistoryFact label="Eligibility reason" value={item.eligibilityReason.replaceAll("-", " ")} /> : null}
            </dl>
          </div>
        </details>

        <BookmarkLifecycleEvents context={context} workflowExecutionId={workflowExecutionId} bookmarkId={bookmarkId} onSelect={onSelect} />
        <HistoryCoverage variant="disclosure" coverage={resource.value.coverage} readAt={resource.value.readAt} />
      </> : resource.status === "loading" ? <p className="wf-history-note" role="status">Loading exact bookmark lifecycle…</p> : null}
    </div>
  </section>;
}

function BookmarkLifecycleEvents({ context, workflowExecutionId, bookmarkId, onSelect }: { context: StudioEndpointContext; workflowExecutionId: string; bookmarkId: string; onSelect(selection: RunHistorySelection): void }) {
  // A bookmark's directly linked lifecycle is available above; use the shared bounded event reader for its retained history.
  return <section className="wf-history-card wf-history-bookmark-events">
    <div className="wf-history-card-header"><span>Recorded bookmark events</span></div>
    <div className="wf-history-card-body">
      <BookmarkEventJournal context={context} workflowExecutionId={workflowExecutionId} bookmarkId={bookmarkId} onSelect={onSelect} />
    </div>
  </section>;
}

function BookmarkEventJournal({ context, workflowExecutionId, bookmarkId, onSelect }: { context: StudioEndpointContext; workflowExecutionId: string; bookmarkId: string; onSelect(selection: RunHistorySelection): void }) {
  const history = useRunHistory(context, workflowExecutionId, { bookmarkId });
  return <>{history.error ? <p role="status">{history.error}</p> : null}{history.value ? <>
    <HistoryEventList compact items={history.value.items} onSelect={event => onSelect({ kind: "event", id: event.historyEventId })} />
    {history.value.next ? <StudioButton type="button" onClick={history.loadMore} disabled={history.loading}>Load more bookmark events</StudioButton> : null}
    {history.value.pending.length ? <StudioButton type="button" onClick={history.showNew}>Show new bookmark events</StudioButton> : null}
  </> : null}</>;
}

export function ActivityBookmarkHistory({ context, workflowExecutionId, activityExecutionId, onSelect, refreshKey = 0 }: {
  context: StudioEndpointContext; workflowExecutionId: string; activityExecutionId: string; onSelect(selection: RunHistorySelection): void; refreshKey?: string | number;
}) {
  useStudioTimeZone();
  const [cursor, setCursor] = useState<string | null>(null);
  const [accumulated, setAccumulated] = useState<{ key: string; items: RuntimeHistoryBookmark[]; scopeComplete: boolean; scopeLimitation?: string | null; page: RuntimeHistoryBookmarkPage } | null>(null);
  const resource = useHistoryResource<RuntimeHistoryBookmarkPage>(context, JSON.stringify([workflowExecutionId, activityExecutionId, "bookmarks", cursor]),
    signal => listWorkflowHistoryBookmarks(context, workflowExecutionId, { activityExecutionId, continuationToken: cursor }, signal), true, true, refreshKey);
  const scopeKey = JSON.stringify([resource.sessionIdentity, workflowExecutionId, activityExecutionId]);
  const previousScope = useRef(scopeKey);
  useEffect(() => {
    if (previousScope.current !== scopeKey) { previousScope.current = scopeKey; setCursor(null); setAccumulated(null); }
  }, [scopeKey]);
  useEffect(() => {
    if (!resource.value) return;
    const page = resource.value;
    setAccumulated(previous => {
      const existing = previous?.key === scopeKey && cursor ? previous.items : [];
      const map = new Map(existing.map(item => [item.bookmark.identity.bookmarkId, item]));
      page.items.forEach(item => map.set(item.bookmark.identity.bookmarkId, item));
      return { key: scopeKey, page, items: [...map.values()].sort((a, b) => a.firstRecordedSequence - b.firstRecordedSequence),
        scopeComplete: page.scopeComplete && (cursor && previous?.key === scopeKey ? previous.scopeComplete : true),
        scopeLimitation: previous?.key === scopeKey && cursor ? previous.scopeLimitation ?? page.scopeLimitation : page.scopeLimitation };
    });
  }, [cursor, resource.value, scopeKey]);
  useEffect(() => { if (resource.status === "unavailable") setAccumulated(null); }, [resource.status]);
  const retryBookmarks = () => { if (cursor) setCursor(null); else resource.retry(); };
  const allowed = resource.status !== "unavailable";
  const items = allowed && accumulated?.key === scopeKey ? accumulated.items : [];
  const page = allowed ? resource.value ?? (accumulated?.key === scopeKey ? accumulated.page : null) : null;
  return <section className="wf-activity-history" aria-label="Activity bookmark history" aria-busy={resource.loading}>
    <p className="wf-history-note">Bookmarks created by this execution. Historical handles remain after retirement or consumption. Eligibility is an observation, not permission to resume.</p>
    <p className="wf-history-note">Each handle has its own observation time. A bounded refresh rereads the first page and replaces older loaded pages only after that read succeeds.</p>
    <StudioButton type="button" onClick={retryBookmarks} disabled={resource.loading}>Refresh bookmarks</StudioButton>
    {resource.loading && page ? <p className="wf-history-note">Reading bookmarks. Previously loaded observations remain visible until confirmed.</p> : null}
    {resource.error ? <p role="status">{resource.error} <StudioButton type="button" onClick={retryBookmarks}>Retry bookmarks</StudioButton></p> : null}
    {page ? <><p>{page.activeCount === null || page.activeCount === undefined ? `${items.filter(item => item.status === "Active").length} recorded active in loaded records` : `${page.activeCount} recorded active`}
      {" · "}{page.totalCount === null || page.totalCount === undefined ? `${items.length} loaded; lifetime total unavailable` : `${page.totalCount} recorded handles`}</p><p className="wf-history-note">Counts observed {historyInstant(page.readAt)}; loaded handles retain their individual observation times.</p>
      {items.map(item => <BookmarkRecord key={item.bookmark.identity.bookmarkId} item={item} onSelect={onSelect} />)}
      {!items.length ? <p>No bookmarks recorded in this available range.</p> : null}
      {page.nextContinuationToken ? <StudioButton type="button" disabled={resource.loading} onClick={() => setCursor(page.nextContinuationToken!)}>Load more bookmarks</StudioButton> : null}
      <HistoryCoverage coverage={page.coverage} readAt={page.readAt} scopeComplete={accumulated?.scopeComplete ?? page.scopeComplete} scopeLimitation={accumulated?.scopeLimitation ?? page.scopeLimitation} />
    </> : resource.status === "loading" ? <p role="status">Loading recorded bookmarks…</p> : null}
  </section>;
}

export function ActivityHistoryJournal({ context, workflowExecutionId, activityExecutionId, selectedEventId, onSelectEvent, onSelectHistory, onOpenTimeline, onActivity, onIncident, navigate, refreshKey = 0 }: {
  context: StudioEndpointContext; workflowExecutionId: string; activityExecutionId: string;
  selectedEventId?: string | null; onSelectEvent?(event: RuntimeHistoryEvent): void;
  onSelectHistory?(selection: RunHistorySelection): void;
  onOpenTimeline?(activityExecutionId: string): void;
  onActivity?(activityExecutionId: string): void;
  onIncident?(incidentId: string): void;
  navigate?(path: string): void;
  refreshKey?: string | number;
}) {
  useStudioTimeZone();
  const [includeDescendants, setIncludeDescendants] = useState(false);
  const [expandedEventId, setExpandedEventId] = useState<string | null>(null);
  const sectionRef = useRef<HTMLElement>(null);
  const history = useRunHistory(context, workflowExecutionId, { activityExecutionId, includeDescendants });
  const value = history.value;
  const inlineMode = !onSelectEvent;
  const handleSelectEvent = (event: RuntimeHistoryEvent) => {
    if (onSelectEvent) {
      onSelectEvent(event);
      return;
    }
    setExpandedEventId(current => current === event.historyEventId ? null : event.historyEventId);
  };
  const handleInlineNavigation = (selection: RunHistorySelection) => {
    if (selection?.kind === "event" && value?.items.some(item => item.historyEventId === selection.id)) {
      setExpandedEventId(selection.id);
      requestAnimationFrame(() => {
        const trigger = [...(sectionRef.current?.querySelectorAll<HTMLButtonElement>(".wf-history-event") ?? [])]
          .find(button => button.dataset.historyEventId === selection.id);
        trigger?.scrollIntoView?.({ block: "nearest" });
        trigger?.focus();
      });
      return;
    }
    onSelectHistory?.(selection);
  };
  const handleKeyDown = (event: ReactKeyboardEvent<HTMLElement>) => {
    if (!inlineMode || !expandedEventId || event.key !== "Escape") return;
    const expandedId = expandedEventId;
    const expandedItem = [...(sectionRef.current?.querySelectorAll<HTMLLIElement>(".wf-history-event-item.is-expanded") ?? [])][0];
    if (!expandedItem || !(event.target instanceof Node) || !expandedItem.contains(event.target)) return;
    event.preventDefault();
    event.stopPropagation();
    setExpandedEventId(null);
    requestAnimationFrame(() => {
      const trigger = [...(sectionRef.current?.querySelectorAll<HTMLButtonElement>(".wf-history-event") ?? [])]
        .find(button => button.dataset.historyEventId === expandedId);
      trigger?.focus();
    });
  };
  return <section ref={sectionRef} className="wf-activity-history" aria-label="Activity journal" onKeyDown={handleKeyDown}>
    <p className="wf-history-note">This execution{includeDescendants ? " and its descendants" : " only, including directly related delivery observations"}.</p>
    <details className="wf-history-disclosure wf-history-technical"><summary><ChevronRight size={14} className="wf-history-disclosure-chevron" aria-hidden="true" /><span>Execution identity</span></summary><div className="wf-history-disclosure-body"><CopyableIdentifier label="Activity execution" value={activityExecutionId} scope={context} /></div></details>
    <label className="wf-history-checkbox"><input type="checkbox" checked={includeDescendants} onChange={event => setIncludeDescendants(event.target.checked)} />Include descendants</label>
    {history.error ? <p role="status">{history.error} <StudioButton type="button" size="sm" onClick={history.refresh}>Retry journal</StudioButton></p> : null}
    {value ? <><HistoryCoverage variant="disclosure" coverage={value.page.coverage} readAt={value.page.readAt} scopeComplete={value.page.scopeComplete} scopeLimitation={value.page.scopeLimitation} />
      <HistoryEventList
        compact
        hideActivityExecutionId={includeDescendants ? undefined : activityExecutionId}
        unloadedAfterSequence={value.next ? value.snapshotUpperSequence : undefined}
        items={value.items}
        selectedEventId={inlineMode ? undefined : selectedEventId}
        expandedEventId={inlineMode ? expandedEventId : undefined}
        onSelect={handleSelectEvent}
        renderExpanded={inlineMode ? event => (
          <HistoryInspection
            context={context}
            workflowExecutionId={workflowExecutionId}
            selection={{ kind: "event", id: event.historyEventId }}
            variant="inline"
            focusOnSelection={false}
            currentActivityExecutionId={activityExecutionId}
            refreshKey={refreshKey}
            onSelect={handleInlineNavigation}
            onActivity={onActivity}
            onFocusTimeline={onOpenTimeline}
            onIncident={onIncident}
            navigate={navigate}
          />
        ) : undefined}
      />
      {!value.items.length ? <p>No recorded events in this activity range.</p> : null}
      {value.pending.length ? <StudioButton type="button" size="sm" onClick={history.showNew}>Show {value.pending.length} new journal events</StudioButton> : null}
      {value.next ? <StudioButton type="button" size="sm" disabled={history.loading} onClick={history.loadMore}>Load more journal events</StudioButton> : null}
    </> : history.loading ? <p role="status">Loading activity journal…</p> : null}
    {onOpenTimeline ? <p><StudioButton type="button" size="sm" onClick={() => onOpenTimeline(activityExecutionId)}>Focus this execution in timeline</StudioButton></p> : null}
  </section>;
}
