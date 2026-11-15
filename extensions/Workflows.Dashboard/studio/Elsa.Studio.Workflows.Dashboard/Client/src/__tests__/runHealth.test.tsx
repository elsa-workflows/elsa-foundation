import React from "react";
import { describe, expect, it } from "vitest";
import { WorkflowRunHealthWidget } from "../WorkflowRunHealthWidget";
import { loadRunHealth, normalizeRunHealthSettings, type RunHealthRange } from "../workflowDashboardApi";
import { register } from "../module";
import { apiFixture, render, runSnapshot } from "./testSupport";

describe("Workflow Run Health widget", () => {
  it("keeps native failed-run links bound to the selected backend",()=>{
    window.history.replaceState({},"","/?backend=runtime");
    const view=render(<WorkflowRunHealthWidget snapshot={runSnapshot()} settings={{range:"24h",includeTestRuns:false}} size="full" />);
    try {expect(new URL(view.querySelector<HTMLAnchorElement>("a")!.href).searchParams.get("backend")).toBe("runtime");}
    finally {window.history.replaceState({},"","/");}
  });
  it("registers permission-gated Home widgets with versioned settings and refresh contracts", () => {
    const { api, items } = apiFixture();
    register(api);
    expect(items.map(item => item.id)).toEqual(["workflows.portfolio", "workflows.run-health"]);
    for (const item of items) expect(item).toMatchObject({ requiresFeature: "workflows", permissions: ["workflows/dashboard:read"] });
    expect(items[1]).toMatchObject({ defaultSize: "full", supportedSizes: ["medium", "wide", "full"], minimumRefreshIntervalMs: 60_000, cacheLifetimeMs: 60_000 });
    expect(items[1].settings).toMatchObject({ schemaVersion: 1, defaults: { range: "24h", includeTestRuns: false } });
    expect(items[1].settings?.validate({ range: "bogus", includeTestRuns: false })).toBeNull();
  });

  it.each([["24h", "hour", 24], ["7d", "day", 168], ["30d", "day", 720]] as Array<[RunHealthRange, string, number]>)("loads %s with exact half-open boundaries and the expected bucket", async (range, bucket, hours) => {
    const { api, getJson } = apiFixture(runSnapshot({ highestFailureDefinitions: [] }));
    const signal = new AbortController().signal;
    await loadRunHealth(api, { range, includeTestRuns: true }, signal, new Date("2026-07-13T12:00:00Z"));
    const url = new URL(getJson.mock.calls[0][0], "https://backend.example");
    expect(url.searchParams.get("bucket")).toBe(bucket);
    expect(url.searchParams.get("includeTestRuns")).toBe("true");
    expect(url.searchParams.get("to")).toBe("2026-07-13T12:00:00.000Z");
    expect(Date.parse(url.searchParams.get("to")!) - Date.parse(url.searchParams.get("from")!)).toBe(hours * 3_600_000);
    expect(getJson.mock.calls[0][1]?.signal).toBe(signal);
  });

  it("uses valid defaults for Home metadata-only settings and does not coerce test inclusion", () => {
    expect(normalizeRunHealthSettings({ keepVisible: true })).toEqual({ range: "24h", includeTestRuns: false });
    expect(normalizeRunHealthSettings({ range: "7d", includeTestRuns: "true" })).toEqual({ range: "7d", includeTestRuns: false });
  });

  it("enriches at most five failure identities and preserves readable aggregates when Design is denied", async () => {
    const snapshot = runSnapshot({ highestFailureDefinitions: Array.from({ length: 7 }, (_, index) => ({ definitionId: `definition-${index}`, failedCount: Math.max(1, 5 - index) })) });
    const { api, getJson } = apiFixture(snapshot);
    getJson.mockImplementation(async path => {
      if (path.startsWith("/_elsa/")) return snapshot;
      if (path.endsWith("definition-0")) return { definition: { name: "Orders" } };
      throw Object.assign(new Error("Forbidden"), { status: 403 });
    });
    const result = await loadRunHealth(api, undefined, new AbortController().signal);
    expect(result.status).toBe("ready");
    if (result.status !== "ready") throw new Error("Expected a ready aggregate");
    expect(result.highestFailureDefinitions[0]).toEqual({ definitionId: "definition-0", failedCount: 5, displayName: "Orders" });
    expect(result.highestFailureDefinitions[1]).toEqual({ definitionId: "definition-1", failedCount: 4 });
    expect(getJson).toHaveBeenCalledTimes(6);
    expect(snapshot.highestFailureDefinitions[0]).not.toHaveProperty("displayName");
  });

  it("distinguishes a missing dashboard module from permission and transport errors", async () => {
    const { api, getJson } = apiFixture();
    getJson.mockRejectedValue(Object.assign(new Error("Missing"), { status: 404 }));
    await expect(loadRunHealth(api, undefined, new AbortController().signal)).resolves.toMatchObject({ status: "unavailable", errorCode: "DASHBOARD_NOT_INSTALLED" });
    getJson.mockRejectedValue(Object.assign(new Error("Forbidden"), { status: 403 }));
    await expect(loadRunHealth(api, undefined, new AbortController().signal)).rejects.toMatchObject({ status: 403 });
  });

  it("accepts Foundation wire buckets without names and retains exact DST interval evidence", () => {
    const snapshot = runSnapshot();
    const view = render(<WorkflowRunHealthWidget settings={{ range: "7d", includeTestRuns: false }} size="wide" snapshot={snapshot} />);
    expect(view.textContent).toContain("orders");
    expect(view.textContent).toContain("Europe/Amsterdam");
    expect(view.querySelector(".workflow-run-health__scope")?.textContent).toContain("GMT+2");
    expect(view.querySelector(".workflow-run-health__scope")?.textContent).toContain("GMT+1");
    // The returned interval/test scope is authoritative even when settings changed during a refresh.
    expect(view.textContent).toMatch(/test runs/i);
    expect(view.querySelector('[role="img"]')?.getAttribute("aria-label")).toContain("12");
    expect(view.querySelector("table")?.textContent).toContain("12");
  });

  it("keeps empty buckets at zero and distinguishes failed-run links from time-window totals", () => {
    const snapshot = runSnapshot({
      includeTestRuns: false,
      buckets: [
        { from: "2026-10-25T00:00:00Z", to: "2026-10-25T01:00:00Z", startedCount: 0, failedCount: 0, incidentBearingRunCount: 0 },
        { from: "2026-10-25T01:00:00Z", to: "2026-10-25T02:00:00Z", startedCount: 12, failedCount: 2, incidentBearingRunCount: 3 }
      ]
    });
    const view = render(<WorkflowRunHealthWidget settings={{ range: "24h", includeTestRuns: false }} size="medium" snapshot={snapshot} />);
    const bars = view.querySelectorAll<HTMLElement>(".workflow-run-health__bar");
    expect(bars[0].style.getPropertyValue("--workflow-dashboard-start-height")).toBe("0%");
    expect(bars[1].style.getPropertyValue("--workflow-dashboard-start-height")).toBe("100%");
    const link = view.querySelector<HTMLAnchorElement>(".workflow-run-health__definitions a")!;
    const query = new URL(link.href).searchParams;
    expect(query.get("status")).toBe("Faulted");
    expect(query.get("definitionId")).toBe("orders");
    expect(query.get("runKind")).toBe("PublishedRun");
    expect(query.has("from")).toBe(false);
    expect(link.getAttribute("aria-label")).toMatch(/all failed runs/i);
  });

  it("does not swallow cancellation while loading optional definition names", async () => {
    const controller = new AbortController();
    const { api, getJson } = apiFixture();
    getJson.mockImplementation(async path => {
      if (path.startsWith("/_elsa/")) return runSnapshot();
      controller.abort();
      throw new DOMException("Aborted", "AbortError");
    });
    await expect(loadRunHealth(api, undefined, controller.signal)).rejects.toMatchObject({ name: "AbortError" });
  });

  it("shows incomplete bucket data honestly without a blank chart", () => {
    const view = render(<WorkflowRunHealthWidget settings={{ range: "24h", includeTestRuns: false }} size="full" snapshot={runSnapshot({ buckets: [] })} />);
    expect(view.querySelector("[role=img]")).toBeNull();
    expect(view.textContent).toContain("time-bucket details are unavailable");
    expect(view.querySelector('[data-metric="Started"]')?.textContent).toContain("12");
  });

  it("renders unavailable statistics without zero metrics", () => {
    const view = render(<WorkflowRunHealthWidget settings={{ range: "7d", includeTestRuns: false }} size="wide" snapshot={{ status: "unavailable", generatedAt: "", errorCode: "PROVIDER_UNSUPPORTED" }} />);
    expect(view.textContent).toMatch(/unavailable/i);
    expect(view.querySelector("[data-metric]")).toBeNull();
  });
});
