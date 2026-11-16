import type { PlaybackOccurrence, PlaybackPage, PlaybackWindow } from "../api/activityPlayback";

export const playbackCadences = { "0.5": 2000, "1": 1000, "2": 500, "4": 250 } as const;
export type PlaybackSpeed = keyof typeof playbackCadences;
export interface PlaybackTarget { position?: string; activityExecutionId?: string; historyEventId?: string; inspectActivity?: boolean; includeSelfScope?: boolean; preserveInspector?: boolean; preserveSelection?: boolean; rememberChoice?: boolean }
export interface PlaybackOperation { signal: AbortSignal; isCurrent(): boolean }
export interface PreparedPlaybackSelection { commit(): void; limitation?: string }
export interface ActivityPlaybackState {
  key: string;
  phase: "loading" | "paused" | "playing" | "buffering" | "ended" | "failed" | "unavailable";
  playing: boolean; speed: PlaybackSpeed;
  page: PlaybackPage | null; window: PlaybackWindow | null; cursor: PlaybackOccurrence | null;
  pending: PlaybackTarget | null; error: string; note: string; diagramLimitation: string; newer: boolean;
  retryIntent: "initial" | "refresh" | "manual";
  outsideSelection: string | null;
}
export function initialPlaybackState(key: string): ActivityPlaybackState {
  return { key, phase: "loading", playing: false, speed: "1", page: null, window: null, cursor: null,
    pending: null, error: "", note: "", diagramLimitation: "", newer: false, outsideSelection: null, retryIntent: "manual" };
}
export function playbackBoundaryReason(reason?: string | null) {
  const reasons: Record<string, string> = {
    "retained-range-changed": "The retained activity range changed. Refresh playback while paused to choose a new range.",
    "retained-prefix-purged": "Earlier history was purged. Playback covers only this retained segment.",
    "capture-not-from-start": "Recording did not begin with this run. Playback covers the available segment only.",
    "history-gap-unlocated": "Recorded history has a gap whose boundaries are unknown. Continuous playback is unavailable.",
    "occurrence-recording-unavailable": "Activity occurrence recording is unavailable for this run.",
    "occurrence-observation-unknown": "This recording uses an unsupported occurrence order. Playback is unavailable.",
    "occurrence-facet-unsupported": "This backend does not support recorded activity playback.",
    "current-authorization-unsupported": "This backend cannot establish current playback access.",
    "history-invalid": "Recorded history integrity could not be established. Playback is unavailable.",
    "history-unavailable": "No usable recorded activity history is available for this run.",
    "scope-unavailable": "This execution scope is unavailable for playback.",
    "scope-ancestry-unknown": "Recorded ancestry is incomplete for this scope. Playback is unavailable.",
    "anchor-outside-range": "The selected occurrence is outside this retained playback range.",
    "anchor-outside-scope": "The selected occurrence is outside this playback scope.",
    "position-outside-range": "That activity position is outside the available playback range."
  };
  return reason && Object.hasOwn(reasons, reason) ? reasons[reason] : "Recorded activity playback is unavailable in this range.";
}
export function samePlaybackOccurrence(a: PlaybackOccurrence, b: PlaybackOccurrence) {
  return a.workflowExecutionId === b.workflowExecutionId && a.activityExecutionId === b.activityExecutionId
    && a.historyEventId === b.historyEventId && a.position === b.position && a.sequence === b.sequence;
}
