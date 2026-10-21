import React, { useEffect, useMemo, useState } from "react";
import type { ElsaStudioModuleApi, StudioRouteComponentProps } from "@elsa-workflows/studio-sdk";
import "./styles.css";

type OTelSignalTab = "overview" | "traces";
type OTelLoadStatus = "idle" | "loading" | "ready" | "empty" | "unavailable" | "unauthorized" | "failed" | "missing";
type SpanStatus = "Unset" | "Ok" | "Error";
type ResourceStatus = "Unknown" | "Active" | "Stale";

interface OTelFilters {
  serviceName: string;
  search: string;
  status: "" | "Ok" | "Error" | "Unset";
  traceId: string;
  take: number;
}

interface OTelResource {
  id: string;
  serviceName: string;
  serviceInstanceId: string | null;
  telemetrySdkLanguage: string | null;
  attributes: Record<string, string | null>;
  lastSeen: string;
  status: ResourceStatus;
}

interface OTelResourceWire {
  id?: string;
  serviceName?: string | null;
  serviceInstanceId?: string | null;
  telemetrySdkLanguage?: string | null;
  attributes?: Record<string, string | null> | null;
  lastSeen?: string | null;
  status?: string | null;
}

interface OTelResourceResultWire {
  items?: OTelResourceWire[];
  droppedCount?: number;
}

interface OTelStorageDiagnostics {
  traceCapacity: number;
  spanCapacity: number;
  metricPointCapacity: number;
  logRecordCapacity: number;
  resourceCount?: number | null;
  traceCount: number;
  spanCount?: number | null;
  metricInstrumentCount: number;
  metricPointCount: number;
  logRecordCount: number;
  droppedTraceCount: number;
  droppedSpanCount: number;
  droppedMetricPointCount: number;
  droppedLogRecordCount: number;
}

interface OTelTraceSummaryWire {
  traceId?: string;
  rootSpanId?: string | null;
  name?: string | null;
  startTime?: string | null;
  endTime?: string | null;
  duration?: string | null;
  resourceIds?: string[] | null;
  workflowInstanceIds?: string[] | null;
  spanCount?: number | null;
  status?: string | null;
}

interface OTelTraceDetailWire {
  trace: OTelTraceSummaryWire;
  spans?: OTelSpanWire[];
  resources?: OTelResourceWire[];
  logs?: unknown[];
}

interface OTelTraceResultWire {
  items?: OTelTraceSummaryWire[];
  droppedCount?: number;
}

interface OTelSpanWire {
  id?: string;
  traceId?: string;
  spanId?: string;
  parentSpanId?: string | null;
  resourceId?: string | null;
  name?: string | null;
  kind?: string | null;
  startTime?: string | null;
  endTime?: string | null;
  status?: string | null;
  statusDescription?: string | null;
  attributes?: Record<string, string | null> | Array<{ name: string; value?: unknown }> | null;
  events?: OTelSpanEventWire[] | null;
  links?: OTelSpanLinkWire[] | null;
}

interface OTelSpanEventWire {
  name?: string | null;
  timestamp?: string | null;
  attributes?: Record<string, string | null> | null;
}

interface OTelSpanLinkWire {
  traceId?: string;
  spanId?: string;
  attributes?: Record<string, string | null> | null;
}

export interface OTelTraceSummary {
  traceId: string;
  rootSpanId: string | null;
  name: string | null;
  startTime: string;
  endTime: string | null;
  duration: string | null;
  durationMs: number;
  spanCount: number;
  resourceIds: string[];
  workflowInstanceIds: string[];
  status: SpanStatus;
}

export interface OTelSpan {
  traceId: string;
  spanId: string;
  parentSpanId: string | null;
  resourceId: string | null;
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
  links: OTelSpanLinkWire[];
  resource: OTelResource | null;
}

interface OTelTraceDetail extends OTelTraceSummary {
  spans: OTelSpan[];
  resources: OTelResource[];
}

interface OTelRead<T> {
  status: OTelLoadStatus;
  data: T | null;
  message: string;
}

interface OTelResourceCollection {
  items: OTelResource[];
  droppedCount: number;
}

interface ClassifiedError {
  status: OTelLoadStatus;
  message: string;
}

const diagnosticsEndpointPrefix = "/diagnostics/opentelemetry";
const resourceSearchTake = 500;
const defaultFilters: OTelFilters = {
  serviceName: "",
  search: "",
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

export function OpenTelemetryPage({ navigate }: { navigate?: StudioRouteComponentProps["navigate"] } = {}) {
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
      </header>

      <aside className="otel-capability-note" aria-label="OpenTelemetry signal availability">
        <p>Trace inspection is supported. Metrics and OpenTelemetry Logs are not available in this experience.</p>
        <a
          href="/diagnostics"
          onClick={event => {
            if (!navigate || event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
            event.preventDefault();
            navigate("/diagnostics");
          }}
        >Browse Diagnostics</a>
      </aside>

      <div className="otel-tabs" role="tablist" aria-label="OpenTelemetry signals">
        {(["overview", "traces"] as OTelSignalTab[]).map(tab => (
          <button type="button" role="tab" aria-selected={state.activeTab === tab} className={state.activeTab === tab ? "active" : ""} onClick={() => state.setActiveTab(tab)} key={tab}>
            {getSignalTabLabel(tab)}
          </button>
        ))}
      </div>

      {state.activeTab === "overview" ? <OverviewPanel state={state} /> : null}
      {state.activeTab === "traces" ? <TracesPanel state={state} selectedTrace={selectedTrace} selectedSpan={selectedSpan} /> : null}
    </section>
  );
}

function useOpenTelemetry() {
  const [activeTab, setActiveTab] = useState<OTelSignalTab>("traces");
  const [filters, setFilters] = useState<OTelFilters>(defaultFilters);
  const [storageRead, setStorageRead] = useState<OTelRead<OTelStorageDiagnostics>>({ status: "loading", data: null, message: "Loading stored data." });
  const [resourcesRead, setResourcesRead] = useState<OTelRead<OTelResourceCollection>>({ status: "loading", data: null, message: "Loading resource metadata." });
  const [traces, setTraces] = useState<OTelTraceSummary[]>([]);
  const [selectedTraceId, setSelectedTraceId] = useState<string | null>(null);
  const [selectedTrace, setSelectedTrace] = useState<OTelTraceDetail | null>(null);
  const [detailStatus, setDetailStatus] = useState<OTelLoadStatus>("idle");
  const [detailMessage, setDetailMessage] = useState("Select a trace to inspect its spans.");
  const [selectedSpanId, setSelectedSpanId] = useState<string | null>(null);
  const [status, setStatus] = useState<OTelLoadStatus>("loading");
  const [message, setMessage] = useState("Loading OpenTelemetry diagnostics.");
  const resourcesById = useMemo(() => new Map((resourcesRead.data?.items ?? []).map(resource => [resource.id, resource])), [resourcesRead.data]);

  useEffect(() => {
    let cancelled = false;
    async function loadStorage() {
      try {
        const response = await moduleApi.backend.http.getJson<OTelStorageDiagnostics>(`${diagnosticsEndpointPrefix}/storage`);
        if (!cancelled) setStorageRead({ status: "ready", data: response, message: "" });
      } catch (error) {
        if (!cancelled) setStorageRead(classifyReadError(error, "Stored data"));
      }
    }
    void loadStorage();
    return () => { cancelled = true; };
  }, []);

  useEffect(() => {
    let cancelled = false;
    async function loadResources() {
      setResourcesRead({ status: "loading", data: null, message: "Loading resource metadata." });
      try {
        const response = await moduleApi.backend.http.postJson<OTelResourceResultWire>(
          `${diagnosticsEndpointPrefix}/resources/search`, { take: resourceSearchTake });
        if (cancelled) return;
        const items = (response.items ?? []).map(normalizeResource);
        const droppedCount = response.droppedCount ?? 0;
        setResourcesRead({
          status: items.length > 0 ? "ready" : "empty",
          data: { items, droppedCount },
          message: items.length > 0 ? "" : "No OpenTelemetry sources reported yet."
        });
      } catch (error) {
        if (!cancelled) setResourcesRead(classifyReadError(error, "Resource metadata"));
      }
    }
    void loadResources();
    return () => { cancelled = true; };
  }, []);

  useEffect(() => {
    let cancelled = false;
    setStatus("loading");
    setMessage("Loading trace summaries.");

    async function loadTraces() {
      try {
        const response = await moduleApi.backend.http.postJson<OTelTraceResultWire>(
          `${diagnosticsEndpointPrefix}/traces/search`, createTraceSearchBody(filters));
        if (cancelled) return;
        const nextTraces = (response.items ?? []).map(normalizeTraceSummary).sort(compareTraceSummaries);
        setTraces(nextTraces);
        setStatus(nextTraces.length > 0 ? "ready" : "empty");
        setMessage(nextTraces.length > 0 ? `${nextTraces.length} trace${nextTraces.length === 1 ? "" : "s"} loaded.` : "No traces match the current filters.");
        setSelectedTraceId(current => nextTraces.some(trace => trace.traceId === current) ? current : nextTraces[0]?.traceId ?? null);
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
    if (!selectedTraceId) {
      setDetailStatus("idle");
      setDetailMessage("Select a trace to inspect its spans.");
      return () => { cancelled = true; };
    }
    setDetailStatus("loading");
    setDetailMessage("Loading trace details.");

    async function loadTrace() {
      try {
        const response = await moduleApi.backend.http.getJson<OTelTraceDetailWire>(`${diagnosticsEndpointPrefix}/traces/${encodeURIComponent(selectedTraceId!)}`);
        if (cancelled) return;
        const detail = normalizeTraceDetail(response);
        setSelectedTrace(detail);
        setDetailStatus("ready");
        setDetailMessage("");
        setSelectedSpanId(detail.spans[0]?.spanId ?? null);
      } catch (error) {
        if (cancelled) return;
        const classified = classifyOpenTelemetryError(error);
        if (classified.status === "unavailable") {
          setDetailStatus("missing");
          setDetailMessage("This trace is no longer available.");
        } else if (classified.status === "unauthorized") {
          setDetailStatus("unauthorized");
          setDetailMessage("Permission denied while loading this trace.");
        } else {
          setDetailStatus("failed");
          setDetailMessage("Trace details failed to load.");
        }
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
    storageRead,
    resourcesRead,
    resourcesById,
    traces,
    selectedTraceId,
    setSelectedTraceId,
    selectedTrace,
    detailStatus,
    detailMessage,
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
  const storageRead = state.storageRead;
  const resourcesRead = state.resourcesRead;
  return (
    <div className="otel-overview">
      <section>
        <h3>Stored data</h3>
        {storageRead.data ? (
          <div className="otel-signal-cards">
            <StoredCountCard label="Resources" count={storageRead.data.resourceCount} />
            <StoredCountCard label="Spans" count={storageRead.data.spanCount} />
          </div>
        ) : <p role="status" data-status={storageRead.status} className="otel-muted">{storageRead.message}</p>}
      </section>
      <section>
        <h3>Sources</h3>
        {resourcesRead.status !== "ready" && <p role="status" data-status={resourcesRead.status} className="otel-muted">{resourcesRead.message}</p>}
        <div className="otel-source-list">
          {(resourcesRead.data?.items ?? []).map(resource => (
            <div className="otel-source-row" data-resource-id={resource.id} key={resource.id}>
              <strong>{resource.serviceName || "Unknown"}</strong>
              {resource.id ? <span>Resource ID: {resource.id}</span> : null}
              <span>Resource status: {resource.status}</span>
              {[resource.serviceInstanceId ? `Instance: ${resource.serviceInstanceId}` : null,
                resource.telemetrySdkLanguage ? `Telemetry SDK language: ${resource.telemetrySdkLanguage}` : null,
                resource.lastSeen ? `Last seen: ${formatDateTime(resource.lastSeen)}` : null].filter(Boolean).map(value => <span key={value}>{value}</span>)}
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
            <span>Start</span><span>Service</span><span>Trace</span><span>Duration</span><span>Spans</span><span>Status</span>
          </div>
          {state.traces.length === 0 ? <div className="otel-empty">{state.message}</div> : null}
          {state.traces.map(trace => {
            const serviceNames = getServiceNames(trace.resourceIds, state.resourcesById);
            return (
              <button type="button" role="row" className={trace.traceId === state.selectedTraceId ? "otel-trace-row selected" : "otel-trace-row"} onClick={() => state.setSelectedTraceId(trace.traceId)} key={trace.traceId}>
                <time title={trace.startTime}>{formatTime(trace.startTime)}</time>
                <span title={serviceNames.join(", ")}>{serviceNames.join(", ")}</span>
                <strong title={trace.name ?? trace.traceId}>{trace.name ?? trace.traceId}{trace.name ? <small>{trace.traceId}</small> : null}</strong>
                <span>{formatDuration(trace.durationMs)}</span>
                <span>{trace.spanCount}</span>
                <StatusChip status={trace.status} />
              </button>
            );
          })}
        </div>
      </section>

      <section className="otel-trace-detail">
        {selectedTrace ? (
          <>
            <TraceDetailHeader trace={selectedTrace} onCopy={state.copyTrace} onDownload={state.downloadTrace} />
            <TraceWaterfall trace={selectedTrace} selectedSpanId={state.selectedSpanId} onSelectSpan={state.setSelectedSpanId} />
            {selectedSpan ? <SpanInspector span={selectedSpan} /> : null}
          </>
        ) : <div className="otel-empty">{state.detailMessage}</div>}
      </section>
    </div>
  );
}

function TraceToolbar({ state }: { state: OTelState }) {
  return (
    <div className="otel-toolbar">
      <input aria-label="Filter by trace ID" placeholder="Trace ID" value={state.filters.traceId} onChange={event => state.setFilters({ ...state.filters, traceId: event.target.value })} />
      <input aria-label="Filter by service" placeholder="Service" value={state.filters.serviceName} onChange={event => state.setFilters({ ...state.filters, serviceName: event.target.value })} />
      <input aria-label="Filter by trace name or ID" placeholder="Trace name or ID" value={state.filters.search} onChange={event => state.setFilters({ ...state.filters, search: event.target.value })} />
      <select aria-label="Filter by trace status" value={state.filters.status} onChange={event => state.setFilters({ ...state.filters, status: event.target.value as OTelFilters["status"] })}>
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
  const resourceById = new Map(trace.resources.map(resource => [resource.id, resource]));
  const serviceNames = getServiceNames(trace.resourceIds, resourceById);
  return (
    <header className="otel-trace-detail-header">
      <div>
        <span>{serviceNames.join(", ")} · {formatDuration(trace.durationMs)} · {trace.spanCount} spans · {trace.status}</span>
        <h3>{trace.name ?? trace.traceId}</h3>
        <p>{trace.traceId}</p>
        <time dateTime={trace.startTime}>{formatDateTime(trace.startTime)}</time>
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

function StoredCountCard({ label, count }: { label: string; count: number | null | undefined }) {
  const displayCount = typeof count === "number" && Number.isFinite(count) && count >= 0 ? count : "Unavailable";
  return <div className="otel-signal-card"><span>Stored data</span><strong>{displayCount}</strong><h4>{label} stored</h4><p>Currently stored by the backend.</p></div>;
}

function StatusChip({ status }: { status: SpanStatus }) {
  return <span className="otel-status-chip" data-status={status.toLowerCase()}>{status}</span>;
}

function DetailRow({ name, value }: { name: string; value?: string | null }) {
  if (!value) return null;
  return <><dt>{name}</dt><dd>{value}</dd></>;
}

function createTraceSearchBody(filters: OTelFilters) {
  return {
    traceId: filters.traceId.trim() || undefined,
    serviceName: filters.serviceName.trim() || undefined,
    status: filters.status || undefined,
    search: filters.search.trim() || undefined,
    take: filters.take
  };
}

export function normalizeTraceSummary(trace: OTelTraceSummaryWire): OTelTraceSummary {
  const startTime = trace.startTime ?? new Date().toISOString();
  const endTime = trace.endTime ?? null;
  return {
    traceId: trace.traceId ?? "",
    rootSpanId: trace.rootSpanId ?? null,
    name: trace.name || null,
    startTime,
    endTime,
    duration: trace.duration ?? null,
    durationMs: normalizeDuration(parseTimeSpanMilliseconds(trace.duration), startTime, endTime),
    spanCount: Number(trace.spanCount ?? 0),
    resourceIds: [...(trace.resourceIds ?? [])],
    workflowInstanceIds: [...(trace.workflowInstanceIds ?? [])],
    status: normalizeSpanStatus(trace.status)
  };
}

export function normalizeTraceDetail(trace: OTelTraceDetailWire): OTelTraceDetail {
  const summary = normalizeTraceSummary(trace.trace);
  const resources = (trace.resources ?? []).map(normalizeResource);
  const resourceById = new Map(resources.map(resource => [resource.id, resource]));
  const spans = (trace.spans ?? []).map(span => normalizeSpan(span, summary.traceId, resourceById)).sort(compareSpans);
  const durationMs = summary.durationMs || calculateTraceDuration(spans);
  return { ...summary, durationMs, spanCount: spans.length || summary.spanCount, spans, resources };
}

export function normalizeSpan(span: OTelSpanWire, fallbackTraceId = "", resourceById: ReadonlyMap<string, OTelResource> = new Map()): OTelSpan {
  const startTime = span.startTime ?? new Date().toISOString();
  const endTime = span.endTime ?? null;
  const resourceId = span.resourceId ?? null;
  const resource = resourceId ? resourceById.get(resourceId) ?? null : null;
  return {
    traceId: span.traceId ?? fallbackTraceId,
    spanId: span.spanId ?? `${Date.now()}-${Math.random()}`,
    parentSpanId: span.parentSpanId ?? null,
    resourceId,
    name: span.name || "Unnamed span",
    kind: span.kind || "Internal",
    serviceName: resource?.serviceName || "Unknown",
    startTime,
    endTime,
    durationMs: normalizeDuration(null, startTime, endTime),
    status: normalizeSpanStatus(span.status),
    statusDescription: span.statusDescription ?? null,
    attributes: normalizeAttributes(span.attributes),
    events: span.events ?? [],
    links: span.links ?? [],
    resource
  };
}

function normalizeResource(resource: OTelResourceWire): OTelResource {
  return {
    id: resource.id ?? "",
    serviceName: resource.serviceName?.trim() ?? "",
    serviceInstanceId: resource.serviceInstanceId ?? null,
    telemetrySdkLanguage: resource.telemetrySdkLanguage ?? null,
    attributes: resource.attributes ?? {},
    lastSeen: resource.lastSeen ?? "",
    status: normalizeResourceStatus(resource.status)
  };
}

function normalizeResourceStatus(status: string | null | undefined): ResourceStatus {
  const normalized = String(status ?? "Unknown").toLowerCase();
  if (normalized === "active") return "Active";
  if (normalized === "stale") return "Stale";
  return "Unknown";
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

function classifyReadError<T>(error: unknown, source: string): OTelRead<T> {
  const classified = classifyOpenTelemetryError(error);
  const message = classified.status === "unauthorized"
    ? `Permission denied while loading ${source.toLowerCase()}.`
    : classified.status === "unavailable"
      ? `${source} unavailable.`
      : `${source} failed to load.`;
  return { status: classified.status, data: null, message };
}

function normalizeSpanStatus(status: string | null | undefined): SpanStatus {
  const normalized = String(status ?? "Unset").toLowerCase();
  if (normalized === "ok") return "Ok";
  if (normalized === "error") return "Error";
  return "Unset";
}

function parseTimeSpanMilliseconds(duration: string | null | undefined): number | null {
  if (!duration) return null;
  const match = /^(-)?(?:(\d+)\.)?(\d{1,2}):([0-5]\d):([0-5]\d)(?:\.(\d{1,7}))?$/.exec(duration);
  if (!match) return null;
  const milliseconds = (((Number(match[2] ?? 0) * 24 + Number(match[3])) * 60 + Number(match[4])) * 60 + Number(match[5])) * 1000
    + Number(`0.${match[6] ?? "0"}`) * 1000;
  return Math.max(0, (match[1] ? -1 : 1) * milliseconds);
}

function normalizeAttributes(attributes: OTelSpanWire["attributes"]): Array<{ name: string; value: string }> {
  if (!attributes) return [];
  if (Array.isArray(attributes)) return attributes.map(item => ({ name: item.name, value: formatValue(item.value) }));
  return Object.entries(attributes).map(([name, value]) => ({ name, value: formatValue(value) }));
}

function compareTraceSummaries(left: OTelTraceSummary, right: OTelTraceSummary) {
  return Date.parse(right.startTime) - Date.parse(left.startTime);
}

function getServiceNames(resourceIds: string[], resources: ReadonlyMap<string, OTelResource>) {
  const services = resourceIds.map(id => resources.get(id)?.serviceName.trim());
  if (!services.some(Boolean)) {
    const count = new Set(resourceIds).size;
    return [`Unknown (${count} resource${count === 1 ? "" : "s"})`];
  }
  return [...new Set(services.map(service => service || "Unknown"))];
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

function formatValue(value: unknown) {
  if (value == null) return "";
  return typeof value === "object" ? JSON.stringify(value) : String(value);
}

function getSignalTabLabel(tab: OTelSignalTab) {
  return tab === "overview" ? "Overview" : "Traces";
}
