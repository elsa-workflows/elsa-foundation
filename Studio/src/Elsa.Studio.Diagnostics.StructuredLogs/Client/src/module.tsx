import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { ElsaStudioModuleApi, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import "./styles.css";

type StructuredLogLevel = "Trace" | "Debug" | "Information" | "Warning" | "Error" | "Critical";
type ConnectionStatus = "connecting" | "live" | "paused" | "disconnected" | "unavailable" | "unauthorized" | "failed";

interface StructuredLogProperty {
  name: string;
  value?: string | number | boolean | null;
}

interface StructuredLogScope {
  items?: StructuredLogProperty[];
  message?: string | null;
}

interface StructuredLogException {
  type?: string | null;
  message?: string | null;
  stackTrace?: string | null;
  inner?: StructuredLogException | null;
}

interface StructuredLogSource {
  id: string;
  displayName?: string | null;
  serviceName?: string | null;
  machineName?: string | null;
  processId?: number | null;
}

interface StructuredLogWireEntry {
  sequence?: number;
  timestamp?: string;
  level?: string | number;
  category?: string;
  eventId?: number | null;
  eventName?: string | null;
  message?: string;
  messageTemplate?: string | null;
  properties?: StructuredLogProperty[] | Record<string, unknown> | null;
  scopes?: StructuredLogScope[] | Record<string, unknown> | null;
  exception?: StructuredLogException | null;
  sourceId?: string | null;
}

interface DroppedEntriesSignal {
  droppedCount?: number;
  count?: number;
  since?: string;
}

interface StructuredLogFilters {
  minLevel: StructuredLogLevel | "";
  sourceId: string;
  category: string;
}

export interface StructuredLogEntry {
  id: string;
  sequence: number;
  timestamp: string;
  level: StructuredLogLevel;
  category: string;
  eventId: number | null;
  eventName: string | null;
  message: string;
  messageTemplate: string | null;
  properties: StructuredLogProperty[];
  scopes: StructuredLogScope[];
  exception: StructuredLogException | null;
  sourceId: string | null;
  raw: StructuredLogWireEntry;
}

interface AppendStructuredLogResult {
  rows: StructuredLogEntry[];
  discarded: number;
}

const structuredLogsEndpointPrefix = "/_elsa/studio/diagnostics/structured-logs";
const structuredLogsRecentLimit = 2_000;
const maxStructuredLogRows = 2_000;
const defaultFilters: StructuredLogFilters = {
  minLevel: "Information",
  sourceId: "",
  category: ""
};
const autoScrollStorageKey = "elsa-studio-structured-logs-autoscroll";
const wrapStorageKey = "elsa-studio-structured-logs-wrap";
const compactStorageKey = "elsa-studio-structured-logs-compact";
const levelOrder: StructuredLogLevel[] = ["Trace", "Debug", "Information", "Warning", "Error", "Critical"];

let moduleApi: ElsaStudioModuleApi;

export function register(api: ElsaStudioModuleApi) {
  moduleApi = api;

  api.navigation.add({
    id: "structured-logs",
    label: "Structured logs",
    path: "/diagnostics/structured-logs",
    order: 860,
    iconColor: "#10b981",
    parentId: "diagnostics"
  });

  api.routes.add({
    id: "structured-logs",
    label: "Structured logs",
    path: "/diagnostics/structured-logs",
    component: StructuredLogsPage
  });

  api.panels.add({
    id: "structured-logs",
    title: "Structured Logs",
    order: 1_010,
    component: StructuredLogsPanel
  });
}

export function StructuredLogsPanel() {
  const state = useStructuredLogs({ includeInspector: false });

  return (
    <section className="structured-logs-panel">
      <StructuredLogsHeader state={state} compactControls />
      <StructuredLogRows state={state} panelMode />
    </section>
  );
}

export function StructuredLogsPage() {
  const state = useStructuredLogs({ includeInspector: true });
  const selectedEntry = state.rows.find(row => row.id === state.selectedEntryId) ?? null;

  return (
    <section className="structured-logs-page">
      <StructuredLogsHeader state={state} />
      <div className={selectedEntry ? "structured-logs-workbench" : "structured-logs-workbench no-selection"}>
        <StructuredLogRows state={state} />
        {selectedEntry ? <StructuredLogInspector entry={selectedEntry} sources={state.sources} onClose={() => state.setSelectedEntryId(null)} /> : null}
      </div>
    </section>
  );
}

function useStructuredLogs({ includeInspector }: { includeInspector: boolean }) {
  const [filters, setFilters] = useState<StructuredLogFilters>(defaultFilters);
  const [sources, setSources] = useState<StructuredLogSource[]>([]);
  const [rows, setRows] = useState<StructuredLogEntry[]>([]);
  const [selectedEntryId, setSelectedEntryId] = useState<string | null>(null);
  const [status, setStatus] = useState<ConnectionStatus>("connecting");
  const [detail, setDetail] = useState("Connecting to structured logs.");
  const [paused, setPaused] = useState(false);
  const [bufferedRows, setBufferedRows] = useState<StructuredLogEntry[]>([]);
  const [queuedCount, setQueuedCount] = useState(0);
  const [upstreamDroppedCount, setUpstreamDroppedCount] = useState(0);
  const [localDiscardedCount, setLocalDiscardedCount] = useState(0);
  const [autoScroll, setAutoScroll] = useState(getInitialBoolean(autoScrollStorageKey, true));
  const [wrapMessages, setWrapMessages] = useState(getInitialBoolean(wrapStorageKey, true));
  const [compact, setCompact] = useState(getInitialBoolean(compactStorageKey, !includeInspector));
  const viewportRef = useRef<HTMLDivElement>(null);
  const pausedRef = useRef(paused);

  useEffect(() => {
    pausedRef.current = paused;
  }, [paused]);

  const addEntries = useCallback((entries: StructuredLogEntry[]) => {
    if (entries.length === 0)
      return;

    if (pausedRef.current) {
      setBufferedRows(current => appendStructuredLogEntries(current, entries).rows);
      setQueuedCount(current => current + entries.length);
      return;
    }

    setRows(current => {
      const next = appendStructuredLogEntries(current, entries);
      if (next.discarded > 0) {
        setLocalDiscardedCount(count => count + next.discarded);
      }
      return next.rows;
    });
  }, []);

  useEffect(() => {
    setStoredBoolean(autoScrollStorageKey, autoScroll);
  }, [autoScroll]);

  useEffect(() => {
    setStoredBoolean(wrapStorageKey, wrapMessages);
  }, [wrapMessages]);

  useEffect(() => {
    setStoredBoolean(compactStorageKey, compact);
  }, [compact]);

  useEffect(() => {
    if (!autoScroll || paused)
      return;

    const viewport = viewportRef.current;
    if (viewport) {
      viewport.scrollTop = viewport.scrollHeight;
    }
  }, [autoScroll, paused, rows]);

  useEffect(() => {
    let cancelled = false;
    setStatus("connecting");
    setDetail("Loading structured log sources.");

    async function loadSources() {
      try {
        const response = await moduleApi.backend.http.getJson<StructuredLogSource[]>(`${structuredLogsEndpointPrefix}/sources`);
        if (!cancelled) {
          setSources(response.sort(compareStructuredLogSources));
        }
      } catch (error) {
        if (!cancelled) {
          const classified = classifyStructuredLogError(error);
          setStatus(classified.status);
          setDetail(classified.message);
          setSources([]);
        }
      }
    }

    void loadSources();
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    let cancelled = false;
    let eventSource: EventSource | null = null;

    setRows([]);
    setSelectedEntryId(null);
    setBufferedRows([]);
    setQueuedCount(0);
    setUpstreamDroppedCount(0);
    setLocalDiscardedCount(0);
    setStatus("connecting");
    setDetail("Loading recent structured logs.");

    async function loadRecent() {
      try {
        const response = await moduleApi.backend.http.getJson<StructuredLogWireEntry[]>(createRecentPath(filters));
        if (!cancelled) {
          setRows(response.map(normalizeStructuredLogEntry).sort(compareStructuredLogEntries).slice(-maxStructuredLogRows));
        }
      } catch (error) {
        if (!cancelled) {
          const classified = classifyStructuredLogError(error);
          setStatus(classified.status);
          setDetail(classified.message);
        }
      }
    }

    function connect() {
      eventSource = new EventSource(createStreamUrl(moduleApi.backend, filters));

      eventSource.onopen = () => {
        if (cancelled)
          return;
        setStatus(pausedRef.current ? "paused" : "live");
        setDetail("Live structured log stream connected.");
      };

      eventSource.addEventListener("entry", event => {
        if (cancelled)
          return;
        addEntries([normalizeStructuredLogEntry(parseSseJson<StructuredLogWireEntry>(event))]);
      });

      eventSource.addEventListener("dropped", event => {
        if (cancelled)
          return;
        const dropped = parseSseJson<DroppedEntriesSignal>(event);
        setUpstreamDroppedCount(current => current + (dropped.droppedCount ?? dropped.count ?? 0));
      });

      eventSource.onerror = () => {
        if (cancelled)
          return;
        setStatus("disconnected");
        setDetail("Live stream disconnected. The browser will retry automatically.");
      };
    }

    void loadRecent().then(() => {
      if (!cancelled) {
        connect();
      }
    });

    return () => {
      cancelled = true;
      eventSource?.close();
    };
  }, [addEntries, filters]);

  function togglePaused() {
    setPaused(current => {
      const next = !current;
      if (current && bufferedRows.length > 0) {
        const appendResult = appendStructuredLogEntries(rows, bufferedRows);
        setRows(appendResult.rows);
        if (appendResult.discarded > 0) {
          setLocalDiscardedCount(count => count + appendResult.discarded);
        }
        setBufferedRows([]);
        setQueuedCount(0);
      }
      setStatus(next ? "paused" : "live");
      return next;
    });
  }

  function clear() {
    setRows([]);
    setSelectedEntryId(null);
    setBufferedRows([]);
    setQueuedCount(0);
    setLocalDiscardedCount(0);
  }

  async function copyRows(targetRows: StructuredLogEntry[]) {
    const text = targetRows.map(formatStructuredLogCopyLine).join("\n");
    if (!text)
      return;

    await navigator.clipboard?.writeText(text);
  }

  async function copyMessage(entry: StructuredLogEntry) {
    if (!entry.message)
      return;

    await navigator.clipboard?.writeText(entry.message);
  }

  function downloadRows(targetRows: StructuredLogEntry[]) {
    if (targetRows.length === 0 || typeof document === "undefined")
      return;

    const blob = new Blob([createStructuredLogExportContent(targetRows)], { type: "application/x-ndjson;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = createStructuredLogExportFilename();
    link.rel = "noopener";
    document.body.appendChild(link);
    link.click();
    link.remove();
    window.setTimeout(() => URL.revokeObjectURL(url), 0);
  }

  return {
    filters,
    setFilters,
    sources,
    rows,
    selectedEntryId,
    setSelectedEntryId,
    status,
    detail,
    paused,
    togglePaused,
    queuedCount,
    upstreamDroppedCount,
    localDiscardedCount,
    autoScroll,
    setAutoScroll,
    wrapMessages,
    setWrapMessages,
    compact,
    setCompact,
    viewportRef,
    clear,
    copyRows,
    copyMessage,
    downloadRows
  };
}

type StructuredLogsState = ReturnType<typeof useStructuredLogs>;

function StructuredLogsHeader({ state, compactControls = false }: { state: StructuredLogsState; compactControls?: boolean }) {
  const statusLabel = getConnectionStatusLabel(state.status);
  const selectedRows = state.selectedEntryId ? state.rows.filter(row => row.id === state.selectedEntryId) : [];
  const levelSelect = (
    <select aria-label="Minimum log level" value={state.filters.minLevel} onChange={event => state.setFilters({ ...state.filters, minLevel: event.target.value as StructuredLogFilters["minLevel"] })}>
      <option value="">All levels</option>
      {levelOrder.map(level => <option value={level} key={level}>{level}</option>)}
    </select>
  );

  return (
    <header className="structured-logs-header">
      <div>
        <h2>Structured logs</h2>
        <p>{state.queuedCount > 0 ? `${state.queuedCount} buffered while paused` : state.detail}</p>
      </div>
      <div className="structured-logs-tools">
        {!compactControls ? (
          <>
            {levelSelect}
            <select aria-label="Structured log source" value={state.filters.sourceId} onChange={event => state.setFilters({ ...state.filters, sourceId: event.target.value })}>
              <option value="">All sources</option>
              {state.sources.map(source => <option value={source.id} key={source.id}>{formatStructuredLogSourceLabel(source)}</option>)}
            </select>
            <input
              aria-label="Filter by category"
              placeholder="Category"
              value={state.filters.category}
              onChange={event => state.setFilters({ ...state.filters, category: event.target.value })}
            />
          </>
        ) : (
          <>
            {levelSelect}
            <select aria-label="Structured log source" value={state.filters.sourceId} onChange={event => state.setFilters({ ...state.filters, sourceId: event.target.value })}>
              <option value="">All sources</option>
              {state.sources.map(source => <option value={source.id} key={source.id}>{formatStructuredLogSourceLabel(source)}</option>)}
            </select>
          </>
        )}
        <span className={`structured-logs-status ${state.status}`} aria-label={statusLabel} />
        <span>{statusLabel}</span>
        {state.upstreamDroppedCount > 0 ? <span>{state.upstreamDroppedCount} dropped upstream</span> : null}
        {state.localDiscardedCount > 0 ? <span>{state.localDiscardedCount} discarded locally</span> : null}
        <button type="button" className={state.paused ? "active" : ""} onClick={state.togglePaused} aria-pressed={state.paused}>
          {state.paused ? "Resume" : "Pause"}
        </button>
        <button type="button" className={state.autoScroll ? "active" : ""} onClick={() => state.setAutoScroll(current => !current)} aria-pressed={state.autoScroll}>
          Autoscroll
        </button>
        {!compactControls ? (
          <>
            <button type="button" className={state.wrapMessages ? "active" : ""} onClick={() => state.setWrapMessages(current => !current)} aria-pressed={state.wrapMessages}>
              Wrap
            </button>
            <button type="button" className={state.compact ? "active" : ""} onClick={() => state.setCompact(current => !current)} aria-pressed={state.compact}>
              Compact
            </button>
            <button type="button" onClick={() => void state.copyRows(selectedRows)} disabled={selectedRows.length === 0}>Copy selected</button>
            <button type="button" onClick={() => void state.copyRows(state.rows)}>Copy visible</button>
            <button type="button" onClick={() => state.downloadRows(state.rows)} disabled={state.rows.length === 0}>Download visible</button>
          </>
        ) : (
          <button type="button" onClick={() => state.downloadRows(state.rows)} disabled={state.rows.length === 0}>Download</button>
        )}
        <button type="button" onClick={state.clear}>Clear</button>
      </div>
    </header>
  );
}

function StructuredLogRows({ state, panelMode = false }: { state: StructuredLogsState; panelMode?: boolean }) {
  const rowClassName = [
    "structured-logs-rows",
    state.wrapMessages ? "wrap" : "",
    state.compact ? "compact" : "",
    panelMode ? "panel-mode" : ""
  ].filter(Boolean).join(" ");

  return (
    <div className={rowClassName} ref={state.viewportRef}>
      <div className="structured-logs-grid-header" role="row">
        <span>Time</span>
        <span>Level</span>
        <span>Source</span>
        <span>Category</span>
        <span>Message</span>
        <span aria-hidden="true" />
      </div>
      {state.rows.length === 0 ? (
        <div className="structured-logs-empty">
          {state.status === "unavailable" || state.status === "unauthorized" ? state.detail : "No structured logs received yet."}
        </div>
      ) : null}
      {state.rows.map(row => (
        <div
          className={row.id === state.selectedEntryId ? "structured-log-row selected" : "structured-log-row"}
          onClick={() => state.setSelectedEntryId(row.id)}
          onKeyDown={event => handleStructuredLogRowKeyDown(event, () => state.setSelectedEntryId(row.id))}
          role="row"
          tabIndex={0}
          aria-label={`Select ${row.level} structured log from ${row.category}`}
          key={row.id}
        >
          <time title={row.timestamp}>{formatLogTime(row.timestamp)}</time>
          <LevelChip level={row.level} />
          <span title={row.sourceId ?? undefined}>{row.sourceId ?? "local"}</span>
          <span title={row.category}>{row.category}</span>
          <span className="structured-log-message">
            {row.exception ? <strong title={row.exception.message ?? undefined}>Exception</strong> : null}
            {row.message}
          </span>
          <button
            type="button"
            className="structured-log-copy-message"
            onClick={event => {
              event.stopPropagation();
              void state.copyMessage(row);
            }}
            aria-label={`Copy structured log message from ${row.category}`}
            title="Copy message">
            <span aria-hidden="true" />
          </button>
        </div>
      ))}
    </div>
  );
}

function StructuredLogInspector({ entry, sources, onClose }: { entry: StructuredLogEntry; sources: StructuredLogSource[]; onClose(): void }) {
  const source = sources.find(candidate => candidate.id === entry.sourceId);

  return (
    <aside className="structured-log-inspector" aria-label="Structured log details">
      <header>
        <div>
          <span>{entry.level} · {formatLogTime(entry.timestamp)}</span>
          <h3>{entry.category}</h3>
        </div>
        <button type="button" onClick={onClose} aria-label="Close structured log details">Close</button>
      </header>

      <section>
        <h4>Message</h4>
        <p>{entry.message}</p>
        {entry.messageTemplate ? <pre>{entry.messageTemplate}</pre> : null}
      </section>

      <section>
        <h4>Metadata</h4>
        <dl>
          <DetailRow name="Sequence" value={String(entry.sequence)} />
          <DetailRow name="Timestamp" value={entry.timestamp} />
          <DetailRow name="Source" value={source ? formatStructuredLogSourceLabel(source) : entry.sourceId} />
          <DetailRow name="Event ID" value={entry.eventId == null ? null : String(entry.eventId)} />
          <DetailRow name="Event name" value={entry.eventName} />
        </dl>
      </section>

      {entry.properties.length > 0 ? (
        <section>
          <h4>Properties</h4>
          <dl>{entry.properties.map(property => <DetailRow name={property.name} value={formatStructuredValue(property.value)} key={property.name} />)}</dl>
        </section>
      ) : null}

      {entry.scopes.length > 0 ? (
        <section>
          <h4>Scopes</h4>
          {entry.scopes.map((scope, index) => (
            <dl key={index}>
              {scope.items?.map(item => <DetailRow name={item.name} value={formatStructuredValue(item.value)} key={item.name} />)}
              {scope.message ? <DetailRow name="Scope" value={scope.message} /> : null}
            </dl>
          ))}
        </section>
      ) : null}

      {entry.exception ? (
        <section>
          <h4>Exception</h4>
          <dl>
            <DetailRow name="Type" value={entry.exception.type} />
            <DetailRow name="Message" value={entry.exception.message} />
          </dl>
          {entry.exception.stackTrace ? <pre>{entry.exception.stackTrace}</pre> : null}
        </section>
      ) : null}

      <section>
        <h4>Raw JSON</h4>
        <pre>{JSON.stringify(entry.raw, null, 2)}</pre>
      </section>
    </aside>
  );
}

function LevelChip({ level }: { level: StructuredLogLevel }) {
  return <span className="structured-log-level" data-level={level.toLowerCase()}>{level}</span>;
}

function handleStructuredLogRowKeyDown(event: React.KeyboardEvent<HTMLDivElement>, selectRow: () => void) {
  if (event.key !== "Enter" && event.key !== " ") {
    return;
  }

  event.preventDefault();
  selectRow();
}

function DetailRow({ name, value }: { name: string; value?: string | null }) {
  if (!value)
    return null;

  return (
    <>
      <dt>{name}</dt>
      <dd>{value}</dd>
    </>
  );
}

export function createRecentPath(filters: StructuredLogFilters, take = structuredLogsRecentLimit) {
  const query = createStructuredLogQuery(filters);
  query.set("take", String(take));
  return `${structuredLogsEndpointPrefix}/recent?${query}`;
}

export function createStreamUrl(context: StudioEndpointContext, filters: StructuredLogFilters) {
  const query = createStructuredLogQuery(filters);
  const queryString = query.toString();
  const suffix = queryString ? `?${queryString}` : "";
  return new URL(`${structuredLogsEndpointPrefix}/stream${suffix}`, context.baseUrl).toString();
}

export function appendStructuredLogEntries(current: StructuredLogEntry[], entries: StructuredLogEntry[]): AppendStructuredLogResult {
  const byId = new Map(current.map(entry => [entry.id, entry]));
  for (const entry of entries) {
    byId.set(entry.id, entry);
  }

  const rows = [...byId.values()].sort(compareStructuredLogEntries);
  const discarded = Math.max(0, rows.length - maxStructuredLogRows);
  return {
    rows: rows.slice(-maxStructuredLogRows),
    discarded
  };
}

export function normalizeStructuredLogEntry(entry: StructuredLogWireEntry): StructuredLogEntry {
  const sequence = Number(entry.sequence ?? Date.now());
  return {
    id: String(sequence),
    sequence,
    timestamp: entry.timestamp ?? new Date().toISOString(),
    level: normalizeLogLevel(entry.level),
    category: entry.category ?? "Unknown",
    eventId: entry.eventId ?? null,
    eventName: entry.eventName ?? null,
    message: entry.message ?? "",
    messageTemplate: entry.messageTemplate ?? null,
    properties: normalizeLogProperties(entry.properties),
    scopes: normalizeLogScopes(entry.scopes),
    exception: entry.exception ?? null,
    sourceId: entry.sourceId ?? null,
    raw: entry
  };
}

export function normalizeLogLevel(level: StructuredLogWireEntry["level"]): StructuredLogLevel {
  if (typeof level === "number") {
    return levelOrder[level] ?? "Information";
  }

  const normalized = levelOrder.find(candidate => candidate.toLowerCase() === String(level ?? "").toLowerCase());
  return normalized ?? "Information";
}

export function formatStructuredLogSourceLabel(source: StructuredLogSource): string {
  const name = source.displayName || source.serviceName || source.id;
  if (source.machineName && source.processId) {
    return `${name} · ${source.machineName}:${source.processId}`;
  }

  return name;
}

export function classifyStructuredLogError(error: unknown): { status: ConnectionStatus; message: string } {
  const message = error instanceof Error ? error.message : String(error);
  if (/\b401\b|\b403\b/i.test(message)) {
    return { status: "unauthorized", message: "You do not have permission to view structured logs." };
  }

  if (/\b404\b/i.test(message)) {
    return { status: "unavailable", message: "Structured logs are not available on this backend." };
  }

  return { status: "failed", message: `Structured logs failed: ${message}` };
}

export function compareStructuredLogEntries(left: StructuredLogEntry, right: StructuredLogEntry) {
  if (left.sequence !== right.sequence) {
    return left.sequence - right.sequence;
  }

  return Date.parse(left.timestamp) - Date.parse(right.timestamp);
}

export function createStructuredLogExportContent(rows: StructuredLogEntry[]) {
  return rows.map(row => JSON.stringify({
    sequence: row.sequence,
    timestamp: row.timestamp,
    level: row.level,
    sourceId: row.sourceId,
    category: row.category,
    eventId: row.eventId,
    eventName: row.eventName,
    message: row.message,
    messageTemplate: row.messageTemplate,
    properties: row.properties,
    scopes: row.scopes,
    exception: row.exception,
    raw: row.raw
  })).join("\n");
}

export function createStructuredLogExportFilename(date = new Date()) {
  const stamp = date.toISOString().replace(/\.\d{3}Z$/, "Z").replace(/[:.]/g, "-");
  return `structured-logs-${stamp}.jsonl`;
}

function createStructuredLogQuery(filters: StructuredLogFilters) {
  const query = new URLSearchParams();
  if (filters.minLevel) {
    query.set("minLevel", filters.minLevel);
  }
  if (filters.category.trim()) {
    query.set("category", filters.category.trim());
  }
  if (filters.sourceId) {
    query.set("source", filters.sourceId);
  }
  return query;
}

function normalizeLogProperties(value: StructuredLogWireEntry["properties"]): StructuredLogProperty[] {
  if (!value)
    return [];

  if (Array.isArray(value)) {
    return value.map(item => ({ name: item.name, value: item.value == null ? null : String(item.value) }));
  }

  return Object.entries(value).map(([name, propertyValue]) => ({ name, value: propertyValue == null ? null : String(propertyValue) }));
}

function normalizeLogScopes(value: StructuredLogWireEntry["scopes"]): StructuredLogScope[] {
  if (!value)
    return [];

  if (Array.isArray(value)) {
    return value.map(scope => ({
      message: scope.message ?? null,
      items: normalizeLogProperties(scope.items ?? [])
    }));
  }

  return [{ items: normalizeLogProperties(value) }];
}

function parseSseJson<T>(event: Event): T {
  return JSON.parse((event as MessageEvent<string>).data) as T;
}

function compareStructuredLogSources(left: StructuredLogSource, right: StructuredLogSource) {
  return formatStructuredLogSourceLabel(left).localeCompare(formatStructuredLogSourceLabel(right));
}

function getConnectionStatusLabel(status: ConnectionStatus) {
  if (status === "live")
    return "live";
  if (status === "paused")
    return "paused";
  if (status === "connecting")
    return "connecting";
  if (status === "unavailable")
    return "unavailable";
  if (status === "unauthorized")
    return "unauthorized";
  if (status === "failed")
    return "failed";
  return "reconnecting";
}

function formatLogTime(timestamp: string) {
  const date = new Date(timestamp);
  return Number.isNaN(date.getTime()) ? timestamp : date.toLocaleTimeString();
}

function formatStructuredValue(value: unknown) {
  if (value == null)
    return null;

  if (typeof value === "string")
    return value;

  return JSON.stringify(value);
}

function formatStructuredLogCopyLine(entry: StructuredLogEntry) {
  return `${entry.timestamp} ${entry.level} ${entry.category} ${entry.message}`;
}

function getInitialBoolean(storageKey: string, fallback: boolean) {
  if (typeof window === "undefined" || typeof window.localStorage?.getItem !== "function") {
    return fallback;
  }

  try {
    const value = window.localStorage.getItem(storageKey);
    return value === null ? fallback : value === "true";
  } catch {
    return fallback;
  }
}

function setStoredBoolean(storageKey: string, value: boolean) {
  if (typeof window === "undefined" || typeof window.localStorage?.setItem !== "function") {
    return;
  }

  try {
    window.localStorage.setItem(storageKey, String(value));
  } catch {
    // Storage may be unavailable in privacy-restricted or test browser contexts.
  }
}
