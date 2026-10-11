import { describe, expect, it } from "vitest";
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

describe("structured logs module", () => {
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

  it("renders a minimum log level selector in the bottom panel", async () => {
    const previousEventSource = globalThis.EventSource;
    globalThis.EventSource = StubEventSource as unknown as typeof EventSource;
    register(stubApi({
      getJson: async url => url.includes("/sources") ? [] : []
    }));

    const { container, unmount } = await renderPanel();

    expect(container.querySelector("[aria-label='Minimum log level']")).toBeTruthy();

    await unmount();
    globalThis.EventSource = previousEventSource;
  });

  it("renders a structured log download action in the bottom panel", async () => {
    const previousEventSource = globalThis.EventSource;
    globalThis.EventSource = StubEventSource as unknown as typeof EventSource;
    register(stubApi({
      getJson: async url => url.includes("/sources") ? [] : [wireEntry(1)]
    }));

    const { container, unmount } = await renderPanel();

    expect(container.querySelector("button[disabled]")?.textContent).not.toBe("Download");
    expect([...container.querySelectorAll("button")].some(button => button.textContent === "Download")).toBe(true);

    await unmount();
    globalThis.EventSource = previousEventSource;
  });

  it("copies a single structured log message from the row action", async () => {
    const previousEventSource = globalThis.EventSource;
    const previousClipboard = navigator.clipboard;
    const copiedText: string[] = [];
    globalThis.EventSource = StubEventSource as unknown as typeof EventSource;
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
    Object.defineProperty(navigator, "clipboard", { configurable: true, value: previousClipboard });
    globalThis.EventSource = previousEventSource;
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

function stubApi(http?: {
  getJson?: (url: string, init?: RequestInit) => Promise<unknown>;
}) {
  return {
    backend: { baseUrl: "https://backend.example/", http: { getJson: http?.getJson ?? (async () => []) } },
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

  flushSync(() => {
    root.render(React.createElement(StructuredLogsPanel));
  });
  await flushPromises();

  return {
    container,
    unmount: async () => {
      flushSync(() => root.unmount());
      container.remove();
      await flushPromises();
    }
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
  url: string;
  withCredentials = false;

  constructor(url: string | URL) {
    super();
    this.url = String(url);
  }

  close() {
    this.readyState = StubEventSource.CLOSED;
  }
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
