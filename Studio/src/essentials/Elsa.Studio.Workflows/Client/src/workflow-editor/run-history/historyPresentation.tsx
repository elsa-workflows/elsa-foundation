import { Activity, BookmarkCheck, CircleAlert, CircleCheck, CirclePlay, Clock3, Inbox, ListPlus, Pause, RotateCcw, Workflow } from "lucide-react";
import type { RuntimeHistoryCoverage, RuntimeHistoryEvent } from "../../runtimeHistoryTypes";
import { formatDuration, humanizeTypeName } from "../../workflowFormatting";

const names: Record<string, string> = {
  WorkflowStarted: "Run started", WorkflowCompleted: "Run completed", WorkflowFaulted: "Run faulted", WorkflowCancelled: "Run cancelled",
  WorkflowSuspended: "Run suspended", ActivityScheduled: "Activity scheduled", ActivityStarted: "Activity started",
  ActivityAttemptClaimed: "Activity execution claimed", ActivityResumeClaimed: "Activity resumption claimed", ActivityResumeRetried: "Continuation delivery retried",
  ActivityCompleted: "Activity completed", ActivitySuspended: "Activity waiting", ActivityCancelled: "Activity cancelled", ActivityRecovered: "Activity recovered",
  ActivityFaulted: "Activity faulted", BookmarkCreated: "Bookmark created", BookmarkConsumed: "Bookmark consumed",
  BookmarkRetired: "Competing bookmark retired", BookmarkRemoved: "Bookmark removed", BookmarkStaleRemoved: "Stale bookmark removed",
  IncidentRecorded: "Incident recorded", IncidentResolved: "Incident resolved", IncidentResolutionBatchApplied: "Incident resolution applied",
  WorkflowDispatchChanged: "Workflow dispatch updated", ValueCaptured: "Value observation recorded", RuntimeAlterationJob: "Run alteration recorded",
  StimulusReceived: "Stimulus received", StimulusAccepted: "Stimulus accepted", StimulusRejected: "Stimulus rejected",
  DeliveryReceived: "Delivery received", DeliveryAccepted: "Delivery accepted", DeliveryRejected: "Delivery rejected",
  DeliveryDispatched: "Delivery dispatched", DeliveryUnmatched: "No matching recipient", DeliveryDeferred: "Delivery deferred", DeliveryFaulted: "Delivery failed",
  TimerScheduled: "Timer scheduled", TimerDispatched: "Timer dispatch reported", TimerRetired: "Timer retired", TimerCancellationRequested: "Timer cancellation requested",
  ActivityContinuationStarted: "Continuation started", ActivityContinuationReturned: "Continuation returned a result",
  ActivityContinuationSuspended: "Continuation waiting again", ActivityContinuationCompleted: "Continuation completed",
  ActivityContinuationFaulted: "Continuation faulted", ActivityContinuationCancelled: "Continuation cancelled",
  ChildWorkflowDispatched: "Child run dispatch recorded", ChildWorkflowSettled: "Child run settled", StimulusPublished: "Stimulus publication recorded",
  ChildControlAccepted: "Child control accepted", ChildControlApplied: "Child control applied", IncidentResolutionApplied: "Incident resolution applied",
  ActivityRetryScheduled: "Activity retry scheduled", RecoveryRequested: "Recovery requested", ControlApplied: "Control applied", AlterationApplied: "Run alteration applied",
  CompensationRegistered: "Compensation registered", CompensationStarted: "Compensation started", CompensationHandlerScheduled: "Compensation handler scheduled",
  CompensationHandlerCompleted: "Compensation handler completed", CompensationHandlerFaulted: "Compensation handler faulted", CompensationConfirmed: "Compensation confirmed",
  CompensationResolved: "Compensation resolved", CompensationCompleted: "Compensation completed", BookmarkSuperseded: "Bookmark superseded",
  DeliveryDuplicate: "Duplicate delivery observed", BookmarkResumeReceived: "Bookmark resume received", BookmarkResumeDispatched: "Bookmark resume dispatched"
};
export function isKnownHistoryEvent(event: Pick<RuntimeHistoryEvent, "name">) { return !!names[event.name]; }
export function historyEventLabel(event: Pick<RuntimeHistoryEvent, "name">) { return names[event.name] ?? (humanizeTypeName(event.name) || event.name); }
export function isHistoryHighlight(event: RuntimeHistoryEvent) { return !["ActivityScheduled", "ActivityAttemptClaimed", "ValueCaptured"].includes(event.name); }
export function historyAuthorityLabel(value: string) {
  return value === "CommittedTransition" ? "Committed runtime transition" : value === "DeliveryObservation" ? "Recorded source observation"
    : value === "Unspecified" ? "Authority unspecified" : `Unrecognized recorded authority (${value})`;
}
export function historyClock(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: false });
}
export function historyDate(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleDateString([], { weekday: "short", year: "numeric", month: "short", day: "numeric", timeZoneName: "shortOffset" });
}
export function historyInstant(value?: string | null) {
  if (!value) return "Not recorded";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : `${date.toLocaleString()} · ${value}`;
}
export function historyZone() { return Intl.DateTimeFormat().resolvedOptions().timeZone || "Local time"; }

export function HistoryCoverage({ coverage, scopeComplete = true, scopeLimitation, readAt }: {
  coverage: RuntimeHistoryCoverage; scopeComplete?: boolean; scopeLimitation?: string | null; readAt?: string;
}) {
  const settled = coverage.settledSequence >= coverage.lastSequence;
  const label = !["captured", "partial", "pending", "uncaptured", "disabled", "unsupported", "invalid"].includes(coverage.state) ? `Unrecognized recording coverage (${coverage.state}). Completeness cannot be established.`
    : coverage.state === "disabled" ? "History capture was disabled when this run was admitted."
    : coverage.state === "uncaptured" ? "History was not captured for this run; its original capture setting is unavailable."
    : coverage.state === "unsupported" ? "This host does not support durable history."
      : coverage.state === "invalid" ? "History integrity is unavailable."
        : coverage.state === "pending" || !settled ? "Recorded history is still settling."
          : coverage.hasGaps || coverage.purgedThroughSequence > 0 || coverage.state === "partial" ? "Partial recorded history."
            : coverage.capturedFromStart ? "History available from run start." : "History is available from a later capture boundary.";
  return <div className="wf-history-coverage" data-state={coverage.state} role="status">
    <p>{label}{!scopeComplete ? " This execution scope is incomplete." : ""}</p>
    {coverage.limitation ? <p>{coverage.limitation}</p> : null}
    {scopeLimitation && scopeLimitation !== coverage.limitation ? <p>{scopeLimitation}</p> : null}
    {coverage.captureLimitation && coverage.captureLimitation !== coverage.limitation ? <p>{coverage.captureLimitation}</p> : null}
    {coverage.sourceHistoryLimitation && coverage.sourceHistoryLimitation !== coverage.limitation ? <p>{coverage.sourceHistoryLimitation}</p> : null}
    <details><summary>Recording coverage</summary><dl className="wf-history-facts">
      {readAt ? <><dt>Inspection observed</dt><dd>{historyInstant(readAt)}</dd></> : null}
      <dt>Current capture chain</dt><dd>{coverage.captureAvailable === true ? "Available" : coverage.captureAvailable === false ? "Unavailable now; recorded history remains separate" : coverage.captureAvailable === null ? "Unknown" : "Not reported by this host"}</dd>
      <dt>Source history responsibility</dt><dd>{coverage.sourceHistoryPending === true ? "Recorded source history is still owed" : coverage.sourceHistoryPending === false ? "No owed source history observed at this read; later facts may arrive" : coverage.sourceHistoryPending === null ? "Cannot establish source settlement" : "Not reported by this host"}</dd>
      <dt>Available positions</dt><dd>{coverage.firstSequence}–{coverage.lastSequence}</dd>
      <dt>Settled through</dt><dd>{coverage.settledSequence} · a settled prefix can receive later events</dd>
      {coverage.purgedThroughSequence > 0 ? <><dt>Purged through</dt><dd>{coverage.purgedThroughSequence}</dd></> : null}
      <dt>Capture profile</dt><dd>{coverage.policyId ? `${coverage.policyId} v${coverage.policyVersion ?? "?"}` : "Not recorded"}</dd>
      <dt>Supported event kinds</dt><dd>{coverage.supportedKinds.length ? coverage.supportedKinds.map(name => historyEventLabel({ name })).join(", ") : "No recorded capture catalog"}</dd>
    </dl></details>
  </div>;
}

function EventIcon({ event }: { event: RuntimeHistoryEvent }) {
  const props = { size: 16, "aria-hidden": true as const };
  if (/Fault|IncidentRecorded|Rejected/.test(event.name)) return <CircleAlert {...props} />;
  if (event.category === "BookmarkLifecycle") return <BookmarkCheck {...props} />;
  if (event.authority === "DeliveryObservation" || event.category === "DeliveryObservation") return <Inbox {...props} />;
  if (/Suspended|Waiting/.test(event.name)) return <Pause {...props} />;
  if (/Completed|Resolved/.test(event.name)) return <CircleCheck {...props} />;
  if (/Retry|Retried|Recovered/.test(event.name)) return <RotateCcw {...props} />;
  if (/Scheduled/.test(event.name)) return <ListPlus {...props} />;
  if (event.category === "WorkflowLifecycle") return <Workflow {...props} />;
  if (/Started|Claimed/.test(event.name)) return <CirclePlay {...props} />;
  return <Activity {...props} />;
}

export function HistoryEventList({ items, selectedEventId, onSelect, labelForEvent, compact = false, unloadedAfterSequence }: {
  items: RuntimeHistoryEvent[]; selectedEventId?: string | null; onSelect(event: RuntimeHistoryEvent): void;
  labelForEvent?(event: RuntimeHistoryEvent): string | null; compact?: boolean; unloadedAfterSequence?: number;
}) {
  const creations = new Map(items.filter(event => event.name === "BookmarkCreated" && event.bookmarkId).map(event => [event.bookmarkId, event]));
  return <ol className={`wf-history-events${compact ? " wf-history-events-compact" : ""}`} aria-label={compact ? "Activity journal events" : "Run history events"}>
    {items.map((event, index) => {
      const creation = event.name === "BookmarkConsumed" ? creations.get(event.bookmarkId) : undefined;
      const date = historyDate(event.occurredAt);
      const previous = items[index - 1];
      const gap = unloadedAfterSequence !== undefined && event.sequence > unloadedAfterSequence && (!previous || previous.sequence <= unloadedAfterSequence);
      const age = creation ? formatDuration(creation.occurredAt, event.occurredAt) : "";
      return <li key={event.historyEventId}>
        {gap ? <p className="wf-history-range-gap" role="note">Earlier snapshot history is not fully loaded. The following events arrived after position #{unloadedAfterSequence}.</p> : null}
        {!previous || historyDate(previous.occurredAt) !== date ? <p className="wf-history-date">{date}</p> : null}
        <button type="button" className="wf-history-event" aria-pressed={selectedEventId === event.historyEventId}
          data-history-event-id={event.historyEventId} data-history-sequence={event.sequence} data-category={event.category}
          onClick={() => onSelect(event)}>
          <time dateTime={event.occurredAt} title={historyInstant(event.occurredAt)}>{historyClock(event.occurredAt)}</time>
          <span className="wf-history-event-icon"><EventIcon event={event} /></span>
          <span className="wf-history-event-body"><strong>{historyEventLabel(event)}</strong>
            {labelForEvent?.(event) ? <small>{labelForEvent(event)}</small> : event.activityExecutionId ? <small>Execution {event.activityExecutionId}</small> : null}
            {event.metadata["history.status"] ? <small>{event.metadata["history.status"]}</small> : null}
            {event.metadata["history.reasonCode"] ? <small>{event.metadata["history.reasonCode"]}</small> : null}
            {age ? <small><Clock3 size={12} aria-hidden="true" /> Handle lifetime {age}</small> : null}
            {event.authority === "DeliveryObservation" ? <small>Recorded source observation</small> : event.authority !== "CommittedTransition" && event.authority !== "Unspecified" ? <small>{historyAuthorityLabel(event.authority)}</small> : null}
          </span>
          <span className="wf-history-position" aria-label={`Recorded position ${event.sequence}`}>#{event.sequence}</span>
        </button>
      </li>;
    })}
  </ol>;
}
