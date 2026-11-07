import { useEffect, useRef, useState } from "react";
import { authSessionEndedEvent, authSessionStartedEvent, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { ApiCapabilityUnavailableError, ApiCapabilityVersionMismatchError } from "../../api/capabilities";
import { RuntimeHistoryIdentityError } from "../../api/runtimeHistory";
import { runtimeValueContextId } from "../RuntimeValueEvidence";

/** Exact/list drill-down reads have the same access invalidation as live history, without borrowing its cursors. */
export function useHistoryResource<T>(context: StudioEndpointContext, identity: string, read: (signal: AbortSignal) => Promise<T>, poll = true, enabled = true, refreshKey: string | number = 0) {
  const [auth, setAuth] = useState({ allowed: true, revision: 0 });
  const [visible, setVisible] = useState(() => !document.hidden);
  const [online, setOnline] = useState(() => navigator.onLine);
  const [revision, setRevision] = useState(0);
  const [reading, setReading] = useState<{ key: string; running: boolean } | null>(null);
  const [state, setState] = useState<{ key: string; value: T | null; status: "ready" | "stale" | "failed" | "unavailable"; error: string } | null>(null);
  const sessionIdentity = JSON.stringify([runtimeValueContextId(context), auth.revision]);
  const key = JSON.stringify([sessionIdentity, identity]);
  const currentKey = useRef(key); currentKey.current = key;
  const reader = useRef(read); reader.current = read;
  const active = useRef<AbortController | null>(null);
  const forceRead = useRef<(() => void) | null>(null);
  const latestRefreshKey = useRef(refreshKey); latestRefreshKey.current = refreshKey;
  const consumedRefreshKey = useRef(refreshKey);
  useEffect(() => {
    const change = (allowed: boolean) => { active.current?.abort(); setState(null); setAuth(value => ({ allowed, revision: value.revision + 1 })); };
    const end = () => change(false), start = () => change(true);
    const visibility = () => setVisible(!document.hidden), connected = () => setOnline(true);
    const disconnected = () => { active.current?.abort(); setOnline(false); };
    window.addEventListener(authSessionEndedEvent, end); window.addEventListener(authSessionStartedEvent, start);
    document.addEventListener("visibilitychange", visibility); window.addEventListener("online", connected); window.addEventListener("offline", disconnected);
    return () => {
      window.removeEventListener(authSessionEndedEvent, end); window.removeEventListener(authSessionStartedEvent, start);
      document.removeEventListener("visibilitychange", visibility); window.removeEventListener("online", connected); window.removeEventListener("offline", disconnected);
    };
  }, []);
  useEffect(() => {
    if (!enabled || !auth.allowed || !visible || !online) return;
    let disposed = false;
    let timer: number | undefined;
    let running = false, queued = false, blocked = false;
    consumedRefreshKey.current = latestRefreshKey.current;
    const load = async () => {
      if (disposed || blocked) return;
      if (running) { queued = true; return; }
      running = true;
      active.current?.abort(); const controller = new AbortController(); active.current = controller;
      const current = () => !disposed && currentKey.current === key && !controller.signal.aborted;
      let stop = false;
      setReading({ key, running: true });
      try {
        const value = await reader.current(controller.signal);
        if (current()) setState({ key, value, status: "ready", error: "" });
      } catch (error) {
        if (!current()) return;
        const status = typeof error === "object" && error !== null && "status" in error ? Number(error.status) : 0;
        stop = status === 401 || status === 403 || status === 404 || error instanceof RuntimeHistoryIdentityError
          || error instanceof ApiCapabilityUnavailableError || error instanceof ApiCapabilityVersionMismatchError;
        const message = status === 401 || status === 403 ? "History is unavailable with your current access."
          : status === 404 ? "This recorded target is unavailable or no longer retained."
            : error instanceof ApiCapabilityUnavailableError || error instanceof ApiCapabilityVersionMismatchError ? "This history view is unsupported by the current host."
              : error instanceof Error ? error.message : "Recorded history could not be read.";
        setState(previous => ({ key, value: !stop && previous?.key === key ? previous.value : null,
          status: stop ? "unavailable" : previous?.key === key && previous.value ? "stale" : "failed", error: message }));
      } finally { running = false; if (current()) setReading({ key, running: false }); }
      if (stop) { blocked = true; queued = false; }
      if (!disposed && !stop && queued) { queued = false; void load(); }
      else if (!disposed && poll && !stop) timer = window.setTimeout(() => { void load(); }, 5000);
    };
    const force = () => { if (timer !== undefined) window.clearTimeout(timer); void load(); };
    forceRead.current = force;
    void load();
    return () => { disposed = true; if (forceRead.current === force) forceRead.current = null; active.current?.abort(); if (timer !== undefined) window.clearTimeout(timer); };
  }, [auth.allowed, enabled, key, online, poll, revision, visible]);
  useEffect(() => {
    if (consumedRefreshKey.current !== refreshKey) { consumedRefreshKey.current = refreshKey; forceRead.current?.(); }
  }, [refreshKey]);
  const current = auth.allowed && state?.key === key ? state : null;
  return { identity: key, sessionIdentity, loading: enabled && auth.allowed && online && visible && reading?.key === key && reading.running && !active.current?.signal.aborted, value: current?.value ?? null, status: !auth.allowed ? "unavailable" : current?.status ?? "loading",
    error: !auth.allowed ? "Sign in again to inspect history." : !online ? "Offline · showing last confirmed history." : current?.error ?? "",
    retry: () => setRevision(value => value + 1) };
}
