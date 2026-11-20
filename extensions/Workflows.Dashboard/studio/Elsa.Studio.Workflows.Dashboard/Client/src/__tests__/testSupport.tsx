import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, vi } from "vitest";
import type { ElsaStudioModuleApi, StudioDashboardWidgetContribution } from "@elsa-workflows/studio-sdk";
import type { PortfolioSnapshot, RunHealthSnapshot } from "../workflowDashboardApi";

const mounted: Array<{ root: Root; container: HTMLElement }> = [];
afterEach(() => {
  for (const { root, container } of mounted.splice(0)) {
    flushSync(() => root.unmount());
    container.remove();
  }
});

export function render(node: React.ReactNode) {
  const container = document.createElement("div");
  document.body.append(container);
  const root = createRoot(container);
  flushSync(() => root.render(node));
  mounted.push({ root, container });
  return container;
}

export function apiFixture(response: unknown = {}) {
  const getJson = vi.fn(async (_url: string, _options?: { signal?: AbortSignal }) => response);
  const items: StudioDashboardWidgetContribution[] = [];
  const registry = { add: (item: StudioDashboardWidgetContribution) => items.push(item) };
  const api = { backend: { http: { getJson } }, dashboardWidgets: registry, homeWidgets: registry } as unknown as ElsaStudioModuleApi;
  return { api, getJson, items };
}

export function portfolioSnapshot(overrides: Partial<PortfolioSnapshot> = {}): PortfolioSnapshot {
  return { status: "ready", generatedAt: "2026-10-26T00:00:00Z", activeDefinitionCount: 42, publishedDefinitionCount: 30, unpublishedDraftCount: 14, invalidDraftCount: 3, ...overrides };
}

export function runSnapshot(overrides: Partial<RunHealthSnapshot> = {}): RunHealthSnapshot {
  return {
    status: "ready", generatedAt: "2026-10-26T00:00:00Z",
    from: "2026-10-24T22:00:00Z", to: "2026-10-25T23:00:00Z", timeZone: "Europe/Amsterdam", bucket: "day", includeTestRuns: true,
    startedCount: 12, succeededCount: 8, failedCount: 2, cancelledCount: 1, incompleteCount: 1,
    incidentBearingRunCount: 3, incidentCount: 4, runningCount: 1, failurePercentage: 16.67, incidentBearingPercentage: 25,
    buckets: [{ from: "2026-10-24T22:00:00Z", to: "2026-10-25T23:00:00Z", startedCount: 12, failedCount: 2, incidentBearingRunCount: 3 }],
    highestFailureDefinitions: [{ definitionId: "orders", failedCount: 2 }], ...overrides
  };
}
