import React, { useCallback, useEffect, useMemo, useState } from "react";
import type { ElsaStudioModuleApi, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import "./styles.css";

type OTelSignalTab = "overview" | "traces" | "metrics" | "logs";
type OTelLoadStatus = "loading" | "ready" | "unavailable" | "unauthorized" | "failed";
type SpanStatus = "Unset" | "Ok" | "Error";

interface OTelFilters {
  serviceName: string;
  operation: string;
  status: "" | "Ok" | "Error" | "Unset";
  traceId: string;
  take: number;
}

interface OTelSource {
  id: string;
  displayName?: string | null;
  serviceName?: string | null;
  environment?: string | null;
  instanceId?: string | null;
  lastSeen?: string | null;
}

interface OTelOverviewResponse {
  sources?: OTelSource[];
  traceCount?: number;
  spanCount?: number;
  metricCount?: number;
  logCount?: number;
  lastSeen?: string | null;
}

interface OTelTraceSummaryWire {
  traceId?: string;
  rootSpanId?: string | null;
  name?: string | null;
  serviceName?: string | null;
  startTime?: string | null;
  endTime?: string | null;
  durationMs?: number | null;
  spanCount?: number | null;
  errorCount?: number | null;
  status?: string | null;
}

interface OTelTraceDetailWire extends OTelTraceSummaryWire {
  spans?: OTelSpanWire[];
}

interface OTelSpanWire {
  traceId?: string;
  spanId?: string;
  parentSpanId?: string | null;
  name?: string | null;
  kind?: string | null;
  serviceName?: string | null;
  startTime?: string | null;
  endTime?: string | null;
  durationMs?: number | null;
  status?: string | null;
  statusDescription?: string | null;
  attributes?: Record<string, unknown> | Array<{ name: string; value?: unknown }> | null;
  events?: OTelSpanEventWire[] | null;
  links?: unknown[] | null;
  resource?: Record<string, unknown> | null;
}

interface OTelSpanEventWire {
  name?: string | null;
  timestamp?: string | null;
  attributes?: Record<string, unknown> | null;
}

export interface OTelTraceSummary {
  traceId: string;
  rootSpanId: string | null;
  name: string;
  serviceName: string;
  startTime: string;
  endTime: string | null;
  durationMs: number;
  spanCount: number;
  errorCount: number;
  status: SpanStatus;
}

export interface OTelSpan {
  traceId: string;
  spanId: string;
  parentSpanId: string | null;
  name: string;
  kind: string;
  serviceName: string;
  startTime: string;
  endTime: string | null;
  durationMs: number;
  status: SpanStatus;
  statusDescription: string | null;
  attributes: Array<{ name: string; value: string }>;
  events: OTelSpanEventWire[];
  links: unknown[];
  resource: Record<string, unknown> | null;
}

interface OTelTraceDetail extends OTelTraceSummary {
  spans: OTelSpan[];
}

interface ClassifiedError {
  status: OTelLoadStatus;
  message: string;
}

const otelEndpointPrefix = "/_elsa/studio/diagnostics/otel";
const defaultFilters: OTelFilters = {
  serviceName: "",
  operation: "",
  status: "",
  traceId: "",
  take: 100
};

let moduleApi: ElsaStudioModuleApi;

export function register(api: ElsaStudioModuleApi) {
  moduleApi = api;

  api.navigation.add({
    id: "open-telemetry",
    label: "OpenTelemetry",
    path: "/diagnostics/open-telemetry",
    activePathPrefix: "/diagnostics/open-telemetry",
    order: 880,
    iconColor: "#22c55e",
    parentId: "diagnostics"
  });

  api.routes.add({
    id: "open-telemetry",
    label: "OpenTelemetry",
    path: "/diagnostics/open-telemetry",
    component: OpenTelemetryPage
  });

  api.panels.add({
    id: "open-telemetry",
    title: "OpenTelemetry",
    order: 1_020,
    component: OpenTelemetryPanel
  });
}

export function OpenTelemetryPanel() {
  const state = useOpenTelemetry();
  const selectedTrace = state.selectedTrace;
  const selectedSpan = selectedTrace?.spans.find(span => span.spanId === state.selectedSpanId) ?? selectedTrace?.spans[0] ?? null;

  return (
    <section className="otel-panel">
      <TracesPanel state={state} selectedTrace={selectedTrace} selectedSpan={selectedSpan} />
    </section>
  );
}

export function OpenTelemetryPage() {
  const state = useOpenTelemetry();
  const selectedTrace = state.selectedTrace;
  const selectedSpan = selectedTrace?.spans.find(span => span.spanId === state.selectedSpanId) ?? selectedTrace?.spans[0] ?? null;

  return (
    <section className="otel-page">
      <header className="otel-header">
        <div>
          <h2>OpenTelemetry</h2>
          <p>{state.message}</p>
        </div>
        <div className="otel-header-summary">
          <SignalCount label="Traces" value={state.overview?.traceCount} />
          <SignalCount label="Spans" value={state.overview?.spanCount} />
          <SignalCount label="Metrics" value={state.overview?.metricCount} />
          <SignalCount label="Logs" value={state.overview?.logCount} />
        </div>
      </header>

      <div className="otel-tabs" role="tablist" aria-label="OpenTelemetry signals">
        {(["overview", "traces", "metrics", "logs"] as OTelSignalTab[]).map(tab => (
          <button type="button" role="tab" aria-selected={state.activeTab === tab} className={state.activeTab === tab ? "active" : ""} onClick={() => state.setActiveTab(tab)} key={tab}>
            {getSignalTabLabel(tab)}
          </button>
        ))}
      </div>

      {state.activeTab === "overview" ? <OverviewPanel state={state} /> : null}
      {state.activeTab === "traces" ? <TracesPanel state={state} selectedTrace={selectedTrace} selectedSpan={selectedSpan} /> : null}
      {state.activeTab === "metrics" ? <SignalPlaceholder title="Metrics" status={state.status} message="Metric charts will appear here when the OpenTelemetry backend exposes metric instruments and datapoints." /> : null}
      {state.activeTab === "logs" ? <SignalPlaceholder title="Logs" status={state.status} message="OpenTelemetry log records will appear here when the backend exposes the log signal. Structured application logs are available under Diagnostics / Structured logs." /> : null}
    </section>
  );
}

function useOpenTelemetry() {
  const [activeTab, setActiveTab] = useState<OTelSignalTab>("traces");
  const [filters, setFilters] = useState<OTelFilters>(defaultFilters);
  const [overview, setOverview] = useState<OTelOverviewResponse | null>(null);
  const [traces, setTraces] = useState<OTelTraceSummary[]>([]);
  const [selectedTraceId, setSelectedTraceId] = useState<string | null>(null);
  const [selectedTrace, setSelectedTrace] = useState<OTelTraceDetail | null>(null);
  const [selectedSpanId, setSelectedSpanId] = useState<string | null>(null);
  const [status, setStatus] = useState<OTelLoadStatus>("loading");
  const [message, setMessage] = useState("Loading OpenTelemetry diagnostics.");

  useEffect(() => {
    let cancelled = false;
    async function loadOverview() {
      try {
        const response = await moduleApi.backend.http.getJson<OTelOverviewResponse>(`${otelEndpointPrefix}/overview`);
        if (!cancelled) setOverview(response);
      } catch {
        if (!cancelled) setOverview(null);
      }
    }
    void loadOverview();
    return () => { cancelled = true; };
  }, []);

  useEffect(() => {
    let cancelled = false;
    setStatus("loading");
    setMessage("Loading trace summaries.");

    async function loadTraces() {
      try {
        const response = await moduleApi.backend.http.getJson<OTelTraceSummaryWire[]>(createTracesPath(filters));
        if (cancelled) return;
        const nextTraces = response.map(normalizeTraceSummary).sort(compareTraceSummaries);
        setTraces(nextTraces);
        setStatus("ready");
        setMessage(`${nextTraces.length} trace${nextTraces.length === 1 ? "" : "s"} loaded.`);
        setSelectedTraceId(current => current ?? nextTraces[0]?.traceId ?? null);
      } catch (error) {
        if (cancelled) return;
        const classified = classifyOpenTelemetryError(error);
        setStatus(classified.status);
        setMessage(classified.message);
        setTraces([]);
        setSelectedTraceId(null);
      }
    }

    void loadTraces();
    return () => { cancelled = true; };
  }, [filters]);

  useEffect(() => {
    let cancelled = false;
    setSelectedTrace(null);
    setSelectedSpanId(null);
    if (!selectedTraceId) return () => { cancelled = true; };

    async function loadTrace() {
      try {
        const response = await moduleApi.backend.http.getJson<OTelTraceDetailWire>(`${otelEndpointPrefix}/traces/${encodeURIComponent(selectedTraceId!)}`);
        if (cancelled) return;
        const detail = normalizeTraceDetail(response);
        setSelectedTrace(detail);
        setSelectedSpanId(detail.spans[0]?.spanId ?? null);
      } catch (error) {
        if (cancelled) return;
        const classified = classifyOpenTelemetryError(error);
        setStatus(classified.status);
        setMessage(classified.message);
      }
    }

    void loadTrace();
    return () => { cancelled = true; };
  }, [selectedTraceId]);

  function resetFilters() {
    setFilters(defaultFilters);
  }

  async function copyTrace() {
    if (!selectedTrace || !navigator.clipboard) return;
    await navigator.clipboard.writeText(JSON.stringify(selectedTrace, null, 2));
  }

  function downloadTrace() {
    if (!selectedTrace || typeof document === "undefined") return;
    const blob = new Blob([JSON.stringify(selectedTrace, null, 2)], { type: "application/json;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `trace-${selectedTrace.traceId}.json`;
    link.rel = "noopener";
    document.body.appendChild(link);
    link.click();
    link.remove();
    window.setTimeout(() => URL.revokeObjectURL(url), 0);
  }

  return {
    activeTab,
    setActiveTab,
    filters,
    setFilters,
    resetFilters,
    overview,
    traces,
    selectedTraceId,
    setSelectedTraceId,
    selectedTrace,
    selectedSpanId,
    setSelectedSpanId,
    status,
    message,
    copyTrace,
    downloadTrace
  };
}

type OTelState = ReturnType<typeof useOpenTelemetry>;

function OverviewPanel({ state }: { state: OTelState }) {
  const sources = state.overview?.sources ?? [];
  return (
    <div className="otel-overview">
      <section>
        <h3>Signal Health</h3>
        <div className="otel-signal-cards">
          <SignalCard title="Traces" detail="Distributed request and workflow execution spans." status={state.status} count={state.overview?.traceCount} />
          <SignalCard title="Metrics" detail="Measurements, counters, gauges, and histograms." status={state.overview?.metricCount ? "ready" : "unavailable"} count={state.overview?.metricCount} />
          <SignalCard title="Logs" detail="OpenTelemetry log records correlated by trace/span IDs." status={state.overview?.logCount ? "ready" : "unavailable"} count={state.overview?.logCount} />
        </div>
      </section>
      <section>
        <h3>Sources</h3>
        {sources.length === 0 ? <p className="otel-muted">No OpenTelemetry sources reported yet.</p> : null}
        <div className="otel-source-list">
          {sources.map(source => (
            <div className="otel-source-row" key={source.id}>
              <strong>{formatSourceLabel(source)}</strong>
              <span>{[source.environment, source.instanceId, source.lastSeen ? `last seen ${formatDateTime(source.lastSeen)}` : null].filter(Boolean).join(" · ")}</span>
            </div>
          ))}
        </div>
      </section>
    </div>
  );
}

function TracesPanel({ state, selectedTrace, selectedSpan }: { state: OTelState; selectedTrace: OTelTraceDetail | null; selectedSpan: OTelSpan | null }) {
  return (
    <div className="otel-traces-layout">
      <section className="otel-trace-list">
        <TraceToolbar state={state} />
        <div className="otel-trace-table" role="table" aria-label="Trace summaries">
          <div className="otel-trace-row header" role="row">
            <span>Start</span><span>Service</span><span>Operation</span><span>Duration</span><span>Spans</span><span>Status</span>
          </div>
          {state.traces.length === 0 ? <div className="otel-empty">{state.status === "ready" ? "No traces match the current filters." : state.message}</div> : null}
          {state.traces.map(trace => (
            <button type="button" role="row" className={trace.traceId === state.selectedTraceId ? "otel-trace-row selected" : "otel-trace-row"} onClick={() => state.setSelectedTraceId(trace.traceId)} key={trace.traceId}>
              <time title={trace.startTime}>{formatTime(trace.startTime)}</time>
              <span title={trace.serviceName}>{trace.serviceName}</span>
              <strong title={trace.name}>{trace.name}</strong>
              <span>{formatDuration(trace.durationMs)}</span>
              <span>{trace.spanCount}</span>
              <StatusChip status={trace.status} errors={trace.errorCount} />
            </button>
          ))}
        </div>
      </section>

      <section className="otel-trace-detail">
        {selectedTrace ? (
          <>
            <TraceDetailHeader trace={selectedTrace} onCopy={state.copyTrace} onDownload={state.downloadTrace} />
            <TraceWaterfall trace={selectedTrace} selectedSpanId={state.selectedSpanId} onSelectSpan={state.setSelectedSpanId} />
            {selectedSpan ? <SpanInspector span={selectedSpan} /> : null}
          </>
        ) : <div className="otel-empty">Select a trace to inspect its spans.</div>}
      </section>
    </div>
  );
}

function TraceToolbar({ state }: { state: OTelState }) {
  return (
    <div className="otel-toolbar">
      <input aria-label="Filter by trace ID" placeholder="Trace ID" value={state.filters.traceId} onChange={event => state.setFilters({ ...state.filters, traceId: event.target.value })} />
      <input aria-label="Filter by service" placeholder="Service" value={state.filters.serviceName} onChange={event => state.setFilters({ ...state.filters, serviceName: event.target.value })} />
      <input aria-label="Filter by operation" placeholder="Operation" value={state.filters.operation} onChange={event => state.setFilters({ ...state.filters, operation: event.target.value })} />
      <select aria-label="Filter by span status" value={state.filters.status} onChange={event => state.setFilters({ ...state.filters, status: event.target.value as OTelFilters["status"] })}>
        <option value="">All statuses</option>
        <option value="Ok">Ok</option>
        <option value="Error">Error</option>
        <option value="Unset">Unset</option>
      </select>
      <select aria-label="Trace result limit" value={state.filters.take} onChange={event => state.setFilters({ ...state.filters, take: Number(event.target.value) })}>
        {[50, 100, 250, 500].map(take => <option value={take} key={take}>{take}</option>)}
      </select>
      <button type="button" onClick={state.resetFilters}>Reset</button>
    </div>
  );
}

function TraceDetailHeader({ trace, onCopy, onDownload }: { trace: OTelTraceDetail; onCopy(): Promise<void>; onDownload(): void }) {
  return (
    <header className="otel-trace-detail-header">
      <div>
        <span>{trace.serviceName} · {formatDuration(trace.durationMs)} · {trace.spanCount} spans</span>
        <h3>{trace.name}</h3>
        <p>{trace.traceId}</p>
      </div>
      <div>
        <button type="button" onClick={() => void onCopy()}>Copy JSON</button>
        <button type="button" onClick={onDownload}>Download</button>
      </div>
    </header>
  );
}

function TraceWaterfall({ trace, selectedSpanId, onSelectSpan }: { trace: OTelTraceDetail; selectedSpanId: string | null; onSelectSpan(spanId: string): void }) {
  const rows = useMemo(() => buildWaterfallRows(trace.spans), [trace.spans]);
  const start = Math.min(...trace.spans.map(span => Date.parse(span.startTime)));
  const duration = Math.max(1, trace.durationMs);

  return (
    <div className="otel-waterfall" aria-label="Trace waterfall">
      <div className="otel-waterfall-scale"><span>0ms</span><span>{formatDuration(duration)}</span></div>
      {rows.map(row => {
        const offsetMs = Math.max(0, Date.parse(row.span.startTime) - start);
        const left = Math.max(0, Math.min(96, (offsetMs / duration) * 100));
        const width = Math.max(1, Math.min(100 - left, (row.span.durationMs / duration) * 100));
        return (
          <button type="button" className={row.span.spanId === selectedSpanId ? "otel-waterfall-row selected" : "otel-waterfall-row"} onClick={() => onSelectSpan(row.span.spanId)} key={row.span.spanId}>
            <span className="otel-waterfall-label" style={{ paddingLeft: `${row.depth * 14}px` }} title={row.span.name}>{row.span.name}</span>
            <span className="otel-waterfall-track"><span className="otel-waterfall-bar" data-status={row.span.status.toLowerCase()} style={{ left: `${left}%`, width: `${width}%` }} /></span>
            <span>{formatDuration(row.span.durationMs)}</span>
          </button>
        );
      })}
    </div>
  );
}

function SpanInspector({ span }: { span: OTelSpan }) {
  return (
    <aside className="otel-span-inspector" aria-label="Span details">
      <header>
        <span>{span.kind} · {span.status}</span>
        <h3>{span.name}</h3>
      </header>
      <dl>
        <DetailRow name="Span ID" value={span.spanId} />
        <DetailRow name="Parent" value={span.parentSpanId} />
        <DetailRow name="Service" value={span.serviceName} />
        <DetailRow name="Started" value={formatDateTime(span.startTime)} />
        <DetailRow name="Duration" value={formatDuration(span.durationMs)} />
        <DetailRow name="Status description" value={span.statusDescription} />
      </dl>
      {span.attributes.length > 0 ? <section><h4>Attributes</h4><dl>{span.attributes.map(attribute => <DetailRow name={attribute.name} value={attribute.value} key={attribute.name} />)}</dl></section> : null}
      {span.events.length > 0 ? <section><h4>Events</h4>{span.events.map((event, index) => <pre key={index}>{JSON.stringify(event, null, 2)}</pre>)}</section> : null}
      {span.resource ? <section><h4>Resource</h4><pre>{JSON.stringify(span.resource, null, 2)}</pre></section> : null}
    </aside>
  );
}

function SignalPlaceholder({ title, status, message }: { title: string; status: OTelLoadStatus; message: string }) {
  return <div className="otel-placeholder"><h3>{title}</h3><p>{status === "unavailable" ? "OpenTelemetry diagnostics endpoint is unavailable." : message}</p></div>;
}

function SignalCount({ label, value }: { label: string; value?: number }) {
  return <span><strong>{value ?? "-"}</strong>{label}</span>;
}

function SignalCard({ title, detail, status, count }: { title: string; detail: string; status: OTelLoadStatus; count?: number }) {
  return <div className="otel-signal-card" data-status={status}><span>{status}</span><strong>{count ?? "-"}</strong><h4>{title}</h4><p>{detail}</p></div>;
}

function StatusChip({ status, errors = 0 }: { status: SpanStatus; errors?: number }) {
  return <span className="otel-status-chip" data-status={status.toLowerCase()}>{errors > 0 ? `${errors} errors` : status}</span>;
}

function DetailRow({ name, value }: { name: string; value?: string | null }) {
  if (!value) return null;
  return <><dt>{name}</dt><dd>{value}</dd></>;
}

export function createTracesPath(filters: OTelFilters) {
  const query = new URLSearchParams();
  if (filters.traceId.trim()) query.set("traceId", filters.traceId.trim());
  if (filters.serviceName.trim()) query.set("service", filters.serviceName.trim());
  if (filters.operation.trim()) query.set("operation", filters.operation.trim());
  if (filters.status) query.set("status", filters.status);
  query.set("take", String(filters.take));
  return `${otelEndpointPrefix}/traces?${query}`;
}

export function normalizeTraceSummary(trace: OTelTraceSummaryWire): OTelTraceSummary {
  const startTime = trace.startTime ?? new Date().toISOString();
  const endTime = trace.endTime ?? null;
  return {
    traceId: trace.traceId ?? "",
    rootSpanId: trace.rootSpanId ?? null,
    name: trace.name || "Unknown operation",
    serviceName: trace.serviceName || "unknown-service",
    startTime,
    endTime,
    durationMs: normalizeDuration(trace.durationMs, startTime, endTime),
    spanCount: Number(trace.spanCount ?? 0),
    errorCount: Number(trace.errorCount ?? (normalizeSpanStatus(trace.status) === "Error" ? 1 : 0)),
    status: normalizeSpanStatus(trace.status)
  };
}

export function normalizeTraceDetail(trace: OTelTraceDetailWire): OTelTraceDetail {
  const summary = normalizeTraceSummary(trace);
  const spans = (trace.spans ?? []).map(span => normalizeSpan(span, summary.traceId)).sort(compareSpans);
  const durationMs = summary.durationMs || calculateTraceDuration(spans);
  return { ...summary, durationMs, spanCount: spans.length || summary.spanCount, errorCount: spans.filter(span => span.status === "Error").length || summary.errorCount, spans };
}

export function normalizeSpan(span: OTelSpanWire, fallbackTraceId = ""): OTelSpan {
  const startTime = span.startTime ?? new Date().toISOString();
  const endTime = span.endTime ?? null;
  return {
    traceId: span.traceId ?? fallbackTraceId,
    spanId: span.spanId ?? `${Date.now()}-${Math.random()}`,
    parentSpanId: span.parentSpanId ?? null,
    name: span.name || "Unnamed span",
    kind: span.kind || "Internal",
    serviceName: span.serviceName || "unknown-service",
    startTime,
    endTime,
    durationMs: normalizeDuration(span.durationMs, startTime, endTime),
    status: normalizeSpanStatus(span.status),
    statusDescription: span.statusDescription ?? null,
    attributes: normalizeAttributes(span.attributes),
    events: span.events ?? [],
    links: span.links ?? [],
    resource: span.resource ?? null
  };
}

export function buildWaterfallRows(spans: OTelSpan[]) {
  const byParent = new Map<string | null, OTelSpan[]>();
  for (const span of spans) {
    byParent.set(span.parentSpanId, [...(byParent.get(span.parentSpanId) ?? []), span]);
  }
  for (const children of byParent.values()) children.sort(compareSpans);

  const rows: Array<{ span: OTelSpan; depth: number }> = [];
  const visited = new Set<string>();
  const roots = byParent.get(null) ?? spans.filter(span => !spans.some(candidate => candidate.spanId === span.parentSpanId));

  function visit(span: OTelSpan, depth: number) {
    if (visited.has(span.spanId)) return;
    visited.add(span.spanId);
    rows.push({ span, depth });
    for (const child of byParent.get(span.spanId) ?? []) visit(child, depth + 1);
  }

  for (const root of roots) visit(root, 0);
  for (const span of spans) visit(span, 0);
  return rows;
}

export function classifyOpenTelemetryError(error: unknown): ClassifiedError {
  const message = error instanceof Error ? error.message : String(error);
  if (/\b(401|403)\b/.test(message)) return { status: "unauthorized", message: "Permission denied while loading OpenTelemetry diagnostics." };
  if (/\b404\b/.test(message)) return { status: "unavailable", message: "OpenTelemetry diagnostics are unavailable. Enable the backend OpenTelemetry diagnostics module." };
  return { status: "failed", message: `OpenTelemetry diagnostics failed: ${message}` };
}

function normalizeSpanStatus(status: string | null | undefined): SpanStatus {
  const normalized = String(status ?? "Unset").toLowerCase();
  if (normalized === "ok") return "Ok";
  if (normalized === "error") return "Error";
  return "Unset";
}

function normalizeAttributes(attributes: OTelSpanWire["attributes"]): Array<{ name: string; value: string }> {
  if (!attributes) return [];
  if (Array.isArray(attributes)) return attributes.map(item => ({ name: item.name, value: formatValue(item.value) }));
  return Object.entries(attributes).map(([name, value]) => ({ name, value: formatValue(value) }));
}

function compareTraceSummaries(left: OTelTraceSummary, right: OTelTraceSummary) {
  return Date.parse(right.startTime) - Date.parse(left.startTime);
}

function compareSpans(left: OTelSpan, right: OTelSpan) {
  return Date.parse(left.startTime) - Date.parse(right.startTime);
}

function calculateTraceDuration(spans: OTelSpan[]) {
  if (spans.length === 0) return 0;
  const starts = spans.map(span => Date.parse(span.startTime));
  const ends = spans.map(span => span.endTime ? Date.parse(span.endTime) : Date.parse(span.startTime) + span.durationMs);
  return Math.max(0, Math.max(...ends) - Math.min(...starts));
}

function normalizeDuration(durationMs: number | null | undefined, startTime: string, endTime: string | null) {
  if (typeof durationMs === "number" && Number.isFinite(durationMs)) return Math.max(0, durationMs);
  if (!endTime) return 0;
  return Math.max(0, Date.parse(endTime) - Date.parse(startTime));
}

function formatDuration(durationMs: number) {
  if (durationMs >= 1000) return `${(durationMs / 1000).toFixed(durationMs >= 10_000 ? 1 : 2)}s`;
  return `${Math.round(durationMs * 10) / 10}ms`;
}

function formatTime(timestamp: string) {
  return new Date(timestamp).toLocaleTimeString();
}

function formatDateTime(timestamp: string) {
  return new Date(timestamp).toLocaleString();
}

function formatSourceLabel(source: OTelSource) {
  return source.displayName || source.serviceName || source.id;
}

function formatValue(value: unknown) {
  if (value == null) return "";
  return typeof value === "object" ? JSON.stringify(value) : String(value);
}

function getSignalTabLabel(tab: OTelSignalTab) {
  return tab === "overview" ? "Overview" : tab === "traces" ? "Traces" : tab === "metrics" ? "Metrics" : "Logs";
}
