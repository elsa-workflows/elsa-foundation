import React from "react";
import { describe, expect, it } from "vitest";
import { WorkflowPortfolioWidget } from "../WorkflowPortfolioWidget";
import { loadPortfolio } from "../workflowDashboardApi";
import { apiFixture, portfolioSnapshot, render } from "./testSupport";

describe("Workflow Portfolio widget", () => {
  it("loads the independent definitions resource with cancellation", async () => {
    const snapshot = portfolioSnapshot();
    const { api, getJson } = apiFixture(snapshot);
    const signal = new AbortController().signal;
    await expect(loadPortfolio(api, signal)).resolves.toEqual(snapshot);
    expect(getJson).toHaveBeenCalledWith("/_elsa/workflows/dashboard/definitions", { signal });
  });

  it("renders independent overlapping counters instead of an invented distribution", () => {
    const view = render(<WorkflowPortfolioWidget settings={undefined} size="small" snapshot={portfolioSnapshot()} />);
    expect(view.textContent).toMatch(/overlap/i);
    expect(view.textContent).toContain("Invalid drafts");
    expect(view.textContent).toContain("42");
  });

  it("renders an unavailable state rather than zero definitions", () => {
    const view = render(<WorkflowPortfolioWidget settings={undefined} size="small" snapshot={{ status: "unavailable", generatedAt: "", errorCode: "PROVIDER_UNSUPPORTED" }} />);
    expect(view.textContent).toMatch(/unavailable/i);
    expect(view.querySelector("[data-metric]")).toBeNull();
  });
});
