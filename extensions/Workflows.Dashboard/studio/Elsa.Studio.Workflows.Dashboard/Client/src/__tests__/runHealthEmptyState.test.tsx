import React from "react";
import { describe, expect, it } from "vitest";
import { WorkflowRunHealthWidget } from "../WorkflowRunHealthWidget";
import { render, runSnapshot } from "./testSupport";

describe("Workflow Run Health empty state", () => {
  it("keeps zero-start buckets empty while showing the all-time running count", () => {
    const snapshot = runSnapshot({
      startedCount: 0,
      succeededCount: 0,
      failedCount: 0,
      incidentBearingRunCount: 0,
      incidentCount: 0,
      runningCount: 3,
      failurePercentage: 0,
      incidentBearingPercentage: 0,
      buckets: [
        { from: "2026-10-24T22:00:00Z", to: "2026-10-25T22:00:00Z", startedCount: 0, failedCount: 0, incidentBearingRunCount: 0 },
        { from: "2026-10-25T22:00:00Z", to: "2026-10-26T22:00:00Z", startedCount: 0, failedCount: 0, incidentBearingRunCount: 0 },
      ],
      highestFailureDefinitions: [],
    });

    const view = render(
      <WorkflowRunHealthWidget
        settings={{ range: "7d", includeTestRuns: false }}
        size="wide"
        snapshot={snapshot}
      />,
    );

    expect(view.querySelector("[role=img]")).toBeNull();
    expect(view.querySelectorAll(".workflow-run-health__bar")).toHaveLength(0);
    expect(view.querySelector('[data-metric="Running now"]')?.textContent).toContain("3");
    expect(view.textContent).toContain("No workflow starts in this window.");
    expect(view.textContent).toContain("across all start times");
    expect(view.textContent).toContain("Europe/Amsterdam");
    expect(view.textContent).toContain("Test runs included");
  });
});
