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
  IncidentRecorded: "Incident recorded", IncidentResolved: "Incident resolved", IncidentResolutionBatchApplied: "Incident strategy decisions applied",
  IncidentStrategyEvaluated: "Incident strategy evaluated", IncidentProjectionReconciled: "Incident projection reconciled",
  IncidentSuppressed: "Incident suppressed", IncidentDecisionApplied: "Incident decision applied",
  WorkflowDispatchChanged: "Workflow dispatch updated", ValueCaptured: "Value observation recorded", RuntimeAlterationJob: "Run alteration recorded",
  StimulusReceived: "Stimulus received", StimulusAccepted: "Stimulus accepted", StimulusRejected: "Stimulus rejected",
  DeliveryReceived: "Delivery received", DeliveryAccepted: "Delivery accepted", DeliveryRejected: "Delivery rejected",
  DeliveryDispatched: "Delivery dispatched", DeliveryUnmatched: "No matching recipient", DeliveryDeferred: "Delivery deferred", DeliveryFaulted: "Delivery failed",
  TimerScheduled: "Timer scheduled", TimerDispatched: "Timer dispatch reported", TimerRetired: "Timer retired", TimerCancellationRequested: "Timer cancellation requested",
  ActivityContinuationStarted: "Activity callback started", ActivityContinuationReturned: "Activity callback returned a result",
  ActivityContinuationSuspended: "Continuation waiting", ActivityContinuationCompleted: "Continuation completed",
  ActivityContinuationFaulted: "Continuation faulted", ActivityContinuationCancelled: "Continuation cancelled",
  ChildWorkflowDispatched: "Child run dispatch recorded", ChildWorkflowDispatchRequested: "Child run dispatch requested",
  ChildWorkflowSettled: "Child run settled", StimulusPublished: "Stimulus publication recorded", StimulusPublicationCommitted: "Stimulus publication committed",
  ChildStartAttemptObserved: "Child start attempt observed", ChildStartDispositionObserved: "Child start disposition observed",
  StimulusRoutingAttemptObserved: "Stimulus routing attempt observed", StimulusRoutingDispositionObserved: "Stimulus routing disposition observed",
  ChildControlAccepted: "Child control accepted", ChildControlApplied: "Child control applied", IncidentResolutionApplied: "Incident resolution applied",
  ActivityRetryScheduled: "Activity retry scheduled", RecoveryRequested: "Recovery requested", ControlApplied: "Control applied", AlterationApplied: "Run alteration applied",
  CompensationRegistered: "Compensation registered", CompensationStarted: "Compensation started", CompensationHandlerScheduled: "Compensation handler scheduled",
  CompensationHandlerCompleted: "Compensation handler completed", CompensationHandlerFaulted: "Compensation handler faulted", CompensationConfirmed: "Compensation confirmed",
  CompensationResolved: "Compensation resolved", CompensationCompleted: "Compensation completed", CompensationAbandoned: "Compensation abandoned", BookmarkSuperseded: "Bookmark superseded",
  DurableStateRecorded: "Durable state recorded", DurableStateRemoved: "Durable state removed",
  DeliveryDuplicate: "Duplicate delivery observed", BookmarkResumeReceived: "Bookmark resume received", BookmarkResumeDispatched: "Bookmark resume dispatched"
};
type HistoryPresentationEvent = Pick<RuntimeHistoryEvent, "name"> & Partial<Pick<RuntimeHistoryEvent, "metadata" | "authority" | "category" | "schemaVersion">>;
const skippedResumeReasons: ReadonlyMap<string, { reason: string; explanation: string }> = new Map([
  ["receiver-bookmark-missing", {
    reason: "The saved bookmark was no longer present.",
    explanation: "This queued resume found that its saved bookmark was no longer present. This attempt did not start a callback."
  }],
  ["receiver-bookmark-expired", {
    reason: "The saved bookmark had expired.",
    explanation: "The saved bookmark had expired when this queued resume reached its activity. This attempt did not start a callback."
  }],
  ["receiver-wait-no-longer-eligible", {
    reason: "The saved wait was no longer eligible.",
    explanation: "The saved wait was no longer eligible when this queued resume reached its activity. This attempt did not start a callback."
  }],
  ["receiver-admission-superseded", {
    reason: "The saved wait failed its final eligibility check.",
    explanation: "The runtime rejected this saved resume after the first eligibility check. This attempt did not start a callback."
  }],
  ["terminal-resume-ineligible", {
    reason: "The run had already ended.",
    explanation: "The run had already ended when the runtime inspected this queued resume during cleanup."
  }]
]);
function skippedResumeReason(event: HistoryPresentationEvent) {
  if (event.name !== "DeliveryUnmatched" || event.authority !== "DeliveryObservation" || event.category !== "DeliveryObservation" || event.schemaVersion !== 1) return null;
  return skippedResumeReasons.get(event.metadata?.["history.reasonCode"] ?? "") ?? null;
}
export function isKnownHistoryEvent(event: Pick<RuntimeHistoryEvent, "name">) { return Object.hasOwn(names, event.name); }
export function historyEventLabel(event: HistoryPresentationEvent) { return skippedResumeReason(event) ? "Resume skipped" : isKnownHistoryEvent(event) ? names[event.name] : humanizeTypeName(event.name) || event.name; }
export function historyEventExplanation(event: HistoryPresentationEvent) {
  const skipped = skippedResumeReason(event);
  if (skipped) return skipped.explanation;
  switch (event.name) {
    case "ActivityContinuationReturned": return "The activity callback returned a result. Its committed continuation outcome is recorded separately; validation or disposal can still fail after the callback returns.";
    case "BookmarkConsumed": return "The handle was consumed at this committed transition. Continuation and its outcome are separate recorded facts.";
    case "ChildWorkflowDispatchRequested": return "A child-start responsibility was committed. Child admission, execution and outcome are separate recorded facts.";
    case "StimulusPublicationCommitted": return "The publication responsibility was committed. Routing, recipient admission and execution are separate recorded facts.";
    case "ChildWorkflowSettled": return "The child run reached a committed terminal outcome. Parent continuation and its result are recorded separately.";
    case "ChildStartAttemptObserved": return "The child-start handler was invoked at the source. This observation does not establish that a child run started or executed.";
    case "ChildStartDispositionObserved": return "The source recorded the child-start attempt's disposition. Inspect its reason and the related run's history for actual execution.";
    case "StimulusRoutingAttemptObserved": return "The source routing handler was invoked. This observation does not establish recipient admission or execution.";
    case "StimulusRoutingDispositionObserved": return "The source recorded a routing attempt's disposition. Recipient execution and continuation outcomes require their own recorded facts.";
    case "IncidentResolutionBatchApplied": return "Incident strategy decisions were applied. This event alone does not mean every incident was resolved.";
    case "IncidentStrategyEvaluated": return "An incident strategy decision was recorded. The incident's status and any separate resolution event show whether it was resolved.";
    case "IncidentProjectionReconciled": return "The incident's recorded projection was reconciled. This does not by itself resolve the incident.";
    case "IncidentDecisionApplied": return "An incident decision was applied. The incident's status and separate lifecycle events show whether it was resolved.";
    case "StimulusPublished": case "ChildWorkflowDispatched": case "DeliveryDispatched": case "TimerDispatched":
      return "A source dispatch or publication does not establish recipient execution or its outcome. Inspect its recorded disposition and related continuation facts.";
    default: return null;
  }
}
export function isHistoryHighlight(event: RuntimeHistoryEvent) { return !["ActivityScheduled", "ActivityAttemptClaimed", "ValueCaptured"].includes(event.name); }
export function historyAuthorityLabel(value: string) {
  return value === "CommittedTransition" ? "Committed runtime transition" : value === "DeliveryObservation" ? "Recorded source observation"
    : value === "Unspecified" ? "Authority unspecified" : `Unrecognized recorded authority (${value})`;
}
export function historyClock(value: string | null) {
  if (!value) return "Time unknown";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: false });
}
export function historyDate(value: string | null) {
  if (!value) return "Occurrence time unknown";
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
  const emptyRetainedRange = coverage.firstSequence === 0 && coverage.lastSequence === 0 || coverage.firstSequence > coverage.lastSequence;
  const availablePositions = emptyRetainedRange
    ? ["captured", "partial", "pending"].includes(coverage.state) ? "No retained events" : "Not available"
    : `${coverage.firstSequence}–${coverage.lastSequence}`;
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
      <dt>Available positions</dt><dd>{availablePositions}</dd>
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
      const reason = skippedResumeReason(event)?.reason ?? event.metadata["history.reasonCode"];
      const creation = event.name === "BookmarkConsumed" ? creations.get(event.bookmarkId) : undefined;
      const occurredAt = event.occurredAtKnown === false ? null : event.occurredAt;
      const date = historyDate(occurredAt);
      const previous = items[index - 1];
      const gap = unloadedAfterSequence !== undefined && event.sequence > unloadedAfterSequence && (!previous || previous.sequence <= unloadedAfterSequence);
      const age = creation && creation.occurredAtKnown !== false && event.occurredAtKnown !== false ? formatDuration(creation.occurredAt, occurredAt) : "";
      return <li key={event.historyEventId}>
        {gap ? <p className="wf-history-range-gap" role="note">Earlier snapshot history is not fully loaded. The following events arrived after position #{unloadedAfterSequence}.</p> : null}
        {!previous || historyDate(previous.occurredAtKnown === false ? null : previous.occurredAt) !== date ? <p className="wf-history-date">{date}</p> : null}
        <button type="button" className="wf-history-event" aria-pressed={selectedEventId === event.historyEventId}
          data-history-event-id={event.historyEventId} data-history-sequence={event.sequence} data-category={event.category}
          onClick={() => onSelect(event)}>
          <time dateTime={occurredAt ?? undefined} title={historyInstant(occurredAt)}>{historyClock(occurredAt)}</time>
          <span className="wf-history-event-icon"><EventIcon event={event} /></span>
          <span className="wf-history-event-body"><strong>{historyEventLabel(event)}</strong>
            {labelForEvent?.(event) ? <small>{labelForEvent(event)}</small> : event.activityExecutionId ? <small>Execution {event.activityExecutionId}</small> : null}
            {event.metadata["history.status"] ? <small>{event.metadata["history.status"]}</small> : null}
            {reason ? <small>{reason}</small> : null}
            {age ? <small><Clock3 size={12} aria-hidden="true" /> Handle lifetime {age}</small> : null}
            {event.authority === "DeliveryObservation" ? <small>Recorded source observation</small> : event.authority !== "CommittedTransition" && event.authority !== "Unspecified" ? <small>{historyAuthorityLabel(event.authority)}</small> : null}
          </span>
          <span className="wf-history-position" aria-label={`Recorded position ${event.sequence}`}>#{event.sequence}</span>
        </button>
      </li>;
    })}
  </ol>;
}
