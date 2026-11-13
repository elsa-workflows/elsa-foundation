import { lazy, Suspense, useEffect, useRef, useState } from "react";
import { ArrowLeft, BookmarkCheck } from "lucide-react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { StudioButton } from "@elsa-workflows/studio-ui";
import { getWorkflowHistoryBookmark, getWorkflowHistoryEvent, listWorkflowHistoryBookmarks } from "../../api/runtimeHistory";
import type { RunHistorySelection, RuntimeHistoryBookmark, RuntimeHistoryBookmarkPage } from "../../runtimeHistoryTypes";
import { formatDuration, humanizeTypeName } from "../../workflowFormatting";
import { CopyValueButton } from "../executableShared";
import { HistoryCoverage, HistoryEventList, historyAuthorityLabel, historyEventExplanation, historyEventLabel, historyInstant, isKnownHistoryEvent } from "./historyPresentation";
import { ActivityHistoryJournal } from "./RunHistoryTimeline";
import { useHistoryResource } from "./useHistoryResource";
import { useRunHistory } from "./useRunHistory";

const HistoryValueInspection = lazy(async () => ({ default: (await import("./HistoryValueInspection")).HistoryValueInspection }));

export function HistoryInspection({ context, workflowExecutionId, selection, onSelect, onBack, onActivity, onFocusTimeline, onIncident, refreshKey = 0, navigate, focusOnSelection = true }: {
  context: StudioEndpointContext; workflowExecutionId: string; selection: Exclude<RunHistorySelection, null>;
  onSelect(selection: RunHistorySelection): void; onBack(): void; onActivity?(activityExecutionId: string): void;
  focusOnSelection?: boolean;
  refreshKey?: string | number; onFocusTimeline?(activityExecutionId: string): void; onIncident?(incidentId: string): void; navigate?(path: string): void;
}) {
  const container = useRef<HTMLDivElement>(null);
  useEffect(() => { if (focusOnSelection) container.current?.querySelector<HTMLElement>("[data-history-heading]")?.focus(); }, [focusOnSelection, selection.id, selection.kind, workflowExecutionId]);
  return <div ref={container}>{selection.kind === "event" ? <HistoryEventInspection key={`${workflowExecutionId}:${selection.id}`} context={context} workflowExecutionId={workflowExecutionId}
    historyEventId={selection.id} onSelect={onSelect} onBack={onBack} onActivity={onActivity} onFocusTimeline={onFocusTimeline} onIncident={onIncident} navigate={navigate} />
    : <HistoryBookmarkInspection key={`${workflowExecutionId}:${selection.id}`} context={context} workflowExecutionId={workflowExecutionId}
      bookmarkId={selection.id} refreshKey={refreshKey} onSelect={onSelect} onBack={onBack} onActivity={onActivity} />}</div>;
}

function HistoryFact({ label, value }: { label: string; value?: string | number | null }) {
  const [message, setMessage] = useState("");
  return <><dt>{label}</dt><dd>{value === undefined || value === null || value === "" ? "Not recorded" : <><span>{value}</span>
    {typeof value === "string" ? <CopyValueButton value={value} ariaLabel={`Copy ${label.toLowerCase()}`} copiedLabel={label} onCopied={() => setMessage("Copied")}
      onCopyFailed={() => setMessage("Copy failed")} /> : null}<span className="wf-history-copy-state" role="status">{message}</span></>}</dd></>;
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

function HistoryEventInspection({ context, workflowExecutionId, historyEventId, onSelect, onBack, onActivity, onFocusTimeline, onIncident, navigate }: {
  context: StudioEndpointContext; workflowExecutionId: string; historyEventId: string; onSelect(selection: RunHistorySelection): void;
  onBack(): void; onActivity?(id: string): void; onFocusTimeline?(id: string): void; onIncident?(id: string): void; navigate?(path: string): void;
}) {
  const resource = useHistoryResource(context, JSON.stringify([workflowExecutionId, "event", historyEventId]), signal => getWorkflowHistoryEvent(context, workflowExecutionId, historyEventId, signal), false);
  const event = resource.value?.event;
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
  return <section className="wf-history-inspection" aria-label="History event details">
    <StudioButton type="button" className="wf-history-back" onClick={onBack}><ArrowLeft size={14} aria-hidden="true" /> Back to activity inspection</StudioButton>
    <p className="wf-history-eyebrow">Recorded event</p><h3 data-history-heading tabIndex={-1}>{event ? historyEventLabel(event) : "History event"}</h3>
    {resource.error ? <p role="status">{resource.error} <StudioButton type="button" onClick={resource.retry}>Retry event</StudioButton></p> : null}
    {event && resource.value ? <>
      <p className="wf-history-note">{historyAuthorityLabel(event.authority)}</p>
      {!isKnownHistoryEvent(event) ? <p className="wf-history-note">This event kind is unrecognized. Its safe common recorded fields remain inspectable.</p> : null}
      {event.schemaVersion !== 1 ? <p className="wf-history-note">This event uses an unrecognized schema version. Only its safe common recorded fields are available here.</p> : null}
      {explanation ? <p>{explanation}</p> : null}
      {event.metadata["history.outcomeCaptureState"] === "size-omitted" ? <p className="wf-history-note" role="status">Full outcome detail was not captured within the metadata size policy. This completion remains recorded; its outcome count and content hash are available in recorded metadata.</p> : null}
      <dl className="wf-history-facts"><HistoryFact label="Recorded position" value={event.sequence} />
        <HistoryFact label="Occurred" value={historyInstant(event.occurredAtKnown === false ? null : event.occurredAt)} />
        {event.metadata["history.status"] ? <HistoryFact label="Recorded status" value={event.metadata["history.status"]} /> : null}
        {event.executableNodeId ? <HistoryFact label="Activity node" value={event.executableNodeId} /> : null}
        {event.metadata["history.valuePhase"] ? <HistoryFact label="Value phase" value={event.metadata["history.valuePhase"]} /> : null}
      </dl>
      <div className="wf-history-lifecycle-links">
        {event.activityExecutionId && onActivity ? <StudioButton type="button" onClick={() => onActivity(event.activityExecutionId!)}>Inspect owning execution →</StudioButton> : null}
        {event.bookmarkId ? <StudioButton type="button" onClick={() => onSelect({ kind: "bookmark", id: event.bookmarkId! })}>Inspect bookmark →</StudioButton> : null}
        {event.incidentId && onIncident ? <StudioButton type="button" onClick={() => onIncident(event.incidentId!)}>Inspect incident →</StudioButton> : null}
        {event.causation?.historyEventId ? <StudioButton type="button" onClick={showCause} disabled={!!event.causation.workflowExecutionId && event.causation.workflowExecutionId !== workflowExecutionId && !navigate}>Inspect causal event →</StudioButton> : null}
      </div>
      <details className="wf-history-technical"><summary>Technical details</summary><dl className="wf-history-facts">
        <HistoryFact label="Event ID" value={event.historyEventId} /><HistoryFact label="Recorded" value={historyInstant(event.recordedAt)} />
        <HistoryFact label="Kind" value={`${event.name} · schema ${event.schemaVersion}`} />
        <HistoryFact label="Category" value={event.category} /><HistoryFact label="Authority" value={event.authority} />
        {event.activityExecutionId ? <HistoryFact label="Activity execution" value={event.activityExecutionId} /> : null}
        {event.bookmarkId ? <HistoryFact label="Bookmark ID" value={event.bookmarkId} /> : null}
        {event.incidentId ? <HistoryFact label="Incident ID" value={event.incidentId} /> : null}
        {event.parentActivityExecutionId ? <HistoryFact label="Parent execution" value={event.parentActivityExecutionId} /> : null}
        {event.executionScopeId ? <HistoryFact label="Execution scope" value={event.executionScopeId} /> : null}
        <HistoryFact label="Logical checkpoint" value={event.checkpointId} /><HistoryFact label="Checkpoint ordinal" value={event.checkpointOrdinal} />
        <HistoryFact label="Atomic commit group" value={event.physicalCommitId} />
        {event.attemptId ? <HistoryFact label="Attempt" value={event.attemptId} /> : null}
        {event.iterationId ? <HistoryFact label="Iteration" value={event.iterationId} /> : null}
        {relatedRun ? <HistoryFact label="Related run" value={relatedRun} /> : null}
        {event.causation?.deliveryId ? <HistoryFact label="Delivery" value={event.causation.deliveryId} /> : null}
        {event.causation?.commandId ? <HistoryFact label="Command" value={event.causation.commandId} /> : null}
        {event.causation?.schedulerWorkItemId ? <HistoryFact label="Scheduled work" value={event.causation.schedulerWorkItemId} /> : null}
      </dl>{event.physicalCommitId ? <p className="wf-history-note">Events in one atomic group became durable together. Their local ordinal does not imply separately committed intermediate states.</p> : null}</details>
      {Object.entries(event.metadata).some(([key]) => metadataLabels[key]) ? <details><summary>Recorded metadata</summary><dl className="wf-history-facts">{Object.entries(event.metadata).filter(([key]) => metadataLabels[key]).map(([key, value]) => <HistoryFact key={key} label={metadataLabels[key]} value={value} />)}</dl></details> : null}
      {event.metadata["history.selectedBookmarkId"] ? <p><StudioButton type="button" onClick={() => onSelect({ kind: "bookmark", id: event.metadata["history.selectedBookmarkId"] })}>Inspect selected bookmark →</StudioButton></p> : null}
      <p><StudioButton type="button" aria-expanded={showSuccessors} onClick={() => setShowSuccessors(value => !value)}>Find causal successors in this run</StudioButton></p>
      {showSuccessors ? <CausalSuccessors context={context} workflowExecutionId={workflowExecutionId} historyEventId={event.historyEventId} onSelect={onSelect} /> : null}
      {relatedRun && navigate ? <p><StudioButton type="button" onClick={() => navigate(`/workflows/instances/${encodeURIComponent(relatedRun)}?view=timeline`)}>Open related run {relatedRun} →</StudioButton></p> : null}
      {valueIds.length ? <div><p>{valueIds.length} phase-specific value reference{valueIds.length === 1 ? "" : "s"}. Values load only when requested through their existing inspection policy.</p>
        <ul>{valueIds.map(id => <li key={id}><code>{id}</code>{event.activityExecutionId ? <StudioButton ref={element => { if (element) valueTriggers.current.set(id, element); else valueTriggers.current.delete(id); }} type="button" aria-expanded={valueTarget === id} onClick={() => setValueTarget(valueTarget === id ? null : id)}>Inspect recorded value →</StudioButton> : <span> Owning execution unavailable.</span>}</li>)}</ul>
      </div> : null}
      {valueTarget && valueIds.includes(valueTarget) && event.activityExecutionId ? <Suspense fallback={<p role="status">Loading recorded value view…</p>}><HistoryValueInspection key={valueTarget} context={context} workflowExecutionId={workflowExecutionId} activityExecutionId={event.activityExecutionId} evidenceId={valueTarget} onClose={() => { const id = valueTarget; setValueTarget(null); requestAnimationFrame(() => valueTriggers.current.get(id)?.focus()); }} /></Suspense> : null}
      {event.activityExecutionId && onFocusTimeline ? <p><StudioButton type="button" onClick={() => onFocusTimeline(event.activityExecutionId!)}>Focus this execution in timeline</StudioButton></p> : null}
      <HistoryCoverage coverage={resource.value.coverage} readAt={resource.value.readAt} />
    </> : resource.status === "loading" ? <p role="status">Loading exact recorded event…</p> : null}
  </section>;
}

function CausalSuccessors({ context, workflowExecutionId, historyEventId, onSelect }: {
  context: StudioEndpointContext; workflowExecutionId: string; historyEventId: string; onSelect(selection: RunHistorySelection): void;
}) {
  const history = useRunHistory(context, workflowExecutionId, { search: historyEventId });
  const value = history.value;
  const items = value?.items.filter(item => item.causation?.historyEventId === historyEventId
    && (!item.causation.workflowExecutionId || item.causation.workflowExecutionId === workflowExecutionId)) ?? [];
  return <section aria-label="Recorded causal successors"><p>Explicit causal references in this run. Other runs require their own authorized inspection.</p>
    {history.error ? <p role="status">{history.error} <StudioButton type="button" onClick={history.refresh}>Retry causal search</StudioButton></p> : null}
    {value ? <><HistoryEventList compact items={items} unloadedAfterSequence={value.next ? value.snapshotUpperSequence : undefined} onSelect={item => onSelect({ kind: "event", id: item.historyEventId })} />
      {!items.length ? <p>No recorded causal successors in this searched range.</p> : null}
      {value.next ? <StudioButton type="button" disabled={history.loading} onClick={history.loadMore}>Continue searching causal history</StudioButton> : null}
      {value.pending.length ? <StudioButton type="button" onClick={history.showNew}>Show new causal observations</StudioButton> : null}
      <HistoryCoverage coverage={value.page.coverage} readAt={value.page.readAt} scopeComplete={value.page.scopeComplete} scopeLimitation={value.page.scopeLimitation} />
    </> : history.loading ? <p role="status">Searching recorded causal references…</p> : null}
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
    <StudioButton type="button" onClick={() => onSelect({ kind: "bookmark", id: bookmark.identity.bookmarkId })}>View lifecycle →</StudioButton>
  </article>;
}

function HistoryBookmarkInspection({ context, workflowExecutionId, bookmarkId, onSelect, onBack, onActivity, refreshKey }: {
  context: StudioEndpointContext; workflowExecutionId: string; bookmarkId: string; onSelect(selection: RunHistorySelection): void; refreshKey: string | number;
  onBack(): void; onActivity?(id: string): void;
}) {
  const resource = useHistoryResource(context, JSON.stringify([workflowExecutionId, "bookmark", bookmarkId]), signal => getWorkflowHistoryBookmark(context, workflowExecutionId, bookmarkId, signal), true, true, refreshKey);
  const item = resource.value?.bookmark;
  const bookmark = item?.bookmark;
  const openAtObservation = item && (item.status === "Active" || item.status === "Unknown")
    && ["eligible", "expired", "ineligible"].includes(item.eligibility);
  const endAt = item?.endedAt ?? (openAtObservation ? item?.eligibilityObservedAt : null);
  const age = item && bookmark && item.priorState !== "Unknown" && endAt ? formatDuration(bookmark.createdAt, endAt) : "";
  return <section className="wf-history-inspection" aria-label="Bookmark lifecycle" aria-busy={resource.loading}>
    <StudioButton type="button" className="wf-history-back" onClick={onBack}><ArrowLeft size={14} aria-hidden="true" /> Back to activity inspection</StudioButton>
    <StudioButton type="button" onClick={resource.retry} disabled={resource.loading}>Refresh bookmark observation</StudioButton>
    {resource.loading && item ? <p className="wf-history-note">Refreshing this exact bookmark. Showing its previous recorded observation until the read completes.</p> : null}
    <p className="wf-history-eyebrow">Bookmark lifecycle</p><h3 data-history-heading tabIndex={-1}>{bookmark ? humanizeTypeName(bookmark.stimulusType) || "Recorded wait" : "Bookmark"}</h3>
    {resource.error ? <p role="status">{resource.error} <StudioButton type="button" onClick={resource.retry}>Retry bookmark</StudioButton></p> : null}
    {item && bookmark && resource.value ? <>
      <p><span className="wf-history-badge" data-status={item.status}>{item.status}</span> · current eligibility: {bookmarkEligibilityLabel(item.eligibility)}</p>
      <dl className="wf-history-facts"><HistoryFact label="Bookmark ID" value={bookmark.identity.bookmarkId} />
        <dt>Owning execution</dt><dd>{onActivity ? <StudioButton type="button" onClick={() => onActivity(bookmark.identity.activityExecutionId)}>{bookmark.identity.activityExecutionId} →</StudioButton> : bookmark.identity.activityExecutionId}</dd>
        <HistoryFact label="Stimulus type" value={bookmark.stimulusType} /><HistoryFact label="Stimulus hash" value={bookmark.stimulusHash} />
        <HistoryFact label="Resume target" value={bookmark.identity.resumeTargetId} />
        <HistoryFact label="Created" value={item.priorState === "Unknown" ? "Not recorded" : historyInstant(bookmark.createdAt)} />
        <HistoryFact label="Ended" value={item.endedAt ? historyInstant(item.endedAt) : item.endedHistoryEventId || ["Consumed", "Removed", "Superseded"].includes(item.status) ? "End time not recorded" : "No end transition recorded"} />
        <HistoryFact label="Recorded reason" value={item.reason} /><HistoryFact label="Handle lifetime" value={age || "Unknown"} />
        <HistoryFact label="Expiry" value={bookmark.expiresAt ? historyInstant(bookmark.expiresAt) : "No expiry"} />
        <HistoryFact label="Eligibility observed" value={historyInstant(item.eligibilityObservedAt)} />
        {item.eligibilityReason ? <HistoryFact label="Eligibility reason" value={item.eligibilityReason.replaceAll("-", " ")} /> : null}
      </dl>
      {item.eligibility === "expired" ? <p className="wf-history-note">Expiry makes this handle unmatchable. It does not wake, time out or cancel the activity.</p> : item.eligibility === "unavailable" || item.eligibility === "unknown" ? <p className="wf-history-note">Current eligibility cannot be established. A missing handle does not prove consumption.</p> : null}
      <div className="wf-history-lifecycle-links">
        {item.createdHistoryEventId ? <StudioButton type="button" onClick={() => onSelect({ kind: "event", id: item.createdHistoryEventId! })}>Inspect creation event →</StudioButton> : <p>Creation event not captured{item.priorState === "CreationMetadata" ? "; creation metadata comes from the recorded handle" : ""}.</p>}
        {item.endedHistoryEventId ? <StudioButton type="button" onClick={() => onSelect({ kind: "event", id: item.endedHistoryEventId! })}>Inspect {item.status.toLowerCase()} event →</StudioButton> : null}
        {item.selectedBookmarkId && item.selectedBookmarkId !== bookmarkId ? <StudioButton type="button" onClick={() => onSelect({ kind: "bookmark", id: item.selectedBookmarkId! })}>Inspect winning bookmark →</StudioButton> : null}
        <StudioButton type="button" onClick={() => onSelect({ kind: "event", id: item.lastHistoryEventId })}>Inspect last recorded transition →</StudioButton>
      </div>
      <BookmarkLifecycleEvents context={context} workflowExecutionId={workflowExecutionId} bookmarkId={bookmarkId} onSelect={onSelect} />
      <HistoryCoverage coverage={resource.value.coverage} readAt={resource.value.readAt} />
    </> : resource.status === "loading" ? <p role="status">Loading exact bookmark lifecycle…</p> : null}
  </section>;
}

function BookmarkLifecycleEvents({ context, workflowExecutionId, bookmarkId, onSelect }: { context: StudioEndpointContext; workflowExecutionId: string; bookmarkId: string; onSelect(selection: RunHistorySelection): void }) {
  // A bookmark's directly linked lifecycle is available above; use the shared bounded event reader for its retained history.
  return <div className="wf-history-note">Bookmark {bookmarkId} belongs to run {workflowExecutionId}. <BookmarkEventJournal context={context} workflowExecutionId={workflowExecutionId} bookmarkId={bookmarkId} onSelect={onSelect} /></div>;
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

export { ActivityHistoryJournal };
