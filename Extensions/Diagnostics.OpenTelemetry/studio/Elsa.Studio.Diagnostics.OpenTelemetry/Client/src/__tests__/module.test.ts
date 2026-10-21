import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it } from "vitest";
import {
  buildWaterfallRows,
  classifyOpenTelemetryError,
  createTracesPath,
  normalizeSpan,
  normalizeTraceDetail,
  normalizeTraceSummary,
  OpenTelemetryPage,
  register
} from "../module";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";

const mountedPages = new Set<() => void>();

afterEach(() => {
  for (const unmount of [...mountedPages]) unmount();
});

describe("open telemetry module", () => {
  it("registers diagnostics navigation, route, and bottom panel contributions", () => {
    const api = stubApi();

    register(api);

    expect(api.navigation.items).toMatchObject([
      {
        id: "open-telemetry",
        label: "OpenTelemetry",
        path: "/diagnostics/open-telemetry",
        parentId: "diagnostics",
        order: 880
      }
    ]);
    expect(api.routes.items).toMatchObject([
      {
        id: "open-telemetry",
        label: "OpenTelemetry",
        path: "/diagnostics/open-telemetry"
      }
    ]);
    expect(api.panels.items).toMatchObject([
      {
        id: "open-telemetry",
        title: "OpenTelemetry",
        order: 1020
      }
    ]);
  });

  it("builds trace query URLs with filters", () => {
    expect(createTracesPath({
      traceId: "abc",
      serviceName: "elsa-server",
      operation: "Run workflow",
      status: "Error",
      take: 250
    })).toBe("/_elsa/studio/diagnostics/otel/traces?traceId=abc&service=elsa-server&operation=Run+workflow&status=Error&take=250");
  });

  it("normalizes trace summaries and details", () => {
    expect(normalizeTraceSummary({
      traceId: "trace-1",
      name: "HTTP GET",
      serviceName: "elsa-server",
      startTime: "2026-06-22T00:00:00.000Z",
      endTime: "2026-06-22T00:00:01.250Z",
      spanCount: 2,
      status: "error"
    })).toMatchObject({
      traceId: "trace-1",
      durationMs: 1250,
      status: "Error",
      errorCount: 1
    });

    const detail = normalizeTraceDetail({
      traceId: "trace-1",
      name: "HTTP GET",
      serviceName: "elsa-server",
      startTime: "2026-06-22T00:00:00.000Z",
      durationMs: 100,
      spans: [
        { traceId: "trace-1", spanId: "child", parentSpanId: "root", name: "Child", startTime: "2026-06-22T00:00:00.020Z", durationMs: 25, attributes: { route: "/workflows" } },
        { traceId: "trace-1", spanId: "root", name: "Root", startTime: "2026-06-22T00:00:00.000Z", durationMs: 100, status: "Ok" }
      ]
    });

    expect(detail.spans.map(span => span.spanId)).toEqual(["root", "child"]);
    expect(detail.spans[1].attributes).toEqual([{ name: "route", value: "/workflows" }]);
  });

  it("orders waterfall rows by parent-child span hierarchy", () => {
    const spans = [
      normalizeSpan({ spanId: "grandchild", parentSpanId: "child", name: "Grandchild", startTime: "2026-06-22T00:00:00.030Z", durationMs: 5 }),
      normalizeSpan({ spanId: "root", name: "Root", startTime: "2026-06-22T00:00:00.000Z", durationMs: 50 }),
      normalizeSpan({ spanId: "child", parentSpanId: "root", name: "Child", startTime: "2026-06-22T00:00:00.010Z", durationMs: 20 })
    ];

    expect(buildWaterfallRows(spans).map(row => [row.span.spanId, row.depth])).toEqual([
      ["root", 0],
      ["child", 1],
      ["grandchild", 2]
    ]);
  });

  it("classifies backend endpoint failures", () => {
    expect(classifyOpenTelemetryError(new Error("Status code '403'"))).toMatchObject({ status: "unauthorized" });
    expect(classifyOpenTelemetryError(new Error("Status code '404'"))).toMatchObject({ status: "unavailable" });
    expect(classifyOpenTelemetryError(new Error("Status code '500'"))).toMatchObject({ status: "failed" });
  });

  it("explains unsupported Metrics and OpenTelemetry Logs before entry and uses a neutral Diagnostics handoff", async () => {
    const { container } = renderPage(async url => url.endsWith("/overview") ? {} : []);
    await flushPromises();

    expect(container.textContent).toMatch(/Metrics.*not available|not available.*Metrics/i);
    expect(container.textContent).toMatch(/OpenTelemetry Logs.*not available|not available.*OpenTelemetry Logs/i);
    const metricsTab = findTab(container, "Metrics");
    const logsTab = findTab(container, "Logs");
    expect(metricsTab === null || metricsTab.disabled || metricsTab.getAttribute("aria-disabled") === "true").toBe(true);
    expect(logsTab === null || logsTab.disabled || logsTab.getAttribute("aria-disabled") === "true").toBe(true);
    expect(container.textContent).not.toContain("Metric charts will appear here");
    expect(container.textContent).not.toContain("OpenTelemetry log records will appear here");
    expect(container.querySelector('a[href="/diagnostics/structured-logs"]')).toBeNull();
    expect(container.querySelector('a[href="/diagnostics"]')?.textContent).toMatch(/diagnostics/i);
    expect(container.textContent).not.toMatch(/Structured application logs are available/i);
  });

  it("renders trace loading and successful empty results as distinct states", async () => {
    const pending = renderPage(() => new Promise(() => {}));
    expect(pending.container.textContent).toMatch(/Loading (OpenTelemetry diagnostics|trace summaries)/);
    pending.unmount();

    const empty = renderPage(async url => url.endsWith("/overview") ? {} : []);
    await flushPromises();

    expect(empty.container.textContent).toContain("No traces match the current filters.");
    expect(empty.container.textContent).not.toMatch(/failed|permission denied|unavailable/i);
    empty.unmount();
  });

  it.each([
    ["permission", "Status code '401'", /Permission denied while loading OpenTelemetry diagnostics/],
    ["permission", "Status code '403'", /Permission denied while loading OpenTelemetry diagnostics/],
    ["unavailable", "Status code '404'", /OpenTelemetry diagnostics are unavailable/],
    ["failure", "Status code '500'", /OpenTelemetry diagnostics failed/]
  ])("renders a distinct trace %s state for %s", async (_state, message, expected) => {
    const { container } = renderPage(async url => {
      if (url.endsWith("/overview")) return {};
      throw new Error(message);
    });
    await flushPromises();

    expect(container.textContent).toMatch(expected);
    expect(container.textContent).not.toContain("No traces match the current filters.");
  });
});

function renderPage(getJson: (url: string) => Promise<unknown>) {
  register(stubApi(getJson));
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root: Root = createRoot(container);
  flushSync(() => root.render(React.createElement(OpenTelemetryPage)));
  const unmount = () => {
    if (!mountedPages.delete(unmount)) return;
    flushSync(() => root.unmount());
    container.remove();
  };
  mountedPages.add(unmount);
  return { container, unmount };
}

function findTab(container: HTMLElement, label: string) {
  return Array.from(container.querySelectorAll<HTMLButtonElement>('[role="tab"]')).find(tab => tab.textContent?.trim() === label) ?? null;
}

async function flushPromises() {
  for (let attempt = 0; attempt < 5; attempt++) await new Promise(resolve => setTimeout(resolve, 0));
}

function stubApi(getJson: (url: string) => Promise<unknown> = async () => []) {
  return {
    backend: { baseUrl: "https://backend.example/", http: { getJson } },
    navigation: registry(),
    routes: registry(),
    panels: registry()
  } as unknown as ElsaStudioModuleApi & {
    navigation: ReturnType<typeof registry>;
    routes: ReturnType<typeof registry>;
    panels: ReturnType<typeof registry>;
  };
}

function registry() {
  const items: unknown[] = [];
  return {
    items,
    add: (item: unknown) => items.push(item),
    list: () => items
  };
}
