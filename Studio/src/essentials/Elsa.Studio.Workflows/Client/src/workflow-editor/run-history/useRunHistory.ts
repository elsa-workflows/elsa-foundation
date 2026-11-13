import { useCallback, useEffect, useRef, useState } from "react";
import { authSessionEndedEvent, authSessionStartedEvent, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { ApiCapabilityUnavailableError, ApiCapabilityVersionMismatchError } from "../../api/capabilities";
import { getWorkflowHistoryPage, RuntimeHistoryIdentityError, supportsWorkflowHistory } from "../../api/runtimeHistory";
import type { RuntimeHistoryEvent, RuntimeHistoryPage, RuntimeHistoryRequest } from "../../runtimeHistoryTypes";
import { runtimeValueContextId } from "../RuntimeValueEvidence";

export type HistoryReadStatus = "loading" | "ready" | "stale" | "failed" | "denied" | "unsupported" | "unavailable";
interface HistoryWindow {
  page: RuntimeHistoryPage;
  items: RuntimeHistoryEvent[];
  pending: RuntimeHistoryEvent[];
  next: string | null;
  catchUp: string | null;
  liveNext: string | null;
  scannedCount: number;
  snapshotUpperSequence: number;
  snapshotScannedThroughSequence: number;
}
interface HistoryState { key: string; status: HistoryReadStatus; loading: boolean; value: HistoryWindow | null; error: string }
type ReadIntent = "initial" | "more" | "poll" | "refresh";

const immutableEventFields = ["historyEventId", "workflowExecutionId", "sequence", "schemaVersion", "name", "category", "authority", "occurredAt", "occurredAtKnown", "activityAncestryKnown", "recordedAt",
  "activityExecutionId", "executableNodeId", "checkpointId", "checkpointOrdinal", "physicalCommitId", "bookmarkId", "incidentId", "attemptId",
  "parentActivityExecutionId", "executionScopeId", "iterationId"] as const;
function immutableEventContent(event: RuntimeHistoryEvent) {
  return JSON.stringify([immutableEventFields.map(key => event[key] ?? null),
    ["historyEventId", "workflowExecutionId", "deliveryId", "commandId", "schedulerWorkItemId"].map(key => event.causation?.[key as keyof NonNullable<RuntimeHistoryEvent["causation"]>] ?? null),
    Object.entries(event.metadata ?? {}).sort(([left], [right]) => left < right ? -1 : left > right ? 1 : 0)]);
}

function assertUnchangedEvent(previous: RuntimeHistoryEvent, incoming: RuntimeHistoryEvent) {
  if (previous.sequence !== incoming.sequence) throw new RuntimeHistoryIdentityError("A history event changed its recorded position.");
  if (immutableEventContent(previous) !== immutableEventContent(incoming)) throw new RuntimeHistoryIdentityError("An immutable history event changed its recorded content.");
}

export function mergeHistoryEvents(existing: readonly RuntimeHistoryEvent[], incoming: readonly RuntimeHistoryEvent[]) {
  const map = new Map(existing.map(item => [item.historyEventId, item]));
  for (const item of incoming) {
    const previous = map.get(item.historyEventId);
    if (previous) assertUnchangedEvent(previous, item);
    map.set(item.historyEventId, item);
  }
  const ordered = [...map.values()].sort((left, right) => left.sequence - right.sequence);
  for (let index = 1; index < ordered.length; index++) if (ordered[index].sequence === ordered[index - 1].sequence)
    throw new RuntimeHistoryIdentityError("Different history events occupied the same recorded position.");
  return ordered;
}

/** One serial bounded read at a time. A settled prefix is never treated as permanent source closure. */
export function useRunHistory(context: StudioEndpointContext, workflowExecutionId: string,
  request: RuntimeHistoryRequest = {}, enabled = true) {
  const requestKey = JSON.stringify({ ...request, continuationToken: undefined, limit: request.limit ?? 100 });
  const requestRef = useRef(request); requestRef.current = request;
  const [auth, setAuth] = useState({ allowed: true, revision: 0 });
  const [visible, setVisible] = useState(() => !document.hidden);
  const [online, setOnline] = useState(() => navigator.onLine);
  const [following, setFollowing] = useState(true);
  const [revision, setRevision] = useState(0);
  const [state, setState] = useState<HistoryState | null>(null);
  const key = JSON.stringify([runtimeValueContextId(context), workflowExecutionId, requestKey, auth.revision]);
  const currentKey = useRef(key); currentKey.current = key;
  const currentState = useRef(state); currentState.current = state;
  const pendingIntent = useRef<ReadIntent>("initial");
  const active = useRef<AbortController | null>(null);
  const publish = useCallback((value: HistoryState) => {
    currentState.current = value;
    setState(value);
  }, []);
  const issue = useCallback((intent: ReadIntent) => {
    pendingIntent.current = intent;
    setRevision(value => value + 1);
  }, []);

  useEffect(() => {
    const invalidate = (allowed: boolean) => {
      active.current?.abort(); currentState.current = null; setState(null);
      pendingIntent.current = "initial"; setAuth(value => ({ allowed, revision: value.revision + 1 }));
    };
    const ended = () => invalidate(false), started = () => invalidate(true);
    const visibility = () => setVisible(!document.hidden);
    const connected = () => setOnline(true);
    const disconnected = () => { active.current?.abort(); setOnline(false); };
    window.addEventListener(authSessionEndedEvent, ended);
    window.addEventListener(authSessionStartedEvent, started);
    document.addEventListener("visibilitychange", visibility);
    window.addEventListener("online", connected); window.addEventListener("offline", disconnected);
    return () => {
      window.removeEventListener(authSessionEndedEvent, ended); window.removeEventListener(authSessionStartedEvent, started);
      document.removeEventListener("visibilitychange", visibility);
      window.removeEventListener("online", connected); window.removeEventListener("offline", disconnected);
    };
  }, []);

  useEffect(() => {
    if (!enabled || !workflowExecutionId || !auth.allowed || !visible || !online) return;
    let disposed = false;
    let timer: number | undefined;
    let failures = 0;
    const owns = () => !disposed && currentKey.current === key;
    const read = async (intent: ReadIntent) => {
      if (!owns()) return;
      const previousState = currentState.current?.key === key ? currentState.current : null;
      const previous = previousState?.value ?? null;
      if (previous && (intent === "poll" && !following || (intent === "poll" || intent === "refresh") && previous.pending.length >= 500)) return;
      const kind = previous ? intent : "initial";
      const cursor = kind === "more" ? previous?.next : (kind === "poll" || kind === "refresh") ? previous?.liveNext ?? previous?.catchUp : null;
      if (kind !== "initial" && !cursor) return;
      active.current?.abort();
      const controller = new AbortController(); active.current = controller;
      const current = () => owns() && active.current === controller && !controller.signal.aborted;
      publish({ key, value: previous, loading: true, status: previous ? previousState?.status === "stale" ? "stale" : "ready" : "loading", error: previousState?.error ?? "" });
      try {
        if (!previous && !await supportsWorkflowHistory(context)) throw new ApiCapabilityUnavailableError("elsa.api.runtime", "workflow-history-page");
        const requestedLimit = requestRef.current.limit ?? 100;
        const limit = previous && (kind === "poll" || kind === "refresh")
          ? Math.min(requestedLimit, 500 - previous.pending.length) : requestedLimit;
        const page = await getWorkflowHistoryPage(context, workflowExecutionId,
          { ...requestRef.current, limit, continuationToken: cursor }, controller.signal);
        if (!current()) return;
        if (page.nextContinuationToken && page.nextContinuationToken === cursor) throw new RuntimeHistoryIdentityError("History continuation did not advance.");
        let value: HistoryWindow;
        if (!previous || kind === "initial") value = { page, items: page.items, pending: [], next: page.nextContinuationToken ?? null,
          catchUp: page.catchUpToken ?? null, liveNext: null, scannedCount: page.scannedCount, snapshotUpperSequence: page.upperSequence, snapshotScannedThroughSequence: page.scannedThroughSequence };
        else if (kind === "more") value = { ...previous, page: { ...page, scopeComplete: previous.page.scopeComplete && page.scopeComplete,
          scopeLimitation: previous.page.scopeLimitation ?? page.scopeLimitation }, items: mergeHistoryEvents(previous.items, page.items),
          next: page.nextContinuationToken ?? null, scannedCount: previous.scannedCount + page.scannedCount, snapshotScannedThroughSequence: page.scannedThroughSequence };
        else {
          const known = new Map(previous.items.map(item => [item.historyEventId, item]));
          const positions = new Map(previous.items.map(item => [item.sequence, item.historyEventId]));
          for (const item of page.items) {
            const existing = known.get(item.historyEventId);
            if (existing) assertUnchangedEvent(existing, item);
            else if (positions.has(item.sequence)) throw new RuntimeHistoryIdentityError("Different history events occupied the same recorded position.");
          }
          value = { ...previous, page: { ...page, scopeComplete: previous.page.scopeComplete && page.scopeComplete,
            scopeLimitation: previous.page.scopeLimitation ?? page.scopeLimitation }, pending: mergeHistoryEvents(previous.pending, page.items.filter(item => !known.has(item.historyEventId))),
            liveNext: page.nextContinuationToken ?? null, catchUp: page.catchUpToken ?? previous.catchUp };
        }
        publish({ key, value, loading: false, status: "ready", error: "" });
        failures = 0;
      } catch (error) {
        if (!current()) return;
        const status = typeof error === "object" && error !== null && "status" in error ? Number(error.status) : 0;
        const unsupported = error instanceof ApiCapabilityUnavailableError || error instanceof ApiCapabilityVersionMismatchError;
        const forbidden = status === 401 || status === 403;
        const unsafe = error instanceof RuntimeHistoryIdentityError || status === 404;
        publish({ key, value: forbidden || unsafe || unsupported ? null : previous, loading: false,
          status: unsupported ? "unsupported" : forbidden ? "denied" : unsafe ? "unavailable" : previous ? "stale" : "failed",
          error: unsupported ? "This host does not support durable run history." : forbidden ? "Run history is unavailable with your current access."
            : status === 404 ? "This run history is unavailable or no longer retained."
              : error instanceof Error ? error.message : "Run history could not be read." });
        if (unsupported || forbidden || unsafe) return;
        failures++;
      }
      // No workflow-terminal or settled-prefix check: later waits/deliveries may append more history.
      if (current() && following) timer = window.setTimeout(() => { void read("poll"); }, failures ? Math.min(30000, 2000 * 2 ** failures) : 2000);
    };
    const intent = pendingIntent.current; pendingIntent.current = "poll";
    void read(intent);
    return () => { disposed = true; active.current?.abort(); if (timer !== undefined) window.clearTimeout(timer); };
  }, [auth.allowed, context, enabled, following, key, online, publish, revision, visible, workflowExecutionId]);

  const current = enabled && auth.allowed && state?.key === key ? state : null;
  const showNew = useCallback(() => {
    const value = currentState.current;
    if (!value || value.key !== currentKey.current || !value.value) return;
    publish({ ...value, value: { ...value.value, items: mergeHistoryEvents(value.value.items, value.value.pending), pending: [] } });
    issue("poll");
  }, [issue, publish]);
  return {
    identity: key, value: current?.value ?? null,
    status: !auth.allowed ? "denied" as const : current?.status ?? "loading" as const,
    error: !auth.allowed ? "Sign in again to inspect run history." : !online ? "Offline · showing last confirmed history." : current?.error ?? "",
    loading: enabled && visible && online && (current ? current.loading && !active.current?.signal.aborted : auth.allowed), paused: !following || !visible || !online,
    following, setFollowing, refresh: () => issue(currentState.current?.key === currentKey.current && currentState.current.value?.catchUp ? "refresh" : "initial"), loadMore: () => issue("more"), showNew
  };
}
