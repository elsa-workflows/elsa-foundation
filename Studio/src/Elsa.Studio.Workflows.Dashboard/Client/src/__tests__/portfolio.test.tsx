import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { describe, expect, it, vi } from "vitest";
import { WorkflowPortfolioWidget } from "../WorkflowPortfolioWidget";
import { loadPortfolio, type WorkflowPortfolioSnapshot } from "../workflowDashboardApi";

describe("Workflow Portfolio widget", () => {
  it("loads the independent definitions resource", async () => {
    const snapshot: WorkflowPortfolioSnapshot = { status: "ready", generatedAt: "2026-07-13T10:00:00Z", activeDefinitionCount: 42, publishedDefinitionCount: 30, unpublishedDraftCount: 14, invalidDraftCount: 3 };
    const getJson = vi.fn(async (_url: string) => snapshot);
    await expect(loadPortfolio({ backend: { http: { getJson } } } as any, new AbortController().signal)).resolves.toEqual(snapshot);
    expect(getJson.mock.calls[0][0]).toBe("/_elsa/workflows/dashboard/definitions");
  });

  it("renders overlapping counters", () => {
    const view = render(<WorkflowPortfolioWidget settings={undefined} snapshot={{ status: "ready", generatedAt: "", activeDefinitionCount: 10, publishedDefinitionCount: 8, unpublishedDraftCount: 4, invalidDraftCount: 2 }} />);
    expect(view.textContent).toContain("counters overlap");
    expect(view.textContent).toContain("Invalid drafts");
  });

  it("renders a safe unavailable state", () => {
    const view = render(<WorkflowPortfolioWidget settings={undefined} snapshot={{ status: "unavailable", generatedAt: "", errorCode: "PROVIDER_UNSUPPORTED" }} />);
    expect(view.textContent).toBe("Portfolio statistics are unavailable (PROVIDER_UNSUPPORTED).");
  });
});

function render(node: React.ReactNode) { const container = document.createElement("div"); const root = createRoot(container); flushSync(() => root.render(node)); return container; }
