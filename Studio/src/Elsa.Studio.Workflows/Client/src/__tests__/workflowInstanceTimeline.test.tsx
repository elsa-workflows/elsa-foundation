import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { WorkflowExecutionTimeline } from "../WorkflowInstanceTimeline";
import type { ExecutableActivityGraph } from "../executableGraph";
import type { ActivityCatalogItem, ActivityExecutionStateSummary } from "../workflowTypes";

describe("WorkflowExecutionTimeline", () => {
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
