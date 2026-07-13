import { describe, expect, it } from "vitest";
import { projectPinnedExecutable } from "../workflow-editor/WorkflowInstances";
import type { WorkflowExecutableDetails, WorkflowInstanceDetails } from "../workflowTypes";

describe("Runtime-pinned workflow instance rendering", () => {
  it("projects the executable pinned by artifactId without a Design version", () => {
    const executable = {
      artifactId: "artifact-pinned",
      artifactHash: "sha256:pinned",
      createdAt: "2026-07-01T00:00:00Z",
      rootActivityType: "Example.Root",
      rootActivityVersion: "1.0.0",
      nodeCount: 1,
      resumeTargetCount: 0,
      rootActivity: {
        executableNodeId: "exec-root",
        authoredActivityId: "authored-root",
        activityType: "Example.Root",
        activityTypeVersion: "1.0.0",
        inputBindings: [],
        childSlots: []
      },
      references: []
    } satisfies WorkflowExecutableDetails;
    const details = {
      instance: {
        workflowExecutionId: "execution-1",
        artifactId: "artifact-pinned",
        artifactVersion: "3.0.0",
        artifactHash: "sha256:pinned",
        definitionId: "definition-1",
        definitionVersionId: "draft:synthetic",
        status: "Running",
        createdAt: "2026-07-01T00:00:00Z",
        activityCount: 0,
        incidentCount: 0
      },
      activities: [],
      incidents: []
    } satisfies WorkflowInstanceDetails;

    const projected = projectPinnedExecutable(executable, details, []);

    expect(projected.definition.description).toContain("artifact-pinned");
    expect(projected.state.rootActivity).toMatchObject({ nodeId: "authored-root", activityVersionId: "Example.Root" });
    expect(projected.id).toBe("draft:synthetic");
  });
});
