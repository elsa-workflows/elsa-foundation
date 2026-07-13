import { useCallback, useEffect, useRef, useState } from "react";
import type { StudioDashboardWidgetContribution } from "@elsa-workflows/studio-sdk";

const snapshotCache = new Map<string, { snapshot: unknown; updatedAt: number; scopeKey: string; widgetId: string }>();

export function clearDashboardWidgetCache(scopeKey: string) {
  for (const [key, entry] of snapshotCache)
    if (entry.scopeKey === scopeKey) snapshotCache.delete(key);
}

export function retainDashboardWidgetCache(scopeKey: string, widgetIds: ReadonlySet<string>) {
  for (const [key, entry] of snapshotCache)
    if (entry.scopeKey === scopeKey && !widgetIds.has(entry.widgetId)) snapshotCache.delete(key);
}

export type DashboardWidgetRuntimeState =
  | { status: "idle" }
  | { status: "loading"; startedAt: number }
  | { status: "ready" | "refreshing"; snapshot: unknown; updatedAt: number; nextRefreshAt?: number; staleAt?: number }
  | { status: "error" | "timedOut"; error: string; updatedAt?: number };

export function useDashboardWidget(
  widget: StudioDashboardWidgetContribution,
  settings: unknown,
  refreshIntervalMs: number,
  defaultTimeoutMs: number,
  cacheScopeKey: string,
  active = true
) {
  const [state, setState] = useState<DashboardWidgetRuntimeState>({ status: "idle" });
  const generation = useRef(0);
  const controller = useRef<AbortController | null>(null);
  const cacheKey = `${cacheScopeKey}:${widget.id}:${JSON.stringify(settings)}`;

  const refresh = useCallback(async () => {
    if (!widget.load || !active) return;
    const request = ++generation.current;
    controller.current?.abort();
    const abort = new AbortController();
    controller.current = abort;
    const timeoutMs = Math.min(widget.timeoutMs ?? defaultTimeoutMs, defaultTimeoutMs);
    const timeout = window.setTimeout(() => abort.abort("timeout"), timeoutMs);
    setState(current => current.status === "ready" || current.status === "refreshing"
      ? { ...current, status: "refreshing" }
      : { status: "loading", startedAt: Date.now() });
    try {
      const snapshot = await widget.load({ settings, signal: abort.signal });
      if (request !== generation.current || abort.signal.aborted) return;
      const updatedAt = Date.now();
      if (widget.cacheLifetimeMs) snapshotCache.set(cacheKey, { snapshot, updatedAt, scopeKey: cacheScopeKey, widgetId: widget.id });
      const effective = refreshIntervalMs === 0 ? 0 : Math.max(refreshIntervalMs, widget.minimumRefreshIntervalMs ?? 0);
      setState({
        status: "ready",
        snapshot,
        updatedAt,
        nextRefreshAt: effective ? updatedAt + effective : undefined,
        staleAt: widget.cacheLifetimeMs ? updatedAt + widget.cacheLifetimeMs : undefined
      });
    } catch (error) {
      if (request !== generation.current) return;
      const timedOut = abort.signal.aborted && abort.signal.reason === "timeout";
      setState({ status: timedOut ? "timedOut" : "error", error: timedOut ? "The widget timed out." : message(error) });
    } finally { window.clearTimeout(timeout); }
  }, [active, cacheKey, defaultTimeoutMs, refreshIntervalMs, settings, widget]);

  useEffect(() => {
    if (!widget.load || !active) { controller.current?.abort(); setState({ status: "idle" }); return; }
    const cached = snapshotCache.get(cacheKey);
    if (cached && widget.cacheLifetimeMs && Date.now() - cached.updatedAt < widget.cacheLifetimeMs) {
      const effective = refreshIntervalMs === 0 ? 0 : Math.max(refreshIntervalMs, widget.minimumRefreshIntervalMs ?? 0);
      setState({
        status: "ready",
        snapshot: cached.snapshot,
        updatedAt: cached.updatedAt,
        nextRefreshAt: effective ? cached.updatedAt + effective : undefined,
        staleAt: widget.cacheLifetimeMs ? cached.updatedAt + widget.cacheLifetimeMs : undefined
      });
    } else {
      void refresh();
    }
    return () => { generation.current += 1; controller.current?.abort(); };
  }, [active, cacheKey, refresh, refreshIntervalMs, widget.cacheLifetimeMs, widget.load, widget.minimumRefreshIntervalMs]);

  useEffect(() => {
    if (!widget.load || !active || refreshIntervalMs === 0) return;
    const effective = Math.max(refreshIntervalMs, widget.minimumRefreshIntervalMs ?? 0);
    const timer = window.setInterval(() => void refresh(), effective);
    return () => window.clearInterval(timer);
  }, [active, refresh, refreshIntervalMs, widget.load, widget.minimumRefreshIntervalMs]);

  return { state, refresh, cancel: () => controller.current?.abort() };
}

function message(error: unknown) { return error instanceof Error ? error.message : String(error); }
