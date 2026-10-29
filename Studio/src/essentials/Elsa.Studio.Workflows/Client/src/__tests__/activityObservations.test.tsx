import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { ActivityObservationsPanel } from "../workflow-editor/ActivityObservationsPanel";
import { WorkflowNodeRuntimeBadges } from "../workflow-editor/WorkflowNodeRuntimeBadges";
import { applyRuntimeOverlays } from "../workflowRuntimeOverlays";
import type { ActivityExecutionObservation, ActivityExecutionStateSummary } from "../workflowTypes";
import type { WorkflowNodeData } from "../workflowAdapter";

const warning: ActivityExecutionObservation = {
  observationId: "undelivered:request-1:attempt-1", kind: "http.response.delivery", code: "http.response.undelivered",
  severity: "Warning", message: "Response prepared but not sent: the original HTTP exchange is closed.", occurredAt: "2026-10-30T00:00:00Z"
};

describe("retained activity observations", () => {
  it("leaves missing and unrecognized delivery evidence Unknown", () => {
    for (const observations of [undefined, [{ ...warning, kind: "unrelated", severity: "Information" }], [{ ...warning, code: "http.response.future", severity: "Information" }]]) {
      const html = renderToStaticMarkup(<ActivityObservationsPanel activityType="Elsa.Activities.Http.Activities.WriteHttpResponse" observations={observations} />);
      expect(html).toContain("Unknown");
      expect(html).not.toContain("Not sent to originating request");
    }
  });

  it("distinguishes an exact server write from client acknowledgement", () => {
    const html = renderToStaticMarkup(<ActivityObservationsPanel activityType="Elsa.WriteHttpResponse" observations={[{
      ...warning, code: "http.response.written", severity: "Information",
      message: "Written to the HTTP response. This records the server write, not acknowledgement by the client."
    }]} />);
    expect(html).toContain("Written to HTTP response");
    expect(html).toContain("not acknowledgement by the client");
    expect(html).not.toContain("1 warning");
  });

  it("shows warnings on the selected invocation without changing Completed or offering incident actions", () => {
    const executions = [1, 2].map(index => ({
      activityExecutionId: `invocation-${index}`, workflowExecutionId: "run-1", executableNodeId: "response", authoredActivityId: "response",
      activityType: "Elsa.WriteHttpResponse", activityTypeVersion: "1.0.0", status: "Completed", executionSequence: index,
      scheduledAt: `2026-10-30T00:00:0${index}Z`, metadata: {}, observations: index === 1 ? [warning] : []
    } satisfies ActivityExecutionStateSummary));
    const data: WorkflowNodeData = { label: "Write HTTP Response", activityVersionId: "response-v1", childSlots: [], acceptsInbound: true, sourcePorts: [] };
    for (const index of [1, 2]) {
      const [node] = applyRuntimeOverlays([{ id: "response", position: { x: 0, y: 0 }, data }], executions, [], `invocation-${index}`);
      const html = renderToStaticMarkup(<WorkflowNodeRuntimeBadges data={node!.data} />);
      expect(html).toContain("Completed");
      expect(html.includes("1 warning")).toBe(index === 1);
      expect(html).not.toContain("wf-node-incident-action");
      expect(node!.data.runtime?.faultCount).toBe(0);
    }
    const details = renderToStaticMarkup(<ActivityObservationsPanel activityType="Elsa.WriteHttpResponse" observations={[warning]} />);
    expect(details).toContain("Not sent to originating request");
    expect(details).toContain(warning.message);
  });
});
