import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { capabilityIds, resolveCapabilityLink } from "./capabilities";
import type { RuntimeHistoryCoverage, RuntimeHistoryEvent } from "../runtimeHistoryTypes";

// This private adapter consumes the history owner's frozen occurrence facet, never the live browser.
const pageSize = 50;
const maximum = 9223372036854775807n;
export class ActivityPlaybackIdentityError extends Error {
  constructor() { super("The recorded activity sequence could not be verified. Refresh playback to recover."); }
}
export type PlaybackCoverage = Omit<RuntimeHistoryCoverage, "firstSequence" | "lastSequence" | "settledSequence" | "purgedThroughSequence"> & {
  firstSequence: string; lastSequence: string; settledSequence: string; purgedThroughSequence: string;
};
export interface PlaybackOccurrence {
  position: string; workflowExecutionId: string; activityExecutionId: string; historyEventId: string; sequence: string;
  observationKind: string; occurredAt?: string | null; occurredAtKnown?: boolean | null; recordedAt?: string | null;
  executableNodeId?: string | null; parentActivityExecutionId?: string | null; activityAncestryKnown?: boolean | null;
  executionScopeId?: string | null; iterationId?: string | null; attemptId?: string | null;
  causation?: RuntimeHistoryEvent["causation"];
  previousActivityExecutionId: string | null; nextActivityExecutionId: string | null;
}
export interface PlaybackPage {
  state: "available" | "partial" | "unavailable" | "range-invalidated" | "target-unavailable";
  reason: string | null; coverage: PlaybackCoverage; upperSequence: string; retainedAfterSequence: string;
  totalCount: string | null; selectedPosition: string | null; items: PlaybackOccurrence[];
  sequenceToken: string | null; hasNewerHistory: boolean; readAt: string;
}
export interface PlaybackWindow {
  sequenceToken: string; upperSequence: string; retainedAfterSequence: string; totalCount: string | null;
}
export interface PlaybackRequest {
  window?: PlaybackWindow; position?: string; anchorActivityExecutionId?: string;
  activityExecutionId?: string | null; includeDescendants?: boolean;
}

/** Ordering remains decimal text on the wire and bigint internally, including values above 2^53. */
export function playbackOrdinal(value: unknown): bigint {
  if (typeof value !== "string" || !/^(0|[1-9][0-9]{0,18})$/.test(value)) throw new ActivityPlaybackIdentityError();
  const result = BigInt(value);
  if (result > maximum) throw new ActivityPlaybackIdentityError();
  return result;
}
export function playbackWindow(page: PlaybackPage): PlaybackWindow | null {
  return (page.state === "available" || page.state === "partial") && page.sequenceToken
    ? { sequenceToken: page.sequenceToken, upperSequence: page.upperSequence, retainedAfterSequence: page.retainedAfterSequence, totalCount: page.totalCount }
    : null;
}
export function selectedPlaybackOccurrence(page: PlaybackPage) {
  return page.items.find(item => item.position === page.selectedPosition) ?? null;
}
export function safePlaybackRange(page: PlaybackPage) {
  if (page.totalCount === null || playbackOrdinal(page.totalCount) > BigInt(Number.MAX_SAFE_INTEGER)) return null;
  const count = Number(page.totalCount);
  return count > 0 ? { min: 0, max: count - 1 } : null;
}
const record = (value: unknown): value is Record<string, unknown> => typeof value === "object" && value !== null && !Array.isArray(value);
const identity = (value: unknown): value is string => typeof value === "string" && value.length > 0;
const optionalIdentity = (value: unknown) => value == null || identity(value);
const neighborIdentity = (value: unknown) => value === null || identity(value);
const optionalBoolean = (value: unknown) => value == null || typeof value === "boolean";
const instant = (value: unknown) => value == null || typeof value === "string" && Number.isFinite(Date.parse(value));
const observationKinds = new Set(["ActivityScheduled", "ActivityStarted", "ActivityAttemptClaimed", "ActivityResumeClaimed", "ActivityResumeRetried",
  "ActivityCompleted", "ActivitySuspended", "ActivityCancelled", "ActivityRecovered", "ActivityFaulted", "ActivityContinuationStarted",
  "ActivityContinuationSuspended", "ActivityContinuationCompleted", "ActivityContinuationFaulted", "ActivityContinuationCancelled",
  "ActivityContinuationReturned", "ActivityRetryScheduled"]);

function validatePage(value: unknown, run: string, request: PlaybackRequest): PlaybackPage {
  const invalid = () => { throw new ActivityPlaybackIdentityError(); };
  if (!record(value) || !record(value.coverage) || !Array.isArray(value.items) || value.items.length > pageSize
    || !["available", "partial", "unavailable", "range-invalidated", "target-unavailable"].includes(String(value.state))
    || !optionalIdentity(value.reason) || typeof value.hasNewerHistory !== "boolean" || !identity(value.readAt) || !instant(value.readAt)) invalid();
  const page = value as unknown as PlaybackPage;
  const upper = playbackOrdinal(page.upperSequence), retained = playbackOrdinal(page.retainedAfterSequence);
  for (const field of ["firstSequence", "lastSequence", "settledSequence", "purgedThroughSequence"] as const) playbackOrdinal(page.coverage[field]);
  if (page.coverage.state === "invalid" || typeof page.coverage.state !== "string" || typeof page.coverage.capturedFromStart !== "boolean"
    || typeof page.coverage.hasGaps !== "boolean" || !Array.isArray(page.coverage.supportedKinds)
    || !page.coverage.supportedKinds.every(identity)) invalid();
  if (page.totalCount !== null) playbackOrdinal(page.totalCount);
  if (page.selectedPosition !== null) playbackOrdinal(page.selectedPosition);
  if (page.state !== "available" && page.state !== "partial") {
    if (page.items.length || page.sequenceToken !== null || page.selectedPosition !== null || page.totalCount !== null) invalid();
    return page;
  }
  if (!identity(page.sequenceToken) || retained > upper || page.coverage.hasGaps || !["captured", "partial"].includes(page.coverage.state)
    || playbackOrdinal(page.coverage.lastSequence) < upper || page.coverage.purgedThroughSequence !== page.retainedAfterSequence
    || page.hasNewerHistory !== (playbackOrdinal(page.coverage.lastSequence) > upper)) invalid();
  if (request.window && (page.sequenceToken !== request.window.sequenceToken || page.upperSequence !== request.window.upperSequence
    || page.retainedAfterSequence !== request.window.retainedAfterSequence || page.totalCount !== request.window.totalCount)) invalid();
  if (!page.items.length) {
    if (page.totalCount !== "0" || page.selectedPosition !== null || request.position !== undefined || request.anchorActivityExecutionId) invalid();
    return page;
  }
  const ids = new Set<string>(), events = new Set<string>();
  let previous: PlaybackOccurrence | undefined;
  let sequence = retained;
  for (const item of page.items) {
    if (!record(item) || item.workflowExecutionId !== run || !identity(item.activityExecutionId) || !identity(item.historyEventId)
      || !observationKinds.has(item.observationKind) || !neighborIdentity(item.previousActivityExecutionId) || !neighborIdentity(item.nextActivityExecutionId)
      || ![item.executableNodeId, item.parentActivityExecutionId, item.executionScopeId, item.iterationId, item.attemptId].every(optionalIdentity)
      || !optionalBoolean(item.occurredAtKnown) || !optionalBoolean(item.activityAncestryKnown)
      || !instant(item.occurredAt) || !instant(item.recordedAt) || item.occurredAtKnown === false && item.occurredAt != null
      || request.activityExecutionId && !request.includeDescendants && item.activityExecutionId !== request.activityExecutionId
      || ids.has(item.activityExecutionId) || events.has(item.historyEventId)) invalid();
    const position = playbackOrdinal(item.position), currentSequence = playbackOrdinal(item.sequence);
    if (currentSequence <= sequence || currentSequence > upper || page.totalCount !== null && position >= playbackOrdinal(page.totalCount)
      || position === 0n && item.previousActivityExecutionId !== null
      || page.totalCount !== null && position + 1n === playbackOrdinal(page.totalCount) && item.nextActivityExecutionId !== null
      || previous && (position !== playbackOrdinal(previous.position) + 1n || previous.nextActivityExecutionId !== item.activityExecutionId
        || item.previousActivityExecutionId !== previous.activityExecutionId)) invalid();
    ids.add(item.activityExecutionId); events.add(item.historyEventId); sequence = currentSequence; previous = item;
  }
  const selected = selectedPlaybackOccurrence(page);
  if (!selected || request.position !== undefined && selected.position !== request.position
    || request.anchorActivityExecutionId && selected.activityExecutionId !== request.anchorActivityExecutionId) invalid();
  return page;
}

export async function readActivityPlayback(context: StudioEndpointContext, workflowExecutionId: string,
  request: PlaybackRequest = {}, signal?: AbortSignal): Promise<PlaybackPage> {
  signal?.throwIfAborted();
  if (!identity(workflowExecutionId) || request.position !== undefined && request.anchorActivityExecutionId
    || request.includeDescendants && !request.activityExecutionId) throw new ActivityPlaybackIdentityError();
  if (request.position !== undefined) playbackOrdinal(request.position);
  const path = await resolveCapabilityLink(context, capabilityIds.runtime, "workflow-history-occurrences", { workflowExecutionId });
  signal?.throwIfAborted();
  // Limit is authenticated by the owner's token: never change it between admission and seek.
  const query = new URLSearchParams({ limit: String(pageSize) });
  if (request.window) query.set("sequenceToken", request.window.sequenceToken);
  if (request.position !== undefined) query.set("position", request.position);
  if (request.anchorActivityExecutionId) query.set("anchorActivityExecutionId", request.anchorActivityExecutionId);
  if (request.activityExecutionId) query.set("activityExecutionId", request.activityExecutionId);
  if (request.includeDescendants) query.set("includeDescendants", "true");
  const value = await context.http.getJson<unknown>(`${path}?${query}`, { signal });
  signal?.throwIfAborted();
  return validatePage(value, workflowExecutionId, request);
}
