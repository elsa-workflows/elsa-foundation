import { afterEach, beforeEach, describe, expect, it } from "vitest";
import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import {
  appendStructuredLogEntries,
  classifyStructuredLogError,
  createRecentPath,
  createStructuredLogExportContent,
  createStructuredLogExportFilename,
  createStreamUrl,
  formatStructuredLogSourceLabel,
  normalizeLogLevel,
  normalizeStructuredLogEntry,
  register,
  StructuredLogsPanel,
  type StructuredLogEntry
} from "../module";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";

const renderedPanelCleanups = new Set<() => Promise<void>>();

describe("structured logs module", () => {
  let originalEventSource: PropertyDescriptor | undefined;
  let originalClipboard: PropertyDescriptor | undefined;

  beforeEach(() => {
    originalEventSource = Object.getOwnPropertyDescriptor(globalThis, "EventSource");
    originalClipboard = Object.getOwnPropertyDescriptor(navigator, "clipboard");
    StubEventSource.instances = [];
    globalThis.EventSource = StubEventSource as unknown as typeof EventSource;
  });

  afterEach(async () => {
    try {
      await Promise.all([...renderedPanelCleanups].map(cleanup => cleanup()));
    } finally {
      restoreProperty(globalThis, "EventSource", originalEventSource);
      restoreProperty(navigator, "clipboard", originalClipboard);
    }
  });

  it("registers navigation, route, and bottom panel contributions", () => {
    const api = stubApi();

    register(api);

    expect(api.navigation.items).toMatchObject([
      {
        id: "structured-logs",
        label: "Structured logs",
        path: "/diagnostics/structured-logs",
        parentId: "diagnostics"
      }
    ]);
    expect(api.routes.items).toMatchObject([
      {
        id: "structured-logs",
        label: "Structured logs",
        path: "/diagnostics/structured-logs"
      }
    ]);
    expect(api.panels.items).toMatchObject([
      {
        id: "structured-logs",
        title: "Structured Logs",
        order: 1010
      }
    ]);
  });

  it("builds recent and stream URLs with filter query parameters", () => {
    const filters = { minLevel: "Warning" as const, sourceId: "server-1", category: "Elsa.Workflows" };

    expect(createRecentPath(filters, 100)).toBe("/_elsa/studio/diagnostics/structured-logs/recent?minLevel=Warning&category=Elsa.Workflows&source=server-1&take=100");
    expect(createStreamUrl({ baseUrl: "https://backend.example/", http: {} as never }, filters))
      .toBe("https://backend.example/_elsa/studio/diagnostics/structured-logs/stream?minLevel=Warning&category=Elsa.Workflows&source=server-1");
  });

  it.each([
    ["same-origin", `${window.location.origin}/`],
    ["separate-origin", "https://backend.example/"]
  ])("passes credentials to the rendered EventSource for a %s backend without changing its filter URL", async (_, baseUrl) => {
    register(stubApi({ baseUrl }));

    await renderPanel();

    const source = StubEventSource.instances[0];
    expect(StubEventSource.instances).toHaveLength(1);
    expect(source?.url).toBe(new URL("/_elsa/studio/diagnostics/structured-logs/stream?minLevel=Information", baseUrl).toString());
    expect(source?.initOptions).toEqual({ withCredentials: true });
  });

  it("renders a minimum log level selector in the bottom panel", async () => {
    register(stubApi({
      getJson: async url => url.includes("/sources") ? [] : []
    }));

    const { container, unmount } = await renderPanel();

    expect(container.querySelector("[aria-label='Minimum log level']")).toBeTruthy();

    await unmount();
  });

  it("renders a structured log download action in the bottom panel", async () => {
    register(stubApi({
      getJson: async url => url.includes("/sources") ? [] : [wireEntry(1)]
    }));

    const { container, unmount } = await renderPanel();

    expect(container.querySelector("button[disabled]")?.textContent).not.toBe("Download");
    expect([...container.querySelectorAll("button")].some(button => button.textContent === "Download")).toBe(true);

    await unmount();
  });

  it("copies a single structured log message from the row action", async () => {
    const copiedText: string[] = [];
    Object.defineProperty(navigator, "clipboard", {
      configurable: true,
      value: { writeText: async (text: string) => copiedText.push(text) }
    });
    register(stubApi({
      getJson: async url => url.includes("/sources") ? [] : [wireEntry(1, "copy this message")]
    }));

    const { container, unmount } = await renderPanel();
    const copyButton = container.querySelector<HTMLButtonElement>("[aria-label='Copy structured log message from Elsa.Test']");

    copyButton?.click();
    await flushPromises();

    expect(copiedText).toEqual(["copy this message"]);

    await unmount();
  });

  it("exports visible rows as structured JSONL", () => {
    const content = createStructuredLogExportContent([
      entry(7, "first"),
      { ...entry(8, "second"), level: "Warning", sourceId: "server" }
    ]);
    const lines = content.split("\n").map(line => JSON.parse(line));

    expect(lines).toMatchObject([
      { sequence: 7, level: "Information", category: "Elsa.Test", message: "first" },
      { sequence: 8, level: "Warning", sourceId: "server", message: "second" }
    ]);
    expect(createStructuredLogExportFilename(new Date("2026-06-22T01:02:03.456Z"))).toBe("structured-logs-2026-06-22T01-02-03Z.jsonl");
  });

  it("normalizes log levels and wire entries", () => {
    expect(normalizeLogLevel(4)).toBe("Error");
    expect(normalizeLogLevel("warning")).toBe("Warning");
    expect(normalizeLogLevel("unknown")).toBe("Information");

    expect(normalizeStructuredLogEntry({
      sequence: 42,
      timestamp: "2026-06-18T10:31:22.114Z",
      level: "Error",
      category: "Elsa.Workflows.Runtime",
      message: "Activity failed",
      properties: { ActivityType: "HttpRequest", Attempt: 2 },
      scopes: [{ items: [{ name: "WorkflowInstanceId", value: "wf-123" }] }],
      sourceId: "elsa-server"
    })).toMatchObject({
      id: "42",
      sequence: 42,
      level: "Error",
      category: "Elsa.Workflows.Runtime",
      message: "Activity failed",
      properties: [
        { name: "ActivityType", value: "HttpRequest" },
        { name: "Attempt", value: "2" }
      ],
      scopes: [
        { items: [{ name: "WorkflowInstanceId", value: "wf-123" }] }
      ],
      sourceId: "elsa-server"
    });
  });

  it("deduplicates, sorts, and caps appended entries", () => {
    const current = Array.from({ length: 1_999 }, (_, index) => entry(index));
    const result = appendStructuredLogEntries(current, [entry(1_000, "replacement"), entry(1_999), entry(2_000)]);

    expect(result.rows).toHaveLength(2_000);
    expect(result.discarded).toBe(1);
    expect(result.rows[0].id).toBe("line-1");
    expect(result.rows.find(row => row.id === "line-1000")?.message).toBe("replacement");
    expect(result.rows.at(-1)?.id).toBe("line-2000");
  });

  it("formats source labels like the console stream module", () => {
    expect(formatStructuredLogSourceLabel({
      id: "machine:123",
      displayName: "Elsa.Server",
      machineName: "machine",
      processId: 123
    })).toBe("Elsa.Server · machine:123");

    expect(formatStructuredLogSourceLabel({
      id: "elsa-server",
      serviceName: "elsa-server"
    })).toBe("elsa-server");
  });

  it("classifies backend endpoint failures", () => {
    expect(classifyStructuredLogError(new Error("Status code '403'"))).toMatchObject({ status: "unauthorized" });
    expect(classifyStructuredLogError(new Error("Status code '404'"))).toMatchObject({ status: "unavailable" });
    expect(classifyStructuredLogError(new Error("Status code '500'"))).toMatchObject({ status: "failed" });
  });
});

function stubApi(options?: {
  baseUrl?: string;
  getJson?: (url: string, init?: RequestInit) => Promise<unknown>;
}) {
  return {
    backend: {
      baseUrl: options?.baseUrl ?? "https://backend.example/",
      http: { getJson: options?.getJson ?? (async () => []) }
    },
    navigation: registry(),
    routes: registry(),
    panels: registry()
  } as unknown as ElsaStudioModuleApi & {
    navigation: ReturnType<typeof registry>;
    routes: ReturnType<typeof registry>;
    panels: ReturnType<typeof registry>;
  };
}

async function renderPanel() {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  let mounted = true;

  const unmount = async () => {
    if (!mounted)
      return;

    mounted = false;
    try {
      flushSync(() => root.unmount());
    } finally {
      container.remove();
      renderedPanelCleanups.delete(unmount);
    }
    await flushPromises();
  };

  renderedPanelCleanups.add(unmount);

  try {
    flushSync(() => {
      root.render(React.createElement(StructuredLogsPanel));
    });
    await flushPromises();
  } catch (error) {
    await unmount();
    throw error;
  }

  return {
    container,
    unmount
  };
}

async function flushPromises() {
  await Promise.resolve();
  await Promise.resolve();
  await new Promise(resolve => setTimeout(resolve, 0));
}

class StubEventSource extends EventTarget {
  static readonly CONNECTING = 0;
  static readonly OPEN = 1;
  static readonly CLOSED = 2;
  readonly CONNECTING = 0;
  readonly OPEN = 1;
  readonly CLOSED = 2;
  onerror: ((this: EventSource, ev: Event) => unknown) | null = null;
  onmessage: ((this: EventSource, ev: MessageEvent) => unknown) | null = null;
  onopen: ((this: EventSource, ev: Event) => unknown) | null = null;
  readyState = StubEventSource.OPEN;
  static instances: StubEventSource[] = [];
  url: string;
  initOptions: EventSourceInit | undefined;
  withCredentials: boolean;

  constructor(url: string | URL, initOptions?: EventSourceInit) {
    super();
    this.url = String(url);
    this.initOptions = initOptions;
    this.withCredentials = initOptions?.withCredentials ?? false;
    StubEventSource.instances.push(this);
  }

  close() {
    this.readyState = StubEventSource.CLOSED;
  }
}

function restoreProperty(target: object, property: PropertyKey, descriptor: PropertyDescriptor | undefined) {
  if (descriptor)
    Object.defineProperty(target, property, descriptor);
  else
    Reflect.deleteProperty(target, property);
}

function registry() {
  const items: unknown[] = [];
  return {
    items,
    add(item: unknown) {
      items.push(item);
    },
    list() {
      return items;
    }
  };
}

function wireEntry(sequence: number, message = `line ${sequence}`) {
  return {
    sequence,
    timestamp: new Date(sequence).toISOString(),
    level: "Information",
    category: "Elsa.Test",
    message
  };
}

function entry(sequence: number, message = `line ${sequence}`): StructuredLogEntry {
  return {
    id: `line-${sequence}`,
    sequence,
    timestamp: new Date(sequence).toISOString(),
    level: "Information",
    category: "Elsa.Test",
    eventId: null,
    eventName: null,
    message,
    messageTemplate: null,
    properties: [],
    scopes: [],
    exception: null,
    sourceId: null,
    raw: { sequence, message }
  };
}
