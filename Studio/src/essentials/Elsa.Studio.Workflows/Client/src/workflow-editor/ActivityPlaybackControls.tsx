import { useId } from "react";
import { ChevronLeft, ChevronRight, Pause, Play, RotateCcw } from "lucide-react";
import { StudioButton, StudioField } from "@elsa-workflows/studio-ui";
import { playbackOrdinal, safePlaybackRange, type PlaybackOccurrence } from "../api/activityPlayback";
import { humanizeTypeName } from "../workflowFormatting";
import { historyInstant } from "./run-history/historyPresentation";
import { playbackBoundaryReason, type PlaybackSpeed } from "./activityPlaybackModel";
import type { ActivityPlayback } from "./useActivityPlayback";
import "./activityPlayback.css";

export function ActivityPlaybackControls({ playback, scopeLabel, labelFor }: {
  playback: ActivityPlayback; scopeLabel: string; labelFor(entry: PlaybackOccurrence): string;
}) {
  const id = useId();
  const cursor = playback.cursor, page = playback.page;
  const range = page ? safePlaybackRange(page) : null;
  const position = cursor ? String(playbackOrdinal(cursor.position) + 1n) : null;
  const label = cursor ? labelFor(cursor) : "";
  const pendingPosition = playback.pending?.position;
  const safePending = pendingPosition !== undefined && playbackOrdinal(pendingPosition) <= BigInt(Number.MAX_SAFE_INTEGER) ? Number(pendingPosition) : null;
  const value = safePending ?? (cursor && playbackOrdinal(cursor.position) <= BigInt(Number.MAX_SAFE_INTEGER) ? Number(cursor.position) : 0);
  const positionText = cursor ? `Step ${position}${page?.totalCount !== null && page?.totalCount !== undefined ? ` of ${page.totalCount}` : " · total unknown"} · ${label}` : "No confirmed activity position";
  const loading = playback.phase === "loading" || playback.phase === "buffering";
  const pendingText = playback.pending ? pendingPosition !== undefined
    ? `${playback.phase === "failed" ? "Requested" : "Loading"} step ${playbackOrdinal(pendingPosition) + 1n}.`
    : playback.phase === "failed" ? "Requested activity occurrence." : "Loading selected occurrence." : "";
  const pendingStatus = pendingText ? `${pendingText}${cursor ? ` Showing confirmed step ${position}.` : ""}` : "";
  const stateLabel = playback.playing ? loading ? "Buffering" : "Playing" : playback.phase === "ended" ? "End of available segment" : playback.phase === "unavailable" ? "Unavailable" : "Paused";
  const instant = cursor?.occurredAtKnown === false ? null : cursor?.occurredAt;
  const displayedInstant = instant ?? cursor?.recordedAt;
  const compactInstant = displayedInstant ? new Intl.DateTimeFormat(undefined, { dateStyle: "short", timeStyle: "medium" }).format(new Date(displayedInstant)) : "Unknown";
  const coverageLabels: Record<string, string> = { disabled: "Recording was disabled for this admission.",
    uncaptured: "No recording was captured for this admission.", unsupported: "Recorded history is unsupported by this host.",
    pending: "Recording is pending.", invalid: "Recorded history could not be verified.", partial: "Only partial recorded history is available.",
    captured: "Recorded history exists; activity playback is unavailable for this range." };
  return <section className="wf-activity-playback" aria-label="Activity playback" aria-busy={loading}>
    <div className="wf-playback-heading"><strong>Activity playback</strong><span>{scopeLabel}</span><span className="wf-playback-state">{stateLabel}</span></div>
    <div className="wf-playback-transport" role="group" aria-label="Playback controls">
      <StudioButton type="button" disabled={!cursor?.previousActivityExecutionId} aria-disabled={!!playback.pending || undefined} onClick={() => { if (!playback.pending) void playback.previous(); }}><ChevronLeft size={15} aria-hidden="true" />Previous</StudioButton>
      {playback.playing ? <StudioButton type="button" aria-label="Pause playback" onClick={() => playback.pause()}><Pause size={15} aria-hidden="true" />Pause</StudioButton>
        : playback.phase === "ended" ? <StudioButton type="button" disabled={!cursor || !!playback.pending || !!playback.error} onClick={() => { void playback.replay(); }}><RotateCcw size={15} aria-hidden="true" />Replay</StudioButton>
          : <StudioButton type="button" disabled={!cursor || !!playback.pending || !!playback.error} onClick={playback.play}><Play size={15} aria-hidden="true" />Play</StudioButton>}
      <StudioButton type="button" disabled={!cursor?.nextActivityExecutionId} aria-disabled={!!playback.pending || undefined} onClick={() => { if (!playback.pending) void playback.next(); }}>Next<ChevronRight size={15} aria-hidden="true" /></StudioButton>
      <StudioField label="Presentation pace"><select aria-label="Playback speed" value={playback.speed} onChange={event => playback.setSpeed(event.currentTarget.value as PlaybackSpeed)}>
        <option value="0.5">0.5×</option><option value="1">1×</option><option value="2">2×</option><option value="4">4×</option>
      </select></StudioField>
      <StudioButton type="button" disabled={playback.playing || loading} onClick={() => { void playback.refresh(); }}>{playback.newer ? "Include new history" : "Refresh playback"}</StudioButton>
    </div>
    <div className="wf-playback-position" role="status" aria-live="polite" aria-atomic="true">
      <strong>{positionText}</strong>
      <span className="wf-playback-observation" title={pendingStatus || (cursor ? historyInstant(displayedInstant) : undefined)}>
        {pendingStatus || (cursor ? `${instant ? "First observed" : "Occurrence time unknown · recorded"} ${compactInstant}` : "")}
      </span>
    </div>
    {range && cursor ? <div className="wf-playback-scrubber"><label htmlFor={id}>Activity position</label>
      <input id={id} type="range" min={range.min} max={range.max} step={1} value={value}
        disabled={playback.phase === "unavailable"} aria-valuetext={`${pendingText ? `${pendingText} ` : ""}${positionText}`}
        onChange={event => { void playback.seek(event.currentTarget.value); }} />
      </div>
      : cursor ? <p className="wf-playback-note">Direct scrubbing is unavailable because the total or its safe numeric range is unknown. Use Previous and Next.</p> : null}
    <p className="wf-playback-note">Recorded order · 1× = one activity step per second</p>
    {playback.newer ? <p className="wf-playback-note" role="status">New history is available. Pause and include it explicitly; this playback range has not changed.</p> : null}
    {playback.note ? <p className="wf-playback-note" role="status">{playback.note}</p> : null}
    {playback.diagramLimitation ? <p className="wf-playback-note" role="status">{playback.diagramLimitation}</p> : null}
    {playback.error ? <p className="wf-playback-error" role="status">{playback.error}{playback.phase === "failed" ? <StudioButton type="button" onClick={() => { void playback.retry(); }}>Retry requested activity</StudioButton> : null}</p> : null}
    {page && !cursor ? <p className="wf-playback-note" role="status">{Object.hasOwn(coverageLabels, page.coverage.state) ? coverageLabels[page.coverage.state] : "Recording status is unavailable."}</p> : null}
    {cursor && playback.window ? <details className="wf-playback-details"><summary>{page?.coverage.state === "partial" ? "Partial playback range" : "Recorded playback range"} · through #{playback.window.upperSequence}</summary>
      <p>Presentation pace does not change recorded durations. Order does not imply causation or reconstruct historical workflow state.</p>
      {page?.coverage.limitation ? <p>{page.coverage.limitation}</p> : null}{page?.reason ? <p>{playbackBoundaryReason(page.reason)}</p> : null}
      <dl>
      <dt>Run</dt><dd>{cursor.workflowExecutionId}</dd><dt>Activity execution</dt><dd>{cursor.activityExecutionId}</dd>
      <dt>Original observation</dt><dd>{humanizeTypeName(cursor.observationKind)}</dd><dt>Event</dt><dd>{cursor.historyEventId}</dd>
      <dt>Occurred</dt><dd>{historyInstant(instant)}</dd><dt>Recorded</dt><dd>{historyInstant(cursor.recordedAt)}</dd>
      <dt>Parent execution</dt><dd>{cursor.parentActivityExecutionId ?? (cursor.activityAncestryKnown ? "Root execution" : "Not recorded")}</dd>
      {cursor.iterationId ? <><dt>Iteration</dt><dd>{cursor.iterationId}</dd></> : null}{cursor.attemptId ? <><dt>Attempt</dt><dd>{cursor.attemptId}</dd></> : null}
      {cursor.executionScopeId ? <><dt>Recorded scope</dt><dd>{cursor.executionScopeId}</dd></> : null}
      <dt>Retained after</dt><dd>#{playback.window.retainedAfterSequence}</dd><dt>Playback upper bound</dt><dd>#{playback.window.upperSequence}</dd>
    </dl></details> : null}
  </section>;
}
