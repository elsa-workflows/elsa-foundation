import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it } from "vitest";
import { buildWaterfallRows, classifyOpenTelemetryError, OpenTelemetryPage, register, normalizeSpan } from "../module";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";

type StudioRequest = { method: "GET" | "POST"; url: string; body?: unknown };
type Responder = (request: StudioRequest) => Promise<unknown>;
type OverviewReadState = "success" | "forbidden" | "missing" | "pending" | "empty";

const traceId = "trace-workflow-3131";
const trace = {
  traceId,
  rootSpanId: "span-root-3131",
  name: "Workflow WriteLine",
  startTime: "2026-10-21T09:00:00.000Z",
  endTime: "2026-10-21T09:00:01.250Z",
  duration: "00:00:01.2500000",
  status: "Ok",
  resourceIds: ["resource-workbench-3131", "resource-activities-3131", "resource-unknown-3131"],
  workflowInstanceIds: ["instance-workflow-3131"],
  spanCount: 3
};

const tracesResponse = {
  items: [
    trace,
    { ...trace, traceId: "trace-unset-3131", name: "Unset operation", startTime: "2026-10-21T08:00:00.000Z", endTime: "2026-10-21T08:00:00.010Z", duration: "00:00:00.0100000", status: "Unset", resourceIds: ["resource-unknown-3131"], workflowInstanceIds: [], spanCount: 1 },
    { ...trace, traceId: "trace-error-3131", name: "Error operation", startTime: "2026-10-21T07:00:00.000Z", endTime: "2026-10-21T07:00:00.025Z", duration: "00:00:00.0250000", status: "Error", resourceIds: ["resource-activities-3131"], workflowInstanceIds: [], spanCount: 1 }
  ],
  droppedCount: 2
};

const resources = [
  { id: "resource-workbench-3131", serviceName: "elsa-workbench", serviceInstanceId: "workbench-instance", telemetrySdkLanguage: "dotnet", attributes: { environment: "test" }, lastSeen: "2026-10-21T09:00:01.250Z", status: "Active" },
  { id: "resource-activities-3131", serviceName: "elsa-activities", serviceInstanceId: "activities-instance", telemetrySdkLanguage: "dotnet", attributes: {}, lastSeen: "2026-10-21T09:00:01.250Z", status: "Stale" },
  { id: "resource-unknown-3131", serviceName: "   ", serviceInstanceId: null, telemetrySdkLanguage: null, attributes: {}, lastSeen: "2026-10-21T09:00:01.250Z", status: "Unknown" }
];

const resourcesResponse = { items: resources, droppedCount: 0 };
const detailResponse = {
  trace,
  spans: [
    { id: "span-root-3131", traceId, spanId: "span-root-3131", parentSpanId: null, resourceId: "resource-workbench-3131", name: "Workflow execution", kind: "Internal", startTime: "2026-10-21T09:00:00.000Z", endTime: "2026-10-21T09:00:01.250Z", status: "Unset", statusDescription: null, attributes: { "elsa.workflow.execution_id": "instance-workflow-3131" }, events: [], links: [] },
    { id: "span-activity-3131", traceId, spanId: "span-activity-3131", parentSpanId: "span-root-3131", resourceId: "resource-activities-3131", name: "WriteLine", kind: "Internal", startTime: "2026-10-21T09:00:00.250Z", endTime: "2026-10-21T09:00:00.500Z", status: "Ok", statusDescription: null, attributes: { "elsa.activity.type": "WriteLine" }, events: [], links: [] },
    { id: "span-unresolved-3131", traceId, spanId: "span-unresolved-3131", parentSpanId: "span-root-3131", resourceId: "resource-missing-3131", name: "Unresolved child", kind: "Internal", startTime: "2026-10-21T09:00:00.500Z", endTime: "2026-10-21T09:00:00.600Z", status: "Error", statusDescription: "synthetic error status", attributes: {}, events: [], links: [] }
  ],
  resources,
  logs: [{ id: "log-sentinel-id-3131", resourceId: "resource-workbench-3131", traceId, timestamp: "2026-10-21T09:00:00.750Z", severityText: "Error", body: "log-sentinel-3131", attributes: { marker: "log-sentinel-3131" } }]
};

const storageResponse = {
  traceCapacity: 100,
  spanCapacity: 1_000,
  metricPointCapacity: 0,
  logRecordCapacity: 0,
  resourceCount: 3,
  traceCount: 2,
  spanCount: 6,
  metricInstrumentCount: 0,
  metricPointCount: 0,
  logRecordCount: 0,
  droppedTraceCount: 2,
  droppedSpanCount: 0,
  droppedMetricPointCount: 0,
  droppedLogRecordCount: 0
};

const mountedPages = new Set<() => void>();
const globalRestorers = new Set<() => void>();

afterEach(() => {
  const errors: unknown[] = [];
  for (const unmount of [...mountedPages]) {
    try { unmount(); } catch (error) { errors.push(error); }
  }
  for (const restore of [...globalRestorers]) {
    try { restore(); } catch (error) { errors.push(error); }
  }
  mountedPages.clear();
  globalRestorers.clear();
  try { document.body.replaceChildren(); } catch (error) { errors.push(error); }
  if (errors.length > 0) throw new AggregateError(errors, "OpenTelemetry module test cleanup failed.");
});

describe("open telemetry module", () => {
  it("registers diagnostics navigation, route, and bottom panel contributions", () => {
    const api = stubApi(async () => []);

    register(api.api);

    expect(api.api.navigation.items).toMatchObject([
      { id: "open-telemetry", label: "OpenTelemetry", path: "/diagnostics/open-telemetry", parentId: "diagnostics", order: 880 }
    ]);
    expect(api.api.routes.items).toMatchObject([
      { id: "open-telemetry", label: "OpenTelemetry", path: "/diagnostics/open-telemetry" }
    ]);
    expect(api.api.panels.items).toMatchObject([
      { id: "open-telemetry", title: "OpenTelemetry", order: 1020 }
    ]);
  });

  it("orders waterfall rows by parent-child span hierarchy", () => {
    const spans = [
      normalizeSpan({ spanId: "grandchild", parentSpanId: "child", name: "Grandchild", startTime: "2026-06-22T00:00:00.030Z", endTime: "2026-06-22T00:00:00.035Z" }),
      normalizeSpan({ spanId: "root", name: "Root", startTime: "2026-06-22T00:00:00.000Z", endTime: "2026-06-22T00:00:00.050Z" }),
      normalizeSpan({ spanId: "child", parentSpanId: "root", name: "Child", startTime: "2026-06-22T00:00:00.010Z", endTime: "2026-06-22T00:00:00.030Z" })
    ];

    expect(buildWaterfallRows(spans).map(row => [row.span.spanId, row.depth])).toEqual([
      ["root", 0], ["child", 1], ["grandchild", 2]
    ]);
  });

  it("classifies backend failures without turning them into empty results", () => {
    expect(classifyOpenTelemetryError(new Error("Status code '403'"))).toMatchObject({ status: "unauthorized" });
    expect(classifyOpenTelemetryError(new Error("Status code '404'"))).toMatchObject({ status: "unavailable" });
    expect(classifyOpenTelemetryError(new Error("Status code '500'"))).toMatchObject({ status: "failed" });
  });
});

describe("US1 — find and inspect a workflow trace", () => {
  it("uses the backend search envelope and resolves multiple and unknown services through resources", async () => {
    const { container, requests } = renderPage(backendResponder);
    await flushPromises();

    const traceSearch = requests.filter(request => request.method === "POST" && request.url.endsWith("/diagnostics/opentelemetry/traces/search"));
    const resourceSearch = requests.filter(request => request.method === "POST" && request.url.endsWith("/diagnostics/opentelemetry/resources/search"));
    const detailReads = requests.filter(request => request.method === "GET" && request.url.endsWith(`/diagnostics/opentelemetry/traces/${traceId}`));

    expect(traceSearch).toHaveLength(1);
    expect(traceSearch[0].body).toMatchObject({ take: 100 });
    expect(resourceSearch).toHaveLength(1);
    expect(resourceSearch[0].body).toHaveProperty("take");
    expect(detailReads).toHaveLength(1);
    const traceRow = Array.from(container.querySelectorAll<HTMLElement>('[role="row"]')).find(row => row.textContent?.includes(traceId));
    expect(traceRow).toBeDefined();
    if (!traceRow) return;
    expect(traceRow.textContent).toContain("Workflow WriteLine");
    expect(traceRow.textContent).toContain("elsa-workbench");
    expect(traceRow.textContent).toContain("elsa-activities");
    expect(traceRow.textContent).toMatch(/unknown/i);
    expect(container.textContent).toContain("Unset");
    expect(container.textContent).toContain("Ok");
    expect(container.textContent).toContain("Error");
    expect(traceRow.textContent).toContain("1.25s");
    expect(traceRow.textContent).not.toMatch(/\b\d+ errors?\b/i);
    const unknownServiceRow = Array.from(container.querySelectorAll<HTMLElement>('[role="row"]')).find(row => row.textContent?.includes("trace-unset-3131"));
    expect(unknownServiceRow?.textContent).toContain("Unknown (1 resource)");

    const waterfall = container.querySelector('[aria-label="Trace waterfall"]');
    const activity = Array.from(waterfall?.querySelectorAll("button") ?? []).find(button => button.textContent?.includes("WriteLine"));
    expect(activity).toBeDefined();
    if (!activity) return;
    activity.click();
    await flushPromises();
    const activityDetails = container.querySelector('[aria-label="Span details"]');
    expect(activityDetails?.textContent).toContain("elsa-activities");
    expect(activityDetails?.textContent).toContain("Ok");
    expect(activityDetails?.textContent).toContain("250ms");

    const unresolved = Array.from(waterfall?.querySelectorAll("button") ?? []).find(button => button.textContent?.includes("Unresolved child"));
    expect(unresolved).toBeDefined();
    if (!unresolved) return;
    unresolved.click();
    await flushPromises();
    const unresolvedDetails = container.querySelector('[aria-label="Span details"]');
    expect(unresolvedDetails?.textContent).toMatch(/unknown/i);
    expect(requests.filter(request => request.method === "GET" && request.url.includes("/diagnostics/opentelemetry/traces/")).length).toBeLessThanOrEqual(1);
  });

  it("maps the visible filters to supported trace-search fields and the selected limit", async () => {
    const { container, requests } = renderPage(backendResponder);
    await flushPromises();

    const traceIdInput = container.querySelector<HTMLInputElement>('input[aria-label="Filter by trace ID"]');
    const serviceInput = container.querySelector<HTMLInputElement>('input[aria-label="Filter by service"]');
    const nameSearchInput = container.querySelector<HTMLInputElement>('input[aria-label="Filter by trace name or ID"]');
    const statusFilter = container.querySelector<HTMLSelectElement>('select[aria-label="Filter by trace status"]');
    const takeFilter = container.querySelector<HTMLSelectElement>('select[aria-label="Trace result limit"]');

    expect(traceIdInput).not.toBeNull();
    expect(serviceInput).not.toBeNull();
    expect(nameSearchInput).not.toBeNull();
    expect(statusFilter).not.toBeNull();
    expect(takeFilter).not.toBeNull();
    if (!traceIdInput || !serviceInput || !nameSearchInput || !statusFilter || !takeFilter) return;

    setValue(traceIdInput, "trace-filter-3131");
    setValue(serviceInput, "elsa-workbench");
    setValue(nameSearchInput, "WriteLine");
    setValue(statusFilter, "Error");
    setValue(takeFilter, "250");
    await flushPromises();

    const traceSearch = requests.filter(request => request.method === "POST" && request.url.endsWith("/diagnostics/opentelemetry/traces/search"));
    expect(traceSearch.at(-1)?.body).toMatchObject({
      traceId: "trace-filter-3131",
      serviceName: "elsa-workbench",
      status: "Error",
      search: "WriteLine",
      take: 250
    });
    expect(traceSearch.at(-1)?.body).not.toHaveProperty("operation");
    expect(traceSearch.at(-1)?.body).not.toHaveProperty("service");
    expect(container.querySelector('input[aria-label="Filter by trace name or ID"]')).not.toBeNull();
  });

  it.each([
    { label: "result limit", ariaLabel: "Trace result limit", value: "250" },
    { label: "trace name search", ariaLabel: "Filter by trace name or ID", value: "WriteLine" }
  ])("keeps selected detail and export usable when search returns the same trace after a $label change", async ({ ariaLabel, value }) => {
    const captured = captureTraceExport();

    const { container, requests } = renderPage(backendResponder);
    await flushPromises();
    const filter = container.querySelector<HTMLInputElement | HTMLSelectElement>(`[aria-label="${ariaLabel}"]`);
    expect(filter).not.toBeNull();
    if (!filter) return;

    setValue(filter, value);
    await flushPromises();

    const traceSearch = requests.filter(request => request.method === "POST" && request.url.endsWith("/diagnostics/opentelemetry/traces/search"));
    expect(traceSearch.at(-1)?.body).toMatchObject(ariaLabel === "Trace result limit" ? { take: 250 } : { search: "WriteLine" });
    const selectedRow = container.querySelector<HTMLButtonElement>(".otel-trace-row.selected");
    expect(selectedRow?.textContent).toContain(traceId);
    selectedRow?.click();
    await flushPromises();

    expect.soft(container.querySelector(".otel-trace-detail-header h3")?.textContent).toBe("Workflow WriteLine");
    expect.soft(container.querySelector('[aria-label="Trace waterfall"]')).not.toBeNull();
    const copy = Array.from(container.querySelectorAll("button")).find(button => button.textContent === "Copy JSON");
    const download = Array.from(container.querySelectorAll("button")).find(button => button.textContent === "Download");
    expect.soft(copy).toBeDefined();
    expect.soft(download).toBeDefined();

    if (copy && download) {
      copy.click();
      download.click();
      await flushPromises();
      expectSupportedTraceExport(captured.copiedJson);
      expect(captured.downloadedBlob).not.toBeNull();
      if (captured.downloadedBlob) expectSupportedTraceExport(await readBlob(captured.downloadedBlob));
    }
  });

  it("projects nested trace detail for display and trace/span export without the unsupported logs", async () => {
    const captured = captureTraceExport();

    const { container } = renderPage(backendResponder);
    await flushPromises();

    expect(container.textContent).not.toContain("log-sentinel-3131");
    const copy = Array.from(container.querySelectorAll("button")).find(button => button.textContent === "Copy JSON");
    const download = Array.from(container.querySelectorAll("button")).find(button => button.textContent === "Download");
    expect(copy).toBeDefined();
    expect(download).toBeDefined();
    if (!copy || !download) return;

    copy.click();
    await flushPromises();
    download.click();
    await flushPromises();

    expectSupportedTraceExport(captured.copiedJson);
    expect(captured.downloadedBlob).not.toBeNull();
    if (captured.downloadedBlob) expectSupportedTraceExport(await readBlob(captured.downloadedBlob));
    expect(container.textContent).not.toContain("log-sentinel-3131");
  });
});

describe("US2 — understand stored diagnostic data", () => {
  it("labels host resource and span counts as stored data without presenting a trace total", async () => {
    const { container, requests } = renderPage(backendResponder);
    await flushPromises();
    findTab(container, "Overview")?.click();
    await flushPromises();

    const text = container.textContent ?? "";
    expectStoredCount(container, /resources?/, "3");
    expectStoredCount(container, /spans?/, "6");
    expect(Array.from(container.querySelectorAll(".otel-signal-card h4"), heading => heading.textContent).some(title => /traces?/i.test(title ?? ""))).toBe(false);
    expect(container.querySelector(".otel-header-summary")?.textContent ?? "").not.toMatch(/\btraces?\b/i);
    expect(text).not.toMatch(/trace total|total traces|signal health|\bhealthy\b/i);
    expect(text).toContain("elsa-workbench");
    expect(text).toContain("elsa-activities");
    expectResourceStatus(container, "resource-workbench-3131", "elsa-workbench", "Active");
    expectResourceStatus(container, "resource-activities-3131", "elsa-activities", "Stale");
    expectResourceStatus(container, "resource-unknown-3131", "Unknown", "Unknown");
    expect(requests.some(request => request.method === "GET" && request.url.endsWith("/diagnostics/opentelemetry/storage"))).toBe(true);
    expect(requests.some(request => request.method === "POST" && request.url.endsWith("/diagnostics/opentelemetry/resources/search"))).toBe(true);
  });

  it.each([
    ["resource", "forbidden"],
    ["storage", "forbidden"],
    ["resource", "missing"],
    ["storage", "missing"]
  ] as const)("keeps a %s %s read distinct from the successful overview read", async (failedRead, failure) => {
    const responder = overviewResponder(failedRead === "resource" ? failure : "success", failedRead === "storage" ? failure : "success");
    const { container } = renderPage(responder);
    await flushPromises();
    findTab(container, "Overview")?.click();
    await flushPromises();

    if (failedRead === "resource") {
      expectOverviewReadState(container, "resource", failure === "forbidden" ? /permission|denied|forbidden/i : /unavailable/i);
      expectStoredCount(container, /resources?/, "3");
      expectStoredCount(container, /spans?/, "6");
    } else {
      expectOverviewReadState(container, "storage", failure === "forbidden" ? /permission|denied|forbidden/i : /unavailable/i);
      expect(container.textContent).toContain("elsa-workbench");
      expect(container.textContent).toContain("elsa-activities");
      expectNoFabricatedStoredZero(container);
    }
  });

  it.each(["resource", "storage"] as const)("keeps a pending %s read visibly loading while its counterpart stays available", async pendingRead => {
    const responder = overviewResponder(pendingRead === "resource" ? "pending" : "success", pendingRead === "storage" ? "pending" : "success");
    const { container } = renderPage(responder);
    await flushPromises();
    findTab(container, "Overview")?.click();
    await flushPromises();

    expectOverviewReadState(container, pendingRead, /loading/i);
    if (pendingRead === "resource") {
      expectStoredCount(container, /resources?/, "3");
      expectStoredCount(container, /spans?/, "6");
    } else {
      expect(container.textContent).toContain("elsa-workbench");
      expect(container.textContent).toContain("elsa-activities");
      expectNoFabricatedStoredZero(container);
    }
  });

  it("keeps a successful empty resource list distinct from failed and loading reads", async () => {
    const emptyStorageResponse = { ...storageResponse, resourceCount: 0 };
    const { container } = renderPage(overviewResponder("empty", "success", emptyStorageResponse));
    await flushPromises();
    findTab(container, "Overview")?.click();
    await flushPromises();

    expect(container.textContent).toMatch(/no (OpenTelemetry )?resources?|no OpenTelemetry sources/i);
    expectStoredCount(container, /resources?/, "0");
    expectStoredCount(container, /spans?/, "6");
    expect(container.textContent).not.toMatch(/permission denied|\bunavailable\b|\bloading\b/i);
  });

  it("does not turn absent or malformed stored counts into zero", async () => {
    const malformedStorageResponse = { ...storageResponse, resourceCount: undefined, spanCount: "invalid" as unknown as number };
    const { container } = renderPage(overviewResponder("success", "success", malformedStorageResponse));
    await flushPromises();
    findTab(container, "Overview")?.click();
    await flushPromises();

    expectStoredCount(container, /resources?/, "Unavailable");
    expectStoredCount(container, /spans?/, "Unavailable");
  });
});

describe("US3 — distinguish empty, denied, and unavailable states", () => {
  it("keeps a loading search distinct from a successful empty envelope", async () => {
    const pending = renderPage(() => new Promise(() => {}));
    expect(pending.container.textContent).toMatch(/Loading (OpenTelemetry diagnostics|trace summaries)/);
    pending.unmount();

    const empty = renderPage(async request => {
      if (request.method === "POST" && request.url.endsWith("/diagnostics/opentelemetry/traces/search")) return { items: [], droppedCount: 0 };
      if (request.method === "POST" && request.url.endsWith("/diagnostics/opentelemetry/resources/search")) return { items: [], droppedCount: 0 };
      if (request.method === "GET" && request.url.endsWith("/diagnostics/opentelemetry/storage")) return storageResponse;
      throw new Error("Status code '404'");
    });
    await flushPromises();

    expect(empty.container.textContent).toContain("No traces match the current filters.");
    expect(empty.container.textContent).not.toMatch(/failed|permission denied|unavailable/i);
  });

  it.each([
    ["permission", "Status code '401'", /Permission denied while loading OpenTelemetry diagnostics/],
    ["permission", "Status code '403'", /Permission denied while loading OpenTelemetry diagnostics/],
    ["unavailable", "Status code '404'", /OpenTelemetry diagnostics are unavailable/],
    ["failure", "Status code '500'", /OpenTelemetry diagnostics failed/]
  ])("renders a distinct trace %s state for %s", async (_state, message, expected) => {
    const { container } = renderPage(async () => { throw new Error(message); });
    await flushPromises();

    expect(container.textContent).toMatch(expected);
    expect(container.textContent).not.toContain("No traces match the current filters.");
  });

  it("reports a missing selected trace without marking the diagnostics surface unavailable", async () => {
    const { container, requests } = renderPage(async request => {
      if (request.method === "POST" && request.url.endsWith("/diagnostics/opentelemetry/traces/search")) return { items: [trace], droppedCount: 0 };
      if (request.method === "POST" && request.url.endsWith("/diagnostics/opentelemetry/resources/search")) return resourcesResponse;
      if (request.method === "GET" && request.url.endsWith("/diagnostics/opentelemetry/storage")) return storageResponse;
      if (request.method === "GET" && request.url.endsWith(`/diagnostics/opentelemetry/traces/${traceId}`)) throw new Error("Status code '404'");
      throw new Error("Status code '404'");
    });
    await flushPromises();

    expect(requests.some(request => request.method === "GET" && request.url.endsWith(`/diagnostics/opentelemetry/traces/${traceId}`))).toBe(true);
    expect(container.textContent).toMatch(/trace.*(missing|no longer available|not found)|(missing|no longer available|not found).*trace/i);
    expect(container.textContent).toContain("Workflow WriteLine");
    expect(container.textContent).not.toMatch(/OpenTelemetry diagnostics are unavailable/i);
  });

  it.each([
    ["permission denied", "Status code '403'", /Permission denied while loading this trace/i],
    ["failure", "Status code '500'", /Trace details failed to load/i]
  ])("keeps selected trace detail %s scoped to the detail pane", async (_state, message, expected) => {
    const { container } = renderPage(async request => {
      if (request.method === "POST" && request.url.endsWith("/diagnostics/opentelemetry/traces/search")) return { items: [trace], droppedCount: 0 };
      if (request.method === "POST" && request.url.endsWith("/diagnostics/opentelemetry/resources/search")) return resourcesResponse;
      if (request.method === "GET" && request.url.endsWith("/diagnostics/opentelemetry/storage")) return storageResponse;
      if (request.method === "GET" && request.url.endsWith(`/diagnostics/opentelemetry/traces/${traceId}`)) throw new Error(message);
      throw new Error("Status code '404'");
    });
    await flushPromises();

    expect(container.textContent).toMatch(expected);
    expect(container.textContent).toContain("Workflow WriteLine");
    expect(container.textContent).not.toMatch(/OpenTelemetry diagnostics (are unavailable|failed)/i);
  });

  it("keeps Metrics and OpenTelemetry Logs explicitly unsupported and preserves the Diagnostics handoff", async () => {
    const { container } = renderPage(backendResponder);
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
  });
});

function backendResponder(request: StudioRequest): Promise<unknown> {
  if (request.method === "POST" && request.url.endsWith("/diagnostics/opentelemetry/traces/search")) return Promise.resolve(tracesResponse);
  if (request.method === "POST" && request.url.endsWith("/diagnostics/opentelemetry/resources/search")) return Promise.resolve(resourcesResponse);
  if (request.method === "GET" && request.url.endsWith("/diagnostics/opentelemetry/storage")) return Promise.resolve(storageResponse);
  if (request.method === "GET" && request.url.endsWith(`/diagnostics/opentelemetry/traces/${traceId}`)) return Promise.resolve(detailResponse);
  return Promise.reject(new Error("Status code '404'"));
}

function overviewResponder(resourceState: OverviewReadState, storageState: OverviewReadState, storage = storageResponse): Responder {
  return request => {
    if (request.method === "POST" && request.url.endsWith("/diagnostics/opentelemetry/traces/search")) return Promise.resolve(tracesResponse);
    if (request.method === "POST" && request.url.endsWith("/diagnostics/opentelemetry/resources/search")) {
      return readResponse(resourceState, resourcesResponse, { items: [], droppedCount: 0 });
    }
    if (request.method === "GET" && request.url.endsWith("/diagnostics/opentelemetry/storage")) {
      return readResponse(storageState, storage, storage);
    }
    if (request.method === "GET" && request.url.endsWith(`/diagnostics/opentelemetry/traces/${traceId}`)) return Promise.resolve(detailResponse);
    return Promise.reject(new Error("Status code '404'"));
  };
}

function readResponse(state: OverviewReadState, success: unknown, empty: unknown): Promise<unknown> {
  if (state === "pending") return new Promise(() => {});
  if (state === "forbidden") return Promise.reject(new Error("Status code '403'"));
  if (state === "missing") return Promise.reject(new Error("Status code '404'"));
  return Promise.resolve(state === "empty" ? empty : success);
}

function renderPage(respond: Responder) {
  const { api, requests } = stubApi(respond);
  register(api);
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
  return { container, requests, unmount };
}

function stubApi(respond: Responder) {
  const requests: StudioRequest[] = [];
  const request = (value: StudioRequest) => {
    requests.push(value);
    return respond(value);
  };
  const api = {
    backend: {
      baseUrl: "https://backend.example/",
      http: {
        getJson: (url: string) => request({ method: "GET", url }),
        postJson: (url: string, body: unknown) => request({ method: "POST", url, body })
      }
    },
    navigation: registry(),
    routes: registry(),
    panels: registry()
  } as unknown as ElsaStudioModuleApi & {
    navigation: ReturnType<typeof registry>;
    routes: ReturnType<typeof registry>;
    panels: ReturnType<typeof registry>;
  };
  return { api, requests };
}

function findTab(container: HTMLElement, label: string) {
  return Array.from(container.querySelectorAll<HTMLButtonElement>('[role="tab"]')).find(tab => tab.textContent?.trim() === label) ?? null;
}

function expectStoredCount(container: HTMLElement, label: RegExp, value: string) {
  const card = Array.from(container.querySelectorAll<HTMLElement>(".otel-signal-card"))
    .find(candidate => label.test((candidate.querySelector("h4")?.textContent ?? "").toLowerCase()));
  expect(card).toBeDefined();
  expect(card?.textContent).toMatch(/stored/i);
  expect(card?.querySelector("strong")?.textContent).toBe(value);
}

function expectResourceStatus(container: HTMLElement, id: string, service: string, status: string) {
  const row = container.querySelector<HTMLElement>(`[data-resource-id="${id}"]`);
  expect(row).toBeDefined();
  expect(row?.textContent).toContain(service);
  expect(row?.textContent).toContain(`Resource status: ${status}`);
  expect(row?.textContent).toContain(`Resource ID: ${id}`);
}

function expectOverviewReadState(container: HTMLElement, read: "resource" | "storage", state: RegExp) {
  const headingPattern = read === "resource" ? /^sources?$/i : /^(?:storage|stored data)$/i;
  const heading = Array.from(container.querySelectorAll<HTMLElement>("section > h2, section > h3"))
    .find(candidate => headingPattern.test(candidate.textContent?.trim() ?? ""));
  expect(heading).toBeDefined();
  expect(heading?.closest("section")?.textContent).toMatch(state);
}

function expectNoFabricatedStoredZero(container: HTMLElement) {
  const storedCountCards = Array.from(container.querySelectorAll<HTMLElement>(".otel-signal-card"))
    .filter(card => /stored/i.test(card.textContent ?? "") && /resources?|spans?/i.test(card.querySelector("h4")?.textContent ?? ""));
  expect(storedCountCards.every(card => card.querySelector("strong")?.textContent !== "0")).toBe(true);
  expect(container.textContent).not.toMatch(/\b0\s*(?:stored\s+)?(?:resources?|spans?)\b/i);
}

function setValue(element: HTMLInputElement | HTMLSelectElement, value: string) {
  const prototype = element instanceof HTMLSelectElement ? HTMLSelectElement.prototype : HTMLInputElement.prototype;
  Object.getOwnPropertyDescriptor(prototype, "value")?.set?.call(element, value);
  element.dispatchEvent(new Event(element instanceof HTMLSelectElement ? "change" : "input", { bubbles: true }));
}

function replaceGlobal(target: object, key: string, value: unknown) {
  const original = Object.getOwnPropertyDescriptor(target, key);
  Object.defineProperty(target, key, { configurable: true, writable: true, value });
  globalRestorers.add(() => {
    if (original) Object.defineProperty(target, key, original);
    else Reflect.deleteProperty(target, key);
  });
}

function captureTraceExport() {
  let copiedJson = "";
  let downloadedBlob: Blob | null = null;
  replaceGlobal(navigator, "clipboard", { writeText: async (text: string) => { copiedJson = text; } });
  replaceGlobal(URL, "createObjectURL", (blob: Blob) => { downloadedBlob = blob; return "blob:trace-3131"; });
  replaceGlobal(URL, "revokeObjectURL", () => {});
  replaceGlobal(HTMLAnchorElement.prototype, "click", function(this: HTMLAnchorElement) {
    if (this.download.endsWith(".json")) this.dataset.downloadCaptured = "true";
  });
  return {
    get copiedJson() { return copiedJson; },
    get downloadedBlob() { return downloadedBlob; }
  };
}

function expectSupportedTraceExport(json: string) {
  expect(json).toContain(traceId);
  expect(json).toContain("span-root-3131");
  expect(json).toContain("resource-workbench-3131");
  expect(json).not.toContain("log-sentinel-3131");
  expect(json).not.toContain("logs");
}

async function flushPromises() {
  for (let attempt = 0; attempt < 5; attempt++) await new Promise(resolve => setTimeout(resolve, 0));
}

function readBlob(blob: Blob) {
  return new Promise<string>((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result ?? ""));
    reader.onerror = () => reject(reader.error ?? new Error("Unable to read exported trace."));
    reader.readAsText(blob);
  });
}

function registry() {
  const items: unknown[] = [];
  return { items, add: (item: unknown) => items.push(item), list: () => items };
}
