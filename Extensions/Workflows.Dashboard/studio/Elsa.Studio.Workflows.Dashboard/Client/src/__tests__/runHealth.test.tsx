import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { describe, expect, it, vi } from "vitest";
import { WorkflowRunHealthWidget } from "../WorkflowRunHealthWidget";
import { loadRunHealth, type RunHealthRange, type RunHealthSnapshot } from "../workflowDashboardApi";
import { register } from "../module";

describe("Workflow Run Health widget", () => {
  it("registers versioned range settings and the required refresh/layout contract", () => {
    const items: any[] = [];
    register({ dashboardWidgets: { add: (item: any) => items.push(item) } } as any);
    expect(items[1]).toMatchObject({ defaultSize: "wide", supportedSizes: ["medium", "wide", "full"], minimumRefreshIntervalMs: 60_000, cacheLifetimeMs: 60_000 });
    expect(items[1].settings).toMatchObject({ schemaVersion: 1, defaults: { range: "7d", includeTestRuns: false } });
  });
  it.each([["24h", "hour"], ["7d", "day"], ["30d", "day"]] as Array<[RunHealthRange, string]>)("loads %s with the expected bucket", async (range, bucket) => {
    const getJson = vi.fn(async (_url: string) => ({ status: "ready" }));
    await loadRunHealth({ backend: { http: { getJson } } } as any, { range, includeTestRuns: true }, new AbortController().signal, new Date("2026-07-13T12:00:00Z"));
    const url = getJson.mock.calls[0][0];
    expect(url).toContain(`bucket=${bucket}`);
    expect(url).toContain("includeTestRuns=true");
  });

  it("accepts the exact Foundation wire bucket and renders DST-shaped labels, failed share, and top failures", () => {
    const snapshot: RunHealthSnapshot = { status: "ready", generatedAt: "", from: "2026-10-24T22:00:00Z", to: "2026-10-26T23:00:00Z", timeZone: "Europe/Amsterdam", bucket: "day", includeTestRuns: true, startedCount: 12, succeededCount: 8, failedCount: 2, cancelledCount: 1, incompleteCount: 1, incidentBearingRunCount: 3, incidentCount: 4, runningCount: 1, failurePercentage: 16.67, incidentBearingPercentage: 25, buckets: [{ from: "2026-10-25T22:00:00Z", to: "2026-10-26T23:00:00Z", startedCount: 12, failedCount: 3, incidentBearingRunCount: 4 }], highestFailureDefinitions: [{ definitionId: "orders", name: "Orders", failedCount: 2 }] };
    const view = render(<WorkflowRunHealthWidget settings={{ range: "7d", includeTestRuns: true }} size="wide" snapshot={snapshot} />);
    expect(view.textContent).toContain("Includes transient test runs");
    expect(view.textContent).toContain("Orders");
    expect(view.querySelector('[role="img"]')?.getAttribute("aria-label")).toContain("2026-10-25T22:00:00Z to 2026-10-26T23:00:00Z");
    expect((view.querySelector('[role="img"] > span') as HTMLElement).style.height).toBe("25%");
  });

  it("renders a safe unavailable state", () => {
    const view = render(<WorkflowRunHealthWidget settings={{ range: "7d", includeTestRuns: false }} size="wide" snapshot={{ status: "unavailable", generatedAt: "", errorCode: "PROVIDER_UNSUPPORTED" }} />);
    expect(view.textContent).toBe("Run statistics are unavailable (PROVIDER_UNSUPPORTED).");
  });
});

function render(node: React.ReactNode) { const container = document.createElement("div"); const root = createRoot(container); flushSync(() => root.render(node)); return container; }
