import { useCallback, useEffect, useRef, useState } from "react";
import { authSessionEndedEvent, authSessionStartedEvent, useStudioWorkspaceTab, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { ApiCapabilityUnavailableError, ApiCapabilityVersionMismatchError } from "../api/capabilities";
import { ActivityPlaybackIdentityError, playbackOrdinal, playbackWindow, readActivityPlayback, selectedPlaybackOccurrence,
  type PlaybackOccurrence, type PlaybackRequest } from "../api/activityPlayback";
import { runtimeValueContextId } from "./RuntimeValueEvidence";
import { initialPlaybackState, playbackBoundaryReason, playbackCadences, samePlaybackOccurrence,
  type ActivityPlaybackState, type PlaybackOperation, type PlaybackSpeed, type PlaybackTarget, type PreparedPlaybackSelection } from "./activityPlaybackModel";

interface PlaybackOptions {
  context: StudioEndpointContext; workflowExecutionId: string; enabled: boolean;
  activityExecutionId?: string | null; includeDescendants?: boolean; initialActivityExecutionId?: string | null;
  preserveInitialInspection?: boolean;
  initialSelectionKey?: string;
  resolveInitialSelection?(signal: AbortSignal): Promise<{ activityExecutionId?: string | null; historyEventId?: string; note?: string }>;
  prepareSelection(entry: PlaybackOccurrence, target: PlaybackTarget, operation: PlaybackOperation): Promise<PreparedPlaybackSelection>;
  onUnavailable?(reason: string): void;
}
type Intent = "initial" | "manual" | "automatic" | "refresh" | "replay";

/** Local presentation only. Every confirmation rechecks the owner; no cached row confers access. */
export function useActivityPlayback(options: PlaybackOptions) {
  const tab = useStudioWorkspaceTab();
  const inputs = useRef(options); inputs.current = options;
  const workspace = useRef(tab); workspace.current = tab;
  const [auth, setAuth] = useState({ allowed: true, revision: 0 });
  const access = useRef(auth); access.current = auth;
  const key = JSON.stringify([runtimeValueContextId(options.context), options.workflowExecutionId,
    options.activityExecutionId ?? null, !!options.includeDescendants, auth.revision, options.initialSelectionKey ?? null]);
  const sessionKey = JSON.stringify([runtimeValueContextId(options.context), options.workflowExecutionId, auth.revision, options.initialSelectionKey ?? null]);
  const sessionIdentity = useRef(sessionKey); sessionIdentity.current = sessionKey;
  const confirmedSession = useRef<string | null>(null);
  const identity = useRef(key); identity.current = key;
  const [stored, setStored] = useState(() => initialPlaybackState(key));
  const state = useRef(stored);
  const active = useRef(false), generation = useRef(0), timerRevision = useRef(0);
  const navigation = useRef<AbortController | null>(null), observation = useRef<AbortController | null>(null);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const actions = useRef<{ navigate(target: PlaybackTarget, intent: Intent): Promise<boolean | undefined>; pause(note?: string): void } | null>(null);
  const visible = useCallback(() => active.current && !document.hidden && workspace.current?.isActive() !== false, []);
  const publish = useCallback((next: ActivityPlaybackState) => { state.current = next; setStored(next); }, []);
  const stopTimer = useCallback(() => {
    timerRevision.current++;
    if (timer.current !== null) clearTimeout(timer.current);
    timer.current = null;
  }, []);
  const retire = useCallback(() => {
    generation.current++; stopTimer(); navigation.current?.abort(); observation.current?.abort();
  }, [stopTimer]);
  const pause = useCallback((note?: string) => {
    retire();
    const current = state.current;
    publish({ ...current, playing: false, pending: current.phase === "failed" ? current.pending : null, note: note ?? current.note,
      phase: current.phase === "failed" ? "failed" : current.cursor ? current.cursor.nextActivityExecutionId ? "paused" : "ended" : current.phase === "loading" ? "paused" : current.phase });
  }, [publish, retire]);
  const unavailable = useCallback((message: string, page: ActivityPlaybackState["page"] = null, forceClear = false) => {
    const hadSelection = !!state.current.cursor || confirmedSession.current === sessionIdentity.current;
    retire();
    publish({ ...initialPlaybackState(identity.current), speed: state.current.speed, phase: "unavailable", page, error: message });
    if (hadSelection || forceClear) inputs.current.onUnavailable?.(message);
  }, [publish, retire]);
  const failure = useCallback((error: unknown, target: PlaybackTarget, intent: Intent = "manual", preserveRecovery = false) => {
    const status = typeof error === "object" && error !== null && "status" in error ? Number(error.status) : 0;
    if ([400, 401, 403, 404, 409, 501].includes(status) || error instanceof ActivityPlaybackIdentityError
      || error instanceof ApiCapabilityUnavailableError || error instanceof ApiCapabilityVersionMismatchError) {
      unavailable(status === 401 || status === 403 ? "Playback is unavailable with your current access."
        : status === 404 ? "This recorded range is unavailable or no longer accessible."
          : error instanceof ApiCapabilityUnavailableError || error instanceof ApiCapabilityVersionMismatchError || status === 501
            ? "This backend does not support recorded activity playback."
            : "The recorded activity range could not be verified. Refresh playback to recover.", null, status === 401 || status === 403);
      return;
    }
    if (preserveRecovery && state.current.phase === "failed") return;
    stopTimer();
    publish({ ...state.current, playing: false, phase: "failed", pending: target,
      retryIntent: intent === "initial" || intent === "refresh" ? intent : "manual",
      error: "The requested activity could not be loaded. The previous confirmed position is unchanged." });
  }, [publish, stopTimer, unavailable]);

  const navigate = useCallback(async (target: PlaybackTarget, intent: Intent) => {
    if (!visible() || !access.current.allowed || !inputs.current.enabled) return;
    if (!navigator.onLine) { pause("Playback paused while offline."); return; }
    const previous = state.current;
    const keepPlaying = intent === "automatic" && previous.playing || intent === "replay";
    retire();
    const controller = new AbortController(); navigation.current = controller;
    const operationKey = identity.current, operationGeneration = generation.current, operationSession = sessionIdentity.current;
    const current = () => !controller.signal.aborted && identity.current === operationKey && generation.current === operationGeneration
      && access.current.allowed && visible() && inputs.current.enabled;
    const input = inputs.current;
    const scope = { activityExecutionId: input.activityExecutionId, includeDescendants: input.includeDescendants };
    const fresh = intent === "refresh" || !previous.window;
    const request: PlaybackRequest = { ...scope, ...(fresh ? {} : { window: previous.window! }),
      ...(target.position !== undefined ? { position: target.position } : { anchorActivityExecutionId: target.activityExecutionId }) };
    publish({ ...previous, pending: target, playing: keepPlaying, phase: keepPlaying ? "buffering" : "loading", error: "", note: "" });
    try {
      let page = await readActivityPlayback(input.context, input.workflowExecutionId, request, controller.signal);
      if (!current()) return;
      if (!fresh && intent === "manual" && page.state === "target-unavailable") {
        publish({ ...previous, playing: false, phase: "paused", pending: null, error: "",
          note: `${playbackBoundaryReason(page.reason)} The confirmed playback position is unchanged.`, outsideSelection: target.activityExecutionId ?? null });
        return false;
      }
      let note = "";
      let preparedTarget = target;
      // Only explicit fresh admission may choose a first step when the former anchor is ineligible.
      if (fresh && (intent === "initial" || intent === "refresh") && target.activityExecutionId
        && page.state === "target-unavailable" && ["anchor-outside-range", "anchor-outside-scope"].includes(page.reason ?? "")) {
        note = `${playbackBoundaryReason(page.reason)} Showing the first available occurrence.`;
        page = await readActivityPlayback(input.context, input.workflowExecutionId, scope, controller.signal);
        if (!current()) return;
      }
      if (fresh && intent === "initial" && confirmedSession.current !== operationSession && input.resolveInitialSelection && playbackWindow(page)) {
        // Discover support before fetching a pre-existing inspector target. Unsupported hosts keep
        // their existing inspection/read behavior; a link owner never comes from a loaded-page guess.
        const original = page;
        const selection = await input.resolveInitialSelection(controller.signal);
        if (!current()) return;
        note = selection.note ?? note;
        if (selection.activityExecutionId) {
          const anchored = await readActivityPlayback(input.context, input.workflowExecutionId,
            { ...scope, window: playbackWindow(original)!, anchorActivityExecutionId: selection.activityExecutionId }, controller.signal);
          if (!current()) return;
          if (anchored.state === "target-unavailable") note = `${playbackBoundaryReason(anchored.reason)} Showing the first available occurrence; the existing inspection is unchanged.`;
          else {
            page = anchored;
            if (selection.historyEventId && selectedPlaybackOccurrence(anchored)?.activityExecutionId === selection.activityExecutionId)
              preparedTarget = { ...target, historyEventId: selection.historyEventId };
          }
        }
      }
      const window = playbackWindow(page);
      if (!window) { unavailable(playbackBoundaryReason(page.reason), page); return; }
      const entry = selectedPlaybackOccurrence(page);
      if (!entry) {
        if (previous.cursor || confirmedSession.current === operationSession) input.onUnavailable?.("No recorded activity occurrences are available in this segment.");
        publish({ ...previous, key: operationKey, page, window, cursor: null, pending: null, playing: false,
          phase: "paused", error: "", note: "No recorded activity occurrences are available in this segment.", newer: page.hasNewerHistory, outsideSelection: null });
        return;
      }
      const prepared = await input.prepareSelection(entry, { ...preparedTarget,
        ...(intent === "initial" ? { preserveInspector: true, preserveSelection: confirmedSession.current !== operationSession
          ? !!input.preserveInitialInspection : !!target.preserveSelection && target.activityExecutionId === entry.activityExecutionId } : {}),
        rememberChoice: intent === "manual" }, { signal: controller.signal, isCurrent: current });
      if (!current()) return;
      // Preparation may await ancestry, diagram and details. Recheck retention/current grants afterward.
      const confirmed = await readActivityPlayback(input.context, input.workflowExecutionId,
        { ...scope, window, position: entry.position }, controller.signal);
      if (!current()) return;
      if (!playbackWindow(confirmed)) { unavailable(playbackBoundaryReason(confirmed.reason), confirmed); return; }
      const exact = selectedPlaybackOccurrence(confirmed);
      if (!exact || !samePlaybackOccurrence(entry, exact)) throw new ActivityPlaybackIdentityError();
      prepared.commit();
      if (!current()) return;
      confirmedSession.current = operationSession;
      const ended = exact.nextActivityExecutionId === null;
      publish({ key: operationKey, page: confirmed, window, cursor: exact, pending: null, playing: keepPlaying && !ended,
        phase: ended ? "ended" : keepPlaying ? "playing" : "paused", speed: state.current.speed, error: "", note,
        diagramLimitation: prepared.limitation ?? "", newer: fresh ? confirmed.hasNewerHistory : previous.newer || confirmed.hasNewerHistory,
        outsideSelection: null, retryIntent: "manual" });
      return true;
    } catch (error) { if (current()) failure(error, target, intent); }
  }, [failure, pause, publish, retire, unavailable, visible]);
  actions.current = { navigate, pause };

  useEffect(() => {
    const change = (allowed: boolean) => {
      retire(); access.current = { allowed, revision: access.current.revision + 1 };
      publish({ ...initialPlaybackState(identity.current), phase: "unavailable", error: allowed ? "Revalidating playback access…" : "Sign in again to inspect playback." });
      inputs.current.onUnavailable?.("Playback access changed."); setAuth(access.current);
    };
    const end = () => change(false), start = () => change(true);
    const visibility = () => { if (document.hidden) actions.current?.pause("Playback paused while this view was in the background."); };
    const offline = () => actions.current?.pause("Playback paused while offline.");
    window.addEventListener(authSessionEndedEvent, end); window.addEventListener(authSessionStartedEvent, start);
    document.addEventListener("visibilitychange", visibility); window.addEventListener("offline", offline);
    return () => {
      window.removeEventListener(authSessionEndedEvent, end); window.removeEventListener(authSessionStartedEvent, start);
      document.removeEventListener("visibilitychange", visibility); window.removeEventListener("offline", offline);
    };
  }, [publish, retire]);

  useEffect(() => {
    active.current = true;
    if (!options.enabled || !auth.allowed) { retire(); publish({ ...initialPlaybackState(key), phase: "unavailable" }); }
    else if (tab?.active === false) actions.current?.pause("Playback paused while this workspace was in the background.");
    else {
      const previous = state.current;
      if (previous.key !== key) publish(initialPlaybackState(key));
      const initialAnchor = inputs.current.resolveInitialSelection && confirmedSession.current !== sessionIdentity.current ? null : inputs.current.initialActivityExecutionId;
      const anchor = confirmedSession.current === sessionIdentity.current ? previous.cursor?.activityExecutionId ?? initialAnchor : initialAnchor;
      if (previous.key !== key || previous.phase !== "failed")
        void actions.current?.navigate(anchor ? { activityExecutionId: anchor,
          preserveSelection: confirmedSession.current === sessionIdentity.current && !!previous.cursor } : {}, "initial");
    }
    return () => { actions.current?.pause(); active.current = false; };
  }, [auth.allowed, key, options.enabled, publish, retire, tab?.active]);

  useEffect(() => {
    stopTimer();
    if (stored.key !== key || !stored.playing || stored.pending || stored.phase !== "playing") return;
    const version = timerRevision.current, owner = generation.current;
    timer.current = setTimeout(() => {
      if (version !== timerRevision.current || owner !== generation.current) return;
      const current = state.current;
      if (!visible()) { actions.current?.pause("Playback paused while this view was in the background."); return; }
      if (current.playing && current.cursor?.nextActivityExecutionId)
        void actions.current?.navigate({ position: String(playbackOrdinal(current.cursor.position) + 1n) }, "automatic");
    }, playbackCadences[stored.speed]);
    return stopTimer;
  }, [key, stored.cursor, stored.key, stored.pending, stored.phase, stored.playing, stored.speed, stopTimer, visible]);

  useEffect(() => {
    if (!options.enabled || !auth.allowed) return;
    let disposed = false;
    let next: ReturnType<typeof setTimeout>;
    const observe = async () => {
      const current = state.current, owner = generation.current, operationKey = identity.current;
      if (visible() && navigator.onLine && current.key === operationKey && current.window && (!current.pending || current.phase === "failed")) {
        const controller = new AbortController(); observation.current = controller;
        try {
          const input = inputs.current;
          const page = await readActivityPlayback(input.context, input.workflowExecutionId, { window: current.window, ...(current.cursor ? { position: current.cursor.position } : {}),
            activityExecutionId: input.activityExecutionId, includeDescendants: input.includeDescendants }, controller.signal);
          if (!disposed && !controller.signal.aborted && operationKey === identity.current && owner === generation.current && visible()) {
            if (!playbackWindow(page)) unavailable(playbackBoundaryReason(page.reason), page);
            else {
              const exact = selectedPlaybackOccurrence(page);
              if (current.cursor ? !exact || !samePlaybackOccurrence(current.cursor, exact) : exact !== null) throw new ActivityPlaybackIdentityError();
              publish({ ...state.current, page, newer: state.current.newer || page.hasNewerHistory });
            }
          }
        } catch (error) {
          if (!disposed && !controller.signal.aborted && operationKey === identity.current && owner === generation.current && visible())
            failure(error, current.cursor ? { position: current.cursor.position } : {}, "manual", current.phase === "failed");
        }
      }
      if (!disposed) next = setTimeout(() => { void observe(); }, 5000);
    };
    next = setTimeout(() => { void observe(); }, 5000);
    return () => { disposed = true; clearTimeout(next); observation.current?.abort(); };
  }, [auth.allowed, failure, key, options.enabled, publish, unavailable, visible]);

  const value = !auth.allowed ? { ...initialPlaybackState(key), phase: "unavailable" as const, error: "Sign in again to inspect playback." }
    : stored.key === key ? stored : initialPlaybackState(key);
  return {
    ...value,
    pause,
    invalidate: (message: string) => unavailable(message, null, true),
    selectOccurrence: (activityExecutionId: string, historyEventId?: string) => navigate({ activityExecutionId, historyEventId }, "manual"),
    inspectOccurrence: (activityExecutionId: string, includeSelfScope = false) => navigate({ activityExecutionId, inspectActivity: true, includeSelfScope }, "manual"),
    seek: (position: string) => navigate({ position }, "manual"),
    previous: () => value.cursor?.previousActivityExecutionId ? navigate({ position: String(playbackOrdinal(value.cursor.position) - 1n) }, "manual") : Promise.resolve(),
    next: () => value.cursor?.nextActivityExecutionId ? navigate({ position: String(playbackOrdinal(value.cursor.position) + 1n) }, "manual") : Promise.resolve(),
    play: () => {
      const current = state.current;
      if (!navigator.onLine) { pause("Playback paused while offline."); return; }
      if (!visible() || !access.current.allowed || !inputs.current.enabled || current.key !== identity.current
        || !current.cursor || current.pending || current.error || !current.cursor.nextActivityExecutionId) return;
      publish({ ...current, playing: true, phase: "playing", note: "" });
    },
    replay: () => navigate({ position: "0" }, "replay"),
    setSpeed: (speed: PlaybackSpeed) => { if (Object.hasOwn(playbackCadences, speed) && speed !== state.current.speed) { stopTimer(); publish({ ...state.current, speed }); } },
    refresh: () => navigate(state.current.outsideSelection || state.current.cursor
      ? { activityExecutionId: state.current.outsideSelection ?? state.current.cursor!.activityExecutionId } : {}, "refresh"),
    retry: () => navigate(state.current.pending ?? (state.current.cursor ? { activityExecutionId: state.current.cursor.activityExecutionId } : {}), state.current.retryIntent)
  };
}
export type ActivityPlayback = ReturnType<typeof useActivityPlayback>;
