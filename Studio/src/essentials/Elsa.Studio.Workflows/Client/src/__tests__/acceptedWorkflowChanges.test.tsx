import React from "react";
import { createRoot } from "react-dom/client";
import { flushSync } from "react-dom";
import { describe, expect, it } from "vitest";
import { AcceptedWorkflowChanges } from "../workflow-editor/run-history/AcceptedWorkflowChanges";
import type { ActivityExecutionStateSummary } from "../workflowTypes";

describe("accepted workflow changes", () => {
  it("retains an accepted suspension after a later fault without inventing history events", () => {
    const activity = execution("accepted", "Suspension");
    activity.status = "Faulted";
    render([activity], undefined, container => {
      expect(container.textContent).toContain("Suspension accepted");
      expect(container.textContent).toContain("Committed changes: workflow outputs");
      expect(container.textContent).not.toContain("Completion accepted");
      expect(container.querySelector("time")?.dateTime).toBe("2026-07-09T10:00:01Z");
      expect(container.querySelectorAll(".wf-history-event")).toHaveLength(0);
    });
  });

  it("shows only the exact selected activity's accepted receipts", () => {
    render([execution("selected"), execution("other", "Suspension")], "selected", container => {
      expect(container.textContent).toContain("selected label");
      expect(container.textContent).not.toContain("other label");
      expect(container.textContent).toContain("Completion accepted");
      expect(container.textContent).not.toContain("Suspension accepted");
    });
  });

  it("does not turn unrecognized kinds or unaccepted activity state into receipt labels", () => {
    const unknown = execution("unknown");
    unknown.workflowStateEffects = ["protected-sentinel"];
    unknown.workflowStateEffectReceipts![0].kinds = ["protected-sentinel"];
    const unaccepted = { ...execution("unaccepted"), workflowStateEffects: [], workflowStateEffectReceipts: [] };
    render([unknown, unaccepted], undefined, container => expect(container.textContent).toBe(""));
  });
});

function execution(id: string, transition: "Completion" | "Suspension" = "Completion"): ActivityExecutionStateSummary {
  return { activityExecutionId: id, status: "Completed", workflowStateEffects: ["outputs"],
    workflowStateEffectReceipts: [{ attemptId: "accepted-attempt", acceptedAt: "2026-07-09T10:00:01Z", transition, kinds: ["outputs"] }] } as ActivityExecutionStateSummary;
}

function render(activities: ActivityExecutionStateSummary[], selected: string | undefined, inspect: (container: HTMLDivElement) => void) {
  const container = document.createElement("div");
  document.body.append(container);
  const root = createRoot(container);
  try {
    flushSync(() => root.render(<AcceptedWorkflowChanges activities={activities} selectedActivityExecutionId={selected} labelForActivity={activity => `${activity.activityExecutionId} label`} />));
    inspect(container);
  } finally {
    flushSync(() => root.unmount());
    container.remove();
  }
}
