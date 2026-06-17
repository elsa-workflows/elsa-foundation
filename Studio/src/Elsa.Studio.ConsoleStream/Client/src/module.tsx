import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import * as signalR from "@microsoft/signalr";
import type { ElsaStudioModuleApi, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import "./styles.css";

type ConsoleHostId = "server" | "studio";

interface ConsoleTarget {
  id: ConsoleHostId;
  label: string;
  context: StudioEndpointContext;
  endpointPrefix: string;
}

interface ConsoleLogSource {
  id: string;
  displayName?: string | null;
  serviceName?: string | null;
  processId?: number | null;
  machineName?: string | null;
  podName?: string | null;
  containerName?: string | null;
  namespace?: string | null;
  nodeName?: string | null;
  lastSeen?: string | null;
  health?: number | string | null;
}

interface ConsoleLogLine {
  id?: string;
  timestamp?: string;
  receivedAt?: string;
  sequence?: number;
  stream?: number | string;
  text?: string;
  source?: ConsoleLogSource | null;
}

interface ConsoleDroppedSummary {
  count: number;
}

interface ConsoleStreamItem {
  line?: ConsoleLogLine;
  droppedLines?: ConsoleDroppedSummary;
  dropped?: ConsoleDroppedSummary;
  source?: ConsoleLogSource;
}

interface ConsoleRecentResponse {
  items?: ConsoleLogLine[];
  lines?: ConsoleLogLine[];
  sources?: ConsoleLogSource[];
}

export interface ConsoleEntry {
  id: string;
  timestamp: string;
  sequence: number | null;
  stream: "stdout" | "stderr";
  text: string;
  sourceId: string | null;
  sourceLabel: string | null;
}

export interface AnsiSegment {
  text: string;
  className: string;
}

interface AnsiState {
  bold: boolean;
  dim: boolean;
  foreground: string;
  background: string;
}

const consoleTargetDefinitions: Record<ConsoleHostId, Pick<ConsoleTarget, "id" | "label" | "endpointPrefix">> = {
  server: { id: "server", label: "Server", endpointPrefix: "/diagnostics/console-logs" },
  studio: { id: "studio", label: "Studio", endpointPrefix: "/_elsa/studio/diagnostics/console-logs" }
};
const consoleReplayLimit = 2_000;
const maxConsoleLines = 2_000;
const consoleStreamServerTimeoutInMilliseconds = 120_000;
const consoleStreamKeepAliveIntervalInMilliseconds = 15_000;
const consoleStreamTimeoutMessage = "Server timeout elapsed without receiving a message from the server.";
const autoScrollStorageKey = "elsa-studio-console-stream-autoscroll";
const ansiEscapePattern = /\x1b\[([0-9;]*)m/g;
const ansiForegroundClasses: Record<number, string> = {
  30: "console-stream-ansi-fg-black",
  31: "console-stream-ansi-fg-red",
  32: "console-stream-ansi-fg-green",
  33: "console-stream-ansi-fg-yellow",
  34: "console-stream-ansi-fg-blue",
  35: "console-stream-ansi-fg-magenta",
  36: "console-stream-ansi-fg-cyan",
  37: "console-stream-ansi-fg-white",
  90: "console-stream-ansi-fg-bright-black",
  91: "console-stream-ansi-fg-bright-red",
  92: "console-stream-ansi-fg-bright-green",
  93: "console-stream-ansi-fg-bright-yellow",
  94: "console-stream-ansi-fg-bright-blue",
  95: "console-stream-ansi-fg-bright-magenta",
  96: "console-stream-ansi-fg-bright-cyan",
  97: "console-stream-ansi-fg-bright-white"
};
const ansiBackgroundClasses: Record<number, string> = {
  40: "console-stream-ansi-bg-black",
  41: "console-stream-ansi-bg-red",
  42: "console-stream-ansi-bg-green",
  43: "console-stream-ansi-bg-yellow",
  44: "console-stream-ansi-bg-blue",
  45: "console-stream-ansi-bg-magenta",
  46: "console-stream-ansi-bg-cyan",
  47: "console-stream-ansi-bg-white",
  100: "console-stream-ansi-bg-bright-black",
  101: "console-stream-ansi-bg-bright-red",
  102: "console-stream-ansi-bg-bright-green",
  103: "console-stream-ansi-bg-bright-yellow",
  104: "console-stream-ansi-bg-bright-blue",
  105: "console-stream-ansi-bg-bright-magenta",
  106: "console-stream-ansi-bg-bright-cyan",
  107: "console-stream-ansi-bg-bright-white"
};

let moduleApi: ElsaStudioModuleApi;

export function register(api: ElsaStudioModuleApi) {
  moduleApi = api;

  api.panels.add({
    id: "console-stream",
    title: "Console",
    order: 1_000,
    component: ConsoleStreamPanel
  });
}

export function ConsoleStreamPanel() {
  const [hostId, setHostId] = useState<ConsoleHostId>("server");
  const [sources, setSources] = useState<ConsoleLogSource[]>([]);
  const [selectedSourceId, setSelectedSourceId] = useState("");
  const [sourcesState, setSourcesState] = useState<"loading" | "ready" | "failed">("loading");
  const [lines, setLines] = useState<ConsoleEntry[]>([]);
  const [lineCount, setLineCount] = useState(0);
  const [connected, setConnected] = useState(false);
  const [paused, setPaused] = useState(false);
  const [pausedLines, setPausedLines] = useState<ConsoleEntry[] | null>(null);
  const [pausedLineCount, setPausedLineCount] = useState(0);
  const [autoScroll, setAutoScroll] = useState(getInitialAutoScroll);
  const seenLineIds = useRef(new Set<string>());
  const viewportRef = useRef<HTMLDivElement>(null);

  const visibleLines = paused ? pausedLines ?? lines : lines;
  const queuedLineCount = paused ? Math.max(0, lineCount - pausedLineCount) : 0;
  const target = useMemo(() => createConsoleTarget(hostId), [hostId]);
  const selectedSource = useMemo(
    () => sources.find(source => source.id === selectedSourceId) ?? null,
    [selectedSourceId, sources]);
  const sourceFilter = selectedSourceId || null;

  const addEntries = useCallback((entries: ConsoleEntry[]) => {
    const uniqueEntries: ConsoleEntry[] = [];

    for (const entry of entries) {
      if (seenLineIds.current.has(entry.id)) {
        continue;
      }

      seenLineIds.current.add(entry.id);
      uniqueEntries.push(entry);
    }

    if (uniqueEntries.length === 0) {
      return;
    }

    setLineCount(current => current + uniqueEntries.length);
    setLines(current => appendConsoleEntries(current, uniqueEntries));
  }, []);

  const addLine = useCallback((stream: "stdout" | "stderr", text: string) => {
    addEntries([createConsoleEntry(stream, text)]);
  }, [addEntries]);

  const loadRecentLines = useCallback(async () => {
    const response = await target.context.http.getJson<ConsoleRecentResponse>(createRecentPath(target, sourceFilter));
    if (response.sources) {
      setSources(response.sources);
    }

    const recentLines = response.items ?? response.lines ?? [];
    addEntries(recentLines.map(createConsoleEntryFromLine));
  }, [addEntries, sourceFilter, target]);

  useEffect(() => {
    let cancelled = false;
    setSourcesState("loading");
    setSelectedSourceId("");

    async function loadSources() {
      try {
        const response = await target.context.http.getJson<ConsoleLogSource[]>(`${target.endpointPrefix}/sources`);
        if (!cancelled) {
          setSources(response);
          setSourcesState("ready");
        }
      } catch {
        if (!cancelled) {
          setSources([]);
          setSourcesState("failed");
        }
      }
    }

    void loadSources();
    return () => {
      cancelled = true;
    };
  }, [target]);

  useEffect(() => {
    window.localStorage.setItem(autoScrollStorageKey, String(autoScroll));
  }, [autoScroll]);

  useEffect(() => {
    if (!autoScroll || paused) {
      return;
    }

    const viewport = viewportRef.current;
    if (viewport) {
      viewport.scrollTop = viewport.scrollHeight;
    }
  }, [autoScroll, paused, visibleLines]);

  useEffect(() => {
    let cancelled = false;
    let subscription: signalR.ISubscription<ConsoleStreamItem> | null = null;
    seenLineIds.current.clear();
    setLines([]);
    setLineCount(0);
    setPausedLines(null);
    setPausedLineCount(0);
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(resolveTargetUrl(target, `${target.endpointPrefix}/hub`))
      .withAutomaticReconnect()
      .build();
    connection.serverTimeoutInMilliseconds = consoleStreamServerTimeoutInMilliseconds;
    connection.keepAliveIntervalInMilliseconds = consoleStreamKeepAliveIntervalInMilliseconds;

    connection.onreconnecting(() => setConnected(false));
    connection.onreconnected(() => {
      setConnected(true);
      void subscribeToStream();
    });
    connection.onclose(() => setConnected(false));

    function disposeSubscription() {
      subscription?.dispose();
      subscription = null;
    }

    function subscribeToStream() {
      if (cancelled || connection.state !== signalR.HubConnectionState.Connected) {
        return;
      }

      disposeSubscription();
      subscription = connection.stream<ConsoleStreamItem>("StreamAsync", createConsoleFilter(sourceFilter)).subscribe({
        next: item => {
          if (item?.line) {
            addEntries([createConsoleEntryFromLine(item.line)]);
          } else if (item?.droppedLines || item?.dropped) {
            const dropped = item.droppedLines ?? item.dropped;
            addLine("stderr", `${dropped?.count ?? 0} console lines were dropped.`);
          } else if (item?.source) {
            setSources(current => upsertConsoleSource(current, item.source!));
          }
        },
        error: error => {
          if (cancelled) {
            return;
          }

          if (isRecoverableConsoleStreamError(error)) {
            void subscribeToStream();
            return;
          }

          addLine("stderr", `Console stream failed: ${getErrorMessage(error)}`);
        },
        complete: () => addLine("stdout", "Console stream completed.")
      });
    }

    async function connect() {
      try {
        await connection.start();
        if (cancelled) {
          return;
        }

        setConnected(true);
        subscribeToStream();

        await loadRecentLines();
      } catch (error) {
        setConnected(false);
        addLine("stderr", `Console stream connection failed: ${getErrorMessage(error)}`);
        await loadRecentLines().catch(recentError =>
          addLine("stderr", `Recent console lines failed: ${getErrorMessage(recentError)}`));
      }
    }

    void connect();

    return () => {
      cancelled = true;
      disposeSubscription();
      void connection.stop();
    };
  }, [addEntries, addLine, loadRecentLines, sourceFilter, target]);

  const renderedLines = useMemo(
    () => visibleLines.map(line => ({ ...line, renderedText: renderConsoleText(line.text) })),
    [visibleLines]
  );

  function togglePaused() {
    if (paused) {
      setPaused(false);
      setPausedLines(null);
      return;
    }

    setPausedLines(lines);
    setPausedLineCount(lineCount);
    setPaused(true);
  }

  function clear() {
    setLines([]);
    setLineCount(0);
    setPausedLines(paused ? [] : null);
    setPausedLineCount(0);
    seenLineIds.current.clear();
  }

  return (
    <section className="console-stream-panel">
      <header className="console-stream-header">
        <div>
          <h2>{target.label} console</h2>
          <p>{getConsolePanelDetail(selectedSource, sourcesState, queuedLineCount)}</p>
        </div>
        <div className="console-stream-tools">
          <div className="console-stream-hosts" role="group" aria-label="Console host">
            {Object.values(consoleTargetDefinitions).map(host => (
              <button
                type="button"
                className={host.id === hostId ? "active" : ""}
                aria-pressed={host.id === hostId}
                onClick={() => setHostId(host.id)}
                key={host.id}>
                {host.label}
              </button>
            ))}
          </div>
          <select
            aria-label="Console source"
            value={selectedSourceId}
            onChange={event => setSelectedSourceId(event.target.value)}>
            <option value="">All sources</option>
            {sources.map(source => (
              <option value={source.id} key={source.id}>{formatConsoleSourceLabel(source)}</option>
            ))}
          </select>
          <span className={connected ? "console-stream-status online" : "console-stream-status"} />
          <span>{connected ? "live" : "waiting"}</span>
          <span>stdout</span>
          <span>stderr</span>
          <button type="button" className={paused ? "active" : ""} onClick={togglePaused} aria-pressed={paused}>
            {paused ? "Resume" : "Pause"}
          </button>
          <button type="button" className={autoScroll ? "active" : ""} onClick={() => setAutoScroll(current => !current)} aria-pressed={autoScroll}>
            Autoscroll
          </button>
          <button type="button" onClick={clear}>Clear</button>
        </div>
      </header>
      <div className="console-stream-lines" ref={viewportRef}>
        {renderedLines.length === 0 ? (
          <div className="console-stream-line stdout">
            <span>{new Date().toLocaleTimeString()}</span>
            <small>{selectedSource ? formatConsoleSourceLabel(selectedSource) : "All sources"}</small>
            <code>Console stream is ready.</code>
          </div>
        ) : null}
        {renderedLines.map(line => (
          <div className={`console-stream-line ${line.stream}`} key={line.id}>
            <span>{new Date(line.timestamp).toLocaleTimeString()}</span>
            <small>{line.sourceLabel ?? "local"}</small>
            <code>{line.renderedText}</code>
          </div>
        ))}
      </div>
    </section>
  );
}

export function getConsoleStreamName(stream: ConsoleLogLine["stream"]): "stdout" | "stderr" {
  return stream === 1 || stream === "stderr" || stream === "Stderr" ? "stderr" : "stdout";
}

export function compareConsoleEntries(left: ConsoleEntry, right: ConsoleEntry) {
  const timestampDelta = Date.parse(left.timestamp) - Date.parse(right.timestamp);
  if (timestampDelta !== 0) {
    return timestampDelta;
  }

  return (left.sequence ?? 0) - (right.sequence ?? 0);
}

export function appendConsoleEntries(current: ConsoleEntry[], entries: ConsoleEntry[]) {
  return [...current, ...entries].sort(compareConsoleEntries).slice(-maxConsoleLines);
}

export function createConsoleEntry(stream: "stdout" | "stderr", text: string): ConsoleEntry {
  return {
    id: `${Date.now()}-${Math.random()}`,
    timestamp: new Date().toISOString(),
    sequence: null,
    stream,
    text,
    sourceId: null,
    sourceLabel: null
  };
}

export function createConsoleEntryFromLine(line: ConsoleLogLine): ConsoleEntry {
  return {
    id: line.id ?? `${line.sequence ?? Date.now()}-${Math.random()}`,
    timestamp: line.timestamp ?? line.receivedAt ?? new Date().toISOString(),
    sequence: line.sequence ?? null,
    stream: getConsoleStreamName(line.stream),
    text: line.text ?? "",
    sourceId: line.source?.id ?? null,
    sourceLabel: line.source ? formatConsoleSourceLabel(line.source) : null
  };
}

export function formatConsoleSourceLabel(source: ConsoleLogSource): string {
  if (source.podName) {
    return [source.namespace, source.podName, source.containerName].filter(Boolean).join(" / ");
  }

  const name = source.displayName || source.serviceName || source.id;
  if (source.machineName && source.processId) {
    return `${name} · ${source.machineName}:${source.processId}`;
  }

  return name;
}

export function createConsoleFilter(sourceId: string | null) {
  return sourceId ? { limit: consoleReplayLimit, sourceId } : { limit: consoleReplayLimit };
}

export function parseAnsiSegments(text: string): AnsiSegment[] {
  if (!text.includes("\x1b[")) {
    return [{ text, className: "" }];
  }

  const segments: AnsiSegment[] = [];
  const state = createAnsiState();
  let lastIndex = 0;
  let match: RegExpExecArray | null;

  ansiEscapePattern.lastIndex = 0;
  while ((match = ansiEscapePattern.exec(text)) !== null) {
    if (match.index > lastIndex) {
      segments.push({ text: text.slice(lastIndex, match.index), className: getAnsiClassName(state) });
    }

    const codes = match[1] === "" ? [0] : match[1].split(";").map(value => Number(value || 0));
    updateAnsiState(state, codes);
    lastIndex = ansiEscapePattern.lastIndex;
  }

  if (lastIndex < text.length) {
    segments.push({ text: text.slice(lastIndex), className: getAnsiClassName(state) });
  }

  return segments;
}

function renderConsoleText(text: string) {
  return parseAnsiSegments(text).map((segment, index) =>
    segment.className ? <span className={segment.className} key={index}>{segment.text}</span> : segment.text);
}

function createAnsiState(): AnsiState {
  return {
    bold: false,
    dim: false,
    foreground: "",
    background: ""
  };
}

function updateAnsiState(state: AnsiState, codes: number[]) {
  for (const code of codes) {
    if (code === 0) {
      Object.assign(state, createAnsiState());
    } else if (code === 1) {
      state.bold = true;
      state.dim = false;
    } else if (code === 2) {
      state.dim = true;
      state.bold = false;
    } else if (code === 22) {
      state.bold = false;
      state.dim = false;
    } else if (code === 39) {
      state.foreground = "";
    } else if (code === 49) {
      state.background = "";
    } else if (ansiForegroundClasses[code]) {
      state.foreground = ansiForegroundClasses[code];
    } else if (ansiBackgroundClasses[code]) {
      state.background = ansiBackgroundClasses[code];
    }
  }
}

function getAnsiClassName(state: AnsiState) {
  return [
    state.bold ? "console-stream-ansi-bold" : "",
    state.dim ? "console-stream-ansi-dim" : "",
    state.foreground,
    state.background
  ].filter(Boolean).join(" ");
}

function getInitialAutoScroll() {
  if (typeof window === "undefined") {
    return true;
  }

  return window.localStorage.getItem(autoScrollStorageKey) !== "false";
}

function getErrorMessage(error: unknown) {
  return error instanceof Error ? error.message : String(error);
}

export function isRecoverableConsoleStreamError(error: unknown) {
  return getErrorMessage(error) === consoleStreamTimeoutMessage;
}

function createConsoleTarget(hostId: ConsoleHostId): ConsoleTarget {
  const definition = consoleTargetDefinitions[hostId];
  return {
    ...definition,
    context: hostId === "server" ? moduleApi.backend : moduleApi.host
  };
}

function createRecentPath(target: ConsoleTarget, sourceId: string | null) {
  const query = new URLSearchParams({ limit: String(consoleReplayLimit) });
  if (sourceId) {
    query.set("sourceId", sourceId);
  }

  return `${target.endpointPrefix}/recent?${query}`;
}

function resolveTargetUrl(target: ConsoleTarget, path: string) {
  return new URL(path, target.context.baseUrl).toString();
}

function upsertConsoleSource(sources: ConsoleLogSource[], source: ConsoleLogSource) {
  const index = sources.findIndex(candidate => candidate.id === source.id);
  if (index === -1) {
    return [...sources, source].sort(compareConsoleSources);
  }

  const next = [...sources];
  next[index] = source;
  return next.sort(compareConsoleSources);
}

function compareConsoleSources(left: ConsoleLogSource, right: ConsoleLogSource) {
  return formatConsoleSourceLabel(left).localeCompare(formatConsoleSourceLabel(right));
}

function getConsolePanelDetail(source: ConsoleLogSource | null, sourcesState: "loading" | "ready" | "failed", queuedLineCount: number) {
  if (queuedLineCount > 0) {
    return `${queuedLineCount} buffered while paused`;
  }

  if (source) {
    return source.podName ? [source.namespace, source.nodeName].filter(Boolean).join(" / ") : source.id;
  }

  return sourcesState === "loading" ? "Loading sources" : sourcesState === "failed" ? "Sources unavailable" : "Merged source stream";
}
