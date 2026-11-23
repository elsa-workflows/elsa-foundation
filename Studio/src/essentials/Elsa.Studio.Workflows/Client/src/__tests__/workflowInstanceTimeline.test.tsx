import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { WorkflowExecutionTimeline } from "../WorkflowInstanceTimeline";
import type { ExecutableActivityGraph } from "../executableGraph";
import type { ActivityCatalogItem, ActivityExecutionStateSummary } from "../workflowTypes";

describe("WorkflowExecutionTimeline", () => {
  it("keeps accepted suspension attribution separate from a later failed evaluation", () => {
    const activity = {
      activityExecutionId: "body", workflowExecutionId: "run", executableNodeId: "node", authoredActivityId: "node",
      activityType: "State writer", activityTypeVersion: "1", status: "Faulted", metadata: {},
      workflowStateEffects: ["outputs", "correlationId"],
      workflowStateEffectReceipts: [
        { attemptId: "accepted-attempt", acceptedAt: "2026-11-22T12:00:00Z", transition: "Suspension", kinds: ["outputs"] },
        { attemptId: "resume-attempt", acceptedAt: "2026-11-22T12:01:00Z", transition: "Completion", kinds: ["correlationId"] }
      ]
    } as ActivityExecutionStateSummary;
    const markup = renderToStaticMarkup(<WorkflowExecutionTimeline activities={[activity]} activityCatalog={[]} />);
    expect(markup).toContain("Faulted");
    expect(markup).toContain("Suspension accepted");
    expect(markup).toContain("Completion accepted");
    expect(markup).toContain("workflow outputs");
    expect(markup).toContain("correlation");
  });

  it("uses frozen source-reference presentation instead of current catalog wording", () => {
    const activity = {
      activityExecutionId: "activity-execution-1",
      workflowExecutionId: "workflow-execution-1",
      executableNodeId: "exec-1",
      authoredActivityId: "authored-1",
      activityType: "Elsa.WriteLine",
      activityTypeVersion: "1.0.0",
      status: "Completed",
      metadata: {}
    } as ActivityExecutionStateSummary;
    const catalog: ActivityCatalogItem[] = [{
      activityVersionId: "write-line-v1",
      activityTypeKey: activity.activityType,
      version: "1.0.0",
      category: "Primitives",
      displayName: "Current catalog wording",
      executionType: "Activity",
      inputs: [],
      outputs: []
    }];
    const graph: ExecutableActivityGraph = {
      root: {
        nodeId: "authored-1",
        activityVersionId: "write-line-v1",
        inputs: [],
        outputs: []
      },
      factsByNodeId: new Map([["authored-1", {
        executableNodeId: "exec-1",
        authoredActivityId: "authored-1",
        activityType: activity.activityType,
        activityTypeVersion: activity.activityTypeVersion,
        structureKind: null,
        structureDiagnostics: [],
        available: true,
        inputBindings: [],
        outputCaptures: [],
        authoredInputs: [],
        authoredInputsAccess: null,
        presentation: { nodeId: "authored-1", displayName: "Frozen timeline wording" }
      }]]),
      activityPresentation: [{ nodeId: "authored-1", displayName: "Frozen timeline wording" }]
    };

    const markup = renderToStaticMarkup(
      <WorkflowExecutionTimeline
        activities={[activity]}
        activityCatalog={catalog}
        executableGraph={graph}
      />
    );

    expect(markup).toContain("Frozen timeline wording");
    expect(markup).not.toContain("Current catalog wording");
  });
});
