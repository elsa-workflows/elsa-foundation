import { describe, expect, it } from "vitest";
import {
  buildWaterfallRows,
  classifyOpenTelemetryError,
  createTracesPath,
  normalizeSpan,
  normalizeTraceDetail,
  normalizeTraceSummary,
  register
} from "../module";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";

describe("open telemetry module", () => {
  it("registers diagnostics navigation and route contributions", () => {
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
});

function stubApi() {
  return {
    backend: { baseUrl: "https://backend.example/", http: { getJson: async () => [] } },
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
