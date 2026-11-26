import { useStudioTimeZone } from "@elsa-workflows/studio-sdk";
import { useEffect, useId, useRef, useState } from "react";
import { ChevronLeft, ChevronRight, Pause, Play, RotateCcw, SlidersHorizontal } from "lucide-react";
import { AnchoredPopover, StudioButton } from "@elsa-workflows/studio-ui";
import { playbackOrdinal, safePlaybackRange, type PlaybackOccurrence } from "../api/activityPlayback";
import { humanizeTypeName } from "../workflowFormatting";
import { CopyValueButton } from "./executableShared";
import { historyCompactInstant } from "./run-history/historyPresentation";
import { playbackBoundaryReason, type PlaybackSpeed } from "./activityPlaybackModel";
import type { ActivityPlayback } from "./useActivityPlayback";
import "./activityPlayback.css";

const playbackSpeeds: readonly PlaybackSpeed[] = ["0.5", "1", "2", "4"];

function PlaybackFact({ label, value, copyValue, title, mono = false, truncate = false, emptyText = "Not recorded", scope }: {
  label: string;
  value?: string | null;
  copyValue?: string;
  title?: string;
  mono?: boolean;
  truncate?: boolean;
  emptyText?: string;
  scope?: string;
}) {
  const [message, setMessage] = useState("");
  const empty = value === undefined || value === null || value === "";
  const rawCopy = copyValue ?? (typeof value === "string" ? value : undefined);
  useEffect(() => { setMessage(""); }, [rawCopy, scope]);
  return (
    <div className="wf-playback-fact-row">
      <dt>{label}</dt>
      <dd>
        {empty ? (
          <span className="wf-playback-fact-empty">{emptyText}</span>
        ) : (
          <>
            <span
              className={`wf-playback-fact-value${truncate ? " wf-playback-fact-truncate" : ""}`}
              title={title ?? rawCopy ?? value}
            >
              {mono ? <code>{value}</code> : value}
            </span>
            <span className="wf-playback-fact-tools">
              {message ? <span className="wf-playback-copy-state" role="status">{message}</span> : null}
              {rawCopy ? (
                <CopyValueButton
                  scope={scope}
                  value={rawCopy}
                  ariaLabel={`Copy ${label.toLowerCase()}`}
                  copiedLabel={label}
                  onCopied={() => setMessage("Copied")}
                  onCopyFailed={() => setMessage("Copy failed")}
                />
              ) : null}
            </span>
          </>
        )}
      </dd>
    </div>
  );
}

export function ActivityPlaybackControls({ playback, scopeLabel, labelFor }: {
  playback: ActivityPlayback; scopeLabel: string; labelFor(entry: PlaybackOccurrence): string;
}) {
  useStudioTimeZone();
  const id = useId();
  const optionsPopoverId = useId();
  const optionsTriggerRef = useRef<HTMLButtonElement>(null);
  const [optionsOpen, setOptionsOpen] = useState(false);
  useEffect(() => {
    if (!optionsOpen) return;
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key !== "Escape") return;
      event.preventDefault();
      setOptionsOpen(false);
      optionsTriggerRef.current?.focus();
    };
    document.addEventListener("keydown", handleKeyDown);
    return () => document.removeEventListener("keydown", handleKeyDown);
  }, [optionsOpen]);
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
  const compactInstant = displayedInstant ? historyCompactInstant(displayedInstant) : "Unknown";
  const copyScope = cursor ? `${cursor.workflowExecutionId}:${cursor.activityExecutionId}:${cursor.historyEventId}` : undefined;
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

      <StudioButton type="button" disabled={playback.playing || loading} onClick={() => { void playback.refresh(); }}>{playback.newer ? "Include new history" : "Refresh playback"}</StudioButton>
      <StudioButton
        ref={optionsTriggerRef}
        type="button"
        className="wf-playback-options-trigger"
        aria-label="Playback options"
        title="Playback options"
        aria-haspopup="dialog"
        aria-expanded={optionsOpen}
        aria-controls={optionsOpen ? optionsPopoverId : undefined}
        onClick={() => setOptionsOpen(current => !current)}
        onKeyDown={event => {
          if (event.key === "Escape" && optionsOpen) {
            event.preventDefault();
            setOptionsOpen(false);
          }
        }}
      ><SlidersHorizontal size={15} aria-hidden="true" /></StudioButton>
    </div>
    <div className="wf-playback-position" role="status" aria-live="polite" aria-atomic="true">
      <strong>{positionText}</strong>
      <span className="wf-playback-observation" title={pendingStatus || undefined}>{pendingStatus}</span>
    </div>
    {range && cursor ? <div className="wf-playback-scrubber"><label htmlFor={id}>Activity position</label>
      <input id={id} type="range" min={range.min} max={range.max} step={1} value={value}
        disabled={playback.phase === "unavailable"} aria-valuetext={`${pendingText ? `${pendingText} ` : ""}${positionText}`}
        onChange={event => { void playback.seek(event.currentTarget.value); }} />
      </div>
      : cursor ? <p className="wf-playback-note">Direct scrubbing is unavailable because the total or its safe numeric range is unknown. Use Previous and Next.</p> : null}
    {playback.newer ? <p className="wf-playback-note" role="status">New history is available. Pause and include it explicitly; this playback range has not changed.</p> : null}
    {playback.note ? <p className="wf-playback-note" role="status">{playback.note}</p> : null}
    {playback.diagramLimitation ? <p className="wf-playback-note" role="status">{playback.diagramLimitation}</p> : null}
    {playback.error ? <p className="wf-playback-error" role="status">{playback.error}{playback.phase === "failed" ? <StudioButton type="button" onClick={() => { void playback.retry(); }}>Retry requested activity</StudioButton> : null}</p> : null}
    {cursor && page?.coverage.sourceHistoryPending === true ? <p className="wf-playback-note" role="status">{page.coverage.limitation || "Some history observations are still pending. Playback shows the recorded prefix."}</p> : null}
    {page && !cursor ? <p className="wf-playback-note" role="status">{Object.hasOwn(coverageLabels, page.coverage.state) ? coverageLabels[page.coverage.state] : "Recording status is unavailable."}</p> : null}
    {cursor && page && (page.state === "partial" || page.coverage.state === "partial") ? <p className="wf-playback-note" role="status">Partial playback range. {page.coverage.limitation || "Playback covers only the recorded segment."}{page.reason ? ` ${playbackBoundaryReason(page.reason)}` : ""}</p> : null}
    <AnchoredPopover
      anchorRef={optionsTriggerRef}
      open={optionsOpen}
      id={optionsPopoverId}
      role="dialog"
      aria-label="Playback options"
      className="wf-playback-options-popover"
      minWidth={300}
      maxHeight={520}
      onDismiss={() => setOptionsOpen(false)}
      onKeyDown={event => {
        if (event.key === "Escape") {
          event.preventDefault();
          event.stopPropagation();
          setOptionsOpen(false);
          optionsTriggerRef.current?.focus();
        }
      }}
    >
      <div className="wf-playback-options-content">
        <div className="wf-playback-options-header">
          <strong>Playback options</strong>
        </div>
        <div className="wf-playback-pace-row">
          <span className="wf-playback-pace-label">Presentation pace</span>
          <div className="wf-playback-pace-group" role="group" aria-label="Playback speed">
            {playbackSpeeds.map(option => (
              <button
                key={option}
                type="button"
                className="wf-playback-pace-option"
                aria-pressed={playback.speed === option}
                onClick={() => playback.setSpeed(option)}
              >
                {option}×
              </button>
            ))}
          </div>
        </div>
        <p className="wf-playback-note">Recorded order · 1× = one activity step per second</p>
        {cursor ? <p className="wf-playback-note" title={displayedInstant ?? undefined}>{instant ? "First observed" : "Occurrence time unknown · recorded"} {compactInstant}</p> : null}
        {cursor && playback.window ? <details className="wf-playback-details">
          <summary>
            <ChevronRight size={14} className="wf-playback-details-chevron" aria-hidden="true" />
            <span>{page?.coverage.state === "partial" ? "Partial playback range" : "Recorded playback range"}</span>
            <small>through #{playback.window.upperSequence}</small>
          </summary>
          <div className="wf-playback-details-body">
            {page?.coverage.limitation && page.coverage.sourceHistoryPending !== true ? <p className="wf-playback-details-warning">{page.coverage.limitation}</p> : null}
            {page?.reason ? <p className="wf-playback-details-warning">{playbackBoundaryReason(page.reason)}</p> : null}
            <dl className="wf-playback-facts">
              <PlaybackFact label="Observation" value={humanizeTypeName(cursor.observationKind)} copyValue={cursor.observationKind} title={cursor.observationKind} scope={copyScope} />
              <PlaybackFact label="Occurred" value={instant ? historyCompactInstant(instant) : null} copyValue={instant ?? undefined} title={instant ?? undefined} scope={copyScope} />
              <PlaybackFact label="Recorded" value={cursor.recordedAt ? historyCompactInstant(cursor.recordedAt) : null} copyValue={cursor.recordedAt ?? undefined} title={cursor.recordedAt ?? undefined} scope={copyScope} />
              <PlaybackFact
                label="Sequence range"
                value={`#${playback.window.retainedAfterSequence} – #${playback.window.upperSequence}`}
                copyValue={`${playback.window.retainedAfterSequence}..${playback.window.upperSequence}`}
                title={`Retained after #${playback.window.retainedAfterSequence} through #${playback.window.upperSequence}`}
                mono
                scope={copyScope}
              />
            </dl>
            <p className="wf-playback-facts-section-label">Identifiers</p>
            <dl className="wf-playback-facts">
              <PlaybackFact label="Run" value={cursor.workflowExecutionId} mono truncate scope={copyScope} />
              <PlaybackFact label="Activity execution" value={cursor.activityExecutionId} mono truncate scope={copyScope} />
              <PlaybackFact
                label="Parent execution"
                value={cursor.parentActivityExecutionId}
                emptyText={cursor.activityAncestryKnown ? "Root execution" : "Not recorded"}
                mono
                truncate
                scope={copyScope}
              />
              {cursor.iterationId ? <PlaybackFact label="Iteration" value={cursor.iterationId} mono truncate scope={copyScope} /> : null}
              {cursor.attemptId ? <PlaybackFact label="Attempt" value={cursor.attemptId} mono truncate scope={copyScope} /> : null}
              {cursor.executionScopeId ? <PlaybackFact label="Recorded scope" value={cursor.executionScopeId} mono truncate scope={copyScope} /> : null}
              <PlaybackFact label="Event" value={cursor.historyEventId} mono truncate scope={copyScope} />
            </dl>
            <p className="wf-playback-details-footer">Presentation pace does not change recorded durations. Order does not imply causation or reconstruct historical workflow state.</p>
          </div>
        </details> : null}
      </div>
    </AnchoredPopover>
  </section>;
}
