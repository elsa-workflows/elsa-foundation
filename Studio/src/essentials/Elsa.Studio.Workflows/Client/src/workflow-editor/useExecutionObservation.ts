import { useCallback, useEffect, useRef, useState } from "react";
import { authSessionEndedEvent, authSessionStartedEvent, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { ApiCapabilityUnavailableError, ApiCapabilityVersionMismatchError } from "../api/capabilities";
import { getWorkflowInstance } from "../api/runtime";
import { runtimeValueContextId } from "./RuntimeValueEvidence";

export type ExecutionObservationStatus = "loading" | "connected" | "paused" | "disconnected" | "unavailable" | "expired";
export class ExecutionObservationIdentityError extends Error {}
export interface ExecutionObservationReadScope<T> {
  identity: string;
  signal: AbortSignal;
  isCurrent(): boolean;
  publish(recipe: (previous: T | null) => T): void;
}
interface ReadResult<T> { value: T | null; terminal: boolean; expired?: boolean }
interface Observation<T> { key: string; status: ExecutionObservationStatus; value: T | null; error: string; revision: number }

export function isTerminalExecution(status?: string) {
  return status === "Completed" || status === "Faulted" || status === "Cancelled";
}

export async function readObservedExecution(context: StudioEndpointContext, executionId: string, artifactId?: string | null, signal?: AbortSignal) {
  const details = await getWorkflowInstance(context, executionId, signal);
  if (details.instance.workflowExecutionId !== executionId || artifactId && details.instance.artifactId !== artifactId)
    throw new ExecutionObservationIdentityError("Runtime evidence identity did not match this execution.");
  return details;
}

/** Shared serial observation for editor attachments and directly opened runtime instances. */
export function useExecutionObservation<T>({ context, identity, enabled = true, read }: {
  context: StudioEndpointContext;
  identity: string;
  enabled?: boolean;
  read(scope: ExecutionObservationReadScope<T>): Promise<ReadResult<T>>;
}) {
  const [refreshRevision, setRefreshRevision] = useState(0);
  const [session, setSession] = useState({ authorized: true, revision: 0 });
  const [visible, setVisible] = useState(() => !document.hidden);
  const [online, setOnline] = useState(() => navigator.onLine);
  const [state, setState] = useState<Observation<T> | null>(null);
  const reader = useRef(read); reader.current = read;
  const key = JSON.stringify([runtimeValueContextId(context), identity, session.revision]);
  const [followChoice, setFollowChoice] = useState<{ key: string; following: boolean } | null>(null);
  const following = followChoice?.key === key ? followChoice.following : true;
  const allowed = enabled && session.authorized;
  const currentKey = useRef(key); currentKey.current = key;
  const active = useRef(false); active.current = allowed && visible && online;
  const request = useRef<AbortController | null>(null);
  const forcedRefresh = useRef(0);
  const consumedRefresh = useRef(0);
  const refresh = useCallback(() => { forcedRefresh.current++; setRefreshRevision(value => value + 1); }, []);
  const isCurrent = useCallback(() => active.current && currentKey.current === key, [key]);
  const updateValue = useCallback((recipe: (value: T) => T) => {
    if (!isCurrent()) return;
    setState(value => value?.key === key && value.status === "connected" && value.value !== null
      ? { ...value, value: recipe(value.value) } : value);
  }, [isCurrent, key]);

  useEffect(() => {
    const invalidate = (authorized: boolean) => {
      active.current = false; request.current?.abort();
      setState(null); setSession(value => ({ authorized, revision: value.revision + 1 }));
    };
    const end = () => invalidate(false);
    const start = () => invalidate(true);
    const visibility = () => setVisible(!document.hidden);
    const connect = () => setOnline(true);
    const disconnect = () => { active.current = false; request.current?.abort(); setOnline(false); };
    window.addEventListener(authSessionEndedEvent, end);
    window.addEventListener(authSessionStartedEvent, start);
    document.addEventListener("visibilitychange", visibility);
    window.addEventListener("online", connect);
    window.addEventListener("offline", disconnect);
    return () => {
      window.removeEventListener(authSessionEndedEvent, end);
      window.removeEventListener(authSessionStartedEvent, start);
      document.removeEventListener("visibilitychange", visibility);
      window.removeEventListener("online", connect);
      window.removeEventListener("offline", disconnect);
    };
  }, []);

  useEffect(() => {
    if (!allowed || !visible || !online || !following && forcedRefresh.current === consumedRefresh.current) return;
    consumedRefresh.current = forcedRefresh.current;
    let disposed = false;
    let timer: number | undefined;
    let failures = 0;
    const ownsLoop = () => !disposed && active.current && currentKey.current === key;
    const poll = async () => {
      if (!ownsLoop()) return;
      request.current?.abort();
      const controller = new AbortController(); request.current = controller;
      const current = () => ownsLoop() && request.current === controller && !controller.signal.aborted;
      let terminal = false;
      let published = false;
      const publish = (recipe: (previous: T | null) => T) => {
        if (!current()) return;
        const increment = published ? 0 : 1; published = true;
        setState(value => ({ key, value: recipe(value?.key === key ? value.value : null), status: "connected", error: "",
          revision: (value?.key === key ? value.revision : 0) + increment }));
      };
      try {
        const result = await reader.current({ identity: key, signal: controller.signal, isCurrent: current, publish });
        if (!current()) return;
        terminal = result.terminal || !!result.expired;
        failures = 0;
        setState(value => ({ key, value: result.expired ? null : result.value, status: result.expired ? "expired" : "connected",
          error: result.expired ? "This Test Run has expired." : "", revision: (value?.key === key ? value.revision : 0) + (published ? 0 : 1) }));
      } catch (error) {
        if (!current()) return;
        const status = typeof error === "object" && error !== null && "status" in error ? error.status : undefined;
        const unavailable = error instanceof ApiCapabilityUnavailableError || error instanceof ApiCapabilityVersionMismatchError
          || error instanceof ExecutionObservationIdentityError || status === 401 || status === 403;
        setState(value => ({ key, value: null, status: unavailable ? "unavailable" : "disconnected",
          error: error instanceof ExecutionObservationIdentityError ? error.message : unavailable ? "Live runtime state is unavailable on this host."
            : error instanceof Error ? error.message : "Execution observation disconnected.", revision: value?.key === key ? value.revision : 0 }));
        // Cancel any parallel reads belonging to this failed reconciliation before a retry starts.
        controller.abort();
        if (unavailable) return;
        failures++;
      }
      if (ownsLoop() && !terminal && following) timer = window.setTimeout(() => { void poll(); }, failures ? Math.min(30000, 2000 * 2 ** failures) : 2000);
    };
    setState(value => value?.key === key && value.value !== null ? { ...value, status: "connected" }
      : { key, value: null, status: "loading", error: "", revision: value?.key === key ? value.revision : 0 });
    void poll();
    return () => { disposed = true; request.current?.abort(); if (timer !== undefined) window.clearTimeout(timer); };
  }, [allowed, context, following, key, online, refreshRevision, visible]);

  const current = allowed && state?.key === key ? state : null;
  const failed = current?.status === "disconnected" || current?.status === "unavailable" || current?.status === "expired";
  const status: ExecutionObservationStatus = !allowed ? "unavailable" : !online ? "disconnected" : failed ? current.status
    : !visible || !following ? "paused" : current?.status ?? "loading";
  return {
    identity: key, status, value: status === "connected" || status === "paused" ? current?.value ?? null : null,
    error: !session.authorized ? "Sign in again to observe this execution." : !online ? "Execution observation disconnected. Reconnect to refresh." : current?.error ?? "",
    revision: current?.revision ?? 0, following,
    canInteract: allowed && visible && online && current?.status === "connected",
    setFollowing: (value: boolean) => setFollowChoice({ key, following: value }), refresh, isCurrent, updateValue
  };
}
