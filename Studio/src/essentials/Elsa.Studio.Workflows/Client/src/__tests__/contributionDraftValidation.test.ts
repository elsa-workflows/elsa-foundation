import { describe, expect, it } from "vitest";
import {
  collectContributionDraftValidation,
  contributionDraftValidationCollectors,
  contributionDraftValidationSignature
} from "../contributionDraftValidation";
import type { ActivityNode } from "../workflowTypes";
import { flowchartStructureKind } from "../flowchartStartNode";

function node(nodeId: string, extra: Record<string, unknown> = {}): ActivityNode {
  return { nodeId, activityVersionId: `${nodeId}-v`, inputs: [], outputs: [], ...extra };
}

describe("contributionDraftValidation", () => {
  it("walks nested flowchart activities and maps collector issues onto ValidationError", async () => {
    const child = node("eval-1", { Decision: { expression: { type: "Literal", value: "approval@v1" } } });
    const root = node("root", {
      structure: {
        kind: flowchartStructureKind,
        schemaVersion: "1",
        payload: { activities: [child] }
      }
    });
    const errors = await collectContributionDraftValidation(root, [
      async activity => typeof activity === "object" && activity !== null && "Decision" in activity
        ? [{ type: "Decision/InputMembership", message: "notes is not in this table.", path: "eval-1/Inputs" }]
        : []
    ]);
    expect(errors).toEqual([{
      type: "Decision/InputMembership",
      code: "Decision/InputMembership",
      message: "notes is not in this table.",
      path: "eval-1/Inputs",
      severity: "Error"
    }]);
    expect(contributionDraftValidationSignature(root)).toContain("eval-1");
    expect(contributionDraftValidationCollectors(
      [{ id: "decisions.version", supports: () => true, component: () => null, collectDraftValidation: async () => [] }],
      []
    )).toHaveLength(1);
  });
});
